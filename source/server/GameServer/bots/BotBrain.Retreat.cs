using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// From Offline DAoC True 1.65 (shadowofze; its veteran review M6, 2026-11-11): the brain side of knowing when to leave (BotGroupRetreat has the rules).
    /// A party member under its party's RUN order, a solo gamebot bailing out of a fight it is losing badly, and a hired
    /// healer whose player died in a lost fight all stop fighting, drop their targets, put their pets on follow, sprint,
    /// use an escape realm ability and run to the plan's point. Routes: one request when the run starts (a party member
    /// may ask once more if it stopped short); a straight walk otherwise. Performers keep their speed song up.
    /// A solo caster also takes one straight step back after a root or snare lands on its own monster (owner O3: solo
    /// only, at most once per 20 s, no route request).
    /// </summary>
    public partial class BotBrain
    {
        private sealed class PersonalRetreat
        {
            private static long _nextId = long.MaxValue / 2;
            public long Id { get; } = System.Threading.Interlocked.Increment(ref _nextId);
            public ushort Region;
            public Vector3 Point;
            public long Until;
            public long EarliestEnd;
            public string Kind;
            public GameLiving Corpse;
        }

        private PersonalRetreat _personalRetreat;
        private long _activeRetreatId;
        private string _activeRetreatKind;
        private int _retreatRoutesLeft;
        private long _nextRetreatMoveTick;
        private long _nextRetreatSpeedTick;
        private List<(IControlledBrain Brain, eAggressionState State)> _retreatPets;
        private Vector3 _lastPeacePosition;
        private bool _soloBailUsedThisFight;
        private long _nextSoloBailLook;
        private long _nextSurviveLook;
        private GameLiving _survivedForCorpse;
        private long _lastStepBackTick = -BotGroupRetreat.StepBackMilliseconds;
        private long _stepBackUntil;

        /// <summary>Solo bail-out: the run lasts at most this long, at least <see cref="SoloBailMinimumMilliseconds"/>.</summary>
        public const long SoloBailMilliseconds = 15_000;
        public const long SoloBailMinimumMilliseconds = 4_000;
        public const int SoloBailDistance = 2_500;
        /// <summary>A companion healer surviving to raise runs at most this long.</summary>
        public const long SurviveMilliseconds = 20_000;

        /// <summary>True while a retreat owns this bot's turn (called early in Think, after /passive).</summary>
        private bool TryHoldRetreat()
        {
            GameBot bot = BotBody;
            if (bot == null || !BotGroupRetreat.Enabled || !bot.IsAlive)
            {
                EndRetreat();
                return false;
            }
            long now = GameLoop.GameLoopTime;
            if (!bot.InCombat && !bot.IsAttacking)
            {
                _lastPeacePosition = new(bot.X, bot.Y, bot.Z);
                _soloBailUsedThisFight = false;
            }
            // The party RUN reads the group's fight board (AutonomousBotGroupCoordinator.CheckGroupRun only peeks at it,
            // under the coordinator lock). In True 1.65 add control, roots and pets rebuild it every think; 1.2 keeps its
            // own versions of those, so a fighting gamebot party member keeps it fresh here (one rebuild and one area
            // search per group per second at most, outside any lock).
            if (bot.Group != null && (bot.InCombat || bot.IsAttacking) && bot.IsAutonomousWorldBot &&
                !bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper)
                FightBoard();

            long id;
            Vector3 point;
            string kind;
            BotGroupRetreat.Order order = bot.IsAutonomousWorldBot && !bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper
                ? BotGroupRetreat.Active(bot.Group, now)
                : null;
            if (order != null && order.Region == bot.CurrentRegionID)
            {
                id = order.Id;
                point = order.Point;
                kind = "party";
            }
            else if (CurrentPersonalRetreat(bot, now) is { } personal)
            {
                id = personal.Id;
                point = personal.Point;
                kind = personal.Kind;
            }
            else
            {
                EndRetreat();
                return false;
            }

            if (id != _activeRetreatId)
                BeginRetreat(bot, id, point, kind);
            KeepRetreating(bot, point, now);
            return true;
        }

        private PersonalRetreat CurrentPersonalRetreat(GameBot bot, long now)
        {
            PersonalRetreat plan = _personalRetreat;
            if (plan != null)
            {
                bool over = now >= plan.Until || plan.Region != bot.CurrentRegionID ||
                    now >= plan.EarliestEnd && plan.Kind switch
                    {
                        "solo" => !bot.InCombat,
                        "survive" => plan.Corpse is not { IsAlive: false, ObjectState: GameObject.eObjectState.Active } ||
                                     (FightBoard()?.MonstersOnGroup ?? 0) == 0,
                        _ => true,
                    };
                if (!over)
                    return plan;
                _personalRetreat = null;
            }
            plan = ConsiderSoloBail(bot, now) ?? ConsiderSurvivingToRez(bot, now);
            _personalRetreat = plan;
            return plan;
        }

        /// <summary>A solo gamebot losing badly with no heal or crowd control ready leaves, once per fight.</summary>
        private PersonalRetreat ConsiderSoloBail(GameBot bot, long now)
        {
            if (!bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper || bot.Group != null || !bot.InCombat ||
                _soloBailUsedThisFight || bot.HealthPercent >= 50 || now < _nextSoloBailLook || bot.IsIncapacitated)
                return null;
            _nextSoloBailLook = now + 500;
            GameLiving target = bot.TargetObject as GameLiving;
            int attackers = bot.attackComponent?.AttackerTracker.Count ?? 0;
            if (!BotGroupRetreat.SoloShouldBail(bot.HealthPercent, target?.IsAlive == true ? target.HealthPercent : 0,
                    attackers, SpellReady(bot, bot.HealSpells, bot.InstantHealSpells, IsSelfHeal),
                    SpellReady(bot, bot.CrowdControlSpells, bot.InstantCrowdControlSpells, _ => true)))
                return null;
            _soloBailUsedThisFight = true;
            Vector3 here = new(bot.X, bot.Y, bot.Z);
            Vector3 enemy = target != null ? new(target.X, target.Y, target.Z) : here;
            // M6 / M11: to the realm's bindstone close by, else the camp's safe spot worked out ahead, else back the way
            // it came (from the monster through the spot where the fight found it); one route request either way
            Vector3 point = BotGroupRetreat.SafePoint(here,
                BotGroupRetreat.NearestBind(bot.CurrentRegionID, bot.X, bot.Y, bot.Realm),
                AutonomousWorldBotController.TryCampSafeSpot(_autonomousWorldController?.CurrentCampId, bot.CurrentRegionID,
                    out Vector3 campSafe) ? campSafe : null,
                enemy, BotGroupRetreat.RetreatPoint(here, _lastPeacePosition, enemy, SoloBailDistance));
            log.Info($"BOT_SOLO_BAIL bot=\"{bot.Name}\" level={bot.Level} class=\"{bot.ClassName}\" health={bot.HealthPercent} " +
                     $"target=\"{target?.Name}\" targetHealth={target?.HealthPercent} attackers={attackers} " +
                     $"to={bot.CurrentRegionID}:{(int)point.X},{(int)point.Y}");
            return new PersonalRetreat
            {
                Region = bot.CurrentRegionID, Point = point, Kind = "solo",
                Until = now + SoloBailMilliseconds, EarliestEnd = now + SoloBailMinimumMilliseconds,
            };
        }

        /// <summary>A hired healer whose player died in a lost fight runs, then comes back to raise.</summary>
        private PersonalRetreat ConsiderSurvivingToRez(GameBot bot, long now)
        {
            if (!bot.IsPlayerLedGroup || bot.ResurrectionSpell == null || now < _nextSurviveLook ||
                AssistedPlayer is not GamePlayer player)
                return null;
            _nextSurviveLook = now + 1_000;
            if (player.IsAlive)
            {
                _survivedForCorpse = null;
                return null;
            }
            if (_survivedForCorpse == player || player.CurrentRegionID != bot.CurrentRegionID)
                return null;
            GroupFightBoard board = FightBoard();
            int fighters = 0;
            foreach (GameLiving member in board?.Members ?? [])
                if (member != bot && member.IsAlive && !(member is GameBot { ResurrectionSpell: not null } other &&
                        BotPartyRoles.IsHealingClass((eCharacterClass)other.CharacterClass.ID)))
                    fighters++;
            if (!BotGroupRetreat.CompanionHealerSurvives(true, true, board?.MonstersOnGroup ?? 0, fighters))
                return null;
            _survivedForCorpse = player;
            Vector3 here = new(bot.X, bot.Y, bot.Z);
            Vector3 monsters = here;
            Vector3 sum = Vector3.Zero;
            int counted = 0;
            foreach (GameNPC npc in board.NpcsOnGroup)
            {
                sum += new Vector3(npc.X, npc.Y, npc.Z);
                counted++;
            }
            if (counted > 0)
                monsters = sum / counted;
            Vector3 point = BotGroupRetreat.RetreatPoint(here, null, monsters, SoloBailDistance);
            log.Info($"COMPANION_SURVIVE_TO_REZ bot=\"{bot.Name}\" player=\"{player.Name}\" monsters={board.MonstersOnGroup} " +
                     $"fighters={fighters} region={bot.CurrentRegionID}");
            return new PersonalRetreat
            {
                Region = bot.CurrentRegionID, Point = point, Kind = "survive", Corpse = player,
                Until = now + SurviveMilliseconds, EarliestEnd = now + SoloBailMinimumMilliseconds,
            };
        }

        private static bool IsSelfHeal(Spell spell) =>
            spell.SpellType is eSpellType.Heal or eSpellType.CombatHeal or eSpellType.HealOverTime or eSpellType.SpreadHeal;

        private static bool SpellReady(GameBot bot, List<Spell> spells, List<Spell> instant, Func<Spell, bool> wanted)
        {
            foreach (List<Spell> list in new[] { spells, instant })
            {
                if (list == null)
                    continue;
                try
                {
                    foreach (Spell spell in list)
                        if (spell != null && spell.Level <= bot.Level && wanted(spell) && bot.GetSkillDisabledDuration(spell) <= 0 &&
                            bot.Mana >= bot.PowerCost(spell))
                            return true;
                }
                catch (InvalidOperationException) { }   // a respec cleared the list mid-scan
            }
            return false;
        }

        private void BeginRetreat(GameBot bot, long id, Vector3 point, string kind)
        {
            EndRetreat();
            _activeRetreatId = id;
            _activeRetreatKind = kind;
            if (bot.IsRecoveryResting)
                bot.WakeRecoveryRest();
            DropTheFight(bot);
            _retreatPets = new();
            var visited = new HashSet<IControlledBrain>();
            foreach (GameNPC pet in BotGroupPetBuffTargets.AttachedTree(Body.ControlledBrain, Body, visited))
                if (pet.Brain is IControlledBrain petBrain)
                {
                    _retreatPets.Add((petBrain, petBrain.AggressionState));
                    if (petBrain is StandardMobBrain { HasAggro: true } petAggro)
                        petAggro.ClearAggroList();
                    petBrain.SetAggressionState(eAggressionState.Passive);   // disengage and follow the owner
                }
            // (True 1.65 also fires Vanish or Speed of Sound here; Living Server's gamebots don't use those actives)
            // one route request when the run starts; a party member may ask once more if it stops short
            _retreatRoutesLeft = kind == "party" ? 1 : 0;
            _nextRetreatMoveTick = GameLoop.GameLoopTime + 3_000;
            bot.StopFollowing();
            bot.PathTo(point, bot.MaxSpeed);
        }

        private void DropTheFight(GameBot bot)
        {
            if (bot.IsAttacking)
                bot.StopAttack();
            if (bot.IsCasting && bot.castingComponent?.SpellHandler?.Spell is { } casting &&
                casting.SpellType != eSpellType.SpeedEnhancement)
                bot.StopCurrentSpellcast();
            if (HasAggro)
                ClearAggroList();
            if (bot.TargetObject is GameLiving)
                bot.TargetObject = null;
        }

        private void KeepRetreating(GameBot bot, Vector3 point, long now)
        {
            DropTheFight(bot);
            if (!bot.IsSprinting && bot.EndurancePercent > 10 && bot.Sprint(true))
                bot.StartEnduranceRegeneration();
            TryRetreatSpeed(bot, now);
            if (bot.IsWithinRadius(new Point3D((int)point.X, (int)point.Y, (int)point.Z), 250) || bot.IsMoving ||
                now < _nextRetreatMoveTick)
                return;
            _nextRetreatMoveTick = now + 3_000;
            if (_retreatRoutesLeft-- > 0)
                bot.PathTo(point, bot.MaxSpeed);
            else
                bot.WalkTo(point, bot.MaxSpeed);   // no route request: straight on
        }

        /// <summary>A performer keeps its speed song up while running.</summary>
        private void TryRetreatSpeed(GameBot bot, long now)
        {
            if (now < _nextRetreatSpeedTick || bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true)
                return;
            _nextRetreatSpeedTick = now + 2_000;
            Spell speed = null;
            foreach (List<Spell> list in new[] { bot.MiscSpells, bot.InstantMiscSpells })
                if (list != null)
                    foreach (Spell spell in list)
                        if (spell is { IsPulsing: true, IsHarmful: false, SpellType: eSpellType.SpeedEnhancement } &&
                            spell.Level <= bot.Level && bot.GetSkillDisabledDuration(spell) <= 0 &&
                            bot.Mana >= bot.PowerCost(spell) && (speed == null || spell.Level > speed.Level))
                            speed = spell;
            if (speed == null)
                return;
            foreach (ECSPulseEffect pulse in Body.effectListComponent.GetPulseEffects())
                if (!pulse.IsEnding && !pulse.IsEnded && pulse.SpellHandler?.Spell?.SpellType == eSpellType.SpeedEnhancement)
                    return;
            if (speed.NeedInstrument && !TryEquipRealInstrument(bot, speed.InstrumentRequirement))
                return;
            GameObject previous = bot.TargetObject;
            bot.TargetObject = bot;
            bot.CastSpell(speed, m_mobSpellLine, false);
            bot.TargetObject = previous;
        }

        private void EndRetreat()
        {
            if (_activeRetreatId == 0)
                return;
            _activeRetreatId = 0;
            GameBot bot = BotBody;
            if (_retreatPets != null)
                foreach ((IControlledBrain brain, eAggressionState state) in _retreatPets)
                    if (brain?.Body?.IsAlive == true && brain.AggressionState == eAggressionState.Passive &&
                        state != eAggressionState.Passive)
                        brain.SetAggressionState(state);
            _retreatPets = null;
            if (bot?.IsSprinting == true)
                bot.Sprint(false);
            if (bot != null && log.IsInfoEnabled)
                log.Info($"BOT_RETREAT_ENDED bot=\"{bot.Name}\" kind={_activeRetreatKind} alive={bot.IsAlive} health={bot.HealthPercent}");
        }

        /// <summary>
        /// O3 / M6 caster step-back: a solo caster whose own monster is rooted or snared close by takes one straight step
        /// back to nuke range (at most once per 20 s, no route request). True while the step owns the turn.
        /// </summary>
        private bool TryCasterStepBack()
        {
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (now < _stepBackUntil)
            {
                if (bot.IsMoving)
                    return true;
                _stepBackUntil = 0;
            }
            if (!BotGroupRetreat.Enabled || bot == null || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper ||
                bot.Group != null || bot.IsCasting || bot.IsIncapacitated ||
                Body.TargetObject is not GameNPC target || target is GameBot || !target.IsAlive)
                return false;
            bool rootedOrSnared = IsBreakablyControlled(target) && !target.IsMezzed ||
                target.effectListComponent?.ContainsEffectForEffectType(eEffect.MovementSpeedDebuff) == true;
            if (!BotGroupRetreat.CasterStepsBack(true, PrefersCurrentSpellRange(), rootedOrSnared,
                    (int)bot.GetDistanceTo(target), now - _lastStepBackTick))
                return false;
            _lastStepBackTick = now;
            int standOff = BotGroupRetreat.StepBackStandOff(SpellApproachRange());
            Vector2 away = new(bot.X - target.X, bot.Y - target.Y);
            if (away.LengthSquared() < 1)
                away = new(1, 0);
            Vector2 spot = new Vector2(target.X, target.Y) + Vector2.Normalize(away) * standOff;
            Vector3 from = new(bot.X, bot.Y, bot.Z), to = new(spot.X, spot.Y, bot.Z);
            if (!PathfindingProvider.Instance.HasLineOfSight(Body.CurrentZone, from, to, PathfindingProvider.Instance.DefaultFilters))
                return false;   // a wall behind: stay and fight (no route request)
            float walk = Vector2.Distance(new(from.X, from.Y), spot);
            if (bot.IsAttacking)
                bot.StopAttack();
            bot.WalkTo(to, bot.MaxSpeed);
            _stepBackUntil = now + (long)(walk * 1000 / Math.Max(1, (int)bot.MaxSpeed)) + 250;
            return true;
        }
    }
}
