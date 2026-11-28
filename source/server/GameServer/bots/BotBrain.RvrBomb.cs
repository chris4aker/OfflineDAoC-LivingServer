using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.ServerProperties;

namespace DOL.AI.Brain
{
    /// <summary>
    /// #531 step 2f: an open-field gamebot bomber walks in on its group's mezzed pile (RvrBombPlan; owner answer 53A). With
    /// two or more bombers in the group it bombs the pile at once; alone it waits beside the pile and bombs as the first
    /// mez there is about to wear off. Autonomous gamebots in the open field only (RvrTacticsActive), under the crowd-control
    /// plan switch (rvr_bot_cc_plan, on). Companions, bots in a player's group, keeps and raids keep today's rule: never a
    /// walk in among enemy players, and never a bomb that would wake a mezzed enemy.
    /// </summary>
    public partial class BotBrain
    {
        private Spell _rvrBombSpell;
        private GameLiving _rvrBombCentre;
        private RvrBombAction _rvrBombAction;
        private long _rvrBombWalkUntil;
        private long _rvrBombWaitUntil;
        private long _nextRvrBombLook;
        /// <summary>While set, a mezzed enemy player or bot does not veto this bot's blast (the plan's bomb on the pile).</summary>
        private bool _rvrBombLiftsMez;

        /// <summary>What the plan's bomb is doing now (None: not walking in or waiting), for the rehearsal's row.</summary>
        public RvrBombAction RvrBombState => _rvrBombSpell == null ? RvrBombAction.None :
            _rvrBombWaitUntil != 0 ? RvrBombAction.Wait : _rvrBombAction;

        /// <summary>The bot has a point blank damage spell with a cast time it can cast (a bomber).</summary>
        internal bool IsRvrBomber() => Body.IsAlive && BestPointBlankSpell() != null;

        /// <summary>The mez veto is lifted for this enemy: the plan bombs the pile it sleeps in.</summary>
        private bool RvrBombLiftsMez(GameLiving living) => _rvrBombLiftsMez && RvrPvpEnemy(living);

        private void EndRvrBomb(long backoff)
        {
            if (_rvrBombSpell == null)
                return;
            _rvrBombSpell = null;
            _rvrBombCentre = null;
            _rvrBombAction = RvrBombAction.None;
            Body.StopMovingOnPath();
            Body.StopMoving();
            if (backoff > 0)
                _pointBlankBackoffUntil = Math.Max(_pointBlankBackoffUntil, GameLoop.GameLoopTime + backoff);
        }

        /// <summary>The group's bombers on the board (this bot included).</summary>
        private static int RvrBombers(RvrFightBoard board)
        {
            int count = 0;
            foreach (RvrCombatant member in board.Members)
                if (!member.IsPet && member.Living is GameBot { Brain: BotBrain brain } && brain.IsRvrBomber())
                    count++;
            return count;
        }

        /// <summary>The least mez time left among the mezzed enemies within <paramref name="radius"/> of <paramref name="at"/>.</summary>
        private long RvrShortestMezLeft(RvrFightBoard board, Vector2 at, int radius)
        {
            long shortest = long.MaxValue;
            float reach = Math.Max(1, radius - BotAoePolicy.EdgeMargin);
            foreach (RvrCombatant enemy in board.Enemies)
            {
                if (enemy?.Living is not { IsAlive: true } living || !living.IsMezzed ||
                    Vector2.Distance(at, new Vector2(living.X, living.Y)) > reach)
                    continue;
                long left = EffectListService.GetEffectOnTarget(living, eEffect.Mez) is { } mez
                    ? Math.Max(0, mez.ExpireTick - GameLoop.GameLoopTime) : 0;
                shortest = Math.Min(shortest, left);
            }
            return shortest;
        }

        /// <summary>
        /// From TryPointBlankArea, before today's walk-in: true while the plan's bomb owns the turn (walking in, waiting
        /// beside the pile, or the bomb cast). Looks for a pile at most once a second.
        /// </summary>
        private bool TryRvrBomb(long now)
        {
            if (_rvrBombSpell != null)
                return ContinueRvrBomb(now);
            if (now < _nextRvrBombLook || !Properties.RVR_BOT_CC_PLAN || !RvrTacticsActive())
                return false;
            _nextRvrBombLook = now + 1_000;
            if (Body.IsBeingInterrupted || now < _pointBlankBackoffUntil || Body.MaxSpeed <= 0 || EnemyOnCaster())
                return false;
            Spell spell = BestPointBlankSpell();
            RvrFightBoard board = spell == null ? null : RvrBoard();
            if (board == null)
                return false;
            RvrBombPlan.Pile pile = RvrBombPlan.Choose(board.Enemies, new Vector2(Body.X, Body.Y), spell.Radius);
            if (pile.Centre?.Living is not GameLiving centre)
                return false;
            Vector2 at = new(centre.X, centre.Y);
            RvrBombAction action = RvrBombPlan.Decide(RvrBombers(board), pile.Mezzed, pile.Free,
                RvrShortestMezLeft(board, at, spell.Radius));
            if (action == RvrBombAction.None)
                return false;
            _rvrBombSpell = spell;
            _rvrBombCentre = centre;
            _rvrBombAction = action;
            _rvrBombWalkUntil = now + RvrBombPlan.WalkMilliseconds;
            _rvrBombWaitUntil = 0;
            BotBody.WakeRecoveryRest();
            Body.StopAttack();
            Body.StopFollowing();
            Body.PathTo(new Vector3(centre.X, centre.Y, centre.Z), Body.MaxSpeed);
            ReportRvrBomb("RVR_BOMB_WALK", spell, pile.Mezzed + pile.Free);
            return true;
        }

        private bool ContinueRvrBomb(long now)
        {
            Spell spell = _rvrBombSpell;
            if (_rvrBombCentre is not { IsAlive: true } centre || !PointBlankReady(spell) || !RvrTacticsActive())
            {
                EndRvrBomb(5_000);
                return false;
            }
            if (Body.IsBeingInterruptedByOther || EnemyOnCaster())
            {
                EndRvrBomb(BotAoePolicy.PointBlankBackoffMilliseconds);
                return false;
            }
            RvrFightBoard board = RvrBoard();
            if (board == null)
            {
                EndRvrBomb(5_000);
                return false;
            }
            Vector2 self = new(Body.X, Body.Y);
            float reach = Math.Max(1, spell.Radius - BotAoePolicy.EdgeMargin);
            bool arrived = Vector2.Distance(self, new Vector2(centre.X, centre.Y)) <= Math.Min(reach / 2, BotAoePolicy.ApproachSlack) ||
                           !Body.IsMoving;
            if (!arrived)
            {
                if (now < _rvrBombWalkUntil)
                    return true; // still walking in: this owns the turn
                EndRvrBomb(5_000);
                return false;
            }

            (int mezzed, int free) = RvrBombPlan.Reach(board.Enemies, self, spell.Radius);
            RvrBombAction action = RvrBombPlan.Decide(RvrBombers(board), mezzed, free, RvrShortestMezLeft(board, self, spell.Radius));
            if (action == RvrBombAction.Wait)
            {
                if (_rvrBombWaitUntil == 0)
                {
                    _rvrBombWaitUntil = now + RvrBombPlan.WaitMilliseconds;
                    ReportRvrBomb("RVR_BOMB_WAIT", spell, mezzed + free);
                }
                if (now >= _rvrBombWaitUntil)
                {
                    EndRvrBomb(5_000);
                    return false;
                }
                Body.StopMoving();
                if (Body.attackComponent?.AttackState == true)
                    Body.StopAttack();
                return true; // waiting beside the pile
            }
            if (action != RvrBombAction.Bomb)
            {
                // the pile woke or went: today's rules again (a blast in place that wakes nobody)
                EndRvrBomb(0);
                return false;
            }

            _rvrBombLiftsMez = true;
            BotAoePolicy.Blast blast;
            try
            {
                blast = EvaluateBlast(Body, spell);
            }
            finally
            {
                _rvrBombLiftsMez = false;
            }
            EndRvrBomb(0);
            if (!blast.Worthwhile)
                return false;
            if (Body.TargetObject is not GameLiving { IsAlive: true })
                Body.TargetObject = centre;
            ReportRvrBomb("RVR_BOMB", spell, blast.Hits);
            return CastPointBlank(spell, blast.Hits);
        }

        /// <summary>The plan's bomb steps, as BOT_AREA_SPELL lines (not rate-limited: a few a fight).</summary>
        private void ReportRvrBomb(string kind, Spell spell, int hits)
        {
            if (!log.IsInfoEnabled)
                return;
            log.Info($"BOT_AREA_SPELL kind={kind} bot=\"{Body.Name}\" class=\"{BotBody.ClassName}\" spell=\"{spell.Name}\" " +
                     $"hits={hits} radius={spell.Radius} region={Body.CurrentRegionID} at={Body.X},{Body.Y} " +
                     $"companion={BotBody.IsTemporaryGroupHelper}");
        }
    }
}
