namespace DOL.GS
{
    /// <summary>
    /// Owner 2026-11-10: solo melee bots kept being sent to mygga camps (Aegir's Landing / Munin Sound): 353 of 916
    /// AUTONOMOUS_PULL_TIMEOUT in 48 h were melee bots under a flying mygga they could never reach. A solo bot with no
    /// ranged attack now skips a camp whose monsters fly; ranged bots and groups keep those camps. Pure rules only.
    /// </summary>
    public static class AutonomousFlyerCampPolicy
    {
        /// <summary>A camp is a flyer camp when most live monsters of its name around it fly.</summary>
        public static bool IsFlyerCamp(int flyingMobs, int totalMobs) =>
            totalMobs > 0 && flyingMobs > 0 && flyingMobs * 2 > totalMobs;

        /// <summary>A bot fights a flyer from range on its own: a nuking caster, or an archer with a usable bow.</summary>
        public static bool HasRangedAttack(bool spellRanged, bool archerWithUsableBow) =>
            spellRanged || archerWithUsableBow;

        /// <summary>At camp selection: only a solo bot with no ranged attack skips a flyer camp.</summary>
        public static bool SkipsCamp(bool flyerCamp, bool solo, bool rangedAttack) =>
            flyerCamp && solo && !rangedAttack;

        /// <summary>At target selection: a solo melee bot takes a flying monster only when it comes down to it - an
        /// aggressive one the bot opens on from range (thrown weapon, crossbow) and then waits for.</summary>
        public static bool SoloCanTakeFlyer(bool rangedAttack, bool rangedOpener, bool aggressive) =>
            rangedAttack || rangedOpener && aggressive;

        /// <summary>A solo melee puller proves a full path to its next target once a pull at this camp has failed (cheap:
        /// no extra navmesh work on an ordinary camp).</summary>
        public static bool VerifiesMeleePullRoute(bool solo, bool meleePuller, int failedPullsAtCamp) =>
            solo && meleePuller && failedPullsAtCamp > 0;
    }
}
