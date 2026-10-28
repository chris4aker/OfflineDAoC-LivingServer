using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Deeds = DOL.GS.AutonomousPlayerDeeds;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>The living-world parts: deeds and reputation, storylines, gold spammers, roleplayers and true rumours.</summary>
    [TestFixture]
    public class UT_LivingWorld
    {
        private static VC _corpus;

        private static VC Corpus
        {
            get
            {
                if (_corpus != null)
                    return _corpus;
                var assembly = typeof(VC).Assembly;
                string Read(string suffix)
                {
                    string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                    using var stream = assembly.GetManifestResourceStream(name);
                    using var reader = new System.IO.StreamReader(stream);
                    return reader.ReadToEnd();
                }
                return _corpus = VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
            }
        }

        #region Deeds

        [TestCase("Grimwold the Giant", true)]
        [TestCase("hill giant", false)]
        [TestCase("", false)]
        public void ANamedMonsterHasACapitalisedName(string name, bool named) =>
            Assert.That(Deeds.IsNamed(name, true, false), Is.EqualTo(named));

        [Test]
        public void GuardsBotsAndRealmNpcsAreNotNamedMonsters()
        {
            Assert.That(Deeds.IsNamed("Captain Aldric", false, false), Is.False);
            Assert.That(Deeds.IsNamed("Aelwyn", true, true), Is.False);
        }

        [Test]
        public void OnlyAHardNamedKillIsNews()
        {
            Assert.That(Deeds.NamedKill(0, true), Is.EqualTo(Deeds.Kind.NamedSolo));
            Assert.That(Deeds.NamedKill(-1, true), Is.EqualTo(Deeds.Kind.NamedSolo), "a blue named solo counts (since 2026-10-27)");
            Assert.That(Deeds.NamedKill(-2, true), Is.EqualTo(Deeds.Kind.None), "a green named solo is not news");
            Assert.That(Deeds.NamedKill(-1, false), Is.EqualTo(Deeds.Kind.NamedGroup));
            Assert.That(Deeds.NamedKill(-2, false), Is.EqualTo(Deeds.Kind.None));
        }

        [Test]
        public void APlayersDeathIsAFallASillyDeathOrAGank()
        {
            Assert.That(Deeds.Death(true, false, 0, false, 0), Is.EqualTo(Deeds.Kind.Fell));
            Assert.That(Deeds.Death(false, true, -2, false, 0), Is.EqualTo(Deeds.Kind.SillyDeath));
            Assert.That(Deeds.Death(false, true, 1, false, 0), Is.EqualTo(Deeds.Kind.None), "an orange monster is a fair death");
            Assert.That(Deeds.Death(false, false, 0, true, 4), Is.EqualTo(Deeds.Kind.Ganked));
            Assert.That(Deeds.Death(false, false, 0, true, 3), Is.EqualTo(Deeds.Kind.Ganked), "three on one is a gank (since 2026-10-27)");
            Assert.That(Deeds.Death(false, false, 0, true, 2), Is.EqualTo(Deeds.Kind.None), "a fair fight is not a gank");
        }

        [Test]
        public void NicknamesAreEarnedHeroicOnesWinAndTheyAreStable()
        {
            Assert.That(Deeds.Earned("Kaelith", 2, 0, 0, ""), Is.Null);
            var named = Deeds.Earned("Kaelith", 3, 0, 0, "");
            Assert.That(named?.Kind, Is.EqualTo("named"));
            Assert.That(Deeds.Earned("Kaelith", 3, 0, 0, "")?.Nickname, Is.EqualTo(named?.Nickname), "the same player, the same word");
            Assert.That(Deeds.Earned("Kaelith", 3, 0, 0, "named"), Is.Null, "nothing changes");
            Assert.That(Deeds.Earned("Kaelith", 0, 0, 4, "")?.Kind, Is.EqualTo("silly"));
            Assert.That(Deeds.Earned("Kaelith", 0, 0, 9, "rvr"), Is.Null, "a silly nickname never replaces a heroic one");
            Assert.That(Deeds.Earned("Kaelith", 0, 30, 9, "silly")?.Kind, Is.EqualTo("rvr"), "a heroic one replaces a silly one");
            Assert.That(Deeds.Earned("Kaelith", 9, 30, 0, "rvr"), Is.Null, "a heroic nickname never flips to the other heroic kind");
            Assert.That(Deeds.Earned("Kaelith", 9, 80, 0, "named"), Is.Null);
        }

        [Test]
        public void ReputationTalkNeedsSomethingToTalkAbout()
        {
            Assert.That(Deeds.RepTopics(new OfflinePlayerReputationRecord()), Is.Empty);
            var row = new OfflinePlayerReputationRecord { NamedSolo = 1, BestNamed = "Grimwold", RvrKills = 12, SillyDeaths = 3 };
            Assert.That(Deeds.RepTopics(row).Select(t => t.Topic), Is.EquivalentTo(new[] { "rep_named", "rep_rvr", "rep_silly" }));
        }

        #endregion

        #region Storylines

        [Test]
        public void EveryArcHasFourEpisodesAndBothEndings()
        {
            var arcs = AutonomousBotStorylines.Arcs(Corpus.Scenes);
            Assert.That(arcs.Keys, Is.EquivalentTo(new[] { "killsteal", "guildsplit", "romance", "ninjaloot" }));
            foreach (var (arc, episodes) in arcs)
            {
                Assert.That(episodes.Length - 1, Is.EqualTo(4), arc);
                Assert.That(episodes[4].SideA, Is.Not.Null, arc);
                Assert.That(episodes[4].SideB, Is.Not.Null, arc);
                for (int n = 1; n <= 4; n++)
                    Assert.That(episodes[n].Neutral.SpeakingRoles, Does.Contain("A").And.Contain("B"), $"{arc} {n}");
                Assert.That(AutonomousBotStorylines.SceneFor(episodes[4], "A"), Is.SameAs(episodes[4].SideA));
                Assert.That(AutonomousBotStorylines.SceneFor(episodes[4], ""), Is.SameAs(episodes[4].Neutral));
                Assert.That(AutonomousBotStorylines.SceneFor(episodes[2], "B"), Is.SameAs(episodes[2].Neutral));
            }
        }

        [Test]
        public void AnEpisodeKeepsItsPairAndCastsOnlyTheOtherRoles()
        {
            VC.Scene scene = AutonomousBotStorylines.Arcs(Corpus.Scenes)["killsteal"][1].Neutral;
            int others = scene.SpeakingRoles.Count(role => role is not ("A" or "B"));
            var candidates = Enumerable.Range(100, others)
                .Select(id => new AutonomousBotDebates.Candidate(id, BotPersonalityType.Socialite, "Cleric", 30)).ToArray();
            var cast = AutonomousBotDebates.Cast(scene, candidates, new Random(1), new Dictionary<string, long> { ["A"] = 1, ["B"] = 2 });
            Assert.That(cast, Is.Not.Null, "exactly enough others for the other roles");
            Assert.That(cast["A"], Is.EqualTo(1));
            Assert.That(cast["B"], Is.EqualTo(2));
            Assert.That(cast.Values.Distinct().Count(), Is.EqualTo(cast.Count));
        }

        [Test]
        public void AStorylineIsNeverPlayedAsAnOrdinaryDebate()
        {
            int[] levels = Enumerable.Range(1, 50).ToArray();
            var heard = new HashSet<string>();
            var random = new Random(3);
            for (int i = 0; i < 400; i++)
            {
                VC.Scene scene = AutonomousBotDebates.ChooseScene(Corpus.Scenes, eRealm.Albion, heard, levels, random, out bool reset);
                Assert.That(AutonomousBotStorylines.IsStory(scene), Is.False, scene?.Id);
                if (reset) heard.Clear();
                heard.Add(scene.Id);
            }
        }

        [TestCase("Aelwyn is right", "A")]
        [TestCase("lol brannoc is lying", "A")]
        [TestCase("nah Brannoc did nothing wrong", "B")]
        [TestCase("team aelwy", "A")]
        [TestCase("boo Aelwyn", "B")]
        [TestCase("both of you stop", null)]
        [TestCase("Aelwyn and Brannoc, kiss and make up", null)]
        [TestCase("anyone lfg?", null)]
        // Both named (in-game check 2026-10-27): the clauses decide when they agree.
        [TestCase("Brannoc you ninja looted that rare, Aelwyn is right", "A")]
        [TestCase("Aelwyn is right and Brannoc is lying", "A")]
        [TestCase("Aelwyn is lying, Brannoc is right", "B")]
        [TestCase("Aelwyn is right, Brannoc is right too", null)]
        public void APlayerTakesASideByNamingOne(string line, string side) =>
            Assert.That(AutonomousBotStorylines.SideOf(line, "Aelwyn", "Brannoc"), Is.EqualTo(side));

        #endregion

        #region Gold spammers and roleplayers

        [Test]
        public void ASpammerHasAGibberishNameAndAnObviouslyFakeSite()
        {
            var random = new Random(9);
            for (int i = 0; i < 2_000; i++)
            {
                string name = AutonomousGoldSpammers.SpammerName(random);
                Assert.That(name.Length, Is.InRange(7, 12), name);
                Assert.That(name.All(char.IsLetter) && char.IsUpper(name[0]), name);
                string site = AutonomousGoldSpammers.FakeSite(random);
                Assert.That(AutonomousGoldSpammers.IsObviouslyFake(site), site);
                Assert.That(site.ToLowerInvariant(), Does.Not.Contain(".com").And.Not.Contain(".net").And.Not.Contain(".org"), site);
            }
        }

        [Test]
        public void AboutFourPercentOfSocialitesAndExplorersRoleplay()
        {
            int pool = 0, rp = 0;
            for (long id = 1; id <= 200_000; id++)
            {
                var type = AutonomousBotPersonality.TypeFor(id);
                bool fits = type is BotPersonalityType.Socialite or BotPersonalityType.Explorer;
                if (fits) pool++;
                if (AutonomousRoleplayers.IsRoleplayerId(id))
                {
                    rp++;
                    Assert.That(fits, Is.True);
                }
            }
            Assert.That((double)rp / pool, Is.InRange(0.033, 0.047));
        }

        [TestCase("thank you, are you ok?", "my thanks, art thou very well?")]
        [TestCase("yes, your sword is nice", "aye, thy sword is nice")]
        [TestCase("lol no", "*chuckles* nay")]
        [TestCase("thanks u", "my thanks thee")]
        public void ARoleplayerSpeaksInTheeAndThou(string line, string olde) =>
            Assert.That(AutonomousRoleplayers.Speak(line, new Random(1)).Split(new[] { ", forsooth", ", m'lord", " *bows*", ", huzzah!", ", by my troth", ", verily", " *tips hat*" },
                StringSplitOptions.None)[0].TrimEnd('?', '.', '!', ' '), Is.EqualTo(olde.TrimEnd('?', '.', '!', ' ')));

        #endregion

        #region Rumours

        [Test]
        public void OnlyARareNamedMakesARumour()
        {
            Assert.That(AutonomousRumours.IsRareNamed("Grimwold", true, false, 30 * 60_000, 35), Is.True);
            Assert.That(AutonomousRumours.IsRareNamed("Grimwold", true, false, 5 * 60_000, 35), Is.False, "a quick respawn is not rare");
            Assert.That(AutonomousRumours.IsRareNamed("Grimwold", true, false, 30 * 60_000, 12), Is.False, "too low to matter");
            Assert.That(AutonomousRumours.IsRareNamed("hill giant", true, false, 30 * 60_000, 35), Is.False);
            Assert.That(AutonomousRumours.Cares(25, 35), Is.True);
            Assert.That(AutonomousRumours.Cares(10, 35), Is.False);
            Assert.That(AutonomousRumours.Cares(45, 35), Is.False);
        }

        [Test]
        public void AKeepIsThinWhenMostGuardsAreDead()
        {
            Assert.That(AutonomousRumours.IsThin(10, 2), Is.True);
            Assert.That(AutonomousRumours.IsThin(10, 3), Is.False);
            Assert.That(AutonomousRumours.IsThin(3, 0), Is.False, "too few guards to tell");
        }

        #endregion

        /// <summary>Every topic the code asks for has lines.</summary>
        [TestCase("deed", "named_solo,named_group,rvr_streak,ganked,silly_death,fell,rep_named,rep_rvr,rep_silly,nickname", 40)]
        [TestCase("deed_reply", "named_solo,named_group,rvr_streak,ganked,silly_death,fell,rep_named,rep_rvr,rep_silly,nickname", 20)]
        [TestCase("rumour", "named_up,keep_thin", 40)]
        [TestCase("rumour_reply", "named_up,keep_thin", 20)]
        [TestCase("spam", "ad,tell", 40)]
        [TestCase("spam_react", "react,banned", 40)]
        [TestCase("roleplay", "ambient,mock,answer", 40)]
        [TestCase("storyline", "side_thanks,side_scorn", 20)]
        [TestCase("bond", "friends,reunion,flake", 25)]
        [TestCase("bond_reply", "friends,reunion,flake", 12)]
        public void EveryTopicHasLines(string kind, string topics, int floor)
        {
            foreach (string topic in topics.Split(','))
                Assert.That(Corpus.Lines.Count(line => line.Kind == kind && line.Topic == topic), Is.GreaterThanOrEqualTo(floor), $"{kind}/{topic}");
        }
    }
}
