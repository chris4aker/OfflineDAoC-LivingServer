using System;
using DOL.Database;
using DOL.GS.PropertyCalc;

namespace DOL.GS
{
    /// <summary>
    /// From shadowofze (1edbcf5), via True165, GPL-3.0. Crafting 2.0 WP6 ports it unchanged; in our tree it is read only
    /// while bot_crafters is on (AutonomousBotEconomy.FitValue and the set score of <see cref="BotGearSet"/>, which builds
    /// the template-aware valuation on these weights and caps). True165's FitValue also added WeaponSpecBonus and
    /// WastedFocus from earlier True165 commits that are not ported, so our FitValue is EquipmentValue plus
    /// <see cref="Adjustment"/> only.
    ///
    /// Gear value by class, up to each bonus's cap (veteran-bots review 2026-11-11, M9 / economy G3).
    /// AutonomousBotEconomy.EquipmentValue adds 3 per point of every bonus, whatever it raises and however much the bot
    /// already has: +5 Piety counted the same as +5 Strength for a Warrior, a stat already at its item cap still scored in
    /// full, and a drop far above the bot's level won on its level alone although its damage and armour stop at the bot's
    /// caps (Prima: "a character wielding a 15th level sword will get no better damage or speed than they would from an
    /// 8th level sword"). A veteran judged gear by the stats his class uses against what he still needed to reach cap;
    /// casters "evaluate magical armor on stat, HP, and resist bonuses as opposed to Armor Factor" (Prima, Runemaster).
    ///
    /// This is the correction FitValue adds to EquipmentValue (EquipmentValue itself is unchanged: logs, listing order
    /// and the vendor-trash order still read it):
    /// - each typed bonus is worth its class weight per point instead of 3, counted only up to the headroom left under
    ///   its cap with this slot's current piece taken out (stats 1.5 x level, hit points 4 x level, resists level/2 + 1,
    ///   skills level/5 + 1, flat power level/2 + 1: the server's own item caps, StatCalculator / MaxHealthCalculator /
    ///   ResistCalculator / SkillLevelCalculator / MaxManaCalculator);
    /// - the item level counts up to the bot's level, a weapon's DPS up to its cap (1.2 + 0.3 x level) and armour's AF up
    ///   to its cap (level for cloth, 2 x level otherwise).
    /// Untyped bonuses, a bot without a class (tests) or without item bonuses keep today's value. Pure arithmetic on the
    /// item's 11 bonus slots and the bot's item-bonus table (kept by RefreshItemBonuses): no allocation (the pairs are read
    /// in place since issue #540), no scan.
    /// </summary>
    public static class BotGearValue165
    {
        /// <summary>EquipmentValue's weight per bonus point; a class weight of 3 changes nothing.</summary>
        public const int BaseWeight = 3;

        /// <summary>The weight per point of a bonus of this property for this bot (3 = today's value).</summary>
        public static int Weight(GameBot bot, eProperty property)
        {
            ICharacterClass cls = bot?.CharacterClass;
            if (cls == null)
                return BaseWeight;
            if (property is >= eProperty.Stat_First and <= eProperty.Stat_Last)
            {
                var stat = (eStat)(int)property;
                if (stat == cls.PrimaryStat)
                    return 4;
                if (cls.ManaStat != eStat.UNDEFINED && stat == cls.ManaStat)
                    return 4;
                if (stat == cls.SecondaryStat || stat == eStat.CON)
                    return 3;
                if (stat == cls.TertiaryStat)
                    return 2;
                return 1;
            }
            switch (property)
            {
                case eProperty.Acuity:
                    return cls.ManaStat != eStat.UNDEFINED ? 4 : 0;
                case eProperty.MaxHealth:
                    return 1;   // a hit point is a quarter as scarce as a stat point (cap 4 x level against 1.5 x level)
                case eProperty.MaxMana:
                case eProperty.PowerPool:
                    return cls.ManaStat != eStat.UNDEFINED ? 3 : 0;
                case eProperty.ArmorFactor:
                    return 2;
            }
            if (property is >= eProperty.Resist_First and <= eProperty.Resist_Last)
                return 3;
            if (property is >= eProperty.Skill_First and <= eProperty.Skill_Last)
                return HasSkillLine(bot, property) ? 6 : 0;
            if (property is eProperty.AllMagicSkills)
                return cls.ManaStat != eStat.UNDEFINED ? 6 : 0;
            if (property is eProperty.AllMeleeWeaponSkills or eProperty.AllDualWieldingSkills or eProperty.AllArcherySkills)
                return 4;
            return BaseWeight;
        }

        /// <summary>Whether one of the bot's trained specializations is raised by this skill bonus.</summary>
        private static bool HasSkillLine(GameBot bot, eProperty property)
        {
            try
            {
                return bot.HasTrainedSpecRaising(property);
            }
            catch (Exception)
            {
                return true;   // no spec table (tests, a bot still loading): value it as today
            }
        }

        /// <summary>The bot's item cap for a bonus of this property, or int.MaxValue when there is none to respect.</summary>
        public static int Cap(int level, eProperty property)
        {
            level = Math.Max(1, level);
            if (property is >= eProperty.Stat_First and <= eProperty.Stat_Last || property == eProperty.Acuity)
                return (int)(level * 1.5);
            if (property == eProperty.MaxHealth)
                return level * 4;
            if (property is >= eProperty.Resist_First and <= eProperty.Resist_Last)
                return level / 2 + 1;
            if (property is >= eProperty.Skill_First and <= eProperty.Skill_Last or eProperty.AllMagicSkills or
                eProperty.AllMeleeWeaponSkills or eProperty.AllDualWieldingSkills or eProperty.AllArcherySkills)
                return level / 5 + 1;
            if (property == eProperty.MaxMana)
                return level / 2 + 1;
            return int.MaxValue;
        }

        /// <summary>A weapon's DPS cap at this level, in DPS_AF units (16.5 DPS = 165).</summary>
        public static int WeaponDpsCap(int level) => 12 + 3 * Math.Max(1, level);

        /// <summary>Armour's AF cap at this level: the level for cloth, twice it otherwise (GamePlayer's item AF cap).</summary>
        public static int ArmorFactorCap(int level, eObjectType type) =>
            type == eObjectType.Cloth ? Math.Max(1, level) : 2 * Math.Max(1, level);

        /// <summary>
        /// What FitValue adds to EquipmentValue for this item in this slot (negative when its bonuses are wasted on the
        /// class, over cap, or its level and DPS/AF beyond what the bot can use). bonuses = the item's 11 typed bonus pairs.
        /// </summary>
        public static int Adjustment(GameBot bot, int itemLevel, int dpsAf, int objectType,
            ItemBonuses bonuses, DbInventoryItem currentInSlot)
        {
            if (bot == null || bot.Level <= 0)
                return 0;
            int level = bot.Level;
            int adjustment = CapAdjustment(level, itemLevel, dpsAf, objectType);

            if (bot.CharacterClass == null || bonuses.IsEmpty)
                return adjustment;
            PropertyIndexer worn = bot.ItemBonus;
            foreach ((int bonusType, int value) in bonuses)
            {
                if (bonusType <= 0 || value == 0)
                    continue;
                var property = (eProperty)bonusType;
                int weight = Weight(bot, property);
                int counted = value;
                if (value > 0 && worn != null)
                {
                    int cap = Cap(level, property);
                    if (cap != int.MaxValue)
                    {
                        int others = worn[property] - BonusOf(currentInSlot, bonusType);
                        counted = Math.Clamp(cap - Math.Max(0, others), 0, value);
                    }
                }
                adjustment += weight * counted - BaseWeight * Math.Abs(value);
            }
            return adjustment;
        }

        /// <summary>
        /// The first half of <see cref="Adjustment"/>, split out unchanged by WP6 so the set score uses the same rule: the
        /// item's level counts only up to the bot's, and its DPS or AF only up to the bot's cap.
        /// </summary>
        public static int CapAdjustment(int level, int itemLevel, int dpsAf, int objectType)
        {
            level = Math.Max(1, level);
            int adjustment = 0;

            // the item's level counts only up to the bot's (EquipmentValue: 10 per level)
            if (itemLevel > level)
                adjustment -= (itemLevel - level) * 10;

            // damage and armour stop at the bot's caps (EquipmentValue: 2 per point of DPS_AF)
            var type = (eObjectType)objectType;
            if (dpsAf > 0)
            {
                int cap = type is >= eObjectType.GenericWeapon and <= eObjectType._LastWeapon && type != eObjectType.Shield &&
                          type != eObjectType.Instrument && type != eObjectType.Arrow && type != eObjectType.Bolt &&
                          type != eObjectType.Poison
                    ? WeaponDpsCap(level)
                    : type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor ? ArmorFactorCap(level, type) : int.MaxValue;
                if (dpsAf > cap)
                    adjustment -= (dpsAf - cap) * 2;
            }
            return adjustment;
        }

        /// <summary>
        /// The item's typed bonus pairs (the 10 numbered bonuses and the extra bonus), read in place. Issue #540: this used
        /// to build an 11-pair array on every call, and the Exchange and loot gear checks call it for every worn piece of
        /// every candidate (the 1.2.2 PC log's largest allocation, ValueTuple`2[Int32,Int32][] at 540-940 MB a minute,
        /// 27-35% of everything the server allocated, feeding the gen-2 collections behind the 1.1-2.4 s freezes).
        /// </summary>
        public static ItemBonuses Bonuses(DbInventoryItem item) => new(item, null);

        public static ItemBonuses Bonuses(DbItemTemplate t) => new(null, t);

        /// <summary>An item's or template's 11 typed bonus pairs, without a copy: foreach gives (Type, Value).</summary>
        public readonly struct ItemBonuses
        {
            public const int Count = 11;
            private readonly DbInventoryItem _item;
            private readonly DbItemTemplate _template;
            private readonly (int Type, int Value)[] _pairs;

            internal ItemBonuses(DbInventoryItem item, DbItemTemplate template)
            {
                _item = item;
                _template = item == null ? template : null;
                _pairs = null;
            }

            private ItemBonuses((int Type, int Value)[] pairs)
            {
                _item = null;
                _template = null;
                _pairs = pairs;
            }

            /// <summary>Tests and callers that hold their own pairs.</summary>
            public static implicit operator ItemBonuses((int Type, int Value)[] pairs) => new(pairs);

            /// <summary>No item, no template and no pairs (the old null array).</summary>
            public bool IsEmpty => _item == null && _template == null && _pairs == null;

            public int Length => _pairs?.Length ?? (IsEmpty ? 0 : Count);

            public (int Type, int Value) this[int index]
            {
                get
                {
                    if (_pairs != null)
                        return _pairs[index];
                    if (_item != null)
                        return index switch
                        {
                            0 => (_item.Bonus1Type, _item.Bonus1), 1 => (_item.Bonus2Type, _item.Bonus2),
                            2 => (_item.Bonus3Type, _item.Bonus3), 3 => (_item.Bonus4Type, _item.Bonus4),
                            4 => (_item.Bonus5Type, _item.Bonus5), 5 => (_item.Bonus6Type, _item.Bonus6),
                            6 => (_item.Bonus7Type, _item.Bonus7), 7 => (_item.Bonus8Type, _item.Bonus8),
                            8 => (_item.Bonus9Type, _item.Bonus9), 9 => (_item.Bonus10Type, _item.Bonus10),
                            10 => (_item.ExtraBonusType, _item.ExtraBonus),
                            _ => throw new ArgumentOutOfRangeException(nameof(index))
                        };
                    if (_template != null)
                        return index switch
                        {
                            0 => (_template.Bonus1Type, _template.Bonus1), 1 => (_template.Bonus2Type, _template.Bonus2),
                            2 => (_template.Bonus3Type, _template.Bonus3), 3 => (_template.Bonus4Type, _template.Bonus4),
                            4 => (_template.Bonus5Type, _template.Bonus5), 5 => (_template.Bonus6Type, _template.Bonus6),
                            6 => (_template.Bonus7Type, _template.Bonus7), 7 => (_template.Bonus8Type, _template.Bonus8),
                            8 => (_template.Bonus9Type, _template.Bonus9), 9 => (_template.Bonus10Type, _template.Bonus10),
                            10 => (_template.ExtraBonusType, _template.ExtraBonus),
                            _ => throw new ArgumentOutOfRangeException(nameof(index))
                        };
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            /// <summary>A copy as an array (tests and logs; the gear checks never need one).</summary>
            public (int Type, int Value)[] ToArray()
            {
                if (IsEmpty)
                    return null;
                var pairs = new (int Type, int Value)[Length];
                for (int i = 0; i < pairs.Length; i++)
                    pairs[i] = this[i];
                return pairs;
            }

            public Enumerator GetEnumerator() => new(this);

            public struct Enumerator
            {
                private readonly ItemBonuses _bonuses;
                private int _index;

                internal Enumerator(ItemBonuses bonuses)
                {
                    _bonuses = bonuses;
                    _index = -1;
                }

                public bool MoveNext() => ++_index < _bonuses.Length;

                public (int Type, int Value) Current => _bonuses[_index];
            }
        }

        /// <summary>The item's bonus of this type (all slots added), 0 for none or no item.</summary>
        public static int BonusOf(DbInventoryItem item, int bonusType)
        {
            if (item == null || bonusType <= 0)
                return 0;
            int sum = 0;
            if (item.Bonus1Type == bonusType) sum += item.Bonus1;
            if (item.Bonus2Type == bonusType) sum += item.Bonus2;
            if (item.Bonus3Type == bonusType) sum += item.Bonus3;
            if (item.Bonus4Type == bonusType) sum += item.Bonus4;
            if (item.Bonus5Type == bonusType) sum += item.Bonus5;
            if (item.Bonus6Type == bonusType) sum += item.Bonus6;
            if (item.Bonus7Type == bonusType) sum += item.Bonus7;
            if (item.Bonus8Type == bonusType) sum += item.Bonus8;
            if (item.Bonus9Type == bonusType) sum += item.Bonus9;
            if (item.Bonus10Type == bonusType) sum += item.Bonus10;
            if (item.ExtraBonusType == bonusType) sum += item.ExtraBonus;
            return sum;
        }
    }
}
