using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace DOL.GS
{
    /// <summary>
    /// The pure parts of ServerMemoryDiagnostics' dotnet-gcdump run (issue #731): the tool's arguments, the command line
    /// as logged, the bounded capture of its stdout and stderr, and how its exit code reads. The rc.2 run's first
    /// snapshot (2026-11-26, liveMB=1603) exited -1 with no file and nothing kept of what the tool said.
    /// </summary>
    public static class HeapSnapshotTool
    {
        /// <summary>
        /// Passed as the tool's --timeout. dotnet-gcdump gives up after 30 s by default, counted from its start (type
        /// table flush included), then prints "Failed to collect gcdump" and exits -1 without a file. The October runs
        /// that worked had 615 MB of live data; rc.2's snapshot fired at 1,603 MB.
        /// </summary>
        public const int ToolTimeoutSeconds = 300;

        /// <summary>How long the server waits for the tool before it logs SERVER_HEAP_SNAPSHOT overran and stops it.</summary>
        public const int WaitMilliseconds = 600_000;

        /// <summary>Characters kept from the start and from the end of each stream (a .NET exception's message is at
        /// the start of what the tool prints; its timeline and verdict at the end).</summary>
        public const int HeadChars = 1024, TailChars = 1024;

        /// <summary>The tool's arguments: -v adds its own timeline ("Timed out after", "Assume no .NET Heap") to stdout.</summary>
        public static string[] Arguments(int processId, string file, int timeoutSeconds = ToolTimeoutSeconds) =>
        [
            "collect", "-p", processId.ToString(CultureInfo.InvariantCulture), "-o", file,
            "--timeout", timeoutSeconds.ToString(CultureInfo.InvariantCulture), "-v"
        ];

        /// <summary>The command line as a person would type it: arguments with a space or a quote are quoted.</summary>
        public static string CommandLine(string tool, IEnumerable<string> arguments) =>
            string.Join(" ", new[] { tool }.Concat(arguments).Select(Quote));

        private static string Quote(string argument)
        {
            if (string.IsNullOrEmpty(argument)) return "\"\"";
            return argument.IndexOfAny([' ', '\t', '"']) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// What an exit code says. 0: the tool wrote the dump. -1: dotnet-gcdump's own failure code (timeout, no heap
        /// events within 5 s, a failed connection to the server's diagnostics port, an unwritable output path). 0x8000808x
        /// and 0x800080Ax: the .NET host could not start the tool at all (no runtime it can roll forward to under
        /// DOTNET_ROOT, a broken install), so the tool never ran.
        /// </summary>
        public static string ExitMeaning(int? exitCode) => exitCode switch
        {
            null => "none",
            0 => "ok",
            -1 => "tool-failed",
            int code when (unchecked((uint)code) & 0xFFFFFF00u) == 0x80008000u => "host-failed",
            _ => "other"
        };

        /// <summary>The exit code as the 8 hex digits the .NET host's error codes are documented in.</summary>
        public static string ExitHex(int? exitCode) =>
            exitCode is int code ? "0x" + unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture) : "none";

        /// <summary>Text for one quoted log field: one line (line breaks become " | "), double quotes become single,
        /// control characters dropped, "none" when empty.</summary>
        public static string ForRow(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "none";
            var row = new StringBuilder(text.Length);
            bool lineBreak = false;
            foreach (char c in text.Trim())
            {
                if (c is '\r' or '\n')
                {
                    lineBreak = true;
                    continue;
                }
                if (lineBreak)
                {
                    row.Append(" | ");
                    lineBreak = false;
                }
                if (c == '"') row.Append('\'');
                else if (c == '\t') row.Append(' ');
                else if (!char.IsControl(c)) row.Append(c);
            }
            return row.ToString();
        }

        /// <summary>
        /// One stream's output, bounded: the first HeadChars and the last TailChars characters, with how many were cut
        /// between them. Fed line by line from Process.OutputDataReceived/ErrorDataReceived (thread-safe), so a chatty
        /// tool can neither fill a pipe and hang nor grow the server's memory.
        /// </summary>
        public sealed class BoundedOutput
        {
            private readonly int _head, _tail;
            private readonly StringBuilder _start = new();
            private readonly StringBuilder _end = new();
            private readonly Lock _sync = new();
            private long _total;

            public BoundedOutput(int head = HeadChars, int tail = TailChars)
            {
                _head = Math.Max(0, head);
                _tail = Math.Max(0, tail);
            }

            /// <summary>Characters received, line breaks included.</summary>
            public long TotalChars { get { lock (_sync) return _total; } }

            public void AppendLine(string line)
            {
                if (line == null) return;
                lock (_sync)
                {
                    string text = line + "\n";
                    _total += text.Length;
                    int intoHead = Math.Min(text.Length, _head - _start.Length);
                    if (intoHead > 0) _start.Append(text, 0, intoHead);
                    if (intoHead == text.Length) return;
                    _end.Append(text, intoHead, text.Length - intoHead);
                    if (_end.Length > _tail) _end.Remove(0, _end.Length - _tail);
                }
            }

            /// <summary>The kept text; "[... N chars cut ...]" marks a gap.</summary>
            public override string ToString()
            {
                lock (_sync)
                {
                    long cut = _total - _start.Length - _end.Length;
                    return cut > 0
                        ? $"{_start}[... {cut.ToString(CultureInfo.InvariantCulture)} chars cut ...]\n{_end}"
                        : _start.ToString() + _end;
                }
            }
        }
    }
}
