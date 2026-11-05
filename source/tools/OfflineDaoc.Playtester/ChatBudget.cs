// The /faction rate the server aims for, worked out the way the server does (AutonomousChatBudget.Band): the channel's
// target in chat-budget.json at the design point, times (bots online in the realm / design_bots_per_realm) ^
// population_exponent, times the hour's factor (by the PC's local hour; hours_clock_schedule with bot_schedule_mode
// clock), times bot_voices_chat_rate (clamped 0-3). The faction-rate scenario passes at half of it.
// The budget is the server's own file: runtime/server/chat-budget.json when an install has one, else the copy built into
// this tool from source/server/GameServer/bots/autonomous/data/chat/chat-budget.json (as the server builds it in).
using System.Text.Json;

namespace OfflineDaoc.Playtester;

internal sealed record ChatBand(double Floor, double Target, double Ceiling);

internal sealed record ChatBudget(int DesignBots, double PopulationExponent, double[] Hours, double[] HoursClock,
    IReadOnlyDictionary<string, ChatBand> Channels, string Source)
{
    public static ChatBudget Parse(string json, string source)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        double[] Hours(string key) => root.TryGetProperty(key, out var a) && a.GetArrayLength() == 24
            ? a.EnumerateArray().Select(e => e.GetDouble()).ToArray()
            : Enumerable.Repeat(1.0, 24).ToArray();
        var channels = new Dictionary<string, ChatBand>();
        if (root.TryGetProperty("channels", out var ch))
            foreach (var c in ch.EnumerateObject())
                channels[c.Name] = new ChatBand(c.Value.GetProperty("floor").GetDouble(), c.Value.GetProperty("target").GetDouble(),
                    c.Value.GetProperty("ceiling").GetDouble());
        return new ChatBudget(
            root.TryGetProperty("design_bots_per_realm", out var d) ? d.GetInt32() : 1000,
            root.TryGetProperty("population_exponent", out var e) ? e.GetDouble() : 0.65,
            Hours("hours"), Hours("hours_clock_schedule"), channels, source);
    }

    /// <summary>The install's runtime/server/chat-budget.json if the server folder has one, else the built-in copy.</summary>
    public static ChatBudget Load(string? serverDir)
    {
        string? local = serverDir == null ? null : Path.Combine(serverDir, "chat-budget.json");
        if (local != null && File.Exists(local))
        {
            try { return Parse(File.ReadAllText(local), local); }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }   // the server ignores a broken copy too
        }
        using var stream = typeof(ChatBudget).Assembly.GetManifestResourceStream("chat-budget.json")
            ?? throw new InvalidOperationException("the built-in chat-budget.json is missing");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), "built in (source/server/GameServer/bots/autonomous/data/chat/chat-budget.json)");
    }

    /// <summary>AutonomousChatBudget.PopulationFactor for a realm-scaled channel.</summary>
    public double PopulationFactor(int botsOnline) => Math.Pow(Math.Max(1, botsOnline) / (double)DesignBots, PopulationExponent);

    public double HourFactor(int localHour, bool clockSchedule) => (clockSchedule ? HoursClock : Hours)[((localHour % 24) + 24) % 24];

    /// <summary>AutonomousChatBudget.Band: a channel's floor, target and ceiling now, in lines per minute.</summary>
    public ChatBand Band(string channel, int botsOnline, int localHour, bool clockSchedule, double chatRate)
    {
        var band = Channels[channel];
        double scale = PopulationFactor(botsOnline) * HourFactor(localHour, clockSchedule) * Math.Clamp(chatRate, 0.0, 3.0);
        return new ChatBand(band.Floor * scale, band.Target * scale, band.Ceiling * scale);
    }
}

/// <summary>What faction-rate expects: the server's /faction band now for the bots online, and the mark that passes.</summary>
internal sealed record FactionPrediction(int BotsOnline, int LocalHour, bool ClockSchedule, double ChatRate, double PopulationFactor,
    double HourFactor, ChatBand Band, double PassMark)
{
    public double Expected => Band.Target;

    public static FactionPrediction Of(ChatBudget budget, int botsOnline, int localHour, bool clockSchedule, double chatRate, double passShare) =>
        new(botsOnline, localHour, clockSchedule, chatRate, budget.PopulationFactor(botsOnline), budget.HourFactor(localHour, clockSchedule),
            budget.Band("faction", botsOnline, localHour, clockSchedule, chatRate),
            budget.Band("faction", botsOnline, localHour, clockSchedule, chatRate).Target * passShare);

    /// <summary>The world's bot_voices_chat_rate (default 2, as the server's) and whether bot_schedule_mode is clock.</summary>
    public static (double Rate, bool Clock) SettingsOf(TestWorld world)
    {
        double rate = double.TryParse(world.Property("bot_voices_chat_rate"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double r) ? r : 2.0;
        bool clock = string.Equals((world.Property("bot_schedule_mode") ?? "off").Trim(), "clock", StringComparison.OrdinalIgnoreCase);
        return (rate, clock);
    }
}
