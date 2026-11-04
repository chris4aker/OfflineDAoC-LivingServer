using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DOL.Database;
using DOL.GS;
using DOL.GS.Quests;
using DOL.GS.ServerProperties;
using DOL.UnitTests.QuestHarness;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The quest harness on synthetic quest rows (docs/QUESTING.md, Living Server 1.2): every step type driven to the
    /// finish on the server's own quest code, with rewards, XP at the XP-by-level rate and markers asserted. The real
    /// rows ship inside the game; UT_QuestHarnessWorld walks those from a world database given by path.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_QuestHarness
    {
        private const string Giver = "Master Edric";
        private const string Lady = "Lady Elaine";
        private const string Mob = "arawnite messenger";
        private const string ClassicType = "DOL.GS.Quests.ClassicQuestStep";

        private static readonly long Level20Span = GamePlayer.GetExperienceAmountForLevel(20) - GamePlayer.GetExperienceAmountForLevel(19);
        private static readonly long Level40Span = GamePlayer.GetExperienceAmountForLevel(40) - GamePlayer.GetExperienceAmountForLevel(39);

        private QuestHarness.QuestHarness _harness;

        private bool _questXpByLevel;
        private string _xpRateByLevel;

        [SetUp]
        public void SetUp()
        {
            _questXpByLevel = Properties.QUEST_XP_BY_LEVEL;
            _xpRateByLevel = Properties.XP_RATE_BY_LEVEL;
            Properties.QUEST_XP_BY_LEVEL = true;
            Properties.XP_RATE_BY_LEVEL = string.Empty;
        }

        [TearDown]
        public void TearDown()
        {
            _harness?.Dispose();
            _harness = null;
            Properties.QUEST_XP_BY_LEVEL = _questXpByLevel;
            Properties.XP_RATE_BY_LEVEL = _xpRateByLevel;
        }

        private QuestHarness.QuestHarness Harness(QuestWorld world, ClassicQuests.Config classic = null)
        {
            _harness?.Dispose();
            _harness = new QuestHarness.QuestHarness(world, classic);
            return _harness;
        }

        private static QuestWorld World(params DbDataQuest[] rows)
        {
            var world = new QuestWorld();
            foreach (DbDataQuest row in rows)
                world.Add(row);
            world.Add(QuestWorld.Template("cq_letter", "sealed letter"), QuestWorld.Template("cq_pelt", "wolf pelt"),
                QuestWorld.Template("cq_fang", "wolf fang"), QuestWorld.Template("cq_reward_ring", "ring of thanks"),
                QuestWorld.Template("cq_orders", "arawnite orders"), QuestWorld.Template("cq_totem", "totem"),
                QuestWorld.Template("cq_choice_a", "a blade"), QuestWorld.Template("cq_ear", "an ear"),
                QuestWorld.Template("cq_coin", "a coin"));
            return world;
        }

        /// <summary>A standard quest row: the giver in region 1, one entry per step in every per-step column.</summary>
        private static DbDataQuest Row(int id, string name, params (DataQuest.eStepType Type, string Target, string Item, string Collect, string Advance)[] steps)
        {
            string Join(Func<(DataQuest.eStepType Type, string Target, string Item, string Collect, string Advance), string> part) =>
                string.Join("|", steps.Select(part));
            return new DbDataQuest
            {
                ID = id, Name = name, StartType = (byte)DataQuest.eStartType.Standard, StartName = Giver, StartRegionID = 1,
                AcceptText = name, Description = "Will you help me with [" + name + "]?", MinLevel = 20, MaxLevel = 25, MaxCount = 1,
                SourceText = Join(_ => "Go on."), StepType = Join(s => ((int)s.Type).ToString()),
                StepText = Join(s => s.Type + " " + s.Target), TargetName = Join(s => s.Target + ";1"), TargetText = Join(_ => "Well done."),
                StepItemTemplates = Join(s => s.Item ?? string.Empty), CollectItemTemplate = Join(s => s.Collect ?? string.Empty),
                AdvanceText = Join(s => s.Advance ?? string.Empty),
                RewardXP = Join(_ => "0"), RewardMoney = Join(_ => "0"), RewardCLXP = Join(_ => "0"), RewardRP = Join(_ => "0"), RewardBP = Join(_ => "0"),
                FinishText = "Thank you.",
            };
        }

        private static (DataQuest.eStepType, string, string, string, string) S(DataQuest.eStepType type, string target, string item = null,
            string collect = null, string advance = null) => (type, target, item, collect, advance);

        private static void AssertFinished(QuestWalk walk)
        {
            Assert.That(walk.Passed, Is.True, walk.ToString());
            Assert.That(walk.Log, Is.Empty, "the quest code logged an error: " + walk);
        }

        // Interact, and the markers

        [Test]
        public void AnInteractQuestIsFinishedWithItsRewardsAndMarkers()
        {
            DbDataQuest row = Row(1001, "Errand", S(DataQuest.eStepType.Interact, Lady), S(DataQuest.eStepType.InteractFinish, Giver));
            row.RewardXP = "1000|2000";
            row.RewardMoney = "10|20";
            row.FinalRewardItemTemplates = "cq_reward_ring";
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);

            AssertFinished(walk);
            Assert.Multiple(() =>
            {
                Assert.That(walk.GiverMarkerBefore, Is.EqualTo(eQuestIndicator.Available), "a yellow knot over the giver before");
                Assert.That(walk.Steps[0].TargetMarker, Is.EqualTo(eQuestIndicator.Pending), "Lady Elaine is the current step");
                Assert.That(walk.Steps[1].TargetMarker, Is.EqualTo(eQuestIndicator.Finish), "the giver finishes it");
                Assert.That(walk.Experience, Is.EqualTo(3000), "1000 for the first step, 2000 at the finish");
                Assert.That(walk.Money, Is.EqualTo(30));
                Assert.That(walk.ItemsReceived, Does.Contain("cq_reward_ring"));
                Assert.That(_harness.Npc(Giver).GetQuestIndicator(_harness.Player), Is.EqualTo(eQuestIndicator.None), "a quest done once is not offered again");
            });
        }

        // Kills: by the player, by a groupmate, by a gamebot in the group

        private static DbDataQuest KillRow(int id) =>
            Row(id, "Messenger Hunt", S(DataQuest.eStepType.Kill, Mob), S(DataQuest.eStepType.InteractFinish, Giver));

        [TestCase(KillBy.Player)]
        [TestCase(KillBy.Groupmate)]
        [TestCase(KillBy.Gamebot)]
        public void AKillStepCountsWhoeverInThePlayersGroupMakesTheKill(KillBy killBy)
        {
            DbDataQuest row = KillRow(1002);
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row, new WalkOptions { KillBy = killBy });
            AssertFinished(walk);
            Assert.That(walk.Steps[0].Action, Does.Contain(killBy.ToString()));
        }

        [Test]
        public void AKillStepWithADropChanceAdvancesWhenTheItemDrops()
        {
            DbDataQuest row = Row(1003, "Orders", S(DataQuest.eStepType.Kill, Mob, item: "cq_orders;35"),
                S(DataQuest.eStepType.DeliverFinish, Giver, item: "cq_orders"));
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            AssertFinished(walk);
            Assert.That(walk.Steps[1].Action, Does.Contain("give cq_orders"), "the dropped orders are handed in");
        }

        [Test]
        public void OneKillAdvancesOneKillStepWithIdleGroupmatesInRange()
        {
            // QUESTING.md defect 6 (fixed in 1.1.2): the killer's quests heard the kill again for each groupmate in range.
            DbDataQuest row = Row(1004, "Three Messengers", S(DataQuest.eStepType.Kill, Mob), S(DataQuest.eStepType.Kill, Mob),
                S(DataQuest.eStepType.KillFinish, Mob));
            QuestHarness.QuestHarness harness = Harness(World(row));
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 20);
            HarnessPlayer one = harness.NewPlayer(QuestWalker.DefaultClass, 20, name: "One", primary: false);
            HarnessPlayer two = harness.NewPlayer(QuestWalker.DefaultClass, 20, name: "Two", primary: false);
            HarnessGroups.Of(player, one, two);
            HarnessNpc giver = harness.Giver(row);
            harness.Whisper(giver, row.AcceptText);
            Assert.That(harness.Active(row.ID)?.Step, Is.EqualTo(1));

            harness.Kill(harness.Monster(Mob), player);
            Assert.That(harness.Active(row.ID)?.Step, Is.EqualTo(2), "one kill, one step");
        }

        // Deliver and collect

        [Test]
        public void ADeliveryHandsItsItemOnAcceptAndTakesItBack()
        {
            DbDataQuest row = Row(1005, "Letter", S(DataQuest.eStepType.Deliver, Lady, item: "cq_letter"), S(DataQuest.eStepType.InteractFinish, Giver));
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            AssertFinished(walk);
            Assert.That(walk.Steps[0].Action, Is.EqualTo("give cq_letter to Lady Elaine"), "the letter came with the quest");
            Assert.That(walk.ItemsReceived, Does.Not.Contain("cq_letter"), "and went to Lady Elaine");
        }

        [Test]
        public void ADeliveryRowWithNoCollectItemTemplateCanBeHandedIn()
        {
            // QUESTING.md defect 7: OnPlayerGiveItem returned at once when a row had no CollectItemTemplate at all, so a
            // delivery quest written without that column (only StepItemTemplates) could never be handed in.
            DbDataQuest row = Row(1006, "Letter Without Collect", S(DataQuest.eStepType.Deliver, Lady, item: "cq_letter"),
                S(DataQuest.eStepType.InteractFinish, Giver));
            row.CollectItemTemplate = null;
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            AssertFinished(walk);
        }

        [Test]
        public void ACollectStepTakesTheCountItNeeds()
        {
            DbDataQuest row = Row(1007, "Pelts", S(DataQuest.eStepType.Collect, Lady, collect: "cq_pelt;3"),
                S(DataQuest.eStepType.CollectFinish, Giver, collect: "cq_fang"));
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            AssertFinished(walk);
            Assert.That(_harness.CarriedIds(), Does.Not.Contain("cq_pelt").And.Not.Contain("cq_fang"), "all three pelts and the fang were taken");
        }

        // Whisper and search

        [Test]
        public void WhisperStepsAdvanceOnTheirKeywords()
        {
            DbDataQuest row = Row(1008, "Passwords", S(DataQuest.eStepType.Whisper, Lady, advance: "dragon"),
                S(DataQuest.eStepType.WhisperFinish, Giver, advance: "home"));
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            AssertFinished(walk);
            Assert.That(walk.Steps.Select(s => s.TargetMarker), Is.EqualTo(new eQuestIndicator?[] { eQuestIndicator.Pending, eQuestIndicator.Finish }));
        }

        [Test]
        public void ASearchStepAdvancesWhereItsAreaIs()
        {
            DbDataQuest row = Row(1009, "Lost Ring", S(DataQuest.eStepType.Search, string.Empty, item: "cq_reward_ring"),
                S(DataQuest.eStepType.InteractFinish, Giver));
            row.SourceName = "SEARCH;1;Search here for the ring;1;10000;10000;500;5";
            row.TargetName = "|" + Giver + ";1";
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            AssertFinished(walk);
            Assert.That(walk.ItemsReceived, Does.Contain("cq_reward_ring"), "the search found the ring");
        }

        [Test]
        public void ASearchStepWithoutItsAreaIsReported()
        {
            DbDataQuest row = Row(1010, "Lost Ring", S(DataQuest.eStepType.Search, string.Empty), S(DataQuest.eStepType.InteractFinish, Giver));
            row.TargetName = "|" + Giver + ";1";
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            Assert.That(walk.Passed, Is.False);
            Assert.That(walk.Stuck, Does.Contain("no SEARCH area"));
        }

        // The classic custom steps

        [Test]
        public void EveryClassicCustomStepIsDrivenToTheFinish()
        {
            const string waypoint = "Classic Step";
            var custom = new[]
            {
                new ClassicQuests.CustomStep { Kind = "travel", Region = 1, X = 20_000, Y = 20_000, Radius = 600 },
                new ClassicQuests.CustomStep { Kind = "use_item", Item = "cq_totem", Give = true, Consume = true, Region = 1, X = 21_000, Y = 21_000, Radius = 600 },
                new ClassicQuests.CustomStep { Kind = "has_item", Item = "cq_coin" },
                new ClassicQuests.CustomStep { Kind = "interact", Target = "Old Hermit" },
                new ClassicQuests.CustomStep { Kind = "group" },
                new ClassicQuests.CustomStep
                {
                    Kind = "trade", Target = "Trader Bryn", Trades = new() { ["cq_ear"] = "cq_coin" },
                    Drops = new() { new ClassicQuests.StepDrop { Mob = "bandit", Item = "cq_ear", Chance = 50 } },
                },
                new ClassicQuests.CustomStep { Kind = "choose", Target = "Smith Dunstan", Choices = new() { ["blade"] = "cq_choice_a" } },
                new ClassicQuests.CustomStep { Kind = "say", Words = "Ardhan mor", Region = 1, X = 22_000, Y = 22_000, Radius = 600 },
                new ClassicQuests.CustomStep { Kind = "die_to", Target = "Sacrificial Priest" },
                new ClassicQuests.CustomStep { Kind = "quest", Quest = "Some Earlier Quest" },
            };
            var steps = custom.Select(_ => S(DataQuest.eStepType.Interact, waypoint)).Append(S(DataQuest.eStepType.InteractFinish, Giver)).ToArray();
            DbDataQuest row = Row(20001, "Classic Walk", steps);
            row.ClassType = ClassicType;
            row.RewardXP = string.Join("|", steps.Select(_ => "0"));
            var info = new ClassicQuests.QuestInfo();
            info.Steps.Add(null); // 1-based
            foreach (ClassicQuests.CustomStep step in custom)
                info.Steps.Add(new ClassicQuests.StepInfo { Custom = step, Marker = new ClassicQuests.Point(1, 1, 2, 0) });
            info.Steps.Add(new ClassicQuests.StepInfo());
            var config = new ClassicQuests.Config();
            config.Quests[row.ID] = info;

            QuestWalk walk = QuestWalker.Walk(Harness(World(row), config), row);
            AssertFinished(walk);
            Assert.Multiple(() =>
            {
                Assert.That(walk.Steps.Take(custom.Length).Select(s => s.Action),
                    Is.EqualTo(custom.Select(c => c.Kind == "trade" ? "classic trade cq_ear to Trader Bryn" : "classic " + c.Kind)));
                Assert.That(walk.Steps.Take(custom.Length).All(s => s.MapMarker), Is.True, "each classic step showed its red map marker");
                Assert.That(walk.ItemsReceived, Does.Contain("cq_choice_a"), "the chosen reward");
                Assert.That(_harness.Carried("cq_totem"), Is.Null, "the totem was used up");
            });
        }

        // A classic step whose monsters drop what it collects (classic-quests.json Custom with Drops and no Kind): the
        // world report found 44 such quests (Hill Cat Hunt: hill cats drop the teeth Thordia collects).

        [TestCase(KillBy.Player)]
        [TestCase(KillBy.Gamebot)]
        public void AClassicCollectStepIsFarmedFromTheMonstersThatDropIt(KillBy killBy)
        {
            DbDataQuest row = Row(20003, "Wolf Pelts", S(DataQuest.eStepType.Collect, Lady, collect: "cq_pelt;2"),
                S(DataQuest.eStepType.InteractFinish, Giver));
            row.ClassType = ClassicType;
            var info = new ClassicQuests.QuestInfo();
            info.Steps.Add(null);
            info.Steps.Add(new ClassicQuests.StepInfo
            {
                Custom = new ClassicQuests.CustomStep { Drops = new() { new ClassicQuests.StepDrop { Mob = "grey wolf", Item = "cq_pelt", Chance = 100 } } },
            });
            info.Steps.Add(new ClassicQuests.StepInfo());
            var config = new ClassicQuests.Config();
            config.Quests[row.ID] = info;

            QuestWalk walk = QuestWalker.Walk(Harness(World(row), config), row, new WalkOptions { KillBy = killBy });
            AssertFinished(walk);
            Assert.That(walk.Steps[0].Action, Does.StartWith("kill grey wolf").And.Contain("give cq_pelt to Lady Elaine"),
                "the pelts came from the wolves, not from the harness");
        }

        // A classic step that spawns its monster from an NPC template (classic-quests.json Spawn): the world report's 12
        // NullReferenceExceptions were the template lookup logging on a logger the harness had not given a queue.

        [Test]
        public void AClassicEventMonsterIsMadeFromItsNpcTemplate()
        {
            DbDataQuest row = Row(20004, "Shades", S(DataQuest.eStepType.Kill, "restless shade"), S(DataQuest.eStepType.InteractFinish, Giver));
            row.ClassType = ClassicType;
            var info = new ClassicQuests.QuestInfo();
            info.Steps.Add(null);
            info.Steps.Add(new ClassicQuests.StepInfo
            {
                Spawn = new ClassicQuests.EventSpawn { Name = "restless shade", TemplateId = 60167680, Level = 12, TriggerRadius = 0 },
            });
            info.Steps.Add(new ClassicQuests.StepInfo());
            var config = new ClassicQuests.Config();
            config.Quests[row.ID] = info;
            QuestWorld world = World(row);
            world.NpcTemplates.Add(new DbNpcTemplate { TemplateId = 60167680, Name = "restless shade", Level = "12", Model = "445" });

            QuestWalk walk = QuestWalker.Walk(Harness(world, config), row);
            Assert.That(walk.Passed, Is.True, walk.ToString());
            Assert.That(walk.HarnessProblem, Is.Null);
        }

        [Test]
        public void ALoggerWithoutAQueueIsAHarnessProblemNotAQuestFailure()
        {
            Exception logged = null;
            try { new DOL.Logging.Logger(null).Error("an error on a logger nobody set up"); }
            catch (Exception ex) { logged = ex; }
            Exception elsewhere = null;
            try { string text = null; _ = text.Length; }
            catch (Exception ex) { elsewhere = ex; }

            Assert.Multiple(() =>
            {
                Assert.That(QuestWalker.HarnessProblemIn(logged), Does.Contain("logger"));
                Assert.That(QuestWalker.HarnessProblemIn(elsewhere), Is.Null, "any other exception is the quest code's");
            });
        }

        // Quest 401 (Human Leg XP Item): its row collects "Human_Leg", the item template is "Human_leg". The server finds
        // templates without case, so the leg exists and drops, but the collection compared ids with case.

        [Test]
        public void ACollectionQuestTakesItsItemWhateverTheCaseOfItsId()
        {
            var row = new DbDataQuest
            {
                ID = 1020, Name = "Human Leg XP Item", StartType = (byte)DataQuest.eStartType.Collection, StartName = "Dera", StartRegionID = 1,
                Description = "Hey, I am looking for Human Legs, do you have some?", SourceText = "Thank you for the Human Leg!",
                StepText = "I can't get this from you yet, come back when you are stronger!", TargetText = "Wow a Human Leg, do you have more?",
                CollectItemTemplate = "Human_Leg", MaxCount = 30, MinLevel = 14, MaxLevel = 16, RewardXP = "1",
            };
            QuestWorld world = World(row);
            world.Add(QuestWorld.Template("Human_leg", "Human Leg"));
            QuestWalk walk = QuestWalker.Walk(Harness(world), row);
            Assert.That(walk.Passed, Is.True, walk.ToString());
            Assert.That(walk.Experience, Is.GreaterThan(0));
        }

        // XP at the XP-by-level rate

        [Test]
        public void QuestXpFollowsTheXpByLevelRate()
        {
            Properties.XP_RATE_BY_LEVEL = "1-34:5;35-50:12";
            DbDataQuest row = Row(1011, "Errand", S(DataQuest.eStepType.Interact, Lady), S(DataQuest.eStepType.InteractFinish, Giver));
            long bubble = Level20Span / 100;
            row.RewardXP = $"{bubble}|{bubble}";
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row, new WalkOptions { Level = 20 });
            AssertFinished(walk);
            Assert.That(walk.Experience, Is.EqualTo(bubble * 5 * 2), "two payments at the 1-34 range's 5x");

            QuestHarness.QuestHarness harness = Harness(World(row));
            Properties.QUEST_XP_BY_LEVEL = false;
            walk = QuestWalker.Walk(harness, row, new WalkOptions { Level = 20 });
            AssertFinished(walk);
            Assert.That(walk.Experience, Is.EqualTo(bubble * 2), "the setting off: the row's own amount");
        }

        [Test]
        public void AtTwelveTimesAQuestPaysAtMostOneLevel()
        {
            Properties.XP_RATE_BY_LEVEL = "35-50:12";
            DbDataQuest row = Row(1012, "Big Errand", S(DataQuest.eStepType.InteractFinish, Giver));
            row.MinLevel = 40;
            row.MaxLevel = 45;
            row.RewardXP = (Level40Span / 10).ToString();
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            AssertFinished(walk);
            Assert.That(walk.Experience, Is.EqualTo(Level40Span), "12 x a bubble is more than a level: one level is paid");
            Assert.That(_harness.Player.Level, Is.EqualTo(41));
        }

        // Outlevelling a quest at high XP rates

        [Test]
        public void AQuestOutlevelledAfterTakingItCanStillBeHandedIn()
        {
            // QUESTING.md defect 2 (fixed in 1.1.2): hand-ins were refused above the quest's maximum level.
            DbDataQuest row = Row(1013, "Pelts", S(DataQuest.eStepType.Collect, Lady, collect: "cq_pelt"),
                S(DataQuest.eStepType.DeliverFinish, Giver, item: "cq_letter", collect: "cq_letter"));
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row, new WalkOptions { OutlevelAfterAccept = true });
            AssertFinished(walk);
            Assert.That(walk.Notes, Has.Some.Contains("levelled to 30"));
        }

        // Repeatable quests

        [Test]
        public void ARepeatableQuestIsOfferedAgainAfterItIsFinished()
        {
            // QUESTING.md defect 5: a finished data quest is at step 0, and AbstractQuest.IsDoingQuest() is true for any
            // step but -1, so CheckQuestQualification refused every repeat of a MaxCount > 1 quest.
            DbDataQuest row = Row(1014, "Daily Pelts", S(DataQuest.eStepType.CollectFinish, Giver, collect: "cq_pelt"));
            row.MaxCount = 3;
            QuestHarness.QuestHarness harness = Harness(World(row));
            QuestWalk first = QuestWalker.Walk(harness, row);
            AssertFinished(first);

            HarnessNpc giver = harness.Npc(Giver);
            DataQuest offer = giver.DataQuestList.Single();
            Assert.That(offer.CheckQuestQualification(harness.Player), Is.True, "offered a second time");
            Assert.That(giver.GetQuestIndicator(harness.Player), Is.EqualTo(eQuestIndicator.Available));

            harness.Whisper(giver, row.AcceptText);
            Assert.That(harness.Active(row.ID), Is.Not.Null, "taken a second time");
            harness.Give(giver, harness.Hand("cq_pelt"));
            Assert.That(harness.Finished(row.ID), Is.True, "finished a second time");
            Assert.That(harness.World.CharacterQuests.Single(c => c.DataQuestID == row.ID).Count, Is.EqualTo(2));

            harness.Whisper(giver, row.AcceptText);
            harness.Give(giver, harness.Hand("cq_pelt"));
            Assert.That(harness.World.CharacterQuests.Single(c => c.DataQuestID == row.ID).Count, Is.EqualTo(3));
            Assert.That(offer.CheckQuestQualification(harness.Player), Is.False, "three times is its maximum");
        }

        [TestCase(0, 1, 0, false, TestName = "MaxCount 0 stays offered once, as every such quest was before 1.2")]
        [TestCase(0, 1, 1, false)]
        [TestCase(0, 1, 3, true)]
        [TestCase(0, 2, 3, true)]
        [TestCase(0, 3, 3, false)]
        [TestCase(2, 1, 3, false, TestName = "Not while it is being done")]
        public void WhenAFinishedQuestIsOfferedAgain(int step, int timesDone, int maxCount, bool offered)
            => Assert.That(QuestRepeats.OffersAgain(step, timesDone, maxCount), Is.EqualTo(offered));

        [Test]
        public void AQuestWithNoCountLimitIsStillOfferedOnce()
        {
            DbDataQuest row = Row(1019, "Unlimited", S(DataQuest.eStepType.InteractFinish, Giver));
            row.MaxCount = 0;
            QuestHarness.QuestHarness harness = Harness(World(row));
            AssertFinished(QuestWalker.Walk(harness, row));
            Assert.That(harness.Npc(Giver).DataQuestList.Single().CheckQuestQualification(harness.Player), Is.False);
        }

        [Test]
        public void AQuestDoneOnceIsNotOfferedAgain()
        {
            DbDataQuest row = Row(1015, "Once", S(DataQuest.eStepType.InteractFinish, Giver));
            QuestHarness.QuestHarness harness = Harness(World(row));
            AssertFinished(QuestWalker.Walk(harness, row));
            Assert.That(harness.Npc(Giver).DataQuestList.Single().CheckQuestQualification(harness.Player), Is.False);
        }

        // The harness reports why a quest can't be finished

        [Test]
        public void AMissingDeliveryItemIsReported()
        {
            // The delivery item is handed when the step before ends, so a missing template stops that step: the
            // player talks to Lady Elaine and nothing happens.
            DbDataQuest row = Row(1016, "Lost Letter", S(DataQuest.eStepType.Interact, Lady), S(DataQuest.eStepType.DeliverFinish, Giver, item: "cq_nowhere"));
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            Assert.That(walk.Passed, Is.False);
            Assert.That(walk.StepReached, Is.EqualTo(1));
            Assert.That(walk.Stuck, Does.Contain("StepItemTemplate cq_nowhere not found"));
        }

        [Test]
        public void AQuestWithoutAnAcceptKeywordIsReported()
        {
            DbDataQuest row = Row(1017, "Silent", S(DataQuest.eStepType.InteractFinish, Giver));
            row.AcceptText = null;
            QuestWalk walk = QuestWalker.Walk(Harness(World(row)), row);
            Assert.That(walk.Started, Is.False);
            Assert.That(walk.Stuck, Does.Contain("accept keyword"));
        }

        [Test]
        public void TheHarnessNeverWritesWorldData()
        {
            DbDataQuest row = KillRow(1018);
            QuestWorld world = World(row);
            QuestWalker.Walk(Harness(world), row);
            Assert.That(world.WorldWrites, Is.Empty);
        }

        // ClassicQuests' refresh runs on the game loop (QUESTING.md defect 13)

        [Test]
        public void TheClassicRefreshTimerOnlyQueuesTheRefreshForTheGameLoop()
        {
            var custom = new ClassicQuests.CustomStep { Kind = "travel", Region = 1, X = 10_000, Y = 10_000, Radius = 600 };
            DbDataQuest row = Row(20002, "Arrive", S(DataQuest.eStepType.Interact, "Classic Step"), S(DataQuest.eStepType.InteractFinish, Giver));
            row.ClassType = ClassicType;
            var info = new ClassicQuests.QuestInfo();
            info.Steps.Add(null);
            info.Steps.Add(new ClassicQuests.StepInfo { Custom = custom });
            info.Steps.Add(new ClassicQuests.StepInfo());
            var config = new ClassicQuests.Config();
            config.Quests[row.ID] = info;
            QuestHarness.QuestHarness harness = Harness(World(row), config);
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 20);
            harness.MoveTo(1, 10_000, 10_000);
            harness.Whisper(harness.Giver(row), row.AcceptText);
            Assert.That(harness.Active(row.ID)?.Step, Is.EqualTo(1), "at the travel step's point already");

            using (OnlinePlayers.Only(player))
            {
                GameLoopService.Instance.ProcessPostedActions(); // anything other tests left queued
                Task.Run(() => { ClassicQuests.OnRefreshTimer(null); ClassicQuests.OnRefreshTimer(null); }).Wait();
                Assert.That(harness.Active(row.ID)?.Step, Is.EqualTo(1), "the timer's thread changed no quest");

                GameLoopService.Instance.ProcessPostedActions();
                Assert.That(harness.Active(row.ID)?.Step, Is.EqualTo(2), "the game loop ran the refresh and the step completed");
            }
        }
    }
}
