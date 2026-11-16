using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.PropertyCalc;
using DOL.GS.ServerRules;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP6 test actors: an inert Hibernian gamebot (the UT_AutonomousLootFlow pattern) with a real
    /// BotInventory, cloth armour training, a class and an item-bonus table its RefreshItemBonuses keeps from what it
    /// wears, and a server double with the normal rules and a read-only empty database.
    /// </summary>
    internal static class GearTestBots
    {
        internal sealed class GearBot : GameBot
        {
            private GearBot() : base((OfflineWorldBotRecord)null) { }
            public byte TestLevel;
            public override byte Level { get => TestLevel; set => TestLevel = value; }
            public override int EffectiveLevel => TestLevel;
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public ICharacterClass TestClass;
            public override ICharacterClass CharacterClass => TestClass;
            public int Refreshes;

            public override void RefreshItemBonuses()
            {
                Refreshes++;
                ItemBonus.Clear();
                foreach (eInventorySlot slot in BotGearSet.ScoredSlots)
                {
                    DbInventoryItem item = Inventory.GetItem(slot);
                    if (item == null)
                        continue;
                    foreach ((int type, int value) in BotGearValue165.Bonuses(item))
                        if (type > 0 && value != 0)
                            ItemBonus[(eProperty)type] += value;
                }
            }
        }

        internal sealed class GearServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();

        public class EmptyReadDatabase : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("Gear unit test attempted a database mutation: " + method.Name);
                Type result = method.ReturnType;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return null;
            }
        }

        private static long _nextId = 7_600_000;

        public static GameServer UseServer()
        {
            GameServer previous = GameServer.Instance;
            GameServer.LoadTestDouble((GearServer)RuntimeHelpers.GetUninitializedObject(typeof(GearServer)));
            return previous;
        }

        private static void Field(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

        public static GearBot Bot(int level, ICharacterClass cls = null, bool helper = false, long money = 0)
        {
            var bot = (GearBot)RuntimeHelpers.GetUninitializedObject(typeof(GearBot));
            Field(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameNPC), bot, "m_brains", new ArrayList());
            Field(typeof(GameLiving), bot, "<ItemBonus>k__BackingField", new PropertyIndexer());
            bot.TestLevel = (byte)level;
            bot.TestClass = cls ?? new ClassHero();
            bot.Realm = eRealm.Hibernia;
            bot.Name = "Gear test bot";
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            bot.Inventory = new BotInventory(bot.InternalID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, !helper);
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, helper);
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord))!.SetValue(bot,
                new OfflineWorldBotRecord { BotId = bot.DatabaseID, Name = bot.Name, Realm = (int)bot.Realm, Level = level, MoneyCopper = money });
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            Field(typeof(GameLiving), bot, "_abilitiesLock", new Lock());
            Field(typeof(GameLiving), bot, "m_abilities", new Dictionary<string, Ability>
            {
                [Abilities.HibArmor] = new Ability(new DbAbility { KeyName = Abilities.HibArmor, Name = "Cloth" }, 1),
            });
            return bot;
        }

        /// <summary>A cloth armour piece or a jewel for the slot, with typed bonuses.</summary>
        public static DbInventoryItem Piece(eInventorySlot slot, int level, int quality = 96, bool crafted = false,
            params (eProperty Type, int Value)[] bonuses)
        {
            bool armor = BotGearSet.IsTemplateSlot(slot);
            var template = new DbItemTemplate
            {
                Id_nb = Guid.NewGuid().ToString(), Name = $"{(crafted ? "crafted " : "")}{slot} {level}/{quality}",
                Realm = (int)eRealm.Hibernia, Level = level, Item_Type = (int)slot,
                Object_Type = armor ? (int)eObjectType.Cloth : (int)eObjectType.Magical, Quality = quality,
                MaxCount = 1, PackSize = 1, MaxCondition = 10000, MaxDurability = 10000, DPS_AF = armor ? level : 0, Price = 100,
                IsDropable = true, IsTradable = true,
            };
            DbInventoryItem item = GameInventoryItem.Create(template);
            item.Quality = quality;
            item.IsCrafted = crafted;
            for (int i = 0; i < bonuses.Length && i < 10; i++)
                SetBonus(item, i + 1, (int)bonuses[i].Type, bonuses[i].Value);
            return item;
        }

        public static void SetBonus(DbInventoryItem item, int index, int type, int value)
        {
            switch (index)
            {
                case 1: item.Bonus1Type = type; item.Bonus1 = value; break;
                case 2: item.Bonus2Type = type; item.Bonus2 = value; break;
                case 3: item.Bonus3Type = type; item.Bonus3 = value; break;
                case 4: item.Bonus4Type = type; item.Bonus4 = value; break;
                case 5: item.Bonus5Type = type; item.Bonus5 = value; break;
                case 6: item.Bonus6Type = type; item.Bonus6 = value; break;
                case 7: item.Bonus7Type = type; item.Bonus7 = value; break;
                case 8: item.Bonus8Type = type; item.Bonus8 = value; break;
                case 9: item.Bonus9Type = type; item.Bonus9 = value; break;
                case 10: item.Bonus10Type = type; item.Bonus10 = value; break;
            }
        }

        /// <summary>Wears the piece in the slot (as loaded gear) and refreshes the item-bonus table.</summary>
        public static void Wear(GearBot bot, eInventorySlot slot, DbInventoryItem item)
        {
            if (!bot.Inventory.AddItem(slot, item))
                throw new InvalidOperationException($"could not wear {item.Name} in {slot}");
            bot.RefreshItemBonuses();
        }

        /// <summary>Puts the piece in the first free backpack slot.</summary>
        public static void Carry(GearBot bot, DbInventoryItem item)
        {
            eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid || !bot.Inventory.AddItem(slot, item))
                throw new InvalidOperationException("backpack full");
        }
    }
}
