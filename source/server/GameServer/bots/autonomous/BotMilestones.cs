using System;
using System.Collections.Generic;
using System.Globalization;

namespace DOL.GS;

/// <summary>The personal goals a gamebot chases in 1.2 (docs/BOTS-AS-PEOPLE-1.2.md, item 6; owner decisions 2 and 3).</summary>
public enum BotMilestoneArc
{
    None,
    /// <summary>The ding to 50: taken at level 45 to 49, reached at level 50.</summary>
    Ding50,
    /// <summary>Realm rank 5 (RR5L0, 513,500 realm points): a level 50 with an RvR leaning.</summary>
    RealmRank5,
    /// <summary>A dragon trophy: an item the bot really received from a dragon or epic-boss raid's loot (decision 3).</summary>
    DragonTrophy,
}

/// <summary>Where a milestone line is said, in the order a bot tries them.</summary>
public enum BotMilestoneChannel { None, Group, Say, Guild, Faction }

/// <summary>
/// Bots as people for 1.2, work package 6: the rules of personal milestones (pure, no game world; UT_BotMilestones). One
/// arc at a time per gamebot, chosen when it fits and kept until it is reached or no longer possible; its progress comes
/// from the bot's real numbers only (experience, realm points, the raid kills it was really at), so every line a bot says
/// about it is true. Epic armour is not an arc (owner decision 2: deferred to 2.0). The arc leans the next goal (decision
/// 10): within 2 levels of 50 a bot takes fewer town breaks; past halfway to realm rank 5 it is likelier to take an RvR
/// slot of the realm's exact goal mix. The pacing of the talk is our design (no period source measures it).
/// </summary>
public static class BotMilestones
{
    public const string Ding50Id = "ding50";
    public const string RealmRank5Id = "rr5";
    public const string DragonTrophyId = "dragon_trophy";

    /// <summary>The level a bot starts the ding to 50 at (the design: level 45 and up).</summary>
    public const int DingFromLevel = 45;
    public const int MaxLevel = 50;
    /// <summary>Realm rank 5 as a realm level (RR5L0 = 40; GameBot.RealmLevel, rank = level / 10 + 1).</summary>
    public const int RealmRank5Level = 40;

    /// <summary>The talk points, in percent of the way: about halfway and nearly there (the design: 50% and 90%).</summary>
    public static readonly int[] TalkPoints = [50, 90];

    /// <summary>The dragon trophy's talk points, as raid kills without a drop: the 2nd and the 5th. Our design.</summary>
    public static readonly int[] TrophyTalkKills = [2, 5];

    public static string Id(BotMilestoneArc arc) => arc switch
    {
        BotMilestoneArc.Ding50 => Ding50Id,
        BotMilestoneArc.RealmRank5 => RealmRank5Id,
        BotMilestoneArc.DragonTrophy => DragonTrophyId,
        _ => string.Empty,
    };

    public static BotMilestoneArc Parse(string id) => id switch
    {
        Ding50Id => BotMilestoneArc.Ding50,
        RealmRank5Id => BotMilestoneArc.RealmRank5,
        DragonTrophyId => BotMilestoneArc.DragonTrophy,
        _ => BotMilestoneArc.None,
    };

    public static readonly BotMilestoneArc[] Arcs = [BotMilestoneArc.Ding50, BotMilestoneArc.RealmRank5, BotMilestoneArc.DragonTrophy];

    // ---- the numbers (the server's own tables) ------------------------------------------------------------------------

    /// <summary>The experience a character has on reaching <paramref name="level"/> (GamePlayer's table, as GameBot levels).</summary>
    public static long ExperienceAt(int level) => level <= 1 ? 0 : GamePlayer.GetExperienceAmountForLevel(level - 1);

    /// <summary>The realm points of realm rank 5 (GamePlayer.REALMPOINTS_FOR_LEVEL[40] = 513,500).</summary>
    public static long RealmRank5Points => GamePlayer.REALMPOINTS_FOR_LEVEL[RealmRank5Level];

    /// <summary>How far along the ding to 50 is, 0 to 1: the experience from level 45 to level 50.</summary>
    public static double DingProgress(int level, long experience)
    {
        if (level >= MaxLevel)
            return 1;
        long from = ExperienceAt(DingFromLevel), to = ExperienceAt(MaxLevel);
        return Math.Clamp((double)(experience - from) / (to - from), 0, 1);
    }

    /// <summary>How far along realm rank 5 is, 0 to 1: the realm points out of 513,500.</summary>
    public static double RankProgress(long realmPoints) => Math.Clamp((double)realmPoints / RealmRank5Points, 0, 1);

    /// <summary>Bubbles left to 50 (1-10) for a level 49, as players counted them (a tenth of the level each); 0 otherwise.</summary>
    public static int BubblesLeft(int level, long experience)
    {
        if (level != MaxLevel - 1)
            return 0;
        long from = ExperienceAt(MaxLevel - 1), to = ExperienceAt(MaxLevel);
        double left = Math.Clamp((double)(to - experience) / (to - from), 0, 1);
        return Math.Clamp((int)Math.Ceiling(left * 10 - 1e-9), 1, 10);
    }

    /// <summary>Realm points left to realm rank 5, in thousands, rounded up (at least 1 while not there).</summary>
    public static long RankThousandsLeft(long realmPoints) =>
        realmPoints >= RealmRank5Points ? 0 : Math.Max(1, (RealmRank5Points - realmPoints + 999) / 1000);

    /// <summary>The highest talk point (0, 50 or 90) an arc has passed. The trophy counts raid kills without a drop.</summary>
    public static int PointOf(BotMilestoneArc arc, double progress, int raidKills)
    {
        if (arc == BotMilestoneArc.DragonTrophy)
            return raidKills >= TrophyTalkKills[1] ? TalkPoints[1] : raidKills >= TrophyTalkKills[0] ? TalkPoints[0] : 0;
        int point = 0;
        foreach (int p in TalkPoints)
            if (progress * 100 >= p - 1e-9)
                point = p;
        return point;
    }

    /// <summary>The progress of an arc from the bot's numbers (the trophy has none: it is won or not).</summary>
    public static double Progress(BotMilestoneArc arc, int level, long experience, long realmPoints) => arc switch
    {
        BotMilestoneArc.Ding50 => DingProgress(level, experience),
        BotMilestoneArc.RealmRank5 => RankProgress(realmPoints),
        _ => 0,
    };

    // ---- choosing ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// An RvR leaning: Daredevils and No-lifers (the types that go to RvR first, AutonomousBotPersonality.GoalAffinity), and
    /// any bot with Risk 7 or more. With no personality (bot_personalities_enabled off) 30% of bots by a fixed roll.
    /// </summary>
    public static bool LeansRvr(BotPersonality? personality, long id) => personality is { } p
        ? p.Type is BotPersonalityType.Daredevil or BotPersonalityType.NoLifer || p.Traits.Risk >= 7
        : Roll(id, "MRVR") < 0.30;

    /// <summary>
    /// A raid leaning: Socialites and No-lifers (big groups, the endgame), curious Explorers (8+), sociable Daredevils (5+) and
    /// patient Levelers (8+); never a Crafter. With no personality, 50% of bots by a fixed roll.
    /// </summary>
    public static bool LeansRaid(BotPersonality? personality, long id) => personality is { } p
        ? p.Type switch
        {
            BotPersonalityType.Socialite or BotPersonalityType.NoLifer => true,
            BotPersonalityType.Explorer => p.Traits.Curiosity >= 8,
            BotPersonalityType.Daredevil => p.Traits.Sociability >= 5,
            BotPersonalityType.Leveler => p.Traits.Patience >= 8,
            _ => false,
        }
        : Roll(id, "MRAI") < 0.50;

    /// <summary>
    /// The arc a bot takes now, or None: the ding to 50 from level 45 (unless reached); at level 50 realm rank 5 (an RvR
    /// leaning, below realm rank 5) or a dragon trophy (a raid leaning), never one already reached. A bot with both
    /// leanings takes one by a fixed roll of its id, and the other once that one is reached. <paramref name="stateOf"/>
    /// gives an arc's saved state (OfflineBotMilestoneRecord.State) or null. Pure.
    /// </summary>
    public static BotMilestoneArc Choose(int level, int realmLevel, BotPersonality? personality, long id, Func<BotMilestoneArc, string> stateOf)
    {
        bool Open(BotMilestoneArc arc) => stateOf?.Invoke(arc) != OfflineBotMilestoneRecord.Reached;
        if (level < DingFromLevel)
            return BotMilestoneArc.None;
        if (level < MaxLevel)
            return Open(BotMilestoneArc.Ding50) ? BotMilestoneArc.Ding50 : BotMilestoneArc.None;
        bool rank = LeansRvr(personality, id) && realmLevel < RealmRank5Level && Open(BotMilestoneArc.RealmRank5);
        bool trophy = LeansRaid(personality, id) && Open(BotMilestoneArc.DragonTrophy);
        if (rank && trophy)
            return Roll(id, "MPIK") < 0.5 ? BotMilestoneArc.RealmRank5 : BotMilestoneArc.DragonTrophy;
        return rank ? BotMilestoneArc.RealmRank5 : trophy ? BotMilestoneArc.DragonTrophy : BotMilestoneArc.None;
    }

    /// <summary>Whether an active arc can still be reached: the ding needs level 45-49, the others level 50. Pure.</summary>
    public static bool StillPossible(BotMilestoneArc arc, int level) => arc switch
    {
        BotMilestoneArc.Ding50 => level >= DingFromLevel,
        BotMilestoneArc.RealmRank5 or BotMilestoneArc.DragonTrophy => level >= MaxLevel,
        _ => false,
    };

    /// <summary>Whether the arc is reached: level 50, realm rank 5, or a raid item really received. Pure.</summary>
    public static bool Reached(BotMilestoneArc arc, int level, int realmLevel, bool wonRaidItem) => arc switch
    {
        BotMilestoneArc.Ding50 => level >= MaxLevel,
        BotMilestoneArc.RealmRank5 => realmLevel >= RealmRank5Level,
        BotMilestoneArc.DragonTrophy => wonRaidItem,
        _ => false,
    };

    // ---- the small push (owner decision 10) ----------------------------------------------------------------------------

    /// <summary>
    /// The town-break chance factor of a bot chasing 50: from level 48 (within 2 levels of 50) 0.4 to 0.6 (fixed by its id),
    /// else 1. The break still happens, only less often. Pure.
    /// </summary>
    public static double TownBreakFactor(BotMilestoneArc arc, int level, long id) =>
        arc == BotMilestoneArc.Ding50 && level >= MaxLevel - 2 && level < MaxLevel ? 0.4 + 0.2 * Roll(id, "MTWN") : 1.0;

    /// <summary>
    /// How much more a bot chasing realm rank 5 wants an RvR slot of the realm's mix (AutonomousBotPersonality.PlanGoals):
    /// x1.5 past halfway, x2 past 90%, else 1. Only who takes the slots changes, never how many. Pure.
    /// </summary>
    public static double RvrLean(BotMilestoneArc arc, double progress) =>
        arc != BotMilestoneArc.RealmRank5 ? 1.0 : progress >= 0.9 ? 2.0 : progress >= 0.5 ? 1.5 : 1.0;

    // ---- words ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The corpus topic (milestones.lines, kind "milestone") for a talk point: "ding50_half", "ding50_bubbles" (a level 49
    /// with 2-9 bubbles left), "ding50_close", "rr5_half", "rr5_close", "trophy_try" and "trophy_long". Pure.
    /// </summary>
    public static string Topic(BotMilestoneArc arc, int point, int bubblesLeft) => (arc, point >= TalkPoints[1]) switch
    {
        (BotMilestoneArc.Ding50, false) => "ding50_half",
        (BotMilestoneArc.Ding50, true) => bubblesLeft is >= 2 and <= 9 ? "ding50_bubbles" : "ding50_close",
        (BotMilestoneArc.RealmRank5, false) => "rr5_half",
        (BotMilestoneArc.RealmRank5, true) => "rr5_close",
        (BotMilestoneArc.DragonTrophy, false) => "trophy_try",
        (BotMilestoneArc.DragonTrophy, true) => "trophy_long",
        _ => null,
    };

    /// <summary>The topic of the moment itself: "rr5_reached"; "trophy_reached", or "trophy_reached_wait" when the bot was
    /// at one or more big kills without a drop before (only then may a line say "finally"); null for the ding to 50, whose
    /// moment the existing ding line, its /faction story and the grats chain already are.</summary>
    public static string ReachedTopic(BotMilestoneArc arc, int dryKills = 0) => arc switch
    {
        BotMilestoneArc.RealmRank5 => "rr5_reached",
        BotMilestoneArc.DragonTrophy => dryKills >= 1 ? "trophy_reached_wait" : "trophy_reached",
        _ => null,
    };

    /// <summary>
    /// Whether a high-end raid kill is a big kill for the trophy: a dragon, an epic boss or a raid's final boss, never a
    /// boss's add. Adds are the game's GameEpicNPC (Xanxicar's champions, shades, rains of fire); bosses are GameEpicBoss
    /// or a class a raid names as its boss. <paramref name="epicAdd"/>: the victim is a GameEpicNPC;
    /// <paramref name="raidBossType"/>: a raid names its class as its boss or final boss. Pure.
    /// </summary>
    public static bool IsBigKill(bool epicAdd, bool raidBossType) => !epicAdd || raidBossType;

    /// <summary>What the realm's reactions answer (reply lines' @answers, then the general good news).</summary>
    public static string[] ReactionAnswers(BotMilestoneArc arc) => arc switch
    {
        BotMilestoneArc.RealmRank5 => ["milestone_rr5", "player_goodnews"],
        BotMilestoneArc.DragonTrophy => ["milestone_trophy", "player_goodnews"],
        _ => ["player_goodnews"],
    };

    /// <summary>A fixed roll in [0, 1) for an id and a salt (AutonomousBotPersonality.Mix). Pure.</summary>
    public static double Roll(long id, string salt)
    {
        ulong s = 0;
        foreach (char c in salt ?? string.Empty)
            s = s * 31 + c;
        return (AutonomousBotPersonality.Mix(id, s) >> 11) * (1.0 / (1UL << 53));
    }

    public static string Percent(double progress) => (progress * 100).ToString("0", CultureInfo.InvariantCulture);
}

/// <summary>
/// When a milestone line may be said (WP6, pure; UT_BotMilestones). A bot passes each talk point once (saved in the row, so
/// a restart never repeats one); whether it says so is a fixed roll by its chattiness (35% to 80%), the rest pass in
/// silence. A point nobody can hear waits (retried at most every 10 minutes), and a later point replaces an earlier one
/// that was never said ("over halfway" stays true). A bot that is fighting when its point is due tries again on a timer
/// (15-30 s, at most 8 times). The realm's /faction hears at most one milestone line every 5 minutes, its group, /say and
/// guild channels one a minute; the channel's own ceiling (AutonomousBotChatCoordinator.BroadcastFaction) and the chat
/// budgets still hold.
/// </summary>
public static class BotMilestoneTalk
{
    public const long RetryMs = 10 * 60_000L;
    public const long FactionGapMs = 5 * 60_000L;
    /// <summary>A realm's milestone lines outside /faction (group, /say, guild): at least a minute apart, so several bots near
    /// the player never say theirs within seconds.</summary>
    public const long LocalGapMs = 60_000L;
    /// <summary>A bot that is fighting, casting or dead when its point is due tries again after 15-30 s, at most this many
    /// times (about 3 minutes), then waits <see cref="RetryMs"/>.</summary>
    public const int MaxBusyTries = 8;
    /// <summary>A bot's own lines about its arc: at least 15 minutes apart.</summary>
    public const long OwnGapMs = 15 * 60_000L;

    /// <summary>The chance a bot says it at a talk point: 35% plus 5% a point of chattiness (0-9). Pure.</summary>
    public static double TalkChance(int chattiness) => 0.35 + 0.05 * Math.Clamp(chattiness, 0, 9);

    /// <summary>Whether the bot will say this talk point (a fixed roll of id, arc and point). Pure.</summary>
    public static bool WillTalk(long id, BotMilestoneArc arc, int point, int chattiness) =>
        BotMilestones.Roll(id, $"MLST{(int)arc}:{point}") < TalkChance(chattiness);

    /// <summary>Whether a passed point is due for its decision now: above the saved point, and past the bot's wait. Pure.</summary>
    public static bool Due(int point, int savedPoint, long nowMs, long notBeforeMs) => point > savedPoint && nowMs >= notBeforeMs;

    /// <summary>
    /// Where a real player can hear it, in order: a talk point goes to the group with the player, /say near the player,
    /// the guild, then /faction (when its 5-minute gap is open); the moment itself goes to /faction first (it is realm
    /// news), then group, guild and /say. None when nobody would hear. Pure.
    /// </summary>
    public static BotMilestoneChannel Pick(bool moment, bool groupHasPlayer, bool playerHears, bool guildListens, bool factionAudience,
        bool factionGapOpen, bool localGapOpen = true)
    {
        bool faction = factionAudience && factionGapOpen;
        if (!localGapOpen)
            return faction ? BotMilestoneChannel.Faction : BotMilestoneChannel.None;
        if (moment && faction)
            return BotMilestoneChannel.Faction;
        if (groupHasPlayer)
            return BotMilestoneChannel.Group;
        if (moment)
            return guildListens ? BotMilestoneChannel.Guild : playerHears ? BotMilestoneChannel.Say : BotMilestoneChannel.None;
        if (playerHears)
            return BotMilestoneChannel.Say;
        if (guildListens)
            return BotMilestoneChannel.Guild;
        return faction ? BotMilestoneChannel.Faction : BotMilestoneChannel.None;
    }

    /// <summary>Whether the realm's /faction gap is open at <paramref name="nowMs"/> (last line at <paramref name="lastMs"/>). Pure.</summary>
    public static bool FactionGapOpen(long nowMs, long lastMs) => nowMs - lastMs >= FactionGapMs;

    /// <summary>Whether the realm's gap for group, /say and guild lines is open. Pure.</summary>
    public static bool LocalGapOpen(long nowMs, long lastMs) => nowMs - lastMs >= LocalGapMs;

    /// <summary>The wait before a busy bot (fighting, casting, dead) tries its line again: 15-30 s. Our design.</summary>
    public static int BusyRetryMs(Random random) => 15_000 + random.Next(15_001);

    /// <summary>Whether a busy bot tries again on its timer (<paramref name="tries"/> so far) or gives up for <see cref="RetryMs"/>. Pure.</summary>
    public static bool RetriesWhenBusy(int tries) => tries < MaxBusyTries;

    public static string ChannelName(BotMilestoneChannel channel) => channel switch
    {
        BotMilestoneChannel.Group => "group",
        BotMilestoneChannel.Say => "say",
        BotMilestoneChannel.Guild => "guild",
        BotMilestoneChannel.Faction => "faction",
        _ => "none",
    };

    /// <summary>The delay before the line is typed: 2-6 s (a person reads their xp bar, then types). Our design.</summary>
    public static int TypingDelayMs(Random random) => 2_000 + random.Next(4_000);

    /// <summary>
    /// BOT_MILESTONE_SUMMARY seconds= active=ding50:N,rr5:N,dragon_trophy:N chosen= dropped= points= (talk points handled:
    /// said or passed in silence) silent= talks=group:N,say:N,guild:N,faction:N waiting= (nobody hears, or the channel's
    /// gap) busy= (retries while fighting) held= reached=ding50:N,rr5:N,dragon_trophy:N raidKills= (big kills, one each)
    /// trophies= unsought= (won while chasing something else) folded= (moments not said: another winner spoke) lines=
    /// suppressed=. Pure.
    /// </summary>
    public static string SummaryLine(long seconds, IReadOnlyDictionary<string, long> counts, int[] active)
    {
        long C(string key) => counts != null && counts.TryGetValue(key, out long n) ? n : 0;
        int A(int i) => active != null && i < active.Length ? active[i] : 0;
        return $"BOT_MILESTONE_SUMMARY seconds={seconds} active=ding50:{A(0)},rr5:{A(1)},dragon_trophy:{A(2)} " +
               $"chosen={C("chosen")} dropped={C("dropped")} points={C("points")} silent={C("silent")} " +
               $"talks=group:{C("talk.group")},say:{C("talk.say")},guild:{C("talk.guild")},faction:{C("talk.faction")} " +
               $"waiting={C("waiting")} busy={C("busy")} held={C("held")} " +
               $"reached=ding50:{C("reached.ding50")},rr5:{C("reached.rr5")},dragon_trophy:{C("reached.dragon_trophy")} " +
               $"raidKills={C("raidKills")} trophies={C("trophies")} unsought={C("unsought")} folded={C("folded")} " +
               $"lines={C("lines")} suppressed={C("suppressed")}";
    }
}
