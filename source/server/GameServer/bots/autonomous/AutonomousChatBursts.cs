using System;
using System.Collections.Generic;
using System.Linq;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// World-driven bursts (Beta 1.1.3, docs/bot-voices/CHAT-BUDGETS.md): a keep taken, a relic moved, a raid won or wiped, a
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
    /// A burst of reactions in /faction to what <paramref name="first"/> just said (or to an event no bot said), from reply lines
    /// answering <paramref name="answerSets"/> in order (for news: "news_keep_lost", then "news").
    /// </summary>
    public static void React(GameBot first, eRealm realm, string trigger, string said, string[] answerSets, IReadOnlyDictionary<string, string> slots = null,
        bool towns = false)
    {
        if (!AutonomousBotVoices.Enabled || answerSets == null || answerSets.Length == 0 || !AutonomousBotChatCoordinator.HasFactionAudience(realm))
            return;
        AutonomousChatBudget.Settings settings = AutonomousChatBudget.Current;
        int realmBots = AutonomousBotChatCoordinator.RealmBots(realm);
        var random = Random.Shared;
        int size = Size(settings, realmBots, random);
        GameBot[] crowd = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot != first && bot.Realm == realm && AutonomousLivingServer.CanTalk(bot) && !bot.IsTemporaryGroupHelper)
            .OrderBy(_ => random.Next()).Take(size * 3).ToArray();
        if (crowd.Length == 0)
            return;
        int[] delays = Delays(size, settings.BurstSpreadS * 1_000, random);
        var heard = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(said))
            heard.Add(AutonomousChatFiller.Skeleton(said));
        var spoken = new HashSet<GameBot>();
        int next = 0;
        using AutonomousChatFiller.ReplyScope scope = AutonomousChatFiller.Replying(said, "faction:" + realm, GameLoop.GameLoopTime);
        for (int i = 0; i < delays.Length; i++)
        {
            string text = null;
            GameBot speaker = null;
            for (; next < crowd.Length && text == null; next++)
            {
                GameBot candidate = crowd[next];
                foreach (string answers in answerSets)
                    if (AutonomousBotVoices.TryLine(candidate, "reply", out VC.Picked picked, answers: answers, slots: slots, relax: true,
                            filter: line => !heard.Contains(AutonomousChatFiller.Skeleton(line.Text))))
                    {
                        if (heard.Add(AutonomousChatFiller.Skeleton(picked.Text)))
                            (text, speaker) = (picked.Text, candidate);
                        break;
                    }
            }
            if (text == null)
                break;
            string line = text;
            GameBot bot = speaker;
            int order = i;
            AutonomousLivingServer.Later(bot, delays[i], () => AutonomousLivingServer.CanTalk(bot) && !AutonomousBotDebates.IsRunning(realm), () =>
            {
                long now = GameLoop.GameLoopTime;
                var band = AutonomousChatBudget.LiveBand(AutonomousChatBudget.Channel.Faction, realmBots);
                if (AutonomousChatBudget.Meter.PerMinute(AutonomousChatBudget.Channel.Faction, (long)realm, now) >= band.Ceiling)
                {
                    AutonomousChatWhy.CountHeld(AutonomousChatBudget.Channel.Faction, AutonomousChatWhy.Held.Ceiling);
                    return;
                }
                lock (spoken)
                    if (!spoken.Add(bot))
                        return;
                AutonomousBotChatCoordinator.BroadcastFaction(bot.Name, realm, line);
                AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), bot, line, "");
                AutonomousChatWhy.Record(bot, "faction", "burst " + trigger, "said", "burst " + string.Join(',', answerSets), order,
                    $"reaction {order + 1} of {delays.Length} to {trigger}", line);
            });
        }
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
            foreach (string answers in answerSets)
                if (AutonomousBotVoices.TryLine(bot, "reply", out VC.Picked picked, answers: answers, slots: slots, relax: true,
                        filter: line => !heard.Contains(AutonomousChatFiller.Skeleton(line.Text))))
                {
                    text = picked.Text;
                    break;
                }
            if (text == null || !heard.Add(AutonomousChatFiller.Skeleton(text)))
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
