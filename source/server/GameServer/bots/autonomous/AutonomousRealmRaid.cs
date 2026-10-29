using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>A raid owns commitments, never merges the underlying eight-person parties.</summary>
    public static class AutonomousRealmRaid
    {
        public sealed record Definition(string Id, string Name, eRealm Realm, ushort Region, string BossType, int SuggestedBots)
        {
            public bool IsDungeon => Region is 60 or 160 or 191 || IsRaidDungeon(Region) || IsNeutral;
            /// <summary>
            /// Summoner's Hall and Darkness Falls belong to no realm: each realm runs its own
            /// expedition there (two or, rarely, all three at once) and they fight each other inside
            /// and on the way.
            /// </summary>
            public bool IsNeutral => RealmRaidNeutralEvents.IsNeutralRegion(Region);
            /// <summary>The dungeon walked through on the way in (Crystal Cave: Avalon City), or 0.</summary>
            public ushort Through => RaidDungeons.FirstOrDefault(d => d.Region == Region)?.Through ?? 0;
            /// <summary>The home region whose entrance a raid stages at when it goes in through another dungeon (neutral
            /// raids, Crystal Cave), or 0 for a dungeon entered straight from open ground.</summary>
            public ushort EntranceHome(RealmRaidMuster.Hub hub) => IsNeutral || Through != 0 ? hub.Region : (ushort)0;
            public Vector3 Trigger => Region switch
            {
                60 => new(29462, 25240, 19490),
                160 => new(34542, 57121, 11881),
                191 => new(39652, 60831, 11893),
                _ => RaidDungeons.FirstOrDefault(d => d.Region == Region) is { } raid ? raid.Final
                    : RealmRaidNeutralEvents.FinalApproach(Region)
            };
            public string[] FinalTypes => Region switch
            {
                60 => ["Apocalypse"], 160 => ["KingTuscar", "QueenKula"],
                191 => ["Olcasgean"],
                _ => RaidDungeons.FirstOrDefault(d => d.Region == Region)?.Finals ?? [BossType]
            };
            /// <summary>Named encounters a neutral expedition clears in order (the final boss last).</summary>
            public string[] Objectives => RealmRaidNeutralEvents.Objectives(Region);
        }
        /// <summary>
        /// The other multi-group dungeons (owner, 2026-10-20: "we have every multi group dungeon covered?"): each realm's
        /// Shrouded Isles raid dungeons and its Catacombs dungeon by the way to Summoner's Hall. Raided like the epic
        /// dungeons (clear the way in for the first hour, then the named encounters, the final boss last), on their own
        /// turn of the schedule. Final = the final boss's room, where the route stages last.
        /// Crystal Cave is entered only through Avalon City: its raid stages outside Avalon City and walks through it, the
        /// way a Summoner's Hall raid walks through Hall of the Corrupt (<see cref="RaidDungeon.Through"/>).
        /// Not here: Fomor City (Myrddraxis), whose navmesh has no path from the entrance to the central chamber of Myrddraxis and
        /// Anurigunda (installed zone180.nav, 2026-10-20: the path stops 3,000-3,600 units short on both sides).
        /// </summary>
        public sealed record RaidDungeon(string Id, string Name, eRealm Realm, ushort Region, string BossType, int SuggestedBots,
            Vector3 Final, string[] Finals, ushort Through = 0);

        public static readonly RaidDungeon[] RaidDungeons =
        [
            new("dungeon-albion-krondon", "Krondon", eRealm.Albion, 61, "Orylle", 160, new(31413, 70773, 15765), ["Orylle"]),
            new("dungeon-albion-avalon", "Avalon City", eRealm.Albion, 50, "DuraekEmpowered", 120, new(32425, 28962, 8756), ["DuraekEmpowered"]),
            new("dungeon-albion-crystal", "Crystal Cave", eRealm.Albion, 62, "Xanxicar", 120, new(31577, 35305, 14551), ["Xanxicar"], Through: 50),
            new("dungeon-albion-corrupt", "Hall of the Corrupt", eRealm.Albion, 277, "AidonTheArchwizard", 120, new(31353, 37634, 14873),
                ["AidonTheArchwizard", "LordGildas"]),
            new("dungeon-midgard-trollheim", "Trollheim", eRealm.Midgard, 150, "Nosdoden", 160, new(37538, 12262, 13377), ["Nosdoden"]),
            new("dungeon-midgard-iarnvidiur", "Iarnvidiur's Lair", eRealm.Midgard, 161, "Iarnvidiur", 120, new(13848, 11523, 18263), ["Iarnvidiur"]),
            new("dungeon-midgard-dodens", "Dodens Gruva", eRealm.Midgard, 246, "SpiritmasterAros", 96, new(39061, 36970, 15495), ["SpiritmasterAros"]),
            new("dungeon-hibernia-tursuil", "Tur Suil", eRealm.Hibernia, 190, "Balor", 160, new(32032, 31048, 15984), ["Balor"]),
            new("dungeon-hibernia-marfach", "Marfach Caverns", eRealm.Hibernia, 276, "BlackLady", 64, new(30086, 37378, 15001),
                ["BlackLady", "ChieftainCaimheul", "Avartack"]),
        ];

        public static bool IsRaidDungeon(ushort region) => RaidDungeons.Any(d => d.Region == region);

        public sealed record View(string EventId, string State, AutonomousBotGroupCoordinator.SharedCamp Camp, bool Hold, bool Muster = false, bool Crossing = false);
        public sealed record Summary(string Id, string Name, string Realm, string State, int Assigned, int Present, int Suggested, long Remaining, string Phase = "");
        private sealed class Raid
        {
            public Definition Definition;
            public GameNPC Boss;
            public long Deadline;
            public bool Started;
            public bool PreparationNoticeSent;
            public bool Forced;
            public long Created;
            public RealmRaidMuster.Hub Hub;
            public bool HubDeparted;
            public long HubDepartedTick;
            public long NextDefenseBroadcast;
            public readonly Dictionary<int, Vector3> HubPosts = new();
            public Vector3[] OutboundSeams = [];
            public readonly List<GameBot[]> ForcedParties = new();
            public readonly Dictionary<int, Vector3> StagingPosts = new();
            /// <summary>Formation slots with no staging or hub post (tight ground by the entrance); skipped from then on.</summary>
            public readonly HashSet<int> UnusableSlots = new();
            public RealmRaidDungeonRoute DungeonRoute;
            public readonly RealmRaidLootOwner.Ledger LootLedger = new();
            public readonly Dictionary<Group, Party> Parties = new();
            public GameLiving[] Support = [];
            /// <summary>Members released from this raid (no route to the hub); never recruited back.</summary>
            public readonly HashSet<long> Released = new();
            public long NextRefill;
            public long NextPostWarning;
            /// <summary>Level-50 bots this raid must stage (200 when forced; scaled to the realm when automatic).</summary>
            public int Required = RealmRaidRecruitmentPolicy.AutonomousMinimumPresent;
            /// <summary>This raid's members did most of the damage to its final encounter (LootOwner).</summary>
            public bool FinalKillCredited;
            /// <summary>Another realm's raid was credited with this raid's final encounter (a contested neutral dungeon).</summary>
            public eRealm BeatenBy = eRealm.None;
            /// <summary>This raid has a Leeroy (AutonomousRaidLeeroy): a dragon raid's charge at staging, once.</summary>
            public bool LeeroyRolled, LeeroyDone;
            public string LeeroyName;
        }
        private sealed class Party
        {
            public GameBot[] Members;
            public int FormationSlot;
            public Vector3 Staging;
            public Vector3 HubPost;
            public bool Departed;
            public int OutboundLeg;
            public Vector3? Anchor;
            public Vector3 AnchorCenter;
            public ushort AnchorRegion;
            // A post search that found nothing is not repeated for the same spot for a while (see TryBattlePost).
            public Vector3 FailedPostCenter;
            public long FailedPostUntil;
            public View View;
            public View DestinationView;
            public readonly HashSet<long> CatchingUp = new();
            public readonly Dictionary<long, long> CorpseSince = new();
            /// <summary>The party size when it joined; released members are replaced up to it.</summary>
            public int Target;
        }
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(AutonomousRealmRaid));
        private static readonly object Sync = new();
        private static readonly Dictionary<string, Raid> Raids = new();
        // Every party destination of a running neutral-dungeon raid, rebuilt each pulse and read
        // without the raid lock by the region router (Darkness Falls entrance rule).
        private static volatile (ushort Region, int X, int Y)[] _raidDestinations = [];
        private static readonly Dictionary<(ushort Region, int X, int Y), long> RecentDestinations = new();
        private const long DestinationMemory = 15 * 60_000;

        public static bool HasActiveRaidIn(ushort region)
        {
            foreach (var d in _raidDestinations)
                if (d.Region == region) return true;
            return false;
        }

        /// <summary>
        /// Run 8 (2026-10-14): the Albion raid in Darkness Falls stood 6,000-10,000 units short of Prince Ba'alorien
        /// for forty minutes, fighting essence shredders, naburite drinkers and succubi that came back every 100-180
        /// seconds (263 essence shredders alone), and skipped him untouched. While a bot raid is inside Darkness
        /// Falls, Summoner's Hall, Caer Sidi or Galladoria, the ordinary monsters there stay down for ten minutes;
        /// bosses, epic encounters and scripted adds keep their own timers, and nothing changes once the raid ends.
        /// </summary>
        public const int RaidTrashRespawnMilliseconds = 10 * 60_000;
        public static readonly ushort[] SlowRespawnRegions = [249, 248, 60, 191, .. RaidDungeons.Select(d => d.Region)];

        public static int RaidRespawnInterval(GameNPC npc, int interval)
        {
            if (interval <= 0 || interval >= RaidTrashRespawnMilliseconds || npc is IGameEpicNpc or GameSummonedPet or GameBot ||
                npc.Realm != eRealm.None || Array.IndexOf(SlowRespawnRegions, npc.CurrentRegionID) < 0 || !HasActiveRaidIn(npc.CurrentRegionID))
                return interval;
            return RaidTrashRespawnMilliseconds;
        }

        public static bool IsActiveRaidDestination(ushort region, int x, int y)
        {
            foreach (var d in _raidDestinations)
                if (d.Region == region && Math.Abs(d.X - x) <= 64 && Math.Abs(d.Y - y) <= 64) return true;
            return false;
        }
        private static readonly Dictionary<Group, Raid> Membership = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Group, byte> DefenseGroups = new();
        private static readonly Dictionary<Group, string> Released = new();
        private static readonly Dictionary<string, long> Cooldowns = new();
        // Each realm's last raid that killed its final boss (automatic or forced); the next automatic start rolls any other.
        private static readonly Dictionary<eRealm, string> LastSuccessful = new();

        /// <summary>
        /// Owner, 2026-10-20: the schedule is a random roll among every raid the realm has available, setting aside only
        /// the one it last won. A raid that failed may come straight back.
        /// </summary>
        public static bool MayStartAutomatic(string id, string lastSuccessfulId) =>
            !string.Equals(id, lastSuccessfulId, StringComparison.Ordinal);
        private static readonly Dictionary<string, GameNPC> Bosses = new();
        private sealed record FinishedLoot(GameBot[] Members, RealmRaidLootOwner.Ledger Ledger, long Expires);
        private static readonly Dictionary<GameNPC, FinishedLoot> CompletedLoot = new();
        private static long _nextPulse;
        private static long _nextForcedRecruitment;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, string> Reservations = new();
        public static bool IsReserved(GameBot bot) => bot != null && Reservations.ContainsKey(bot.DatabaseID);

        /// <summary>A member of a raid party whose rally has not begun its battle yet (hub, travel or staging).</summary>
        public static bool IsInRally(GameBot bot)
        {
            Group group = bot?.Group;
            if (group == null) return false;
            lock (Sync) return Membership.TryGetValue(group, out Raid raid) && !raid.Started && raid.Parties.ContainsKey(group);
        }
        public static bool IsReservedFor(GameBot bot, string id) => bot != null && Reservations.GetValueOrDefault(bot.DatabaseID) == id;
        public static bool IsForcedExpedition(string id)
        {
            lock (Sync) return Raids.TryGetValue(id, out var raid) && raid.Forced;
        }
        public static bool IsEligible(GameBot bot) => bot != null && !AutonomousBeggars.Is(bot) && RealmRaidRecruitmentPolicy.Eligible(
            bot.Level, bot.IsAutonomousWorldBot, bot.IsTemporaryGroupHelper, bot.IsPlayerLedGroup);

        /// <summary>
        /// An automatic raid recruits level 50s waiting for a group and level 50s grinding solo (who earn no
        /// experience there); RvR bots are left to the frontier.
        /// </summary>
        public static bool MayAnswerAutomaticRaid(GameBot bot) => bot != null &&
            (AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.GroupPve) ||
             bot.Level >= 50 && AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.SoloPve));

        private static int OnlineLevelFifty(eRealm realm) =>
            AutonomousBotRegistry.Snapshot().Count(b => IsEligible(b) && b.Realm == realm);

        private static readonly eRealm[] ScheduledRealms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];
        private static readonly Dictionary<eRealm, long> NextScheduled = new();
        /// <summary>Opens each realm's scheduled raid: a random roll among its available raids (dragon, epic dungeon,
        /// neutral raids, the other raid dungeons), except the one it last won.</summary>
        private static void PulseSchedule(long now)
        {
            int minutes = RealmRaidRecruitmentPolicy.ScheduleMinutes;
            for (int index = 0; index < ScheduledRealms.Length; index++)
            {
                eRealm realm = ScheduledRealms[index];
                if (!NextScheduled.TryGetValue(realm, out long due))
                    NextScheduled[realm] = due = now + RealmRaidRecruitmentPolicy.FirstScheduledDelayMilliseconds(index);
                int online = OnlineLevelFifty(realm);
                if (!RealmRaidRecruitmentPolicy.ScheduledRaidDue(now, due, Raids.Values.Any(r => r.Definition.Realm == realm), online, minutes))
                    continue;
                NextScheduled[realm] = now + RealmRaidRecruitmentPolicy.NextScheduleMilliseconds(minutes, Random.Shared.NextDouble());
                Definition chosen = Definitions.Where(d => d.Realm == realm && Available(d.Id) &&
                        MayStartAutomatic(d.Id, LastSuccessful.GetValueOrDefault(realm)))
                    .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
                if (chosen == null)
                {
                    Log.Info($"REALM_RAID_SCHEDULE_SKIPPED realm={realm} online={online} reason=\"no encounter available (respawning or on cooldown)\"");
                    continue;
                }
                if (Start(chosen.Id, realm, out string reason))
                {
                    Log.Info($"REALM_RAID_SCHEDULED event={chosen.Id} realm={realm} online={online} required={Raids[chosen.Id].Required} " +
                             $"recruitCap={RealmRaidRecruitmentPolicy.RecruitCap(Raids[chosen.Id].Required)} nextMinutes={minutes}");
                }
                else
                    Log.Warn($"REALM_RAID_SCHEDULE_FAILED event={chosen.Id} realm={realm} online={online} reason=\"{reason}\"");
            }
        }
        /// <summary>
        /// GM test (/lstest raid): the realm's automatic raid opens now, sized to its online level 50s like a scheduled one
        /// (forced raids from the launcher need 300). <paramref name="dragon"/> picks the realm's dragon, else a random
        /// available raid. Returns false with <paramref name="reason"/>.
        /// </summary>
        public static bool StartAutomaticForTest(eRealm realm, bool dragon, out string reason)
        {
            lock (Sync)
            {
                int online = OnlineLevelFifty(realm);
                if (Raids.Values.Any(r => r.Definition.Realm == realm))
                {
                    reason = "that realm already has a raid running (watch or stop it on the launcher's Realm Events tab)";
                    return false;
                }
                if (online < RealmRaidRecruitmentPolicy.MinimumScaledBots)
                {
                    reason = $"only {online} level 50 gamebot(s) of that realm are free for a raid (the rest are at sieges, in groups or in town); at least {RealmRaidRecruitmentPolicy.MinimumScaledBots} are needed";
                    return false;
                }
                Definition chosen = Definitions.Where(d => d.Realm == realm && Available(d.Id) && (!dragon || d.Id.StartsWith("dragon-", StringComparison.Ordinal)))
                    .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
                if (chosen == null)
                {
                    reason = dragon ? "that realm's dragon is not up (respawning or on cooldown)" : "no raid encounter of that realm is up";
                    return false;
                }
                if (!Start(chosen.Id, realm, out reason))
                    return false;
                reason = $"{chosen.Name}: {Raids[chosen.Id].Required} of {online} free level 50s must gather (at least 15 minutes)";
                Log.Info($"REALM_RAID_SCHEDULED event={chosen.Id} realm={realm} online={online} required={Raids[chosen.Id].Required} forced=lstest");
                return true;
            }
        }

        public static readonly Definition[] Definitions =
        [
            new("dragon-albion", "Golestandt", eRealm.Albion, 1, "AlbGolestandt", 200),
            new("dragon-midgard", "Gjalpinulva", eRealm.Midgard, 100, "MidGjalpinulva", 200),
            new("dragon-hibernia", "Cuuldurach", eRealm.Hibernia, 200, "HibCuuldurach", 200),
            new("epic-albion", "Caer Sidi", eRealm.Albion, 60, "ApocInitializator", 200),
            new("epic-midgard", "Tuscaran Glacier", eRealm.Midgard, 160, "KingTuscar", 200),
            new("epic-hibernia", "Galladoria", eRealm.Hibernia, 191, "Olcasgean", 200),
            new("summoners-albion", "Summoner's Hall", eRealm.Albion, 248, "GrandSummonerGovannon", 200),
            new("summoners-midgard", "Summoner's Hall", eRealm.Midgard, 248, "GrandSummonerGovannon", 200),
            new("summoners-hibernia", "Summoner's Hall", eRealm.Hibernia, 248, "GrandSummonerGovannon", 200),
            new("darkness-albion", "Darkness Falls", eRealm.Albion, 249, "Legion", 200),
            new("darkness-midgard", "Darkness Falls", eRealm.Midgard, 249, "Legion", 200),
            new("darkness-hibernia", "Darkness Falls", eRealm.Hibernia, 249, "Legion", 200),
            ..RaidDungeons.Select(d => new Definition(d.Id, d.Name, d.Realm, d.Region, d.BossType, d.SuggestedBots))
        ];

        public static void Pulse(long now)
        {
            lock (Sync)
            {
                if (now < _nextPulse) return;
                _nextPulse = now + 10_000;
                foreach (GameNPC expired in CompletedLoot.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray())
                    CompletedLoot.Remove(expired);
                foreach (Definition definition in Definitions)
                {
                    // Remember dead instances: their actual IsRespawning flag is
                    // authoritative even after they leave the region object list.
                    if (!Bosses.TryGetValue(definition.Id, out var remembered) || remembered.ObjectState != GameObject.eObjectState.Active)
                    {
                        var boss = WorldMgr.GetRegion(definition.Region)?.Objects.OfType<GameNPC>()
                            .FirstOrDefault(n => n.GetType().Name == definition.BossType && n.IsAlive);
                        if (boss != null) Bosses[definition.Id] = boss;
                    }
                }
                PulseSchedule(now);
                foreach (var raid in Raids.Values.ToArray())
                {
                    foreach (Group group in raid.Parties.Keys.ToArray())
                    {
                        Party party = raid.Parties[group];
                        foreach (var alive in party.Members.Where(b => b.IsAlive)) party.CorpseSince.Remove(alive.DatabaseID);
                        if (party.Members.Any(b => !IsEligible(b) || b.Group != group || !AutonomousBotRegistry.Contains(b.DatabaseID)))
                            RemoveParty(group);
                    }
                    raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
                    int presentAtHub = raid.Parties.Values.Sum(p => PresentAtHub(raid, p));
                    bool departing = !raid.HubDeparted && RealmRaidRecruitmentPolicy.DepartHub(false, presentAtHub,
                        raid.Forced ? 0 : GameLoop.GameLoopTime - raid.Created, raid.Required);
                    raid.HubDeparted |= departing;
                    if (departing) raid.HubDepartedTick = GameLoop.GameLoopTime;
                    if (departing) RealmEventNotices.Queue(raid.Definition.Id, raid.Definition.Realm,
                        $"{presentAtHub} adventurers present at {raid.Hub.Name}; departing together for {raid.Definition.Name}. Late arrivals will join the expedition directly.",
                        "raid_forming", RaidSlots(raid.Definition.Name, null));
                    if (raid.HubDeparted)
                        foreach (Party p in raid.Parties.Values)
                            if (!p.Departed)
                            {
                                foreach (var b in p.Members.Where(b => !IsAtHub(raid, p, b))) p.CatchingUp.Add(b.DatabaseID);
                                p.Departed = true;
                            }
                    if (!raid.Definition.IsDungeon && (!raid.Boss.IsAlive || raid.Boss.ObjectState != GameObject.eObjectState.Active))
                    { End(raid, now, "The dragon encounter has ended; checking actual respawn before the next expedition."); continue; }
                    if (raid.Started && raid.Definition.IsNeutral)
                        RealmRaidNeutralEvents.ApplyEncounterLevels(raid.Definition.Region, raid.Definition.Objectives);
                    raid.DungeonRoute?.ObserveCompletion();
                    if (raid.DungeonRoute?.Complete == true)
                    { End(raid, now, CompletionReason(raid, "The final dungeon encounter has been defeated.")); continue; }
                    if (raid.Started && RealmRaidRecruitmentPolicy.BattleExpired(raid.Forced, now, raid.Deadline))
                    { End(raid, now, "The four-hour expedition window ended."); continue; }
                    if (!raid.Started)
                    {
                        long noticeDeadline = RealmRaidRecruitmentPolicy.StagingDeadline(raid.Created, raid.HubDepartedTick);
                        if (!raid.Forced && RealmEventBanter.ReminderDue(raid.Started, raid.PreparationNoticeSent, noticeDeadline - now))
                        {
                            raid.PreparationNoticeSent = true;
                            RealmEventNotices.Queue(raid.Definition.Id, raid.Definition.Realm,
                                RealmEventBanter.RaidReminder(raid.Definition.Name, raid.Forced), "raid_forming", RaidSlots(raid.Definition.Name, null));
                        }
                        int present = PresentAtStaging(raid);
                        bool ready = RealmRaidRecruitmentPolicy.Ready(raid.Forced, now - raid.Created, present, DragonLanded(raid), raid.Required);
                        bool expired = !ready && RealmRaidRecruitmentPolicy.StagingExpired(raid.Forced, now, raid.Created, raid.HubDepartedTick,
                            present, DragonLanded(raid), raid.Required);
                        // Out of time with at least half the raid there: it goes in short, and the rest follow it in.
                        if (expired && DragonLanded(raid) && RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(raid.Forced, present, raid.Required))
                        {
                            ready = true;
                            Log.Info($"REALM_RAID_SHORT_START event={raid.Definition.Id} present={present} required={raid.Required} " +
                                     $"recruited={raid.Support.Length} minutes={(now - raid.Created) / 60_000}");
                        }
                        // A dragon raid's Leeroy: half the raid is there, not all, and he goes; the raid goes in after him. A raid
                        // that is ready before that moment came still has him: he jumps its pull.
                        GameBot leeroy = null;
                        GameLiving[] staged = null;
                        if (raid.LeeroyRolled && !raid.LeeroyDone && !raid.Definition.IsDungeon && DragonLanded(raid) &&
                            (ready || AutonomousRaidLeeroy.GoesNow(AutonomousRaidLeeroy.ShouldChargeStaging(present, raid.Required, now - raid.Created), present)))
                        {
                            raid.LeeroyDone = true;
                            staged = StagedBots(raid);
                            leeroy = AutonomousRaidLeeroy.Pick(staged);
                            if (leeroy != null)
                            {
                                ready = true;
                                Log.Info($"REALM_RAID_LEEROY_START event={raid.Definition.Id} bot=\"{leeroy.Name}\" present={present} required={raid.Required}");
                            }
                        }
                        if (ready)
                        {
                            raid.Started = true;
                            raid.Deadline = raid.Forced ? long.MaxValue : now + RealmRaidRecruitmentPolicy.BattleMilliseconds;
                            RealmEventRecords.Progress(raid.Definition.Id, "Battle", "Expedition began advancing to the encounter.", raid.Support.Length, present);
                            RealmEventNotices.Queue(raid.Definition.Id, raid.Definition.Realm,
                                $"The {raid.Definition.Name} expedition is advancing with {present} staged level-50 adventurers.");
                            // An expedition that lost here the last two times brings the Epic Special Forces from the start.
                            if (raid.Definition.IsDungeon && AutonomousEpicSpecialForces.ComesFromStart(raid.Definition.Id))
                                AutonomousEpicSpecialForces.Request(raid.Definition.Id, raid.Definition.Name, raid.Definition.Realm,
                                    "the last two expeditions here were defeated");
                            if (leeroy != null && AutonomousRaidLeeroy.Charge(leeroy, raid.Boss, staged, raid.Definition.Name))
                                raid.LeeroyName = leeroy.Name;
                        }
                        else if (expired)
                        { End(raid, now, $"Staging failed: {present} adventurers arrived; no undersized or airborne assault was ordered."); continue; }
                    }
                    if (raid.Started && raid.DungeonRoute != null)
                    {
                        raid.DungeonRoute.Advance(raid.Support, now);
                        while (raid.DungeonRoute.TryTakeWipe(out string wipedBy, out int failures))
                            OnWipe(raid, wipedBy, failures, now);
                        if (raid.DungeonRoute.Defeated)
                        {
                            End(raid, now, $"Defeated at {raid.DungeonRoute.DefeatedAt} after {RealmRaidDungeonRoute.MaximumFailedAttempts} failed attempts.");
                            continue;
                        }
                        if (raid.DungeonRoute.Blocked)
                        { End(raid, now, "Route blocked: no remaining encounter was reachable from the raid's positions for 20 minutes."); continue; }
                        if (raid.DungeonRoute.Complete)
                        { End(raid, now, CompletionReason(raid, $"The {raid.Definition.Name} expedition defeated the final encounter.")); continue; }
                    }
                    foreach (Party party in raid.Parties.Values) UpdateView(raid, party);
                }
                foreach (string id in Cooldowns.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) Cooldowns.Remove(id);
                // Late members still walking toward an earlier waypoint keep a valid destination
                // for a while after the front moved on (the Darkness Falls entrance rule checks it);
                // nothing outlives the raids running in that region.
                foreach (var c in Raids.Values.Where(r => r.Definition.IsNeutral)
                             .SelectMany(r => r.Parties.Values.SelectMany(p => new[] { p.View?.Camp, p.DestinationView?.Camp }))
                             .Where(c => c != null && RealmRaidNeutralEvents.IsNeutralRegion(c.RegionId)))
                    RecentDestinations[(c.RegionId, c.X, c.Y)] = now;
                var running = Raids.Values.Where(r => r.Definition.IsNeutral).Select(r => r.Definition.Region).ToHashSet();
                foreach (var key in RecentDestinations.Where(p => now - p.Value > DestinationMemory || !running.Contains(p.Key.Region))
                             .Select(p => p.Key).ToArray())
                    RecentDestinations.Remove(key);
                _raidDestinations = RecentDestinations.Keys.ToArray();
            }
        }

        public static bool TryJoin(GameBot leader, out View view)
        {
            view = null;
            if (leader?.Group == null || leader.Group.LivingLeader != leader) return false;
            GameBot[] members = leader.Group.GetMembersInTheGroup().OfType<GameBot>().ToArray();
            // A whole PvE party of five to eight level 50s may answer (most parties form with five or six).
            if (members.Length is < RealmRaidRecruitmentPolicy.MinimumJoiningParty or > 8 || members.Any(b => !IsEligible(b) || IsReserved(b) ||
                b.Realm != leader.Realm || !AutonomousObjectiveAssignments.Is(b, eAutonomousObjectiveKind.GroupPve))) return false;
            lock (Sync)
            {
                if (Membership.TryGetValue(leader.Group, out Raid joined)) { view = joined.Parties[leader.Group].View; return true; }
                Raid raid = Raids.Values.FirstOrDefault(r => r.Definition.Realm == leader.Realm && !r.Forced && r.Parties.Count < RealmRaidRecruitmentPolicy.MaximumParties);
                if (raid == null && Raids.Values.Any(r => r.Definition.Realm == leader.Realm)) return false;
                if (raid == null)
                {
                    // Only a full eight opens an unscheduled raid (the schedule is the regular way); smaller
                    // parties only join one that is recruiting.
                    if (members.Length != 8) return false;
                    // Decisions occur once when a party chooses a new task, not
                    // once per member or AI tick; keep ordinary PvE populated.
                    if (Random.Shared.NextDouble() >= RealmRaidRecruitmentPolicy.NewEventChance) return false;
                    if (OnlineLevelFifty(leader.Realm) < RealmRaidRecruitmentPolicy.MinimumScaledBots * 2) return false;
                    var definition = Definitions.Where(d => d.Realm == leader.Realm && Available(d.Id) &&
                            MayStartAutomatic(d.Id, LastSuccessful.GetValueOrDefault(leader.Realm)))
                        .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
                    if (definition == null || !Start(definition.Id, leader.Realm, out _)) return false;
                    raid = Raids[definition.Id];
                }
                else if (Random.Shared.NextDouble() >= RealmRaidRecruitmentPolicy.JoinExistingChance) return false;
                // Removing a party must not make its count reuse a surviving
                // party's staging slot and pile reinforcements on top of it.
                if (raid.Support.Length + members.Length > RealmRaidRecruitmentPolicy.MaximumBots) return false;
                if (!TryFreeSlot(raid, out int slot, out Vector3 staging, out var hubPost)) return false;
                var party = new Party { Members = members, FormationSlot = slot, Staging = staging, HubPost = hubPost, Target = members.Length };
                raid.Parties[leader.Group] = party;
                Membership[leader.Group] = raid;
                DefenseGroups[leader.Group] = 0;
                raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
                UpdateView(raid, party);
                view = party.View;
                return true;
            }
        }

        public static bool Start(string id, eRealm realm, out string reason, bool forced = false)
        {
            RealmRaidMuster.Hub rallyHub = RealmRaidMuster.Hubs.FirstOrDefault(h => h.Event == id);
            // A forced dungeon raid proves its entrance before reserving 300 bots: a route that can never be built
            // reports why at once instead of after the roster is gathered (forced Darkness Falls, 2026-10-11).
            Definition preview = forced ? Definitions.FirstOrDefault(d => d.Id == id && d.Realm == realm) : null;
            if (preview is { IsDungeon: true } && !RealmRaidMuster.Hubs.Where(h => h.Event == id).Any(h =>
                    RealmRaidDungeonRoute.TryCreate(preview.Region, preview.Trigger, preview.FinalTypes, out _, realm,
                        preview.EntranceHome(h), preview.Objectives, h.Center)))
            {
                reason = $"The dungeon entrance/final approach could not be validated ({RealmRaidDungeonRoute.LastFailure}). No party was assigned.";
                Log.Warn($"REALM_RAID_START_UNAVAILABLE event={id} reason=\"no validated entrance\" detail=\"{RealmRaidDungeonRoute.LastFailure}\"");
                return false;
            }
            if (preview != null) SupersedeAutomatic(realm, id);
            GameBot[][] recruits = forced && rallyHub != null
                ? AutonomousBotGroupCoordinator.PlanForcedRaid(realm, rallyHub.Region, rallyHub.Center) : [];
            if (forced && recruits.Sum(p => p.Length) < RealmRaidRecruitmentPolicy.MaximumBots)
            {
                reason = $"Not started: only {recruits.Sum(p => p.Length)}/300 eligible level-50 bots could be reserved. No tasks were cancelled.";
                return false;
            }
            lock (Sync)
            {
                Definition definition = Definitions.FirstOrDefault(d => d.Id == id && d.Realm == realm);
                if (definition == null || !Available(id)) { reason = "The encounter is not alive/available, or this event is on cooldown."; return false; }
                if (!RealmRaidRecruitmentPolicy.CanOpenEvent(forced, Raids.Values.Any(r => r.Definition.Realm == realm), Raids.ContainsKey(id)))
                { reason = "This realm already has an autonomous PvE expedition, or this encounter is already assigned."; return false; }
                if (forced && recruits.SelectMany(p => p).Any(b => IsReserved(b) || GetView(b.Group) != null))
                { reason = "A planned recruit joined another expedition. Nothing was reassigned; please retry."; return false; }
                long now = GameLoop.GameLoopTime;
                // An event may list several muster hubs; the first with a complete, connected
                // hub-to-encounter route is used (far frontier entrances, e.g. Summoner's Hall).
                Raid raid = null;
                reason = "No muster hub is defined for this event.";
                foreach (RealmRaidMuster.Hub hub in RealmRaidMuster.Hubs.Where(h => h.Event == id))
                {
                    var candidate = new Raid { Definition = definition, Boss = Bosses[id], Forced = forced, Created = now, Hub = hub,
                        Required = forced ? RealmRaidRecruitmentPolicy.AutonomousMinimumPresent
                            : RealmRaidRecruitmentPolicy.RequiredBots(OnlineLevelFifty(realm), definition.SuggestedBots, RealmRaidRecruitmentPolicy.MinimumBotsSetting),
                        Deadline = forced ? long.MaxValue : now + RealmRaidRecruitmentPolicy.AutonomousStagingLimitMilliseconds };
                    if (definition.IsDungeon)
                    {
                        if (!RealmRaidDungeonRoute.TryCreate(definition.Region, definition.Trigger, definition.FinalTypes, out var route,
                                realm, definition.EntranceHome(hub), definition.Objectives, hub.Center))
                        {
                            reason = "The dungeon entrance/final approach could not be validated. No party was assigned.";
                            Log.Warn($"REALM_RAID_START_UNAVAILABLE event={id} hub=\"{hub.Name}\" reason=\"no validated entrance\" detail=\"{RealmRaidDungeonRoute.LastFailure}\"");
                            continue;
                        }
                        candidate.DungeonRoute = route;
                    }
                    bool staged = TryStaging(candidate, 0, out var destination);
                    bool posted = TryHubPost(candidate, 0, out var origin);
                    if (!staged || !posted || !RealmRaidMuster.TryRoute(WorldMgr.GetRegion(hub.Region), PathfindingProvider.Instance, realm,
                            origin, destination, out candidate.OutboundSeams, hub.Via))
                    {
                        reason = "No complete connected hub-to-encounter route was found. No party was assigned.";
                        Log.Warn($"REALM_RAID_START_UNAVAILABLE event={id} hub=\"{hub.Name}\" staging={staged} hubPost={posted} " +
                                 $"entrance={candidate.DungeonRoute?.Entrance?.Id} route=\"{(staged && posted ? RealmRaidMuster.LastRouteFailure : "not tried")}\"");
                        continue;
                    }
                    raid = candidate;
                    break;
                }
                if (raid == null) return false;
                Raids[id] = raid;
                // One raid in twenty has a raider who can't wait for the final encounter (bot_living_leeroy).
                raid.LeeroyRolled = AutonomousRaidLeeroy.On && AutonomousRaidLeeroy.RollForRaid(Random.Shared.NextDouble());
                if (raid.DungeonRoute != null) raid.DungeonRoute.LeeroyRolled = raid.LeeroyRolled;
                if (raid.LeeroyRolled) Log.Info($"RAID_LEEROY_ROLLED event={id}");
                RealmEventRecords.Begin(id, definition.Name, definition.IsNeutral ? "Neutral raid" : definition.IsDungeon ? "Epic dungeon" : "Dragon",
                    GlobalConstants.RealmToName(realm), (forced ? "Forced" : "Automatic") + " rally via " + raid.Hub.Name);
                raid.ForcedParties.AddRange(recruits);
                foreach (GameBot bot in recruits.SelectMany(p => p)) Reservations[bot.DatabaseID] = id;
                string startCondition = ForcedStartCondition(definition.IsDungeon);
                // One quiet top-of-screen line for the realm's players (automatic and launcher-forced alike),
                // so a player can join the bots at the rally point.
                RealmEventNotices.QueueScreen(id, realm, $"A {definition.Name} raid is forming at {raid.Hub.Name}.", AnnouncementKind.PveRealmEvent);
                RealmEventNotices.Queue(id, realm, forced
                    ? $"{definition.Name}: {recruits.Sum(p => p.Length)} level-50 adventurers reserved; gather at {raid.Hub.Name}. {startCondition}"
                    : $"{definition.Name}: recruiting up to 300 level-50 adventurers via {raid.Hub.Name}; at least 200 must arrive before assault.",
                    "raid_forming", RaidSlots(definition.Name, null));
                reason = forced ? $"Reserved {recruits.Sum(p => p.Length)} level-50 bots, closest to {raid.Hub.Name} first. The raid starts as soon as 200 are staged and runs until the encounter is defeated or you press Stop event. {startCondition}" :
                    $"Recruiting up to 300 level-50 bots via {raid.Hub.Name}. At least 200 must arrive before assault; staging can last up to {RealmRaidRecruitmentPolicy.AutonomousStagingLimitMilliseconds / 60_000} minutes.";
                return true;
            }
        }

        // Epic dungeons have no landing to wait for; only dragon rallies mention it.
        public static string ForcedStartCondition(bool isDungeon) => isDungeon
            ? "At least 200 must arrive before the dungeon assault."
            : "At least 200 must arrive and the dragon must land.";

        // A grounded dragon that is fighting counts as landed wherever the fight
        // dragged it. Requiring the 2,000-unit lair circle parked most of a raid
        // on "Waiting for dragon landing" while a few bots fought on (Cuuldurach
        // took 2 h 20 min on 2026-10-02).
        private static bool DragonLanded(Raid raid) => raid.Definition.IsDungeon ||
            RealmRaidStaging.DragonCountsAsLanded((raid.Boss.Flags & GameNPC.eFlags.FLYING) != 0,
                raid.Boss.IsWithinRadius(DragonLairPlacement.Home(raid.Definition.Realm), 2000), raid.Boss.InCombat);

        private static int PresentAtHub(Raid raid, Party party) => party.Members.Count(b => IsAtHub(raid, party, b));

        private static bool IsAtHub(Raid raid, Party party, GameBot b) => IsEligible(b) && b.IsAlive && !b.IsReturningAfterRelease && !b.IsOnStableMasterRoute &&
                b.CurrentRegionID == raid.Hub.Region && b.Group != null &&
                b.IsWithinRadius(new Point3D((int)party.HubPost.X, (int)party.HubPost.Y, (int)party.HubPost.Z), 600);

        /// <summary>
        /// The first free formation slot that has both a staging post and a hub post. A slot without one is set aside, so
        /// a raid by tight ground fills the slots that fit instead of waiting on one that never will (Krondon, 2026-10-21:
        /// the raid asked for slot 7 for 90 minutes with 7 parties gathered, and its rally failed).
        /// </summary>
        private static bool TryFreeSlot(Raid raid, out int slot, out Vector3 staging, out Vector3 hubPost)
        {
            staging = hubPost = default;
            for (slot = 0; slot < RealmRaidRecruitmentPolicy.MaximumParties; slot++)
            {
                int candidate = slot;
                if (raid.UnusableSlots.Contains(candidate) || raid.Parties.Values.Any(p => p.FormationSlot == candidate)) continue;
                if (TryStaging(raid, candidate, out staging) && TryHubPost(raid, candidate, out hubPost)) return true;
                raid.UnusableSlots.Add(candidate);
            }
            slot = -1;
            return false;
        }

        private static bool TryHubPost(Raid raid, int slot, out Vector3 post)
        {
            if (raid.HubPosts.TryGetValue(slot, out post)) return true;
            var hub = raid.Hub;
            var region = WorldMgr.GetRegion(hub.Region);
            var zone = region?.GetZone((int)hub.Center.X, (int)hub.Center.Y);
            // Cached once per party, not a scan on each bot's AI turn.
            var nearby = region?.Objects.OfType<GameNPC>().Where(n => n is not GameBot && n.IsAlive &&
                Vector3.DistanceSquared(new(n.X,n.Y,n.Z),hub.Center) < 3000*3000).ToArray() ?? [];
            bool Safe(Vector3 p) => !nearby.Any(n =>
                Vector3.DistanceSquared(new(n.X,n.Y,n.Z),p) <
                (n.Realm == eRealm.None && n.Brain is StandardMobBrain { AggroLevel: > 0 } ? 750*750 : 100*100));
            if (!RealmRaidMuster.TryPost(PathfindingProvider.Instance, zone, hub, slot,
                raid.HubPosts.Values.ToArray(), out post, Safe)) return false;
            raid.HubPosts[slot] = post;
            return true;
        }

        private static int PresentAtStaging(Raid raid) => raid.Parties.Values
            .Sum(p =>
            p.Members.Count(b => IsEligible(b) && b.IsAlive && !b.IsOnStableMasterRoute && b.Group != null &&
                b.CurrentRegionID == (raid.Definition.IsDungeon ? raid.DungeonRoute.Entrance.SourceRegion : raid.Definition.Region) &&
                AtStaging(raid, b)));

        private static GameLiving[] StagedBots(Raid raid) => raid.Parties.Values
            .SelectMany(p => p.Members.Where(b => IsEligible(b) && b.IsAlive && !b.IsOnStableMasterRoute && b.Group != null &&
                b.CurrentRegionID == raid.Definition.Region && AtStaging(raid, b)))
            .Cast<GameLiving>().ToArray();

        private static bool AtStaging(Raid raid, GameBot bot)
        {
            var home = raid.Definition.IsDungeon ? null : DragonLairPlacement.Home(raid.Definition.Realm);
            return DragonRallyRoute.AtAssembly(new(bot.X,bot.Y,bot.Z), raid.StagingPosts.Values,
                home == null ? null : new Vector3(home.X,home.Y,home.Z));
        }

        public static bool TryPendingDragonRally(GameBot bot, out Vector3 home)
        {
            home=default;
            if (bot?.IsAutonomousWorldBot != true || bot.Group == null) return false;
            lock(Sync)
            {
                if (!Membership.TryGetValue(bot.Group,out var raid) || raid.Started || raid.Definition.IsDungeon ||
                    !raid.HubDeparted || bot.CurrentRegionID != raid.Definition.Region) return false;
                var point=DragonLairPlacement.Home(raid.Definition.Realm);
                home=new(point.X,point.Y,point.Z);
                return true;
            }
        }

        public static bool IsPendingDragonTarget(GameBot bot, GameLiving target)
        {
            if(bot?.IsAutonomousWorldBot != true || bot.Group == null || target == null) return false;
            lock(Sync) return Membership.TryGetValue(bot.Group,out var raid) && !raid.Started &&
                !raid.Definition.IsDungeon && ReferenceEquals(raid.Boss,target);
        }

        // Called outside the raid lock by the single coordinator. At most one
        // party per running expedition is rebuilt per five seconds (it was one in
        // total, so three rallies at once took three times as long to form);
        // never 300 native route probes at once.
        public static void RecruitForcedParty(long now)
        {
            var batch = new List<(string Id, GameBot[] Members)>();
            lock (Sync)
            {
                if (now < _nextForcedRecruitment) return;
                _nextForcedRecruitment = now + 5_000;
                foreach (var raid in Raids.Values)
                {
                    if (!raid.Forced)
                        foreach (var stale in raid.ForcedParties.Where(p => p.Any(b =>
                                     !IsEligible(b) || !AutonomousBotRegistry.Contains(b.DatabaseID) || b.Group != null ||
                                     !MayAnswerAutomaticRaid(b))).ToArray())
                        {
                            raid.ForcedParties.Remove(stale);
                            foreach (var b in stale)
                                if (Reservations.GetValueOrDefault(b.DatabaseID) == raid.Definition.Id)
                                    Reservations.TryRemove(b.DatabaseID, out _);
                        }
                    // An automatic raid holds up to three planned parties at once and forms whichever eight (or the final
                    // four) are free first: one fixed roster of solo grinders was rarely out of combat all at once, and
                    // the first scheduled dragon rally (2026-10-19) stalled at 24 of 58 for twenty minutes.
                    int recruitCap = RealmRaidRecruitmentPolicy.RecruitCap(raid.Required);
                    int planned = raid.ForcedParties.Sum(p => p.Length);
                    if (!raid.Forced && raid.Support.Length + planned < recruitCap &&
                        raid.ForcedParties.Count < RealmRaidRecruitmentPolicy.AutomaticPlannedParties)
                    {
                        int size = Math.Min(8, recruitCap - raid.Support.Length - planned);
                        var waiting = AutonomousBotGroupCoordinator.PlanWaitingRaidParty(raid.Definition.Realm, size);
                        if (waiting != null)
                        {
                            raid.ForcedParties.Add(waiting);
                            foreach (var b in waiting) Reservations[b.DatabaseID] = raid.Definition.Id;
                        }
                    }
                    // A bot riding a stable route can join: it lands and travels to the hub
                    // like everyone else. Only an active fight holds a member back.
                    bool Free(GameBot b) => IsEligible(b) && b.IsAlive && AutonomousBotRegistry.Contains(b.DatabaseID) &&
                        !b.InCombat && !b.IsAttacking && (b.Brain as BotBrain)?.HasAggro != true;
                    // Only a whole planned roster: after pooled parties formed, the rosters they drew
                    // from are left short, can never form (4 or 8 only), and used to block the pooled
                    // path below for good (Darkness Falls Albion stuck at 21 parties, 2026-10-06). An
                    // automatic raid's waiting party (8, or the final 4) is always whole.
                    GameBot[] members = raid.ForcedParties.FirstOrDefault(p => (p.Length == 8 || !raid.Forced) && p.All(Free));
                    if (members != null)
                    {
                        raid.ForcedParties.Remove(members);
                        raid.ForcedParties.Add(members); // A bad route cannot starve the other parties.
                    }
                    else
                    {
                        // Planned rosters were fixed sets of 8 from all over the realm, so one busy
                        // member held the other seven back (Oct 5 test: Albion formed 17 of 38
                        // parties in 22 minutes). Build the next party from whichever reserved
                        // bots are free, closest to the hub first, with the same role rules.
                        GameBot[] reserved = raid.ForcedParties.SelectMany(p => p).Distinct().ToArray();
                        members = AutonomousBotGroupCoordinator.ComposeForcedParty(reserved.Where(Free).ToArray(), reserved.Length);
                    }
                    if (members == null) continue;
                    batch.Add((raid.Definition.Id, members));
                }
            }
            // A party that lost a member (released after failing to reach the hub) takes one
            // replacement per raid every 10 seconds, so the raid gets back to its full size
            // instead of staying short (Galladoria, 2026-10-06: 296/300 after four members were
            // trapped in Dun Lamfhota). The newcomer joins as a late member and travels on its own.
            var refills = new List<(string Id, Group Group, RealmRaidMuster.Hub Hub, bool Forced, long[] Released)>();
            lock (Sync)
            {
                foreach (var raid in Raids.Values)
                {
                    if (now < raid.NextRefill) continue;
                    var shortParty = raid.Parties.FirstOrDefault(p => RealmRaidRecruitmentPolicy.NeedsReplacement(p.Value.Target, p.Value.Members.Length));
                    if (shortParty.Key == null) continue;
                    raid.NextRefill = now + 10_000;
                    refills.Add((raid.Definition.Id, shortParty.Key, raid.Hub, raid.Forced, raid.Released.ToArray()));
                }
            }
            foreach (var (id, group, hub, forced, released) in refills)
                AutonomousBotGroupCoordinator.TryRefillRaidParty(id, group, hub.Region, hub.Center, forced, released);

            foreach (var (id, members) in batch)
            {
                if (!AutonomousBotGroupCoordinator.FormForcedRaidParty(id, members)) continue;
                lock (Sync)
                {
                    if (Raids.TryGetValue(id, out var raid) && !raid.ForcedParties.Remove(members))
                    {
                        // A pooled party: take its members out of whichever planned rosters held them.
                        var formed = members.ToHashSet();
                        var remaining = raid.ForcedParties.Select(p => p.Where(b => !formed.Contains(b)).ToArray())
                            .Where(p => p.Length > 0).ToList();
                        raid.ForcedParties.Clear();
                        raid.ForcedParties.AddRange(remaining);
                    }
                    foreach (GameBot bot in members) Reservations.TryRemove(bot.DatabaseID, out _);
                }
            }
        }

        /// <summary>Keeps a party in its expedition when one member is released (unreachable hub route).</summary>
        public static void ReleasePartyMember(Group group, GameBot bot)
        {
            if (group == null || bot == null) return;
            lock (Sync)
            {
                if (!Membership.TryGetValue(group, out Raid raid) || !raid.Parties.TryGetValue(group, out Party party)) return;
                party.Members = party.Members.Where(member => member != bot).ToArray();
                party.CatchingUp.Remove(bot.DatabaseID);
                raid.Released.Add(bot.DatabaseID);
                raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
            }
        }

        /// <summary>
        /// Adds a replacement to a party that lost a member. It skips the muster attendance once
        /// the raid has left the hub and walks to the party's destination like a late member.
        /// </summary>
        public static bool AddReplacement(Group group, GameBot bot)
        {
            if (group == null || bot == null) return false;
            lock (Sync)
            {
                if (!Membership.TryGetValue(group, out Raid raid) || !raid.Parties.TryGetValue(group, out Party party) ||
                    !RealmRaidRecruitmentPolicy.NeedsReplacement(party.Target, party.Members.Length) ||
                    raid.Support.Length >= RealmRaidRecruitmentPolicy.MaximumBots) return false;
                party.Members = party.Members.Append(bot).ToArray();
                if (raid.HubDeparted || raid.Started) party.CatchingUp.Add(bot.DatabaseID);
                raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
                UpdateView(raid, party);
                return true;
            }
        }

        public static bool TryGetForcedStaging(string id, out AutonomousBotGroupCoordinator.SharedCamp camp)
        {
            camp = null;
            lock (Sync)
            {
                if (!Raids.TryGetValue(id, out var raid) || raid.Parties.Count >= RealmRaidRecruitmentPolicy.MaximumParties) return false;
                if (!TryFreeSlot(raid, out int slot, out _, out var staging))
                {
                    if (GameLoop.GameLoopTime >= raid.NextPostWarning)
                    {
                        raid.NextPostWarning = GameLoop.GameLoopTime + 60_000;
                        Log.Warn($"REALM_RAID_NO_MUSTER_POST event={id} hub=\"{raid.Hub.Name}\" parties={raid.Parties.Count} " +
                                 $"staging={raid.StagingPosts.Count} hubPosts={raid.HubPosts.Count} unusableSlots={raid.UnusableSlots.Count}");
                    }
                    return false;
                }
                camp = new($"realm-event-{id}", raid.Definition.Name, raid.Hub.Name,
                    raid.Hub.Region,
                    (int)staging.X, (int)staging.Y, (int)staging.Z, false, false, 50);
                return true;
            }
        }

        public static bool CommitForcedParty(string id, Group group)
        {
            lock (Sync)
            {
                if (!Raids.TryGetValue(id, out var raid) ||
                    raid.Parties.Count >= RealmRaidRecruitmentPolicy.MaximumParties || Membership.ContainsKey(group)) return false;
                var members = group.GetMembersInTheGroup().OfType<GameBot>().ToArray();
                // The Epic Special Forces come on top of the realm's own raiders (the 300-bot limit is the realm's).
                bool specialForces = members.Length > 0 && members.All(b => b.IsEpicSpecialForce);
                if (members.Length is not (4 or 8) ||
                    !specialForces && raid.Support.Length + members.Length > RealmRaidRecruitmentPolicy.MaximumBots ||
                    members.Any(b => !IsEligible(b) || b.Realm != raid.Definition.Realm ||
                    Reservations.GetValueOrDefault(b.DatabaseID) != id)) return false;
                if (!TryFreeSlot(raid, out int slot, out var staging, out var hubPost)) return false;
                var party = new Party { Members = members, FormationSlot = slot, Staging = staging, HubPost = hubPost, Target = members.Length };
                // They arrive at the hub after the raid has left it, and walk in like late members.
                if (specialForces && (raid.HubDeparted || raid.Started))
                {
                    party.Departed = true;
                    foreach (GameBot b in members) party.CatchingUp.Add(b.DatabaseID);
                }
                raid.Parties[group] = party;
                Membership[group] = raid;
                DefenseGroups[group] = 0;
                raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
                UpdateView(raid, party);
                return true;
            }
        }

        /// <summary>Where the Epic Special Forces arrive for a running raid: its muster hub.</summary>
        public static bool TryGetSpecialForcesPost(string id, out ushort region, out Vector3 point)
        {
            region = 0; point = default;
            lock (Sync)
            {
                if (!Raids.TryGetValue(id, out var raid) || raid.Hub == null) return false;
                region = raid.Hub.Region;
                point = raid.Hub.Center;
                return true;
            }
        }

        public static bool IsRunning(string id)
        {
            lock (Sync) return Raids.ContainsKey(id);
        }

        /// <summary>A Special Forces member is reserved for its raid like a recruit, so it can be formed into a party.</summary>
        public static void ReserveSpecialForce(GameBot bot, string id)
        {
            if (bot?.IsEpicSpecialForce == true) Reservations[bot.DatabaseID] = id;
        }

        private static bool Available(string id) => !Raids.ContainsKey(id) &&
            Cooldowns.GetValueOrDefault(id) <= GameLoop.GameLoopTime && Bosses.TryGetValue(id, out var boss) &&
            boss.IsAlive && !boss.IsRespawning && boss.ObjectState == GameObject.eObjectState.Active &&
            (id != "epic-albion" || !ApocInitializator.PickedTarget);

        private static bool TryStaging(Raid raid, int slot, out Vector3 staging)
        {
            if (raid.StagingPosts.TryGetValue(slot, out staging)) return true;
            staging = default;
            var nav = PathfindingProvider.Instance;
            if (raid.DungeonRoute != null)
            {
                var edge = raid.DungeonRoute.Entrance;
                Zone exterior = WorldMgr.GetRegion(edge.SourceRegion)?.GetZone(edge.SourceX, edge.SourceY);
                Vector3 portal = new(edge.SourceX, edge.SourceY, edge.SourceZ);
                if (RealmRaidStaging.TryDungeonPost(nav,exterior,portal,slot,raid.StagingPosts.Values.ToArray(),out staging))
                {
                    raid.StagingPosts[slot] = staging;
                    return true;
                }
                return false;
            }
            Point3D home = DragonLairPlacement.Home(raid.Definition.Realm);
            Region region = WorldMgr.GetRegion(raid.Definition.Region);
            if (RealmRaidStaging.TryDragonPost(nav, region?.GetZone(home.X, home.Y), new(home.X, home.Y, home.Z), slot,
                raid.StagingPosts.Values.ToArray(), out staging))
            {
                raid.StagingPosts[slot] = staging;
                return true;
            }
            return false;
        }

        private static void UpdateView(Raid raid, Party party)
        {
            if (raid.HubDeparted && !party.Departed)
            {
                foreach (var b in party.Members) party.CatchingUp.Add(b.DatabaseID);
                party.Departed = true;
            }
            if (!party.Departed)
            {
                Vector3 p = party.HubPost;
                party.View = new(raid.Definition.Id, $"Assembling at {raid.Hub.Name}",
                    new($"realm-event-{raid.Definition.Id}", raid.Definition.Name, raid.Hub.Name, raid.Hub.Region,
                        (int)p.X, (int)p.Y, (int)p.Z, false, false, 50), true, true);
                return;
            }
            UpdateDestinationView(raid, party);
            if (raid.Started || party.Members.All(b => party.CatchingUp.Contains(b.DatabaseID)))
                party.OutboundLeg = raid.OutboundSeams.Length;
            // Follow the retained, validated zone sequence instead of repeatedly
            // choosing a new shortest zone route that can bounce at blocked seams.
            while (party.OutboundLeg < raid.OutboundSeams.Length)
            {
                Vector3 p = raid.OutboundSeams[party.OutboundLeg];
                Zone targetZone = WorldMgr.GetRegion(raid.Hub.Region)?.GetZone((int)p.X,(int)p.Y);
                if (party.Members.Any(b => b.IsAlive && !party.CatchingUp.Contains(b.DatabaseID) &&
                    b.CurrentRegionID == raid.Hub.Region && b.CurrentZone == targetZone &&
                    b.IsWithinRadius(new Point3D((int)p.X,(int)p.Y,(int)p.Z),175)))
                { party.OutboundLeg++; continue; }
                party.View = new(raid.Definition.Id, $"Traveling from {raid.Hub.Name} — leg {party.OutboundLeg+1}/{raid.OutboundSeams.Length}",
                    new($"realm-event-{raid.Definition.Id}",raid.Definition.Name,raid.Definition.Name,raid.Hub.Region,
                        (int)p.X,(int)p.Y,(int)p.Z,false,false,50),true,false,true);
                return;
            }
            party.View = party.DestinationView;
        }

        private static void UpdateDestinationView(Raid raid, Party party)
        {
            if (raid.DungeonRoute is { } route)
            {
                Vector3 destination = raid.Started ? route.Destination : party.Staging;
                ushort region = raid.Started ? raid.Definition.Region : route.Entrance.SourceRegion;
                string target = raid.Started ? route.TargetName : raid.Definition.Name;
                bool queued = false;
                Vector3 front = destination;
                if (raid.Started && !TryBattlePost(raid, party, destination, region, out destination))
                {
                    queued = true;
                    region = party.Anchor.HasValue ? party.AnchorRegion : route.Entrance.SourceRegion;
                    destination = party.Anchor ?? party.Staging;
                    // Far from the front, a party without a free post there walks to it anyway instead of waiting at its old
                    // post: at Caer Sidi's final staging no post was free, every party waited where it stood, nobody came
                    // near, and the front was moved back down after three minutes, twice (runs 22, 23).
                    if (region == raid.Definition.Region && Vector3.DistanceSquared(destination, front) > QueuedApproachDistance * QueuedApproachDistance)
                        destination = front;
                }
                party.DestinationView = new(raid.Definition.Id, queued ? "RAID — waiting for a clear formation post" : raid.Started ? "RAID — " + route.Status : "Raid rally",
                    new($"realm-event-{raid.Definition.Id}", target, raid.Definition.Name, region,
                        (int)destination.X, (int)destination.Y, (int)destination.Z, region == raid.Definition.Region, false,
                        raid.Started ? route.TargetLevel : 50), !raid.Started || route.Hold || queued);
                return;
            }
            bool landed = DragonLanded(raid);
            bool hold = !raid.Started || !landed;
            // A dragon drawn out of its lair is immune there: the raid forms at the lair and draws it home
            // (Gjalpinulva, 2026-10-20: the raid's posts followed her out and she stayed at 100% for an hour).
            Vector3 point = hold ? party.Staging :
                DragonLairPlacement.IsOutOfLair(raid.Boss) && DragonLairPlacement.Home(DragonLairPlacement.DragonRealm(raid.Boss)) is { } lair
                    ? new(lair.X, lair.Y, lair.Z) : new(raid.Boss.X, raid.Boss.Y, raid.Boss.Z);
            if (!hold && !TryBattlePost(raid, party, point, raid.Definition.Region, out point))
            { point = party.Anchor ?? party.Staging; hold = true; }
            string state = !raid.Started ? !landed
                ? "Raid rally — dragon airborne" : "Raid rally"
                : !landed ? "Waiting for dragon landing" : hold ? "RAID — waiting for a clear formation post" : "RAID — fighting";
            party.DestinationView = new(raid.Definition.Id, state,
                new($"realm-event-{raid.Definition.Id}", raid.Boss.Name, raid.Boss.CurrentZone?.Description ?? raid.Definition.Name,
                    raid.Definition.Region, (int)point.X, (int)point.Y, (int)point.Z, false, false, raid.Boss.Level), hold);
        }

        public const long FailedPostRetryMilliseconds = 30_000;
        public const float QueuedApproachDistance = 2_500f;

        private static bool TryBattlePost(Raid raid, Party party, Vector3 center, ushort region, out Vector3 point)
        {
            if (party.Anchor.HasValue && party.AnchorRegion == region &&
                Vector3.DistanceSquared(center, party.AnchorCenter) < 128 * 128)
            { point = party.Anchor.Value; return true; }
            // Run 4 (2026-10-14): around Prince Abdin no post was free, and every party re-ran the full post search
            // (about 300 path queries, 1-3 s) on every AI turn; 414 such turns averaged 1,026 ms.
            long now = GameLoop.GameLoopTime;
            if (now < party.FailedPostUntil && Vector3.DistanceSquared(center, party.FailedPostCenter) < 128 * 128)
            { point = default; return false; }
            Zone zone = WorldMgr.GetRegion(region)?.GetZone((int)center.X, (int)center.Y);
            Vector3[] occupied = raid.Parties.Values.Where(p => p != party && p.Anchor.HasValue && p.AnchorRegion == region)
                .Select(p => p.Anchor.Value).ToArray();
            if (!RealmRaidFormation.TryResolve(PathfindingProvider.Instance, zone, center, party.FormationSlot, occupied, out point))
            {
                party.FailedPostCenter = center;
                party.FailedPostUntil = now + FailedPostRetryMilliseconds;
                return false;
            }
            party.Anchor = point; party.AnchorRegion = region; party.AnchorCenter = center;
            return true;
        }

        public static View GetView(Group group)
        {
            if (group == null) return null;
            lock (Sync) return Membership.TryGetValue(group, out var raid) && raid.Parties.TryGetValue(group, out var party) ? party.View : null;
        }

        // A late or released member follows the current encounter, never an
        // obsolete town muster or a dead leader's old position.
        public static View GetTravelView(GameBot bot)
        {
            Group group = bot?.Group;
            if (group == null) return null;
            lock (Sync)
            {
                if (!Membership.TryGetValue(group, out var raid) || !raid.Parties.TryGetValue(group, out var party)) return null;
                return raid.HubDeparted && party.CatchingUp.Contains(bot.DatabaseID)
                    ? party.DestinationView ?? party.View : party.View;
            }
        }

        // Late/released members skip muster attendance, not the connected exterior route.
        // Navigation is performed by their controller outside the expedition lock.
        public static bool TryIndependentApproach(GameBot bot, out Vector3 destination, out Vector3? via)
        {
            destination = default; via = null;
            Group group = bot?.Group;
            if (group == null) return false;
            lock (Sync)
            {
                if (!Membership.TryGetValue(group, out var raid) || !raid.Parties.TryGetValue(group, out var party) ||
                    !raid.HubDeparted || bot.CurrentRegionID != raid.Hub.Region ||
                    !(raid.Started || party.CatchingUp.Contains(bot.DatabaseID))) return false;
                destination = raid.DungeonRoute != null ? party.Staging :
                    new Vector3(party.DestinationView.Camp.X, party.DestinationView.Camp.Y, party.DestinationView.Camp.Z);
                via = raid.Hub.Via;
                return true;
            }
        }

        /// <summary>
        /// Where a raider who died inside a raid dungeon releases: the raid's muster hub (the town players bind at for it).
        /// Run 14 (2026-10-16): epic dungeons have no bind point, so Caer Sidi's fallen released to Camelot and walked back
        /// across the realm; 67 of the 300 were left inside when Bane of Hope reset at 37%.
        /// </summary>
        public static bool TryRaidRelease(GameBot bot, out ushort region, out Point3D point)
        {
            region = 0; point = null;
            Group group = bot?.Group;
            if (group == null) return false;
            lock (Sync)
            {
                if (!Membership.TryGetValue(group, out var raid) || raid.DungeonRoute == null || raid.Hub == null) return false;
                region = raid.Hub.Region;
                point = new Point3D((int)raid.Hub.Center.X, (int)raid.Hub.Center.Y, (int)raid.Hub.Center.Z);
                return true;
            }
        }

        public static void RejoinAfterRelease(GameBot bot)
        {
            Group group = bot?.Group;
            if (group == null) return;
            lock (Sync)
                if (Membership.TryGetValue(group, out var raid) && raid.Parties.TryGetValue(group, out var party))
                {
                    party.CatchingUp.Add(bot.DatabaseID);
                    party.CorpseSince.Remove(bot.DatabaseID);
                }
        }

        public static void ClearCorpseWait(GameBot bot)
        {
            Group group = bot?.Group;
            if (group == null) return;
            lock (Sync)
                if (Membership.TryGetValue(group, out var raid) && raid.Parties.TryGetValue(group, out var party))
                    party.CorpseSince.Remove(bot.DatabaseID);
        }

        public static AutonomousBotGroupCoordinator.PveCorpseDisposition CorpseRecovery(GameBot bot)
        {
            Group group = bot?.Group;
            lock (Sync)
            {
                if (group == null || !Membership.TryGetValue(group, out var raid) ||
                    !raid.Parties.TryGetValue(group, out var party))
                    return AutonomousBotGroupCoordinator.PveCorpseDisposition.NotManaged;
                long now = GameLoop.GameLoopTime;
                if (!party.CorpseSince.TryGetValue(bot.DatabaseID, out long since))
                    party.CorpseSince[bot.DatabaseID] = since = now;
                // Bounded per-corpse wait, even during an endless nearby fight.
                // Shared resurrection claims still select one safe caster.
                long deadline = since + 90_000;
                bool casting = raid.Support.OfType<GameBot>().Any(b => b.IsAlive && b.IsCasting &&
                    b.castingComponent.SpellHandler is { } cast && cast.Target == bot &&
                    cast.Spell.SpellType == eSpellType.Resurrect &&
                    BotGroupSupport.CanFinishResurrectionBeforeRelease(cast.CastStartTick, deadline, now, cast.Spell.CastTime));
                if (now < deadline || casting)
                    return AutonomousBotGroupCoordinator.PveCorpseDisposition.HoldForResurrection;
                party.CorpseSince.Remove(bot.DatabaseID);
                return AutonomousBotGroupCoordinator.PveCorpseDisposition.ReleaseAndRejoin;
            }
        }

        public static object SupportScope(GameBot bot)
        {
            Group group = bot?.Group;
            lock (Sync)
                if (group != null && Membership.TryGetValue(group, out var raid)) return raid;
            return RealmWarbandSupport.Get(bot)?.Scope ?? bot?.Group;
        }

        public static bool HasSharedSupport(GameBot bot) => GetView(bot?.Group) != null || RealmWarbandSupport.Get(bot) != null;

        public static IGameStaticItemOwner LootOwner(GameNPC victim, IEnumerable<GameBot> contributors) =>
            LootOwner(victim, contributors.Where(b => b != null).Distinct().ToDictionary(b => b, _ => 1.0));

        /// <summary>
        /// The expedition that did the most damage gets the drop. In a contested neutral dungeon two realms' raids can both
        /// be on the final encounter; only the one credited here wins it (2026-10-20: Hibernia and Midgard were both in
        /// Summoner's Hall, every Govannon drop went to Hibernians and both raids ended "Boss defeated").
        /// </summary>
        public static IGameStaticItemOwner LootOwner(GameNPC victim, IReadOnlyDictionary<GameBot, double> damageByBot)
        {
            lock (Sync)
            {
                GameBot[] damaging = damageByBot.Keys.ToArray();
                Raid raid = damageByBot.Where(p => p.Key?.Group != null)
                    .Select(p => (Raid: Membership.GetValueOrDefault(p.Key.Group), Damage: p.Value))
                    .Where(p => p.Raid != null && p.Raid.Started && p.Raid.Definition.Region == victim.CurrentRegionID)
                    .GroupBy(p => p.Raid).OrderByDescending(g => g.Sum(p => p.Damage)).Select(g => g.Key).FirstOrDefault();
                if (raid != null && raid.Definition.FinalTypes.Contains(victim.GetType().Name))
                    CreditFinalKill(raid, victim);
                if (raid == null)
                {
                    // Completion can race the native death/loot callback. Keep
                    // only the final dead bosses' former roster briefly, not an
                    // ongoing entitlement to unrelated kills after disbanding.
                    if (!CompletedLoot.TryGetValue(victim, out var completed) || completed.Expires <= GameLoop.GameLoopTime ||
                        !damaging.Any(b => completed.Members.Contains(b))) return null;
                    return new RealmRaidLootOwner(completed.Members, completed.Ledger);
                }
                GameBot[] nearby = raid.Support.OfType<GameBot>().Where(b => b.CurrentRegionID == victim.CurrentRegionID &&
                    b.IsWithinRadius(victim, WorldMgr.VISIBILITY_DISTANCE)).Distinct().ToArray();
                return nearby.Length == 0 ? null : new RealmRaidLootOwner(nearby, raid.LootLedger);
            }
        }

        private static void CreditFinalKill(Raid winner, GameNPC victim)
        {
            winner.FinalKillCredited = true;
            foreach (Raid rival in Raids.Values.Where(r => r != winner && r.Definition.Region == victim.CurrentRegionID &&
                                                           r.Definition.FinalTypes.Contains(victim.GetType().Name)))
                rival.BeatenBy = winner.Definition.Realm;
            Log.Info($"REALM_RAID_FINAL_CREDIT event={winner.Definition.Id} boss=\"{victim.Name}\" region={victim.CurrentRegionID}");
        }

        /// <summary>"Boss defeated", unless another realm's raid was credited with this raid's final encounter.</summary>
        public static string CompletionOutcome(bool credited, eRealm beatenBy) =>
            !credited && beatenBy != eRealm.None ? "Beaten to it" : "Boss defeated";

        private const string BeatenPrefix = "Beaten to it";

        private static string CompletionReason(Raid raid, string won) =>
            CompletionOutcome(raid.FinalKillCredited, raid.BeatenBy) == BeatenPrefix
                ? $"{BeatenPrefix}: {GlobalConstants.RealmToName(raid.BeatenBy)}'s expedition killed the final encounter first."
                : won;

        public static GameLiving[] SupportMembers(GameBot bot)
        {
            Group group = bot?.Group;
            lock (Sync)
                if (group != null && Membership.TryGetValue(group, out var raid)) return raid.Support;
            // Never acquire a group lock while holding the expedition lock;
            // group disbanding calls RemoveParty in the opposite direction.
            return RealmWarbandSupport.Get(bot)?.Members ?? bot?.Group?.GetMembersInTheGroup().ToArray() ?? (bot == null ? [] : [bot]);
        }

        // At most four bounded roster scans per expedition per second, even
        // when hundreds of combatants are hit. Normal eight-person defense is
        // immediate and unchanged; this only wakes nearby sister parties.
        public static GameLiving[] ClaimNearbyDefenseBroadcast(GameBot victim, long now)
        {
            if (victim?.Group == null || !DefenseGroups.ContainsKey(victim.Group)) return [];
            lock (Sync)
            {
                if (victim?.Group == null || !Membership.TryGetValue(victim.Group, out var raid) ||
                    now < raid.NextDefenseBroadcast) return [];
                raid.NextDefenseBroadcast = now + 250;
                return raid.Support;
            }
        }

        public static bool SameExpedition(GameBot first, GameBot second)
        {
            if (first?.IsAutonomousWorldBot != true || second?.IsAutonomousWorldBot != true) return false;
            lock (Sync) return first.Group != null && second.Group != null &&
                Membership.TryGetValue(first.Group, out var raid) && Membership.GetValueOrDefault(second.Group) == raid;
        }

        public static bool Protects(GameBot bot)
        {
            if (bot?.IsAlive != true || !bot.IsAutonomousWorldBot) return false;
            lock(Sync)
                if(bot.Group != null && Membership.TryGetValue(bot.Group,out var raid) &&
                    raid.HubDeparted && !raid.Started && !bot.IsOnStableMasterRoute &&
                    bot.CurrentRegionID == (raid.Definition.IsDungeon ? raid.DungeonRoute.Entrance.SourceRegion : raid.Definition.Region) &&
                    AtStaging(raid,bot)) return true;
            // A started dungeon expedition fights wherever its route leads (a boss
            // room can be far from the next route point). Real combat inside the
            // expedition's own dungeon is never a 15-minute "no movement" stall.
            lock (Sync)
                if (bot.InCombat && bot.Group != null && Membership.TryGetValue(bot.Group, out var raid) &&
                    raid.Started && raid.DungeonRoute != null && bot.CurrentRegionID == raid.Definition.Region)
                    return true;
            View view = GetTravelView(bot);
            return view != null && bot.CurrentRegionID == view.Camp.RegionId &&
                (view.Hold && bot.IsWithinRadius(new Point3D(view.Camp.X, view.Camp.Y, view.Camp.Z), 600) ||
                 bot.InCombat && bot.IsWithinRadius(new Point3D(view.Camp.X, view.Camp.Y, view.Camp.Z), 4000));
        }

        /// <summary>
        /// The expedition's current front inside its dungeon: a navmesh floor point the
        /// route planner has already proven connects to the next encounter. A member
        /// stranded on a disconnected floor island regroups here instead of retrying
        /// an impossible corridor for hours or being sent back to its capital.
        /// </summary>
        public static bool TryDungeonRegroupPoint(GameBot bot, out ushort region, out Vector3 point)
        {
            region = 0; point = default;
            if (bot?.Group == null) return false;
            lock (Sync)
            {
                if (!Membership.TryGetValue(bot.Group, out var raid) || raid.DungeonRoute == null || !raid.Started ||
                    bot.CurrentRegionID != raid.Definition.Region)
                    return false;
                region = raid.Definition.Region;
                point = raid.DungeonRoute.Front;
                return true;
            }
        }

        public static IEnumerable<GameLiving> SupportPets(GameBot bot, int range)
        {
            if (!HasSharedSupport(bot)) yield break;
            foreach (GameNPC pet in AllSupportPets(SupportMembers(bot)))
                if (pet.IsAlive && pet.Realm == bot.Realm && pet.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(pet, range))
                    yield return pet;
        }

        // Every healer of a 300-bot raid walked every member's pet tree on each heal check (95 MB of
        // allocations in 40 s, run 8 trace). The pet list of one roster is now built at most once a
        // second and shared; each healer only filters it by region and range.
        private sealed class PetCache { public long Until; public GameNPC[] Pets = []; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameLiving[], PetCache> SupportPetCache = new();

        private static GameNPC[] AllSupportPets(GameLiving[] members)
        {
            if (members == null || members.Length == 0) return [];
            PetCache cache = SupportPetCache.GetOrCreateValue(members);
            long now = GameLoop.GameLoopTime;
            lock (cache)
            {
                if (now < cache.Until) return cache.Pets;
                var visited = new HashSet<IControlledBrain>();
                var pets = new List<GameNPC>();
                foreach (GameLiving owner in members)
                    if (owner?.ControlledBrain != null)
                        pets.AddRange(BotGroupPetBuffTargets.AttachedTree(owner.ControlledBrain, owner, visited));
                cache.Pets = pets.ToArray();
                cache.Until = now + 1_000;
                return cache.Pets;
            }
        }

        public static void RemoveParty(Group group)
        {
            if (group == null) return;
            lock (Sync)
            {
                Released.Remove(group);
                if (Membership.Remove(group, out Raid raid))
                {
                    DefenseGroups.TryRemove(group, out _);
                    raid.Parties.Remove(group);
                    raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
                }
            }
        }

        public static bool TryConsumeRelease(Group group, out string reason)
        {
            lock (Sync) return Released.Remove(group, out reason);
        }

        private static void End(Raid raid, long now, string reason)
        {
            bool stoppedByPlayer = reason.StartsWith(StoppedByPlayer);
            string outcome = stoppedByPlayer ? "Stopped by player" :
                reason.StartsWith("Route blocked") ? "Route blocked" :
                reason.StartsWith("Staging failed") ? "Failed rally" : reason.Contains("four-hour") ? "Timed out" :
                reason.StartsWith("Defeated at") ? "Defeated" : reason.StartsWith(BeatenPrefix) ? BeatenPrefix :
                raid.Definition.IsDungeon ? raid.DungeonRoute?.Complete == true ? "Boss defeated" : "Ended (unconfirmed)" :
                !raid.Boss.IsAlive ? "Boss defeated" : "Encounter unavailable";
            // Partial success: the named encounters the raid killed before it was stopped are part of its record.
            string[] named = raid.DungeonRoute?.NamedDefeated.ToArray() ?? [];
            if (outcome != "Boss defeated" && named.Length > 0)
                reason += $" Named encounters defeated: {string.Join(", ", named)}.";
            RealmEventRecords.Finish(raid.Definition.Id, outcome, reason, raid.Support.Length);
            if (outcome == "Boss defeated") LastSuccessful[raid.Definition.Realm] = raid.Definition.Id;
            AutonomousEpicSpecialForces.RaidEnded(raid.Definition.Id, outcome);
            Log.Info($"REALM_RAID_ENDED event={raid.Definition.Id} outcome=\"{outcome}\" support={raid.Support.Length} required={raid.Required} " +
                     $"forced={raid.Forced} minutes={(now - raid.Created) / 60_000} named={named.Length} " +
                     $"specialForces={AutonomousEpicSpecialForces.Count(raid.Definition.Id)} reason=\"{reason}\"");
            if (outcome == "Defeated")
                GameWideAnnouncements.Queue(AnnouncementKind.PveRealmEvent, GameWideAnnouncements.RaidDefeated(raid.Definition.Realm,
                    raid.DungeonRoute?.DefeatedAt ?? raid.Definition.BossType, raid.Definition.Name));
            // Owner 2026-10-07: every realm hears when a raid (automatic or forced) brings down the final boss of an
            // epic dungeon, Summoner's Hall or Darkness Falls.
            if (outcome == "Boss defeated" && raid.Definition.IsDungeon)
            {
                GameNPC finalBoss = (raid.DungeonRoute?.FinalBosses ?? [raid.Boss]).FirstOrDefault(n => n != null && !n.IsAlive);
                GameWideAnnouncements.Queue(AnnouncementKind.PveRealmEvent, GameWideAnnouncements.FinalBossDefeated(raid.Definition.Realm,
                    finalBoss?.Name ?? raid.Definition.BossType, raid.Definition.Name));
            }
            GameBot[] recipients = raid.Support.OfType<GameBot>().ToArray();
            // A raid beaten to its final encounter has no share in its loot.
            foreach (GameNPC final in (raid.DungeonRoute?.FinalBosses ?? [raid.Boss]).Where(n => n != null && !n.IsAlive && outcome != BeatenPrefix))
            {
                if (CompletedLoot.Count >= 32) CompletedLoot.Remove(CompletedLoot.MinBy(p => p.Value.Expires).Key);
                CompletedLoot[final] = new(recipients, raid.LootLedger, now + 60_000);
            }
            foreach (Group group in raid.Parties.Keys) { Membership.Remove(group); DefenseGroups.TryRemove(group, out _); Released[group] = reason; }
            raid.Parties.Clear();
            raid.Support = [];
            Raids.Remove(raid.Definition.Id);
            foreach (var reservation in Reservations.Where(p => p.Value == raid.Definition.Id).ToArray()) Reservations.TryRemove(reservation.Key, out _);
            // A player stop is not a failed attempt: no cooldown, so it can be restarted at once.
            if (!stoppedByPlayer)
                Cooldowns[raid.Definition.Id] = now + (30 + Random.Shared.Next(61)) * 60_000L;
            // The talking update: a win or a defeat is news the realm's bots tell and talk about.
            string bossName = (raid.DungeonRoute?.FinalBosses ?? [raid.Boss]).FirstOrDefault(n => n != null)?.Name ?? raid.Boss?.Name;
            string newsTopic = outcome == "Boss defeated" ? "raid_won" : outcome == "Defeated" ? "raid_wiped" : null;
            if (newsTopic != null)
                AutonomousChatNews.RaidOutcome(raid.Definition.Realm, raid.Definition.Name, bossName, newsTopic == "raid_won");
            RealmEventNotices.Queue(raid.Definition.Id, raid.Definition.Realm, RealmEventBanter.RaidOutcome(raid.Definition.Name, outcome),
                newsTopic, RaidSlots(raid.Definition.Name, bossName));
            string leeroyName = raid.LeeroyName ?? raid.DungeonRoute?.LeeroyName;
            if (leeroyName != null && newsTopic != null)
                // The raiders as they were (raid.Support is emptied above when the parties are released: in-game check
                // 2026-10-29, Golestandt fell after a Leeroy and nobody said a word about him).
                AutonomousRaidLeeroy.AfterRaid(raid.Definition.Realm, leeroyName, bossName, newsTopic == "raid_won", recipients);
        }

        /// <summary>The names a raid's news lines use ({raid}, {boss}).</summary>
        public static Dictionary<string, string> RaidSlots(string raid, string boss)
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["raid"] = raid };
            if (!string.IsNullOrWhiteSpace(boss))
                slots["boss"] = boss;
            return slots;
        }

        private const string StoppedByPlayer = "Stopped by the player";

        /// <summary>A named encounter wiped the raid: the realm hears it, and the raid regroups for one more attempt.</summary>
        private static void OnWipe(Raid raid, string boss, int failures, long now)
        {
            Log.Info($"REALM_RAID_WIPE event={raid.Definition.Id} boss=\"{boss}\" failures={failures} support={raid.Support.Length} " +
                     $"living={raid.Support.Count(b => b.IsAlive)} minutes={(now - raid.Created) / 60_000}");
            if (failures >= RealmRaidDungeonRoute.MaximumFailedAttempts) return;
            AutonomousChatNews.RaidOutcome(raid.Definition.Realm, raid.Definition.Name, boss, false);
            RealmEventNotices.Queue(raid.Definition.Id, raid.Definition.Realm, RealmEventBanter.RaidWipe(raid.Definition.Name, boss),
                "raid_wiped", RaidSlots(raid.Definition.Name, boss));
            AutonomousEpicSpecialForces.Request(raid.Definition.Id, raid.Definition.Name, raid.Definition.Realm, $"a wipe at {boss}");
        }

        // Runs 12 and 13 (2026-10-15/16): an automatic Darkness Falls or Summoner's Hall rally opened a minute before the
        // forced raid and held 140+ of the realm's level-50 bots, so forced Galladoria reserved only 288 of 300 and forced
        // Caer Sidi rallied 143 of the 200 it needs. A forced expedition ends the realm's automatic rallies that have not
        // started yet (no cooldown), the way a forced siege supersedes an unstarted automatic siege.
        private static void SupersedeAutomatic(eRealm realm, string forcedId)
        {
            lock (Sync)
                foreach (Raid automatic in Raids.Values.Where(r => r.Definition.Realm == realm && !r.Forced && !r.Started &&
                             r.Definition.Id != forcedId).ToArray())
                    End(automatic, GameLoop.GameLoopTime, $"{StoppedByPlayer}: superseded by the forced {forcedId} expedition.");
        }

        /// <summary>Launcher "Stop event": ends an active dragon or epic dungeon expedition now.</summary>
        public static bool Stop(string id, out string reason)
        {
            lock (Sync)
            {
                if (!Raids.TryGetValue(id, out Raid raid))
                { reason = "This expedition is not running."; return false; }
                End(raid, GameLoop.GameLoopTime, StoppedByPlayer + " from the launcher.");
                reason = $"{raid.Definition.Name} stopped. The bots return to their own goals; no cooldown was set.";
                return true;
            }
        }

        public static bool ResetCooldown(string id)
        {
            lock (Sync) { if (Raids.ContainsKey(id)) return false; Cooldowns.Remove(id); return true; }
        }

        public static Summary[] Snapshot()
        {
            lock (Sync) return Definitions.Select(d =>
            {
                if (Raids.TryGetValue(d.Id, out var r)) return new Summary(d.Id, d.Name, GlobalConstants.RealmToName(d.Realm),
                    r.Started ? "RAID — " + (r.DungeonRoute?.Status ?? (DragonLanded(r) ? "underway" : "waiting for dragon landing")) :
                    $"Raid rally — {(r.Forced ? "forced" : "automatic")} — {(r.HubDeparted ? "travel/final staging" : r.Hub.Name)}" +
                        (r.Forced ? " — starts when 200 are staged" + (!DragonLanded(r) ? " and the dragon lands" : "") :
                        GameLoop.GameLoopTime >= r.Deadline ? !DragonLanded(r) ? " — waiting for dragon landing" : " — waiting for arrivals" : ""),
                    r.Support.Length + r.ForcedParties.Sum(p => p.Count(IsEligible)),
                    !r.HubDeparted ? r.Parties.Values.Sum(p => PresentAtHub(r,p)) : !r.Started ? PresentAtStaging(r) :
                    r.Parties.Values.Sum(p => p.Members.Count(b => b.IsAlive && b.CurrentRegionID == p.View.Camp.RegionId &&
                        b.IsWithinRadius(new Point3D(p.View.Camp.X, p.View.Camp.Y, p.View.Camp.Z), 1500))),
                    r.Required,
                    // Forced expeditions have no clock: -1 tells the launcher to show "No time limit".
                    r.Forced ? -1 : Math.Max(0, r.Deadline - GameLoop.GameLoopTime), r.Started ? "Battle" :
                        r.Forced ? !r.HubDeparted ? "Muster" : "Staging" :
                        GameLoop.GameLoopTime >= r.Deadline ? "Waiting" : !r.HubDeparted ? "Muster" : "Staging");
                GameNPC boss = Bosses.GetValueOrDefault(d.Id);
                long respawn = boss is ApocInitializator initializer ? initializer.EncounterRespawnRemainingMilliseconds : boss?.RespawnRemainingMilliseconds ?? 0;
                long cooldown = Math.Max(0, Cooldowns.GetValueOrDefault(d.Id) - GameLoop.GameLoopTime);
                string state = Available(d.Id) ? "Available" : respawn > 0 ? "Respawning" : cooldown > 0 ? "Event cooldown" : "Encounter unavailable";
                return new Summary(d.Id, d.Name, GlobalConstants.RealmToName(d.Realm), state, 0, 0,
                    d.SuggestedBots, Math.Max(respawn, cooldown));
            }).ToArray();
        }
    }
}
