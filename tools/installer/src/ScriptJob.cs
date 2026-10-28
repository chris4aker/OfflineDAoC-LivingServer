using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace LivingServerSetup
{
    /// <summary>Runs one PowerShell script hidden, streams its lines (curl's carriage-return progress included) and can
    /// stop it with its whole process tree.</summary>
    internal sealed class ScriptJob
    {
        public event Action<string, bool> Line;   // text, from stderr
        Process proc;
        public string LastError;                  // the script's own message from its last error record
        bool sawRecord;

        public static string PowerShellPath
        {
            get
            {
                var sys = Environment.SystemDirectory;
                var p = Path.Combine(sys, @"WindowsPowerShell\v1.0\powershell.exe");
                return File.Exists(p) ? p : "powershell.exe";
            }
        }

        /// <summary>Quotes one argument by the Windows command-line rules (trailing backslashes doubled before the quote).</summary>
        public static string Quote(string arg)
        {
            var s = arg.Replace("\"", "\\\"");
            int n = 0;
            for (int i = s.Length - 1; i >= 0 && s[i] == '\\'; i--) n++;
            return "\"" + s + new string('\\', n) + "\"";
        }

        public int Run(string script, string args, CancellationToken cancel)
        {
            var psi = new ProcessStartInfo(PowerShellPath,
                "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(script) + " " + args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WorkingDirectory = Path.GetDirectoryName(script),
            };
            Encoding enc;
            try { enc = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { enc = Encoding.Default; }
            psi.StandardOutputEncoding = enc;
            psi.StandardErrorEncoding = enc;
            Log.Write("> powershell " + Path.GetFileName(script) + " " + args);
            proc = Process.Start(psi);
            proc.StandardInput.Close(); // a Read-Host can never wait for input
            var t1 = new Thread(() => Pump(proc.StandardOutput, false)) { IsBackground = true };
            var t2 = new Thread(() => Pump(proc.StandardError, true)) { IsBackground = true };
            t1.Start(); t2.Start();
            using (cancel.Register(Kill))
            {
                proc.WaitForExit();
                t1.Join(5000); t2.Join(5000);
            }
            var code = proc.ExitCode;
            Log.Write("< exit " + code);
            return code;
        }

        void Pump(StreamReader reader, bool err)
        {
            var sb = new StringBuilder();
            var buf = new char[4096];
            int n;
            try
            {
                while ((n = reader.Read(buf, 0, buf.Length)) > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        char c = buf[i];
                        if (c == '\r' || c == '\n') { Emit(sb, err); continue; }
                        sb.Append(c);
                    }
                }
            }
            catch { }
            Emit(sb, err);
        }

        static readonly Regex CurlBar = new Regex(@"^[#\s=O\-]*\s*\d{1,3}(\.\d)?%\s*$");
        // Bar fragments without a percentage ("#=#=#", "##O#-#"): curl's progress, not errors (walkthrough 2026-10-28).
        static readonly Regex CurlBarOnly = new Regex(@"^[#\s=O\-]+$");

        void Emit(StringBuilder sb, bool err)
        {
            if (sb.Length == 0) return;
            var line = sb.ToString().TrimEnd();
            sb.Clear();
            if (line.Length == 0) return;
            if (err && !CurlBar.IsMatch(line) && !line.StartsWith("#"))
            {
                // PowerShell's error record: "<script> : <message>" then "At line:...", "+ ..." lines
                // the error record's own line ("<script>.ps1 : message") wins over any other stderr text
                // ("At C:\...\Get-OfflineDAoC.ps1:497 char:34" is the record's position line, not its message)
                // A cmdlet's own record counts too ("Invoke-WebRequest : The remote name could not be resolved"): before, its
                // wrapped tail ("eption" of "WebException") became the message (owner's PC, 2026-10-28, a network drop).
                var m = line.StartsWith("At ") ? Match.Empty : Regex.Match(line, @"^(?:.*\.ps1|Exception\S*|throw|[A-Z][A-Za-z]+-[A-Z][A-Za-z]+)\s*:\s*(.+)$");
                if (m.Success) { LastError = m.Groups[1].Value.Trim(); sawRecord = true; }
                else if (!sawRecord && !line.StartsWith("At ") && !line.TrimStart().StartsWith("+")) LastError = line.Trim();
            }
            if (!(err && (CurlBar.IsMatch(line) || CurlBarOnly.IsMatch(line)))) Log.Write((err ? "! " : "  ") + line);
            // a handler's mistake must never stop the reading: PowerShell would block on a full pipe for ever
            try { Line?.Invoke(line, err); } catch (Exception e) { Log.Write("(output handler: " + e.Message + ")"); }
        }

        public void Kill()
        {
            try
            {
                if (proc == null || proc.HasExited) return;
                Log.Write("Stopping the script and its downloads...");
                using (var k = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + proc.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false }))
                    k?.WaitForExit(10000);
            }
            catch (Exception e) { Log.Write("Could not stop the script: " + e.Message); }
        }
    }
}
