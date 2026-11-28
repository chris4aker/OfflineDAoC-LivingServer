// Living Server 1.2, open-field RvR work package 3 ("Hands": docs/RVR-FIELD-1.2.md): where a bot-only group's members stand
// in a fight against players or bots. The rules are RvrPositioning's and RvrPeelPlan's (pure, tested); this partial reads
// the fight boards, proves each walk on the navigation mesh and walks it. Tests only until seen in the owner's PC log.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Positioning in an open-field fight (rvr_bot_positioning): casters and healers stand behind the tanks relative to
    /// the enemy, keep more than 350 units apart (one mez cannot catch two healers) and scatter wider on "incoming"; a
    /// shield tank guarding a named member stays beside it and leaves enemies far from it to the others; a healer with an
    /// enemy melee fighter on it kites ("run a bit, throw heal"), then heals. In the Kite stance (work package 4) the
    /// group fights while backing away: the backline keeps its stand-off distance from the nearest enemy melee fighter
    /// in short proven legs and the melee screen it (RvrKiteTurn). Each group makes its tier's mistakes
    /// (RvrPositioning, RvrPeelPlan). Every walk is proven first on dry ground on the bot's own floor (the combat
    /// steps' ReachableLeg, piece by piece): no ledge, no water, no other floor. The PvE combat movement
    /// (BotBrain.CombatMovement) stays off for player and bot targets; this is the only movement added for them.
    /// Only for bots the open-field tactics apply to (RvrTacticsActive: no raid, battleground assault, keep fight,
    /// companion or player's group), in a group of two or more, while the group's board shows enemies.
    /// </summary>
    public partial class BotBrain
    {
        /// <summary>RVR_KITE at most this often per healer; RVR_BACKLINE at most this often per group.</summary>
        public const long RvrKiteLogGapMilliseconds = 10_000;
        public const long RvrBacklineLogGapMilliseconds = 10_000;
        /// <summary>The kiting group's RVR_KITE line (kind=group) at most this often per group.</summary>
        public const long RvrKiteGroupLogGapMilliseconds = 10_000;

        private enum RvrPlaceWalk : byte { None, Place, Kite, KiteStep }

        private RvrPlaceWalk _rvrPlaceWalk;
        private Vector3 _rvrPlaceTo;
        private long _rvrPlaceStarted;
        private long _rvrPlaceUntil;
        private long _rvrPlaceNextCheck;
        private long _rvrPlaceDueAt;
        private bool _rvrPlaceHold;
        private long _rvrKiteReadyAt;
        private long _rvrKiteContacts;
        private GameLiving _rvrKiteFrom;
        private long _rvrKiteLogAt = long.MinValue / 2;
        private long _rvrKiteSteps;
        private long _rvrKiteCastMilliseconds;

        private sealed class RvrBacklineLog
        {
            public long LastAt = long.MinValue / 2;
            public int Moved;
        }

        private static readonly ConditionalWeakTable<object, RvrBacklineLog> RvrBacklineLogs = new();

        private sealed class RvrKiteGroupLog
        {
            public long LastAt = long.MinValue / 2;
            public int Stepped;
            public int Held;
            public int Screened;
        }

        private static readonly ConditionalWeakTable<object, RvrKiteGroupLog> RvrKiteGroupLogs = new();

        // ---- shared reads -------------------------------------------------------------------------------------------------

        /// <summary>An enemy player, bot or pet of theirs.</summary>
        private bool RvrPlaceEnemy(GameLiving living) =>
            living != null && living.IsAlive && living.Realm != Body.Realm && living.Realm != eRealm.None &&
            BotPvpCrowdControl.PlayerLike(living);

        /// <summary>
        /// The group's RvR board when the bot is in a fight against players or bots: its target or its last attacker is
        /// one, or a group mate built a board in the last two seconds; and the board shows enemies. Null otherwise, so a
        /// PvE fight never builds a board here.
        /// </summary>
        private RvrFightBoard RvrPlaceBoard(GameLiving target)
        {
            if (Body?.CurrentRegion == null)
                return null;
            if (!RvrPlaceEnemy(target) && !RvrPlaceEnemy(RecentDirectAttacker()) &&
                RvrFightBoards.Peek(Body) is not { Enemies.IsDefaultOrEmpty: false })
                return null;
            RvrFightBoard board = RvrBoard();
            return board == null || board.Enemies.IsDefaultOrEmpty || board.EnemyCentroid == null ? null : board;
        }

        private long RvrPlaceGroupId()
        {
            GameLiving leader = Body.Group?.LivingLeader ?? Body;
            return leader is GameBot bot ? bot.DatabaseID : leader?.ObjectID ?? 0;
        }

        private static string RvrPlaceKind(GameLiving living) => living switch
        {
            null => "none",
            GamePlayer => "player",
            GameBot => "bot",
            GameNPC npc when BotPvpCrowdControl.PlayerLike(npc) => "pet",
            _ => "monster",
        };

        private static Vector2 Flat(GameObject living) => new(living.X, living.Y);

        // ---- the turns ------------------------------------------------------------------------------------------------------

        /// <summary>The attack turn (TryRvrTacticsMove): backline, spread, a guard's escort, and a hybrid healer's kite.</summary>
        private bool TryRvrPlaceMove(GameLiving target) => RvrPlaceTurn(target, support: false);

        /// <summary>The support turn (PerformGroupSupport, through TryRvrTacticsSupport): a main healer's kite and its place.</summary>
        private bool TryRvrPlaceSupport() => RvrPlaceTurn(Body.TargetObject as GameLiving, support: true);

        private bool RvrPlaceTurn(GameLiving target, bool support)
        {
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot?.Group == null || bot.Group.MemberCount < 2 || !bot.IsAlive || bot.CurrentRegion == null)
                return false;
            // #531 step 2f: a bomber walking in on its group's mezzed pile, or waiting beside it, is not called back to its place.
            if (_rvrBombSpell != null)
                return false;
            // Bots as people 1.2 (WP5): a strafe or a back-off facing the enemy (BotBrain.MovementHabits) owns the turn.
            if (ContinueHabitLegs(bot, now))
                return true;
            if (ContinueRvrPlaceWalk(bot, now))
                return true;
            if (bot.IsCasting || bot.IsIncapacitated || bot.IsCrowdControlled || bot.MaxSpeed <= 0 || bot.IsStealthed ||
                bot.IsUnderwater || bot.IsTurningDisabled)
            {
                _rvrPlaceHold = false;
                return false;
            }
            if (now < _rvrPlaceNextCheck)
                return _rvrPlaceHold && !support && target != null;
            _rvrPlaceNextCheck = now + RvrPositioning.CheckMilliseconds + Math.Abs(bot.DatabaseID % 250);
            _rvrPlaceHold = false;

            RvrFightBoard board = RvrPlaceBoard(target);
            RvrFightPlan plan = board?.Plan;
            // Retreat and regroup belong to the stance (work package 4); a kiting group backs away (RvrKiteTurn).
            if (plan == null || !RvrPositioning.PlacesIn(plan.Stance))
                return false;
            bool kiting = plan.Stance == RvrStance.Kite;
            RvrCombatant me = RvrPeelPlan.Find(board.Members, Body);
            if (me == null)
                return false;

            // AI_PROFILE: the layout and the walk proof (the board's build is timed by its own system).
            using var aiProfile = AiProfile.Scope(AiSystem.RvrTactics);
            RvrSkillTier tier = plan.SkillTier;
            // Bots as people WP2 ("Standing in fire"): a backline member hit by an enemy's area effect steps out of it after
            // the group tier's delay, moved by its own fire facet (owner decision 5).
            if (RvrPositioning.StandFor(me.Role, me.Class) == RvrStand.Back &&
                TryRvrStepOutOfFire(bot, tier, now, new Vector2(board.EnemyCentroid.Value.X, board.EnemyCentroid.Value.Y)))
                return true;
            if ((IsHealer || RvrPositioning.CountsAsHealer(me.Role, me.Class)) && TryRvrKite(bot, board, plan, me, now))
                return true;

            var placed = new List<RvrPositioning.Placed>(board.Members.Length);
            var positions = new List<(Vector2, RvrStand)>(board.Members.Length);
            Vector2 centre = Vector2.Zero;
            foreach (RvrCombatant member in board.Members)
            {
                if (member?.Living == null || member.IsPet)
                    continue;
                RvrStand stand = RvrPositioning.StandFor(member.Role, member.Class);
                GameLiving charge = null;
                if (member.Living is GameBot { Brain: BotBrain brain } && brain.RvrPeelSticks(plan, now, out GameLiving sticksTo))
                {
                    stand = RvrStand.Escort;
                    charge = sticksTo;
                }
                placed.Add(new RvrPositioning.Placed(member, stand, charge));
                positions.Add((RvrPeelPlan.At(member), stand));
                centre += RvrPeelPlan.At(member);
            }
            if (placed.Count < 2)
                return false;
            centre /= placed.Count;
            Vector2 enemy = new(board.EnemyCentroid.Value.X, board.EnemyCentroid.Value.Y);
            bool targeted = false;
            foreach (RvrCombatant foe in board.Enemies)
                targeted |= foe.TargetsGroup;
            bool incoming = RvrPositioning.Incoming(board.Enemies, targeted, centre);
            var slots = RvrPositioning.Layout(placed, enemy, tier, incoming, now);
            RvrSlot mine = default;
            foreach (RvrSlot slot in slots)
                if (ReferenceEquals(slot.Member, Body))
                    mine = slot;
            Vector2 self = Flat(bot);

            if (mine.Stand == RvrStand.Escort)
                return !support && RvrEscort(bot, target, plan, enemy, now);
            if (kiting && RvrKiteTurn(bot, target, support, board, plan, slots, mine, positions, centre, enemy, now) is bool kited)
                return kited;
            if (mine.Stand != RvrStand.Back)
                return !support && TryRvrStrafe(bot, target, tier, now); // WP5: melee circle-strafe
            // WP5: a caster with an enemy melee fighter on it faces it and backs off in an arc.
            if (!IsHealer && !RvrPositioning.CountsAsHealer(me.Role, me.Class) &&
                TryRvrCasterFace(bot, board, plan, me, RvrPositioning.Line(positions), now))
                return true;
            // Heals first: a healer walks for its place only while nobody needs a heal.
            if (IsHealer)
                foreach (RvrCombatant member in board.Members)
                    if (!member.IsPet && member.HealthPercent < RvrPositioning.HealFirstPercent)
                        return false;

            Vector2? line = RvrPositioning.Line(positions);
            double front = RvrPositioning.Bearing(line ?? centre, enemy);
            var others = new List<(Vector2, bool)>();
            foreach (RvrSlot slot in slots)
                if (slot.Stand == RvrStand.Back && !ReferenceEquals(slot.Member, Body) &&
                    RvrPeelPlan.Find(board.Members, slot.Member) is { } other)
                    others.Add((RvrPeelPlan.At(other), slot.Healer));
            RvrMoveReason reason = RvrPositioning.NeedsMove(self, mine, line, front, incoming, others);
            if (reason == RvrMoveReason.None)
            {
                _rvrPlaceDueAt = 0;
                return false;
            }
            // A person notices he stands wrong a moment later (the tier's delay; Bots as people WP2: the member's positioning
            // facet moves it inside the group tier's band, owner decision 5).
            if (_rvrPlaceDueAt == 0)
                _rvrPlaceDueAt = now + BotSkillHands.RvrPlaceDelay(bot, tier, RvrPositioning.ReactionMilliseconds(tier, bot.DatabaseID, now));
            if (now < _rvrPlaceDueAt)
                return false;
            Vector2 spot = mine.Spot;
            if (!support && target != null && PrefersCurrentSpellRange())
                spot = RvrPositioning.WithinRange(spot, Flat(target), Math.Max(300, OffensiveApproachRange(target) - 75));
            if (!StartRvrPlaceWalk(bot, spot, RvrPlaceWalk.Place, now))
                return false;
            _rvrPlaceDueAt = 0;
            LogRvrBackline(reason, line is Vector2 anchor ? -RvrPositioning.Ahead(spot, anchor, front) : 0, tier, now);
            return true;
        }

        // ---- standing in fire (Bots as people WP2) ---------------------------------------------------------------------------

        /// <summary>A hit from an area effect older than this ends the episode (the bot is no longer standing in it).</summary>
        public const long RvrFireMemoryMilliseconds = 3_000;

        private long _rvrFireAt;
        private long _rvrFireFirstAt;
        private long _rvrFireDueAt;
        private Vector2 _rvrFireCenter;
        private Vector2 _rvrFireFrom;
        private int _rvrFireRadius;

        /// <summary>
        /// Every hit (OnAttackedByEnemy): notes an enemy player's (or bot's, or their pet's) area effect on an autonomous
        /// gamebot: where it lands and how wide. A new place or a gap of 3 s starts a new episode (a new delay).
        /// </summary>
        private void NoteRvrFire(AttackData ad)
        {
            if (BotBody is not { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsPlayerLedGroup: false } ||
                !BotIncomingFireReaction.IsAreaEffect(ad) || ad.Attacker is not GameLiving caster ||
                caster.CurrentRegionID != Body.CurrentRegionID || BotIncomingFireReaction.Fighter(caster) == null)
                return;
            Spell spell = ad.SpellHandler.Spell;
            Vector2 center;
            if (spell.Target == eSpellTarget.AREA && caster.GroundTarget.IsValid)
                center = new(caster.GroundTarget.X, caster.GroundTarget.Y);
            else if (spell.IsPBAoE || spell.Range <= 0 || ad.SpellHandler.Target is not GameLiving primary)
                center = Flat(caster);
            else
                center = Flat(primary);
            long now = GameLoop.GameLoopTime;
            if (now - _rvrFireAt > RvrFireMemoryMilliseconds || Vector2.Distance(center, _rvrFireCenter) > Math.Max(50, spell.Radius / 2))
            {
                _rvrFireFirstAt = now;
                _rvrFireDueAt = 0;
            }
            _rvrFireAt = now;
            _rvrFireCenter = center;
            _rvrFireFrom = Flat(caster);
            _rvrFireRadius = spell.Radius;
        }

        /// <summary>
        /// A backline member still inside the area it was hit by in the last 3 s walks out of it once its delay has passed:
        /// BotSkillHands.FireDelay, the group tier's band (Casual 2-5 s, Average 1-3 s, Veteran 0.5-1.5 s) moved by its fire
        /// facet. One step per episode, proven on the mesh like every place walk. Out of scope: today's rule (it stays).
        /// </summary>
        private bool TryRvrStepOutOfFire(GameBot bot, RvrSkillTier tier, long now, Vector2 enemy)
        {
            if (_rvrFireAt == 0 || now - _rvrFireAt > RvrFireMemoryMilliseconds)
                return false;
            Vector2 self = Flat(bot);
            float distance = Vector2.Distance(self, _rvrFireCenter);
            if (distance > _rvrFireRadius + 25 || _rvrFireDueAt < 0)
                return false;
            if (_rvrFireDueAt == 0)
            {
                long delay = BotSkillHands.FireDelay(bot, tier);
                _rvrFireDueAt = delay < 0 ? -1 : _rvrFireFirstAt + Math.Max(1, delay);
                if (delay < 0)
                    return false;
            }
            if (now < _rvrFireDueAt)
                return false;
            _rvrFireDueAt = -1;
            // Out of the circle, never toward the enemy (an area centred on a groupmate: out on the far side from them).
            Vector2 spot = BotSkillMistakes.FireStepSpot(self, _rvrFireCenter, _rvrFireRadius, enemy, _rvrFireFrom);
            return StartRvrPlaceWalk(bot, spot, RvrPlaceWalk.Place, now);
        }

        /// <summary>
        /// A guard with a charge: he fights an enemy within RvrPeelPlan.LeashRadius of his charge; otherwise he walks back
        /// beside it and stands there facing the fight (2003: "Guard + intercept + /stick on your casters").
        /// </summary>
        private bool RvrEscort(GameBot bot, GameLiving target, RvrFightPlan plan, Vector2 enemy, long now)
        {
            if (!RvrPeelSticks(plan, now, out GameLiving charge))
                return false;
            if (target != null && target.IsAlive && charge.IsWithinRadius(target, RvrPeelPlan.LeashRadius))
                return false;
            // A bot that is hit always fights back: a guard hit in melee does not stand idle beside his charge.
            if (RecentDirectAttacker() is GameLiving attacker && attacker.IsAlive && bot.IsWithinRadius(attacker, bot.MeleeAttackRange + 50))
                return false;
            Vector2 spot = RvrPositioning.EscortSpot(Flat(charge), enemy);
            if (Vector2.Distance(Flat(bot), spot) > RvrPeelPlan.StickRadius)
            {
                if (!StartRvrPlaceWalk(bot, spot, RvrPlaceWalk.Place, now))
                    return false;
                LogRvrBackline(RvrMoveReason.Escort, 0, plan.SkillTier, now);
                return true;
            }
            if (Body.attackComponent?.AttackState == true)
                Body.StopAttack();
            Body.StopFollowing();
            if (target != null)
                Body.TurnTo(target);
            _rvrPlaceHold = true;
            return true;
        }

        /// <summary>
        /// A healer with an enemy melee fighter on it runs a bit (by tier: some do not), toward its guard or the tanks'
        /// line so they can peel the attacker, then heals for 3-5 s before it kites again.
        /// </summary>
        private bool TryRvrKite(GameBot bot, RvrFightBoard board, RvrFightPlan plan, RvrCombatant me, long now)
        {
            RvrCombatant attacker = RvrPositioning.KiteFrom(Body, me.X, me.Y, board.Enemies);
            if (attacker?.Living == null || now < _rvrKiteReadyAt)
                return false;
            RvrSkillTier tier = plan.SkillTier;
            long contact = ++_rvrKiteContacts;
            double roll = RvrPeelPlan.Roll(bot.DatabaseID, contact, 0x4B495445);
            if (RvrPositioning.KiteSkipped(tier, bot.DatabaseID, contact))
            {
                _rvrKiteReadyAt = now + RvrPositioning.KiteHealWindow(roll);
                return false;
            }
            Vector2? refuge = null;
            if (RvrPeelPlan.GuardOf(plan, Body) is GameLiving guard && guard.IsAlive && guard.CurrentRegion == Body.CurrentRegion)
                refuge = Flat(guard);
            else
            {
                var line = new List<(Vector2, RvrStand)>();
                foreach (RvrCombatant member in board.Members)
                    if (!member.IsPet && member.Living != Body)
                        line.Add((RvrPeelPlan.At(member), RvrPositioning.StandFor(member.Role, member.Class)));
                refuge = RvrPositioning.Line(line);
            }
            float distance = RvrPositioning.KiteDistance(tier, roll);
            Vector2 spot = RvrPositioning.KiteSpot(Flat(bot), RvrPeelPlan.At(attacker), refuge, distance);
            // WP5: a healer that faces the enemy runs the same kite facing it, in short legs along an arc, then heals.
            if (TryRvrFaceBack(bot, attacker.Living, refuge, tier, contact, spot))
            {
                _rvrKiteReadyAt = _habitUntil + RvrPositioning.KiteHealWindow(roll);
                return true;
            }
            if (!StartRvrPlaceWalk(bot, spot, RvrPlaceWalk.Kite, now))
                return false;
            _rvrKiteFrom = attacker.Living;
            _rvrKiteReadyAt = _rvrPlaceUntil + RvrPositioning.KiteHealWindow(roll);
            if (log.IsInfoEnabled && RvrPositioning.ShouldLog(now, _rvrKiteLogAt, RvrKiteLogGapMilliseconds))
            {
                _rvrKiteLogAt = now;
                log.Info($"RVR_KITE group={RvrPlaceGroupId()} region={Body.CurrentRegionID} healer=\"{Body.Name}\" class={me.Class} " +
                         $"attacker=\"{attacker.Living.Name}\" attackerKind={RvrPlaceKind(attacker.Living)} attackerClass={attacker.Class} " +
                         $"run={(int)distance} guard={(RvrPeelPlan.GuardOf(plan, Body) is GameLiving g ? $"\"{g.Name}\"" : "none")} skill={tier} " +
                         $"fight={RvrFights.IdOf(Body)}");
            }
            return true;
        }

        // ---- the kiting group (stance Kite) -----------------------------------------------------------------------------

        /// <summary>
        /// The Kite stance (work package 4 publishes it; this moves the group). A backline member keeps its stand-off
        /// distance from the nearest enemy melee fighter: it steps back in a short leg along the group's back direction
        /// (away from the enemy centroid, bent toward its back-arc slot), each leg proven whole on dry ground on its own
        /// floor and kept away from other enemy groups and enemy keeps' guards; then it casts or shoots for 2-3.5 s. With
        /// no safe leg it holds and fights. A melee member screens the backline and fights only what reaches it. Errors
        /// by tier (RvrPositioning.KiteErrors, KiteChases). True: the turn was used; false: fight as usual; null: no enemy
        /// melee fighter chases, so the layout of the other stances applies.
        /// </summary>
        private bool? RvrKiteTurn(GameBot bot, GameLiving target, bool support, RvrFightBoard board, RvrFightPlan plan,
            ImmutableArray<RvrSlot> slots, RvrSlot mine, List<(Vector2, RvrStand)> positions, Vector2 centre, Vector2 enemy, long now)
        {
            RvrSkillTier tier = plan.SkillTier;
            Vector2 self = Flat(bot);
            Vector2? line = RvrPositioning.Line(positions);
            double front = RvrPositioning.Bearing(line ?? centre, enemy);

            if (mine.Stand is RvrStand.Front or RvrStand.Side)
            {
                if (support)
                    return false;
                var backline = new List<Vector2>();
                foreach (RvrSlot slot in slots)
                    if (slot.Stand == RvrStand.Back && RvrPeelPlan.Find(board.Members, slot.Member) is { } member)
                        backline.Add(RvrPeelPlan.At(member));
                bool hit = RecentDirectAttacker() is GameLiving attacker && attacker.IsAlive &&
                           bot.IsWithinRadius(attacker, bot.MeleeAttackRange + 50);
                Vector2? at = target != null && target.IsAlive ? Flat(target) : null;
                (RvrKitePeel action, Vector2 spot) = RvrPositioning.KitePeel(self, at, backline, enemy, hit,
                    RvrPositioning.KiteChases(tier, bot.DatabaseID, now));
                if (action == RvrKitePeel.Fight)
                    return false;
                if (action == RvrKitePeel.Screen)
                {
                    if (!StartRvrPlaceWalk(bot, spot, RvrPlaceWalk.Place, now))
                        return false;
                    LogRvrKiteGroup("screen", 0, 0, 0, RvrKiteHeld.None, RvrKiteError.None, tier, now);
                    return true;
                }
                if (Body.attackComponent?.AttackState == true)
                    Body.StopAttack();
                Body.StopFollowing();
                if (target != null)
                    Body.TurnTo(target);
                _rvrPlaceHold = true;
                return true;
            }
            if (mine.Stand != RvrStand.Back)
                return false;

            if (RvrPositioning.NearestMelee(self, board.Enemies, out float meleeDistance) is not { } melee)
                return null;
            if (IsHealer && meleeDistance > RvrPositioning.KiteHealFirstDistance)
                foreach (RvrCombatant member in board.Members)
                    if (!member.IsPet && member.HealthPercent < RvrPositioning.HealFirstPercent)
                    {
                        LogRvrKiteGroup("back", meleeDistance, 0, 0, RvrKiteHeld.HealFirst, RvrKiteError.None, tier, now);
                        return false;
                    }
            IReadOnlyList<AutonomousArmyDetour.Hazard> keeps = meleeDistance < RvrPositioning.KiteStandOff + 300
                ? RvrKiteKeepHazards(bot) : null;
            long stepNumber = _rvrKiteSteps + 1;
            Vector3? proven = null;
            RvrKiteStep step = RvrPositioning.KiteStep(self, mine.Spot, front, board.Enemies, enemy, keeps, tier, bot.DatabaseID,
                stepNumber, (_, to) => (proven = ProveRvrWalk(bot, to, whole: true, out float _)) != null);
            if (step.Spot == null || proven == null)
            {
                _rvrPlaceDueAt = 0;
                if (step.Held != RvrKiteHeld.NoMelee)
                    LogRvrKiteGroup("back", step.MeleeDistance, 0, step.StandOff, step.Held, step.Error, tier, now);
                return step.Held == RvrKiteHeld.NoMelee ? null : false;
            }
            // A person notices the melee closing a moment later (the tier's delay).
            if (_rvrPlaceDueAt == 0)
                _rvrPlaceDueAt = now + RvrPositioning.ReactionMilliseconds(tier, bot.DatabaseID, now);
            if (now < _rvrPlaceDueAt)
                return false;
            if (!BeginRvrPlaceWalk(bot, proven.Value, RvrPlaceWalk.KiteStep, now))
                return false;
            _rvrPlaceDueAt = 0;
            _rvrKiteSteps = stepNumber;
            _rvrKiteFrom = melee.Living;
            _rvrKiteCastMilliseconds = RvrPositioning.KiteFightWindow(RvrPeelPlan.Roll(bot.DatabaseID, stepNumber, 0x4B495445));
            LogRvrKiteGroup("back", step.MeleeDistance, step.Length, step.StandOff, RvrKiteHeld.None, step.Error, tier, now);
            return true;
        }

        /// <summary>The enemy keeps and towers of the bot's region with their guards' range (RvrPositioning.KiteKeepGuardClearance).</summary>
        private static AutonomousArmyDetour.Hazard[] RvrKiteKeepHazards(GameBot bot)
        {
            if (GameServer.KeepManager == null || bot.CurrentRegion == null)
                return [];
            var hazards = new List<AutonomousArmyDetour.Hazard>();
            foreach (DOL.GS.Keeps.AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID))
                if (keep != null && keep.Realm != bot.Realm && !keep.IsPortalKeep)
                    hazards.Add(new(new(keep.X, keep.Y), RvrPositioning.KiteKeepGuardClearance));
            return hazards.ToArray();
        }

        /// <summary>RVR_KITE group= region= kind=group stepped= held= screened= member= role= enemyMelee= step= standOff=
        /// error= held= skill=: at most every 10 s per group, with the group's steps, holds and screen walks since the last
        /// line and the latest member's numbers.</summary>
        private void LogRvrKiteGroup(string role, float meleeDistance, float stepLength, float standOff, RvrKiteHeld held,
            RvrKiteError error, RvrSkillTier tier, long now)
        {
            if (!log.IsInfoEnabled)
                return;
            RvrKiteGroupLog entry = RvrKiteGroupLogs.GetValue(RvrFightBoards.KeyOf(Body), static _ => new RvrKiteGroupLog());
            int stepped, holds, screened;
            lock (entry)
            {
                if (role == "screen")
                    entry.Screened++;
                else if (held == RvrKiteHeld.None)
                    entry.Stepped++;
                else if (held != RvrKiteHeld.Clear)
                    entry.Held++;
                if (!RvrPositioning.ShouldLog(now, entry.LastAt, RvrKiteGroupLogGapMilliseconds))
                    return;
                (stepped, holds, screened) = (entry.Stepped, entry.Held, entry.Screened);
                (entry.Stepped, entry.Held, entry.Screened) = (0, 0, 0);
                entry.LastAt = now;
            }
            log.Info($"RVR_KITE group={RvrPlaceGroupId()} region={Body.CurrentRegionID} kind=group stepped={stepped} held={holds} " +
                     $"screened={screened} member=\"{Body.Name}\" class={(eCharacterClass)(BotBody?.CharacterClass?.ID ?? 0)} role={role} " +
                     $"enemyMelee={(int)meleeDistance} step={(int)stepLength} standOff={(int)standOff} error={error} " +
                     $"heldReason={held} skill={tier} fight={RvrFights.IdOf(Body)}");
        }

        // ---- walking --------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Proves a walk from the bot toward <paramref name="spot"/> (at most RvrPositioning.MaxWalk) on the mesh piece by
        /// piece with the combat steps' ReachableLeg (same zone, dry ground, the bot's own floor: never off a ledge or into
        /// water). With <paramref name="whole"/> every piece must hold; otherwise a walk blocked part way ends where the
        /// proof ended when that is at least half the way. The walk's end, or null (no walk).
        /// </summary>
        private static Vector3? ProveRvrWalk(GameBot bot, Vector2 spot, bool whole, out float covered)
        {
            covered = 0;
            Vector3 from = new(bot.X, bot.Y, bot.Z);
            Vector2 start = new(from.X, from.Y);
            float total = Vector2.Distance(start, spot);
            if (total < RvrPositioning.SlotTolerance)
                return null;
            if (total > RvrPositioning.MaxWalk)
            {
                spot = start + (spot - start) / total * RvrPositioning.MaxWalk;
                total = RvrPositioning.MaxWalk;
            }
            Vector3 at = from;
            int proven = 0;
            Vector2[] pieces = RvrPositioning.Chunks(start, spot);
            foreach (Vector2 piece in pieces)
            {
                if (ReachableLeg(bot, at, new Vector3(piece.X, piece.Y, at.Z)) is not Vector3 leg)
                    break;
                at = leg;
                proven++;
            }
            covered = Vector2.Distance(start, new(at.X, at.Y));
            if (proven == 0 || proven < pieces.Length && (whole || covered < total / 2 || covered < 80))
            {
                BotCombatMovement.Count(BotCombatMovement.Counter.Blocked);
                return null;
            }
            return at;
        }

        /// <summary>
        /// Walks toward <paramref name="spot"/> at full speed once the walk is proven (ProveRvrWalk, part way allowed).
        /// True when the bot set off.
        /// </summary>
        private bool StartRvrPlaceWalk(GameBot bot, Vector2 spot, RvrPlaceWalk kind, long now) =>
            ProveRvrWalk(bot, spot, whole: false, out _) is Vector3 at && BeginRvrPlaceWalk(bot, at, kind, now);

        /// <summary>Sets off on a proven walk to <paramref name="at"/>. True when the bot set off.</summary>
        private bool BeginRvrPlaceWalk(GameBot bot, Vector3 at, RvrPlaceWalk kind, long now)
        {
            float covered = Vector2.Distance(Flat(bot), new(at.X, at.Y));
            if (Body.attackComponent?.AttackState == true)
                Body.StopAttack();
            Body.StopFollowing();
            bot.PathTo(at, bot.MaxSpeed);
            _rvrPlaceWalk = kind;
            _rvrPlaceTo = at;
            _rvrPlaceStarted = now;
            _rvrPlaceUntil = now + Math.Min(RvrPositioning.MoveTimeoutMilliseconds, (long)(covered * 1000 / Math.Max(50, (int)bot.MaxSpeed)) + 1_000);
            _rvrPlaceHold = false;
            return true;
        }

        /// <summary>True while a walk this partial started still owns the turn; ends it (facing the fight) when it arrived,
        /// stopped or timed out.</summary>
        private bool ContinueRvrPlaceWalk(GameBot bot, long now)
        {
            if (_rvrPlaceWalk == RvrPlaceWalk.None)
                return false;
            bool arrived = Vector2.Distance(Flat(bot), new(_rvrPlaceTo.X, _rvrPlaceTo.Y)) <= RvrPositioning.SlotTolerance;
            if (!arrived && now < _rvrPlaceUntil && !bot.IsIncapacitated && (bot.IsMoving || now - _rvrPlaceStarted < 600))
                return true;
            if (bot.IsMoving && !arrived)
                bot.StopMoving();
            RvrPlaceWalk walk = _rvrPlaceWalk;
            _rvrPlaceWalk = RvrPlaceWalk.None;
            // After a kite step the member casts or shoots for its window before it steps again.
            _rvrPlaceNextCheck = Math.Max(_rvrPlaceNextCheck, now + (walk == RvrPlaceWalk.KiteStep
                ? Math.Max(RvrPositioning.CheckMilliseconds, _rvrKiteCastMilliseconds) : RvrPositioning.CheckMilliseconds));
            GameLiving face = walk is RvrPlaceWalk.Kite or RvrPlaceWalk.KiteStep && _rvrKiteFrom?.IsAlive == true
                ? _rvrKiteFrom : Body.TargetObject as GameLiving;
            if (face?.IsAlive == true && face.CurrentRegion == bot.CurrentRegion)
                bot.TurnTo(face);
            _rvrKiteFrom = null;
            return false;
        }

        /// <summary>RVR_BACKLINE group= region= moved= member= class= reason= depth= skill=: at most every 10 s per group,
        /// with how many members of the group walked for their place since the last line.</summary>
        private void LogRvrBackline(RvrMoveReason reason, float depth, RvrSkillTier tier, long now)
        {
            if (!log.IsInfoEnabled)
                return;
            RvrBacklineLog entry = RvrBacklineLogs.GetValue(RvrFightBoards.KeyOf(Body), static _ => new RvrBacklineLog());
            int moved;
            lock (entry)
            {
                entry.Moved++;
                if (!RvrPositioning.ShouldLog(now, entry.LastAt, RvrBacklineLogGapMilliseconds))
                    return;
                moved = entry.Moved;
                entry.Moved = 0;
                entry.LastAt = now;
            }
            log.Info($"RVR_BACKLINE group={RvrPlaceGroupId()} region={Body.CurrentRegionID} moved={moved} member=\"{Body.Name}\" " +
                     $"class={(eCharacterClass)(BotBody?.CharacterClass?.ID ?? 0)} reason={reason} depth={(int)depth} skill={tier}");
        }
    }
}
