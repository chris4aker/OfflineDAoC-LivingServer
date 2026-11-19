using System;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Arbiter = DOL.GS.AutonomousChatArbiter;
using Budget = DOL.GS.AutonomousChatBudget;
using Coord = DOL.GS.AutonomousBotChatCoordinator;

namespace DOL.UnitTests
{
    /// <summary>
    /// The owner's PC log on build 5154cae, 2026-11-19 07:04-07:27 (Testrunner, Albion, in Camelot). Two /faction questions were
    /// answered 33 s later (07:08:25 to 07:08:58, 07:10:24 to 07:10:57), and at 07:17:26 "you ready?" in /say to the targeted
    /// gamebot Linaibel drew only the companion Pannax's "ok". Tests only: none of this has been seen on the owner's PC yet.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_ChatAnswerLatency0730 : DutyLeaseFixture
    {
        // The two answers as the log has them (Ogria, Thaxard).
        private const string Keltoi = "Keltoi fogou? The keltoi holy cave in campacorentin. Priests, banishers, gremlins and ants. Teens to twenties.";
        private const string Fielath = "fielath? the demon in the tallest broken keep in the ruins upriver. low level but its next to the df door";

        private static Budget.Settings Settings => Budget.Parse(Budget.BuiltIn());

        // ---- 1. /faction answers to a player's question --------------------------------------------------------------------------

        [Test]
        public void The_33_second_answers_were_typing_time()
        {
            // At the 25-60 wpm the budget types at, these two lines take 20-50 s to arrive: the 33 s in the log is about 40 wpm.
            var random = new Random(730);
            foreach ((string asked, string answer) in new[] { ("is it far?", Keltoi), ("can someone help me in Camelot Hills?", Fielath) })
            {
                int at40 = Arbiter.DelayMs(asked, answer, 40, 0, new Random(1));
                Assert.That(at40, Is.InRange(29_000, 37_000), "about 33 s at 40 wpm, as on the PC");
                for (int wpm = 25; wpm <= 60; wpm++)
                {
                    int raw = Arbiter.DelayMs(asked, answer, wpm, 0, random);
                    Assert.That(raw, Is.GreaterThan(Coord.DirectTypingCapMs), $"{wpm} wpm: slower than a real reply");
                    int capped = Coord.PlayerAnswerDueMs(raw, 0, directed: false, firstAnswerToQuestion: true);
                    Assert.That(capped, Is.InRange(4_000, 15_000), $"{wpm} wpm: a short reply's time");
                    Assert.That(capped, Is.EqualTo(Coord.DirectTypingCapMs));
                }
            }
        }

        [Test]
        public void Only_the_first_answer_to_a_question_is_capped()
        {
            var random = new Random(5);
            int raw = Arbiter.DelayMs("is it far?", Keltoi, 30, 0, random);
            // A remark, a greeting's answer, a pile-on: each keeps its own typing time (the owner's pace for other talk).
            Assert.That(Coord.PlayerAnswerDueMs(raw, 0, false, false), Is.EqualTo(raw));
            int pileOn = Arbiter.DelayMs("is it far?", Fielath, 30, 12_000, random);
            Assert.That(Coord.PlayerAnswerDueMs(pileOn, 12_000, false, false), Is.EqualTo(pileOn));
            // A line that names the bot keeps DESIGN-018's cap, after the reply before it.
            Assert.That(Coord.PlayerAnswerDueMs(pileOn, 12_000, true, false), Is.EqualTo(Math.Min(pileOn, 24_000)));
            // A quick answer is never made slower.
            Assert.That(Coord.PlayerAnswerDueMs(5_000, 0, false, true), Is.EqualTo(5_000));
        }

        [Test]
        public void The_faction_pace_still_holds_openers_and_scenes()
        {
            // The cap moves no other line: the owner's 5-7 a minute for openers and scenes is as it was (#368).
            (double max, double scene) = Budget.FactionPace(Settings, 2.0);
            Assert.That(max, Is.EqualTo(7.0));
            Assert.That(scene, Is.EqualTo(6.0));
            Assert.That(Budget.OpenerFitsPace(6, 0, max), Is.False, "an opener still waits at 6 lines a minute");
            Assert.That(Budget.OpenerFitsPace(4, 0, max), Is.True);
        }

        // ---- 2. "you ready?" in /say to a targeted gamebot -----------------------------------------------------------------------

        [Test]
        public void A_say_line_aimed_at_a_gamebot_is_not_the_companions()
        {
            long[] companions = [11, 12];
            const long linaibel = 3045;
            Assert.That(CompanionChat165.AimedElsewhere(companions, linaibel, 0, true), Is.True, "the player targets another bot");
            Assert.That(CompanionChat165.AimedElsewhere(companions, 0, linaibel, true), Is.True, "another bot has the floor");
            Assert.That(CompanionChat165.AimedElsewhere(companions, 0, linaibel, false), Is.False, "a hello is to everyone, floor or not");
            Assert.That(CompanionChat165.AimedElsewhere(companions, 11, 0, true), Is.False, "the player targets their companion");
            Assert.That(CompanionChat165.AimedElsewhere(companions, 0, 12, true), Is.False, "a companion has the floor");
            Assert.That(CompanionChat165.AimedElsewhere(companions, 0, 0, true), Is.False, "said to nobody: the companion may chime in");
        }

        [Test]
        public void The_companion_stays_quiet_on_a_line_for_another_bot()
        {
            Bot pannax = Gamebot(5);
            var near = new GameBot[] { pannax };
            int chimed = Enumerable.Range(0, 200).Count(seed => CompanionChat165.ChooseSpeaker(near, null, 0, new Random(seed)) != null);
            Assert.That(chimed, Is.GreaterThan(0), "with nobody else addressed it chimes in now and then (as at 07:17:28)");
            for (int seed = 0; seed < 200; seed++)
                Assert.That(CompanionChat165.ChooseSpeaker(near, null, 0, new Random(seed), aimedElsewhere: true), Is.Null);
            Assert.That(CompanionChat165.ChooseSpeaker(near, pannax, 0, new Random(1), aimedElsewhere: true), Is.SameAs(pannax),
                "its name still gets its answer");
        }

        [Test]
        public void A_dropped_answer_says_why_in_the_log()
        {
            Assert.That(Coord.BusyAtSendReason(true, true, false, false, false, false, false, false), Is.EqualTo("left_region"));
            Assert.That(Coord.BusyAtSendReason(true, true, false, false, true, false, true, true), Is.EqualTo("riding"));
            Assert.That(Coord.BusyAtSendReason(true, true, false, false, true, true, true, true), Is.EqualTo("stable_route"));
            Assert.That(Coord.BusyAtSendReason(true, true, false, false, false, false, true, false), Is.EqualTo("out_of_earshot"));
            Assert.That(Coord.BusyAtSendReason(true, true, true, true, false, false, true, true), Is.EqualTo("fighting"));
            Assert.That(Coord.BusyAtSendReason(false, true, false, false, false, false, true, true), Is.EqualTo("gone"));
            Assert.That(Coord.BusyAtSendReason(true, false, false, false, false, false, true, true), Is.EqualTo("dead"));
        }
    }
}
