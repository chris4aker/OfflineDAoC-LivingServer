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
/// Beta 1.1.3: an evening hour of one realm's ambient chat, before (Beta 1.1.1's gaps, capped at 1.8) and after (the
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
        public void Add(string channel, string text)
        {
            if (!Lines.TryGetValue(channel, out var list))
                Lines[channel] = list = new List<string>();
            list.Add(text);
        }

        /// <summary>The share of a channel's lines that repeat an earlier line of it exactly, and nearly (same skeleton).</summary>
        public (double Exact, double Near) Duplicates(string channel)
        {
            if (!Lines.TryGetValue(channel, out var list) || list.Count == 0)
                return (0, 0);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var skeletons = new HashSet<string>(StringComparer.Ordinal);
            int exact = 0, near = 0;
            foreach (string text in list)
            {
                if (!seen.Add(text))
                    exact++;
                if (!skeletons.Add(AutonomousChatFiller.Skeleton(text)))
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
        public double Rate;
    }

    /// <summary>Beta 1.1.1's /faction gap (AutonomousBotChatCoordinator.FactionGapMs before 1.1.3), for the "before" side.</summary>
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

    private static bool Pick(VC.RecentLines recent, DateTime now, Random random, string kind, VC.Speaker speaker, out VC.Picked picked,
        string answers = null, Func<VC.Line, bool> filter = null) =>
        Corpus.TryPick(new VC.Request(kind, speaker, Slots(speaker), recent, now, random)
        {
            Answers = answers, Filter = filter, LocalHour = 20, RelaxTo = VC.RecentLines.Relaxed,
        }, out picked);

    /// <summary>One realm's evening hour (after a ten-minute warm-up) with <paramref name="bots"/> online.</summary>
    public static Result Evening(int bots, bool budget, int seed)
    {
        var random = new Random(seed);
        var recent = new VC.RecentLines();
        var meter = new Budget.ChannelMeter();
        var fresh = new AutonomousChatFiller.RecentTexts();
        Budget.Settings settings = Budget.Current;
        var result = new Result { Minutes = 60 };
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
        long start = 10 * 60_000, end = start + 60 * 60_000;
        var dueReplies = new List<(long At, string Channel, string Text)>();
        var macros = new List<(long At, string Text)>();
        void Say(long now, string channel, string text)
        {
            if (now >= start && now < end)
                result.Add(channel, text);
            Budget.Channel? metered = channel switch
            {
                "faction" => Budget.Channel.Faction, "lfg" => Budget.Channel.Lfg, "trade" => Budget.Channel.Trade, "say" => Budget.Channel.Say,
                "broadcast" or "macro" => Budget.Channel.Capital, "guild" => Budget.Channel.Guild, "group" => Budget.Channel.Group, _ => null,
            };
            if (metered is { } c)
                meter.Count(c, 1, now);
        }
        void Replies(long now, Bot starter, string channel, VC.Picked opener, DateTime clock)
        {
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
                        dueReplies.Add((now + due, channel, reply.Text));
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
                        Say(now, "macro", macros[i].Text);
                    macros.RemoveAt(i);
                }
            for (int i = dueReplies.Count - 1; i >= 0; i--)
                if (dueReplies[i].At <= now)
                {
                    Say(now, dueReplies[i].Channel, dueReplies[i].Text);
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
                        Say(now, "group", g.Text);
                    continue;
                }
                double pressure = Math.Max(Math.Max(factionPressure, bot.NearPlayer ? sayPressure : 1),
                    Math.Max(bot.InBroadcast ? capitalPressure : 1, bot.InGuild ? guildPressure : 1));
                if (!AutonomousBotChat.ShouldSpeak(clock, clock.AddMilliseconds(bot.LastSpoke - now), false, random, bot.Rate, pressure))
                    continue;
                // The guild first, as AutonomousBotGuildSocial.TryIdle runs before the ambient line.
                if (bot.InGuild)
                {
                    bool turn;
                    if (budget)
                        turn = meter.TryStart(Budget.Channel.Guild, 1, now, Budget.Band(settings, Budget.Channel.Guild, guildSize, 20, false, 1.0), random)
                            is Budget.Verdict.Speak or Budget.Verdict.SpeakUnderFloor;
                    else if (turn = lastGuild < 0 || now - lastGuild >= AutonomousBotGuildSocial.IdleIntervalMs(BotGuildCharter.Social, guildSize))
                        lastGuild = now;
                    if (turn && Pick(recent, clock, random, "guild", SpeakerOf(bot), out VC.Picked gl))
                    {
                        Say(now, "guild", gl.Text);
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
                Say(now, picked.Line.Kind, picked.Text);
                return true;
            }
            Say(now, faction ? "faction" : "say", picked.Text);
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
            Add(Budget.Channel.Faction, "faction", bots);
            Add(Budget.Channel.Lfg, "lfg", bots);
            Add(Budget.Channel.Trade, "trade", bots);
            if (bot.NearPlayer)
                Add(Budget.Channel.Say, "say", bots);
            if (bot.InBroadcast)
                Add(Budget.Channel.Capital, "broadcast", bots);
            while (options.Count > 0)
            {
                double total = options.Sum(o => Budget.ChoiceWeight(o.Band.Item2, o.Measured));
                double draw = random.NextDouble() * total;
                int pick = 0;
                for (; pick < options.Count - 1; pick++)
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
                string kind = choice.Name switch
                {
                    "lfg" => "lfg", "trade" => "trade",
                    "broadcast" => random.NextDouble() is var roll && roll < 0.5 ? "trade" : roll < 0.75 ? "lfg" : "ambient",
                    _ => AmbientKind(bot, choice.Name == "faction") is var k && k is "lfg" or "trade" && random.NextDouble() < 0.7 ? "ambient" : k,
                };
                VC.Speaker speaker = choice.Name == "broadcast" ? SpeakerOf(bot, "in_town") : SpeakerOf(bot);
                if (!Pick(recent, clock, random, kind, speaker, out VC.Picked picked, filter: line => AutonomousChatFiller.AmbientAllowed(line, choice.Name != "say", random)) &&
                    (choice.Name is "lfg" or "trade" || !Pick(recent, clock, random, "ambient", speaker, out picked)))
                    continue;
                if (!fresh.TryAdd(choice.Name.GetHashCode(), picked.Text, now))
                    continue;
                Say(now, choice.Name, picked.Text);
                // A seller's macro (AutonomousChatBroadcast.StartMacro): again every few minutes for a while, under the ceiling.
                if (choice.Name == "broadcast" && picked.Line?.Kind == "trade" && random.NextDouble() < settings.MacroChance)
                {
                    long until = now + 1_000L * (settings.MacroLastMinS + random.Next(settings.MacroLastMaxS - settings.MacroLastMinS));
                    for (long at = now + 1_000L * (settings.MacroRepeatMinS + random.Next(settings.MacroRepeatMaxS - settings.MacroRepeatMinS)); at < until;
                         at += 1_000L * (settings.MacroRepeatMinS + random.Next(settings.MacroRepeatMaxS - settings.MacroRepeatMinS)))
                        macros.Add((at, picked.Text));
                }
                if (choice.Name is "faction" or "say")
                    Replies(now, bot, choice.Name, picked, clock);
                return true;
            }
            return false;
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

    public static string Report(int[] populations, out Dictionary<(int, bool), Result> results)
    {
        results = new Dictionary<(int, bool), Result>();
        var text = new StringBuilder();
        text.AppendLine("An evening hour (20:00) of one realm's ambient chat, lines a minute as the player hears each channel (UT_ChatBudgetSimulation).");
        text.AppendLine("Before = Beta 1.1.1 gaps (population factor capped at 1.8); after = the Beta 1.1.3 ambient budget (chat-budget.json).");
        text.AppendLine();
        text.AppendLine("Broadcast includes sellers' macros (the same WTS line every 3-8 min, by design); repeats are counted without them.");
        text.AppendLine();
        text.AppendLine("| bots/realm | side | " + string.Join(" | ", Channels) + " | total | macro repeats | exact repeats | near repeats |");
        text.AppendLine("|---|---|" + string.Concat(Channels.Select(_ => "---|")) + "---|---|---|---|");
        foreach (int bots in populations)
            foreach (bool budget in new[] { false, true })
            {
                Result result = Evening(bots, budget, 1103 + bots);
                results[(bots, budget)] = result;
                string[] spoken = result.Lines.Keys.Where(c => c != "macro").ToArray();
                int lines = spoken.Sum(c => result.Lines[c].Count);
                double exact = spoken.Sum(c => result.Duplicates(c).Exact * result.Lines[c].Count) / Math.Max(1, lines);
                double near = spoken.Sum(c => result.Duplicates(c).Near * result.Lines[c].Count) / Math.Max(1, lines);
                text.AppendLine($"| {bots} | {(budget ? "after" : "before")} | " + string.Join(" | ", Channels.Select(c => result.PerMinute(c).ToString("0.00"))) +
                                $" | {result.Total:0.00} | {(result.Lines.TryGetValue("macro", out var m) ? m.Count / 60.0 : 0):0.00}/min | {exact:P1} | {near:P1} |");
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
        Assert.That(after.Total, Is.GreaterThan(before.Total * 1.5), "the ambient world at 1,000 bots a realm is clearly busier");
        Assert.That(after.PerMinute("faction"), Is.GreaterThan(before.PerMinute("faction")), "/faction is busier");
        Assert.That(results[(300, true)].Total, Is.GreaterThanOrEqualTo(results[(300, false)].Total), "and never quieter at 300");
        Assert.That(after.Total, Is.GreaterThan(results[(300, true)].Total), "more bots, more chat");
        Budget.Settings settings = Budget.Current;
        foreach ((string name, Budget.Channel channel) in new[] { ("faction", Budget.Channel.Faction), ("lfg", Budget.Channel.Lfg),
                     ("trade", Budget.Channel.Trade), ("broadcast", Budget.Channel.Capital) })
            Assert.That(after.PerMinute(name), Is.LessThanOrEqualTo(Budget.Band(settings, channel, 1_000, 20, false, 1.0).Ceiling),
                name + " stays under its ceiling over the hour");
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
