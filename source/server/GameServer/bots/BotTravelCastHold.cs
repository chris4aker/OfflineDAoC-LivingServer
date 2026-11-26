// Hand port from zomboden/OfflineDAoC-True165 03dce76ee (bots/BotTravelCastHold.cs, GPL-3.0), reviewed to e2af3f2 (#567 A1).
// Changed: the settle runs from when this bot last saw its party walk (our MovementComponent keeps no last-walk tick), and
// only the buffs the formation follow would refuse anyway (CompanionFollowPolicy.IsOrdinaryBuff) are held.
namespace DOL.GS
{
    /// <summary>
    /// #567 A1: an autonomous gamebot party (no player) kept stopping and turning while it walked. Its buff upkeep
    /// (BotBrain.TryMaintainTravelAndClassBuffs) stopped a bot for a cast that GameBot.CastSpell then refused
    /// (AutonomousGroupFormationFollow.DeferBuff), and walked it toward a member out of buff range until the formation
    /// follow turned it back. The ordinary out-of-combat buffs now wait until the party has stood still a moment. Heals,
    /// fights, songs and the travel speed buff are never held.
    /// </summary>
    public static class BotTravelCastHold
    {
        /// <summary>
        /// The party counts as travelling this long after it was last seen walking. True 1.65 (owner 2026-11-10): longer
        /// than the slowest travel AI turn, so a leader's pause between path legs never counts as standing still.
        /// </summary>
        public const long SettleMilliseconds = 6_000;

        /// <summary>
        /// While held, the upkeep looks again within this long (main-session review of #700): a held pass that casts nothing
        /// must not rescan on every think, and must sample the walk well inside <see cref="SettleMilliseconds"/> so a pause
        /// between path legs is never mistaken for a stop.
        /// </summary>
        public const long HeldRecheckMilliseconds = 1_000;

        /// <summary>Pure: the wait before the upkeep's next scan, the ordinary <paramref name="milliseconds"/> or, while held,
        /// at most <see cref="HeldRecheckMilliseconds"/>.</summary>
        public static long NextScanAfter(bool held, long milliseconds) =>
            held && milliseconds > HeldRecheckMilliseconds ? HeldRecheckMilliseconds : milliseconds;

        /// <summary>
        /// Pure: an autonomous party holds its ordinary upkeep while the bot or its leader walks, or within
        /// <see cref="SettleMilliseconds"/> of last being seen walking (-1: never seen). A known stop (the bot or its leader
        /// sitting or resting) ends the settle at once. Never while anyone in it fights.
        /// </summary>
        public static bool Holds(bool autonomousParty, bool anyoneFighting, bool walking, bool restingStop,
            long millisecondsSinceSeenWalking) =>
            autonomousParty && !anyoneFighting &&
            (walking || !restingStop && millisecondsSinceSeenWalking >= 0 && millisecondsSinceSeenWalking < SettleMilliseconds);
    }
}
