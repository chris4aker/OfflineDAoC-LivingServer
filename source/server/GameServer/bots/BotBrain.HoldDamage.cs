using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>#568 C5: the brain side of <see cref="BotHoldDamage"/> (casters wait for the tank or the pet).</summary>
    public partial class BotBrain
    {
        private GameLiving _holdDamageTarget;
        private long _holdDamageSince;

        // made once per brain, not on every cast decision (#727 review)
        private System.Predicate<Spell> _holdStripsOnMonster;
        private System.Predicate<Spell> _holdStripsOnPlayerLike;

        /// <summary>The C5 strip predicate for this target kind, made once.</summary>
        private System.Predicate<Spell> HoldStrips(bool playerLikeTarget) => playerLikeTarget
            ? _holdStripsOnPlayerLike ??= spell => BotControlSpells.HoldDamageStrips(spell, true)
            : _holdStripsOnMonster ??= spell => BotControlSpells.HoldDamageStrips(spell, false);

        /// <summary>
        /// The C5 hold and the C6 debuff opening apply here: not in a realm raid or warband (shared support) and not in a
        /// fight against a tactic boss - the bot's, the assisted player's or the party leader's target (as #703's gate).
        /// </summary>
        private bool CasterVeteranRulesApply()
        {
            bool sharedSupport = AutonomousRealmRaid.HasSharedSupport(BotBody);
            bool bossFight = BotHealTriage165.IsBossTarget(Body.TargetObject) ||
                BotHealTriage165.IsBossTarget(AssistedPlayer?.TargetObject) ||
                BotHealTriage165.IsBossTarget(Body.Group?.LivingLeader?.TargetObject);
            return BotHoldDamage.AppliesHere(sharedSupport, bossFight);
        }

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
