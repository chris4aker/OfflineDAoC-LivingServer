using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>A craft request always gets an answer (player report 2026-11-08: level 40 endurance regen potions
    /// "did nothing, like the hotkey was dead"). The 1.130 client builds its recipe window from its own tdl.crf,
    /// so it offers recipe ids the world may lack or that belong to another realm.
    /// From rambodolphin13's DividedRealms (abe7b327), GPL-3.0, for Crafting 2.0 WP1 (M0's FindBy already returns null).</summary>
    [TestFixture, NonParallelizable]
    public class UT_CraftRequestReplies
    {
        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, NoRows>();

        /// <summary>A world with no rows at all: every lookup misses.</summary>
        public class NoRows : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                Type result = method.ReturnType;
                if (result == typeof(bool))
                    return false;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return result.IsValueType && result != typeof(void) ? Activator.CreateInstance(result) : null;
            }
        }

        private GameServer _previousServer;
        private bool _previousAdjustPrice;

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            _previousAdjustPrice = Properties.CRAFTING_ADJUST_PRODUCT_PRICE;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
        }

        [TearDown]
        public void TearDown()
        {
            Properties.CRAFTING_ADJUST_PRODUCT_PRICE = _previousAdjustPrice;
            GameServer.LoadTestDouble(_previousServer);
        }

        [TestCase(false, (ushort)65001)]
        [TestCase(true, (ushort)65002)]
        public void A_recipe_id_missing_from_the_world_comes_back_null_without_throwing(bool adjustPrice, ushort id)
        {
            // With price adjusting on, the null recipe used to be priced in FindBy's finally block and threw a
            // NullReferenceException out of the craft request, so the player's "not known here" reply never came.
            Properties.CRAFTING_ADJUST_PRODUCT_PRICE = adjustPrice;

            Recipe recipe = null;
            Assert.DoesNotThrow(() => recipe = RecipeDB.FindBy(id));
            Assert.That(recipe, Is.Null);
        }

        [TestCase(1, "Albion")]
        [TestCase(2, "Midgard")]
        [TestCase(3, "Hibernia")]
        [TestCase(0, "no realm")]
        public void Another_realms_recipe_is_refused_with_a_reason(int realm, string realmName)
        {
            var product = new DbItemTemplate { Name = "Strong Elixir of Invigoration", Realm = realm };

            string text = AbstractCraftingSkill.CraftRealmRefusal(product);

            Assert.That(text, Does.Contain("Strong Elixir of Invigoration"));
            Assert.That(text, Does.Contain(realmName));
            Assert.That(text, Does.Contain("/craft"));
        }
    }
}
