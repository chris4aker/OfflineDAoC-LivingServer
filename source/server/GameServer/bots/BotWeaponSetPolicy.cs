using DOL.Database;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #569 step 7: which melee set a bot fights with, two-hander or weapon (and shield or off-hand). Port by hand of
    /// zomboden/OfflineDAoC-True165 <c>27164a0</c> and its later rules (BotWeaponSetPolicy.cs at <c>50c407d</c>: Q4's
    /// trained shield and the trained-set rule; GPL-3.0, shadowofze). One rule for the brain's weapon choice
    /// (BotBrain.SwitchToUsableMeleeWeapon) and the attack start (GameNPC.StartAttack); before it the two used different
    /// "two-handed build" tests, so a staff caster or Friar carrying a one-hander could be switched one way by the brain
    /// and back by the attack start.
    ///
    /// In this order:
    /// 1. a set that is not usable is never chosen (the bot uses only weapons it already carries: nothing is created);
    /// 2. a hired companion keeps the melee set its player readied last in the companion window;
    /// 3. a party's only tank with Guard fights with weapon and shield when it carries a shield it has trained to half
    ///    its level (Prima's group chapter: the defensive Armsman, Warrior, Hero or Thane tanks and guards with a shield;
    ///    with another tank present the offensive one takes the two-hander);
    /// 4. the set whose weapon line it has trained higher (both sets stay equipped);
    /// 5. its build, as before.
    /// With bot_weapon_set_rules off each call site keeps its old rule.
    ///
    /// Bot types: autonomous gamebots, hired companions and /bot companions alike (rule 2 only for hired companions).
    /// </summary>
    public static class BotWeaponSetPolicy
    {
        public const string PropertyKey = "bot_weapon_set_rules";

        public static bool On => Properties.BOT_WEAPON_SET_RULES;

        /// <summary>The rule before #569 step 7, kept for the switch off.</summary>
        public static bool OldRule(bool twoHandUsable, bool standardUsable, bool twoHandedBuild) =>
            twoHandUsable && (!standardUsable || twoHandedBuild);

        /// <summary>
        /// The two-handed build: the bot's spec template says two-hander, or it is a list caster (a staff), a Friar or a
        /// Valewalker. The same test for both call sites.
        /// </summary>
        public static bool TwoHandedBuild(GameBot bot) =>
            bot?.BotSpec?.Is2H == true ||
            bot?.CharacterClass != null && (bot.CharacterClass.ClassType == eClassType.ListCaster ||
                                            bot.CharacterClass.ID is (int)eCharacterClass.Friar or (int)eCharacterClass.Valewalker);

        /// <summary>The bot's choice: true for the two-handed slot.</summary>
        public static bool PreferTwoHanded(GameBot bot, bool twoHandUsable, bool standardUsable, bool twoHandedBuild)
        {
            if (!twoHandUsable)
                return false;
            if (!standardUsable)
                return true;
            bool hired = bot != null && HiredCompanions165.IsHired(bot);
            return Choose(hired ? bot.PlayerReadiedSlot : null, GuardDuty(bot), TrainedSet(bot), twoHandedBuild);
        }

        /// <summary>
        /// Pure: with both sets usable, the player's readied set (a hired companion), then guard duty, then the set trained
        /// higher, then the build.
        /// </summary>
        public static bool Choose(eActiveWeaponSlot? playerReadied, bool guardDuty, bool? trainedTwoHand, bool twoHandedBuild)
        {
            if (playerReadied is eActiveWeaponSlot readied && readied != eActiveWeaponSlot.Distance)
                return readied == eActiveWeaponSlot.TwoHanded;
            if (guardDuty)
                return false;
            if (trainedTwoHand is bool trained)
                return trained;
            return twoHandedBuild;
        }

        /// <summary>
        /// The set the bot is trained in more: true for the two-hander, false for the right-hand weapon, null with no
        /// opinion (one set missing, a dual-wield set, or the same line trained alike: a Midgard two-handed and
        /// one-handed sword share Sword). Shields does not count: it is a defensive line.
        /// </summary>
        public static bool? TrainedSet(GameBot bot)
        {
            if (bot?.Inventory == null)
                return null;
            DbInventoryItem twoHand = bot.Inventory.GetItem(eInventorySlot.TwoHandWeapon);
            DbInventoryItem rightHand = bot.Inventory.GetItem(eInventorySlot.RightHandWeapon);
            DbInventoryItem leftHand = bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon);
            if (twoHand == null || rightHand == null)
                return null;
            return TrainedSet(TrainedSpec(bot, twoHand), TrainedSpec(bot, rightHand),
                leftHand != null && leftHand.Object_Type != (int)eObjectType.Shield);
        }

        /// <summary>Pure: the trained-set rule on its numbers (see <see cref="TrainedSet(GameBot)"/>).</summary>
        public static bool? TrainedSet(int twoHandSpec, int rightHandSpec, bool dualWieldSet) =>
            dualWieldSet || twoHandSpec == rightHandSpec ? null : twoHandSpec > rightHandSpec;

        /// <summary>The points the bot trained in the weapon's line (base spec, without item bonuses).</summary>
        private static int TrainedSpec(GameBot bot, DbInventoryItem weapon)
        {
            string line = SkillBase.ObjectTypeToSpec((eObjectType)weapon.Object_Type);
            return string.IsNullOrWhiteSpace(line) ? 0 : bot.GetBaseSpecLevel(line);
        }

        /// <summary>
        /// Pure (True165 Q4): a shield is worth guard duty only when the bot trained Shields to at least half its level. A
        /// two-hander Armsman, Paladin, Warrior or Thane is never swapped to a shield it never trained.
        /// </summary>
        public static bool ShieldTrainedForGuard(int shieldSpec, int level) => shieldSpec * 2 >= level;

        /// <summary>Pure: the guard-duty rule on its facts.</summary>
        public static bool GuardDuty(bool grouped, bool tank, bool hasGuard, bool shieldInLeftHand, int shieldSpec, int level,
            bool anotherLivingTank) =>
            grouped && tank && hasGuard && shieldInLeftHand && ShieldTrainedForGuard(shieldSpec, level) && !anotherLivingTank;

        /// <summary>Grouped, a tank class with Guard, a trained shield in the left hand, and no other living tank in the group.</summary>
        public static bool GuardDuty(GameBot bot)
        {
            if (bot?.Group == null || bot.Group.MemberCount < 2 || !BotPartyRoles.IsTank(bot) || !bot.HasAbility(Abilities.Guard) ||
                bot.Inventory?.GetItem(eInventorySlot.LeftHandWeapon) is not DbInventoryItem { Object_Type: (int)eObjectType.Shield })
                return false;
            bool anotherTank = false;
            foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
            {
                if (member == null || member == bot || !member.IsAlive)
                    continue;
                if (member is GameBot other ? BotPartyRoles.IsTank(other) :
                    member is GamePlayer { CharacterClass: not null } player &&
                    BotPartyRoles.For((eCharacterClass)player.CharacterClass.ID) == BotPartyRole.Tank)
                {
                    anotherTank = true;
                    break;
                }
            }
            return GuardDuty(true, true, true, true, bot.GetBaseSpecLevel(Specs.Shields), bot.Level, anotherTank);
        }

        /// <summary>
        /// Pure (anti flip-flop, True165 27164a0): an archer goes back to its bow from melee only with endurance for
        /// three shots; at the bare cost of one it flipped bow and melee on every shot.
        /// </summary>
        public static bool EnduranceForBow(int endurance, int shotCost, bool onBow) =>
            endurance >= shotCost * (onBow ? 1 : 3);

        /// <summary>
        /// Pure (anti flip-flop): once in melee, a monster has to step this far past melee reach before the bow comes back.
        /// </summary>
        public static int MeleeHold(bool onBow) => onBow ? 0 : 150;
    }
}
