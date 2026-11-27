using System.IO;
using System.Linq;
using System.Reflection;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using W = DOL.GS.BotWeaponSetPolicy;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #569 step 7, weapon sets like players (port of True165 27164a0 and its later BotWeaponSetPolicy rules). One server
    /// setting, bot_weapon_set_rules, on by default. Only weapons the bot already carries are used. Tests only until seen
    /// in a PC log.
    /// </summary>
    [TestFixture]
    public class UT_WeaponSets569
    {
        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        [Test]
        public void OneSettingOnByDefaultAndShippedOn()
        {
            ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_WEAPON_SET_RULES))
                .GetCustomAttribute<ServerPropertyAttribute>();
            Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", W.PropertyKey, (object)true)));
            Assert.That(Source("..", "..", "tools", "release-server-settings.json"), Does.Contain("\"bot_weapon_set_rules\": \"True\""));
        }

        [Test]
        public void AnUnusableSetIsNeverChosen() => Assert.Multiple(() =>
        {
            Assert.That(W.PreferTwoHanded(null, false, true, true), Is.False, "no usable two-hander: the one-hander");
            Assert.That(W.PreferTwoHanded(null, true, false, false), Is.True, "no usable one-hander: the two-hander");
            Assert.That(W.PreferTwoHanded(null, true, true, true), Is.True, "both usable, no other opinion: the build");
            Assert.That(W.PreferTwoHanded(null, true, true, false), Is.False);
        });

        [Test]
        public void ThePlayersReadiedSetThenGuardDutyThenTrainingThenTheBuild() => Assert.Multiple(() =>
        {
            Assert.That(W.Choose(eActiveWeaponSlot.Standard, false, true, true), Is.False, "the player readied sword and shield");
            Assert.That(W.Choose(eActiveWeaponSlot.TwoHanded, true, false, false), Is.True, "the player's choice beats guard duty");
            Assert.That(W.Choose(eActiveWeaponSlot.Distance, false, null, true), Is.True, "a readied bow is no melee choice");
            Assert.That(W.Choose(null, true, true, true), Is.False, "the party's only Guard tank takes its shield");
            Assert.That(W.Choose(null, false, true, false), Is.True, "trained higher in the two-hander");
            Assert.That(W.Choose(null, false, false, true), Is.False, "trained higher in the one-hander");
            Assert.That(W.Choose(null, false, null, true), Is.True, "no training opinion: the build");
        });

        [Test]
        public void TheTrainedSetRule() => Assert.Multiple(() =>
        {
            Assert.That(W.TrainedSet(9, 1, false), Is.True, "Two Handed 9 against Slash 1");
            Assert.That(W.TrainedSet(20, 39, false), Is.False);
            Assert.That(W.TrainedSet(20, 20, false), Is.Null, "the same line (a Midgard sword) or a tie");
            Assert.That(W.TrainedSet(30, 10, true), Is.Null, "a dual-wield set: no opinion");
        });

        [Test]
        public void GuardDutyNeedsTheOnlyTankWithGuardAndATrainedShield() => Assert.Multiple(() =>
        {
            Assert.That(W.GuardDuty(true, true, true, true, 25, 50, false), Is.True);
            Assert.That(W.GuardDuty(true, true, true, true, 24, 50, false), Is.False, "a shield trained below half its level");
            Assert.That(W.GuardDuty(true, true, true, true, 25, 50, true), Is.False, "another tank: the offensive one takes its two-hander");
            Assert.That(W.GuardDuty(true, true, false, true, 25, 50, false), Is.False, "no Guard");
            Assert.That(W.GuardDuty(true, true, true, false, 25, 50, false), Is.False, "no shield carried: nothing is given to it");
            Assert.That(W.GuardDuty(false, true, true, true, 25, 50, false), Is.False, "solo");
            Assert.That(W.GuardDuty(true, false, true, true, 25, 50, false), Is.False, "not a tank");
            Assert.That(W.ShieldTrainedForGuard(1, 1), Is.True);
        });

        [Test]
        public void ArchersStopFlippingBetweenBowAndMelee() => Assert.Multiple(() =>
        {
            Assert.That(W.EnduranceForBow(30, 10, onBow: false), Is.True, "three shots' endurance: back to the bow");
            Assert.That(W.EnduranceForBow(29, 10, onBow: false), Is.False, "less: stay in melee");
            Assert.That(W.EnduranceForBow(10, 10, onBow: true), Is.True, "already on the bow: one shot is enough");
            Assert.That(W.MeleeHold(onBow: false), Is.EqualTo(150));
            Assert.That(W.MeleeHold(onBow: true), Is.Zero);
        });

        [Test]
        public void TheSwitchOffKeepsTheOldRule() => Assert.Multiple(() =>
        {
            Assert.That(W.OldRule(true, true, true), Is.True);
            Assert.That(W.OldRule(true, true, false), Is.False);
            Assert.That(W.OldRule(true, false, false), Is.True);
            Assert.That(W.OldRule(false, true, true), Is.False);
        });

        [Test]
        public void TheBrainAndTheAttackStartUseTheSameRule()
        {
            string brain = Source("bots", "BotBrain.cs");
            string npc = Source("gameobjects", "GameNPC.cs");
            string peel = Source("bots", "BotBrain.TankPeel.cs");
            string window = Source("mode165", "CompanionWindows165.cs");
            Assert.Multiple(() =>
            {
                Assert.That(brain, Does.Contain(
                    "BotWeaponSetPolicy.PreferTwoHanded(BotBody, twoHandUsable, oneHandUsable, BotWeaponSetPolicy.TwoHandedBuild(BotBody))"));
                Assert.That(npc.Split("BotWeaponSetPolicy.PreferTwoHanded(bot, twoHandUsable, standardUsable, BotWeaponSetPolicy.TwoHandedBuild(bot))")
                    .Length - 1, Is.EqualTo(2), "both attack-start choices");
                Assert.That(npc, Does.Not.Contain("(!standardUsable || bot.BotSpec?.Is2H == true)"), "no second, narrower build test");
                Assert.That(peel, Does.Contain("bot.IsWithinRadius(member, GuardAbilityHandler.GUARD_DISTANCE)"), "Guard on a member in reach");
                Assert.That(window, Does.Contain("bot.PlayerReadiedSlot = want;"), "the player's readied set is remembered");
            });
        }
    }
}
