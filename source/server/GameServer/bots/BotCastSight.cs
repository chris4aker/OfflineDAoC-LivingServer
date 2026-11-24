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
            Zone zone = caster.CurrentZone;
            if (zone == null || target.CurrentRegionID != caster.CurrentRegionID || !PathfindingProvider.Instance.IsAvailable ||
                !PathfindingProvider.Instance.HasNavmesh(zone))
                return false;
            return !PathfindingProvider.Instance.HasLineOfSight(zone, new(caster.X, caster.Y, caster.Z),
                new(target.X, target.Y, target.Z), PathfindingProvider.Instance.DefaultFilters);
        }
    }
}
