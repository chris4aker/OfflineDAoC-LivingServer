using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>GitHub #544: the sampled AUTONOMOUS_KILL_XP row (format, sampling, no behaviour change).</summary>
    [TestFixture]
    public sealed class UT_KillXpRow123
    {
        private static KillXpRow Row()
        {
            var killer = new KillXpParty("group", "Albion", 4, 620);
            var other = new KillXpParty("solo", "Albion", 1, 300);
            return new KillXpRow
            {
                Sequence = 50, Every = 25, Dropped = 2, Realm = "Albion", Zone = "Salisbury Plains", Region = 1,
                Npc = "bandit \"lieutenant\"", NpcLevel = 14, NpcXp = 52000, Con = "ORANGE", PartyKind = "group",
                GroupSize = 5, Contributors = 4, MinLevel = 11, MaxLevel = 13, AverageLevel = 12.25,
                CampId = "classic165-restored:1:2:3", CampTarget = "giant frog", CampLevel = 15, OnTarget = 0, SameName = 0,
                XpPerMember = [8061, 8061, 8061, 0], TotalDamage = 1000, Killer = killer, Parties = [killer, other]
            };
        }

        [Test]
        public void RowCarriesEveryFieldTheRealmQuestionNeeds()
        {
            string line = AutonomousKillXpDiagnostics.Format(Row());
            Assert.Multiple(() =>
            {
                Assert.That(line, Does.StartWith("AUTONOMOUS_KILL_XP seq=50 every=25 dropped=2 realm=Albion zone=\"Salisbury Plains\" region=1 "));
                Assert.That(line, Does.Contain("npc=\"bandit 'lieutenant'\" npcLevel=14 npcXp=52000 con=ORANGE"));
                Assert.That(line, Does.Contain("party=group size=5 contributors=4 minLevel=11 maxLevel=13 avgLevel=12.3 levelDiff=1"));
                Assert.That(line, Does.Contain("camp=\"classic165-restored:1:2:3\" campTarget=\"giant frog\" campLevel=15 onTarget=0 sameName=0"));
                Assert.That(line, Does.Contain("xpPerMember=8061/8061/8061/0 xpSum=24183"));
                Assert.That(line, Does.Contain("share=0.620 otherParties=1 lost=0.080"));
                Assert.That(line, Does.EndWith("shares=\"group/Albion/4/0.620/killer;solo/Albion/1/0.300;lost/0.080\""));
                Assert.That(line, Does.Not.Contain("\n"));
            });
        }

        [Test]
        public void UnknownCampAndSoloKillStillFormat()
        {
            var solo = new KillXpParty("solo", "Hibernia", 1, 50);
            string line = AutonomousKillXpDiagnostics.Format(new KillXpRow
            {
                Realm = "Hibernia", PartyKind = "solo", TotalDamage = 50, Killer = solo, Parties = [solo], XpPerMember = [1200]
            });
            Assert.That(line, Does.Contain("campLevel=? onTarget=? sameName=?"), "an unknown camp level reads ?");
            Assert.That(line, Does.Contain("share=1.000 otherParties=0 lost=0.000 shares=\"solo/Hibernia/1/1.000/killer\""));
        }

        [Test]
        public void SamplerTakesEveryNthKillAndCapsAMinute()
        {
            var sampler = new KillXpSampler(3);
            int taken = 0;
            long lastDropped = -1;
            for (int kill = 1; kill <= 100; kill++)
                if (sampler.Take(10, 1_000, out long seq, out long dropped))
                {
                    taken++;
                    Assert.That(seq % 10, Is.Zero);
                    lastDropped = dropped;
                }
            Assert.That(taken, Is.EqualTo(3), "10 sampled kills in one minute, 3 written");
            Assert.That(sampler.Take(10, 1_000, out _, out _), Is.False);
            for (int kill = 0; kill < 8; kill++) sampler.Take(10, 61_000, out _, out _);
            Assert.That(sampler.Take(10, 61_000, out long next, out long skipped), Is.True, "a new minute opens");
            Assert.Multiple(() =>
            {
                Assert.That(next, Is.EqualTo(110));
                Assert.That(skipped, Is.EqualTo(7), "kills 40-100 were sampled but capped");
                Assert.That(lastDropped, Is.Zero);
            });
        }

        [Test]
        public void SamplerOffAndSamplingIsDeterministic()
        {
            var off = new KillXpSampler(120);
            Assert.That(Enumerable.Range(0, 50).Any(i => off.Take(0, i, out _, out _)), Is.False);
            var a = new KillXpSampler(120);
            var b = new KillXpSampler(120);
            bool[] first = Enumerable.Range(0, 200).Select(i => a.Take(7, i, out _, out _)).ToArray();
            bool[] second = Enumerable.Range(0, 200).Select(i => b.Take(7, i, out _, out _)).ToArray();
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.Count(x => x), Is.EqualTo(28));
        }

        [Test]
        public void DefaultSamplesOneKillIn25()
        {
            Assert.That(AutonomousDiagnosticsProperties.KillXpSampleEvery, Is.EqualTo(25));
            Assert.That(AutonomousKillXpDiagnostics.MaxRowsPerMinute, Is.EqualTo(120));
        }

        [Test]
        public void TheRowUsesNoRandomNumbersAndWritesNoState()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            string code = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous",
                "AutonomousKillXpDiagnostics.cs"));
            string body = Regex.Replace(code, @"///.*|//.*", string.Empty);
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Not.Match(@"\bUtil\.|\bRandom\b|Chance\(|RandomNumber"), "no game random numbers");
                Assert.That(body, Does.Not.Match(@"Database\.|SaveObject|AddObject|MarkAutonomousStateDirty|Persistence\.Queue"),
                    "no database or roster writes");
                Assert.That(body, Does.Not.Match(@"\.(Experience|CampBonus|Health|Level)\s*[-+]?=[^=]"), "never sets the kill's state");
                Assert.That(body, Does.Not.Match(@"GainExperience|GoalDiagnosticAttempt\s*="), "never pays or changes a goal");
            });

            string rules = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "serverrules", "AbstractServerRules.cs"));
            int begin = rules.IndexOf("AutonomousKillXpDiagnostics.Begin(", StringComparison.Ordinal);
            int award = rules.IndexOf("AwardBotOnNpcKill(bot, totalDamage", StringComparison.Ordinal);
            int finish = rules.IndexOf("AutonomousKillXpDiagnostics.Finish(", StringComparison.Ordinal);
            Assert.That(begin, Is.GreaterThan(0).And.LessThan(award), "XP is read before the award");
            Assert.That(finish, Is.GreaterThan(award), "the row is written after the award");
        }
    }
}
