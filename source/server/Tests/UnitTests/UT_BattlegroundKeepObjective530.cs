using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using L = DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #530 step 5a (piece C, structure.md finding 8): the battleground keep is an event-layer objective with its wave as the
    /// muster, and one gather-hold rule replaces the three copies. Unit tests only: nothing here has been seen in game.
    /// Autonomous gamebots only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BattlegroundKeepObjective530
    {
        [SetUp]
        public void Reset() => L.ResetForceIntents();

        private static string Code(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            string text = File.ReadAllText(Path.Combine(new[] { dir, "source", "server", "GameServer" }.Concat(parts).ToArray()));
            return Regex.Replace(Regex.Replace(text.Replace("\r\n", "\n"), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
                @"//[^\n]*", string.Empty);
        }

        // ---- the order -------------------------------------------------------------------------------------------------

        [Test]
        public void The_order_names_the_keep_and_the_part()
        {
            Assert.Multiple(() =>
            {
                Assert.That(L.DecideBattleground(null, false, false, false, 7),
                    Is.EqualTo(new L.BattlegroundOrder(L.BattlegroundStep.Roam, null, "bg.roam.nokeep")));
                Assert.That(L.DecideBattleground(12, true, false, false, 9),
                    Is.EqualTo(new L.BattlegroundOrder(L.BattlegroundStep.Hold, "bg-keep-12", "bg.hold")), "a third holds the walls");
                Assert.That(L.DecideBattleground(12, true, false, false, 10),
                    Is.EqualTo(new L.BattlegroundOrder(L.BattlegroundStep.Roam, "bg-keep-12", "bg.roam.held")), "the rest roam");
                Assert.That(L.DecideBattleground(12, false, true, true, 10),
                    Is.EqualTo(new L.BattlegroundOrder(L.BattlegroundStep.Roam, "bg-keep-12", "bg.roam.toofew")), "too few: the field");
                Assert.That(L.DecideBattleground(12, false, false, true, 10),
                    Is.EqualTo(new L.BattlegroundOrder(L.BattlegroundStep.Muster, "bg-keep-12", "bg.muster")));
                Assert.That(L.DecideBattleground(12, false, false, false, 10),
                    Is.EqualTo(new L.BattlegroundOrder(L.BattlegroundStep.Assault, "bg-keep-12", "bg.wave")));
                Assert.That(L.IsBattlegroundObjective("bg-keep-12"), Is.True);
                Assert.That(L.IsBattlegroundObjective("rvr-keep-12"), Is.False);
                Assert.That(L.IsBattlegroundObjective(null), Is.False);
            });
        }

        [Test]
        public void The_intent_a_bot_acts_on_is_the_old_rule_in_every_case()
        {
            // Before 5a the controller proposed AssaultKeep when another realm (or nobody) held the keep and enough of the
            // realm were inside, else Roam; and a third of the holder's parts (partId % 3 == 0) held the walls.
            foreach (bool keep in new[] { false, true })
                foreach (bool held in new[] { false, true })
                    foreach (bool tooFewRaw in new[] { false, true })
                        foreach (bool mustering in new[] { false, true })
                            for (long part = 0; part < 6; part++)
                            {
                                if (!keep && held) continue;
                                bool assault = keep && !held;
                                bool tooFew = assault && tooFewRaw;
                                L.BattlegroundOrder order = L.DecideBattleground(keep ? 3 : null, held, tooFew, mustering, part);
                                string at = $"keep={keep} held={held} tooFew={tooFew} mustering={mustering} part={part}";
                                Assert.That(order.Intent, Is.EqualTo(assault && !tooFew ? L.Intent.AssaultKeep : L.Intent.Roam), at);
                                Assert.That(order.Attacks, Is.EqualTo(assault && !tooFew), at);
                                Assert.That(order.Step == L.BattlegroundStep.Hold, Is.EqualTo(keep && held && part % 3 == 0), at);
                                Assert.That(L.HoldsBattlegroundWalls(part), Is.EqualTo(part % 3 == 0), at);
                            }
        }

        [Test]
        public void The_order_is_proposed_with_its_role_never_as_an_event()
        {
            L.IntentProposal muster = L.DecideBattleground(4, false, false, true, 1).Proposal;
            Assert.That(muster.Source, Is.EqualTo(L.IntentSource.Battleground));
            Assert.That(muster.Shared, Is.False);
            Assert.That(muster.ObjectiveId, Is.EqualTo("bg-keep-4"));
            Assert.That(muster.Role, Is.EqualTo(L.ForceRole.Muster));
            Assert.That(L.DecideBattleground(4, false, false, false, 1).Proposal.Role, Is.EqualTo(L.ForceRole.Attacker));
            Assert.That(L.DecideBattleground(4, true, false, false, 3).Proposal.Role, Is.EqualTo(L.ForceRole.Defender));
            Assert.That(L.DecideBattleground(4, true, false, false, 4).Proposal.Role, Is.EqualTo(L.ForceRole.Roamer));
        }

        [Test]
        public void The_wave_going_in_is_one_write_and_one_logged_change()
        {
            const string force = "bg:test-force";
            var turn = new L.ForceTurn();
            L.IntentView view = new(L.Intent.Roam, false);

            turn.Begin(force, writer: true);
            L.ForceIntent mustered = turn.Propose(view, L.DecideBattleground(8, false, false, true, 1).Proposal, 100);
            Assert.That(turn.End(null, 100), Is.True);
            Assert.That(mustered.Summary, Is.EqualTo("AssaultKeep@bg-keep-8/muster"));
            Assert.That(L.IntentOf(force).Role, Is.EqualTo(L.ForceRole.Muster));

            // The same muster next turn writes nothing.
            turn.Begin(force, writer: true);
            turn.Propose(new(L.Intent.AssaultKeep, false), L.DecideBattleground(8, false, false, true, 1).Proposal, 200);
            Assert.That(turn.End(null, 200), Is.False);

            // The wave goes in: the same intent and keep, a new role, one write, and a summary that differs (RVR_DECIDE is
            // change-only on it).
            turn.Begin(force, writer: true);
            L.ForceIntent going = turn.Propose(new(L.Intent.AssaultKeep, false), L.DecideBattleground(8, false, false, false, 1).Proposal, 300);
            Assert.That(turn.End(null, 300), Is.True);
            Assert.That(going.Summary, Is.EqualTo("AssaultKeep@bg-keep-8"));
            Assert.That(L.IntentOf(force).Role, Is.EqualTo(L.ForceRole.Attacker));
            Assert.That(L.IntentOf(force).Rule, Is.EqualTo("bg.wave"));
            Assert.That(L.ForceIntentWrites(force), Is.EqualTo(2));
            Assert.That(L.IntentOf(force).SinceTick, Is.EqualTo(100), "the same objective since the muster");

            // A wall holder is named too; a frontier record's summary is unchanged.
            Assert.That(L.Decide("bg:h", view, null, L.DecideBattleground(8, true, false, false, 3).Proposal, 1).Summary,
                Is.EqualTo("Roam@bg-keep-8/defender"));
            Assert.That(L.Decide("bg:r", view, null, L.DecideBattleground(8, true, false, false, 4).Proposal, 1).Summary,
                Is.EqualTo("Roam@bg-keep-8"));
            Assert.That(L.Decide("f", view, null, new(L.IntentSource.Plan, L.Intent.AssaultKeep, "rvr-keep-3", true, -1, "plan.assaultKeep"), 1).Summary,
                Is.EqualTo("AssaultKeep@rvr-keep-3+event"));
        }

        // ---- the one gather-hold rule ----------------------------------------------------------------------------------

        [Test]
        public void One_gather_hold_rule()
        {
            Assert.Multiple(() =>
            {
                foreach (bool isLord in new[] { false, true })
                    foreach (bool comes in new[] { false, true })
                        foreach (bool posted in new[] { false, true })
                            foreach (bool stormer in new[] { false, true })
                            {
                                string at = $"lord={isLord} comes={comes} posted={posted} stormer={stormer}";
                                Assert.That(L.MusterAllowsFight(L.MusterHold.None, isLord, comes, posted, stormer), Is.True, "not held: " + at);
                                Assert.That(L.MusterAllowsFight(L.MusterHold.Storm, isLord, comes, posted, stormer), Is.EqualTo(stormer), "storm: " + at);
                                foreach (L.MusterHold held in new[] { L.MusterHold.Gather, L.MusterHold.Rally })
                                    Assert.That(L.MusterAllowsFight(held, isLord, comes, posted, stormer), Is.EqualTo(!isLord && (comes || posted)),
                                        held + ": " + at);
                            }
            });
        }

        [Test]
        public void The_three_sites_use_the_one_rule()
        {
            string battleground = Code("bots", "autonomous", "AutonomousWorldBotController.Battleground.cs");
            string threat = Code("bots", "autonomous", "AutonomousWorldBotController.FrontierThreat.cs");
            string controller = Code("bots", "autonomous", "AutonomousWorldBotController.cs");
            Assert.Multiple(() =>
            {
                // The threat scan, the combat drop and the keep's guard pick each call it.
                Assert.That(threat, Does.Contain("MusterAllowsFight(bot, hold, gatherStaging, target)"));
                Assert.That(battleground, Does.Contain("if (MusterAllowsFight(bot, hold, staging, target)) return false;"));
                Assert.That(controller, Does.Contain("MusterAllowsFight(bot, AutonomousRvrEventLayer.MusterHold.Rally, rally, guard)"));
                // The gatherers' test is called only inside the rule (and defined once).
                string all = battleground + threat + controller;
                Assert.That(Regex.Matches(all, @"GathererMayEngage\(bot,").Count, Is.EqualTo(1), "only MusterAllowsFight calls it");
                // The order comes from the event layer: no inline wall share and no battleground proposal built in the controller.
                Assert.That(battleground, Does.Contain("AutonomousRvrEventLayer.DecideBattleground("));
                Assert.That(battleground, Does.Not.Contain("partId % 3"));
                Assert.That(battleground, Does.Not.Contain("IntentSource.Battleground"));
            });
        }
    }
}
