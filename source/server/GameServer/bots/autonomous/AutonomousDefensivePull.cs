using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>One bounded, real ranged pull per level-50 PvE party. No new timers or world scans.</summary>
    public static class AutonomousDefensivePull
    {
        public const int ContactRadius = 400;
        public const int TimeoutMilliseconds = 30_000;
        private sealed class State
        {
            public GameBot[] Members;
            public GameBot Shooter;
            public GameLiving Target;
            public Vector3 Anchor, FiringPoint;
            public Spell Spell;
            public eActiveWeaponSlot PreviousWeapon;
            public readonly Dictionary<IControlledBrain, eAggressionState> Pets = new();
            public long Until, RetryAfter, NextMove;
            public bool Shot, Active;
            public readonly UnreachableBench Bench = new();
        }

        /// <summary>
        /// 1.2.3 (#539): a party's memory of ranged pulls with no firing point. A target with no firing point
        /// <see cref="TriesBeforeBench"/> times is benched for the camp: the party pulls it the ordinary way (the puller's
        /// watched pull, retried once, then another spawn) instead of standing on the ranged pull. With
        /// <see cref="BenchedTargetsBeforeCampGiveUp"/> targets benched the camp is given up (a new camp). Owner's PC, 1.2.2
        /// 868a82d, 11/24: 2,788 AUTONOMOUS_DEFENSIVE_PULL_UNREACHABLE, 55 runs of one shooter on one target for 10 minutes
        /// or more (Hendrik and a sheevra skirmisher 126 times in 74 minutes), because nothing remembered the failure.
        /// Pure: fed one observation at a time; not thread-safe (the pull state's lock guards it).
        /// </summary>
        public sealed class UnreachableBench
        {
            public const int TriesBeforeBench = 2;
            public const int BenchedTargetsBeforeCampGiveUp = 3;
            public const long BenchMilliseconds = 20 * 60_000L;

            private readonly Dictionary<int, (int Tries, long BenchedAt)> _targets = new();

            public string CampKey { get; private set; } = string.Empty;

            /// <summary>The party works this camp; a different camp starts with a clean bench.</summary>
            public void ForCamp(string campKey)
            {
                campKey ??= string.Empty;
                if (string.Equals(campKey, CampKey, StringComparison.Ordinal)) return;
                CampKey = campKey;
                _targets.Clear();
            }

            public bool IsBenched(int target, long now) =>
                _targets.TryGetValue(target, out var entry) && entry.BenchedAt > 0 && now - entry.BenchedAt < BenchMilliseconds;

            public int Tries(int target) => _targets.TryGetValue(target, out var entry) ? entry.Tries : 0;

            /// <summary>One more ranged pull of this target found no firing point; true when this one benches it.</summary>
            public bool RecordUnreachable(int target, long now)
            {
                if (IsBenched(target, now)) return false;
                _targets.TryGetValue(target, out var entry);
                // A bench that ran out starts counting again.
                int tries = entry.BenchedAt > 0 ? 1 : entry.Tries + 1;
                bool bench = tries >= TriesBeforeBench;
                _targets[target] = (tries, bench ? Math.Max(1, now) : 0);
                return bench;
            }

            /// <summary>
            /// A ranged pull that was made but timed out with the target alive and never at the party (it ignored the
            /// shot or couldn't path) is a failed try too: two in a row bench it like two with no firing point.
            /// DividedRealms saw 280 of 1,097 PvE tasks expire on this loop ("Holding group formation"). Port of
            /// rambodolphin13/DividedRealms 1d18f864, folded into the #539 bench (which already had its 7e736ca8 half).
            /// </summary>
            public bool RecordIgnored(int target, long now) => RecordUnreachable(target, now);

            /// <summary>The target reached the party: its failed tries are forgotten (a bench already set stays).</summary>
            public void Reached(int target, long now)
            {
                if (!IsBenched(target, now)) _targets.Remove(target);
            }

            public int BenchedCount(long now) => _targets.Values.Count(entry => entry.BenchedAt > 0 && now - entry.BenchedAt < BenchMilliseconds);

            /// <summary>Enough of this camp's targets are benched: the camp is given up.</summary>
            public bool ShouldGiveUpCamp(long now) => BenchedCount(now) >= BenchedTargetsBeforeCampGiveUp;

            public void Clear() => _targets.Clear();
        }

        /// <summary>
        /// 1.2.3 (#539), called by the party's camp worker before a pull: true when enough of this camp's targets had no
        /// firing point that the party should give the camp up. The bench is then cleared for the next camp.
        /// </summary>
        public static bool ShouldGiveUpCamp(Group group, string campKey, out int benched)
        {
            benched = 0;
            if (group == null) return false;
            State state = States.GetOrCreateValue(group);
            lock (state)
            {
                state.Bench.ForCamp(campKey);
                long now = GameLoop.GameLoopTime;
                benched = state.Bench.BenchedCount(now);
                if (!state.Bench.ShouldGiveUpCamp(now)) return false;
                state.Bench.Clear();
                return true;
            }
        }
        private static readonly ConditionalWeakTable<Group, State> States = new();
        private static readonly ConditionalWeakTable<GameBot, State> Holding = new();

        // Every PvE party of three or more pulls from range into the party (owner, 2026-10-06): a tank
        // walking into a large pack never works at any level. allLevelFifty is kept for callers.
        public const int MinimumPullParty = 3;
        public const float MaximumFiringAdvance = 450;
        public static bool UsesDefensivePull(bool groupPve, int memberCount, bool allLevelFifty) =>
            groupPve && memberCount >= MinimumPullParty;

        public static bool IsPullSpell(Spell spell, int level) => spell != null && spell.Level <= level &&
            spell.Target == eSpellTarget.ENEMY && spell.Range >= 1000 && spell.Radius == 0 && spell.Damage > 0 && !spell.NeedInstrument &&
            spell.SpellType is eSpellType.DirectDamage or eSpellType.Lifedrain or eSpellType.DamageOverTime;

        private static Spell PullSpell(GameBot bot) => bot.Spells?.Where(spell => IsPullSpell(spell, bot.Level))
            .OrderByDescending(spell => spell.Range).ThenByDescending(spell => spell.Level).FirstOrDefault();

        // Flying-target handoff must choose a spell that can be fired now.
        // This is deliberately separate from the established level-50 pull
        // selection so ordinary targets retain their original behavior.
        public static Spell SelectReadyPullSpell(IEnumerable<Spell> spells, int level, int mana,
            Func<Spell, int> powerCost, Func<Spell, int> disabledDuration) =>
            spells?.Where(spell => IsPullSpell(spell, level) &&
                    powerCost(spell) <= mana && disabledDuration(spell) == 0)
                .OrderByDescending(spell => spell.Range).ThenByDescending(spell => spell.Level)
                .FirstOrDefault();

        private static Spell ReadyPullSpell(GameBot bot) => bot == null ? null :
            SelectReadyPullSpell(bot.Spells, bot.Level, bot.Mana, bot.PowerCost, bot.GetSkillDisabledDuration);

        private static bool HasUsableDistanceWeapon(GameBot bot) =>
            BotRangedCombat.CanUse(bot, bot.Inventory?.GetItem(eInventorySlot.DistanceWeapon));

        public static bool HasRangedPull(GameBot bot) => bot?.CharacterClass != null &&
            !SavageBotCombatPolicy.MustMeleePull((eCharacterClass)bot.CharacterClass.ID) &&
            (PullSpell(bot) != null || BotRangedCombat.CanUse(bot, bot.Inventory?.GetItem(eInventorySlot.DistanceWeapon)));

        public static bool OwnsRangedPosition(GameNPC npc) => npc is GameBot bot && bot.Group != null &&
            States.TryGetValue(bot.Group, out State state) && state.Active && state.Shooter == bot;

        public static bool IsScopedFlyingHandoff(ushort region, GameNPC.eFlags flags,
            bool designatedHasMeleeApproach, bool groupCanPull) =>
            (region is 248 or 249 or 60 or 160 or 191 || AutonomousRealmRaid.IsRaidDungeon(region)) &&
            (flags & GameNPC.eFlags.FLYING) != 0 && !designatedHasMeleeApproach && groupCanPull;

        /// <summary>Only a DF/epic-dungeon flyer unreachable by the normal
        /// tank receives a temporary ranged group shooter.  The designated
        /// puller and all ordinary camps retain their existing behavior.</summary>
        public static bool TryBeginFlyingHandoff(GameBot designated, GameNPC target, bool corridorBlocker = false)
            => TryChooseFlyingHandoffShooter(designated, target, corridorBlocker, out GameBot shooter) &&
               TryBeginCore(shooter, target, flyingHandoff: true, out _);

        public static bool CanAttemptFlyingHandoff(GameBot designated, GameNPC target, bool corridorBlocker = false)
            => TryChooseFlyingHandoffShooter(designated, target, corridorBlocker, out _);

        private static bool TryChooseFlyingHandoffShooter(GameBot designated, GameNPC target,
            bool corridorBlocker, out GameBot shooter)
        {
            shooter = null;
            if (designated?.Group == null || target?.IsAlive != true ||
                !IsScopedFlyingHandoff(target.CurrentRegionID, target.Flags, false,
                    designated.CurrentRegion == target.CurrentRegion &&
                    AutonomousBotGroupCoordinator.CanInitiateNewPull(designated, corridorBlocker))) return false;
            var nav = PathfindingProvider.Instance;
            Zone zone = designated.CurrentZone;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
            Vector3 from = new(designated.X, designated.Y, designated.Z);
            Vector3 enemy = new(target.X, target.Y, target.Z);
            if (AutonomousDungeonTargetRoute.CanReach(nav, zone, from, enemy)) return false;
            GameBot[] members = designated.Group.GetMembersInTheGroup().OfType<GameBot>()
                .Where(member => member.IsAlive && member.Group == designated.Group &&
                    member.CurrentRegion == designated.CurrentRegion && !member.IsOnStableMasterRoute &&
                    GameServer.ServerRules.IsAllowedToAttack(member, target, true))
                .ToArray();
            if (members.Length < 2) return false;
            shooter = members.Where(member => member != designated &&
                    !SavageBotCombatPolicy.MustMeleePull((eCharacterClass)member.CharacterClass.ID) &&
                    (ReadyPullSpell(member) != null || HasUsableDistanceWeapon(member)))
                .OrderBy(member => member.GetDistanceTo(target))
                .FirstOrDefault(member =>
                {
                    Spell spell = ReadyPullSpell(member);
                    // Do not switch several candidates' equipped weapons just
                    // to inspect their range. 1000 is a conservative preflight
                    // for a stowed bow; the actual range is checked after the
                    // chosen shooter switches in TryBeginCore.
                    int range = spell?.Range ?? (member.ActiveWeaponSlot == eActiveWeaponSlot.Distance
                        ? member.attackComponent.AttackRange : 1000);
                    return TryFiringPoint(nav, member.CurrentZone,
                        new(member.X, member.Y, member.Z), enemy, range, out _);
                });
            return shooter != null;
        }

        // True means this party's pull is handled, including a failed approach.
        // Never fall through into a melee charge merely because a ranged pull failed.
        public static bool TryBegin(GameBot shooter, GameLiving target)
            => TryBeginCore(shooter, target, flyingHandoff: false, out _);

        /// <summary>
        /// Same as TryBegin, and also says why (#569 step 9: the puller's camp turn tells "too far, look for a nearer one"
        /// from "benched, close in"). The returned bool is exactly TryBegin's.
        /// </summary>
        public static bool TryBegin(GameBot shooter, GameLiving target, out PartyPullStart why)
            => TryBeginCore(shooter, target, flyingHandoff: false, out why);

        /// <summary>Pure: a target with no firing point is too far when it is beyond the short proven approach.</summary>
        public static bool IsTooFarForFiringPoint(float distance, int range) => distance - (range - 100) > MaximumFiringAdvance;

        private static bool TryBeginCore(GameBot shooter, GameLiving target, bool flyingHandoff, out PartyPullStart why)
        {
            why = PartyPullStart.Waiting;
            // Suppress only a new scripted pull of this expedition's dragon.
            // Attacked-by-enemy and shared defense paths are unchanged.
            if (AutonomousRealmRaid.IsPendingDragonTarget(shooter,target)) return true;
            if (shooter?.CharacterClass != null &&
                SavageBotCombatPolicy.MustMeleePull((eCharacterClass)shooter.CharacterClass.ID))
            {
                why = PartyPullStart.NoRangedPull;
                return false;
            }
            if (shooter?.Group == null ||
                !flyingHandoff && !AutonomousBotGroupCoordinator.UsesDefensivePullGroup(shooter.Group))
            {
                why = PartyPullStart.NotApplicable;
                return false;
            }
            State state = States.GetOrCreateValue(shooter.Group);
            lock (state)
            {
                if (state.Active) return true;
                // 1.2.3 (#539): a target benched for this camp is pulled the ordinary way, not stood on again.
                if (target != null && state.Bench.IsBenched(target.ObjectID, GameLoop.GameLoopTime))
                {
                    why = PartyPullStart.Benched;
                    return false;
                }
                if (GameLoop.GameLoopTime < state.RetryAfter) return true;
                GameBot[] members = shooter.Group.GetMembersInTheGroup().OfType<GameBot>().ToArray();
                if (members.Length < (flyingHandoff ? 2 : MinimumPullParty) ||
                    target?.IsAlive != true || shooter.CurrentRegion != target.CurrentRegion)
                    return true;
                // An enemy already in the party is a real defensive fight, not a ranged pull.
                if (!flyingHandoff && members.Any(member => member.IsWithinRadius(target, ContactRadius)))
                {
                    why = PartyPullStart.ContactFight;
                    return false;
                }
                Spell spell = flyingHandoff ? ReadyPullSpell(shooter) : PullSpell(shooter);
                // A puller with no ranged pull at all (common below level 50) pulls the ordinary way;
                // reporting "handled" here left such a party standing at its camp for good.
                if (spell == null && !flyingHandoff && !HasRangedPull(shooter))
                {
                    why = PartyPullStart.NoRangedPull;
                    return false;
                }
                state.RetryAfter = GameLoop.GameLoopTime + TimeoutMilliseconds;
                eActiveWeaponSlot previous = shooter.ActiveWeaponSlot;
                if (spell == null)
                {
                    if (flyingHandoff && !HasUsableDistanceWeapon(shooter)) return true;
                    shooter.SwitchWeapon(eActiveWeaponSlot.Distance);
                }
                int range = spell?.Range ?? shooter.attackComponent.AttackRange;
                Vector3 origin = new(shooter.X, shooter.Y, shooter.Z), enemy = new(target.X, target.Y, target.Z);
                if (!TryFiringPoint(PathfindingProvider.Instance, shooter.CurrentZone, origin, enemy, range, out Vector3 point))
                {
                    shooter.SwitchWeapon(previous);
                    // Merely too far for the short proven approach: the party keeps walking in and the
                    // camp check (every 1.5 s) pulls once in range. Holding here stood parties 2,000
                    // units from their camp (1,087 unreachable pulls in run 11's first hour, mostly
                    // low-level parties with short pull ranges).
                    if (IsTooFarForFiringPoint(Vector3.Distance(origin, enemy), range))
                    {
                        state.RetryAfter = 0;
                        why = PartyPullStart.TooFar;
                        return false;
                    }
                    bool benched = state.Bench.RecordUnreachable(target.ObjectID, GameLoop.GameLoopTime);
                    var log = LoggerManager.Create(typeof(AutonomousDefensivePull));
                    log.Info($"AUTONOMOUS_DEFENSIVE_PULL_UNREACHABLE bot={shooter.Name} target={target.Name} region={shooter.CurrentRegionID} " +
                             $"x={shooter.X} y={shooter.Y} z={shooter.Z} tries={state.Bench.Tries(target.ObjectID)}");
                    if (!benched)
                    {
                        why = PartyPullStart.Unreachable;
                        return true;
                    }
                    // Benched: no more waiting on this ranged pull; the caller pulls it the ordinary way now.
                    state.RetryAfter = 0;
                    log.Warn($"AUTONOMOUS_DEFENSIVE_PULL_BENCHED bot={shooter.Name} target={target.Name} targetId={target.ObjectID} " +
                             $"camp=\"{state.Bench.CampKey}\" benched={state.Bench.BenchedCount(GameLoop.GameLoopTime)} " +
                             $"region={shooter.CurrentRegionID} x={shooter.X} y={shooter.Y} z={shooter.Z}; " +
                             $"no firing point {UnreachableBench.TriesBeforeBench} times, an ordinary pull instead");
                    why = PartyPullStart.Benched;
                    return false;
                }
                state.Members = members;
                state.Shooter = shooter;
                state.Target = target;
                state.Anchor = origin;
                state.FiringPoint = point;
                state.Spell = spell;
                state.PreviousWeapon = previous;
                state.Shot = false;
                state.Until = GameLoop.GameLoopTime + TimeoutMilliseconds;
                state.NextMove = 0;
                state.Active = true;
                foreach (GameBot member in members)
                {
                    member.WakeRecoveryRest();
                    Holding.Remove(member);
                    Holding.Add(member, state);
                    member.StopAttack();
                    member.StopFollowing();
                    member.StopMovingOnPath();
                    member.StopMoving();
                    var visited = new HashSet<IControlledBrain>();
                    foreach (GameNPC pet in BotGroupPetBuffTargets.AttachedTree(member.ControlledBrain, member, visited))
                        if (pet.Brain is IControlledBrain brain && state.Pets.TryAdd(brain, brain.AggressionState))
                        {
                            brain.Disengage();
                            brain.SetAggressionState(eAggressionState.Passive);
                            brain.FollowOwner();
                        }
                }
                why = PartyPullStart.Started;
                return true;
            }
        }

        public static bool TryFiringPoint(IPathfindingMgr nav, Zone zone, Vector3 origin, Vector3 target,
            int range, out Vector3 point)
        {
            point = origin;
            if (nav?.IsAvailable != true || zone == null || range < 1000) return false;
            float distance = Vector3.Distance(origin, target);
            float advance = Math.Max(0, distance - (range - 100));
            // No solo expedition into a pack: at most a short, proven approach from the party.
            if (advance > MaximumFiringAdvance) return false;
            Vector3 desired = advance == 0 ? origin : Vector3.Lerp(origin, target, advance / distance);
            if (!AutonomousNavigationSurface.TryFloor(nav, zone, desired, out point)) return false;
            return Vector3.Distance(origin, point) <= 450 && Vector3.Distance(point, target) <= range - 50 &&
                nav.HasLineOfSight(zone, point, target, nav.DefaultFilters) &&
                AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, origin, point) &&
                AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, point, origin);
        }

        public static bool Hold(GameBot bot)
        {
            if (bot == null || !Holding.TryGetValue(bot, out State state)) return false;
            lock (state)
            {
                if (!state.Active) return false;
                bool broken = bot.Group == null || !state.Target.IsAlive || state.Target.ObjectState != GameObject.eObjectState.Active ||
                    state.Members.Any(member => !member.IsAlive || member.Group != bot.Group ||
                        member.CurrentRegion != state.Shooter.CurrentRegion || member.IsOnStableMasterRoute) ||
                    state.Target.CurrentRegion != bot.CurrentRegion;
                if (broken || GameLoop.GameLoopTime >= state.Until)
                {
                    // A live target that never reached the party in the whole window is a failed pull (DividedRealms
                    // 1d18f864): the second benches it and the party pulls it the ordinary way.
                    if (!broken) RecordIgnoredPull(state);
                    Finish(state, false);
                    return false;
                }
                if (state.Members.Any(member => member.IsWithinRadius(state.Target, ContactRadius)))
                {
                    state.Bench.Reached(state.Target.ObjectID, GameLoop.GameLoopTime);
                    Finish(state, true);
                    return false;
                }
                if (bot != state.Shooter) return true;
                Vector3 destination = state.Shot ? state.Anchor : state.FiringPoint;
                if (Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), destination) > 45 * 45)
                {
                    if (!bot.IsCasting && GameLoop.GameLoopTime >= state.NextMove)
                    {
                        state.NextMove = GameLoop.GameLoopTime + 1500;
                        bot.PathTo(destination, bot.MaxSpeed);
                    }
                    return true;
                }
                if (bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true) return true;
                bot.StopMovingOnPath();
                bot.StopMoving();
                if (state.Shot || bot.IsCrowdControlled) return true;
                int range = state.Spell?.Range ?? bot.attackComponent.AttackRange;
                if (!bot.IsWithinRadius(state.Target, range - 50) ||
                    !PathfindingProvider.Instance.HasLineOfSight(bot.CurrentZone, new(bot.X, bot.Y, bot.Z),
                        new(state.Target.X, state.Target.Y, state.Target.Z), PathfindingProvider.Instance.DefaultFilters)) return true;
                bot.TargetObject = state.Target;
                if (state.Spell != null)
                {
                    if (bot.Mana >= bot.PowerCost(state.Spell) && bot.GetSkillDisabledDuration(state.Spell) == 0)
                        bot.CastSpell(state.Spell, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells));
                }
                else if (!bot.IsAttacking && bot.Endurance >= bot.rangeAttackComponent.ShotEnduranceCost)
                    bot.StartAttack(state.Target);
                return true;
            }
        }

        public static void OnAttack(GameLiving actor, AttackData attack)
        {
            if (actor?.Group == null || !States.TryGetValue(actor.Group, out State state)) return;
            lock (state)
            {
                if (!state.Active || state.Shooter != actor || attack?.Attacker != actor ||
                    attack.Target != state.Target || !attack.CausesCombat) return;
                state.Shot = true;
                state.Shooter.StopAttack();
            }
        }

        public static void OnThreat(GameLiving member, GameLiving attacker)
        {
            if (member?.Group == null || !States.TryGetValue(member.Group, out State state)) return;
            lock (state)
                if (state.Active && (attacker != state.Target || member.IsWithinRadius(attacker, ContactRadius)))
                    Finish(state, attacker == state.Target);
        }

        /// <summary>This party's ranged pull of this target is under way (not a retry wait).</summary>
        public static bool IsPulling(Group group, GameLiving target)
        {
            if (group == null || target == null || !States.TryGetValue(group, out State state)) return false;
            lock (state) return state.Active && state.Target == target;
        }

        public static void Cancel(Group group)
        {
            if (group == null || !States.TryGetValue(group, out State state)) return;
            lock (state) if (state.Active) Finish(state, false);
        }

        private static void RecordIgnoredPull(State state)
        {
            if (state.Target == null || state.Shooter == null) return;
            bool benched = state.Bench.RecordIgnored(state.Target.ObjectID, GameLoop.GameLoopTime);
            var log = LoggerManager.Create(typeof(AutonomousDefensivePull));
            if (log.IsInfoEnabled)
                log.Info($"AUTONOMOUS_DEFENSIVE_PULL_IGNORED bot={state.Shooter.Name} target={state.Target.Name} shot={state.Shot} " +
                     $"tries={state.Bench.Tries(state.Target.ObjectID)} distance={(int)state.Shooter.GetDistanceTo(state.Target)} " +
                     $"action={(benched ? "ordinary-pull" : "retry")} region={state.Shooter.CurrentRegionID} " +
                     $"x={state.Shooter.X} y={state.Shooter.Y} z={state.Shooter.Z}");
            // Benched: TryBegin checks the bench before the retry wait, so the caller pulls it the ordinary way at once.
        }

        private static void Finish(State state, bool engage)
        {
            state.Active = false;
            state.RetryAfter = GameLoop.GameLoopTime + 10_000;
            state.Shooter.StopAttack();
            state.Shooter.SwitchWeapon(state.PreviousWeapon);
            foreach (var pet in state.Pets)
                if (pet.Key.AggressionState == eAggressionState.Passive) pet.Key.SetAggressionState(pet.Value);
            state.Pets.Clear();
            foreach (GameBot member in state.Members)
            {
                Holding.Remove(member);
                if (member.Brain is BotBrain brain)
                {
                    if (engage && member.IsAlive && member.Group == state.Shooter.Group &&
                        member.CurrentRegion == state.Target.CurrentRegion)
                    {
                        brain.AddToAggroList(state.Target, Math.Max(25, state.Target.EffectiveLevel * 10));
                        brain.CommitDungeonPull(state.Target);
                        brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    }
                    else brain.RemoveFromAggroList(state.Target);
                }
            }
        }
    }
}
