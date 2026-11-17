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
/// <item>Raiders: saved autonomous gamebots (BotPeopleScope rule Record). The Epic Special Forces have no person and count as
/// having seen everything. Real players, companions, /raid helpers and buffbots are not counted.</item>
/// <item>A raider is credited once per raid (the record's seed), at the first call of the mechanic it is present for; what it
/// had seen before this raid is what counts in this raid.</item>
/// <item>An upgraded world is seeded once, off the game loop, after the store and the record have loaded
/// (<see cref="Seed"/>); until then, and with the switch off, the familiarity is the record's exactly.</item>
/// </list>
/// Wired at one place, the stomp's call (EpicEncounterTactics.RaidCalls.cs). Logs RAID_LEARN (a raid's roster, when it
/// changes: about one line a raid) and RAID_MECHANIC_SEEN (raiders credited, and the seeding).
/// </summary>
public static class RaidLearning
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>Raiders of the raid's parties within this distance of the boss (same region, alive) are present.</summary>
    public const int PresentRange = 6_000;

    /// <summary>One raider as the learning sees it: its id, its person (none for an Epic Special Force) and whether it is one.</summary>
    public readonly record struct Learner(long Id, BotPersonKey Person, bool SpecialForce);

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
    /// a mechanic that is not learned, no raid or the seeding not yet done: the record's (<see cref="RaidDiscipline.FamiliarityOf"/>),
    /// exactly as before. Otherwise it reads the raiders present, credits the new ones and returns the roster's familiarity.
    /// </summary>
    public static double FamiliarityAtCall(string raidId, string mechanic, Group[] parties, GameNPC boss)
    {
        double record = RaidDiscipline.FamiliarityOf(raidId);
        if (!BotPeopleSwitches.Current.RaidLearnsFromRaiders || !RaidDiscipline.IsLearned(mechanic) || string.IsNullOrEmpty(raidId) ||
            Seeding != SeedingState.Done)
            return record;
        try
        {
            List<Learner> raiders = Present(parties, boss);
            return AtCall(BotPeopleStore.Current, true, raidId, mechanic, RaidDiscipline.SeedOf(raidId), record,
                RaidDiscipline.FamiliarityOverride.HasValue, raiders, DateTime.UtcNow, threshold: boss?.HealthPercent ?? 0);
        }
        catch (Exception exception)
        {
            Log.Warn($"RAID_LEARN failed raid={raidId} mechanic={mechanic}: {exception.Message}");
            return record;
        }
    }

    /// <summary>The raiders present: gamebots of the raid's parties, alive, in the boss's region and within
    /// <see cref="PresentRange"/>, that the learning counts (a saved gamebot with a person, or an Epic Special Force).</summary>
    private static List<Learner> Present(Group[] parties, GameNPC boss)
    {
        var raiders = new List<Learner>();
        foreach (Group group in parties ?? [])
        {
            if (group == null) continue;
            foreach (GameLiving member in group.GetMembersInTheGroup())
            {
                if (member is not GameBot bot || !bot.IsAlive || boss == null || bot.CurrentRegionID != boss.CurrentRegionID ||
                    !bot.IsWithinRadius(boss, PresentRange))
                    continue;
                if (bot.IsEpicSpecialForce)
                    raiders.Add(new Learner(bot.DatabaseID, default, true));
                else if (BotPeopleScope.Check(bot, BotPeopleRule.Record, true, out _) == BotPeopleRefusal.None)
                    raiders.Add(new Learner(bot.DatabaseID, BotPersonKey.Of(bot), false));
            }
        }
        return raiders;
    }

    /// <summary>
    /// The learning at a call (the game's state passed in, so tests can run it): reads what the raiders present had seen
    /// before this raid, credits each new raider once for this raid (a row change through the store), logs, and returns the
    /// raid's familiarity. With the switch off it returns <paramref name="record"/> and changes nothing; with the play-test
    /// override it returns the override (the record's value here) but still credits the raiders.
    /// </summary>
    public static double AtCall(BotPeopleStore store, bool switchOn, string raidId, string mechanic, int seed, double record,
        bool overridden, IReadOnlyList<Learner> raiders, DateTime nowUtc, int threshold = 0)
    {
        if (!switchOn || store == null || string.IsNullOrEmpty(raidId) || !RaidDiscipline.IsLearned(mechanic))
            return record;
        int credited = 0, firstTime = 0;
        string now = nowUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        string line = null;
        double raid;
        lock (Sync)
        {
            if (!Raids.TryGetValue((raidId, mechanic), out RaidState state) || state.Seed != seed)
                Raids[(raidId, mechanic)] = state = new RaidState { Seed = seed };
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
            state.Raid = raid = overridden ? record : RaidDiscipline.RosterFamiliarity(record, state.Roster);
            if (state.LoggedRaiders != state.Roster.Raiders || state.LoggedSeen != state.Roster.HaveSeen)
            {
                state.LoggedRaiders = state.Roster.Raiders;
                state.LoggedSeen = state.Roster.HaveSeen;
                line = LearnLine(raidId, mechanic, record, state.Roster, raid, threshold, overridden);
            }
        }
        if (Log.IsInfoEnabled)
        {
            if (line != null) Log.Info(line);
            if (credited > 0) Log.Info(SeenLine(raidId, mechanic, credited, firstTime, threshold));
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

    /// <summary>The same with the game's state passed in (tests).</summary>
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

    /// <summary>RAID_LEARN raid= mechanic= record= seenShare= familiarity= (pure).</summary>
    public static string LearnLine(string raidId, string mechanic, double record, RaidDiscipline.RosterKnowledge roster, double raid,
        int threshold = 0, bool overridden = false) =>
        $"RAID_LEARN raid={raidId} mechanic={mechanic} record={F(record)} seenShare={F(roster.SeenShare)} familiarity={F(raid)} " +
        $"raiders={roster.Raiders} seen={roster.HaveSeen} specialForces={roster.SpecialForces} knowledge={F(roster.MeanKnowledge)} " +
        $"health={threshold}" + (overridden ? " override=true" : string.Empty);

    /// <summary>RAID_MECHANIC_SEEN for raiders credited at a call (pure).</summary>
    public static string SeenLine(string raidId, string mechanic, int credited, int firstTime, int threshold = 0) =>
        $"RAID_MECHANIC_SEEN how=raid raid={raidId} mechanic={mechanic} credited={credited} firstTime={firstTime} health={threshold}";

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

    /// <summary>
    /// Called once the store has loaded at server start (BotPeopleStore.OnServerStarted): runs the seeding on a pool
    /// thread after the raid record has loaded. With the switch off at start nothing is seeded and the familiarity stays
    /// the record's until a start with the switch on.
    /// </summary>
    public static void StartSeeding(BotPeopleStore store)
    {
        if (!BotPeopleSwitches.Current.RaidLearnsFromRaiders)
        {
            Seeding = SeedingState.Pending;
            return;
        }
        Task.Run(() =>
        {
            try
            {
                RealmRaidRecord.EnsureLoading();
                long until = Environment.TickCount64 + SeedWaitMilliseconds;
                while (!RealmRaidRecord.Loaded)
                {
                    if (Environment.TickCount64 > until)
                    {
                        Seeding = SeedingState.Failed;
                        Log.Warn("RAID_MECHANIC_SEEN how=seeded failed: the raid record did not load; raids keep the record's familiarity this run");
                        return;
                    }
                    Thread.Sleep(2_000);
                }
                RosterBot[] bots = DOLDB<OfflineWorldBotRecord>.SelectObjects(DB.Column("Level").IsGreaterOrEqualTo(RaidDiscipline.SeedLevel))
                    .Where(b => !b.IsRetired).Select(b => new RosterBot(b.BotId, b.Realm, b.Level)).ToArray();
                var encounters = AutonomousRealmRaid.Definitions
                    .SelectMany(d => d.FinalTypes.Append(d.BossType).Distinct().Select(boss => (d.Id, d.Realm, boss))).ToArray();
                List<SeedResult> results = Seed(store, bots, encounters, id => RaidDiscipline.Familiarity(RealmRaidRecord.GetResults(id)),
                    DateTime.UtcNow);
                Seeding = SeedingState.Done;
                foreach (SeedResult result in results)
                    Log.Info(SeedLine(result));
            }
            catch (Exception exception)
            {
                Seeding = SeedingState.Failed;
                Log.Warn("RAID_MECHANIC_SEEN how=seeded failed; raids keep the record's familiarity this run", exception);
            }
        });
    }
}
