namespace DAoCLivingServer.Updates;

// What the launcher tells the player after they click "Check for updates" and no update is offered (issue #795).
// The answer used to go only into the update strip's status line, which was squeezed to almost no width, so the click
// seemed to do nothing. The status line now has a row of its own, and the launcher also shows this notice in a plain
// message box (Windows, and Linux under Wine).
// The automatic check at start-up never shows it.
public sealed record UpdateCheckNotice(string Caption, string Text, string StatusLine, bool IsProblem)
{
    public const string DialogCaption = "Living Server update";

    /// <summary>
    /// The notice for an asked check that offered nothing: nothing newer was found, or GitHub gave no answer (problem,
    /// from ReleaseFeed: a network error, a timeout or an HTTP error such as a 403 rate limit).
    /// </summary>
    /// <remarks>
    /// "No newer version was found", not "this is the newest version": a newer final with no package attached yet, or
    /// (with test builds on) a failed /releases/latest call beside a working list call, also end here.
    /// </remarks>
    public static UpdateCheckNotice NothingOffered(BuildVersion installed, string? problem, bool offerTestBuilds)
    {
        string name = installed.DisplayName;
        if (problem != null)
            return new UpdateCheckNotice(DialogCaption,
                $"Couldn't get an answer from GitHub: {problem}{Environment.NewLine}{Environment.NewLine}" +
                $"You have Living Server {name}. Try again later.",
                $"Couldn't reach GitHub ({problem}). Try again later.", IsProblem: true);
        string testBuilds = offerTestBuilds ? " No newer test build was found either." : "";
        return new UpdateCheckNotice(DialogCaption,
            $"No newer version was found: you have Living Server {name}.{testBuilds}",
            $"No newer Living Server was found (you have {name}).", IsProblem: false);
    }

    /// <summary>The notice for an asked check that failed in the launcher itself (an exception, not GitHub's answer).</summary>
    public static UpdateCheckNotice CheckFailed(BuildVersion installed, string error) =>
        new(DialogCaption,
            $"Couldn't check for updates: {error}{Environment.NewLine}{Environment.NewLine}" +
            $"You have Living Server {installed.DisplayName}. Try again later.",
            "Couldn't check for updates: " + error, IsProblem: true);
}
