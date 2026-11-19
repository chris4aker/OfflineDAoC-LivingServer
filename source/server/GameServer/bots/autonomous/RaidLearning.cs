using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Raid learning from raiders (Bots as people WP2R, owner decision 1, 2026-11-17; raid_learns_from_raiders, default on).
/// The realm's record still says how well the realm knows a boss (<see cref="RaidDiscipline.FamiliarityOf"/>); now the raid
/// plays it that well only when enough of the raiders present have seen the mechanic before
/// (<see cref="RaidDiscipline.RosterFamiliarity(double, RaidDiscipline.RosterKnowledge)"/>), and each raider's own reaction
/// and lapse follow its own count (<see cref="RaidDiscipline.RaiderFamiliarity"/>). Each raider remembers the raids at
/// which it saw a mechanic in <c>offline_bot_mechanics_seen</c> (one row per person and mechanic, written only through
/// <see cref="BotPeopleStore.MechanicsSeen"/>, saved off the game loop).
/// <list type="bullet">
/// <item>Only in a running realm raid with no real player in its parties. A player's own group (no realm raid) and a
/// raid the player sits in keep the record's familiarity exactly, as before.</item>
/// <item>Raiders: saved autonomous gamebots (BotPeopleScope rule Record). The Epic Special Forces have no person and count as
/// having seen everything. Companions (hired ones too), /raid helpers and buffbots are not counted.</item>
/// <item>A raider is credited once per raid (the raid's instance: its id and when it was created, so two forced runs in one
/// session are two raids), at the first call of the mechanic it is present for; what it had seen before this raid is
/// what counts in this raid.</item>
/// <item>The /epictest familiarity override stands as set and credits nobody: a play test's forced familiarity is not a
/// raid the raiders learned from.</item>
/// <item>An upgraded world is seeded once, off the game loop, after the store and the record have loaded
/// (<see cref="Seed"/>); until then, and with the switch off, the familiarity is the record's exactly.</item>
/// </list>
/// Wired at one place, the stomp's call (EpicEncounterTactics.RaidCalls.cs: the call and the raider's reaction). Logs
/// RAID_LEARN (a raid's roster, when it changes: about one line a raid, with the newcomer's and the veteran's familiarity)
/// and RAID_MECHANIC_SEEN (raiders credited, and the seeding).
/// </summary>
public static class RaidLearning
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>A warning never breaks a raid or the seeding (a logger without its queue, in tests, throws).</summary>
    private static void Warn(string message, Exception exception = null)
    {
        try
        {
            if (exception == null) Log.Warn(message);
            else Log.Warn(message, exception);
        }
        catch (Exception)
        {
            // nothing to do: the state already says what happened
        }
    }

    /// <summary>Raiders of the raid's parties within this distance of the boss (same region, alive) are present.</summary>
    public const int PresentRange = 6_000;

    /// <summary>One raider as the learning sees it: its id, its person (none for an Epic Special Force) and whether it is one.</summary>
    public readonly record struct Learner(long Id, BotPersonKey Person, bool SpecialForce);

    /// <summary>What a party member is to the learning.</summary>
    public enum Seat
    {
        /// <summary>Not counted: a companion (hired too), a /raid helper, a buffbot, a test NPC.</summary>
        Skip,
        /// <summary>A saved autonomous gamebot: it remembers.</summary>
        Raider,
        /// <summary>An Epic Special Force: has seen everything, remembers nothing.</summary>
        SpecialForce,
        /// <summary>A real player: the raid keeps the record's familiarity.</summary>
        Player,
    }

    public enum SeedingState { Pending, Done, Failed }

    private static int _seeding = (int)SeedingState.Pending;

    /// <summary>The upgrade seeding has run (or found nothing to do). Until then the familiarity stays the record's.</summary>
    public static SeedingState Seeding
    {
        get => (SeedingState)Volatile.Read(ref _seeding);
        set => Volatile.Write(ref _seeding, (int)value);
    }

    /// <summary>One raid's reading of one mechanic: its roster before this raid and the raiders already credited.</summary>
    private sealed class RaidState
    {
        public long Instance;
        public int Seed;
        public double Record, Raid;
        public RaidDiscipline.RosterKnowledge Roster;
        public readonly Dictionary<long, int> SeenBefore = new();
        public int LoggedRaiders = -1, LoggedSeen = -1;
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<(string RaidId, string Mechanic), RaidState> Raids = new();

    // ---- at the call --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The raid's familiarity at a new call of <paramref name="mechanic"/> (the one call site: the stomp's call). Switch off,
    /// a mechanic that is not learned, no running realm raid, a real player in the parties, the play-test override or the
    /// seeding not yet done: the record's (<see cref="RaidDiscipline.FamiliarityOf"/>), exactly as before. Otherwise it reads
    /// the raiders present, credits the new ones and returns the roster's familiarity.
    /// </summary>
    public static double FamiliarityAtCall(string raidId, string mechanic, Group[] parties, GameNPC boss)
    {
        double record = RaidDiscipline.FamiliarityOf(raidId);
        if (!BotPeopleSwitches.Current.RaidLearnsFromRaiders || !RaidDiscipline.IsLearned(mechanic) || string.IsNullOrEmpty(raidId) ||
            Seeding != SeedingState.Done || RaidDiscipline.FamiliarityOverride.HasValue)
            return record;
        try
        {
            List<Learner> raiders = Present(parties, boss, out bool player);
            if (player) return record;
            long instance = InstanceOf(raidId);
            if (instance == 0) return record;
            return AtCall(BotPeopleStore.Current, true, raidId, mechanic, instance, RaidDiscipline.SeedOf(raidId), record, false, raiders,
                DateTime.UtcNow, health: boss?.HealthPercent ?? 0);
        }
        catch (Exception exception)
        {
            Warn($"RAID_LEARN failed raid={raidId} mechanic={mechanic}: {exception.Message}");
            return record;
        }
    }

    /// <summary>The running realm raid's instance (its creation tick), or 0 when no realm raid of that id runs (a player's own
    /// group at the boss, a test).</summary>
    private static long InstanceOf(string raidId)
    {
        foreach (eRealm realm in AutonomousRealmRaid.Definitions.Where(d => d.Id == raidId).Select(d => d.Realm).Distinct())
            foreach (AutonomousRealmRaid.PlayerRaid raid in AutonomousRealmRaid.RaidsOfRealm(realm))
                if (raid.Id == raidId) return raid.Created;
        return 0;
    }

    /// <summary>What one party member is to the learning (reads the game, no position).</summary>
    public static Seat SeatOf(GameLiving member, out Learner learner)
    {
        learner = default;
        switch (member)
        {
            case GamePlayer:
                return Seat.Player;
            case GameBot { IsEpicSpecialForce: true } esf:
                learner = new Learner(esf.DatabaseID, default, true);
                return Seat.SpecialForce;
            case GameBot bot when BotPeopleScope.Check(bot, BotPeopleRule.Record, true, out _) == BotPeopleRefusal.None:
                learner = new Learner(bot.DatabaseID, BotPersonKey.Of(bot), false);
                return Seat.Raider;
            default:
                return Seat.Skip;
        }
    }

    /// <summary>The raiders present: gamebots of the raid's parties, alive, in the boss's region and within
    /// <see cref="PresentRange"/>, that the learning counts. <paramref name="player"/>: a real player is in a party.</summary>
    private static List<Learner> Present(Group[] parties, GameNPC boss, out bool player)
    {
        var raiders = new List<Learner>();
        player = false;
        foreach (Group group in parties ?? [])
        {
            if (group == null) continue;
            foreach (GameLiving member in group.GetMembersInTheGroup())
            {
                if (member is GamePlayer)
                {
                    player = true;
                    continue;
                }
                if (member is not GameBot bot || !bot.IsAlive || boss == null || bot.CurrentRegionID != boss.CurrentRegionID ||
                    !bot.IsWithinRadius(boss, PresentRange))
                    continue;
                if (SeatOf(bot, out Learner learner) is Seat.Raider or Seat.SpecialForce)
                    raiders.Add(learner);
            }
        }
        return raiders;
    }

    /// <summary>
    /// The learning at a call (the game's state passed in, so tests can run it): reads what the raiders present had seen
    /// before this raid instance, credits each new raider once for it (a row change through the store), logs, and returns
    /// the raid's familiarity. The switch off, a real player present or the play-test override: <paramref name="record"/>,
    /// and nothing changes.
    /// </summary>
    public static double AtCall(BotPeopleStore store, bool switchOn, string raidId, string mechanic, long instance, int seed, double record,
        bool playerPresent, IReadOnlyList<Learner> raiders, DateTime nowUtc, int health = 0, bool overridden = false)
    {
        if (!switchOn || playerPresent || overridden || store == null || string.IsNullOrEmpty(raidId) || !RaidDiscipline.IsLearned(mechanic))
            return record;
        int credited = 0, firstTime = 0;
        string now = nowUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        string line = null;
        double raid;
        lock (Sync)
        {
            if (!Raids.TryGetValue((raidId, mechanic), out RaidState state) || state.Instance != instance)
                Raids[(raidId, mechanic)] = state = new RaidState { Instance = instance };
            state.Seed = seed;
            foreach (Learner raider in raiders ?? [])
            {
                if (state.SeenBefore.ContainsKey(raider.Id)) continue;
                if (raider.SpecialForce)
                {
                    state.SeenBefore[raider.Id] = RaidDiscipline.KnowsAfterRaids;
                    state.Roster = state.Roster.WithVeteran();
                    continue;
                }
                if (raider.Person.IsNone) continue;
                BotPersonKey person = raider.Person;
                int before = store.MechanicsSeen.TryGet((person, mechanic), out OfflineBotMechanicSeenRecord row) ? Math.Max(0, row.Seen) : 0;
                state.SeenBefore[raider.Id] = before;
                state.Roster = state.Roster.With(before);
                long characterId = raider.Id;
                store.MechanicsSeen.Change((person, mechanic),
                    () => new OfflineBotMechanicSeenRecord { PersonKey = person.ToString(), Mechanic = mechanic, CharacterId = characterId },
                    seen =>
                    {
                        seen.Seen = seen.Seen >= int.MaxValue - 1 ? int.MaxValue : Math.Max(0, seen.Seen) + 1;
                        seen.LastSeenUtc = now;
                    });
                credited++;
                if (before == 0) firstTime++;
            }
            state.Record = record;
            state.Raid = raid = RaidDiscipline.RosterFamiliarity(record, state.Roster);
            if (state.LoggedRaiders != state.Roster.Raiders || state.LoggedSeen != state.Roster.HaveSeen)
            {
                state.LoggedRaiders = state.Roster.Raiders;
                state.LoggedSeen = state.Roster.HaveSeen;
                line = LearnLine(raidId, mechanic, record, state.Roster, raid, health, instance);
            }
        }
        if (Log.IsInfoEnabled)
        {
            if (line != null) Log.Info(line);
            if (credited > 0) Log.Info(SeenLine(raidId, mechanic, credited, firstTime, health));
        }
        return raid;
    }

    /// <summary>
    /// One raider's familiarity for its reaction (the call's raider loop). Switch off, an unknown raid or a raider the call
    /// did not count (a real player, a companion, a bot that came after the call): <paramref name="callFamiliarity"/>, the
    /// raid's, exactly as before.
    /// </summary>
    public static double FamiliarityOf(GameBot raider, string raidId, string mechanic, int seed, double callFamiliarity) =>
        raider == null ? callFamiliarity
            : ForRaider(BotPeopleSwitches.Current.RaidLearnsFromRaiders, raidId, mechanic, seed, raider.DatabaseID, callFamiliarity,
                RaidDiscipline.FamiliarityOverride.HasValue);

    /// <summary>The same with the game's state passed in (tests). The raid's current reading is the one this call made; a
    /// call whose record seed differs from it is an older raid's and keeps its familiarity.</summary>
    public static double ForRaider(bool switchOn, string raidId, string mechanic, int seed, long raiderId, double callFamiliarity,
        bool overridden = false)
    {
        if (!switchOn || overridden || string.IsNullOrEmpty(raidId) || mechanic == null) return callFamiliarity;
        lock (Sync)
        {
            if (!Raids.TryGetValue((raidId, mechanic), out RaidState state) || state.Seed != seed ||
                !state.SeenBefore.TryGetValue(raiderId, out int seen))
                return callFamiliarity;
            return RaidDiscipline.RaiderFamiliarity(state.Record, state.Raid, RaidDiscipline.OwnKnowledge(seen), state.Roster.MeanKnowledge);
        }
    }

    /// <summary>The roster this raid read for a mechanic (tests, /lswhy later): null when none.</summary>
    public static RaidDiscipline.RosterKnowledge? RosterOf(string raidId, string mechanic)
    {
        lock (Sync) return Raids.TryGetValue((raidId, mechanic), out RaidState state) ? state.Roster : null;
    }

    /// <summary>Tests: forget every raid's reading, and set the seeding's state.</summary>
    public static void ResetForTests(SeedingState seeding = SeedingState.Done)
    {
        lock (Sync) Raids.Clear();
        Seeding = seeding;
    }

    /// <summary>RAID_LEARN raid= mechanic= record= seenShare= familiarity=, with the familiarity a newcomer and a veteran of
    /// this raid react with, and run= the raid instance (its creation tick), so a log reader compares runs, not roster
    /// changes inside one run (pure).</summary>
    public static string LearnLine(string raidId, string mechanic, double record, RaidDiscipline.RosterKnowledge roster, double raid,
        int health = 0, long run = 0) =>
        $"RAID_LEARN raid={raidId} mechanic={mechanic} record={F(record)} seenShare={F(roster.SeenShare)} familiarity={F(raid)} " +
        $"raiders={roster.Raiders} seen={roster.HaveSeen} specialForces={roster.SpecialForces} knowledge={F(roster.MeanKnowledge)} " +
        $"newcomer={F(RaidDiscipline.RaiderFamiliarity(record, raid, 0, roster.MeanKnowledge))} " +
        $"veteran={F(RaidDiscipline.RaiderFamiliarity(record, raid, 1, roster.MeanKnowledge))} run={run} health={health}";

    /// <summary>RAID_MECHANIC_SEEN for raiders credited at a call (pure).</summary>
    public static string SeenLine(string raidId, string mechanic, int credited, int firstTime, int health = 0) =>
        $"RAID_MECHANIC_SEEN how=raid raid={raidId} mechanic={mechanic} credited={credited} firstTime={firstTime} health={health}";

    private static string F(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    // ---- the upgrade seeding ------------------------------------------------------------------------------------------

    /// <summary>One saved bot as the seeding sees it.</summary>
    public readonly record struct RosterBot(long Id, int Realm, int Level);

    /// <summary>What the seeding did for one raid encounter and realm.</summary>
    public readonly record struct SeedResult(string RaidId, string Mechanic, int Realm, double Record, double Chance, int Bots,
        int Seeded, bool AlreadyKnown);

    /// <summary>
    /// The seeding (pure apart from the store's rows): for each learned mechanic and each raid encounter of a realm that fights
    /// its boss, with a counted attempt on the record, and no row yet for that mechanic among the realm's bots, every level
    /// 50 bot of the realm is marked as having seen it (<see cref="RaidDiscipline.KnowsAfterRaids"/> raids) when its fixed
    /// roll says so (<see cref="RaidDiscipline.SeededAtUpgrade"/>). A realm that already has rows is never seeded again.
    /// </summary>
    public static List<SeedResult> Seed(BotPeopleStore store, IEnumerable<RosterBot> bots,
        IEnumerable<(string RaidId, eRealm Realm, string BossType)> encounters, Func<string, double> recordFamiliarity, DateTime nowUtc)
    {
        var results = new List<SeedResult>();
        RosterBot[] roster = (bots ?? []).ToArray();
        string now = nowUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        foreach (var (mechanic, bossType) in RaidDiscipline.LearnedMechanics)
        {
            OfflineBotMechanicSeenRecord[] rows = store.MechanicsSeen.Where(row => row.Mechanic == mechanic);
            var known = rows.Select(row => row.CharacterId).ToHashSet();
            foreach (var (raidId, realm, _) in (encounters ?? []).Where(e => e.BossType == bossType).Distinct())
            {
                double record = recordFamiliarity(raidId);
                if (record <= 0) continue;
                RosterBot[] realmBots = roster.Where(b => b.Realm == (int)realm).ToArray();
                bool already = realmBots.Any(b => known.Contains(b.Id));
                int seeded = 0;
                if (!already)
                {
                    foreach (RosterBot bot in realmBots)
                    {
                        if (!RaidDiscipline.SeededAtUpgrade(bot.Id, bot.Level, mechanic, record)) continue;
                        BotPersonKey person = BotPersonKey.Of(bot.Id);
                        long id = bot.Id;
                        store.MechanicsSeen.Change((person, mechanic),
                            () => new OfflineBotMechanicSeenRecord { PersonKey = person.ToString(), Mechanic = mechanic, CharacterId = id },
                            row =>
                            {
                                row.Seen = Math.Max(row.Seen, RaidDiscipline.KnowsAfterRaids);
                                row.LastSeenUtc = now;
                            });
                        known.Add(id);
                        seeded++;
                    }
                }
                results.Add(new SeedResult(raidId, mechanic, (int)realm, record, RaidDiscipline.SeedChance(record),
                    realmBots.Count(b => b.Level >= RaidDiscipline.SeedLevel), seeded, already));
            }
        }
        return results;
    }

    /// <summary>RAID_MECHANIC_SEEN how=seeded (pure).</summary>
    public static string SeedLine(SeedResult r) =>
        $"RAID_MECHANIC_SEEN how=seeded raid={r.RaidId} mechanic={r.Mechanic} realm={r.Realm} record={F(r.Record)} " +
        $"chance={F(r.Chance)} level50={r.Bots} seeded={r.Seeded}" + (r.AlreadyKnown ? " already=true" : string.Empty);

    /// <summary>How long the seeding waits for the raid record to load before it gives up for this run.</summary>
    public const int SeedWaitMilliseconds = 10 * 60_000;

    /// <summary>Where the seeding reads the world (the server's by default; tests pass their own).</summary>
    public sealed class SeedingSources
    {
        public Action EnsureLoading { get; init; } = RealmRaidRecord.EnsureLoading;
        public Func<bool> Loaded { get; init; } = () => RealmRaidRecord.Loaded;
        /// <summary>The saved level 50 gamebots: not retired, not a player's hired companion.</summary>
        public Func<IEnumerable<RosterBot>> Bots { get; init; } = () =>
            DOLDB<OfflineWorldBotRecord>.SelectObjects(DB.Column("Level").IsGreaterOrEqualTo(RaidDiscipline.SeedLevel))
                .Where(b => !b.IsRetired && !HiredCompanions165.IsHired(b.BotId)).Select(b => new RosterBot(b.BotId, b.Realm, b.Level)).ToArray();
        public Func<IEnumerable<(string RaidId, eRealm Realm, string BossType)>> Encounters { get; init; } = () => AutonomousRealmRaid.Definitions
            .SelectMany(d => d.FinalTypes.Append(d.BossType).Distinct().Select(boss => (d.Id, d.Realm, boss))).ToArray();
        public Func<string, double> RecordFamiliarity { get; init; } = id => RaidDiscipline.Familiarity(RealmRaidRecord.GetResults(id));
        public int PollMilliseconds { get; init; } = 2_000;
        public int WaitMilliseconds { get; init; } = SeedWaitMilliseconds;
    }

    /// <summary>
    /// Called once the store has loaded at server start (BotPeopleStore.OnServerStarted): the seeding waits for the raid
    /// record without holding a thread (a delayed continuation), then runs on a pool thread. With the switch off at start
    /// nothing is seeded and the familiarity stays the record's until a start with the switch on.
    /// </summary>
    public static void StartSeeding(BotPeopleStore store) =>
        _ = StartSeeding(store, BotPeopleSwitches.Current.RaidLearnsFromRaiders, new SeedingSources());

    /// <summary>The same with the sources passed in (tests). The task ends with the seeding's state.</summary>
    public static Task<SeedingState> StartSeeding(BotPeopleStore store, bool switchOn, SeedingSources sources)
    {
        if (!switchOn || store == null || sources == null)
        {
            Seeding = SeedingState.Pending;
            return Task.FromResult(SeedingState.Pending);
        }
        Seeding = SeedingState.Pending;
        return Task.Run(async () =>
        {
            try
            {
                sources.EnsureLoading?.Invoke();
                long until = Environment.TickCount64 + sources.WaitMilliseconds;
                while (!sources.Loaded())
                {
                    if (Environment.TickCount64 > until)
                    {
                        Seeding = SeedingState.Failed;
                        Warn("RAID_MECHANIC_SEEN how=seeded failed: the raid record did not load; raids keep the record's familiarity this run");
                        return SeedingState.Failed;
                    }
                    await Task.Delay(sources.PollMilliseconds).ConfigureAwait(false);
                }
                List<SeedResult> results = Seed(store, sources.Bots(), sources.Encounters(), sources.RecordFamiliarity, DateTime.UtcNow);
                Seeding = SeedingState.Done;
                if (Log.IsInfoEnabled)
                    foreach (SeedResult result in results)
                        Log.Info(SeedLine(result));
                return SeedingState.Done;
            }
            catch (Exception exception)
            {
                Seeding = SeedingState.Failed;
                Warn("RAID_MECHANIC_SEEN how=seeded failed; raids keep the record's familiarity this run", exception);
                return SeedingState.Failed;
            }
        });
    }
}
