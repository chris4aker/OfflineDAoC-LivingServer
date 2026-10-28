using System;
using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        // Ported from stefanrows/OfflineDAoC 0.271 (BotBrain.IncomingFire): the reaction hold of BotIncomingFireReaction.

        /// <summary>A held turn thinks again no sooner than this, so the end of a hold is not missed by much.</summary>
        private const int IncomingFireMinThinkMilliseconds = 100;

        // The hit pulling the bot into combat right now (set only around the state change), and the hold it drew.
        // Every hold is time-bound, so a race between service threads can at worst draw another kind or none.
        private BotIncomingFireKind _pendingIncomingFire;
        private GameLiving _pendingIncomingFireAttacker;
        private BotIncomingFireKind _incomingFireKind;
        private long _incomingFireHoldUntil;

        /// <summary>Enters the combat state because of this hit; only this entry sees the hit.</summary>
        private void EnterAggroFromIncomingFire(BotIncomingFireKind kind, GameLiving attacker)
        {
            _pendingIncomingFire = kind;
            _pendingIncomingFireAttacker = attacker;
            try
            {
                if (FSM.GetCurrentState() != FSM.GetState(eFSMStateType.AGGRO))
                    FSM.SetCurrentState(eFSMStateType.AGGRO);
            }
            finally
            {
                _pendingIncomingFire = BotIncomingFireKind.None;
                _pendingIncomingFireAttacker = null;
            }
        }

        private void BeginIncomingFireHold()
        {
            int delay = BotIncomingFireReaction.Draw(BotBody, _pendingIncomingFire, _pendingIncomingFireAttacker);
            _incomingFireKind = delay > 0 ? _pendingIncomingFire : BotIncomingFireKind.None;
            _incomingFireHoldUntil = delay > 0 ? GameLoop.GameLoopTime + delay : 0;
            if (delay > 0 && log.IsDebugEnabled)
                log.Debug($"BOT_INCOMING_FIRE bot={Body?.Name} kind={_incomingFireKind} hold={delay}ms attacker={_pendingIncomingFireAttacker?.Name}");
        }

        private void EndIncomingFireHold()
        {
            _incomingFireKind = BotIncomingFireKind.None;
            _incomingFireHoldUntil = 0;
        }

        /// <summary>
        /// True while the bot is still reacting to the hit that drew it into combat. An attacker in melee reach ends a
        /// hold drawn by a far hit at once. A held turn shortens its own think interval to the time left.
        /// </summary>
        private bool HoldsIncomingFire()
        {
            if (_incomingFireHoldUntil <= 0)
                return false;
            long now = GameLoop.GameLoopTime;
            bool pressed = _incomingFireKind != BotIncomingFireKind.Melee &&
                RecentDirectAttacker() is { } attacker && BotIncomingFireReaction.InMeleeReach(Body, attacker);
            if (!BotIncomingFireReaction.Holds(now, _incomingFireHoldUntil, pressed))
            {
                EndIncomingFireHold();
                return false;
            }
            ThinkInterval = (int)Math.Clamp(_incomingFireHoldUntil - now, IncomingFireMinThinkMilliseconds,
                Math.Max(IncomingFireMinThinkMilliseconds, ThinkInterval));
            return true;
        }

        /// <summary>
        /// The direct attacker as the target choice sees it: unchanged outside the rules; for an autonomous gamebot only
        /// while it has no other live player-shaped target on its list, or the attacker is in melee reach. A mob as the
        /// current target never holds the bot: players dropped the mob for a ganker at once.
        /// </summary>
        private GameLiving KillOrderAttacker(GameLiving directAttacker)
        {
            if (directAttacker == null || !BotIncomingFireReaction.Applies(BotBody))
                return directAttacker;
            bool hasOtherPlayer = Body.TargetObject is GameLiving { IsAlive: true } current && current != directAttacker &&
                BotIncomingFireReaction.Fighter(current) is { } fighter &&
                fighter != BotIncomingFireReaction.Fighter(directAttacker) && AggroList.ContainsKey(current);
            return BotIncomingFireReaction.OverridesKillOrder(hasOtherPlayer, BotIncomingFireReaction.InMeleeReach(Body, directAttacker))
                ? directAttacker
                : null;
        }
    }
}
