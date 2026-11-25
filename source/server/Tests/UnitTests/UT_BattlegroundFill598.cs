using System;
using System.IO;
using System.Linq;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.UnitTests
{
    /// <summary>
    /// #598 (owner, 2026-11-24: "build options A and C. No floor for now"). A: the new default goal shares and the one-time
    /// upgrade of a saved file that still holds the 1.2.2 defaults. C: a whole bot group goes to a battleground together at
    /// its task's end, its members counted against the battleground share.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BattlegroundFill598
    {
        private string _directory;
        private string PathName => Path.Combine(_directory, BotGoalSettings.FileName);

        [SetUp]
        public void Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "bg-fill-598-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Environment.SetEnvironmentVariable(AutonomousBotGoalPolicy.OverrideVariable, null);
            AutonomousBotGoalPolicy.Initialize(_directory); // no file: the code defaults
        }

        [TearDown]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable(AutonomousBotGoalPolicy.OverrideVariable, null);
            if (File.Exists(PathName)) File.Delete(PathName);
            AutonomousBotGoalPolicy.Initialize(_directory);
            Directory.Delete(_directory, true);
        }

        // ---- A: defaults ----

        [Test]
        public void NewDefaultShares()
        {
            BotGoalSettings defaults = BotGoalSettings.Defaults;
            Assert.Multiple(() =>
            {
                Assert.That(defaults.Levels1To19, Is.EqualTo(new BotGoalWeights(55, 35, 0, 10)));
                Assert.That(defaults.Levels20To49, Is.EqualTo(new BotGoalWeights(30, 30, 15, 25)));
                Assert.That(defaults.Level50, Is.EqualTo(new BotGoalWeights(20, 40, 40, 0)), "level 50 unchanged");
                Assert.That(BotGoalSettings.PreviousDefaults.Levels1To19, Is.EqualTo(new BotGoalWeights(60, 40, 0, 0)));
                Assert.That(BotGoalSettings.PreviousDefaults.Levels20To49, Is.EqualTo(new BotGoalWeights(35, 35, 15, 15)));
            });
            Assert.DoesNotThrow(() => defaults.Validate());
        }

        [Test]
        public void TheShareStillAppliesToTheBracketBotsOnly()
        {
            // 1-19: 10% of the 15-19 bots (here 200 of 600), never of the whole band.
            var low = AutonomousObjectiveAssignments.LimitBattlegrounds(AutonomousObjectiveAssignments.TargetForLowLevelPopulation(600), 200, 600);
            Assert.That(low.Battlegrounds, Is.EqualTo(20));
            Assert.That(low.SoloPve + low.GroupPve + low.Battlegrounds, Is.EqualTo(600));
        }

        // ---- A: the one-time upgrade ----

        [Test]
        public void AFileWithTheUntouchedOldDefaultsMovesToTheNewOnes()
        {
            BotGoalSettings.PreviousDefaults.Save(PathName, () => true);
            Assert.That(BotGoalSettings.UpgradeUntouchedDefaults(PathName), Is.True);
            Assert.That(BotGoalSettings.Load(PathName), Is.EqualTo(BotGoalSettings.Defaults));
            Assert.That(BotGoalSettings.UpgradeUntouchedDefaults(PathName), Is.False, "already the new defaults");
        }

        [Test]
        public void TheOwnersOwnValuesAreNeverTouched()
        {
            // The owner's own rows from #598 (40/50/0/10, 30/40/10/20, 20/40/40/0).
            var owner = new BotGoalSettings
            {
                Levels1To19 = new(40, 50, 0, 10), Levels20To49 = new(30, 40, 10, 20), Level50 = new(20, 40, 40),
            };
            owner.Save(PathName, () => true);
            byte[] before = File.ReadAllBytes(PathName);
            Assert.That(BotGoalSettings.UpgradeUntouchedDefaults(PathName), Is.False);
            Assert.That(File.ReadAllBytes(PathName), Is.EqualTo(before));
            // One row changed from the old default is the player's choice too.
            (BotGoalSettings.PreviousDefaults with { Level50 = new(30, 30, 40) }).Save(PathName, () => true);
            Assert.That(BotGoalSettings.UpgradeUntouchedDefaults(PathName), Is.False);
        }

        [Test]
        public void NoFileOrABrokenFileIsLeftAlone()
        {
            Assert.That(BotGoalSettings.UpgradeUntouchedDefaults(PathName), Is.False);
            Assert.That(File.Exists(PathName), Is.False, "a world with no file already uses the new defaults");
            File.WriteAllText(PathName, "{ not json");
            Assert.That(BotGoalSettings.UpgradeUntouchedDefaults(PathName), Is.False);
            Assert.That(File.ReadAllText(PathName), Is.EqualTo("{ not json"));
        }

        [Test]
        public void TheUpgradeRunsOnceThroughTheSettingsVersion()
        {
            Assert.That(LivingServerSettingsUpgrade.Version, Is.EqualTo("1.2.3"));
            Assert.That(LivingServerSettingsUpgrade.PlanFileChanges("1.2.1").Select(change => change.File),
                Is.EqualTo(new[] { BotGoalSettings.FileName }));
            Assert.That(LivingServerSettingsUpgrade.PlanFileChanges(""), Has.Length.EqualTo(1), "a world that never recorded a build");
            Assert.That(LivingServerSettingsUpgrade.PlanFileChanges("1.2.3"), Is.Empty, "once only");
            Assert.That(LivingServerSettingsUpgrade.PlanFileChanges("1.3"), Is.Empty);
            // No database row changes in 1.2.3.
            Assert.That(LivingServerSettingsUpgrade.Changes.Where(change => change.Since == "1.2.3"), Is.Empty);
        }

        [Test]
        public void ThePolicyUpgradesTheServersFileAndReloads()
        {
            BotGoalSettings.PreviousDefaults.Save(PathName, () => true);
            AutonomousBotGoalPolicy.Initialize(_directory);
            Assert.That(AutonomousBotGoalPolicy.Settings, Is.EqualTo(BotGoalSettings.PreviousDefaults));
            Assert.That(AutonomousBotGoalPolicy.UpgradeUntouchedDefaults(), Is.True);
            Assert.That(AutonomousBotGoalPolicy.Settings, Is.EqualTo(BotGoalSettings.Defaults));
            Assert.That(BotGoalSettings.Load(PathName), Is.EqualTo(BotGoalSettings.Defaults));
        }

        // ---- C: a group drawn together ----

        private static bool Can(int[] levels, bool onlyGamebots = true, bool hostsPlayer = false, bool inRaid = false,
            bool dead = false, bool fighting = false, bool bagsFull = false, Func<int, bool> allowed = null) =>
            AutonomousBotGroupCoordinator.CanGoToBattlegroundTogether(levels, onlyGamebots, hostsPlayer, inRaid, dead, fighting,
                bagsFull, allowed ?? (_ => true));

        [Test]
        public void AGroupInOneBracketAtItsBreakMayGoTogether()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Can([15, 17, 19, 18]), Is.True, "Abermenai");
                Assert.That(Can([20, 24, 22, 21, 23, 20, 24, 22]), Is.True, "Thidranki, a full group");
                Assert.That(Can([30, 35]), Is.True, "Caledonia");
                Assert.That(Can([19, 20]), Is.False, "two brackets");
                Assert.That(Can([14, 15]), Is.False, "level 14 has no bracket");
                Assert.That(Can([35, 36]), Is.False, "level 36 has no bracket");
                Assert.That(Can([17]), Is.False, "one bot is not a group");
                Assert.That(Can(Enumerable.Repeat(17, 9).ToArray()), Is.False);
            });
        }

        [Test]
        public void NeverAPlayersGroupARaidOrAGroupThatCannotGoNow()
        {
            int[] levels = [16, 17, 18];
            Assert.Multiple(() =>
            {
                Assert.That(Can(levels, onlyGamebots: false), Is.False, "a player or a companion in the group");
                Assert.That(Can(levels, hostsPlayer: true), Is.False, "a party hosting the player");
                Assert.That(Can(levels, inRaid: true), Is.False);
                Assert.That(Can(levels, dead: true), Is.False, "a member waiting for a resurrection");
                Assert.That(Can(levels, fighting: true), Is.False, "never mid-fight");
                Assert.That(Can(levels, bagsFull: true), Is.False, "a nearly full bag sells at the break first");
                Assert.That(Can(levels, allowed: level => level != 18), Is.False, "the goal must be allowed for every member");
            });
        }

        [Test]
        public void TheWholeGroupMustFitTheOpenSlotsWithASingleBotsOdds()
        {
            var open = new AutonomousObjectiveAssignments.OpenSlots(10, 0);
            Assert.That(AutonomousObjectiveAssignments.GroupTakesBattleground(open, 8, 0.99), Is.True, "only battleground slots open");
            Assert.That(AutonomousObjectiveAssignments.GroupTakesBattleground(new(5, 5), 8, 0.0), Is.False, "the group does not fit");
            Assert.That(AutonomousObjectiveAssignments.GroupTakesBattleground(new(8, 8), 8, 0.4), Is.True);
            Assert.That(AutonomousObjectiveAssignments.GroupTakesBattleground(new(8, 8), 8, 0.6), Is.False, "8 of 16 open: half the time");
            Assert.That(AutonomousObjectiveAssignments.GroupTakesBattleground(new(0, 0), 2, 0.0), Is.False);
            Assert.That(AutonomousObjectiveAssignments.GroupTakesBattleground(new(8, 0), 1, 0.0), Is.False, "a group has two or more");
        }

        [Test]
        public void TheGroupsMembersAreCountedAgainstTheShare()
        {
            // 16 slots open in Midgard's 1-19 band: two groups of eight take them; a third finds none.
            AutonomousObjectiveAssignments.SetOpenSlotsForTest(eRealm.Midgard, 0, new(16, 0));
            Assert.That(AutonomousObjectiveAssignments.TryReserveGroupBattleground(eRealm.Midgard, 0, 8, 0.5), Is.True);
            Assert.That(AutonomousObjectiveAssignments.TryReserveGroupBattleground(eRealm.Midgard, 0, 8, 0.5), Is.True);
            Assert.That(AutonomousObjectiveAssignments.TryReserveGroupBattleground(eRealm.Midgard, 0, 2, 0.0), Is.False);
            // Another realm and band are their own share.
            AutonomousObjectiveAssignments.SetOpenSlotsForTest(eRealm.Albion, 20, new(5, 0));
            Assert.That(AutonomousObjectiveAssignments.TryReserveGroupBattleground(eRealm.Albion, 20, 6, 0.0), Is.False, "6 > 5 open");
            Assert.That(AutonomousObjectiveAssignments.TryReserveGroupBattleground(eRealm.Albion, 20, 5, 0.0), Is.True);
            Assert.That(AutonomousObjectiveAssignments.TryReserveGroupBattleground(eRealm.Hibernia, 20, 2, 0.0), Is.False,
                "no pass has published Hibernia's 20-49 band");
        }

        [Test]
        public void AGroupIsKeptTogetherOnOneBattlegroundTask()
        {
            DateTime ends = DateTime.UtcNow.AddMinutes(80);
            OfflineWorldBotRecord[] members = Enumerable.Range(0, 4).Select(i => new OfflineWorldBotRecord
                { Level = 16 + i, ObjectiveKind = "GroupPve", ObjectiveAssignmentId = "old", CurrentCampId = "camp" }).ToArray();
            foreach (OfflineWorldBotRecord member in members)
                AutonomousObjectiveAssignments.AssignGroupBattleground(member, "bgtrip-midgard-x-001", ends, "Leader", "Abermenai");
            Assert.Multiple(() =>
            {
                // The next pass counts a group-owned bot by its goal (never redraws it): four battleground goals.
                Assert.That(members.Select(member => AutonomousObjectiveAssignments.Parse(member.ObjectiveKind)),
                    Has.All.EqualTo(eAutonomousObjectiveKind.Battleground));
                Assert.That(members.Select(member => member.ObjectiveAssignmentId).Distinct(), Is.EqualTo(new[] { "bgtrip-midgard-x-001" }));
                Assert.That(members.Select(member => member.ObjectiveExpiresUtc).Distinct().Count(), Is.EqualTo(1), "one end for the group");
                Assert.That(members.Select(member => member.CurrentCampId), Has.All.Empty);
                Assert.That(members.All(member => AutonomousObjectiveAssignments.HasActivePveAssignment(member, DateTime.UtcNow)), Is.True,
                    "a live battleground task, so the pass leaves it alone");
            });
        }

        [Test]
        public void TheBandsMatchTheAllocator()
        {
            Assert.That(new[] { 15, 19, 20, 35, 49, 50 }.Select(AutonomousObjectiveAssignments.BandOf), Is.EqualTo(new[] { 0, 0, 20, 20, 20, 50 }));
        }
    }
}
