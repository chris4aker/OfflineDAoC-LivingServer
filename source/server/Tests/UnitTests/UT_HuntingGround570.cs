using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #570 step 3: solo autonomous gamebots keep a hunting ground (AutonomousHuntingGround and
    /// AutonomousWorldBotController.HuntingGround.cs). Pure rules, plus source pins on the five wiring sites.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_HuntingGround570
    {
        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        private static string Tools(string file)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "source", "tools", file)))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the repository is above the tests");
            return File.ReadAllText(Path.Combine(dir, "source", "tools", file));
        }

        private bool _enabled;

        [SetUp]
        public void Remember() => _enabled = AutonomousHuntingGround.Enabled;

        [TearDown]
        public void Restore()
        {
            AutonomousHuntingGround.Enabled = _enabled;
            AutonomousHuntingGround.Forget(570_001);
        }

        [Test]
        public void TheSwitchIsOnInTheCodeAndInTheReleaseWorld()
        {
            FieldInfo field = typeof(AutonomousHuntingGround).GetField(nameof(AutonomousHuntingGround.Enabled));
            var attribute = field.GetCustomAttribute<ServerPropertyAttribute>();
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.Key, Is.EqualTo("bot_hunting_grounds"));
            Assert.That(attribute.DefaultValue, Is.EqualTo(true));
            Assert.That(Tools("release-server-settings.json"), Does.Contain("\"bot_hunting_grounds\": \"True\""));
        }

        [Test]
        public void TheZoneFactorKeepsTheBotNearItsGround()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousHuntingGround.ZoneFactor(0, 0, 200, 5, false), Is.EqualTo(1), "no ground yet: unchanged");
                Assert.That(AutonomousHuntingGround.ZoneFactor(200, 5, 200, 5, false), Is.EqualTo(8), "its own zone");
                Assert.That(AutonomousHuntingGround.ZoneFactor(200, 5, 200, 6, true), Is.EqualTo(3), "a neighbour");
                Assert.That(AutonomousHuntingGround.ZoneFactor(200, 5, 200, 9, false), Is.EqualTo(1), "elsewhere in the region");
                Assert.That(AutonomousHuntingGround.ZoneFactor(200, 5, 1, 5, true), Is.EqualTo(0.25), "another region");
                Assert.That(AutonomousHuntingGround.Weigh(3, 8), Is.EqualTo(24));
                Assert.That(AutonomousHuntingGround.Weigh(1, 0.25), Is.EqualTo(1), "a legal camp never drops to no chance");
                Assert.That(AutonomousHuntingGround.Weigh(12, 0.25), Is.EqualTo(3));
            });
        }

        [Test]
        public void SixGreenKillsOfTheLastEightMoveTheBotUp()
        {
            int fill = 0;
            byte bits = 0;
            bool[] kills = { true, true, false, true, true, true, false };
            foreach (bool green in kills)
            {
                (fill, bits) = AutonomousHuntingGround.AddKill(fill, bits, green);
                Assert.That(AutonomousHuntingGround.ShouldMoveUp(fill, bits), Is.False, "the window is not full yet");
            }
            (fill, bits) = AutonomousHuntingGround.AddKill(fill, bits, true);
            Assert.That(AutonomousHuntingGround.ShouldMoveUp(fill, bits), Is.True, "6 of the last 8 were green");
            (fill, bits) = AutonomousHuntingGround.AddKill(fill, bits, false);
            Assert.That(AutonomousHuntingGround.ShouldMoveUp(fill, bits), Is.False, "the oldest green fell out of the window");
        }

        [Test]
        public void FewerThanEightKillsNeverMoveUp()
        {
            int fill = 0;
            byte bits = 0;
            for (int i = 0; i < 7; i++)
                (fill, bits) = AutonomousHuntingGround.AddKill(fill, bits, true);
            Assert.That(AutonomousHuntingGround.ShouldMoveUp(fill, bits), Is.False);
        }

        [Test]
        public void TheMoveUpFlagIsSetOnceAtTheCurrentCampAndTakenOnce()
        {
            AutonomousHuntingGround.Enabled = true;
            const long bot = 570_001;
            AutonomousHuntingGround.NoteSoloCamp(bot, "camp-a", 200, 5);
            Assert.That(AutonomousHuntingGround.Home(bot), Is.EqualTo(((ushort)200, (ushort)5)));
            for (int i = 0; i < 7; i++)
                Assert.That(AutonomousHuntingGround.RecordKill(bot, "camp-a", true), Is.False);
            Assert.That(AutonomousHuntingGround.RecordKill(bot, "camp-b", true), Is.False, "a kill away from its camp");
            Assert.That(AutonomousHuntingGround.RecordKill(bot, "camp-a", true), Is.True, "the eighth green kill here");
            Assert.That(AutonomousHuntingGround.TakeMoveUp(bot), Is.True);
            Assert.That(AutonomousHuntingGround.TakeMoveUp(bot), Is.False, "taken once");

            // a new camp starts the count over
            for (int i = 0; i < 5; i++)
                AutonomousHuntingGround.RecordKill(bot, "camp-a", true);
            AutonomousHuntingGround.NoteSoloCamp(bot, "camp-c", 200, 6);
            for (int i = 0; i < 7; i++)
                Assert.That(AutonomousHuntingGround.RecordKill(bot, "camp-c", true), Is.False);
            Assert.That(AutonomousHuntingGround.Home(bot), Is.EqualTo(((ushort)200, (ushort)6)));

            AutonomousHuntingGround.Enabled = false;
            Assert.That(AutonomousHuntingGround.RecordKill(bot, "camp-c", true), Is.False, "switched off");
        }

        [Test]
        public void AMovedUpBotTakesABlueOrHarderCampWhenOneExists()
        {
            ConColor[] legal = { ConColor.GREEN, ConColor.BLUE, ConColor.GREEN, ConColor.YELLOW };
            Assert.That(AutonomousHuntingGround.MoveUpCamps(legal, con => con),
                Is.EqualTo(new[] { ConColor.BLUE, ConColor.YELLOW }));
            ConColor[] onlyGreen = { ConColor.GREEN, ConColor.GREEN };
            Assert.That(AutonomousHuntingGround.MoveUpCamps(onlyGreen, con => con), Is.EqualTo(onlyGreen),
                "nothing harder: every legal camp stays");
        }

        [Test]
        public void AreaHuntingIsForSoloBotsOutdoorsOutsideDarknessFalls()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousHuntingGround.AreaHuntingApplies(true, true, false, false), Is.True);
                Assert.That(AutonomousHuntingGround.AreaHuntingApplies(true, false, false, false), Is.False, "groups");
                Assert.That(AutonomousHuntingGround.AreaHuntingApplies(true, true, true, false), Is.False, "dungeons");
                Assert.That(AutonomousHuntingGround.AreaHuntingApplies(true, true, false, true), Is.False, "Darkness Falls");
                Assert.That(AutonomousHuntingGround.AreaHuntingApplies(false, true, false, false), Is.False, "switched off");
                Assert.That(AutonomousHuntingGround.WithinCampArea(1000, 1000, 1000 + 2600, 1000), Is.True);
                Assert.That(AutonomousHuntingGround.WithinCampArea(1000, 1000, 1000 + 2601, 1000), Is.False);
            });
        }

        [Test]
        public void AnAreaMonsterConsGreenUpToTheSoloCeiling()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousHuntingGround.AreaConAllowed(ConColor.GREY, ConColor.YELLOW), Is.False, "no experience");
                Assert.That(AutonomousHuntingGround.AreaConAllowed(ConColor.GREEN, ConColor.YELLOW), Is.True);
                Assert.That(AutonomousHuntingGround.AreaConAllowed(ConColor.YELLOW, ConColor.YELLOW), Is.True);
                Assert.That(AutonomousHuntingGround.AreaConAllowed(ConColor.ORANGE, ConColor.YELLOW), Is.False, "above the ceiling");
                Assert.That(AutonomousHuntingGround.AreaConAllowed(ConColor.YELLOW, ConColor.BLUE), Is.False,
                    "after deaths the ceiling is lower");
                Assert.That(AutonomousHuntingGround.AreaConAllowed(ConColor.UNKNOWN, ConColor.YELLOW), Is.False);
            });
        }

        [Test]
        public void TheMonsterStandingApartIsPulledFirst()
        {
            // candidate 0 stands in a pack of two others 300 away; candidate 1 stands alone
            Vector3[] candidates = { new(0, 0, 0), new(5_000, 0, 0) };
            Vector3[] pool = { new(0, 0, 0), new(300, 0, 0), new(0, 300, 0), new(5_000, 0, 0) };
            int[] counts = AutonomousHuntingGround.NeighbourCounts(candidates, new[] { 400, 400 }, pool);
            Assert.That(counts, Is.EqualTo(new[] { 2, 0 }), "a candidate does not count itself");
            Assert.That(AutonomousHuntingGround.IsolationOrder(counts), Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void IsolationKeepsTheGivenOrderAmongEqualsAndGreysLast()
        {
            Assert.That(AutonomousHuntingGround.IsolationOrder(new[] { 1, 0, 1, 0, 2 }), Is.EqualTo(new[] { 1, 3, 0, 2, 4 }));
            // index 1 is grey: it stays behind every non-grey even with no neighbours
            Assert.That(AutonomousHuntingGround.IsolationOrder(new[] { 2, 0, 1 }, new[] { 0, 1, 0 }),
                Is.EqualTo(new[] { 2, 0, 1 }));
        }

        [Test]
        public void AMonsterThatDoesNotAggroStillCountsItsFriendsNearby()
        {
            // aggro range 0 (not aggressive): its friends within 300 + 200 still come when it is pulled
            int[] counts = AutonomousHuntingGround.NeighbourCounts(new[] { new Vector3(0, 0, 0) }, new[] { 0 },
                new[] { new Vector3(450, 0, 0), new Vector3(600, 0, 0) });
            Assert.That(counts, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void HealersBardsAndArchersPreferBlueEveryoneElseYellow()
        {
            foreach (eCharacterClass c in new[] { eCharacterClass.Shaman, eCharacterClass.Healer, eCharacterClass.Druid,
                         eCharacterClass.Bard, eCharacterClass.Minstrel, eCharacterClass.Scout, eCharacterClass.Hunter,
                         eCharacterClass.Ranger })
                Assert.That(AutonomousHuntingGround.PreferredSoloCon(c), Is.EqualTo(ConColor.BLUE), c.ToString());
            foreach (eCharacterClass c in new[] { eCharacterClass.Cleric, eCharacterClass.Friar, eCharacterClass.Armsman,
                         eCharacterClass.Wizard, eCharacterClass.Berserker, eCharacterClass.Hero })
                Assert.That(AutonomousHuntingGround.PreferredSoloCon(c), Is.EqualTo(ConColor.YELLOW), c.ToString());
        }

        [Test]
        public void WithYellowPreferredTheSortIsExactlyTheOldOne()
        {
            foreach (ConColor con in new[] { ConColor.GREY, ConColor.GREEN, ConColor.BLUE, ConColor.YELLOW, ConColor.ORANGE })
                Assert.That(AutonomousHuntingGround.ConSortPenalty(con, ConColor.YELLOW), Is.EqualTo(Math.Abs((int)con) * 220));
            Assert.That(AutonomousHuntingGround.ConSortPenalty(ConColor.BLUE, ConColor.BLUE), Is.Zero);
            Assert.That(AutonomousHuntingGround.ConSortPenalty(ConColor.YELLOW, ConColor.BLUE), Is.EqualTo(220));
            Assert.That(AutonomousHuntingGround.ConSortPenalty(ConColor.GREEN, ConColor.BLUE), Is.EqualTo(220));
        }

        [Test]
        public void TheControllerWiresEveryRule()
        {
            string controller = Source("bots", "autonomous", "AutonomousWorldBotController.cs");
            string rules = Source("serverrules", "AbstractServerRules.cs");
            Assert.Multiple(() =>
            {
                Assert.That(controller, Does.Contain("if (TryMoveUpSpot(brain, bot))\n                return true;\n            GameNPC target = FindCampTarget(bot);"),
                    "the move-up runs in the camp loop before the target search");
                Assert.That(controller, Does.Contain("area ? IsAreaHuntTarget(bot, npc, nowTick) : AutonomousPveTargetPolicy.IsAssignedTarget("));
                Assert.That(controller, Does.Contain("if (found == null && AreaHuntingApplies(bot))"),
                    "the area is searched only when the camp's own monster is gone");
                Assert.That(controller, Does.Contain("AutonomousHuntingGround.ConSortPenalty(ConLevels.GetConColor(bot.GetConLevel(npc)), preferredCon)"));
                Assert.That(controller, Does.Contain("AutonomousHuntingGround.PreferIsolated(sorted, nearby,"));
                Assert.That(controller, Does.Contain("legal = ApplyMoveUp(legal);"));
                Assert.That(controller, Does.Contain("legalCells = WeighHuntingGround(bot, legalCells, destinations);"));
                Assert.That(controller, Does.Contain("NoteHuntingGround(bot, selected, sharedGroup);"));
                Assert.That(rules, Does.Contain("AutonomousHuntingGround.RecordKill(botToAward, killedNpc);"));
            });
            // the zone weights come after the quiet-camp filter, so camp competition still decides first
            Assert.That(controller.IndexOf("AutonomousCampCompetition.PreferQuiet(legalCells", StringComparison.Ordinal),
                Is.LessThan(controller.IndexOf("legalCells = WeighHuntingGround(", StringComparison.Ordinal)));
        }
    }
}
