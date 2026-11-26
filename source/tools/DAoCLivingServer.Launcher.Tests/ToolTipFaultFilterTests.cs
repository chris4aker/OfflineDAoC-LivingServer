using NUnit.Framework;

namespace DAoCLivingServer.Launcher.Tests;

/// <summary>Only errors from the WinForms keyboard tooltip code are skipped; every other error still reaches the dialog.</summary>
public sealed class ToolTipFaultFilterTests
{
    // Stands in for System.Windows.Forms.KeyboardToolTipStateMachine so the stack trace names it.
    private static class KeyboardToolTipStateMachine
    {
        public static void Transition() => throw new KeyNotFoundException("The given key was not present in the dictionary.");

        // Wine can't load the accessibility type library a DataGridView cell's keyboard tooltip asks for.
        public static void ShowToolTip() => throw new System.Runtime.InteropServices.COMException("TYPE_E_CANTLOADLIBRARY", unchecked((int)0x80029C4A));
    }

    private static Exception Caught(Action action)
    {
        try { action(); }
        catch (Exception e) { return e; }
        throw new AssertionException("expected an exception");
    }

    [Test]
    public void Skips_TheKeyboardToolTipTimerFault() =>
        Assert.That(ToolTipFaultFilter.IsKeyboardToolTipFault(Caught(KeyboardToolTipStateMachine.Transition)), Is.True);

    [Test]
    public void Skips_TheGridKeyboardToolTipComFault() =>
        Assert.That(ToolTipFaultFilter.IsKeyboardToolTipFault(Caught(KeyboardToolTipStateMachine.ShowToolTip)), Is.True);

    [Test]
    public void Keeps_OtherMissingKeyErrors() =>
        Assert.That(ToolTipFaultFilter.IsKeyboardToolTipFault(Caught(() => _ = new Dictionary<int, int>()[1])), Is.False);

    [Test]
    public void Logs_OnlyTheFirstSkippedFaultsOfARun()
    {
        Assert.That(ToolTipFaultFilter.SkippedFaultLogLimit, Is.EqualTo(20));
        Assert.That(ToolTipFaultFilter.LogsSkippedFault(1), Is.True);
        Assert.That(ToolTipFaultFilter.LogsSkippedFault(20), Is.True);
        Assert.That(ToolTipFaultFilter.LogsSkippedFault(21), Is.False);
    }

    [Test]
    public void Keeps_OtherErrorTypes() =>
        Assert.That(ToolTipFaultFilter.IsKeyboardToolTipFault(Caught(() => throw new InvalidOperationException("KeyboardToolTipStateMachine"))), Is.False);
}
