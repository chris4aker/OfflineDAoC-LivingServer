using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.SkillHandler;
using DOL.GS.Styles;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Tanks use Protect, Guard and Intercept as players do (owner 2026-10-29: bots only ever taunted). In a group fight,
    /// once a second: Protect sits on the group's healer (or the squishy member being hit), so the monsters' anger at it
    /// goes to the tank; Guard, only with a shield in use, covers the squishiest member a monster is hitting in melee
    /// (otherwise the healer); Intercept, on its one-minute reuse, takes the blows for a squishy member below half health
    /// being hit next to the tank. The existing priority taunt still pulls monsters off group members.
    /// Open-field RvR (1.2 work package 3, rvr_bot_peel): in a bot-only group's fight against players or bots the peel
    /// reads the fight boards instead, so an enemy player or bot hitting a healer or caster counts (see
    /// <see cref="TryRvrPeel"/>), and each shield tank works for the charge the group's plan named.
    /// </summary>
    public partial class BotBrain
    {
        private long _nextTankPeelTick;
        public const int InterceptHealthPercent = 50;

        private void MaintainTankPeel()
        {
            if (RvrTacticsPeel()) return;
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot?.Group == null || bot.Group.MemberCount < 2 || !bot.IsAlive || bot.IsIncapacitated || now < _nextTankPeelTick)
                return;
            _nextTankPeelTick = now + 1_000;
            bool guard = bot.HasAbility(Abilities.Guard), protect = bot.HasAbility(Abilities.Protect),
                intercept = bot.HasAbility(Abilities.Intercept);
            if (!guard && !protect && !intercept)
                return;
            PeelSquishies(bot, guard, protect, intercept, BeingHitInMelee);
        }

        /// <summary>Today's peel: Protect on the healer, Guard and Intercept for the squishiest member being hit.</summary>
        private void PeelSquishies(GameBot bot, bool guard, bool protect, bool intercept, Func<GameLiving, bool> beingHit)
        {
            GameLiving[] squishies = bot.Group.GetMembersInTheGroup()
                .Where(member => member != null && member != bot && member.IsAlive &&
                    member.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(member, ProtectAbilityHandler.PROTECT_DISTANCE) &&
                    PeelClass(member) is eCharacterClass characterClass && BotPartyRoles.For(characterClass) != BotPartyRole.Tank)
                .ToArray();
            if (squishies.Length == 0)
                return;
            GameLiving threatened = squishies.Where(beingHit).OrderBy(member => member.HealthPercent).FirstOrDefault();
            GameLiving healer = squishies.FirstOrDefault(member =>
                PeelClass(member) is eCharacterClass characterClass && BotPartyRoles.IsHealingClass(characterClass));

            if (protect && (healer ?? threatened) is GameLiving protectTarget)
                Apply(eEffect.Protect, protectTarget);
            if (guard && ShieldInUse(bot) && (threatened ?? healer) is GameLiving guardTarget)
                Apply(eEffect.Guard, guardTarget);
            if (intercept && threatened != null)
                TryIntercept(bot, threatened);
        }

        /// <summary>Intercept, on its one-minute reuse, for a member below half health next to the tank; true when used.</summary>
        private static bool TryIntercept(GameBot bot, GameLiving threatened)
        {
            if (threatened.HealthPercent >= InterceptHealthPercent ||
                !bot.IsWithinRadius(threatened, InterceptAbilityHandler.INTERCEPT_DISTANCE) ||
                bot.GetAbility(Abilities.Intercept) is not Ability interceptAbility || bot.GetSkillDisabledDuration(interceptAbility) > 0)
                return false;
            InterceptAbilityHandler.CheckExistingEffectsOnTarget(bot, threatened, false, out bool ours, out var other);
            if (ours || other != null)
                return false;
            InterceptAbilityHandler.CancelOurEffectThenAddOnTarget(bot, threatened);
            bot.DisableSkill(interceptAbility, InterceptAbilityHandler.REUSE_TIMER);
            return true;
        }

        /// <summary>Puts our Guard or Protect on <paramref name="target"/>; true when it was newly put on.</summary>
        private bool Apply(eEffect kind, GameLiving target)
        {
            if (kind == eEffect.Guard)
            {
                GuardAbilityHandler.CheckExistingEffectsOnTarget(Body, target, false, out bool ours, out var other);
                if (ours || other != null)
                    return false;
                GuardAbilityHandler.CancelOurEffectThenAddOnTarget(Body, target);
                return true;
            }
            else
            {
                ProtectAbilityHandler.CheckExistingEffectsOnTarget(Body, target, false, out bool ours, out var other);
                if (ours || other != null)
                    return false;
                ProtectAbilityHandler.CancelOurEffectThenAddOnTarget(Body, target);
                return true;
            }
        }

        private static eCharacterClass? PeelClass(GameLiving member) => member switch
        {
            GameBot { CharacterClass: not null } memberBot => (eCharacterClass)memberBot.CharacterClass.ID,
            GamePlayer { CharacterClass: not null } player => (eCharacterClass)player.CharacterClass.ID,
            _ => null,
        };

        /// <summary>A monster in melee range is attacking this member.</summary>
        private static bool BeingHitInMelee(GameLiving member)
        {
            foreach (GameNPC npc in member.GetNPCsInRadius(400))
                if (npc.IsAlive && npc.TargetObject == member && (npc.IsAttacking || npc.InCombat) &&
                    npc.IsWithinRadius(member, npc.MeleeAttackRange + 50) && npc is not GameBot)
                    return true;
            return false;
        }

        private static bool ShieldInUse(GameBot bot) =>
            bot.ActiveWeaponSlot == eActiveWeaponSlot.Standard &&
            bot.Inventory?.GetItem(eInventorySlot.LeftHandWeapon) is { Object_Type: (int)eObjectType.Shield };

        // ---- open-field RvR: protect, peel and slam (1.2 work package 3, rvr_bot_peel) ------------------------------------

        /// <summary>RVR_PEEL: the same action on the same charge and attacker at most this often per tank ...</summary>
        public const long RvrPeelLogRepeatMilliseconds = 15_000;
        /// <summary>... and any RVR_PEEL line of one tank at most this often.</summary>
        public const long RvrPeelLogGapMilliseconds = 3_000;

        private string _rvrPeelLogKey;
        private long _rvrPeelLogKeyAt = long.MinValue / 2;
        private long _rvrPeelLogAt = long.MinValue / 2;
        private GameLiving _rvrSlamTarget;
        private Style _rvrSlamStyle;

        /// <summary>A guard in a sticking mood: a shield tank with Guard, assigned a charge by the plan, not over-extended.</summary>
        private bool RvrPeelSticks(RvrFightPlan plan, long now, out GameLiving charge)
        {
            charge = RvrPeelPlan.ChargeOf(plan, Body);
            if (charge == null || !charge.IsAlive || charge.CurrentRegion != Body.CurrentRegion || BotBody == null ||
                !BotBody.HasAbility(Abilities.Guard) || !ShieldInUse(BotBody))
                return false;
            return !RvrPeelOverExtended(plan, now);
        }

        /// <summary>Whether this tank over-extends now (RvrPeelPlan.OverExtends, by the group's tier and his boldness).</summary>
        private bool RvrPeelOverExtended(RvrFightPlan plan, long now) =>
            RvrPeelPlan.OverExtends(plan?.SkillTier ?? RvrSkillTier.Average, RvrPeelBoldness(), BotBody?.DatabaseID ?? 0, now);

        private double RvrPeelBoldness() => AutonomousRvrPerception.Boldness(
            BotBody != null && AutonomousBotPersonality.Applies(BotBody) ? AutonomousBotPersonality.For(BotBody) : null);

        /// <summary>
        /// The peel in an open-field fight (called by <see cref="RvrTacticsPeel"/>, once a second like today's peel). The
        /// fight boards say who is hit in melee: the RvR board's enemies (players, bots, their pets) and the group's fight
        /// board (HitInMelee, from the attacker lists), so a player or bot hitting a healer or caster counts. A tank with a
        /// charge in the plan puts Protect and, with a shield, Guard on it, Intercepts for it below half health, and slams
        /// (a stun style queued, when its class has one usable now) the attacker it pulled off it; an over-extended tank
        /// (a tier error) leaves its charge. A tank with no charge runs today's peel with the same "hit in melee". False
        /// when the bot is not in such a fight (today's peel runs).
        /// </summary>
        private bool TryRvrPeel()
        {
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot?.Group == null || bot.Group.MemberCount < 2 || !bot.IsAlive || bot.IsIncapacitated || bot.CurrentRegion == null ||
                now < _nextTankPeelTick)
                return false;
            bool guard = bot.HasAbility(Abilities.Guard), protect = bot.HasAbility(Abilities.Protect),
                intercept = bot.HasAbility(Abilities.Intercept);
            if (!guard && !protect && !intercept)
                return false;
            RvrFightBoard board = RvrPlaceBoard(Body.TargetObject as GameLiving);
            if (board == null)
                return false;

            // AI_PROFILE: the peel decision on the boards (the boards' own builds are timed by their systems).
            using var aiProfile = AiProfile.Scope(AiSystem.RvrTactics);
            _nextTankPeelTick = now + 1_000;
            RvrFightPlan plan = board.Plan;
            IReadOnlySet<GameLiving> hitInMelee = GetFightBoard(Body)?.HitInMelee;
            bool BeingHit(GameLiving member) => RvrPeelPlan.Threatened(member, member.X, member.Y, board.Enemies, hitInMelee);

            GameLiving charge = RvrPeelPlan.ChargeOf(plan, bot);
            if (charge == null || !charge.IsAlive || charge.CurrentRegion != bot.CurrentRegion)
            {
                PeelSquishies(bot, guard, protect, intercept, BeingHit);
                return true;
            }
            if (RvrPeelOverExtended(plan, now))
            {
                LogRvrPeel(charge, Body.TargetObject as GameLiving, "overextend", plan, now);
                return true;
            }
            GameLiving attacker = RvrPeelPlan.AttackerOf(RvrPeelPlan.Find(board.Members, charge), board.Enemies)?.Living;
            if (protect && bot.IsWithinRadius(charge, ProtectAbilityHandler.PROTECT_DISTANCE) && Apply(eEffect.Protect, charge))
                LogRvrPeel(charge, attacker, "protect", plan, now);
            if (guard && ShieldInUse(bot) && bot.IsWithinRadius(charge, GuardAbilityHandler.GUARD_DISTANCE) && Apply(eEffect.Guard, charge))
                LogRvrPeel(charge, attacker, "guard", plan, now);
            if (intercept && BeingHit(charge) && TryIntercept(bot, charge))
                LogRvrPeel(charge, attacker, "intercept", plan, now);
            if (attacker != null && TryRvrSlam(attacker))
                LogRvrPeel(charge, attacker, "slam", plan, now);
            return true;
        }

        /// <summary>
        /// In an open-field fight a tank's protection target is its own assignment's attacker only
        /// (RvrPeelPlan.ProtectionTargetFor; called by <see cref="RvrTacticsProtectionTarget"/>), so the tanks do not all
        /// chase the same enemy: the enemy on its charge from the RvR board, or else a monster hitting the charge (the
        /// group's fight board). A tank with no charge, or an over-extended one, gets nobody. False for no opinion (not a
        /// tank, no group, not in such a fight): today's search runs.
        /// </summary>
        private bool TryRvrProtectionTarget(out GameLiving target)
        {
            target = null;
            if (BotBody?.Group == null || BotBody.Group.MemberCount < 2 || !IsTankClass || Body.CurrentRegion == null)
                return false;
            RvrFightBoard board = RvrPlaceBoard(Body.TargetObject as GameLiving);
            if (board == null)
                return false;
            // AI_PROFILE: the protection decision on the board.
            using var aiProfile = AiProfile.Scope(AiSystem.RvrTactics);
            long now = GameLoop.GameLoopTime;
            RvrFightPlan plan = board.Plan;
            GameLiving charge = RvrPeelPlan.ChargeOf(plan, Body);
            if (charge == null || !charge.IsAlive || RvrPeelOverExtended(plan, now))
                return true;
            GameLiving attacker = RvrPeelPlan.ProtectionTargetFor(plan, board, Body);
            if (attacker == null && GetFightBoard(Body) is GroupFightBoard fightBoard)
                foreach (GameLiving threat in fightBoard.Threats)
                    if (threat?.TargetObject == charge && threat.IsAlive &&
                        (attacker == null || charge.GetDistanceTo(threat) < charge.GetDistanceTo(attacker)))
                        attacker = threat;
            if (attacker == null || !attacker.IsAlive || !CanAggroTarget(attacker))
                return true;
            target = attacker;
            LogRvrPeel(charge, attacker, "peel", plan, now);
            return true;
        }

        /// <summary>
        /// Slam: queues a stun style on the attacker the tank is hitting (as the priority taunt queues its style), when the
        /// class has one usable now (BotMeleeStylePolicy.Usable: weapon, endurance, opening and CheckStyleStun) and the
        /// attacker is not stunned or stun-immune. The server's own style-stun and immunity rules then apply (owner
        /// decision 9: keep the server's 1.65 values). Classes without a stun style do not slam; no stun spell is used.
        /// </summary>
        private bool TryRvrSlam(GameLiving attacker)
        {
            GameBot bot = BotBody;
            if (bot?.Styles == null || Body.TargetObject != attacker || Body.ActiveWeapon == null ||
                Body.styleComponent == null || !Body.IsWithinRadius(attacker, Body.MeleeAttackRange + 50) ||
                attacker.IsStunned || attacker.IsMezzed || attacker.effectListComponent == null ||
                attacker.effectListComponent.ContainsEffectForEffectType(eEffect.StunImmunity) ||
                attacker.effectListComponent.ContainsEffectForEffectType(eEffect.NPCStunImmunity))
                return false;
            if (_rvrSlamTarget == attacker && Body.styleComponent.NextCombatStyle == _rvrSlamStyle && _rvrSlamStyle != null)
                return false;
            AttackData last = Body.attackComponent?.attackAction?.LastAttackData;
            Style stun = null;
            foreach (Style style in bot.Styles)
                if (RvrPeelPlan.IsStunStyle(style) && (stun == null || style.Level > stun.Level) &&
                    BotMeleeStylePolicy.Usable(bot, style, last))
                    stun = style;
            if (stun == null)
                return false;
            Body.styleComponent.NextCombatStyle = stun;
            _rvrSlamTarget = attacker;
            _rvrSlamStyle = stun;
            return true;
        }

        /// <summary>A slam is queued on <paramref name="target"/>: the priority taunt does not replace it (BotBrain.TryPriorityTaunt).</summary>
        private bool RvrSlamQueued(GameLiving target) =>
            target != null && _rvrSlamTarget == target && _rvrSlamStyle != null && Body.styleComponent?.NextCombatStyle == _rvrSlamStyle;

        /// <summary>
        /// RVR_PEEL group= protector= protected= protectedClass= attacker= attackerKind= action= skill=. Rate-limited per
        /// tank: the same line at most every 15 s, any line at most every 3 s.
        /// </summary>
        private void LogRvrPeel(GameLiving charge, GameLiving attacker, string action, RvrFightPlan plan, long now)
        {
            if (!log.IsInfoEnabled)
                return;
            string key = $"{action}|{charge?.ObjectID}|{attacker?.ObjectID}";
            if (!RvrPositioning.ShouldLog(now, _rvrPeelLogAt, RvrPeelLogGapMilliseconds) ||
                key == _rvrPeelLogKey && !RvrPositioning.ShouldLog(now, _rvrPeelLogKeyAt, RvrPeelLogRepeatMilliseconds))
                return;
            _rvrPeelLogAt = now;
            _rvrPeelLogKey = key;
            _rvrPeelLogKeyAt = now;
            log.Info($"RVR_PEEL group={RvrPlaceGroupId()} region={Body.CurrentRegionID} protector=\"{Body.Name}\" " +
                     $"protected=\"{charge?.Name}\" protectedClass={PeelClass(charge)?.ToString() ?? "none"} " +
                     $"attacker=\"{attacker?.Name}\" attackerKind={RvrPlaceKind(attacker)} action={action} " +
                     $"skill={plan?.SkillTier ?? RvrSkillTier.Average}");
        }
    }
}
