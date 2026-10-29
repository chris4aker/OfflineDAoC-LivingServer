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
/// <item>An enemy frontier keep whose guards are mostly dead (under 30% alive): "scout says {keep} is wide open".</item>
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

    #region Pure rules (unit tested)

    /// <summary>A spawn worth a rumour: a named monster of no realm, rare (20+ minute respawn), level 20 or more. Pure.</summary>
    public static bool IsRareNamed(string name, bool noRealm, bool isBot, int respawnMs, int level) =>
        AutonomousPlayerDeeds.IsNamed(name, noRealm, isBot) && respawnMs >= RareRespawnMs && level >= MinimumNamedLevel;

    /// <summary>A player who would care: from ten levels under the monster to five over it. Pure.</summary>
    public static bool Cares(int playerLevel, int monsterLevel) => playerLevel >= monsterLevel - 10 && playerLevel <= monsterLevel + 5;

    /// <summary>A keep is thin when it has at least four guards and under 30% of them are alive. Pure.</summary>
    public static bool IsThin(int guards, int alive) => guards >= MinimumGuards && alive < guards * ThinGuardShare;

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
            Log.Debug($"RUMOUR_NAMED_CHECK target=\"{npc.Name}\" level={npc.Level} zone=\"{zone.Description}\" lands={realm} caring_player={cares}");
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
                var slots = new Dictionary<string, string> { ["target"] = monster.Name, ["place"] = zone.Description };
                if (Say(realm, "named_up", slots, now))
                {
                    LastAbout[key] = now;
                    Log.Info($"RUMOUR_NAMED realm={GlobalConstants.RealmToName(realm)} target=\"{monster.Name}\" zone=\"{zone.Description}\"");
                }
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
        var slots = new Dictionary<string, string> { ["target"] = named.Name, ["place"] = named.CurrentZone.Description };
        LastRumourAt.TryRemove(player.Realm, out _);
        about = $"{named.Name} (level {named.Level}) in {named.CurrentZone.Description}";
        // Say's reasons, told apart (in-game check 2026-10-28: "no bot had a line" was shown while a scene was playing).
        if (AutonomousBotDebates.IsRunning(player.Realm))
        {
            about += ": a /faction scene is playing; try again when it ends";
            return false;
        }
        if (!Say(player.Realm, "named_up", slots, GameLoop.GameLoopTime))
        {
            about += ": no free bot of your realm had a line for it";
            return false;
        }
        Log.Info($"RUMOUR_NAMED realm={GlobalConstants.RealmToName(player.Realm)} target=\"{named.Name}\" zone=\"{named.CurrentZone.Description}\" forced=lstest");
        return true;
    }

    /// <summary>Every five minutes: an undefended enemy keep (AutonomousLivingWorld.Pulse).</summary>
    public static void Pulse(long now)
    {
        if (!On || !AutonomousBotVoices.Enabled || now < _nextKeepCheck)
            return;
        _nextKeepCheck = now + 5 * 60_000;
        AbstractGameKeep[] keeps;
        try
        {
            keeps = GameServer.KeepManager.GetFrontierKeeps().Where(keep => keep is not GameKeepTower && keep.Realm != eRealm.None).ToArray();
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
                if (!IsThin(guards.Length, alive) || !AboutIsFresh(key, KeepRest, now))
                    continue;
                var slots = new Dictionary<string, string>
                {
                    ["keep"] = keep.Name,
                    ["place"] = keep.CurrentZone?.Description ?? "the frontier",
                    ["enemy"] = AutonomousLivingServer.RealmPlural(keep.Realm),
                };
                if (Say(realm, "keep_thin", slots, now))
                {
                    LastAbout[key] = now;
                    Log.Info($"RUMOUR_KEEP realm={GlobalConstants.RealmToName(realm)} keep=\"{keep.Name}\" guards={alive}/{guards.Length}");
                }
                break;
            }
        }
    }

    /// <summary>A bot of the realm passes the rumour on in /faction, and one or two react.</summary>
    private static bool Say(eRealm realm, string topic, Dictionary<string, string> slots, long now)
    {
        if (!AutonomousBotChatCoordinator.HasFactionAudience(realm) || AutonomousBotDebates.IsRunning(realm))
            return false;
        GameBot[] bots = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.Realm == realm && AutonomousLivingServer.CanTalk(bot) && !AutonomousBeggars.Is(bot))
            .OrderBy(_ => Random.Shared.Next()).Take(3).ToArray();
        if (bots.Length == 0 || !AutonomousBotVoices.TryEventLine(bots[0], "rumour", out string line, topic, slots))
            return false;
        LastRumourAt[realm] = now;
        AutonomousBotChatCoordinator.BroadcastFaction(bots[0].Name, realm, line);
        AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), bots[0], line, "chat");
        int due = 0;
        foreach (GameBot bot in bots.Skip(1).Take(1 + Random.Shared.Next(2)))
        {
            due += 4_000 + Random.Shared.Next(9_000);
            GameBot speaker = bot;
            AutonomousLivingServer.Later(speaker, due, () => AutonomousLivingServer.CanTalk(speaker), () =>
            {
                if (AutonomousBotVoices.TryEventLine(speaker, "rumour_reply", out string reply, topic, slots))
                    AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, reply);
            });
        }
        return true;
    }
}
