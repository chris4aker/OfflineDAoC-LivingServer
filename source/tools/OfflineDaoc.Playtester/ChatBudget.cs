// The /faction rate the server aims for, worked out the way the server does (AutonomousChatBudget.Band): the channel's
// target in chat-budget.json at the design point, times (bots online in the realm / design_bots_per_realm) ^
// population_exponent, times the hour's factor (by the PC's local hour; hours_clock_schedule with bot_schedule_mode
// clock), times bot_voices_chat_rate (clamped 0-3). From Living Server 1.2 a channel's replies have a budget of their own
// (reply_target, reply_ceiling, scaled the same way but by bot_voices_chat_rate never below 1): the realm hears up to the
// ambient target plus the reply target. The faction-rate scenario passes at half of that and flags a run above the ambient
// ceiling plus the reply ceiling (AutonomousChatBudget.ReplyBand).
// The budget is the server's own file: runtime/server/chat-budget.json when an install has one, else the copy built into
// this tool from source/server/GameServer/bots/autonomous/data/chat/chat-budget.json (as the server builds it in).
using System.Text.Json;

namespace OfflineDaoc.Playtester;

internal sealed record ChatBand(double Floor, double Target, double Ceiling, double ReplyTarget = 0, double ReplyCeiling = 0);

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
            {
                // As the server reads them: no reply values, no reply budget; a reply ceiling is never under its target.
                double replyTarget = c.Value.TryGetProperty("reply_target", out var rt) ? Math.Max(0, rt.GetDouble()) : 0;
                double replyCeiling = c.Value.TryGetProperty("reply_ceiling", out var rc) ? rc.GetDouble() : replyTarget * 2;
                channels[c.Name] = new ChatBand(c.Value.GetProperty("floor").GetDouble(), c.Value.GetProperty("target").GetDouble(),
                    c.Value.GetProperty("ceiling").GetDouble(), replyTarget, Math.Max(replyTarget, replyCeiling));
            }
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

    /// <summary>AutonomousChatBudget.ReplyBand: a channel's reply target and ceiling now (0, 0 without a reply budget).</summary>
    public (double Target, double Ceiling) ReplyBand(string channel, int botsOnline, int localHour, bool clockSchedule, double chatRate)
    {
        var band = Channels[channel];
        if (band.ReplyCeiling <= 0.0005)
            return (0, 0);
        double scale = PopulationFactor(botsOnline) * HourFactor(localHour, clockSchedule) * Math.Max(1.0, Math.Clamp(chatRate, 0.0, 3.0));
        return (band.ReplyTarget * scale, band.ReplyCeiling * scale);
    }
}

/// <summary>
/// What faction-rate expects: the server's /faction band now for the bots online and its reply band, the mark that passes
/// (a share of the ambient target plus the reply target) and the high mark that flags a run (the ambient ceiling plus the
/// reply ceiling: the most the server lets the channel carry).
/// </summary>
internal sealed record FactionPrediction(int BotsOnline, int LocalHour, bool ClockSchedule, double ChatRate, double PopulationFactor,
    double HourFactor, ChatBand Band, double PassMark, (double Target, double Ceiling) Replies = default)
{
    public double Expected => Band.Target + Replies.Target;

    public double HighMark => Band.Ceiling + Replies.Ceiling;

    public static FactionPrediction Of(ChatBudget budget, int botsOnline, int localHour, bool clockSchedule, double chatRate, double passShare)
    {
        var band = budget.Band("faction", botsOnline, localHour, clockSchedule, chatRate);
        var replies = budget.ReplyBand("faction", botsOnline, localHour, clockSchedule, chatRate);
        return new(botsOnline, localHour, clockSchedule, chatRate, budget.PopulationFactor(botsOnline), budget.HourFactor(localHour, clockSchedule),
            band, (band.Target + replies.Target) * passShare, replies);
    }

    /// <summary>A measured rate against the marks: whether it passes (at or above the pass mark, with a line at all) and whether
    /// it runs too high (above the high mark).</summary>
    public static (bool Passes, bool TooHigh) Judge(int lines, double rate, double passMark, double highMark) =>
        (lines > 0 && rate >= passMark, highMark > 0 && rate > highMark);

    /// <summary>The world's bot_voices_chat_rate (default 2, as the server's) and whether bot_schedule_mode is clock.</summary>
    public static (double Rate, bool Clock) SettingsOf(TestWorld world)
    {
        double rate = double.TryParse(world.Property("bot_voices_chat_rate"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double r) ? r : 2.0;
        bool clock = string.Equals((world.Property("bot_schedule_mode") ?? "off").Trim(), "clock", StringComparison.OrdinalIgnoreCase);
        return (rate, clock);
    }
}
