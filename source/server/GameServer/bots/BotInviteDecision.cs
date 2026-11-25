using System;
using System.Collections.Concurrent;

namespace DOL.GS;

/// <summary>
/// Realism 3a (#485): how an autonomous gamebot answers a player's /invite, as a person would. One decision for the act
/// (BotGroupInvite.TryInvite) and for the words (the tell replies to "inv?" and "sent u an inv"), so the reasons a bot gives
/// in chat ("im 48 lol, way too high for you", "with my group atm", "sec, fighting") are the reasons the invite goes the way
/// it does. Pure, apart from the short memory of a bot-group answer (<see cref="Remember"/>).
/// <list type="bullet">
/// <item>More than <see cref="MaxLevelGap"/> levels apart: no (a friend the bot outlevels is power-levelled instead).</item>
/// <item>In its own bot group (owner, 2026-11-24): it sometimes leaves for the player, more often the less its group is doing;
/// mid-fight, never. Otherwise it says no and why. The answer holds for <see cref="BotGroupAnswerMs"/>, so asking again does not
/// reroll it.</item>
/// <item>Fighting alone: it finishes the fight first ("sec, fighting"), up to <see cref="AfterFightCapMs"/>.</item>
/// <item>Otherwise it accepts after a human pause that depends on what it is doing.</item>
/// </list>
/// Hired companions, /spawn helpers and an invite the bot itself sent never come here: they stay instant.
/// </summary>
public static class BotInviteDecision
{
    /// <summary>Further apart than this and the xp is wasted for one of them (the tell's LevelGapReply uses the same 8).</summary>
    public const int MaxLevelGap = 8;
    /// <summary>A bot fighting alone joins when the fight ends, or says no after this long.</summary>
    public const int AfterFightCapMs = 60_000;
    /// <summary>How often a bot finishing a fight looks again.</summary>
    public const int AfterFightPollMs = 2_000;
    /// <summary>A bot's answer about leaving its group is kept this long for the same player.</summary>
    public const long BotGroupAnswerMs = 10 * 60_000L;

    /// <summary>What the bot is doing when the invite comes.</summary>
    public enum Activity
    {
        /// <summary>Standing about, alone.</summary>
        Idle,
        /// <summary>In a town, at a merchant, the bank or a trainer.</summary>
        Town,
        /// <summary>Running or riding somewhere, alone.</summary>
        Travelling,
        /// <summary>Sitting to rest.</summary>
        Resting,
        /// <summary>In a fight, alone.</summary>
        Fighting,
        /// <summary>In its bot group, meeting up or choosing a camp.</summary>
        GroupGathering,
        /// <summary>In its bot group, on the way to a camp.</summary>
        GroupTravelling,
        /// <summary>In its bot group, at its camp between pulls.</summary>
        GroupGrinding,
        /// <summary>In its bot group, someone in it fighting.</summary>
        GroupFighting,
    }

    public enum Verdict
    {
        /// <summary>Joins after <see cref="Decision.DelayMs"/>.</summary>
        Accept,
        /// <summary>Joins when its fight ends (checked every <see cref="AfterFightPollMs"/>, at most <see cref="AfterFightCapMs"/>).</summary>
        AcceptAfterFight,
        /// <summary>Leaves its bot group and joins after <see cref="Decision.DelayMs"/>.</summary>
        LeaveGroupAndAccept,
        /// <summary>No: too far apart in level.</summary>
        DeclineLevelGap,
        /// <summary>No: stays with its bot group.</summary>
        DeclineBotGroup,
    }

    public readonly record struct Decision(Verdict Verdict, int DelayMs)
    {
        public bool Joins => Verdict is Verdict.Accept or Verdict.AcceptAfterFight or Verdict.LeaveGroupAndAccept;
    }

    /// <summary>The chance a bot leaves its own group for the player, by what the group is doing (a friend adds <see cref="FriendLeaveBonus"/>).</summary>
    public static double LeaveChance(Activity activity, bool friend)
    {
        double chance = activity switch
        {
            Activity.GroupGathering => 0.45,
            Activity.GroupTravelling => 0.30,
            Activity.GroupGrinding => 0.20,
            _ => 0,
        };
        return chance > 0 && friend ? Math.Min(1, chance + FriendLeaveBonus) : chance;
    }

    public const double FriendLeaveBonus = 0.25;

    /// <summary>The pause before "joins": reading the box, finishing a sentence, a click. Milliseconds, from a 0..1 roll.</summary>
    public static int AcceptDelayMs(Activity activity, double roll)
    {
        (int min, int max) = activity switch
        {
            Activity.Idle => (2_000, 6_000),
            Activity.Town => (3_000, 9_000),          // at a merchant or the bank: finish the window first
            Activity.Travelling => (4_000, 10_000),   // stop running, then click
            Activity.Resting => (2_500, 7_000),
            Activity.GroupGathering or Activity.GroupTravelling or Activity.GroupGrinding => (6_500, 15_000),   // a word to its group first
            _ => (2_000, 8_000),
        };
        return min + (int)Math.Round(Math.Clamp(roll, 0, 1) * (max - min));
    }

    /// <summary>
    /// The answer. <paramref name="leaveRoll"/> (0..1) decides whether a grouped bot leaves (below the chance: it leaves);
    /// <paramref name="delayRoll"/> (0..1) where in its range the pause falls. Pure.
    /// </summary>
    public static Decision Decide(int botLevel, int playerLevel, Activity activity, bool friend, double leaveRoll, double delayRoll)
    {
        int gap = botLevel - playerLevel;
        // A friend the bot outlevels gets power-levelled ("ill pl u for a bit"); anyone else hears about the gap.
        if (Math.Abs(gap) > MaxLevelGap && !(friend && gap > 0))
            return new Decision(Verdict.DeclineLevelGap, 0);
        if (activity == Activity.GroupFighting)
            return new Decision(Verdict.DeclineBotGroup, 0);
        if (activity is Activity.GroupGathering or Activity.GroupTravelling or Activity.GroupGrinding)
            return leaveRoll < LeaveChance(activity, friend)
                ? new Decision(Verdict.LeaveGroupAndAccept, AcceptDelayMs(activity, delayRoll))
                : new Decision(Verdict.DeclineBotGroup, 0);
        if (activity == Activity.Fighting)
            return new Decision(Verdict.AcceptAfterFight, AcceptDelayMs(Activity.Idle, delayRoll));
        return new Decision(Verdict.Accept, AcceptDelayMs(activity, delayRoll));
    }

    /// <summary>What a bot is doing, from its state. Pure.</summary>
    public static Activity ActivityOf(bool inBotGroup, bool groupFighting, string groupPhase, bool inCombat, bool resting, bool moving, bool inTown)
    {
        if (inBotGroup)
            return groupFighting || inCombat ? Activity.GroupFighting
                : groupPhase == "Traveling" ? Activity.GroupTravelling
                : groupPhase == "Grinding" ? Activity.GroupGrinding
                : Activity.GroupGathering;
        return inCombat ? Activity.Fighting
            : resting ? Activity.Resting
            : moving ? Activity.Travelling
            : inTown ? Activity.Town
            : Activity.Idle;
    }

    // A grouped bot's roll for one player, kept for BotGroupAnswerMs: asking again, by tell or by invite, gets the same answer.
    private static readonly ConcurrentDictionary<(long Bot, string Player), (double Roll, long At)> Rolls = new();

    /// <summary>The bot's leave roll for this player now (a new one after <see cref="BotGroupAnswerMs"/>). Times are game-loop ms.</summary>
    public static double Remember(long botId, string player, long now, Func<double> roll)
    {
        var key = (botId, player ?? string.Empty);
        if (Rolls.TryGetValue(key, out var held) && now - held.At >= 0 && now - held.At < BotGroupAnswerMs)
            return held.Roll;
        double fresh = roll();
        Rolls[key] = (fresh, now);
        if (Rolls.Count > 512)
            foreach (var stale in Rolls)
                if (now - stale.Value.At >= BotGroupAnswerMs)
                    Rolls.TryRemove(stale.Key, out _);
        return fresh;
    }

    /// <summary>Forgets every held answer (tests).</summary>
    public static void Reset() => Rolls.Clear();

    /// <summary>What the bot says by tell when it turns the invite down for its group. Null for other verdicts. Pure.</summary>
    public static string BotGroupDeclineLine(Activity activity, bool friend, Random random)
    {
        string line = activity == Activity.GroupFighting
            ? Pick(random, "mid pull with my grp, sry", "cant, my group is fighting atm", "in a fight with my grp, sry")
            : activity == Activity.GroupTravelling
                ? Pick(random, "with my group atm, were heading out. sry", "cant leave my grp, were on the way to camp")   // promise: none (its group travels, the bot promises nothing)
                : Pick(random, "in a grp already, sry", "with my group atm, cant just leave them", "sry, grouped up already");
        return friend ? line + Pick(random, ", catch u after?", ". next time tho") : line;
    }

    /// <summary>What a bot that leaves its group for the player may say first (half the time nothing). Pure.</summary>
    public static string LeavingLine(bool friend, Random random) =>
        random.Next(2) == 0 ? null
        : friend ? Pick(random, "for you? sure, leaving my grp", "ok ditching these guys lol, sec")
        : Pick(random, "sure, sec, leaving my grp", "ok, one sec");

    /// <summary>A friend the bot outlevels by more than the gap: it comes along anyway. Pure.</summary>
    public static string PowerLevelLine(Random random) =>
        Pick(random, "ill pl u for a bit", "sure, ill tag along and keep u alive", "ok, ill come help for a bit");

    /// <summary>A bot fighting alone, answering the box. Pure. promise: true (it joins when the fight ends, or says it could not).</summary>
    public static string FightingLine(Random random) => Pick(random, "sec, fighting", "one sec, finishing this mob", "sec, almost dead mob");

    /// <summary>The fight went on past <see cref="AfterFightCapMs"/>. Pure.</summary>
    public static string FightTooLongLine(Random random) => Pick(random, "sry, this fight is taking forever. inv again in a bit?", "still fighting, inv me again after");

    private static string Pick(Random random, params string[] lines) => lines[random.Next(lines.Length)];
}
