using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ex = DOL.GS.AutonomousChatExchange;
using Field = DOL.GS.AutonomousChatFieldTalk;

namespace DOL.GS.Tests;

/// <summary>
/// 1.1.3, /faction that holds together (owner 2026-11-08: "The chat doesn't seem like it's working right, faction feels
/// disconnected"). Threads in order with the right speaker per role, answers only from bots the answer is true for, reactions
/// capped and timely, one short reaction per channel per ten minutes, and every line's source in the transcript. The last
/// test plays an evening on a simulated clock with the old scheduling and the new, and writes both transcripts for
/// source/tools/chat_audit.py.
/// </summary>
[TestFixture]
public class UT_FactionCoherence
{
    private static readonly Field.WorldView Frontier = new(["Caer Benowyc", "Caer Berkstead"], [], ["Caer Renaris"], ["Midgard"], 0, 1, "Forest Sauvage");

    private static readonly string[] Albion =
        ["Armsman", "Paladin", "Mercenary", "Reaver", "Cleric", "Friar", "Minstrel", "Scout", "Infiltrator", "Sorcerer", "Wizard", "Theurgist", "Cabalist", "Necromancer"];

    private static readonly string[] Zones = ["Camelot Hills", "Salisbury Plains", "Lyonesse", "Dartmoor", "Avalon Marsh", "Cornwall", "Forest Sauvage"];

    private static Field.Facts Facts(long id, string cls, int level, string zone = "Lyonesse", eRealm realm = eRealm.Albion) =>
        new(id, realm, cls, level, zone, AutonomousBotVoices.DungeonFor(realm, level, new Random((int)id)), false, false, false, false, 1, false, false,
            false, true, "large skeleton", null, 0, false, null, 3, 20, Field.Fills(cls, "healer"), Frontier);

    private static int _lane;

    private static string NewLane() => "test:" + System.Threading.Interlocked.Increment(ref _lane);

    // ---- 1. ordering ------------------------------------------------------------------------------------------------------

    /// <summary>Plays a run on a simulated clock: foreign lines land at the given times; returns the said lines with their times.</summary>
    private static List<(long At, int Index, Ex.Line Line)> Drive(Ex.Run run, string lane, Func<Ex.Line, long, bool> free, IEnumerable<long> foreignAt = null,
        long until = 600_000)
    {
        var said = new List<(long, int, Ex.Line)>();
        var foreign = new Queue<long>((foreignAt ?? []).OrderBy(t => t));
        for (long now = 0; now <= until && !run.Done; now += 100)
        {
            while (foreign.Count > 0 && foreign.Peek() <= now)
                Ex.NoteLine(lane, "Other" + foreign.Dequeue(), now);
            if (run.DueAt > now)
                continue;
            run.Step(now, line => free(line, now), line => "Bot" + line.Who, (line, index) =>
            {
                said.Add((now, index, line));
                Ex.NoteLine(lane, "Bot" + line.Who, now);
                return true;
            }, new Random(1));
        }
        return said;
    }

    [Test]
    public void AThreadsLinesComeInOrderEvenWhenOneIsHeldForSpacing()
    {
        // Cabowyn's thread, as planned: "sup" (answerer), "solo or grp?" (asker), "main line first" (answerer), "makes sense ty".
        string lane = NewLane();
        var lines = new List<Ex.Line>
        {
            new(1, "me, sup", 3_000, "answerer"), new(0, "what do u put points in at 50?", 3_000, "asker"), new(1, "main line first", 3_000, "answerer"),
            new(0, "makes sense ty", 2_500, "asker"),
        };
        // A burst of other lines right when "sup" is due used to put it back 2-4 s three times while the rest fired on time.
        var run = new Ex.Run(1, "thread", lane, Ex.LaneOf(lane), lines, 0);
        var said = Drive(run, lane, (_, _) => true, [2_900, 3_500, 9_000]);
        Assert.That(said.Select(s => s.Line.Text), Is.EqualTo(lines.Select(l => l.Text)), "every line, in the planned order");
        Assert.That(said.Select(s => s.At), Is.Ordered.Ascending);
        Assert.That(said.Select(s => s.Index), Is.EqualTo(new[] { 1, 2, 3, 4 }), "indexes after the opener (0)");
        for (int i = 1; i < said.Count; i++)
            Assert.That(said[i].At - said[i - 1].At, Is.GreaterThanOrEqualTo(lines[i].GapMs), "each line timed from the one before it");
    }

    [Test]
    public void ALineWhoseSpeakerStaysBusyEndsTheThreadSoNoThanksFollowsNothing()
    {
        string lane = NewLane();
        var lines = new List<Ex.Line> { new(1, "crafters, pst one", 3_000, "answerer"), new(0, "ty", 2_500, "asker") };
        var run = new Ex.Run(2, "thread", lane, Ex.LaneOf(lane), lines, 0);
        var said = Drive(run, lane, (line, _) => line.Who != 1);
        Assert.That(said, Is.Empty, "the answer never came, so neither does the thanks");
        Assert.That(run.StopReason, Is.EqualTo("speaker busy"));
    }

    [Test]
    public void ABusyThirdBotsChimeIsSkippedAndTheThreadGoesOn()
    {
        string lane = NewLane();
        var lines = new List<Ex.Line>
        {
            new(1, "me", 3_000, "answerer"), new(2, "watch the adds there", 3_000, "third", Optional: true), new(0, "kk inv", 2_500, "asker"),
        };
        var run = new Ex.Run(3, "thread", lane, Ex.LaneOf(lane), lines, 0);
        var said = Drive(run, lane, (line, _) => line.Who != 2);
        Assert.That(said.Select(s => s.Line.Text), Is.EqualTo(new[] { "me", "kk inv" }));
    }

    [Test]
    public void AShortReplyIsNotSaidOnceOtherTalkHasScrolledItsReferentAway()
    {
        string lane = NewLane();
        var lines = new List<Ex.Line> { new(1, "which part?", 20_000, "asker") };
        var run = new Ex.Run(4, "thread", lane, Ex.LaneOf(lane), lines, 0);
        var said = Drive(run, lane, (_, _) => true, [2_000, 6_000, 10_000, 14_000]);
        Assert.That(said, Is.Empty, "four other lines since: \"which part?\" would answer nothing on screen");
        Assert.That(run.StopReason, Is.EqualTo("lost its referent"));

        string calm = NewLane();
        var again = new Ex.Run(5, "thread", calm, Ex.LaneOf(calm), lines, 0);
        Assert.That(Drive(again, calm, (_, _) => true, [2_000, 6_000]).Count, Is.EqualTo(1), "two lines between is still readable");
    }

    [Test]
    public void ALoneFollowUpComesOnlyIfItsSpeakerSaidNothingElseMeanwhile()
    {
        string lane = NewLane();
        var lines = new List<Ex.Line> { new(0, "anyone? 29 here", 120_000, "asker", Lone: true) };
        var run = new Ex.Run(6, "thread", lane, Ex.LaneOf(lane), lines, 0);
        Ex.NoteLine(lane, "Bot0", 30_000);   // the asker went on to something else
        Assert.That(Drive(run, lane, (_, _) => true), Is.Empty);
        Assert.That(run.StopReason, Is.EqualTo("the speaker moved on"));

        string quiet = NewLane();
        var still = new Ex.Run(7, "thread", quiet, Ex.LaneOf(quiet), lines, 0);
        Assert.That(Drive(still, quiet, (_, _) => true, [10_000, 20_000, 30_000, 40_000, 50_000]).Count, Is.EqualTo(1),
            "a bump after others talked is fine: it is about the speaker's own line");
    }

    [Test]
    public void AReactionComesWhileItsTriggerIsStillOnScreen()
    {
        string lane = NewLane();
        var lines = new List<Ex.Line>
        {
            new(1, "nice going", 3_000, "reactor", Optional: true, Reaction: true), new(2, "show off", 40_000, "reactor", Optional: true, Reaction: true),
        };
        var run = new Ex.Run(8, "burst", lane, Ex.LaneOf(lane), lines, 0);
        var said = Drive(run, lane, (_, _) => true, [8_000, 12_000, 18_000, 25_000, 30_000]);
        Assert.That(said.Select(s => s.Line.Text), Is.EqualTo(new[] { "nice going" }), "\"show off\" 43 s and five lines later is left out");
    }

    // ---- 2. role and class truth ----------------------------------------------------------------------------------------

    [TestCase("Armsman", "tank", true)]
    [TestCase("Cabalist", "tank", false)]
    [TestCase("Cleric", "healer", true)]
    [TestCase("Paladin", "healer", false)]
    [TestCase("Sorcerer", "cc", true)]
    [TestCase("Wizard", "cc", false)]
    [TestCase("Scout", "puller", true)]
    [TestCase("Druid", "class:Druid", true)]
    [TestCase("Bard", "class:Druid", false)]
    [TestCase("Wizard", "dps", true)]
    [TestCase("Cleric", "dps", false)]
    public void AClassFillsTheRolesItReallyHas(string cls, string role, bool fills) => Assert.That(Field.Fills(cls, role), Is.EqualTo(fills), $"{cls} {role}");

    [Test]
    public void NeedATankIsAnsweredByATankNeverACabalist()
    {
        var said = new Field.Said("need a tank for sh barrows", "ask_group", ["sec, me", "me", "{cls} {lvl}, x?"]) { Role = "tank", LevelMin = 44, LevelMax = 50 };
        var random = new Random(4);
        for (int i = 0; i < 200; i++)
        {
            Assert.That(Field.Answer(said, Facts(1, "Cabalist", 50), null, random), Is.Null, "a cabalist is not a tank");
            Assert.That(Field.Answer(said, Facts(2, "Armsman", 30), null, random), Is.Null, "a level 30 is not in 44-50");
            Assert.That(Field.Answer(said, Facts(3, "Armsman", 48), null, random), Is.Not.Null);
        }
    }

    [Test]
    public void EveryGroupAskInAnEveningIsAnsweredOnlyByBotsItIsTrueFor()
    {
        var random = new Random(11);
        var memory = new Field.Memory();
        int roleAsks = 0, answered = 0;
        for (int i = 0; i < 6_000; i++)
        {
            Field.Facts asker = Facts(1_000 + i, Albion[i % Albion.Length], 10 + i % 40, Zones[i % Zones.Length]);
            if (Field.Line("faction", asker, memory, random, i * 3_000L) is not { Kind: "ask_group" } said || said.Answers == null)
                continue;
            Assert.That(said.LevelMax, Is.GreaterThan(0), said.Text);
            if (said.Role != null)
                roleAsks++;
            for (int k = 0; k < 6; k++)
            {
                Field.Facts mate = Facts(5_000 + k, Albion[(i + k * 5) % Albion.Length], 5 + (i * 7 + k * 13) % 46);
                if (Field.Answer(said, mate, null, random) is null)
                    continue;
                answered++;
                Assert.That(Field.CanAnswer(said, mate.ClassName, mate.Level), Is.True, $"{said.Text} <- {mate.ClassName} {mate.Level}");
            }
        }
        Assert.That(roleAsks, Is.GreaterThan(30), "the evening had role asks");
        Assert.That(answered, Is.GreaterThan(100));
    }

    [Test]
    public void NeedsAreTheAskersRealmsClasses()
    {
        var random = new Random(5);
        var memory = new Field.Memory();
        string[] notAlbion = ["druid", "bard", "shaman"];
        for (int i = 0; i < 6_000; i++)
            if (Field.Line("faction", Facts(i, Albion[i % Albion.Length], 20 + i % 25), memory, random, i * 3_000L) is { Role: { } role } said)
                Assert.That(notAlbion.Any(cls => role.EndsWith(cls, StringComparison.OrdinalIgnoreCase)), Is.False, said.Text);
    }

    [Test]
    public void AnyReaversOnIsAnsweredMeByAReaverAndTheAskerThenAsksItsQuestion()
    {
        var random = new Random(6);
        var said = new Field.Said("any reavers on? quick q", "class", ["{same}me, whats up", "{same}me"]) { Family = "class_q", SameClass = "Reaver", Thanks = ["ty"] };
        for (int i = 0; i < 200; i++)
            Assert.That(Field.Answer(said, Facts(1, "Wizard", 50), null, random), Is.Null, "a wizard doesn't say \"me\" to \"any reavers on?\"");
        int ran = 0;
        for (int i = 0; i < 2_000; i++)
        {
            List<Field.Step> steps = Field.Thread(said, Facts(1, "Reaver", 50), Facts(2, "Reaver", 47), null, null, random);
            if (steps.Count == 0)
                continue;
            Assert.That(steps[0].Who, Is.EqualTo(1));
            Assert.That(steps[0].Text, Does.StartWith("me"));
            if (steps.Count < 3)
                continue;
            ran++;
            Assert.That(steps[1].Who, Is.EqualTo(0), "the asker asks next");
            Assert.That(steps[1].Text, Is.Not.EqualTo("solo or grp?").And.Not.EqualTo("solo or grp"), "never the answerer's question");
            Assert.That(steps[2].Who, Is.EqualTo(1), "the answerer answers it");
            Assert.That(steps.Select(s => s.AtMs), Is.Ordered.Ascending);
        }
        Assert.That(ran, Is.GreaterThan(50));
    }

    [Test]
    public void AGearQuestionDoesntRunOnToWhichPart()
    {
        var random = new Random(7);
        var said = new Field.Said("anyone know where to buy theurg stuff at 50?", "help", ["merchants in the capital", "crafters, pst one"])
            { Family = "q_gear", Thanks = ["ty"] };
        for (int i = 0; i < 2_000; i++)
            foreach (Field.Step step in Field.Thread(said, Facts(1, "Theurgist", 50), Facts(2, "Wizard", 44), Facts(3, "Cleric", 40), null, random))
                Assert.That(step.Text, Is.Not.EqualTo("which part?").And.Not.EqualTo("which part").And.Not.EqualTo("the map helps"));
    }

    [Test]
    public void NobodyAsksTheLevelTheAnswerAlreadyGave()
    {
        var random = new Random(8);
        var said = new Field.Said("need a tank for sh barrows", "ask_group", ["{cls} {lvl}, x?", "me, {cls} {lvl}"]) { Thanks = ["kk inv"] };
        for (int i = 0; i < 3_000; i++)
        {
            List<Field.Step> steps = Field.Thread(said, Facts(1, "Cleric", 50), Facts(2, "Armsman", 49), null, null, random);
            if (steps.Count > 1 && steps[0].Text.Any(char.IsDigit))
                Assert.That(Field.QuestionKind(steps[1].Text), Is.Not.EqualTo("level"), string.Join(" / ", steps.Select(s => s.Text)));
            int invite = steps.FindIndex(s => s.Text.Contains("inv"));
            if (invite >= 0)
                Assert.That(steps.Skip(invite + 1).Any(s => s.Who == 0 && Field.QuestionKind(s.Text) == "level"), Is.False, "no level question after the invite");
        }
    }

    [Test]
    public void WhereIsOnlyAskedWhenTheTellerDidntSayAndIsAnsweredWithAPlace()
    {
        var random = new Random(9);
        int asked = 0;
        for (int i = 0; i < 3_000; i++)
        {
            List<Field.Step> story = Field.Story("death", Facts(1, "Scout", 30, "Lyonesse"), Facts(2, "Cleric", 31), Facts(3, "Wizard", 33), random);
            for (int k = 1; k + 1 < story.Count; k++)
                if (story[k].Who == 1 && story[k].Text.StartsWith("where"))
                {
                    asked++;
                    Assert.That(story[0].Text, Does.Not.Contain("lyonesse"), "the teller hadn't said where");
                    Assert.That(story[k + 1].Who, Is.EqualTo(0));
                    Assert.That(Field.Fits(story[k + 1].Text, "place"), Is.True, story[k + 1].Text);
                    Assert.That(story[k + 1].Text, Does.Contain("lyonesse"), "and says where now");
                }
        }
        Assert.That(asked, Is.GreaterThan(100));
    }

    // ---- 3. reactions -------------------------------------------------------------------------------------------------------

    [Test]
    public void ABotsOwnMomentDrawsOneOrTwoReactionsAndNewsAtMostThree()
    {
        var random = new Random(10);
        for (int i = 0; i < 1_000; i++)
        {
            Assert.That(AutonomousChatBursts.CappedSize("revenge", 6, 900, random), Is.InRange(1, 2));
            Assert.That(AutonomousChatBursts.CappedSize("nemesis", 6, 900, random), Is.InRange(1, 2));
            Assert.That(AutonomousChatBursts.CappedSize("keep_lost", 8, 100, random), Is.InRange(1, 3));
            Assert.That(AutonomousChatBursts.CappedSize("keep_lost", 8, 1_000, random), Is.InRange(1, 4));
        }
        Assert.That(AutonomousChatBursts.SpreadMs("revenge", AutonomousChatBudget.Current), Is.LessThanOrEqualTo(20_000), "and soon after it");
        Assert.That(AutonomousChatBursts.SpreadMs("keep_lost", AutonomousChatBudget.Current), Is.LessThanOrEqualTo(60_000));
    }

    [Test]
    public void OneBurstAtATimeInARealm()
    {
        long now = 5_000_000_000L;
        Assert.That(AutonomousChatBursts.TryClaim(eRealm.Midgard, now, now + 25_000), Is.True);
        Assert.That(AutonomousChatBursts.TryClaim(eRealm.Midgard, now + 10_000, now + 35_000), Is.False, "a cheer doesn't run into a commiseration");
        Assert.That(AutonomousChatBursts.TryClaim(eRealm.Midgard, now + 26_000, now + 50_000), Is.True);
    }

    [Test]
    public void AShortReactionIsHeardOnceInTenMinutesInAChannel()
    {
        var window = new Ex.ShortWindow();
        Assert.That(window.TryUse("faction:1", "oof", 0), Is.True);
        Assert.That(window.Fresh("faction:1", "Oof!", 60_000), Is.False, "the same reaction, however typed");
        Assert.That(window.TryUse("faction:1", "oof", 9 * 60_000), Is.False);
        Assert.That(window.TryUse("faction:2", "oof", 60_000), Is.True, "another realm's /faction is another channel");
        Assert.That(window.TryUse("faction:1", "oof", 11 * 60_000), Is.True, "ten minutes after it was said (up to a minute after it was planned) it may come again");
        Assert.That(Ex.IsShort("lol rip"), Is.True);
        Assert.That(Ex.IsShort("they hit hard at 30 for sure"), Is.False);
    }

    [Test]
    public void AStoryLeavesOutARiffTheChannelJustHeard()
    {
        var random = new Random(12);
        var window = new Ex.ShortWindow();
        string lane = NewLane();
        long now = 0;
        for (int i = 0; i < 200; i++, now += 30_000)
        {
            List<Field.Step> story = Field.Story("death", Facts(i, "Scout", 30), Facts(i + 1, "Cleric", 31), null, random, null,
                text => window.Fresh(lane, text, now));
            foreach (Field.Step step in story.Where(s => s.Who != 0 && Ex.IsShort(s.Text)))
                Assert.That(window.TryUse(lane, step.Text, now), Is.True, $"\"{step.Text}\" again within ten minutes");
        }
    }

    // ---- 4. the transcript and trade closings ---------------------------------------------------------------------------

    [TestCase("gl selling, someone will want it", false)]
    [TestCase("gl selling that", false)]
    [TestCase("ill pass, gl selling", true)]
    [TestCase("too much for me", true)]
    [TestCase("nah im good", true)]
    public void ATradeClosingFollowsATurnDownOnly(string reply, bool down) => Assert.That(AutonomousBotChatCoordinator.TurnsTradeDown(reply), Is.EqualTo(down));

    [TestCase("AutonomousBotChatCoordinator.TryField", "field")]
    [TestCase("AutonomousBotDebates.PlayBeat", "debate")]
    [TestCase("AutonomousLivingServer.RecordKill~", "nemesis")]
    [TestCase("AutonomousBotChatCoordinator.SayNews", "news")]
    [TestCase("Something.Else", "other")]
    public void ALineOutsideAnExchangeStillNamesItsFeature(string via, string feature) => Assert.That(BotChatTranscript.FeatureOf(via), Is.EqualTo(feature));

    [Test]
    public void ALineInsideAnExchangeIsTaggedWithItsIdIndexAndRole()
    {
        Assert.That(Ex.Current, Is.Null);
        using (Ex.Tag("thread", 42, 2, "answerer"))
        {
            Assert.That(BotChatTranscript.SourceFields("Misteater", eRealm.Albion, "x.y"), Does.StartWith(" src=thread ex=42 i=2 role=answerer"));
            using (Ex.Tag("burst", 43, 1, "reactor"))
                Assert.That(Ex.Current.Exchange, Is.EqualTo(43));
            Assert.That(Ex.Current.Exchange, Is.EqualTo(42), "the outer tag comes back");
        }
        Assert.That(Ex.Current, Is.Null);
        Assert.That(BotChatTranscript.SourceFields("Misteater", eRealm.Albion, "AutonomousBotChatCoordinator.TryField"), Does.StartWith(" src=field"));
    }

    // ---- 5. an evening, simulated -----------------------------------------------------------------------------------------

    private sealed class SimBot
    {
        public long Id;
        public string Name, Class, Zone;
        public int Level;
        public Field.Facts Facts => UT_FactionCoherence.Facts(Id, Class, Level, Zone);
    }

    private sealed record Plan(long Id, string Feature, List<Ex.Line> Lines, SimBot[] Speakers, Field.Said Said = null);

    /// <summary>
    /// What a simulated evening said, by exchange. The old scheduling (<paramref name="fixedCode"/> false) plays every line on its
    /// own timer with the old spacing rule, drops a thread line whose speaker is busy, and lets bursts of 3-6 run into each other;
    /// the new plays the same plans through <see cref="Ex.Run"/>, with the new answerer choice, caps and short-reaction window.
    /// </summary>
    private sealed class Evening
    {
        public readonly List<string> Log = [];
        public readonly List<(long At, long Ex, int Index, string Role, string Speaker, string Feature, string Text)> Said = [];
        public readonly Dictionary<long, Plan> Plans = [];
    }

    private static Evening Simulate(bool fixedCode, int seed, long lengthMs)
    {
        var random = new Random(seed);
        var evening = new Evening();
        string lane = "sim:" + (fixedCode ? "after" : "before") + ":" + seed;
        Ex.Lane laneState = Ex.LaneOf(lane);
        var shorts = new Ex.ShortWindow();
        var memory = new Field.Memory();
        var bots = Enumerable.Range(0, 120).Select(i => new SimBot
        {
            Id = 70_000 + i, Name = "Simbot" + (char)('a' + i % 26) + (char)('a' + i / 26 % 26), Class = Albion[(i * 7 + seed) % Albion.Length],
            Level = 5 + (i * 37 + seed) % 46, Zone = Zones[i % Zones.Length],
        }).ToList();
        bool Busy(SimBot bot, long now) => (now / 1_000 + bot.Id * 7) % 60 < 15;
        long baseTime = 20 * 3_600_000L;
        void Emit(long now, SimBot bot, string feature, long ex, int index, string role, string text)
        {
            Ex.NoteLine(lane, bot.Name, now);
            evening.Said.Add((now, ex, index, role, bot.Name, feature, text));
            long t = baseTime + now;
            evening.Log.Add($"11/08 {t / 3_600_000 % 24:00}:{t / 60_000 % 60:00}:{t / 1_000 % 60:00} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=bot ch=faction " +
                            $"realm=Albion from=\"{bot.Name}\" via=Sim src={feature} ex={ex} i={index} role={role} cls=\"{bot.Class}\" lvl={bot.Level} " +
                            $"text=\"{text.Replace('"', '\'')}\"");
        }
        SimBot Pick(SimBot not, Func<SimBot, bool> fits = null)
        {
            for (int tries = 0; tries < 200; tries++)
            {
                SimBot bot = bots[random.Next(bots.Count)];
                if (bot != not && (fits == null || fits(bot)))
                    return bot;
            }
            return null;
        }
        // The old scheduler: (due, plan, line index, spacing waits).
        var legacy = new List<(long Due, Plan Plan, int Line, int Waits)>();
        var runs = new List<(Ex.Run Run, Plan Plan)>();
        void Start(long now, Plan plan, int firstIndex = 1)
        {
            evening.Plans[plan.Id] = plan;
            if (!fixedCode)
            {
                long at = now;
                for (int i = 0; i < plan.Lines.Count; i++)
                {
                    at += plan.Lines[i].GapMs;
                    legacy.Add((at, plan, i, 0));
                }
                return;
            }
            var rules = plan.Feature == "burst" ? Ex.Rules.Faction with { ReactionWithinMs = 30_000 } : Ex.Rules.Faction;
            runs.Add((new Ex.Run(plan.Id, plan.Feature, lane, laneState, plan.Lines, now, rules, firstIndex), plan));
            Ex.Touch(laneState, plan.Id, now);
        }
        string[] boasts = ["nice going", "well earned", "show off", "gz", "nice!", "well played", "lucky you", "now thats a good night", "grats grats", "way to go",
            "ha, nice, now find something meaner", "sweet", "woot, nice"];
        string[] complaints = ["oof", "ugh, that sucks", "that stinks", "bad luck", "been there", "rough night huh", "the dice hate us all", "hang in there",
            "thats annoying", "bleh, been there", "argh, hate that"];
        for (long now = 0; now < lengthMs; now += 1_000)
        {
            // A field line in /faction about every 15 s (the busy realm of the owner's evening), and its thread.
            if (random.NextDouble() < 1 / 15.0)
            {
                SimBot starter = bots[random.Next(bots.Count)];
                if (Field.Line("faction", starter.Facts, memory, random, now) is { } said)
                {
                    long ex = Ex.NewId();
                    Emit(now, starter, "field", ex, 0, "asker", said.Text);
                    if (said.Answers != null && (!fixedCode || Ex.RunningCount(lane, now) < Ex.MaxRunning))
                    {
                        Func<SimBot, bool> fits = fixedCode ? bot => Field.CanAnswer(said, bot.Class, bot.Level, Field.Fills(bot.Class, "healer")) : null;
                        SimBot answerer = Pick(starter, fits), third = Pick(starter, bot => bot != answerer && (fits == null || fits(bot)));
                        List<Field.Step> steps = Field.Thread(said, starter.Facts, answerer?.Facts, third?.Facts, memory, random, now);
                        if (steps.Count > 0)
                            Start(now, new Plan(ex, "thread", Ex.FromSteps(steps, ["asker", "answerer", "third"], step => step.Who == 2), [starter, answerer, third], said));
                    }
                }
            }
            // A story from a death or a wipe about every two minutes.
            if (random.NextDouble() < 1 / 120.0 && (!fixedCode || Ex.RunningCount(lane, now) < Ex.MaxRunning))
            {
                SimBot teller = bots[random.Next(bots.Count)], mate = Pick(teller), third = Pick(teller, bot => bot != mate);
                string kind = random.NextDouble() < 0.75 ? "death" : "wipe";
                long at = now;
                List<Field.Step> story = Field.Story(kind, teller.Facts, mate.Facts, third.Facts, random, memory,
                    fixedCode ? text => shorts.Fresh(lane, text, at) : null);
                if (fixedCode)
                    story = story.Where(step => step.Who == 0 || !Ex.IsShort(step.Text) || shorts.TryUse(lane, step.Text, at)).ToList();
                if (story.Count > 0)
                    Start(now, new Plan(Ex.NewId(), "story", Ex.FromSteps(story.Select(s => s with { AtMs = s.AtMs + 2_000 }), ["teller", "riff", "riff"],
                        step => step.Who == 2), [teller, mate, third]), firstIndex: 0);
            }
            // A nemesis or a revenge about every three minutes, and its burst.
            if (random.NextDouble() < 1 / 180.0)
            {
                SimBot teller = bots[random.Next(bots.Count)];
                bool revenge = random.NextDouble() < 0.5;
                long ex = Ex.NewId();
                Emit(now, teller, revenge ? "revenge" : "nemesis", ex, 0, "teller",
                    revenge ? "got that dwarf berserker. we are even" : "same norseman spiritmaster got me at caledon again");
                int size = 3 + random.Next(4);
                int spread = 90_000;
                bool go = true;
                if (fixedCode)
                {
                    string trigger = revenge ? "revenge" : "nemesis";
                    size = AutonomousChatBursts.CappedSize(trigger, size, bots.Count, random);
                    spread = AutonomousChatBursts.SpreadMs(trigger, AutonomousChatBudget.Current);
                    go = AutonomousChatBursts.TryClaim(eRealm.Hibernia, 9_000_000_000L * (seed + 1) + now, 9_000_000_000L * (seed + 1) + now + spread + 5_000);
                }
                if (go)
                {
                    int[] delays = AutonomousChatBursts.Delays(size, spread, random);
                    string[] pool = revenge ? boasts : complaints;
                    var lines = new List<Ex.Line>();
                    var speakers = new List<SimBot> { teller };
                    var heard = new HashSet<string>();
                    int last = 0;
                    foreach (int delay in delays)
                    {
                        string text = pool.OrderBy(_ => random.Next()).FirstOrDefault(t => !heard.Contains(t) && (!fixedCode || shorts.Fresh(lane, t, now)));
                        if (text == null || fixedCode && !shorts.TryUse(lane, text, now))
                            break;
                        heard.Add(text);
                        speakers.Add(Pick(teller, bot => !speakers.Contains(bot)));
                        lines.Add(new Ex.Line(speakers.Count - 1, text, delay - last, "reactor", Optional: true, Reaction: true));
                        last = delay;
                    }
                    if (lines.Count > 0)
                        Start(now, new Plan(ex, "burst", lines, speakers.ToArray()));
                }
            }
            // Due lines, the old way.
            for (int k = 0; k < legacy.Count; k++)
            {
                var (due, plan, index, waits) = legacy[k];
                if (due > now)
                    continue;
                Ex.Line line = plan.Lines[index];
                SimBot bot = line.Who < plan.Speakers.Length ? plan.Speakers[line.Who] : null;
                if (bot != null && waits < 3 && now - laneState.LastAt < 2_000)
                {
                    legacy[k] = (now + 2_000 + random.Next(2_000), plan, index, waits + 1);
                    continue;
                }
                legacy.RemoveAt(k--);
                // PlayThread's Schedule dropped a line whose speaker was fighting; stories and bursts said it anyway.
                if (bot == null || plan.Feature == "thread" && Busy(bot, now))
                    continue;
                Emit(now, bot, plan.Feature, plan.Id, (plan.Feature == "story" ? 0 : 1) + index, line.Role, line.Text);
            }
            // Due lines, the new way.
            for (int k = 0; k < runs.Count; k++)
            {
                var (run, plan) = runs[k];
                if (run.DueAt > now)
                    continue;
                long? next = run.Step(now, line => line.Who < plan.Speakers.Length && plan.Speakers[line.Who] is { } bot && (plan.Feature == "story" && line.Who == 0 || !Busy(bot, now)),
                    line => line.Who < plan.Speakers.Length ? plan.Speakers[line.Who]?.Name : null, (line, index) =>
                    {
                        Emit(now, plan.Speakers[line.Who], plan.Feature, plan.Id, index, line.Role, line.Text);
                        return true;
                    }, random);
                if (next == null)
                {
                    Ex.Finish(laneState, plan.Id);
                    runs.RemoveAt(k--);
                }
                else
                    Ex.Touch(laneState, plan.Id, now);
            }
        }
        return evening;
    }

    private static string Norm(string text) => Ex.Normalize(text);

    [Test]
    public void AnEveningHoldsTogether()
    {
        const long hours3 = 3 * 3_600_000L;
        Evening before = Simulate(false, 1, hours3), after = Simulate(true, 1, hours3);
        string folder = Environment.GetEnvironmentVariable("CHAT_SIM_DIR") is { Length: > 0 } dir ? dir : Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-sim");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "faction-sim-before.chat.log"), before.Log);
        File.WriteAllLines(Path.Combine(folder, "faction-sim-after.chat.log"), after.Log);

        int OutOfOrder(Evening e) => e.Said.GroupBy(s => s.Ex).Count(g => g.Select(s => s.Index).Zip(g.Select(s => s.Index).Skip(1), (a, b) => b < a).Any(x => x));
        int RoleSwaps(Evening e) => e.Said.Where(s => s.Role is "asker" or "answerer" or "teller").GroupBy(s => (s.Ex, s.Role)).Count(g => g.Select(s => s.Speaker).Distinct().Count() > 1);
        int RoleFalse(Evening e) => e.Plans.Values.Where(p => p.Said?.Role != null || p.Said?.LevelMax > 0)
            .Count(p => e.Said.Any(s => s.Ex == p.Id && s.Role == "answerer" && p.Speakers[1] is { } a && !Field.CanAnswer(p.Said, a.Class, a.Level)));
        int Orphans(Evening e) => e.Plans.Values.Where(p => p.Feature == "thread").Count(p =>
        {
            var said = e.Said.Where(s => s.Ex == p.Id && s.Index > 0).Select(s => s.Index - 1).ToHashSet();
            // A line whose earlier non-optional line was never said: an answer to nothing on screen.
            return said.Any(i => Enumerable.Range(0, i).Any(j => !p.Lines[j].Optional && !said.Contains(j)));
        });
        int PersonalOverTwo(Evening e) => e.Said.Where(s => s.Feature == "burst").GroupBy(s => s.Ex).Count(g => g.Count() > 2);
        int ReactionRepeats(Evening e)
        {
            int repeats = 0;
            var last = new Dictionary<string, long>();
            foreach (var s in e.Said.Where(s => s.Role is "riff" or "reactor" && Ex.IsShort(s.Text)).OrderBy(s => s.At))
            {
                if (last.TryGetValue(Norm(s.Text), out long at) && s.At - at < Ex.ShortWindow.WindowMs)
                    repeats++;
                last[Norm(s.Text)] = s.At;
            }
            return repeats;
        }
        string Line(string name, Evening e) =>
            $"{name}: {e.Said.Count} lines ({e.Said.Count / 180.0:0.00}/min), out of order {OutOfOrder(e)}, role swaps {RoleSwaps(e)}, untrue answers {RoleFalse(e)}, " +
            $"thread lines after a missing one {Orphans(e)}, bursts over two {PersonalOverTwo(e)}, reaction repeats in 10 min {ReactionRepeats(e)}";
        TestContext.Progress.WriteLine("CHAT_SIM " + Line("before", before));
        TestContext.Progress.WriteLine("CHAT_SIM " + Line("after", after));

        Assert.That(OutOfOrder(before), Is.GreaterThan(0), "the simulation reproduces the old bug");
        Assert.That(OutOfOrder(after), Is.Zero, Line("after", after));
        Assert.That(RoleSwaps(after), Is.Zero);
        Assert.That(RoleFalse(after), Is.Zero);
        Assert.That(Orphans(after), Is.Zero);
        Assert.That(PersonalOverTwo(after), Is.Zero);
        Assert.That(ReactionRepeats(after), Is.Zero);
        Assert.That(after.Said.Count, Is.GreaterThan(before.Said.Count * 0.7), "coherent, not quiet");
    }
}
