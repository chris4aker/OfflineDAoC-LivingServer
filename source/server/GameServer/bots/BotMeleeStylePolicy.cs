using System;
using System.Collections.Generic;
using DOL.Database;
using DOL.GS.Styles;

namespace DOL.GS
{
    /// <summary>
    /// Shared companion/world-bot selection; native openings and costs remain authoritative. Night playtest 2026-11-04: the
    /// selector took the usable style with the highest growth rate every swing and dropped every positional style, so melee bots
    /// spammed one style (Warrior Plague 134, Draw Out 81, Provoke 68; Mauler Jab at 18-50), taunt styles won on damage dealers
    /// and chains whose opener grew less never started. Now each usable style is scored (<see cref="Score"/>): a chain follow-up
    /// after its landed opener first, then a back or side style when the bot is there (StyleProcessor checks the position), then
    /// reactive styles, the opener of a chain the bot knows, and plain styles by growth rate; taunt styles only for a grouped
    /// tank (whose deliberate taunt queue is kept as before); and two near-equal plain styles take turns.
    /// Scoring ported from rambodolphin13/DividedRealms 0146664a (by rambodolphin13), merged with our #123: which styles are
    /// eligible (<see cref="IsEligible"/>, <see cref="NeedsPosition"/>) stays ours.
    /// </summary>
    public static class BotMeleeStylePolicy
    {
        public const double ChainFollowUpBonus = 3.0;
        public const double PositionalBonus = 2.0;
        public const double ReactiveBonus = 1.5;
        public const double OpenerBonus = 0.6;
        public const double TauntFactor = 0.1;
        /// <summary>A plain style this close to the best one's score is used in turn with it.</summary>
        public const double RotationShare = 0.8;
        public const int TauntSpellId = 20000;

        /// <summary>
        /// Styles a bot may pick. Side, back and front styles count (owner 2026-11-08: bots never used them, so a bot
        /// behind a boss swung its front styles): CanUseStyle checks the real angle to the target at selection and again at
        /// the swing, so one is only picked when the bot really is beside or behind. Stealth openers stay out: bots do not
        /// stalk (2.0, with what bots can know).
        /// </summary>
        public static bool IsEligible(Style style) => style != null && !style.StealthRequirement;

        /// <summary>A side, back or front style: usable only from that spot.</summary>
        public static bool NeedsPosition(Style style) => style?.OpeningRequirementType == Style.eOpening.Positional;

        public static bool IsTauntStyle(Style style)
        {
            if (style?.Procs == null) return false;
            foreach (StyleProcInfo proc in style.Procs)
                if (proc?.Spell?.SpellType == eSpellType.StyleTaunt && proc.Spell.ID == TauntSpellId)
                    return true;
            return false;
        }

        public static bool IsChainFollowUp(Style style) => style != null &&
            style.OpeningRequirementType == Style.eOpening.Offensive && style.OpeningRequirementValue > 0;

        /// <summary>Back and side styles: usable only from there. A front style is an ordinary style while facing.</summary>
        public static bool IsBackOrSide(Style style) => style?.OpeningRequirementType == Style.eOpening.Positional &&
            (Style.eOpeningPosition)style.OpeningRequirementValue is Style.eOpeningPosition.Back or Style.eOpeningPosition.Side;

        /// <summary>After an evade, block, parry, miss or a hit of the target's own: rarely open, usually strong.</summary>
        public static bool IsReactive(Style style) => style != null && !IsChainFollowUp(style) &&
            (style.OpeningRequirementType == Style.eOpening.Defensive ||
             style.OpeningRequirementType == Style.eOpening.Offensive &&
             style.AttackResultRequirement is not Style.eAttackResultRequirement.Any and not Style.eAttackResultRequirement.Style);

        /// <summary>How much a usable style is worth this swing. Pure.</summary>
        public static double Score(Style style, bool opensKnownChain, bool tauntWanted)
        {
            double score = Math.Max(0.05, style.GrowthRate);
            if (IsChainFollowUp(style)) score += ChainFollowUpBonus;
            else if (IsBackOrSide(style)) score += PositionalBonus;
            else if (IsReactive(style)) score += ReactiveBonus;
            else if (opensKnownChain) score += OpenerBonus;
            if (!tauntWanted && IsTauntStyle(style)) score *= TauntFactor;
            return score;
        }

        /// <summary>A plain style: no chain, position or reaction behind it.</summary>
        public static bool IsPlain(Style style) => !IsChainFollowUp(style) && !IsBackOrSide(style) && !IsReactive(style);

        /// <summary>
        /// The best scored style, except that a plain best style used on the last swing gives way to another plain style within
        /// <see cref="RotationShare"/> of its score on every other swing (<paramref name="roll"/> below 0.5). Pure.
        /// </summary>
        public static Style Choose(IReadOnlyList<(Style Style, double Score)> scored, Style lastUsed, double roll)
        {
            Style best = null, second = null;
            double bestScore = double.MinValue, secondScore = double.MinValue;
            foreach ((Style style, double score) in scored)
            {
                if (style == null) continue;
                if (score > bestScore || score == bestScore && best != null && style.Level > best.Level)
                {
                    second = best; secondScore = bestScore;
                    best = style; bestScore = score;
                }
                else if (score > secondScore)
                {
                    second = style; secondScore = score;
                }
            }
            if (best != null && second != null && lastUsed != null && best.ID == lastUsed.ID && IsPlain(best) && IsPlain(second) &&
                secondScore >= bestScore * RotationShare && roll < 0.5)
                return second;
            return best;
        }

        public static bool Better(Style candidate, Style current) => current == null ||
            candidate.GrowthRate > current.GrowthRate ||
            candidate.GrowthRate == current.GrowthRate && candidate.Level > current.Level;

        public static DbInventoryItem Weapon(GameBot bot, Style style) =>
            style?.WeaponTypeRequirement == (int)eObjectType.Shield && bot.ActiveWeaponSlot == eActiveWeaponSlot.Standard
                ? bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon) : bot.ActiveWeapon;

        public static bool Usable(GameBot bot, Style style, AttackData lastAttack) =>
            IsEligible(style) && style.Level <= bot.Level &&
            MatchesWeapon(style, bot.ActiveWeapon, bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon), bot.ActiveWeaponSlot) &&
            StyleProcessor.CheckEnduranceCost(bot, Weapon(bot, style), style) &&
            bot.CheckStyleStun(style) &&
            StyleProcessor.CanUseStyle(lastAttack, bot, style, Weapon(bot, style));

        public static bool MatchesWeapon(Style style, DbInventoryItem primary, DbInventoryItem left, eActiveWeaponSlot slot)
        {
            if (style == null || primary == null || slot == eActiveWeaponSlot.Distance) return false;
            if (style.WeaponTypeRequirement == Style.SpecialWeaponType.DualWield)
            {
                if (slot != eActiveWeaponSlot.Standard || left == null ||
                    primary.Item_Type is not Slot.RIGHTHAND and not Slot.LEFTHAND ||
                    left.Object_Type == (int)eObjectType.Shield) return false;
                if (style.Spec == Specs.HandToHand)
                    return primary.Object_Type == (int)eObjectType.HandToHand && left.Object_Type == (int)eObjectType.HandToHand;
                if (style.Spec == Specs.Left_Axe) return left.Object_Type is (int)eObjectType.LeftAxe or (int)eObjectType.Axe;
                return GlobalConstants.IsWeapon(left.Object_Type);
            }
            if (style.WeaponTypeRequirement == Style.SpecialWeaponType.AnyWeapon)
                return GlobalConstants.IsWeapon(primary.Object_Type) && primary.Object_Type != (int)eObjectType.Shield;
            if (style.WeaponTypeRequirement == (int)eObjectType.Shield)
                return slot == eActiveWeaponSlot.Standard && left?.Object_Type == (int)eObjectType.Shield;
            eObjectType type = primary.Object_Type == (int)eObjectType.LeftAxe ? eObjectType.Axe : (eObjectType)primary.Object_Type;
            return GameServer.ServerRules.IsObjectTypesEqual((eObjectType)style.WeaponTypeRequirement, type);
        }

        public static Style Select(GameBot bot, AttackData lastAttack, bool preserveQueued = true)
        {
            if (bot?.Styles == null || !BotWeaponStats.FitsConfiguredSlot(bot, bot.ActiveWeapon,
                    bot.ActiveWeaponSlot == eActiveWeaponSlot.TwoHanded ? eInventorySlot.TwoHandWeapon : eInventorySlot.RightHandWeapon) ||
                bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance)
                return null;
            // Preserve a deliberate tank taunt, but revalidate at the actual swing.
            Style queued = bot.styleComponent.NextCombatStyle;
            if (preserveQueued && Usable(bot, queued, lastAttack)) return queued;
            Style anytime = null;
            HashSet<int> openers = null;
            foreach (Style style in bot.Styles)
                if (IsChainFollowUp(style) && style.Level <= bot.Level)
                    (openers ??= new HashSet<int>()).Add(style.OpeningRequirementValue);
            bool tauntWanted = BotPartyRoles.IsTank(bot) && bot.Group?.MemberCount > 1;
            var scored = new List<(Style, double)>();
            foreach (Style style in bot.Styles)
            {
                if (!Usable(bot, style, lastAttack)) continue;
                scored.Add((style, Score(style, openers?.Contains(style.ID) == true, tauntWanted)));
                if (SavageBotCombatPolicy.IsReliableAnytimeStyle(style) && !IsTauntStyle(style) && Better(style, anytime)) anytime = style;
            }
            bot.styleComponent.NextCombatBackupStyle = anytime;
            return Choose(scored, lastAttack?.Style, Random.Shared.NextDouble());
        }
    }
}
