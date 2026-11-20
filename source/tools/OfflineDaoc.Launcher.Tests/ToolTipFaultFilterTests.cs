using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>Only the WinForms keyboard tooltip timer fault is skipped; every other error still reaches the dialog.</summary>
public sealed class ToolTipFaultFilterTests
{
    // Stands in for System.Windows.Forms.KeyboardToolTipStateMachine so the stack trace names it.
    private static class KeyboardToolTipStateMachine
    {
        public static void Transition() => throw new KeyNotFoundException("The given key was not present in the dictionary.");
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
    public void Keeps_OtherMissingKeyErrors() =>
        Assert.That(ToolTipFaultFilter.IsKeyboardToolTipFault(Caught(() => _ = new Dictionary<int, int>()[1])), Is.False);

    [Test]
    public void Keeps_OtherErrorTypes() =>
        Assert.That(ToolTipFaultFilter.IsKeyboardToolTipFault(Caught(() => throw new InvalidOperationException("KeyboardToolTipStateMachine"))), Is.False);
}
