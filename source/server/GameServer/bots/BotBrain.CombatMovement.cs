// Living Server 1.2 (planned as 1.2.1): movement that doesn't look scripted, first slice. Our own work: the fork survey of
// 2026-11-08 found no fork with bot strafing. Works beside the flank step (BotBrain.Flank, hand port from True165): the
// flank picks the spot, circling keeps the bot from standing there like a post. The rules are BotCombatMovement's.
using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS;
using DOL.GS.Styles;

namespace DOL.AI.Brain
{
    /// <summary>
    /// A melee gamebot circles a little around a monster now and then, and a caster steps aside right after a cast. Both
    /// walk short legs on the navigation mesh that keep them facing their target (see <see cref="BotCombatMovement"/>).
    /// Never: companions and player-led groups (kept as before), raid parties, raid dungeons and epic encounters (their
    /// tactics place the raiders), fights against players or bots, a tank holding aggro, a bot casting.
    /// </summary>
    public partial class BotBrain
    {
        private GameLiving _stepTarget;
        private Vector3[] _stepLegs;
        private int _stepLeg;
        private long _stepStarted;
        private long _lastStepEnded;

        private GameLiving _circleTarget;
        private long _circleSettledSince;
        private long _nextCircleTick;
        private long _nextCrowdCheckTick;

        private bool _stepWasCasting;
        private long _castEndedAt;
        private long _nextCasterStepTick;

        /// <summary>The start of each attack turn: notes when a cast just ended (for a caster's step).</summary>
        private void NoteCastForStep()
        {
            bool casting = Body.IsCasting;
            if (_stepWasCasting && !casting)
                _castEndedAt = GameLoop.GameLoopTime;
            _stepWasCasting = casting;
        }

        /// <summary>True while a step this brain started is still walking: it owns the turn (and keeps the bot's style
        /// queued). False once it ended, was interrupted or is no longer allowed; the attack below then re-follows as usual.</summary>
        private bool ContinueCombatStep(GameLiving target)
        {
            if (_stepLegs == null)
                return false;
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot == null || target == null || target != _stepTarget || !target.IsAlive || bot.IsCasting ||
                bot.IsCrowdControlled || _flankTarget != null || now - _stepStarted > BotCombatMovement.StepTimeoutMs ||
                AutonomousRealmRaid.IsRaidDungeon(bot.CurrentRegionID))
                return EndCombatStep(stop: bot != null && bot.IsMoving && now - _stepStarted > BotCombatMovement.StepTimeoutMs);
            if (bot.IsMoving)
            {
                QueueStepStyle();
                return true;
            }
            if (++_stepLeg >= _stepLegs.Length)
                return EndCombatStep(stop: false);
            Vector3 from = new(bot.X, bot.Y, bot.Z);
            // Something else moved the bot meanwhile (a flank, a knockback): the rest of the arc is not from here.
            if (Vector2.Distance(new(from.X, from.Y), new(_stepLegs[_stepLeg].X, _stepLegs[_stepLeg].Y)) >
                BotCombatMovement.LegLength + BotCombatMovement.LegTolerance)
                return EndCombatStep(stop: false);
            if (ReachableLeg(bot, from, _stepLegs[_stepLeg]) is not Vector3 leg)
            {
                BotCombatMovement.Count(BotCombatMovement.Counter.Blocked);
                return EndCombatStep(stop: false);
            }
            WalkLeg(bot, target, leg);
            QueueStepStyle();
            return true;
        }

        private bool EndCombatStep(bool stop)
        {
            if (stop)
                Body.StopMoving();
            _stepLegs = null;
            _stepTarget = null;
            _lastStepEnded = GameLoop.GameLoopTime;
            return false;
        }

        /// <summary>A melee bot keeps its style queued while it steps (the swing goes on: an NPC swings while it walks).</summary>
        private void QueueStepStyle()
        {
            if (Body.IsAttacking && Body.ActiveWeaponSlot != eActiveWeaponSlot.Distance &&
                Body.attackComponent?.attackAction != null &&
                Body.styleComponent != null)
                QueueUsableMeleeStyle();
        }

        /// <summary>Who may move in a fight against <paramref name="target"/> at all (BotCombatMovement.Allowed).</summary>
        private bool CombatMovementAllowed(GameBot bot, GameLiving target) =>
            BotCombatMovement.Allowed(bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !bot.IsEpicSpecialForce,
                bot.IsPlayerLedGroup, target is GameNPC npc && InRaidOrEpicFight(bot, npc),
                target is GameNPC and not GameBot);

        /// <summary>
        /// A melee bot hitting its target circles a little now and then (BotCombatMovement.Kind / CircleDue / ChooseDirection).
        /// Cheap checks every turn; the mesh walks only when a step is due.
        /// </summary>
        private bool TryCircle(GameLiving target)
        {
            GameBot bot = BotBody;
            if (ContinueCombatStep(target))
                return true;
            long now = GameLoop.GameLoopTime;
            // Settled: hitting this target, standing at its stick distance, neither of them walking.
            if (bot == null || target is not GameNPC monster || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper ||
                bot.IsPlayerLedGroup || !bot.IsAttacking || bot.TargetObject != target || bot.IsMoving || monster.IsMoving ||
                bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true || bot.IsCrowdControlled ||
                bot.MaxSpeed <= 0 || bot.IsStealthed || bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance ||
                _flankTarget != null || bot.IsTurningDisabled ||
                !bot.IsWithinRadius(target, bot.movementComponent.MinFollowDistance + BotCombatMovement.SettledSlack))
            {
                if (_circleTarget != target || bot?.IsAttacking != true)
                {
                    _circleTarget = null;
                    _circleSettledSince = 0;
                }
                return false;
            }
            if (_circleTarget != target)
            {
                _circleTarget = target;
                _circleSettledSince = now;
                _nextCircleTick = 0;
            }
            if (now - _circleSettledSince < BotCombatMovement.SettleMs)
                return false;

            bool holdsAggro = monster.TargetObject == bot;
            bool due = now >= _nextCircleTick && _nextCircleTick > 0;
            bool crowdCheck = now >= _nextCrowdCheckTick;
            if (_nextCircleTick == 0)
            {
                // The first step's timer starts once settled; the kind is checked again when it is due.
                BotCombatMovement.CircleKind firstKind = BotCombatMovement.Kind(true, true, false, false, holdsAggro,
                    BotPartyRoles.IsTank(bot));
                _nextCircleTick = now + BotCombatMovement.NextCircleDelay(
                    firstKind == BotCombatMovement.CircleKind.None ? BotCombatMovement.CircleKind.Normal : firstKind,
                    Random.Shared.NextDouble()) / 2;
            }
            if (!due && !crowdCheck)
                return false;

            using var aiProfile = AiProfile.Scope(AiSystem.CombatMovement); // AI_PROFILE: measures only
            if (crowdCheck)
                _nextCrowdCheckTick = now + BotCombatMovement.CrowdedRetryMs;
            Vector2 center = new(monster.X, monster.Y), self = new(bot.X, bot.Y);
            int crowdSide = crowdCheck ? BotCombatMovement.CrowdSide(center, self, FightersNear(bot, monster)) : 0;
            if (!BotCombatMovement.CircleDue(now, _circleSettledSince, _nextCircleTick, crowdSide != 0, _lastStepEnded))
                return false;

            BotCombatMovement.CircleKind kind = BotCombatMovement.Kind(CombatMovementAllowed(bot, target), true,
                PrefersCurrentSpellRange(), bot.IsStealthed, holdsAggro, BotPartyRoles.IsTank(bot));
            _nextCircleTick = now + BotCombatMovement.NextCircleDelay(kind, Random.Shared.NextDouble());
            if (kind == BotCombatMovement.CircleKind.None || bot.IsUnderwater)
                return false;

            float radius = Vector2.Distance(center, self);
            float sweep = BotCombatMovement.SweepDegrees(kind, radius, Random.Shared.NextDouble());
            Style flankStyle = holdsAggro ? null : FlankStyle();
            Style.eOpeningPosition? keep = flankStyle == null ? null : (Style.eOpeningPosition)flankStyle.OpeningRequirementValue;
            float angle = monster.GetAngle(bot);
            int direction = BotCombatMovement.ChooseDirection(angle, sweep, crowdSide, holdsAggro, keep, Random.Shared.NextDouble());
            Vector2[] arc = BotCombatMovement.ArcLegs(center, radius, monster.GetHeading(bot), direction, sweep);
            if (!StartCombatStep(bot, target, arc))
                return false;
            BotCombatMovement.Count(crowdSide != 0 ? BotCombatMovement.Counter.Crowded
                : kind == BotCombatMovement.CircleKind.Small ? BotCombatMovement.Counter.HeldStep : BotCombatMovement.Counter.Circle);
            return true;
        }

        /// <summary>
        /// A caster takes a short side step right after a cast ended, now and then (BotCombatMovement.CasterStepDue), and only
        /// where it still sees its target from the new spot.
        /// </summary>
        private bool TryCasterStep(GameLiving target)
        {
            GameBot bot = BotBody;
            if (ContinueCombatStep(target))
                return true;
            long now = GameLoop.GameLoopTime;
            if (bot == null || target is not GameNPC monster ||
                !BotCombatMovement.CasterStepDue(now, _castEndedAt, _nextCasterStepTick) || bot.IsMoving ||
                bot.IsCrowdControlled || bot.MaxSpeed <= 0 || bot.IsStealthed || bot.IsTurningDisabled || bot.IsUnderwater)
                return false;
            using var aiProfile = AiProfile.Scope(AiSystem.CombatMovement); // AI_PROFILE: measures only
            bool pressure = RecentDirectAttacker() is GameLiving attacker && bot.IsWithinRadius(attacker, bot.MeleeAttackRange + 50);
            bool allowed = BotCombatMovement.CasterStepAllowed(CombatMovementAllowed(bot, target), PrefersCurrentSpellRange(),
                bot.IsCasting, bot.castingComponent?.HasPendingSkillRequests == true, monster.TargetObject == bot, pressure);
            _nextCasterStepTick = now + BotCombatMovement.NextCasterStepDelay(Random.Shared.NextDouble());
            _castEndedAt = 0;
            if (!allowed)
                return false;
            Vector2 spot = BotCombatMovement.CasterStepSpot(new(bot.X, bot.Y), new(monster.X, monster.Y), Random.Shared.NextDouble());
            IPathfindingMgr nav = PathfindingProvider.Instance;
            // It stepped to cast again: a spot it cannot see its target from is not taken.
            if (nav == null || !nav.IsAvailable || bot.CurrentZone == null ||
                !nav.HasLineOfSight(bot.CurrentZone, new(spot.X, spot.Y, bot.Z), new(monster.X, monster.Y, monster.Z), nav.DefaultFilters))
                return false;
            if (!StartCombatStep(bot, target, [spot]))
                return false;
            BotCombatMovement.Count(BotCombatMovement.Counter.CasterStep);
            return true;
        }

        /// <summary>Walks the first leg of a step whose legs all proved walkable from the one before; false (and no move) when
        /// even the first leg is not.</summary>
        private bool StartCombatStep(GameBot bot, GameLiving target, Vector2[] ends)
        {
            if (ends.Length == 0)
                return false;
            var legs = new List<Vector3>(ends.Length);
            Vector3 from = new(bot.X, bot.Y, bot.Z);
            foreach (Vector2 end in ends)
            {
                if (ReachableLeg(bot, from, new(end.X, end.Y, from.Z)) is not Vector3 leg)
                    break;
                legs.Add(leg);
                from = leg;
            }
            if (legs.Count == 0)
            {
                BotCombatMovement.Count(BotCombatMovement.Counter.Blocked);
                return false;
            }
            _stepLegs = legs.ToArray();
            _stepLeg = 0;
            _stepTarget = target;
            _stepStarted = GameLoop.GameLoopTime;
            // The melee follow would stop the walk on its next check (the bot is already at its stick distance); the next
            // attack turn after the step re-follows (NpcStartAttack).
            bot.StopFollowing();
            WalkLeg(bot, target, _stepLegs[0]);
            return true;
        }

        /// <summary>
        /// One leg: face the target first (so the turn happens in small pieces along the arc), then walk. At most
        /// BotCombatMovement.LegLength, so the mover does not turn the bot to walk sideways. The brain thinks again as the leg
        /// ends, so the next leg (or the next cast) follows without a pause.
        /// </summary>
        private void WalkLeg(GameBot bot, GameLiving target, Vector3 leg)
        {
            bot.TurnTo(target);
            short speed = (short)Math.Clamp(bot.MaxSpeed * 55 / 100, 70, 130);
            bot.PathTo(leg, speed);
            BotCombatMovement.Count(BotCombatMovement.Counter.Leg);
            float length = Vector2.Distance(new(bot.X, bot.Y), new(leg.X, leg.Y));
            int arrival = (int)(length * 1000 / speed) + 40;
            AutonomousLivingServer.Later(bot, arrival, () => bot.Brain == this && _stepLegs != null && bot.IsAlive,
                () => NextThinkTick = GameLoop.GameLoopTime);
        }

        /// <summary>The leg's end as the mesh walks it: same zone, dry ground, the bot's own floor; null when not.</summary>
        private static Vector3? ReachableLeg(GameBot bot, Vector3 from, Vector3 to)
        {
            Zone zone = bot.CurrentZone;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            if (zone == null || nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone) ||
                bot.CurrentRegion?.GetZone((int)to.X, (int)to.Y) != zone || zone.IsUnderwater((int)to.X, (int)to.Y, (int)to.Z))
                return null;
            Vector3? walked = AutonomousNavigationSurface.MoveAlongDryGround(nav, zone, from, to);
            if (!BotCombatMovement.LegReached(to, walked, from.Z) || zone.IsUnderwater((int)walked.Value.X, (int)walked.Value.Y,
                    (int)walked.Value.Z))
                return null;
            return walked;
        }

        /// <summary>Where the other fighters near the bot stand (players, bots, pets): only someone the bot could stand in.</summary>
        private static List<Vector2> FightersNear(GameBot bot, GameNPC monster)
        {
            var near = new List<Vector2>();
            ushort radius = (ushort)(BotCombatMovement.CrowdRadius + 4);
            foreach (GameNPC npc in bot.GetNPCsInRadius(radius))
                if (npc != bot && npc != monster && npc.IsAlive && npc.Realm == bot.Realm)
                    near.Add(new(npc.X, npc.Y));
            foreach (GamePlayer player in bot.GetPlayersInRadius(radius))
                if (player.IsAlive && player.Realm == bot.Realm)
                    near.Add(new(player.X, player.Y));
            return near;
        }
    }
}
