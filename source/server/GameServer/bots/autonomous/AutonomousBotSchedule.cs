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

    // Share of the roster online by local hour (a 2002 North American evening crowd). 1.1.3: a deeper trough, so a
    // 2,700-bot roster has about 330 online at 5 a.m. on a weekday (the 1.1.2 curve kept a fifth, about 560; a 2003
    // server had a few hundred at 4-6 a.m.) and the whole evening crowd from 7 to 11 p.m.
    private static readonly double[] Weekday =
    [
        0.42, 0.32, 0.24, 0.17, 0.13, 0.12, 0.14, 0.19, 0.25, 0.30, 0.34, 0.38,
        0.44, 0.46, 0.48, 0.52, 0.58, 0.68, 0.80, 0.90, 0.96, 0.97, 0.90, 0.66,
    ];

    private static readonly double[] Weekend =
    [
        0.58, 0.46, 0.35, 0.25, 0.18, 0.15, 0.16, 0.20, 0.30, 0.42, 0.55, 0.64,
        0.70, 0.74, 0.76, 0.78, 0.80, 0.84, 0.89, 0.95, 0.98, 0.98, 0.93, 0.76,
    ];

    /// <summary>Friday from 6 p.m., Saturday and Sunday use the weekend curve.</summary>
    public static bool IsWeekend(DateTime local) => local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                                                     local.DayOfWeek == DayOfWeek.Friday && local.Hour >= 18;

    private static double CurveAt(DateTime local) => (IsWeekend(local) ? Weekend : Weekday)[local.Hour];

    /// <summary>
    /// The share of the roster the clock mode wants online at this local time (hour curve, interpolated by minute). Each
    /// hour runs toward the next hour's value on the curve of that next hour's day, so Friday 6 p.m. and Monday midnight,
    /// where the weekday and weekend curves meet, are smooth (1.1.2 stepped there: about 400 bots of 2,700 at once on
    /// Monday 00:00).
    /// </summary>
    public static double ClockShare(DateTime local)
    {
        DateTime hour = new(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Kind);
        double here = CurveAt(hour), next = CurveAt(hour.AddHours(1));
        return here + (next - here) * ((local - hour).TotalMinutes / 60.0);
    }

    /// <summary>The hours (local) in which each bot's place in line moves from one day's to the next: the quiet hours.</summary>
    public const int ReshuffleStartHour = 2, ReshuffleHours = 3;

    /// <summary>
    /// A bot's place in the day's line, 0-1: online when it is under the hour's share. Stable from 5 a.m. to 2 a.m., so a
    /// bot doesn't flicker on and off and a bot up early is still up when the evening fills; between 2 and 5 a.m. it moves
    /// a little at a time to the next day's place, so different bots play late on different days. 1.1.2 changed every
    /// place at midnight at once: about 17% of the roster (some 450 of 2,700 bots) wanted to swap in the same minute. Pure.
    /// </summary>
    public static double ClockPlace(long botId, DateTime local)
    {
        DateTime shifted = local.AddHours(-ReshuffleStartHour);
        double blend = Math.Clamp(shifted.TimeOfDay.TotalHours / ReshuffleHours, 0.0, 1.0);
        double today = Unit(botId, 1000 + DayKey(shifted)), yesterday = Unit(botId, 1000 + DayKey(shifted.AddDays(-1)));
        double offset = yesterday + (today - yesterday) * blend;
        return (Unit(botId, 4) + 0.25 * offset) % 1.0;
    }

    private static int DayKey(DateTime day) => day.Year * 400 + day.DayOfYear;

    public static bool ClockOnline(long botId, BotPersonalityType type, DateTime local)
    {
        double share = ClockShare(local);
        if (type == BotPersonalityType.NoLifer)
            share = Math.Min(1.0, share * 1.5 + 0.1);
        return ClockPlace(botId, local) < share;
    }

    public static bool ScheduledOnline(Mode mode, long botId, BotPersonalityType type, TimeSpan sinceStart, DateTime local) => mode switch
    {
        Mode.Session => SessionOnline(botId, type, sinceStart),
        Mode.Clock => ClockOnline(botId, type, local),
        _ => true,
    };

    /// <summary>
    /// How many bots the population controller may queue for login now while a schedule is on (pure). <paramref name="desired"/>
    /// is the schedule's number (through the startup ramp), <paramref name="scheduledOnline"/> those of them online,
    /// <paramref name="totalOnline"/> every gamebot online (scheduled-off bots still finishing something included). For their
    /// first 15 minutes those overstaying bots count, so logins wait for them (10% slack); after that they no longer hold the
    /// schedule back; and the total never runs more than a quarter over the schedule's number.
    /// </summary>
    public static int LoginsWanted(int desired, int scheduledOnline, int totalOnline, int recentOverstays, int pending)
    {
        int missing = desired - scheduledOnline - pending;
        missing = Math.Min(missing, desired + desired / 10 - scheduledOnline - recentOverstays - pending);
        return Math.Min(missing, desired + desired / 4 - totalOnline - pending);
    }

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

    /// <summary>
    /// A gamebot the schedule (or a break) wants offline now but that is still online, finishing something. It joins no
    /// new group or automatic raid and isn't counted in a raid's crowd, so it can finish and log off instead of being drawn
    /// into the next two hours (1.1.3). Always false with the schedule and breaks off.
    /// </summary>
    public static bool LeavingSoon(GameBot bot) => Active && bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
                                                   bot.PersistentRecord != null && !WantsOnline(bot.PersistentRecord, DateTime.UtcNow);

    /// <summary>Only bots that can leave without breaking anything: ungrouped, alive, idle, not carrying or sieging.</summary>
    public static bool CanLogOut(GameBot bot) =>
        bot != null && CanLogOut(bot.IsAutonomousWorldBot, bot.IsTemporaryGroupHelper, bot.ObjectState == GameObject.eObjectState.Active,
            bot.IsAlive, bot.Group != null, bot.InCombat || bot.IsCasting, bot.IsOnStableMasterRoute || bot.IsReturningAfterRelease,
            BotSiegeRuntime.Assigned(bot), bot.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK) != null,
            InRealmEvent(bot));

    /// <summary>
    /// The logout rule, pure: an autonomous gamebot (not a helper) in the world, alive, ungrouped, not fighting or casting,
    /// not travelling or running back after a death, not at a siege, carrying no relic and not committed to a realm event.
    /// </summary>
    public static bool CanLogOut(bool autonomous, bool temporaryHelper, bool inWorld, bool alive, bool grouped, bool busyFighting,
        bool travelling, bool atSiege, bool carryingRelic, bool inRealmEvent) =>
        autonomous && !temporaryHelper && inWorld && alive && !grouped && !busyFighting && !travelling && !atSiege && !carryingRelic && !inRealmEvent;

    /// <summary>
    /// Committed to a realm event (a siege muster, march or battle, a defense, a relic run). Every RvR gamebot carries its
    /// force id ("rvr-&lt;id&gt;") once it has run its RvR goal, and it is never cleared; 1.1.2 read "has a force id" as
    /// "in an event", so an RvR bot could never log out on a schedule or take a break (1.1.3). The id's event is asked instead.
    /// </summary>
    public static bool InRealmEvent(GameBot bot)
    {
        string force = bot?.TempProperties.GetProperty<string>("RvrEventForce");
        return !string.IsNullOrEmpty(force) && AutonomousRvrEventLayer.IsForceCommitted(force, GameLoop.GameLoopTime);
    }

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
                !InRealmEvent(bot) && !Frozen.ContainsKey(bot.DatabaseID)).ToArray();
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
