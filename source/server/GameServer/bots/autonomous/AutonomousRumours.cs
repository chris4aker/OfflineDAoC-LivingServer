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
/// True rumours (bot_living_rumours, docs/bot-voices/TALKING-UPDATE.md section 15): a bot passes on something that is
/// really so, vaguely, the way rumours went in 2003.
/// <list type="bullet">
/// <item>A rare named monster (a capitalised name and a respawn of 20 minutes or more, level 20+) that just respawned in a
/// realm's lands, when a player of that realm is online at a level to care: "heard {target} is up again in {place}". Said
/// one to four minutes after the spawn, and only if the monster is still alive then.</item>
/// <item>An enemy frontier keep whose guards are mostly dead (under 30% alive): "free rps at {keep}. guards are down". Never a
/// portal keep, one under siege, one that changed hands in the last half hour, or one with its owners near (chat audit
/// 2026-11-18: all three keep rumours of a night came within minutes of a capture, while the army was still inside).</item>
/// </list>
/// Each realm hears at most one rumour every 20 minutes, a monster at most every six hours, a keep at most every two.
/// </summary>
public static class AutonomousRumours
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool On => Properties.BOT_LIVING_RUMOURS;

    public const int RareRespawnMs = 20 * 60_000;
    public const int MinimumNamedLevel = 20;
    public const double ThinGuardShare = 0.3;
    public const int MinimumGuards = 4;
    public static readonly TimeSpan RealmRest = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan NamedRest = TimeSpan.FromHours(6);
    public static readonly TimeSpan KeepRest = TimeSpan.FromHours(2);

    private static readonly ConcurrentDictionary<eRealm, long> LastRumourAt = new();
    private static readonly ConcurrentDictionary<string, long> LastAbout = new(StringComparer.OrdinalIgnoreCase);
    private static long _nextKeepCheck;

    /// <summary>
    /// A rumour /faction held (a scene, the ceiling, the spacing after a scene): it keeps its turn, tried again every few
    /// seconds for up to three minutes while it is still so (1.1.3 RC1: a held rumour was lost, and /lstest said no bot had a
    /// line for it).
    /// </summary>
    private sealed class Waiting
    {
        public string Topic, Key, Said;
        public Dictionary<string, string> Slots;
        public Func<bool> StillSo;
        public long Exchange, Since, NextAt;
    }

    private static readonly ConcurrentDictionary<eRealm, Waiting> Due = new();

    #region Pure rules (unit tested)

    /// <summary>A spawn worth a rumour: a named monster of no realm, rare (20+ minute respawn), level 20 or more. Pure.</summary>
    public static bool IsRareNamed(string name, bool noRealm, bool isBot, int respawnMs, int level) =>
        AutonomousPlayerDeeds.IsNamed(name, noRealm, isBot) && respawnMs >= RareRespawnMs && level >= MinimumNamedLevel;

    /// <summary>A player who would care: from ten levels under the monster to five over it. Pure.</summary>
    public static bool Cares(int playerLevel, int monsterLevel) => playerLevel >= monsterLevel - 10 && playerLevel <= monsterLevel + 5;

    /// <summary>A keep is thin when it has at least four guards and under 30% of them are alive. Pure.</summary>
    public static bool IsThin(int guards, int alive) => guards >= MinimumGuards && alive < guards * ThinGuardShare;

    /// <summary>How long after a keep changes hands no thin-keep rumour is told about it.</summary>
    public static readonly TimeSpan CaptureRest = TimeSpan.FromMinutes(30);

    /// <summary>The range in which a living player or gamebot of the keep's owner means it is not left empty.</summary>
    public const int OwnersNearRadius = 3_000;

    /// <summary>
    /// Whether a thin keep may be called open: not a portal keep, no siege on it, not taken in the last
    /// <see cref="CaptureRest"/> (<paramref name="sinceCapture"/> null when it has not changed hands lately), and none of its
    /// owners near. Pure.
    /// </summary>
    public static bool MayCallOpen(bool portalKeep, bool underSiege, TimeSpan? sinceCapture, int ownersNear) =>
        !portalKeep && !underSiege && (sinceCapture == null || sinceCapture.Value >= CaptureRest) && ownersNear == 0;

    /// <summary>A rumour's names as chat wrote them: the keep and the zone short ("hlid", "jamtland mountains"), the monster as it is. Pure but for the draw.</summary>
    public static Dictionary<string, string> ChatSlots(Dictionary<string, string> slots, Random random)
    {
        if (slots.TryGetValue("keep", out string keep))
            slots["keep"] = AutonomousChatFieldTalk.ShortKeep(keep, random);
        if (slots.TryGetValue("place", out string place))
            slots["place"] = AutonomousChatFieldTalk.ShortZone(place, random);
        return slots;
    }

    #endregion

    private static bool RealmMayHear(eRealm realm, long now) =>
        !LastRumourAt.TryGetValue(realm, out long last) || now - last >= (long)RealmRest.TotalMilliseconds;

    private static bool AboutIsFresh(string key, TimeSpan rest, long now) =>
        !LastAbout.TryGetValue(key, out long last) || now - last >= (long)rest.TotalMilliseconds;

    /// <summary>A monster just respawned (GameNPC.RespawnTimerCallback).</summary>
    public static void OnRespawned(GameNPC npc)
    {
        if (!On || !AutonomousBotVoices.Enabled || npc == null || npc.CurrentRegion == null ||
            !IsRareNamed(npc.Name, npc.Realm == eRealm.None, npc is GameBot, npc.RespawnInterval, npc.Level))
            return;
        try
        {
            Zone zone = npc.CurrentZone;
            if (zone == null)
                return;
            eRealm realm = AutonomousWorldBotController.ProtectedRealm(npc.CurrentRegionID, zone.ID);
            if (realm == eRealm.None)
                realm = AutonomousWorldBotController.ProtectedRealm(npc.CurrentRegionID, ushort.MaxValue);
            bool cares = (realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia) &&
                         ClientService.Instance.GetPlayersOfRealm(realm).Any(player => Cares(player.Level, npc.Level));
            // For in-game checks (2026-10-27: a day without a named rumour): every rare named respawn and whether anyone cares.
            Log.Debug($"RUMOUR_NAMED_CHECK target=\"{npc.Name}\" level={npc.Level} zone=\"{zone.Description}\" lands={GlobalConstants.RealmToName(realm)} caring_player={cares}");
            if (!cares)
                return;
            long spawned = npc.SpawnTick;
            string key = $"named:{npc.InternalID ?? npc.Name}";
            GameNPC monster = npc;
            _ = new ECSGameTimer(npc, _ =>
            {
                long now = GameLoop.GameLoopTime;
                // Still up, the same spawn, and nobody heard a rumour lately: only then is it said.
                if (!monster.IsAlive || monster.ObjectState != GameObject.eObjectState.Active || monster.SpawnTick != spawned ||
                    !RealmMayHear(realm, now) || !AboutIsFresh(key, NamedRest, now))
                    return 0;
                var slots = ChatSlots(new Dictionary<string, string> { ["target"] = monster.Name, ["place"] = zone.Description }, Random.Shared);
                Tell(realm, "named_up", slots, now, key, $"RUMOUR_NAMED realm={GlobalConstants.RealmToName(realm)} target=\"{monster.Name}\" zone=\"{zone.Description}\"",
                    () => monster.IsAlive && monster.ObjectState == GameObject.eObjectState.Active && monster.SpawnTick == spawned);
                return 0;
            }, 60_000 + Random.Shared.Next(180_000));
        }
        catch (Exception exception)
        {
            Log.Warn("Rumours: a respawn could not be read", exception);
        }
    }

    /// <summary>
    /// GM test (/lstest rumour): a bot of the GM's realm tells /faction about a rare named monster that is up in the GM's
    /// region, as it would after a respawn (the realm's rests are skipped). False with the reason when none is up.
    /// </summary>
    public static bool TellNow(GamePlayer player, out string about)
    {
        about = null;
        if (player?.CurrentRegion == null)
            return false;
        GameNPC named = player.CurrentRegion.Objects.OfType<GameNPC>()
            .Where(npc => npc.IsAlive && npc.ObjectState == GameObject.eObjectState.Active && npc.CurrentZone != null &&
                          IsRareNamed(npc.Name, npc.Realm == eRealm.None, npc is GameBot, npc.RespawnInterval, npc.Level))
            .OrderBy(npc => Math.Abs(npc.Level - player.Level)).FirstOrDefault();
        if (named == null)
        {
            about = "no rare named monster is up in this region";
            return false;
        }
        var slots = ChatSlots(new Dictionary<string, string> { ["target"] = named.Name, ["place"] = named.CurrentZone.Description }, Random.Shared);
        LastRumourAt.TryRemove(player.Realm, out _);
        Due.TryRemove(player.Realm, out _);
        about = $"{named.Name} (level {named.Level}) in {named.CurrentZone.Description}";
        GameNPC monster = named;
        string outcome = Tell(player.Realm, "named_up", slots, GameLoop.GameLoopTime, null,
            $"RUMOUR_NAMED realm={GlobalConstants.RealmToName(player.Realm)} target=\"{named.Name}\" zone=\"{named.CurrentZone.Description}\" forced=lstest",
            () => monster.IsAlive && monster.ObjectState == GameObject.eObjectState.Active);
        // The reasons, told apart (in-game checks 2026-10-28 and 2026-11-08: "no bot had a line" was shown for a held line).
        switch (outcome)
        {
            case null:
                return true;
            case "scene":
                about += ", once the scene playing in /faction now is over";
                return true;
            case "ceiling":
                about += ", as soon as /faction has room under its ceiling (it waits up to three minutes)";
                return true;
            case "spacing" or "duplicate" or "held":
                about += ", in a few seconds (/faction just had a line)";
                return true;
            case "no_bot":
                about += ": no bot of your realm is free to talk (all fighting, casting or offline)";
                return false;
            case "no_line":
                about += ": no free bot of your realm had a line for it";
                return false;
            default:
                about += $": {outcome.Replace('_', ' ')}";
                return false;
        }
    }

    /// <summary>Every five minutes: an undefended enemy keep (AutonomousLivingWorld.Pulse).</summary>
    public static void Pulse(long now)
    {
        if (!On || !AutonomousBotVoices.Enabled)
            return;
        foreach (var (waitingRealm, waiting) in Due)
            if (now >= waiting.NextAt)
                Retry(waitingRealm, waiting, now);
        if (now < _nextKeepCheck)
            return;
        _nextKeepCheck = now + 5 * 60_000;
        AbstractGameKeep[] keeps;
        try
        {
            keeps = GameServer.KeepManager.GetFrontierKeeps().Where(keep => keep is not GameKeepTower && !keep.IsPortalKeep && keep.Realm != eRealm.None).ToArray();
        }
        catch (Exception exception)
        {
            Log.Warn("Rumours: could not list keeps", exception);
            return;
        }
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            if (!AutonomousBotChatCoordinator.HasFactionAudience(realm) || !RealmMayHear(realm, now))
                continue;
            foreach (AbstractGameKeep keep in keeps.Where(keep => keep.Realm != realm).OrderBy(_ => Random.Shared.Next()))
            {
                // Only guards meant to be up count: one in the world or waiting to respawn. A captured keep's guards above its
                // new level are taken out of the world (not dead), so they are not missing defenders.
                GameKeepGuard[] guards = keep.Guards.Values.Where(guard => guard.ObjectState == GameObject.eObjectState.Active || guard.IsRespawning).ToArray();
                int alive = guards.Count(guard => guard.IsAlive);
                // For in-game checks: how close a keep comes to "wide open" (2026-10-27: a day of sieges and no keep rumour).
                if (guards.Length >= MinimumGuards && alive < guards.Length * 0.6)
                    Log.Debug($"RUMOUR_KEEP_CHECK hearer={GlobalConstants.RealmToName(realm)} keep=\"{keep.Name}\" guards={alive}/{guards.Length}");
                string key = $"keep:{keep.KeepID}";
                if (!IsThin(guards.Length, alive) || !AboutIsFresh(key, KeepRest, now) || !Open(keep, now))
                    continue;
                var slots = ChatSlots(new Dictionary<string, string>
                {
                    ["keep"] = keep.Name,
                    ["place"] = keep.CurrentZone?.Description ?? "the frontier",
                    ["enemy"] = AutonomousLivingServer.RealmPlural(keep.Realm),
                }, Random.Shared);
                AbstractGameKeep thin = keep;
                eRealm owner = keep.Realm;
                Tell(realm, "keep_thin", slots, now, key, $"RUMOUR_KEEP realm={GlobalConstants.RealmToName(realm)} keep=\"{keep.Name}\" guards={alive}/{guards.Length}",
                    () => thin.Realm != realm && thin.Realm == owner && Open(thin, GameLoop.GameLoopTime));
                break;
            }
        }
    }

    /// <summary>The live facts <see cref="MayCallOpen"/> reads for a keep: a siege on it, a recent capture, its owners near.</summary>
    private static bool Open(AbstractGameKeep keep, long nowTick)
    {
        bool sieged = AutonomousRvrEventLayer.ActiveSieges(nowTick).Any(siege =>
            string.Equals(siege.KeepName, keep.Name, StringComparison.OrdinalIgnoreCase));
        DateTime utc = DateTime.UtcNow;
        TimeSpan? sinceCapture = AutonomousChatNews.Board.Snapshot()
            .Where(item => item.Topic.StartsWith("keep_", StringComparison.Ordinal) &&
                           string.Equals(item.Keep, keep.Name, StringComparison.OrdinalIgnoreCase))
            .Select(item => (TimeSpan?)(utc - item.When)).OrderBy(age => age).FirstOrDefault();
        int ownersNear = WorldMgr.GetPlayersCloseToSpot(keep.Region, keep.X, keep.Y, keep.Z, OwnersNearRadius)
                             .Count(player => player.Realm == keep.Realm && player.IsAlive) +
                         WorldMgr.GetNPCsCloseToSpot(keep.Region, keep.X, keep.Y, keep.Z, OwnersNearRadius).OfType<GameBot>()
                             .Count(bot => bot.Realm == keep.Realm && bot.IsAlive);
        return MayCallOpen(keep.IsPortalKeep, sieged, sinceCapture, ownersNear);
    }

    /// <summary>
    /// A rumour the realm should hear: said now, or (held by a scene, the ceiling or the spacing after a scene) waiting for its
    /// turn (<see cref="Retry"/>). Null when said; else why not ("scene", "ceiling", "spacing" when it now waits; "no_bot",
    /// "no_line", "no_audience", "waiting" when nothing waits: another rumour already does).
    /// </summary>
    private static string Tell(eRealm realm, string topic, Dictionary<string, string> slots, long now, string key, string said, Func<bool> stillSo)
    {
        if (Due.ContainsKey(realm))
            return "waiting";
        long exchange = AutonomousChatExchange.NewId();
        string why = Say(realm, topic, slots, now, exchange);
        if (why == null)
        {
            if (key != null)
                LastAbout[key] = now;
            Log.Info(said);
            return null;
        }
        if (why is "no_bot" or "no_line" or "no_audience")
            return why;
        // Held: it keeps its turn. Its rest and its subject's start now, so nothing else crowds in behind it.
        if (key != null)
            LastAbout[key] = now;
        Due[realm] = new Waiting
        {
            Topic = topic, Slots = slots, Key = key, Said = said, StillSo = stillSo, Exchange = exchange, Since = now,
            NextAt = now + AutonomousChatExchange.DueRetryMs,
        };
        Log.Info(AutonomousChatExchange.DueLog("rumour", realm, exchange, $" topic={topic} held={why}"));
        return why;
    }

    /// <summary>A waiting rumour's next try: said, tried again in a few seconds, or given up (logged) after three minutes.</summary>
    private static void Retry(eRealm realm, Waiting waiting, long now)
    {
        string why = waiting.StillSo != null && !waiting.StillSo() ? "no_longer_so" : Say(realm, waiting.Topic, waiting.Slots, now, waiting.Exchange);
        if (why == null)
        {
            Due.TryRemove(realm, out _);
            Log.Info($"{waiting.Said} waited_s={(now - waiting.Since) / 1_000}");
            return;
        }
        if (why is "no_audience" or "no_longer_so" ||
            AutonomousChatExchange.AfterTry(false, why, waiting.Since, AutonomousBotDebates.LastSceneEnd(realm), now) == AutonomousChatExchange.DueStep.GiveUp)
        {
            Due.TryRemove(realm, out _);
            Log.Info(AutonomousChatExchange.DroppedLog("rumour", realm, waiting.Exchange, now - waiting.Since, why));
            return;
        }
        waiting.NextAt = now + AutonomousChatExchange.DueRetryMs;
    }

    /// <summary>
    /// A bot of the realm passes the rumour on in /faction, and one or two react (the replies share its exchange). Null when
    /// it was said; else why not.
    /// </summary>
    private static string Say(eRealm realm, string topic, Dictionary<string, string> slots, long now, long exchange)
    {
        if (!AutonomousBotChatCoordinator.HasFactionAudience(realm))
            return "no_audience";
        if (AutonomousBotDebates.IsRunning(realm))
            return "scene";
        GameBot[] bots = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.Realm == realm && AutonomousLivingServer.CanTalk(bot) && !AutonomousBeggars.Is(bot))
            .OrderBy(_ => Random.Shared.Next()).Take(4).ToArray();
        if (bots.Length == 0)
            return "no_bot";
        // The first of them with a line for it tells it (only the first was asked before).
        GameBot teller = null;
        string line = null;
        foreach (GameBot bot in bots)
            if (AutonomousBotVoices.TryEventLine(bot, "rumour", out line, topic, slots))
            {
                teller = bot;
                break;
            }
        if (teller == null)
            return "no_line";
        // The rumour's rest starts even when /faction holds it (1.1.3): a held rumour waits its turn (Tell), not every tick.
        LastRumourAt[realm] = now;
        bool said;
        using (AutonomousChatExchange.Tag("rumour", exchange, 0, "teller", teller))
            said = AutonomousBotChatCoordinator.BroadcastFaction(teller.Name, realm, line);
        if (!said)
            return AutonomousBotChatCoordinator.LastHeld ?? "held";
        AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), teller, line, "chat");
        int due = 0, index = 0;
        foreach (GameBot bot in bots.Where(bot => bot != teller).Take(1 + Random.Shared.Next(2)))
        {
            due += 4_000 + Random.Shared.Next(9_000);
            GameBot speaker = bot;
            int reply = ++index;
            AutonomousLivingServer.Later(speaker, due, () => AutonomousLivingServer.CanTalk(speaker), () =>
            {
                if (!AutonomousBotVoices.TryEventLine(speaker, "rumour_reply", out string text, topic, slots))
                    return;
                using var tag = AutonomousChatExchange.Tag("rumour", exchange, reply, "reactor", speaker);
                AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, text);
            });
        }
        return null;
    }
}
