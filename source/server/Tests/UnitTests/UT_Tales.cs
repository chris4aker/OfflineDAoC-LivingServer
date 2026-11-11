using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Debates = DOL.GS.AutonomousBotDebates;
using Tales = DOL.GS.AutonomousTales;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Tales (Living Server 1.2, the owner's idea, 2026-11-10; docs/TALES.md): a bot asks the realm's storyteller for a
    /// story in /faction, others hush the channel, the teller tells it, a quiet moment follows, then a rumour that is
    /// sometimes a raid really forming. Fireside tales are the short versions a groupmate tells in /group. These tests cover
    /// the pure rules and the written tales (data/chat/tales.lines). Nothing here is a played check.
    /// </summary>
    [TestFixture]
    public class UT_Tales
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

        private static VC.Scene[] TaleScenes => Corpus.Scenes.Where(Tales.IsTale).ToArray();
        private static VC.Scene[] FiresideScenes => Corpus.Scenes.Where(Tales.IsFireside).ToArray();
        private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

        [Test]
        public void EveryRealmHasFiveTalesAndEachIsAboutALoreBoss()
        {
            Assert.That(TaleScenes.Length, Is.GreaterThanOrEqualTo(15));
            foreach (eRealm realm in Realms)
                Assert.That(TaleScenes.Count(t => VC.RealmFits(t.Realm, realm)), Is.GreaterThanOrEqualTo(5), realm.ToString());
            foreach (VC.Scene tale in TaleScenes.Concat(FiresideScenes))
            {
                string loreId = Tales.LoreIdOf(tale.Topic);
                Assert.That(LoreBible.Current.BossById(loreId), Is.Not.Null, $"{tale.Id}: no lore file \"{loreId}\"");
            }
        }

        [Test]
        public void ATaleIsAskedForToldByTheTellerAndEndsInAQuietMoment()
        {
            foreach (VC.Scene tale in TaleScenes)
            {
                VC.Beat[] spoken = tale.Beats.Where(b => !b.IsPause).ToArray();
                Assert.That(spoken[0].Role, Is.EqualTo("A"), $"{tale.Id}: a bot asks first");
                Assert.That(spoken[0].Variants, Has.All.Contain("{name_b}"), $"{tale.Id}: the ask names the storyteller");
                Assert.That(spoken[0].Optional, Is.False);
                Assert.That(spoken.Count(b => b.Role == Tales.TellerRole && !b.Optional), Is.GreaterThanOrEqualTo(8), $"{tale.Id}: the teller tells it");
                // Someone hushes the channel before the teller begins, and someone asks a question on the way.
                int firstTelling = Array.FindIndex(spoken, b => b.Role == Tales.TellerRole);
                Assert.That(firstTelling, Is.GreaterThanOrEqualTo(2), $"{tale.Id}: hushed and begged before it starts");
                Assert.That(spoken.Skip(firstTelling).Count(b => b.Role != Tales.TellerRole && b.Variants.Any(v => v.Contains('?'))),
                    Is.GreaterThanOrEqualTo(1), $"{tale.Id}: a question leads it along");
                // The quiet moment: a long pause, then one to three closing lines, none of them the teller's.
                int hush = Array.FindLastIndex(tale.Beats, b => b.IsPause);
                Assert.That(tale.Beats[hush].PauseSeconds, Is.GreaterThanOrEqualTo(20), $"{tale.Id}: the hush");
                VC.Beat[] after = tale.Beats.Skip(hush + 1).ToArray();
                Assert.That(after.Length, Is.InRange(1, 3), $"{tale.Id}: lines after the hush");
                Assert.That(after, Has.None.Matches<VC.Beat>(b => b.Role == Tales.TellerRole), $"{tale.Id}: the teller has said its piece");
                // Only the ask and the thanks name the teller; the teller never names itself.
                Assert.That(spoken.Where(b => b.Role == Tales.TellerRole).SelectMany(b => b.Variants), Has.None.Contain("{name_b}"));
                Assert.That(tale.Roles.Keys, Does.Contain(Tales.TellerRole));
            }
        }

        [Test]
        public void EveryTaleHasItsFiresideVersionForTheSameRealms()
        {
            foreach (VC.Scene tale in TaleScenes)
                foreach (eRealm realm in Realms.Where(r => VC.RealmFits(tale.Realm, r)))
                    Assert.That(FiresideScenes.Any(f => Tales.LoreIdOf(f.Topic) == Tales.LoreIdOf(tale.Topic) && VC.RealmFits(f.Realm, realm)),
                        Is.True, $"{tale.Id}: no fireside tale for {realm}");
            foreach (VC.Scene fireside in FiresideScenes)
            {
                VC.Beat[] spoken = fireside.Beats.Where(b => !b.IsPause).ToArray();
                Assert.That(spoken.Length, Is.InRange(4, 7), $"{fireside.Id}: short");
                Assert.That(spoken.Select(b => b.Role).Distinct(), Is.SubsetOf(new[] { "A", "B" }), $"{fireside.Id}: a teller and a listener");
                Assert.That(spoken[0].Role, Is.EqualTo("A"));
                // Told with no listener too: the teller's own lines stand alone (at least three of them).
                Assert.That(spoken.Count(b => b.Role == "A" && !b.Optional), Is.GreaterThanOrEqualTo(3), fireside.Id);
            }
        }

        [Test]
        public void EveryTaleHasItsRealmsRaid()
        {
            foreach (VC.Scene tale in TaleScenes)
                foreach (eRealm realm in Realms.Where(r => VC.RealmFits(tale.Realm, r)))
                {
                    string raid = Tales.RaidIdFor(Tales.LoreIdOf(tale.Topic), realm);
                    Assert.That(raid, Is.Not.Null, tale.Id);
                    Assert.That(AutonomousRealmRaid.Definitions.Any(d => d.Id == raid && d.Realm == realm), Is.True, $"{tale.Id}: no raid {raid} for {realm}");
                }
            Assert.That(Tales.RaidIdFor("legion", eRealm.Midgard), Is.EqualTo("darkness-midgard"));
            Assert.That(Tales.RaidIdFor("xanxicar", eRealm.Albion), Is.EqualTo("dungeon-albion-crystal"));
            Assert.That(Tales.RaidIdFor("nobody", eRealm.Albion), Is.Null);
            Assert.That(Tales.RaidIdFor("legion", eRealm.None), Is.Null);
        }

        [Test]
        public void OrdinaryDebatesNeverPlayATale()
        {
            var random = new Random(5);
            var levels = Enumerable.Repeat(40, 30).ToArray();
            foreach (eRealm realm in Realms)
                for (int i = 0; i < 400; i++)
                {
                    VC.Scene scene = Debates.ChooseScene(Corpus.Scenes, realm, new HashSet<string>(), levels, random, out _);
                    Assert.That(Tales.IsTaleScene(scene), Is.False, scene?.Id);
                }
            Assert.That(Tales.IsTaleScene(Corpus.Scenes.First(s => s.Topic == "camp_etiquette")), Is.False, "a debate about camps is no fireside tale");
        }

        [Test]
        public void TheStorytellerIsATalkerAndAVeteran()
        {
            var pool = new[]
            {
                new Tales.TellerCandidate(5, "Grinder", BotPersonalityType.NoLifer, 50),
                new Tales.TellerCandidate(7, "Wanderer", BotPersonalityType.Explorer, 50),
                new Tales.TellerCandidate(9, "Young", BotPersonalityType.Socialite, 38),
                new Tales.TellerCandidate(3, "Elder", BotPersonalityType.Socialite, 50),
            };
            Assert.That(Tales.PickTeller(pool)?.Name, Is.EqualTo("Elder"), "a talker first, the veteran among them");
            Assert.That(Tales.PickTeller(pool, new HashSet<long> { 3 })?.Name, Is.EqualTo("Young"), "the second storyteller is another bot");
            Assert.That(Tales.PickTeller(pool.Where(c => c.Type != BotPersonalityType.Socialite))?.Name, Is.EqualTo("Wanderer"));
            Assert.That(Tales.PickTeller(pool.Reverse())?.Name, Is.EqualTo("Elder"), "whoever is listed first");
            Assert.That(Tales.PickTeller([]), Is.Null);
        }

        [Test]
        public void TheTaleHeardLongestAgoIsToldUnlessOneIsAskedFor()
        {
            VC.Scene[] tales = TaleScenes.Where(t => VC.RealmFits(t.Realm, eRealm.Albion)).ToArray();
            var random = new Random(2);
            var told = tales.ToDictionary(t => t.Id, _ => 100L);
            told["tale_xanxicar"] = 0;   // never told
            for (int i = 0; i < 20; i++)
                Assert.That(Tales.ChooseTale(tales, null, id => told[id], random).Id, Is.EqualTo("tale_xanxicar"));
            Assert.That(Tales.ChooseTale(tales, "legion", id => told[id], random).Id, Is.EqualTo("tale_legion_alb"), "the one asked for, the realm's own telling");
            Assert.That(Tales.ChooseTale(tales, "gjalpinulva", id => told[id], random).Id, Is.EqualTo("tale_xanxicar"), "another realm's tale is not ours to tell");
            Assert.That(Tales.ChooseTale([], null, null, random), Is.Null);
        }

        [TestCase("tell the one about legion", true)]
        [TestCase("any stories tonight?", true)]
        [TestCase("Maeve, tell us a tale", true)]
        [TestCase("story time!", true)]
        [TestCase("tell me where the trainer is", false)]
        [TestCase("dont tell him i said that", false)]
        [TestCase("lfg 50 cleric", false)]
        [TestCase("i told you so", false)]
        public void APlayerCanAskForATale(string message, bool asks) => Assert.That(Tales.IsRequest(message), Is.EqualTo(asks), message);

        [Test]
        public void ARequestNamesItsBossByAnyOfItsNames()
        {
            var bosses = new (string, IEnumerable<string>)[]
            {
                ("golestandt", ["golestandt", "the stone dragon", "gole"]),
                ("legion", ["legion"]),
                ("cuuldurach-the-glimmer-king", ["cuuldurach the glimmer king", "cuuldurach", "the glimmer king"]),
            };
            Assert.That(Tales.RequestedLore("tell the one about the stone dragon", bosses), Is.EqualTo("golestandt"));
            Assert.That(Tales.RequestedLore("tell the gole story", bosses), Is.EqualTo("golestandt"));
            Assert.That(Tales.RequestedLore("Tell us of the Glimmer King!", bosses), Is.EqualTo("cuuldurach-the-glimmer-king"));
            Assert.That(Tales.RequestedLore("any stories tonight?", bosses), Is.Null, "any tale will do");
            Assert.That(Tales.RequestedLore("tell the legionnaire joke", bosses), Is.Null, "a name is matched as a word");
        }

        [Test]
        public void TheTellerAnswersQuestionsOnly()
        {
            Assert.That(Tales.IsQuestion("who was ansel?"), Is.True);
            Assert.That(Tales.IsQuestion("why does it eat them"), Is.True);
            Assert.That(Tales.IsQuestion("lol"), Is.False);
            Assert.That(Tales.IsQuestion("great story"), Is.False);
            Assert.That(Tales.MaxAsides, Is.InRange(1, 3));
        }

        [Test]
        public void ARealmHearsAtMostOneOfItsOwnTalesAnEvening()
        {
            DateTime now = new(2026, 11, 10, 22, 0, 0, DateTimeKind.Utc);
            Assert.That(Tales.GapOver(now, null, Tales.TaleGapMs), Is.True, "never told");
            Assert.That(Tales.GapOver(now, now.AddHours(-2), Tales.TaleGapMs), Is.False);
            Assert.That(Tales.GapOver(now, now.AddHours(-4), Tales.TaleGapMs), Is.True);
            // A player who asks waits only twenty minutes after the last one.
            Assert.That(Tales.GapOver(now, now.AddMinutes(-10), Tales.RequestGapMs), Is.False);
            Assert.That(Tales.GapOver(now, now.AddMinutes(-25), Tales.RequestGapMs), Is.True);
            Assert.That(Tales.GapOver(now, now.AddDays(1), Tales.TaleGapMs), Is.True, "a clock set back never locks tales out");
            Assert.That(Tales.TaleGapMs, Is.GreaterThanOrEqualTo(3 * 60 * 60_000L));
            Assert.That(Tales.FirstTaleDelayMs, Is.GreaterThanOrEqualTo(20 * 60_000L), "not in a session's first minutes");
        }

        [Test]
        public void AFiresideTaleWaitsForARestingGroup()
        {
            long now = 10 * 60_000;
            Assert.That(Tales.FiresideDue(true, now - Tales.FiresideQuietMs, long.MinValue / 2, now), Is.True);
            Assert.That(Tales.FiresideDue(false, now - Tales.FiresideQuietMs, long.MinValue / 2, now), Is.False, "standing: on the move");
            Assert.That(Tales.FiresideDue(true, now - 5_000, long.MinValue / 2, now), Is.False, "just out of a fight");
            Assert.That(Tales.FiresideDue(true, 0, long.MinValue / 2, now), Is.False, "not yet seen resting");
            Assert.That(Tales.FiresideDue(true, now - Tales.FiresideQuietMs, now - 60_000, now), Is.False, "one in half an hour");
        }

        [Test]
        public void TheWrittenAsidesAreChatLines()
        {
            foreach (string line in Tales.Deflections.Concat(Tales.CutShort).Concat(Tales.TellerAway).Concat(Tales.RaidRumours))
            {
                Assert.That(line.Length, Is.InRange(8, 110), line);
                Assert.That(line, Is.EqualTo(line.ToLowerInvariant()), "chat voice: " + line);
            }
            Assert.That(Tales.RaidRumours, Has.All.Contain("{boss}"));
            Assert.That(Tales.TellerAway, Has.All.Contain("{teller}"));
            Assert.That(Tales.TrueRumourChance, Is.InRange(0.2, 0.8), "sometimes true, never always");
        }

        [Test]
        public void ATaleGoesOnWhenAListenerLeaves()
        {
            // PC check 2026-11-10, build 15fcc70: the bot that begged for the tale logged off and the tale stopped mid-story.
            Assert.That(Debates.MayRecast(roleSpoke: true, storyProtagonist: false, taleListener: true), Is.True, "another listener asks");
            Assert.That(Debates.MayRecast(roleSpoke: true, storyProtagonist: true, taleListener: true), Is.False, "never the teller");
            Assert.That(Debates.MayRecast(roleSpoke: true, storyProtagonist: false), Is.False, "a debate's role keeps its bot");
            // With nobody to take the role, its line is skipped while the teller and one listener are still there.
            Assert.That(Debates.DecideBeat(Debates.SpeakerState.Gone, 0, standInAvailable: false, speakersAround: 3, roleSpoke: false),
                Is.EqualTo(Debates.BeatDecision.Skip));
        }

        [Test]
        public void ABusyTellerIsWaitedForBrieflyThenTypesBetweenPulls()
        {
            // PC check 2026-11-10, build 15fcc70: three minutes of a silent /faction while the teller fought.
            Assert.That(Debates.TaleBusyWaitMs(0), Is.EqualTo(Debates.MaxBusyWaitMs), "the first fight: as long as any speaker");
            Assert.That(Debates.TaleBusyWaitMs(Debates.MaxBusyWaitMs), Is.EqualTo(Debates.TaleBusyBudgetMs - Debates.MaxBusyWaitMs));
            Assert.That(Debates.TaleBusyWaitMs(Debates.TaleBusyBudgetMs), Is.EqualTo(Debates.TaleBusyMinMs), "the budget is spent");
            Assert.That(Debates.TaleBusyWaitMs(10 * Debates.TaleBusyBudgetMs), Is.EqualTo(Debates.TaleBusyMinMs));
            Assert.That(Debates.TaleBusyBudgetMs, Is.LessThan(Debates.StoryBusyWaitMs));
        }

        [Test]
        public void AnAnswerOrRumourDoesNotRepeatTheTale()
        {
            // PC check 2026-11-10, build 15fcc70: the teller's answer, a line of the tale and the rumour after it were one sentence.
            string[] tale =
            [
                "camelot's priests say he is the thing in the gospel that named itself for its number",
                "his kingdom has three doors. ours is in camelot hills",
            ];
            Assert.That(Tales.Repeats("camelot's priests say legion is the thing in the gospel that named itself for its number", tale), Is.True);
            Assert.That(Tales.Repeats("heard the imps sell the way down for a candle end", tale), Is.False);
            // Build 5300a87: the same fact in other words ("stops" and "stopped", "a second" and "one breath").
            Assert.That(Tales.Repeats("heard the whole fortress stops turning for a second when apoc dies",
                ["they say the whole fortress stopped turning for one breath"]), Is.True);
            Assert.That(Tales.Repeats("someone said apocalypse isnt even angry, just curious",
                ["they say the whole fortress stopped turning for one breath", "do you challenge fate"]), Is.False);
            Assert.That(Tales.Repeats("wait for it", tale), Is.False, "a short line is never a repeat");
            Assert.That(Tales.Repeats("his kingdom has three doors", ["wait for it"]), Is.False);
        }

        [Test]
        public void EveryTaleLeavesItsBossARumourItDoesNotTellItself()
        {
            // The rumour after a tale is one the tale did not just say, so every tale's boss needs one the tale leaves out.
            foreach (var scene in TaleScenes)
            {
                var boss = LoreBible.Current.BossById(Tales.LoreIdOf(scene.Topic));
                Assert.That(boss, Is.Not.Null, scene.Id);
                string[] lines = Tales.LinesOf(scene).ToArray();
                foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                {
                    if (!VC.RealmFits(scene.Realm, realm))
                        continue;
                    if ((boss.RumourFor(realm) ?? []).Length == 0)
                        continue;
                    Assert.That(Tales.FreshRumours(boss, realm, lines, new Random(1)), Is.Not.Empty,
                        $"{scene.Id} ({realm}): every rumour of {boss.Id} repeats the tale");
                }
            }
        }
    }
}
