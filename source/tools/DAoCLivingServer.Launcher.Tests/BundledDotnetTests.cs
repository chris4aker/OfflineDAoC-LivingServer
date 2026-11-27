#nullable enable
using NUnit.Framework;

namespace DAoCLivingServer.Launcher.Tests;

/// <summary>Issue #732: START SERVER uses the game's bundled .NET (tools\dotnet) whatever the launcher's own
/// environment, and says so when the server closes within seconds.</summary>
public sealed class BundledDotnetTests
{
    private string _game = "";
    private string _runtime = "";

    [SetUp]
    public void SetUp()
    {
        _game = Path.Combine(Path.GetTempPath(), "bundled-dotnet-" + Guid.NewGuid().ToString("N"));
        _runtime = Path.Combine(_game, "runtime");
        Directory.CreateDirectory(_runtime);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_game))
            Directory.Delete(_game, true);
    }

    private string MakeBundled()
    {
        string dotnet = Path.Combine(_game, "tools", "dotnet");
        Directory.CreateDirectory(Path.Combine(dotnet, "host", "fxr", "10.0.11"));
        Directory.CreateDirectory(Path.Combine(dotnet, "shared", "Microsoft.NETCore.App", "10.0.11"));
        return Path.GetFullPath(dotnet);
    }

    [Test]
    public void Bundled_Present_SetsTheThreeVariablesTheStartFileSets()
    {
        string dotnet = MakeBundled();
        var environment = new Dictionary<string, string?> { ["DOTNET_ROOT"] = @"C:\Program Files\dotnet", ["PATH"] = "x" };
        Assert.That(BundledDotnet.Apply(environment, _runtime), Is.EqualTo(dotnet));
        Assert.That(environment["DOTNET_ROOT"], Is.EqualTo(dotnet));
        Assert.That(environment["DOTNET_ROOT_X64"], Is.EqualTo(dotnet));
        Assert.That(environment["DOTNET_MULTILEVEL_LOOKUP"], Is.EqualTo("0"));
        Assert.That(environment["PATH"], Is.EqualTo("x"));
    }

    [Test]
    public void Bundled_Absent_LeavesTheEnvironmentUntouched()
    {
        var environment = new Dictionary<string, string?> { ["DOTNET_ROOT"] = "inherited", ["PATH"] = "x" };
        Assert.That(BundledDotnet.Apply(environment, _runtime), Is.Null);
        Assert.That(environment, Is.EqualTo(new Dictionary<string, string?> { ["DOTNET_ROOT"] = "inherited", ["PATH"] = "x" }));
    }

    [Test]
    public void Bundled_EmptyFolder_IsNotARuntime()
    {
        Directory.CreateDirectory(Path.Combine(_game, "tools", "dotnet"));
        Assert.That(BundledDotnet.Find(_runtime), Is.Null);
        var environment = new Dictionary<string, string?>();
        Assert.That(BundledDotnet.Apply(environment, _runtime), Is.Null);
        Assert.That(environment, Is.Empty);
    }

    [Test]
    public void EarlyExit_BundledMissing_SaysReinstallOrExtractAgain()
    {
        string? message = BundledDotnet.EarlyExitMessage(TimeSpan.FromSeconds(2), unchecked((int)0x80008096), false, false);
        Assert.That(message, Does.Contain("closed 2 seconds after START SERVER"));
        Assert.That(message, Does.Contain("exit code -2147450730"));
        Assert.That(message, Does.Contain("tools\\dotnet"));
        Assert.That(message, Does.Contain("Reinstall the game"));
        Assert.That(message, Does.Contain("extract the whole download again"));
        Assert.That(message, Does.Not.Contain("START OFFLINE DAOC.cmd"));
    }

    [Test]
    public void EarlyExit_WithBundled_PointsAtTheServerLog()
    {
        string? message = BundledDotnet.EarlyExitMessage(TimeSpan.FromSeconds(1), 1, false, true);
        Assert.That(message, Does.Contain("exit code 1"));
        Assert.That(message, Does.Contain("closed 1 second after START SERVER"));
        Assert.That(message, Does.Contain("end of runtime\\logs\\server-console.log"));
        Assert.That(message, Does.Not.Contain("START OFFLINE DAOC.cmd"));
        Assert.That(message, Does.Not.Contain("Reinstall"));
    }

    [Test]
    public void EarlyExit_VeryFast_SaysRightAfter()
    {
        string? message = BundledDotnet.EarlyExitMessage(TimeSpan.FromMilliseconds(300), 1, false, true);
        Assert.That(message, Does.Contain("closed right after START SERVER"));
        Assert.That(message, Does.Not.Contain("0 seconds"));
    }

    [Test]
    public void EarlyExit_NoExitCode_StillShown()
    {
        string? message = BundledDotnet.EarlyExitMessage(TimeSpan.Zero, null, false, false);
        Assert.That(message, Is.Not.Null);
        Assert.That(message, Does.Not.Contain("exit code"));
    }

    [Test]
    public void NoMessage_WhenAskedToStop_OrAfterTheWindow()
    {
        Assert.That(BundledDotnet.EarlyExitMessage(TimeSpan.FromSeconds(1), 0, true, false), Is.Null);
        Assert.That(BundledDotnet.EarlyExitMessage(BundledDotnet.EarlyExitWindow, 1, false, false), Is.Null);
        Assert.That(BundledDotnet.EarlyExitMessage(TimeSpan.FromMinutes(5), 1, false, false), Is.Null);
    }
}
