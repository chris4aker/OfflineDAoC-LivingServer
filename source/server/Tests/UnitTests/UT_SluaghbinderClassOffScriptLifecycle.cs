using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler.Client.v168;
using DOL.GS.Quests.Hibernia;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The 0.37 edition (classes/enable_sluaghbinder off): the Sluaghbinder epic runtime adds no handlers at load, so
    /// its unload must remove none. Seen on the owner's PC with 1.2 candidate 4be6fab16: every server stop logged
    /// "Error in Invoke. Event: Script.Unloaded" with ArgumentNullException ('chain') from ScriptUnloaded.
    /// Also the class-off refusal at character creation.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_SluaghbinderClassOffScriptLifecycle
    {
        private bool _enableSluaghbinder;
        private bool _loadQuests;
        private GameServer _previousServer;
        private GameEventMgr _previousEvents;
        private int _foreignEntered;

        private sealed class InertServer : GameServer
        {
            public IObjectDatabase TestDatabase;
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => TestDatabase;
        }

        /// <summary>A database that finds nothing and accepts every write (the armour templates are written at load).</summary>
        public class NullDatabase : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.ReturnType == typeof(bool))
                    return true;
                if (method.ReturnType == typeof(int))
                    return 0;
                return null;
            }
        }

        private static FieldInfo EventsInstance =>
            typeof(GameEventMgr).GetField("_soleInstance", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            _enableSluaghbinder = Properties.ENABLE_SLUAGHBINDER;
            _loadQuests = Properties.LOAD_QUESTS;
            _previousServer = GameServer.Instance;
            _previousEvents = (GameEventMgr)EventsInstance.GetValue(null);

            InertServer server = (InertServer)RuntimeHelpers.GetUninitializedObject(typeof(InertServer));
            server.TestDatabase = DispatchProxy.Create<IObjectDatabase, NullDatabase>();
            GameServer.LoadTestDouble(server);
            GameEventMgr.LoadTestDouble(new GameEventMgr());
            _foreignEntered = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Properties.ENABLE_SLUAGHBINDER = _enableSluaghbinder;
            Properties.LOAD_QUESTS = _loadQuests;
            GameEventMgr.LoadTestDouble(_previousEvents);
            GameServer.LoadTestDouble(_previousServer);
        }

        // Other scripts' handlers on the same events: with these present the old unload walked the chain to its end.
        private void ForeignEntered(DOLEvent e, object sender, EventArgs args) => _foreignEntered++;
        private static void ForeignQuit(DOLEvent e, object sender, EventArgs args) { }
        private static void ForeignAccept(DOLEvent e, object sender, EventArgs args) { }

        private void AddForeignHandlers()
        {
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, ForeignEntered);
            GameEventMgr.AddHandler(GamePlayerEvent.Quit, ForeignQuit);
            GameEventMgr.AddHandler(GamePlayerEvent.AcceptQuest, ForeignAccept);
        }

        [TestCase(false, true, TestName = "ClassOff_LoadThenUnload_AddsAndRemovesNothing")]
        [TestCase(true, false, TestName = "QuestsOff_LoadThenUnload_AddsAndRemovesNothing")]
        [TestCase(false, false, TestName = "ClassAndQuestsOff_LoadThenUnload_AddsAndRemovesNothing")]
        public void SwitchedOffLoadUnloadPair(bool classOn, bool loadQuests)
        {
            Properties.ENABLE_SLUAGHBINDER = classOn;
            Properties.LOAD_QUESTS = loadQuests;
            AddForeignHandlers();
            int before = GameEventMgr.NumGlobalHandlers;

            SluaghbinderEpicQuestRuntime.ScriptLoaded(ScriptEvent.Loaded, null, EventArgs.Empty);
            Assert.That(GameEventMgr.NumGlobalHandlers, Is.EqualTo(before), "a switched-off load adds no handler");
            Assert.That(SluaghbinderEpicQuestRuntime.QuestGiver, Is.Null);

            Assert.DoesNotThrow(() => SluaghbinderEpicQuestRuntime.ScriptUnloaded(ScriptEvent.Unloaded, null, EventArgs.Empty));
            Assert.That(GameEventMgr.NumGlobalHandlers, Is.EqualTo(before), "the unload leaves the other scripts' handlers");

            GameEventMgr.Notify(GamePlayerEvent.GameEntered, null, EventArgs.Empty);
            Assert.That(_foreignEntered, Is.EqualTo(1), "the other script's handler still fires");

            // A second stop (or an unload with no load) is harmless too.
            Assert.DoesNotThrow(() => SluaghbinderEpicQuestRuntime.ScriptUnloaded(ScriptEvent.Unloaded, null, EventArgs.Empty));
            Assert.That(GameEventMgr.NumGlobalHandlers, Is.EqualTo(before));
        }

        [Test]
        public void ClassOff_UnloadThroughTheScriptUnloadedEvent_LogsNoError()
        {
            // The real path: the server notifies Script.Unloaded and the chain invokes ScriptUnloaded, catching and
            // logging whatever it throws. Invoke it the same way and check the handler itself does not throw.
            Properties.ENABLE_SLUAGHBINDER = false;
            AddForeignHandlers();
            SluaghbinderEpicQuestRuntime.ScriptLoaded(ScriptEvent.Loaded, null, EventArgs.Empty);

            Exception thrown = null;
            void Unload(DOLEvent e, object sender, EventArgs args)
            {
                try { SluaghbinderEpicQuestRuntime.ScriptUnloaded(e, sender, args); }
                catch (Exception ex) { thrown = ex; }
            }
            GameEventMgr.AddHandler(ScriptEvent.Unloaded, Unload);
            GameEventMgr.Notify(ScriptEvent.Unloaded, null, EventArgs.Empty);
            Assert.That(thrown, Is.Null, thrown?.ToString());
        }

        [Test]
        public void RemovingAHandlerThatWasNeverAdded_LeavesTheChain()
        {
            DOLEventHandler kept = (_, _, _) => { };
            DOLEventHandler never = (_, _, _) => { };
            DOLEventHandlerChain chain = DOLEventHandlerChain.Subscribe(null, kept);

            Assert.That(DOLEventHandlerChain.Unsubscribe(null, never), Is.Null);
            Assert.That(DOLEventHandlerChain.Unsubscribe(chain, never), Is.SameAs(chain));
            Assert.That(DOLEventHandlerChain.Unsubscribe(chain, kept), Is.Null);

            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, kept);
            Assert.DoesNotThrow(() => GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, never));
            Assert.That(GameEventMgr.NumGlobalHandlers, Is.EqualTo(1));
        }

        [TestCase(eCharacterClass.MaulerHib)]
        [TestCase(eCharacterClass.Sluaghbinder)]
        [TestCase(eCharacterClass.Acolyte)]
        public void ClassOff_PlayerRequestForTheCustomClassIsRefused(eCharacterClass requested)
        {
            Assert.That(CharacterCreateRequestHandler.IsCustomClassRefused(false, eRealm.Hibernia, (int)requested, ePrivLevel.Player), Is.True);
        }

        [Test]
        public void ClassOff_RefusalTouchesOnlyTheCustomClassForPlayers()
        {
            Assert.That(CharacterCreateRequestHandler.IsCustomClassRefused(false, eRealm.Hibernia, (int)eCharacterClass.Naturalist, ePrivLevel.Player), Is.False);
            Assert.That(CharacterCreateRequestHandler.IsCustomClassRefused(false, eRealm.Albion, (int)eCharacterClass.Acolyte, ePrivLevel.Player), Is.False,
                "Albion's Acolyte is a classic class");
            Assert.That(CharacterCreateRequestHandler.IsCustomClassRefused(false, eRealm.Hibernia, (int)eCharacterClass.MaulerHib, ePrivLevel.GM), Is.False);
            Assert.That(CharacterCreateRequestHandler.IsCustomClassRefused(true, eRealm.Hibernia, (int)eCharacterClass.MaulerHib, ePrivLevel.Player), Is.False,
                "class on: the slot makes the novice");
        }

        /// <summary>
        /// The class decisions of CreateCharacter in its order: the class-on slot mapping, the custom-class refusal, then
        /// IsCharacterValid (realm, starting class, race, stats, gender). A player's request with legal stats (race base
        /// plus 10 in each of three stats: the 30-point budget). The world's disabled_classes list is not part of it.
        /// </summary>
        private static bool PlayerCreationAccepted(bool classOn, eRealm realm, eRace race, eCharacterClass requested, out int savedClass)
        {
            savedClass = CharacterCreateRequestHandler.MapCustomClassSlot(classOn, realm, (int)requested);
            if (CharacterCreateRequestHandler.IsCustomClassRefused(classOn, realm, savedClass, ePrivLevel.Player))
                return false;

            var stats = GlobalConstants.STARTING_STATS_DICT[race];
            var ch = new DbCoreCharacter
            {
                Realm = (int)realm, Race = (int)race, Class = savedClass, Level = 1, Gender = 0, AccountName = "classoff-test",
                Strength = stats[eStat.STR] + 10, Constitution = stats[eStat.CON] + 10, Dexterity = stats[eStat.DEX] + 10,
                Quickness = stats[eStat.QUI], Intelligence = stats[eStat.INT], Piety = stats[eStat.PIE],
                Empathy = stats[eStat.EMP], Charisma = stats[eStat.CHR],
            };
            return CharacterCreateRequestHandler.IsCharacterValid(ch);
        }

        // Class 16 is also Albion's Acolyte (Cleric, Friar, Heretic): the class-off refusal is for Hibernia only.
        [TestCase(eRace.Briton)]
        [TestCase(eRace.Avalonian)]
        [TestCase(eRace.Highlander)]
        public void AlbionAcolyteIsAcceptedOnBothEditions(eRace race)
        {
            foreach (bool classOn in new[] { false, true })
            {
                Assert.That(PlayerCreationAccepted(classOn, eRealm.Albion, race, eCharacterClass.Acolyte, out int saved), Is.True,
                    $"{race} Acolyte, class {(classOn ? "on" : "off")}");
                Assert.That(saved, Is.EqualTo((int)eCharacterClass.Acolyte));
            }
        }

        [TestCase(eCharacterClass.MaulerHib)]
        [TestCase(eCharacterClass.Sluaghbinder)]
        [TestCase(eCharacterClass.Acolyte)]
        public void ClassOff_HibernianCustomClassPathIsRefused(eCharacterClass requested)
        {
            Assert.That(PlayerCreationAccepted(false, eRealm.Hibernia, eRace.Celt, requested, out int saved), Is.False);
            Assert.That(saved, Is.EqualTo((int)requested), "class off: nothing is mapped");
        }

        [TestCase(eCharacterClass.MaulerHib)]
        [TestCase(eCharacterClass.Sluaghbinder)]
        public void ClassOn_TheSlotStillMakesTheNovice(eCharacterClass requested)
        {
            Assert.That(PlayerCreationAccepted(true, eRealm.Hibernia, eRace.Celt, requested, out int saved), Is.True);
            Assert.That(saved, Is.EqualTo((int)eCharacterClass.Acolyte));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClassicHibernianClassIsAcceptedOnBothEditions(bool classOn)
        {
            Assert.That(PlayerCreationAccepted(classOn, eRealm.Hibernia, eRace.Celt, eCharacterClass.Naturalist, out int saved), Is.True);
            Assert.That(saved, Is.EqualTo((int)eCharacterClass.Naturalist));
        }

        [Test]
        public void ClassOff_MaulerSlotIsNotAHibernianStartingClassEither()
        {
            // The second line of refusal (IsCharacterValid) for a GM or a world without the disabled_classes row.
            Assert.That(GlobalConstants.STARTING_CLASSES_DICT[eRealm.Hibernia], Does.Not.Contain(eCharacterClass.MaulerHib));
        }
    }
}
