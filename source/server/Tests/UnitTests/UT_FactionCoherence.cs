using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ex = DOL.GS.AutonomousChatExchange;
using Field = DOL.GS.AutonomousChatFieldTalk;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

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
        Assert.That(answered, Is.GreaterThan(50));
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
            Assert.That(AutonomousChatBursts.CappedSize("keep_lost", 8, 1_000, random), Is.InRange(1, 3), "a full realm too");
        }
        Assert.That(AutonomousChatBursts.SpreadMs("revenge", AutonomousChatBudget.Current), Is.LessThanOrEqualTo(20_000), "and soon after it");
        Assert.That(AutonomousChatBursts.SpreadMs("keep_lost", AutonomousChatBudget.Current), Is.LessThanOrEqualTo(45_000));
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
    public void EveryLineButATaggedEventFitsUnderTheCeiling()
    {
        Assert.That(Ex.Fits(6.0, 7.1), Is.True, "12 lines in two minutes, a 13th makes 6.5");
        Assert.That(Ex.Fits(6.7, 7.1), Is.False, "a 15th would make 7.2");
        Assert.That(Ex.Fits(6.5, 7.0), Is.True, "exactly at the ceiling fits");
        Assert.That(Ex.ExemptFromCeiling(new Ex.Source("event", 1, 0, "teller")), Is.True);
        Assert.That(Ex.ExemptFromCeiling(new Ex.Source("player", 0, 0, "answerer")), Is.True);
        foreach (string feature in new[] { "thread", "story", "burst", "nemesis", "revenge", "news", "debate", "exchange_ad", "ambient", "field", "reaction" })
            Assert.That(Ex.ExemptFromCeiling(new Ex.Source(feature, 1, 0, "x")), Is.False, feature);
        Assert.That(Ex.ExemptFromCeiling(null), Is.False, "an untagged line waits its turn too");
        Assert.That(AutonomousChatBursts.IsEvent("keep_lost") && AutonomousChatBursts.IsEvent("relic_won") && AutonomousChatBursts.IsEvent("first"), Is.True);
        Assert.That(AutonomousChatBursts.IsEvent("nemesis") || AutonomousChatBursts.IsEvent("revenge"), Is.False);
    }

    [Test]
    public void TheFactionSinksRule()
    {
        var player = new Ex.Source("player", 0, 0, "answerer");
        var evt = new Ex.Source("event", 7, 1, "reactor");
        var beat = new Ex.Source("debate", 8, 3, "B");
        var opener = new Ex.Source("field", 9, 0, "asker");
        var reply = new Ex.Source("thread", 9, 1, "answerer");
        long never = long.MinValue / 2, now = 1_000_000;
        // A player's question is answered while a scene plays and while /faction is full (live test 2026-11-08: no answer for two
        // minutes during a debate).
        Assert.That(Ex.SinkHold(player, sceneRunning: true, fits: false, now, now - 100, never), Is.Null);
        Assert.That(Ex.SinkHold(evt, true, false, now, now - 100, never), Is.Null, "an event goes");
        Assert.That(Ex.SinkHold(beat, true, true, now, now - 100, never), Is.Null, "the scene's own line goes");
        Assert.That(Ex.SinkHold(beat, true, false, now, now - 100, never), Is.EqualTo("ceiling"), "and waits for room");
        Assert.That(Ex.SinkHold(reply, true, true, now, now - 100, never), Is.EqualTo("scene"), "anything else yields to the scene");
        Assert.That(Ex.SinkHold(null, true, true, now, now - 100, never), Is.EqualTo("scene"));
        Assert.That(Ex.SinkHold(reply, false, false, now, now - 100, never), Is.EqualTo("ceiling"));
        Assert.That(Ex.SinkHold(reply, false, true, now, now - 100, never), Is.Null, "a reply in a conversation has its own spacing");
        Assert.That(Ex.SinkHold(opener, false, true, now, now - 2_000, never), Is.EqualTo("spacing"), "an opener keeps 3 s from the last line");
        Assert.That(Ex.SinkHold(opener, false, true, now, now - 4_000, never), Is.Null);
        // Just after a scene ended: ten seconds between openers, so the lines that waited don't all land at once.
        Assert.That(Ex.SinkHold(opener, false, true, now, now - 4_000, now - 30_000), Is.EqualTo("spacing"));
        Assert.That(Ex.SinkHold(null, false, true, now, now - 9_000, now - 30_000), Is.EqualTo("spacing"), "an untagged line too");
        Assert.That(Ex.SinkHold(opener, false, true, now, now - 11_000, now - 30_000), Is.Null);
        Assert.That(Ex.SinkHold(opener, false, true, now, now - 4_000, now - 130_000), Is.Null, "two minutes on, as usual");
    }

    [TestCase("where do i buy a better staff for my elem?", eRealm.Albion, "fletcher", "camelot")]
    [TestCase("where can i get a new sword", eRealm.Midgard, "weaponcrafter", "jordheim")]
    [TestCase("who sells chain armor around here", eRealm.Albion, "armorcrafter", "camelot")]
    [TestCase("where do i buy better gear at 30?", eRealm.Hibernia, "tir na nog", "tir na nog")]
    public void WhereDoIBuyGetsATrueAnswer(string question, eRealm realm, string mentions, string capital)
    {
        Assert.That(AutonomousChatKnowledge.TryAnswerBuyGear(realm, question, out string answer), Is.True, question);
        Assert.That(answer, Does.Contain(mentions).And.Contain(capital), answer);
        Assert.That(answer, Is.EqualTo(answer.ToLowerInvariant()), "typed like the period");
    }

    [TestCase("my staff is better than yours")]
    [TestCase("where is the cleric trainer")]
    [TestCase("anyone want to group")]
    public void NotEveryLineIsABuyQuestion(string line) => Assert.That(AutonomousChatKnowledge.TryAnswerBuyGear(eRealm.Albion, line, out _), Is.False, line);

    [Test]
    public void ARoleThatSpokeKeepsItsBot()
    {
        var Free = AutonomousBotDebates.SpeakerState.Free;
        var Busy = AutonomousBotDebates.SpeakerState.Busy;
        var Gone = AutonomousBotDebates.SpeakerState.Gone;
        // Not yet spoken: as before (a stand-in may take it).
        Assert.That(AutonomousBotDebates.DecideBeat(Gone, 0, true, 3, roleSpoke: false), Is.EqualTo(AutonomousBotDebates.BeatDecision.Speak));
        // Spoken: the same bot or nothing (live check 2026-11-08: the dunce was Iagoys, then Yno).
        Assert.That(AutonomousBotDebates.DecideBeat(Free, 0, true, 3, roleSpoke: true), Is.EqualTo(AutonomousBotDebates.BeatDecision.Speak));
        Assert.That(AutonomousBotDebates.DecideBeat(Busy, 10_000, true, 3, roleSpoke: true), Is.EqualTo(AutonomousBotDebates.BeatDecision.Wait));
        Assert.That(AutonomousBotDebates.DecideBeat(Busy, AutonomousBotDebates.MaxBusyWaitMs, true, 3, roleSpoke: true), Is.EqualTo(AutonomousBotDebates.BeatDecision.Speak),
            "still fighting after the wait: the same bot types between swings (PlayBeat gives it its own bot, never a stand-in)");
        Assert.That(AutonomousBotDebates.DecideBeat(Gone, 0, true, 5, roleSpoke: true), Is.EqualTo(AutonomousBotDebates.BeatDecision.End), "left the game: the scene ends");
        Assert.That(AutonomousBotDebates.MayRecast(roleSpoke: false, storyProtagonist: false), Is.True);
        Assert.That(AutonomousBotDebates.MayRecast(roleSpoke: true, storyProtagonist: false), Is.False);
        Assert.That(AutonomousBotDebates.MayRecast(roleSpoke: false, storyProtagonist: true), Is.False);
    }

    /// <summary>
    /// Every scene in the corpus (the dunce's roasts, storyline episodes, debates), played by the beat rules (a model of
    /// PlayBeat's decisions), with the first speaker, right after its first line, busy for minutes in one run and gone from the
    /// game in another: a role is said by one bot only, never a stand-in once it has spoken. A busy one is waited for and then
    /// says its lines (the scene plays to its end, a dunce to its rage quit); one that left ends the scene.
    /// </summary>
    [Test]
    public void ARoleIsSaidByOneBotInEveryScene()
    {
        VC.Scene[] scenes = AutonomousBotVoices.Corpus.Scenes;
        int dunces = 0, stories = 0, debates = 0, ended = 0, busyPlayedOut = 0, busyOthers = 0;
        foreach (VC.Scene scene in scenes)
            foreach (string mode in new[] { "leaves", "busy" })
            {
                bool story = AutonomousBotStorylines.IsStory(scene);
                var cast = scene.SpeakingRoles.Select((role, i) => (role, id: 1_000L + i)).ToDictionary(c => c.role, c => c.id);
                long nextStandIn = 9_000;
                var speakersOf = new Dictionary<string, HashSet<long>>();
                var said = new HashSet<string>();
                string victim = scene.SpeakingRoles[0];
                bool victimHit = false, end = false;
                long waited = 0;
                for (int b = 0; b < scene.Beats.Length && !end; b++)
                {
                    VC.Beat beat = scene.Beats[b];
                    if (beat.IsPause || beat.Variants.Length == 0)
                        continue;
                    var state = AutonomousBotDebates.SpeakerState.Free;
                    if (beat.Role == victim && victimHit)
                        state = mode == "leaves" ? AutonomousBotDebates.SpeakerState.Gone : AutonomousBotDebates.SpeakerState.Busy;
                    bool spoke = said.Contains(beat.Role);
                    bool protagonist = story && beat.Role is "A" or "B";
                    // A storyline's protagonist is never replaced: waited for up to three minutes, else the episode ends.
                    if (protagonist && state != AutonomousBotDebates.SpeakerState.Free)
                    {
                        if (state == AutonomousBotDebates.SpeakerState.Busy && waited < AutonomousBotDebates.StoryBusyWaitMs)
                        {
                            waited += AutonomousBotDebates.BusyRetryMs;
                            b--;
                            continue;
                        }
                        end = true;
                        ended++;
                        break;
                    }
                    long? speaker = spoke ? (state != AutonomousBotDebates.SpeakerState.Gone ? cast[beat.Role] : null)
                        : state == AutonomousBotDebates.SpeakerState.Free ? cast[beat.Role]
                        : AutonomousBotDebates.MayRecast(spoke, protagonist) ? nextStandIn++ : null;
                    var decision = AutonomousBotDebates.DecideBeat(state, waited, speaker != null, scene.SpeakingRoles.Length, spoke);
                    if (decision == AutonomousBotDebates.BeatDecision.Wait)
                    {
                        waited += AutonomousBotDebates.BusyRetryMs;
                        b--;
                        continue;
                    }
                    waited = 0;
                    if (decision == AutonomousBotDebates.BeatDecision.End || spoke && speaker == null)
                    {
                        end = true;
                        ended++;
                        break;
                    }
                    if (decision == AutonomousBotDebates.BeatDecision.Skip || speaker == null)
                        continue;
                    if (speaker != cast[beat.Role])
                        cast[beat.Role] = speaker.Value;   // a stand-in for a role nobody heard yet
                    (speakersOf.TryGetValue(beat.Role, out var who) ? who : speakersOf[beat.Role] = new HashSet<long>()).Add(speaker.Value);
                    said.Add(beat.Role);
                    if (beat.Role == victim)
                        victimHit = true;
                }
                foreach (var role in speakersOf)
                    Assert.That(role.Value.Count, Is.EqualTo(1), $"{scene.Id} ({mode}): role {role.Key} said by {role.Value.Count} bots");
                if (mode == "busy" && !story)
                {
                    busyOthers++;
                    if (!end)
                        busyPlayedOut++;
                }
                if (scene.Exit.Length > 0)
                    dunces++;
                else if (story)
                    stories++;
                else
                    debates++;
            }
        Assert.That(dunces, Is.GreaterThanOrEqualTo(12), "the dunce scenes, both ways");
        Assert.That(stories, Is.GreaterThan(0));
        Assert.That(debates, Is.GreaterThan(0));
        Assert.That(ended, Is.GreaterThan(0), "scenes whose first speaker left ended");
        Assert.That(busyPlayedOut, Is.EqualTo(busyOthers), "a busy speaker that spoke is waited for and goes on: every dunce scene and debate plays out");
    }

    [Test]
    public void AFeatureThatIsDueKeepsItsTurn()
    {
        const long due = 100_000, never = long.MinValue / 2;
        Assert.That(Ex.AfterTry(true, null, due, never, due), Is.EqualTo(Ex.DueStep.Said));
        Assert.That(Ex.AfterTry(false, "ceiling", due, never, due + 30_000), Is.EqualTo(Ex.DueStep.Retry));
        Assert.That(Ex.AfterTry(false, "ceiling", due, never, due + Ex.DueWaitMs), Is.EqualTo(Ex.DueStep.GiveUp), "three minutes without room");
        Assert.That(Ex.AfterTry(false, "scene", due, never, due + 10 * 60_000), Is.EqualTo(Ex.DueStep.Retry), "a scene's time is not counted");
        Assert.That(Ex.AfterTry(false, "ceiling", due, due + 5 * 60_000, due + 6 * 60_000), Is.EqualTo(Ex.DueStep.Retry), "counted from the scene's end");
        Assert.That(Ex.AfterTry(false, "spacing", due, due + 5 * 60_000, due + 8 * 60_000), Is.EqualTo(Ex.DueStep.GiveUp));
        var random = new Random(3);
        for (int i = 0; i < 50; i++)
            Assert.That(AutonomousGoldSpammers.NextAdIn(true, null, 0, never, due, random), Is.InRange(18_000, 47_999), "a said ad: the next in 18-48 s");
        Assert.That(AutonomousGoldSpammers.NextAdIn(false, "ceiling", due, never, due + 1_000, random), Is.EqualTo(Ex.DueRetryMs), "a held ad: again in 3 s");
        Assert.That(AutonomousGoldSpammers.NextAdIn(false, "ceiling", due, never, due + Ex.DueWaitMs, random), Is.Null);
        Assert.That(Ex.DueLog("spammer", eRealm.Albion, 42, " name=\"Xkqhoaa\""), Is.EqualTo("LIVING_FEATURE_DUE feature=spammer realm=Albion ex=42 name=\"Xkqhoaa\""));
        Assert.That(Ex.DroppedLog("rumour", eRealm.Midgard, 7, 181_500, "ceiling"), Is.EqualTo("LIVING_FEATURE_DROPPED feature=rumour realm=Midgard ex=7 waited_s=181 reason=ceiling"));
    }

    /// <summary>
    /// The live check of 1.1.3 RC1 (2026-11-08 21:31): a gold spammer turned up 4 s before a debate, which owned /faction for
    /// 35 s, and the channel then sat at its ceiling for two minutes. The RC1 way spent each held ad and tried the next 18-48 s
    /// later (33 s here, the mean): every ad of the wave was held and the spammer never spoke. The fixed way keeps the ad and
    /// tries every 3 s: it speaks as soon as there is room, and the whole wave is said. Played through the sink's own rule (<see cref="Ex.SinkHold"/>).
    /// </summary>
    [Test]
    public void ASpammerThatTurnsUpAsASceneBeginsStillSpams()
    {
        const long sceneFrom = 4_000, sceneTo = 39_000, roomFrom = sceneTo + 120_000;
        string Held(long now, long lastLine) =>
            Ex.SinkHold(new Ex.Source("spammer", 1, 0, "spammer"), now >= sceneFrom && now < sceneTo, now >= roomFrom, now, lastLine,
                now >= sceneTo ? sceneTo : long.MinValue / 2);
        (int Said, long FirstAt) Play(bool rc1)
        {
            var random = new Random(5);
            int adsLeft = 5, said = 0;
            long firstAt = -1, lastLine = sceneTo, heldSince = 0;
            for (long next = 0; adsLeft > 0 && next < 30 * 60_000;)
            {
                long now = next;
                string held = Held(now, lastLine);
                if (held == null)
                {
                    said++;
                    adsLeft--;
                    lastLine = now;
                    heldSince = 0;
                    firstAt = firstAt < 0 ? now : firstAt;
                    next = now + (rc1 ? 18_000 + random.Next(30_000) : AutonomousGoldSpammers.NextAdIn(true, null, 0, 0, now, random)!.Value);
                    continue;
                }
                if (rc1)
                {
                    adsLeft--;   // RC1: a held ad was spent, and the next tried 18-48 s on (here the mean, 33 s)
                    next = now + 33_000;
                    continue;
                }
                heldSince = heldSince == 0 ? Math.Max(now, 1) : heldSince;
                if (AutonomousGoldSpammers.NextAdIn(false, held, heldSince, now >= sceneTo ? sceneTo : long.MinValue / 2, now, random) is not long retry)
                    break;
                next = now + retry;
            }
            return (said, firstAt);
        }
        Assert.That(Play(rc1: true).Said, Is.Zero, "the RC1 way: a started spammer said nothing (as seen live)");
        var fixedWay = Play(rc1: false);
        Assert.That(fixedWay.Said, Is.EqualTo(5), "the whole wave is said");
        Assert.That(fixedWay.FirstAt, Is.InRange(roomFrom, roomFrom + Ex.DueRetryMs), "its first ad as soon as there is room");
    }

    /// <summary>The RC1 live check at Camelot's bind (2026-11-08): about 25 "released, running back" lines in 45 s in /say.</summary>
    [Test]
    public void TheWayBackAfterAReleaseIsToldToTheBotsOwnGroup()
    {
        Assert.That(GameBot.ReturnLineChannel(inGroup: true, groupHasPlayer: true), Is.EqualTo("group"));
        Assert.That(GameBot.ReturnLineChannel(inGroup: true, groupHasPlayer: false), Is.Null, "a bots-only group needs no word");
        Assert.That(GameBot.ReturnLineChannel(inGroup: false, groupHasPlayer: false), Is.Null);
        // And never /say: the code that said it there is gone (each stage goes through TellOwnGroup).
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string file = Path.Combine(dir.FullName, "GameServer", "bots", "GameBot.cs");
            if (!File.Exists(file))
                continue;
            Assert.That(File.ReadAllText(file), Does.Not.Contain("Say(ReturnDialogue("));
            break;
        }
    }

    /// <summary>The RC1 live check: Cabowyn greeted Manawatch twice a second apart (a GM test had cleared the cooldowns).</summary>
    [Test]
    public void ABotGreetsAPlayerOncePerMeeting()
    {
        const long now = 50 * 60_000, never = long.MinValue / 2;
        Assert.That(AutonomousLivingServer.MayGreet(never, 0, now), Is.True);
        Assert.That(AutonomousLivingServer.MayGreet(never, now + 1_500, now), Is.False, "a greeting on its way");
        Assert.That(AutonomousLivingServer.MayGreet(now - 1_000, 0, now), Is.False, "greeted a second ago");
        Assert.That(AutonomousLivingServer.MayGreet(now - AutonomousLivingServer.MeetingMs + 1, 0, now), Is.False);
        Assert.That(AutonomousLivingServer.MayGreet(now - AutonomousLivingServer.MeetingMs, 0, now), Is.True, "a new meeting");
    }

    /// <summary>The RC1 live check: a forced deed read "saw Manawatch's group take a named monster in City of Camelot".</summary>
    [Test]
    public void ADeedNamesAPlaceItCouldHappen()
    {
        Assert.That(AutonomousPlayerDeeds.PlausiblePlace(AutonomousPlayerDeeds.Kind.NamedGroup, capital: true, frontier: false), Is.False);
        Assert.That(AutonomousPlayerDeeds.PlausiblePlace(AutonomousPlayerDeeds.Kind.NamedGroup, capital: false, frontier: false), Is.True);
        Assert.That(AutonomousPlayerDeeds.PlausiblePlace(AutonomousPlayerDeeds.Kind.SillyDeath, capital: true, frontier: false), Is.False);
        Assert.That(AutonomousPlayerDeeds.PlausiblePlace(AutonomousPlayerDeeds.Kind.RvrStreak, capital: false, frontier: false), Is.False, "a streak is in the frontier");
        Assert.That(AutonomousPlayerDeeds.PlausiblePlace(AutonomousPlayerDeeds.Kind.Ganked, capital: false, frontier: true), Is.True);
        var zones = new List<(string Name, bool Frontier, bool Home)>
        {
            ("City of Camelot", false, false), ("Salisbury Plains", false, true), ("Forest Sauvage", true, false), ("Vale of Mularn", false, false),
        };
        var random = new Random(1);
        Assert.That(AutonomousPlayerDeeds.TestPlace(AutonomousPlayerDeeds.Kind.NamedGroup, "City of Camelot", false, zones, random), Is.EqualTo("Salisbury Plains"));
        Assert.That(AutonomousPlayerDeeds.TestPlace(AutonomousPlayerDeeds.Kind.RvrStreak, "City of Camelot", false, zones, random), Is.EqualTo("Forest Sauvage"));
        Assert.That(AutonomousPlayerDeeds.TestPlace(AutonomousPlayerDeeds.Kind.NamedSolo, "Black Mountains North", true, zones, random), Is.EqualTo("Black Mountains North"));
        Assert.That(AutonomousPlayerDeeds.TestPlace(AutonomousPlayerDeeds.Kind.Fell, "City of Camelot", false, [("City of Camelot", false, false)], random), Is.Null);
    }

    [Test]
    public void ASceneBeatWaitsForRoomAndOnlyACutSceneLosesItsEnd()
    {
        Assert.That(AutonomousBotDebates.DecideRoom(true, 0), Is.EqualTo(AutonomousBotDebates.RoomDecision.Speak));
        Assert.That(AutonomousBotDebates.DecideRoom(false, 0), Is.EqualTo(AutonomousBotDebates.RoomDecision.Wait));
        Assert.That(AutonomousBotDebates.DecideRoom(false, AutonomousBotDebates.MaxRoomWaits - 1), Is.EqualTo(AutonomousBotDebates.RoomDecision.Wait));
        Assert.That(AutonomousBotDebates.DecideRoom(false, AutonomousBotDebates.MaxRoomWaits), Is.EqualTo(AutonomousBotDebates.RoomDecision.Cut));
        Assert.That(AutonomousBotDebates.DecideRoom(true, AutonomousBotDebates.MaxRoomWaits), Is.EqualTo(AutonomousBotDebates.RoomDecision.Speak));
        // A dunce cut short after its own lines still rage quits; one cut before it spoke doesn't.
        Assert.That(AutonomousBotDebates.ExitOnCut("A", ["A", "B", "C"]), Is.True);
        Assert.That(AutonomousBotDebates.ExitOnCut("A", ["B"]), Is.False);
        Assert.That(AutonomousBotDebates.ExitOnCut("", ["A"]), Is.False, "a scene with no exit");
        Assert.That(Ex.IsScene(new Ex.Source("debate", 1, 0, "A")) && Ex.IsScene(new Ex.Source("storyline", 1, 0, "B")), Is.True);
        Assert.That(Ex.IsScene(new Ex.Source("thread", 1, 0, "asker")), Is.False);
    }

    /// <summary>
    /// Every scene in the corpus (debates, storyline episodes, the dunce's roasts) played as AutonomousBotDebates.PlayBeat plays
    /// it, on a realm whose /faction is already at its ceiling: the two minutes before are full, every other line yields to the
    /// scene (BroadcastFactionAsWritten), and a realm event's capped burst lands in the middle. Each beat waits for room
    /// (DecideRoom), never longer than the cut, and the scene plays every line in order to its end, so a dunce's exit runs.
    /// A model of the beat loop, not the game's timers: no bots log off here.
    /// </summary>
    [Test]
    public void EveryScenePlaysToItsEndOnARealmAtItsCeiling()
    {
        var settings = AutonomousChatBudget.Current;
        var band = AutonomousChatBudget.Band(settings, AutonomousChatBudget.Channel.Faction, SimRealmBots, 20, false, 1.0);
        VC.Scene[] scenes = AutonomousBotVoices.Corpus.Scenes;
        Assert.That(scenes.Length, Is.GreaterThan(20));
        int dunces = 0, stories = 0, debates = 0;
        foreach (VC.Scene scene in scenes)
        {
            var meter = new AutonomousChatBudget.ChannelMeter();
            const long key = 1;
            long now = 10 * 60_000;
            // Full: as many lines in the last two minutes as the ceiling allows.
            int full = (int)Math.Floor(band.Ceiling * 2);
            for (int i = 0; i < full; i++)
                meter.Count(AutonomousChatBudget.Channel.Faction, key, now - 119_000 + i * (118_000 / Math.Max(1, full)));
            var said = new List<string>();
            int waits = 0, beatIndex = 0;
            bool cut = false, eventSaid = false;
            foreach (VC.Beat beat in scene.Beats)
            {
                if (beat.IsPause)
                {
                    now += Math.Max(1, beat.PauseSeconds) * 1_000L;
                    continue;
                }
                if (beat.Variants.Length == 0)
                    continue;
                // A keep falls during the scene: its tagged event and capped burst go out, not held, and count in the meter.
                if (!eventSaid && beatIndex == 1)
                {
                    for (int e = 0; e < 4; e++)
                        meter.Count(AutonomousChatBudget.Channel.Faction, key, now + e * 3_000);
                    eventSaid = true;
                }
                while (true)
                {
                    var decision = AutonomousBotDebates.DecideRoom(Ex.Fits(meter.PerMinute(AutonomousChatBudget.Channel.Faction, key, now), band.Ceiling), waits);
                    if (decision == AutonomousBotDebates.RoomDecision.Speak)
                        break;
                    if (decision == AutonomousBotDebates.RoomDecision.Cut)
                    {
                        cut = true;
                        break;
                    }
                    waits++;
                    now += AutonomousBotDebates.RoomRetryMs;
                }
                if (cut)
                    break;
                waits = 0;
                meter.Count(AutonomousChatBudget.Channel.Faction, key, now);
                said.Add(beat.Role);
                beatIndex++;
                now += AutonomousBotDebates.BeatDelayMs(beat.Variants[0]);
            }
            Assert.That(cut, Is.False, $"{scene.Id} was cut at a full channel");
            Assert.That(said, Is.EqualTo(scene.Beats.Where(beat => !beat.IsPause && beat.Variants.Length > 0).Select(beat => beat.Role).ToList()),
                $"{scene.Id}: every line, in order");
            if (scene.Exit.Length > 0)
            {
                dunces++;
                Assert.That(said, Does.Contain(scene.Exit), $"{scene.Id}: the dunce spoke, so its exit runs at the end");
            }
            else if (AutonomousBotStorylines.IsStory(scene))
                stories++;
            else
                debates++;
        }
        Assert.That(dunces, Is.GreaterThanOrEqualTo(6), "the dunce scenes");
        Assert.That(stories, Is.GreaterThan(0), "storyline episodes");
        Assert.That(debates, Is.GreaterThan(0), "debates");
    }

    [Test]
    public void AReactionAnyPathSaysIsHeardOnceInTenMinutes()
    {
        var window = new Ex.ShortWindow();
        Assert.That(window.TryUse("faction:1", "lol", 0), Is.True, "a plan claims it");
        Assert.That(window.SayOrHold("faction:1", "lol", 3_000), Is.True, "and says it");
        Assert.That(window.SayOrHold("faction:1", "LOL", 200_000), Is.False, "another path's \"lol\" three minutes later is held");
        Assert.That(window.SayOrHold("faction:1", "lol", 603_000), Is.True, "ten minutes after it was said");
        Assert.That(Ex.IsReaction("been there"), Is.True);
        Assert.That(Ex.IsReaction("caer benowyc fell, mids have it"), Is.False);
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

    /// <summary>
    /// Owner's PC, RC3 2026-11-09: with the evening's memory on, every longer death opener was told once and then each death
    /// story opened with "dead. again.", seven times in half an hour from different bots. The teller's short line now keeps
    /// the ten-minute window too, and a story whose opener the channel just heard is not told.
    /// </summary>
    [Test]
    public void ATellersShortOpenerIsNotHeardTwiceInTenMinutes()
    {
        var random = new Random(7);
        var window = new Ex.ShortWindow();
        var memory = new Field.Memory();
        string lane = NewLane();
        long now = 0;
        var heard = new Dictionary<string, long>();
        int told = 0;
        for (int i = 0; i < 200; i++, now += 30_000)
        {
            List<Field.Step> story = Field.Story("death", Facts(i, "Scout", 30), Facts(i + 1, "Cleric", 31), Facts(i + 2, "Wizard", 33), random,
                memory, text => window.Fresh(lane, text, now));
            long at = now;
            story = AutonomousBotChatCoordinator.KeepFreshShorts(story, text => window.TryUse(lane, text, at));
            if (story.Count > 0)
            {
                told++;
                Assert.That(story[0].Who, Is.EqualTo(0), "a story starts with its teller");
            }
            foreach (Field.Step step in story.Where(s => Ex.IsShort(s.Text)))
            {
                string norm = Ex.Normalize(step.Text);
                Assert.That(!heard.TryGetValue(norm, out long last) || now - last >= Ex.ShortWindow.WindowMs, Is.True,
                    $"\"{step.Text}\" again {(now - last) / 1000} s later");
                heard[norm] = now;
            }
        }
        Assert.That(told, Is.GreaterThan(5), "stories are still told");
    }

    [Test]
    public void AStoryWhoseOpenerIsStaleIsNotToldAndAStaleRiffIsLeftOut()
    {
        var steps = new List<Field.Step> { new(0, "dead. again.", 0), new(1, "lol rip", 2_000), new(0, "third time tonight in lyonesse", 4_000) };
        Assert.That(AutonomousBotChatCoordinator.KeepFreshShorts(steps, text => Ex.Normalize(text) != "dead again"), Is.Empty);
        List<Field.Step> kept = AutonomousBotChatCoordinator.KeepFreshShorts(steps, text => Ex.Normalize(text) != "lol rip");
        Assert.That(kept.Select(s => s.Text), Is.EqualTo(new[] { "dead. again.", "third time tonight in lyonesse" }));
    }

    [TestCase("thrawn ogre miner", false, "thrawn ogre miner")]
    [TestCase("  apple fly ", false, "apple fly")]
    [TestCase("Sneakywarrior", false, null)]
    [TestCase("Summoner Roesia", false, null)]
    [TestCase("Bledmeer Jarl", false, null)]
    [TestCase("thrawn ogre miner", true, null)]
    [TestCase("", false, null)]
    [TestCase(null, false, null)]
    public void OnlyAKindOfMonsterIsTalkedAboutAsOne(string target, bool playerLed, string kind) =>
        Assert.That(Field.MonsterKind(target, playerLed), Is.EqualTo(kind));

    /// <summary>
    /// Owner's PC, RC2 and RC3 (2026-11-08/09): a bot killed in the frontier told "siuses in murdaigean hate me" or "train of
    /// sneakywarriors, im dead". A frontier death is told as one, with no monster in it.
    /// </summary>
    [Test]
    public void AFrontierDeathIsToldAsOneWithNoMonsterInIt()
    {
        var random = new Random(3);
        for (int i = 0; i < 300; i++)
        {
            Field.Facts teller = Facts(i, "Scout", 50, "Emain Macha") with { InRvR = true, Monster = null };
            List<Field.Step> story = Field.Story("death", teller, Facts(i + 1, "Cleric", 50), Facts(i + 2, "Wizard", 50), random);
            Assert.That(story, Is.Not.Empty);
            Assert.That(story[0].Who, Is.EqualTo(0));
            foreach (Field.Step step in story)
            {
                Assert.That(step.Text, Does.Not.Contain("adds").And.Not.Contain("skeleton").And.Not.Contain("camp"), step.Text);
                Assert.That(step.Text, Does.Not.Contain("pulled"), step.Text);
            }
        }
    }

    [Test]
    public void APveDeathStillNamesItsKindOfMonster()
    {
        var random = new Random(5);
        bool named = false;
        for (int i = 0; i < 100 && !named; i++)
        {
            List<Field.Step> story = Field.Story("death", Facts(i, "Scout", 30), Facts(i + 1, "Cleric", 31), null, random);
            named = story.Any(step => step.Text.Contains("skeletons"));
        }
        Assert.That(named, Is.True, "\"large skeletons\" is told in some PvE death story");
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

    private sealed record Plan(long Id, string Feature, List<Ex.Line> Lines, SimBot[] Speakers, Field.Said Said = null, SimBot[] StandIns = null);

    /// <summary>What a simulated evening said, by exchange, and the /faction band it was held to.</summary>
    private sealed class Evening
    {
        public readonly List<string> Log = [];
        public readonly List<(long At, long Ex, int Index, string Role, string Speaker, string Feature, string Text)> Said = [];
        public readonly Dictionary<long, Plan> Plans = [];
        /// <summary>The living-world features that decided to speak (a spammer, a rumour, a roleplayer's line), and those given up.</summary>
        public readonly List<(string Feature, long Ex, long At)> Due = [];
        public int Dropped;
        /// <summary>Questions not asked because the channel had no room for their answer (the new way only).</summary>
        public int QuestionsHeld;
        public (double Floor, double Target, double Ceiling) Band;
    }

    /// <summary>The realm the owner played: 594 bots a realm, at 8 pm.</summary>
    public const int SimRealmBots = 594;

    /// <summary>
    /// An evening of a realm's /faction on a simulated clock, with every path that writes to it: unprompted openers (through
    /// the real budget meter: field lines and written remarks), the threads and single reactions they draw, stories from deaths
    /// and wipes, nemesis and revenge lines with their bursts, keep news (an event) with its burst, debate scenes, and Realm
    /// Exchange adverts. <paramref name="fixedCode"/> false plays it the old way: every line of an exchange on its own timer with
    /// the old spacing rule, a busy speaker's thread line dropped, bursts of 3-6 (by the crowd) over 90 s that may overlap, no
    /// short-reaction window, only openers and burst lines held by the ceiling, adverts 3 minutes apart. True plays the same
    /// plans through <see cref="Ex.Run"/> and the same rule as <c>AutonomousBotChatCoordinator.BroadcastFactionAsWritten</c>:
    /// every line fits under the ceiling or isn't said, but a tagged event; debates wait for room. Both use the same seed.
    /// </summary>
    private static Evening Simulate(bool fixedCode, int seed, long lengthMs)
    {
        var random = new Random(seed);
        var evening = new Evening();
        string lane = "sim:" + (fixedCode ? "after" : "before") + ":" + seed;
        Ex.Lane laneState = Ex.LaneOf(lane);
        var shorts = new Ex.ShortWindow();
        var memory = new Field.Memory();
        var stance = new AutonomousChatStance();
        var meter = new AutonomousChatBudget.ChannelMeter();
        AutonomousChatBudget.Settings settings = AutonomousChatBudget.Current;
        var band = AutonomousChatBudget.Band(settings, AutonomousChatBudget.Channel.Faction, SimRealmBots, 20, false, 1.0);
        evening.Band = band;
        const long key = 1;
        var bots = Enumerable.Range(0, 160).Select(i => new SimBot
        {
            Id = 70_000 + i, Name = "Simbot" + (char)('a' + i % 26) + (char)('a' + i / 26 % 26), Class = Albion[(i * 7 + seed) % Albion.Length],
            Level = 5 + (i * 37 + seed) % 46, Zone = Zones[i % Zones.Length],
        }).ToList();
        bool Busy(SimBot bot, long now) => (now / 1_000 + bot.Id * 7) % 60 < 15;
        double PerMinute(long now) => meter.PerMinute(AutonomousChatBudget.Channel.Faction, key, now);
        // The old ceiling check (bursts, stories' start): the rate before the line; the new one: the rate the line would make.
        bool OldRoom(long now) => PerMinute(now) < band.Ceiling;
        bool Room(long now) => Ex.Fits(PerMinute(now), band.Ceiling);
        long debateUntil = -1, lastAdvert = -10 * 60_000_000L, lastAmbientNews = -10 * 60_000_000L, burstUntil = -1, sceneEndedAt = long.MinValue / 2;
        long baseTime = 20 * 3_600_000L;
        // AutonomousBotChatCoordinator.BroadcastFactionAsWritten: false when the line doesn't fit and isn't a tagged event.
        bool Say(long now, SimBot bot, string feature, long ex, int index, string role, string text)
        {
            if (bot == null)
                return false;
            if (fixedCode && Ex.SinkHold(new Ex.Source(feature, ex, index, role), now < debateUntil, Room(now), now, laneState.LastAt, sceneEndedAt) != null)
                return false;
            if (fixedCode && Ex.IsReaction(text) && !shorts.SayOrHold(lane, text, now))
                return false;
            Ex.NoteLine(lane, bot.Name, now);
            meter.Count(AutonomousChatBudget.Channel.Faction, key, now);
            evening.Said.Add((now, ex, index, role, bot.Name, feature, text));
            long t = baseTime + now;
            evening.Log.Add($"11/08 {t / 3_600_000 % 24:00}:{t / 60_000 % 60:00}:{t / 1_000 % 60:00} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=bot ch=faction " +
                            $"realm=Albion from=\"{bot.Name}\" via=Sim src={feature} ex={ex} i={index} role={role} cls=\"{bot.Class}\" lvl={bot.Level} " +
                            $"cap={band.Ceiling.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} text=\"{text.Replace('"', '\'')}\"");
            return true;
        }
        SimBot Pick(SimBot not, Func<SimBot, bool> fits = null)
        {
            for (int tries = 0; tries < 300; tries++)
            {
                SimBot bot = bots[random.Next(bots.Count)];
                if (bot != not && (fits == null || fits(bot)))
                    return bot;
            }
            return null;
        }
        var legacy = new List<(long Due, Plan Plan, int Line, int Waits)>();
        var spoke = new HashSet<(long, int)>();
        var runs = new List<(Ex.Run Run, Plan Plan)>();
        var beatsDue = new List<(long Due, Plan Plan, int Line, int Waits)>();
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
            var rules = plan.Feature is "burst" or "event" ? Ex.Rules.Faction with { ReactionWithinMs = 44_000 } : Ex.Rules.Faction;
            runs.Add((new Ex.Run(plan.Id, plan.Feature, lane, laneState, plan.Lines, now, rules, firstIndex), plan));
            Ex.Touch(laneState, plan.Id, now);
        }
        void Burst(long now, SimBot teller, long ex, string trigger, string[] pool)
        {
            int size = AutonomousChatBursts.Size(settings, SimRealmBots, random);
            int spread = Math.Max(5, settings.BurstSpreadS) * 1_000;
            if (fixedCode)
            {
                size = AutonomousChatBursts.CappedSize(trigger, size, SimRealmBots, random);
                spread = AutonomousChatBursts.SpreadMs(trigger, settings);
                // AutonomousChatBursts.TryClaim, on the simulated clock.
                if (now < burstUntil)
                    return;
                burstUntil = now + spread + 5_000;
            }
            int[] delays = AutonomousChatBursts.Delays(size, spread, random);
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
                Start(now, new Plan(ex, fixedCode && AutonomousChatBursts.IsEvent(trigger) ? "event" : "burst", lines, speakers.ToArray()));
        }
        string[] boasts = ["nice going", "well earned", "show off", "gz", "nice!", "well played", "lucky you", "now thats a good night", "grats grats", "way to go",
            "ha, nice, now find something meaner", "sweet", "woot, nice"];
        string[] complaints = ["oof", "ugh, that sucks", "that stinks", "bad luck", "been there", "rough night huh", "the dice hate us all", "hang in there",
            "thats annoying", "bleh, been there", "argh, hate that"];
        string[] news = ["again??", "omw", "who has it now", "ugh, not again", "need to take it back", "where is everyone", "porting", "we'll get it back"];
        string[] remarks = ["cant believe its tuesday already", "first kill after login never drops anything", "the frontier was busy last night",
            "love the music in this zone", "bwgans are the worst", "anyone else lag in camelot?", "need more bag space lol"];
        string[] reactions = ["lol", "heh", "true", "ha", "lol true", "true true"];
        // The living-world features that decide to speak, on a fixed clock (a gold spammer every 40 minutes, not during a scene;
        // a rumour every 20, whenever; a roleplayer's line every 15, not during a scene). The new way they wait for their turn
        // (Ex.AfterTry: every 3 s, the roleplayer every 5-10 s, given up after three minutes outside a scene); each must speak.
        var dues = new List<(string Feature, long Ex, long Since, long NextTry, SimBot Speaker, string Text)>();
        var wanted = new HashSet<string>();
        var spammer = new SimBot { Id = 99_999, Name = "Xkqhoaab", Class = "", Level = 1, Zone = Zones[0] };
        string Stamp(long now)
        {
            long t = baseTime + now;
            return $"11/08 {t / 3_600_000 % 24:00}:{t / 60_000 % 60:00}:{t / 1_000 % 60:00}";
        }
        void Due(long now, string feature, SimBot speaker, string text)
        {
            if (dues.Any(waiting => waiting.Feature == feature))
                return;
            long ex = Ex.NewId();
            dues.Add((feature, ex, now, now, speaker, text));
            evening.Due.Add((feature, ex, now));
            evening.Log.Add($"{Stamp(now)} | INF | DOL.GS.Sim | " + Ex.DueLog(feature, eRealm.Albion, ex));
        }
        string[] beats = ["is a house worth the coin?", "a house is a home, plus storage", "its a weekly rent bill for a room u visit twice",
            "decorate it right and ur friends will visit", "my friends are in the frontier lol", "maybe a small cottage to start"];
        // From one second: the budget meter takes a line stamped 0 for an empty slot (the game's clock is never 0), so a first line
        // at 0 went uncounted and the channel ran half a line over its ceiling in the first minute (1.1.3, after RC5).
        for (long now = 1_000; now < lengthMs; now += 1_000)
        {
            bool debate = now < debateUntil;
            // Unprompted openers, through the realm's budget (AutonomousBotChatCoordinator.TryStartBudgeted): none during a debate.
            if (!debate && random.NextDouble() < 0.5 &&
                meter.TryStart(AutonomousChatBudget.Channel.Faction, key, now, band, random) is AutonomousChatBudget.Verdict.Speak or AutonomousChatBudget.Verdict.SpeakUnderFloor)
            {
                SimBot starter = bots[random.Next(bots.Count)];
                if (random.NextDouble() < 0.25)
                {
                    // A written remark, and now and then one fitting reaction (WrittenReaction, 30%).
                    long ex = Ex.NewId();
                    if (Say(now, starter, "ambient", ex, 0, "opener", remarks[random.Next(remarks.Length)]))
                    {
                        string reaction = reactions[random.Next(reactions.Length)];
                        if (random.NextDouble() < 0.3 && (!fixedCode || shorts.TryUse(lane, reaction, now)))
                            Start(now, new Plan(ex, "reaction", [new Ex.Line(1, reaction, 2_500 + random.Next(2_000), "reactor", Optional: true, Reaction: true)],
                                [starter, Pick(starter)]));
                    }
                }
                // The new way keeps each bot's story (AutonomousChatStance.Shared in the live code).
                else if (Field.Line("faction", starter.Facts, memory, random, now, fixedCode ? stance : null) is { } said)
                {
                    long ex = Ex.NewId();
                    // 1.1.3, after RC5 (AutonomousBotChatCoordinator.TryField): a question is asked only when the channel has room
                    // for its answer too.
                    if (fixedCode && Field.WantsAnswer(said) && (Ex.RunningCount(lane, now) >= Ex.MaxRunning || !Ex.Fits(PerMinute(now) + 0.5, band.Ceiling)))
                        evening.QuestionsHeld++;
                    else if (Say(now, starter, "field", ex, 0, "asker", said.Text) && said.Answers != null && (!fixedCode || Ex.RunningCount(lane, now) < Ex.MaxRunning))
                    {
                        Func<SimBot, bool> fits = fixedCode ? bot => Field.CanGiveAnswer(said, bot.Class, bot.Level, Field.Fills(bot.Class, "healer")) : null;
                        SimBot answerer = Pick(starter, fits), third = Pick(starter, bot => bot != answerer && (fits == null || fits(bot)));
                        List<Field.Step> steps = null;
                        for (int attempt = 0; attempt < (fixedCode ? 3 : 1); attempt++)
                        {
                            steps = Field.Thread(said, starter.Facts, answerer?.Facts, third?.Facts, memory, random, now);
                            if (!fixedCode || steps.All(st => st.Who == 0 || !Ex.IsReaction(st.Text) || shorts.Fresh(lane, st.Text, now)))
                                break;
                            steps = null;
                        }
                        if (fixedCode && steps != null)
                            foreach (Field.Step st in steps.Where(st => st.Who != 0 && Ex.IsReaction(st.Text)))
                                shorts.TryUse(lane, st.Text, now);
                        // A busy answerer's answer is said by another bot that can answer, unless it is about the answerer itself
                        // (AutonomousBotChatCoordinator.ScheduleFieldThread).
                        SimBot[] standIns = null;
                        if (fixedCode && steps != null && answerer != null && !steps.Any(st => st.Who == 1 && Field.MentionsSelf(st.Text, answerer.Facts)))
                            standIns = Enumerable.Range(0, 4)
                                .Select(_ => Pick(starter, bot => bot != answerer && bot != third && fits(bot) &&
                                                                  Field.Fills(bot.Class, "healer") == Field.Fills(answerer.Class, "healer") &&
                                                                  (said.SameClass == null || bot.Class == answerer.Class)))
                                .Where(bot => bot != null).Distinct().ToArray();
                        if (steps is { Count: > 0 })
                            Start(now, new Plan(ex, "thread", Ex.FromSteps(steps, ["asker", "answerer", "third"], step => step.Who == 2), [starter, answerer, third], said,
                                standIns));
                    }
                }
            }
            // A story from a death or a wipe about every two minutes (TellStory: below the ceiling, not during a debate).
            if (random.NextDouble() < 1 / 120.0 && !debate && (fixedCode ? Room(now) : OldRoom(now)) && (!fixedCode || Ex.RunningCount(lane, now) < Ex.MaxRunning))
            {
                SimBot teller = bots[random.Next(bots.Count)], mate = Pick(teller), third = Pick(teller, bot => bot != mate);
                string kind = random.NextDouble() < 0.75 ? "death" : "wipe";
                long at = now;
                List<Field.Step> story = Field.Story(kind, teller.Facts, mate.Facts, third.Facts, random, memory,
                    fixedCode ? text => shorts.Fresh(lane, text, at) : null);
                if (fixedCode)
                    story = story.Where(step => step.Who == 0 || !Ex.IsShort(step.Text) || shorts.TryUse(lane, step.Text, at)).ToList();
                if (story.Count > 0)
                    Start(now, new Plan(Ex.NewId(), "story", Ex.FromSteps(story.Select(st => st with { AtMs = st.AtMs + 2_000 }), ["teller", "riff", "riff"],
                        step => step.Who == 2), [teller, mate, third]), firstIndex: 0);
            }
            // A nemesis or a revenge about every three minutes, and its burst (none when the line itself was held).
            if (random.NextDouble() < 1 / 180.0 && !debate)
            {
                SimBot teller = bots[random.Next(bots.Count)];
                bool revenge = random.NextDouble() < 0.5;
                long ex = Ex.NewId();
                if (Say(now, teller, revenge ? "revenge" : "nemesis", ex, 0, "teller",
                        revenge ? "got that dwarf berserker. we are even" : "same norseman spiritmaster got me at caledon again"))
                    Burst(now, teller, ex, revenge ? "revenge" : "nemesis", revenge ? boasts : complaints);
            }
            // A keep lost about every 25 minutes: the realm's event, with its burst.
            if (random.NextDouble() < 1 / 1_500.0 && !debate)
            {
                SimBot teller = bots[random.Next(bots.Count)];
                long ex = Ex.NewId();
                if (Say(now, teller, fixedCode ? "event" : "news", ex, 0, "teller", "caer benowyc fell, mids have it"))
                {
                    lastAmbientNews = now;
                    Burst(now, teller, ex, "keep_lost", news);
                }
            }
            // A debate scene about every twenty minutes: six beats seven seconds apart; it owns the channel while it runs, and
            // (the new way) a beat waits for room, three seconds at a time, up to two minutes.
            if (!debate && random.NextDouble() < 1 / 1_200.0)
            {
                long ex = Ex.NewId();
                SimBot a = bots[random.Next(bots.Count)], b = Pick(a), c = Pick(a, bot => bot != b);
                SimBot[] cast = [a, b, c];
                var lines = beats.Select((text, i) => new Ex.Line(i % 3, text, 7_000, i % 3 == 0 ? "A" : i % 3 == 1 ? "B" : "C")).ToList();
                var plan = new Plan(ex, "debate", lines, cast);
                evening.Plans[ex] = plan;
                beatsDue.Add((now + 7_000, plan, 0, 0));
                debateUntil = long.MaxValue;
            }
            for (int k = 0; k < beatsDue.Count; k++)
            {
                var (due, plan, index, waits) = beatsDue[k];
                if (due > now)
                    continue;
                beatsDue.RemoveAt(k--);
                if (fixedCode && !Room(now))
                {
                    if (waits < AutonomousBotDebates.MaxRoomWaits)
                        beatsDue.Add((now + AutonomousBotDebates.RoomRetryMs, plan, index, waits + 1));
                    else
                        debateUntil = sceneEndedAt = now;
                    continue;
                }
                Ex.Line line = plan.Lines[index];
                Say(now, plan.Speakers[line.Who], "debate", plan.Id, index, line.Role, line.Text);
                if (index + 1 < plan.Lines.Count)
                    beatsDue.Add((now + 7_000, plan, index + 1, 0));
                else
                    debateUntil = sceneEndedAt = now + 2_000;
            }
            if (now % 2_400_000 == 1_500_000)
                wanted.Add("spammer");
            if (now % 1_200_000 == 420_000)
                wanted.Add("rumour");
            if (now % 900_000 == 660_000)
                wanted.Add("roleplayer");
            if (wanted.Remove("rumour"))
                Due(now, "rumour", bots[random.Next(bots.Count)], "heard grimwold is up again in black mtns");
            if (now >= debateUntil && wanted.Remove("spammer"))
                Due(now, "spammer", spammer, "WTS cheap plat w w w . F A S T G 0 L D . C 0 M");
            if (now >= debateUntil && wanted.Remove("roleplayer"))
                Due(now, "roleplayer", bots[random.Next(bots.Count)], "hail, good folk! the road to caer sursbrooke is safe this eve");
            for (int k = 0; k < dues.Count; k++)
            {
                var pending = dues[k];
                if (pending.NextTry > now)
                    continue;
                bool dueSaid = Say(now, pending.Speaker, pending.Feature, pending.Ex, 0, pending.Feature == "rumour" ? "teller" : pending.Feature, pending.Text);
                string dueHeld = dueSaid ? null : now < debateUntil ? "scene" : "ceiling";
                Ex.DueStep dueStep = Ex.AfterTry(dueSaid, dueHeld, pending.Since, sceneEndedAt, now);
                if (dueStep == Ex.DueStep.Retry)
                {
                    dues[k] = (pending.Feature, pending.Ex, pending.Since,
                        now + (pending.Feature == "roleplayer" ? 5_000 + random.Next(5_000) : Ex.DueRetryMs), pending.Speaker, pending.Text);
                    continue;
                }
                dues.RemoveAt(k--);
                if (dueStep == Ex.DueStep.GiveUp)
                {
                    evening.Dropped++;
                    evening.Log.Add($"{Stamp(now)} | INF | DOL.GS.Sim | " + Ex.DroppedLog(pending.Feature, eRealm.Albion, pending.Ex, now - pending.Since, dueHeld));
                }
            }
            // A bot lists an item about every two minutes and advertises it 70% of the time (BotBrain, TryAdvertiseExchangeItem).
            if (random.NextDouble() < 1 / 120.0 && random.NextDouble() < 0.7 && !debate)
            {
                bool ok = now - Math.Max(lastAdvert, lastAmbientNews) >= 180_000 &&
                          (!fixedCode || now - lastAdvert >= AutonomousBotChatCoordinator.ExchangeAdvertRestMs && PerMinute(now) < band.Target);
                if (ok && Say(now, bots[random.Next(bots.Count)], "exchange_ad", Ex.NewId(), 0, "seller", "anyone want a ghostly cloak? make me an offer"))
                    lastAdvert = now;
            }
            // Due lines, the old way.
            for (int k = 0; k < legacy.Count; k++)
            {
                var (due, plan, index, waits) = legacy[k];
                if (due > now)
                    continue;
                Ex.Line line = plan.Lines[index];
                SimBot bot = line.Who < plan.Speakers.Length ? plan.Speakers[line.Who] : null;
                // The old spacing rule held thread and story lines (PlayThread, PlayStory); bursts had none.
                if (bot != null && plan.Feature is "thread" or "story" && waits < 3 && now - laneState.LastAt < 2_000)
                {
                    legacy[k] = (now + 2_000 + random.Next(2_000), plan, index, waits + 1);
                    continue;
                }
                legacy.RemoveAt(k--);
                // PlayThread's Schedule dropped a line whose speaker was fighting; a burst's line held at the ceiling or in a debate.
                if (bot == null || plan.Feature == "thread" && Busy(bot, now) || plan.Feature == "burst" && (!OldRoom(now) || now < debateUntil))
                    continue;
                Say(now, bot, plan.Feature, plan.Id, (plan.Feature == "story" ? 0 : 1) + index, line.Role, line.Text);
            }
            // Due lines, the new way: busy speakers, a debate and a full channel all wait (an event's lines but for the channel).
            for (int k = 0; k < runs.Count; k++)
            {
                var (run, plan) = runs[k];
                if (run.DueAt > now)
                    continue;
                long? next = run.Step(now, line =>
                    {
                        // A stand-in for a busy answerer that hasn't spoken yet (Ex.Play's standIns).
                        if (line.Who == 1 && plan.StandIns is { Length: > 0 } && !spoke.Contains((plan.Id, 1)) && plan.Speakers[1] is { } first && Busy(first, now) &&
                            plan.StandIns.FirstOrDefault(bot => !Busy(bot, now)) is { } free)
                            plan.Speakers[1] = free;
                        return line.Who < plan.Speakers.Length && plan.Speakers[line.Who] is { } bot && now >= debateUntil &&
                               (plan.Feature == "event" || Room(now)) && (plan.Feature == "story" && line.Who == 0 || !Busy(bot, now));
                    },
                    line => line.Who < plan.Speakers.Length ? plan.Speakers[line.Who]?.Name : null,
                    (line, index) =>
                    {
                        if (!Say(now, plan.Speakers[line.Who], plan.Feature, plan.Id, index, line.Role, line.Text))
                            return false;
                        spoke.Add((plan.Id, line.Who));
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
        int BurstsOver(Evening e, string trigger, int cap) => e.Said.Where(s => s.Feature is "burst" or "event" && s.Index > 0)
            .GroupBy(s => s.Ex).Count(g => e.Said.Any(t => t.Ex == g.Key && t.Index == 0 && t.Feature == trigger) && g.Count() > cap);
        int ReactionRepeats(Evening e)
        {
            int repeats = 0;
            var last = new Dictionary<string, long>();
            foreach (var s in e.Said.Where(s => s.Role is "riff" or "reactor" || s.Role == "answerer" && Ex.IsReaction(s.Text)).OrderBy(s => s.At))
            {
                if (last.TryGetValue(Norm(s.Text), out long at) && s.At - at < Ex.ShortWindow.WindowMs)
                    repeats++;
                last[Norm(s.Text)] = s.At;
            }
            return repeats;
        }
        // The /faction rate as the server measures it (over two minutes), each second, of the lines the ceiling holds: all but
        // tagged events (the old way had no tags, so all of its lines count).
        (double Max, int SecondsOver) Rate(Evening e)
        {
            long[] times = e.Said.Where(s => s.Feature != "event").Select(s => s.At).OrderBy(t => t).ToArray();
            double max = 0;
            int over = 0;
            for (long t = 120_000; t < hours3; t += 1_000)
            {
                // AutonomousChatBudget.ChannelMeter.PerMinute: the lines of the last two minutes, halved.
                int n = 0;
                foreach (long at in times)
                    if (at > t - 120_000 && at <= t)
                        n++;
                double perMinute = n / 2.0;
                max = Math.Max(max, perMinute);
                if (perMinute > e.Band.Ceiling + 1e-9)
                    over++;
            }
            return (max, over);
        }
        int Adverts(Evening e) => e.Said.Count(s => s.Feature == "exchange_ad");
        // Bot questions answered by someone else within 60 s (chat_audit.py check_unanswered: rhetorical ones aside).
        (int Asked, int Answered) Questions(Evening e)
        {
            var asked = e.Said.Where(s => s.Feature == "field" && s.Index == 0 && s.Text.TrimEnd().EndsWith('?') && !Rhetorical(s.Text) &&
                                          s.At <= hours3 - 60_000).ToList();
            return (asked.Count, asked.Count(q => e.Said.Any(r => r.Ex == q.Ex && r.Speaker != q.Speaker && r.At >= q.At && r.At - q.At <= 60_000)));
        }
        // A spammer, rumour or roleplayer that decided to speak and never did (live check of RC1: a started spammer was silent).
        int Silent(Evening e) => e.Due.Count(d => !e.Said.Any(s => s.Ex == d.Ex));
        // The most exchanges (events aside) with a line in any ten seconds of the two minutes after a debate ended (live test: 7
        // lines in 7 s). 1.1.3, after RC5: exchanges, not lines: one story or thread talking at its own pace (four lines in eight
        // seconds) is no rush of lines that waited for the scene.
        int AfterScene(Evening e)
        {
            var ends = e.Said.Where(s => s.Feature == "debate").GroupBy(s => s.Ex).Select(g => g.Max(s => s.At)).ToArray();
            var lines = e.Said.Where(s => s.Feature is not ("event" or "debate")).Select(s => (s.At, s.Ex)).ToArray();
            int most = 0;
            foreach (long end in ends)
                for (long t = end; t < end + 120_000; t += 1_000)
                    most = Math.Max(most, lines.Where(l => l.At >= t && l.At < t + 10_000).Select(l => l.Ex).Distinct().Count());
            return most;
        }
        string Line(string name, Evening e) =>
            $"{name}: {e.Said.Count} lines ({e.Said.Count / 180.0:0.00}/min; band {e.Band.Target:0.0}/{e.Band.Ceiling:0.0}; peak {Rate(e).Max:0.0}/min, " +
            $"{Rate(e).SecondsOver} s over the ceiling), out of order {OutOfOrder(e)}, role swaps {RoleSwaps(e)}, untrue answers {RoleFalse(e)}, " +
            $"thread lines after a missing one {Orphans(e)}, personal bursts over two {BurstsOver(e, "nemesis", 2) + BurstsOver(e, "revenge", 2)}, " +
            $"event bursts over three {BurstsOver(e, "event", 3) + BurstsOver(e, "news", 3)}, most exchanges in 10 s after a debate {AfterScene(e)}, reaction repeats in 10 min {ReactionRepeats(e)}, exchange adverts {Adverts(e)}, " +
            $"features due {e.Due.Count} (spammer {e.Due.Count(d => d.Feature == "spammer")}, rumour {e.Due.Count(d => d.Feature == "rumour")}, " +
            $"roleplayer {e.Due.Count(d => d.Feature == "roleplayer")}), silent {Silent(e)}, given up {e.Dropped}, " +
            $"questions answered in 60 s {Questions(e).Answered}/{Questions(e).Asked}, held for room {e.QuestionsHeld}";
        TestContext.Progress.WriteLine("CHAT_SIM " + Line("before", before));
        TestContext.Progress.WriteLine("CHAT_SIM " + Line("after", after));

        Assert.That(OutOfOrder(before), Is.GreaterThan(0), "the simulation reproduces the old bug");
        Assert.That(OutOfOrder(after), Is.Zero, Line("after", after));
        Assert.That(RoleSwaps(after), Is.Zero);
        Assert.That(RoleFalse(after), Is.Zero);
        Assert.That(Orphans(after), Is.Zero);
        Assert.That(BurstsOver(after, "nemesis", 2) + BurstsOver(after, "revenge", 2) + BurstsOver(after, "event", 3), Is.Zero);
        Assert.That(ReactionRepeats(after), Is.Zero);
        Assert.That(Adverts(after), Is.LessThanOrEqualTo(12), "one in 15 minutes at most");
        // Every spammer, rumour and roleplayer line that was due was said.
        Assert.That(after.Due.Count(d => d.Feature == "spammer"), Is.GreaterThanOrEqualTo(4));
        Assert.That(after.Due.Count(d => d.Feature == "rumour"), Is.GreaterThanOrEqualTo(8));
        Assert.That(after.Due.Count(d => d.Feature == "roleplayer"), Is.GreaterThanOrEqualTo(10));
        Assert.That(Silent(after), Is.Zero, Line("after", after));
        // After a debate the lines that waited don't all land at once: an opener at most every ten seconds for two minutes.
        // (an opener and its own answers may share ten seconds).
        Assert.That(AfterScene(after), Is.LessThanOrEqualTo(3), Line("after", after));
        Assert.That(AfterScene(after), Is.LessThanOrEqualTo(AfterScene(before)), "never a bigger rush than the old way");
        // Every line but a tagged event fits under the ceiling.
        Assert.That(Rate(after).SecondsOver, Is.Zero, Line("after", after));
        Assert.That(Rate(after).Max, Is.LessThanOrEqualTo(after.Band.Ceiling), Line("after", after));
        // Coherent, not thin: the evening stays above the realm's target (the 1.1.2 model's busy-peak design) on average.
        Assert.That(after.Said.Count / 180.0, Is.GreaterThan(after.Band.Target), Line("after", after));
        // 1.1.3, after RC5 (owner's PC: 71 of 106 bot questions unanswered): at least four in five questions get an answer
        // within a minute.
        Assert.That(Questions(after).Asked, Is.GreaterThan(50), Line("after", after));
        Assert.That(Questions(after).Answered, Is.GreaterThanOrEqualTo(0.8 * Questions(after).Asked), Line("after", after));
    }

    /// <summary>A question that wants no answer (chat_audit.py RHETORICAL): one word, an opening laugh or groan, the pride tags.</summary>
    private static bool Rhetorical(string text)
    {
        string t = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " "), @"\s+", " ").Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(t, @"^(?:\S+|(?:lol|heh|haha|ugh|bleh|argh|ok|so|well)\b.*|only \d+ \w+|.*\bnot bad|.*\bkeeps not bad)$");
    }
}
