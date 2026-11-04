using System;
using System.Collections.Generic;
using System.Linq;

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
    /// <summary>An asked invite (/lfg, "lfg?" to a bot) can come within a minute of asking.</summary>
    public const long AskedWindowMs = 60_000L;
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

        public bool IsAsking(long now) => now - AskedAt >= 0 && now - AskedAt <= AskedWindowMs;

        public bool LeaderDeclined(long leaderId, long now) =>
            DeclinedByLeader.TryGetValue(leaderId, out long at) && now - at < LeaderDeclineMs;
    }

    /// <summary>
    /// Why no invite may be sent now, or null when one may. Asked (within a minute of /lfg or a tell): only a box that
    /// is already waiting stops it. Unasked: invites on, 10 minutes online, 45 minutes since the last unasked invite and
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
    /// counts as <c>weight</c> members, so with seven gamebots nearby and weight 2 the player gets 2 of every 9 items.
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
    /// Whether the next item goes to the player. An item the player's class can use that beats what they wear comes to
    /// them first. Otherwise the player's turn comes whenever their items fall short of their share
    /// (<c>weight / (weight + bots)</c>) of the items so far, the next one included: the first item, then e.g. the 5th
    /// and 10th of 9-member rotations. With no gamebot in range every item is the player's.
    /// </summary>
    public static bool NextItemGoesToPlayer(LootLedger ledger, int playerWeight, int botsInRange, bool upgradeForPlayer)
    {
        if (upgradeForPlayer || botsInRange <= 0)
            return true;
        int weight = ClampWeight(playerWeight);
        return (long)ledger.PlayerItems * (weight + botsInRange) < (long)(ledger.Items + 1) * weight;
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
