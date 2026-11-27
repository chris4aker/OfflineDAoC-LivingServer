using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// #571 step 3 (port of True165 1630980, their GitHub #38): in Darkness Falls a bot a real player leads (a hired
/// companion, a gamebot in the player's group, a bot with a player owner) moves by the companion path rules that /spawn
/// and /bot companions already use (#438); only gamebots no player leads keep the certified-corridor gate and the
/// gamebot fall rules. Tests only: no PC log yet.
/// </summary>
[TestFixture]
public class UT_CompanionDarknessFallsPaths571
{
    private const long HiredBotId = 987_654_321_571;
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

    [TearDown]
    public void TearDown() => HiredMap().TryRemove(HiredBotId, out _);

    // ---- the pure rule ------------------------------------------------------------------------------------------------

    [Test]
    public void GamebotRules_OnlyForAnAutonomousBotNoPlayerLeads()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules(true, false), Is.True, "an unled gamebot keeps the gate");
            Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules(true, true), Is.False, "hired / in a player's group");
            Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules(false, true), Is.False, "/spawn and /bot companions");
            Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules(false, false), Is.False);
        });
    }

    // ---- each bot type ------------------------------------------------------------------------------------------------

    [Test]
    public void UnledGamebot_KeepsTheGamebotRules()
    {
        GameBot bot = Bot(autonomous: true);
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.IsPlayerLed(bot), Is.False);
            Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules(bot), Is.True);
            Assert.That(CompanionDarknessFallsPaths.KindOf(bot), Is.EqualTo("gamebot"));
        });
    }

    [Test]
    public void GamebotInAPlayersGroup_MovesAsACompanion()
    {
        GameBot bot = Bot(autonomous: true);
        Set(bot, nameof(GameBot.PlayerGroupLeader), Player());
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.IsPlayerLed(bot), Is.True);
            Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules(bot), Is.False);
            Assert.That(CompanionDarknessFallsPaths.KindOf(bot), Is.EqualTo("grouped"));
        });
    }

    [Test]
    public void HiredCompanion_MovesAsACompanion()
    {
        GameBot bot = Bot(autonomous: true);
        bot.DatabaseID = HiredBotId;
        HiredMap()[HiredBotId] = (OfflineHiredCompanionRecord) RuntimeHelpers.GetUninitializedObject(typeof(OfflineHiredCompanionRecord));
        Assert.Multiple(() =>
        {
            Assert.That(HiredCompanions165.IsHired(bot), Is.True);
            Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules(bot), Is.False);
            Assert.That(CompanionDarknessFallsPaths.KindOf(bot), Is.EqualTo("hired"));
        });
    }

    [Test]
    public void GamebotWithAPlayerOwner_MovesAsACompanion()
    {
        GameBot bot = Bot(autonomous: true);
        Set(bot, nameof(GameBot.Owner), Player());
        Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules(bot), Is.False);
    }

    [Test]
    public void SpawnAndBotCompanions_AreUnchanged()
    {
        GameBot bot = Bot(autonomous: false);
        Set(bot, nameof(GameBot.Owner), Player());
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules(bot), Is.False);
            Assert.That(CompanionDarknessFallsPaths.KindOf(bot), Is.EqualTo("companion"));
            Assert.That(CompanionDarknessFallsPaths.UsesGamebotRules((GameBot) null), Is.False);
            Assert.That(CompanionDarknessFallsPaths.IsPlayerLed(null), Is.False);
        });
    }

    // ---- the log row's rate limit -------------------------------------------------------------------------------------

    [Test]
    public void LogSlot_IsRateLimitedPerBot()
    {
        ConcurrentDictionary<long, long> next = new();
        const long interval = CompanionDarknessFallsPaths.LogIntervalMilliseconds;
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.LogDue(next, 1, 1_000), Is.True);
            Assert.That(CompanionDarknessFallsPaths.TryClaimLogSlot(next, 1, 1_000, interval), Is.True);
            Assert.That(CompanionDarknessFallsPaths.LogDue(next, 1, 1_000 + interval - 1), Is.False);
            Assert.That(CompanionDarknessFallsPaths.TryClaimLogSlot(next, 1, 1_000 + interval - 1, interval), Is.False);
            Assert.That(CompanionDarknessFallsPaths.TryClaimLogSlot(next, 2, 1_500, interval), Is.True, "per bot");
            Assert.That(CompanionDarknessFallsPaths.TryClaimLogSlot(next, 1, 1_000 + interval, interval), Is.True);
        });
    }

    // ---- wiring: the four DF sites use the one rule -------------------------------------------------------------------

    [Test]
    public void Pathfinder_GatesOnlyGamebotsNoPlayerLeads()
    {
        string calc = Between(Source("world", "Pathfinding", "Pathfinder.cs"), "private PathfindingStatus CalculatePath(",
            "if (pathfindingResult.Status is PathfindingStatus.BufferTooSmall)");
        int note = calc.IndexOf("CompanionDarknessFallsPaths.NoteUngatedPath(");
        int gate = calc.IndexOf("if (Owner is GameBot dfBot && CompanionDarknessFallsPaths.UsesGamebotRules(dfBot) &&");
        int noPath = calc.IndexOf("return PathfindingStatus.NoPathFound;");
        Assert.Multiple(() =>
        {
            Assert.That(calc, Does.Not.Contain("Owner is GameBot { IsAutonomousWorldBot: true } &&"),
                "the gate no longer keys on IsAutonomousWorldBot alone");
            Assert.That(note, Is.GreaterThan(0), "the player-led bot's bypassed rejection is logged");
            Assert.That(gate, Is.GreaterThan(note));
            Assert.That(noPath, Is.GreaterThan(gate), "NoPathFound only behind the gamebot gate");
            Assert.That(calc.IndexOf("return PathfindingStatus.NoPathFound;", noPath + 1), Is.LessThan(0));
            Assert.That(calc[..gate], Does.Not.Contain("TryStrictDarknessFallsPrefix"),
                "the old-gate check for the log never rewrites the companion's path");
        });
    }

    [Test]
    public void Mover_FallsClimbsAndReordersByTheOneRule()
    {
        string mover = Source("ECS-Components", "NpcMovementComponent.cs");
        string climb = Between(mover, "private bool TryClimbDarknessFallsStairs(", "if (!AutonomousDarknessFallsNavigation.TryPlanStairClimb(");
        string pathTo = Between(mover, "public void PathTo(Vector3 destination, short speed)", "if (StairClimbUnderWay())");
        string fallStep = Between(mover, "bool hasNextNode = _pathfinder.TryGetNextNode(", "_fallContinuation = new(air, landing, destination,");
        Assert.Multiple(() =>
        {
            Assert.That(climb, Does.Contain("CompanionDarknessFallsPaths.UsesGamebotRules(bot)\n                ? AutonomousDarknessFallsNavigation.CertifiedFallLinks"));
            Assert.That(pathTo, Does.Contain("bool gamebotRules = CompanionDarknessFallsPaths.UsesGamebotRules(fallingBot);"));
            Assert.That(pathTo, Does.Contain("if (!gamebotRules && IsFlagSet(MovementState.WalkTo))"));
            Assert.That(pathTo, Does.Not.Contain("IsAutonomousWorldBot"));
            Assert.That(fallStep, Does.Contain("(CompanionDarknessFallsPaths.UsesGamebotRules(bot)\n                        ? _pathfinder.PathfindingStatus == PathfindingStatus.PathFound"));
            Assert.That(fallStep, Does.Not.Contain("bot.IsAutonomousWorldBot"));
        });
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private static GameBot Bot(bool autonomous)
    {
        GameBot bot = (GameBot) RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        Set(bot, nameof(GameBot.IsAutonomousWorldBot), autonomous);
        return bot;
    }

    private static GamePlayer Player() => (GamePlayer) RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));

    private static void Set(GameBot bot, string name, object value) =>
        typeof(GameBot).GetProperty(name, Declared)!.SetValue(bot, value);

    private static ConcurrentDictionary<long, OfflineHiredCompanionRecord> HiredMap() =>
        (ConcurrentDictionary<long, OfflineHiredCompanionRecord>) typeof(HiredCompanions165)
            .GetField("Hired", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);

    private static string Between(string text, string start, string end)
    {
        int a = text.IndexOf(start);
        Assert.That(a, Is.GreaterThanOrEqualTo(0), start);
        int b = text.IndexOf(end, a);
        Assert.That(b, Is.GreaterThan(a), end);
        return text[a..b];
    }

    private static string Source(params string[] parts)
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
            dir = Path.GetDirectoryName(dir);
        Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
        return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }
}
