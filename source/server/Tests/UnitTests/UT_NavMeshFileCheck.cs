using System;
using System.IO;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Port of rambodolphin13/DividedRealms a5f19f5c (their DR-376): damaged navmesh files are refused before the native loader reads them.</summary>
    [TestFixture]
    public class UT_NavMeshFileCheck
    {
        private static byte[] File(int tiles, int tileSize, bool truncate = false, int tileMagic = 0x444E4156)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(0x4d534554); writer.Write(1); writer.Write(tiles);
            writer.Write(new byte[28]);
            for (int i = 0; i < tiles; i++)
            {
                writer.Write((ulong)(i + 1)); writer.Write(tileSize); writer.Write(0);
                writer.Write(tileMagic); writer.Write(7); writer.Write(new byte[tileSize - 8]);
            }
            writer.Flush();
            byte[] bytes = stream.ToArray();
            return truncate ? bytes[..^10] : bytes;
        }

        private static string Check(byte[] bytes) => NavMeshFileCheck.Problem(new MemoryStream(bytes));

        [Test]
        public void AWholeFileIsAccepted() => Assert.That(Check(File(3, 64)), Is.Null);

        [Test]
        public void DamagedFilesAreRefused()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Check(File(3, 64, truncate: true)), Does.Contain("claims"), "cut short in the last tile");
                Assert.That(Check(File(2, 64, tileMagic: 0)), Does.Contain("not a Detour"), "zeroed tile data");
                Assert.That(Check(new byte[12]), Does.Contain("shorter than the header"));
                Assert.That(Check(File(0, 64)[..40]), Is.Null, "no tiles");
            });
        }

        [Test]
        public void EveryInstalledNavmeshPasses()
        {
            string folder = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            Assume.That(folder != null && Directory.Exists(Path.Combine(folder, "navmesh")));
            foreach (string path in Directory.GetFiles(Path.Combine(folder, "navmesh"), "*.nav"))
                Assert.That(NavMeshFileCheck.Problem(path), Is.Null, path);
        }
    }
}
