using System;
using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>What the group's pick knows of one enemy it may engage (one snapshot per look; pure input).</summary>
    /// <param name="Rank">Kill-order rank: <see cref="CompanionRvrEngage.Rank"/> (0 healers first, 3 pets last).</param>
    /// <param name="Noticed">The group has noticed it (perception: noticed and the reaction delay over), or a stealther
    /// the close-range rule caught.</param>
    /// <param name="Controlled">A groupmate holds it with a mesmerize or a breakable root (hitting it breaks the hold).</param>
    /// <param name="HealthPercent">Its health, 0-100.</param>
    /// <param name="FromPlayer">Distance from the player (the bound is measured from him, not from the chasers).</param>
    public readonly record struct CompanionRvrCandidate(int Rank, bool Noticed, bool Controlled, int HealthPercent, int FromPlayer);

    /// <summary>
    /// #664 step 1: the player's group picks and engages enemies on its own in the frontier, the battlegrounds, Darkness
    /// Falls and the neutral battle dungeons, not only the player's target (FarmVibes: in Thidranki, /spawn companions
    /// followed the player and attacked only when he did).
    ///
    /// Owner answers (#663 and #664, 2026-11-26 22:19 / 22:23):
    /// - who: the player's whole group (/spawn and /raid helpers, hired companions, /bot companions, befriended gamebots);
    ///   autonomous gamebots outside a player's group are unchanged;
    /// - where: the #663 places (AutonomousWorldBotController.IsBattleground); PvE stays as it is;
    /// - eyes: the perception model (AutonomousRvrPerception, rvr_bot_awareness: line of sight, the notice curve, spotting
    ///   by groupmates, stealth by the close-range rule), bounded by the player's position; /defensive a tighter bound;
    /// - enemy guards and keep lords are never engaged unless the player attacks them (only enemy gamebots and their pets
    ///   are candidates: CompanionPvpEngagement.Enemy);
    /// - a size-up (#662's RvrSightSizeUp): never open on a clearly losing fight; always defend (the threat path, kept
    ///   above this pick, is never sized up);
    /// - on by default with a server switch.
    ///
    /// The pick (the period kill order, docs/rvr-audit-1.2.3/reference.md 3.4: "anything that wears a dress" first, tanks
    /// last): when the group opens a fight (<see cref="Opening"/>: no pick and nobody fighting an enemy-realm foe) it never
    /// takes a held enemy and sizes the fight up first; mid-fight it takes a held enemy only when that is the last one
    /// left; then healers, then casters, then fighters, then pets; then the lowest health; then the nearest the player.
    /// Never an enemy in a safe or bind area or under its realm's tower or keep guards (<see cref="MayOpenOn"/>). Sticky:
    /// the group stays on its pick until a better-ranked enemy appears, and through a short spell out of sight
    /// (<see cref="KeepsOutOfSight"/>). The player's own target (leader focus, #571) and an enemy hitting the group
    /// (CompanionPvpEngagement.RecordThreat) come before it.
    ///
    /// #664 step 2: <c>/hold</c> (owner answer 3, 2026-11-26 22:23: "companions only defend themselves") and the drop
    /// leash (<see cref="MayFight(bool, bool, bool, bool, int, int, bool, bool)"/>): no bot of the player's group keeps an enemy-realm target past
    /// <see cref="Leash"/> from the player unless the player is attacking it or it is hitting the group.
    /// Pure rules here; the world read is CompanionRvrEngage.World.cs. Tests only until seen in a PC log.
    /// </summary>
    public static partial class CompanionRvrEngage
    {
        public const string PropertyKey = "bot_companion_rvr_engage";

        public static bool On => Properties.BOT_COMPANION_RVR_ENGAGE;

        /// <summary>/aggressive: candidates within this of the player (the /raid frontier range, owner 2026-10-07).</summary>
        public const int AggressiveBound = CompanionPvpEngagement.FrontierRaidAggressiveRadius;

        /// <summary>/defensive: the tighter bound (the /raid frontier defensive range).</summary>
        public const int DefensiveBound = CompanionPvpEngagement.FrontierRaidDefensiveRadius;

        /// <summary>How often the group looks: the perception curve is calibrated for the frontier scan's cadence.</summary>
        public const int LookIntervalMilliseconds = AutonomousRvrPerception.DefaultScanIntervalMilliseconds;

        /// <summary>Line-of-sight rays per enemy per look: from the nearest few of the group (the player and his bots).</summary>
        public const int LookersPerEnemy = 3;

        /// <summary>
        /// The size-up's boldness for the player's group: the boldest gamebot line (RvrSightSizeUp.LineFor(1) = 0.64), so
        /// the group holds only when the enemy is more than about 1.5 times its strength ("clearly losing", owner answer 4
        /// on #663). The owner set 0.64 on #664 (2026-11-27 12:11Z).
        /// </summary>
        public const double SizeUpBoldness = 1.0;

        /// <summary>The bound around the player, by stance. Pure.</summary>
        public static int Bound(bool defensive) => defensive ? DefensiveBound : AggressiveBound;

        /// <summary>
        /// How much farther than the bound an enemy the group is already on may go before it is let go (step 2): a pick at
        /// the edge does not flip between picked and dropped each look. A design value.
        /// </summary>
        public const int LeashMargin = 250;

        /// <summary>The drop leash around the player, by stance: the bound plus <see cref="LeashMargin"/>. Pure.</summary>
        public static int Leash(bool defensive) => Bound(defensive) + LeashMargin;

        /// <summary>
        /// How long the group keeps its pick after it was last seen (step 2, review of #765: one look without line of sight
        /// dropped it, and the group opened again). A design value: a few looks, long enough for a ridge or a pillar.
        /// </summary>
        public const long SightMemoryMilliseconds = 6_000;

        /// <summary>The pick is kept though this look did not see it: it was seen within <see cref="SightMemoryMilliseconds"/>
        /// (<paramref name="lastSeen"/> 0 or less: never seen). Pure.</summary>
        public static bool KeepsOutOfSight(bool seenNow, long now, long lastSeen) =>
            seenNow || lastSeen > 0 && now - lastSeen < SightMemoryMilliseconds;

        /// <summary>
        /// The group opens a fight: it has no pick and nobody in it is fighting an enemy-realm foe (review of #765: "no pick"
        /// alone meant every new pick after a kill was an opening). Only an opening is sized up, and only an opening never
        /// takes a held enemy. Pure.
        /// </summary>
        public static bool Opening(bool hasPick, bool groupFighting) => !hasPick && !groupFighting;

        /// <summary>
        /// Whether a bot of the player's group may fight <paramref name="target"/> (step 2). Under <c>/hold</c> only what is
        /// hitting the bot itself or its pet. Otherwise, in a #663 place, an enemy-realm target farther than the leash from
        /// the player is let go unless the player is attacking it or it is hitting someone in the group; everything else is
        /// unchanged (PvE, monsters, the leash's own exceptions). Pure.
        /// </summary>
        public static bool MayFight(bool holding, bool hitsMe, bool warZone, bool enemyRealm, int fromPlayer, int leash,
            bool playersTarget, bool hitsGroup)
        {
            if (holding) return hitsMe;
            return !warZone || !enemyRealm || fromPlayer <= leash || playersTarget || hitsGroup || hitsMe;
        }

        /// <summary>The group pick runs: the switch is on and the player is in a #663 place. Pure.</summary>
        public static bool Applies(bool on, bool warZone) => on && warZone;

        /// <summary>
        /// A bot of the player's group joins the pick: not on the passive stance, not suspended by /bot hold, not held in
        /// place by /bot stay. Pure.
        /// </summary>
        public static bool Joins(bool inPlayersGroup, bool passiveStance, bool suspended, bool staying) =>
            inPlayersGroup && !passiveStance && !suspended && !staying;

        /// <summary>Kill-order rank of a role: healers and hybrid healers 0, casters 1, fighters 2, pets 3. Pure.</summary>
        public static int Rank(RvrRole role) => role switch
        {
            RvrRole.Healer or RvrRole.Support => 0,
            RvrRole.Caster => 1,
            RvrRole.Pet => 3,
            _ => 2,
        };

        /// <summary>A candidate the group may open on: noticed and within the bound of the player. Pure.</summary>
        public static bool Eligible(in CompanionRvrCandidate candidate, int bound) =>
            candidate.Noticed && candidate.FromPlayer <= bound;

        /// <summary>A keep or tower guard's cover when its brain gives no aggro range (the keep guards' 1,800).</summary>
        public const int DefaultGuardCover = 1_800;

        /// <summary>The widest guard cover (the keep archers' 2,100): how far around an enemy the guards are looked for.</summary>
        public const int MaxGuardCover = 2_100;

        /// <summary>How far a guard with this aggro range covers (its range, at most <see cref="MaxGuardCover"/>). Pure.</summary>
        public static int GuardCover(int aggroRange) => aggroRange > 0 ? Math.Min(aggroRange, MaxGuardCover) : DefaultGuardCover;

        /// <summary>
        /// The group may pick this enemy on its own: it is not in a safe or bind area and not within the cover of a live
        /// guard of a realm hostile to the player (charging it there brings the guards in). The player's own target (leader
        /// focus) and an enemy hitting the group come before the pick and are not held by this. Pure.
        /// </summary>
        public static bool MayOpenOn(bool safeArea, bool underHostileGuards) => !safeArea && !underHostileGuards;

        /// <summary>Whether <paramref name="a"/> comes before <paramref name="b"/> in the pick order. Pure.</summary>
        public static bool Before(in CompanionRvrCandidate a, in CompanionRvrCandidate b)
        {
            if (a.Controlled != b.Controlled) return !a.Controlled;
            if (a.Rank != b.Rank) return a.Rank < b.Rank;
            if (a.HealthPercent != b.HealthPercent) return a.HealthPercent < b.HealthPercent;
            return a.FromPlayer < b.FromPlayer;
        }

        /// <summary>
        /// The group's pick: the index of the candidate to engage, or -1. <paramref name="current"/> is the index of the
        /// group's pick from before (-1 when none or gone); it is kept while it is eligible, not held by a groupmate while a
        /// free one is there, and no eligible candidate outranks it. <paramref name="opening"/> (<see cref="Opening"/>): the
        /// group would open a fight, so a held enemy is never taken (mez and run: the group does not break the hold);
        /// mid-fight (the group is already fighting) a held enemy is taken only when it is the last one left. Pure.
        /// </summary>
        /// <param name="leash">How far from the player the pick from before is kept (step 2: <see cref="Leash"/>); a new
        /// pick is always within <paramref name="bound"/>. Below zero: the bound.</param>
        public static int Pick(IReadOnlyList<CompanionRvrCandidate> candidates, int bound, int current = -1, bool opening = false,
            int leash = -1)
        {
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
                if (Eligible(candidates[i], bound) && !(opening && candidates[i].Controlled) &&
                    (best < 0 || Before(candidates[i], candidates[best])))
                    best = i;
            if (current < 0 || current >= candidates.Count || !Eligible(candidates[current], Math.Max(bound, leash)) ||
                opening && candidates[current].Controlled)
                return best;
            if (best < 0)
                return current;
            CompanionRvrCandidate kept = candidates[current], better = candidates[best];
            bool keep = (!kept.Controlled || better.Controlled) && kept.Rank <= better.Rank;
            return keep ? current : best;
        }

        /// <summary>
        /// The size-up before the group opens on its own (#662's rule with the group's line, no misread): the call, odds and
        /// line. Never asked when the group is attacked (the threat path engages first). Pure.
        /// </summary>
        public static RvrSightDecision SizeUp(in RvrSightFacts facts) =>
            RvrSightSizeUp.Decide(facts with { Attacked = false }, SizeUpBoldness, RvrSkillTier.Veteran, 1.0);

        /// <summary>The log row is change-only: a new call, or the same call on another target. Pure.</summary>
        public static bool Logs<T>(string call, T target, string lastCall, T lastTarget) where T : class =>
            call != null && (!string.Equals(call, lastCall, StringComparison.Ordinal) || !ReferenceEquals(target, lastTarget));
    }
}
