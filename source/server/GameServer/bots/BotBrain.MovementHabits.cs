// Bots as people for 1.2, work package 5 (docs/BOTS-AS-PEOPLE-1.2.md, item 5): circle-strafing and facing the enemy in an
// open-field RvR fight. The rules are BotMovementHabits' (pure, tested); this partial proves the legs on the mesh and walks
// them, on top of the RvR field's positioning (BotBrain.RvrPositioning calls it). Tests only until seen in the owner's PC log.
using System;
using System.Numerics;
using DOL.GS;
using DOL.GS.Styles;

namespace DOL.AI.Brain
{
    /// <summary>
    /// A melee gamebot in an open-field fight against a player or a bot circle-strafes its target every 3-8 s, out of the
    /// target's front and inside its own positional window (Casual 20% of fights, Average 50%, Veteran 85%). A caster or
    /// healer with an enemy melee fighter on it runs its kite facing it, along an arc of short legs (a caster backs off 80
    /// units), instead of turning its back (30/60/90% of
    /// contacts). The tier is the group's (owner decision 5). Legs of at most 22 units proven on dry ground of the bot's
    /// floor, from the server-wide leg budget; on a leg this short the bot keeps facing its target. Only where the RvR
    /// tactics apply (RvrTacticsActive) and BotPeopleScope allows movement habits (bot_move_strafe).
    /// </summary>
    public partial class BotBrain
    {
        private Vector3[] _habitLegs;
        private int _habitLeg;
        private GameLiving _habitFace;
        private long _habitStarted;
        private bool _habitStrafe;
        private short _habitSpeed;
        private long _habitUntil;

        private GameLiving _strafeTarget;
        private bool _strafeFight;
        private long _nextStrafeAt;
        private bool _strafeFightCounted;

        private long _faceReadyAt;
        private long _faceContacts;

        /// <summary>A habit walk this brain started is still walking: it owns the turn. False once it ended.</summary>
        private bool ContinueHabitLegs(GameBot bot, long now)
        {
            if (_habitLegs == null)
                return false;
            if (!bot.IsAlive || bot.IsCasting || bot.IsCrowdControlled || _habitFace?.IsAlive != true ||
                _habitFace.CurrentRegion != bot.CurrentRegion || now > _habitUntil)
                return EndHabitLegs(bot, stop: bot.IsMoving && now > _habitUntil);
            if (bot.IsMoving)
            {
                if (_habitStrafe)
                    QueueStepStyle();
                return true;
            }
            if (++_habitLeg >= _habitLegs.Length)
                return EndHabitLegs(bot, stop: false);
            Vector3 next = _habitLegs[_habitLeg];
            // Something else moved the bot meanwhile (a knockback, a peel): the rest of the walk is not from here.
            if (Vector2.Distance(new(bot.X, bot.Y), new(next.X, next.Y)) > BotCombatMovement.LegLength + BotCombatMovement.LegTolerance)
                return EndHabitLegs(bot, stop: false);
            WalkHabitLeg(bot, next);
            return true;
        }

        private bool EndHabitLegs(GameBot bot, bool stop)
        {
            if (stop)
                bot.StopMoving();
            if (_habitFace?.IsAlive == true && _habitFace.CurrentRegion == bot.CurrentRegion)
                bot.TurnTo(_habitFace);
            _habitLegs = null;
            _habitFace = null;
            return false;
        }

        private void StartHabitLegs(GameBot bot, GameLiving face, Vector3[] legs, bool strafe, short speed)
        {
            _habitLegs = legs;
            _habitLeg = 0;
            _habitFace = face;
            _habitStarted = GameLoop.GameLoopTime;
            _habitStrafe = strafe;
            _habitSpeed = speed;
            _habitUntil = _habitStarted + HabitWalkMilliseconds(bot, legs, speed);
            // A follow would stop the walk on its next check; the next attack turn after the walk re-follows.
            bot.StopFollowing();
            WalkHabitLeg(bot, legs[0]);
        }

        /// <summary>How long a habit walk may take: its legs at its speed, a little for each leg's turn, and a second.</summary>
        private static long HabitWalkMilliseconds(GameBot bot, Vector3[] legs, short speed)
        {
            float length = 0;
            Vector2 at = new(bot.X, bot.Y);
            foreach (Vector3 leg in legs)
            {
                length += Vector2.Distance(at, new(leg.X, leg.Y));
                at = new(leg.X, leg.Y);
            }
            return (long)(length * 1000 / Math.Max((short)40, speed)) + 120L * legs.Length + 1_000;
        }

        /// <summary>One leg: face first, then walk (at most 22 units, so the mover does not turn the bot); think again as it ends.</summary>
        private void WalkHabitLeg(GameBot bot, Vector3 leg)
        {
            bot.TurnTo(_habitFace);
            bot.PathTo(leg, _habitSpeed);
            float length = Vector2.Distance(new(bot.X, bot.Y), new(leg.X, leg.Y));
            int arrival = (int)(length * 1000 / Math.Max((short)40, _habitSpeed)) + 40;
            AutonomousLivingServer.Later(bot, arrival, () => bot.Brain == this && _habitLegs != null && bot.IsAlive,
                () => NextThinkTick = GameLoop.GameLoopTime);
        }

        /// <summary>
        /// Circle-strafe (BotMovementHabits): a melee member in a fight against an enemy player or bot, at its tier's share
        /// of fights, every 3-8 s, takes one to three side legs around the target. True when it set off.
        /// </summary>
        private bool TryRvrStrafe(GameBot bot, GameLiving target, RvrSkillTier tier, long now)
        {
            if (target is not (GamePlayer or GameBot) || !target.IsAlive || target.Realm == bot.Realm ||
                target.CurrentRegion != bot.CurrentRegion)
            {
                _strafeTarget = null;
                return false;
            }
            long id = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
            if (_strafeTarget != target)
            {
                _strafeTarget = target;
                _strafeFight = BotMovementHabits.StrafesThisFight(tier, id, target is GameBot enemyBot && enemyBot.DatabaseID > 0
                    ? enemyBot.DatabaseID : target.ObjectID);
                _strafeFightCounted = false;
                _nextStrafeAt = now + BotMovementHabits.StrafeSettleMs +
                    BotMovementHabits.NextStrafeDelay(Random.Shared.NextDouble()) / 2;
            }
            if (!_strafeFight || now < _nextStrafeAt)
                return false;
            _nextStrafeAt = now + BotMovementHabits.NextStrafeDelay(Random.Shared.NextDouble());
            if (!BotMovementHabits.StrafeAllowed(bot.ActiveWeaponSlot != eActiveWeaponSlot.Distance, PrefersCurrentSpellRange(),
                    bot.IsAttacking, true, bot.IsWithinRadius(target, bot.MeleeAttackRange + BotMovementHabits.StrafeReachSlack),
                    bot.IsMoving, target.IsMoving, bot.IsCasting, bot.IsStealthed,
                    bot.IsCrowdControlled || bot.IsTurningDisabled || bot.IsUnderwater || bot.MaxSpeed <= 0))
                return false;
            if (BotPeopleScope.Check(bot, BotPeopleRule.Movement, BotPeopleSwitches.Current.MoveStrafe, out _) != BotPeopleRefusal.None)
            {
                _strafeFight = false;
                return false;
            }
            if (!_strafeFightCounted)
            {
                // A strafing fight counts once the scope allowed it (the first strafe that is due).
                _strafeFightCounted = true;
                BotMovementHabitsRuntime.Count(BotMovementHabitsRuntime.Counter.StrafeFights);
            }
            using var aiProfile = AiProfile.Scope(AiSystem.MovementHabits);
            Vector2 center = new(target.X, target.Y), self = new(bot.X, bot.Y);
            float radius = Vector2.Distance(center, self);
            float sweep = BotMovementHabits.StrafeSweep(radius, Random.Shared.NextDouble());
            Style flankStyle = FlankStyle();
            Style.eOpeningPosition? keep = flankStyle == null ? null : (Style.eOpeningPosition)flankStyle.OpeningRequirementValue;
            int direction = BotCombatMovement.ChooseDirection(target.GetAngle(bot), sweep, 0, false, keep, Random.Shared.NextDouble());
            Vector2[] arc = BotCombatMovement.ArcLegs(center, radius, target.GetHeading(bot), direction, sweep);
            Vector3[] legs = BotMovementHabitsRuntime.ProveLegs(bot, new(bot.X, bot.Y, bot.Z), arc);
            if (legs.Length == 0)
                return false;
            StartHabitLegs(bot, target, legs, strafe: true, (short)Math.Clamp(bot.MaxSpeed * 60 / 100, 80, 150));
            BotMovementHabitsRuntime.Count(BotMovementHabitsRuntime.Counter.Strafes);
            if (BotMovementHabitsRuntime.ShouldLog(BotMovementHabitsRuntime.EventTag.Strafe))
                BotMovementHabitsRuntime.Info($"BOT_MOVE_STRAFE kind=circle bot=\"{bot.Name}\" id={bot.DatabaseID} " +
                    $"class={(eCharacterClass)(bot.CharacterClass?.ID ?? 0)} target=\"{target.Name}\" targetKind={RvrPlaceKind(target)} " +
                    $"legs={legs.Length} sweep={(int)sweep} region={bot.CurrentRegionID} skill={tier}");
            return true;
        }

        /// <summary>
        /// Face the enemy (BotMovementHabits): a caster or healer with <paramref name="attacker"/> on it backs off in an arc
        /// facing it, bent toward <paramref name="refuge"/>, at its tier's share of contacts. True when it set off.
        /// </summary>
        private bool TryRvrFaceBack(GameBot bot, GameLiving attacker, Vector2? refuge, RvrSkillTier tier, long contact,
            Vector2? kiteSpot = null)
        {
            if (attacker == null || !attacker.IsAlive || bot.IsCasting || bot.IsMoving || bot.MaxSpeed <= 0 || bot.IsStealthed ||
                bot.IsTurningDisabled || bot.IsUnderwater)
                return false;
            long id = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
            if (!BotMovementHabits.FacesThisContact(tier, id, contact) ||
                BotPeopleScope.Check(bot, BotPeopleRule.Movement, BotPeopleSwitches.Current.MoveStrafe, out _) != BotPeopleRefusal.None)
                return false;
            using var aiProfile = AiProfile.Scope(AiSystem.MovementHabits);
            Vector2 self = new(bot.X, bot.Y);
            // A healer's face-back is its kite's own run (to the kite's spot, at the kite's speed), walked facing the
            // attacker along a slight arc: the habit changes facing and the path, never the safety distance. A caster,
            // which did not kite before, backs off 80 units.
            Vector2[] ends = kiteSpot is Vector2 spot
                ? BotMovementHabits.ArcLegsTo(self, spot, Random.Shared.NextDouble())
                : BotMovementHabits.BackOffLegs(self, new(attacker.X, attacker.Y), refuge, Random.Shared.NextDouble());
            Vector3[] legs = BotMovementHabitsRuntime.ProveLegs(bot, new(bot.X, bot.Y, bot.Z), ends);
            if (!BotMovementHabits.FaceWalkTaken(legs.Length, ends.Length,
                    legs.Length == 0 ? 0 : Vector2.Distance(self, new(legs[^1].X, legs[^1].Y)),
                    ends.Length == 0 ? 0 : Vector2.Distance(self, ends[^1]), kiteSpot.HasValue))
                return false;
            if (Body.attackComponent?.AttackState == true)
                Body.StopAttack();
            StartHabitLegs(bot, attacker, legs, strafe: false, kiteSpot.HasValue
                ? bot.MaxSpeed
                : (short)Math.Clamp(bot.MaxSpeed * 60 / 100, 70, 140));
            BotMovementHabitsRuntime.Count(BotMovementHabitsRuntime.Counter.Faces);
            if (BotMovementHabitsRuntime.ShouldLog(BotMovementHabitsRuntime.EventTag.Strafe))
                BotMovementHabitsRuntime.Info($"BOT_MOVE_STRAFE kind=face bot=\"{bot.Name}\" id={bot.DatabaseID} " +
                    $"class={(eCharacterClass)(bot.CharacterClass?.ID ?? 0)} attacker=\"{attacker.Name}\" attackerKind={RvrPlaceKind(attacker)} " +
                    $"legs={legs.Length} refuge={(refuge.HasValue ? "yes" : "no")} region={bot.CurrentRegionID} skill={tier}");
            return true;
        }

        /// <summary>
        /// A caster (not a healer: a healer's turn is its kite) with an enemy melee fighter on it faces it and backs off,
        /// at most every 3-5 s. True when it set off.
        /// </summary>
        private bool TryRvrCasterFace(GameBot bot, RvrFightBoard board, RvrFightPlan plan, RvrCombatant me, Vector2? line, long now)
        {
            if (now < _faceReadyAt || RvrPositioning.KiteFrom(Body, me.X, me.Y, board.Enemies) is not { Living: GameLiving melee })
                return false;
            _faceReadyAt = now + BotMovementHabits.NextFaceDelay(Random.Shared.NextDouble());
            return TryRvrFaceBack(bot, melee, line, plan.SkillTier, ++_faceContacts);
        }
    }
}
