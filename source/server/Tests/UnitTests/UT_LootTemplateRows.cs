using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Owner 2026-10-31 audit: a mob with two MobXLootTemplate rows used to re-read its own loot table per row and carry
    // the list over, so its 100% items dropped twice and every other item was rolled three times, while the second
    // row's own table was never read. Each row now reads its own LootTemplateName once.
    [TestFixture, NonParallelizable]
    public class UT_LootTemplateRows
    {
        private sealed class Mob : GameNPC
        {
            public override byte Level { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Db;
        }

        // The generator only reads: Select* returns empty lists, FindObjectByKey<DbItemTemplate> returns Items[key].
        public class ReadOnlyDb : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "FindObjectByKey" && method.ReturnType == typeof(DbItemTemplate))
                    return Items.TryGetValue(Convert.ToString(args[0]) ?? string.Empty, out DbItemTemplate item) ? item : null;
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("loot test attempted a database write: " + method.Name);
                Type result = method.ReturnType;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return null;
            }
        }

        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, ReadOnlyDb>();
        private static readonly Dictionary<string, DbItemTemplate> Items = new(StringComparer.OrdinalIgnoreCase);

        // Gives the test access to the generator's loaded tables.
        private sealed class Generator : LootGeneratorTemplate
        {
            public static void Use(Dictionary<string, Dictionary<string, DbLootTemplate>> loot, Dictionary<string, List<DbMobXLootTemplate>> mobx)
            {
                m_lootTemplates = loot;
                m_mobXLootTemplates = mobx;
            }
        }

        private GameServer _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            Items.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Generator.Use(null, null);
            GameServer.LoadTestDouble(_previous);
        }

        private static T Inert<T>() where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(npc, new PropertyCollection());
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(npc, new ArrayList());
            return npc;
        }

        private static DbLootTemplate Row(string table, string item, int chance)
        {
            Items[item] = new DbItemTemplate { Id_nb = item, Name = item, Realm = 0 };
            return new DbLootTemplate { TemplateName = table, ItemTemplateID = item, Chance = chance, Count = 1 };
        }

        private static (int Fixed, int Random) Counts(LootList loot)
        {
            var f = (IList)typeof(LootList).GetField("m_fixedItemDrops", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loot);
            var r = (IList)typeof(LootList).GetField("m_randomItemDrops", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loot);
            return (f.Count, r.Count);
        }

        private static LootList Kill(string mobName, Dictionary<string, Dictionary<string, DbLootTemplate>> loot,
            List<DbMobXLootTemplate> rows)
        {
            Generator.Use(loot, new Dictionary<string, List<DbMobXLootTemplate>> { [mobName.ToLower()] = rows });
            var generator = (LootGeneratorTemplate)RuntimeHelpers.GetUninitializedObject(typeof(LootGeneratorTemplate));
            Mob mob = Inert<Mob>();
            mob.Name = mobName;
            return generator.GenerateLoot(mob, Owner());
        }

        // ResolveLootOwner accepts GamePlayer or GameBot; a GameBot test double built the same inert way.
        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override eRealm Realm { get; set; }
        }

        private static GameObject Owner()
        {
            Bot bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(bot, new PropertyCollection());
            bot.Realm = eRealm.Albion;
            return bot;
        }

        [Test]
        public void TwoRowsReadEachTableOnce()
        {
            var own = new Dictionary<string, DbLootTemplate>
            {
                ["sure_thing"] = Row("ghoulic viper", "sure_thing", 100),
                ["maybe_thing"] = Row("ghoulic viper", "maybe_thing", 30),
            };
            var other = new Dictionary<string, DbLootTemplate> { ["other_thing"] = Row("viper extras", "other_thing", 20) };
            LootList loot = Kill("Ghoulic Viper",
                new() { ["ghoulic viper"] = own, ["viper extras"] = other },
                [new DbMobXLootTemplate { MobName = "Ghoulic Viper", LootTemplateName = "Ghoulic Viper", DropCount = 2 },
                 new DbMobXLootTemplate { MobName = "Ghoulic Viper", LootTemplateName = "viper extras", DropCount = 1 }]);
            Assert.That(Counts(loot), Is.EqualTo((1, 2)), "the 100% item once, each other item rolled once");
        }

        [Test]
        public void ADeadRowNoLongerDoublesTheOwnTable()
        {
            var own = new Dictionary<string, DbLootTemplate> { ["fang"] = Row("ghoulic viper", "fang", 25) };
            LootList loot = Kill("Ghoulic Viper", new() { ["ghoulic viper"] = own },
                [new DbMobXLootTemplate { MobName = "Ghoulic Viper", LootTemplateName = "Ghoulic Viper", DropCount = 1 },
                 new DbMobXLootTemplate { MobName = "Ghoulic Viper", LootTemplateName = "ghoulic_viper_fang", DropCount = 1 }]);
            Assert.That(Counts(loot), Is.EqualTo((0, 1)), "rolled once, not three times");
        }

        [Test]
        public void OnlyADeadRowStillUsesTheOwnTable()
        {
            var own = new Dictionary<string, DbLootTemplate> { ["fang"] = Row("ghoulic viper", "fang", 25) };
            LootList loot = Kill("Ghoulic Viper", new() { ["ghoulic viper"] = own },
                [new DbMobXLootTemplate { MobName = "Ghoulic Viper", LootTemplateName = "ghoulic_viper_fang", DropCount = 1 }]);
            Assert.That(Counts(loot), Is.EqualTo((0, 1)), "no lootless mob when its only row names a missing table");
        }
    }
}
