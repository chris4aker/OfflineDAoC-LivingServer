using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>#568 C5: the brain side of <see cref="BotHoldDamage"/> (casters wait for the tank or the pet).</summary>
    public partial class BotBrain
    {
        private GameLiving _holdDamageTarget;
        private long _holdDamageSince;

        private bool HoldDamageForTank()
        {
            if (BotBody?.CharacterClass == null || Body.TargetObject is not GameNPC target || target is GameBot || !target.IsAlive)
                return false;
            bool ranged = PrefersCurrentSpellRange();
            bool monster = !BotPvpCrowdControl.PlayerLike(target);
            if (!ranged || !monster || target.TargetObject == Body)
                return false;
            GameNPC pet = Body.ControlledBrain?.Body;
            bool petAlive = pet?.IsAlive == true;
            bool tankInGroup = GroupHasLivingTank();
            if (!tankInGroup && !petAlive)
                return false;
            long now = GameLoop.GameLoopTime;
            if (_holdDamageTarget != target)
            {
                _holdDamageTarget = target;
                _holdDamageSince = now;
            }
            BotHoldDamage.Holder holder = target.TargetObject switch
            {
                GameLiving living when petAlive && living == pet => BotHoldDamage.Holder.OwnPet,
                GameLiving living when living != Body && living.IsAlive && Body.Group?.IsInTheGroup(living) == true &&
                                       (living is GamePlayer || living is GameBot tankBot && BotPartyRoles.IsTank(tankBot)) =>
                    BotHoldDamage.Holder.GroupTank,
                GameLiving => BotHoldDamage.Holder.Other,
                _ => BotHoldDamage.Holder.Nobody,
            };
            return BotHoldDamage.Holds(true, true, false, tankInGroup, petAlive, now - _holdDamageSince, holder,
                target.HealthPercent, (eCharacterClass)BotBody.CharacterClass.ID);
        }
    }
}
