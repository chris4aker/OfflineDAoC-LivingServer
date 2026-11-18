using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The scene fixes of the 1.2 chat audit (corpus-b1): who may be cast in a scene, whose level picks its shared
    /// {dungeon}, and the scene lines themselves (tests only; not yet seen on the owner's PC).
    /// </summary>
    [TestFixture]
    public class UT_ChatScenesAudit
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

        private static VC.Scene Scene(int low, int high, params (string Role, VC.RoleSpec Spec)[] roles)
        {
            var specs = roles.ToDictionary(r => r.Role, r => r.Spec, StringComparer.Ordinal);
            var beats = new[]
            {
                new VC.Beat("A", ["anyone going to thidranki?"], false, 0),
                new VC.Beat("B", ["im in"], false, 0),
                new VC.Beat("C", ["watch out for twinks"], false, 0),
            };
            return new VC.Scene("test", "test", "*", low, high, specs, beats);
        }

        private static AutonomousBotDebates.Candidate Bot(long id, int level, string className = "Armsman", bool roleplayer = false) =>
            new(id, BotPersonalityType.Leveler, className, level, Roleplayer: roleplayer);

        [Test]
        public void ANarrowSceneIsCastOnlyInsideItsBand()
        {
            // Owner's PC, 2026-11-18 01:36: sh_thid_tonight (Thidranki, 20-24) was said by level 28, 26 and 28 bots.
            VC.Scene battleground = Scene(20, 24);
            Assert.That(AutonomousBotDebates.LevelFits(battleground, 24), Is.True);
            Assert.That(AutonomousBotDebates.LevelFits(battleground, 26), Is.False, "No slack on a five-level band.");
            Assert.That(AutonomousBotDebates.LevelFits(battleground, 18), Is.False);
            VC.Scene newbie = Scene(1, 10);
            Assert.That(AutonomousBotDebates.LevelFits(newbie, 15), Is.False, "A 1-10 newbie scene is not played by level 15s.");
            VC.Scene wide = Scene(15, 40);
            Assert.That(AutonomousBotDebates.LevelFits(wide, 44), Is.True, "A wide scene keeps its five levels of slack.");
            Assert.That(AutonomousBotDebates.LevelFits(wide, 46), Is.False);

            var tooHigh = new[] { Bot(1, 28), Bot(2, 26), Bot(3, 28), Bot(4, 25) };
            Assert.That(AutonomousBotDebates.Cast(battleground, tooHigh, new Random(1)), Is.Null);
            var inBracket = tooHigh.Append(Bot(5, 22)).Append(Bot(6, 24)).Append(Bot(7, 20)).ToArray();
            Dictionary<string, long> cast = AutonomousBotDebates.Cast(battleground, inBracket, new Random(1));
            Assert.That(cast, Is.Not.Null);
            Assert.That(cast.Values.All(id => id is 5 or 6 or 7), Is.True, "Only the bots inside 20-24 speak.");
        }

        [Test]
        public void TheThidrankiScenesNeedBracketLevels()
        {
            foreach (string id in new[] { "sh_thid_tonight", "twinks", "rvr_thid_twinks", "rvr_thid_first_night" })
            {
                VC.Scene scene = Corpus.Scenes.Single(s => s.Id == id);
                Assert.That(scene.LevelLow, Is.GreaterThanOrEqualTo(20), id);
                Assert.That(scene.LevelHigh, Is.LessThanOrEqualTo(24), id);
                Assert.That(AutonomousBotDebates.SlackFor(scene), Is.Zero, id);
            }
        }

        [Test]
        public void RoleplayersAreNotCastInScenes()
        {
            // Owner's PC, 2026-11-18 02:15: a roleplayer said a scene line as "... forgive me everyone, huzzah!".
            VC.Scene scene = Scene(20, 40);
            var mostlyRoleplayers = Enumerable.Range(1, 6).Select(i => Bot(i, 30, roleplayer: true)).Append(Bot(10, 30)).Append(Bot(11, 30)).Append(Bot(12, 30)).ToArray();
            for (int seed = 0; seed < 20; seed++)
            {
                Dictionary<string, long> cast = AutonomousBotDebates.Cast(scene, mostlyRoleplayers, new Random(seed));
                Assert.That(cast, Is.Not.Null);
                Assert.That(cast.Values.All(id => id is 10 or 11 or 12), Is.True, "Only the three plain writers are cast.");
            }
            Assert.That(AutonomousBotDebates.Cast(scene, mostlyRoleplayers.Where(c => c.Roleplayer).ToArray(), new Random(1)), Is.Null);
            // A storyline's fixed protagonists are kept whoever they are.
            var fixedRoles = new Dictionary<string, long> { ["A"] = 1, ["B"] = 2 };
            Dictionary<string, long> story = AutonomousBotDebates.Cast(scene, mostlyRoleplayers.Skip(2).ToArray(), new Random(1), fixedRoles);
            Assert.That(story["A"], Is.EqualTo(1));
            Assert.That(story["C"], Is.AnyOf(10L, 11L, 12L));
        }

        [Test]
        public void TheSharedDungeonFollowsTheFirstSpeakerNotTheFirstRoleCast()
        {
            // B has a class requirement, so it is cast first; A still speaks first and its level picks {dungeon}.
            VC.Scene scene = Scene(10, 50, ("B", new VC.RoleSpec([], ["Cleric"])));
            var bots = new[] { Bot(1, 12), Bot(2, 45, "Cleric"), Bot(3, 20) };
            Dictionary<string, long> cast = AutonomousBotDebates.Cast(scene, bots, new Random(3));
            Assert.That(cast.Keys.First(), Is.EqualTo("B"), "The constrained role is cast first (the old opener).");
            Assert.That(AutonomousBotDebates.OpenerRole(scene, cast), Is.EqualTo("A"));
            var withPause = new VC.Scene("p", "t", "*", 10, 50, scene.Roles,
                [new VC.Beat("~", [], false, 5), new VC.Beat("C", ["x"], false, 0), new VC.Beat("A", ["y"], false, 0)]);
            Assert.That(AutonomousBotDebates.OpenerRole(withPause, new Dictionary<string, long> { ["A"] = 1, ["C"] = 2 }), Is.EqualTo("C"));
        }

        private static IEnumerable<(VC.Scene Scene, string Text)> SceneWordings() =>
            Corpus.Scenes.Where(s => !s.Topic.StartsWith("story_", StringComparison.Ordinal) && !s.Topic.StartsWith("tale_", StringComparison.Ordinal))
                .SelectMany(s => s.Beats.Where(b => !b.IsPause).SelectMany(b => b.Variants).Select(v => (s, v)));

        [Test]
        public void SceneLinesDoNotNameRolesOrPromiseInvites()
        {
            // corpus-b1 -001: "B says void, C says light"; -018: "inv sent", "sending invites", "we have room".
            var roleLetter = new Regex(@"(^|[\s,.])[A-H]($|[\s,.])");
            var invite = new Regex(@"\b(inv sent|invite sent|invites? (going out|out|coming)|sending (an |you an )?invites?|(?<!before )inviting (you|them)|we have (a spot|room))\b");
            foreach ((VC.Scene scene, string text) in SceneWordings())
            {
                // A shouted all-caps line (the dunce's rage quit) is not a role letter.
                if (text != text.ToUpperInvariant())
                    Assert.That(roleLetter.IsMatch(text), Is.False, $"{scene.Id}: \"{text}\" names a role letter");
                Assert.That(invite.IsMatch(text), Is.False, $"{scene.Id}: \"{text}\" says an invite was sent");
            }
        }

        [Test]
        public void SceneLinesStateNoClockTimeOutsideATimeScene()
        {
            // corpus-b1 -017: "3am and still on", "its 4am" were said at any hour.
            var clock = new Regex(@"\b\d ?[ap]m\b");
            foreach ((VC.Scene scene, string text) in SceneWordings())
                if (clock.IsMatch(text))
                    Assert.That(scene.Needs, Is.Not.Empty, $"{scene.Id}: \"{text}\"");
        }

        private static VC.Scene Live(string id, params string[] needs) =>
            new VC.Scene(id, "keep_live", "*", 20, 50, new Dictionary<string, VC.RoleSpec>(StringComparer.Ordinal),
                [new VC.Beat("A", ["{keep} is under attack"], false, 0), new VC.Beat("B", ["how many"], false, 0)]) { Needs = needs };

        [Test]
        public void ALiveScenePlaysOnlyWhileItsNewsIsTrueAndComesFirst()
        {
            VC.Scene talk = Scene(20, 50);
            VC.Scene siege = Live("siege", "defending");
            VC.Scene[] scenes = [talk, siege];
            int[] levels = [30, 35, 40];
            var random = new Random(2);
            for (int i = 0; i < 20; i++)
                Assert.That(AutonomousBotDebates.ChooseScene(scenes, eRealm.Albion, new HashSet<string>(), levels, random, out _, "evening")?.Id,
                    Is.EqualTo("test"), "No siege: the live scene never plays.");
            var defending = new HashSet<string>(StringComparer.Ordinal) { "defending" };
            for (int i = 0; i < 20; i++)
                Assert.That(AutonomousBotDebates.ChooseScene(scenes, eRealm.Albion, new HashSet<string>(), levels, random, out _, "evening",
                    defending)?.Id, Is.EqualTo("siege"), "While a keep is under siege its scene comes first.");
            Assert.That(AutonomousBotDebates.ChooseScene(scenes, eRealm.Albion, new HashSet<string> { "siege" }, levels, random, out _, "evening",
                defending, liveOnly: true), Is.Null, "An early live start never repeats a live scene or plays ordinary talk.");
            Assert.That(AutonomousBotDebates.LiveSceneWaiting(scenes, eRealm.Albion, new HashSet<string>(), defending), Is.True);
            Assert.That(AutonomousBotDebates.LiveSceneWaiting(scenes, eRealm.Albion, new HashSet<string> { "siege" }, defending), Is.False);
            Assert.That(AutonomousBotDebates.LiveSceneWaiting(scenes, eRealm.Albion, new HashSet<string>(), new HashSet<string>()), Is.False);
            VC.Scene notOurs = Live("lost", "df_changed_recent", "!df_ours");
            Assert.That(AutonomousBotDebates.LiveSceneWaiting([notOurs], eRealm.Albion, new HashSet<string>(),
                new HashSet<string> { "df_changed_recent", "df_ours" }), Is.False, "We just got DF: the 'lost df' scene stays quiet.");
        }

        [Test]
        public void ALiveSceneNamesTheKeepAndTheEnemyOfItsNews()
        {
            VC.Scene siege = Live("siege", "defending");
            var needSlots = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["defending"] = new Dictionary<string, string> { ["keep"] = "Caer Benowyc", ["enemy"] = "Midgard", ["zone"] = "Pennine Mountains" },
                ["keep_lost_recent"] = new Dictionary<string, string> { ["keep"] = "Caer Boldiam" },
            };
            Dictionary<string, string> slots = AutonomousBotDebates.LiveSlots(siege, needSlots);
            Assert.That(slots["keep"], Is.EqualTo("Caer Benowyc"), "The besieged keep, not the lost one or a random one.");
            Assert.That(slots["enemy"], Is.EqualTo("mids"), "The attacker, as the realm's players call them.");
            Assert.That(slots.ContainsKey("zone"), Is.False, "{zone} stays the speaker's own.");
        }

        [Test]
        public void TheLiveScenesAreWrittenForEveryRealmAndPromiseNothing()
        {
            // Owner, 2026-11-18: "if they are the best scenes then let's double the number written". Nine live scenes were
            // flattened into talk; eighteen are written now, and a bot that says "omw" must really come (none here does).
            VC.Scene[] live = Corpus.Scenes.Where(AutonomousBotDebates.IsLive).ToArray();
            Assert.That(live.Length, Is.GreaterThanOrEqualTo(18));
            foreach (string realm in new[] { "*", "alb", "mid", "hib" })
                Assert.That(live.Any(s => s.Realm == realm && s.Needs.Contains("defending")), Is.True, realm);
            Assert.That(live.Select(s => s.Needs.First(n => !n.StartsWith('!'))).Distinct(),
                Is.SupersetOf(new[] { "defending", "attacking", "keep_lost_recent", "keep_won_recent", "relic_lost_recent", "relic_won_recent" }));
            var promise = new Regex(@"\b(omw|on my way|riding (in|out|over)|coming,|im coming|i will come|i will be there|heading over|count me in)\b");
            foreach ((VC.Scene scene, string text) in SceneWordings())
                Assert.That(promise.IsMatch(text), Is.False, $"{scene.Id}: \"{text}\" promises to come");
        }

        [Test]
        public void NotEveryDebateEndsOnTheAskersQuip()
        {
            // corpus-b1 -021: all 70 debates ended on A's line.
            string[] files = ["debates", "debates_general", "debates_realms"];
            VC.Scene[] debates = Corpus.Scenes.Where(s => s.Beats.All(b => b.IsPause || b.Variants.Length == 1) && s.Beats.Length <= 9 &&
                                                          !s.Topic.StartsWith("story_", StringComparison.Ordinal)).ToArray();
            Assert.That(debates.Length, Is.GreaterThan(40), string.Join(",", files));
            int otherEnd = debates.Count(s => s.Beats.Last(b => !b.IsPause).Role != "A");
            Assert.That(otherEnd, Is.GreaterThanOrEqualTo(debates.Length / 4), "B or C has the last word in about a third of them.");
        }
    }
}
