namespace DAoCLivingServer.Updates;

// What the launcher tells the player after they click "Check for updates" and no update is offered (issue #795).
// The answer used to go only into the update strip's status line, which was squeezed to almost no width, so the click
// seemed to do nothing. The status line now has a row of its own, and the launcher also shows this notice in a plain
// message box (Windows, and Linux under Wine).
// The automatic check at start-up never shows it.
public sealed record UpdateCheckNotice(string Caption, string Text, string StatusLine, bool IsProblem)
{
    public const string DialogCaption = "Living Server update";

    /// <summary>The notice for an asked check that offered nothing: up to date, or the reason it couldn't check.</summary>
    public static UpdateCheckNotice NothingOffered(BuildVersion installed, string? problem, bool offerTestBuilds)
    {
        string name = installed.DisplayName;
        if (problem != null)
            return new UpdateCheckNotice(DialogCaption,
                $"Couldn't check for updates: {problem}{Environment.NewLine}{Environment.NewLine}" +
                $"You have Living Server {name}. Check your internet connection and try again later.",
                $"Couldn't reach GitHub ({problem}). Try again later.", IsProblem: true);
        string testBuilds = offerTestBuilds ? " No newer test build is out either." : "";
        return new UpdateCheckNotice(DialogCaption,
            $"You're up to date: Living Server {name} is the newest version.{testBuilds}",
            $"You have the newest Living Server ({name}).", IsProblem: false);
    }
}
