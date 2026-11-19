using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Budget = DOL.GS.AutonomousChatBudget;
using Ex = DOL.GS.AutonomousChatExchange;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// The realm's /faction pace with scenes (1.2, sweep 7c on the owner's PC, build 9a173f4). In under three minutes online the
/// player heard 13 /faction lines a minute (11.9 and 14.6 in the two windows) against the owner's 5-7 ("5-7 is the right
/// pace", 2026-11-18): the storyline story_mistell_1 (18 lines) and the tale tale_govannon_alb (7 lines) played their beats
/// 4-12 s apart, and a login after a quiet spell let six field questions through in 28 s. Tests only.
/// </summary>
[TestFixture]
public class UT_FactionPace
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

    private static Budget.Settings Settings => Budget.Parse(Budget.BuiltIn());

    [Test]
    public void TheBuiltInBudgetCarriesTheOwnersPace()
    {
        (double max, double scene) = Budget.FactionPace(Settings, 2.0);
        Assert.That(max, Is.EqualTo(7.0));
        Assert.That(scene, Is.EqualTo(6.0));
        Assert.That(Budget.SceneLineGapMs(scene), Is.EqualTo(10_000));
        // The launcher's "How much bots talk" keeps its meaning: calmer at 1, busier at 3.
        Assert.That(Budget.FactionPace(Settings, 1.0).Max, Is.EqualTo(3.5));
        Assert.That(Budget.FactionPace(Settings, 3.0).Max, Is.EqualTo(10.5));
        // An older copy of chat-budget.json without "pace" gets the same defaults.
        Assert.That(Budget.FactionPace(Budget.Parse("{}"), 2.0), Is.EqualTo((7.0, 6.0)));
    }

    [Test]
    public void AnOpenerWaitsForRoomForItselfAndItsAnswer()
    {
        Assert.That(Budget.OpenerFitsPace(5, 0, 7.0), Is.True, "5 + the opener + its answer = 7");
        Assert.That(Budget.OpenerFitsPace(6, 0, 7.0), Is.False);
        Assert.That(Budget.OpenerFitsPace(4, 1, 7.0), Is.True, "a running thread's next line is counted");
        Assert.That(Budget.OpenerFitsPace(4, 2, 7.0), Is.False);
        var opener = new Ex.Source("field", 9, 0, "asker");
        var answer = new Ex.Source("thread", 9, 1, "answerer");
        var evt = new Ex.Source("event", 7, 0, "herald");
        var player = new Ex.Source("player", 3, 0, "answerer");
        long never = long.MinValue / 2, now = 1_000_000;
        Assert.That(Ex.SinkHold(opener, false, true, now, now - 20_000, never, openerFitsPace: false), Is.EqualTo("pace"));
        Assert.That(Ex.SinkHold(null, false, true, now, now - 20_000, never, openerFitsPace: false), Is.EqualTo("pace"), "an untagged line is an opener");
        Assert.That(Ex.SinkHold(answer, false, true, now, now - 20_000, never, openerFitsPace: false), Is.Null, "an answer is never held by the pace");
        Assert.That(Ex.SinkHold(evt, false, false, now, now - 20_000, never, openerFitsPace: false), Is.Null, "a realm event goes");
        Assert.That(Ex.SinkHold(player, false, false, now, now - 20_000, never, openerFitsPace: false), Is.Null, "a line to the player goes");
    }

    [Test]
    public void ASceneIsSlowedToThePaceNeverCut()
    {
        (double, double) pace = Budget.FactionPace(Settings, 2.0);
        long now = 1_000_000;
        // A quiet channel: a scene (a live keep or relic scene too) says its first line at once.
        Assert.That(Budget.ScenePacedAt(now, long.MinValue / 2, 0, pace), Is.EqualTo(now));
        // Ten seconds after the channel's last line, whatever said it.
        Assert.That(Budget.ScenePacedAt(now, now - 4_000, 3, pace), Is.EqualTo(now + 6_000));
        Assert.That(Budget.ScenePacedAt(now, now - 12_000, 3, pace), Is.EqualTo(now));
        // A full minute (an event line came in between): try again in a few seconds.
        Assert.That(Budget.ScenePacedAt(now, now - 12_000, 7, pace, 3_000), Is.EqualTo(now + 3_000));
    }

    [Test]
    public void ABroadcastQuestionIsAnsweredWhereThePlayerHearsIt()
    {
        Assert.That(AutonomousBotChatCoordinator.BroadcastAnswerChannel(true, true), Is.EqualTo(AutonomousBotChatCoordinator.BroadcastAnswerWay.Say));
        Assert.That(AutonomousBotChatCoordinator.BroadcastAnswerChannel(false, true), Is.EqualTo(AutonomousBotChatCoordinator.BroadcastAnswerWay.Broadcast),
            "sweep 7c: before, an answerer beside an asker across the capital from the player could not speak at all");
        Assert.That(AutonomousBotChatCoordinator.BroadcastAnswerChannel(false, false), Is.EqualTo(AutonomousBotChatCoordinator.BroadcastAnswerWay.None));
        long now = 10_000_000, never = long.MinValue / 2;
        var say = AutonomousBotChatCoordinator.BroadcastAnswerWay.Say;
        Assert.That(AutonomousBotChatCoordinator.TakesBroadcastAnswerTurn(true, say, never, now), Is.True);
        Assert.That(AutonomousBotChatCoordinator.TakesBroadcastAnswerTurn(false, say, never, now), Is.False,
            "sweep 7c: a question nobody near could answer no longer uses up the region's next three minutes");
        Assert.That(AutonomousBotChatCoordinator.TakesBroadcastAnswerTurn(true, AutonomousBotChatCoordinator.BroadcastAnswerWay.None, never, now), Is.False);
        Assert.That(AutonomousBotChatCoordinator.TakesBroadcastAnswerTurn(true, say, now - 60_000, now), Is.False, "one a region in three minutes");
        Assert.That(AutonomousBotChatCoordinator.TakesBroadcastAnswerTurn(true, say, now - 180_000, now), Is.True);
    }

    // ---- the evening -----------------------------------------------------------------------------------------------------

    private sealed class Evening
    {
        public readonly List<(long At, string Kind)> Lines = new();
        public int SceneBeats, SceneLinesSaid;
        public readonly List<(long First, long Last, int Lines)> Scenes = new();
        public long Start, End;

        public IEnumerable<long> Times(Func<string, bool> kind) => Lines.Where(line => kind(line.Kind)).Select(line => line.At);

        /// <summary>The most lines of these kinds in any 60 s.</summary>
        public int MaxInAMinute(Func<string, bool> kind)
        {
            long[] at = Times(kind).OrderBy(t => t).ToArray();
            int most = 0;
            for (int i = 0, j = 0; i < at.Length; i++)
            {
                while (at[i] - at[j] >= 60_000)
                    j++;
                most = Math.Max(most, i - j + 1);
            }
            return most;
        }

        public double PerMinute(Func<string, bool> kind) => Times(kind).Count() * 60_000.0 / (End - Start);
    }

    private static bool NotEvent(string kind) => kind != "event";

    /// <summary>
    /// One realm's /faction for an hour with the player online from the first second (a login after a quiet spell, as in the
    /// second window of 22:23:44), 800 bots a realm at prime time and bot_voices_chat_rate 2 (the owner's PC: the ceiling read
    /// 17.3-18.8): the real ambient gate (ChannelMeter.TryStart) tried every second by a bot with a line (busier than the PC,
    /// where /faction openers ran at about half their target), six in ten openers answered 4-10 s later, two realm events,
    /// the storyline story_mistell_1 at minute 8 and the tale tale_govannon_alb at minute 35 played beat by beat with their
    /// real texts and typing delays (BeatDelayMs), and the real sink rule (SinkHold). With <paramref name="paced"/> off it is
    /// build 9a173f4's rules. Deterministic (a fixed seed, a simulated clock).
    /// </summary>
    private static Evening Run(bool paced, int seed = 5378)
    {
        Budget.Settings settings = Settings;
        var meter = new Budget.ChannelMeter();
        var band = Budget.Band(settings, Budget.Channel.Faction, 800, 21, false, 2.0);
        var pace = Budget.FactionPace(settings, 2.0);
        var random = new Random(seed);
        const long key = 1;
        long start = 1_000_000, end = start + 60 * 60_000L;
        var evening = new Evening { Start = start, End = end };
        var queue = new Queue<(long At, VC.Scene Scene)>(new[]
        {
            (start + 8 * 60_000L, Corpus.Scenes.Single(scene => scene.Id == "story_mistell_1")),
            (start + 35 * 60_000L, Corpus.Scenes.Single(scene => scene.Id == "tale_govannon_alb")),
        });
        long[] events = [start + 20 * 60_000L, start + 47 * 60_000L];
        var answers = new List<long>();
        VC.Scene scene = null;
        int beat = 0, sceneLines = 0;
        long sceneNext = 0, sceneFirst = 0, sceneLast = 0, sceneEnded = long.MinValue / 2;

        void Say(long now, string kind, Budget.LineKind lineKind = Budget.LineKind.Ambient)
        {
            meter.Count(Budget.Channel.Faction, key, now, lineKind);
            evening.Lines.Add((now, kind));
        }

        string NextText(int from)
        {
            for (int i = from; i < scene.Beats.Length; i++)
                if (!scene.Beats[i].IsPause && scene.Beats[i].Variants.Length > 0)
                    return scene.Beats[i].Variants[0];
            return string.Empty;
        }

        for (long now = start; now < end; now += 500)
        {
            bool fits = Ex.Fits(meter.PerMinute(Budget.Channel.Faction, key, now), band.Ceiling);
            long lastLine = meter.LastLine(Budget.Channel.Faction, key);
            if (events.Contains(now))
                Say(now, "event");
            if (scene == null && queue.Count > 0 && queue.Peek().At <= now)
            {
                scene = queue.Dequeue().Scene;
                beat = sceneLines = 0;
                sceneNext = now;
                sceneFirst = 0;
                evening.SceneBeats += scene.Beats.Count(b => !b.IsPause && b.Variants.Length > 0);
            }
            if (scene != null && now >= sceneNext)
            {
                long pacedAt = paced
                    ? Budget.ScenePacedAt(now, lastLine, meter.LinesIn(Budget.Channel.Faction, key, now, Budget.PaceWindowMs), pace)
                    : now;
                if (now < pacedAt)
                    sceneNext = pacedAt;
                else if (beat >= scene.Beats.Length)
                {
                    evening.Scenes.Add((sceneFirst, sceneLast, sceneLines));
                    scene = null;
                    sceneEnded = now;
                }
                else
                {
                    VC.Beat next = scene.Beats[beat++];
                    if (next.IsPause)
                        sceneNext = now + Math.Max(1, next.PauseSeconds) * 1_000L;
                    else if (next.Variants.Length > 0 &&
                             Ex.SinkHold(new Ex.Source("storyline", 1, beat - 1, next.Role), true, fits, now, lastLine, sceneEnded) == null)
                    {
                        Say(now, "scene");
                        evening.SceneLinesSaid++;
                        sceneLines++;
                        if (sceneFirst == 0)
                            sceneFirst = now;
                        sceneLast = now;
                        sceneNext = now + AutonomousBotDebates.BeatDelayMs(NextText(beat));
                    }
                }
            }
            for (int i = answers.Count - 1; i >= 0; i--)
            {
                if (answers[i] > now)
                    continue;
                answers.RemoveAt(i);
                if (Ex.SinkHold(new Ex.Source("thread", 2, 1, "answerer"), scene != null, fits, now, lastLine, sceneEnded) == null)
                    Say(now, "answer", Budget.LineKind.Reply);
            }
            if ((now - start) % 1_000 != 0 || scene != null)
                continue;
            Budget.Verdict verdict = meter.TryStart(Budget.Channel.Faction, key, now, band, random);
            if (verdict is not (Budget.Verdict.Speak or Budget.Verdict.SpeakUnderFloor))
                continue;
            bool fitsPace = !paced || Budget.OpenerFitsPace(meter.LinesIn(Budget.Channel.Faction, key, now, Budget.PaceWindowMs), answers.Count, pace.Max);
            if (Ex.SinkHold(new Ex.Source("field", 2, 0, "asker"), false, fits, now, lastLine, sceneEnded, fitsPace) != null)
                continue;
            Say(now, "ambient");
            if (random.NextDouble() < 0.6)
                answers.Add(now + 4_000 + random.Next(6_000));
        }
        return evening;
    }

    [Test]
    public void AnEveningWithAStorylineAndATaleStaysWithinFiveToSevenAMinute()
    {
        Evening before = Run(paced: false), after = Run(paced: true);
        TestContext.Out.WriteLine($"before: {before.PerMinute(NotEvent):0.00}/min, busiest minute {before.MaxInAMinute(NotEvent)}, scenes " +
                                  string.Join(", ", before.Scenes.Select(s => $"{s.Lines} lines in {(s.Last - s.First) / 1000} s")));
        TestContext.Out.WriteLine($"after:  {after.PerMinute(NotEvent):0.00}/min, busiest minute {after.MaxInAMinute(NotEvent)}, scenes " +
                                  string.Join(", ", after.Scenes.Select(s => $"{s.Lines} lines in {(s.Last - s.First) / 1000} s")));

        // Build 9a173f4's rules: the scenes alone ran over the owner's pace (the PC: 12-15 a minute with them).
        Assert.That(before.Scenes, Has.Count.EqualTo(2));
        Assert.That(before.Scenes.Max(s => s.Lines * 60_000.0 / Math.Max(1, s.Last - s.First)), Is.GreaterThan(7.0), "9a173f4: a scene over 7 a minute");
        Assert.That(before.MaxInAMinute(NotEvent), Is.GreaterThan(7), "9a173f4: a minute over 7");

        // Now: within 5-7 over the evening, no minute over 7 (a realm event, exempt by design, aside), scenes included.
        Assert.That(after.Scenes, Has.Count.EqualTo(2), "both scenes started and finished");
        Assert.That(after.PerMinute(NotEvent), Is.InRange(5.0, 7.0), "the evening's /faction pace");
        Assert.That(after.MaxInAMinute(NotEvent), Is.LessThanOrEqualTo(7), "no minute over the owner's 7");
        Assert.That(after.MaxInAMinute(_ => true), Is.LessThanOrEqualTo(8), "with a realm event at most one over");
        foreach (var s in after.Scenes)
        {
            double rate = s.Lines * 60_000.0 / Math.Max(1, s.Last - s.First);
            Assert.That(rate, Is.InRange(5.0, 7.0), $"a scene plays at 5-7 a minute ({s.Lines} lines in {(s.Last - s.First) / 1000} s)");
        }
        // Nothing cut: every beat of both scenes was said, as before.
        Assert.That(after.SceneLinesSaid, Is.EqualTo(after.SceneBeats));
        Assert.That(after.SceneLinesSaid, Is.EqualTo(before.SceneLinesSaid));
    }

    [Test]
    public void ThePaceHoldsWhateverTheDraw()
    {
        foreach (int seed in new[] { 1, 2, 3, 117, 9173 })
        {
            Evening after = Run(paced: true, seed);
            Assert.That(after.PerMinute(NotEvent), Is.InRange(5.0, 7.0), $"seed {seed}");
            Assert.That(after.MaxInAMinute(NotEvent), Is.LessThanOrEqualTo(7), $"seed {seed}");
            Assert.That(after.SceneLinesSaid, Is.EqualTo(after.SceneBeats), $"seed {seed}");
        }
    }
}
