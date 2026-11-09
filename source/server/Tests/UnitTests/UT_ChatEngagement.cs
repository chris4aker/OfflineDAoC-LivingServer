using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Engagement = DOL.GS.AutonomousChatEngagement;
using Ex = DOL.GS.AutonomousChatExchange;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Recall = DOL.GS.AutonomousChatRecall;
using Regulars = DOL.GS.AutonomousChatRegulars;

namespace DOL.GS.Tests;

/// <summary>
/// 1.1.3, the realism gaps of the owner's evening (RC3 on the owner's PC: 5 bot lines named the player in 2.5 hours; no topic
/// ever came back; 301 bots spoke in /faction and the top ten carried a quarter of it). Bots that take notice of the player
/// (paced), follow-ups to what the channel really carried, and a stable cast of regulars with running bits. The last test plays
/// an evening on a simulated clock the old way and the new, writes both transcripts for source/tools/chat_audit.py and prints
/// the measurements. These are tests: nothing here was seen on the owner's PC yet.
/// </summary>
[TestFixture]
public class UT_ChatEngagement
{
    // ---- 1. pacing ------------------------------------------------------------------------------------------------------------

    [Test]
    public void ASoloPlayerHearsAFewUnpromptedLinesAnHourAndMoreWhenGroupedOrAmongFriends()
    {
        Assert.That(Engagement.Allowance(false, 0), Is.EqualTo((Engagement.SoloPerHour, Engagement.SoloGapMs)));
        Assert.That(Engagement.Allowance(true, 0).PerHour, Is.GreaterThan(Engagement.SoloPerHour));
        Assert.That(Engagement.Allowance(false, 2).PerHour, Is.GreaterThan(Engagement.SoloPerHour), "two friends online");
        var pacer = new Engagement.Pacer();
        int solo = 0, grouped = 0;
        for (long now = 0; now < 60 * 60_000; now += 10_000)
        {
            if (pacer.TryTake("Questor", now, false, 0))
                solo++;
            if (pacer.TryTake("Grouper", now, true, 0))
                grouped++;
        }
        Assert.That(solo, Is.EqualTo(Engagement.SoloPerHour));
        Assert.That(grouped, Is.EqualTo(Engagement.WarmPerHour));
        // A death seen in front of a bot still gets its line in a full hour, a minute after the last one.
        Assert.That(pacer.TryTake("Questor", 59 * 60_000, false, 0), Is.False);
        Assert.That(pacer.TryTake("Questor", 59 * 60_000, false, 0, witnessed: true), Is.True);
        Assert.That(pacer.TryTake("Questor", 59 * 60_000 + 30_000, false, 0, witnessed: true), Is.False);
    }

    [Test]
    public void LinesFromOtherPathsCountAgainstTheAllowance()
    {
        var pacer = new Engagement.Pacer();
        pacer.Note("Questor", 0);
        Assert.That(pacer.TryTake("Questor", 60_000, false, 0), Is.False, "five minutes apart at the least");
        Assert.That(pacer.TryTake("Questor", Engagement.SoloGapMs, false, 0), Is.True);
        Assert.That(pacer.LastHour("Questor", Engagement.SoloGapMs), Is.EqualTo(2));
        Assert.That(pacer.LastHour("Questor", 2 * 60 * 60_000), Is.Zero, "an hour later it's forgotten");
    }

    // ---- 2. the words ---------------------------------------------------------------------------------------------------------

    [Test]
    public void EveryEngagementLineNamesWhatReallyHappened()
    {
        var random = new Random(4);
        for (int i = 0; i < 50; i++)
        {
            Assert.That(Engagement.LevelUpTell("Questor", 27, random), Does.Contain("27").Or.Contain("ding"));
            Assert.That(Engagement.LevelUpTell("Questor", 50, random), Does.Contain("50"));
            Assert.That(Engagement.ZoneTell("Questor", "Lyonesse", random), Does.Contain("Lyonesse").Or.Contain("Questor"));
            Assert.That(Engagement.GearLine("Questor", "Ghostly Cloak", false, random), Does.Contain("Ghostly Cloak"));
            Assert.That(Engagement.LoginTell("Questor", true, random), Does.Contain("Questor").Or.Contain("duo").Or.Contain("company"));
            Assert.That(Engagement.RezOffer("Questor", false, random), Does.Contain("rez"));
        }
    }

    [Test]
    public void OnlyNotableGearDrawsARemark()
    {
        Assert.That(Engagement.NotableGear(100, 30, 30, true), Is.True, "100% quality");
        Assert.That(Engagement.NotableGear(90, 35, 30, true), Is.True, "above the player's level");
        Assert.That(Engagement.NotableGear(90, 28, 30, true), Is.False);
        Assert.That(Engagement.NotableGear(100, 30, 30, false), Is.False);
    }

    [TestCase(BotGroupInvite.Duty.RealmRaid, "Krondon", "Krondon")]
    [TestCase(BotGroupInvite.Duty.Siege, null, "keep")]
    [TestCase(BotGroupInvite.Duty.StableRoute, null, "horse")]
    [TestCase(BotGroupInvite.Duty.Relic, null, "relic")]
    public void ABotTurningAnInviteDownSaysWhyLikeAPlayer(BotGroupInvite.Duty duty, string zone, string mentions)
    {
        var random = new Random(2);
        for (int i = 0; i < 20; i++)
        {
            string reply = Engagement.DeclineReply(duty, false, zone, true, random);
            if (duty == BotGroupInvite.Duty.RealmRaid)
                Assert.That(reply, Does.Contain(mentions), reply);
            else
                Assert.That(reply, Does.Contain(mentions).Or.Contain("riding").Or.Contain("siege"), reply);
        }
        Assert.That(Engagement.DeclineReply(BotGroupInvite.Duty.None, true, null, false, random), Does.Contain("grp").Or.Contain("grouped"));
        Assert.That(Engagement.DeclineReply(BotGroupInvite.Duty.Helper, false, null, false, random), Is.Null, "a companion helper isn't ours to voice");
    }

    [TestCase("hey what are you up to tonight?", true)]
    [TestCase("wyd", true)]
    [TestCase("hows it going", true)]
    [TestCase("hey", true)]
    [TestCase("hey Ilaan", true)]
    [TestCase("where is the cloth merchant?", false)]
    [TestCase("wts arcane shield", false)]
    public void SmallTalkIsTold(string message, bool small) => Assert.That(Engagement.IsSmallTalk(message), Is.EqualTo(small));

    [Test]
    public void AFriendAnswersSmallTalkFromWhatItIsReallyDoing()
    {
        var random = new Random(5);
        var hunting = new Engagement.Doing("Lyonesse", "large skeleton", null, false, false, false, false);
        for (int i = 0; i < 30; i++)
        {
            string line = Engagement.FriendChat("Manawatch", hunting, "Avalon City", true, random);
            Assert.That(line, Does.Contain("Manawatch"));
            Assert.That(line, Does.Contain("Lyonesse"));
            Assert.That(line, Does.Contain("skeletons"));
            Assert.That(line, Does.Contain("?").Or.Contain("join"), "it asks the player along");
        }
        var grouped = new Engagement.Doing("Lyonesse", null, null, true, false, false, false);
        string busy = Engagement.FriendChat("Manawatch", grouped, "Avalon City", false, random);
        Assert.That(busy, Does.Contain("grp").Or.Contain("grouped"));
        Assert.That(busy, Does.Not.Contain("want in"));
    }

    [TestCase("ty for the inv, where are you all?", 2, false, false)]
    [TestCase("ty for the inv, where are you all?", 4, false, true)]
    [TestCase("who is main assist?", 2, false, false)]
    [TestCase("who is main assist?", 5, false, true)]
    [TestCase("hi all", 2, false, false)]
    [TestCase("back", 2, false, false)]
    [TestCase("back", 2, true, true)]
    [TestCase("brb, door", 2, false, true)]
    [TestCase("nice pull", 2, false, true)]
    [TestCase("all good here", 2, false, true)]
    public void AGroupLineFitsTheGroupsRealSize(string text, int size, bool away, bool fits) =>
        Assert.That(Engagement.FitsGroup(text, size, away), Is.EqualTo(fits), text);

    [Test]
    public void BackComesOnlyAfterABrb()
    {
        Engagement.NoteGroupLine(4242, "nice pull", 0);
        Assert.That(Engagement.WasAway(4242, 1_000, false), Is.False);
        Engagement.NoteGroupLine(4242, "brb, door", 1_000);
        Assert.That(Engagement.WasAway(4242, 60_000, false), Is.True);
        Engagement.NoteGroupLine(4242, "back", 70_000);
        Assert.That(Engagement.WasAway(4242, 80_000, false), Is.False, "back once");
        Assert.That(Engagement.WasAway(4242, 80_000, true), Is.True, "after a death");
    }

    [Test]
    public void AnInvitedBotThatWalkedOverSaysHereNotBack()
    {
        var random = new Random(1);
        for (int i = 0; i < 20; i++)
            Assert.That(GameBot.GamebotReturnLine("arrive_walk", random, null, null), Is.Not.EqualTo("back"));
    }

    // ---- 3. continuity -------------------------------------------------------------------------------------------------------

    [Test]
    public void AFollowUpComesOnlyAboutWhatTheChannelReallyCarried()
    {
        var store = new Recall.Store();
        // A story planned but never heard leaves no note.
        store.ExpectStory(11, "large skeleton", 0);
        Assert.That(store.Count, Is.Zero);
        store.Heard("story", 5, "riff", 12, "Brenna", null, 1_000);
        Assert.That(store.Count, Is.Zero, "a riff isn't the teller");
        store.Heard("story", 5, "teller", 11, "Aldric", null, 2_000);
        Assert.That(store.Count, Is.EqualTo(1));
        // The expectation is spent: a second teller line of another story makes no second note.
        store.Heard("story", 6, "teller", 11, "Aldric", null, 3_000);
        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(store.Due(5 * 60_000), Is.Empty, "too fresh to bring up");
        Assert.That(store.Due(Recall.MinAgeMs + 2_000), Has.Length.EqualTo(1));
        Assert.That(store.Due(Recall.EveningMs + 10_000), Is.Empty, "forgotten after the evening");
    }

    [Test]
    public void ADebateIsRememberedOnceBothSidesSpoke()
    {
        var store = new Recall.Store();
        store.Heard("debate", 9, "A", 21, "Aldric", "housing", 0);
        Assert.That(store.Count, Is.Zero);
        store.Heard("debate", 9, "C", 23, "Cadoc", "housing", 7_000);
        store.Heard("debate", 9, "B", 22, "Brenna", "housing", 14_000);
        Recall.Note note = store.Notes.Single();
        Assert.That((note.Kind, note.BotName, note.OtherName, note.Subject), Is.EqualTo((Recall.Kind.Debated, "Aldric", "Brenna", "housing")));
        store.Heard("debate", 9, "B", 22, "Brenna", "housing", 21_000);
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public void AFollowUpIsAnsweredFromWhatIsTrueNow()
    {
        var random = new Random(3);
        var asked = new Recall.Note(Recall.Kind.AskedGroup, 1, "Aldric", null, 0);
        for (int i = 0; i < 20; i++)
        {
            var grouped = Recall.FollowUp(asked, new Recall.Now(true, false, "Lyonesse"), 0, random).Value;
            Assert.That(grouped.Ask, Does.Contain("aldric"));
            Assert.That(grouped.Answer, Does.StartWith("ya").Or.StartWith("yep"));
            var solo = Recall.FollowUp(asked, new Recall.Now(false, false, "Lyonesse"), 0, random).Value;
            Assert.That(solo.Answer, Does.StartWith("nah").Or.StartWith("nope").Or.StartWith("not yet"));
            var camp = Recall.FollowUp(asked with { Kind = Recall.Kind.AskedCamp }, new Recall.Now(false, true, "Lyonesse"), 0, random).Value;
            Assert.That(camp.Answer, Does.Contain("Lyonesse"));
        }
        var died = new Recall.Note(Recall.Kind.Died, 1, "Aldric", "large skeleton", 0);
        Assert.That(Recall.FollowUp(died, new Recall.Now(false, false, null), 1, random).Value.Ask, Does.Contain("skeletons"));
        Assert.That(Recall.FollowUp(died, new Recall.Now(false, false, null), 3, random).Value.Ask, Does.Contain("3"));
        var debated = new Recall.Note(Recall.Kind.Debated, 1, "Aldric", "death_penalty", 0, 2, "Brenna");
        Assert.That(Recall.FollowUp(debated, new Recall.Now(false, false, null), 0, random).Value.Ask, Does.Contain("the death penalty"));
        foreach (var lines in Enumerable.Range(0, 40).Select(_ => Recall.FollowUp(died, new Recall.Now(false, false, null), 1, random).Value))
        {
            Assert.That(Ex.IsReaction(lines.Answer), Is.False, "an answer, not a short reaction the channel rations");
            Assert.That(Ex.IsShort(lines.Ask), Is.False);
        }
    }

    [Test]
    public void TheNoteBroughtUpNeedsItsBotsOnline()
    {
        var debated = new Recall.Note(Recall.Kind.Debated, 1, "Aldric", "housing", 0, 2, "Brenna");
        var died = new Recall.Note(Recall.Kind.Died, 3, "Cadoc", "wolf", 10);
        Assert.That(Recall.Choose([debated, died], id => id != 2, new Random(1)), Is.SameAs(died), "Brenna logged off");
        Assert.That(Recall.Choose([debated], id => id != 2, new Random(1)), Is.Null);
    }

    [TestCase("lfg", Recall.Kind.AskedGroup)]
    [TestCase("ask_group", Recall.Kind.AskedGroup)]
    [TestCase("camp", Recall.Kind.AskedCamp)]
    public void WhatAFieldLineAskedFor(string kind, Recall.Kind asked) => Assert.That(Recall.AskedOf(kind), Is.EqualTo(asked));

    [Test]
    public void SalesAndCallsAreNotAskedAfter() => Assert.That(new[] { "wts", "inc", "ding", "death" }.Select(Recall.AskedOf), Is.All.Null);

    // ---- 4. regulars ----------------------------------------------------------------------------------------------------------

    [Test]
    public void AnEveningKeepsOneCast()
    {
        HashSet<long> At(long now) => Enumerable.Range(1, 600).Select(i => (long)i)
            .Where(id => Regulars.IsRegular(id, AutonomousBotPersonality.TypeFor(id), now, 600)).ToHashSet();
        HashSet<long> eight = At(10 * 60_000), eleven = At(3 * 60 * 60_000 + 10 * 60_000);
        Assert.That(eight.SetEquals(eleven), Is.True, "the same cast three hours later");
        Assert.That(eight.Count, Is.InRange(3, 14), "about seven at 600 bots");
        Assert.That(At(Regulars.RotationMs + 60_000).SetEquals(eight), Is.False, "another cast another evening");
    }

    [Test]
    public void ARegularHasAPartAndARunningBitOfItsOwn()
    {
        var parts = new HashSet<Regulars.Archetype>();
        for (long id = 1; id < 400; id++)
        {
            Regulars.Archetype part = Regulars.ArchetypeOf(id, AutonomousBotPersonality.TypeFor(id), true);
            parts.Add(part);
            string first = Regulars.BitOf(id, part, "Order of the Chalice", 0), second = Regulars.BitOf(id, part, "Order of the Chalice", 1);
            Assert.That(first, Is.Not.Null.And.Not.Empty);
            Assert.That(first, Is.Not.EqualTo(second), "two bits to come back to");
            Assert.That(Regulars.BitOf(id, part, "Order of the Chalice", 2), Is.EqualTo(first), "the same two, in turn");
            Assert.That(Ex.IsShort(first), Is.False);
            Assert.That(Regulars.BitRestMs(id), Is.InRange(35 * 60_000, 65 * 60_000));
        }
        Assert.That(parts, Is.EquivalentTo(Enum.GetValues<Regulars.Archetype>()));
        Assert.That(Regulars.BitOf(5, Regulars.Archetype.Recruiter, null, 0), Is.Null, "a recruiter names its real guild or says nothing");
    }

    [Test]
    public void TheQuietManyTellFewerOfTheirDeaths()
    {
        var random = new Random(9);
        Assert.That(Enumerable.Range(0, 100).All(_ => Regulars.TellsOwnStory(true, random)), Is.True);
        int told = Enumerable.Range(0, 2_000).Count(_ => Regulars.TellsOwnStory(false, random));
        Assert.That(told / 2_000.0, Is.InRange(Regulars.OthersTellStory - 0.05, Regulars.OthersTellStory + 0.05));
    }

    // ---- 5. the simulated evening ---------------------------------------------------------------------------------------------

    private static readonly Field.WorldView Frontier = new(["Caer Benowyc", "Caer Berkstead"], [], ["Caer Renaris"], ["Midgard"], 0, 1, "Forest Sauvage");

    private static readonly string[] Albion =
        ["Armsman", "Paladin", "Mercenary", "Reaver", "Cleric", "Friar", "Minstrel", "Scout", "Infiltrator", "Sorcerer", "Wizard", "Theurgist", "Cabalist", "Necromancer"];

    private static readonly string[] Zones = ["Camelot Hills", "Salisbury Plains", "Lyonesse", "Dartmoor", "Avalon Marsh", "Cornwall", "Forest Sauvage"];

    private static readonly string[] Monsters = ["large skeleton", "bwgan horde", "cornish hen", "moor boogey", "dartmoor wolf", "brigand"];

    private sealed class SimBot
    {
        public long Id;
        public string Name, Class, Zone, Monster;
        public int Level;
        public BotPersonalityType Type;
        public long RestUntil;
        public bool Grouped, AtCamp;

        public Field.Facts Facts => new(Id, eRealm.Albion, Class, Level, Zone, AutonomousBotVoices.DungeonFor(eRealm.Albion, Level, new Random((int)Id)),
            false, false, false, Grouped, Grouped ? 4 : 1, false, false, false, AtCamp, Monster, null, 0, false, null, 3, 20, Field.Fills(Class, "healer"), Frontier);
    }

    /// <summary>What a simulated evening said, and the measurements.</summary>
    public sealed class Evening
    {
        public readonly List<string> Log = [];
        public readonly List<(long At, long Ex, int Index, string Role, string Speaker, string Feature, string Channel, string Text)> Said = [];
        public double Ceiling;

        public IEnumerable<(long At, long Ex, int Index, string Role, string Speaker, string Feature, string Channel, string Text)> Faction =>
            Said.Where(s => s.Channel == "faction");

        public double TopTenShare
        {
            get
            {
                var counts = Faction.GroupBy(s => s.Speaker).Select(g => g.Count()).OrderByDescending(n => n).ToArray();
                return counts.Length == 0 ? 0 : counts.Take(10).Sum() / (double)counts.Sum();
            }
        }

        public int Speakers => Faction.Select(s => s.Speaker).Distinct().Count();
        public int Busiest => Faction.GroupBy(s => s.Speaker).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        public int PlayerLines => Said.Count(s => s.Feature == "player");
        public int PlayerLinesSolo, PlayerLinesGrouped;
        public int FollowUps => Faction.Where(s => s.Feature == "followup").Select(s => s.Ex).Distinct().Count();

        /// <summary>The bot each follow-up asked after (its exchange id to its name).</summary>
        public readonly Dictionary<long, string> Subjects = [];

        /// <summary>Follow-ups said whose subject spoke in /faction at least ten minutes before the question, which names it.</summary>
        public int FollowUpsAnchored => Faction.Where(s => s.Feature == "followup" && s.Role == "asker").Count(ask =>
            Subjects.TryGetValue(ask.Ex, out string subject) && ask.Text.Contains(subject.ToLowerInvariant()) &&
            Faction.Any(s => s.Speaker == subject && s.Feature != "followup" && ask.At - s.At >= 10 * 60_000));
        public int Bits => Faction.Count(s => s.Feature == "bit");

        /// <summary>Bot questions answered by someone else within 60 s (as chat_audit.py check_unanswered: rhetorical ones aside).</summary>
        public (int Asked, int Answered) Questions
        {
            get
            {
                var asked = Faction.Where(s => s.Feature == "field" && s.Index == 0 && s.Text.TrimEnd().EndsWith('?') && !Rhetorical(s.Text) &&
                                               s.At <= 3 * 3_600_000L - 60_000).ToList();
                return (asked.Count, asked.Count(q => Faction.Any(r => r.Ex == q.Ex && r.Speaker != q.Speaker && r.At >= q.At && r.At - q.At <= 60_000)));
            }
        }

        private static bool Rhetorical(string text)
        {
            string t = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " "), @"\s+", " ").Trim();
            return System.Text.RegularExpressions.Regex.IsMatch(t, @"^(?:\S+|(?:lol|heh|haha|ugh|bleh|argh|ok|so|well)\b.*|only \d+ \w+|.*\bnot bad|.*\bkeeps not bad)$");
        }
    }

    /// <summary>The 1.1.2 rule for who is a regular: a three-hour rotation staggered by bot, 1.2% of the realm.</summary>
    private static bool OldRegular(long botId, BotPersonalityType type, long now, int realmBots)
    {
        int target = Math.Clamp((int)Math.Round(realmBots * 0.012), 5, 15);
        long slot = (Math.Max(0, now) + Math.Abs(botId % 180) * 60_000) / (3 * 60 * 60_000L);
        double p = Math.Min(1.0, target / (double)realmBots * Regulars.Weight(type) / ((3.0 + 1.5 + 4 * 1.0) / 6));
        return AutonomousBotPersonality.Mix(botId ^ (slot * 7_919), 71) % 1_000_000 / 1_000_000.0 < p;
    }

    public const int SimRealmBots = 594;
    public const string Player = "Questor";

    /// <summary>
    /// Three hours of a realm's /faction (as UT_FactionCoherence plays it, through the 1.1.3 exchange and ceiling rules) and of
    /// what the player hears. The player logs in, levels every 25 minutes, dies three times, moves zone every half hour, puts
    /// on two good items, and is grouped from 60 to 120 minutes with two friends online. <paramref name="newCode"/> false: the
    /// 1.1.2 regulars (staggered, 1.2%), every bot telling its deaths, no follow-ups or bits, and of the player-aimed lines only
    /// the old ones (a tell every 25-45 minutes, gossip every 30-60, a nearby grats in /say). True: this branch.
    /// </summary>
    public static Evening Simulate(bool newCode, int seed, long lengthMs)
    {
        var random = new Random(seed);
        var evening = new Evening();
        string lane = "engage:" + (newCode ? "after" : "before") + ":" + seed;
        Ex.Lane laneState = Ex.LaneOf(lane);
        var shorts = new Ex.ShortWindow();
        var memory = new Field.Memory();
        // Each bot keeps its story, as AutonomousChatStance.Shared does in the live code (1.1.3, after RC5); the old way had none.
        var stance = newCode ? new AutonomousChatStance() : null;
        var meter = new AutonomousChatBudget.ChannelMeter();
        var band = AutonomousChatBudget.Band(AutonomousChatBudget.Current, AutonomousChatBudget.Channel.Faction, SimRealmBots, 20, false, 1.0);
        evening.Ceiling = band.Ceiling;
        var recall = new Recall.Store();
        var pacer = new Engagement.Pacer();
        const long key = 1;
        long baseTime = 20 * 3_600_000L;
        var bots = Enumerable.Range(0, SimRealmBots).Select(i => new SimBot
        {
            Id = 80_000 + i, Name = "Sim" + (char)('a' + i % 26) + (char)('a' + i / 26 % 26) + (char)('a' + i / 676 % 26),
            Class = Albion[(i * 7 + seed) % Albion.Length], Level = 5 + (i * 37 + seed) % 46, Zone = Zones[i % Zones.Length],
            Monster = Monsters[i % Monsters.Length], Type = AutonomousBotPersonality.TypeFor(80_000 + i),
        }).ToList();
        bool Regular(SimBot bot, long now) => newCode ? Regulars.IsRegular(bot.Id, bot.Type, now, SimRealmBots) : OldRegular(bot.Id, bot.Type, now, SimRealmBots);
        bool Busy(SimBot bot, long now) => (now / 1_000 + bot.Id * 7) % 60 < 15;
        double PerMinute(long now) => meter.PerMinute(AutonomousChatBudget.Channel.Faction, key, now);
        bool Room(long now) => Ex.Fits(PerMinute(now), band.Ceiling);
        long debateUntil = -1, sceneEndedAt = long.MinValue / 2;
        string Stamp(long now)
        {
            long t = baseTime + now;
            return $"11/08 {t / 3_600_000 % 24:00}:{t / 60_000 % 60:00}:{t / 1_000 % 60:00}";
        }
        void Write(long now, SimBot bot, string channel, string feature, long ex, int index, string role, string text, string to = null)
        {
            evening.Said.Add((now, ex, index, role, bot.Name, feature, channel, text));
            string cap = channel == "faction" ? $" cap={band.Ceiling.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}" : "";
            evening.Log.Add($"{Stamp(now)} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=bot ch={channel} realm=Albion from=\"{bot.Name}\"" +
                            (to != null ? $" to=\"{to}\"" : "") + $" via=Sim src={feature} ex={ex} i={index} role={role} cls=\"{bot.Class}\" lvl={bot.Level}{cap} " +
                            $"text=\"{text.Replace('"', '\'')}\"");
        }
        // AutonomousBotChatCoordinator.BroadcastFactionAsWritten, with the recall hook of this branch.
        bool Say(long now, SimBot bot, string feature, long ex, int index, string role, string text)
        {
            if (bot == null || Ex.SinkHold(new Ex.Source(feature, ex, index, role), now < debateUntil, Room(now), now, laneState.LastAt, sceneEndedAt) != null)
                return false;
            if (Ex.IsReaction(text) && !shorts.SayOrHold(lane, text, now))
                return false;
            Ex.NoteLine(lane, bot.Name, now);
            meter.Count(AutonomousChatBudget.Channel.Faction, key, now);
            Write(now, bot, "faction", feature, ex, index, role, text);
            if (newCode)
                recall.Heard(feature, ex, role, bot.Id, bot.Name, feature == "debate" ? "housing" : null, now);
            return true;
        }
        var realmBots = bots.ToArray();
        // AutonomousBotChatCoordinator.Repliers: a regular first (a question: two times in five), then a bot near the speaker's
        // level, then anyone free (a slice); fitting bots past those go to the stand-ins, up to four.
        (SimBot, SimBot) Repliers(SimBot starter, long now, Func<SimBot, bool> fits = null, List<SimBot> spare = null, double regularShare = 1.0)
        {
            bool regularFirst = regularShare >= 1 || random.NextDouble() < regularShare;
            SimBot regular = null, near = null, any = null, second = null;
            // The slice: 200 of the realm's bots now; before, 200 of all three realms' (about 66 of this one).
            int slice = newCode ? AutonomousBotChatCoordinator.RepliersSlice : 66;
            for (int i = 0, at = random.Next(realmBots.Length); i < slice; i++, at = (at + 1) % realmBots.Length)
            {
                SimBot bot = realmBots[at];
                if (bot == starter || fits != null && !fits(bot))
                    continue;
                if (regularFirst && regular == null && Regular(bot, now))
                    regular = bot;
                else if (near == null && Math.Abs(bot.Level - starter.Level) <= 5)
                    near = bot;
                else if (any == null)
                    any = bot;
                else if (second == null)
                    second = bot;
                else if (spare != null && spare.Count < 4)
                    spare.Add(bot);
            }
            SimBot answerer = regular ?? near ?? any;
            SimBot chime = new[] { near, any, second }.FirstOrDefault(bot => bot != null && bot != answerer);
            spare?.InsertRange(0, new[] { near, any, second }.Where(bot => bot != null && bot != answerer && bot != chime));
            return (answerer, chime);
        }
        var runs = new List<(Ex.Run Run, SimBot[] Speakers, string Feature, SimBot[] StandIns)>();
        var spoke = new HashSet<(long, int)>();
        void Start(long now, long id, string feature, List<Ex.Line> lines, SimBot[] speakers, int firstIndex = 1, SimBot[] standIns = null)
        {
            runs.Add((new Ex.Run(id, feature, lane, laneState, lines, now, Ex.Rules.Faction, firstIndex), speakers, feature, standIns));
            Ex.Touch(laneState, id, now);
        }
        string[] beats = ["is a house worth the coin?", "a house is a home, plus storage", "its a weekly rent bill for a room u visit twice",
            "decorate it right and ur friends will visit", "my friends are in the frontier lol", "maybe a small cottage to start"];
        var beatsDue = new List<(long Due, SimBot[] Cast, long Ex, int Index, int Waits)>();
        long nextRecallScan = 0, nextBitInRealm = 0;
        var nextBit = new Dictionary<long, (long At, int Turn)>();

        // The player's evening.
        bool PlayerGrouped(long now) => now >= 60 * 60_000 && now < 120 * 60_000;
        int FriendsOnline(long now) => PlayerGrouped(now) ? 2 : 1;
        SimBot friend = bots[3], friend2 = bots[11];
        long nextOldTell = 4 * 60_000 + random.Next(6 * 60_000), nextGossip = 15 * 60_000 + random.Next(15 * 60_000);
        void ToPlayer(long now, SimBot bot, string channel, string role, string text)
        {
            Write(now, bot, channel, "player", Ex.NewId(), 0, role, text, channel == "tell" ? Player : null);
            if (PlayerGrouped(now))
                evening.PlayerLinesGrouped++;
            else
                evening.PlayerLinesSolo++;
        }
        bool Turn(long now, bool witnessed = false) => !newCode || pacer.TryTake(Player, now, PlayerGrouped(now), FriendsOnline(now), witnessed);
        evening.Log.Add($"{Stamp(0)} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=player ch=faction realm=Albion from=\"{Player}\" intent=Greeting zone=\"Lyonesse\" text=\"evening all\"");

        for (long now = 0; now < lengthMs; now += 1_000)
        {
            bool debate = now < debateUntil;
            // ---- the player's moments ----
            if (newCode && now == 45_000 && Turn(now))
                ToPlayer(now, friend, "tell", "login", Engagement.LoginTell(Player, true, random));
            if (now > 0 && now % (25 * 60_000) == 0)
            {
                // A ding: a bot or two nearby say grats in /say half the time (both ways); a friend away tells grats (new).
                if (random.NextDouble() < 0.5)
                    ToPlayer(now + 2_000, bots[random.Next(bots.Count)], "say", "grats", "gz " + Player);
                if (newCode && random.NextDouble() < 0.6 && Turn(now))
                    ToPlayer(now + 9_000, friend2, "tell", "grats", Engagement.LevelUpTell(Player, 30 + (int)(now / (25 * 60_000)), random));
            }
            if (now is 40 * 60_000 or 130 * 60_000 or 160 * 60_000 && newCode)
            {
                // A death: a bot standing by (60%), and a healer that sets out to rez (half the time, not paced).
                if (random.NextDouble() < 0.6 && Turn(now, witnessed: true))
                    ToPlayer(now + 3_000, bots[random.Next(bots.Count)], "say", "death", Engagement.DeathSympathy(Player, false, random));
                if (random.NextDouble() < 0.5)
                    ToPlayer(now + 5_000, bots[4], "say", "rez_offer", Engagement.RezOffer(Player, false, random));
            }
            if (newCode && now > 0 && now % (30 * 60_000) == 15 * 60_000 && Turn(now))
                ToPlayer(now + 45_000, friend, "tell", "zone", Engagement.ZoneTell(Player, Zones[(int)(now / (30 * 60_000)) % Zones.Length], random));
            if (newCode && now is 50 * 60_000 or 140 * 60_000 && random.NextDouble() < 0.5 && Turn(now))
                ToPlayer(now + 4_000, bots[random.Next(bots.Count)], PlayerGrouped(now) ? "group" : "say", "gear", Engagement.GearLine(Player, "Ghostly Cloak", false, random));
            if (now >= nextOldTell)
            {
                // AutonomousLivingServer.TryStartTell: now through the player's turns too.
                nextOldTell = now + AutonomousLivingServer.NextTellGap(false, random);
                if (Turn(now))
                    ToPlayer(now, friend, "tell", "tell", "hey " + Player + ", benowyc is under attack if u want a fight");
            }
            if (now >= nextGossip)
            {
                // AutonomousLivingServer.TryGossip: now through the player's turns too.
                nextGossip = now + AutonomousLivingServer.GossipRealmCooldownMinMs + random.Next(30 * 60_000);
                if (Turn(now) && Say(now, friend, "player", Ex.NewId(), 0, "gossip", "grouped with " + Player + " the other day, solid player"))
                {
                    if (PlayerGrouped(now)) evening.PlayerLinesGrouped++; else evening.PlayerLinesSolo++;
                }
            }

            // ---- unprompted openers through the realm's budget (TryStartBudgeted), the starter by the regulars' rules ----
            if (!debate)
            {
                SimBot starter = null;
                for (int tries = 0; tries < 6 && starter == null; tries++)
                {
                    SimBot bot = bots[random.Next(bots.Count)];
                    if (now < bot.RestUntil)
                        continue;
                    bool regular = Regular(bot, now);
                    // AutonomousBotChat.ShouldSpeak: a regular 70% once rested, anyone else about 1%; /faction for others 8% or when thin.
                    double wants = regular ? Regulars.RegularChance : 0.012 * 4;
                    if (random.NextDouble() >= wants)
                        continue;
                    if (regular || random.NextDouble() < Regulars.OthersShare || PerMinute(now) < Regulars.OthersBelow * band.Target)
                        starter = bot;
                }
                if (starter != null && meter.TryStart(AutonomousChatBudget.Channel.Faction, key, now, band, random) is AutonomousChatBudget.Verdict.Speak or AutonomousChatBudget.Verdict.SpeakUnderFloor)
                {
                    starter.RestUntil = now + (Regular(starter, now) ? Regulars.RegularRestMs : 2 * 60_000 + random.Next(2 * 60_000));
                    if (Field.Line("faction", starter.Facts, memory, random, now, stance) is { } said)
                    {
                        long ex = Ex.NewId();
                        // AutonomousBotChatCoordinator.TryField (1.1.3, after RC5): a question only when there is room for its answer.
                        bool held = Field.WantsAnswer(said) && (Ex.RunningCount(lane, now) >= Ex.MaxRunning || !Ex.Fits(PerMinute(now) + 0.5, band.Ceiling));
                        if (!held && Say(now, starter, "field", ex, 0, "asker", said.Text))
                        {
                            if (newCode && Recall.AskedOf(said.Kind) is { } asked)
                                recall.Add(new Recall.Note(asked, starter.Id, starter.Name, said.Idea, now));
                            if (said.Answers != null && Ex.RunningCount(lane, now) < Ex.MaxRunning)
                            {
                                // ScheduleFieldThread: a bot that has an answer, a regular first two times in five for a question,
                                // and stand-ins for a busy answerer (unless the answer is about the answerer itself).
                                Func<SimBot, bool> fits = bot => Field.CanGiveAnswer(said, bot.Class, bot.Level, Field.Fills(bot.Class, "healer"));
                                var spare = new List<SimBot>(4);
                                (SimBot answerer, SimBot third) = Repliers(starter, now, fits, spare,
                                    Field.WantsAnswer(said) ? AutonomousBotChatCoordinator.QuestionRegularShare : 1.0);
                                List<Field.Step> steps = Field.Thread(said, starter.Facts, answerer?.Facts, third?.Facts, memory, random, now);
                                SimBot[] standIns = answerer == null || steps.Any(st => st.Who == 1 && Field.MentionsSelf(st.Text, answerer.Facts)) ? null
                                    : spare.Where(bot => bot != third && Field.Fills(bot.Class, "healer") == Field.Fills(answerer.Class, "healer") &&
                                                         (said.SameClass == null || bot.Class == answerer.Class)).ToArray();
                                if (steps.All(st => st.Who == 0 || !Ex.IsReaction(st.Text) || shorts.TryUse(lane, st.Text, now)) && steps.Count > 0)
                                    Start(now, ex, "thread", Ex.FromSteps(steps, ["asker", "answerer", "third"], step => step.Who == 2), [starter, answerer, third],
                                        standIns: standIns);
                            }
                        }
                    }
                }
            }
            // ---- a death or wipe story about every two minutes (TellStory): the teller is whoever died ----
            if (random.NextDouble() < 1 / 120.0 && !debate && Room(now) && Ex.RunningCount(lane, now) < Ex.MaxRunning)
            {
                SimBot teller = bots[random.Next(bots.Count)];
                if (!newCode || Regulars.TellsOwnStory(Regular(teller, now), random))
                {
                    (SimBot mate, SimBot third) = Repliers(teller, now);
                    long at = now;
                    List<Field.Step> story = Field.Story("death", teller.Facts, mate.Facts, third?.Facts, random, memory, text => shorts.Fresh(lane, text, at));
                    story = story.Where(step => step.Who == 0 || !Ex.IsShort(step.Text) || shorts.TryUse(lane, step.Text, at)).ToList();
                    if (story.Count > 0)
                    {
                        if (newCode)
                            recall.ExpectStory(teller.Id, teller.Monster, now);
                        Start(now, Ex.NewId(), "story", Ex.FromSteps(story.Select(st => st with { AtMs = st.AtMs + 2_000 }), ["teller", "riff", "riff"],
                            step => step.Who == 2), [teller, mate, third], firstIndex: 0);
                    }
                }
            }
            // ---- a debate about every twenty minutes ----
            if (!debate && random.NextDouble() < 1 / 1_200.0)
            {
                SimBot a = bots[random.Next(bots.Count)], b = bots[random.Next(bots.Count)], c = bots[random.Next(bots.Count)];
                if (a != b && b != c && a != c)
                {
                    beatsDue.Add((now + 7_000, [a, b, c], Ex.NewId(), 0, 0));
                    debateUntil = long.MaxValue;
                }
            }
            for (int k = 0; k < beatsDue.Count; k++)
            {
                var (due, cast, ex, index, waits) = beatsDue[k];
                if (due > now)
                    continue;
                beatsDue.RemoveAt(k--);
                if (!Room(now))
                {
                    if (waits < AutonomousBotDebates.MaxRoomWaits)
                        beatsDue.Add((now + AutonomousBotDebates.RoomRetryMs, cast, ex, index, waits + 1));
                    else
                        debateUntil = sceneEndedAt = now;
                    continue;
                }
                Say(now, cast[index % 3], "debate", ex, index, index % 3 == 0 ? "A" : index % 3 == 1 ? "B" : "C", beats[index]);
                if (index + 1 < beats.Length)
                    beatsDue.Add((now + 7_000, cast, ex, index + 1, 0));
                else
                    debateUntil = sceneEndedAt = now + 2_000;
            }
            // ---- this branch: a follow-up or a regular's running bit (AutonomousLivingServer.EngagePulse, every 15 s) ----
            if (newCode && now >= nextRecallScan && !debate)
            {
                nextRecallScan = now + 15_000;
                bool under = PerMinute(now) < band.Target && Room(now) && Ex.RunningCount(lane, now) < Ex.MaxRunning;
                bool followed = false;
                Recall.Note[] due = under ? recall.Due(now) : [];
                if (due.Length > 0 && recall.TryTurn(now, random) &&
                    Recall.Choose(due, id => !Busy(bots.First(b => b.Id == id), now), random) is { } note)
                {
                    SimBot subject = bots.First(b => b.Id == note.BotId);
                    SimBot asker = note.Kind == Recall.Kind.Debated ? bots.First(b => b.Id == note.OtherId)
                        : bots.Where(b => b != subject && Regular(b, now) && !Busy(b, now)).OrderBy(_ => random.Next()).FirstOrDefault();
                    subject.Grouped = random.NextDouble() < 0.5;
                    subject.AtCamp = random.NextDouble() < 0.5;
                    if (asker != null && Recall.FollowUp(note, new Recall.Now(subject.Grouped, subject.AtCamp, subject.Zone), recall.Deaths(note.BotId, now), random) is { } lines)
                    {
                        recall.MarkUsed(note);
                        followed = true;
                        long id = Ex.NewId();
                        evening.Subjects[id] = subject.Name;
                        Start(now, id, "followup", [new Ex.Line(0, lines.Ask, 0, "asker"), new Ex.Line(1, lines.Answer, 5_000 + random.Next(6_000), "answerer")],
                            [asker, subject], firstIndex: 0);
                    }
                }
                if (!followed && under && now >= nextBitInRealm)
                {
                    foreach (SimBot bot in bots.Where(b => Regular(b, now) && !Busy(b, now)).OrderBy(_ => random.Next()))
                    {
                        nextBit.TryGetValue(bot.Id, out var state);
                        if (now < state.At)
                            continue;
                        string bit = Regulars.BitOf(bot.Id, Regulars.ArchetypeOf(bot.Id, bot.Type, false), null, state.Turn);
                        if (bit == null)
                            continue;
                        if (Say(now, bot, "bit", Ex.NewId(), 0, "opener", bit))
                        {
                            nextBit[bot.Id] = (now + Regulars.BitRestMs(bot.Id), state.Turn + 1);
                            nextBitInRealm = now + AutonomousLivingServer.BitRealmRestMinMs + random.Next(6 * 60_000);
                        }
                        break;
                    }
                }
            }
            // ---- due lines (Ex.Run, as AutonomousChatExchange.Play) ----
            for (int k = 0; k < runs.Count; k++)
            {
                var (run, speakers, feature, standIns) = runs[k];
                if (run.DueAt > now)
                    continue;
                long? next = run.Step(now, line =>
                    {
                        // A stand-in for a busy answerer that hasn't spoken yet (AutonomousChatExchange.Play's standIns).
                        if (line.Who == 1 && standIns is { Length: > 0 } && !spoke.Contains((run.Id, 1)) && speakers[1] is { } first && Busy(first, now) &&
                            standIns.FirstOrDefault(bot => !Busy(bot, now)) is { } free)
                            speakers[1] = free;
                        return line.Who < speakers.Length && speakers[line.Who] is { } bot && now >= debateUntil && Room(now) &&
                               (feature == "story" && line.Who == 0 || !Busy(bot, now));
                    },
                    line => line.Who < speakers.Length ? speakers[line.Who]?.Name : null,
                    (line, index) =>
                    {
                        if (!Say(now, speakers[line.Who], feature, run.Id, index, line.Role, line.Text))
                            return false;
                        spoke.Add((run.Id, line.Who));
                        return true;
                    }, random);
                if (next == null)
                {
                    Ex.Finish(laneState, run.Id);
                    runs.RemoveAt(k--);
                }
                else
                    Ex.Touch(laneState, run.Id, now);
            }
        }
        evening.Said.Sort((a, b) => a.At.CompareTo(b.At));
        evening.Log.Sort(StringComparer.Ordinal);
        return evening;
    }

    [Test]
    public void AnEveningWithRegularsFollowUpsAndAPlayerTheBotsNotice()
    {
        const long hours3 = 3 * 3_600_000L;
        Evening before = Simulate(false, 1, hours3), after = Simulate(true, 1, hours3);
        string folder = Environment.GetEnvironmentVariable("CHAT_SIM_DIR") is { Length: > 0 } dir ? dir : Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-sim");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "faction-sim-engage-before.chat.log"), before.Log);
        File.WriteAllLines(Path.Combine(folder, "faction-sim-engage-after.chat.log"), after.Log);
        string Line(string name, Evening e) =>
            $"{name}: /faction {e.Faction.Count()} lines ({e.Faction.Count() / 180.0:0.00}/min, ceiling {e.Ceiling:0.0}) from {e.Speakers} speakers, " +
            $"top 10 carry {e.TopTenShare * 100:0}%, busiest {e.Busiest}; lines aimed at the player {e.PlayerLines} ({e.PlayerLines / 3.0:0.0}/h: " +
            $"solo {e.PlayerLinesSolo / 2.0:0.0}/h, grouped {e.PlayerLinesGrouped:0.0}/h); follow-ups {e.FollowUps} ({e.FollowUpsAnchored} about a real earlier line), " +
            $"running bits {e.Bits}; questions answered in 60 s {e.Questions.Answered}/{e.Questions.Asked}";
        TestContext.Progress.WriteLine("ENGAGE_SIM " + Line("before", before));
        TestContext.Progress.WriteLine("ENGAGE_SIM " + Line("after", after));

        // The cast: the top ten carry 40-50% of /faction (it was about a quarter), from far fewer speakers.
        Assert.That(after.TopTenShare, Is.InRange(0.38, 0.60), Line("after", after));
        Assert.That(after.TopTenShare, Is.GreaterThan(before.TopTenShare + 0.07), Line("before", before));
        Assert.That(after.Speakers, Is.LessThan(before.Speakers));
        // Continuity: follow-ups, each about something the channel carried at least ten minutes before.
        Assert.That(after.FollowUps, Is.GreaterThanOrEqualTo(6), Line("after", after));
        Assert.That(after.FollowUpsAnchored, Is.EqualTo(after.FollowUps), "every follow-up refers to a real earlier line");
        Assert.That(before.FollowUps, Is.Zero);
        Assert.That(after.Bits, Is.GreaterThan(0));
        // The answers (#155) hold with the cast: at least four in five bot questions get an answer.
        Assert.That(after.Questions.Answered, Is.GreaterThanOrEqualTo(after.Questions.Asked * 0.8), Line("after", after));
        // Engagement: a few lines an hour (the paced ones never past the allowance; a ding's /say grats and a healer's rez on top).
        Assert.That(after.PlayerLinesSolo / 2.0, Is.InRange(2.5, Engagement.SoloPerHour + 3.0), Line("after", after));
        Assert.That(after.PlayerLinesGrouped, Is.InRange(2, Engagement.WarmPerHour + 3), Line("after", after));
        Assert.That(after.PlayerLines, Is.GreaterThan(before.PlayerLines));
        Assert.That(after.Said.Any(s => s.Feature == "player" && s.Role == "death"), Is.True, "a death is seen");
        // The channel still holds: never above its ceiling (a line to the player aside), and busy enough.
        long[] times = after.Faction.Where(s => s.Feature != "player").Select(s => s.At).OrderBy(t => t).ToArray();
        for (long t = 120_000; t < hours3; t += 5_000)
            Assert.That(times.Count(at => at > t - 120_000 && at <= t) / 2.0, Is.LessThanOrEqualTo(after.Ceiling + 1e-9), $"at {t / 1_000} s");
        Assert.That(after.Faction.Count() / 180.0, Is.GreaterThan(after.Ceiling / 4));
    }
}
