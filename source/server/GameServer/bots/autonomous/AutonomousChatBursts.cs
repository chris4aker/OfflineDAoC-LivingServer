using System;
using System.Collections.Generic;
using System.Linq;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// World-driven bursts (Beta 1.1.2, docs/bot-voices/CHAT-BUDGETS.md): a keep taken, a relic moved, a raid won or wiped, a
/// server first or a nemesis paid back sets off a short burst of varied reactions in /faction ("again??", "omw", "gj all"),
/// and for the realm's big moments a line or two in a capital's /broadcast where a player hears it, the way a 2003 alarm
/// rippled through every channel. 3-6 lines (chat-budget.json), early ones close together and later ones spread out (a
/// half-life of about two minutes in the research), each from a different bot, none a near-copy, and none once /faction is
/// at its ceiling. Built only when an event happens, never on a tick.
/// </summary>
public static class AutonomousChatBursts
{
    /// <summary>News topics that start a burst; the rest (calls for defenders, a raid forming) keep their usual answers.</summary>
    public static bool BurstsOn(string topic) => topic is "keep_won" or "keep_lost" or "keep_other" or "relic_won" or "relic_lost" or "relic_back" or
        "raid_won" or "raid_wiped" or "df_won" or "df_lost" or "siege_held" or "siege_failed";

    /// <summary>Topics a capital's /broadcast picks up too (the realm-wide ones).</summary>
    public static bool ReachesTowns(string topic) => topic is "keep_lost" or "relic_won" or "relic_lost" or "relic_back" or "df_won" or "df_lost";

    /// <summary>
    /// The burst's timing, pure: <paramref name="count"/> delays in ms within <paramref name="spreadMs"/>, sorted, the first after
    /// two or three seconds, then gaps that grow (exponential draws), so most lines land in the first minute.
    /// </summary>
    public static int[] Delays(int count, int spreadMs, Random random)
    {
        var delays = new int[Math.Max(0, count)];
        double mean = Math.Max(1_000, spreadMs / 3.0);
        for (int i = 0; i < delays.Length; i++)
            delays[i] = (int)Math.Min(spreadMs, 2_000 + random.Next(1_500) - mean * Math.Log(1 - random.NextDouble() * 0.95));
        Array.Sort(delays);
        for (int i = 1; i < delays.Length; i++)
            delays[i] = Math.Max(delays[i], delays[i - 1] + 1_200);
        return delays;
    }

    /// <summary>How many lines a burst has: the budget's 3-6, fewer with few bots online, more on a full server. Pure.</summary>
    public static int Size(AutonomousChatBudget.Settings settings, int realmBots, Random random)
    {
        double crowd = Math.Clamp(Math.Pow(Math.Max(1, realmBots) / (double)settings.DesignBots, 0.5), 0.4, 1.3);
        int low = Math.Max(1, settings.BurstMin), high = Math.Max(low, settings.BurstMax);
        return Math.Clamp((int)Math.Round((low + random.Next(high - low + 1)) * crowd), 1, high + 2);
    }

    /// <summary>
    /// A realm's big moment said on purpose (a keep, a relic, a raid's end, Darkness Falls, a server first): its burst is a tagged
    /// event (CHAT_LINE src=event), capped at three, and not held by /faction's ceiling (1.1.3).
    /// </summary>
    public static bool IsEvent(string trigger) => trigger == "first" || BurstsOn(trigger);

    /// <summary>A bot's own moment (a nemesis, a revenge): one or two bots react, not a crowd.</summary>
    public static bool Personal(string trigger) => trigger is "nemesis" or "revenge";

    /// <summary>
    /// How many reactions a burst may have (1.1.3, owner 2026-11-08: three congratulations within 13 s to one kill line, and a
    /// fourth 40 s later): one or two for a bot's own moment, at most three for realm news (a keep, a relic, a raid). Pure.
    /// </summary>
    public static int CappedSize(string trigger, int size, int realmBots, Random random)
    {
        if (Personal(trigger))
            return random.NextDouble() < 0.4 ? 2 : 1;
        return Math.Clamp(size, 1, 3);
    }

    /// <summary>How long after its trigger a burst's reactions may come: 20 s for a bot's own moment, 40 s for an event.</summary>
    public static int SpreadMs(string trigger, AutonomousChatBudget.Settings settings) =>
        Math.Min(Math.Max(5, settings.BurstSpreadS) * 1_000, Personal(trigger) ? 20_000 : 40_000);

    /// <summary>
    /// The longer reactions a channel heard over the evening (1.1.3, after RC5: "ugh, that one again", "oh well not my keep" and
    /// "lol of course" came back to the next keep lost). A reaction of three words or more ("ugh that one again") rests for
    /// <see cref="EveningMemory.WindowMs"/> in the channel, by its skeleton, so "ugh, that one again" and "ugh that one again"
    /// are one line; the one- and two-word ones ("oof", "again??") keep the ten-minute rule (AutonomousChatExchange.Shorts).
    /// A burst that runs out of fresh lines is shorter, never longer, so the caps hold.
    /// </summary>
    public sealed class EveningMemory
    {
        public const long WindowMs = 3 * 60 * 60_000L;
        public const int MinimumWords = 3;
        private readonly Dictionary<(string Key, string Skeleton), long> _heard = new();

        /// <summary>Whether the rule covers the reaction (three words or more). Pure.</summary>
        public static bool Covers(string text) =>
            AutonomousChatExchange.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MinimumWords;

        private static string KeyOf(string text) => AutonomousChatFiller.Skeleton(text);

        /// <summary>Whether the reaction was not heard in the channel this evening (always true for a short one).</summary>
        public bool Fresh(string key, string text, long now)
        {
            if (!Covers(text))
                return true;
            lock (_heard)
                return !_heard.TryGetValue((key, KeyOf(text)), out long at) || now - at >= WindowMs;
        }

        /// <summary>Claims the reaction for the channel when it is fresh; false when it was heard this evening.</summary>
        public bool TryUse(string key, string text, long now)
        {
            if (!Covers(text))
                return true;
            lock (_heard)
            {
                var k = (key, KeyOf(text));
                if (_heard.TryGetValue(k, out long at) && now - at < WindowMs)
                    return false;
                if (_heard.Count > 4_096)
                    foreach (var old in _heard.Where(pair => now - pair.Value >= WindowMs).Select(pair => pair.Key).ToList())
                        _heard.Remove(old);
                _heard[k] = now;
                return true;
            }
        }
    }

    /// <summary>The server's evening memory of longer reactions.</summary>
    public static readonly EveningMemory Evening = new();

    private static readonly Dictionary<eRealm, long> RunningUntil = new();

    /// <summary>
    /// Whether a burst may start in the realm now: one at a time (1.1.3: a commiseration and a cheer ran into each other, "that
    /// stinks" and "nice!" to two lines at once). Claims the realm until <paramref name="until"/> when it may.
    /// </summary>
    public static bool TryClaim(eRealm realm, long now, long until)
    {
        lock (RunningUntil)
        {
            if (RunningUntil.TryGetValue(realm, out long busy) && now < busy)
                return false;
            RunningUntil[realm] = until;
            return true;
        }
    }

    /// <summary>
    /// A burst of reactions in /faction to what <paramref name="first"/> just said (or to an event no bot said), from reply lines
    /// answering <paramref name="answerSets"/> in order (for news: "news_keep_lost", then "news"). <paramref name="exchange"/> is
    /// the trigger line's exchange id (the reactions are its lines 1, 2...), or 0.
    /// </summary>
    public static void React(GameBot first, eRealm realm, string trigger, string said, string[] answerSets, IReadOnlyDictionary<string, string> slots = null,
        bool towns = false, long exchange = 0)
    {
        if (!AutonomousBotVoices.Enabled || answerSets == null || answerSets.Length == 0 || !AutonomousBotChatCoordinator.HasFactionAudience(realm))
            return;
        AutonomousChatBudget.Settings settings = AutonomousChatBudget.Current;
        int realmBots = AutonomousBotChatCoordinator.RealmBots(realm);
        var random = Random.Shared;
        long now = GameLoop.GameLoopTime;
        int size = CappedSize(trigger, Size(settings, realmBots, random), realmBots, random);
        int spread = SpreadMs(trigger, settings);
        if (!TryClaim(realm, now, now + spread + 5_000))
        {
            AutonomousChatWhy.CountHeld(AutonomousChatBudget.Channel.Faction, AutonomousChatWhy.Held.Ceiling);
            return;
        }
        // Bots plausibly there to react: free to type, and for a bot's own frontier moment, ones in the same region first.
        GameBot[] crowd = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot != first && bot.Realm == realm && AutonomousLivingServer.CanTalk(bot) && !bot.IsTemporaryGroupHelper && !bot.InCombat)
            .OrderByDescending(bot => Personal(trigger) && first != null && bot.CurrentRegion == first.CurrentRegion ? 1 : 0)
            .ThenBy(_ => random.Next()).Take(size * 3).ToArray();
        if (crowd.Length == 0)
            return;
        string lane = AutonomousChatExchange.FactionKey(realm);
        int[] delays = Delays(size, spread, random);
        var heard = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(said))
            heard.Add(AutonomousChatFiller.Skeleton(said));
        var speakers = new List<GameBot> { first };
        var lines = new List<AutonomousChatExchange.Line>();
        int next = 0, at = 0;
        using AutonomousChatFiller.ReplyScope scope = AutonomousChatFiller.Replying(said, "faction:" + realm, now);
        for (int i = 0; i < delays.Length; i++)
        {
            string text = null;
            GameBot speaker = null;
            for (; next < crowd.Length && text == null; next++)
            {
                GameBot candidate = crowd[next];
                foreach (string answers in answerSets)
                    if (AutonomousBotVoices.TryLine(candidate, "reply", out VC.Picked picked, answers: answers, slots: slots, relax: true,
                            filter: line => !heard.Contains(AutonomousChatFiller.Skeleton(line.Text)) &&
                                            AutonomousChatExchange.Shorts.Fresh(lane, line.Text, now) && Evening.Fresh(lane, line.Text, now)))
                    {
                        if (heard.Add(AutonomousChatFiller.Skeleton(picked.Text)) && Evening.Fresh(lane, picked.Text, now) &&
                            AutonomousChatExchange.Shorts.TryUse(lane, picked.Text, now) && Evening.TryUse(lane, picked.Text, now))
                            (text, speaker) = (picked.Text, candidate);
                        break;
                    }
            }
            if (text == null)
                break;
            speakers.Add(speaker);
            lines.Add(new AutonomousChatExchange.Line(speakers.Count - 1, text, delays[i] - at, "reactor", Optional: true, Reaction: true));
            at = delays[i];
        }
        int total = lines.Count;
        var rules = AutonomousChatExchange.Rules.Faction with { ReactionWithinMs = Math.Min(spread + 10_000, 44_000) };
        // An event's reactions are said as the event (not held by the ceiling); a bot's own moment's wait for room like any line.
        bool isEvent = IsEvent(trigger);
        AutonomousChatExchange.Play(isEvent ? "event" : "burst", exchange != 0 ? exchange : AutonomousChatExchange.NewId(), lane, lines, speakers.ToArray(),
            bot => AutonomousLivingServer.CanTalk(bot) && !AutonomousBotDebates.IsRunning(realm) && (isEvent || AutonomousBotChatCoordinator.FactionRoom(realm)),
            (bot, line) =>
            {
                if (!AutonomousBotChatCoordinator.BroadcastFaction(bot.Name, realm, line.Text))
                    return false;
                AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), bot, line.Text, "");
                AutonomousChatWhy.Record(bot, "faction", "burst " + trigger, "said", "burst " + string.Join(',', answerSets), line.Who,
                    $"reaction {line.Who} of {total} to {trigger}", line.Text);
                return true;
            }, rules);
        if (towns)
            TownBroadcast(realm, trigger, answerSets, slots, heard, settings, random);
    }

    /// <summary>One or two reactions in the realm's capital /broadcast, from bots standing where a player hears them.</summary>
    private static void TownBroadcast(eRealm realm, string trigger, string[] answerSets, IReadOnlyDictionary<string, string> slots, HashSet<string> heard,
        AutonomousChatBudget.Settings settings, Random random)
    {
        GameBot[] inTown = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.Realm == realm && AutonomousBotVoices.IsCapitalRegion(bot.CurrentRegionID) && AutonomousLivingServer.CanTalk(bot))
            .OrderBy(_ => random.Next()).Take(6).ToArray();
        int lines = 0;
        foreach (GameBot bot in inTown)
        {
            if (lines >= 1 + random.Next(2))
                break;
            if (AutonomousChatBroadcast.Listeners(bot).Count == 0)
                continue;
            string text = null;
            string town = "town:" + realm;
            long now = GameLoop.GameLoopTime;
            foreach (string answers in answerSets)
                if (AutonomousBotVoices.TryLine(bot, "reply", out VC.Picked picked, answers: answers, slots: slots, relax: true,
                        filter: line => !heard.Contains(AutonomousChatFiller.Skeleton(line.Text)) && Evening.Fresh(town, line.Text, now)))
                {
                    text = picked.Text;
                    break;
                }
            if (text == null || !heard.Add(AutonomousChatFiller.Skeleton(text)) || !Evening.TryUse(town, text, now))
                continue;
            lines++;
            string line = text;
            GameBot speaker = bot;
            AutonomousLivingServer.Later(bot, 4_000 + random.Next(settings.BurstSpreadS * 500), () => AutonomousLivingServer.CanTalk(speaker), () =>
            {
                var band = AutonomousChatBudget.LiveBand(AutonomousChatBroadcast.ChannelOf(speaker), AutonomousBotChatCoordinator.RealmBots(realm));
                if (AutonomousChatBudget.Meter.PerMinute(AutonomousChatBroadcast.ChannelOf(speaker), AutonomousChatBroadcast.KeyOf(speaker), GameLoop.GameLoopTime) <
                    band.Ceiling && AutonomousChatBroadcast.Send(speaker, line))
                    AutonomousChatWhy.Record(speaker, "broadcast", "burst " + trigger, "said", "burst town", 0, "the news reaches the capital", line);
            });
        }
    }
}
