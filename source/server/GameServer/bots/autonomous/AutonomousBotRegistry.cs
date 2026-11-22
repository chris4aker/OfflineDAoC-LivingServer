using System.Collections.Concurrent;
using System;
using System.Linq;
using System.Collections.Generic;
using DOL.Database;
using System.Threading;

namespace DOL.GS;

/// <summary>Keeps live autonomous actors available to the normal server save cycle.</summary>
public static class AutonomousBotRegistry
{
    private static readonly ConcurrentDictionary<long, GameBot> Active = new();
    // Epic Special Forces members (GameBot.IsEpicSpecialForce): found by id like any gamebot, so their raid keeps
    // them, but never part of the population, the planners' roster, the guild roster or the save sweep.
    private static readonly ConcurrentDictionary<long, GameBot> SpecialForces = new();
    public static int SpecialForcesOnline => SpecialForces.Count;
    // The No Lifers' buffbots (GameBot.IsNoLiferBuffBot, NoLiferBuffBots): found by id and by name like any gamebot (/who,
    // /send, /target, /invite reach them), but, like the Special Forces, never part of the population, the planners' roster,
    // any group or the save sweep.
    private static readonly ConcurrentDictionary<long, GameBot> BuffBots = new();
    public static int BuffBotsOnline => BuffBots.Count;
    // Players' hired companions (HiredCompanions165, from True 1.65): persistent bot rows their players log in and out.
    // Found by id like any gamebot and saved by the save sweep, but never part of the population count, the planners'
    // roster (Snapshot), the realm guilds or friends lists: no raid, siege, guild group or chat planner ever picks one.
    private static readonly ConcurrentDictionary<long, GameBot> Hired = new();
    public static int HiredOnline => Hired.Count;

    /// <summary>The hired companions in the world now.</summary>
    public static GameBot[] HiredSnapshot() =>
        Hired.Values.Where(bot => bot?.ObjectState == GameObject.eObjectState.Active).ToArray();

    /// <summary>The Epic Special Forces members in the world now.</summary>
    public static GameBot[] SpecialForcesSnapshot() =>
        SpecialForces.Values.Where(bot => bot?.ObjectState == GameObject.eObjectState.Active).ToArray();

    /// <summary>The buffbots in the world now.</summary>
    public static GameBot[] BuffBotSnapshot() =>
        BuffBots.Values.Where(bot => bot?.ObjectState == GameObject.eObjectState.Active).ToArray();
    private static int _populationForBrainTick;
    private static long _nextPopulationSampleTick;
    private const int PopulationSampleIntervalMs = 1_000;

    // A cadence input, not a replacement for the exact live population checks
    // used by spawning, transfer recovery and the launcher. Sample once before
    // parallel AI dispatch instead of copying/counting 4,500 actors per brain.
    public static int PopulationForBrainTick => Volatile.Read(ref _populationForBrainTick);

    public static void PrepareBrainTick()
    {
        // This walk runs serially on the game-loop thread before the parallel
        // brain dispatch. Population and per-dungeon counts only steer AI
        // cadence and dungeon crowding, so sampling them once a second keeps
        // the same behavior without a 6,000-actor scan on every tick.
        long now = GameLoop.GameLoopTime;
        if (now < _nextPopulationSampleTick)
            return;
        _nextPopulationSampleTick = now + PopulationSampleIntervalMs;
        SamplePopulationNow();
    }

    /// <summary>Recounts live actors immediately, ignoring the once-a-second cadence.</summary>
    public static void SamplePopulationNow()
    {
        int count = 0;
        var dungeonPopulation = new Dictionary<ushort, int>();
        foreach (var entry in Active)
        {
            GameBot bot = entry.Value;
            if (bot?.ObjectState != GameObject.eObjectState.Active)
                continue;
            count++;

            ushort dungeonRegion = 0;
            if (bot.CurrentZone?.IsDungeon == true || bot.CurrentRegion?.IsDungeon == true)
                dungeonRegion = bot.CurrentRegionID;
            else
                AutonomousDungeonPopulationPolicy.TryGetAssignedRegion(
                    bot.PersistentRecord?.CurrentCampId, out dungeonRegion);
            if (dungeonRegion != 0)
                dungeonPopulation[dungeonRegion] = dungeonPopulation.GetValueOrDefault(dungeonRegion) + 1;
        }
        Volatile.Write(ref _populationForBrainTick, count);
        AutonomousDungeonPopulationPolicy.PublishPopulation(dungeonPopulation);
    }

    // A failed cross-region AddToWorld can briefly leave an entry inactive.
    // Population targets are counts of live actors, never dictionary slots.
    public static int Count => Active.Values.Count(bot => bot?.ObjectState == GameObject.eObjectState.Active);

    public static bool Contains(long botId) => Active.ContainsKey(botId) || SpecialForces.ContainsKey(botId) || BuffBots.ContainsKey(botId) ||
                                               Hired.ContainsKey(botId);

    public static bool TryGet(long botId, out GameBot bot) =>
        Active.TryGetValue(botId, out bot) || SpecialForces.TryGetValue(botId, out bot) || BuffBots.TryGetValue(botId, out bot) ||
        Hired.TryGetValue(botId, out bot);

    // One array per game-loop tick, shared by every caller (all read-only): RvR planning built a
    // fresh 9,000-entry array per AI turn (55 MB of allocations in 40 s, run 9 trace).
    private sealed record CachedSnapshot(long Tick, GameBot[] Bots);
    private static volatile CachedSnapshot _snapshot = new(-1, []);
    public const long SnapshotMaxAgeMilliseconds = 250;

    public static GameBot[] Snapshot()
    {
        long tick = GameLoop.GameLoopTime;
        CachedSnapshot cached = _snapshot;
        // Register/Unregister invalidate it; otherwise a quarter-second-old roster is fine for
        // planning (callers check state themselves). Per-tick rebuilds were ~23 MB per 40 s.
        if (cached.Tick > 0 && tick >= cached.Tick && tick - cached.Tick < SnapshotMaxAgeMilliseconds) return cached.Bots;
        GameBot[] bots = Active.Values.Where(bot => bot?.ObjectState == GameObject.eObjectState.Active).ToArray();
        _snapshot = new(tick, bots);
        return bots;
    }

    public static IReadOnlyDictionary<eRealm, int> CountByRealm() => Active.Values
        .Where(bot => bot?.ObjectState == GameObject.eObjectState.Active)
        .GroupBy(bot => bot.Realm)
        .ToDictionary(group => group.Key, group => group.Count());

    public static bool TryGetByName(string name, out GameBot bot)
    {
        bot = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string requestedName = name.Trim();
        bot = Active.Values.FirstOrDefault(candidate =>
            candidate?.ObjectState == GameObject.eObjectState.Active &&
            string.Equals(candidate.Name, requestedName, StringComparison.OrdinalIgnoreCase));
        return bot != null;
    }

    /// <summary>An online autonomous gamebot of the realm, by its exact name: /send, /invite and /target reach
    /// bots as they reach players (in-game check 2026-10-17: "pst me" lines, but "/send Haralhelm" answered
    /// "not in the game"). Companion bots and temporary helpers are not included.</summary>
    public static bool TryGetOnline(string name, eRealm realm, out GameBot bot)
    {
        bot = TryGetByName(name, out GameBot found) && found.Realm == realm && found.IsAutonomousWorldBot &&
              !found.IsTemporaryGroupHelper ? found : null;
        // A buffbot answers to its name as a player's second character would (it declines invites and says it is a buffbot).
        bot ??= string.IsNullOrWhiteSpace(name) ? null : BuffBots.Values.FirstOrDefault(candidate =>
            candidate?.ObjectState == GameObject.eObjectState.Active && candidate.Realm == realm &&
            string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return bot != null;
    }

    public static void Register(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot == true && bot.DatabaseID > 0 && bot.IsEpicSpecialForce)
        {
            SpecialForces[bot.DatabaseID] = bot;
            return;
        }
        if (bot?.IsAutonomousWorldBot == true && bot.DatabaseID > 0 && bot.IsNoLiferBuffBot)
        {
            BuffBots[bot.DatabaseID] = bot;
            return;
        }
        if (bot?.IsAutonomousWorldBot == true && bot.DatabaseID > 0 && HiredCompanions165.IsHired(bot))
        {
            Hired[bot.DatabaseID] = bot;
            return;
        }
        if (bot?.IsAutonomousWorldBot == true && bot.DatabaseID > 0)
        {
            Active[bot.DatabaseID] = bot;
            _snapshot = new(-1, []);
            AutonomousBotRealmGuilds.OnBotOnline(bot);
            BotFriends.OnBotOnline(bot); // Fork: friends hear when a bot comes online
            BotSkill.OnLoggedIn(bot); // Bots as people WP1: its skill profile (BOT_SKILL_PROFILE)
            BotMilestoneLife.OnLoggedIn(bot); // Bots as people WP6: it takes or resumes its milestone arc
        }
    }

    public static void Unregister(GameBot bot)
    {
        AutonomousBeggars.Forget(bot);
        BotDutyLeases.OnLoggedOut(bot); // a bot that leaves the world gives up whatever had its time
        ChatPromises.OnLoggedOut(bot); // and an "omw" it had not kept is broken (logged_out)
        BotPeopleStore.OnLoggedOut(bot); // Bots as people: its rows are saved soon, off the game loop (no-op for companions)
        if (bot?.DatabaseID > 0 && bot.IsEpicSpecialForce)
        {
            SpecialForces.TryRemove(bot.DatabaseID, out _);
            AutonomousBotEconomy.ForgetCachedDecision(bot.DatabaseID);
            return;
        }
        if (bot?.DatabaseID > 0 && bot.IsNoLiferBuffBot)
        {
            BuffBots.TryRemove(bot.DatabaseID, out _);
            return;
        }
        if (bot?.DatabaseID > 0 && Hired.TryRemove(new KeyValuePair<long, GameBot>(bot.DatabaseID, bot)))
        {
            AutonomousBotEconomy.ForgetCachedDecision(bot.DatabaseID);
            return;
        }
        if (bot?.DatabaseID > 0)
        {
            AutonomousGoalDiagnostics.End(bot, GoalAttemptEnd.Logout, "Bot left the active world; not a goal failure");
            Active.TryRemove(bot.DatabaseID, out _);
            AutonomousBotRealmGuilds.OnBotOffline(bot);
            BotFriends.OnBotOffline(bot); // Fork: friends hear when a bot goes offline
            _snapshot = new(-1, []);
            AutonomousBotEconomy.ForgetCachedDecision(bot.DatabaseID);
        }
    }

    public static int SaveAll()
    {
        GameBot[] bots = Active.Values.Concat(Hired.Values)
            .Where(bot => bot?.PersistentRecord?.IsPersisted == true)
            .ToArray();
        if (bots.Length == 0)
            return 0;

        // Inventory is already persisted by AutonomousBotStatusPersistence on
        // real inventory events. Rewriting every slot for every online bot made
        // a 739-bot server save monopolize SQLite for roughly nineteen seconds.
        // Queue the full state sweep into the same FIFO coalescer as live status
        // changes. At 32 records every two seconds, all 4,500 slots complete in
        // at most 4m42s while inventory and exchange writes retain low latency.
        foreach (GameBot bot in bots)
            AutonomousBotStatusPersistence.Queue(bot);
        return bots.Length;
    }
}
