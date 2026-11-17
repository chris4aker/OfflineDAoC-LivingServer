using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>A raid owns commitments, never merges the underlying eight-person parties.</summary>
    public static partial class AutonomousRealmRaid
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
            /// <summary>An open-world big boss (RealmRaidOpenWorld): fought like a dragon, at its spawn instead of a lair.</summary>
            public bool IsOpenWorld => RealmRaidOpenWorld.Is(Id);
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
            // Sizes: the period's comfortable raid (docs/RAID-SIZES.md, 2026-11-04); win-rate sizing moves them 70-150%.
            new("dungeon-albion-krondon", "Krondon", eRealm.Albion, 61, "Orylle", 64, new(31413, 70773, 15765), ["Orylle"]),
            new("dungeon-albion-avalon", "Avalon City", eRealm.Albion, 50, "DuraekEmpowered", 64, new(32425, 28962, 8756), ["DuraekEmpowered"]),
            new("dungeon-albion-crystal", "Crystal Cave", eRealm.Albion, 62, "Xanxicar", 120, new(31577, 35305, 14551), ["Xanxicar"], Through: 50),
            new("dungeon-albion-corrupt", "Hall of the Corrupt", eRealm.Albion, 277, "AidonTheArchwizard", 48, new(31353, 37634, 14873),
                ["AidonTheArchwizard", "LordGildas"]),
            new("dungeon-midgard-trollheim", "Trollheim", eRealm.Midgard, 150, "Nosdoden", 64, new(37538, 12262, 13377), ["Nosdoden"]),
            new("dungeon-midgard-iarnvidiur", "Iarnvidiur's Lair", eRealm.Midgard, 161, "Iarnvidiur", 64, new(13848, 11523, 18263), ["Iarnvidiur"]),
            new("dungeon-midgard-dodens", "Dodens Gruva", eRealm.Midgard, 246, "SpiritmasterAros", 40, new(39061, 36970, 15495), ["SpiritmasterAros"]),
            new("dungeon-hibernia-tursuil", "Tur Suil", eRealm.Hibernia, 190, "Balor", 40, new(32032, 31048, 15984), ["Balor"]),
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
            /// <summary>Raiders walking back to the dungeon gather at the way in and go in together (RealmRaidReturnWave).</summary>
            public bool ReturnGathering;
            public long ReturnOpenUntil, ReturnWaitingSince;
            /// <summary>The return wave walking in together now (RealmRaidReturnWave.WalkTogether), or null; how many have
            /// formed; its last headway (the stall clock's, RealmRaidReturnWave.Stalled); and the returners waiting outside.</summary>
            public ReturnWalk Walk;
            public int WalksFormed;
            public long LastHeadwayTick;
            public int ReturnWaitingNow;
            /// <summary>Raiders seen inside the dungeon since the raid began. Only they are "on their way back"; a raider who
            /// has never been in is still on the way in and is never held at the way in.</summary>
            public readonly HashSet<long> BeenInside = new();
            /// <summary>The raid-wide post a frontier raid's released raiders gather at (RealmRaidReturnWave.RaidPost), once
            /// chosen from the staging posts.</summary>
            public Vector3? RaidPost;
            /// <summary>Raiders released at the hub because enemies held the raid-wide post: they wait there and go back
            /// together (RealmRaidReturnWave.HubGroup).</summary>
            public readonly HashSet<long> HubReturners = new();
            public long HubWaitingSince;
            /// <summary>A dragon raid's raiders walking back after a release (or from beyond the staging ring), who gather at
            /// their staging post and go back in as a wave (RealmRaidReturnWave.ClassifyDragonRaider).</summary>
            public readonly HashSet<long> ReturningToDragon = new();
            public bool PreparationNoticeSent;
            /// <summary>The stall rule (RealmRaidRecruitmentPolicy.Stalled): when the raid last moved forward, and what it had then.</summary>
            public long LastProgressTick;
            public int ProgressNamed, ProgressWipes, ProgressLowest = 101;
            public string ProgressTarget;
            public bool Forced;
            /// <summary>A play test's raid (/epictest): forced recruiting, but the automatic raid's clocks (late and short
            /// starts at the staging deadline, the stall rule in battle), so it ends the way a real raid does.</summary>
            public bool Playtest;
            /// <summary>The raid's staging and battle run on the automatic raid's clocks.</summary>
            public bool Clocked => !Forced || Playtest;
            public long Created;
            public RealmRaidMuster.Hub Hub;
            public bool HubDeparted;
            public long HubDepartedTick;
            /// <summary>The most staged at once, and when that count last grew (an arrival).</summary>
            public int StagingBest;
            public long StagingArrivalTick;
            public long NextDefenseBroadcast;
            public readonly Dictionary<int, Vector3> HubPosts = new();
            public Vector3[] OutboundSeams = [];
            public readonly List<GameBot[]> ForcedParties = new();
            public readonly Dictionary<int, Vector3> StagingPosts = new();
            /// <summary>An open-world boss's neighbours no staging post stands near (AutonomousRealmRaid.RingMonsters).</summary>
            public RealmRaidTetherReach.RingMonster[] RingMonsters;
            /// <summary>Formation slots with no staging or hub post (tight ground by the entrance); skipped from then on.</summary>
            public readonly HashSet<int> UnusableSlots = new();
            public RealmRaidDungeonRoute DungeonRoute;
            /// <summary>A living boss on the raid's walk through another dungeon, fought there before the raid goes on
            /// (RealmRaidRouteGate); null when the way is clear.</summary>
            public RealmRaidDungeonRoute Transit;
            public long NextGateCheck;
            /// <summary>Named killed and wipes taken at gates already passed (the stall rule's progress).</summary>
            public int GateNamed, GateWipes;
            /// <summary>A dragon raid's attempts and regroups (2026-10-30); null for a dungeon raid.</summary>
            public RealmRaidDragonFight DragonFight;
            public readonly RealmRaidLootOwner.Ledger LootLedger = new();
            public readonly Dictionary<Group, Party> Parties = new();
            public GameLiving[] Support = [];
            /// <summary>The most bots the raid had recruited at any time (RealmRaidRecruitmentPolicy.UnderStrength).</summary>
            public int MostRecruited;
            /// <summary>Members released from this raid (no route to the hub); never recruited back.</summary>
            public readonly HashSet<long> Released = new();
            public long NextRefill;
            public long NextPostWarning;
            /// <summary>Level-50 bots this raid must stage (200 when forced; scaled to the realm when automatic).</summary>
            public int Required = RealmRaidRecruitmentPolicy.AutonomousMinimumPresent;
            /// <summary>Level-50 bots of the realm this raid recruits (300 when forced; sized with Required when automatic).</summary>
            public int RecruitCap = RealmRaidRecruitmentPolicy.MaximumBots;
            /// <summary>How an automatic raid was sized from its encounter's record (null when forced).</summary>
            public RealmRaidRecruitmentPolicy.RaidSizing Sizing;
            /// <summary>This raid's members did most of the damage to its final encounter (LootOwner).</summary>
            public bool FinalKillCredited;
            /// <summary>Another realm's raid was credited with this raid's final encounter (a contested neutral dungeon).</summary>
            public eRealm BeatenBy = eRealm.None;
            /// <summary>This raid has a Leeroy (AutonomousRaidLeeroy): a dragon raid's charge at staging, once.</summary>
            public bool LeeroyRolled, LeeroyDone;
            public string LeeroyName;
            /// <summary>Wipes this raid took (dungeon encounters and dragons): a time-out with none is no result.</summary>
            public int Wipes;
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
            /// <summary>A dungeon raid's party staging outside the way in, as before the raid went in (return waves).</summary>
            public View RallyView;
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

        /// <summary>A running neutral raid (Darkness Falls, Summoner's Hall) has a party destination in
        /// <paramref name="region"/>. Realm raids in their own dungeons are never counted (<see cref="_raidDestinations"/>).</summary>
        public static bool HasActiveRaidIn(ushort region)
        {
            foreach (var d in _raidDestinations)
                if (d.Region == region) return true;
            return false;
        }

        /// <summary>
        /// Run 8 (2026-10-14): the Albion raid in Darkness Falls stood 6,000-10,000 units short of Prince Ba'alorien
        /// for forty minutes, fighting essence shredders, naburite drinkers and succubi that came back every 100-180
        /// seconds (263 essence shredders alone), and skipped him untouched. While a neutral bot raid is inside Darkness
        /// Falls or Summoner's Hall, the ordinary monsters there stay down for ten minutes; bosses, epic encounters and
        /// scripted adds keep their own timers, and nothing changes once the raid ends.
        /// Neutral raids only (owner, 2026-11-15): the realm raid dungeons (Caer Sidi, Galladoria, Krondon and the other
        /// <see cref="RaidDungeons"/>) keep their monsters' own respawn timers while a raid is in them. They were listed here
        /// before, but <see cref="HasActiveRaidIn"/> only ever saw neutral raids, so the hold never applied in them; the list
        /// now says what the code does. Their first-hour sweep leaves out monsters that came back instead
        /// (RealmRaidDungeonRoute.SweepTarget).
        /// </summary>
        public const int RaidTrashRespawnMilliseconds = 10 * 60_000;
        public static readonly ushort[] SlowRespawnRegions = [RealmRaidNeutralEvents.DarknessFalls, RealmRaidNeutralEvents.SummonersHall];

        public static int RaidRespawnInterval(GameNPC npc, int interval)
        {
            if (interval <= 0 || interval >= RaidTrashRespawnMilliseconds || npc is IGameEpicNpc or GameSummonedPet or GameBot ||
                npc.Realm != eRealm.None || Array.IndexOf(SlowRespawnRegions, npc.CurrentRegionID) < 0 || !HasActiveRaidIn(npc.CurrentRegionID))
                return interval;
            return RaidTrashRespawnMilliseconds;
        }

        private static volatile ushort[] _epicHoldRegions = [];

        /// <summary>A dead epic monster whose respawn waits while a realm raid is in its dungeon (RealmRaidRouteGate): read
        /// without the raid lock by the monster's respawn timer.</summary>
        public static bool HoldsEpicRespawn(GameNPC npc)
        {
            bool holds = npc != null && RealmRaidRouteGate.HoldsRespawn(RealmRaidRouteGate.HoldEpicRespawn, npc is IGameEpicNpc,
                npc.Realm == eRealm.None, npc.RespawnInterval, Array.IndexOf(_epicHoldRegions, npc.CurrentRegionID) >= 0);
            // Once per wait, so the owner's log shows which epics a raid kept down (Gatekeeper Dommel, PC 2026-11-14).
            if (npc == null) return false;
            if (!holds) { HeldRespawns.TryRemove(npc, out _); return false; }
            if (HeldRespawns.TryAdd(npc, 0))
                Log.Info($"REALM_RAID_EPIC_RESPAWN_HELD npc=\"{npc.Name}\" region={npc.CurrentRegionID} respawn_s={npc.RespawnInterval / 1000} " +
                         $"reason=\"a realm raid is in its dungeon; it comes back once the raid has ended\"");
            return true;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<GameNPC, byte> HeldRespawns = new();

        /// <summary>The regions whose epic respawns a started dungeon raid holds (pure): its own dungeon and the one its way
        /// in leads through (Hall of the Corrupt for Albion's Summoner's Hall raid, where Aidon stands).</summary>
        public static ushort[] EpicHoldRegionsOf(ushort raidRegion, ushort? entranceTarget) =>
            entranceTarget is ushort through && through != 0 && through != raidRegion ? [raidRegion, through] : [raidRegion];

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
        // A raid's claim on a bot is its duty lease (docs/DUTY-LEASE.md): raid_reserved while it is a planned recruit (or
        // an Epic Special Force, for the whole raid), raid_party once its party has formed, bound to that party's group.
        // Only the reserved stage is what "reserved" means here, as the old reservation map did.
        public static bool IsReserved(GameBot bot) => BotDutyLeases.HoldsWithReason(bot, eDutyLeaseKind.RealmRaid, DutyLeaseReason.RaidReserved);

        private static void Reserve(GameBot bot, Raid raid) =>
            BotDutyLeases.Claim(bot, eDutyLeaseKind.RealmRaid, raid.Definition.Id, DutyLeaseReason.RaidReserved, raid.Definition.Name);

        private static void EndReservation(GameBot bot, string id)
        {
            if (IsReservedFor(bot, id)) BotDutyLeases.Release(bot, eDutyLeaseKind.RealmRaid, id);
        }

        /// <summary>The bot is in one of the raid's parties: its lease moves to raid_party, bound to the party's group.</summary>
        private static void ClaimPartyLease(GameBot bot, Raid raid, Group group) =>
            BotDutyLeases.Claim(bot, eDutyLeaseKind.RealmRaid, raid.Definition.Id, DutyLeaseReason.RaidParty, raid.Definition.Name, group);

        private static void EndPartyLease(GameBot bot, string id)
        {
            if (BotDutyLeases.HoldsWithReason(bot, eDutyLeaseKind.RealmRaid, DutyLeaseReason.RaidParty, id))
                BotDutyLeases.Release(bot, eDutyLeaseKind.RealmRaid, id);
        }

        /// <summary>A member of a raid party whose rally has not begun its battle yet (hub, travel or staging).</summary>
        public static bool IsInRally(GameBot bot)
        {
            Group group = bot?.Group;
            if (group == null) return false;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid)) return Membership.TryGetValue(group, out Raid raid) && !raid.Started && raid.Parties.ContainsKey(group);
        }
        /// <summary>The raid a party belongs to: its event id and its parties in the raid's order (the first party's leader is
        /// the raid leader, as in its briefing and its ledger row). Null when the group is in no raid.</summary>
        public static (string Id, Group[] Parties)? RaidOf(Group group)
        {
            if (group == null) return null;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                return Membership.TryGetValue(group, out Raid raid) && raid.Parties.ContainsKey(group)
                    ? (raid.Definition.Id, raid.Parties.Keys.ToArray()) : null;
        }

        /// <summary>The raid event whose encounters include this boss class (its final or named boss), or null.</summary>
        public static string RaidIdForBoss(string bossType) => string.IsNullOrEmpty(bossType) ? null :
            Definitions.FirstOrDefault(d => d.BossType == bossType || d.FinalTypes.Contains(bossType))?.Id;

        public static bool IsReservedFor(GameBot bot, string id) =>
            id != null && BotDutyLeases.HoldsWithReason(bot, eDutyLeaseKind.RealmRaid, DutyLeaseReason.RaidReserved, id);
        public static bool IsForcedExpedition(string id)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid)) return Raids.TryGetValue(id, out var raid) && raid.Forced;
        }
        public static bool IsEligible(GameBot bot) => bot != null && !AutonomousBeggars.Is(bot) && RealmRaidRecruitmentPolicy.Eligible(
            bot.Level, bot.IsAutonomousWorldBot, bot.IsTemporaryGroupHelper, BotDutyLeases.IsSpokenForByPlayer(bot));

        /// <summary>
        /// An automatic raid recruits level 50s waiting for a group and level 50s grinding solo (who earn no
        /// experience there); RvR bots are left to the frontier.
        /// </summary>
        public static bool MayAnswerAutomaticRaid(GameBot bot) => bot != null && !AutonomousBotSchedule.LeavingSoon(bot) &&
            (AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.GroupPve) ||
             bot.Level >= 50 && AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.SoloPve));

        private static int OnlineLevelFifty(eRealm realm) =>
            AutonomousBotRegistry.Snapshot().Count(b => IsEligible(b) && b.Realm == realm && !AutonomousBotSchedule.LeavingSoon(b));

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
                // A slot that comes due during the startup ramp waits for it (Start refuses until then).
                if (!AutonomousPopulationController.StartupRampFinished) continue;
                int online = OnlineLevelFifty(realm);
                if (!RealmRaidRecruitmentPolicy.ScheduledRaidDue(now, due, Raids.Values.Any(r => r.Definition.Realm == realm), online, minutes))
                    continue;
                NextScheduled[realm] = now + RealmRaidRecruitmentPolicy.NextScheduleMilliseconds(minutes, Random.Shared.NextDouble());
                // The open-world bosses take their share of the rolls (RealmRaidOpenWorld.Choose), the rest keep theirs.
                Definition chosen = ChooseAutomatic(realm);
                if (chosen == null)
                {
                    Log.Info($"REALM_RAID_SCHEDULE_SKIPPED realm={GlobalConstants.RealmToName(realm)} online={online} reason=\"no encounter available (respawning or on cooldown)\"");
                    continue;
                }
                // 1.2: the boss's tale first, then the call (AutonomousRealmRaid.Call.cs); the raid forms when the tale ends.
                if (BeginCall(chosen, realm, now, false))
                    continue;
                if (Start(chosen.Id, realm, out string reason))
                {
                    Log.Info($"REALM_RAID_SCHEDULED event={chosen.Id} realm={GlobalConstants.RealmToName(realm)} online={online} required={Raids[chosen.Id].Required} " +
                             $"recruitCap={Raids[chosen.Id].RecruitCap} {SizeField(Raids[chosen.Id])} nextMinutes={minutes}");
                }
                else
                {
                    Log.Warn($"REALM_RAID_SCHEDULE_FAILED event={chosen.Id} realm={GlobalConstants.RealmToName(realm)} online={online} reason=\"{reason}\"");
                    // An open-world boss its hub can't reach doesn't take the realm's next slots too (RealmRaidOpenWorld).
                    if (chosen.IsOpenWorld) Cooldowns[chosen.Id] = now + RealmRaidOpenWorld.FailedStartCooldownMilliseconds;
                }
            }
        }
        /// <summary>
        /// Whether the realm's automatic raid on this encounter could open now (AutonomousTales, before a bot says a raid is
        /// forming): automatic raids are on, the server is past its startup ramp, the realm has no raid, the encounter is up
        /// and off cooldown, and twice the smallest raid's worth of level 50s are free, as for a scheduled raid.
        /// </summary>
        public static bool CouldStartForTale(string id, eRealm realm, out string reason)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                int online = OnlineLevelFifty(realm);
                reason = RealmRaidRecruitmentPolicy.ScheduleMinutes <= 0 ? "automatic raids are off"
                    : !AutonomousPopulationController.StartupRampFinished ? "the realm's gamebots are still logging in"
                    : !Definitions.Any(d => d.Id == id && d.Realm == realm) ? "no such raid for that realm"
                    : Raids.Values.Any(r => r.Definition.Realm == realm) ? "the realm already has a raid"
                    : PendingCalls.ContainsKey(realm) ? "the realm's next raid is waiting for its tale"
                    : !Available(id) ? "the encounter is not up, or on cooldown"
                    : online < RealmRaidRecruitmentPolicy.MinimumScaledBots * 2 ? $"only {online} level 50s of the realm are free"
                    : null;
                return reason == null;
            }
        }

        /// <summary>
        /// A tale's raid (AutonomousTales: the rumour after a tale was true): the realm's automatic raid on this encounter
        /// opens now, sized, recruited and announced like a scheduled one, and the realm's next scheduled raid moves on a
        /// full slot so the two don't follow each other. False with the reason.
        /// </summary>
        public static bool StartForTale(string id, eRealm realm, out string reason)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!CouldStartForTale(id, realm, out reason) || !Start(id, realm, out reason))
                    return false;
                Raid opened = Raids[id];
                long now = GameLoop.GameLoopTime;
                NextScheduled[realm] = now + RealmRaidRecruitmentPolicy.NextScheduleMilliseconds(RealmRaidRecruitmentPolicy.ScheduleMinutes, Random.Shared.NextDouble());
                reason = $"{opened.Definition.Name}: {opened.Required} must gather, up to {opened.RecruitCap} recruited";
                Log.Info($"REALM_RAID_SCHEDULED event={id} realm={GlobalConstants.RealmToName(realm)} online={OnlineLevelFifty(realm)} required={opened.Required} " +
                         $"recruitCap={opened.RecruitCap} {SizeField(opened)} forced=tale");
                return true;
            }
        }

        /// <summary>
        /// GM test (/lstest raid): the realm's automatic raid opens now, sized to its online level 50s like a scheduled one
        /// (forced raids from the launcher need 300). <paramref name="dragon"/> picks the realm's dragon, else a random
        /// available raid. Returns false with <paramref name="reason"/>.
        /// </summary>
        public static bool StartAutomaticForTest(eRealm realm, bool dragon, out string reason)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
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
                Raid opened = Raids[chosen.Id];
                reason = $"{chosen.Name}: {opened.Required} of {online} free level 50s must gather (at least 15 minutes), up to {opened.RecruitCap} recruited" +
                         (opened.Sizing is { } sizing && sizing.Percent != 100 ? $"; {sizing.Percent:0.#}% of its full {sizing.FullRequired} ({sizing.Why})" : "");
                Log.Info($"REALM_RAID_SCHEDULED event={chosen.Id} realm={GlobalConstants.RealmToName(realm)} online={online} required={opened.Required} " +
                         $"recruitCap={opened.RecruitCap} {SizeField(opened)} forced=lstest");
                return true;
            }
        }

        /// <summary>The realm's automatic pick (under the raid lock): every raid it could open now but the one it last won, the
        /// open-world bosses at their share (RealmRaidOpenWorld.Choose).</summary>
        private static Definition ChooseAutomatic(eRealm realm)
        {
            string[] candidates = Definitions.Where(d => d.Realm == realm && Available(d.Id) &&
                MayStartAutomatic(d.Id, LastSuccessful.GetValueOrDefault(realm))).Select(d => d.Id).ToArray();
            string id = RealmRaidOpenWorld.Choose(candidates);
            return id == null ? null : Definitions.First(d => d.Id == id && d.Realm == realm);
        }

        private static string SizeField(Raid raid) => raid.Sizing is { } sizing ? $"size={sizing.Percent:0.#}% full={sizing.FullRequired}" : "size=100%";

        /// <summary>
        /// Every raid encounter. The last number is the raid's full size (an automatic raid's Required before win-rate
        /// sizing moves it 70-150%): the comfortable size of a 2002-2004 raid there, from period forum threads
        /// (docs/RAID-SIZES.md, owner 2026-11-04). It was 200 for every dragon, epic and neutral raid. Forced raids
        /// from the launcher still need 200.
        /// </summary>
        public static readonly Definition[] Definitions =
        [
            new("dragon-albion", "Golestandt", eRealm.Albion, 1, "AlbGolestandt", 70),
            new("dragon-midgard", "Gjalpinulva", eRealm.Midgard, 100, "MidGjalpinulva", 70),
            new("dragon-hibernia", "Cuuldurach", eRealm.Hibernia, 200, "HibCuuldurach", 70),
            new("epic-albion", "Caer Sidi", eRealm.Albion, 60, "ApocInitializator", 100),
            new("epic-midgard", "Tuscaran Glacier", eRealm.Midgard, 160, "KingTuscar", 100),
            new("epic-hibernia", "Galladoria", eRealm.Hibernia, 191, "Olcasgean", 100),
            new("summoners-albion", "Summoner's Hall", eRealm.Albion, 248, "GrandSummonerGovannon", 64),
            new("summoners-midgard", "Summoner's Hall", eRealm.Midgard, 248, "GrandSummonerGovannon", 64),
            new("summoners-hibernia", "Summoner's Hall", eRealm.Hibernia, 248, "GrandSummonerGovannon", 64),
            new("darkness-albion", "Darkness Falls", eRealm.Albion, 249, "Legion", 64),
            new("darkness-midgard", "Darkness Falls", eRealm.Midgard, 249, "Legion", 64),
            new("darkness-hibernia", "Darkness Falls", eRealm.Hibernia, 249, "Legion", 64),
            ..RaidDungeons.Select(d => new Definition(d.Id, d.Name, d.Realm, d.Region, d.BossType, d.SuggestedBots)),
            // Living Server 1.2: the open-world big bosses, each its realm's raid (RealmRaidOpenWorld).
            ..RealmRaidOpenWorld.Raids.Select(r => new Definition(r.Id, r.Name, r.Realm, r.Region, r.BossType, r.SuggestedBots))
        ];

        public static void Pulse(long now)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
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
                    // finals that stand from the start beside the event's boss (Queen Kula beside King Tuscar)
                    foreach (string type in StandingFinals(definition).Where(t => t != definition.BossType))
                    {
                        string key = definition.Id + "/" + type;
                        if (!Bosses.TryGetValue(key, out var standing) || standing.ObjectState != GameObject.eObjectState.Active)
                        {
                            var final = WorldMgr.GetRegion(definition.Region)?.Objects.OfType<GameNPC>()
                                .FirstOrDefault(n => n.GetType().Name == type && n.IsAlive);
                            if (final != null) Bosses[key] = final;
                        }
                    }
                }
                PulseSchedule(now);
                PulseCalls(now);
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
                    raid.MostRecruited = Math.Max(raid.MostRecruited, raid.Support.Length);
                    int presentAtHub = raid.Parties.Values.Sum(p => PresentAtHub(raid, p));
                    bool departing = !raid.HubDeparted && RealmRaidRecruitmentPolicy.DepartHub(false, presentAtHub,
                        GameLoop.GameLoopTime - raid.Created, raid.Required, !raid.Clocked);
                    raid.HubDeparted |= departing;
                    if (departing) raid.HubDepartedTick = GameLoop.GameLoopTime;
                    // Living Server 1.2: the raid leader briefs the realm on the boss as the raid leaves the muster (docs/LORE.md).
                    if (departing) LoreChat.Briefing(raid.Parties.Keys.Select(g => g.LivingLeader).OfType<GameBot>().FirstOrDefault(), raid.Definition.BossType);
                    if (departing && raid.Forced && presentAtHub < raid.Required)
                        Log.Info($"REALM_RAID_FORCED_PATIENCE event={raid.Definition.Id} step=hub present={presentAtHub} required={raid.Required} " +
                                 $"recruited={raid.Support.Length} minutes={(GameLoop.GameLoopTime - raid.Created) / 60_000} " +
                                 $"reason=\"the rest follow as late members\"");
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
                    { End(raid, now, (raid.Definition.IsOpenWorld ? "The boss encounter" : "The dragon encounter") +
                        " has ended; checking actual respawn before the next expedition."); continue; }
                    if (raid.Started && raid.Definition.IsNeutral)
                        RealmRaidNeutralEvents.ApplyEncounterLevels(raid.Definition.Region, raid.Definition.Objectives);
                    raid.DungeonRoute?.ObserveCompletion();
                    if (raid.DungeonRoute?.Complete == true)
                    { End(raid, now, CompletionReason(raid, "The final dungeon encounter has been defeated.")); continue; }
                    if (raid.Started && WatchProgress(raid, now))
                    {
                        Log.Info($"REALM_RAID_STALLED event={raid.Definition.Id} minutes={(now - raid.LastProgressTick) / 60_000} " +
                                 $"named={raid.ProgressNamed} wipes={raid.ProgressWipes} target=\"{raid.ProgressTarget}\" lowest={raid.ProgressLowest} " +
                                 $"headway_minutes={(raid.LastHeadwayTick > 0 ? (now - raid.LastHeadwayTick) / 60_000 : -1)} " +
                                 $"wave={(raid.Walk != null ? $"{raid.Walk.Number}@{raid.Walk.Step + 1}/{raid.Walk.Steps.Length}" : "none")}");
                        End(raid, now, $"Stalled: no named or boss kill, no new low on its target, no wipe and no return-wave headway for {StallLimit(raid) / 60_000} minutes.");
                        continue;
                    }
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
                        if (present > raid.StagingBest)
                        {
                            raid.StagingBest = present;
                            raid.StagingArrivalTick = now;
                        }
                        // A forced raid's staging patience counts from when it left the hub (it has no staging before that).
                        long waited = !raid.Clocked ? raid.HubDeparted ? now - raid.HubDepartedTick : 0 : now - raid.Created;
                        bool ready = RealmRaidRecruitmentPolicy.Ready(!raid.Clocked, waited, present, DragonLanded(raid), raid.Required);
                        if (ready && !raid.Clocked && present < raid.Required)
                            Log.Info($"REALM_RAID_FORCED_PATIENCE event={raid.Definition.Id} step=staging present={present} required={raid.Required} " +
                                     $"recruited={raid.Support.Length} minutes={waited / 60_000} reason=\"the rest follow as late members\"");
                        bool expired = !ready && RealmRaidRecruitmentPolicy.StagingExpired(!raid.Clocked, now, raid.Created, raid.HubDepartedTick,
                            present, DragonLanded(raid), raid.Required, raid.StagingArrivalTick);
                        // Out of time with at least half the raid there: it goes in short, and the rest follow it in.
                        if (expired && DragonLanded(raid) && RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(!raid.Clocked, present, raid.Required))
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
                            // No battle window: a raid in battle ends when beaten, blocked or stalled (WatchProgress).
                            raid.Deadline = long.MaxValue;
                            raid.LastProgressTick = now;
                            RealmEventRecords.Progress(raid.Definition.Id, "Battle", "Expedition began advancing to the encounter.", raid.Support.Length, present);
                            Log.Info($"REALM_RAID_STARTED event={raid.Definition.Id} present={present} required={raid.Required} " +
                                     $"support={raid.Support.Length} minutes={(now - raid.Created) / 60_000} forced={raid.Forced} playtest={raid.Playtest}");
                            RealmEventNotices.Queue(raid.Definition.Id, raid.Definition.Realm,
                                $"The {raid.Definition.Name} expedition is advancing with {present} staged level-50 adventurers.");
                            // An expedition that lost here the last two times brings the Epic Special Forces from the start
                            // (dragon raids too since 2026-10-30; their record is the dragon's own ledger).
                            if (AutonomousEpicSpecialForces.ComesFromStart(raid.Definition.Id))
                                AutonomousEpicSpecialForces.Request(raid.Definition.Id, raid.Definition.Name, raid.Definition.Realm,
                                    "the last two expeditions here were defeated");
                            // A dragon raid's first attempt: everyone at the staging ring is in it.
                            raid.DragonFight?.Engage(StagedBots(raid).OfType<GameBot>().Select(b => b.DatabaseID), DragonState(raid), now);
                            if (leeroy != null && AutonomousRaidLeeroy.Charge(leeroy, raid.Boss, staged, raid.Definition.Name))
                                raid.LeeroyName = leeroy.Name;
                        }
                        else if (expired)
                        { End(raid, now, $"Staging failed: {present} adventurers arrived; no undersized or airborne assault was ordered."); continue; }
                    }
                    if (raid.Started && raid.DungeonRoute != null)
                    {
                        // A living boss on the walk through another dungeon is fought first (RealmRaidRouteGate).
                        RealmRaidDungeonRoute route = PulseRouteGate(raid, now);
                        route.Advance(raid.Support, now);
                        PulseReturnWave(raid, now);
                        PulseReturnWalk(raid, now);
                        while (route.TryTakeWipe(out string wipedBy, out int failures, out int engaged, out int fallen))
                            OnWipe(raid, wipedBy, failures, now, engaged, fallen);
                        if (route.Defeated)
                        {
                            End(raid, now, $"Defeated at {route.DefeatedAt} after {RealmRaidDungeonRoute.MaximumFailedAttempts} failed attempts.");
                            continue;
                        }
                        if (route == raid.DungeonRoute && raid.DungeonRoute.Blocked)
                        { End(raid, now, "Route blocked: no remaining encounter was reachable from the raid's positions for 20 minutes."); continue; }
                        if (raid.DungeonRoute.Complete)
                        { End(raid, now, CompletionReason(raid, $"The {raid.Definition.Name} expedition defeated the final encounter.")); continue; }
                    }
                    // A dragon raid's wipe (2026-10-30): the same regroup and Special Forces call as a dungeon raid's.
                    if (raid.Started && raid.DragonFight is { } fight)
                    {
                        PulseDragonReturnWave(raid, now);
                        fight.Advance(DragonRaiders(raid), DragonState(raid), now);
                        while (fight.TryTakeWipe(out int failures))
                            OnWipe(raid, raid.Boss.Name, failures, now);
                        if (fight.Defeated)
                        {
                            // "after 2 failed attempts", or a regroup nobody reached for 20 minutes (2026-10-31).
                            End(raid, now, $"Defeated at {fight.DefeatedAt} {fight.DefeatedHow}.");
                            continue;
                        }
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
                // The dungeons whose epic respawns wait while a raid is in them (RealmRaidRouteGate.HoldEpicRespawn).
                _epicHoldRegions = Raids.Values.Where(r => r.Started && r.DungeonRoute != null)
                    .SelectMany(r => EpicHoldRegionsOf(r.Definition.Region, r.DungeonRoute.Entrance?.TargetRegion))
                    .Distinct().ToArray();
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (Membership.TryGetValue(leader.Group, out Raid joined)) { view = joined.Parties[leader.Group].View; return true; }
                Raid raid = Raids.Values.FirstOrDefault(r => r.Definition.Realm == leader.Realm && !r.Forced && RealmParties(r) < RealmRaidRecruitmentPolicy.MaximumParties);
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
                    var definition = ChooseAutomatic(leader.Realm);
                    if (definition == null || !Start(definition.Id, leader.Realm, out _)) return false;
                    raid = Raids[definition.Id];
                }
                else if (Random.Shared.NextDouble() >= RealmRaidRecruitmentPolicy.JoinExistingChance) return false;
                // Removing a party must not make its count reuse a surviving
                // party's staging slot and pile reinforcements on top of it.
                // An automatic raid takes parties that answer on their own only up to its recruit cap, counting the
                // parties it has planned (its size is the lever of the win-rate balance; before, they filled it to 300).
                if (PartyCapRefusal(raid, false, members.Length + raid.ForcedParties.Sum(p => p.Length), raid.RecruitCap) != null) return false;
                if (!TryFreeSlot(raid, out int slot, out Vector3 staging, out var hubPost)) return false;
                var party = new Party { Members = members, FormationSlot = slot, Staging = staging, HubPost = hubPost, Target = members.Length };
                raid.Parties[leader.Group] = party;
                Membership[leader.Group] = raid;
                DefenseGroups[leader.Group] = 0;
                foreach (GameBot member in members) ClaimPartyLease(member, raid, leader.Group);
                raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
                UpdateView(raid, party);
                view = party.View;
                return true;
            }
        }

        public static bool Start(string id, eRealm realm, out string reason, bool forced = false) =>
            Start(id, realm, out reason, forced, 0, null, false);

        /// <summary>
        /// A play test's raid (/epictest, 1.2): a forced raid of <paramref name="playtestSize"/> bots (the encounter's period
        /// size, docs/RAID-SIZES.md) instead of the launcher's 300; it goes in when 90% of them stand at the staging post.
        /// <paramref name="playtestFocus"/> (names or class names) limits the dungeon route to those encounters and the adds
        /// around them, with no first-hour trash clear: the raid walks to them and fights what attacks it on the way.
        /// <paramref name="playtestAtStaging"/> musters the raid at its own staging post instead of the realm's hub (a test
        /// server with only the dungeon's navmeshes, or a test of the fight alone): the walk from the hub is skipped.
        /// </summary>
        public static bool Start(string id, eRealm realm, out string reason, bool forced, int playtestSize, string[] playtestFocus,
            bool playtestAtStaging)
        {
            int forcedRoster = playtestSize > 0 ? playtestSize : RealmRaidRecruitmentPolicy.MaximumBots;
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
            // An automatic raid waits for the startup ramp: its size comes from the realm's online level 50s, and two minutes
            // after a restart few are in (Tuscaran Glacier opened at 20:48 on 2026-11-04 with full=58, not 100).
            if (!forced && !AutonomousPopulationController.StartupRampFinished)
            {
                reason = $"the realm's gamebots are still logging in after the server start (at most {AutonomousPopulationController.StartupRampMinutesLeft} more minutes)";
                return false;
            }
            if (preview != null) SupersedeAutomatic(realm, id);
            GameBot[][] recruits = forced && rallyHub != null
                ? AutonomousBotGroupCoordinator.PlanForcedRaid(realm, rallyHub.Region, rallyHub.Center, forcedRoster) : [];
            if (forced && recruits.Sum(p => p.Length) < forcedRoster)
            {
                reason = $"Not started: only {recruits.Sum(p => p.Length)}/{forcedRoster} eligible level-50 bots could be reserved. No tasks were cancelled.";
                return false;
            }
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                Definition definition = Definitions.FirstOrDefault(d => d.Id == id && d.Realm == realm);
                if (definition == null || !Available(id)) { reason = "The encounter is not alive/available, or this event is on cooldown."; return false; }
                if (!RealmRaidRecruitmentPolicy.CanOpenEvent(forced, Raids.Values.Any(r => r.Definition.Realm == realm), Raids.ContainsKey(id)))
                { reason = "This realm already has an autonomous PvE expedition, or this encounter is already assigned."; return false; }
                if (forced && recruits.SelectMany(p => p).Any(b => IsReserved(b) || GetView(b.Group) != null))
                { reason = "A planned recruit joined another expedition. Nothing was reassigned; please retry."; return false; }
                long now = GameLoop.GameLoopTime;
                // An automatic raid's size comes from its encounter's record (win-rate sizing, 2026-11-04); a forced
                // raid keeps its 200 to stage and 300 to recruit.
                RealmRaidRecruitmentPolicy.RaidSizing sizing = forced ? null : RealmRaidRecruitmentPolicy.Size(
                    RealmRaidRecruitmentPolicy.RequiredBots(OnlineLevelFifty(realm), definition.SuggestedBots, RealmRaidRecruitmentPolicy.MinimumBotsSetting),
                    RealmRaidRecord.GetResults(id), RealmRaidRecruitmentPolicy.RulesFromSettings(), RealmRaidRecord.Loaded,
                    RealmRaidRecruitmentPolicy.MinimumBotsSetting > 0);
                // An event may list several muster hubs; the first with a complete, connected
                // hub-to-encounter route is used (far frontier entrances, e.g. Summoner's Hall).
                Raid raid = null;
                reason = "No muster hub is defined for this event.";
                foreach (RealmRaidMuster.Hub hub in RealmRaidMuster.Hubs.Where(h => h.Event == id))
                {
                    var candidate = new Raid { Definition = definition, Boss = Bosses[id], Forced = forced, Playtest = playtestSize > 0, Created = now, Hub = hub,
                        Required = playtestSize > 0 ? PlaytestRequired(playtestSize) : sizing?.Required ?? RealmRaidRecruitmentPolicy.AutonomousMinimumPresent,
                        RecruitCap = playtestSize > 0 ? playtestSize : sizing?.RecruitCap ?? RealmRaidRecruitmentPolicy.MaximumBots, Sizing = sizing,
                        Deadline = forced && playtestSize <= 0 ? long.MaxValue : now + RealmRaidRecruitmentPolicy.AutonomousStagingLimitMilliseconds };
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
                        if (playtestFocus is { Length: > 0 }) route.Focus = playtestFocus;
                    }
                    else
                        candidate.DragonFight = new RealmRaidDragonFight(candidate.Boss.Name, candidate.Definition.IsOpenWorld);
                    bool staged = TryStaging(candidate, 0, out var destination);
                    if (staged && playtestAtStaging)
                    {
                        ushort stagingRegion = definition.IsDungeon ? candidate.DungeonRoute.Entrance.SourceRegion : definition.Region;
                        Zone stagingZone = WorldMgr.GetRegion(stagingRegion)?.GetZone((int)destination.X, (int)destination.Y);
                        candidate.Hub = new RealmRaidMuster.Hub(id, $"the {definition.Name} staging post (play test)", stagingRegion,
                            (ushort)(stagingZone?.ID ?? 0), 0, 0, destination);
                    }
                    bool posted = TryHubPost(candidate, 0, out var origin);
                    if (!staged || !posted || !RealmRaidMuster.TryRoute(WorldMgr.GetRegion(candidate.Hub.Region), PathfindingProvider.Instance, realm,
                            origin, destination, out candidate.OutboundSeams, candidate.Hub.Via))
                    {
                        reason = StartUnavailableReason(staged, posted);
                        Log.Warn($"REALM_RAID_START_UNAVAILABLE event={id} hub=\"{hub.Name}\" staging={staged} hubPost={posted} " +
                                 $"entrance={candidate.DungeonRoute?.Entrance?.Id} route=\"{(staged && posted ? RealmRaidMuster.LastRouteFailure : "not tried")}\"");
                        continue;
                    }
                    raid = candidate;
                    break;
                }
                if (raid == null) return false;
                Raids[id] = raid;
                if (playtestSize > 0)
                    Log.Info($"REALM_RAID_PLAYTEST_START event={id} size={playtestSize} required={raid.Required} hub=\"{raid.Hub.Name}\" " +
                             $"focus=\"{string.Join(',', playtestFocus ?? [])}\" reserved={recruits.Sum(p => p.Length)}");
                if (sizing != null) Log.Info(RealmRaidRecruitmentPolicy.SizedLine(id, GlobalConstants.RealmToName(realm), sizing));
                // One raid in twenty has a raider who can't wait for the final encounter (bot_living_leeroy).
                raid.LeeroyRolled = AutonomousRaidLeeroy.On && AutonomousRaidLeeroy.RollForRaid(Random.Shared.NextDouble());
                if (raid.DungeonRoute != null) raid.DungeonRoute.LeeroyRolled = raid.LeeroyRolled;
                if (raid.LeeroyRolled) Log.Info($"RAID_LEEROY_ROLLED event={id}");
                RealmEventRecords.Begin(id, definition.Name, definition.IsNeutral ? "Neutral raid" : definition.IsDungeon ? "Epic dungeon" :
                    definition.IsOpenWorld ? RealmRaidOpenWorld.Kind : "Dragon",
                    GlobalConstants.RealmToName(realm), (forced ? "Forced" : "Automatic") + " rally via " + raid.Hub.Name);
                raid.ForcedParties.AddRange(recruits);
                foreach (GameBot bot in recruits.SelectMany(p => p)) Reserve(bot, raid);
                string startCondition = playtestSize > 0
                    ? $"At least {raid.Required} must arrive{(definition.IsDungeon ? " before the dungeon assault" : " and the dragon must land")}."
                    : ForcedStartCondition(definition.IsDungeon);
                // One quiet top-of-screen line for the realm's players (automatic and launcher-forced alike),
                // so a player can join the bots at the rally point.
                // 1.2: a raid of the game or the launcher is called with the large line and a horn (the owner, 2026-11-12);
                // a play test's raid keeps the plain line.
                if (playtestSize > 0)
                    RealmEventNotices.QueueScreen(id, realm, $"A {definition.Name} raid is forming at {raid.Hub.Name}.", AnnouncementKind.PveRealmEvent);
                else
                    RealmEventNotices.QueueScreen(id, realm, RealmRaidRecruitmentPolicy.CallLine(GlobalConstants.RealmToName(realm), definition.Name, raid.Hub.Name),
                        AnnouncementKind.PveRealmEvent, RealmRaidRecruitmentPolicy.CallHornSound);
                // Living Server 1.2: the place's own line for the realm, after the forming notice (docs/LORE.md).
                if (GameWideAnnouncements.Enabled(AnnouncementKind.PveRealmEvent))
                    LoreAtmosphere.RaidForming(realm, definition.Region, definition.Name);
                RealmEventNotices.Queue(id, realm, forced
                    ? $"{definition.Name}: {recruits.Sum(p => p.Length)} level-50 adventurers reserved; gather at {raid.Hub.Name}. {startCondition}"
                    : $"{definition.Name}: recruiting up to {raid.RecruitCap} level-50 adventurers via {raid.Hub.Name}; at least {raid.Required} must arrive before assault.",
                    "raid_forming", RaidSlots(definition.Name, null));
                reason = forced ? $"Reserved {recruits.Sum(p => p.Length)} level-50 bots, closest to {raid.Hub.Name} first. The raid runs until the encounter is defeated or you press Stop event. {startCondition}" :
                    $"Recruiting up to {raid.RecruitCap} level-50 bots via {raid.Hub.Name}. At least {raid.Required} must arrive before assault; staging can last up to {RealmRaidRecruitmentPolicy.AutonomousStagingLimitMilliseconds / 60_000} minutes.";
                return true;
            }
        }

        /// <summary>A play-test raid goes in when 80% of its roster is staged (rounded up; the rest follow it in, as late arrivals
        /// of any raid do): an automatic raid's 125% recruits 160 to stage 125 (78%). 90% held a Caer Sidi raid at 87 of 90 for
        /// half an hour with members out of reach (2026-11-07).</summary>
        public static int PlaytestRequired(int size) => Math.Max(8, (size * 4 + 4) / 5);

        /// <summary>The bots a running raid has reserved and not yet formed into parties (the play test moves them to the hub).</summary>
        public static GameBot[] ReservedRecruits(string id)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                return Raids.TryGetValue(id, out Raid raid) ? raid.ForcedParties.SelectMany(p => p).Distinct().ToArray() : [];
        }

        /// <summary>
        /// The play test's gather (/epictest gather): before the raid leaves its hub, the party members outside the hub (a
        /// bot reserved in a capital city was taken out by its own town porter before it formed); after it left and before it
        /// goes in, the members not yet on the staging ring, and where to put them (the hub, or the entrance's outside point /
        /// the dragon's lair). It stands in for the walk across the realm, which the container's partial navmeshes can't do.
        /// </summary>
        public static GameBot[] MembersToGather(string id, out ushort region, out Vector3 point)
        {
            region = 0;
            point = default;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Raids.TryGetValue(id, out Raid raid) || raid.Started) return [];
                GameBot[] members = raid.Parties.Values.SelectMany(p => p.Members).Where(b => b.IsAlive).ToArray();
                if (!raid.HubDeparted)
                {
                    region = raid.Hub.Region;
                    point = raid.Hub.Center;
                    var hub = new Point3D((int)raid.Hub.Center.X, (int)raid.Hub.Center.Y, (int)raid.Hub.Center.Z);
                    return members.Where(b => b.CurrentRegionID != raid.Hub.Region || !b.IsWithinRadius(hub, 3_000)).ToArray();
                }
                if (raid.DungeonRoute != null)
                {
                    var edge = raid.DungeonRoute.Entrance;
                    region = edge.SourceRegion;
                    point = new(edge.SourceX, edge.SourceY, edge.SourceZ);
                }
                else
                {
                    // A dragon raid gathers on its staging ring, never on the lair's home (play test duo4: a gather to the
                    // home put 15 bots on Gjalpinulva before the raid started).
                    region = raid.Definition.Region;
                    if (!TryDragonGatherPoint(raid.StagingPosts.OrderBy(p => p.Key).Select(p => p.Value), out point)) return [];
                }
                return members.Where(b => !IsStaged(raid, b)).ToArray();
            }
        }

        /// <summary>Play test only: the raid's living members inside its dungeon that are not within 1,000 of the route's front
        /// (its next target's approach, or the final encounter's trigger), and that front. It lets a test skip the climb to
        /// the final room (Caer Sidi's lower floor wiped every raid of the 2026-11-07 play test before the horsemen).</summary>
        public static GameBot[] MembersToFront(string id, out ushort region, out Vector3 point)
        {
            region = 0;
            point = default;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Raids.TryGetValue(id, out Raid raid) || !raid.Started || raid.DungeonRoute == null) return [];
                region = ActiveRoute(raid).Region;
                point = ActiveRoute(raid).Front;
                var front = new Point3D((int)point.X, (int)point.Y, (int)point.Z);
                ushort inside = region;
                return raid.Parties.Values.SelectMany(p => p.Members)
                    .Where(b => b.IsAlive && b.CurrentRegionID == inside && !b.IsWithinRadius(front, 1_000)).ToArray();
            }
        }

        /// <summary>
        /// Play test only (the sweep's guest): the raid's living raiders, which one is the raid leader (the first party's
        /// leader), and where the raid's front is now: its hub before it leaves, the way in while it marches to a dungeon,
        /// the route's front inside it, a dragon raid's staging ring and then its dragon. False when the raid isn't running;
        /// <paramref name="hasFront"/> is false when a dragon raid has no staging post yet.
        /// </summary>
        public static bool TryGuestFront(string id, out bool hasFront, out ushort region, out Vector3 point,
            out (GameBot Bot, bool RaidLeader)[] raiders)
        {
            hasFront = false;
            region = 0;
            point = default;
            raiders = [];
            if (id == null) return false;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Raids.TryGetValue(id, out Raid raid)) return false;
                GameLiving raidLeader = raid.Parties.Keys.FirstOrDefault()?.LivingLeader;
                raiders = raid.Parties.Values.SelectMany(p => p.Members).Distinct()
                    .Where(b => b != null && b.IsAlive && b.ObjectState == GameObject.eObjectState.Active)
                    .Select(b => (b, b == raidLeader)).ToArray();
                if (!raid.HubDeparted && raid.Hub != null)
                {
                    region = raid.Hub.Region;
                    point = raid.Hub.Center;
                    hasFront = true;
                }
                else if (raid.DungeonRoute != null)
                {
                    if (raid.Started)
                    {
                        region = raid.DungeonRoute.Region;
                        point = raid.DungeonRoute.Front;
                    }
                    else
                    {
                        var edge = raid.DungeonRoute.Entrance;
                        region = edge.SourceRegion;
                        point = new(edge.SourceX, edge.SourceY, edge.SourceZ);
                    }
                    hasFront = true;
                }
                else
                {
                    region = raid.Definition.Region;
                    if (raid.Started && raid.Boss is { IsAlive: true, ObjectState: GameObject.eObjectState.Active } boss &&
                        boss.CurrentRegionID == region)
                    {
                        point = new(boss.X, boss.Y, boss.Z);
                        hasFront = true;
                    }
                    else hasFront = TryDragonGatherPoint(raid.StagingPosts.OrderBy(p => p.Key).Select(p => p.Value), out point);
                }
                return true;
            }
        }

        /// <summary>Where the play test gathers a departed dragon raid's stragglers: its first staging post (the ring around
        /// the lair), or nowhere while it has none.</summary>
        public static bool TryDragonGatherPoint(IEnumerable<Vector3> stagingPosts, out Vector3 point)
        {
            point = default;
            foreach (Vector3 post in stagingPosts)
            {
                point = post;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Why a raid could not start at a hub (pure). Owner's PC, 2026-11-16: Curengkur's raid failed with "No complete connected
        /// hub-to-encounter route was found" while the route was never tried (staging=False): no staging post round the
        /// encounter could be placed. The reason now says which part failed.
        /// </summary>
        public static string StartUnavailableReason(bool staged, bool posted) =>
            !staged ? "No staging post with a walk to the encounter could be placed. No party was assigned." :
            !posted ? "No muster post could be placed at the hub. No party was assigned." :
            "No complete connected hub-to-encounter route was found. No party was assigned.";

        /// <summary>The running raid's muster hub (region and centre), for the play test.</summary>
        public static bool TryGetHub(string id, out RealmRaidMuster.Hub hub)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                hub = Raids.TryGetValue(id, out Raid raid) ? raid.Hub : null;
                return hub != null;
            }
        }

        // Epic dungeons have no landing to wait for; only dragon rallies mention it.
        public static string ForcedStartCondition(bool isDungeon) => isDungeon
            ? $"At least 200 must arrive before the dungeon assault{ForcedPatienceNote}."
            : $"At least 200 must arrive and the dragon must land{ForcedPatienceNote}.";

        /// <summary>How a forced raid's wait ends (RealmRaidRecruitmentPolicy.ForcedGoesNow).</summary>
        private static string ForcedPatienceNote =>
            $" (after {RealmRaidRecruitmentPolicy.ForcedPatienceMilliseconds / 60_000} minutes of waiting 90% will do, " +
            $"after {RealmRaidRecruitmentPolicy.ForcedLastCallMilliseconds / 60_000} half; the rest follow)";

        // A grounded dragon that is fighting counts as landed wherever the fight
        // dragged it. Requiring the 2,000-unit lair circle parked most of a raid
        // on "Waiting for dragon landing" while a few bots fought on (Cuuldurach
        // took 2 h 20 min on 2026-10-02).
        // An open-world boss never flies off: it counts as landed wherever it walks (RealmRaidOpenWorld).
        private static bool DragonLanded(Raid raid) => raid.Definition.IsDungeon || raid.Definition.IsOpenWorld ||
            RealmRaidStaging.DragonCountsAsLanded((raid.Boss.Flags & GameNPC.eFlags.FLYING) != 0,
                raid.Boss.IsWithinRadius(DragonLairPlacement.Home(raid.Definition.Realm), 2000), raid.Boss.InCombat);

        /// <summary>The point a non-dungeon raid stages around and draws its boss back to: a dragon's lair, an open-world
        /// boss's spawn (RealmRaidOpenWorld.Home).</summary>
        private static Point3D LairHome(Raid raid) => raid.Definition.IsOpenWorld ? RealmRaidOpenWorld.Home(raid.Boss)
            : DragonLairPlacement.Home(raid.Definition.Realm);

        /// <summary>The stall clock's limit: 60 minutes, a forced raid's 3 hours.</summary>
        private static long StallLimit(Raid raid) =>
            raid.Clocked ? RealmRaidRecruitmentPolicy.StalledMilliseconds : RealmRaidRecruitmentPolicy.ForcedStalledMilliseconds;

        /// <summary>True when a raid in battle has stalled. Notes any progress first: a named or boss down, a wipe, or the
        /// named, boss or dragon it fights at a new low.</summary>
        private static bool WatchProgress(Raid raid, long now)
        {
            int named = (raid.DungeonRoute?.NamedDefeatedCount ?? 0) + raid.GateNamed + (raid.Transit?.NamedDefeatedCount ?? 0);
            int wipes = raid.DungeonRoute != null ? raid.DungeonRoute.WipesSoFar + raid.GateWipes + (raid.Transit?.WipesSoFar ?? 0)
                : raid.DragonFight?.FailedAttempts ?? 0;
            GameNPC target = raid.DungeonRoute != null ? ActiveRoute(raid).NamedTarget : raid.Boss?.IsAlive == true ? raid.Boss : null;
            string name = target?.Name;
            int health = target?.HealthPercent ?? 101;
            if (RealmRaidRecruitmentPolicy.MadeProgress(raid.ProgressNamed, named, raid.ProgressWipes, wipes, raid.ProgressTarget, name,
                    raid.ProgressLowest, health))
                raid.LastProgressTick = now;
            if (name != raid.ProgressTarget) raid.ProgressLowest = health;
            else raid.ProgressLowest = Math.Min(raid.ProgressLowest, health);
            raid.ProgressNamed = named;
            raid.ProgressWipes = wipes;
            raid.ProgressTarget = name;
            // The first-hour trash clear is progress (owner, 2026-11-16): the stall clock starts when it ends.
            if (raid.DungeonRoute?.ClearingTrash(now) == true) raid.LastProgressTick = now;
            // A return wave clearing its way back in is headway (owner, 2026-11-17): it restarts the clock, at most one limit
            // beyond the last real progress (RealmRaidReturnWave.Stalled).
            return RealmRaidReturnWave.Stalled(StallLimit(raid), now, raid.LastProgressTick, raid.LastHeadwayTick);
        }

        /// <summary>The dragon as its raid's fight tracker sees it this pulse (RealmRaidDragonFight).</summary>
        private static RealmRaidDragonFight.Dragon DragonState(Raid raid) =>
            new(raid.Boss.InCombat, DragonLanded(raid), DragonLairPlacement.IsOutOfLair(raid.Boss), raid.Boss.HealthPercent, raid.Boss.Health);

        /// <summary>
        /// The raid's roster as the fight tracker sees it: standing (alive, in the dragon's region, not walking back from
        /// a release), at the dragon (within the dungeon route's presence radius) and at the staging ring.
        /// Review fix, 2026-10-31: a dragon raider releases to a bind in the dragon's own region (TryRaidRelease is for
        /// dungeons), and the autonomous controller clears IsReturningAfterRelease on its next turn, so a released raider
        /// is "standing" again seconds after it fell. The tracker therefore remembers who fell during an attempt instead
        /// of judging the wipe from the corpses of the last minute and a half.
        /// </summary>
        private static List<RealmRaidDragonFight.Raider> DragonRaiders(Raid raid)
        {
            var raiders = new List<RealmRaidDragonFight.Raider>(raid.Support.Length);
            foreach (GameBot bot in raid.Support.OfType<GameBot>())
            {
                bool standing = bot.IsAlive && bot.CurrentRegionID == raid.Definition.Region && !bot.IsReturningAfterRelease;
                raiders.Add(new(bot.DatabaseID, standing,
                    standing && bot.IsWithinRadius(raid.Boss, RealmRaidDungeonRoute.TargetPresenceRadius),
                    standing && IsStaged(raid, bot)));
            }
            return raiders;
        }

        /// <summary>A dragon raid regrouping at its staging ring after a wipe (RealmRaidDragonFight).</summary>
        private static bool Regrouping(Raid raid) => raid.DragonFight?.Regrouping == true;

        private static int PresentAtHub(Raid raid, Party party) => party.Members.Count(b => IsAtHub(raid, party, b));

        private static bool IsAtHub(Raid raid, Party party, GameBot b) => IsEligible(b) && b.IsAlive && !b.IsReturningAfterRelease && !b.IsOnStableMasterRoute &&
                b.CurrentRegionID == raid.Hub.Region && b.Group != null &&
                b.IsWithinRadius(new Point3D((int)party.HubPost.X, (int)party.HubPost.Y, (int)party.HubPost.Z), 600);

        /// <summary>
        /// The first free formation slot that has both a staging post and a hub post. A slot without one is set aside, so
        /// a raid by tight ground fills the slots that fit instead of waiting on one that never will (Krondon, 2026-10-21:
        /// the raid asked for slot 7 for 90 minutes with 7 parties gathered, and its rally failed).
        /// </summary>
        private static bool TryFreeSlot(Raid raid, out int slot, out Vector3 staging, out Vector3 hubPost, bool specialForces = false)
        {
            staging = hubPost = default;
            foreach (int candidate in RealmRaidRecruitmentPolicy.SlotOrder(specialForces))
            {
                if (raid.UnusableSlots.Contains(candidate) || raid.Parties.Values.Any(p => p.FormationSlot == candidate)) continue;
                slot = candidate;
                if (TryStaging(raid, candidate, out staging) && TryHubPost(raid, candidate, out hubPost)) return true;
                raid.UnusableSlots.Add(candidate);
            }
            slot = -1;
            return false;
        }

        /// <summary>A party made only of the Epic Special Forces (they come on top of the realm's limits).</summary>
        private static bool IsSpecialForcesParty(Party party) => party.Members.Length > 0 && party.Members.All(b => b.IsEpicSpecialForce);

        private static int RealmParties(Raid raid) => raid.Parties.Values.Count(p => !IsSpecialForcesParty(p));
        private static int SpecialForcesParties(Raid raid) => raid.Parties.Values.Count(IsSpecialForcesParty);
        private static int RealmBots(Raid raid) => raid.Support.Count(b => b is not GameBot { IsEpicSpecialForce: true });

        private static string PartyCapRefusal(Raid raid, bool specialForces, int joining, int recruitCap = RealmRaidRecruitmentPolicy.MaximumBots) =>
            RealmRaidRecruitmentPolicy.PartyCapRefusal(specialForces, RealmParties(raid), RealmBots(raid), SpecialForcesParties(raid), joining, recruitCap);

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

        private static int PresentAtStaging(Raid raid) => raid.Parties.Values.Sum(p => p.Members.Count(b => IsStaged(raid, b)));

        /// <summary>A living member standing on the raid's staging ring (outside a dungeon's door, around a dragon's lair).</summary>
        private static bool IsStaged(Raid raid, GameBot b) => IsEligible(b) && b.IsAlive && !b.IsOnStableMasterRoute && b.Group != null &&
            b.CurrentRegionID == (raid.Definition.IsDungeon ? raid.DungeonRoute.Entrance.SourceRegion : raid.Definition.Region) &&
            AtStaging(raid, b);

        private static GameLiving[] StagedBots(Raid raid) => raid.Parties.Values
            .SelectMany(p => p.Members.Where(b => IsEligible(b) && b.IsAlive && !b.IsOnStableMasterRoute && b.Group != null &&
                b.CurrentRegionID == raid.Definition.Region && AtStaging(raid, b)))
            .Cast<GameLiving>().ToArray();

        private static bool AtStaging(Raid raid, GameBot bot)
        {
            var home = raid.Definition.IsDungeon ? null : LairHome(raid);
            return DragonRallyRoute.AtAssembly(new(bot.X,bot.Y,bot.Z), raid.StagingPosts.Values,
                home == null ? null : new Vector3(home.X,home.Y,home.Z));
        }

        /// <summary>A dragon raid gathering at its staging ring, before it starts or while it regroups after a wipe: its
        /// members in the dragon's region walk the checked route around the lair, never through it.</summary>
        public static bool TryPendingDragonRally(GameBot bot, out Vector3 home)
        {
            home=default;
            if (bot?.IsAutonomousWorldBot != true || bot.Group == null) return false;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Membership.TryGetValue(bot.Group,out var raid) || raid.Started && !Regrouping(raid) || raid.Definition.IsDungeon ||
                    !raid.HubDeparted || bot.CurrentRegionID != raid.Definition.Region) return false;
                var point=LairHome(raid);
                home=new(point.X,point.Y,point.Z);
                return true;
            }
        }

        /// <summary>The raid's dragon while the raid is not going in: nobody pulls it alone (also during a regroup).</summary>
        public static bool IsPendingDragonTarget(GameBot bot, GameLiving target)
        {
            if(bot?.IsAutonomousWorldBot != true || bot.Group == null || target == null) return false;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid)) return Membership.TryGetValue(bot.Group,out var raid) && (!raid.Started || Regrouping(raid)) &&
                !raid.Definition.IsDungeon && ReferenceEquals(raid.Boss,target);
        }

        // Called outside the raid lock by the single coordinator. At most one
        // party per running expedition is rebuilt per five seconds (it was one in
        // total, so three rallies at once took three times as long to form);
        // never 300 native route probes at once.
        public static void RecruitForcedParty(long now)
        {
            var batch = new List<(string Id, GameBot[] Members)>();
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
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
                                EndReservation(b, raid.Definition.Id);
                        }
                    // An automatic raid holds up to three planned parties at once and forms whichever eight (or the final
                    // four) are free first: one fixed roster of solo grinders was rarely out of combat all at once, and
                    // the first scheduled dragon rally (2026-10-19) stalled at 24 of 58 for twenty minutes.
                    int recruitCap = raid.RecruitCap;
                    int planned = raid.ForcedParties.Sum(p => p.Length);
                    int realmBots = RealmBots(raid); // the Special Forces come on top of the cap
                    if (!raid.Forced && realmBots + planned < recruitCap &&
                        raid.ForcedParties.Count < RealmRaidRecruitmentPolicy.AutomaticPlannedParties)
                    {
                        int size = Math.Min(8, recruitCap - realmBots - planned);
                        var waiting = AutonomousBotGroupCoordinator.PlanWaitingRaidParty(raid.Definition.Realm, size);
                        if (waiting != null)
                        {
                            raid.ForcedParties.Add(waiting);
                            foreach (var b in waiting) Reserve(b, raid);
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                foreach (var raid in Raids.Values)
                {
                    if (now < raid.NextRefill) continue;
                    // The Special Forces are not topped up with the realm's own bots.
                    var shortParty = raid.Parties.FirstOrDefault(p => !IsSpecialForcesParty(p.Value) &&
                        RealmRaidRecruitmentPolicy.NeedsReplacement(p.Value.Target, p.Value.Members.Length));
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
                using (AiProfile.Lock(Sync, AiSystem.LockRaid))
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
                    // The recruits are a party now: no longer reserved, but still the raid's (raid_party).
                    foreach (GameBot bot in members)
                    {
                        if (raid != null && Raids.ContainsKey(id) && bot.Group != null && Membership.GetValueOrDefault(bot.Group) == raid)
                            ClaimPartyLease(bot, raid, bot.Group);
                        else if (IsReserved(bot))
                            BotDutyLeases.Release(bot, eDutyLeaseKind.RealmRaid);
                    }
                }
            }
        }

        /// <summary>Keeps a party in its expedition when one member is released (unreachable hub route).</summary>
        public static void ReleasePartyMember(Group group, GameBot bot)
        {
            if (group == null || bot == null) return;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Membership.TryGetValue(group, out Raid raid) || !raid.Parties.TryGetValue(group, out Party party)) return;
                party.Members = party.Members.Where(member => member != bot).ToArray();
                party.CatchingUp.Remove(bot.DatabaseID);
                raid.Released.Add(bot.DatabaseID);
                EndPartyLease(bot, raid.Definition.Id);
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Membership.TryGetValue(group, out Raid raid) || !raid.Parties.TryGetValue(group, out Party party) ||
                    !RealmRaidRecruitmentPolicy.NeedsReplacement(party.Target, party.Members.Length) || IsSpecialForcesParty(party) ||
                    RealmBots(raid) >= RealmRaidRecruitmentPolicy.MaximumBots) return false;
                party.Members = party.Members.Append(bot).ToArray();
                ClaimPartyLease(bot, raid, group);
                if (raid.HubDeparted || raid.Started) party.CatchingUp.Add(bot.DatabaseID);
                raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
                UpdateView(raid, party);
                return true;
            }
        }

        /// <summary>The raid's post for a new party, or why there is none (named in REALM_RAID_SPECIAL_FORCES_FAILED).</summary>
        public static bool TryGetForcedStaging(string id, bool specialForces, int joining,
            out AutonomousBotGroupCoordinator.SharedCamp camp, out string refusal)
        {
            camp = null;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Raids.TryGetValue(id, out var raid)) { refusal = "the raid is not running"; return false; }
                if ((refusal = PartyCapRefusal(raid, specialForces, joining)) != null) return false;
                if (!TryFreeSlot(raid, out int slot, out _, out var staging, specialForces))
                {
                    refusal = $"no free formation slot with a staging and hub post (parties={raid.Parties.Count} unusableSlots={raid.UnusableSlots.Count})";
                    // Every realm slot is taken or unusable: the forced raid is as large as its hub can hold.
                    if (raid.Forced && !specialForces && raid.ForcedParties.Count > 0)
                    {
                        TrimForcedToPosts(raid);
                        return false;
                    }
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

        /// <summary>
        /// A forced raid whose hub has no formation post left (RealmRaidRecruitmentPolicy.ForcedRequiredForPosts): its other
        /// reserved bots go back to their own play, and it stages what its posts hold. Called under the raid lock.
        /// </summary>
        private static void TrimForcedToPosts(Raid raid)
        {
            GameBot[] reserved = raid.ForcedParties.SelectMany(p => p).Distinct().ToArray();
            raid.ForcedParties.Clear();
            foreach (GameBot bot in reserved)
                EndReservation(bot, raid.Definition.Id);
            int was = raid.Required;
            raid.Required = RealmRaidRecruitmentPolicy.ForcedRequiredForPosts(raid.Required, RealmBots(raid));
            Log.Warn($"REALM_RAID_FORCED_TRIMMED event={raid.Definition.Id} hub=\"{raid.Hub.Name}\" parties={RealmParties(raid)} " +
                     $"posted={RealmBots(raid)} unusableSlots={raid.UnusableSlots.Count} required={raid.Required} was={was} " +
                     $"released={reserved.Length} reason=\"every formation post at the hub is taken; the other reserved bots go back to their own play\"");
        }

        public static bool CommitForcedParty(string id, Group group, out string refusal)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Raids.TryGetValue(id, out var raid)) { refusal = "the raid is not running"; return false; }
                if (Membership.ContainsKey(group)) { refusal = "the group is already in a raid"; return false; }
                var members = group.GetMembersInTheGroup().OfType<GameBot>().ToArray();
                // The Epic Special Forces come on top of the realm's own raiders: the 38-party and 300-bot limits are the
                // realm's, and they have three parties of their own (in-game check 2026-11-04: a full raid refused them).
                bool specialForces = members.Length > 0 && members.All(b => b.IsEpicSpecialForce);
                if ((refusal = PartyCapRefusal(raid, specialForces, members.Length)) != null) return false;
                if (members.Length is not (4 or 8)) { refusal = $"the group has {members.Length} bots, not 4 or 8"; return false; }
                if (members.Any(b => !IsEligible(b))) { refusal = "a member is not an eligible level 50"; return false; }
                if (members.Any(b => b.Realm != raid.Definition.Realm)) { refusal = "a member is of another realm"; return false; }
                if (members.Any(b => !IsReservedFor(b, id))) { refusal = "a member is not reserved for this raid"; return false; }
                if (!TryFreeSlot(raid, out int slot, out var staging, out var hubPost, specialForces))
                { refusal = $"no free formation slot with a staging and hub post (parties={raid.Parties.Count} unusableSlots={raid.UnusableSlots.Count})"; return false; }
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Raids.TryGetValue(id, out var raid) || raid.Hub == null) return false;
                region = raid.Hub.Region;
                point = raid.Hub.Center;
                return true;
            }
        }

        public static bool IsRunning(string id)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid)) return Raids.ContainsKey(id);
        }

        /// <summary>A Special Forces member is reserved for its raid like a recruit, so it can be formed into a party.</summary>
        public static void ReserveSpecialForce(GameBot bot, string id)
        {
            if (bot?.IsEpicSpecialForce != true) return;
            lock (Sync)
                if (Raids.TryGetValue(id, out Raid raid)) Reserve(bot, raid);
                else BotDutyLeases.Claim(bot, eDutyLeaseKind.RealmRaid, id, DutyLeaseReason.RaidReserved);
        }

        private static bool Available(string id) => !Raids.ContainsKey(id) &&
            Cooldowns.GetValueOrDefault(id) <= GameLoop.GameLoopTime && Up(Bosses.GetValueOrDefault(id)) &&
            StandingFinalsUp(id) &&
            (id != "epic-albion" || CaerSidiEncounterOpen(ApocInitializator.PickedTarget,
                (Bosses[id] as ApocInitializator)?.EncounterRespawnRemainingMilliseconds ?? 0,
                Fames.FamesIsUp || Bellum.BellumUP || Morbus.MorbusUP || Funus.FunusUp || Apocalypse.ApocUP));

        private static bool Up(GameNPC npc) => npc != null && npc.IsAlive && !npc.IsRespawning && npc.ObjectState == GameObject.eObjectState.Active;

        /// <summary>The finals in the world from the start (Tuscaran's King and Queen; Caer Sidi's Apocalypse and Galladoria's
        /// Olcasgean appear or wake only during the fight, so only the event's boss object is watched there).
        /// From Offline DAoC True 1.65 (shadowofze).</summary>
        public static string[] StandingFinals(Definition definition) => definition.Region == 160 ? definition.FinalTypes : [definition.BossType];

        // Tuscaran opened when the King was up although the Queen was respawning; the raid then killed him and waited for
        // her (True 1.65 epic-bosses T5). Every standing final must be up.
        private static bool StandingFinalsUp(string id)
        {
            Definition definition = Definitions.FirstOrDefault(d => d.Id == id);
            if (definition == null) return false;
            foreach (string type in StandingFinals(definition))
                if (type != definition.BossType && !Up(Bosses.GetValueOrDefault(id + "/" + type))) return false;
            return true;
        }

        /// <summary>
        /// Caer Sidi (True 1.65 epic-bosses B2): the event was refused while the encounter had picked its challenger, which
        /// stays set until 60 minutes after Apocalypse dies, so a raid that failed after waking the horsemen locked the event
        /// until a restart. A waiting or wounded horseman, or Apocalypse, is an ordinary target for the next raid; only the
        /// encounter's own respawn after Apocalypse died keeps it closed.
        /// </summary>
        public static bool CaerSidiEncounterOpen(bool challengerPicked, long encounterRespawnRemaining, bool anyEncounterUp) =>
            encounterRespawnRemaining <= 0 && (!challengerPicked || anyEncounterUp);

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
            Point3D home = LairHome(raid);
            Region region = WorldMgr.GetRegion(raid.Definition.Region);
            Zone zone = region?.GetZone(home.X, home.Y);
            Vector3 centre = new(home.X, home.Y, home.Z);
            // An open-world boss's ring (where the raid regroups) starts past its tether, aggro and roam, and no post stands in
            // the reach of the monsters around it (RealmRaidTetherReach; Marfoirwort, owner's PC 2026-11-16). If no such post
            // is found, the ring the raid always used.
            if (raid.Definition.IsOpenWorld && raid.Boss != null &&
                RealmRaidTetherReach.OpenWorldStagingInner(EpicEncounterTactics.LeashOf(raid.Boss)?.TetherRange is int t && t > 0 ? t : raid.Boss.TetherRange,
                    (raid.Boss.Brain as StandardMobBrain)?.AggroRange ?? 0, Math.Max(0, raid.Boss.RoamingRange)) is float inner &&
                inner > RealmRaidTetherReach.DefaultStagingInner)
            {
                raid.RingMonsters ??= RingMonsters(region, raid.Boss, centre, inner + 3_000);
                if (RealmRaidStaging.TryDragonPost(nav, zone, centre, slot, raid.StagingPosts.Values.ToArray(), out staging, inner,
                        p => RealmRaidTetherReach.ClearOfMonsters(p, raid.RingMonsters)))
                {
                    raid.StagingPosts[slot] = staging;
                    if (slot == 0)
                        Log.Info($"REALM_RAID_STAGING_OUT_OF_REACH event={raid.Definition.Id} boss=\"{raid.Boss.Name}\" inner={(int)inner} " +
                                 $"post={(int)staging.X},{(int)staging.Y},{(int)staging.Z} fromSpawn={(int)Vector3.Distance(staging, centre)} " +
                                 $"monsters={raid.RingMonsters.Length} reason=\"the ring the raid regroups on is out of the boss's reach and its neighbours'\"");
                    return true;
                }
                Log.Info($"REALM_RAID_STAGING_IN_REACH event={raid.Definition.Id} boss=\"{raid.Boss.Name}\" slot={slot} inner={(int)inner} " +
                         $"reason=\"no post out of the boss's reach; the 3,600 ring is used for this slot\"");
            }
            if (RealmRaidStaging.TryDragonPost(nav, zone, centre, slot, raid.StagingPosts.Values.ToArray(), out staging))
            {
                raid.StagingPosts[slot] = staging;
                return true;
            }
            return false;
        }

        /// <summary>The aggressive monsters around an open-world boss's ring (not the boss, not a realm's): no post stands in
        /// their aggro and roam (RealmRaidTetherReach.ClearOfMonsters).</summary>
        private static RealmRaidTetherReach.RingMonster[] RingMonsters(Region region, GameNPC boss, Vector3 centre, float within) =>
            region?.Objects.OfType<GameNPC>()
                .Where(n => n != null && n != boss && n is not GameBot && n.IsAlive && n.Realm == eRealm.None &&
                            n.Brain is StandardMobBrain { AggroLevel: > 0, AggroRange: > 0 } &&
                            Vector3.DistanceSquared(new(n.SpawnPoint.X, n.SpawnPoint.Y, n.SpawnPoint.Z), centre) < within * within)
                .Select(n => new RealmRaidTetherReach.RingMonster(new(n.SpawnPoint.X, n.SpawnPoint.Y, n.SpawnPoint.Z),
                    ((StandardMobBrain)n.Brain).AggroRange, Math.Max(0, n.RoamingRange)))
                .ToArray() ?? [];

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
            if (raid.DungeonRoute != null)
            {
                // The gate on the way while one is fought (RealmRaidRouteGate), else the raid's own dungeon.
                RealmRaidDungeonRoute route = ActiveRoute(raid);
                // A frontier way in: the raid-wide post, where the released raiders stand together (ReturnPost).
                Vector3 rally = ReturnPost(raid, party);
                party.RallyView = route.Entrance == null ? null : new(raid.Definition.Id, "RAID — gathering at the way in to go back in together",
                    new($"realm-event-{raid.Definition.Id}", raid.Definition.Name, raid.Definition.Name, route.Entrance.SourceRegion,
                        (int)rally.X, (int)rally.Y, (int)rally.Z, false, false, 50), true);
                Vector3 destination = raid.Started ? route.Destination : party.Staging;
                ushort region = raid.Started ? route.Region : route.Entrance.SourceRegion;
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
                    if (region == route.Region && Vector3.DistanceSquared(destination, front) > QueuedApproachDistance * QueuedApproachDistance)
                        destination = front;
                }
                party.DestinationView = new(raid.Definition.Id, queued ? "RAID — waiting for a clear formation post" : raid.Started ? "RAID — " + route.Status : "Raid rally",
                    new($"realm-event-{raid.Definition.Id}", target, raid.Definition.Name, region,
                        (int)destination.X, (int)destination.Y, (int)destination.Z, region == route.Region, false,
                        raid.Started ? route.TargetLevel : 50), !raid.Started || route.Hold || queued);
                return;
            }
            // Walking back after a release while the raid gathers a return wave: wait at the party's staging post on the
            // ring and go back in with the others (PulseDragonReturnWave).
            party.RallyView = new(raid.Definition.Id, "RAID — gathering at the staging ring to go back in together",
                new($"realm-event-{raid.Definition.Id}", raid.Boss.Name, raid.Boss.CurrentZone?.Description ?? raid.Definition.Name,
                    raid.Definition.Region, (int)party.Staging.X, (int)party.Staging.Y, (int)party.Staging.Z, false, false, 50), true);
            bool landed = DragonLanded(raid);
            // After a wipe the raid regroups at its staging ring, outside the dragon's aggro, and goes in again together
            // (RealmRaidDragonFight, 2026-10-30).
            bool regrouping = Regrouping(raid);
            bool hold = !raid.Started || !landed || regrouping;
            // A dragon drawn out of its lair is immune there: the raid forms at the lair and draws it home
            // (Gjalpinulva, 2026-10-20: the raid's posts followed her out and she stayed at 100% for an hour).
            Vector3 point = hold ? party.Staging :
                DragonLairPlacement.IsOutOfLair(raid.Boss) && DragonLairPlacement.Home(DragonLairPlacement.DragonRealm(raid.Boss)) is { } lair
                    ? new(lair.X, lair.Y, lair.Z) : new(raid.Boss.X, raid.Boss.Y, raid.Boss.Z);
            if (!hold && !TryBattlePost(raid, party, point, raid.Definition.Region, out point))
            { point = party.Anchor ?? party.Staging; hold = true; }
            string state = !raid.Started ? !landed
                ? "Raid rally — dragon airborne" : "Raid rally"
                : regrouping ? "RAID — regrouping at the staging ring after a wipe"
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid)) return Membership.TryGetValue(group, out var raid) && raid.Parties.TryGetValue(group, out var party) ? party.View : null;
        }

        // A late or released member follows the current encounter, never an
        // obsolete town muster or a dead leader's old position.
        public static View GetTravelView(GameBot bot)
        {
            Group group = bot?.Group;
            if (group == null) return null;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Membership.TryGetValue(group, out var raid) || !raid.Parties.TryGetValue(group, out var party)) return null;
                // Released at the hub because enemies held a frontier raid's raid-wide post: it waits at its post there and
                // goes back with the others (PulseHubGroup), never alone through the frontier.
                if (raid.HubReturners.Contains(bot.DatabaseID) && raid.Hub != null)
                {
                    Vector3 wait = RealmRaidMuster.ReleasePoint(raid.Hub.Center, party.HubPost);
                    return new(raid.Definition.Id, "RAID — waiting at the hub to go back together",
                        new($"realm-event-{raid.Definition.Id}", raid.Definition.Name, raid.Hub.Name, raid.Hub.Region,
                            (int)wait.X, (int)wait.Y, (int)wait.Z, false, false, 50), true);
                }
                // Walking in with a return wave, or one of the main body sent back to meet it (RealmRaidReturnWave.WalkTogether).
                if (raid.Definition.IsDungeon && WalkView(raid, group, bot) is { } walking) return walking;
                // Walking back after a release while the raid gathers a return wave: it waits at its party's staging by the
                // way in, as it did before the raid went in, and enters with the others (RealmRaidReturnWave).
                if (raid.ReturnGathering && party.RallyView != null && (raid.Definition.IsDungeon
                        ? !InRaidDungeon(raid, bot.CurrentRegionID) && raid.BeenInside.Contains(bot.DatabaseID)
                        : raid.ReturningToDragon.Contains(bot.DatabaseID)))
                    return party.RallyView;
                return raid.HubDeparted && party.CatchingUp.Contains(bot.DatabaseID)
                    ? party.DestinationView ?? party.View : party.View;
            }
        }

        /// <summary>
        /// Under the raid lock, each pulse of a started dungeon raid: whether the raiders outside the dungeon gather at the
        /// way in or go in (RealmRaidReturnWave). Owner's PC, 2026-11-12: after a wipe the released raiders walked back in
        /// ones and twos through monsters that had come back, died or stalled on the way, and nobody reached the muster
        /// for 20 minutes (Nosdoden three times, Trollkarl Bonchar, High Lord Oro, Icelord Agmundr, Hurika, Lydsyg).
        /// </summary>
        private static void PulseReturnWave(Raid raid, long now)
        {
            RealmRaidDungeonRoute route = raid.DungeonRoute;
            PulseHubGroup(raid, now);
            if (!RealmRaidReturnWave.On || route.Entrance == null)
            {
                raid.ReturnGathering = false;
                route.ReturnGathering = false;
                return;
            }
            int outside = 0, waiting = 0, fallen = 0;
            bool walks = WalksTogether(raid);
            var waitingBots = new List<GameBot>();
            // Owner's PC, 2026-11-12 (build ff5a8bc): Summoner's Hall's raid started from its staging town, and two minutes
            // later every raider still walking to the far entrance was counted as "on the way back" and sent back to the town:
            // nobody had reached the dungeon 7 minutes in. Only raiders who have been inside count now.
            foreach (GameBot b in raid.Support.OfType<GameBot>())
            {
                // The dungeon walked through on the way (Hall of the Corrupt for Summoner's Hall) is inside too: a raider
                // crossing it is on its way in, never sent back to the way in to wait.
                if (b.IsAlive && InRaidDungeon(raid, b.CurrentRegionID))
                {
                    raid.BeenInside.Add(b.DatabaseID);
                    continue;
                }
                if (!raid.BeenInside.Contains(b.DatabaseID)) continue;
                if (!b.IsAlive) { fallen++; continue; }
                // Already walking in with the wave (on its way from the staging post to the door): not waiting for the next one.
                if (raid.Walk?.Members.Contains(b.DatabaseID) == true) continue;
                outside++;
                if (IsStaged(raid, b)) { waiting++; waitingBots.Add(b); }
            }
            raid.ReturnWaitingNow = walks ? waiting : 0;
            if (waiting == 0) raid.ReturnWaitingSince = 0;
            else if (raid.ReturnWaitingSince == 0) raid.ReturnWaitingSince = now;
            long waited = raid.ReturnWaitingSince == 0 ? 0 : now - raid.ReturnWaitingSince;
            string name = raid.Definition.Id;
            // Walking in together (2026-11-17), a few on their way back gather too instead of walking in one by one.
            RealmRaidReturnWave.Step step = walks
                ? RealmRaidReturnWave.NextWalking(outside, fallen, waiting, waited, now < raid.ReturnOpenUntil)
                : RealmRaidReturnWave.Next(outside, fallen, waiting, waited, now < raid.ReturnOpenUntil);
            switch (step)
            {
                case RealmRaidReturnWave.Step.Gather:
                    if (!raid.ReturnGathering)
                        Log.Info($"REALM_RAID_RETURN_GATHER event={name} outside={outside} fallen={fallen} waiting={waiting}");
                    raid.ReturnGathering = true;
                    break;
                case RealmRaidReturnWave.Step.Go:
                    raid.ReturnGathering = false;
                    raid.ReturnOpenUntil = now + RealmRaidReturnWave.OpenMilliseconds;
                    raid.ReturnWaitingSince = 0;
                    Log.Info($"REALM_RAID_RETURN_WAVE event={name} waiting={waiting} outside={outside} fallen={fallen} waited_s={waited / 1000}");
                    break;
                default:
                    raid.ReturnGathering = false;
                    break;
            }
            // The ones going in walk with the wave: a new one, or the one already walking (they follow it on the walk it cleared).
            if (walks && waitingBots.Count > 0 && now < raid.ReturnOpenUntil) JoinWalk(raid, waitingBots, now);
            // While raiders stand at the way in waiting for their wave, or a wave walks in making headway, the muster inside is
            // not "unreachable".
            route.ReturnGathering = raid.ReturnGathering && waiting > 0 ||
                                    raid.Walk != null && !RealmRaidReturnWave.Stuck(raid.Walk.LastMove, now);
            if (raid.Transit != null) raid.Transit.ReturnGathering = route.ReturnGathering;
        }

        /// <summary>
        /// Under the raid lock, each pulse of a started dungeon raid: the raiders released at the hub because enemies held a
        /// frontier raid's raid-wide post (RealmRaidReturnWave.HubGroup) wait there and go back together, then join the
        /// return wave at the way in. Owner's decision, 2026-11-15: never walking back alone (Pennine Mountains, build 1dc66b6:
        /// raiders released alone at the way in were picked off by enemy-realm bots two or three times in a row).
        /// </summary>
        private static void PulseHubGroup(Raid raid, long now)
        {
            if (raid.HubReturners.Count == 0) { raid.HubWaitingSince = 0; return; }
            GameBot[] bots = raid.Support.OfType<GameBot>().Where(b => raid.HubReturners.Contains(b.DatabaseID)).ToArray();
            // Gone from the raid: no longer waited for. One down again is not counted; its next release decides again.
            raid.HubReturners.RemoveWhere(id => !bots.Any(b => b.DatabaseID == id));
            if (!RealmRaidReturnWave.On || raid.Hub == null) { raid.HubReturners.Clear(); raid.HubWaitingSince = 0; return; }
            GameBot[] returners = bots.Where(b => b.IsAlive).ToArray();
            GameBot[] atHub = returners.Where(b => b.CurrentRegionID == raid.Hub.Region && AtHubPost(raid, b)).ToArray();
            if (atHub.Length == 0) raid.HubWaitingSince = 0;
            else if (raid.HubWaitingSince == 0) raid.HubWaitingSince = now;
            long waited = raid.HubWaitingSince == 0 ? 0 : now - raid.HubWaitingSince;
            if (RealmRaidReturnWave.HubGroup(returners.Length, atHub.Length, waited) != RealmRaidReturnWave.Step.Go) return;
            foreach (GameBot b in atHub) raid.HubReturners.Remove(b.DatabaseID);
            raid.HubWaitingSince = 0;
            Log.Info($"REALM_RAID_HUB_GROUP event={raid.Definition.Id} going={atHub.Length} returners={returners.Length} " +
                     $"waited_s={waited / 1000} hub=\"{raid.Hub.Name}\"");
        }

        private static bool AtHubPost(Raid raid, GameBot bot)
        {
            Group group = bot.Group;
            Vector3 post = RealmRaidMuster.ReleasePoint(raid.Hub.Center,
                group != null && raid.Parties.TryGetValue(group, out Party party) ? party.HubPost : default);
            return bot.IsWithinRadius(new Point3D((int)post.X, (int)post.Y, (int)post.Z), 600);
        }

        /// <summary>
        /// Under the raid lock, each pulse of a started dragon raid: the return wave of RealmRaidReturnWave for a dragon (from
        /// zomboden/OfflineDAoC-True165's AutonomousRealmRaid.Reinforcements.cs, adapted). A dragon raider who falls releases to
        /// a bind in the dragon's region and walked back alone into the fight; now, like a dungeon raider, it waits at its
        /// party's staging post on the ring and goes back in with a wave. Never while the raid regroups after a wipe (the
        /// regroup gathers everyone at the ring already).
        /// </summary>
        private static void PulseDragonReturnWave(Raid raid, long now)
        {
            if (!RealmRaidReturnWave.On || Regrouping(raid))
            {
                raid.ReturnGathering = false;
                raid.ReturningToDragon.Clear();
                return;
            }
            Point3D home = DragonLairPlacement.Home(raid.Definition.Realm);
            int outside = 0, waiting = 0, fallen = 0;
            foreach (GameBot b in raid.Support.OfType<GameBot>())
            {
                long id = b.DatabaseID;
                bool inRegion = b.CurrentRegionID == raid.Definition.Region;
                float lair = inRegion ? Vector2.Distance(new(b.X, b.Y), new(home.X, home.Y)) : float.MaxValue;
                switch (RealmRaidReturnWave.ClassifyDragonRaider(b.IsAlive, raid.BeenInside.Contains(id),
                            raid.ReturningToDragon.Contains(id), lair))
                {
                    case RealmRaidReturnWave.DragonRaider.WithTheRaid:
                        raid.BeenInside.Add(id);
                        break;
                    case RealmRaidReturnWave.DragonRaider.Fallen:
                        raid.ReturningToDragon.Add(id);
                        fallen++;
                        break;
                    case RealmRaidReturnWave.DragonRaider.Returning:
                        raid.ReturningToDragon.Add(id);
                        outside++;
                        if (IsStaged(raid, b)) waiting++;
                        break;
                }
            }
            if (waiting == 0) raid.ReturnWaitingSince = 0;
            else if (raid.ReturnWaitingSince == 0) raid.ReturnWaitingSince = now;
            long waited = raid.ReturnWaitingSince == 0 ? 0 : now - raid.ReturnWaitingSince;
            RealmRaidReturnWave.Step step = RealmRaidReturnWave.Next(outside, fallen, waiting, waited, now < raid.ReturnOpenUntil);
            if (step == RealmRaidReturnWave.Step.Gather)
            {
                if (!raid.ReturnGathering)
                    Log.Info($"REALM_RAID_RETURN_GATHER event={raid.Definition.Id} dragon=true outside={outside} fallen={fallen} waiting={waiting}");
                raid.ReturnGathering = true;
                return;
            }
            if (step == RealmRaidReturnWave.Step.Go)
            {
                raid.ReturnOpenUntil = now + RealmRaidReturnWave.OpenMilliseconds;
                raid.ReturnWaitingSince = 0;
                Log.Info($"REALM_RAID_RETURN_WAVE event={raid.Definition.Id} dragon=true waiting={waiting} outside={outside} fallen={fallen} waited_s={waited / 1000}");
            }
            // Gone in (a wave, too few to wait for, or the wave is still open): the living walk to the dragon as before;
            // those still down are on their way back when they stand up.
            raid.ReturnGathering = false;
            raid.ReturningToDragon.RemoveWhere(id => raid.Support.OfType<GameBot>().Any(b => b.DatabaseID == id && b.IsAlive));
        }

        private static RealmRaidDungeonRoute ActiveRoute(Raid raid) => raid.Transit ?? raid.DungeonRoute;

        /// <summary>
        /// Under the raid lock, each pulse of a started dungeon raid: the route the raid follows now. A gate (a living boss on
        /// the walk through another dungeon, RealmRaidRouteGate) is looked for every minute while most of the raid has not
        /// gone past it; while one stands, the raid fights it there, and once it is dead (or it can't be reached) the raid
        /// goes on to its own dungeon.
        /// </summary>
        private static RealmRaidDungeonRoute PulseRouteGate(Raid raid, long now)
        {
            RealmRaidDungeonRoute main = raid.DungeonRoute;
            if (raid.Transit is { } gate)
            {
                gate.ObserveCompletion();
                if (RealmRaidRouteGate.On && !gate.Complete && !gate.Blocked) return gate;
                raid.GateNamed += gate.NamedDefeatedCount;
                raid.GateWipes += gate.WipesSoFar;
                raid.Transit = null;
                raid.NextGateCheck = now + (gate.Complete ? 0 : RealmRaidRouteGate.SetAsideMilliseconds);
                main.Resumed(now);
                Log.Warn($"REALM_EXPEDITION_ROUTE_GATE_{(gate.Complete ? "CLEARED" : "SET_ASIDE")} event={raid.Definition.Id} region={gate.Region} " +
                         $"bosses=\"{string.Join(",", gate.Gates.Select(g => $"{g.Name}:{(g.IsAlive ? g.HealthPercent + "%" : "dead")}"))}\" " +
                         $"wipes={gate.WipesSoFar} reason=\"{(gate.Complete ? "the way is clear; the raid goes on" : !RealmRaidRouteGate.On ? "route gates switched off" : "the gate could not be reached; the raid goes on and looks again later")}\"");
            }
            if (!RealmRaidRouteGate.On || now < raid.NextGateCheck) return main;
            raid.NextGateCheck = now + RealmRaidRouteGate.CheckMilliseconds;
            int living = raid.Support.Count(b => b.IsAlive);
            int past = raid.Support.Count(b => b.IsAlive && b.CurrentRegionID == raid.Definition.Region);
            if (!RealmRaidRouteGate.RaidStillBeforeIt(past, living) ||
                !RealmRaidDungeonRoute.TryCreateGate(main, out RealmRaidDungeonRoute created, out string detail))
                return main;
            raid.Transit = created;
            Log.Warn($"REALM_EXPEDITION_ROUTE_GATE event={raid.Definition.Id} region={created.Region} into={main.Region} living={living} " +
                     $"past={past} {detail} reason=\"a living boss covers the walk to the way in and there is no way round; the raid " +
                     $"musters for it and fights it there before it goes on\"");
            return created;
        }

        /// <summary>The raid's dungeon, or the dungeon its way in leads through (Summoner's Hall's Hall of the Corrupt, Dodens
        /// Gruva or Marfach Caverns; Crystal Cave's Avalon City).</summary>
        private static bool InRaidDungeon(Raid raid, ushort region) => region == raid.Definition.Region ||
            raid.DungeonRoute?.Entrance is { } edge && edge.TargetRegion == region;

        /// <summary>
        /// Whether <paramref name="region"/> is the dungeon the bot's raid walks through on its way in (Dodens Gruva, Hall of
        /// the Corrupt or Marfach Caverns for Summoner's Hall; Avalon City for Crystal Cave), not the raid's own dungeon.
        /// Owner's PC, 2026-11-14 (build e3b8238): after the Roesia wipe the Midgard raiders walking back through Dodens Gruva
        /// stood for up to 15 minutes before ordinary monsters there (cavernous yeti, decayed dire wolf, icebound moribund:
        /// AUTONOMOUS_DUNGEON_HOLD_LONG mayPull=False raidHold=True), because only the raid's own dungeon let a held raid
        /// clear its walk. Its ordinary monsters are now the raid's walk too (AutonomousDungeonTraversal); its epic monsters
        /// are not (they are route gates, RealmRaidRouteGate).
        /// </summary>
        public static bool IsRaidApproachDungeon(GameBot bot, ushort region)
        {
            Group group = bot?.Group;
            if (group == null) return false;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                return Membership.TryGetValue(group, out var raid) && region != raid.Definition.Region && InRaidDungeon(raid, region);
        }

        /// <summary>
        /// Where a raid member that has not moved for 15 minutes inside its raid's dungeon (or the one its way in leads
        /// through) is put back (AutonomousStuckWatchdog): where the living raider nearest the route's front stands, or the
        /// front itself. Owner's PC, 2026-11-11 (build e90bf21, Krondon): 26 "no movement for fifteen minutes" recoveries came from
        /// the entrance ogre ceorls' camp (x 31,200-31,720, y 34,750-35,560, z 15,130-15,310: the same spots 31533,35487 and
        /// 31285,35307 again and again, route traces PartialPathFound nodes=0); each sent the raider to Camelot, it walked
        /// back for 30 minutes and stood in the same spot again, so the orbs never had the raid. False when the bot is in no
        /// started dungeon raid or not in its dungeon (then the capital, as before).
        /// </summary>
        public static bool TryRecoverInsideRaid(GameBot bot, out ushort region, out Vector3 point, out string near)
        {
            region = 0; point = default; near = null;
            Group group = bot?.Group;
            if (group == null) return false;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Membership.TryGetValue(group, out var raid) || !raid.Started || raid.DungeonRoute == null) return false;
                RealmRaidDungeonRoute route = ActiveRoute(raid);
                if (bot.CurrentRegionID != route.Region) return false;
                Vector3 front = route.Front;
                GameLiving[] others = raid.Support.Where(b => b != bot && b.IsAlive && b.CurrentRegionID == route.Region &&
                    b.ObjectState == GameObject.eObjectState.Active).ToArray();
                if (RealmRaidStuckRecovery.Choose(new Vector3(bot.X, bot.Y, bot.Z), front,
                        others.Select(b => new Vector3(b.X, b.Y, b.Z)).ToArray(), out point, out int index))
                {
                    region = route.Region;
                    near = index >= 0 ? others[index].Name : "the route's front";
                    return true;
                }
                return false;
            }
        }

        // Late/released members skip muster attendance, not the connected exterior route.
        // Navigation is performed by their controller outside the expedition lock.
        public static bool TryIndependentApproach(GameBot bot, out Vector3 destination, out Vector3? via)
        {
            destination = default; via = null;
            Group group = bot?.Group;
            if (group == null) return false;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Membership.TryGetValue(group, out var raid) || !raid.Parties.TryGetValue(group, out var party) ||
                    !raid.HubDeparted || bot.CurrentRegionID != raid.Hub.Region ||
                    !(raid.Started || party.CatchingUp.Contains(bot.DatabaseID))) return false;
                // Released at the hub because enemies held the raid-wide post: it waits at the hub for the others (GetTravelView).
                if (raid.HubReturners.Contains(bot.DatabaseID)) return false;
                destination = raid.DungeonRoute != null ? raid.Started ? ReturnPost(raid, party) : party.Staging :
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
            string id;
            ushort stagingRegion = 0;
            Vector3 staging = default, hub;
            Vector3? raidPost;
            bool partyHasStaging, frontier;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Membership.TryGetValue(group, out var raid) || raid.DungeonRoute == null || raid.Hub == null) return false;
                id = raid.Definition.Id;
                raid.Parties.TryGetValue(group, out Party party);
                partyHasStaging = party != null && party.Staging != default && raid.DungeonRoute.Entrance != null;
                if (partyHasStaging)
                {
                    stagingRegion = raid.DungeonRoute.Entrance.SourceRegion;
                    staging = party.Staging;
                }
                frontier = partyHasStaging && InFrontier(stagingRegion, staging);
                raidPost = frontier ? RaidWidePost(raid) : null;
                // The hub: the party's own post there (proven to lead to the hub and, since build 4, from it:
                // RealmRaidMuster.HubReachesPost), not on the hub's centre, where Snowdonia Station's 12 failed raiders stood
                // (PC 2026-11-14). The centre only when the party has no post.
                region = raid.Hub.Region;
                hub = RealmRaidMuster.ReleasePoint(raid.Hub.Center, party?.HubPost ?? default);
            }
            // Enemies at the raid-wide post are looked for outside the raid lock (a spatial scan of the region).
            int enemies = raidPost.HasValue ? EnemiesNear(bot.Realm, stagingRegion, raidPost.Value) : 0;
            RealmRaidReturnWave.ReleasePost choice = RealmRaidReturnWave.ChooseReleasePost(RealmRaidReturnWave.ReleaseAtStaging,
                true, partyHasStaging, RealmRaidReturnWave.FrontierRaidPost, frontier, raidPost.HasValue, enemies, out string reason);
            Vector3 release = choice switch
            {
                // One raid-wide post when the way in lies in the frontier, so the released raiders stand together.
                RealmRaidReturnWave.ReleasePost.Raid => raidPost.Value,
                // The party's staging post by the way in, where the return wave gathers (RealmRaidReturnWave).
                RealmRaidReturnWave.ReleasePost.Party => staging,
                _ => hub,
            };
            if (choice != RealmRaidReturnWave.ReleasePost.Hub) region = stagingRegion;
            point = new Point3D((int)release.X, (int)release.Y, (int)release.Z);
            // Released at the hub because enemies held the raid-wide post: it waits there for the others and goes back with
            // them (PulseHubGroup), never alone through the frontier.
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                if (Membership.TryGetValue(group, out var raid))
                {
                    if (choice == RealmRaidReturnWave.ReleasePost.Hub && enemies > 0) raid.HubReturners.Add(bot.DatabaseID);
                    else raid.HubReturners.Remove(bot.DatabaseID);
                }
            // The party post outside the frontier is today's release: only the frontier's choices are logged.
            if (frontier)
                Log.Info($"REALM_RAID_RELEASE_POST event={id} bot={bot.Name} id={bot.DatabaseID} " +
                         $"post={choice.ToString().ToLowerInvariant()} reason={reason} enemies={enemies} region={region} " +
                         $"at={point.X},{point.Y},{point.Z}");
            return true;
        }

        /// <summary>A point in an RvR/frontier zone (Pennine Mountains, Hadrian's Wall, ...) or an RvR region.</summary>
        private static bool InFrontier(ushort regionId, Vector3 point)
        {
            Region region = WorldMgr.GetRegion(regionId);
            return region?.IsRvR == true || region?.GetZone((int)point.X, (int)point.Y)?.IsRvR == true;
        }

        /// <summary>Under the raid lock: the raid-wide post (the staging post nearest the middle of them all), chosen once.</summary>
        private static Vector3? RaidWidePost(Raid raid)
        {
            if (raid.RaidPost == null)
            {
                Vector3[] posts = raid.StagingPosts.OrderBy(p => p.Key).Select(p => p.Value).ToArray();
                int index = RealmRaidReturnWave.RaidPost(posts);
                if (index >= 0) raid.RaidPost = posts[index];
            }
            return raid.RaidPost;
        }

        /// <summary>Under the raid lock: where a frontier raid's raiders gather to go back in (the raid-wide post), else the
        /// party's own staging post.</summary>
        private static Vector3 ReturnPost(Raid raid, Party party) =>
            RealmRaidReturnWave.FrontierRaidPost && party.Staging != default && raid.DungeonRoute?.Entrance is { } edge &&
            InFrontier(edge.SourceRegion, party.Staging) && RaidWidePost(raid) is { } post ? post : party.Staging;

        /// <summary>Enemy-realm players, bots, pets and guards (not peaceful NPCs) within the hold radius of a post.</summary>
        private static int EnemiesNear(eRealm realm, ushort region, Vector3 post)
        {
            int count = 0;
            ushort radius = RealmRaidReturnWave.RaidPostHeldRadius;
            foreach (GamePlayer p in WorldMgr.GetPlayersCloseToSpot(region, (int)post.X, (int)post.Y, (int)post.Z, radius))
                if (p.IsAlive && p.Realm != eRealm.None && p.Realm != realm) count++;
            foreach (GameNPC n in WorldMgr.GetNPCsCloseToSpot(region, (int)post.X, (int)post.Y, (int)post.Z, radius))
                if (n.IsAlive && n.ObjectState == GameObject.eObjectState.Active && n.Realm != eRealm.None && n.Realm != realm &&
                    (n.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0)
                    count++;
            return count;
        }

        public static void RejoinAfterRelease(GameBot bot)
        {
            Group group = bot?.Group;
            if (group == null) return;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                if (Membership.TryGetValue(group, out var raid) && raid.Parties.TryGetValue(group, out var party))
                    party.CorpseSince.Remove(bot.DatabaseID);
        }

        public static AutonomousBotGroupCoordinator.PveCorpseDisposition CorpseRecovery(GameBot bot)
        {
            Group group = bot?.Group;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (group == null || !Membership.TryGetValue(group, out var raid) ||
                    !raid.Parties.TryGetValue(group, out var party))
                    return AutonomousBotGroupCoordinator.PveCorpseDisposition.NotManaged;
                long now = GameLoop.GameLoopTime;
                if (!party.CorpseSince.TryGetValue(bot.DatabaseID, out long since))
                    party.CorpseSince[bot.DatabaseID] = since = now;
                // Bounded per-corpse wait, even during an endless nearby fight.
                // Shared resurrection claims still select one safe caster.
                // After a wipe nobody is left near to raise it: it releases soon and goes back with the return wave.
                bool raiderNear = raid.Support.Any(b => b != bot && b.IsAlive && b.CurrentRegionID == bot.CurrentRegionID &&
                    b.IsWithinRadius(bot, RealmRaidReturnWave.RescuerRadius));
                long deadline = since + RealmRaidReturnWave.CorpseWait(raiderNear);
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                if (group != null && Membership.TryGetValue(group, out var raid)) return raid;
            return RealmWarbandSupport.Get(bot)?.Scope ?? bot?.Group;
        }

        public static bool HasSharedSupport(GameBot bot) => GetView(bot?.Group) != null || RealmWarbandSupport.Get(bot) != null;

        /// <summary>
        /// The high-end raid encounters whose loot a player in the encounter shares (owner 2026-10-31): the named epic
        /// encounters, and each raid's own boss (the dragon) or final dungeon bosses. Trash on the way is not one.
        /// </summary>
        public static bool IsHighEndRaidEncounter(GameNPC victim)
        {
            if (victim is IGameEpicNpc) return true;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                return CompletedLoot.ContainsKey(victim) || Raids.Values.Any(r => ReferenceEquals(r.Boss, victim) ||
                    r.DungeonRoute?.FinalBosses.Contains(victim) == true);
        }

        public static IGameStaticItemOwner LootOwner(GameNPC victim, IEnumerable<GameBot> contributors) =>
            LootOwner(victim, contributors.Where(b => b != null).Distinct().ToDictionary(b => b, _ => 1.0));

        /// <summary>
        /// The expedition that did the most damage gets the drop. In a contested neutral dungeon two realms' raids can both
        /// be on the final encounter; only the one credited here wins it (2026-10-20: Hibernia and Midgard were both in
        /// Summoner's Hall, every Govannon drop went to Hibernians and both raids ended "Boss defeated").
        /// </summary>
        public static IGameStaticItemOwner LootOwner(GameNPC victim, IReadOnlyDictionary<GameBot, double> damageByBot)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                GameBot[] damaging = damageByBot.Keys.ToArray();
                Raid raid = damageByBot.Where(p => p.Key?.Group != null)
                    .Select(p => (Raid: Membership.GetValueOrDefault(p.Key.Group), Damage: p.Value))
                    .Where(p => p.Raid != null && p.Raid.Started && InRaidDungeon(p.Raid, victim.CurrentRegionID))
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid)) return first.Group != null && second.Group != null &&
                Membership.TryGetValue(first.Group, out var raid) && Membership.GetValueOrDefault(second.Group) == raid;
        }

        public static bool Protects(GameBot bot)
        {
            if (bot?.IsAlive != true || !bot.IsAutonomousWorldBot) return false;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                if(bot.Group != null && Membership.TryGetValue(bot.Group,out var raid) &&
                    raid.HubDeparted && !raid.Started && !bot.IsOnStableMasterRoute &&
                    bot.CurrentRegionID == (raid.Definition.IsDungeon ? raid.DungeonRoute.Entrance.SourceRegion : raid.Definition.Region) &&
                    AtStaging(raid,bot)) return true;
            // A started dungeon expedition fights wherever its route leads (a boss
            // room can be far from the next route point). Real combat inside the
            // expedition's own dungeon is never a 15-minute "no movement" stall.
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                if (bot.InCombat && bot.Group != null && Membership.TryGetValue(bot.Group, out var raid) &&
                    raid.Started && raid.DungeonRoute != null && InRaidDungeon(raid, bot.CurrentRegionID))
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Membership.TryGetValue(bot.Group, out var raid) || raid.DungeonRoute == null || !raid.Started)
                    return false;
                // In the dungeon of a gate being fought on the way, that route's front (RealmRaidRouteGate).
                RealmRaidDungeonRoute route = raid.Transit is { } gate && bot.CurrentRegionID == gate.Region ? gate
                    : bot.CurrentRegionID == raid.Definition.Region ? raid.DungeonRoute : null;
                if (route == null) return false;
                region = route.Region;
                point = route.Front;
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
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                Released.Remove(group);
                if (Membership.Remove(group, out Raid raid))
                {
                    DefenseGroups.TryRemove(group, out _);
                    if (raid.Parties.TryGetValue(group, out Party leaving))
                        foreach (GameBot member in leaving.Members) EndPartyLease(member, raid.Definition.Id);
                    raid.Parties.Remove(group);
                    raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
                }
            }
        }

        public static bool TryConsumeRelease(Group group, out string reason)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid)) return Released.Remove(group, out reason);
        }

        private static void End(Raid raid, long now, string reason)
        {
            bool stoppedByPlayer = !SetsCooldown(reason);
            string outcome = StopOutcome(reason) ?? (
                reason.StartsWith("Route blocked") ? "Route blocked" :
                reason.StartsWith("Staging failed") ? "Failed rally" : reason.Contains("four-hour") || reason.StartsWith("Stalled") ? "Timed out" :
                reason.StartsWith("Defeated at") ? "Defeated" : reason.StartsWith(BeatenPrefix) ? BeatenPrefix :
                raid.Definition.IsDungeon ? raid.DungeonRoute?.Complete == true ? "Boss defeated" : "Ended (unconfirmed)" :
                !raid.Boss.IsAlive ? "Boss defeated" : "Encounter unavailable");
            // Partial success: the named encounters the raid killed before it was stopped are part of its record.
            string[] named = raid.DungeonRoute?.NamedDefeated.ToArray() ?? [];
            if (outcome != "Boss defeated" && named.Length > 0)
                reason += $" Named encounters defeated: {string.Join(", ", named)}.";
            // A raid started from the launcher is marked in the ledger: its result is not part of the encounter's record
            // for raid sizing or the Special Forces (owner, 2026-11-04). So is a raid the Special Forces joined: a win they
            // took part in makes their next call-up here a party smaller.
            int specialForcesJoined = AutonomousEpicSpecialForces.Joined(raid.Definition.Id);
            // And the size it ran at, so the next raid here carries on from it (2026-11-05).
            double? sizePercent = raid.Sizing?.RecordedPercent;
            // And how many wipes it took: a time-out without one stays out of the record (owner, 2026-11-05).
            // And who led it, for the Herald's chronicle sentence (Living Server 1.2, LoreChronicle): the first party's leader.
            string raidLeader = raid.Parties.Keys.Select(g => g?.LivingLeader?.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            // And whether it ever had the bots it was sized for: a loss under strength is no result (owner, 2026-11-11).
            raid.MostRecruited = Math.Max(raid.MostRecruited, raid.Support.Length);
            bool underStrength = !raid.Forced && raid.Sizing != null &&
                RealmRaidRecruitmentPolicy.UnderStrength(raid.MostRecruited, raid.Required);
            string details = RealmEventRecordStore.WithWipes(raid.Forced ? RealmEventRecordStore.ForcedRaidPrefix + reason : reason, raid.Wipes);
            if (underStrength) details = RealmEventRecordStore.WithUnderStrength(details, raid.MostRecruited, raid.Required);
            RealmEventRecords.Finish(raid.Definition.Id, outcome, LoreChannels.LoreChronicle.WithRaidLeader(RealmEventRecordStore.WithRaidSize(RealmEventRecordStore.WithSpecialForces(
                details, specialForcesJoined), sizePercent), raidLeader), raid.Support.Length);
            if (outcome == "Boss defeated") LastSuccessful[raid.Definition.Realm] = raid.Definition.Id;
            AutonomousEpicSpecialForces.RaidEnded(raid.Definition.Id,
                RealmEventRecordStore.RecordOutcome(AutonomousEpicSpecialForces.RecordOutcome(outcome, raid.Wipes), underStrength),
                raid.Forced, sizePercent);
            Log.Info($"REALM_RAID_ENDED event={raid.Definition.Id} outcome=\"{outcome}\" support={raid.Support.Length} required={raid.Required} " +
                     $"mostRecruited={raid.MostRecruited} underStrength={underStrength} " +
                     $"forced={raid.Forced} minutes={(now - raid.Created) / 60_000} named={named.Length} " +
                     $"specialForces={AutonomousEpicSpecialForces.Count(raid.Definition.Id)} specialForcesJoined={specialForcesJoined} wipes={raid.Wipes} reason=\"{reason}\"");
            // The encounter that beat the raid: a route gate's (Aidon in Hall of the Corrupt), its own dungeon's or the dragon.
            string defeatedBy = DefeatedBy(raid.Transit?.DefeatedAt, raid.DungeonRoute?.DefeatedAt, raid.DragonFight?.DefeatedAt,
                FinalBossName(raid), raid.Definition.BossType);
            if (outcome == "Defeated")
                GameWideAnnouncements.Queue(AnnouncementKind.PveRealmEvent, GameWideAnnouncements.RaidDefeated(raid.Definition.Realm,
                    defeatedBy, raid.Definition.Name));
            // Owner 2026-10-07: every realm hears when a raid (automatic or forced) brings down the final boss of an
            // epic dungeon, Summoner's Hall or Darkness Falls.
            if (outcome == "Boss defeated" && raid.Definition.IsDungeon)
            {
                GameNPC finalBoss = (raid.DungeonRoute?.FinalBosses ?? [raid.Boss]).FirstOrDefault(n => n != null && !n.IsAlive);
                GameWideAnnouncements.Queue(AnnouncementKind.PveRealmEvent, GameWideAnnouncements.FinalBossDefeated(raid.Definition.Realm,
                    finalBoss?.Name ?? Readable(raid.Definition.BossType), raid.Definition.Name));
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
            BotDutyLeases.ReleaseOwner(eDutyLeaseKind.RealmRaid, raid.Definition.Id);
            // A player stop or a supersede is not a failed attempt: no cooldown, so it can be restarted at once.
            if (!stoppedByPlayer)
                Cooldowns[raid.Definition.Id] = now + (30 + Random.Shared.Next(61)) * 60_000L;
            // The talking update: a win or a defeat is news the realm's bots tell and talk about.
            // A defeat is told with the encounter that beat the raid (Aidon, not Govannon, for a raid lost on its way in).
            string bossName = outcome == "Defeated" ? defeatedBy : FinalBossName(raid) ?? Readable(raid.Definition.BossType);
            string newsTopic = outcome == "Boss defeated" ? "raid_won" : outcome == "Defeated" ? "raid_wiped" : null;
            if (newsTopic != null)
                AutonomousChatNews.RaidOutcome(raid.Definition.Realm, raid.Definition.Name, bossName, newsTopic == "raid_won");
            RealmEventNotices.Queue(raid.Definition.Id, raid.Definition.Realm, RealmEventBanter.RaidOutcome(raid.Definition.Name, outcome),
                newsTopic, RaidSlots(raid.Definition.Name, bossName));
            // Living Server 1.2: a veteran's tale of the win or the loss, then a lore line about the place (docs/LORE.md).
            if (newsTopic != null)
                LoreChat.Aftermath(recipients, raid.Definition.BossType, raid.Definition.Region, newsTopic == "raid_won");
            string leeroyName = raid.LeeroyName ?? raid.DungeonRoute?.LeeroyName;
            if (leeroyName != null && newsTopic != null)
                // The raiders as they were (raid.Support is emptied above when the parties are released: in-game check
                // 2026-10-29, Golestandt fell after a Leeroy and nobody said a word about him).
                AutonomousRaidLeeroy.AfterRaid(raid.Definition.Realm, leeroyName, bossName, newsTopic == "raid_won", recipients);
        }

        /// <summary>The display name of the raid's final boss (its dungeon's first final, or the raid's boss), or null.</summary>
        private static string FinalBossName(Raid raid) =>
            (raid.DungeonRoute?.FinalBosses ?? [raid.Boss]).FirstOrDefault(n => n != null && !string.IsNullOrWhiteSpace(n.Name))?.Name ??
            (string.IsNullOrWhiteSpace(raid.Boss?.Name) ? null : raid.Boss.Name);

        /// <summary>
        /// The name a defeat is announced with (pure): the encounter that beat the raid, a route gate's first (it is fought on
        /// the way in, before the raid's own dungeon), then the raid's own dungeon route's, then the dragon's; with none, the
        /// final boss's display name; with no display name either, its class name made readable. Owner's PC, 2026-11-14
        /// (build 1dc66b6): Albion's Summoner's Hall raid lost twice at Aidon the Archwizard in Hall of the Corrupt, a route
        /// gate, and every player read "Albion's expedition has been driven out of Summoner's Hall by GrandSummonerGovannon!":
        /// only the main route's defeat was read, and the fallback was the final boss's C# class name.
        /// </summary>
        public static string DefeatedBy(string gateDefeatedAt, string routeDefeatedAt, string dragonDefeatedAt, string finalBossName,
            string bossType) =>
            new[] { gateDefeatedAt, routeDefeatedAt, dragonDefeatedAt, finalBossName }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ??
            Readable(bossType);

        /// <summary>A class name as words ("GrandSummonerGovannon" -> "Grand Summoner Govannon"); anything else unchanged.</summary>
        public static string Readable(string typeName)
        {
            if (string.IsNullOrEmpty(typeName) || typeName.Contains(' ')) return typeName;
            var words = new System.Text.StringBuilder(typeName.Length + 8);
            for (int i = 0; i < typeName.Length; i++)
            {
                char c = typeName[i];
                if (i > 0 && char.IsUpper(c) && (char.IsLower(typeName[i - 1]) ||
                                                 i + 1 < typeName.Length && char.IsLower(typeName[i + 1]) && char.IsUpper(typeName[i - 1])))
                    words.Append(' ');
                words.Append(c);
            }
            return words.ToString();
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

        /// <summary>
        /// The reason a forced expedition gives the realm's own raid it ends (SupersedeAutomatic). Owner's PC, 2026-11-17
        /// (build ccba364, /epictest sweep): the realm's own Dodens Gruva raid, called at 01:12:54, ended at 01:15:55 as
        /// "Stopped by player" when the sweep started its forced Summoner's Hall raid, although nobody pressed Stop. A
        /// supersede is now its own outcome, "Superseded" (the word a superseded siege rally already uses); like a stop it
        /// sets no cooldown and is no result for the raid's record.
        /// </summary>
        public static string SupersededReason(string forcedId) => $"{SupersededPrefix} by the forced {forcedId} expedition.";
        private const string SupersededPrefix = "Superseded";

        /// <summary>The outcome of a raid ended by the player or superseded by a forced expedition (pure), or null when
        /// <paramref name="reason"/> is another ending.</summary>
        public static string StopOutcome(string reason) =>
            reason.StartsWith(StoppedByPlayer, StringComparison.Ordinal) ? "Stopped by player" :
            reason.StartsWith(SupersededPrefix, StringComparison.Ordinal) ? "Superseded" : null;

        /// <summary>A raid that ends for <paramref name="reason"/> waits out a cooldown before the realm calls it again (pure):
        /// every ending but a player stop or a supersede.</summary>
        public static bool SetsCooldown(string reason) => StopOutcome(reason) == null;

        /// <summary>A named encounter (or the dragon, since 2026-10-30) wiped the raid: the realm hears it, the raid regroups
        /// for one more attempt, and the Epic Special Forces are asked for, sized by this raid's own record.</summary>
        private static void OnWipe(Raid raid, string boss, int failures, long now, int engaged = -1, int fallen = -1)
        {
            raid.Wipes++;
            Log.Info(WipeLine(raid.Definition.Id, boss, failures, raid.Support.Length, raid.Support.Count(b => b.IsAlive), engaged, fallen,
                (now - raid.Created) / 60_000));
            if (failures >= RealmRaidDungeonRoute.MaximumFailedAttempts) return;
            AutonomousChatNews.RaidOutcome(raid.Definition.Realm, raid.Definition.Name, boss, false);
            RealmEventNotices.Queue(raid.Definition.Id, raid.Definition.Realm, RealmEventBanter.RaidWipe(raid.Definition.Name, boss),
                "raid_wiped", RaidSlots(raid.Definition.Name, boss));
            AutonomousEpicSpecialForces.Request(raid.Definition.Id, raid.Definition.Name, raid.Definition.Realm, $"a wipe at {boss}");
        }

        /// <summary>
        /// The REALM_RAID_WIPE line (pure). <paramref name="living"/> is every living raider wherever it stands, raiders who
        /// released and stand at their staging post outside included; <paramref name="engaged"/> and <paramref name="fallen"/>
        /// are the raiders in the failed attempt and how many of them fell (-1: not known, a dragon's wipe). Owner's PC,
        /// 2026-11-14 (build 1dc66b6): "failures=2 support=80 living=76" read as a wipe scored while the raid stood; all 65
        /// raiders in that attempt at Aidon the Archwizard had fallen (REALM_EXPEDITION_WIPE engaged=65 fallen=65) and 54 of them
        /// were already back on their feet at the staging post outside.
        /// </summary>
        public static string WipeLine(string id, string boss, int failures, int support, int living, int engaged, int fallen, long minutes) =>
            $"REALM_RAID_WIPE event={id} boss=\"{boss}\" failures={failures} support={support} living={living} " +
            (engaged >= 0 ? $"engaged={engaged} fallen={fallen} " : "") + $"minutes={minutes}";

        // Runs 12 and 13 (2026-10-15/16): an automatic Darkness Falls or Summoner's Hall rally opened a minute before the
        // forced raid and held 140+ of the realm's level-50 bots, so forced Galladoria reserved only 288 of 300 and forced
        // Caer Sidi rallied 143 of the 200 it needs. A forced expedition ends the realm's automatic rallies that have not
        // started yet (no cooldown), the way a forced siege supersedes an unstarted automatic siege.
        private static void SupersedeAutomatic(eRealm realm, string forcedId)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                foreach (Raid automatic in Raids.Values.Where(r => r.Definition.Realm == realm && !r.Forced && !r.Started &&
                             r.Definition.Id != forcedId).ToArray())
                    End(automatic, GameLoop.GameLoopTime, SupersededReason(forcedId));
                // 1.2: so is the realm's own raid that still waits for its tale.
                if (PendingCalls.TryGetValue(realm, out PendingCall waiting) && !waiting.Forced && waiting.Id != forcedId)
                    CancelCall(waiting.Id, $"superseded by the forced {forcedId} expedition");
            }
        }

        /// <summary>Launcher "Stop event": ends an active dragon or epic dungeon expedition now.</summary>
        public static bool Stop(string id, out string reason)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!Raids.TryGetValue(id, out Raid raid))
                {
                    bool waiting = CancelCall(id, "stopped from the launcher");
                    reason = waiting ? "The raid was waiting for its tale and will not be called." : "This expedition is not running.";
                    return waiting;
                }
                End(raid, GameLoop.GameLoopTime, StoppedByPlayer + " from the launcher.");
                reason = $"{raid.Definition.Name} stopped. The bots return to their own goals; no cooldown was set.";
                return true;
            }
        }

        /// <summary>The encounter's boss for <paramref name="id"/> (null when it is not known): the play-test sweep keeps its
        /// guest where it is while it stands near the fight (EpicPlaytestSweep.TagAlong).</summary>
        public static GameNPC BossOf(string id)
        {
            if (id == null) return null;
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
                return Raids.TryGetValue(id, out var raid) && raid.Boss != null ? raid.Boss : Bosses.GetValueOrDefault(id);
        }

        public static bool ResetCooldown(string id)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid)) { if (Raids.ContainsKey(id)) return false; Cooldowns.Remove(id); return true; }
        }

        public static Summary[] Snapshot()
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid)) return Definitions.Select(d =>
            {
                if (Raids.TryGetValue(d.Id, out var r)) return new Summary(d.Id, d.Name, GlobalConstants.RealmToName(d.Realm),
                    r.Started ? "RAID — " + ((r.Transit ?? r.DungeonRoute)?.Status ?? r.DragonFight?.Status ?? (DragonLanded(r) ? "underway" : "waiting for dragon landing")) :
                    $"Raid rally — {(r.Forced ? "forced" : "automatic")} — {(r.HubDeparted ? "travel/final staging" : r.Hub.Name)}" +
                        (r.Forced ? $" — starts when {r.Required} are staged (fewer after {RealmRaidRecruitmentPolicy.ForcedPatienceMilliseconds / 60_000} minutes)" + (!DragonLanded(r) ? " and the dragon lands" : "") :
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
                foreach (string type in StandingFinals(d).Where(t => t != d.BossType))
                    respawn = Math.Max(respawn, Bosses.GetValueOrDefault(d.Id + "/" + type)?.RespawnRemainingMilliseconds ?? 0);
                long cooldown = Math.Max(0, Cooldowns.GetValueOrDefault(d.Id) - GameLoop.GameLoopTime);
                string state = Available(d.Id) ? "Available" : respawn > 0 ? "Respawning" : cooldown > 0 ? "Event cooldown" : "Encounter unavailable";
                return new Summary(d.Id, d.Name, GlobalConstants.RealmToName(d.Realm), state, 0, 0,
                    d.SuggestedBots, Math.Max(respawn, cooldown));
            }).ToArray();
        }
    }
}
