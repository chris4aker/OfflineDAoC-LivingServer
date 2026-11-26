using System.Reflection;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>
/// 0.36c: the new client's launcher (new-client\runtime) runs the original game's server (runtime), so both clients
/// share one account, one bot roster and one world.
/// </summary>
public sealed class NewClientLauncherTests
{
    private static readonly Assembly Launcher = Assembly.Load("DAoCLivingServer");
    private static readonly BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

    private static string ServerRootFor(string folder) =>
        (string)Launcher.GetType("OfflineDaoc.Launcher.MainForm")!.GetMethod("ServerRootFor", HiddenStatic)!.Invoke(null, [folder])!;

    private static bool IsNewClientFolder(string folder) =>
        (bool)Launcher.GetType("OfflineDaoc.Launcher.MainForm")!.GetMethod("IsNewClientFolder", HiddenStatic)!.Invoke(null, [folder])!;

    [Test]
    public void NewClientLauncherUsesTheOriginalGamesServer()
    {
        string game = Path.Combine(Path.GetTempPath(), "odc-newclient-" + Guid.NewGuid().ToString("N"));
        string runtime = Path.Combine(game, "runtime");
        string newRuntime = Path.Combine(game, "new-client", "runtime");
        Directory.CreateDirectory(runtime);
        Directory.CreateDirectory(newRuntime);
        Directory.CreateDirectory(Path.Combine(game, "new-client", "godot"));
        File.WriteAllText(Path.Combine(game, "new-client", "godot", "project.godot"), "");
        try
        {
            File.WriteAllText(Path.Combine(newRuntime, "server_root.txt"), "# the shared server\r\n..\\..\\runtime\r\n");
            Assert.That(ServerRootFor(newRuntime), Is.EqualTo(runtime), "the new client's launcher runs the original game's server");
            Assert.That(IsNewClientFolder(newRuntime), Is.True, "the new client sits beside new-client\\runtime");
            Assert.That(IsNewClientFolder(runtime), Is.False, "the original game's launcher keeps the original client");
            Assert.That(ServerRootFor(runtime), Is.EqualTo(runtime), "without server_root.txt a launcher runs its own folder");
        }
        finally { Directory.Delete(game, true); }
    }

    [Test]
    public void MissingServerFolderFallsBackToTheLaunchersOwnFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "odc-newclient-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "server_root.txt"), "..\\does-not-exist\\runtime\r\n");
            Assert.That(ServerRootFor(folder), Is.EqualTo(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)));
        }
        finally { Directory.Delete(folder, true); }
    }
}
