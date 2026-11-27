using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>Why a party ranged pull did or did not start (#569 step 9; True165 0e0a397).</summary>
    public enum PartyPullStart
    {
        /// <summary>The ranged pull is under way.</summary>
        Started,
        /// <summary>A pull is running, its retry window is open, or the party isn't ready: stand and wait.</summary>
        Waiting,
        /// <summary>Not a ranged-pull party (solo, fewer than 3, not group PvE).</summary>
        NotApplicable,
        /// <summary>The monster is already in the party: a real fight, not a pull.</summary>
        ContactFight,
        /// <summary>The puller has no ranged pull (a Savage, or no bow or pull spell).</summary>
        NoRangedPull,
        /// <summary>Beyond the short proven approach to a firing point.</summary>
        TooFar,
        /// <summary>No firing point this try; the party waits for the retry.</summary>
        Unreachable,
        /// <summary>Benched for this camp after its tries (#539): pulled the ordinary way.</summary>
        Benched
    }

    /// <summary>What the party puller's camp turn does after a party ranged pull attempt.</summary>
    public enum PartyPullFallback
    {
        /// <summary>The ranged pull runs, waits for its retry window, or retries an unreachable target: stand and report.</summary>
        Handled,
        /// <summary>Too far for the short proven approach: never walk the tank in; look for a nearer target.</summary>
        SkipTooFar,
        /// <summary>The designed close-in pull: contact fight, no ranged pull, not a ranged-pull party, or benched.</summary>
        OrdinaryPull
    }

    /// <summary>
    /// The rest of the party puller's camp turn (1.2.3, #569 step 9). Port of zomboden/OfflineDAoC-True165 0e0a397
    /// (GPL-3.0, shadowofze); step 1 (AutonomousPartyPullStall) took its stall monitor, and Living Server already had the
    /// starvation fix (6d41a77) and the unreachable release (#539). This takes the three parts left:
    /// <list type="bullet">
    /// <item>A target too far for the short approach to a firing point is skipped for a minute instead of falling
    /// through to the ordinary pull, which walked the tank into the pack (owner, 2026-10-06: "a tank walking into a large
    /// pack never works at any level"). After 45 s of only too-far targets the party's camp point moves onto the target's
    /// floor, at most twice per camp in 10 minutes (AUTONOMOUS_GROUP_CAMP_ADVANCED).</item>
    /// <item>A puller that is not the leader counts as at the camp when it stands beside a leader who is, so it gets its
    /// camp turn even when its formation slot is outside the arrival ring.</item>
    /// <item>That puller waits beside the party at an empty camp instead of patrolling away from it.</item>
    /// </list>
    /// Autonomous gamebot PvE parties only, outdoors only. Pure rules; the controller and coordinator wire them.
    /// </summary>
    public static class AutonomousPartyPullCamp
    {
        /// <summary>The switch bot_party_puller_camp_turn (declared in ServerProperties, as the launcher reads it).</summary>
        public static bool CampTurn => Properties.BOT_PARTY_PULLER_CAMP_TURN;

        public const long TooFarSkipMilliseconds = 60_000;
        public const long TooFarAdvanceAfterMilliseconds = 45_000;
        /// <summary>A skip this much older than the last one is not "consecutive" (one too-far target is re-found every 60 s).</summary>
        public const long TooFarEpisodeGapMilliseconds = TooFarSkipMilliseconds + 15_000;
        public const long TooFarLogMilliseconds = 3 * 60_000L;
        public const int MaximumAdvancesPerWindow = 2;
        public const long AdvanceWindowMilliseconds = 10 * 60_000L;

        /// <summary>A non-leader puller is at the camp when it is inside the ring, or beside a leader who is.</summary>
        public static bool PartyPullerHasArrived(int pullerCampDistance, int leaderCampDistance, int pullerToLeader,
            int arrivalRadius, int cohesionRadius) =>
            pullerCampDistance <= arrivalRadius ||
            leaderCampDistance <= arrivalRadius && pullerToLeader <= cohesionRadius;

        /// <summary>
        /// After TryBegin: the bool it returned, why, and whether the switch is on. Off keeps today's fall-through (anything
        /// not handled is the ordinary pull). On, too far never reaches the ordinary pull.
        /// </summary>
        public static PartyPullFallback AfterPartyPull(bool rangedHandled, PartyPullStart why, bool campTurn)
        {
            if (!campTurn)
                return rangedHandled ? PartyPullFallback.Handled : PartyPullFallback.OrdinaryPull;
            if (why == PartyPullStart.TooFar)
                return PartyPullFallback.SkipTooFar;
            if (why is PartyPullStart.ContactFight or PartyPullStart.NoRangedPull or PartyPullStart.Benched or
                PartyPullStart.NotApplicable)
                return PartyPullFallback.OrdinaryPull;
            return PartyPullFallback.Handled;   // Started, Waiting, Unreachable (before the bench)
        }

        public static bool ShouldAdvanceAfterTooFar(long tooFarMilliseconds) =>
            tooFarMilliseconds >= TooFarAdvanceAfterMilliseconds;

        /// <summary>The empty-camp clock after a turn that only skipped a too-far target: it keeps running, or starts.</summary>
        public static long EmptyClockAfterTooFarSkip(long emptySinceBefore, long now) =>
            emptySinceBefore == 0 ? now : emptySinceBefore;

        /// <summary>
        /// Whether this too-far skip continues the episode: only consecutive too-far turns count toward the 45 s advance. A
        /// cleared clock, a new camp, group combat since the last skip, or a long gap starts a new episode.
        /// </summary>
        public static bool TooFarEpisodeContinues(long since, bool sameCamp, long lastSkipTick, long lastGroupCombatTick,
            long now) =>
            since != 0 && sameCamp && lastGroupCombatTick <= lastSkipTick &&
            now - lastSkipTick <= TooFarEpisodeGapMilliseconds;

        /// <summary>A turn with no target at all ends the too-far episode once no too-far skip is still pending.</summary>
        public static bool EmptyTurnEndsTooFarEpisode(long skipPendingUntil, long now) => now >= skipPendingUntil;

        /// <summary>AUTONOMOUS_PARTY_PULL_TOO_FAR: at most one line per puller per 3 minutes.</summary>
        public static bool ShouldLogTooFar(long lastLogTick, long now) =>
            lastLogTick == 0 || now - lastLogTick >= TooFarLogMilliseconds;

        /// <summary>Advances still allowed at this camp: fewer than 2 in the last 10 minutes. Prunes old ticks.</summary>
        public static bool CanAdvanceCamp(List<long> advanceTicks, long now)
        {
            if (advanceTicks == null) return true;
            advanceTicks.RemoveAll(tick => now - tick >= AdvanceWindowMilliseconds || tick > now);
            return advanceTicks.Count < MaximumAdvancesPerWindow;
        }
    }
}
