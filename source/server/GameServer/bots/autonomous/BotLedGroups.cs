using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Language;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Bot-led groups, "You are wanted" (docs/BOT-LED-GROUPS.md, Living Server 1.2 phase B, step B1): the leader of an
/// autonomous gamebots' PvE party invites the real player, who can then just follow. This first part holds the pure
/// rules (who may be invited and when, which leader asks, how a hosted player is followed, how loot is shared), so each
/// is tested on its own (UT_BotLedInvitePolicy, UT_BotLedGroupSession, UT_BotLedLoot). The game glue (the tell, the box,
/// the session, the loot) is in the second part.
/// <para>
/// Only autonomous gamebots invite. A /spawn companion or a real player never does, and a real player is never leased;
/// the bots of a group that hosts the player hold the duty lease <see cref="eDutyLeaseKind.BotLedGroupWithPlayer"/>.
/// </para>
/// </summary>
public static partial class BotLedGroups
{
    #region Constants (docs/BOT-LED-GROUPS.md, "When a bot invites you" and "Following")

    /// <summary>The group's average level is within this many levels of the player's.</summary>
    public const int LevelBand = 3;
    /// <summary>No unasked invite in the player's first 10 minutes online.</summary>
    public const long FirstInviteAfterLoginMs = 10 * 60_000L;
    /// <summary>At most one unasked invite every 45 minutes.</summary>
    public const long UnaskedGapMs = 45 * 60_000L;
    /// <summary>
    /// An asked invite (/lfg, "lfg?" to a bot): the leader answers by tell first, and the invite box follows within 15
    /// seconds of asking (owner 2026-11-04).
    /// </summary>
    public const long AskedWindowMs = 15_000L;
    /// <summary>The leader's answer to an ask comes 1 to 2.5 seconds after the invite is decided.</summary>
    public const int AskedTellDelayMinMs = 1_000, AskedTellDelaySpreadMs = 1_500;
    /// <summary>The box follows the answer 1.5 to 3 seconds later.</summary>
    public const int AskedBoxDelayMinMs = 1_500, AskedBoxDelaySpreadMs = 1_500;
    /// <summary>The longest an asked invite takes from the decision to the box.</summary>
    public const long AskedLongestDelayMs = AskedTellDelayMinMs + AskedTellDelaySpreadMs + AskedBoxDelayMinMs + AskedBoxDelaySpreadMs;
    /// <summary>
    /// An ask is still served (by the 5-second pulse when no party fitted at once) only while the box can still come
    /// within <see cref="AskedWindowMs"/>.
    /// </summary>
    public const long AskedLatestStartMs = AskedWindowMs - AskedLongestDelayMs;
    /// <summary>No unasked invite for 30 minutes after a no.</summary>
    public const long DeclineQuietMs = 30 * 60_000L;
    /// <summary>Nothing from a leader for 6 hours after the player said no to it.</summary>
    public const long LeaderDeclineMs = 6 * 60 * 60_000L;
    /// <summary>After three noes no unasked invite for the rest of the session.</summary>
    public const int SessionDeclineLimit = 3;

    /// <summary>The leader stops when the player falls this far behind (the group's own trail radius).</summary>
    public const int TrailRadius = AutonomousGroupPace.TrailRadius;
    /// <summary>The player counts as at the meeting spot, or near the camp for a pull, within this distance of the leader.</summary>
    public const int NearLeaderRadius = TrailRadius;
    /// <summary>A trailing player who is this much closer than before is "getting closer".</summary>
    public const int CloserStep = 200;
    /// <summary>After 2 minutes stopped for the player the leader asks "where u at?".</summary>
    public const long WhereAreYouMs = 2 * 60_000L;
    /// <summary>After 10 minutes with the player not getting closer the group goes on and keeps the spot.</summary>
    public const long GoOnWithoutMs = 10 * 60_000L;
    /// <summary>The group waits at the meeting spot up to 20 minutes and calls out where it is at 5 and 10.</summary>
    public const long MeetupWaitMs = 20 * 60_000L;
    public const long MeetupFirstReminderMs = 5 * 60_000L;
    public const long MeetupSecondReminderMs = 10 * 60_000L;
    /// <summary>Stop doing anything for 10 minutes (or /afk) and the group pulls in place but goes nowhere new.</summary>
    public const long IdleCountsAsAfkMs = 10 * 60_000L;
    /// <summary>After 15 minutes away the group holds ("waiting on X, everyone med").</summary>
    public const long AfkHoldMs = 15 * 60_000L;
    /// <summary>After 30 minutes away it lets the player go at the next break.</summary>
    public const long AfkReleaseMs = 30 * 60_000L;
    /// <summary>Logging out or going linkdead counts as leaving after 5 minutes.</summary>
    public const long LinkdeadLeaveMs = 5 * 60_000L;
    /// <summary>A moved player is doing something (the AFK clock restarts).</summary>
    public const int ActivityMoveUnits = 50;

    /// <summary>A hosted group's camp is never more than 6 levels above the player.</summary>
    public const int CampCapAbovePlayer = 6;
    /// <summary>The group camp floor, as AutonomousWorldBotController's group filter (planning level + 3).</summary>
    public const int CampFloorAbovePlanning = 3;

    /// <summary>The player counts as this many members in the coin split and the item rotation (owner 2026-11-04).</summary>
    public const int DefaultPlayerLootWeight = 2;
    public const int MaximumPlayerLootWeight = 8;

    #endregion

    #region May the player be invited (states, band, place, cadence)

    /// <summary>What the invite needs to know about the real player now. Pure data.</summary>
    public readonly record struct PlayerState(
        bool Alive, bool InCombat, bool Riding, bool OnBoat, bool DialogOpen, bool Trading, bool Grouped,
        bool HasCompanions, bool Afk, bool InFrontier, bool InBattleground, bool InRaid, bool Linkdead);

    /// <summary>
    /// Why the player can't be invited in this state, or null when they can. The "never" list of the design: fighting,
    /// dead, riding, on a boat, in another yes/no box, trading, in a group, with /spawn companions out, /afk, in a
    /// frontier, battleground or raid. It holds for asked invites too.
    /// </summary>
    public static string StateBlocks(in PlayerState state) =>
        !state.Alive ? "dead"
        : state.Linkdead ? "linkdead"
        : state.InCombat ? "in_combat"
        : state.Riding ? "riding"
        : state.OnBoat ? "on_boat"
        : state.DialogOpen ? "dialog_open"
        : state.Trading ? "trading"
        : state.Grouped ? "grouped"
        : state.HasCompanions ? "companions_out"
        : state.Afk ? "afk"
        : state.InFrontier ? "in_frontier"
        : state.InBattleground ? "in_battleground"
        : state.InRaid ? "in_raid"
        : null;

    /// <summary>The group's average level is within <see cref="LevelBand"/> of the player's.</summary>
    public static bool InLevelBand(int playerLevel, int groupAverageLevel) => Math.Abs(playerLevel - groupAverageLevel) <= LevelBand;

    /// <summary>
    /// A bot party that may invite the player, as the coordinator sees it. <paramref name="Hunting"/>: a PvE party past
    /// its leader's staging (meeting up, choosing a camp, travelling or grinding), not regrouping after a wipe, not
    /// ending. <paramref name="OnRealmDuty"/>: a raid or RvR party, or any member whose lease a hosted player can't take.
    /// <paramref name="Reachable"/>: hunting in the player's own realm's lands, not in a frontier, battleground or
    /// Darkness Falls (the player gets there on foot or by horse).
    /// </summary>
    public readonly record struct Candidate(long LeaderId, string LeaderName, eRealm Realm, int AverageLevel, int Members, int MaxMembers,
        bool HasHealer, bool Hunting, bool OnRealmDuty, bool Reachable, bool HasPlayer, double Distance);

    /// <summary>Why this party can't invite this player, or null when it can.</summary>
    public static string CandidateBlocks(in Candidate group, eRealm playerRealm, int playerLevel) =>
        group.Realm != playerRealm ? "other_realm"
        : group.HasPlayer ? "has_player"
        : !group.Hunting ? "not_hunting"
        : group.OnRealmDuty ? "on_duty"
        : !group.Reachable ? "unreachable"
        : group.Members >= group.MaxMembers ? "full"
        : !InLevelBand(playerLevel, group.AverageLevel) ? "level_band"
        : null;

    /// <summary>
    /// How well a party fits the player (higher is better): a party short of a healer asks a healer first and a
    /// non-healer last; then the closest level; then the nearest leader.
    /// </summary>
    public static double CandidateScore(in Candidate group, int playerLevel, bool playerIsHealer)
    {
        double score = 100 - Math.Abs(playerLevel - group.AverageLevel) * 10;
        if (!group.HasHealer)
            score += playerIsHealer ? 50 : -50;
        // Up to 10 points for being near (none past 60,000 units or in another region).
        if (group.Distance >= 0 && group.Distance < 60_000)
            score += 10 * (1 - group.Distance / 60_000);
        return score;
    }

    /// <summary>
    /// What one player's invites remember: when they came online, the last unasked invite, the noes (when, from whom,
    /// how many this session), when they last asked and whether a box is waiting for an answer. Times are game-loop
    /// milliseconds. The rules that read it are pure; the glue keeps one per player for the session.
    /// </summary>
    public sealed class InviteMemory
    {
        public long OnlineSince { get; set; }
        public long LastUnaskedAt { get; set; } = long.MinValue / 2;
        public long LastDeclineAt { get; set; } = long.MinValue / 2;
        public int SessionDeclines { get; set; }
        public long AskedAt { get; set; } = long.MinValue / 2;
        public bool Pending { get; set; }
        /// <summary>The player typed /invites off (B2): no unasked invite, from a party or a raid. Asking still works.</summary>
        public bool PlayerInvitesOff { get; set; }
        public Dictionary<long, long> DeclinedByLeader { get; } = new();

        public InviteMemory(long onlineSince) => OnlineSince = onlineSince;

        /// <summary>The player said no to this leader (No in the box, or "no thanks" to its tell).</summary>
        public void Declined(long leaderId, long now)
        {
            LastDeclineAt = now;
            SessionDeclines++;
            DeclinedByLeader[leaderId] = now;
            Pending = false;
        }

        /// <summary>The player asked for a group (/lfg, or "lfg?" by tell).</summary>
        public void Asked(long now) => AskedAt = now;

        /// <summary>The player asked recently enough that an invite decided now still shows its box within 15 seconds.</summary>
        public bool IsAsking(long now) => now - AskedAt >= 0 && now - AskedAt <= AskedLatestStartMs;

        public bool LeaderDeclined(long leaderId, long now) =>
            DeclinedByLeader.TryGetValue(leaderId, out long at) && now - at < LeaderDeclineMs;
    }

    /// <summary>
    /// Why no invite may be sent now, or null when one may. Asked (/lfg or a tell, served within 15 seconds): only a box
    /// that is already waiting stops it; the 10-minute start, the 45-minute gap, the 30-minute quiet after a no and the
    /// three-noes rule are for unasked invites. Unasked: invites on, 10 minutes online, 45 minutes since the last unasked invite and
    /// <paramref name="sinceLastBotTell"/> at least the bot tells' own gap (an unasked invite counts as that period's
    /// bot tell, so they never stack), 30 minutes since a no, fewer than three noes this session.
    /// </summary>
    public static string CadenceBlocks(InviteMemory memory, long now, bool invitesOn, long sinceLastBotTell)
    {
        if (memory.Pending)
            return "box_waiting";
        if (memory.IsAsking(now))
            return null;
        if (!invitesOn)
            return "invites_off";
        if (memory.PlayerInvitesOff)
            return "invites_off_by_player";
        if (now - memory.OnlineSince < FirstInviteAfterLoginMs)
            return "just_logged_in";
        if (memory.SessionDeclines >= SessionDeclineLimit)
            return "declined_three_times";
        if (now - memory.LastDeclineAt < DeclineQuietMs)
            return "declined_recently";
        if (now - memory.LastUnaskedAt < UnaskedGapMs)
            return "invited_recently";
        if (sinceLastBotTell < AutonomousLivingServer.TellGapMinMs)
            return "bot_told_recently";
        return null;
    }

    /// <summary>
    /// The party whose leader invites the player: the best-scoring party that fits, leaving out a leader the player said
    /// no to in the last 6 hours. Null when none fits.
    /// </summary>
    public static Candidate? PickInvitingLeader(IEnumerable<Candidate> candidates, eRealm playerRealm, int playerLevel, bool playerIsHealer,
        InviteMemory memory, long now)
    {
        Candidate? best = null;
        double bestScore = double.MinValue;
        foreach (Candidate candidate in candidates)
        {
            if (CandidateBlocks(candidate, playerRealm, playerLevel) != null || memory != null && memory.LeaderDeclined(candidate.LeaderId, now))
                continue;
            double score = CandidateScore(candidate, playerLevel, playerIsHealer);
            if (score > bestScore || score == bestScore && best is { } held && candidate.LeaderId < held.LeaderId)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    #endregion

    #region Leadership

    /// <summary>One group member as leader succession sees it.</summary>
    public readonly record struct MemberView(bool IsPlayer, bool IsAutonomousBot, bool Alive);

    /// <summary>
    /// The new leader of a group that hosts a player when its leader leaves: the first living autonomous gamebot, else the
    /// first autonomous gamebot. Never the player (the player leads only by asking, in B2's /lead). -1 when no gamebot is
    /// left.
    /// </summary>
    public static int ElectLeader(IReadOnlyList<MemberView> members)
    {
        int firstBot = -1;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].IsPlayer || !members[i].IsAutonomousBot)
                continue;
            if (members[i].Alive)
                return i;
            if (firstBot < 0)
                firstBot = i;
        }
        return firstBot;
    }

    /// <summary>The player's job in the group, as the party roster counts it: a healing class heals, a tank tanks, the rest hit.</summary>
    public static BotPveGroupRole PlayerRole(eCharacterClass characterClass) =>
        BotPartyRoles.IsHealingClass(characterClass) ? BotPveGroupRole.Healer
        : BotPartyRoles.For(characterClass) == BotPartyRole.Tank ? BotPveGroupRole.Tank
        : BotPveGroupRole.Attacker;

    #endregion

    #region The camp

    /// <summary>
    /// The planning level and level bonus a hosted group's camp search uses: the group's level counts the player, and no
    /// camp is more than <see cref="CampCapAbovePlayer"/> levels above the player. The search takes camps from planning
    /// + 3 to planning + bonus, so the planning level is held at most 3 below the cap and the bonus at least 3.
    /// </summary>
    public static (int PlanningLevel, int Bonus) HostedCampBand(int groupLevelWithPlayer, int preferredBonus, int playerLevel)
    {
        int cap = playerLevel + CampCapAbovePlayer;
        int planning = Math.Min(groupLevelWithPlayer, cap - CampFloorAbovePlanning);
        int bonus = Math.Max(CampFloorAbovePlanning, Math.Min(preferredBonus, cap - planning));
        return (planning, bonus);
    }

    /// <summary>The group's level with the player: the average of the bots' levels and the player's.</summary>
    public static int GroupLevelWithPlayer(IReadOnlyCollection<int> botLevels, int playerLevel) =>
        botLevels == null || botLevels.Count == 0 ? playerLevel
            : (int)Math.Round((botLevels.Sum() + (double)playerLevel) / (botLevels.Count + 1), MidpointRounding.AwayFromZero);

    #endregion

    #region Following

    /// <summary>What the coordinator sees of the hosted player on one pass. Pure data.</summary>
    public readonly record struct Observation(bool SameRegion, float DistanceToLeader, bool Alive, bool Sitting, bool ResourcesReady,
        bool AfkFlag, bool Moved, bool InCombat, bool Linkdead, bool MeetingUp, bool GroupFighting);

    /// <summary>A line the leader says, or an action the coordinator takes, from one pass.</summary>
    public enum Say
    {
        None,
        /// <summary>"we're at X" at 5 and 10 minutes of the meetup.</summary>
        MeetupReminder,
        /// <summary>After 20 minutes the group goes without the player and keeps the spot.</summary>
        MeetupGoingOn,
        /// <summary>"where u at?" after 2 minutes stopped for the player.</summary>
        WhereAreYou,
        /// <summary>After 10 minutes the player isn't getting closer: on to the camp, the spot kept.</summary>
        GoingOn,
        /// <summary>15 minutes away: "waiting on X, everyone med".</summary>
        AfkHold,
        /// <summary>30 minutes away, at the next break: the group lets the player go, with a tell.</summary>
        AfkLetGo,
        /// <summary>5 minutes linkdead: the player counts as having left.</summary>
        LinkdeadLeft,
    }

    public enum AfkStage { Active, KeepPulling, Hold, LetGo }

    /// <summary>
    /// How long the player has been away decides: under 10 minutes idle the player is active; /afk or 10 minutes idle,
    /// the group pulls where it stands but goes nowhere new; 15 minutes away it holds; 30 it lets the player go.
    /// <paramref name="awayMs"/> counts from /afk, or from the last thing the player did.
    /// </summary>
    public static AfkStage StageOfAway(bool afkFlag, long awayMs)
    {
        if (!afkFlag && awayMs < IdleCountsAsAfkMs)
            return AfkStage.Active;
        if (awayMs >= AfkReleaseMs)
            return AfkStage.LetGo;
        return awayMs >= AfkHoldMs ? AfkStage.Hold : AfkStage.KeepPulling;
    }

    /// <summary>
    /// The coordinator's memory of how the hosted player is following, and its rules. <see cref="Step"/> takes one
    /// observation and says what the leader says or does; <see cref="HoldsPace"/>, <see cref="BlocksPull"/> and
    /// <see cref="BlocksNewTravel"/> are what the party's pace, pull gate and travel read. No world state is read here.
    /// </summary>
    public sealed class FollowState
    {
        public long JoinedAt { get; }
        public bool MeetupDone { get; private set; }
        public int MeetupReminders { get; private set; }
        /// <summary>The group stopped waiting for the player (meetup over, or 10 minutes not getting closer). Cleared once the player is close.</summary>
        public bool Released { get; private set; }
        /// <summary>When the leader stopped for the trailing player; null while not waiting.</summary>
        public long? WaitingSince { get; private set; }
        public long LastCloserAt { get; private set; }
        public float ClosestWhileWaiting { get; private set; } = float.MaxValue;
        public bool AskedWhere { get; private set; }
        public long LastActivityAt { get; private set; }
        public long? AfkFlagSince { get; private set; }
        public bool AfkHoldSaid { get; private set; }
        public long? LinkdeadSince { get; private set; }
        public AfkStage Away { get; private set; }
        public Observation Last { get; private set; }

        public FollowState(long joinedAt, bool meetupDone = false)
        {
            JoinedAt = joinedAt;
            LastActivityAt = joinedAt;
            MeetupDone = meetupDone;
        }

        public bool IsClose(in Observation seen) => seen.SameRegion && seen.DistanceToLeader <= NearLeaderRadius;

        /// <summary>
        /// The group's pace waits for the player: far behind (or in another region) and not released, or dead (the group
        /// waits until the player is back up). Not while the player is away: then it goes nowhere new anyway.
        /// </summary>
        public bool HoldsPace => !Released && Away == AfkStage.Active && (!Last.Alive || !IsClose(Last));

        /// <summary>
        /// No new pull while the player isn't ready like the rest: dead, sitting, low on health or power, not near (unless
        /// the group went on without them), or away long enough that the group holds. Pulling in place goes on for a
        /// short /afk.
        /// </summary>
        public bool BlocksPull =>
            Away is AfkStage.Hold or AfkStage.LetGo ||
            Away == AfkStage.Active && !Released && (!Last.Alive || Last.Sitting || !Last.ResourcesReady || !IsClose(Last)) ||
            Away == AfkStage.Active && Released && IsClose(Last) && (!Last.Alive || Last.Sitting || !Last.ResourcesReady);

        /// <summary>While the player is away the group pulls where it stands but goes nowhere new.</summary>
        public bool BlocksNewTravel => Away != AfkStage.Active;

        /// <summary>The meetup is over for the player's part: there, or the group went on without them.</summary>
        public bool ReadyToLeaveMeetup => MeetupDone;

        public Say Step(in Observation seen, long now)
        {
            Last = seen;
            // Linkdead: 5 minutes count as leaving.
            if (seen.Linkdead)
            {
                LinkdeadSince ??= now;
                return now - LinkdeadSince.Value >= LinkdeadLeaveMs ? Say.LinkdeadLeft : Say.None;
            }
            LinkdeadSince = null;

            if (seen.Moved || seen.InCombat)
                LastActivityAt = now;
            if (seen.AfkFlag)
                AfkFlagSince ??= now;
            else
                AfkFlagSince = null;
            long away = seen.AfkFlag ? now - AfkFlagSince.Value : now - LastActivityAt;
            Away = StageOfAway(seen.AfkFlag, away);
            if (Away == AfkStage.Active)
                AfkHoldSaid = false;
            else if (Away == AfkStage.LetGo && !seen.GroupFighting)
                return Say.AfkLetGo;
            else if (Away == AfkStage.Hold && !AfkHoldSaid)
            {
                AfkHoldSaid = true;
                return Say.AfkHold;
            }

            bool close = IsClose(seen);
            if (close)
            {
                Released = false;
                WaitingSince = null;
                AskedWhere = false;
                ClosestWhileWaiting = float.MaxValue;
            }

            // The meetup: the group waits up to 20 minutes, and says where it is at 5 and 10.
            if (!MeetupDone)
            {
                if (close && seen.Alive)
                {
                    MeetupDone = true;
                    return Say.None;
                }
                long waited = now - JoinedAt;
                if (waited >= MeetupWaitMs)
                {
                    MeetupDone = true;
                    Released = true;
                    return Say.MeetupGoingOn;
                }
                if (MeetupReminders == 0 && waited >= MeetupFirstReminderMs || MeetupReminders == 1 && waited >= MeetupSecondReminderMs)
                {
                    MeetupReminders++;
                    return Say.MeetupReminder;
                }
                return Say.None;
            }

            if (close || Released || !seen.Alive || Away != AfkStage.Active)
                return Say.None;

            // Trailing: the leader is stopped for the player.
            float distance = seen.SameRegion ? seen.DistanceToLeader : float.MaxValue;
            if (WaitingSince == null)
            {
                WaitingSince = now;
                LastCloserAt = now;
                ClosestWhileWaiting = distance;
                return Say.None;
            }
            if (distance <= ClosestWhileWaiting - CloserStep)
            {
                ClosestWhileWaiting = distance;
                LastCloserAt = now;
            }
            if (now - LastCloserAt >= GoOnWithoutMs)
            {
                Released = true;
                WaitingSince = null;
                AskedWhere = false;
                return Say.GoingOn;
            }
            if (!AskedWhere && now - WaitingSince.Value >= WhereAreYouMs)
            {
                AskedWhere = true;
                return Say.WhereAreYou;
            }
            return Say.None;
        }
    }

    #endregion

    #region Loot (owner's decision 2, 2026-11-04)

    /// <summary>
    /// A group's item rotation: how many items it has handed out, and how many of them went to the player. The player
    /// counts as <c>weight</c> members, so with seven gamebots nearby and weight 2 the player gets 2 of every 9 of these
    /// items. Upgrades (for the player or a gamebot) are given outside the rotation and not counted in it.
    /// </summary>
    public sealed class LootLedger
    {
        public int Items { get; private set; }
        public int PlayerItems { get; private set; }

        public void Record(bool toPlayer)
        {
            Items++;
            if (toPlayer)
                PlayerItems++;
        }
    }

    /// <summary>The player's loot weight, kept between 1 and <see cref="MaximumPlayerLootWeight"/>.</summary>
    public static int ClampWeight(int weight) => Math.Clamp(weight, 1, MaximumPlayerLootWeight);

    /// <summary>
    /// The rotation: whether the next item that is no one's upgrade goes to the player. The player's turn comes whenever
    /// their items fall short of their share (<c>weight / (weight + bots)</c>) of the rotation so far, the next one
    /// included: the first item, then e.g. the 5th and 10th of 9-member rotations. With no gamebot in range every item is
    /// the player's.
    /// </summary>
    public static bool NextItemGoesToPlayer(LootLedger ledger, int playerWeight, int botsInRange)
    {
        if (botsInRange <= 0)
            return true;
        int weight = ClampWeight(playerWeight);
        return (long)ledger.PlayerItems * (weight + botsInRange) < (long)(ledger.Items + 1) * weight;
    }

    /// <summary>Why an item went where it went (the BOT_LED_LOOT reason).</summary>
    public enum LootRoute { UpgradePlayer, UpgradeBot, RotationPlayer, RotationBot }

    public static string ReasonOf(LootRoute route) => route switch
    {
        LootRoute.UpgradePlayer => "upgrade-player",
        LootRoute.UpgradeBot => "upgrade-bot",
        _ => "rotation",
    };

    /// <summary>
    /// Where an item goes in a group with the player and gamebots, whoever leads (owner 2026-11-04): the player gets the
    /// primary spoils, and gamebots, who start without armour and earn it, still gear up from drops and fund their own
    /// improvements. In order: (a) an upgrade for the player goes to the player; (b) otherwise an upgrade for a gamebot in
    /// range goes to the gamebot it improves most; (c) everything else goes through the rotation, with the player counting
    /// as <paramref name="playerWeight"/> members.
    /// </summary>
    public static LootRoute Route(bool upgradeForPlayer, bool upgradeForABot, LootLedger ledger, int playerWeight, int botsInRange,
        bool playerInRange)
    {
        if (upgradeForPlayer && playerInRange)
            return LootRoute.UpgradePlayer;
        if (upgradeForABot && botsInRange > 0)
            return LootRoute.UpgradeBot;
        return playerInRange && NextItemGoesToPlayer(ledger, playerWeight, botsInRange) ? LootRoute.RotationPlayer : LootRoute.RotationBot;
    }

    /// <summary>
    /// The gamebot an item improves most: the highest gain among those it is an upgrade for (null: not an upgrade for that
    /// bot, by the gamebots' own appraisal, AutonomousBotEconomy.TryGetEquipmentUpgrade), the first of equals. -1 when it
    /// is an upgrade for none.
    /// </summary>
    public static int PickBotForUpgrade(IReadOnlyList<int?> gains)
    {
        int best = -1;
        for (int i = 0; i < gains.Count; i++)
            if (gains[i] is int gain && (best < 0 || gain > gains[best]!.Value))
                best = i;
        return best;
    }

    /// <summary>
    /// A coin drop split evenly among the members in range, each player counting as <paramref name="playerWeight"/>
    /// members: the players' part is <c>copper × players × weight / (players × weight + bots)</c>, rounded down, the rest
    /// is the bots'; each part is shared evenly, the odd coppers to the first. The shares add up to the drop exactly.
    /// </summary>
    public static (long[] Players, long[] Bots) CoinShares(long copper, int playerWeight, int players, int bots)
    {
        players = Math.Max(0, players);
        bots = Math.Max(0, bots);
        long[] toPlayers = new long[players], toBots = new long[bots];
        if (copper <= 0 || players + bots == 0)
            return (toPlayers, toBots);
        int weight = ClampWeight(playerWeight);
        long units = (long)players * weight + bots;
        long playerPart = bots == 0 ? copper : players == 0 ? 0 : (long)Math.Floor((decimal)copper * players * weight / units);
        Spread(toPlayers, playerPart);
        Spread(toBots, copper - playerPart);
        return (toPlayers, toBots);

        static void Spread(long[] shares, long total)
        {
            if (shares.Length == 0)
                return;
            long each = total / shares.Length, odd = total % shares.Length;
            for (int i = 0; i < shares.Length; i++)
                shares[i] = each + (i < odd ? 1 : 0);
        }
    }

    /// <summary>
    /// Whether a dropped item beats what the player wears, as the gamebots' own appraisal judges an upgrade: the class
    /// can use it, the player is high enough for it, and its value beats the worn item's by more than
    /// <paramref name="minimumGain"/>. An empty armour or jewellery slot is beaten by anything usable; an empty weapon
    /// hand is not (a shield or second weapon the player chose not to carry).
    /// </summary>
    public static bool IsUpgradeForPlayer(bool classCanUse, int levelRequirement, int playerLevel, bool slotKnown, bool slotHoldsItem,
        bool armourOrJewellerySlot, int newValue, int wornValue, int minimumGain) =>
        classCanUse && slotKnown && levelRequirement <= playerLevel &&
        (slotHoldsItem ? newValue - wornValue > minimumGain : armourOrJewellerySlot);

    #endregion
}

// ---------------------------------------------------------------------------------------------------------------------
// The game glue: the invite (a tell, then the native group box), the hosted view the party's pace and pulls read, the
// leader's lines, and the loot of a group with the player and gamebots.
// ---------------------------------------------------------------------------------------------------------------------
public static partial class BotLedGroups
{
    private static readonly Logger Log = LoggerManager.Create(typeof(BotLedGroups));

    /// <summary>The thread tag of a leader's invite tell, so "no thanks" to it counts as a no (AutonomousBotChatCoordinator).</summary>
    public const string InviteThread = "bot_led_invite";

    /// <summary>Unasked invites are on (bot_group_invite_player). Asking (/lfg) works either way.</summary>
    public static bool InvitesOn => Properties.BOT_GROUP_INVITE_PLAYER;

    /// <summary>What the player counts as in the loot (bot_group_player_loot_weight).</summary>
    public static int LootWeight => ClampWeight(Properties.BOT_GROUP_PLAYER_LOOT_WEIGHT);

    #region The hosted view (read without the coordinator's lock)

    /// <summary>
    /// A party that hosts the player, as its members' movement and pulls read it every tick: the coordinator writes it
    /// under its own lock once per pass; readers never take that lock.
    /// </summary>
    public sealed class HostedView
    {
        public HostedView(GamePlayer player, string sessionId)
        {
            Player = player;
            SessionId = sessionId;
        }

        public GamePlayer Player { get; }
        public string SessionId { get; }
        public volatile bool HoldsPace;
        public volatile bool BlocksPull;
        public volatile bool BlocksNewTravel;
        /// <summary>
        /// B3: the player sits in a realm raid's party (the raid leader's invite). The raid runs the party: no follow rules,
        /// no /lead, and the party's gamebots don't ride porters with the player (they muster at the raid's hub).
        /// </summary>
        public bool RaidGuest { get; init; }
    }

    private static readonly ConcurrentDictionary<Group, HostedView> Hosted = new(ReferenceEqualityComparer.Instance);

    /// <summary>The party's hosted view, or null when the group hosts no player (or the player has left it).</summary>
    public static HostedView ViewOf(Group group) =>
        group != null && Hosted.TryGetValue(group, out HostedView view) && view.Player.Group == group ? view : null;

    /// <summary>The group is a gamebot party that hosts a real player (the bots lead).</summary>
    public static bool IsHostedGroup(Group group) => ViewOf(group) != null;

    /// <summary>The real player a gamebot party hosts, or null.</summary>
    public static GamePlayer HostedPlayerOf(Group group) => ViewOf(group)?.Player;

    /// <summary>The coordinator publishes a hosted party here (and tests do).</summary>
    public static void SetHosted(Group group, HostedView view) => Hosted[group] = view;

    public static void ClearHosted(Group group)
    {
        if (group != null)
            Hosted.TryRemove(group, out _);
    }

    /// <summary>Forgets every hosted group, player memory and loot ledger (tests).</summary>
    public static void Clear()
    {
        Hosted.Clear();
        Memories.Clear();
    }

    #endregion

    #region The invite

    private static readonly ConcurrentDictionary<string, InviteMemory> Memories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> LastSeen = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>A player unseen this long started a new session when they come back (the 10-minute start, three noes).</summary>
    public const long NewSessionAfterMs = 2 * 60_000L;
    public const int PulseIntervalMs = 5_000;
    private static long _nextPulse;

    internal static InviteMemory MemoryOf(GamePlayer player, long now) =>
        Memories.GetOrAdd(player.Name, _ => new InviteMemory(now) { PlayerInvitesOff = StoredInvitesOff(player) });

    /// <summary>The player's state for the invite rules, read from the live player.</summary>
    public static PlayerState StateOf(GamePlayer player) => new(
        Alive: player.IsAlive,
        InCombat: player.InCombat,
        Riding: player.IsRiding || player.IsOnHorse,
        OnBoat: player.Steed is GameBoat,
        DialogOpen: player.CustomDialogCallback != null,
        Trading: player.TradeWindow != null,
        Grouped: player.Group != null,
        HasCompanions: BotManager.GetBotsForOwner(player).Any(bot => bot.ObjectState == GameObject.eObjectState.Active),
        Afk: player.TempProperties.GetProperty<string>(GamePlayer.AFK_MESSAGE) != null,
        InFrontier: player.CurrentZone?.IsRvR == true || player.CurrentRegion?.IsRvR == true,
        InBattleground: BattlegroundBrackets.IsBattlegroundRegion(player.CurrentRegionID),
        InRaid: player.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY) != null,
        Linkdead: player.Client?.ClientState == GameClient.eClientState.Linkdead);

    /// <summary>Every five seconds (AutonomousLivingServer's pulse): unasked invites, and asked ones still waiting for a leader.</summary>
    public static void Pulse(long now)
    {
        if (now < _nextPulse)
            return;
        _nextPulse = now + PulseIntervalMs;
        // B3: a forming raid's leader may invite a level-50 player (bot_raid_invite_player).
        try
        {
            AutonomousRealmRaid.PulsePlayerInvites(now);
        }
        catch (Exception exception)
        {
            Log.Warn("RAID_PLAYER_INVITE pulse failed", exception);
        }
        foreach (GamePlayer player in ClientService.Instance.GetPlayers())
        {
            if (player?.Client == null || player.ObjectState != GameObject.eObjectState.Active || player.CurrentRegion == null)
                continue;
            try
            {
                InviteMemory memory = MemoryOf(player, now);
                if (LastSeen.TryGetValue(player.Name, out long seen) && now - seen > NewSessionAfterMs)
                {
                    memory.OnlineSince = now;
                    memory.SessionDeclines = 0;
                    memory.Pending = false;
                }
                LastSeen[player.Name] = now;
                if (StateBlocks(StateOf(player)) != null ||
                    CadenceBlocks(memory, now, InvitesOn, AutonomousLivingServer.SinceLastBotTell(player, now)) != null)
                    continue;
                TryInvite(player, memory, now);
            }
            catch (Exception exception)
            {
                Log.Warn($"BOT_LED_INVITE pulse failed for {player.Name}", exception);
            }
        }
    }

    /// <summary>
    /// The player asked for a group (/lfg, or "lfg?" / "room for a cleric?" in a tell to a bot): an invite can come
    /// within 15 seconds (the leader answers by tell first). Returns whether a leader's invite is on its way (the caller then makes no duo offer).
    /// <paramref name="askedBot"/>: the bot the player asked, which invites when it leads a party that fits.
    /// </summary>
    public static bool OnPlayerAsked(GamePlayer player, GameBot askedBot = null)
    {
        if (player?.Client == null || player.ObjectState != GameObject.eObjectState.Active)
            return false;
        long now = GameLoop.GameLoopTime;
        InviteMemory memory = MemoryOf(player, now);
        memory.Asked(now);
        string blocked = StateBlocks(StateOf(player)) ?? CadenceBlocks(memory, now, InvitesOn, long.MaxValue);
        if (blocked != null)
        {
            LogInvite("not_sent", null, player, blocked, asked: true);
            return false;
        }
        return TryInvite(player, memory, now, askedBot);
    }

    private static bool TryInvite(GamePlayer player, InviteMemory memory, long now, GameBot askedBot = null)
    {
        bool asked = memory.IsAsking(now);
        if (!AutonomousBotGroupCoordinator.TryPickHostingLeader(player, memory, now, askedBot, out GameBot leader, out Candidate party))
        {
            if (asked && askedBot != null)
                LogInvite("not_sent", askedBot, player, "no_party_fits", asked);
            return false;
        }
        memory.Pending = true;
        if (!asked)
        {
            memory.LastUnaskedAt = now;
            // An unasked invite is that period's bot tell: the next bot-started tell waits its full gap.
            AutonomousLivingServer.CountInviteAsTell(player, now);
        }
        LogInvite("sent", leader, player, asked ? "asked" : "unasked", asked, party);
        string tell = InviteTell(leader, asked);
        // Asked: the answer, then the box, within 15 seconds of the ask (AskedLatestStartMs + AskedLongestDelayMs).
        int tellDelay = asked ? AskedTellDelayMinMs + Random.Shared.Next(AskedTellDelaySpreadMs) : 1_500 + Random.Shared.Next(2_500);
        int boxDelay = asked ? AskedBoxDelayMinMs + Random.Shared.Next(AskedBoxDelaySpreadMs) : 2_500 + Random.Shared.Next(2_000);
        AutonomousLivingServer.Later(leader, tellDelay, null, () =>
        {
            AutonomousLivingServer.Tell(leader, player, tell, InviteThread);
            // The player's dialog state belongs to the client service (as AutonomousBotGuildSocial's invites).
            AutonomousLivingServer.Later(leader, boxDelay, null,
                () => ClientService.Instance.Post(static state => SendInviteBox(state.Player, state.Leader, state.Asked),
                    (Player: player, Leader: leader, Asked: asked)));
        });
        return true;
    }

    private static string InviteTell(GameBot leader, bool asked)
    {
        string zone = AutonomousBotGroupCoordinator.HuntingPlace(leader) ?? leader.CurrentZone?.Description ?? "town";
        string target = AutonomousBotGroupCoordinator.HuntingTarget(leader);
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["destination"] = zone, ["monsters"] = target ?? "stuff" };
        // An unasked line names the hunt, so it needs one; lead_call keeps these lines out of every other tell.
        if ((asked || target != null) &&
            AutonomousBotVoices.TryLine(leader, "tell", out AutonomousBotVoiceCorpus.Picked picked, topic: asked ? "lead_invite_asked" : "lead_invite",
                slots: slots, flags: LeadCall))
            return picked.Text;
        return asked
            ? "got room, sending you an inv"
            : target != null ? $"lf 1 more for {target} in {zone}, want in?" : $"lf 1 more, heading out from {zone}, want in?";
    }

    private static void SendInviteBox(GamePlayer player, GameBot leader, bool asked)
    {
        long now = GameLoop.GameLoopTime;
        InviteMemory memory = MemoryOf(player, now);
        string blocked = player?.Out == null ? "offline" : StateBlocks(StateOf(player));
        if (blocked == null && !AutonomousBotGroupCoordinator.StillHosting(leader))
            blocked = "party_changed";
        if (blocked != null)
        {
            memory.Pending = false;
            LogInvite("not_sent", leader, player, blocked, asked);
            return;
        }
        string pronoun = leader.GenderId == (byte)eGender.Female ? "her" : "his";
        player.Out.SendCustomDialog($"{leader.Name} has invited you to join {pronoun} group. Do you wish to join?", (answering, response) =>
        {
            long at = GameLoop.GameLoopTime;
            InviteMemory answer = MemoryOf(answering, at);
            answer.Pending = false;
            if (response != 0x01)
            {
                answer.Declined(leader.DatabaseID, at);
                LogInvite("declined", leader, answering, "box", asked);
                return;
            }
            string why = StateBlocks(StateOf(answering) with { DialogOpen = false });
            // bot_led_group_join_mode (B2): with "lead", saying yes makes the player the leader at once.
            bool leadAtOnce = CurrentJoinMode == JoinMode.Lead;
            if (why == null && AutonomousBotGroupCoordinator.TryHostPlayer(leader, answering, out why, welcome: !leadAtOnce))
            {
                LogInvite("accepted", leader, answering, asked ? "asked" : "unasked", asked);
                if (leadAtOnce)
                    answering.Out.SendMessage(TakeLead(answering, "join_mode_lead"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            LogInvite("not_joined", leader, answering, why, asked);
            AutonomousLivingServer.Tell(leader, answering, why == "full" ? "sorry, we just filled up" : "nvm, plans changed, sorry");
        });
    }

    /// <summary>The player answered the leader's invite tell: "no thanks" counts as a no to that leader.</summary>
    public static void OnInviteTellAnswered(GameBot leader, GamePlayer player, bool yes)
    {
        if (leader == null || player == null || yes)
            return;
        MemoryOf(player, GameLoop.GameLoopTime).Declined(leader.DatabaseID, GameLoop.GameLoopTime);
        LogInvite("declined", leader, player, "tell", asked: false);
    }

    private static void LogInvite(string outcome, GameBot leader, GamePlayer player, string reason, bool asked, Candidate? party = null)
    {
        if (!Log.IsInfoEnabled)
            return;
        Log.Info($"BOT_LED_INVITE outcome={outcome} player=\"{player?.Name}\" level={player?.Level} leader=\"{leader?.Name}\" id={leader?.DatabaseID ?? 0} " +
                 $"asked={asked} reason={reason}" +
                 (party is { } p ? $" groupLevel={p.AverageLevel} members={p.Members} distance={(int)p.Distance}" : string.Empty));
    }

    #endregion

    #region The leader's lines

    /// <summary>What the leader says to the group it hosts the player in (topic lead_* in the voice corpus, with a plain fallback).</summary>
    public enum Lead
    {
        Welcome, WelcomeOnTheWay, MeetupReminder, MeetupGoingOn, WhereAreYou, GoingOn, AfkHold, AfkLetGo, Move, Farewell, PlayerLeft,
        // B2: the handover. Handover: /lead ("all yours"); TakeBack: /lead bot or /makeleader; LastBot: before the box.
        Handover, TakeBack, LastBot,
        // B3: the raid party's leader welcomes the player the raid leader invited.
        RaidWelcome,
        // B4: the natural break ("last pull then town", then the goodbye) and the search for a replacement.
        LastPull, TownBreak, Recruited, RecruitGaveUp, RecruitEnd,
    }

    public static string Topic(Lead what) => what switch
    {
        Lead.Welcome => "lead_welcome",
        Lead.WelcomeOnTheWay => "lead_welcome_out",
        Lead.MeetupReminder => "lead_wait",
        Lead.MeetupGoingOn => "lead_going_on",
        Lead.WhereAreYou => "lead_where",
        Lead.GoingOn => "lead_going_on",
        Lead.AfkHold => "lead_afk_hold",
        Lead.AfkLetGo => "lead_afk_let_go",
        Lead.Move => "lead_move",
        Lead.Farewell => "lead_break",
        Lead.PlayerLeft => "lead_player_left",
        Lead.Handover => "lead_handover",
        Lead.TakeBack => "lead_take",
        Lead.LastBot => "lead_last_bot",
        Lead.RaidWelcome => "lead_raid_welcome",
        Lead.LastPull => "lead_sell",
        Lead.TownBreak => "lead_town",
        Lead.Recruited => "lead_recruited",
        Lead.RecruitGaveUp => "lead_recruit_none",
        Lead.RecruitEnd => "lead_recruit_end",
        _ => "lead",
    };

    /// <summary>The flag only these calls carry (data/chat/bot_led_groups.lines), so ambient talk never says them.</summary>
    private static readonly string[] LeadCall = ["lead_call"];

    /// <summary>The plain line when no voice line fits (voices off). {place}, {camp}, {player} are filled in.</summary>
    public static string Fallback(Lead what) => what switch
    {
        Lead.Welcome => "welcome! meet up at {place}",
        Lead.WelcomeOnTheWay => "welcome! we're at {place}, come find us. /follow me if you're close",
        Lead.MeetupReminder => "still at {place}, waiting on you",
        Lead.MeetupGoingOn => "heading on to {camp}, catch up when you can, spot's saved",
        Lead.WhereAreYou => "where u at?",
        Lead.GoingOn => "going on to {camp}, meet us there",
        Lead.AfkHold => "waiting on {player}, everyone med",
        Lead.AfkLetGo => "had to move on, send me a tell when you're back",
        Lead.Move => "moving to {camp}",
        Lead.Farewell => "thats it for me, good group all",
        Lead.PlayerLeft => "{player} went ld, we carry on",
        Lead.Handover => "all yours {player}, we follow you",
        Lead.TakeBack => "ok i'll lead, finding us a camp near here",
        Lead.LastBot => "just us two left, want to lead?",
        Lead.RaidWelcome => "welcome, we wait at {place} until the raid leaves",
        Lead.LastPull => "last pull then town",
        Lead.TownBreak => "ok town to sell, good group all",
        Lead.Recruited => "got someone coming, hang tight",
        Lead.RecruitGaveUp => "no luck finding anyone, we carry on as we are",
        Lead.RecruitEnd => "couldnt find anyone, gonna call it here",
        _ => string.Empty,
    };

    /// <summary>Fills a line's {place}, {camp} and {player}.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> slots)
    {
        if (string.IsNullOrEmpty(text) || slots == null)
            return text;
        foreach ((string key, string value) in slots)
            text = text.Replace("{" + key + "}", value ?? string.Empty, StringComparison.Ordinal);
        return text;
    }

    /// <summary>
    /// The leader says it in /group (a let-go goes by tell to the player). These lines carry what the player needs to
    /// follow (where to meet, where the group went), so they are not held by the chat budget's gaps; there are only a few
    /// an hour by construction. Flavour talk keeps going through the group chatter and its budget.
    /// </summary>
    internal static void Announce(GameBot leader, Lead what, GamePlayer player, string place, string camp)
    {
        if (leader == null || player == null)
            return;
        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["place"] = string.IsNullOrWhiteSpace(place) ? "the camp" : place,
            ["destination"] = string.IsNullOrWhiteSpace(place) ? "the camp" : place,
            ["camp"] = string.IsNullOrWhiteSpace(camp) ? "the camp" : camp,
            ["player"] = player.Name,
        };
        string text = AutonomousBotVoices.TryLine(leader, what == Lead.AfkLetGo ? "tell" : "group", out AutonomousBotVoiceCorpus.Picked picked,
            topic: Topic(what), slots: slots, flags: LeadCall)
            ? picked.Text
            : Fill(Fallback(what), slots);
        AutonomousLivingServer.Later(leader, 800 + Random.Shared.Next(1_500), null, () =>
        {
            if (what == Lead.AfkLetGo)
                AutonomousLivingServer.Tell(leader, player, text);
            else
                AutonomousLivingServer.Speak(leader, AutonomousLivingServer.Channel.Group, text);
        });
    }

    #endregion

    #region Loot

    private static readonly ConditionalWeakTable<Group, LootLedger> Ledgers = new();

    /// <summary>An autonomous gamebot near a drop that takes a loot share (not a companion, not the Epic Special Forces).</summary>
    private static bool TakesLoot(GameBot bot, GameObject drop) =>
        bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !bot.IsEpicSpecialForce && bot.DatabaseID > 0 &&
        bot.ObjectState == GameObject.eObjectState.Active && bot.CurrentRegion == drop.CurrentRegion &&
        bot.IsWithinRadius(drop, WorldMgr.VISIBILITY_DISTANCE);

    /// <summary>A group with the real player and at least one autonomous gamebot, whoever leads.</summary>
    public static bool IsMixed(List<GameLiving> members) =>
        members.Any(member => member is GamePlayer) &&
        members.Any(member => member is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false });

    /// <summary>
    /// Coin in a group with the player and gamebots: split evenly among the members in range, the player counting as
    /// <see cref="LootWeight"/> members. False when the group is not mixed (the old rules apply).
    /// </summary>
    public static bool TrySplitMoney(Group group, GameMoney money, out TryPickUpResult result)
    {
        result = TryPickUpResult.DoesNotWant;
        if (group == null || money == null)
            return false;
        List<GameLiving> members = group.GetMembersInTheGroup();
        if (!IsMixed(members))
            return false;
        money.AssertLockAcquisition();
        if (!group.AutosplitCoins)
            return true;
        GamePlayer[] players = members.OfType<GamePlayer>()
            .Where(player => player.ObjectState == GameObject.eObjectState.Active && player.CanSeeObject(money)).ToArray();
        GameBot[] bots = members.OfType<GameBot>().Where(bot => TakesLoot(bot, money)).ToArray();
        if (players.Length + bots.Length == 0)
        {
            result = TryPickUpResult.Blocked;
            return true;
        }
        (long[] toPlayers, long[] toBots) = CoinShares(money.Value, LootWeight, players.Length, bots.Length);
        for (int i = 0; i < players.Length; i++)
        {
            long share = players[i].ApplyGuildDues(toPlayers[i]);
            if (share <= 0)
                continue;
            players[i].AddMoney(share, LanguageMgr.GetTranslation(players[i].Client.Account.Language, "GamePlayer.PickupObject.YourLootShare",
                Money.GetString(toPlayers[i])));
            InventoryLogging.LogInventoryAction("(ground)", players[i], eInventoryActionType.Loot, share);
        }
        for (int i = 0; i < bots.Length; i++)
            if (toBots[i] > 0)
                AutonomousBotGuildEconomy.PayLootShare(bots[i], toBots[i]);
        _ = money.RemoveFromWorld();
        result = TryPickUpResult.Success;
        return true;
    }

    /// <summary>
    /// An item in a group with the player and gamebots (<see cref="Route"/>): an upgrade for the player goes to them; else
    /// an upgrade for a gamebot in range goes to the one it improves most; else the rotation gives the player
    /// <see cref="LootWeight"/> shares and the rest go at random to the gamebots in range. False when the group is not
    /// mixed (the old rules apply).
    /// </summary>
    public static bool TrySplitItem(Group group, WorldInventoryItem item, out TryPickUpResult result)
    {
        result = TryPickUpResult.DoesNotWant;
        if (group == null || item == null)
            return false;
        List<GameLiving> members = group.GetMembersInTheGroup();
        if (!IsMixed(members))
            return false;
        item.AssertLockAcquisition();
        if (!group.AutosplitLoot || item.IsPlayerDiscarded)
            return true;
        GamePlayer[] players = members.OfType<GamePlayer>()
            .Where(player => player.ObjectState == GameObject.eObjectState.Active && player.AutoSplitLoot && player.CanSeeObject(item))
            .OrderBy(_ => Random.Shared.Next()).ToArray();
        GameBot[] bots = members.OfType<GameBot>().Where(bot => TakesLoot(bot, item)).OrderBy(_ => Random.Shared.Next()).ToArray();
        LootLedger ledger = Ledgers.GetOrCreateValue(group);
        lock (ledger)
        {
            // (a) an upgrade for the player, (b) else an upgrade for the gamebot it improves most, (c) else the rotation.
            GamePlayer upgradeFor = players.FirstOrDefault(player => IsUpgradeFor(player, item.Item));
            int?[] gains = bots.Select(bot => BotUpgradeGain(bot, item.Item)).ToArray();
            LootRoute route = Route(upgradeFor != null, gains.Any(gain => gain != null), ledger, LootWeight, bots.Length, players.Length > 0);
            if (route == LootRoute.UpgradePlayer && GiveToAPlayer(group, [upgradeFor], item, ledger, LootRoute.UpgradePlayer))
            {
                result = TryPickUpResult.Success;
                return true;
            }
            if (route is LootRoute.UpgradePlayer or LootRoute.UpgradeBot)
            {
                // The bot it improves most first; one that can't carry it passes it to the next.
                for (int pick = PickBotForUpgrade(gains); pick >= 0; pick = PickBotForUpgrade(gains))
                {
                    if (GiveToABot(group, bots[pick], item, ledger, LootRoute.UpgradeBot, gains[pick]))
                    {
                        result = TryPickUpResult.Success;
                        return true;
                    }
                    gains[pick] = null;
                }
            }
            bool playersTurn = players.Length > 0 && NextItemGoesToPlayer(ledger, LootWeight, bots.Length);
            if (playersTurn && GiveToAPlayer(group, players, item, ledger, LootRoute.RotationPlayer))
            {
                result = TryPickUpResult.Success;
                return true;
            }
            foreach (GameBot bot in bots)
            {
                if (GiveToABot(group, bot, item, ledger, LootRoute.RotationBot, null))
                {
                    result = TryPickUpResult.Success;
                    return true;
                }
            }
            // No gamebot could carry it: a player who can still has it.
            if (!playersTurn && GiveToAPlayer(group, players, item, ledger, LootRoute.RotationPlayer))
            {
                result = TryPickUpResult.Success;
                return true;
            }
        }
        result = TryPickUpResult.Blocked;
        return true;
    }

    /// <summary>
    /// How much the item would improve this gamebot, by the gamebots' own appraisal (AutonomousBotEconomy.TryGetEquipmentUpgrade,
    /// the rule loot, merchants and the Realm Exchange share), or null when it is no upgrade for it.
    /// </summary>
    private static int? BotUpgradeGain(GameBot bot, DbInventoryItem item)
    {
        try
        {
            if (item == null || item.IsStackable || !AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, item, out eInventorySlot slot))
                return null;
            return AutonomousBotEconomy.EquipmentValue(item) - AutonomousBotEconomy.EquipmentValue(bot.Inventory.GetItem(slot));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void LogLoot(LootRoute route, string to, string item, int? gain = null)
    {
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_LED_LOOT to=\"{to}\" item=\"{item}\" reason={ReasonOf(route)}" + (gain is int value ? $" gain={value}" : string.Empty));
    }

    private static bool GiveToABot(Group group, GameBot bot, WorldInventoryItem item, LootLedger ledger, LootRoute route, int? gain)
    {
        string name = item.Item.Name, shown = item.Item.GetName(1, true);
        // The bot's own pickup stores it and, when it is an upgrade, equips it (GameBot.TryAutoPickUpItem).
        if (bot.TryAutoPickUpItem(item) != TryPickUpResult.Success)
            return false;
        if (route == LootRoute.RotationBot)
            ledger.Record(false);
        group.SendMessageToGroupMembers(LanguageMgr.GetTranslation(Properties.DB_LANGUAGE, "GamePlayer.PickupObject.Autosplit",
            shown, bot.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
        LogLoot(route, bot.Name, name, gain);
        return true;
    }

    private static bool GiveToAPlayer(Group group, IEnumerable<GamePlayer> players, WorldInventoryItem item, LootLedger ledger, LootRoute route)
    {
        foreach (GamePlayer player in players)
        {
            DbInventoryItem loot = item.Item;
            bool stored = loot.IsStackable
                ? player.Inventory.AddTemplate(loot, loot.Count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                : player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, loot);
            if (!stored)
            {
                player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "GamePlayer.PickupObject.BackpackFull"),
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                continue;
            }
            if (route == LootRoute.RotationPlayer)
                ledger.Record(true);
            group.SendMessageToGroupMembers(LanguageMgr.GetTranslation(player.Client.Account.Language, "GamePlayer.PickupObject.Autosplit",
                loot.GetName(1, true), player.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            InventoryLogging.LogInventoryAction("(ground)", player, eInventoryActionType.Loot, loot.Template, loot.IsStackable ? loot.Count : 1);
            LogLoot(route, player.Name, loot.Name);
            _ = item.RemoveFromWorld();
            return true;
        }
        return false;
    }

    private static readonly eInventorySlot[] ArmourAndJewellery =
    [
        eInventorySlot.HeadArmor, eInventorySlot.HandsArmor, eInventorySlot.FeetArmor, eInventorySlot.TorsoArmor, eInventorySlot.Cloak,
        eInventorySlot.LegsArmor, eInventorySlot.ArmsArmor, eInventorySlot.Jewelry, eInventorySlot.Neck, eInventorySlot.Waist,
        eInventorySlot.LeftBracer, eInventorySlot.RightBracer, eInventorySlot.LeftRing, eInventorySlot.RightRing, eInventorySlot.Mythical,
    ];

    private static readonly eInventorySlot[] Weapons =
        [eInventorySlot.RightHandWeapon, eInventorySlot.LeftHandWeapon, eInventorySlot.TwoHandWeapon, eInventorySlot.DistanceWeapon];

    /// <summary>The item beats what the player wears in its slot (rings and bracers: the weaker of the two), and the class can use it.</summary>
    public static bool IsUpgradeFor(GamePlayer player, DbInventoryItem item)
    {
        if (player?.Inventory == null || item?.Template == null || item.IsStackable)
            return false;
        try
        {
            var slot = (eInventorySlot)item.Item_Type;
            bool armour = ArmourAndJewellery.Contains(slot);
            bool known = armour || Weapons.Contains(slot);
            if (!known)
                return false;
            DbInventoryItem worn = slot is eInventorySlot.LeftRing or eInventorySlot.RightRing
                ? Weaker(player.Inventory.GetItem(eInventorySlot.LeftRing), player.Inventory.GetItem(eInventorySlot.RightRing))
                : slot is eInventorySlot.LeftBracer or eInventorySlot.RightBracer
                ? Weaker(player.Inventory.GetItem(eInventorySlot.LeftBracer), player.Inventory.GetItem(eInventorySlot.RightBracer))
                : player.Inventory.GetItem(slot);
            bool canUse = GameServer.ServerRules.CheckAbilityToUseItem(player, item.Template);
            return IsUpgradeForPlayer(canUse, item.LevelRequirement, player.Level, true, worn != null, armour,
                AutonomousBotEconomy.EquipmentValue(item), AutonomousBotEconomy.EquipmentValue(worn), MinimumUpgradeGain);
        }
        catch (Exception)
        {
            return false;
        }

        static DbInventoryItem Weaker(DbInventoryItem a, DbInventoryItem b) =>
            a == null || b == null ? null : AutonomousBotEconomy.EquipmentValue(a) <= AutonomousBotEconomy.EquipmentValue(b) ? a : b;
    }

    /// <summary>The gamebots' own upgrade margin (AutonomousBotEconomy).</summary>
    public const int MinimumUpgradeGain = 8;

    #endregion
}
