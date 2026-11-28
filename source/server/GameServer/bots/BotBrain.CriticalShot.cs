using DOL.GS;
using DOL.GS.ServerProperties;

namespace DOL.AI.Brain
{
    /// <summary>
    /// #569 step 8: an archer bot opens with Critical Shot (NpcAttackAction sets it on the draw at
    /// an idle target, BotRangedCombat.OpensWithCriticalShot); a Hunter's pet waits at heel for that opener instead of
    /// putting the monster in combat first. Bots only: the player's own pet never comes here (SynchronizeIndependentPet
    /// runs for a GameBot owner only).
    /// </summary>
    public partial class BotBrain
    {
        private GameLiving _criticalShotHoldTarget;
        private long _criticalShotHoldSince;

        /// <summary>Whether the pet waits for this bot's Critical Shot opener on <paramref name="target"/>.</summary>
        public bool HoldsPetForCriticalShot(GameLiving target, IControlledBrain petBrain)
        {
            GameBot bot = BotBody;
            if (!Properties.BOT_CRITICAL_SHOT_OPENER || bot == null || target == null || !target.IsAlive)
                return false;
            long now = GameLoop.GameLoopTime;
            if (_criticalShotHoldTarget != target)
            {
                _criticalShotHoldTarget = target;
                _criticalShotHoldSince = now;
            }
            // cheapest facts first: most calls end at "no bow in hand" or "already fighting"
            bool bowReady = bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance &&
                bot.Endurance >= RangeAttackComponent.CRITICAL_SHOT_ENDURANCE_COST + RangeAttackComponent.DEFAULT_ENDURANCE_COST;
            bool targetIdle = !target.InCombat && target.TargetObject != bot;
            if (!bowReady || !targetIdle || !CompanionControls165.CriticalShotAllowed(bot))
                return false;
            bool petOnIt = petBrain is ControlledMobBrain controlled && controlled.OrderedAttackTarget == target ||
                           petBrain?.Body?.TargetObject == target && petBrain.Body.IsAttacking;
            return BotRangedCombat.HoldPetForCriticalShot(BotRangedCombat.CriticalShotLevel(bot) > 0,
                bowReady, targetIdle, petOnIt, now - _criticalShotHoldSince);
        }
    }
}
