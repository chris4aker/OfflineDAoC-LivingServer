using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using DOL.GS.Styles;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR tactics, work package 0 (docs/RVR-FIELD-1.2.md): the hook points and the settings. Every hook is a
    /// stub: with every switch on, for a bot the tactics apply to, it has no opinion, so the code at its site runs as
    /// before; and no stub builds a fight board. Gamebots without a world (DutyLeaseTestKit).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrTacticsHooks : DutyLeaseFixture
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>Field name, setting name, the work package it is reserved for (0: the master switch).</summary>
        private static readonly (string Field, string Key, int WorkPackage)[] Switches =
        [
            ("RVR_BOT_FIELD_TACTICS", "rvr_bot_field_tactics", 0),
            ("RVR_BOT_PERCEPTION_BOTS", "rvr_bot_perception_bots", 1),
            ("RVR_BOT_GROUP_LOOKERS", "rvr_bot_group_lookers", 1),
            ("RVR_BOT_ASSIST", "rvr_bot_assist", 2),
            ("RVR_BOT_CC_PLAN", "rvr_bot_cc_plan", 2),
            ("RVR_BOT_INTERRUPTS", "rvr_bot_interrupts", 2),
            ("RVR_BOT_PEEL", "rvr_bot_peel", 3),
            ("RVR_BOT_POSITIONING", "rvr_bot_positioning", 3),
            ("RVR_BOT_SIZE_UP", "rvr_bot_size_up", 4),
            ("RVR_BOT_DISENGAGE", "rvr_bot_disengage", 4),
            ("RVR_BOT_REGROUP", "rvr_bot_regroup", 4),
            ("RVR_MARCH_LANES", "rvr_march_lanes", 5),
            ("RVR_ROAM_LANES", "rvr_roam_lanes", 5),
            ("RVR_ROAMING_PARTIES", "rvr_roaming_parties", 6),
            ("RVR_ROAM_CHECKS", "rvr_roam_checks", 6),
            ("RVR_BOT_STEALTH_OPENERS", "rvr_bot_stealth_openers", 6),
        ];

        private static FieldInfo Field(string name) => typeof(Properties).GetField(name) ?? throw new MissingFieldException(name);

        private readonly Dictionary<string, bool> _before = new();

        [SetUp]
        public void EverySwitchOn()
        {
            BotDutyLeases.Clear();
            foreach ((string field, _, _) in Switches)
            {
                _before[field] = (bool)Field(field).GetValue(null)!;
                Field(field).SetValue(null, true);
            }
        }

        [TearDown]
        public void RestoreSwitches()
        {
            foreach ((string field, bool value) in _before)
                Field(field).SetValue(null, value);
            BotDutyLeases.Clear();
        }

        /// <summary>An RvR gamebot in a battleground region (a battle region that needs no zone), with a brain.</summary>
        private static (Bot Bot, BotBrain Brain) FieldBot(ushort region = 250)
        {
            Bot bot = Gamebot(objective: "RvR");
            bot.Region = region;
            var brain = new BotBrain { Body = bot };
            return (bot, brain);
        }

        // ---- the gate -----------------------------------------------------------------------------------------------------

        [Test]
        public void The_gate_is_the_master_switch_a_battle_region_and_the_scope()
        {
            (Bot inScope, BotBrain brain) = FieldBot();
            Assert.That(BotBrain.RvrTacticsApplyTo(inScope), Is.True);
            Assert.That(brain.RvrTacticsActive(), Is.True);

            (Bot atHome, BotBrain homeBrain) = FieldBot(region: 1);
            Assert.That(BotBrain.RvrTacticsApplyTo(atHome), Is.False, "not in the frontier, a battleground or a battle dungeon");
            Assert.That(homeBrain.RvrTacticsActive(), Is.False);

            (Bot raider, BotBrain raidBrain) = FieldBot();
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            Assert.That(raidBrain.RvrTacticsActive(), Is.False, "a raider is out of scope");

            Bot companion = Companion();
            companion.Region = 250;
            Assert.That(BotBrain.RvrTacticsApplyTo(companion), Is.False);
            Assert.That(new BotBrain { Body = companion }.RvrTacticsActive(), Is.False);

            Player player = RealPlayer();
            (Bot invited, BotBrain invitedBrain) = FieldBot();
            JoinPlayersGroup(invited, player);
            Assert.That(invitedBrain.RvrTacticsActive(), Is.False, "the player's group");

            Assert.That(BotBrain.RvrTacticsApplyTo(null), Is.False);
            Assert.That(new BotBrain().RvrTacticsActive(), Is.False, "a brain with no body");

            Properties.RVR_BOT_FIELD_TACTICS = false;
            (Bot off, BotBrain offBrain) = FieldBot();
            Assert.That(BotBrain.RvrTacticsApplyTo(off), Is.False);
            Assert.That(offBrain.RvrTacticsActive(), Is.False);
            Assert.That(brain.RvrTacticsActive(), Is.False, "the master switch is read on every call, before the tick's kept answer");
        }

        // ---- every stub has no opinion ------------------------------------------------------------------------------------

        private static Style StealthOpener() =>
            new(new DbStyle { Name = "opener", ID = 335, SpecKeyName = Specs.Critical_Strike, SpecLevelRequirement = 2, StealthRequirement = true }, null);

        [Test]
        public void With_every_switch_on_each_brain_hook_has_no_opinion()
        {
            (Bot bot, BotBrain brain) = FieldBot();
            Bot enemy = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            enemy.Region = 250;
            long builds = RvrFightBoards.Builds;
            Assert.That(brain.RvrTacticsActive(), Is.True, "the tactics do apply to this bot: the stubs are asked for real");
            Assert.Multiple(() =>
            {
                Assert.That(brain.RvrTacticsTarget(enemy, null, null), Is.Null, "target pick");
                Assert.That(brain.RvrTacticsTarget(null, enemy, enemy), Is.Null);
                Assert.That(brain.RvrTacticsCrowdControl(), Is.Null, "crowd control entry");
                Assert.That(brain.TryRvrTacticsSupport(), Is.False, "support turn");
                Assert.That(brain.RvrTacticsPeel(), Is.False, "tank peel");
                Assert.That(brain.RvrTacticsProtectionTarget(out GameLiving protect), Is.False, "protection target");
                Assert.That(protect, Is.Null);
                Assert.That(brain.TryRvrTacticsMove(enemy), Is.False, "attack-turn movement");
                Assert.That(BotBrain.RvrTacticsAllowsStealthOpener(bot, StealthOpener()), Is.False, "stealth opener");
                Assert.That(BotBrain.RvrTacticsAllowsStealthOpener(bot, null), Is.False);
                Assert.That(BotMeleeStylePolicy.IsEligible(StealthOpener()), Is.False, "so a stealth style stays out, as before");
                Assert.That(RvrFightBoards.Builds, Is.EqualTo(builds), "no stub builds a fight board");
                Assert.That(RvrFightBoards.Peek(bot), Is.Null);
                Assert.That(brain.PeekRvrPlan(), Is.Null);
            });
        }

        [Test]
        public void With_every_switch_on_the_controller_and_coordinator_hooks_have_no_opinion()
        {
            (Bot bot, BotBrain brain) = FieldBot();
            Bot enemy = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            var controller = new AutonomousWorldBotController();
            Type type = typeof(AutonomousWorldBotController);
            GameLiving[] visible = [enemy];
            long builds = RvrFightBoards.Builds;
            Assert.That(brain.RvrTacticsActive(), Is.True);

            MethodInfo gate = type.GetMethod("RvrTacticsGateThreats", Private)!;
            MethodInfo hold = type.GetMethod("RvrTacticsTravelHold", Private)!;
            MethodInfo roam = type.GetMethod("RvrTacticsRoamDestination", Private)!;
            Assert.Multiple(() =>
            {
                Assert.That(gate.Invoke(controller, [brain, bot, visible]), Is.SameAs(visible), "frontier threat gate: the same targets");
                GameLiving[] none = [];
                Assert.That(gate.Invoke(controller, [brain, bot, none]), Is.SameAs(none));
                Assert.That(gate.Invoke(controller, [null, bot, visible]), Is.SameAs(visible));
                Assert.That(hold.Invoke(controller, [brain, bot]), Is.EqualTo(false), "travel and regroup hold");
                Assert.That(hold.Invoke(controller, [null, bot]), Is.EqualTo(false));
                Assert.That(roam.Invoke(controller, [bot, null]), Is.Null, "roam destination");
                Assert.That(AutonomousBotGroupCoordinator.RvrTacticsRoster(bot, [bot], [bot], 2), Is.Null, "group formation");
                Assert.That(AutonomousBotGroupCoordinator.RvrTacticsRoster(null, [], [], 2), Is.Null);
                Assert.That(RvrFightBoards.Builds, Is.EqualTo(builds), "no stub builds a fight board");
            });
        }

        [Test]
        public void Out_of_scope_or_switched_off_the_hooks_have_no_opinion_either()
        {
            Bot enemy = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            void NoOpinion(BotBrain brain, string why) => Assert.Multiple(() =>
            {
                Assert.That(brain.RvrTacticsActive(), Is.False, why);
                Assert.That(brain.RvrTacticsTarget(enemy, null, null), Is.Null, why);
                Assert.That(brain.RvrTacticsCrowdControl(), Is.Null, why);
                Assert.That(brain.TryRvrTacticsSupport(), Is.False, why);
                Assert.That(brain.RvrTacticsPeel(), Is.False, why);
                Assert.That(brain.RvrTacticsProtectionTarget(out _), Is.False, why);
                Assert.That(brain.TryRvrTacticsMove(enemy), Is.False, why);
                Assert.That(brain.RvrBoard(), Is.Null, why);
                Assert.That(brain.RvrPlan(), Is.Null, why);
            });

            (Bot raider, BotBrain raidBrain) = FieldBot();
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
            NoOpinion(raidBrain, "a raider");

            Bot companion = Companion();
            companion.Region = 250;
            NoOpinion(new BotBrain { Body = companion }, "a companion");

            (Bot defender, BotBrain defenderBrain) = FieldBot();
            defender.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.DefendEvent);
            NoOpinion(defenderBrain, "a keep defender");

            Properties.RVR_BOT_FIELD_TACTICS = false;
            NoOpinion(FieldBot().Brain, "the master switch off");
        }

        // ---- the settings -------------------------------------------------------------------------------------------------

        [Test]
        public void Every_switch_is_a_bots_setting_that_is_on_by_default_and_says_what_it_does_not_do_yet()
        {
            foreach ((string field, string key, int workPackage) in Switches)
            {
                var attribute = (ServerPropertyAttribute)Attribute.GetCustomAttribute(Field(field), typeof(ServerPropertyAttribute));
                Assert.Multiple(() =>
                {
                    Assert.That(attribute, Is.Not.Null, field);
                    Assert.That(attribute.Key, Is.EqualTo(key));
                    Assert.That(attribute.Category, Is.EqualTo("bots"), key);
                    Assert.That(attribute.DefaultValue, Is.EqualTo(true), key);
                    if (workPackage == 0)
                        Assert.That(attribute.Description, Does.Contain("In this build it changes nothing by itself"), key);
                    else if (workPackage is 1 or 2 or 3)
                        Assert.That(attribute.Description, Does.StartWith("Open-field RvR (1.2):").And.Contain("Log: RVR_"), key);
                    else
                        Assert.That(attribute.Description, Does.StartWith($"Does nothing yet: reserved for 1.2 work package {workPackage}."), key);
                });
            }
            Assert.That(Switches.Select(s => s.Key).Distinct().Count(), Is.EqualTo(16));
        }

        [Test]
        public void The_switches_value_reads_each_setting_and_the_master_switch_turns_all_off()
        {
            PropertyInfo[] members = typeof(RvrTacticsSwitches).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(bool)).ToArray();
            Assert.That(members, Has.Length.EqualTo(Switches.Length), "one member per setting");
            object allOn = RvrTacticsSwitches.AllOn, allOff = RvrTacticsSwitches.AllOff;
            Assert.That(members.All(p => (bool)p.GetValue(allOn)!), Is.True);
            Assert.That(members.Any(p => (bool)p.GetValue(allOff)!), Is.False);
            Assert.That(RvrTacticsSwitches.Current, Is.EqualTo(RvrTacticsSwitches.AllOn));

            // each setting turns off exactly one member
            foreach ((string field, _, int workPackage) in Switches.Skip(1))
            {
                Field(field).SetValue(null, false);
                object current = RvrTacticsSwitches.Current;
                Assert.That(members.Count(p => !(bool)p.GetValue(current)!), Is.EqualTo(1), field);
                Assert.That(((RvrTacticsSwitches)current).FieldTactics, Is.True, field);
                Field(field).SetValue(null, true);
            }
            Properties.RVR_BOT_ASSIST = false;
            Assert.That(RvrTacticsSwitches.Current.Assist, Is.False);
            Assert.That(RvrTacticsSwitches.Current.CrowdControlPlan, Is.True);
            Properties.RVR_BOT_ASSIST = true;

            Properties.RVR_BOT_FIELD_TACTICS = false;
            Assert.That(RvrTacticsSwitches.Current, Is.EqualTo(RvrTacticsSwitches.AllOff), "the master switch off: everything reads off");
        }

        private static string RepoRoot()
        {
            DirectoryInfo dir = new(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "source", "tools", "release-server-settings.json")))
                dir = dir.Parent;
            return dir?.FullName;
        }

        [Test]
        public void Every_switch_ships_on_in_the_release_world()
        {
            string root = RepoRoot();
            Assert.That(root, Is.Not.Null, "the repository root above the test directory");
            using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "source", "tools", "release-server-settings.json")));
            JsonElement shipped = settings.RootElement.GetProperty("Settings");
            foreach ((_, string key, _) in Switches)
            {
                Assert.That(shipped.TryGetProperty(key, out JsonElement value), Is.True, key);
                Assert.That(value.GetString(), Is.EqualTo("True"), key);
            }
            Assert.That(settings.RootElement.GetProperty("KeptOff").EnumerateObject().Any(p => p.Name.StartsWith("rvr_")), Is.False);
        }

        // ---- the profiler ---------------------------------------------------------------------------------------------------

        [Test]
        public void The_fight_board_and_the_hooks_have_their_own_profiler_systems() => Assert.Multiple(() =>
        {
            Assert.That(AiSystemNames.Of(AiSystem.RvrFightBoard), Is.EqualTo("frontier.rvr_fight_board"));
            Assert.That(AiSystemNames.Of(AiSystem.RvrTactics), Is.EqualTo("frontier.rvr_tactics"));
            Assert.That((int)AiSystem.RvrFightBoard, Is.EqualTo((int)AiSystem.FrontierThreatScan + 1), "next to the frontier scan");
            Assert.That((int)AiSystem.RvrTactics, Is.EqualTo((int)AiSystem.FrontierThreatScan + 2));
            Assert.That(AiSystemNames.IsLockWait((int)AiSystem.RvrFightBoard), Is.False);
            Assert.That(AiSystemNames.IsLockWait((int)AiSystem.LockCoordinator), Is.True);
            Assert.That(AiSystemNames.IsLockWait((int)AiSystem.LockDatabaseWrite), Is.True);
        });
    }
}
