using System.Diagnostics;
using System.Runtime.InteropServices;
using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Stops WinForms' keyboard tooltip code from ending the launcher with the .NET error dialog (issue #392, related #389).
/// On Linux under Wine, alt-tabbing from the game back to the launcher starts the keyboard tooltip timers
/// (KeyboardToolTipStateMachine). Two errors came from them on the owner's PC (2026-11-20):
/// - KeyNotFoundException: a timer tick in a state the state machine has no rule for;
/// - COMException TYPE_E_CANTLOADLIBRARY: a DataGridView cell's keyboard tooltip asks Windows accessibility for the
///   cell's bounds, and Wine can't load that type library.
/// The only harm is a tooltip that doesn't show, so an error raised under KeyboardToolTipStateMachine is logged and
/// skipped. Every other error still gets the normal dialog.
/// </summary>
public static class ToolTipFaultFilter
{
    public static bool IsKeyboardToolTipFault(Exception exception) =>
        exception.StackTrace?.Contains("KeyboardToolTipStateMachine", StringComparison.Ordinal) == true;

    internal static UpdateLog? Log { get; private set; }

    internal static void Install(string serverRoot)
    {
        Log = new UpdateLog(Path.Combine(serverRoot, "logs", "launcher-ui.log"));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            if (IsKeyboardToolTipFault(e.Exception))
            {
                Log.Write($"Skipped a WinForms keyboard tooltip error ({e.Exception.GetType().Name}): {e.Exception.Message}");
                return;
            }
            Log.Write("Unhandled launcher error: " + e.Exception);
            using var dialog = new ThreadExceptionDialog(e.Exception);
            if (dialog.ShowDialog() == DialogResult.Abort)
                Application.Exit();
        };
    }
}

/// <summary>Tooltip rules for the launcher when it runs under Wine (Linux), issue #389 and #392.</summary>
internal static class LauncherToolTips
{
    /// <summary>True under Wine: its ntdll exports wine_get_version, Windows' doesn't.</summary>
    internal static bool UnderWine { get; } = DetectWine();

    /// <summary>
    /// Under Wine, turns off the DataGridViews' own cell tooltips. They only ever show cut-off cell text (no grid sets
    /// cell tooltip text), and under Wine they showed over the game and their keyboard tooltip crashed on alt-tab.
    /// Windows keeps them.
    /// </summary>
    internal static void TurnOffGridToolTipsUnderWine(Control root)
    {
        if (!UnderWine)
            return;
        if (root is DataGridView grid && grid.ShowCellToolTips)
            grid.ShowCellToolTips = false;
        foreach (Control child in root.Controls)
            TurnOffGridToolTipsUnderWine(child);
    }

    /// <summary>The window in front belongs to this process. Under Wine, the launcher's Deactivate isn't a reliable
    /// sign that the game is in front, so the launcher also asks which process owns the foreground window.</summary>
    internal static bool LauncherIsInFront(out string frontProcess)
    {
        frontProcess = "";
        IntPtr front = GetForegroundWindow();
        if (front == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(front, out uint pid);
        if (pid == (uint)Environment.ProcessId)
            return true;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            frontProcess = process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            frontProcess = "pid " + pid;
        }
        return false;
    }

    private static bool DetectWine()
    {
        try
        {
            IntPtr ntdll = GetModuleHandle("ntdll.dll");
            return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
}
