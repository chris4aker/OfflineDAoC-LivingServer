using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using DOL.AI.Brain;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS;

/// <summary>What a gamebot does on a trip to a place (decided once, when the trip starts).</summary>
public enum BotWayChoice : byte
{
    /// <summary>It knows the way (or knows it anyway: a guide, a guildmate): today's route.</summary>
    Knows,
    /// <summary>It asks the way in /say or /faction; a realm-mate that knows the place answers, and it then half knows it.</summary>
    Ask,
    /// <summary>It tags along behind a realm-mate going to the same zone, then leaves it at the zone line.</summary>
    Follow,
    /// <summary>It takes another branch of the route network for a short way, looks around, and comes back.</summary>
    WrongRoad,
}

/// <summary>Why a wrong road is not taken here and now (the exclusions of owner decision 7, and the trip's own state).</summary>
public enum BotWrongRoadRefusal : byte
{
    None,
    SwitchedOff,
    /// <summary>Once a trip at most (owner decision 7).</summary>
    UsedThisTrip,
    StableRide,
    /// <summary>A group takes its leader's way; in 1.2 no group takes a wrong road (stricter than the design: see the report).</summary>
    Grouped,
    /// <summary>Any frontier or battleground (decision 7 says an enemy frontier; ours is stricter: enemies roam our own too).</summary>
    Frontier,
    Dungeon,
    /// <summary>An aggressive monster more than 2 levels above the bot within 1,500 units of the wrong leg.</summary>
    Threat,
    /// <summary>Hurt or just back from a death: a habit may cost time, never a character.</summary>
    Hurt,
    /// <summary>Not walking a trip of its own right now (standing, riding, in a town port, on a group's errand).</summary>
    NotWalking,
    /// <summary>No other branch of the route network here (or none proven both ways).</summary>
    NoBranch,
}

/// <summary>What became of a bot's question (BOT_WAY_ASK said=).</summary>
public enum BotWayAskOutcome : byte
{
    /// <summary>Said in /faction or /say.</summary>
    Said,
    /// <summary>The realm asked in /faction less than 10 minutes ago (owner decision 7) and nobody near can hear /say.</summary>
    Capped,
    /// <summary>No real player could hear it (nobody of the realm online for /faction, nobody near for /say).</summary>
    Unheard,
    /// <summary>The channel is at its ceiling or has no room for the answer (the chat budgets).</summary>
    Held,
    /// <summary>Bot voices or the switch are off, or the bot can't talk now.</summary>
    Off,
}

/// <summary>The shares (in percent, summing to 100) of the four choices for one tier and knowledge.</summary>
public readonly record struct BotWayShares(int Knows, int Ask, int Follow, int WrongRoad)
{
    public int Total => Knows + Ask + Follow + WrongRoad;
}

/// <summary>A group's part in the choice: groups take their leader's way, and if no member knows, the leader asks.</summary>
public readonly record struct BotWayGroup(bool InGroup, bool IsLeader, bool AnyMemberKnows)
{
    public static BotWayGroup Solo => default;
}

/// <summary>The facts the wrong-road exclusions read, as plain values (tests build their own).</summary>
public readonly record struct BotWrongRoadFacts(bool SwitchOn, bool UsedThisTrip, bool StableRide, bool InGroup, bool InFrontier,
    bool InDungeon, bool Threatened, int HealthPercent, bool DiedLately, bool Walking);

/// <summary>A wrong leg: which anchor it heads for, how far the bot goes before it stops, and how long it looks around.</summary>
public readonly record struct BotWrongLeg(int Anchor, float Length, int LookMilliseconds)
{
    public bool IsNone => Anchor < 0;

    public static BotWrongLeg None => new(-1, 0, 0);
}

/// <summary>
/// Bots as people, item 4, work package 4 (docs/BOTS-AS-PEOPLE-1.2.md, "Knowing only what a player would know"): a gamebot
/// heading somewhere it does not know (WP3's <see cref="BotPlaceKnowledge"/>) behaves like a 2003 player. Decided once,
/// when the trip starts, by its skill tier: it knows the way anyway (a guide, a guildmate), asks the way (the Travel scene's
/// "how do i get to ..." becomes its real question; only a realm-mate that knows the place answers, truthfully, and the asker
/// then half knows it), tags along behind a realm-mate going there, or takes a wrong road: another branch of the route network
/// (<see cref="AutonomousZoneRoads"/>) for 600-1,500 units, a look around, and back to where it left its route.
///
/// Owner decision 7: a wrong road at most once a trip and 3 minutes, only on proven route legs, never in a frontier, a dungeon
/// or near an aggressive monster more than 2 levels above; one ask in /faction per realm every 10 minutes. A wrong road never
/// strands a bot: both directions of the leg are proven before it starts, and it ends back at the point it left (or, at the
/// 3-minute cap or on any failed order, hands the walk back to the controller, which plans from where the bot stands on that
/// proven ground). Nothing here calls the pathfinder per tick: one order per leg, two corridor checks per wrong road.
///
/// The rules are pure (<see cref="Shares"/>, <see cref="Choose"/>, <see cref="WrongRoadRefusal"/>, <see cref="WrongBranch"/>,
/// <see cref="LegFor"/>, <see cref="Answer{T}"/>, <see cref="FollowStep"/>, <see cref="BotWayAskCap"/>); the live part asks
/// <see cref="BotPeopleScope"/> (the WayFinding rule) and the switches first. Autonomous gamebots only: companions, real players,
/// raids, the player's group and stable rides keep today's route.
/// </summary>
public static class BotWayFinding
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    // ---- values (design values, BOTS-AS-PEOPLE-1.2 item 4; owner decision 7 for the caps) --------------------------------

    /// <summary>Owner decision 7: a wrong road costs at most this much of a trip.</summary>
    public const long WrongRoadCapMilliseconds = 180_000;

    /// <summary>Owner decision 7: one ask in /faction per realm in this time.</summary>
    public const long FactionAskGapMilliseconds = 10 * 60_000;

    /// <summary>The wrong leg's length: 600 to 1,500 units.</summary>
    public const float WrongLegMinimum = 600f;

    public const float WrongLegMaximum = 1_500f;

    /// <summary>The bot stops this far short of the anchor it heads for at most (the leg stays on the proven corridor).</summary>
    public const float WrongLegAnchorMargin = 150f;

    /// <summary>A branch is another way: 60 to 150 degrees off the way to the goal (not the right road, not straight back).</summary>
    public const float BranchMinDegrees = 60f;

    public const float BranchMaxDegrees = 150f;

    /// <summary>The anchors a branch may head for (the route network's cells are 2,048 units).</summary>
    public const float BranchMinDistance = WrongLegMinimum + WrongLegAnchorMargin;

    public const float BranchMaxDistance = 5_000f;

    /// <summary>The look around at the end of the wrong leg.</summary>
    public const int LookMinMilliseconds = 4_000;

    public const int LookMaxMilliseconds = 8_000;

    /// <summary>A wrong road is only taken this long into a trip, and no later than this (else the trip goes on as today).</summary>
    public const long WrongRoadEarliestMilliseconds = 15_000;

    public const long WrongRoadLatestMilliseconds = 10 * 60_000;

    /// <summary>The checks of a pending wrong road (cheap ones every time; the monster scan and corridor checks a few times a trip).</summary>
    public const long PendingCheckMilliseconds = 2_000;

    public const int WrongRoadAttemptsPerTrip = 4;

    /// <summary>Hurt below this, or dead within the last minute: no wrong road.</summary>
    public const int WrongRoadMinimumHealth = 80;

    /// <summary>A death this soon after a wrong road counts in BOT_WAY_SUMMARY's deaths_60s.</summary>
    public const long DeathWindowMilliseconds = 60_000;

    /// <summary>Tagging along: a leader within this distance, followed at 300-600 units, for at most 10 minutes.</summary>
    public const float FollowSearchRadius = 2_500f;

    public const float FollowNearest = 300f;

    public const float FollowFarthest = 600f;

    /// <summary>A leader farther than this is lost; the bot goes on alone.</summary>
    public const float FollowLostDistance = 3_000f;

    public const long FollowMaxMilliseconds = 10 * 60_000;

    /// <summary>A leader at most this many levels above or below the follower: what it walks past is the follower's own con.</summary>
    public const int FollowLevelGap = 2;

    /// <summary>The design's search budget for tagging along: 4 searches a second server-wide.</summary>
    public const int FollowSearchesPerSecond = 4;

    /// <summary>BOT_WAY_* event lines at most per minute server-wide (the rest are counted in BOT_WAY_SUMMARY).</summary>
    public const int EventLinesPerMinute = 30;

    // ---- the rules (pure) -------------------------------------------------------------------------------------------

    /// <summary>
    /// The design's table for a place it does not know: Casual 40/30/15/15, Average 55/25/12/8, Veteran 75/15/8/2 (knows,
    /// asks, tags along, wrong road). A half-known place (told the way) asks no more and halves the wrong road; what it gives
    /// up goes to "knows". A known place is always known. A switch that is off gives its shares to "knows". Pure.
    /// </summary>
    public static BotWayShares Shares(RvrSkillTier tier, BotPlaceKnowing knowing, bool askOn, bool wrongRoadsOn)
    {
        if (knowing == BotPlaceKnowing.Known)
            return new BotWayShares(100, 0, 0, 0);
        (int ask, int follow, int wrong) = tier switch
        {
            RvrSkillTier.Casual => (30, 15, 15),
            RvrSkillTier.Veteran => (15, 8, 2),
            _ => (25, 12, 8),
        };
        if (knowing == BotPlaceKnowing.HalfKnown)
        {
            ask = 0;
            wrong /= 2;
        }
        if (!askOn)
            ask = follow = 0;
        if (!wrongRoadsOn)
            wrong = 0;
        return new BotWayShares(100 - ask - follow - wrong, ask, follow, wrong);
    }

    /// <summary>
    /// The choice for a trip: by the shares (<paramref name="roll"/> 0 to 1), except in a group. A member takes its leader's
    /// way; a leader knows the way when any member knows it, and otherwise asks (when asking is on). Pure.
    /// </summary>
    public static BotWayChoice Choose(BotPlaceKnowing knowing, RvrSkillTier tier, BotWayGroup group, bool askOn, bool wrongRoadsOn,
        double roll)
    {
        if (knowing == BotPlaceKnowing.Known)
            return BotWayChoice.Knows;
        if (group.InGroup)
            return !group.IsLeader || group.AnyMemberKnows || !askOn || knowing == BotPlaceKnowing.HalfKnown
                ? BotWayChoice.Knows
                : BotWayChoice.Ask;
        BotWayShares shares = Shares(tier, knowing, askOn, wrongRoadsOn);
        double point = Math.Clamp(roll, 0, 0.999999) * 100;
        if (point < shares.WrongRoad)
            return BotWayChoice.WrongRoad;
        if (point < shares.WrongRoad + shares.Ask)
            return BotWayChoice.Ask;
        if (point < shares.WrongRoad + shares.Ask + shares.Follow)
            return BotWayChoice.Follow;
        return BotWayChoice.Knows;
    }

    /// <summary>The first exclusion that stops a wrong road (owner decision 7 and the trip's state), or None. Pure.</summary>
    public static BotWrongRoadRefusal WrongRoadRefusal(in BotWrongRoadFacts f)
    {
        if (!f.SwitchOn)
            return BotWrongRoadRefusal.SwitchedOff;
        if (f.UsedThisTrip)
            return BotWrongRoadRefusal.UsedThisTrip;
        if (f.StableRide)
            return BotWrongRoadRefusal.StableRide;
        if (f.InGroup)
            return BotWrongRoadRefusal.Grouped;
        if (f.InFrontier)
            return BotWrongRoadRefusal.Frontier;
        if (f.InDungeon)
            return BotWrongRoadRefusal.Dungeon;
        if (f.HealthPercent < WrongRoadMinimumHealth || f.DiedLately)
            return BotWrongRoadRefusal.Hurt;
        if (!f.Walking)
            return BotWrongRoadRefusal.NotWalking;
        return f.Threatened ? BotWrongRoadRefusal.Threat : BotWrongRoadRefusal.None;
    }

    /// <summary>
    /// The branch a wrong road takes from <paramref name="from"/>: one of the route network's anchors (as
    /// <see cref="AutonomousZoneRoads.AnchorsNear"/> gives them: proven walking legs, which may cross open ground) that lies 750-5,000 units away and 60-150 degrees off the way
    /// to <paramref name="goal"/>, so neither the right road nor straight back. Picked by <paramref name="roll"/> (0 to 1).
    /// -1 for none. Pure.
    /// </summary>
    public static int WrongBranch(Vector3 from, Vector3 goal, IReadOnlyList<Vector3> anchors, double roll)
    {
        if (anchors == null || anchors.Count == 0)
            return -1;
        Vector2 toGoal = new(goal.X - from.X, goal.Y - from.Y);
        if (toGoal.LengthSquared() < 1)
            return -1;
        toGoal = Vector2.Normalize(toGoal);
        double cosMin = Math.Cos(BranchMinDegrees * Math.PI / 180), cosMax = Math.Cos(BranchMaxDegrees * Math.PI / 180);
        Span<int> candidates = stackalloc int[Math.Min(anchors.Count, 64)];
        int count = 0;
        for (int i = 0; i < anchors.Count && count < candidates.Length; i++)
        {
            Vector2 way = new(anchors[i].X - from.X, anchors[i].Y - from.Y);
            float distance = way.Length();
            if (distance < BranchMinDistance || distance > BranchMaxDistance)
                continue;
            double cos = Vector2.Dot(way / distance, toGoal);
            if (cos <= cosMin && cos >= cosMax)
                candidates[count++] = i;
        }
        if (count == 0)
            return -1;
        return candidates[Math.Min(count - 1, (int)(Math.Clamp(roll, 0, 0.999999) * count))];
    }

    /// <summary>
    /// The wrong leg toward a branch <paramref name="branchDistance"/> away: 600-1,500 units by <paramref name="lengthRoll"/>,
    /// stopping at least 150 short of the anchor, and a 4-8 s look around by <paramref name="lookRoll"/>. Never longer than
    /// fits the 3-minute cap at the bot's speed, there and back with the look. None when the branch is too short. Pure.
    /// </summary>
    public static BotWrongLeg LegFor(int anchor, float branchDistance, double lengthRoll, double lookRoll, float speed)
    {
        if (anchor < 0)
            return BotWrongLeg.None;
        float length = WrongLegMinimum + (float)(Math.Clamp(lengthRoll, 0, 1) * (WrongLegMaximum - WrongLegMinimum));
        length = Math.Min(length, branchDistance - WrongLegAnchorMargin);
        int look = LookMinMilliseconds + (int)(Math.Clamp(lookRoll, 0, 1) * (LookMaxMilliseconds - LookMinMilliseconds));
        // There and back with the look inside the cap, with 30 s to spare for the turns of a winding corridor.
        float fits = (WrongRoadCapMilliseconds - look - 30_000) / 1000f * Math.Max(1f, speed) / 2f;
        length = Math.Min(length, fits);
        return length < WrongLegMinimum ? BotWrongLeg.None : new BotWrongLeg(anchor, length, look);
    }

    /// <summary>How long a wrong road should take at this speed: there, a look, and back (the planned cost). Pure.</summary>
    public static double PlannedSeconds(BotWrongLeg leg, float speed) =>
        leg.IsNone ? 0 : 2 * leg.Length / Math.Max(1f, speed) + leg.LookMilliseconds / 1000.0;

    /// <summary>Owner decision 7: the wrong road ends now, wherever the bot is, once it has cost 3 minutes. Pure.</summary>
    public static bool MustEnd(long elapsedMilliseconds) => elapsedMilliseconds >= WrongRoadCapMilliseconds;

    /// <summary>Only a bot that knows a place (been there, not only told) answers the way to it. Pure.</summary>
    public static bool MayAnswer(BotPlaceKnowing answerer) => answerer == BotPlaceKnowing.Known;

    /// <summary>
    /// Who answers a question about the way: the first candidate (in the chat's order: a regular, a bot near the asker's level,
    /// anyone) that knows the place; default when none does (then nobody answers and the asker goes on as it is). Pure.
    /// </summary>
    public static T Answer<T>(IEnumerable<(T Who, BotPlaceKnowing Knowing)> candidates)
    {
        foreach ((T who, BotPlaceKnowing knowing) in candidates ?? [])
            if (MayAnswer(knowing))
                return who;
        return default;
    }

    /// <summary>
    /// Whether a realm-mate may be tagged along behind: near enough, going to the same place, knows it, free to lead, and within
    /// <see cref="FollowLevelGap"/> levels of the follower (a level 20 behind a level 45 would walk past monsters orange to it). Pure.
    /// </summary>
    public static bool FollowFits(float distance, bool sameDestination, BotPlaceKnowing leaderKnows, bool leaderRiding, bool leaderFighting,
        bool leaderGroupedWithPlayer, int levelGap) =>
        distance <= FollowSearchRadius && sameDestination && leaderKnows == BotPlaceKnowing.Known && !leaderRiding && !leaderFighting &&
        !leaderGroupedWithPlayer && Math.Abs(levelGap) <= FollowLevelGap;

    /// <summary>What a tagging bot does at this distance from its leader: -1 wait (closer than 300), 0 keep going, 1 catch up. Pure.</summary>
    public static int FollowStep(float distance, float desired) =>
        distance < FollowNearest ? -1 : distance > Math.Clamp(desired, FollowNearest, FollowFarthest) ? 1 : 0;

    /// <summary>
    /// The bot's skill tier for way finding: WP1's start tier (the same roll, salt "BSKL", on RVR-FIELD-1.2's shares moved by
    /// rvr_bot_awareness). A live bot uses its grown profile tier (<see cref="TierOf(GameBot)"/>). Pure.
    /// </summary>
    public static RvrSkillTier TierOf(BotPersonKey person, RvrBotAwareness awareness) =>
        BotSkillProfile.StartTierOf(person, awareness);

    /// <summary>
    /// A live gamebot's tier for way finding: the one tier every package reads (<see cref="BotSkill.TierOf"/>: its skill
    /// profile's tier, grown by play, else the same start roll).
    /// </summary>
    public static RvrSkillTier TierOf(GameBot bot) => BotSkill.TierOf(bot);

    public static string ChoiceName(BotWayChoice choice) => choice switch
    {
        BotWayChoice.Ask => "ask",
        BotWayChoice.Follow => "follow",
        BotWayChoice.WrongRoad => "wrong_road",
        _ => "knows",
    };

    public static string RefusalName(BotWrongRoadRefusal refusal) => refusal switch
    {
        BotWrongRoadRefusal.SwitchedOff => "switched_off",
        BotWrongRoadRefusal.UsedThisTrip => "used_this_trip",
        BotWrongRoadRefusal.StableRide => "stable_ride",
        BotWrongRoadRefusal.Grouped => "grouped",
        BotWrongRoadRefusal.Frontier => "frontier",
        BotWrongRoadRefusal.Dungeon => "dungeon",
        BotWrongRoadRefusal.Threat => "threat",
        BotWrongRoadRefusal.Hurt => "hurt",
        BotWrongRoadRefusal.NotWalking => "not_walking",
        BotWrongRoadRefusal.NoBranch => "no_branch",
        _ => "none",
    };

    public static string OutcomeName(BotWayAskOutcome outcome) => outcome switch
    {
        BotWayAskOutcome.Said => "said",
        BotWayAskOutcome.Capped => "capped",
        BotWayAskOutcome.Unheard => "unheard",
        BotWayAskOutcome.Held => "held",
        _ => "off",
    };

    public static string TierName(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => "casual",
        RvrSkillTier.Veteran => "veteran",
        _ => "average",
    };

    // ---- the live part ----------------------------------------------------------------------------------------------

    private static BotPeopleSwitches Switches => BotPeopleSwitches.Current;

    /// <summary>The ambient Travel scene's ask lines are said only as real questions (bot_place_knowledge and bot_ask_the_way on).</summary>
    public static bool AsksAreReal
    {
        get
        {
            BotPeopleSwitches s = Switches;
            return s.PlaceKnowledge && s.AskTheWay;
        }
    }

    /// <summary>One ask in /faction per realm every 10 minutes (owner decision 7), server-wide.</summary>
    public static readonly BotWayAskCap FactionCap = new(FactionAskGapMilliseconds);

    /// <summary>
    /// What a gamebot knows of a travel destination: the best of the places it names (WP3's catalog); Known when it names no
    /// listed place (an unlisted spot is no new ground we can judge) or for anything that is not a saved gamebot.
    /// </summary>
    public static BotPlaceKnowing KnowingOf(GameBot bot, string destination)
    {
        BotPlaceKnowing best = BotPlaceKnowing.Unknown;
        bool any = false;
        foreach (BotPlaceKey place in BotPlaceCatalog.Current.For(destination))
        {
            any = true;
            BotPlaceKnowing knowing = BotPlaceKnowledge.Knowing(bot, place);
            if (knowing > best)
                best = knowing;
            if (best == BotPlaceKnowing.Known)
                break;
        }
        return any ? best : BotPlaceKnowing.Known;
    }

    /// <summary>The chat coordinator's answer filter: a way question (Said.WayTo) is answered only by a bot that knows the place.</summary>
    public static bool MayAnswer(GameBot answerer, AutonomousChatFieldTalk.Said said)
    {
        if (said?.WayTo == null)
            return true;
        try
        {
            return MayAnswer(KnowingOf(answerer, said.WayTo));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// A line of a way question's thread was said by <paramref name="speaker"/>: when it knows the place, the asker is told the
    /// way and half knows it (WP3, how=told). Called by the chat coordinator for each line said in the thread.
    /// </summary>
    public static void Told(GameBot asker, GameBot speaker, AutonomousChatFieldTalk.Said said)
    {
        if (asker == null || speaker == null || speaker == asker || said?.WayTo == null)
            return;
        try
        {
            using var aiProfile = AiProfile.Scope(AiSystem.WayFinding); // AI_PROFILE: measures only
            foreach (BotPlaceKey place in BotPlaceCatalog.Current.For(said.WayTo))
            {
                if (!MayAnswer(BotPlaceKnowledge.Knowing(speaker, place)))
                    continue;
                if (BotPlaceKnowledge.Learn(asker, place, BotPlaceLearning.Told))
                {
                    Interlocked.Increment(ref _told);
                    Line(() => $"BOT_WAY_TOLD asker={asker.Name} id={asker.DatabaseID} answerer={speaker.Name} answererId={speaker.DatabaseID} " +
                               $"realm={GlobalConstants.RealmToName(asker.Realm)} place=\"{place.Name}\" dest=\"{said.WayTo}\"");
                }
                return;
            }
        }
        catch (Exception exception)
        {
            Log.Warn("Bots as people: a way answer could not be learned", exception);
        }
    }

    /// <summary>
    /// The asking bot's real question: in /say when a real player is near it, else in /faction while the realm's 10 minutes
    /// are over (or <paramref name="test"/>: /lstest ask). The line and its answers go through the chat budgets
    /// (AutonomousBotChatCoordinator.AskTheWay).
    /// </summary>
    public static BotWayAskOutcome Ask(GameBot bot, string destination, RvrSkillTier tier, bool test = false)
    {
        long now = GameLoop.GameLoopTime;
        BotWayAskOutcome outcome = AutonomousBotChatCoordinator.AskTheWay(bot, destination, faction: false);
        string channel = "say";
        if (outcome != BotWayAskOutcome.Said && outcome != BotWayAskOutcome.Off)
        {
            channel = "faction";
            long previous = 0;
            if (!test && !FactionCap.TryTake(bot.Realm, now, out previous))
                outcome = BotWayAskOutcome.Capped;
            else
            {
                outcome = AutonomousBotChatCoordinator.AskTheWay(bot, destination, faction: true);
                if (outcome != BotWayAskOutcome.Said && !test)
                    FactionCap.Restore(bot.Realm, previous);
                else if (outcome == BotWayAskOutcome.Said && test)
                    FactionCap.Take(bot.Realm, now);
            }
        }
        CountAsk(outcome, channel);
        Line(() => $"BOT_WAY_ASK bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} level={bot.Level} " +
                   $"tier={TierName(tier)} dest=\"{destination}\" channel={channel} said={OutcomeName(outcome)}{(test ? " test=true" : "")}");
        return outcome;
    }

    // ---- monsters on the way (today's route-threat rules) ------------------------------------------------------------------

    private static bool IsThreat(GameBot bot, GameNPC npc, out StandardMobBrain mob)
    {
        mob = null;
        if (npc == null || npc is GameBot || !npc.IsAlive || npc.Realm != eRealm.None || npc.InCombat)
            return false;
        mob = npc.Brain as StandardMobBrain;
        return mob != null && mob.AggroRange > 0 && mob.AggroLevel > 0 && mob.CanAggroTarget(bot);
    }

    /// <summary>
    /// A wrong leg is refused before it starts when an aggressive monster more than 2 levels above the bot is within 1,500
    /// units of it (owner decision 7), or when today's route-threat guard would act on one beside it (yellow or worse to the
    /// bot, a pack, a boss: AutonomousRouteThreatPolicy.WayWalkMustYield). One scan, at the decision.
    /// </summary>
    public static bool WrongLegThreatened(GameBot bot, Vector3 from, Vector3 to)
    {
        float reach = Vector3.Distance(from, to) + AutonomousRouteThreatPolicy.WrongRoadThreatRadius;
        List<GameNPC> near = bot.GetNPCsInRadius((ushort)Math.Min(ushort.MaxValue, reach));
        foreach (GameNPC npc in near)
        {
            if (!IsThreat(bot, npc, out StandardMobBrain mob))
                continue;
            Vector3 at = new(npc.X, npc.Y, npc.Z);
            if (AutonomousRouteThreatPolicy.WrongLegThreatened(from, to, bot.Level, npc.Level, true, at))
                return true;
            if (AutonomousRouteThreatPolicy.SegmentDistance2D(from, to, at) <= mob.AggroRange + AutonomousRouteThreatPolicy.RangedMargin &&
                AutonomousRouteThreatPolicy.WayWalkMustYield(ConLevels.GetConColor(bot.GetConLevel(npc)), PackOf(npc, mob, near), npc is IGameEpicNpc))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Today's route-threat look for a walk that owns the bot (a follow or a wrong road): a monster on the bot's upcoming path
    /// to <paramref name="target"/> (1,500 units ahead, within its aggro range and a margin) that the guard would act on
    /// (AutonomousRouteThreatPolicy.WayWalkMustYield). The caller looks every 2.5 s, as the guard does.
    /// </summary>
    public static bool ThreatAhead(GameBot bot, Vector3 target)
    {
        if (!AutonomousRouteThreatPolicy.Enabled || bot?.CurrentZone == null)
            return false;
        Span<Vector3> route = stackalloc Vector3[65];
        route[0] = new(bot.X, bot.Y, bot.Z);
        int count = 1;
        if (bot.movementComponent != null)
            count += bot.movementComponent.CopyUpcomingPath(target, route[1..]);
        if (count < 2)
        {
            route[1] = target;
            count = 2;
        }
        ReadOnlySpan<Vector3> corridor = route[..count];
        List<GameNPC> near = bot.GetNPCsInRadius((ushort)AutonomousRouteThreatPolicy.ScanRadius);
        foreach (GameNPC npc in near)
        {
            if (!IsThreat(bot, npc, out StandardMobBrain mob))
                continue;
            if (!AutonomousDungeonPolicy.IntersectsCorridor(corridor, new(npc.X, npc.Y, npc.Z),
                    mob.AggroRange + AutonomousRouteThreatPolicy.RangedMargin, AutonomousRouteThreatPolicy.LookAhead, out _))
                continue;
            if (AutonomousRouteThreatPolicy.WayWalkMustYield(ConLevels.GetConColor(bot.GetConLevel(npc)), PackOf(npc, mob, near), npc is IGameEpicNpc))
                return true;
        }
        return false;
    }

    /// <summary>The monster and those near enough to come with it (as today's guard counts a pack).</summary>
    private static int PackOf(GameNPC npc, StandardMobBrain mob, List<GameNPC> near)
    {
        int pack = 1;
        foreach (GameNPC other in near)
            if (other != npc && other.IsAlive && other.Realm == eRealm.None && Math.Abs(other.Z - npc.Z) <= 160 &&
                other.Brain is StandardMobBrain otherBrain && other.GetDistanceTo(npc) < Math.Max(320, mob.AggroRange + otherBrain.AggroRange + 80))
                pack++;
        return pack;
    }

    // ---- the travellers by destination (tagging along) ----------------------------------------------------------------

    private static readonly Dictionary<string, List<GameBot>> Travellers = new(StringComparer.Ordinal);
    private static readonly object TravellersLock = new();
    private static long _searchSecond;
    private static int _searchesThisSecond;

    private static string TravelKey(eRealm realm, string destination) =>
        ((int)realm).ToString(CultureInfo.InvariantCulture) + ":" + destination.Trim().ToLowerInvariant();

    internal static void NoteTraveller(GameBot bot, string oldKey, string newKey)
    {
        lock (TravellersLock)
        {
            if (oldKey != null && Travellers.TryGetValue(oldKey, out List<GameBot> old))
                old.Remove(bot);
            if (newKey == null)
                return;
            if (!Travellers.TryGetValue(newKey, out List<GameBot> list))
                Travellers[newKey] = list = new List<GameBot>(4);
            if (!list.Contains(bot))
            {
                if (list.Count >= 64)
                    list.RemoveAll(other => other.ObjectState != GameObject.eObjectState.Active);
                list.Add(bot);
            }
        }
    }

    private static void ForgetTraveller(GameBot bot)
    {
        lock (TravellersLock)
            foreach (List<GameBot> list in Travellers.Values)
                list.Remove(bot);
    }

    /// <summary>At most <see cref="FollowSearchesPerSecond"/> leader searches a second, server-wide.</summary>
    internal static bool FollowSearchAllowed(long nowMs)
    {
        lock (TravellersLock)
        {
            long second = nowMs / 1000;
            if (second != _searchSecond)
            {
                _searchSecond = second;
                _searchesThisSecond = 0;
            }
            return ++_searchesThisSecond <= FollowSearchesPerSecond;
        }
    }

    /// <summary>A realm-mate within 2,500 units going to the same place that knows it and is free to lead; null when none.</summary>
    internal static GameBot FindLeader(GameBot bot, string destination, string key)
    {
        GameBot[] candidates;
        lock (TravellersLock)
            candidates = Travellers.TryGetValue(key, out List<GameBot> list) ? list.ToArray() : [];
        GameBot best = null;
        float bestDistance = float.MaxValue;
        foreach (GameBot other in candidates)
        {
            if (other == bot || other.ObjectState != GameObject.eObjectState.Active || !other.IsAlive || other.Realm != bot.Realm ||
                other.CurrentRegion != bot.CurrentRegion || other.CurrentZone != bot.CurrentZone || !other.IsAutonomousWorldBot)
                continue;
            float distance = Vector2.Distance(new(bot.X, bot.Y), new(other.X, other.Y));
            bool same = string.Equals(other.PersistentRecord?.TravelDestination, destination, StringComparison.OrdinalIgnoreCase);
            if (distance >= bestDistance || !same)
                continue;
            if (!FollowFits(distance, same, KnowingOf(other, destination), other.IsOnStableMasterRoute, other.InCombat,
                    other.IsPlayerLedGroup || BotPeopleScope.FactsOf(other).InPlayerGroup, other.Level - bot.Level))
                continue;
            best = other;
            bestDistance = distance;
        }
        return best;
    }

    // ---- the /lstest hooks ----------------------------------------------------------------------------------------------

    private static readonly ConcurrentDictionary<long, bool> ForcedWrongRoads = new();
    private static int _forcedCount;

    internal static bool TakeForcedWrongRoad(GameBot bot)
    {
        if (Volatile.Read(ref _forcedCount) == 0 || !ForcedWrongRoads.TryRemove(bot.DatabaseID, out _))
            return false;
        Interlocked.Decrement(ref _forcedCount);
        return true;
    }

    internal static bool AnyForced => Volatile.Read(ref _forcedCount) > 0;

    internal static bool HasForcedWrongRoad(GameBot bot) =>
        Volatile.Read(ref _forcedCount) > 0 && ForcedWrongRoads.ContainsKey(bot.DatabaseID);

    private static string TestLost(GamePlayer gm, GameBot target, string[] words)
    {
        if (target == null || BotPersonKey.Of(target).IsNone)
            return "Target an autonomous gamebot first.";
        if (ForcedWrongRoads.TryAdd(target.DatabaseID, true))
            Interlocked.Increment(ref _forcedCount);
        string dest = AutonomousBotVoices.TravelingTo(target);
        return $"{target.Name}'s next trip to a place it does not know takes a wrong road (still never on a stable ride, in a group, " +
               $"a frontier, a dungeon or near a monster more than 2 levels above it; 3 minutes at most). " +
               (dest == null ? "It is not travelling now." : $"It is travelling to {dest} ({KnowingName(KnowingOf(target, dest))}); " +
                   "an unknown trip that has not had its wrong road takes it within 15 s of walking.");
    }

    private static string TestAsk(GamePlayer gm, GameBot target, string[] words)
    {
        if (target == null || BotPersonKey.Of(target).IsNone)
            return "Target an autonomous gamebot first.";
        string dest = AutonomousBotVoices.TravelingTo(target);
        if (dest == null || KnowingOf(target, dest) == BotPlaceKnowing.Known)
            dest = UnknownPlaceFor(target) ?? dest;
        if (dest == null)
            return $"{target.Name} knows every place it could ask about.";
        BotWayAskOutcome outcome = Ask(target, dest, TierOf(target), test: true);
        return $"{target.Name} asks the way to {dest} ({KnowingName(KnowingOf(target, dest))}): {OutcomeName(outcome)}" +
               (outcome == BotWayAskOutcome.Said ? ". Only a realm-mate that knows the place answers; the asker then half knows it." :
                   outcome == BotWayAskOutcome.Unheard ? " (no player of its realm could hear it)." : ".");
    }

    /// <summary>A place of its own realm (a zone, then a dungeon) the bot does not know yet, nearest its level; null when none.</summary>
    private static string UnknownPlaceFor(GameBot bot)
    {
        BotPlaceCatalog catalog = BotPlaceCatalog.Current;
        return catalog.All
            .Where(key => catalog.IsOfRealm(key, bot.Realm) && catalog.PlaceOf(key) is { } place &&
                          (BotPlaceCatalog.IsType(place, "zone") || BotPlaceCatalog.IsType(place, "dungeon")) &&
                          BotPlaceKnowledge.Knowing(bot, key) != BotPlaceKnowing.Known)
            .OrderBy(key => catalog.PlaceOf(key).Levels is { Length: 2 } levels ? Math.Abs((levels[0] + levels[1]) / 2 - bot.Level) : 99)
            .Select(key => catalog.PlaceOf(key).Zones is { Length: > 0 } zones ? zones[0] : key.Name)
            .FirstOrDefault();
    }

    private static RvrBotAwareness Awareness => AutonomousRvrPerception.ParseAwareness(DOL.GS.ServerProperties.Properties.RVR_BOT_AWARENESS);

    public static string KnowingName(BotPlaceKnowing knowing) => knowing switch
    {
        BotPlaceKnowing.Known => "known",
        BotPlaceKnowing.HalfKnown => "half known",
        _ => "unknown",
    };

    // ---- /lswhy way ----------------------------------------------------------------------------------------------------

    private static readonly ConcurrentDictionary<long, string> LastTrips = new();

    internal static void NoteTrip(GameBot bot, string line) => LastTrips[bot.DatabaseID] = line;

    internal static void NoteTrip(long botId, string line) => LastTrips[botId] = line;

    /// <summary>/lswhy way: WP3's place lines first, then the bot's last trip, its tier's shares and the realm's /faction cap.</summary>
    public static IEnumerable<string> WhyLines(GameBot bot)
    {
        var lines = new List<string>(BotPlaceKnowledge.WhyLines(bot));
        BotPeopleSwitches s = Switches;
        if (!s.PlaceKnowledge)
            return lines;
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone)
            return lines;
        RvrSkillTier tier = TierOf(bot);
        BotWayShares unknown = Shares(tier, BotPlaceKnowing.Unknown, s.AskTheWay, s.WrongRoads);
        lines.Add($"Way finding ({TierName(tier)}): to a place it does not know it knows the way anyway {unknown.Knows}%, asks {unknown.Ask}%, " +
                  $"tags along {unknown.Follow}%, takes a wrong road {unknown.WrongRoad}% (asking {(s.AskTheWay ? "on" : "off")}, wrong roads " +
                  $"{(s.WrongRoads ? "on" : "off")}).");
        long wait = FactionCap.WaitMilliseconds(bot.Realm, GameLoop.GameLoopTime);
        lines.Add(wait <= 0 ? "Its realm may ask the way in /faction now." : $"Its realm may ask the way in /faction again in {Math.Ceiling(wait / 60_000.0):0} min.");
        lines.Add(LastTrips.TryGetValue(bot.DatabaseID, out string last) ? "Last trip: " + last : "Last trip: none seen since the server started.");
        if (HasForcedWrongRoad(bot))
            lines.Add("/lstest lost is armed: its next unknown trip takes a wrong road.");
        return lines;
    }

    // ---- log lines and the summary ----------------------------------------------------------------------------------

    private static readonly long[,] Choices = new long[3, 4];
    private static long _trips;
    private static long _unknownTrips;
    private static long _asksSaid;
    private static long _asksSay;
    private static long _asksCapped;
    private static long _asksUnheard;
    private static long _asksHeld;
    private static long _told;
    private static long _follows;
    private static long _followsNone;
    private static long _wrongRoads;
    private static long _wrongMilliseconds;
    private static long _wrongDeaths;
    private static long _followDeaths;
    private static long _lines;
    private static long _suppressed;
    private static long _lineWindowStart;
    private static int _linesInWindow;
    private static readonly ConcurrentDictionary<BotWrongRoadRefusal, long> Refused = new();

    internal static void CountChoice(RvrSkillTier tier, BotWayChoice choice) =>
        Interlocked.Increment(ref Choices[Math.Clamp((int)tier, 0, 2), (int)choice]);

    internal static void CountTrip(bool unknown)
    {
        Interlocked.Increment(ref _trips);
        if (unknown)
            Interlocked.Increment(ref _unknownTrips);
    }

    private static void CountAsk(BotWayAskOutcome outcome, string channel)
    {
        switch (outcome)
        {
            case BotWayAskOutcome.Said:
                Interlocked.Increment(ref _asksSaid);
                if (channel == "say")
                    Interlocked.Increment(ref _asksSay);
                break;
            case BotWayAskOutcome.Capped: Interlocked.Increment(ref _asksCapped); break;
            case BotWayAskOutcome.Unheard: Interlocked.Increment(ref _asksUnheard); break;
            default: Interlocked.Increment(ref _asksHeld); break;
        }
    }

    internal static void CountFollow(bool found)
    {
        if (found)
            Interlocked.Increment(ref _follows);
        else
            Interlocked.Increment(ref _followsNone);
    }

    internal static void CountRefusal(BotWrongRoadRefusal refusal) => Refused.AddOrUpdate(refusal, 1, (_, n) => n + 1);

    internal static void CountWrongRoad(long milliseconds)
    {
        Interlocked.Increment(ref _wrongRoads);
        Interlocked.Add(ref _wrongMilliseconds, Math.Max(0, milliseconds));
    }

    internal static void CountWrongRoadDeath() => Interlocked.Increment(ref _wrongDeaths);

    internal static void CountFollowDeath() => Interlocked.Increment(ref _followDeaths);

    /// <summary>One event line, at most <see cref="EventLinesPerMinute"/> a minute server-wide (the text is built only when said).</summary>
    internal static void Line(Func<string> text)
    {
        if (!Log.IsInfoEnabled || !TakeLine(DateTime.UtcNow.Ticks))
            return;
        try
        {
            Log.Info(text());
        }
        catch
        {
            // A log line never stops a bot.
        }
    }

    private static bool TakeLine(long nowTicks)
    {
        long start = Interlocked.Read(ref _lineWindowStart);
        if (nowTicks - start >= TimeSpan.TicksPerMinute && Interlocked.CompareExchange(ref _lineWindowStart, nowTicks, start) == start)
            Interlocked.Exchange(ref _linesInWindow, 0);
        if (Interlocked.Increment(ref _linesInWindow) > EventLinesPerMinute)
        {
            Interlocked.Increment(ref _suppressed);
            return false;
        }
        Interlocked.Increment(ref _lines);
        return true;
    }

    /// <summary>
    /// "BOT_WAY_SUMMARY seconds=300 trips=N unknown=N choices=casual:k/a/f/w,average:k/a/f/w,veteran:k/a/f/w
    /// asks=said:N(say:N),capped:N,unheard:N,held:N told=N follows=N,none:N wrong_roads=N minutes_lost=M.M
    /// refused=reason:n,...|none deaths_60s=N follow_deaths_60s=N lines=N suppressed=N" (deaths during or within 60 s after
    /// a wrong road, and a follow). Pure.
    /// </summary>
    public static string SummaryLine(long seconds, long trips, long unknown, long[,] choices, long asksSaid, long asksSay, long capped,
        long unheard, long held, long told, long follows, long followsNone, long wrongRoads, long wrongMilliseconds,
        IReadOnlyDictionary<BotWrongRoadRefusal, long> refused, long deaths, long followDeaths, long lines, long suppressed)
    {
        string Tier(int t) => $"{TierName((RvrSkillTier)t)}:{choices[t, 0]}/{choices[t, 1]}/{choices[t, 2]}/{choices[t, 3]}";
        string refusals = refused == null || refused.Count == 0 || refused.Values.All(n => n == 0)
            ? "none"
            : string.Join(",", refused.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key).Select(pair => $"{RefusalName(pair.Key)}:{pair.Value}"));
        return $"BOT_WAY_SUMMARY seconds={seconds} trips={trips} unknown={unknown} choices={Tier(0)},{Tier(1)},{Tier(2)} " +
               $"asks=said:{asksSaid}(say:{asksSay}),capped:{capped},unheard:{unheard},held:{held} told={told} follows={follows},none:{followsNone} " +
               $"wrong_roads={wrongRoads} minutes_lost={(wrongMilliseconds / 60_000.0).ToString("0.0", CultureInfo.InvariantCulture)} " +
               $"refused={refusals} deaths_60s={deaths} follow_deaths_60s={followDeaths} lines={lines} suppressed={suppressed}";
    }

    /// <summary>The 5-minute BOT_WAY_SUMMARY line of the window that ends now (its counts start again).</summary>
    public static IEnumerable<string> TakeSummary(long seconds)
    {
        var choices = new long[3, 4];
        for (int t = 0; t < 3; t++)
            for (int c = 0; c < 4; c++)
                choices[t, c] = Interlocked.Exchange(ref Choices[t, c], 0);
        var refused = new Dictionary<BotWrongRoadRefusal, long>();
        foreach (BotWrongRoadRefusal key in Refused.Keys.ToArray())
            if (Refused.TryRemove(key, out long n))
                refused[key] = n;
        yield return SummaryLine(seconds, Interlocked.Exchange(ref _trips, 0), Interlocked.Exchange(ref _unknownTrips, 0), choices,
            Interlocked.Exchange(ref _asksSaid, 0), Interlocked.Exchange(ref _asksSay, 0), Interlocked.Exchange(ref _asksCapped, 0),
            Interlocked.Exchange(ref _asksUnheard, 0), Interlocked.Exchange(ref _asksHeld, 0), Interlocked.Exchange(ref _told, 0),
            Interlocked.Exchange(ref _follows, 0), Interlocked.Exchange(ref _followsNone, 0), Interlocked.Exchange(ref _wrongRoads, 0),
            Interlocked.Exchange(ref _wrongMilliseconds, 0), refused, Interlocked.Exchange(ref _wrongDeaths, 0),
            Interlocked.Exchange(ref _followDeaths, 0), Interlocked.Exchange(ref _lines, 0), Interlocked.Exchange(ref _suppressed, 0));
    }

    // ---- start-up -----------------------------------------------------------------------------------------------------

    private static int _registered;

    /// <summary>Registers /lswhy way (WP3's lines first, then these), /lstest lost and ask, BOT_WAY_SUMMARY and the logout clean-up.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
            return;
        BotPlaceKnowledge.Register();
        BotPeopleWhy.Register(BotPeopleWhy.Topic.Way, WhyLines);
        BotPeopleTest.Register("lost", TestLost);
        BotPeopleTest.Register("ask", TestAsk);
        BotPeopleSummary.Register("BOT_WAY_SUMMARY", TakeSummary);
        BotPeopleStore.LoggedOut += (person, bot) =>
        {
            if (bot == null)
                return;
            ForgetTraveller(bot);
            LastTrips.TryRemove(bot.DatabaseID, out string _);
        };
    }

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            Register();
        }
        catch (Exception exception)
        {
            Log.Warn("Bots as people: way finding could not register", exception);
        }
    }

    /// <summary>Tests only: forgets every count, the realm caps, the travellers and the /lstest arms.</summary>
    public static void ResetForTests()
    {
        Array.Clear(Choices);
        _trips = _unknownTrips = _asksSaid = _asksSay = _asksCapped = _asksUnheard = _asksHeld = _told = 0;
        _follows = _followsNone = _wrongRoads = _wrongMilliseconds = _wrongDeaths = _followDeaths = _lines = _suppressed = 0;
        _linesInWindow = 0;
        _lineWindowStart = 0;
        Refused.Clear();
        FactionCap.Clear();
        ForcedWrongRoads.Clear();
        _forcedCount = 0;
        LastTrips.Clear();
        lock (TravellersLock)
            Travellers.Clear();
    }

    internal static string TravelKeyFor(eRealm realm, string destination) => TravelKey(realm, destination);
}

/// <summary>One ask the way in /faction per realm every <see cref="GapMilliseconds"/> (owner decision 7). Thread-safe.</summary>
public sealed class BotWayAskCap
{
    private readonly object _lock = new();
    private readonly Dictionary<eRealm, long> _last = new();

    public BotWayAskCap(long gapMilliseconds) => GapMilliseconds = gapMilliseconds;

    public long GapMilliseconds { get; }

    /// <summary>Takes the realm's turn when its gap is over; <paramref name="previous"/> is what <see cref="Restore"/> puts back.</summary>
    public bool TryTake(eRealm realm, long nowMs, out long previous)
    {
        lock (_lock)
        {
            bool had = _last.TryGetValue(realm, out previous);
            if (had && nowMs - previous < GapMilliseconds)
                return false;
            if (!had)
                previous = long.MinValue;
            _last[realm] = nowMs;
            return true;
        }
    }

    /// <summary>Takes the realm's turn whatever the gap (a GM test that was said).</summary>
    public void Take(eRealm realm, long nowMs)
    {
        lock (_lock)
            _last[realm] = nowMs;
    }

    /// <summary>Gives back a turn taken for a line that was not said.</summary>
    public void Restore(eRealm realm, long previous)
    {
        lock (_lock)
        {
            if (previous == long.MinValue)
                _last.Remove(realm);
            else
                _last[realm] = previous;
        }
    }

    /// <summary>How long until the realm may ask again (0 or less: now).</summary>
    public long WaitMilliseconds(eRealm realm, long nowMs)
    {
        lock (_lock)
            return _last.TryGetValue(realm, out long last) ? GapMilliseconds - (nowMs - last) : 0;
    }

    public void Clear()
    {
        lock (_lock)
            _last.Clear();
    }
}

