using System;
using System.Linq;
using DOL.GS;
using DOL.GS.ServerProperties;
using DOL.GS.Styles;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Open-field RvR tactics, work package 0 (docs/RVR-FIELD-1.2.md): the brain's hook points. Each hook is called by
    /// one line at its site in the brain and is a stub here: it asks <see cref="RvrTacticsActive"/> (the master switch
    /// rvr_bot_field_tactics, a battle region, and <see cref="RvrTacticsScope"/>) and its own switch, and then has no
    /// opinion, so the code at the site runs exactly as before. The later packages fill their own hook:
    /// <list type="bullet">
    /// <item>work package 2: <see cref="RvrTacticsTarget"/>, <see cref="RvrTacticsCrowdControl"/>, <see cref="TryRvrTacticsSupport"/></item>
    /// <item>work package 3: <see cref="RvrTacticsPeel"/>, <see cref="RvrTacticsProtectionTarget"/>, <see cref="TryRvrTacticsMove"/>, <see cref="TryRvrTacticsSupport"/> (a healer's kite)</item>
    /// <item>work package 4: the stance, through the plan (<see cref="RvrPlan"/>) the other hooks read</item>
    /// <item>work package 6: <see cref="RvrTacticsAllowsStealthOpener"/> (and <see cref="TryRvrTacticsMove"/> for the opener itself)</item>
    /// </list>
    /// The world controller's and the group coordinator's hooks are in AutonomousWorldBotController.RvrTactics.cs and
    /// AutonomousBotGroupCoordinator.RvrTactics.cs. A hook that does real work wraps it in
    /// AiProfile.Scope(AiSystem.RvrTactics); the stubs are not timed (two timestamps would cost more than they do).
    /// The fight board is built only when a hook asks for it (<see cref="RvrBoard"/>); no stub does.
    /// </summary>
    public partial class BotBrain
    {
        private long _rvrTacticsGateTick = long.MinValue;
        private bool _rvrTacticsGate;

        /// <summary>
        /// The gate every hook passes first: the master switch is on, and this bot is an autonomous gamebot in a battle
        /// region that <see cref="RvrTacticsScope"/> allows. The answer is kept for the rest of the game-loop tick, so
        /// the hooks of one think share one scope check.
        /// </summary>
        public bool RvrTacticsActive()
        {
            if (!Properties.RVR_BOT_FIELD_TACTICS)
                return false;
            long now = GameLoop.GameLoopTime;
            if (now == _rvrTacticsGateTick)
                return _rvrTacticsGate;
            _rvrTacticsGate = RvrTacticsApplyTo(BotBody);
            _rvrTacticsGateTick = now;
            return _rvrTacticsGate;
        }

        /// <summary>
        /// Whether the open-field tactics apply to this bot now, checked afresh: the master switch, an autonomous gamebot
        /// that no player has, in the frontier, a battleground or a neutral battle dungeon, and in
        /// <see cref="RvrTacticsScope"/> (no raid, battleground assault, keep fight, siege job or relic carrier).
        /// </summary>
        public static bool RvrTacticsApplyTo(GameBot bot) =>
            Properties.RVR_BOT_FIELD_TACTICS && bot?.IsAutonomousWorldBot == true && !bot.IsPlayerLedGroup &&
            !bot.IsTemporaryGroupHelper && !bot.IsHiredCompanion && AutonomousWorldBotController.IsBattleground(bot) &&
            RvrTacticsScope.Allows(bot);

        /// <summary>
        /// This bot's group's fight board, at most a second old, or null when the tactics do not apply to the bot. Asking
        /// builds it when it is stale (one area search per group and second). Not for use under the coordinator's lock.
        /// </summary>
        public RvrFightBoard RvrBoard() => RvrTacticsActive() ? RvrFightBoards.Get(Body) : null;

        /// <summary>The group's fight plan (see <see cref="RvrBoard"/>), or null when the tactics do not apply to the bot.</summary>
        public RvrFightPlan RvrPlan() => RvrBoard()?.Plan;

        /// <summary>The group's plan if a board was built in the last two seconds, else null. Never builds.</summary>
        public RvrFightPlan PeekRvrPlan() => RvrTacticsActive() ? RvrFightBoards.PeekPlan(Body) : null;

        // ---- work package 2: assist, crowd control, interrupts ------------------------------------------------------

        #region work package 2: assist, crowd control, interrupts (rvr_bot_assist, rvr_bot_cc_plan, rvr_bot_interrupts)

        // The member's side of the plan (docs/RVR-FIELD-1.2.md "Hands"): its own delay to follow a call, whether it
        // tunnels this fight, its crowd-control subject and its interrupt subject with the look cadence. Per-member state
        // lives here, in the member's own brain (ARCHITECTURE.md, threading). Rules: RvrAssist, RvrCrowdControlPlan,
        // RvrInterruptPlan (pure, tested).
        private GameLiving _rvrCallSeen;
        private long _rvrCallSeenAt;
        private long _rvrLastCallSeenAt = long.MinValue / 2;
        private long _rvrFightId;
        private bool _rvrTunnels;
        private long _rvrFollowAt;
        private bool _rvrCallAggroAdded;
        private bool _rvrSwitchLogged;
        private long _rvrLastSwitchLogAt = long.MinValue / 2;
        private GameLiving _rvrInterruptSubject;
        private bool _rvrInterruptAggroAdded;
        private long _rvrInterruptLookAt;
        private bool _rvrInterruptSawCasting;
        private long _rvrLastInterruptLogAt = long.MinValue / 2;
        private GameLiving _rvrCcSubject;
        private int _rvrCcCasts;
        private long _rvrLastCcLogAt = long.MinValue / 2;

        /// <summary>An enemy player, bot or pet of theirs (the kind of target the plan is about).</summary>
        private bool RvrPvpEnemy(GameLiving living) =>
            living != null && living.Realm != Body.Realm && living.Realm != eRealm.None && BotPvpCrowdControl.PlayerLike(living);

        /// <summary>The bot is in an open-field fight: it has a player-like enemy in hand, or its group has a fresh plan.</summary>
        private bool RvrInPvpFight(GameLiving picked, GameLiving attacker) =>
            RvrPvpEnemy(picked) || RvrPvpEnemy(attacker) || RvrPvpEnemy(Body.TargetObject as GameLiving) || PeekRvrPlan() != null;

        private RvrSkillTier RvrTier(RvrFightPlan plan) => plan?.SkillTier ?? RvrSkillTier.Average;

        private double RvrBoldness() => AutonomousRvrPerception.Boldness(
            AutonomousBotPersonality.Applies(BotBody) ? AutonomousBotPersonality.For(BotBody) : null);

        private long RvrGroupLogId()
        {
            GameLiving leader = Body.Group?.LivingLeader ?? Body;
            return leader is GameBot bot ? bot.DatabaseID : leader?.ObjectID ?? 0;
        }

        /// <summary>
        /// Whether this bot may go for a planned enemy now: alive, visible, a legal enemy, not held by a group mate's
        /// crowd control, near enough, and not one it gave up on (IgnoresTarget: unreachable or stalemated, which it
        /// reports so the group never calls it again).
        /// </summary>
        private bool RvrMayGoFor(GameLiving enemy, long now)
        {
            if (enemy == null || !enemy.IsAlive || enemy.ObjectState != GameObject.eObjectState.Active || enemy.IsStealthed)
                return false;
            if (_autonomousWorldController?.IgnoresTarget(enemy) == true)
            {
                RvrFieldReader.ReportUnreachable(RvrFightBoards.KeyOf(Body), enemy, now);
                return false;
            }
            return BotSiegeRuntime.LegalEnemy(Body, enemy) && !BotPvpCrowdControl.Protected(Body, enemy) &&
                Body.IsWithinRadius(enemy, RvrAssist.CallRadius + 500);
        }

        /// <summary>
        /// Hook: AttackMostWanted, right after the target pick. The target the group's plan wants this bot on, or null
        /// for no opinion (the pick stands). <paramref name="picked"/> is what the brain chose;
        /// <paramref name="protectionTarget"/> and <paramref name="directAttacker"/> are the two it ranks first. A tank
        /// pulling an enemy off a group mate keeps doing so (work package 3's ground). Otherwise an interrupter goes for
        /// its caster, and a member follows the call after its own delay unless it tunnels this fight.
        /// </summary>
        public GameLiving RvrTacticsTarget(GameLiving picked, GameLiving protectionTarget, GameLiving directAttacker)
        {
            if (!RvrTacticsActive() || !(Properties.RVR_BOT_ASSIST || Properties.RVR_BOT_INTERRUPTS))
                return null;
            if (Body.CurrentRegion == null || protectionTarget != null || !RvrInPvpFight(picked, directAttacker))
                return null;
            // AI_PROFILE: the plan read (and the board build when it is a second old), the member's follow decision.
            using var aiProfile = AiProfile.Scope(AiSystem.RvrTactics);
            RvrFightPlan plan = RvrPlan();
            if (plan == null)
                return null;
            long now = GameLoop.GameLoopTime;
            if (Properties.RVR_BOT_INTERRUPTS && RvrInterruptTarget(plan, now) is { } subject)
                return subject;
            return Properties.RVR_BOT_ASSIST ? RvrAssistTarget(plan, now) : null;
        }

        private GameLiving RvrAssistTarget(RvrFightPlan plan, long now)
        {
            GameLiving called = plan.CalledTarget;
            if (called == null)
                return null;
            RvrSkillTier tier = RvrTier(plan);
            bool mainAssist = ReferenceEquals(plan.MainAssist, Body);
            if (!ReferenceEquals(called, _rvrCallSeen))
            {
                long fight = RvrAssist.FightId(_rvrFightId, _rvrLastCallSeenAt, plan.CalledAt);
                if (fight != _rvrFightId)
                {
                    _rvrFightId = fight;
                    _rvrTunnels = RvrAssist.Tunnels(tier, BotBody.DatabaseID, fight, RvrBoldness());
                }
                _rvrCallSeen = called;
                _rvrCallSeenAt = plan.CalledAt;
                // Bots as people (WP7): the member's reactions facet moves its delay inside the group tier's band (D5).
                _rvrFollowAt = mainAssist ? now : plan.CalledAt + BotSkillHands.RvrFollowDelay(BotBody, tier,
                    RvrAssist.FollowDelay(tier, BotBody.DatabaseID, plan.CalledAt));
                _rvrCallAggroAdded = false;
                _rvrSwitchLogged = false;
                LogRvrCall(plan, now);
            }
            _rvrLastCallSeenAt = now;
            // The main assist never tunnels: his target is the call.
            if (_rvrTunnels && !mainAssist)
            {
                LogRvrSwitch(called, now - _rvrCallSeenAt, false, tier, now);
                return null;
            }
            if (now < _rvrFollowAt || !RvrMayGoFor(called, now))
                return null;
            if (!_rvrCallAggroAdded)
            {
                // As protection targets are (BotBrain.AttackMostWanted): the call goes onto the aggro list with a high weight.
                AddToAggroList(called, RvrAssist.CalledAggro);
                _rvrCallAggroAdded = true;
            }
            LogRvrSwitch(called, Math.Max(0, _rvrFollowAt - _rvrCallSeenAt), true, tier, now);
            return called;
        }

        /// <summary>The interrupter's subject, as it last looked (every 1.5-3 s by tier), when it may go for it now.</summary>
        private GameLiving RvrInterruptTarget(RvrFightPlan plan, long now)
        {
            RvrInterruptLook(plan, now);
            GameLiving subject = _rvrInterruptSubject;
            if (subject == null || !RvrMayGoFor(subject, now))
                return null;
            if (!_rvrInterruptAggroAdded)
            {
                AddToAggroList(subject, RvrAssist.CalledAggro);
                _rvrInterruptAggroAdded = true;
            }
            return subject;
        }

        private void RvrInterruptLook(RvrFightPlan plan, long now)
        {
            if (now < _rvrInterruptLookAt)
                return;
            RvrSkillTier tier = RvrTier(plan);
            _rvrInterruptLookAt = now + RvrInterruptPlan.LookMilliseconds(tier, BotBody.DatabaseID, now);
            GameLiving subject = RvrFightPlan.SubjectFor(plan.Interrupts, Body);
            if (!ReferenceEquals(subject, _rvrInterruptSubject))
            {
                _rvrInterruptSubject = subject;
                _rvrInterruptAggroAdded = false;
                if (subject != null)
                    LogRvrInterrupt(subject, "assign", tier, now);
            }
            _rvrInterruptSawCasting = subject?.IsCasting == true;
        }

        /// <summary>
        /// Hook: the entry of TryPvpCrowdControl. Null for no opinion (today's crowd control runs); true when the plan
        /// cast its crowd control (or an interrupt) and the turn is used; false when the plan wants no crowd control from
        /// this bot now. With a call or a crowd-control job in the plan, only the plan's subject is controlled, so the
        /// kill target is never mezzed.
        /// </summary>
        public bool? RvrTacticsCrowdControl()
        {
            if (!RvrTacticsActive() || !(Properties.RVR_BOT_CC_PLAN || Properties.RVR_BOT_INTERRUPTS))
                return null;
            if (Body.CurrentRegion == null || !RvrInPvpFight(Body.TargetObject as GameLiving, null))
                return null;
            // AI_PROFILE: the plan read, the interrupt spell and the planned crowd control.
            using var aiProfile = AiProfile.Scope(AiSystem.RvrTactics);
            RvrFightPlan plan = RvrPlan();
            if (plan == null)
                return null;
            long now = GameLoop.GameLoopTime;
            // #531 step 2a: the opener (the group's area mez on the pile) is the first action on the charge.
            if (Properties.RVR_BOT_CC_PLAN && TryRvrOpener(plan, now))
                return true;
            if (Properties.RVR_BOT_INTERRUPTS && TryRvrInterruptSpell(plan, now))
                return true;
            if (!Properties.RVR_BOT_CC_PLAN || plan.CalledTarget == null && plan.CrowdControl.IsEmpty)
                return null;
            GameLiving subject = RvrFightPlan.SubjectFor(plan.CrowdControl, Body);
            RvrSkillTier tier = RvrTier(plan);
            if (!ReferenceEquals(subject, _rvrCcSubject))
            {
                _rvrCcSubject = subject;
                _rvrCcCasts = 0;
                if (subject != null)
                    LogRvrCrowdControl(subject, RvrControlKind.None, "assign", plan.CalledTarget, tier, now);
            }
            if (subject == null)
                return false;
            if (!TryPlannedCrowdControl(subject, plan.CalledTarget, out RvrControlKind kind))
                return false;
            LogRvrCrowdControl(subject, kind, ++_rvrCcCasts == 1 ? "open" : "recast", plan.CalledTarget, tier, now);
            return true;
        }

        /// <summary>
        /// #531 step 2a: the plan's opener (<see cref="RvrOpener"/>). When this bot is the plan's opener, it casts its best
        /// targeted area mez on the opener's centre. Native levels, power, timers, range, sight, interruption and the
        /// server's mez immunity decide as for the planned crowd control. The opener may catch the kill target: the
        /// train's first blow wakes only the one it hits. The 60 s area mez hold of the older path is set as after any
        /// area mez.
        /// </summary>
        private bool TryRvrOpener(RvrFightPlan plan, long now)
        {
            GameLiving centre = plan.OpenerCentre;
            if (centre == null || !ReferenceEquals(plan.OpenerMember, Body) || BotBody?.CrowdControlSpells == null ||
                Body.IsCasting || Body.IsIncapacitated || Body.castingComponent?.HasPendingSkillRequests == true ||
                !centre.IsAlive || centre.IsMezzed || centre.IsStealthed || !BotSiegeRuntime.LegalEnemy(Body, centre))
                return false;
            foreach (Spell spell in BotBody.CrowdControlSpells.Where(s => s.SpellType == eSpellType.Mesmerize && s.Radius > 0 &&
                         s.Target != eSpellTarget.SELF && s.Range > 0).OrderByDescending(s => s.Level))
            {
                if (spell.Level > Body.Level || Body.GetSkillDisabledDuration(spell) > 0 || Body.Mana < BotBody.PowerCost(spell) ||
                    BotInterruptPolicy.CastWouldBeLost(Body, spell) || RvrCrowdControlPlan.MezImmune(centre, spell.Duration, Body) ||
                    !Body.IsWithinRadius(centre, spell.CalculateEffectiveRange(Body)) || LivingHasEffect(centre, spell) ||
                    !NeedsOffensiveSpellApplication(centre, spell) || !BotSiegeRuntime.Visible(Body, centre) ||
                    !BotPvpCrowdControl.Reserve(BotBody, centre, spell.CastTime + 2000, spell))
                    continue;
                GameObject previous = Body.TargetObject;
                Body.TargetObject = centre;
                if (spell.CastTime > 0) { Body.StopMovingOnPath(); Body.StopMoving(); }
                if (!CheckOffensiveSpells(spell))
                {
                    BotPvpCrowdControl.Release(BotBody, centre);
                    Body.TargetObject = previous;
                    continue;
                }
                _pvpControlSpellId = spell.ID;
                _pvpControlCastUntil = now + spell.CastTime + 2000;
                _nextPvpAreaMez = now + PvpAreaMezHoldMilliseconds;
                LogRvrCrowdControl(centre, RvrControlKind.Mez, "opener", plan.CalledTarget, RvrTier(plan), now);
                return true;
            }
            return false;
        }

        /// <summary>
        /// An interrupter that saw its caster casting at its last look fires an instant harmful spell at it (any hit
        /// interrupts: the server's own rule and 1.65 values). One spell per look.
        /// </summary>
        private bool TryRvrInterruptSpell(RvrFightPlan plan, long now)
        {
            RvrInterruptLook(plan, now);
            GameLiving subject = _rvrInterruptSubject;
            if (!_rvrInterruptSawCasting || subject?.IsCasting != true || Body.IsCasting || Body.IsIncapacitated ||
                Body.castingComponent?.HasPendingSkillRequests == true || !Body.CanCastInstantHarmfulSpells ||
                !RvrMayGoFor(subject, now))
                return false;
            foreach (Spell spell in Body.InstantHarmfulSpells)
            {
                if (spell == null || spell.Level > Body.Level || Body.GetSkillDisabledDuration(spell) > 0 ||
                    Body.Mana < BotBody.PowerCost(spell) || spell.Radius > 0 ||
                    !Body.IsWithinRadius(subject, spell.CalculateEffectiveRange(Body)) || LivingHasEffect(subject, spell))
                    continue;
                GameObject previous = Body.TargetObject;
                Body.TargetObject = subject;
                if (!CheckOffensiveSpells(spell))
                {
                    Body.TargetObject = previous;
                    continue;
                }
                _rvrInterruptSawCasting = false;
                LogRvrInterrupt(subject, "spell", RvrTier(plan), now);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Hook: BotBrain.AreaSpells, for an enemy the blast would reach that is mezzed or held by a breakable root. True
        /// when this bot's area damage goes ahead anyway: the human error "AE breaks the mez", by tier (Casual 25%,
        /// Average 12%, Veteran 4% per caster, enemy and 10 s). Only for player-like enemies in an open-field fight.
        /// </summary>
        public bool RvrTacticsBlastIgnoresMez(GameLiving controlled)
        {
            if (!Properties.RVR_BOT_CC_PLAN || !RvrPvpEnemy(controlled) || !RvrTacticsActive())
                return false;
            RvrFightPlan plan = PeekRvrPlan();
            return plan != null && RvrCrowdControlPlan.AreaDamageBreaksMez(plan.SkillTier, BotBody.DatabaseID,
                RvrFieldReader.KeyOf(controlled), GameLoop.GameLoopTime);
        }

        // ---- the log lines (rate-limited) -----------------------------------------------------------------------------

        private void LogRvrCall(RvrFightPlan plan, long now)
        {
            if (!log.IsInfoEnabled)
                return;
            RvrFieldReader.Memory memory = RvrFieldReader.MemoryOf(RvrFightBoards.KeyOf(Body));
            if (memory == null)
                return;
            lock (memory.Lock)
            {
                if (!RvrAssist.ShouldLogCall(memory.LoggedCall, plan.CalledTarget, now, memory.LastCallLogAt))
                    return;
                memory.LoggedCall = plan.CalledTarget;
                memory.LastCallLogAt = now;
            }
            RvrFightBoard board = RvrFightBoards.Peek(Body);
            RvrCombatant enemy = null;
            if (board != null)
                foreach (RvrCombatant listed in board.Enemies)
                    if (ReferenceEquals(listed.Living, plan.CalledTarget))
                        enemy = listed;
            RvrEnemyFacts facts = RvrFieldReader.Of(board).Enemy(enemy);
            double score = enemy == null ? 0 : RvrAssist.CallScore(enemy, facts, plan.SkillTier, RvrAssist.GroupSeed(board), plan.CalledAt);
            GameLiving target = plan.CalledTarget;
            log.Info($"RVR_ASSIST_CALL group={RvrGroupLogId()} realm={GlobalConstants.RealmToName(Body.Realm)} region={Body.CurrentRegionID} " +
                     $"mainAssist=\"{plan.MainAssist?.Name}\" target=\"{target.Name}\" targetRealm={GlobalConstants.RealmToName(target.Realm)} " +
                     $"targetClass={enemy?.Class} role={RvrTargetPriority.KillRole(enemy)} score={score:0.#} tier={plan.SkillTier} " +
                     $"rising={facts.Rising} hold={RvrAssist.HoldMilliseconds(RvrAssist.GroupSeed(board), plan.CalledAt)} " +
                     $"fight={RvrFights.Touch(Body, target, "call")}");
        }

        private void LogRvrSwitch(GameLiving called, long delay, bool followed, RvrSkillTier tier, long now)
        {
            if (_rvrSwitchLogged || !log.IsInfoEnabled || now - _rvrLastSwitchLogAt < 4_000)
                return;
            _rvrSwitchLogged = true;
            _rvrLastSwitchLogAt = now;
            log.Info($"RVR_ASSIST_SWITCH group={RvrGroupLogId()} member=\"{Body.Name}\" target=\"{called.Name}\" delay={delay} " +
                     $"result={(followed ? "followed" : "tunnelled")} tier={tier} fight={RvrFights.IdOf(Body)}");
        }

        private void LogRvrCrowdControl(GameLiving subject, RvrControlKind kind, string action, GameLiving killTarget,
            RvrSkillTier tier, long now)
        {
            if (action is "assign" or "open" or "opener")
                RvrFights.CrowdControl(Body);   // #505: the fight's first crowd control (logging only)
            if (!log.IsInfoEnabled || action is not ("assign" or "opener") && now - _rvrLastCcLogAt < 3_000)
                return;
            _rvrLastCcLogAt = now;
            log.Info($"RVR_CC_ASSIGN group={RvrGroupLogId()} member=\"{Body.Name}\" subject=\"{subject.Name}\" " +
                     $"subjectClass={(subject as GameBot)?.CharacterClass?.Name ?? (subject as GamePlayer)?.CharacterClass?.Name ?? "pet"} " +
                     $"kind={kind} action={action} killTarget=\"{killTarget?.Name}\" tier={tier} fight={RvrFights.IdOf(Body)}");
        }

        private void LogRvrInterrupt(GameLiving subject, string action, RvrSkillTier tier, long now)
        {
            if (!log.IsInfoEnabled || now - _rvrLastInterruptLogAt < 3_000)
                return;
            _rvrLastInterruptLogAt = now;
            log.Info($"RVR_INTERRUPT group={RvrGroupLogId()} member=\"{Body.Name}\" subject=\"{subject.Name}\" " +
                     $"subjectClass={(subject as GameBot)?.CharacterClass?.Name ?? (subject as GamePlayer)?.CharacterClass?.Name ?? "pet"} " +
                     $"action={action} casting={subject.IsCasting} tier={tier}");
        }

        #endregion

        /// <summary>
        /// Hook: the support turn (PerformGroupSupport), after the bot stopped attacking and before heals and crowd
        /// control. True when the tactics used the turn; false for no opinion.
        /// Work package 3 (rvr_bot_positioning): a main healer's kite and its walk to its place (BotBrain.RvrPositioning).
        /// </summary>
        public bool TryRvrTacticsSupport()
        {
            if (!RvrTacticsActive())
                return false;
            return Properties.RVR_BOT_POSITIONING && TryRvrPlaceSupport();
        }

        // ---- work package 3: peel, protection, positioning -----------------------------------------------------------

        #region work package 3: peel, protection, positioning (rvr_bot_peel, rvr_bot_positioning)

        // The work is in BotBrain.TankPeel.cs (peel, protection target, slam) and BotBrain.RvrPositioning.cs (backline,
        // spread, escort, kiting); the rules are RvrPeelPlan and RvrPositioning (pure, tested). Each hook answers "no
        // opinion" outside a fight against players or bots, so PvE fights in the frontier run as before.

        /// <summary>
        /// Hook: the entry of MaintainTankPeel. True when the open-field peel handled Protect, Guard, Intercept and the
        /// slam this turn (the bot's group is in a fight against players or bots); false for no opinion (today's peel runs).
        /// </summary>
        public bool RvrTacticsPeel()
        {
            if (!RvrTacticsActive() || !Properties.RVR_BOT_PEEL)
                return false;
            return TryRvrPeel();
        }

        /// <summary>
        /// Hook: the entry of FindProtectionTarget. True when the plan decides whom this bot pulls off a group mate:
        /// <paramref name="target"/> is that enemy, or null for "nobody now" (a tank pulls only attackers off its own charge
        /// in an open-field fight). False for no opinion (today's search runs).
        /// </summary>
        public bool RvrTacticsProtectionTarget(out GameLiving target)
        {
            target = null;
            if (!RvrTacticsActive() || !Properties.RVR_BOT_PEEL)
                return false;
            return TryRvrProtectionTarget(out target);
        }

        /// <summary>
        /// Hook: AttackMostWanted, with a target, before the ambush approach, the flank step and the circle. True when
        /// the tactics used the turn to move (backline, spread, a guard's escort, a hybrid healer's kite; a main healer
        /// moves in TryRvrTacticsSupport; work package 6: a stealth opener); false for no opinion.
        /// </summary>
        public bool TryRvrTacticsMove(GameLiving target)
        {
            if (!RvrTacticsActive() || !(Properties.RVR_BOT_POSITIONING || Properties.RVR_BOT_STEALTH_OPENERS))
                return false;
            // Work package 6: a hidden melee stealther at melee range opens from stealth (BotRvrAmbush.TryStealthOpener),
            // before any work package 3 placement step.
            if (Properties.RVR_BOT_STEALTH_OPENERS && BotRvrAmbush.TryStealthOpener(BotBody, target))
                return true;
            // #522: a hidden archer opens from bow range (a Critical Shot from stealth; BotStealthArcher).
            if (Properties.RVR_BOT_STEALTH_OPENERS && BotStealthArcher.TryOpen(BotBody, target))
                return true;
            if (Properties.RVR_BOT_POSITIONING && TryRvrPlaceMove(target))
                return true;
            return false;
        }

        #endregion

        // ---- work package 4: stances (size-up, watching, retreat, regroup, chasing) ---------------------------------
        #region WP4 stances (RvrEngagement)

        private GameLiving _rvrStanceLeashed;
        private long _rvrStanceLeashedUntil;
        private long _rvrStanceChaseLogAt = long.MinValue / 2;

        /// <summary>A chase this bot gave up is not taken up again for this long.</summary>
        public const long RvrChaseGiveUpMilliseconds = 10_000;

        /// <summary>
        /// Hook (work package 4): TryHoldRetreat, right after the group's retreat order is read, on every think of a live
        /// bot. For a bot-only group in the open field it keeps the group's fight board fresh while the bot fights or the
        /// group has a stance to keep (one build per group and second at most), acts once on each new board
        /// (RvrEngagementRuntime.Observe: logs, the group's own retreat order, the warband relay), makes the group's own
        /// retreat ragged (each member reacts after its tier's delay, some never run, a member being hit in melee fights
        /// back) and gives up a chase past the leash. Returns the order this bot obeys now (null: none); any other order
        /// (a PvE RUN) passes unchanged. Out of scope, solo or switched off: the order unchanged and nothing built.
        /// </summary>
        public BotGroupRetreat.Order RvrStanceRetreatOrder(BotGroupRetreat.Order order, long now)
        {
            GameBot bot = BotBody;
            if (bot?.Group == null || !(Properties.RVR_BOT_SIZE_UP || Properties.RVR_BOT_DISENGAGE || Properties.RVR_BOT_REGROUP) ||
                !RvrTacticsActive())
                return order;
            using var aiProfile = AiProfile.Scope(AiSystem.RvrTactics);
            bool fighting = bot.InCombat || bot.IsAttacking;
            RvrFightBoard board;
            if (fighting || RvrEngagementRuntime.NeedsUpkeep(bot, now))
            {
                RvrEngagementRuntime.EnsureFacts(bot);
                board = RvrFightBoards.Get(bot);
            }
            else
                board = RvrFightBoards.Peek(bot);
            if (board != null)
                RvrEngagementRuntime.Observe(bot, board, now);
            if (order != null && RvrEngagementRuntime.IsOwnOrder(bot.Group, order, out long publishedAt))
            {
                RvrSkillTier tier = board?.Plan?.SkillTier ?? RvrFightBoards.StateOf(bot.Group).SkillTier ?? RvrSkillTier.Average;
                var reaction = RvrEngagement.RetreatReaction(tier, RvrEngagementRuntime.MemberKey(bot), order.Id);
                if (!RvrEngagement.HeedsRetreat(now, publishedAt, reaction, RvrEngagementRuntime.HitInMelee(bot, now)))
                    order = null;
            }
            if (fighting && order == null && board != null)
                MaintainRvrChaseLeash(bot, board, now);
            return order;
        }

        /// <summary>Whether this bot gave up chasing <paramref name="target"/> lately (the threat gate leaves it alone).</summary>
        public bool RvrStanceLeashed(GameLiving target, long now) =>
            target != null && ReferenceEquals(target, _rvrStanceLeashed) && now < _rvrStanceLeashedUntil;

        /// <summary>
        /// Chasing (docs/RVR-FIELD-1.2.md, "Hands"): a member that ran past its leash from its group's centre after an
        /// enemy running further away gives up and goes back; a bold bot's leash is 2.5 times longer, into the enemy's
        /// guards. A bot that is hit, or whose enemy is on a group mate, keeps fighting.
        /// </summary>
        private void MaintainRvrChaseLeash(GameBot bot, RvrFightBoard board, long now)
        {
            if (bot.TargetObject is not GameLiving target || target is not (GamePlayer or GameBot) || !target.IsAlive ||
                target.Realm == bot.Realm || RvrEngagementRuntime.Hit(bot, now) ||
                target.TargetObject is GameLiving victim && (victim == bot || victim.Group == bot.Group && bot.Group != null))
                return;
            BotPersonality? personality = AutonomousBotPersonality.Applies(bot) ? AutonomousBotPersonality.For(bot) : null;
            double boldness = AutonomousRvrPerception.Boldness(personality);
            int leash = RvrEngagement.ChaseLeash(board.Plan?.SkillTier ?? RvrSkillTier.Average, boldness);
            System.Numerics.Vector3 centre = board.FriendlyCentroid;
            double botFrom = System.Numerics.Vector2.Distance(new System.Numerics.Vector2(bot.X, bot.Y), new System.Numerics.Vector2(centre.X, centre.Y));
            double targetFrom = System.Numerics.Vector2.Distance(new System.Numerics.Vector2(target.X, target.Y), new System.Numerics.Vector2(centre.X, centre.Y));
            if (!RvrEngagement.StopsChasing(botFrom, targetFrom, leash, false))
                return;
            if (bot.IsAttacking)
                bot.StopAttack();
            RemoveFromAggroList(target);
            bot.TargetObject = null;
            bot.StopMoving();
            _rvrStanceLeashed = target;
            _rvrStanceLeashedUntil = now + RvrChaseGiveUpMilliseconds;
            if (now - _rvrStanceChaseLogAt >= RvrChaseGiveUpMilliseconds && log.IsInfoEnabled)
            {
                _rvrStanceChaseLogAt = now;
                log.Info($"RVR_CHASE group={RvrEngagementRuntime.GroupName(bot)} bot=\"{bot.Name}\" target=\"{target.Name}\" " +
                         $"fromGroup={(int)botFrom} targetFromGroup={(int)targetFrom} leash={leash} boldness={boldness:0.00} " +
                         $"skill={board.Plan?.SkillTier}");
            }
        }

        #endregion

        // ---- work package 6: stealth openers ---------------------------------------------------------------------------

        /// <summary>
        /// Hook: BotMeleeStylePolicy.Usable, for a style that needs stealth (which bots never picked before 1.2). True lets
        /// the bot use it as its opener: work package 6 (rvr_bot_stealth_openers), a hidden Infiltrator, Nightshade or
        /// Shadowblade in scope, once per target (BotRvrAmbush.AllowsOpener); false for no opinion (the style stays out).
        /// The cheap checks (stealthed) come before the scope.
        /// </summary>
        public static bool RvrTacticsAllowsStealthOpener(GameBot bot, Style style)
        {
            if (style?.StealthRequirement != true || !Properties.RVR_BOT_STEALTH_OPENERS || bot?.IsStealthed != true ||
                !RvrTacticsApplyTo(bot))
                return false;
            return BotRvrAmbush.AllowsOpener(bot, style, inScope: true);
        }
    }
}
