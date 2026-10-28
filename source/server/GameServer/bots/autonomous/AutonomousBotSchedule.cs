using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// When autonomous gamebots are online (Living Server, docs/LIVING-SERVER.md).
/// <list type="bullet">
/// <item><b>off</b> (default): every bot stays online, as before.</item>
/// <item><b>session</b>: each bot plays sessions of 1–4 hours with 20 minutes to 2 hours away, so
/// names come and go while you play. Timed from server start.</item>
/// <item><b>clock</b>: follows your PC's clock like a 2002 server: busy evenings and weekends, a
/// thin crowd at 5 a.m.</item>
/// </list>
/// Separately, <c>bot_living_breaks</c> gives bots short "brb" breaks (they log out and come back)
/// and, rarely, a linkdead moment when grouped with you. Only ungrouped, alive bots out of combat
/// that aren't carrying a relic or working a siege ever log out; the population controller simply
/// doesn't log scheduled-off bots back in. Companion bots and players are never affected.
/// </summary>
public static class AutonomousBotSchedule
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public enum Mode { Off, Session, Clock }

    public static Mode CurrentMode => Parse(Properties.BOT_SCHEDULE_MODE);

    public static bool BreaksOn => Properties.BOT_LIVING_BREAKS;

    public static bool Active => CurrentMode != Mode.Off || BreaksOn;

    public static Mode Parse(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "session" => Mode.Session,
        "clock" => Mode.Clock,
        _ => Mode.Off,
    };

    private static DateTime _startedUtc = DateTime.UtcNow;

    public static void NoteServerStarted(DateTime nowUtc) => _startedUtc = nowUtc;

    #region Schedule math (pure, unit tested)

    /// <summary>A stable 64-bit mix of a bot id and a salt (SplitMix64), never string.GetHashCode.</summary>
    public static ulong Mix(long id, long salt)
    {
        ulong z = unchecked((ulong)id * 0x9E3779B97F4A7C15UL + (ulong)salt * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static double Unit(long id, long salt) => (Mix(id, salt) >> 11) * (1.0 / (1UL << 53));

    /// <summary>Session mode: minutes online and away for this bot (1–4 hours on, 20–120 minutes off).</summary>
    public static (int Online, int Away) SessionLengths(long botId, BotPersonalityType type)
    {
        double online = 60 + Unit(botId, 1) * 180;
        double away = 20 + Unit(botId, 2) * 100;
        switch (type)
        {
            case BotPersonalityType.NoLifer: online *= 1.5; away *= 0.5; break;
            case BotPersonalityType.Socialite: online *= 1.2; break;
            case BotPersonalityType.Crafter: away *= 1.2; break;
        }
        return ((int)Math.Round(online), (int)Math.Round(Math.Max(10, away)));
    }

    public static bool SessionOnline(long botId, BotPersonalityType type, TimeSpan sinceStart)
    {
        (int online, int away) = SessionLengths(botId, type);
        int cycle = online + away;
        int phase = (int)(Unit(botId, 3) * cycle);
        double minute = Math.Max(0, sinceStart.TotalMinutes) + phase;
        return minute % cycle < online;
    }

    // Share of the roster online by local hour (a 2002 North American evening crowd).
    private static readonly double[] Weekday =
    [
        0.45, 0.35, 0.28, 0.22, 0.20, 0.20, 0.24, 0.30, 0.34, 0.36, 0.38, 0.42,
        0.46, 0.48, 0.50, 0.54, 0.60, 0.70, 0.82, 0.92, 0.96, 0.95, 0.85, 0.62,
    ];

    private static readonly double[] Weekend =
    [
        0.60, 0.48, 0.38, 0.30, 0.25, 0.24, 0.26, 0.32, 0.42, 0.52, 0.62, 0.70,
        0.74, 0.76, 0.78, 0.80, 0.82, 0.86, 0.90, 0.95, 0.97, 0.96, 0.90, 0.75,
    ];

    /// <summary>The share of the roster the clock mode wants online at this local time (hour curve, interpolated by minute).</summary>
    public static double ClockShare(DateTime local)
    {
        bool weekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                       local.DayOfWeek == DayOfWeek.Friday && local.Hour >= 18;
        double[] curve = weekend ? Weekend : Weekday;
        double here = curve[local.Hour], next = curve[(local.Hour + 1) % 24];
        return here + (next - here) * (local.Minute / 60.0);
    }

    public static bool ClockOnline(long botId, BotPersonalityType type, DateTime local)
    {
        double share = ClockShare(local);
        if (type == BotPersonalityType.NoLifer)
            share = Math.Min(1.0, share * 1.5 + 0.1);
        // Each bot's place in line for the day: stable for the day, so it doesn't flicker on and off.
        int day = local.Year * 400 + local.DayOfYear;
        double place = (Unit(botId, 4) + 0.25 * Unit(botId, 1000 + day)) % 1.0;
        return place < share;
    }

    public static bool ScheduledOnline(Mode mode, long botId, BotPersonalityType type, TimeSpan sinceStart, DateTime local) => mode switch
    {
        Mode.Session => SessionOnline(botId, type, sinceStart),
        Mode.Clock => ClockOnline(botId, type, local),
        _ => true,
    };

    #endregion

    #region Breaks

    private static readonly ConcurrentDictionary<long, DateTime> BreakUntil = new();
    private static readonly ConcurrentDictionary<long, byte> ReturningFromBreak = new();

    public static bool OnBreak(long botId, DateTime nowUtc) => BreakUntil.TryGetValue(botId, out DateTime until) && nowUtc < until;

    // A bot that rage quit after a pile-on (a debate scene with "@exit") stays away a few hours whatever the schedule mode,
    // then comes back a little sheepish.
    private static readonly ConcurrentDictionary<long, DateTime> RageQuitUntil = new();
    private static readonly ConcurrentDictionary<long, byte> ReturningFromRageQuit = new();

    /// <summary>Hours a rage quitter stays away: 2-6. Pure.</summary>
    public static TimeSpan RageQuitAway(Random random) => TimeSpan.FromMinutes(120 + random.Next(241));

    public static bool RageQuitting(long botId, DateTime nowUtc)
    {
        AutonomousBotRageQuits.EnsureLoaded();
        if (!RageQuitUntil.TryGetValue(botId, out DateTime until))
            return false;
        if (nowUtc < until)
            return true;
        RageQuitUntil.TryRemove(botId, out _);
        return false;
    }

    /// <summary>A saved rage quit (AutonomousBotRageQuits): away until then, and sheepish when it logs back in.</summary>
    public static void RestoreRageQuit(long botId, DateTime untilUtc, DateTime nowUtc)
    {
        if (untilUtc > nowUtc)
            RageQuitUntil[botId] = untilUtc;
        ReturningFromRageQuit[botId] = 0;
    }

    /// <summary>
    /// The dunce of a pile-on logs off a few seconds after its last line and stays away 2-6 hours. A bot that can't log
    /// out (grouped, fighting, carrying a relic) is tried again for about a minute, then left alone.
    /// </summary>
    public static void RageQuit(GameBot bot, Random random)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper)
            return;
        DateTime until = DateTime.UtcNow + RageQuitAway(random);
        int tries = 0;
        _ = new ECSGameTimer(bot, timer =>
        {
            try
            {
                if (bot.ObjectState != GameObject.eObjectState.Active)
                    return 0;
                if (!CanLogOut(bot))
                    return ++tries < 12 ? 5_000 : 0;
                RageQuitUntil[bot.DatabaseID] = until;
                ReturningFromRageQuit[bot.DatabaseID] = 0;
                AutonomousBotRageQuits.Save(bot.DatabaseID, until);
                LeavingNow[bot.DatabaseID] = 0;
                Log.Info($"LIVING_SERVER_LOGOUT bot=\"{bot.Name}\" id={bot.DatabaseID} reason=ragequit until={until:O}");
                bot.Delete();
            }
            catch (Exception exception)
            {
                Log.Warn($"Living Server: {bot.Name} could not rage quit", exception);
            }
            finally
            {
                LeavingNow.TryRemove(bot.DatabaseID, out _);
            }
            return 0;
        }, 3_000 + random.Next(3_000));
    }

    #endregion

    /// <summary>Whether the population controller should have this bot online now (thread-safe).</summary>
    public static bool WantsOnline(OfflineWorldBotRecord record, DateTime nowUtc)
    {
        if (record == null)
            return false;
        if (RageQuitting(record.BotId, nowUtc))
            return false;
        Mode mode = CurrentMode;
        if (mode == Mode.Off && !BreaksOn)
            return true;
        if (BreaksOn && OnBreak(record.BotId, nowUtc))
            return false;
        return ScheduledOnline(mode, record.BotId, AutonomousBotPersonality.TypeFor(record.BotId), nowUtc - _startedUtc, nowUtc.ToLocalTime());
    }

    #region Logging out (game loop)

    private static long _nextLogoutScan;
    private static long _nextBreakRoll;
    private static long _nextLinkdeadRoll;
    private static readonly ConcurrentDictionary<long, byte> LeavingNow = new();
    public const int MaxLogoutsPerScan = 3;

    /// <summary>Only bots that can leave without breaking anything: ungrouped, alive, idle, not carrying or sieging.</summary>
    public static bool CanLogOut(GameBot bot) =>
        bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && bot.ObjectState == GameObject.eObjectState.Active &&
        bot.IsAlive && bot.Group == null && !bot.InCombat && !bot.IsCasting && !bot.IsOnStableMasterRoute &&
        !bot.IsReturningAfterRelease && !BotSiegeRuntime.Assigned(bot) &&
        bot.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK) == null &&
        string.IsNullOrEmpty(bot.TempProperties.GetProperty<string>("RvrEventForce"));

    internal static void Pulse(long now)
    {
        Mode mode = CurrentMode;
        bool breaks = BreaksOn;
        if (mode == Mode.Off && !breaks)
        {
            if (!BreakUntil.IsEmpty)
                BreakUntil.Clear();
            if (!OverstaySince.IsEmpty)
                OverstaySince.Clear();
            RecentOverstays = 0;
            if (!Frozen.IsEmpty)
                foreach (long id in Frozen.Keys.ToArray())
                    Unfreeze(id, false);
            return;
        }
        DateTime nowUtc = DateTime.UtcNow;
        if (breaks && now >= _nextBreakRoll)
        {
            _nextBreakRoll = now + 60_000;
            RollBreaks(nowUtc);
        }
        if (breaks && now >= _nextLinkdeadRoll)
        {
            _nextLinkdeadRoll = now + 30_000;
            RollLinkdead(now);
        }
        PulseFrozen(now);
        if (now < _nextLogoutScan)
            return;
        _nextLogoutScan = now + 5_000;
        int leaving = 0;
        int recentOverstays = 0;
        var overstaying = new HashSet<long>();
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
        {
            if (WantsOnline(bot.PersistentRecord, nowUtc))
                continue;
            overstaying.Add(bot.DatabaseID);
            DateTime since = OverstaySince.GetOrAdd(bot.DatabaseID, nowUtc);
            if (nowUtc - since < OverstayGrace)
                recentOverstays++;
            if (leaving >= MaxLogoutsPerScan || LeavingNow.ContainsKey(bot.DatabaseID) || !CanLogOut(bot))
                continue;
            leaving++;
            LogOut(bot, OnBreak(bot.DatabaseID, nowUtc) ? "brb" : "logoff");
        }
        foreach (long gone in OverstaySince.Keys.Where(id => !overstaying.Contains(id)).ToArray())
            OverstaySince.TryRemove(gone, out _);
        RecentOverstays = recentOverstays;
    }

    // Scheduled-off bots still online because they're busy (often in a bot group). For 15 minutes they
    // count against the scheduled number, so the total doesn't run far past it; after that they no longer
    // hold back the bots whose turn it is (review 2026-10-16).
    private static readonly ConcurrentDictionary<long, DateTime> OverstaySince = new();
    public static readonly TimeSpan OverstayGrace = TimeSpan.FromMinutes(15);
    public static volatile int RecentOverstays;

    private static void LogOut(GameBot bot, string topic)
    {
        LeavingNow[bot.DatabaseID] = 0;
        if (AutonomousBotVoices.TryEventLine(bot, "session", out string line, topic))
        {
            if (bot.Guild != null && AutonomousBotGuildSocial.HasListener(bot.Guild) && Random.Shared.NextDouble() < 0.6)
                AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Guild, line);
            else
                AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Say, line);
        }
        _ = new ECSGameTimer(bot, timer =>
        {
            try
            {
                if (CanLogOut(bot) && !WantsOnline(bot.PersistentRecord, DateTime.UtcNow))
                {
                    // Only a bot that really left on a break says "back" when it returns.
                    if (topic == "brb")
                        ReturningFromBreak[bot.DatabaseID] = 0;
                    Log.Info($"LIVING_SERVER_LOGOUT bot=\"{bot.Name}\" id={bot.DatabaseID} reason={topic} mode={CurrentMode}");
                    bot.Delete();
                }
            }
            catch (Exception exception)
            {
                Log.Warn($"Living Server: {bot.Name} could not log out", exception);
            }
            finally
            {
                LeavingNow.TryRemove(bot.DatabaseID, out _);
            }
            return 0;
        }, 2_500 + Random.Shared.Next(2_000));
    }

    private static void RollBreaks(DateTime nowUtc)
    {
        foreach (var expired in BreakUntil.Where(pair => pair.Value <= nowUtc).Select(pair => pair.Key).ToArray())
            BreakUntil.TryRemove(expired, out _);
        // About one break per bot every four hours online, 5–25 minutes long.
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
        {
            if (!CanLogOut(bot) || BreakUntil.ContainsKey(bot.DatabaseID) || Random.Shared.NextDouble() >= 1.0 / 240)
                continue;
            BreakUntil[bot.DatabaseID] = nowUtc.AddMinutes(5 + Random.Shared.Next(21));
        }
    }

    /// <summary>A bot logged in (called by the population controller): a returning or scheduled bot says hello.</summary>
    public static void OnLoggedIn(GameBot bot)
    {
        // Back from a rage quit: a sheepish word in /faction, whatever the schedule mode.
        AutonomousBotRageQuits.EnsureLoaded();
        if (bot != null && ReturningFromRageQuit.TryRemove(bot.DatabaseID, out _))
        {
            AutonomousBotRageQuits.Forget(bot.DatabaseID);
            AutonomousLivingServer.Later(bot, 20_000 + Random.Shared.Next(40_000), () => AutonomousLivingServer.CanTalk(bot), () =>
            {
                if (AutonomousBotVoices.TryEventLine(bot, "session", out string sheepish, "ragequit_back"))
                    AutonomousBotChatCoordinator.BroadcastFaction(bot.Name, bot.Realm, sheepish);
            });
            return;
        }
        if (!Active || bot == null)
            return;
        bool back = ReturningFromBreak.TryRemove(bot.DatabaseID, out _);
        // No greetings during the first logins after server start.
        if (!back && DateTime.UtcNow - _startedUtc < TimeSpan.FromMinutes(10))
            return;
        if (!back && Random.Shared.NextDouble() >= 0.5)
            return;
        AutonomousLivingServer.Later(bot, 5_000 + Random.Shared.Next(8_000), () => AutonomousLivingServer.CanTalk(bot), () =>
        {
            if (!AutonomousBotVoices.TryEventLine(bot, "session", out string line, back ? "back" : "logon"))
                return;
            if (bot.Guild != null && AutonomousBotGuildSocial.HasListener(bot.Guild))
                AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Guild, line);
            else
                AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Say, line);
        });
    }

    #endregion

    #region Linkdead

    private static readonly ConcurrentDictionary<long, (GameBot Bot, long Until)> Frozen = new();
    private static readonly ConcurrentDictionary<Group, long> LastLinkdead = new();
    public const long LinkdeadGroupCooldownMs = 45 * 60_000;

    /// <summary>True while a bot is "linkdead": it stands still and does nothing (checked by the bot's brain).</summary>
    public static bool IsFrozen(GameBot bot) => bot != null && !Frozen.IsEmpty && Frozen.TryGetValue(bot.DatabaseID, out var entry) &&
                                               ReferenceEquals(entry.Bot, bot) && entry.Until > GameLoop.GameLoopTime;

    /// <summary>Anything that hits a "linkdead" bot wakes it at once (its connection "comes back").</summary>
    public static void OnAttacked(GameBot bot)
    {
        if (bot != null && Frozen.TryGetValue(bot.DatabaseID, out var entry) && ReferenceEquals(entry.Bot, bot))
            Unfreeze(bot.DatabaseID, true);
    }

    private static void RollLinkdead(long now)
    {
        foreach (GamePlayer player in ClientService.Instance.GetPlayers())
        {
            Group group = player?.Group;
            if (group == null || LastLinkdead.TryGetValue(group, out long last) && now - last < LinkdeadGroupCooldownMs)
                continue;
            GameLiving[] members = group.GetMembersInTheGroup().ToArray();
            if (members.Any(member => member.InCombat) || Random.Shared.NextDouble() >= 1.0 / 90)
                continue;
            GameBot[] candidates = members.OfType<GameBot>().Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper &&
                bot.IsAlive && !bot.IsCasting && !bot.IsOnStableMasterRoute && !BotSiegeRuntime.Assigned(bot) &&
                bot.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK) == null &&
                string.IsNullOrEmpty(bot.TempProperties.GetProperty<string>("RvrEventForce")) &&
                !Frozen.ContainsKey(bot.DatabaseID)).ToArray();
            if (candidates.Length == 0)
                continue;
            LastLinkdead[group] = now;
            GameBot frozen = candidates[Random.Shared.Next(candidates.Length)];
            long until = now + 30_000 + Random.Shared.Next(60_000);
            Frozen[frozen.DatabaseID] = (frozen, until);
            frozen.StopMoving();
            frozen.StopFollowing();
            Log.Info($"LIVING_SERVER_LINKDEAD bot=\"{frozen.Name}\" id={frozen.DatabaseID} seconds={(until - now) / 1000}");
            GameBot noticer = members.OfType<GameBot>().Where(bot => bot != frozen && AutonomousLivingServer.CanTalk(bot) &&
                bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
            if (noticer != null)
                AutonomousLivingServer.Later(noticer, 15_000 + Random.Shared.Next(10_000),
                    () => IsFrozen(frozen) && noticer.Group == frozen.Group && AutonomousLivingServer.CanTalk(noticer), () =>
                    {
                        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = frozen.Name };
                        if (AutonomousBotVoices.TryEventLine(noticer, "linkdead", out string line, "gone", slots, noRepeat: false))
                            AutonomousLivingServer.Speak(noticer, AutonomousLivingServer.Channel.Group, line);
                    });
        }
        foreach (Group stale in LastLinkdead.Where(pair => now - pair.Value > LinkdeadGroupCooldownMs).Select(pair => pair.Key).ToArray())
            LastLinkdead.TryRemove(stale, out _);
    }

    private static void PulseFrozen(long now)
    {
        foreach (var entry in Frozen.ToArray())
        {
            GameBot bot = entry.Value.Bot;
            if (bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAlive)
                Unfreeze(entry.Key, false);
            // A fight anywhere in its group, or the group breaking up, "reconnects" it at once (review 2026-10-16).
            else if (entry.Value.Until <= now || bot.InCombat || bot.Group is not { } group ||
                     group.GetMembersInTheGroup().Any(member => member.IsAlive && member.InCombat))
                Unfreeze(entry.Key, true);
        }
    }

    private static void Unfreeze(long botId, bool speak)
    {
        if (!Frozen.TryRemove(botId, out var entry))
            return;
        GameBot bot = entry.Bot;
        if (!speak || bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAlive)
            return;
        AutonomousLivingServer.Later(bot, 1_500 + Random.Shared.Next(2_000), () => bot.IsAlive, () =>
        {
            if (AutonomousBotVoices.TryEventLine(bot, "linkdead", out string line, "back", noRepeat: false))
                AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Group, line);
        });
    }

    #endregion
}
