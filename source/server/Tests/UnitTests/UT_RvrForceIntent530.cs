using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using L = DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #530 (RvR work 12, piece C): the dead director is gone, the siege rule is written once, and the event layer is the
    /// one writer of a force's intent. Unit tests only: nothing here has been seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrForceIntent530
    {
        private static string GameServerRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return Path.Combine(dir, "source", "server", "GameServer");
        }

        private static string StripComments(string code) =>
            Regex.Replace(Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//[^\n]*", string.Empty);

        [SetUp]
        public void Reset() => L.ResetForceIntents();

        // ---- step 1: the dead layer ------------------------------------------------------------------------------------

        [Test]
        public void Every_Rvr_type_in_the_bots_has_a_caller()
        {
            // The CI check (#530): a production type in an Rvr*/AutonomousRvr* file that nothing uses is dead code with a
            // green test, as AutonomousRvrDirector was. Command handlers are loaded by attribute and live elsewhere.
            string root = GameServerRoot();
            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToArray();
            string all = string.Join("\n", files.Select(f => StripComments(File.ReadAllText(f))));
            var declaration = new Regex(@"^(?: {4}|\t)?(?:public|internal)\s+(?:(?:static|sealed|abstract|partial|readonly)\s+)*" +
                                        @"(?:class|record|struct|enum|interface)\s+(?:struct\s+|class\s+)?(\w+)", RegexOptions.Multiline);
            string[] candidates = files.Where(f => f.Contains($"{Path.DirectorySeparatorChar}bots{Path.DirectorySeparatorChar}") &&
                                                   Regex.IsMatch(Path.GetFileName(f), @"^(Autonomous)?Rvr")).ToArray();
            Assert.That(candidates.Length, Is.GreaterThan(40));
            int types = 0;
            Assert.Multiple(() =>
            {
                foreach (string file in candidates)
                    foreach (string name in declaration.Matches(StripComments(File.ReadAllText(file))).Select(m => m.Groups[1].Value).Distinct())
                    {
                        types++;
                        int uses = Regex.Matches(all, $@"\b{name}\b").Count;
                        int declared = Regex.Matches(all, $@"(?:class|record|struct|enum|interface)\s+{name}\b").Count;
                        Assert.That(uses - declared, Is.GreaterThan(0), $"{Path.GetFileName(file)}: {name} has no caller");
                    }
            });
            Assert.That(types, Is.GreaterThan(150));
            Assert.That(File.Exists(Path.Combine(root, "bots", "autonomous", "AutonomousRvrDirector.cs")), Is.False);
        }

        [Test]
        public void One_strength_counts_every_healing_class()
        {
            Assert.Multiple(() =>
            {
                foreach (eCharacterClass healer in new[] { eCharacterClass.Cleric, eCharacterClass.Friar, eCharacterClass.Druid,
                             eCharacterClass.Bard, eCharacterClass.Warden, eCharacterClass.Healer, eCharacterClass.Shaman })
                    Assert.That(ForceStrength.Heals(healer), Is.True, healer.ToString());
                foreach (eCharacterClass other in new[] { eCharacterClass.Minstrel, eCharacterClass.Skald, eCharacterClass.Armsman,
                             eCharacterClass.Wizard, eCharacterClass.Ranger, eCharacterClass.Unknown })
                    Assert.That(ForceStrength.Heals(other), Is.False, other.ToString());
            });
            ForceStrength midgard = ForceStrength.Of(new[] { (eCharacterClass.Shaman, 50), (eCharacterClass.Shaman, 50),
                (eCharacterClass.Warrior, 49), (eCharacterClass.Skald, 50) });
            Assert.That(midgard, Is.EqualTo(new ForceStrength(4, 50, 49, 2)), "two Shamans; the old substring count saw none");
            Assert.That(midgard.CanSiege, Is.True);
            Assert.That(midgard.AtSiegeLevel, Is.False, "one member below 50");
            Assert.That(ForceStrength.Of(Array.Empty<(eCharacterClass, int)>()), Is.EqualTo(new ForceStrength(0, 0, 0, 0)));
        }

        [Test]
        public void The_one_siege_rule_is_the_event_layers_old_rule()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new ForceStrength(4, 50, 50, 0).CanSiege, Is.True);
                Assert.That(new ForceStrength(3, 50, 50, 0).CanSiege, Is.False);
                Assert.That(new ForceStrength(8, 49, 49, 3).CanSiege, Is.False);
                Assert.That(new L.Force("f", eRealm.Albion, 8, 50, 0, CanSupplySiege: false).SiegeCapable, Is.False, "the force's own say");
                // The controller's level-35 rule decided nothing: below 50 a force that could supply a siege and one that
                // could not choose the same, at every roll.
                foreach (int level in new[] { 30, 35, 40, 49 })
                    foreach (double roll in new[] { 0, .05, .5, .99 })
                        foreach (bool enemy in new[] { false, true })
                            Assert.That(L.ChooseIntent(new L.Force("a", eRealm.Albion, 8, level, 2, true), enemy, true, true, roll),
                                Is.EqualTo(L.ChooseIntent(new L.Force("b", eRealm.Albion, 8, level, 2, false), enemy, true, true, roll)),
                                $"level {level} roll {roll}");
                Assert.That(L.ChooseIntent(new L.Force("big", eRealm.Albion, 8, 50, 2), false, true, true, .10),
                    Is.EqualTo(L.Intent.AssaultRelicKeep), "unchanged at 50");
                Assert.That(L.ChooseIntent(new L.Force("five", eRealm.Albion, 5, 50, 0), false, false, true, 0),
                    Is.EqualTo(L.Intent.Roam), "a relic siege still needs six");
            });
        }

        // ---- steps 2-4: one intent, proposals, one write per turn ----------------------------------------------------

        [Test]
        public void The_rules_moved_to_the_event_layer_are_the_old_rules()
        {
            Assert.Multiple(() =>
            {
                Assert.That(L.AcceptsRoamOverride(L.Intent.Roam, false, "rvr-camp-1"), Is.True);
                Assert.That(L.AcceptsRoamOverride(L.Intent.Roam, true, "rvr-camp-1"), Is.False, "an event plan");
                foreach (L.Intent kept in new[] { L.Intent.DefendEvent, L.Intent.AssaultKeep, L.Intent.AssaultRelicKeep })
                    Assert.That(L.AcceptsRoamOverride(kept, false, "rvr-keep-1"), Is.False, kept.ToString());
                Assert.That(L.AcceptsRoamOverride(L.Intent.HuntEnemy, false, "rvr-enemy-9"), Is.True, "a force hunt: the #417 rule decides");
                Assert.That(L.AcceptsRoamOverride(L.Intent.HuntEnemy, false, "rvr-relic-carrier-1"), Is.False);
                Assert.That(L.AcceptsRoamOverride(L.Intent.HuntEnemy, false, null), Is.True);
                Assert.That(L.AcceptsChatPromise(new(L.Intent.Roam, false)), Is.True);
                Assert.That(L.AcceptsChatPromise(new(L.Intent.DefendEvent, false)), Is.True, "a promise may follow a promise");
                Assert.That(L.AcceptsChatPromise(new(L.Intent.AssaultKeep, true)), Is.False, "never over an event plan");
            });
        }

        [Test]
        public void A_roam_override_is_decided_with_its_plan()
        {
            var turn = new L.ForceTurn();
            turn.Begin("f-roam", writer: true);
            L.ForceIntent roam = turn.Propose(new(L.Intent.Roam, false), new(L.IntentSource.Plan, L.Intent.Roam, "rvr-camp-1", false, -1,
                "plan.roam", null, "rvr-spot-7", "roam.override"), 1000);
            Assert.That(roam.Source, Is.EqualTo(L.IntentSource.RoamOverride));
            Assert.That(roam.ObjectiveId, Is.EqualTo("rvr-spot-7"));
            Assert.That(roam.Rule, Is.EqualTo("roam.override"));

            L.ForceIntent siege = turn.Propose(new(L.Intent.Roam, false), new(L.IntentSource.Plan, L.Intent.AssaultKeep, "rvr-keep-3", true, -1,
                "plan.assaultKeep", null, "rvr-spot-7", "roam.override"), 1000);
            Assert.That(siege.Source, Is.EqualTo(L.IntentSource.Plan), "a siege plan is kept over the circuit");
            Assert.That(siege.ObjectiveId, Is.EqualTo("rvr-keep-3"));
            Assert.That(siege.Role, Is.EqualTo(L.ForceRole.Attacker));
        }

        [Test]
        public void One_write_per_turn()
        {
            const string force = "f-turn";
            var turn = new L.ForceTurn();
            // The old turn wrote up to four times; now each proposal is decided and the force is written once.
            turn.Begin(force, writer: true);
            turn.Propose(new(L.Intent.Roam, false), new(L.IntentSource.Released, L.Intent.Roam, null, false, null, "event.ended"), 10);
            turn.Propose(new(L.Intent.Roam, false), new(L.IntentSource.Plan, L.Intent.HuntEnemy, "rvr-enemy-4", false, -1, "plan.huntEnemy"), 10);
            turn.Propose(new(L.Intent.HuntEnemy, false), new(L.IntentSource.Committed, L.Intent.DefendEvent, "rvr-keep-2", true, null, "exec.committed"), 10);
            Assert.That(L.ForceIntentWrites(force), Is.Zero, "nothing written mid-turn");
            Assert.That(turn.End(null, 10), Is.True);
            Assert.That(L.ForceIntentWrites(force), Is.EqualTo(1));
            Assert.That(turn.Overruled, Is.EqualTo(new[] { "event.ended", "plan.huntEnemy" }), "the row names what it overruled");
            L.ForceIntent written = L.IntentOf(force);
            Assert.That(written.Intent, Is.EqualTo(L.Intent.DefendEvent));
            Assert.That(written.Rule, Is.EqualTo("exec.committed"));
            Assert.That(written.DefendingKeep, Is.EqualTo(-1), "the committed plan keeps the keep it had");

            // The same decision next turn writes nothing (change-only); a follower's turn never writes the force.
            turn.Begin(force, writer: true);
            turn.Propose(new(L.Intent.DefendEvent, true), new(L.IntentSource.Committed, L.Intent.DefendEvent, "rvr-keep-2", true, null, "exec.committed"), 20);
            Assert.That(turn.End(null, 20), Is.False);
            turn.Begin(force, writer: false);
            turn.Propose(new(L.Intent.Roam, false), new(L.IntentSource.Plan, L.Intent.Roam, "rvr-camp-1", false, -1, "plan.roam"), 30);
            Assert.That(turn.End(null, 30), Is.False);
            Assert.That(L.ForceIntentWrites(force), Is.EqualTo(1));
            Assert.That(L.IntentOf(force).SinceTick, Is.EqualTo(10));
        }

        [Test]
        public void A_chat_promise_proposes_and_is_refused_over_an_event_plan()
        {
            var turn = new L.ForceTurn();
            turn.Begin("f-chat", writer: true);
            Assert.That(turn.Propose(new(L.Intent.AssaultKeep, true),
                new(L.IntentSource.ChatPromise, L.Intent.DefendEvent, "rvr-keep-5", null, 5, "chat.promise"), 1), Is.Null);
            Assert.That(turn.Overruled, Is.EqualTo(new[] { "chat.promise:refused" }));
            L.ForceIntent promise = turn.Propose(new(L.Intent.Roam, false),
                new(L.IntentSource.ChatPromise, L.Intent.DefendEvent, "rvr-keep-5", null, 5, "chat.promise"), 1);
            Assert.That(promise.Shared, Is.False, "a promise registers no event");
            Assert.That(promise.DefendingKeep, Is.EqualTo(5));
            Assert.That(turn.End(null, 1), Is.True);
        }

        [Test]
        public void The_shadow_compares_a_bots_fields_with_its_force()
        {
            Assert.That(L.ShadowAgrees(null, L.Intent.HuntEnemy, true), Is.True, "no record: nothing to compare");
            var record = new L.ForceIntent("f", L.Intent.AssaultKeep, "rvr-keep-1", L.ForceRole.Attacker, "-", 0, L.IntentSource.Committed,
                "exec.committed", null, true, -1);
            Assert.That(L.ShadowAgrees(record, L.Intent.AssaultKeep, true), Is.True);
            Assert.That(L.ShadowAgrees(record, L.Intent.AssaultKeep, false), Is.False);
            Assert.That(L.ShadowAgrees(record, L.Intent.Roam, true), Is.False);
            Assert.That(L.Shadow(null, "nobody", L.Intent.Roam, false), Is.True);
            Assert.That(L.ShadowChecks, Is.Zero);
        }

        [Test]
        public void The_controller_changes_its_intent_only_through_the_event_layer()
        {
            // Source pin: every write of _rvrIntent and _rvrSharedEvent is in ProposeRvrIntent, except the follower's read
            // of its force's intent; RvrWarbandIntent and RvrDefendingKeep are set only there (as a mirror of the decision).
            string dir = Path.Combine(GameServerRoot(), "bots", "autonomous");
            var intentWrite = new Regex(@"_rvrIntent\s*=[^=]|_rvrSharedEvent\s*=[^=]");
            var mirror = new Regex(@"SetProperty\(""Rvr(WarbandIntent|DefendingKeep)""");
            int intentWrites = 0, mirrors = 0;
            foreach (string file in Directory.GetFiles(dir, "AutonomousWorldBotController*.cs"))
                foreach (string line in StripComments(File.ReadAllText(file)).Split('\n'))
                {
                    if (intentWrite.IsMatch(line))
                    {
                        intentWrites++;
                        Assert.That(line, Does.Match(@"decided\.(Intent|Shared)|IntentOf\(forceId\)"), $"{Path.GetFileName(file)}: {line.Trim()}");
                    }
                    if (mirror.IsMatch(line))
                        mirrors++;
                }
            Assert.That(intentWrites, Is.EqualTo(3), "two in ProposeRvrIntent, one follower read");
            Assert.That(mirrors, Is.EqualTo(2), "the two mirror lines in ProposeRvrIntent");
        }
    }
}
