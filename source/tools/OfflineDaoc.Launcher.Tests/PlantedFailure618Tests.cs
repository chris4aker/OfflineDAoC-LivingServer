using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>THROWAWAY (#618): proves the launcher check goes red. Reverted in the next commit.</summary>
public sealed class PlantedFailure618Tests
{
    [Test]
    public void APlantedLauncherFailure() => Assert.Fail("planted by #618 to prove the launcher gate");
}
