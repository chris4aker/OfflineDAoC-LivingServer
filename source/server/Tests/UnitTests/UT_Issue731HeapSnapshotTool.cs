using System.Threading.Tasks;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Issue #731: rc.2's first heap snapshot exited -1 with no file, and nothing the tool said was kept. The pure parts
    /// of the tool run: its arguments, the logged command line, the bounded capture of its output and the exit code's
    /// meaning. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_Issue731HeapSnapshotTool
    {
        [Test]
        public void ArgumentsAskForTheTimelineAndALongerTimeout()
        {
            string[] arguments = HeapSnapshotTool.Arguments(4242, @"C:\Game\runtime\server\logs\heap-1.gcdump");
            Assert.That(arguments, Is.EqualTo(new[]
            {
                "collect", "-p", "4242", "-o", @"C:\Game\runtime\server\logs\heap-1.gcdump", "--timeout", "300", "-v"
            }));
            // The tool's timeout ends well before the server stops waiting for it.
            Assert.That(HeapSnapshotTool.ToolTimeoutSeconds * 1000, Is.LessThan(HeapSnapshotTool.WaitMilliseconds));
        }

        [Test]
        public void CommandLineQuotesOnlyWhatNeedsIt()
        {
            string line = HeapSnapshotTool.CommandLine(@"C:\Users\Some One\.dotnet\tools\dotnet-gcdump.exe",
                HeapSnapshotTool.Arguments(7, @"D:\Games\Living Server\logs\h.gcdump", 60));
            Assert.That(line, Is.EqualTo(
                "\"C:\\Users\\Some One\\.dotnet\\tools\\dotnet-gcdump.exe\" collect -p 7 -o \"D:\\Games\\Living Server\\logs\\h.gcdump\" --timeout 60 -v"));
            Assert.That(HeapSnapshotTool.CommandLine("tool", new[] { "", "a\"b" }), Is.EqualTo("tool \"\" \"a\\\"b\""));
        }

        [TestCase(null, "none")]
        [TestCase(0, "ok")]
        [TestCase(-1, "tool-failed")]
        [TestCase(unchecked((int)0x80008096u), "host-failed")] // FrameworkMissingFailure
        [TestCase(unchecked((int)0x80008083u), "host-failed")] // CoreHostLibMissingFailure
        [TestCase(1, "other")]
        public void ExitMeaningTellsTheToolFromTheHost(int? exit, string meaning)
        {
            Assert.That(HeapSnapshotTool.ExitMeaning(exit), Is.EqualTo(meaning));
        }

        [Test]
        public void ExitHexIsEightDigits()
        {
            Assert.That(HeapSnapshotTool.ExitHex(-1), Is.EqualTo("0xFFFFFFFF"));
            Assert.That(HeapSnapshotTool.ExitHex(unchecked((int)0x80008096u)), Is.EqualTo("0x80008096"));
            Assert.That(HeapSnapshotTool.ExitHex(0), Is.EqualTo("0x00000000"));
            Assert.That(HeapSnapshotTool.ExitHex(null), Is.EqualTo("none"));
        }

        [Test]
        public void ForRowIsOneLineWithoutDoubleQuotes()
        {
            Assert.That(HeapSnapshotTool.ForRow("Writing gcdump to 'x'...\r\n\t Failed to collect \"gcdump\".\n"),
                Is.EqualTo("Writing gcdump to 'x'... |   Failed to collect 'gcdump'."));
            Assert.That(HeapSnapshotTool.ForRow("a\n\n\nb\u0007"), Is.EqualTo("a | b"));
            Assert.That(HeapSnapshotTool.ForRow(""), Is.EqualTo("none"));
            Assert.That(HeapSnapshotTool.ForRow(null), Is.EqualTo("none"));
            Assert.That(HeapSnapshotTool.ForRow(" \r\n "), Is.EqualTo("none"));
        }

        [Test]
        public void ShortOutputIsKeptWhole()
        {
            var output = new HeapSnapshotTool.BoundedOutput(16, 16);
            output.AppendLine("first");
            output.AppendLine(null); // the end-of-stream event
            output.AppendLine("second");
            Assert.That(output.ToString(), Is.EqualTo("first\nsecond\n"));
            Assert.That(output.TotalChars, Is.EqualTo(13));
        }

        [Test]
        public void LongOutputKeepsItsStartAndItsEnd()
        {
            var output = new HeapSnapshotTool.BoundedOutput(10, 10);
            output.AppendLine("[ERROR] System.TimeoutException");
            for (int i = 0; i < 100; i++) output.AppendLine("   at Frame" + i);
            output.AppendLine("Failed.");
            string kept = output.ToString();
            Assert.That(kept, Does.StartWith("[ERROR] Sy"));
            Assert.That(kept, Does.EndWith("9\nFailed.\n"));
            long cut = output.TotalChars - 20;
            Assert.That(kept, Does.Contain($"[... {cut} chars cut ...]"));
        }

        [Test]
        public void ExactlyFullHeadAndTailCutsNothing()
        {
            var output = new HeapSnapshotTool.BoundedOutput(4, 4);
            output.AppendLine("abc");
            output.AppendLine("def");
            Assert.That(output.ToString(), Is.EqualTo("abc\ndef\n"));
        }

        [Test]
        public void ConcurrentLinesAreAllCounted()
        {
            var output = new HeapSnapshotTool.BoundedOutput(64, 64);
            Parallel.For(0, 1000, i => output.AppendLine("line"));
            Assert.That(output.TotalChars, Is.EqualTo(5000));
            Assert.That(output.ToString().Length, Is.LessThanOrEqualTo(64 + 64 + 40));
        }
    }
}
