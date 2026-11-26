using System;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using DAoCLivingServer.Configuration;

[TestFixture, NonParallelizable]
public class UT_BotGoalSettings
{
    private string _directory;
    private string PathName => Path.Combine(_directory, BotGoalSettings.FileName);
    [SetUp] public void Setup() { _directory = Path.Combine(Path.GetTempPath(), "bot-goal-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }
    [TearDown] public void Cleanup()
    {
        File.Delete(PathName);
        AutonomousBotGoalPolicy.Initialize(_directory);
        Directory.Delete(_directory, true);
    }
    private void Apply(BotGoalWeights weights)
    {
        (BotGoalSettings.Defaults with { Levels20To49 = weights, Level50 = weights }).Save(PathName, () => true);
        AutonomousBotGoalPolicy.Initialize(_directory);
    }

    [TestCase(1, 55, 35, 0, 10)] [TestCase(19, 55, 35, 0, 10)]
    [TestCase(20, 30, 30, 15, 25)] [TestCase(49, 30, 30, 15, 25)] [TestCase(50, 20, 40, 40, 0)]
    public void DefaultsAndBrackets(int level, int solo, int group, int rvr, int battlegrounds) =>
        Assert.That(BotGoalSettings.Defaults.ForLevel(level), Is.EqualTo(new BotGoalWeights(solo, group, rvr, battlegrounds)));

    [TestCase(100, 0, 0, 0)] [TestCase(0, 100, 0, 1)] [TestCase(0, 0, 100, 2)]
    public void ExclusivePoliciesApplyToAllocationRollsRecoveryAndOldEligibility(int solo, int group, int rvr, int kind)
    {
        Apply(new(solo, group, rvr));
        foreach (int level in new[] { 20, 49, 50 })
        {
            for (int seed = 0; seed < 300; seed++)
                Assert.That((int)AutonomousBotGoalPolicy.Choose(level, new Random(seed)), Is.EqualTo(kind));
            foreach (eAutonomousObjectiveKind previous in Enum.GetValues<eAutonomousObjectiveKind>())
                Assert.That((int)AutonomousBotGoalPolicy.EnsureAllowed(level, previous), Is.EqualTo(kind));
            var record = new OfflineWorldBotRecord { Level = level, ObjectiveRvrEligibleUtc = AutonomousObjectiveAssignments.PveCompletionRequired };
            Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(record, DateTime.UtcNow), Is.EqualTo(rvr > 0));
        }
        foreach (bool fifty in new[] { false, true })
        {
            var allocation = AutonomousObjectiveAssignments.TargetForPopulation(101, fifty);
            Assert.That(allocation, Is.EqualTo(new AutonomousObjectiveAssignments.Allocation(solo > 0 ? 101 : 0, group > 0 ? 101 : 0, rvr > 0 ? 101 : 0)));
        }
    }

    [TestCase(0, 25, 75)] [TestCase(25, 0, 75)] [TestCase(25, 75, 0)]
    public void ZeroWeightsNeverAppearInMixedPolicies(int solo, int group, int rvr)
    {
        var weights = new BotGoalWeights(solo, group, rvr);
        var choices = Enumerable.Range(0, 10000).Select(i => weights.Choose(i / 10000d)).ToArray();
        Assert.That(choices.Count(i => i == 0), Is.EqualTo(solo * 100));
        Assert.That(choices.Count(i => i == 1), Is.EqualTo(group * 100));
        Assert.That(choices.Count(i => i == 2), Is.EqualTo(rvr * 100));
    }

    [TestCase(0, 100, 0, 1)] [TestCase(0, 50, 50, 2)] [TestCase(50, 50, 0, 0)]
    public void MatchmakingFallbackNeverInventsADisabledGoal(int solo, int group, int rvr, int expected)
    {
        var weights = new BotGoalWeights(solo, group, rvr);
        Assert.That(Enumerable.Range(0, 100).Select(i => weights.Choose(i / 100d, true)), Has.All.EqualTo(expected));
    }

    [Test]
    public void DisabledSavedTaskIsClearedWithoutTouchingProgress()
    {
        Apply(new(0, 0, 100));
        var record = new OfflineWorldBotRecord { Level = 50, Experience = 12345, Name = "PreserveMe", RegionId = 1, X = 456,
            ObjectiveKind = "GroupPve", ObjectiveAssignmentId = "old-group", ObjectiveExpiresUtc = DateTime.UtcNow.AddHours(1).ToString("O"),
            CurrentCampId = "old-camp", TargetName = "old-target", ObjectiveRvrEligibleUtc = AutonomousObjectiveAssignments.PveCompletionRequired };
        Assert.That(AutonomousBotGoalPolicy.ReconcileSavedAssignment(record), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(record.ObjectiveKind, Is.EqualTo("RvR"));
            Assert.That(record.ObjectiveAssignmentId, Is.Empty);
            Assert.That(record.ObjectiveExpiresUtc, Is.Empty);
            Assert.That(record.CurrentCampId, Is.Empty);
            Assert.That(record.ObjectiveRvrEligibleUtc, Is.Empty);
            Assert.That(record.Experience, Is.EqualTo(12345));
            Assert.That(record.X, Is.EqualTo(456));
            Assert.That(record.Name, Is.EqualTo("PreserveMe"));
        });
    }

    [Test]
    public void AllowedTaskAndNecessaryServicePhaseArePreserved()
    {
        Apply(new(0, 0, 100));
        foreach (var (kind, id) in new[] { ("RvR", "current-rvr"), ("SoloPve", "between-pve-services-T-1-123") })
        {
            var record = new OfflineWorldBotRecord { Level = 50, ObjectiveKind = kind, ObjectiveAssignmentId = id };
            Assert.That(AutonomousBotGoalPolicy.ReconcileSavedAssignment(record), Is.False);
            Assert.That(record.ObjectiveAssignmentId, Is.EqualTo(id));
        }
    }

    [Test]
    public void LowLevelExclusivePoliciesAndRoundingRemainLegal()
    {
        foreach (var weights in new[] { new BotGoalWeights(100, 0, 0), new BotGoalWeights(0, 100, 0), new BotGoalWeights(1, 99, 0) })
        {
            (BotGoalSettings.Defaults with { Levels1To19 = weights }).Save(PathName, () => true);
            AutonomousBotGoalPolicy.Initialize(_directory);
            for (int count = 1; count < 100; count++)
            {
                var target = AutonomousObjectiveAssignments.TargetForLowLevelPopulation(count);
                Assert.That(target.RvR, Is.Zero);
                Assert.That(target.SoloPve + target.GroupPve, Is.EqualTo(count));
                Assert.That(AutonomousBotGoalPolicy.Choose(19), Is.Not.EqualTo(eAutonomousObjectiveKind.RvR));
            }
        }
    }

    [Test]
    public void InvalidSettingsCannotReplaceWorkingFileAndServerStartRaceIsRejected()
    {
        BotGoalSettings.Defaults.Save(PathName, () => true);
        string original = File.ReadAllText(PathName);
        foreach (var bad in new[] { new BotGoalWeights(-1, 101, 0), new(1, 1, 1), new(0, 0, 0), new(0, 0, 100) })
            Assert.Throws<InvalidDataException>(() => (BotGoalSettings.Defaults with { Levels1To19 = bad }).Save(PathName, () => true));
        Assert.Throws<InvalidOperationException>(() => BotGoalSettings.Defaults.Save(PathName, () => false));
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => BotGoalSettings.Defaults.Save(PathName, () => ++calls == 1));
        Assert.That(File.ReadAllText(PathName), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }

    [Test]
    public void PortableRoundTripAndMissingFileLegacyBehavior()
    {
        AutonomousBotGoalPolicy.Initialize(_directory);
        Assert.That(AutonomousBotGoalPolicy.IsConfigured, Is.False);
        Apply(new(0, 0, 100));
        Assert.That(BotGoalSettings.Load(PathName).Level50, Is.EqualTo(new BotGoalWeights(0, 0, 100)));
        Assert.That(File.ReadAllText(PathName), Does.Not.Contain(_directory));
        Assert.That(AutonomousBotGoalPolicy.IsConfigured, Is.True);
    }

    // #669: the task-break draw of a world without bot-goals.json follows the default shares (personalities off: the draw
    // is the policy's own, as AutonomousBotPersonality.ChooseGoal returns it untouched). CompleteBetweenTaskServices,
    // BeginSoloAfterGroupTask and EndBattlegroundTour all go through TaskBreakGoal.
    private static double[] Shares(Func<Random, eAutonomousObjectiveKind> goal, int draws = 10_000)
    {
        var counts = new double[4];
        var random = new Random(669); // one seeded stream: consecutive seeds' first draws are too evenly spread to test a share
        for (int draw = 0; draw < draws; draw++)
            counts[(int)goal(random)]++;
        return counts.Select(n => n * 100 / draws).ToArray();
    }

    [TestCase(20)] [TestCase(22)] [TestCase(35)]
    public void NoFile_TaskBreakFollowsTheDefaultShares(int level)
    {
        AutonomousBotGoalPolicy.Initialize(_directory);
        Assert.That(AutonomousBotGoalPolicy.IsConfigured, Is.False);
        Assert.That(AutonomousBotGoalPolicy.Source, Is.EqualTo("defaults"));
        double[] shares = Shares(random =>
            AutonomousObjectiveAssignments.TaskBreakGoal(level, () => AutonomousBotGoalPolicy.Choose(level, random), random));
        TestContext.Out.WriteLine($"level {level}: solo {shares[0]:F1} group {shares[1]:F1} rvr {shares[2]:F1} bg {shares[3]:F1}");
        Assert.That(shares[0], Is.EqualTo(30).Within(2), "solo");
        Assert.That(shares[1], Is.EqualTo(30).Within(2), "group");
        Assert.That(shares[2], Is.EqualTo(15).Within(2), "RvR");
        Assert.That(shares[3], Is.EqualTo(25).Within(2), "battleground");
    }

    [Test]
    public void NoFile_TaskBreakAt15To19AndAbove35FollowsTheDefaults()
    {
        AutonomousBotGoalPolicy.Initialize(_directory);
        double[] low = Shares(random => AutonomousObjectiveAssignments.TaskBreakGoal(17, () => AutonomousBotGoalPolicy.Choose(17, random), random));
        Assert.That(low[0], Is.EqualTo(55).Within(2)); Assert.That(low[1], Is.EqualTo(35).Within(2));
        Assert.That(low[2], Is.Zero); Assert.That(low[3], Is.EqualTo(10).Within(2));
        // 36-49: no bracket, the battleground share goes to the others in their ratio (30/30/15 of 75).
        double[] high = Shares(random => AutonomousObjectiveAssignments.TaskBreakGoal(40, () => AutonomousBotGoalPolicy.Choose(40, random), random));
        Assert.That(high[0], Is.EqualTo(40).Within(2)); Assert.That(high[1], Is.EqualTo(40).Within(2));
        Assert.That(high[2], Is.EqualTo(20).Within(2)); Assert.That(high[3], Is.Zero);
    }

    [Test]
    public void NoFile_EndingABattlegroundTourNeverGivesABattleground()
    {
        AutonomousBotGoalPolicy.Initialize(_directory);
        double[] shares = Shares(random => AutonomousObjectiveAssignments.TaskBreakGoal(22,
            () => AutonomousBotGoalPolicy.Choose(22, random, excludeBattlegrounds: true), random));
        Assert.That(shares[3], Is.Zero);
        Assert.That(shares[2], Is.EqualTo(20).Within(2), "RvR 15 of 75");
    }

    [Test]
    public void NoFile_LevelFiftyIsUnchanged()
    {
        AutonomousBotGoalPolicy.Initialize(_directory);
        int drawn = 0;
        eAutonomousObjectiveKind Draw() { drawn++; return eAutonomousObjectiveKind.RvR; }
        // Level 50 without a file keeps its untilted draw from the shares (RollLevelFiftyObjective), never the tilted draw.
        double[] fifty = Shares(random => AutonomousObjectiveAssignments.TaskBreakGoal(50, Draw, random));
        Assert.That(drawn, Is.Zero);
        Assert.That(fifty[0], Is.EqualTo(20).Within(2)); Assert.That(fifty[1], Is.EqualTo(40).Within(2));
        Assert.That(fifty[2], Is.EqualTo(40).Within(2)); Assert.That(fifty[3], Is.Zero);
        for (int seed = 0; seed < 200; seed++)
            Assert.That(AutonomousObjectiveAssignments.TaskBreakGoal(50, Draw, new Random(seed)),
                Is.EqualTo(AutonomousObjectiveAssignments.RollLevelFiftyObjective(new Random(seed))));
    }

    // #669 follow-up (owner, 2026-11-26: remove the rule). A world without bot-goals.json behaves like one with saved
    // defaults: a bot just off an RvR tour is in the pass's RvR pool and draws the default shares at its next task break.
    [TestCase(22)] [TestCase(35)] [TestCase(49)]
    public void NoFile_BotJustOffRvrFollowsTheDefaultSharesAndIsInTheRvrPool(int level)
    {
        AutonomousBotGoalPolicy.Initialize(_directory);
        Assert.That(AutonomousBotGoalPolicy.IsConfigured, Is.False);
        // What BeginPveIntermission / BeginSoloAfterGroupTask leave on a bot just off RvR now: no marker.
        var record = new OfflineWorldBotRecord { Level = level, ObjectiveKind = "SoloPve", ObjectiveRvrEligibleUtc = string.Empty };
        Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(record, DateTime.UtcNow), Is.True, "in the pass's RvR pool");
        // Its next task break: the shares, not SoloPve (the draw is the only input; there is no just-off-RvR input any more).
        double[] shares = Shares(random =>
            AutonomousObjectiveAssignments.TaskBreakGoal(level, () => AutonomousBotGoalPolicy.Choose(level, random), random));
        TestContext.Out.WriteLine($"level {level}: solo {shares[0]:F1} group {shares[1]:F1} rvr {shares[2]:F1} bg {shares[3]:F1}");
        var expected = BotGoalSettings.Defaults.ForLevel(level);
        bool bracket = BattlegroundBrackets.LevelHasBracket(level);
        double scale = bracket ? 1 : 100d / (100 - expected.Battlegrounds);
        Assert.That(shares[0], Is.EqualTo(expected.SoloPve * scale).Within(2), "solo");
        Assert.That(shares[1], Is.EqualTo(expected.GroupPve * scale).Within(2), "group");
        Assert.That(shares[2], Is.EqualTo(expected.RvR * scale).Within(2), "RvR");
        Assert.That(shares[3], Is.EqualTo(bracket ? expected.Battlegrounds : 0).Within(2), "battleground");
    }

    [TestCase(22)] [TestCase(49)] [TestCase(50)]
    public void NoFile_SavedMarkerFromAnEarlierBuildIsIgnoredAndCleared(int level)
    {
        AutonomousBotGoalPolicy.Initialize(_directory);
        Assert.That(AutonomousBotGoalPolicy.IsConfigured, Is.False);
        var record = new OfflineWorldBotRecord { Level = level, ObjectiveKind = "SoloPve", ObjectiveAssignmentId = "old-solo",
            ObjectiveExpiresUtc = DateTime.UtcNow.AddHours(1).ToString("O"), Experience = 777, X = 12,
            ObjectiveRvrEligibleUtc = AutonomousObjectiveAssignments.PveCompletionRequired };
        // Ignored while it is still there ...
        Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(record, DateTime.UtcNow), Is.True);
        // ... and cleared when the bot loads (GameBot's constructor calls ReconcileSavedAssignment), keeping its task.
        Assert.That(AutonomousBotGoalPolicy.ReconcileSavedAssignment(record), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(record.ObjectiveRvrEligibleUtc, Is.Empty);
            Assert.That(record.Dirty, Is.True);
            Assert.That(record.ObjectiveKind, Is.EqualTo("SoloPve"));
            Assert.That(record.ObjectiveAssignmentId, Is.EqualTo("old-solo"));
            Assert.That(record.Experience, Is.EqualTo(777));
            Assert.That(record.X, Is.EqualTo(12));
        });
        Assert.That(AutonomousBotGoalPolicy.ReconcileSavedAssignment(record), Is.False, "once");
        // An old time-based value is ignored too.
        var timed = new OfflineWorldBotRecord { Level = level, ObjectiveRvrEligibleUtc = DateTime.UtcNow.AddHours(2).ToString("O") };
        Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(timed, DateTime.UtcNow), Is.True);
    }

    [Test]
    public void NoFile_MatchesASavedDefaultsFileBelowFifty()
    {
        var withoutFile = new bool[51];
        var noFileDraws = new eAutonomousObjectiveKind[51];
        AutonomousBotGoalPolicy.Initialize(_directory);
        Assert.That(AutonomousBotGoalPolicy.IsConfigured, Is.False);
        for (int level = 1; level <= 50; level++)
        {
            int l = level;
            withoutFile[level] = AutonomousObjectiveAssignments.IsRvrEligible(
                new OfflineWorldBotRecord { Level = level, ObjectiveRvrEligibleUtc = AutonomousObjectiveAssignments.PveCompletionRequired }, DateTime.UtcNow);
            if (level < 50)
                noFileDraws[level] = AutonomousObjectiveAssignments.TaskBreakGoal(l, () => AutonomousBotGoalPolicy.Choose(l, new Random(l)));
        }
        BotGoalSettings.Defaults.Save(PathName, () => true);
        AutonomousBotGoalPolicy.Initialize(_directory);
        Assert.That(AutonomousBotGoalPolicy.IsConfigured, Is.True);
        for (int level = 1; level <= 50; level++)
        {
            int l = level;
            var record = new OfflineWorldBotRecord { Level = level, ObjectiveRvrEligibleUtc = AutonomousObjectiveAssignments.PveCompletionRequired };
            Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(record, DateTime.UtcNow), Is.EqualTo(withoutFile[level]), $"level {level}");
            Assert.That(withoutFile[level], Is.EqualTo(BotGoalSettings.Defaults.ForLevel(level).RvR > 0), $"level {level}");
            if (level < 50)
                Assert.That(AutonomousObjectiveAssignments.TaskBreakGoal(l, () => AutonomousBotGoalPolicy.Choose(l, new Random(l))),
                    Is.EqualTo(noFileDraws[level]), $"level {level}");
        }
    }

    [Test]
    public void WithAFile_TaskBreakIsTheDrawAtEveryLevel()
    {
        Apply(new(0, 0, 100));
        Assert.That(AutonomousBotGoalPolicy.Source, Is.EqualTo("file"));
        foreach (int level in new[] { 22, 50 })
            Assert.That(AutonomousObjectiveAssignments.TaskBreakGoal(level, () => AutonomousBotGoalPolicy.Choose(level, new Random(1))),
                Is.EqualTo(eAutonomousObjectiveKind.RvR));
    }

    [Test]
    public void WithAFile_RvrEligibilityAndMarkerClearingAreUnchanged()
    {
        Apply(new(100, 0, 0));
        var record = new OfflineWorldBotRecord { Level = 30, ObjectiveKind = "SoloPve", ObjectiveAssignmentId = "keep",
            ObjectiveRvrEligibleUtc = AutonomousObjectiveAssignments.PveCompletionRequired };
        Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(record, DateTime.UtcNow), Is.False, "RvR share 0");
        Assert.That(AutonomousBotGoalPolicy.ReconcileSavedAssignment(record), Is.True);
        Assert.That(record.ObjectiveRvrEligibleUtc, Is.Empty);
        Assert.That(record.ObjectiveAssignmentId, Is.EqualTo("keep"));
        Apply(new(0, 0, 100));
        Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(record, DateTime.UtcNow), Is.True, "RvR share 100");
    }

    [Test]
    public void TheTestOverrideStillWins()
    {
        string other = Path.Combine(Path.GetTempPath(), "bot-goal-override-" + Guid.NewGuid().ToString("N") + ".json");
        string previous = Environment.GetEnvironmentVariable(AutonomousBotGoalPolicy.OverrideVariable);
        try
        {
            (BotGoalSettings.Defaults with { Levels20To49 = new(100, 0, 0, 0) }).Save(other, () => true);
            Environment.SetEnvironmentVariable(AutonomousBotGoalPolicy.OverrideVariable, other);
            AutonomousBotGoalPolicy.Initialize(_directory); // no bot-goals.json here: the override is the one loaded
            Assert.That(AutonomousBotGoalPolicy.IsConfigured, Is.True);
            Assert.That(AutonomousBotGoalPolicy.Source, Is.EqualTo("override"));
            for (int seed = 0; seed < 300; seed++)
                Assert.That(AutonomousObjectiveAssignments.TaskBreakGoal(22, () => AutonomousBotGoalPolicy.Choose(22, new Random(seed))),
                    Is.EqualTo(eAutonomousObjectiveKind.SoloPve));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AutonomousBotGoalPolicy.OverrideVariable, previous);
            File.Delete(other);
        }
    }

    [Test]
    public void StartupRowSaysWhichSettingsRan()
    {
        var rows = new System.Collections.Generic.List<string>();
        LivingRows.TestSink = rows.Add;
        try
        {
            AutonomousBotGoalPolicy.Initialize(_directory);
            Apply(new(0, 0, 100));
        }
        finally { LivingRows.TestSink = null; }
        Assert.That(rows.Count(r => r.StartsWith("BOT_GOALS ")), Is.EqualTo(2), "one row per start");
        Assert.That(rows[0], Is.EqualTo("BOT_GOALS source=defaults reason=start l1_19=55/35/0/10 l20_49=30/30/15/25 l50=20/40/40/0"));
        Assert.That(rows[1], Is.EqualTo("BOT_GOALS source=file reason=start l1_19=55/35/0/10 l20_49=0/0/100/0 l50=0/0/100/0"));
        Assert.That(rows.All(r => !r.Contains(_directory)), Is.True, "no path in the row");
    }

    [Test]
    public void GoalPassRow_CarriesThePoolAndSlots_NoNames()
    {
        var A = (int s, int g, int r, int b) => new AutonomousObjectiveAssignments.Allocation(s, g, r, b);
        string row = BotGoalRows.Pass(120_000, eRealm.Midgard, 20, 196, 2, 2, A(59, 59, 29, 49), A(59, 19, 17, 1), A(0, 1, 0, 1), true);
        Assert.That(row, Is.EqualTo("GOAL_PASS t=120000 realm=Midgard band=20 members=196 pool=2 bgEligible=2 target=59/59/29/49 " +
                                    "held=59/19/17/1 open=0/40/12/48 planned=0/1/0/1 personalities=on"));
        Assert.That(BotGoalRows.Pass(0, eRealm.Albion, 0, 1, 1, 0, A(1, 0, 0, 0), A(3, 0, 0, 0), A(0, 0, 0, 0), false),
            Does.Contain(" open=0/0/0/0 ").And.EndsWith("personalities=off"));
    }

    [Test]
    public void GoalPassRows_OnOnePassInFiveMinutes()
    {
        LivingRows.TestSink = _ => { };
        try
        {
            BotGoalRows.ResetForTest();
            Assert.That(BotGoalRows.PassDue(1_000), Is.True);
            Assert.That(BotGoalRows.PassDue(61_000), Is.False);
            Assert.That(BotGoalRows.PassDue(1_000 + BotGoalRows.PassIntervalMilliseconds), Is.True);
        }
        finally { LivingRows.TestSink = null; BotGoalRows.ResetForTest(); }
    }

    [TestCase("{}")] [TestCase("null")] [TestCase("not json")]
    [TestCase("{\"Version\":1,\"Levels1To19\":null,\"Levels20To49\":null,\"Level50\":null}")]
    public void BrokenFilesAreRejectedInsteadOfSilentlyRestoringDisabledGoals(string json)
    {
        File.WriteAllText(PathName, json);
        Assert.That(() => AutonomousBotGoalPolicy.Initialize(_directory), Throws.Exception);
    }
}
