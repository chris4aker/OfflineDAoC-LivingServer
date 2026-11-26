using System.IO;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #567 B7 (owner, 2026-11-26): a Hibernian Bard may fill a bot party's Healer slot when no healing class is in the
    /// formation pool, and then heals as the party's healer. Switch bot_bard_fills_healer_slot, on by default.
    /// </summary>
    [TestFixture]
    public class UT_BardHealerSlot567
    {
        private static readonly eCharacterClass[] NoHealer =
            [eCharacterClass.Bard, eCharacterClass.Blademaster, eCharacterClass.Eldritch, eCharacterClass.Ranger];

        [Test]
        public void ABardMayHealOnlyWhenNoHealingClassIsInThePool()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BardHealerSlot.Allowed(eCharacterClass.Hero, NoHealer, true), Is.True);
                Assert.That(BardHealerSlot.Allowed(eCharacterClass.Hero, NoHealer, false), Is.False, "switch off");
                Assert.That(BardHealerSlot.Allowed(eCharacterClass.Hero,
                    [eCharacterClass.Bard, eCharacterClass.Druid, eCharacterClass.Eldritch], true), Is.False, "a Druid heals");
                Assert.That(BardHealerSlot.Allowed(eCharacterClass.Warden, NoHealer, true), Is.False, "a Warden leader heals");
                Assert.That(BardHealerSlot.Allowed(eCharacterClass.Bard,
                    [eCharacterClass.Hero, eCharacterClass.Eldritch], true), Is.True, "the leader is the Bard");
                Assert.That(BardHealerSlot.Allowed(eCharacterClass.Hero,
                    [eCharacterClass.Blademaster, eCharacterClass.Eldritch], true), Is.False, "no Bard at all");
            });
        }

        [Test]
        public void OnlyABardFillsOnlyAHealerSlot()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BardHealerSlot.FillsSlot(eCharacterClass.Bard, BotPveGroupRole.Healer, true), Is.True);
                Assert.That(BardHealerSlot.FillsSlot(eCharacterClass.Bard, BotPveGroupRole.Healer, false), Is.False);
                Assert.That(BardHealerSlot.FillsSlot(eCharacterClass.Bard, BotPveGroupRole.Tank, true), Is.False);
                Assert.That(BardHealerSlot.FillsSlot(eCharacterClass.Minstrel, BotPveGroupRole.Healer, true), Is.False);
                Assert.That(BardHealerSlot.FillsSlot(eCharacterClass.Skald, BotPveGroupRole.Healer, true), Is.False);
                Assert.That(BotPartyRoles.CanFill(eCharacterClass.Bard, BotPveGroupRole.Healer), Is.False,
                    "CanFill and the class-wide healer flag are unchanged");
            });
        }

        [Test]
        public void APartyWithNoHealerFormsAroundABardOnlyWithTheSwitchOn()
        {
            bool saved = ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT;
            try
            {
                ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT = false;
                Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Hero, NoHealer), Is.EqualTo(0),
                    "without a healer no party forms (as before)");
                ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT = true;
                Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Hero, NoHealer), Is.EqualTo(5));
                Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Hero,
                        eCharacterClass.Blademaster, eCharacterClass.Eldritch, eCharacterClass.Ranger, eCharacterClass.Champion),
                    Is.EqualTo(0), "no Bard: still no party");
            }
            finally
            {
                ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT = saved;
            }
        }

        [Test]
        public void ADruidInThePoolHoldsTheHealerSlotNotTheBard()
        {
            bool saved = ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT;
            try
            {
                ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT = true;
                eCharacterClass[] withDruid =
                    [eCharacterClass.Bard, eCharacterClass.Druid, eCharacterClass.Blademaster, eCharacterClass.Eldritch];
                Assert.That(AutonomousBotGroupCoordinator.PveHealerSlotClasses(eCharacterClass.Hero, withDruid),
                    Is.EqualTo(new[] { eCharacterClass.Druid }));
                Assert.That(AutonomousBotGroupCoordinator.PveHealerSlotClasses(eCharacterClass.Hero, NoHealer),
                    Is.EqualTo(new[] { eCharacterClass.Bard }), "no Druid or Warden: the Bard holds it");
            }
            finally
            {
                ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT = saved;
            }
        }

        [Test]
        public void ARaidPartyWithNoHealingClassStillFails()
        {
            bool saved = ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT;
            try
            {
                ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT = true;
                Assert.That(AutonomousBotGroupCoordinator.PveHealerSlotClasses(eCharacterClass.Hero, NoHealer, raidParty: true),
                    Is.Empty, "owner 2026-11-26: no Bard healer in realm raids");
            }
            finally
            {
                ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT = saved;
            }
        }

        [Test]
        public void TheDutyLastsWhileTheBardIsInThatPartyAndTheSwitchIsOn()
        {
            var party = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
            var other = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
            Assert.Multiple(() =>
            {
                Assert.That(BardHealerSlot.Holds(party, party, true), Is.True);
                Assert.That(BardHealerSlot.Holds(party, other, true), Is.False, "moved to another party");
                Assert.That(BardHealerSlot.Holds(party, null, true), Is.False, "left the party");
                Assert.That(BardHealerSlot.Holds(party, party, false), Is.False, "switch turned off");
                Assert.That(BardHealerSlot.Holds(null, null, true), Is.False);
            });
        }

        [Test]
        public void AHealingClassStaysAHealerAndABardWithoutASlotIsNot()
        {
            var healer = (BotBrain)RuntimeHelpers.GetUninitializedObject(typeof(BotBrain));
            healer.IsHealer = true;
            var bard = (BotBrain)RuntimeHelpers.GetUninitializedObject(typeof(BotBrain));
            bard.IsHealer = false;
            Assert.Multiple(() =>
            {
                Assert.That(healer.IsHealer, Is.True);
                Assert.That(bard.IsHealer, Is.False);
                Assert.That(bard.HealerSlotGroup, Is.Null);
            });
        }

        [Test]
        public void EveryOrdinaryRolePathMarksTheBardAndNoRaidPathAdmitsIt()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Dawn of Light.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null, "repository root");
            string autonomous = Path.Combine(dir, "GameServer", "bots", "autonomous");
            string coordinator = File.ReadAllText(Path.Combine(autonomous, "AutonomousBotGroupCoordinator.cs"));
            string handover = File.ReadAllText(Path.Combine(autonomous, "AutonomousBotGroupCoordinator.Handover.cs"));
            string forcedRaid = File.ReadAllText(Path.Combine(autonomous, "AutonomousBotGroupCoordinator.ForcedRaid.cs"));
            Assert.Multiple(() =>
            {
                Assert.That(coordinator, Does.Contain("BardHealerSlot.Mark(session.Id, group, BotMembers(group), session.PveRoles, MemberKey, Log)"));
                Assert.That(coordinator, Does.Contain("bool bardHealers = BardHealerSlot.Allowed(leader, pool);"));
                Assert.That(handover, Does.Contain("if (session.PlayerRole != BotPveGroupRole.Healer)"));
                Assert.That(handover, Does.Contain("BardHealerSlot.Mark(session.Id, group, bots, session.PveRoles, MemberKey, Log)"));
                Assert.That(coordinator, Does.Contain("BardHealerSlot.Mark(session.Id, group, exact, session.PveRoles, MemberKey, Log)"),
                    "NewSession: session recovery and new ordinary parties");
                Assert.That(forcedRaid.Split("TryBuildPveRoster(").Length - 1, Is.EqualTo(3), "raid roster calls");
                Assert.That(forcedRaid.Split("out var others, out _, allowBardHealer: false)").Length - 1, Is.EqualTo(3),
                    "every raid roster call keeps the Bard fallback out");
                Assert.That(forcedRaid, Does.Contain("TryAssignExactPveRoles(members, leader, out roles, allowBardHealer: false)"));
            });
        }
    }
}
