using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;
using Budget = DOL.GS.AutonomousChatBudget;

namespace DOL.GS.Tests;

/// <summary>
/// Beta 1.1.2: an evening hour of one realm's ambient chat, before (Beta 1.1.1's gaps, capped at 1.8) and after (the
/// ambient budget), through the real corpus picker, the real gap and budget rules and the real reply counts. An approximate
/// model, not a world: each bot checks whether to talk every 15-35 s as BotBrain does, is free (not fighting or riding)
/// 55% of the time, and passes AutonomousBotChat.ShouldSpeak with its personality's rate; the player stands in their
/// capital with 3% of the realm's bots within /say range and 8% in its broadcast area; one guild has 2% of the realm online;
/// four bots are in the player's group. Set TALK_BUDGET_OUT to keep the report.
/// </summary>
[TestFixture]
public class UT_ChatBudgetSimulation
{
    private static VC _corpus;
    private static VC Corpus => _corpus ??= Load();

    private static VC Load()
    {
        var assembly = typeof(VC).Assembly;
        string Read(string suffix)
        {
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
    }

    private static readonly string[] Classes = ["Armsman", "Cleric", "Wizard", "Friar", "Minstrel", "Paladin", "Sorcerer", "Scout", "Infiltrator",
        "Theurgist", "Cabalist", "Mercenary", "Necromancer", "Reaver"];

    public sealed class Result
    {
        public readonly Dictionary<string, List<string>> Lines = new();
        public int Minutes;
        /// <summary>Lines a minute; a capital's /broadcast includes its sellers' macros (repeats by design, kept apart as "macro").</summary>
        public double PerMinute(string channel) => ((Lines.TryGetValue(channel, out var list) ? list.Count : 0) +
            (channel == "broadcast" && Lines.TryGetValue("macro", out var macros) ? macros.Count : 0)) / (double)Minutes;
        public double Total => Lines.Values.Sum(list => list.Count) / (double)Minutes;
        /// <summary>Who said each channel's lines, for the share of the busiest speaker.</summary>
        public readonly Dictionary<string, List<long>> Speakers = new();

        /// <summary>The share of a channel's lines said by its busiest bot.</summary>
        public double TopSpeakerShare(string channel) =>
            Speakers.TryGetValue(channel, out var who) && who.Count > 0 ? who.GroupBy(id => id).Max(g => g.Count()) / (double)who.Count : 0;

        /// <summary>When each channel's lines were said (ms), for the gaps between them.</summary>
        public readonly Dictionary<string, List<long>> Times = new();

        /// <summary>The share of a channel's gaps between consecutive lines that are 2 s or less (round 8: the stream judge counted them).</summary>
        public double FastGapShare(string channel)
        {
            if (!Times.TryGetValue(channel, out var at) || at.Count < 2)
                return 0;
            long[] sorted = at.OrderBy(t => t).ToArray();
            int fast = 0;
            for (int i = 1; i < sorted.Length; i++)
                if (sorted[i] - sorted[i - 1] <= 2_000)
                    fast++;
            return fast / (double)(sorted.Length - 1);
        }

        public void Add(string channel, string text, long speaker, long at)
        {
            if (!Times.TryGetValue(channel, out var times))
                Times[channel] = times = new List<long>();
            times.Add(at);
            Add(channel, text, speaker);
        }

        public void Add(string channel, string text, long speaker)
        {
            if (!Speakers.TryGetValue(channel, out var who))
                Speakers[channel] = who = new List<long>();
            who.Add(speaker);
            Add(channel, text);
        }

        public void Add(string channel, string text)
        {
            if (!Lines.TryGetValue(channel, out var list))
                Lines[channel] = list = new List<string>();
            list.Add(text);
        }

        /// <summary>How many different bots said each 30 lines of a channel, on average (round 5: real channels have regulars).</summary>
        public double SpeakersPer30(string channel)
        {
            if (!Speakers.TryGetValue(channel, out var who) || who.Count < 30)
                return 0;
            int windows = 0;
            double sum = 0;
            for (int at = 0; at + 30 <= who.Count; at += 30, windows++)
                sum += who.Skip(at).Take(30).Distinct().Count();
            return sum / windows;
        }

        /// <summary>Per channel, each inviting field line's thread: 0 when nobody answered, else its length in lines (opener included).</summary>
        public readonly Dictionary<string, List<int>> Threads = new();

        /// <summary>The share of inviting lines that got an answer.</summary>
        public double ThreadRate(string channel) => Threads.TryGetValue(channel, out var list) && list.Count > 0 ? list.Count(n => n > 0) / (double)list.Count : 0;

        /// <summary>The average length of answered threads, and the share that ran to three lines or more.</summary>
        public (double Mean, double Long) ThreadLength(string channel)
        {
            if (!Threads.TryGetValue(channel, out var list) || list.All(n => n == 0))
                return (0, 0);
            int[] answered = list.Where(n => n > 0).ToArray();
            return (answered.Average(), answered.Count(n => n >= 4) / (double)answered.Length);
        }

        /// <summary>The share of a channel's lines said by its top tenth of speakers (round 5; research: about 60% on a live server).</summary>
        public double TopTenthShare(string channel)
        {
            if (!Speakers.TryGetValue(channel, out var who) || who.Count == 0)
                return 0;
            int[] counts = who.GroupBy(id => id).Select(g => g.Count()).OrderByDescending(c => c).ToArray();
            int top = Math.Max(1, (int)Math.Ceiling(counts.Length * 0.1));
            return counts.Take(top).Sum() / (double)who.Count;
        }

        /// <summary>
        /// The share of distinct lines among a channel's lines of three words or more (Beta 1.1.2, round 4: a 3-hour run had 810
        /// /faction lines and 531 distinct). Short reactions ("me", "gz", "omw") are left out; 1 when there are none.
        /// </summary>
        public double DistinctShare(string channel)
        {
            if (!Lines.TryGetValue(channel, out var list))
                return 1;
            var counted = list.Where(text => AutonomousChatFiller.Skeleton(text).Split(' ').Length > 2).ToList();
            return counted.Count == 0 ? 1 : counted.Distinct(StringComparer.OrdinalIgnoreCase).Count() / (double)counted.Count;
        }

        /// <summary>
        /// The share of a channel's lines that repeat an earlier line of it exactly, and nearly (same skeleton). Short reactions
        /// ("gz", "omw", "lol rip": two words at most) are left out, as the live near-copy rule leaves them out (Beta 1.1.2).
        /// </summary>
        public (double Exact, double Near) Duplicates(string channel)
        {
            if (!Lines.TryGetValue(channel, out var list) || list.Count == 0)
                return (0, 0);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var skeletons = new HashSet<string>(StringComparer.Ordinal);
            int exact = 0, near = 0;
            foreach (string text in list)
            {
                string skeleton = AutonomousChatFiller.Skeleton(text);
                if (skeleton.IndexOf(' ') < 0 || skeleton.Split(' ').Length <= 2 && skeleton.Length <= 10)
                    continue;
                if (!seen.Add(text))
                    exact++;
                if (!skeletons.Add(skeleton))
                    near++;
            }
            return (exact / (double)list.Count, near / (double)list.Count);
        }
    }

    private sealed class Bot
    {
        public long Id;
        public string Class;
        public int Level;
        public bool Solo, NearPlayer, InBroadcast, InGuild, InGroup;
        public long NextCheck, LastSpoke = -10 * 60_000;
        public BotPersonalityType Type => (BotPersonalityType)(Id % 6);
        public double Rate;
    }

    /// <summary>Beta 1.1.1's /faction gap (AutonomousBotChatCoordinator.FactionGapMs before 1.1.2), for the "before" side.</summary>
    private static long OldFactionGapMs(int bots, Random random)
    {
        double population = Math.Clamp(Math.Sqrt(Math.Max(0, bots) / 120.0), 0.5, 1.8);
        double gap = (120_000 + random.Next(120_001)) / (population * 1.35);
        return (long)Math.Clamp(gap, 40_000, 20 * 60_000);
    }

    private static long OldLocalGapMs(Random random) => (long)Math.Clamp((60_000 + random.Next(60_001)) / 1.35, 30_000, 10 * 60_000);

    private static VC.Speaker SpeakerOf(Bot bot, params string[] extra)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal) { bot.Solo ? "solo" : "in_group", "evening", VC.TimeFlag(20) };
        flags.Add(bot.NearPlayer || bot.InBroadcast ? "in_capital" : "outdoors");
        if (bot.Level <= 14) flags.Add("newbie");
        else if (bot.Level >= 40) flags.Add("veteran");
        foreach (string flag in extra)
            flags.Add(flag);
        return new VC.Speaker(bot.Id, "Bot" + bot.Id, eRealm.Albion, bot.Class, bot.Level, (BotPersonalityType)(bot.Id % 6), flags);
    }

    private static Dictionary<string, string> Slots(VC.Speaker speaker) => new(StringComparer.Ordinal)
    {
        ["me"] = speaker.Name, ["class"] = speaker.ClassName.ToLowerInvariant(), ["level"] = speaker.Level.ToString(), ["realm"] = "Albion",
        ["zone"] = "Camelot Hills", ["capital"] = "Camelot", ["camp"] = "the spiders by Prydwen", ["dungeon"] = "Keltoi Fogou",
        ["monster"] = "large skeleton", ["monsters"] = "large skeletons", ["enemy"] = "Midgard", ["keep"] = "Caer Benowyc", ["frontier"] = "Hadrian's Wall",
        ["item"] = "a bone-studded ring", ["count"] = "3", ["player"] = "Wynn", ["q"] = "", ["bubbles"] = "4",
    };

    private static readonly string[][] Zones =
    [
        ["Camelot Hills", "Salisbury Plains", "Black Mountains South"], ["Campacorentin Forest", "Llyn Barfog", "Black Mountains North"],
        ["Lyonesse", "Dartmoor", "Cornwall"], ["Avalon Marsh", "Lyonesse", "Dartmoor"], ["Salisbury Plains", "Dartmoor", "Avalon Marsh"],
    ];

    private static readonly string[] Monsters = ["large skeleton", "bwgan horde", "black wolf", "ghoulic viper", "moor boogey", "giant spider"];
    // Bots carry different loot: the first eight are gear (a quality and a price), the rest are drops.
    private static readonly string[] Items = ["Bone-studded Ring", "Ghostly Cloak", "Silver Bracer", "Rusty Gladius", "Bloodied Chain Hauberk",
        "Studded Leggings", "Etched Short Sword", "Mithril Bound Gloves", "Spider Silk", "Kobold Tooth", "Glowing Gem", "Pristine Bear Pelt",
        "Ruby Necklace", "Wolf Pelt Cloak", "Bronze Hauberk", "Runed Staff", "Banded Arms", "Moor Boogey Claw", "Tattered Robe", "Stone Golem Heart",
        "Bone Bracelet", "Spectral Belt", "Tarnished Ring", "Glimmering Jewel", "Bear Claw Necklace", "Leather Jerkin", "Iron Kite Shield",
        "Heavy Crossbow", "Hunting Bow", "Bloodstone", "Silver Ring", "Gold Earring", "Ancient Tome", "Wyvern Scale", "Faerie Wing",
        "Polished Stone", "Hardened Leather Boots", "Studded Helm", "Chain Coif", "Scale Leggings"];

    /// <summary>A modelled frontier: seven keeps, one lost to Midgard, one under attack for the first half of the hour, one relic gone.</summary>
    private static AutonomousChatFieldTalk.WorldView WorldAt(long now) => new(
        ["Caer Benowyc", "Caer Berkstead", "Caer Erasleigh", "Caer Boldiam", "Caer Sursbrooke", "Caer Hurbury"],
        now < 40 * 60_000 ? ["Caer Benowyc"] : [], ["Caer Renaris"], ["Midgard"], 0, 1, "Forest Sauvage");

    private static AutonomousChatFieldTalk.Facts FactsOf(Bot bot, long now, Random random) => new(
        bot.Id, eRealm.Albion, bot.Class, bot.Level, Zones[Math.Min(4, bot.Level / 10)][(int)(bot.Id % 3)],
        AutonomousBotVoices.DungeonFor(eRealm.Albion, bot.Level, random), bot.Level >= 40 && bot.Id % 4 == 0, false, bot.NearPlayer || bot.InBroadcast,
        !bot.Solo, bot.Solo ? 1 : 2 + (int)(bot.Id % 6), random.NextDouble() < 0.03, random.NextDouble() < 0.02, bot.Id % 7 == 0, bot.Solo && bot.Id % 2 == 0,
        Monsters[(int)(bot.Id % Monsters.Length)], bot.Id % 3 == 0 ? null : Items[(int)((bot.Id / 3) % Items.Length)], 85 + (int)(bot.Id % 16),
        (bot.Id / 3) % Items.Length < 8 || (bot.Id / 3) % Items.Length is 14 or 15 or 16 or 26 or 27 or 28 or 36 or 37 or 38 or 39,
        bot.Id % 11 == 0 ? "Camelot" : null, 1 + (int)(bot.Id % 9), 20, bot.Class is "Cleric" or "Friar", WorldAt(now));

    private static bool Pick(VC.RecentLines recent, DateTime now, Random random, string kind, VC.Speaker speaker, out VC.Picked picked,
        string answers = null, Func<VC.Line, bool> filter = null) =>
        Corpus.TryPick(new VC.Request(kind, speaker, Slots(speaker), recent, now, random)
        {
            Answers = answers, Filter = filter, LocalHour = 20, RelaxTo = VC.RecentLines.Relaxed,
        }, out picked);

    /// <summary>One realm's evening hour (after a ten-minute warm-up) with <paramref name="bots"/> online.</summary>
    public static Result Evening(int bots, bool budget, int seed, int minutes = 60)
    {
        var random = new Random(seed);
        var recent = new VC.RecentLines();
        var meter = new Budget.ChannelMeter();
        var fresh = new AutonomousChatFiller.RecentTexts();
        var field = new AutonomousChatFieldTalk.Memory();
        var guildField = new AutonomousChatFieldTalk.Memory();
        Budget.Settings settings = Budget.Current;
        var result = new Result { Minutes = minutes };
        var all = new List<Bot>(bots);
        int guildSize = Math.Max(4, bots / 50);
        for (int i = 0; i < bots; i++)
            all.Add(new Bot
            {
                Id = 10_000 + i, Class = Classes[random.Next(Classes.Length)], Level = 5 + random.Next(46), Solo = random.Next(3) != 0,
                NearPlayer = random.NextDouble() < 0.03, InBroadcast = random.NextDouble() < 0.08, InGuild = i < guildSize, InGroup = i >= bots - 4,
                NextCheck = random.Next(35_000),
                Rate = AutonomousBotPersonality.RegularFactor(10_000 + i),
            });
        long lastFaction = -1, factionGap = 0, lastLocal = -1, localGap = 0, lastGuild = -1, nextGroup = 90_000;
        long start = 10 * 60_000, end = start + minutes * 60_000L;
        var dueReplies = new List<(long At, string Channel, string Text, long Who)>();
        var macros = new List<(long At, string Text, long Who)>();
        long lastFactionLine = long.MinValue / 2;
        var deferred = new HashSet<long>();
        var waits = new Dictionary<string, int>(StringComparer.Ordinal);
        void Say(long now, string channel, string text, long who)
        {
            if (channel == "faction")
                lastFactionLine = now;
            if (now >= start && now < end)
                result.Add(channel, text, who, now);
            Budget.Channel? metered = channel switch
            {
                "faction" => Budget.Channel.Faction, "lfg" => Budget.Channel.Lfg, "trade" => Budget.Channel.Trade, "say" => Budget.Channel.Say,
                "broadcast" or "macro" => Budget.Channel.Capital, "guild" => Budget.Channel.Guild, "group" => Budget.Channel.Group, _ => null,
            };
            if (metered is { } c)
                meter.Count(c, 1, now);
        }
        bool Regular(Bot bot, long now) => budget && (AutonomousChatRegulars.IsRegular(bot.Id, bot.Type, now, bots) ||
            bot.InGuild && AutonomousChatRegulars.IsGuildRegular(bot.Id, bot.Type, now, guildSize));
        Bot Answerer(long now, Func<Bot, bool> pool)
        {
            // AutonomousBotChatCoordinator.ScheduleFieldAnswer: a regular first when one is about.
            Bot fallback = null;
            for (int tries = 0; tries < 160; tries++)
            {
                Bot mate = all[random.Next(all.Count)];
                if (!pool(mate))
                    continue;
                if (Regular(mate, now))
                    return mate;
                fallback ??= mate;
            }
            return fallback ?? all[random.Next(all.Count)];
        }
        Bot Near(long now, Bot to, Func<Bot, bool> pool, double regularShare = 1.0)
        {
            // AutonomousBotChatCoordinator.Repliers: a regular first (for a question two times in five), then a bot near the
            // speaker's level, then anyone.
            bool regularFirst = regularShare >= 1 || random.NextDouble() < regularShare;
            Bot near = null, any = null;
            for (int tries = 0; tries < 200; tries++)
            {
                Bot mate = all[random.Next(all.Count)];
                if (mate == to || !pool(mate))
                    continue;
                if (regularFirst && Regular(mate, now))
                    return mate;
                if (near == null && Math.Abs(mate.Level - to.Level) <= 5)
                    near = mate;
                any ??= mate;
            }
            return near ?? any;
        }
        void Play(long now, string channel, List<AutonomousChatFieldTalk.Step> steps, Bot[] speakers, bool invited)
        {
            foreach (AutonomousChatFieldTalk.Step step in steps)
                if (speakers[step.Who] is { } who)
                    dueReplies.Add((now + Math.Max(500, step.AtMs), channel, AutonomousChatIdiolect.Apply(step.Text, who.Id, false, random, terse: true), who.Id));
            if (now < start || now >= end || !invited)
                return;
            result.Threads.TryAdd(channel, new List<int>());
            result.Threads[channel].Add(steps.Any(step => step.Who == 1) ? 1 + steps.Count : 0);
        }
        void Field(long now, Bot bot, string channel, AutonomousChatFieldTalk.Said said, AutonomousChatFieldTalk.Memory memory, Func<Bot, bool> pool)
        {
            // AutonomousBotChatCoordinator.ScheduleFieldThread.
            // 1.1.3: only a bot that can answer it (the role and levels it asks for).
            // 1.1.3, after RC5: and one that has an answer ("any reavers on?" is for a reaver).
            bool Fits(Bot other) => AutonomousChatFieldTalk.CanGiveAnswer(said, other.Class, other.Level, other.Class is "Cleric" or "Friar");
            double share = AutonomousChatFieldTalk.WantsAnswer(said) ? AutonomousBotChatCoordinator.QuestionRegularShare : 1.0;
            Bot mate = Near(now, bot, other => pool(other) && Fits(other), share), third = Near(now, bot, other => other != mate && pool(other) && Fits(other));
            var steps = AutonomousChatFieldTalk.Thread(said, FactsOf(bot, now, random), mate == null ? null : FactsOf(mate, now, random),
                third == null ? null : FactsOf(third, now, random), memory, random, now);
            Play(now, channel, steps, [bot, mate, third], said.Answers != null);
        }
        void Replies(long now, Bot starter, string channel, VC.Picked opener, DateTime clock)
        {
            // Round 5: a written musing draws no canned agreement, at most one fitting reaction.
            if (budget && opener.Line?.Kind is "ambient" or "rvr" or "camp" or "group")
            {
                if (AutonomousBotChatCoordinator.WrittenReaction(opener.Line, random) is { } reaction)
                {
                    Bot mate = all[random.Next(all.Count)];
                    dueReplies.Add((now + AutonomousBotChatCoordinator.ReplyDelayMs(reaction, 0, random), channel, reaction, mate.Id));
                }
                return;
            }
            int count = AutonomousBotChatCoordinator.BotReplyCount(opener.Line?.Invites, 20, random);
            int due = 0;
            for (int r = 0; r < count; r++)
            {
                Bot mate = all[random.Next(all.Count)];
                foreach (string answers in VC.AnswerChain(opener.Line?.Invites))
                    if (Pick(recent, clock, random, "reply", SpeakerOf(mate), out VC.Picked reply, answers: answers,
                            filter: line => AutonomousChatShortTalk.ReplyFits(opener.Text, line.Text)))
                    {
                        due = AutonomousBotChatCoordinator.ReplyDelayMs(reply.Text, due, random);
                        dueReplies.Add((now + due, channel, reply.Text, mate.Id));
                        break;
                    }
            }
        }
        for (long now = 0; now < end; now += 1_000)
        {
            DateTime clock = new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc).AddMilliseconds(now);
            for (int i = macros.Count - 1; i >= 0; i--)
                if (macros[i].At <= now)
                {
                    var capital = Budget.Band(settings, Budget.Channel.Capital, bots, 20, false, 1.0);
                    if (meter.PerMinute(Budget.Channel.Capital, 1, now) < capital.Ceiling)
                        Say(now, "macro", macros[i].Text, macros[i].Who);
                    macros.RemoveAt(i);
                }
            // Round 7: stories from real events (AutonomousBotChatCoordinator.TellStory): deaths, wipes, dings to 50, at a modelled
            // rate of about one every 6 minutes at 1,000 bots.
            if (budget && now % 60_000 == 0 && random.NextDouble() < 0.17 * bots / 1_000.0)
            {
                Bot teller = all[random.Next(all.Count)];
                Bot mate = Near(now, teller, _ => true), third = Near(now, teller, other => other != mate);
                double roll = random.NextDouble();
                string story = roll < 0.6 ? "death" : roll < 0.95 ? "wipe" : "ding50";
                foreach (AutonomousChatFieldTalk.Step step in AutonomousChatFieldTalk.Story(story, FactsOf(teller, now, random), FactsOf(mate, now, random),
                             third == null ? null : FactsOf(third, now, random), random, field))
                    dueReplies.Add((now + 2_000 + step.AtMs, "faction", step.Text, (step.Who switch { 0 => teller, 1 => mate, _ => third ?? mate }).Id));
            }
            for (int i = dueReplies.Count - 1; i >= 0; i--)
                if (dueReplies[i].At <= now)
                {
                    // AutonomousBotChatCoordinator.PlayThread/PlayStory: not within 2 s of the channel's last line.
                    if (budget && dueReplies[i].Channel == "faction" && now - lastFactionLine < 2_000 && deferred.Count(at => at == dueReplies[i].At) < 1 &&
                        (waits.TryGetValue(dueReplies[i].Text, out int n) ? n : 0) < 3)
                    {
                        waits[dueReplies[i].Text] = (waits.TryGetValue(dueReplies[i].Text, out int m) ? m : 0) + 1;
                        dueReplies[i] = dueReplies[i] with { At = now + 2_000 + random.Next(2_000) };
                        continue;
                    }
                    Say(now, dueReplies[i].Channel, dueReplies[i].Text, dueReplies[i].Who);
                    dueReplies.RemoveAt(i);
                }
            // AutonomousChatBudget.LivePressure: the realm's /faction, and the bot's own /say, capital and guild channels.
            double Pressure(Budget.Channel channel, int count) => budget
                ? Budget.Pressure(meter.PerMinute(channel, 1, now), Budget.Band(settings, channel, count, 20, false, 1.0).Target)
                : 1.0;
            double factionPressure = Pressure(Budget.Channel.Faction, bots), sayPressure = Pressure(Budget.Channel.Say, bots),
                capitalPressure = Pressure(Budget.Channel.Capital, bots), guildPressure = Pressure(Budget.Channel.Guild, guildSize);
            foreach (Bot bot in all)
            {
                if (bot.NextCheck > now)
                    continue;
                bot.NextCheck = now + 15_000 + random.Next(20_001) + bot.Id % 4_000;
                if (random.NextDouble() >= 0.55)
                    continue;   // fighting, casting or riding
                if (bot.InGroup)
                {
                    // In the player's group: /group banter only.
                    bool turn;
                    if (budget)
                        turn = meter.TryStart(Budget.Channel.Group, 1, now, Budget.Band(settings, Budget.Channel.Group, 0, 20, false, 1.0), random)
                            is Budget.Verdict.Speak or Budget.Verdict.SpeakUnderFloor;
                    else if (turn = now >= nextGroup)
                        nextGroup = now + AutonomousBotGroupChatter.NextGapMs(false, random);
                    if (turn && Pick(recent, clock, random, "group", SpeakerOf(bot, "in_player_group"), out VC.Picked g))
                        Say(now, "group", g.Text, bot.Id);
                    continue;
                }
                double pressure = Math.Max(Math.Max(factionPressure, bot.NearPlayer ? sayPressure : 1),
                    Math.Max(bot.InBroadcast ? capitalPressure : 1, bot.InGuild ? guildPressure : 1));
                bool regular = Regular(bot, now);
                if (!AutonomousBotChat.ShouldSpeak(clock, clock.AddMilliseconds(bot.LastSpoke - now), false, random, bot.Rate, pressure, regular))
                    continue;
                // The guild first, as AutonomousBotGuildSocial.TryIdle runs before the ambient line.
                if (bot.InGuild && (!budget || regular || random.NextDouble() < AutonomousChatRegulars.OthersShare ||
                                    meter.PerMinute(Budget.Channel.Guild, 1, now) < Budget.Band(settings, Budget.Channel.Guild, guildSize, 20, false, 1.0).Floor))
                {
                    bool turn;
                    if (budget)
                        turn = meter.TryStart(Budget.Channel.Guild, 1, now, Budget.Band(settings, Budget.Channel.Guild, guildSize, 20, false, 1.0), random)
                            is Budget.Verdict.Speak or Budget.Verdict.SpeakUnderFloor;
                    else if (turn = lastGuild < 0 || now - lastGuild >= AutonomousBotGuildSocial.IdleIntervalMs(BotGuildCharter.Social, guildSize))
                        lastGuild = now;
                    // Beta 1.1.2, round 4: guild lines come mostly from the field (AutonomousBotGuildSocial.FieldLine), a short
                    // written one now and then.
                    if (turn && budget && random.NextDouble() < AutonomousChatFieldTalk.Share("guild") &&
                        AutonomousChatFieldTalk.Line("guild", FactsOf(bot, now, random) with
                            {
                                GuildmateDinged = random.NextDouble() < 0.04 ? "Bot" + (10_000 + random.Next(guildSize)) : null,
                                GuildKeep = "Caer Benowyc",
                            }, guildField, random, now) is { } gsaid)
                    {
                        Say(now, "guild", AutonomousChatIdiolect.Apply(gsaid.Text, bot.Id, false, random, terse: true), bot.Id);
                        Bot gmate = all[random.Next(guildSize)], gthird = all[random.Next(guildSize)];
                        if (gmate == bot) gmate = null;
                        if (gthird == bot || gthird == gmate) gthird = null;
                        Play(now, "guild", AutonomousChatFieldTalk.Thread(gsaid, FactsOf(bot, now, random), gmate == null ? null : FactsOf(gmate, now, random),
                            gthird == null ? null : FactsOf(gthird, now, random), guildField, random, now), [bot, gmate, gthird], gsaid.Answers != null);
                        bot.LastSpoke = now;
                        continue;
                    }
                    if (turn && Pick(recent, clock, random, "guild", SpeakerOf(bot), out VC.Picked gl,
                            filter: budget ? line => AutonomousChatFiller.GuildCorpusFits(line, guildField, now) : null))
                    {
                        if (budget)
                            AutonomousChatFiller.NoteGuildCorpus(gl.Line, guildField, now);
                        Say(now, "guild", gl.Text, bot.Id);
                        bot.LastSpoke = now;
                        continue;
                    }
                }
                if (budget ? Budgeted(bot, now, clock) : Old(bot, now, clock))
                    bot.LastSpoke = now;
            }
        }
        return result;

        bool Old(Bot bot, long now, DateTime clock)
        {
            bool faction = !bot.NearPlayer || random.NextDouble() < 0.24;
            if (faction)
            {
                if (lastFaction >= 0 && now - lastFaction < factionGap)
                    return false;
                lastFaction = now;
                factionGap = OldFactionGapMs(bots, random);
            }
            else
            {
                if (lastLocal >= 0 && now - lastLocal < localGap)
                    return false;
                lastLocal = now;
                localGap = OldLocalGapMs(random);
            }
            string kind = AmbientKind(bot, faction);
            if (!Pick(recent, clock, random, kind, SpeakerOf(bot), out VC.Picked picked) &&
                !Pick(recent, clock, random, "ambient", SpeakerOf(bot), out picked))
                return false;
            if (faction && picked.Line.Kind is "lfg" or "trade" && random.NextDouble() < 0.7)
            {
                Say(now, picked.Line.Kind, picked.Text, bot.Id);
                return true;
            }
            Say(now, faction ? "faction" : "say", picked.Text, bot.Id);
            Replies(now, bot, faction ? "faction" : "say", picked, clock);
            return true;
        }

        bool Budgeted(Bot bot, long now, DateTime clock)
        {
            var options = new List<(Budget.Channel Channel, string Name, (double, double, double) Band, double Measured)>();
            void Add(Budget.Channel channel, string name, int count)
            {
                var band = Budget.Band(settings, channel, count, 20, false, 1.0);
                options.Add((channel, name, band, meter.PerMinute(channel, 1, now)));
            }
            // Round 5: /faction is mostly the regulars; anyone else now and then, or when it runs under its floor.
            if (Regular(bot, now) || random.NextDouble() < AutonomousChatRegulars.OthersShare ||
                meter.PerMinute(Budget.Channel.Faction, 1, now) < AutonomousChatRegulars.OthersBelow * Budget.Band(settings, Budget.Channel.Faction, bots, 20, false, 1.0).Target)
                Add(Budget.Channel.Faction, "faction", bots);
            Add(Budget.Channel.Lfg, "lfg", bots);
            Add(Budget.Channel.Trade, "trade", bots);
            if (bot.NearPlayer)
                Add(Budget.Channel.Say, "say", bots);
            if (bot.InBroadcast)
                Add(Budget.Channel.Capital, "broadcast", bots);
            // Round 5: a regular talks in /faction first when its gap is over (AutonomousBotChatCoordinator.TryStartBudgeted).
            bool regularFirst = Regular(bot, now) && options.Count > 0 && options[0].Name == "faction";
            while (options.Count > 0)
            {
                double total = options.Sum(o => Budget.ChoiceWeight(o.Band.Item2, o.Measured));
                double draw = random.NextDouble() * total;
                int pick = 0;
                if (regularFirst)
                {
                    regularFirst = false;
                    draw = -1;
                }
                for (; draw >= 0 && pick < options.Count - 1; pick++)
                {
                    double weight = Budget.ChoiceWeight(options[pick].Band.Item2, options[pick].Measured);
                    if (draw < weight)
                        break;
                    draw -= weight;
                }
                var choice = options[pick];
                options.RemoveAt(pick);
                if (meter.TryStart(choice.Channel, 1, now, choice.Band, random) is Budget.Verdict.HoldCeiling or Budget.Verdict.HoldGap)
                    continue;
                // Beta 1.1.2, round 3: most unprompted lines are field lines from the bot's state (AutonomousChatFieldTalk).
                if (random.NextDouble() < AutonomousChatFieldTalk.Share(choice.Name) &&
                    AutonomousChatFieldTalk.Line(choice.Name, FactsOf(bot, now, random), field, random, now) is { } said)
                {
                    string text = AutonomousChatIdiolect.Apply(said.Text, bot.Id, false, random, terse: true);
                    if (!fresh.TryAdd(choice.Name.GetHashCode(), text, now))
                        continue;
                    Say(now, choice.Name, text, bot.Id);
                    if (choice.Name == "broadcast" && said.Kind == "wts")
                        Macro(now, text, bot.Id);
                    if (choice.Name is "faction" or "say")
                        Field(now, bot, choice.Name, said, field, choice.Name == "faction" ? _ => true : mate => mate.NearPlayer);
                    return true;
                }
                string kind = choice.Name switch
                {
                    "lfg" => "lfg", "trade" => "trade",
                    "broadcast" => random.NextDouble() is var roll && roll < 0.5 ? "trade" : roll < 0.75 ? "lfg" : "ambient",
                    _ => AmbientKind(bot, choice.Name == "faction") is var k && k is "lfg" or "trade" && random.NextDouble() < 0.7 ? "ambient" : k,
                };
                VC.Speaker speaker = choice.Name == "broadcast" ? SpeakerOf(bot, "in_town") : SpeakerOf(bot);
                if (!Pick(recent, clock, random, kind, speaker, out VC.Picked picked, filter: CorpusFilter(choice.Name == "faction", now)) &&
                    (choice.Name is "lfg" or "trade" || !Pick(recent, clock, random, "ambient", speaker, out picked, filter: CorpusFilter(choice.Name == "faction", now))))
                    continue;
                if (!fresh.TryAdd(choice.Name.GetHashCode(), picked.Text, now))
                    continue;
                // AutonomousBotChatCoordinator.NoteWritten: the line's idea rests, a musing starts the realm's 20 minutes.
                if (picked.Line?.Kind is "ambient" or "rvr" or "camp" or "group")
                {
                    field.Use(null, AutonomousChatFiller.IdeaOf(picked.Line), now);
                    field.NoteWritten(now);
                }
                Say(now, choice.Name, picked.Text, bot.Id);
                // A seller's macro (AutonomousChatBroadcast.StartMacro): again every few minutes for a while, under the ceiling.
                if (choice.Name == "broadcast" && picked.Line?.Kind == "trade")
                    Macro(now, picked.Text, bot.Id);
                if (choice.Name is "faction" or "say")
                    Replies(now, bot, choice.Name, picked, clock);
                return true;
            }
            return false;
        }

        Func<VC.Line, bool> CorpusFilter(bool faction, long now)
        {
            if (!field.WrittenAllowed(now))
                return _ => false;
            Func<VC.Line, bool> basic = AutonomousChatFiller.AmbientFilter(faction, random);
            return line => basic(line) && !field.Rested(null, AutonomousChatFiller.IdeaOf(line), now);
        }

        void Macro(long now, string text, long who)
        {
            if (random.NextDouble() >= settings.MacroChance)
                return;
            long until = now + 1_000L * (settings.MacroLastMinS + random.Next(settings.MacroLastMaxS - settings.MacroLastMinS));
            for (long at = now + 1_000L * (settings.MacroRepeatMinS + random.Next(settings.MacroRepeatMaxS - settings.MacroRepeatMinS)); at < until;
                 at += 1_000L * (settings.MacroRepeatMinS + random.Next(settings.MacroRepeatMaxS - settings.MacroRepeatMinS)))
                macros.Add((at, text, who));
        }

        string AmbientKind(Bot bot, bool faction)
        {
            var kinds = new List<(string, double)> { ("ambient", 6) };
            if (bot.Solo && bot.Level >= 5) kinds.Add(("lfg", bot.Id % 6 is 2 or 0 ? 3 : 1.5));
            if (bot.Id % 6 == 1) kinds.Add(("trade", 3));
            if (bot.Level >= 40 && bot.Id % 6 is 4 or 5) kinds.Add(("rvr", 1));
            double draw = random.NextDouble() * kinds.Sum(k => k.Item2);
            foreach ((string kind, double weight) in kinds)
            {
                if (draw < weight)
                    return kind;
                draw -= weight;
            }
            return "ambient";
        }
    }

    private static readonly string[] Channels = ["faction", "lfg", "trade", "say", "broadcast", "guild", "group"];

    private static readonly string[] DistinctChannels = ["faction", "lfg", "trade", "broadcast", "guild"];

    /// <summary>/faction threads: the share of inviting lines answered, the mean length of answered threads, the share of 4+ lines.</summary>
    public static readonly Dictionary<(int, bool), (double Rate, double Mean, double Long)> threadShape = new();

    /// <summary>The share of /faction gaps of 2 s or less.</summary>
    public static readonly Dictionary<(int, bool), double> fastShape = new();

    /// <summary>The most life lines (weather, school, work, pets...) one evening's guild heard.</summary>
    public static readonly Dictionary<(int, bool), int> lifeShape = new();

    /// <summary>Speakers per 30 lines and the top tenth's share, for /faction and guild (averaged over the evenings).</summary>
    public static readonly Dictionary<(int, bool), (double Per30Faction, double TopFaction, double Per30Guild, double TopGuild)> speakerShape = new();

    /// <summary>The worst evening's distinct share per channel, lines of three words or more (macros left out).</summary>
    public static readonly Dictionary<(int, bool), Dictionary<string, double>> distinctShares = new();

    /// <summary>The near-repeat share of each run's spoken lines (within an evening, macros left out).</summary>
    public static readonly Dictionary<(int, bool), double> nearRepeats = new();

    public static string Report(int[] populations, out Dictionary<(int, bool), Result> results)
    {
        results = new Dictionary<(int, bool), Result>();
        var text = new StringBuilder();
        text.AppendLine("An evening hour (20:00, three evenings averaged) of one realm's ambient chat, lines a minute as the player hears each channel (UT_ChatBudgetSimulation).");
        text.AppendLine("Before = Beta 1.1.1 gaps (population factor capped at 1.8); after = the Beta 1.1.2 ambient budget (chat-budget.json).");
        text.AppendLine();
        text.AppendLine("Broadcast includes sellers' macros (the same WTS line every 3-8 min, by design); repeats are counted without them.");
        text.AppendLine();
        text.AppendLine("| bots/realm | side | " + string.Join(" | ", Channels) + " | total | macro repeats | exact repeats | near repeats | busiest bot (faction / lfg / trade / say / broadcast) | distinct, 3+ words (faction / lfg / trade / broadcast / guild, worst hour) | speakers per 30 lines (faction / guild) | top 10% of speakers' share (faction / guild) | threads: answered / mean lines / 4+ lines (faction) | /faction gaps of 2 s or less |");
        text.AppendLine("|---|---|" + string.Concat(Channels.Select(_ => "---|")) + "---|---|---|---|---|---|---|---|---|---|");
        foreach (int bots in populations)
            foreach (bool budget in new[] { false, true })
            {
                // Three evenings, so the averages don't hang on one draw.
                Result result = new() { Minutes = 0 };
                double exact = 0, near = 0;
                var distinct = DistinctChannels.ToDictionary(c => c, _ => 1.0);
                int lifeLines = 0;
                double per30Faction = 0, per30Guild = 0, topFaction = 0, topGuild = 0, threadRate = 0, threadMean = 0, threadLong = 0, fastGaps = 0;
                int[] seeds = [1103, 2207, 3301];
                foreach (int seed in seeds)
                {
                    Result evening = Evening(bots, budget, seed + bots);
                    result.Minutes += evening.Minutes;
                    foreach (var pair in evening.Lines)
                        for (int k = 0; k < pair.Value.Count; k++)
                            result.Add(pair.Key, pair.Value[k], evening.Speakers.TryGetValue(pair.Key, out var who) && k < who.Count
                                ? seed * 1_000_000L + who[k] : 0);
                    // Repeats within one evening (the server keeps its no-repeat memory across evenings).
                    string[] spoken = evening.Lines.Keys.Where(c => c != "macro").ToArray();
                    int lines = Math.Max(1, spoken.Sum(c => evening.Lines[c].Count));
                    exact += spoken.Sum(c => evening.Duplicates(c).Exact * evening.Lines[c].Count) / lines / seeds.Length;
                    near += spoken.Sum(c => evening.Duplicates(c).Near * evening.Lines[c].Count) / lines / seeds.Length;
                    // Distinct lines within each evening hour, the worst of the three.
                    foreach (string c in DistinctChannels)
                        distinct[c] = Math.Min(distinct[c], evening.DistinctShare(c));
                    per30Faction += evening.SpeakersPer30("faction") / seeds.Length;
                    per30Guild += evening.SpeakersPer30("guild") / seeds.Length;
                    topFaction += evening.TopTenthShare("faction") / seeds.Length;
                    topGuild += evening.TopTenthShare("guild") / seeds.Length;
                    threadRate += evening.ThreadRate("faction") / seeds.Length;
                    threadMean += evening.ThreadLength("faction").Mean / seeds.Length;
                    threadLong += evening.ThreadLength("faction").Long / seeds.Length;
                    fastGaps += evening.FastGapShare("faction") / seeds.Length;
                    lifeLines = Math.Max(lifeLines, evening.Lines.TryGetValue("guild", out var guildLines) ? guildLines.Count(AutonomousChatFiller.IsLife) : 0);
                }
                results[(bots, budget)] = result;
                text.AppendLine($"| {bots} | {(budget ? "after" : "before")} | " + string.Join(" | ", Channels.Select(c => result.PerMinute(c).ToString("0.00"))) +
                                $" | {result.Total:0.00} | {(result.Lines.TryGetValue("macro", out var m) ? m.Count / (double)result.Minutes : 0):0.00}/min | {exact:P1} | {near:P1} | " +
                                string.Join(" / ", new[] { "faction", "lfg", "trade", "say", "broadcast" }.Select(c => result.TopSpeakerShare(c).ToString("P0"))) + " | " +
                                string.Join(" / ", DistinctChannels.Select(c => distinct[c].ToString("P0"))) +
                                $" | {per30Faction:0.0} / {per30Guild:0.0} | {topFaction:P0} / {topGuild:P0} | {threadRate:P0} / {threadMean:0.0} / {threadLong:P0} | {fastGaps:P0} |");
                fastShape[(bots, budget)] = fastGaps;
                threadShape[(bots, budget)] = (threadRate, threadMean, threadLong);
                lifeShape[(bots, budget)] = lifeLines;
                speakerShape[(bots, budget)] = (per30Faction, topFaction, per30Guild, topGuild);
                nearRepeats[(bots, budget)] = near;
                distinctShares[(bots, budget)] = distinct;
            }
        return text.ToString();
    }

    [Test]
    public void AFullServerEveningIsClearlyBusierThanBeta111AndStaysUnderTheCeilings()
    {
        string report = Report([300, 1_000], out var results);
        string path = Environment.GetEnvironmentVariable("TALK_BUDGET_OUT");
        if (!string.IsNullOrEmpty(path))
            File.WriteAllText(path, report);
        else
            TestContext.Out.WriteLine(report);
        Result before = results[(1_000, false)], after = results[(1_000, true)];
        // The owner's standing rule: never quiet. /faction meets its busy-peak target of 4 a minute at 1,000 bots.
        Assert.That(after.PerMinute("faction"), Is.GreaterThanOrEqualTo(4.0), "/faction at its target at 1,000 bots");
        // TALK_BUDGET_SAMPLE: the first lines of each channel at 1,000 bots, to read.
        string sample = Environment.GetEnvironmentVariable("TALK_BUDGET_SAMPLE");
        if (!string.IsNullOrEmpty(sample))
            File.WriteAllLines(sample, Channels.Where(after.Lines.ContainsKey).SelectMany(c => after.Lines[c].Take(80).Select(line => $"[{c}] {line}")));
        Assert.That(after.Total, Is.GreaterThan(before.Total * 1.5), "the ambient world at 1,000 bots a realm is clearly busier");
        Assert.That(after.PerMinute("faction"), Is.GreaterThan(before.PerMinute("faction")), "/faction is busier");
        Assert.That(results[(300, true)].Total, Is.GreaterThanOrEqualTo(results[(300, false)].Total), "and never quieter at 300");
        Assert.That(after.Total, Is.GreaterThan(results[(300, true)].Total), "more bots, more chat");
        // Busy, not spam (owner, 2026-11-03): few near-repeats, and no one bot carries a realm channel.
        foreach (int bots in new[] { 300, 1_000 })
        {
            Assert.That(nearRepeats[(bots, true)], Is.LessThan(0.02), $"near-repeats at {bots}");
            foreach (string channel in new[] { "faction", "lfg", "trade" })
                Assert.That(results[(bots, true)].TopSpeakerShare(channel), Is.LessThan(0.15), $"busiest bot in {channel} at {bots}");
            // Round 5: regulars. /faction has fewer speakers than lines, and its top tenth of speakers carry 40-65% of the lines.
            var shape = speakerShape[(bots, true)];
            // Round 6: 45-55% at a busy peak (research: about 60%); a small server's few regulars may carry a little more.
            // 1.1.3 after RC6 (owner's call, 2026-11-09): up to 58%, so "what class?" can be answered only with a class
            // (fewer bots fit that answer, so the regulars carry a little more of /faction; about 0.557 in the simulation).
            Assert.That(shape.TopFaction, bots >= 1_000 ? Is.InRange(0.45, 0.58) : Is.InRange(0.40, 0.75), $"top tenth's share of /faction at {bots}");
            Assert.That(shape.Per30Faction, Is.LessThanOrEqualTo(22), $"speakers per 30 /faction lines at {bots}");
            // Round 7: a line that invites a reply usually gets one, and some threads run on; a guild hears one life line an evening at most.
            // 1.1.3, after RC5 (owner's PC: most bot questions unanswered): a question is always answered when a bot can answer it,
            // the other inviting lines about seven in ten.
            var threads = threadShape[(bots, true)];
            Assert.That(threads.Rate, Is.InRange(0.75, 0.97), $"inviting /faction lines answered at {bots}");
            // 1.1.3, after RC5: a bot's lines now fit its state (no "lfg" or "lf duo" from a bot in a group, no trainer question
            // from a 50, ...), which shifts the mix of openers; at 1,000 bots 11.1% of answered threads ran to four lines.
            Assert.That(threads.Long, Is.InRange(0.10, 0.35), $"threads of 4+ lines at {bots}");
            Assert.That(lifeShape[(bots, true)], Is.LessThanOrEqualTo(1), $"life lines in one guild evening at {bots}");
            // Round 8: answers take a person's time, and no line starts within 2-3 s of the last. Fewer /faction gaps are 2 s or less
            // than chance would give at this rate (about 15% for 5 lines a minute); the stream judge had seen 19 of 41.
            Assert.That(fastShape[(bots, true)], Is.LessThan(0.15), $"/faction gaps of 2 s or less at {bots}");
            // Round 4: at least 80% of an hour's lines of three words or more are distinct, in every channel.
            foreach ((string channel, double share) in distinctShares[(bots, true)])
                Assert.That(share, Is.GreaterThanOrEqualTo(0.8), $"distinct lines in {channel} at {bots}");
        }
        // The owner's rule (2026-11-03): the amount of chatting must not go down, in any channel the player hears.
        foreach (int bots in new[] { 300, 1_000 })
            foreach (string channel in Channels)
                Assert.That(results[(bots, true)].PerMinute(channel), Is.GreaterThanOrEqualTo(results[(bots, false)].PerMinute(channel)),
                    $"{channel} at {bots} bots a realm");
        Budget.Settings settings = Budget.Current;
        foreach ((string name, Budget.Channel channel) in new[] { ("faction", Budget.Channel.Faction), ("lfg", Budget.Channel.Lfg),
                     ("trade", Budget.Channel.Trade), ("broadcast", Budget.Channel.Capital) })
            Assert.That(after.PerMinute(name), Is.LessThanOrEqualTo(Budget.Band(settings, channel, 1_000, 20, false, 1.0).Ceiling),
                name + " stays under its ceiling over the hour");
    }

    /// <summary>
    /// Round 4: over a three-hour evening at 1,000 bots a realm, at least 80% of each channel's lines of three words or more are
    /// distinct, and no short reply ("me", "x me") carries more than 2% of /faction. TALK_BUDGET_LONG keeps the counts.
    /// </summary>
    [Test]
    public void AThreeHourEveningStaysVaried()
    {
        Result evening = Evening(1_000, true, 4409, minutes: 180);
        var report = new StringBuilder("Three hours at 1,000 bots a realm (lines of three words or more; distinct share)\n");
        foreach (string channel in DistinctChannels)
        {
            int lines = evening.Lines.TryGetValue(channel, out var list) ? list.Count : 0;
            double all = lines == 0 ? 1 : list.Distinct().Count() / (double)lines;
            report.AppendLine($"{channel}: {lines} lines, {evening.DistinctShare(channel):P0} distinct (3+ words), {all:P0} of all lines" +
                              (list == null ? "" : ", top: " + string.Join(", ", list.GroupBy(t => t).OrderByDescending(g => g.Count()).Take(4).Select(g => $"\"{g.Key}\" {g.Count()}"))));
        }
        report.AppendLine($"faction speakers per 30 lines {evening.SpeakersPer30("faction"):0.0}, top tenth's share {evening.TopTenthShare("faction"):P0}; " +
                          $"guild {evening.SpeakersPer30("guild"):0.0}, {evening.TopTenthShare("guild"):P0}");
        string path = Environment.GetEnvironmentVariable("TALK_BUDGET_LONG");
        if (!string.IsNullOrEmpty(path))
            File.WriteAllText(path, report.ToString());
        else
            TestContext.Out.WriteLine(report);
        // Round 6: at least 95% of the lines of three words or more are distinct over three hours.
        foreach (string channel in DistinctChannels)
            Assert.That(evening.DistinctShare(channel), Is.GreaterThanOrEqualTo(0.95), channel);
        // 1.1.3: an evening keeps one cast of regulars (no staggered rotation), so over three hours they carry a little more.
        Assert.That(evening.TopTenthShare("faction"), Is.InRange(0.45, 0.70), "the regulars carry about half of /faction");
        Assert.That(evening.SpeakersPer30("faction"), Is.LessThanOrEqualTo(22));
        List<string> faction = evening.Lines["faction"];
        Assert.That(faction.GroupBy(t => t).Max(g => g.Count()) / (double)faction.Count, Is.LessThan(0.02), "no one short reply carries /faction");
    }

    [Test]
    public void TheBudgetHasNoPopulationCap()
    {
        Budget.Settings settings = Budget.Current;
        double Target(int bots) => Budget.Band(settings, Budget.Channel.Faction, bots, 20, false, 1.0).Target;
        Assert.That(Target(1_000), Is.GreaterThan(Target(390)));
        Assert.That(Target(3_000), Is.GreaterThan(Target(1_000)));
        Assert.That(Target(3_000) / Target(1_000), Is.LessThan(3.0), "sublinear");
        Assert.That(Target(1_000), Is.EqualTo(settings.Band(Budget.Channel.Faction).Target).Within(1e-9), "the design point is a full server at prime time");
    }
}
