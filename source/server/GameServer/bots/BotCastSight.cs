using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>
    /// #521 (mechanics.md finding 8): a spell's line of sight is checked by the real player's client, so a gamebot's cast at
    /// another bot (or its pet) was never checked and went through walls and ridges. For an autonomous gamebot caster in PvP
    /// (<see cref="PvpRules.TakesPlayerRules(GameLiving, GameLiving)"/>) on a target that is not the real player, the navmesh
    /// answers instead, at the start and at the end of the cast (SpellHandler). One ray each; no navmesh: no check, as before.
    /// A bot's cast at a monster, guard or boss is not checked here (PvE unchanged).
    /// </summary>
    public static class BotCastSight
    {
        public static bool Blocked(GameLiving caster, GameLiving target, Spell spell)
        {
            if (caster is not GameBot || target == null || target == caster || spell == null)
                return false;
            if (!PvpRules.BotCastNeedsNavmeshSight(PvpRules.TakesPlayerRules(caster, target), target is GamePlayer, spell.RequiresLosCheck()))
                return false;
            return NavmeshSight.Blocked(caster, target, anyHeight: true);
        }

        /// <summary>
        /// 1.2.3 (DOL sweep X1): an NPC's cast that no client checks (no player near enough to be asked) at the start of the
        /// cast: a monster's, a guard's, a pet's, or a gamebot's at a monster. The navmesh answers on one floor
        /// (<see cref="NavmeshSight"/>); a gamebot's cast at another bot is left to <see cref="Blocked"/>, which the spell
        /// handler asks at the start and the end of the cast.
        /// </summary>
        public static bool BlockedWithoutChecker(GameLiving caster, GameLiving target, Spell spell)
        {
            if (caster is not GameNPC || target == null || target == caster || spell == null || !spell.RequiresLosCheck())
                return false;
            if (caster is GameBot && PvpRules.BotCastNeedsNavmeshSight(PvpRules.TakesPlayerRules(caster, target), target is GamePlayer, true))
                return false;
            return NavmeshSight.Blocked(caster, target);
        }

        /// <summary>
        /// The same for an NPC's arrow that no client checks, for every shooter (gamebots included) and on one floor only: a
        /// wall archer, a gamebot defender on a keep wall among them, keeps its shots at the ground below.
        /// </summary>
        public static bool ShotBlockedWithoutChecker(GameNPC shooter, GameObject target)
        {
            if (shooter == null || target is not GameLiving living || living == shooter)
                return false;
            return NavmeshSight.Blocked(shooter, living);
        }
    }
}
