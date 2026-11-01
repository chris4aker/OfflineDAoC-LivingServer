using System.Linq;
using DOL.GS;
using DOL.GS.SkillHandler;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Tanks use Protect, Guard and Intercept as players do (owner 2026-10-29: bots only ever taunted). In a group fight,
    /// once a second: Protect sits on the group's healer (or the squishy member being hit), so the monsters' anger at it
    /// goes to the tank; Guard, only with a shield in use, covers the squishiest member a monster is hitting in melee
    /// (otherwise the healer); Intercept, on its one-minute reuse, takes the blows for a squishy member below half health
    /// being hit next to the tank. The existing priority taunt still pulls monsters off group members.
    /// </summary>
    public partial class BotBrain
    {
        private long _nextTankPeelTick;
        public const int InterceptHealthPercent = 50;

        private void MaintainTankPeel()
        {
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot?.Group == null || bot.Group.MemberCount < 2 || !bot.IsAlive || bot.IsIncapacitated || now < _nextTankPeelTick)
                return;
            _nextTankPeelTick = now + 1_000;
            bool guard = bot.HasAbility(Abilities.Guard), protect = bot.HasAbility(Abilities.Protect),
                intercept = bot.HasAbility(Abilities.Intercept);
            if (!guard && !protect && !intercept)
                return;

            GameLiving[] squishies = bot.Group.GetMembersInTheGroup()
                .Where(member => member != null && member != bot && member.IsAlive &&
                    member.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(member, ProtectAbilityHandler.PROTECT_DISTANCE) &&
                    PeelClass(member) is eCharacterClass characterClass && BotPartyRoles.For(characterClass) != BotPartyRole.Tank)
                .ToArray();
            if (squishies.Length == 0)
                return;
            GameLiving threatened = squishies.Where(BeingHitInMelee).OrderBy(member => member.HealthPercent).FirstOrDefault();
            GameLiving healer = squishies.FirstOrDefault(member =>
                PeelClass(member) is eCharacterClass characterClass && BotPartyRoles.IsHealingClass(characterClass));

            if (protect && (healer ?? threatened) is GameLiving protectTarget)
                Apply(eEffect.Protect, protectTarget);
            if (guard && ShieldInUse(bot) && (threatened ?? healer) is GameLiving guardTarget)
                Apply(eEffect.Guard, guardTarget);
            if (intercept && threatened != null && threatened.HealthPercent < InterceptHealthPercent &&
                bot.IsWithinRadius(threatened, InterceptAbilityHandler.INTERCEPT_DISTANCE) &&
                bot.GetAbility(Abilities.Intercept) is Ability interceptAbility && bot.GetSkillDisabledDuration(interceptAbility) <= 0)
            {
                InterceptAbilityHandler.CheckExistingEffectsOnTarget(bot, threatened, false, out bool ours, out var other);
                if (!ours && other == null)
                {
                    InterceptAbilityHandler.CancelOurEffectThenAddOnTarget(bot, threatened);
                    bot.DisableSkill(interceptAbility, InterceptAbilityHandler.REUSE_TIMER);
                }
            }
        }

        private void Apply(eEffect kind, GameLiving target)
        {
            if (kind == eEffect.Guard)
            {
                GuardAbilityHandler.CheckExistingEffectsOnTarget(Body, target, false, out bool ours, out var other);
                if (!ours && other == null)
                    GuardAbilityHandler.CancelOurEffectThenAddOnTarget(Body, target);
            }
            else
            {
                ProtectAbilityHandler.CheckExistingEffectsOnTarget(Body, target, false, out bool ours, out var other);
                if (!ours && other == null)
                    ProtectAbilityHandler.CancelOurEffectThenAddOnTarget(Body, target);
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
    }
}
