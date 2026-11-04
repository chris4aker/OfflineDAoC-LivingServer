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
/// stays quiet (AutonomousChatReplyBudget).</item>
/// </list>
/// The rules are pure (a <see cref="Settings"/> and numbers in, numbers out) and unit-tested; the live state is a few
/// dictionaries of small fixed rings, touched only when a bot tries to talk.
/// </summary>
public static class AutonomousChatBudget
{
    /// <summary>The channels the ambient budget paces.</summary>
    public enum Channel { Faction, Lfg, Trade, Say, Capital, Town, Guild, Group, Count }

    public sealed record ChannelBand(double Floor, double Target, double Ceiling, string Scale, int Members = 50, double MembersExponent = 0.8);

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
                channels[channel] = new ChannelBand(floor, target, Math.Max(target, Num(e, "ceiling", target * 2)), Str(e, "scale", "realm"),
                    (int)Math.Max(1, Num(e, "members", 50)), Num(e, "members_exponent", 0.8));
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
    /// the gate knows the real rate, replies and news included. One ring of 96 slots per channel key; no allocation once a
    /// key exists.
    /// </summary>
    public sealed class ChannelMeter
    {
        public const int Slots = 96;
        public const int WindowMs = 120_000;
        private sealed class Ring
        {
            public readonly long[] At = new long[Slots];
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

        /// <summary>A bot line was sent in this channel.</summary>
        public void Count(Channel channel, long key, long now)
        {
            lock (_rings)
            {
                Ring ring = For(channel, key);
                ring.At[ring.Next] = now;
                ring.Next = (ring.Next + 1) % Slots;
                ring.LastLine = Math.Max(ring.LastLine, now);
            }
        }

        /// <summary>Lines per minute over the last two minutes.</summary>
        public double PerMinute(Channel channel, long key, long now)
        {
            lock (_rings)
            {
                if (!_rings.TryGetValue((channel, key), out Ring ring))
                    return 0;
                int count = 0;
                foreach (long at in ring.At)
                    if (at > 0 && now - at < WindowMs && at <= now)
                        count++;
                return count * 60_000.0 / WindowMs;
            }
        }

        /// <summary>The gate for one channel moment: decides, and on a yes sets the next gap.</summary>
        public Verdict TryStart(Channel channel, long key, long now, (double Floor, double Target, double Ceiling) band, Random random)
        {
            lock (_rings)
            {
                Ring ring = For(channel, key);
                int count = 0;
                foreach (long at in ring.At)
                    if (at > 0 && now - at < WindowMs && at <= now)
                        count++;
                Verdict verdict = Decide(count * 60_000.0 / WindowMs, band, now, ring.NextDue);
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
    private static readonly long[] PressureAt = new long[4];

    /// <summary>The live band of a channel for a realm's bots online, the PC's hour and bot_voices_chat_rate.</summary>
    public static (double Floor, double Target, double Ceiling) LiveBand(Channel channel, int count) =>
        Band(Current, channel, count, DateTime.Now.Hour, AutonomousBotSchedule.CurrentMode == AutonomousBotSchedule.Mode.Clock,
            ServerProperties.Properties.BOT_VOICES_CHAT_RATE);

    /// <summary>
    /// How much more often a bot of this realm tries to talk now, from how far its /faction runs under target (cached for
    /// five seconds per realm, so a bot's tick reads two numbers).
    /// </summary>
    public static double LivePressure(eRealm realm, int realmBots, long now)
    {
        int index = (int)realm is >= 0 and < 4 ? (int)realm : 0;
        if (now - PressureAt[index] < 5_000 && PressureAt[index] > 0)
            return PressureByRealm[index];
        double target = LiveBand(Channel.Faction, realmBots).Target;
        double pressure = Pressure(Meter.PerMinute(Channel.Faction, (long)realm, now), target);
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
            pressure = Math.Max(pressure, Pressure(Meter.PerMinute(Channel.Say, bot.CurrentRegionID, now), LiveBand(Channel.Say, realmBots).Target));
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
