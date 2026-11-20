using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Catches one WinForms bug so it can't stop the launcher with the .NET error dialog.
/// The keyboard tooltip state machine (KeyboardToolTipStateMachine) can get a timer tick in a state it has no rule for
/// and throw KeyNotFoundException. On Linux under Wine this happened after alt-tabbing from the game to the launcher
/// (owner's PC, 2026-11-20; issue #392, related to #389). The only harm is a tooltip that doesn't show,
/// so it is logged and skipped. Every other exception still gets the normal dialog.
/// </summary>
public static class ToolTipFaultFilter
{
    public static bool IsKeyboardToolTipFault(Exception exception) =>
        exception is KeyNotFoundException
        && exception.StackTrace?.Contains("KeyboardToolTipStateMachine", StringComparison.Ordinal) == true;

    internal static void Install(string serverRoot)
    {
        var log = new UpdateLog(Path.Combine(serverRoot, "logs", "launcher-errors.log"));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            if (IsKeyboardToolTipFault(e.Exception))
            {
                log.Write("Skipped a WinForms tooltip timer error (KeyboardToolTipStateMachine): " + e.Exception.Message);
                return;
            }
            log.Write("Unhandled launcher error: " + e.Exception);
            using var dialog = new ThreadExceptionDialog(e.Exception);
            if (dialog.ShowDialog() == DialogResult.Abort)
                Application.Exit();
        };
    }
}
