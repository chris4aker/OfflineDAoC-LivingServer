using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #569 step 6, main assist for autonomous gamebot PvE parties. Port by hand of zomboden/OfflineDAoC-True165
    /// <c>897ddfa</c> and <c>f745061</c> (bots/BotBrain.MainAssist.cs, GPL-3.0, shadowofze): before it every gamebot in a
    /// PvE party fought its own attacker or its own highest threat, so a party's damage spread over every monster of a
    /// pull. Now the tank fighting first publishes its target as the party's kill target, and the other members take it
    /// over their own attacker, their pet's fight and their top threat, unless they are below half health and being hit
    /// (then they fight back), and never onto a monster a groupmate holds with a mesmerize or root.
    ///
    /// Period sources (docs/rvr-audit-1.2.3/reference.md §3.4; docs/research/rvr-field-1.2/ERA-RESEARCH.md §2): groups
    /// named one main assist and "everyone then makes an /assist macro" (FH Nov 2003); crowd control works first, then
    /// everyone hits the main assist's target; the tank holds and peels. Prima's group chapter: one tank main-tanks and
    /// pulls, the other takes adds off the healers.
    ///
    /// Where: bot-only parties of autonomous gamebots doing group PvE. A group with a real player in it keeps leader
    /// focus instead (CompanionLeaderFocus: the player is the main assist), RvR warbands keep their own calls
    /// (RvrTactics), and companions, /spawn and /raid helpers and hired companions are untouched.
    ///
    /// Not taken from True165: its later "main tank only" writer and off-tank assist (d2fc3fe3d, veteran review M2) need a
    /// per-party main tank in the brain, which Living Server's party code doesn't expose; the first tank's target keeps the
    /// kill (<see cref="ReplacesKillTarget"/>), as in 897ddfa. The companion half (M7) is #571's ground.
    /// </summary>
    public static class BotPartyMainAssist
    {
        public const string PropertyKey = "bot_party_main_assist";

        [ServerProperty("bots", "bot_party_main_assist",
            "Gamebot parties fight one kill target (#569): in a party of autonomous gamebots doing group PvE, the tank's target is the party's kill target and the other members hit it before whoever is hitting them or their own top threat, unless they are below half health and being hit. A monster a groupmate has mesmerized or rooted is never the kill target. Groups with a real player in them keep leader focus (bot_leader_focus), and RvR warbands and companions are unchanged. Log: AUTONOMOUS_PARTY_KILL_TARGET.", true)]
        public static bool On = true;

        /// <summary>Below this health a member being hit fights its own attacker instead of the kill target.</summary>
        public const int LowHealthPercent = 50;

        /// <summary>A kill target nobody has confirmed for this long is stale: members drop it, any tank may replace it.</summary>
        public const long KillTargetStaleMilliseconds = 10_000;

        /// <summary>
        /// Main assist runs for this bot: the switch is on, it is an autonomous gamebot (not a companion, helper or hired
        /// companion) in a group with no real player in it, and its objective is group PvE.
        /// </summary>
        public static bool Applies(bool on, bool autonomousGamebot, bool companionOrHelper, bool grouped,
            bool realPlayerInGroup, bool groupPve) =>
            on && autonomousGamebot && !companionOrHelper && grouped && !realPlayerInGroup && groupPve;

        /// <summary>
        /// A member follows the kill target unless it is gone or out of reach (<paramref name="killTargetValid"/>), held
        /// by a mesmerize or root, or the member is low and being hit.
        /// </summary>
        public static bool AssistsKillTarget(bool killTargetValid, bool killTargetControlled, int healthPercent, bool beingHit) =>
            killTargetValid && !killTargetControlled && !(healthPercent < LowHealthPercent && beingHit);

        /// <summary>
        /// The stored kill target is replaced only when it is gone, when its own writer offers a new one, when that writer
        /// has left it, or when nobody has confirmed it for a while. A second tank on an add does not take over.
        /// </summary>
        public static bool ReplacesKillTarget(bool storedAlive, bool sameWriter, bool writerStillOnIt, long ageMilliseconds) =>
            !storedAlive || sameWriter || !writerStillOnIt || ageMilliseconds > KillTargetStaleMilliseconds;

        /// <summary>
        /// A peel (a tank going for a monster on a groupmate) may start a kill target when there is none, but never moves
        /// a live one: the others stay on the main monster while the tank takes the add off the healer (True165 M2).
        /// </summary>
        public static bool MayWriteKillTarget(bool peel, bool liveStoredTarget) => !(peel && liveStoredTarget);

        /// <summary>The kill target is fresh: confirmed by its writer within <see cref="KillTargetStaleMilliseconds"/>.</summary>
        public static bool IsFresh(long now, long tick) => now - tick <= KillTargetStaleMilliseconds;
    }
}
