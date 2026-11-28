using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #569 step 6, main assist for autonomous gamebot PvE parties. Before it every gamebot in a PvE party fought its own
    /// attacker or its own highest threat, so a party's damage spread over every monster of a pull. Now the tank fighting
    /// first publishes its target as the party's kill target, and the other members take it over their own attacker, their
    /// pet's fight and their top threat, unless they are below half health and being hit
    /// (then they fight back), and never onto a monster a groupmate holds with a mesmerize or root.
    ///
    /// Period sources (docs/rvr-audit-1.2.3/reference.md §3.4; docs/research/rvr-field-1.2/ERA-RESEARCH.md §2): groups
    /// named one main assist and "everyone then makes an /assist macro" (FH Nov 2003); crowd control works first, then
    /// everyone hits the main assist's target; the tank holds and peels. Prima's group chapter: one tank main-tanks and
    /// pulls, the other takes adds off the healers.
    ///
    /// Where: bot-only parties of autonomous gamebots doing group PvE. A group with a real player in it keeps leader
    /// focus instead (CompanionLeaderFocus: the player is the main assist), RvR warbands keep their own calls
    /// (RvrTactics), and companions, /spawn and /raid helpers and hired companions are untouched. It steps aside
    /// (<see cref="StepsAside"/>) inside a realm raid (owner, 2026-11-27, #783: "inside realm raids, the raid's own calls
    /// win"), where the open-field RvR tactics apply, and while an enemy player or gamebot is in view; a member hit by a
    /// player-like enemy fights it (BotIncomingFireReaction); and enemy-realm pets and siege engines are never the kill
    /// target (<see cref="EligibleKillTarget"/>).
    ///
    /// Not built yet: a single main tank as the only writer, with an off-tank assist. BotGroupRoles ranks a main tank,
    /// but it isn't wired into the brain yet, so the first tank's target keeps the kill (<see cref="ReplacesKillTarget"/>).
    /// The companion half is #571's ground.
    /// </summary>
    public static class BotPartyMainAssist
    {
        public const string PropertyKey = "bot_party_main_assist";

        /// <summary>The switch (ServerProperties: bot_party_main_assist, on).</summary>
        public static bool On => Properties.BOT_PARTY_MAIN_ASSIST;

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

        /// <summary>Within this range an enemy player or gamebot in view makes the party step aside.</summary>
        public const ushort EnemyWatchRadius = 1500;

        /// <summary>
        /// Main assist steps aside for the calls that outrank it: a realm raid's own calls (owner, 2026-11-27, #783), the
        /// open-field RvR tactics, and any fight with an enemy player or gamebot in view (#787 review: a PvE party in the
        /// frontier must not go back to the tank's monster on the next think).
        /// </summary>
        public static bool StepsAside(bool realmRaidGroup, bool rvrTacticsActive, bool enemyInView) =>
            realmRaidGroup || rvrTacticsActive || enemyInView;

        /// <summary>
        /// A member follows the kill target unless it is gone or out of reach (<paramref name="killTargetValid"/>), held
        /// by a mesmerize or root, the member is low and being hit, or the one hitting it is an enemy player, gamebot or
        /// their pet (then it fights back at once, as BotIncomingFireReaction drops a monster for a ganker).
        /// </summary>
        public static bool AssistsKillTarget(bool killTargetValid, bool killTargetControlled, int healthPercent, bool beingHit,
            bool attackerPlayerLike) =>
            killTargetValid && !killTargetControlled && !attackerPlayerLike && !(healthPercent < LowHealthPercent && beingHit);

        /// <summary>
        /// A monster may be the kill target; a gamebot, an enemy-realm pet or a siege engine never is (#787 review).
        /// </summary>
        public static bool EligibleKillTarget(bool isBot, bool enemyRealmPet, bool siegeEngine) =>
            !isBot && !enemyRealmPet && !siegeEngine;

        /// <summary>
        /// The stored kill target is replaced only when it is gone, when its own writer offers a new one, when that writer
        /// has left it, or when nobody has confirmed it for a while. A second tank on an add does not take over.
        /// </summary>
        public static bool ReplacesKillTarget(bool storedAlive, bool sameWriter, bool writerStillOnIt, long ageMilliseconds) =>
            !storedAlive || sameWriter || !writerStillOnIt || ageMilliseconds > KillTargetStaleMilliseconds;

        /// <summary>
        /// A peel (a tank going for a monster on a groupmate) may start a kill target when there is none, but never moves
        /// a live one: the others stay on the main monster while the tank takes the add off the healer.
        /// </summary>
        public static bool MayWriteKillTarget(bool peel, bool liveStoredTarget) => !(peel && liveStoredTarget);

        /// <summary>The kill target is fresh: confirmed by its writer within <see cref="KillTargetStaleMilliseconds"/>.</summary>
        public static bool IsFresh(long now, long tick) => now - tick <= KillTargetStaleMilliseconds;
    }
}
