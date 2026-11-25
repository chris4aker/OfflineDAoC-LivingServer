using System.Collections.Generic;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Port of True165 d2fc3fe3d (its veteran review M2, 2026-11-11): spec-aware group roles - a shield tank main-tanks
    /// and pulls, two-hander tanks are damage or the off-tank, the healer slot is ranked by spec, one crowd-control slot.
    /// (True165's kill-target and peel sheet is not ported; its tests are left out.)
    /// </summary>
    [TestFixture]
    public class UT_VeteranGroupRoles
    {
        private static System.Func<string, int> Spec(Dictionary<string, int> levels) =>
            line => levels.TryGetValue(line, out int level) ? level : 1;

        [Test]
        public void TheMainLineIsTheHighestTrainedLineThenTheTemplateLabel() => Assert.Multiple(() =>
        {
            Assert.That(BotGroupRoles.MainLine(eCharacterClass.Cleric,
                Spec(new() { [Specs.Smite] = 40, [Specs.Rejuvenation] = 10 }), eSpecType.RejuvCleric), Is.EqualTo(Specs.Smite),
                "spent points beat the template label");
            Assert.That(BotGroupRoles.MainLine(eCharacterClass.Cleric, Spec(new()), eSpecType.SmiteCleric),
                Is.EqualTo(Specs.Smite), "a fresh bot: the label decides");
            Assert.That(BotGroupRoles.MainLine(eCharacterClass.Healer,
                Spec(new() { [Specs.Pacification] = 20, [Specs.Mending] = 20 }), eSpecType.MendHealer),
                Is.EqualTo(Specs.Mending), "a tie: the label decides");
            Assert.That(BotGroupRoles.MainLine(eCharacterClass.Armsman, Spec(new()), eSpecType.OneHandAndShield), Is.Null);
        });

        [Test]
        public void HealerSlotsAreRankedBySpec() => Assert.Multiple(() =>
        {
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Cleric, Specs.Rejuvenation), Is.EqualTo(BotGroupRoles.PrimaryHealer));
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Healer, Specs.Mending), Is.EqualTo(BotGroupRoles.PrimaryHealer));
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Druid, Specs.Regrowth), Is.EqualTo(BotGroupRoles.PrimaryHealer));
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Shaman, Specs.Mending), Is.EqualTo(BotGroupRoles.PrimaryHealer));
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Cleric, Specs.Enhancement), Is.EqualTo(BotGroupRoles.StrongHealer));
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Friar, Specs.Rejuvenation, 25, 50), Is.EqualTo(BotGroupRoles.StrongHealer));
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Mentalist, Specs.Mentalism), Is.EqualTo(BotGroupRoles.BackupHealer));
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Mentalist, Specs.Light), Is.EqualTo(BotGroupRoles.NoHealer));
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Cleric, Specs.Smite), Is.EqualTo(BotGroupRoles.PinchHealer),
                "a Smite Cleric heals only in a pinch");
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Paladin, null), Is.EqualTo(BotGroupRoles.LastResortHealer));
            Assert.That(BotGroupRoles.HealerRank(eCharacterClass.Friar, Specs.Staff, 10, 50), Is.EqualTo(BotGroupRoles.LastResortHealer),
                "a Friar with Rejuvenation under half its level");
            Assert.That(BotGroupRoles.IsPrimaryHealer(BotGroupRoles.StrongHealer), Is.True);
            Assert.That(BotGroupRoles.IsPrimaryHealer(BotGroupRoles.BackupHealer), Is.False);
        });

        [Test]
        public void CrowdControlSpecsAreRanked() => Assert.Multiple(() =>
        {
            Assert.That(BotGroupRoles.CrowdControlRank(eCharacterClass.Enchanter, Specs.Mana), Is.EqualTo(0));
            Assert.That(BotGroupRoles.CrowdControlRank(eCharacterClass.Sorcerer, Specs.Mind_Magic), Is.EqualTo(0));
            Assert.That(BotGroupRoles.CrowdControlRank(eCharacterClass.Sorcerer, Specs.Body_Magic), Is.EqualTo(2));
            Assert.That(BotGroupRoles.CrowdControlRank(eCharacterClass.Healer, Specs.Pacification), Is.EqualTo(0));
            Assert.That(BotGroupRoles.CrowdControlRank(eCharacterClass.Eldritch, Specs.Light), Is.EqualTo(1));
            Assert.That(BotGroupRoles.CrowdControlRank(eCharacterClass.Eldritch, Specs.Void), Is.EqualTo(BotGroupRoles.NoCrowdControl));
            Assert.That(BotGroupRoles.CrowdControlRank(eCharacterClass.Theurgist, Specs.Wind_Magic), Is.EqualTo(2));
            Assert.That(BotGroupRoles.CrowdControlRank(eCharacterClass.Armsman, null), Is.EqualTo(BotGroupRoles.NoCrowdControl));
            Assert.That(BotGroupRoles.ChooseCrowdControl([2, 0, 1], [true, true, true], 8), Is.EqualTo(1));
            Assert.That(BotGroupRoles.ChooseCrowdControl([2, 0, 1], [true, false, true], 8), Is.EqualTo(2),
                "only a member that can control");
            Assert.That(BotGroupRoles.ChooseCrowdControl([0], [true], 4), Is.EqualTo(-1), "a crowd-control slot from five up");
        });

        [Test]
        public void AShieldTankIsMainTankAndPuller() => Assert.Multiple(() =>
        {
            Assert.That(BotGroupRoles.IsShieldTank(true, 25, 50), Is.True);
            Assert.That(BotGroupRoles.IsShieldTank(true, 1, 40), Is.False, "a two-hander Armsman");
            Assert.That(BotGroupRoles.IsShieldTank(false, 50, 50), Is.False, "not a tank class");
            Assert.That(BotGroupRoles.RankTanks([false, true], [50, 48], -1), Is.EqualTo(new[] { 1, 0 }), "shield first");
            Assert.That(BotGroupRoles.RankTanks([false, true], [50, 48], 0), Is.EqualTo(new[] { 0, 1 }),
                "the puller is the main tank");
            Assert.That(BotGroupRoles.RankTanks([true, true], [40, 42], -1), Is.EqualTo(new[] { 1, 0 }), "then the higher level");
            for (int run = 0; run < 20; run++)
                Assert.That(AutonomousBotGroupCoordinator.PreferredPveTankSlot([30, 30], false, new System.Random(run),
                    [false, true]), Is.EqualTo(1), "the shield tank pulls");
            Assert.That(AutonomousBotGroupCoordinator.PreferredPveTankSlot([30, 31], false, null, [true, false]), Is.EqualTo(0),
                "a shield tank beats a higher two-hander");
            Assert.That(AutonomousBotGroupCoordinator.PreferredPveTankSlot([30, 31], false, null, [false, false]), Is.EqualTo(1),
                "no shield tank: the old rule");
            Assert.That(BotGroupRoles.RangedPullerOrder(BotPveGroupRole.Tank, true, false),
                Is.LessThan(BotGroupRoles.RangedPullerOrder(BotPveGroupRole.Tank, false, false)));
            Assert.That(BotGroupRoles.RangedPullerOrder(BotPveGroupRole.Tank, false, false),
                Is.LessThan(BotGroupRoles.RangedPullerOrder(BotPveGroupRole.Buffer, false, true)),
                "the crowd-control slot pulls when no tank can");
            Assert.That(BotGroupRoles.RangedPullerOrder(BotPveGroupRole.Buffer, false, true),
                Is.LessThan(BotGroupRoles.RangedPullerOrder(BotPveGroupRole.Attacker, false, false)));
            Assert.That(BotGroupRoles.RangedPullerOrder(BotPveGroupRole.Attacker, false, false),
                Is.LessThan(BotGroupRoles.RangedPullerOrder(BotPveGroupRole.Healer, false, false)));
        });

        [Test]
        public void TheBestHealersTakeTheHealerSlots()
        {
            BotPveGroupRole T = BotPveGroupRole.Tank, H = BotPveGroupRole.Healer, B = BotPveGroupRole.Buffer, A = BotPveGroupRole.Attacker;
            BotPveGroupRole[] template = [T, H, B, A, A];
            // a Paladin in the healer slot, a Rejuvenation Cleric in the buffer slot: they swap
            eCharacterClass[] classes = [eCharacterClass.Armsman, eCharacterClass.Paladin, eCharacterClass.Cleric,
                eCharacterClass.Wizard, eCharacterClass.Mercenary];
            int[] ranks = [9, BotGroupRoles.LastResortHealer, BotGroupRoles.PrimaryHealer, 9, 9];
            List<(int, int)> swaps = BotGroupRoles.PreferBetterHealers(template, classes, ranks);
            Assert.Multiple(() =>
            {
                Assert.That(swaps, Is.EqualTo(new List<(int, int)> { (1, 2) }));
                Assert.That(classes[1], Is.EqualTo(eCharacterClass.Cleric));
                Assert.That(classes[2], Is.EqualTo(eCharacterClass.Paladin));
            });

            // a Healer in an attacker slot cannot swap: a Paladin could take the attacker slot, but a Healer cannot fill it
            eCharacterClass[] cannotMove = [eCharacterClass.Armsman, eCharacterClass.Paladin, eCharacterClass.Bard,
                eCharacterClass.Wizard, eCharacterClass.Mercenary];
            int[] cannotMoveRanks = [9, BotGroupRoles.LastResortHealer, 9, 9, 9];
            Assert.That(BotGroupRoles.PreferBetterHealers(template, cannotMove, cannotMoveRanks), Is.Empty,
                "nobody heals better than the Paladin");

            // a Mentalism Mentalist is the second healer only beside a real healer
            BotPveGroupRole[] two = [T, H, H, A, A];
            eCharacterClass[] withPrimary = [eCharacterClass.Hero, eCharacterClass.Druid, eCharacterClass.Warden,
                eCharacterClass.Mentalist, eCharacterClass.Blademaster];
            int[] primaryRanks = [9, BotGroupRoles.PrimaryHealer, BotGroupRoles.BackupHealer + 2, BotGroupRoles.BackupHealer, 9];
            Assert.That(BotGroupRoles.PreferBetterHealers(two, withPrimary, primaryRanks), Is.EqualTo(new List<(int, int)> { (2, 3) }));
            eCharacterClass[] noPrimary = [eCharacterClass.Hero, eCharacterClass.Warden, eCharacterClass.Warden,
                eCharacterClass.Mentalist, eCharacterClass.Blademaster];
            int[] noPrimaryRanks = [9, BotGroupRoles.LastResortHealer, BotGroupRoles.LastResortHealer, BotGroupRoles.BackupHealer, 9];
            Assert.That(BotGroupRoles.PreferBetterHealers(two, noPrimary, noPrimaryRanks), Is.Empty,
                "no real healer: the Mentalist does not take a healer slot");
        }
    }
}
