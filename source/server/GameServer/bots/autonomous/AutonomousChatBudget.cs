using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DOL.GS;

/// <summary>
/// The gamebots' two chat budgets (Beta 1.1.2, docs/bot-voices/CHAT-BUDGETS.md), read from <c>chat-budget.json</c>:
/// <list type="bullet">
/// <item>the <b>ambient budget</b>: per channel, a floor, a target and a ceiling in lines per minute, by the hour and by
/// how many bots are online, with no population cap. <see cref="Gate"/> decides whether a bot may start a line now, and
/// <see cref="Meter"/> counts what each channel really carried;</item>
/// <item>the <b>reply budget</b>: the fit a reply to a player needs (<see cref="MinReplyFit"/>), below which the bot
/// stays quiet (AutonomousChatReplyBudget), and (1.2) how many replies a channel carries: a reply target and a reply ceiling
/// per channel, counted apart from the ambient lines (<see cref="DecideReply"/>). Tells and lines that name a bot are always
/// answered.</item>
/// </list>
/// The rules are pure (a <see cref="Settings"/> and numbers in, numbers out) and unit-tested; the live state is a few
/// dictionaries of small fixed rings, touched only when a bot tries to talk.
/// </summary>
public static class AutonomousChatBudget
{
    /// <summary>The channels the ambient budget paces.</summary>
    public enum Channel { Faction, Lfg, Trade, Say, Capital, Town, Guild, Group, Count }

    public sealed record ChannelBand(double Floor, double Target, double Ceiling, string Scale, int Members = 50, double MembersExponent = 0.8,
        double ReplyTarget = 0, double ReplyCeiling = 0)
    {
        /// <summary>Whether this channel counts its replies apart from its ambient lines (1.2: /faction and /say).</summary>
        public bool BudgetsReplies => ReplyCeiling > 0.0005;
    }

    /// <summary>What a line in a channel is, for the meter: an unprompted (ambient) line, or a reply to someone's line.</summary>
    public enum LineKind { Ambient, Reply }

    public sealed record Settings(int DesignBots, double PopulationExponent, IReadOnlyDictionary<Channel, ChannelBand> Channels,
        double[] Hours, double[] HoursClock, double MacroChance, int MacroRepeatMinS, int MacroRepeatMaxS, int MacroLastMinS, int MacroLastMaxS,
        int BurstMin, int BurstMax, int BurstSpreadS, int MinReplyFit, int PileOnCap, int WpmMin, int WpmMax)
    {
        public ChannelBand Band(Channel channel) => Channels.TryGetValue(channel, out ChannelBand band) ? band : new ChannelBand(0, 0, 0, "none");
    }

    public const string FileName = "chat-budget.json";

    private static readonly Lazy<Settings> Loaded = new(LoadDefault);

    /// <summary>The settings in use: runtime/server/chat-budget.json when it is there, else the built-in file.</summary>
    public static Settings Current => Loaded.Value;

    private static Settings LoadDefault()
    {
        try
        {
            string local = Path.Combine(AppContext.BaseDirectory, FileName);
            if (File.Exists(local))
                return Parse(File.ReadAllText(local));
        }
        catch (Exception)
        {
            // A broken local copy falls back to the built-in budget.
        }
        return Parse(BuiltIn());
    }

    /// <summary>The chat-budget.json built into the server.</summary>
    public static string BuiltIn()
    {
        var assembly = typeof(AutonomousChatBudget).Assembly;
        string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("chat." + FileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Reads a budget file (comments allowed). Missing values take safe defaults.</summary>
    public static Settings Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = document.RootElement;
        static double Num(JsonElement e, string name, double fallback) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
        static string Str(JsonElement e, string name, string fallback) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : fallback;
        static double[] Curve(JsonElement e, string name)
        {
            double[] curve = Enumerable.Repeat(1.0, 24).ToArray();
            if (e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array)
                for (int i = 0; i < 24 && i < v.GetArrayLength(); i++)
                    curve[i] = Math.Clamp(v[i].GetDouble(), 0.0, 5.0);
            return curve;
        }
        var channels = new Dictionary<Channel, ChannelBand>();
        if (root.TryGetProperty("channels", out JsonElement list) && list.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty entry in list.EnumerateObject())
            {
                if (!Enum.TryParse(entry.Name, true, out Channel channel) || channel == Channel.Count)
                    continue;
                JsonElement e = entry.Value;
                double floor = Math.Max(0, Num(e, "floor", 0)), target = Math.Max(floor, Num(e, "target", floor));
                double replyTarget = Math.Max(0, Num(e, "reply_target", 0));
                channels[channel] = new ChannelBand(floor, target, Math.Max(target, Num(e, "ceiling", target * 2)), Str(e, "scale", "realm"),
                    (int)Math.Max(1, Num(e, "members", 50)), Num(e, "members_exponent", 0.8),
                    replyTarget, Math.Max(replyTarget, Num(e, "reply_ceiling", replyTarget * 2)));
            }
        root.TryGetProperty("texture", out JsonElement texture);
        root.TryGetProperty("reply", out JsonElement reply);
        return new Settings((int)Math.Max(1, Num(root, "design_bots_per_realm", 1000)), Math.Clamp(Num(root, "population_exponent", 0.75), 0.1, 1.5),
            channels, Curve(root, "hours"), Curve(root, "hours_clock_schedule"),
            Math.Clamp(Num(texture, "macro_chance", 0.5), 0, 1), (int)Num(texture, "macro_repeat_min_s", 180), (int)Num(texture, "macro_repeat_max_s", 480),
            (int)Num(texture, "macro_last_min_s", 900), (int)Num(texture, "macro_last_max_s", 2400),
            (int)Num(texture, "burst_min", 3), (int)Num(texture, "burst_max", 6), (int)Num(texture, "burst_spread_s", 90),
            (int)Math.Clamp(Num(reply, "min_fit", 2), 0, 4), (int)Math.Clamp(Num(reply, "pileon_cap", 2), 0, 3),
            (int)Math.Clamp(Num(reply, "wpm_min", 25), 5, 200), (int)Math.Clamp(Num(reply, "wpm_max", 60), 5, 200));
    }

    /// <summary>The fit a reply to a player needs (the reply budget).</summary>
    public static int MinReplyFit => Current.MinReplyFit;

    #region Pure rules

    /// <summary>
    /// How a channel's volume grows with the bots online: (bots / design) ^ exponent, rising without a cap (Beta 1.1.1
    /// clamped its factor at 1.8, about 390 bots a realm). For a guild, the online members against the band's members.
    /// </summary>
    public static double PopulationFactor(Settings settings, ChannelBand band, int count)
    {
        count = Math.Max(0, count);
        return band.Scale switch
        {
            "none" => 1.0,
            "members" => Math.Pow(Math.Max(1, count) / (double)band.Members, band.MembersExponent),
            _ => Math.Pow(Math.Max(1, count) / (double)settings.DesignBots, settings.PopulationExponent),
        };
    }

    /// <summary>The hour's factor (prime time 1). With the clock schedule the population already falls at night.</summary>
    public static double HourFactor(Settings settings, int localHour, bool clockSchedule) =>
        (clockSchedule ? settings.HoursClock : settings.Hours)[((localHour % 24) + 24) % 24];

    /// <summary>A channel's floor, target and ceiling now, in lines per minute (rate: bot_voices_chat_rate, 0 = none).</summary>
    public static (double Floor, double Target, double Ceiling) Band(Settings settings, Channel channel, int count, int localHour, bool clockSchedule, double rate)
    {
        ChannelBand band = settings.Band(channel);
        double scale = PopulationFactor(settings, band, count) * HourFactor(settings, localHour, clockSchedule) * Math.Clamp(rate, 0.0, 3.0);
        return (band.Floor * scale, band.Target * scale, band.Ceiling * scale);
    }

    /// <summary>
    /// A channel's reply target and reply ceiling now, in lines per minute (1.2): scaled like its ambient band, by the bots online
    /// and the hour, and by bot_voices_chat_rate but never below 1, so "0 = bots only answer" still answers. (0, 0) for a channel
    /// that doesn't count its replies apart.
    /// </summary>
    public static (double Target, double Ceiling) ReplyBand(Settings settings, Channel channel, int count, int localHour, bool clockSchedule, double rate)
    {
        ChannelBand band = settings.Band(channel);
        if (!band.BudgetsReplies)
            return (0, 0);
        double scale = PopulationFactor(settings, band, count) * HourFactor(settings, localHour, clockSchedule) * Math.Max(1.0, Math.Clamp(rate, 0.0, 3.0));
        return (band.ReplyTarget * scale, band.ReplyCeiling * scale);
    }

    /// <summary>
    /// How long a channel's rate is measured over (1.2): long enough to hold about <see cref="LinesPerWindow"/> lines at its
    /// target, from 2 to 20 minutes. A fixed two minutes could not see a slow channel's floor: at 10 bots a realm the /faction
    /// floor is 0.18 a minute, and one line in two minutes already reads as 0.5, so "under the floor" meant "the last two minutes
    /// were silent" and a bot spoke at once (the playtester's faction-rate runs: about 4 times the target). Pure.
    /// </summary>
    public static long WindowMs(double targetPerMinute, double linesPerWindow = LinesPerWindow)
    {
        if (targetPerMinute <= 0.0005)
            return MaxWindowMs;
        return (long)Math.Clamp(60_000.0 * linesPerWindow / targetPerMinute, MinWindowMs, MaxWindowMs);
    }

    public const double LinesPerWindow = 4;

    /// <summary>Replies come in clumps (a thread is three or four lines in half a minute), so their rate is read over twice as many.</summary>
    public const double ReplyLinesPerWindow = 8;
    public const long MinWindowMs = 120_000, MaxWindowMs = 1_200_000;

    /// <summary>Who a reply answers, for the reply budget (1.2).</summary>
    public enum ReplyKind
    {
        /// <summary>A tell to the bot, a line naming it, a /say to the bot in front of the player, or a follow-up to the bot's own
        /// line: always answered.</summary>
        Direct,
        /// <summary>The first answer to a player's line said to nobody in particular: answered below the reply ceiling.</summary>
        ToPlayer,
        /// <summary>A bystander's pile-on after the first answer to a player ("lol", "+1", another hello).</summary>
        PileOn,
        /// <summary>Bots answering bots: an opener's answers and thread, a story's riffs, reactions to a spammer.</summary>
        BotThread,
        /// <summary>The realm reacting to a big moment (a burst): answered below the reply ceiling.</summary>
        Burst,
    }

    /// <summary>What the reply budget says about a reply.</summary>
    public enum ReplyVerdict { Exempt, Speak, SpeakOverTarget, HoldTarget, HoldCeiling }

    /// <summary>
    /// The reply budget, pure (1.2). A direct reply (a tell, the bot's name) is never held. Above the reply ceiling nothing else
    /// is said. The first answer to a player and a burst are said up to the ceiling; bots answering bots and pile-ons are said
    /// below the target, and between the target and the ceiling with a chance that falls from 1 to 0, so the room above the
    /// target is mostly left for the player's lines. A channel with no reply ceiling holds nothing. <paramref name="roll"/>: 0-1.
    /// </summary>
    public static ReplyVerdict DecideReply(ReplyKind kind, double measuredPerMinute, (double Target, double Ceiling) band, double roll)
    {
        if (kind == ReplyKind.Direct)
            return ReplyVerdict.Exempt;
        if (band.Ceiling <= 0.0005)
            return ReplyVerdict.Speak;
        if (measuredPerMinute >= band.Ceiling)
            return ReplyVerdict.HoldCeiling;
        if (measuredPerMinute < band.Target)
            return ReplyVerdict.Speak;
        if (kind is ReplyKind.ToPlayer or ReplyKind.Burst)
            return ReplyVerdict.SpeakOverTarget;
        double room = (band.Ceiling - measuredPerMinute) / Math.Max(0.0005, band.Ceiling - band.Target);
        return roll < room ? ReplyVerdict.SpeakOverTarget : ReplyVerdict.HoldTarget;
    }

    /// <summary>
    /// How often the realm's population-independent /faction scenes come (1.2: debates, gold spammer waves), from 0.1 to 1: 1
    /// from <see cref="SceneFullRoster"/> bots a realm up (300, 900 and 1,000 keep today's pace), and as the square root of the
    /// share below it, so 10 bots a realm hear them about a fifth as often. A scene is many lines in a few minutes; on a small
    /// roster it used to be most of what /faction said (the playtester's faction-rate runs, 2026-11-05). Pure.
    /// </summary>
    public static double SceneScale(int realmBots) =>
        Math.Clamp(Math.Sqrt(Math.Max(0, realmBots) / (double)SceneFullRoster), MinSceneScale, 1.0);

    public const int SceneFullRoster = 300;
    public const double MinSceneScale = 0.1;

    /// <summary>
    /// Whether a scene planned at <paramref name="scheduledAt"/> for <paramref name="dueAt"/> is due now: its wait stretched by
    /// <see cref="SceneScale"/> for the bots online now, not when it was planned (at a server start few bots are on yet). A due
    /// time at or before the planning (a GM's "start soon") is due at once. Pure.
    /// </summary>
    public static bool SceneDue(long scheduledAt, long dueAt, long now, int realmBots)
    {
        if (dueAt <= scheduledAt)
            return now >= dueAt;
        return now - scheduledAt >= (dueAt - scheduledAt) / SceneScale(realmBots);
    }

    /// <summary>A gold spammer's ads in one wave for the bots online: fewer on a small roster, never under 2. Pure.</summary>
    public static int SceneLines(int lines, int realmBots) =>
        Math.Max(Math.Min(2, lines), (int)Math.Round(lines * SceneScale(realmBots)));

    /// <summary>Whether a reply verdict lets the line be said.</summary>
    public static bool Says(ReplyVerdict verdict) => verdict is ReplyVerdict.Exempt or ReplyVerdict.Speak or ReplyVerdict.SpeakOverTarget;

    /// <summary>
    /// The wait before a channel's next unprompted line, around a minute over the target. Round 5: bursty, not even (the
    /// stream test's minor tell): most gaps are short (0.25-0.6 times), some ordinary (0.6-1.6), and one in ten a lull (2.5-4.5
    /// times), about 0.93 times the mean overall. Never under 4 s; "never" (a very long wait) when the target is 0.
    /// </summary>
    public static long GapMs(double targetPerMinute, Random random)
    {
        if (targetPerMinute <= 0.0005)
            return long.MaxValue / 4;
        double mean = 60_000.0 / targetPerMinute;
        double kind = random.NextDouble(), within = random.NextDouble();
        // Round 8: short gaps start at a quarter of the mean, and never under 4 s.
        double factor = kind < 0.6 ? 0.25 + 0.35 * within : kind < 0.9 ? 0.6 + 1.0 * within : 2.5 + 2.0 * within;
        return (long)Math.Max(4_000, mean * factor);
    }

    /// <summary>What the gate says about a line a bot wants to start.</summary>
    public enum Verdict { Speak, SpeakUnderFloor, HoldCeiling, HoldGap }

    /// <summary>
    /// The ambient gate, pure: above the ceiling nothing starts; under the floor a line starts whenever a bot has one;
    /// otherwise when the channel's gap since its last unprompted line is over.
    /// </summary>
    public static Verdict Decide(double measuredPerMinute, (double Floor, double Target, double Ceiling) band, long now, long nextDue)
    {
        if (band.Target <= 0.0005)
            return Verdict.HoldGap;
        if (measuredPerMinute >= band.Ceiling)
            return Verdict.HoldCeiling;
        if (measuredPerMinute < band.Floor)
            return Verdict.SpeakUnderFloor;
        return now >= nextDue ? Verdict.Speak : Verdict.HoldGap;
    }

    /// <summary>
    /// How much more often a bot should try to talk when its channels run under target: 1 at or above it, up to 4 when a
    /// channel is silent (few bots in a place can still keep it alive). Pure.
    /// </summary>
    public static double Pressure(double measuredPerMinute, double targetPerMinute)
    {
        if (targetPerMinute <= 0.0005)
            return 1.0;
        return Math.Clamp(targetPerMinute / Math.Max(measuredPerMinute, targetPerMinute / 4), 1.0, 4.0);
    }

    /// <summary>
    /// How strongly a channel draws a bot's next line: how far it runs under its target (0.05 at or above it), so the
    /// emptiest channel a bot can be heard in fills first. Pure.
    /// </summary>
    public static double ChoiceWeight(double target, double measured) =>
        target <= 0.0005 ? 0.0001 : Math.Max(0.05, (target - measured) / target);

    /// <summary>A bot's typing speed in words per minute, fixed from its id within the budget's range; NoLifers type fastest.</summary>
    public static int TypingWpm(Settings settings, long botId, BotPersonalityType type)
    {
        int low = Math.Min(settings.WpmMin, settings.WpmMax), high = Math.Max(settings.WpmMin, settings.WpmMax);
        double roll = AutonomousBotPersonality.Mix(botId, 41) % 1000 / 1000.0;
        double tilt = type switch { BotPersonalityType.NoLifer => 0.25, BotPersonalityType.Socialite => 0.15, BotPersonalityType.Crafter => -0.15, _ => 0 };
        return (int)Math.Round(low + (high - low) * Math.Clamp(roll + tilt, 0, 1));
    }

    #endregion

    #region Meter (what each channel carried)

    /// <summary>
    /// Counts bot lines per channel moment (a realm's /faction, a region's /say, a guild, a group) in small fixed rings, so
    /// the gates know the real rate. Each line is an ambient line or a reply (1.2): the ambient gate counts the ambient lines,
    /// the reply budget the replies. One ring of 192 slots per channel key; no allocation once a key exists.
    /// </summary>
    public sealed class ChannelMeter
    {
        public const int Slots = 192;
        /// <summary>The window of <see cref="PerMinute(Channel, long, long)"/> (every line, two minutes).</summary>
        public const int WindowMs = 120_000;
        private sealed class Ring
        {
            public readonly long[] At = new long[Slots];
            public readonly bool[] Reply = new bool[Slots];
            public int Next;
            public long NextDue;
            public long LastAmbient;
            public long LastLine = long.MinValue / 2;
        }

        /// <summary>No unprompted line starts within this long of the channel's last line of any kind (round 8: lines 1-2 s apart).</summary>
        public const long MinSpacingMs = 3_000;

        /// <summary>When the channel moment last carried a line of any kind (long.MinValue / 2: never).</summary>
        public long LastLine(Channel channel, long key)
        {
            lock (_rings)
                return _rings.TryGetValue((channel, key), out Ring ring) ? ring.LastLine : long.MinValue / 2;
        }

        private readonly Dictionary<(Channel, long), Ring> _rings = new();

        /// <summary>
        /// The Beta 1.1.2 ambient gate, kept for the simulation's "before" side (UT_ChatBudgetSimulation): every line, ambient or
        /// reply, over a fixed two minutes. Never set on the server's meter.
        /// </summary>
        public bool CountsEveryLineOverTwoMinutes { get; init; }

        private Ring For(Channel channel, long key)
        {
            if (!_rings.TryGetValue((channel, key), out Ring ring))
            {
                if (_rings.Count > 4096)
                    _rings.Clear();
                _rings[(channel, key)] = ring = new Ring();
            }
            return ring;
        }

        /// <summary>A bot line was sent in this channel (an ambient line unless <paramref name="kind"/> says it is a reply).</summary>
        public void Count(Channel channel, long key, long now, LineKind kind = LineKind.Ambient)
        {
            lock (_rings)
            {
                Ring ring = For(channel, key);
                ring.At[ring.Next] = now;
                ring.Reply[ring.Next] = kind == LineKind.Reply;
                ring.Next = (ring.Next + 1) % Slots;
                ring.LastLine = Math.Max(ring.LastLine, now);
            }
        }

        private static int CountIn(Ring ring, long now, long windowMs, LineKind? kind)
        {
            int count = 0;
            for (int i = 0; i < Slots; i++)
            {
                long at = ring.At[i];
                if (at > 0 && now - at < windowMs && at <= now && (kind == null || ring.Reply[i] == (kind == LineKind.Reply)))
                    count++;
            }
            return count;
        }

        /// <summary>Every line's rate (ambient and replies), per minute over the last two minutes.</summary>
        public double PerMinute(Channel channel, long key, long now)
        {
            lock (_rings)
                return _rings.TryGetValue((channel, key), out Ring ring) ? CountIn(ring, now, WindowMs, null) * 60_000.0 / WindowMs : 0;
        }

        /// <summary>One kind of line's rate per minute over <paramref name="windowMs"/> (1.2; see <see cref="AutonomousChatBudget.WindowMs"/>).</summary>
        public double PerMinute(Channel channel, long key, long now, LineKind kind, long windowMs)
        {
            windowMs = Math.Max(1_000, windowMs);
            lock (_rings)
                return _rings.TryGetValue((channel, key), out Ring ring) ? CountIn(ring, now, windowMs, kind) * 60_000.0 / windowMs : 0;
        }

        /// <summary>The ambient lines' rate, measured over the window the channel's target needs.</summary>
        public double AmbientPerMinute(Channel channel, long key, long now, double targetPerMinute) =>
            PerMinute(channel, key, now, LineKind.Ambient, AutonomousChatBudget.WindowMs(targetPerMinute));

        /// <summary>The replies' rate, measured over the window the channel's reply target needs.</summary>
        public double RepliesPerMinute(Channel channel, long key, long now, double replyTargetPerMinute) =>
            PerMinute(channel, key, now, LineKind.Reply, AutonomousChatBudget.WindowMs(replyTargetPerMinute, ReplyLinesPerWindow));

        /// <summary>
        /// The reply budget for one channel moment (1.2): whether a reply of this kind may be said now. Pure but for the meter.
        /// </summary>
        public ReplyVerdict TryReply(Channel channel, long key, long now, (double Target, double Ceiling) band, ReplyKind kind, Random random)
        {
            if (kind == ReplyKind.Direct || band.Ceiling <= 0.0005)
                return DecideReply(kind, 0, band, 0);
            return DecideReply(kind, RepliesPerMinute(channel, key, now, band.Target), band, random.NextDouble());
        }

        /// <summary>
        /// The ambient gate for one channel moment: decides, and on a yes sets the next gap. It counts the channel's ambient lines
        /// over the window its target needs (1.2: before, every line over two minutes, so replies took the openers' turns and a slow
        /// channel's floor could not be seen).
        /// </summary>
        public Verdict TryStart(Channel channel, long key, long now, (double Floor, double Target, double Ceiling) band, Random random)
        {
            lock (_rings)
            {
                Ring ring = For(channel, key);
                long window = CountsEveryLineOverTwoMinutes ? WindowMs : AutonomousChatBudget.WindowMs(band.Target);
                Verdict verdict = Decide(CountIn(ring, now, window, CountsEveryLineOverTwoMinutes ? null : LineKind.Ambient) * 60_000.0 / window,
                    band, now, ring.NextDue);
                if (verdict is Verdict.Speak or Verdict.SpeakUnderFloor && now - ring.LastLine < MinSpacingMs && now >= ring.LastLine)
                    verdict = Verdict.HoldGap;
                if (verdict is Verdict.Speak or Verdict.SpeakUnderFloor)
                {
                    ring.NextDue = now + GapMs(band.Target, random);
                    ring.LastAmbient = now;
                }
                return verdict;
            }
        }

        /// <summary>When the channel moment last started an unprompted line (0: never).</summary>
        public long LastAmbient(Channel channel, long key)
        {
            lock (_rings)
                return _rings.TryGetValue((channel, key), out Ring ring) ? ring.LastAmbient : 0;
        }

        /// <summary>Pushes the next unprompted line back (a player just spoke there, or a scene started).</summary>
        public void Delay(Channel channel, long key, long until)
        {
            lock (_rings)
            {
                Ring ring = For(channel, key);
                ring.NextDue = Math.Max(ring.NextDue, until);
            }
        }

        public void Clear()
        {
            lock (_rings)
                _rings.Clear();
        }
    }

    /// <summary>The server's meter.</summary>
    public static readonly ChannelMeter Meter = new();

    #endregion

    #region Live helpers

    private static readonly double[] PressureByRealm = new double[4];
    private static readonly Channel[] RealmChannels = [Channel.Faction, Channel.Lfg, Channel.Trade];
    private static readonly long[] PressureAt = new long[4];

    /// <summary>The live band of a channel for a realm's bots online, the PC's hour and bot_voices_chat_rate.</summary>
    public static (double Floor, double Target, double Ceiling) LiveBand(Channel channel, int count) =>
        Band(Current, channel, count, DateTime.Now.Hour, AutonomousBotSchedule.CurrentMode == AutonomousBotSchedule.Mode.Clock,
            ServerProperties.Properties.BOT_VOICES_CHAT_RATE);

    /// <summary>The live reply band of a channel (1.2): (0, 0) where replies aren't counted apart.</summary>
    public static (double Target, double Ceiling) LiveReplyBand(Channel channel, int count) =>
        ReplyBand(Current, channel, count, DateTime.Now.Hour, AutonomousBotSchedule.CurrentMode == AutonomousBotSchedule.Mode.Clock,
            ServerProperties.Properties.BOT_VOICES_CHAT_RATE);

    /// <summary>
    /// The live reply budget for a reply about to be said or scheduled in a channel moment (1.2), counted for /lswhy. The caller
    /// counts the line itself with <see cref="LineKind.Reply"/> when it is said.
    /// </summary>
    public static ReplyVerdict LiveReply(Channel channel, long key, int realmBots, ReplyKind kind)
    {
        ReplyVerdict verdict;
        try
        {
            verdict = Meter.TryReply(channel, key, GameLoop.GameLoopTime, LiveReplyBand(channel, realmBots), kind, Random.Shared);
        }
        catch (Exception)
        {
            // No world (tests without the game loop or properties): replies go out as before.
            verdict = kind == ReplyKind.Direct ? ReplyVerdict.Exempt : ReplyVerdict.Speak;
        }
        AutonomousChatWhy.CountReply(channel, verdict);
        return verdict;
    }

    /// <summary>
    /// How much more often a bot of this realm tries to talk now, from how far its /faction, /lfg or /trade runs under target
    /// (cached for five seconds per realm, so a bot's tick reads two numbers). 1.2: the ambient lines only, and /lfg and /trade
    /// too: once replies stopped filling /faction's count, the turns they had pushed to /lfg and /trade went back to /faction,
    /// and a thin /lfg or /trade now draws its own lines.
    /// </summary>
    public static double LivePressure(eRealm realm, int realmBots, long now)
    {
        int index = (int)realm is >= 0 and < 4 ? (int)realm : 0;
        if (now - PressureAt[index] < 5_000 && PressureAt[index] > 0)
            return PressureByRealm[index];
        double pressure = 1.0;
        foreach (Channel channel in RealmChannels)
        {
            double target = LiveBand(channel, realmBots).Target;
            pressure = Math.Max(pressure, Pressure(Meter.AmbientPerMinute(channel, (long)realm, now, target), target));
        }
        PressureByRealm[index] = pressure;
        PressureAt[index] = now;
        return pressure;
    }

    /// <summary>
    /// A bot's pressure to talk: its realm's (/faction), and where a player is in its region that region's /say and, in a
    /// capital, its /broadcast, and its guild's. The highest wins, so a quiet capital or guild fills from the bots in it.
    /// No allocation: the regions with players are refreshed every five seconds.
    /// </summary>
    public static double LivePressure(GameBot bot, int realmBots, long now)
    {
        double pressure = LivePressure(bot.Realm, realmBots, now);
        if (PlayerRegions(now).Contains(bot.CurrentRegionID))
        {
            double say = LiveBand(Channel.Say, realmBots).Target;
            pressure = Math.Max(pressure, Pressure(Meter.AmbientPerMinute(Channel.Say, bot.CurrentRegionID, now, say), say));
            if (AutonomousBotVoices.IsCapitalRegion(bot.CurrentRegionID))
                pressure = Math.Max(pressure, Pressure(Meter.PerMinute(Channel.Capital, AutonomousChatBroadcast.KeyOf(bot), now),
                    LiveBand(Channel.Capital, realmBots).Target));
        }
        if (bot.Guild != null)
            lock (GuildPressure)
                if (GuildPressure.TryGetValue(AutonomousBotGuildSocial.KeyOf(bot.Guild), out double guild))
                    pressure = Math.Max(pressure, guild);
        return pressure;
    }

    private static readonly Dictionary<long, double> GuildPressure = new();

    /// <summary>Remembers how far a guild's chat runs under its target (set where the guild's gate runs).</summary>
    public static void NoteGuildPressure(long guildKey, double pressure)
    {
        lock (GuildPressure)
        {
            if (GuildPressure.Count > 2048)
                GuildPressure.Clear();
            GuildPressure[guildKey] = pressure;
        }
    }

    private static HashSet<ushort> _playerRegions = new();
    private static long _playerRegionsAt;

    /// <summary>The regions a player is in, refreshed every five seconds.</summary>
    private static HashSet<ushort> PlayerRegions(long now)
    {
        if (now - _playerRegionsAt < 5_000 && _playerRegionsAt > 0)
            return _playerRegions;
        var regions = new HashSet<ushort>();
        try
        {
            foreach (GamePlayer player in ClientService.Instance.GetPlayers())
                if (player?.CurrentRegion != null)
                    regions.Add(player.CurrentRegionID);
        }
        catch (Exception)
        {
            // No world (tests).
        }
        _playerRegions = regions;
        _playerRegionsAt = now;
        return regions;
    }

    #endregion
}
