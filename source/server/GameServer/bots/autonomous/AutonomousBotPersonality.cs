using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>A bot's personality type (docs/BOT-PERSONALITIES.md).</summary>
public enum BotPersonalityType
{
    Leveler,
    Socialite,
    Daredevil,
    Explorer,
    Crafter,
    NoLifer,
}

/// <summary>Small persistent traits, 0-9, fixed from the bot's database id.</summary>
public readonly record struct BotPersonalityTraits(int Curiosity, int Risk, int Sociability, int Patience, int Chattiness);

public readonly record struct BotPersonality(BotPersonalityType Type, BotPersonalityTraits Traits);

/// <summary>
/// Bot personalities (docs/BOT-PERSONALITIES.md, part of the Living Server). Every autonomous
/// gamebot has a type and traits, fixed from its database id, that tilt how it spends its time:
/// town breaks, solo versus group goals, dungeon versus outdoor, which legal camp it picks, and how
/// often it talks. Hard rules never change: realm, reachability, level and con legality, recovery,
/// services and the owner's gameplay rules. With <c>bot_personalities_enabled</c> off every tilt is
/// neutral and behavior is exactly as before.
/// </summary>
public static class AutonomousBotPersonality
{
    /// <summary>Type shares in percent, in enum order (sum 100).</summary>
    public static readonly int[] TypeSharePercent = [28, 20, 20, 16, 12, 4];

    public const int ZoneMemoryCapacity = 32;
    public static readonly TimeSpan ZoneFreshAfter = TimeSpan.FromHours(6);

    public static bool Enabled => Properties.BOT_PERSONALITIES_ENABLED;

    #region Assignment

    /// <summary>A well-mixed 64-bit value from an id and a salt (SplitMix64).</summary>
    public static ulong Mix(long id, ulong salt)
    {
        unchecked
        {
            ulong z = (ulong)id + salt * 0x9E3779B97F4A7C15UL + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public static BotPersonalityType TypeFor(long botId)
    {
        int roll = (int)(Mix(botId, 1) % 100);
        for (int index = 0; index < TypeSharePercent.Length; index++)
        {
            if (roll < TypeSharePercent[index])
                return (BotPersonalityType)index;
            roll -= TypeSharePercent[index];
        }
        return BotPersonalityType.Leveler;
    }

    /// <summary>Trait ranges by type: each trait is drawn within its type's band, so types differ but individuals vary.</summary>
    private static (int Low, int High) Band(BotPersonalityType type, int trait) => (type, trait) switch
    {
        // trait: 0 curiosity, 1 risk, 2 sociability, 3 patience, 4 chattiness
        (BotPersonalityType.Leveler, 0) => (1, 4), (BotPersonalityType.Leveler, 1) => (3, 6), (BotPersonalityType.Leveler, 2) => (2, 5),
        (BotPersonalityType.Leveler, 3) => (5, 9), (BotPersonalityType.Leveler, 4) => (1, 5),
        (BotPersonalityType.Socialite, 0) => (3, 6), (BotPersonalityType.Socialite, 1) => (2, 5), (BotPersonalityType.Socialite, 2) => (7, 9),
        (BotPersonalityType.Socialite, 3) => (3, 7), (BotPersonalityType.Socialite, 4) => (6, 9),
        (BotPersonalityType.Daredevil, 0) => (3, 7), (BotPersonalityType.Daredevil, 1) => (7, 9), (BotPersonalityType.Daredevil, 2) => (3, 6),
        (BotPersonalityType.Daredevil, 3) => (1, 4), (BotPersonalityType.Daredevil, 4) => (4, 8),
        (BotPersonalityType.Explorer, 0) => (7, 9), (BotPersonalityType.Explorer, 1) => (4, 7), (BotPersonalityType.Explorer, 2) => (2, 6),
        (BotPersonalityType.Explorer, 3) => (3, 7), (BotPersonalityType.Explorer, 4) => (3, 7),
        (BotPersonalityType.Crafter, 0) => (2, 5), (BotPersonalityType.Crafter, 1) => (0, 3), (BotPersonalityType.Crafter, 2) => (4, 7),
        (BotPersonalityType.Crafter, 3) => (6, 9), (BotPersonalityType.Crafter, 4) => (3, 7),
        (BotPersonalityType.NoLifer, 0) => (2, 6), (BotPersonalityType.NoLifer, 1) => (6, 9), (BotPersonalityType.NoLifer, 2) => (3, 7),
        (BotPersonalityType.NoLifer, 3) => (6, 9), (BotPersonalityType.NoLifer, 4) => (5, 9),
        _ => (3, 6),
    };

    public static BotPersonality For(long botId)
    {
        BotPersonalityType type = TypeFor(botId);
        int Trait(int index)
        {
            (int low, int high) = Band(type, index);
            return low + (int)(Mix(botId, 10UL + (ulong)index) % (ulong)(high - low + 1));
        }
        return new BotPersonality(type, new BotPersonalityTraits(Trait(0), Trait(1), Trait(2), Trait(3), Trait(4)));
    }

    public static BotPersonality For(GameBot bot) => For(bot?.DatabaseID ?? 0);

    public static bool Applies(GameBot bot) =>
        Enabled && bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && bot.DatabaseID > 0;

    #endregion

    #region Tilts (pure; neutral values when off)

    /// <summary>Chance of a town break at a task boundary (today: 15% for everyone).</summary>
    public static double TownBreakChance(BotPersonality personality) => personality.Type switch
    {
        BotPersonalityType.Socialite => 0.30,
        BotPersonalityType.Crafter => 0.28,
        BotPersonalityType.Explorer => 0.10,
        BotPersonalityType.Daredevil => 0.10,
        BotPersonalityType.Leveler => 0.07,
        BotPersonalityType.NoLifer => 0.05,
        _ => AutonomousTownDowntime.StartChancePerEligibilityCheck,
    } * (0.8 + personality.Traits.Sociability * 0.05);

    /// <summary>Town break length as a share of the normal 15-30 minutes (1.0 = today).</summary>
    public static double TownBreakLengthFactor(BotPersonality personality) => personality.Type switch
    {
        BotPersonalityType.Socialite => 1.5,
        BotPersonalityType.Crafter => 1.4,
        BotPersonalityType.Leveler or BotPersonalityType.NoLifer => 0.7,
        _ => 1.0,
    };

    /// <summary>
    /// Goal tilt: the chance to re-draw a goal the type dislikes from the owner's configured mix.
    /// Social types re-draw some solo goals hoping for a group; solitary types re-draw some group
    /// goals. A re-draw comes from the same configured mix, so no goal type is ever forced or banned.
    /// </summary>
    public static double RedrawChance(BotPersonality personality, eAutonomousObjectiveKind first) => (personality.Type, first) switch
    {
        (BotPersonalityType.Socialite, eAutonomousObjectiveKind.SoloPve) => 0.45,
        (BotPersonalityType.NoLifer, eAutonomousObjectiveKind.SoloPve) => 0.55,
        (BotPersonalityType.Leveler, eAutonomousObjectiveKind.GroupPve) => 0.25,
        (BotPersonalityType.Explorer, eAutonomousObjectiveKind.GroupPve) => 0.35,
        (BotPersonalityType.Crafter, eAutonomousObjectiveKind.RvR) => 0.5,
        (BotPersonalityType.Daredevil, eAutonomousObjectiveKind.SoloPve) => 0.2,
        _ => 0,
    };

    /// <summary>
    /// How much a type wants a goal (1 = no preference). The goal allocator uses it to decide WHO fills each open
    /// slot of the owner's mix, never how many slots there are (review of goals, 2026-10-17): No-lifers and
    /// Daredevils go to RvR first, Socialites to groups, Levelers and Crafters to their own camps, Crafters rarely
    /// to RvR. Every value is above zero, so a slot no one else is free for is still filled.
    /// </summary>
    public static double GoalAffinity(BotPersonalityType type, eAutonomousObjectiveKind kind) => (type, kind) switch
    {
        (BotPersonalityType.Leveler, eAutonomousObjectiveKind.SoloPve) => 2.0,
        (BotPersonalityType.Leveler, eAutonomousObjectiveKind.GroupPve) => 1.2,
        (BotPersonalityType.Leveler, eAutonomousObjectiveKind.RvR) => 0.5,
        (BotPersonalityType.Leveler, eAutonomousObjectiveKind.Battleground) => 0.7,
        (BotPersonalityType.Socialite, eAutonomousObjectiveKind.SoloPve) => 0.4,
        (BotPersonalityType.Socialite, eAutonomousObjectiveKind.GroupPve) => 2.5,
        (BotPersonalityType.Socialite, eAutonomousObjectiveKind.RvR) => 1.0,
        (BotPersonalityType.Socialite, eAutonomousObjectiveKind.Battleground) => 1.2,
        (BotPersonalityType.Explorer, eAutonomousObjectiveKind.SoloPve) => 1.8,
        (BotPersonalityType.Explorer, eAutonomousObjectiveKind.GroupPve) => 0.7,
        (BotPersonalityType.Explorer, eAutonomousObjectiveKind.RvR) => 1.0,
        (BotPersonalityType.Explorer, eAutonomousObjectiveKind.Battleground) => 1.0,
        (BotPersonalityType.Daredevil, eAutonomousObjectiveKind.SoloPve) => 0.7,
        (BotPersonalityType.Daredevil, eAutonomousObjectiveKind.GroupPve) => 1.0,
        (BotPersonalityType.Daredevil, eAutonomousObjectiveKind.RvR) => 3.0,
        (BotPersonalityType.Daredevil, eAutonomousObjectiveKind.Battleground) => 2.0,
        (BotPersonalityType.Crafter, eAutonomousObjectiveKind.SoloPve) => 1.8,
        (BotPersonalityType.Crafter, eAutonomousObjectiveKind.GroupPve) => 1.0,
        (BotPersonalityType.Crafter, eAutonomousObjectiveKind.RvR) => 0.2,
        (BotPersonalityType.Crafter, eAutonomousObjectiveKind.Battleground) => 0.4,
        (BotPersonalityType.NoLifer, eAutonomousObjectiveKind.SoloPve) => 0.5,
        (BotPersonalityType.NoLifer, eAutonomousObjectiveKind.GroupPve) => 1.5,
        (BotPersonalityType.NoLifer, eAutonomousObjectiveKind.RvR) => 3.0,
        (BotPersonalityType.NoLifer, eAutonomousObjectiveKind.Battleground) => 1.5,
        _ => 1.0,
    };

    /// <summary>
    /// Goals for a pool of bots that need one (the allocator's pass). The open slots of the owner's mix are filled
    /// exactly (shared out in proportion when the pool is smaller than the open slots); personality only decides who
    /// takes which: RvR goes to the bots that want it most relative to their other choices, then battlegrounds,
    /// then groups versus solo. Still random (a weighted draw), never a fixed ranking. Types are null for bots
    /// without a personality (weight 1). Bots beyond the open slots get null (the caller's usual over-quota rule).
    /// </summary>
    public static eAutonomousObjectiveKind?[] PlanGoals(BotPersonalityType?[] types, bool[] canBattleground, int solo, int group,
        int rvr, int battleground, Random random)
    {
        int count = types.Length;
        var plan = new eAutonomousObjectiveKind?[count];
        int[] open = [Math.Max(0, solo), Math.Max(0, group), Math.Max(0, rvr), Math.Max(0, battleground)];
        int battlegroundPool = canBattleground.Count(can => can);
        open[3] = Math.Min(open[3], battlegroundPool);
        int totalOpen = open.Sum();
        if (totalOpen > count)
        {
            // Share the pool out in proportion to the open slots (largest remainder), as the old draw did on average.
            double[] exact = open.Select(n => (double)n * count / totalOpen).ToArray();
            int[] floor = exact.Select(x => (int)Math.Floor(x)).ToArray();
            int left = count - floor.Sum();
            foreach (int k in Enumerable.Range(0, 4).OrderByDescending(k => exact[k] - floor[k]).ThenBy(_ => random.Next()))
            {
                if (left == 0) break;
                floor[k]++;
                left--;
            }
            open = floor;
        }
        double A(int i, eAutonomousObjectiveKind kind) => types[i] is { } type ? GoalAffinity(type, kind) : 1.0;
        void Take(eAutonomousObjectiveKind kind, int slots, Func<int, bool> eligible, Func<int, double> weight)
        {
            if (slots <= 0)
                return;
            foreach (int i in Enumerable.Range(0, count).Where(i => plan[i] == null && eligible(i))
                         .Select(i => (Index: i, Key: Math.Pow(random.NextDouble(), 1.0 / Math.Max(0.01, weight(i)))))
                         .OrderByDescending(entry => entry.Key).Take(slots).Select(entry => entry.Index).ToList())
                plan[i] = kind;
        }
        // Relative wish: how much more this bot wants the goal than the other goals still open to it.
        double Relative(int i, eAutonomousObjectiveKind kind, params eAutonomousObjectiveKind[] others)
        {
            double[] alternatives = others.Select(other => A(i, other)).ToArray();
            return A(i, kind) / (alternatives.Length == 0 ? 1.0 : alternatives.Average());
        }
        // Battlegrounds first: only some bots can go (level brackets), so RvR mustn't use them up (review 2026-10-17).
        Take(eAutonomousObjectiveKind.Battleground, open[3], i => canBattleground[i],
            i => Relative(i, eAutonomousObjectiveKind.Battleground, eAutonomousObjectiveKind.SoloPve, eAutonomousObjectiveKind.GroupPve));
        Take(eAutonomousObjectiveKind.RvR, open[2], _ => true,
            i => Relative(i, eAutonomousObjectiveKind.RvR, eAutonomousObjectiveKind.SoloPve, eAutonomousObjectiveKind.GroupPve));
        Take(eAutonomousObjectiveKind.GroupPve, open[1], _ => true,
            i => Relative(i, eAutonomousObjectiveKind.GroupPve, eAutonomousObjectiveKind.SoloPve));
        Take(eAutonomousObjectiveKind.SoloPve, open[0], _ => true, _ => 1.0);
        return plan;
    }

    /// <summary>The planner's type for a bot, or null when personalities don't apply to it.</summary>
    public static BotPersonalityType? PlannerType(GameBot bot) => Applies(bot) ? TypeFor(bot.DatabaseID) : null;

    /// <summary>
    /// The order a pass assigns its pool in: every planned bot first, then the rest (pool order kept within each).
    /// The rest then see counters that already include every planned slot, so they can't take a planned bot's slot
    /// (review 2026-10-17: interleaving let a goal run over its number).
    /// </summary>
    public static int[] PlannedFirst(eAutonomousObjectiveKind?[] plan) =>
        Enumerable.Range(0, plan.Length).Where(i => plan[i] != null).Concat(Enumerable.Range(0, plan.Length).Where(i => plan[i] == null)).ToArray();

    /// <summary>Dungeon preference as a percentage of today's (100 = unchanged).</summary>
    public static int DungeonPreferencePercent(BotPersonality personality) => personality.Type switch
    {
        BotPersonalityType.Daredevil => 170,
        BotPersonalityType.NoLifer => 150,
        BotPersonalityType.Explorer => 130,
        BotPersonalityType.Socialite => 100,
        BotPersonalityType.Leveler => 80,
        BotPersonalityType.Crafter => 60,
        _ => 100,
    };

    /// <summary>
    /// Extra weight (percent) for one legal outdoor camp, on top of the live-spawn weight:
    /// Daredevils like the harder cons of their legal range, Explorers like zones they haven't seen
    /// lately, Levelers like full camps. Never zero: every legal camp stays possible.
    /// </summary>
    public static int CampWeightPercent(BotPersonality personality, ConColor typicalCon, bool zoneIsFresh, int liveMobCount)
    {
        int weight = 100;
        int risk = personality.Traits.Risk;
        if (typicalCon >= ConColor.YELLOW)
            weight = weight * (personality.Type == BotPersonalityType.Daredevil ? 200 + risk * 15 : 60 + risk * 8) / 100;
        else if (typicalCon != ConColor.UNKNOWN && typicalCon <= ConColor.GREEN)
            weight = weight * (personality.Type == BotPersonalityType.Daredevil ? 50 : 100 + (9 - risk) * 5) / 100;
        if (zoneIsFresh)
            weight = weight * (personality.Type == BotPersonalityType.Explorer ? 250 + personality.Traits.Curiosity * 25 : 100 + personality.Traits.Curiosity * 5) / 100;
        if (personality.Type == BotPersonalityType.Leveler && liveMobCount >= 4)
            weight = weight * 140 / 100;
        return Math.Max(10, weight);
    }

    /// <summary>Talk-rate multiplier for ambient chat (1.0 = today).</summary>
    public static double ChatRateFactor(BotPersonality personality) => 0.25 + personality.Traits.Chattiness * 0.145;

    #endregion

    #region Live hooks (each returns today's behavior when personalities are off)

    public static bool RollTownBreak(GameBot bot, double roll) =>
        Applies(bot) ? roll >= 0 && roll < TownBreakChance(For(bot)) : AutonomousTownDowntime.RollAtTaskBoundary(roll);

    public static TimeSpan? ScaleTownBreak(GameBot bot, TimeSpan? duration, TimeSpan remaining)
    {
        if (duration == null || !Applies(bot))
            return duration;
        TimeSpan scaled = TimeSpan.FromTicks((long)(duration.Value.Ticks * TownBreakLengthFactor(For(bot))));
        if (scaled > remaining)
            scaled = remaining;
        return scaled < TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : scaled;
    }

    /// <summary>A goal from the owner's configured mix, tilted by personality.</summary>
    public static eAutonomousObjectiveKind ChooseGoal(GameBot bot, Func<eAutonomousObjectiveKind> draw, Random random = null)
    {
        eAutonomousObjectiveKind first = draw();
        if (!Applies(bot))
            return first;
        random ??= Random.Shared;
        return random.NextDouble() < RedrawChance(For(bot), first) ? draw() : first;
    }

    public static int DungeonPreferencePercent(GameBot bot) => Applies(bot) ? DungeonPreferencePercent(For(bot)) : 100;

    public static Func<AutonomousBotDecisionEngine.Camp, int> CampWeigher(GameBot bot)
    {
        if (!Applies(bot))
            return null;
        BotPersonality personality = For(bot);
        long botId = bot.DatabaseID;
        DateTime now = DateTime.UtcNow;
        return camp => CampWeightPercent(personality, camp.TypicalCon, IsZoneFresh(botId, camp.ZoneName, now), camp.LiveMobCount);
    }

    public static double ChatRateFactor(GameBot bot) =>
        (Applies(bot) ? ChatRateFactor(For(bot)) : 1.0) *
        (AutonomousBotVoices.Enabled ? RegularFactor(bot.DatabaseID) * Math.Clamp(Properties.BOT_VOICES_CHAT_RATE, 0.0, 3.0) : 1.0);

    /// <summary>
    /// With bot voices on, about one bot in seven is a "regular" who talks two and a half times as often, and
    /// about a third talk half as often (0.36-se.4). Real chat is uneven: in one study the median visitor said
    /// about 3 lines a day against a mean of 10-13, so the same few names come up, and players get to know them.
    /// Fixed from the bot's id.
    /// </summary>
    public static double RegularFactor(long botId)
    {
        ulong roll = Mix(botId, 7) % 100;
        return roll < 14 ? 2.5 : roll < 47 ? 0.5 : 1.0;
    }

    #endregion

    #region Zone memory (in memory; "lately" means since the server started, at most 32 zones)

    private static readonly ConcurrentDictionary<long, ZoneMemory> Memories = new();

    private sealed class ZoneMemory
    {
        private readonly Dictionary<string, DateTime> _visits = new(StringComparer.OrdinalIgnoreCase);

        public void Visit(string zone, DateTime now)
        {
            lock (_visits)
            {
                _visits[zone] = now;
                if (_visits.Count > ZoneMemoryCapacity)
                    _visits.Remove(_visits.OrderBy(pair => pair.Value).First().Key);
            }
        }

        public bool IsFresh(string zone, DateTime now)
        {
            lock (_visits)
                return !_visits.TryGetValue(zone, out DateTime last) || now - last >= ZoneFreshAfter;
        }
    }

    public static void RememberZone(GameBot bot, string zone)
    {
        if (!Applies(bot) || string.IsNullOrWhiteSpace(zone))
            return;
        Memories.GetOrAdd(bot.DatabaseID, _ => new ZoneMemory()).Visit(zone, DateTime.UtcNow);
    }

    public static bool IsZoneFresh(long botId, string zone, DateTime now) =>
        string.IsNullOrWhiteSpace(zone) || !Memories.TryGetValue(botId, out ZoneMemory memory) || memory.IsFresh(zone, now);

    /// <summary>Tests only: a memory with fixed times.</summary>
    public static void RememberZoneAt(long botId, string zone, DateTime when) =>
        Memories.GetOrAdd(botId, _ => new ZoneMemory()).Visit(zone, when);

    #endregion

    public static string Describe(BotPersonality personality) =>
        $"{personality.Type} (curiosity {personality.Traits.Curiosity}, risk {personality.Traits.Risk}, " +
        $"sociability {personality.Traits.Sociability}, patience {personality.Traits.Patience}, chattiness {personality.Traits.Chattiness})";
}
