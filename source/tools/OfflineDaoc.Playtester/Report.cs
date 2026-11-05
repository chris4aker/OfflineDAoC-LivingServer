// What a run found, written as JSON (for tools and CI) and Markdown (for a person): each scenario's outcome, its steps
// with timings, what it measured, what is still to do, and every chat line its players saw.
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfflineDaoc.Playtester;

[JsonConverter(typeof(JsonStringEnumConverter<Outcome>))]
internal enum Outcome { Passed, Failed, Skipped, Incomplete }

internal sealed class StepResult
{
    public string Name { get; set; } = "";
    public Outcome Outcome { get; set; }
    public double Seconds { get; set; }
    public string Detail { get; set; } = "";
}

internal sealed class ScenarioResult
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public Outcome Outcome { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public double Seconds { get; set; }
    public List<StepResult> Steps { get; set; } = new();
    public Dictionary<string, object> Measured { get; set; } = new();
    public List<string> Todo { get; set; } = new();
    /// <summary>What a passing scenario found wrong enough to look at (faction-rate: a channel over its ceiling).</summary>
    public List<string> Flags { get; set; } = new();
    public List<ChatLine> Chat { get; set; } = new();
}

internal sealed class RunReport
{
    public string Tool { get; set; } = "OfflineDaoc.Playtester";
    public DateTimeOffset StartedUtc { get; set; }
    public double Seconds { get; set; }
    public string World { get; set; } = "";
    public Dictionary<string, string> WorldMarker { get; set; } = new();
    public string Server { get; set; } = "";
    public List<string> ServerLogs { get; set; } = new();
    public int ServerErrorLines { get; set; }
    public List<string> ServerFatalLines { get; set; } = new();
    public List<ScenarioResult> Scenarios { get; set; } = new();

    public bool AnyFailed => Scenarios.Any(s => s.Outcome == Outcome.Failed);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public string ToMarkdown(int chatLinesPerScenario = 300)
    {
        var md = new StringBuilder();
        md.AppendLine($"# Playtest report {StartedUtc:yyyy-MM-dd HH:mm} UTC");
        md.AppendLine();
        md.AppendLine("Automated run of the scripted player (OfflineDaoc.Playtester). Nothing here was played by a person.");
        md.AppendLine();
        md.AppendLine($"- World: `{World}` ({WorldMarker.GetValueOrDefault("mode", "?")}, made {WorldMarker.GetValueOrDefault("made_utc", "?")})");
        md.AppendLine($"- Server: {Server}");
        md.AppendLine($"- Took {Seconds:0} s; server log lines marked ERR or FATAL: {ServerErrorLines}");
        foreach (var log in ServerLogs)
            md.AppendLine($"- Server log: `{log}`");
        md.AppendLine();
        md.AppendLine("| Scenario | Outcome | Seconds | Why |");
        md.AppendLine("|---|---|---|---|");
        foreach (var s in Scenarios)
            md.AppendLine($"| {s.Id} | **{s.Outcome}** | {s.Seconds:0.0} | {Cell(s.Reason)} |");
        if (ServerFatalLines.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("## Fatal server lines");
            md.AppendLine();
            foreach (var line in ServerFatalLines.Take(20))
                md.AppendLine($"    {line}");
        }
        foreach (var s in Scenarios)
        {
            md.AppendLine();
            md.AppendLine($"## {s.Id}: {s.Title}");
            md.AppendLine();
            md.AppendLine($"**{s.Outcome}**{(s.Reason.Length > 0 ? ": " + s.Reason : "")}");
            md.AppendLine();
            if (s.Steps.Count > 0)
            {
                md.AppendLine("| Step | Outcome | Seconds | Detail |");
                md.AppendLine("|---|---|---|---|");
                foreach (var step in s.Steps)
                    md.AppendLine($"| {Cell(step.Name)} | {step.Outcome} | {step.Seconds:0.00} | {Cell(step.Detail)} |");
                md.AppendLine();
            }
            if (s.Measured.Count > 0)
            {
                md.AppendLine("Measured:");
                foreach (var (key, value) in s.Measured)
                    md.AppendLine($"- {key}: {value}");
                md.AppendLine();
            }
            if (s.Flags.Count > 0)
            {
                md.AppendLine("Flagged:");
                foreach (var flag in s.Flags)
                    md.AppendLine($"- FLAG: {flag}");
                md.AppendLine();
            }
            if (s.Todo.Count > 0)
            {
                md.AppendLine("Still to do (needs the real release world or more code):");
                foreach (var todo in s.Todo)
                    md.AppendLine($"- TODO: {todo}");
                md.AppendLine();
            }
            if (s.Chat.Count > 0)
            {
                md.AppendLine($"<details><summary>Chat lines seen ({s.Chat.Count})</summary>");
                md.AppendLine();
                md.AppendLine("```");
                foreach (var line in s.Chat.Take(chatLinesPerScenario))
                    md.AppendLine($"{line.At,8:0.00} {line.Player,-10} [{line.Channel}] {line.Text}");
                if (s.Chat.Count > chatLinesPerScenario)
                    md.AppendLine($"... {s.Chat.Count - chatLinesPerScenario} more in the JSON report");
                md.AppendLine("```");
                md.AppendLine();
                md.AppendLine("</details>");
            }
        }
        return md.ToString();
    }

    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    /// <summary>Writes report.json and report.md into the folder and returns their paths.</summary>
    public (string Json, string Markdown) Write(string folder)
    {
        Directory.CreateDirectory(folder);
        string json = Path.Combine(folder, "report.json"), markdown = Path.Combine(folder, "report.md");
        File.WriteAllText(json, ToJson());
        File.WriteAllText(markdown, ToMarkdown());
        return (json, markdown);
    }
}
