using System.Linq;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2.1 (owner 2026-11-21): a hired companion joins at its player's level (never below 5, never above
    /// the player), trained in its build and, above level 5, in plain merchant gear in its empty slots; one hired on 1.2 at
    /// level 5 is raised once to its player's level at its next summon. The pure decisions (the spec training itself is
    /// in UT_BotSpecPointsMatchAPlayer). Tests only.
    /// </summary>
    [TestFixture]
    public sealed class UT_HiredCompanionJoinsAtPlayerLevel
    {
        [TestCase(5, 5)]
        [TestCase(6, 6)]
        [TestCase(23, 23)]
        [TestCase(50, 50)]
        [TestCase(3, 5)]    // hiring is refused below 5; the level is still never below the first slot
        [TestCase(60, 50)]
        public void ANewHireStartsAtItsPlayersLevel(int playerLevel, int expected) =>
            Assert.That(HiredCompanions165.StartingLevel(playerLevel), Is.EqualTo(expected));

        [Test]
        public void ANewHireIsTrainedAtTheLevelItWasHiredAt()
        {
            Assert.That(HiredCompanions165.PlanArrival(HiredCompanions165.LevelRuleNewHire, 23, 23),
                Is.EqualTo((HiredCompanions165.ArrivalKind.NewHire, 23)));
            // its player levelled before the first summon (a full group): trained at its own level, levelling beside them after
            Assert.That(HiredCompanions165.PlanArrival(HiredCompanions165.LevelRuleNewHire, 23, 25),
                Is.EqualTo((HiredCompanions165.ArrivalKind.NewHire, 23)));
        }

        [TestCase(5, 40, 40)]
        [TestCase(5, 6, 6)]
        [TestCase(17, 50, 50)]
        [TestCase(5, 55, 50)]
        public void ACompanionHiredOn12IsRaisedToItsPlayersLevel(int companionLevel, int playerLevel, int expected) =>
            Assert.That(HiredCompanions165.PlanArrival(HiredCompanions165.LevelRuleRaisePending, companionLevel, playerLevel),
                Is.EqualTo((HiredCompanions165.ArrivalKind.Raise, expected)));

        [TestCase(5, 5)]
        [TestCase(20, 20)]
        [TestCase(20, 12)]   // its player is lower (a lost level, another character's companion): never lowered
        public void ACompanionAtOrAboveItsPlayersLevelIsLeftAsItIs(int companionLevel, int playerLevel) =>
            Assert.That(HiredCompanions165.PlanArrival(HiredCompanions165.LevelRuleRaisePending, companionLevel, playerLevel),
                Is.EqualTo((HiredCompanions165.ArrivalKind.None, companionLevel)));

        [Test]
        public void TheRaiseHappensOnce()
        {
            // the summon marks the hire row done after the companion joined; every later summon plans nothing
            int rule = HiredCompanions165.LevelRuleRaisePending;
            (HiredCompanions165.ArrivalKind kind, int level) = HiredCompanions165.PlanArrival(rule, 5, 30);
            Assert.That(kind, Is.EqualTo(HiredCompanions165.ArrivalKind.Raise));
            rule = HiredCompanions165.LevelRuleDone;
            Assert.That(HiredCompanions165.PlanArrival(rule, level, 30).Kind, Is.EqualTo(HiredCompanions165.ArrivalKind.None));
            Assert.That(HiredCompanions165.PlanArrival(rule, level, 45).Kind, Is.EqualTo(HiredCompanions165.ArrivalKind.None),
                "its player levelled since: it levels beside them, as before");
            Assert.That(HiredCompanions165.PlanArrival(rule, 5, 50).Kind, Is.EqualTo(HiredCompanions165.ArrivalKind.None));
            Assert.That(HiredCompanions165.PlanArrival(HiredCompanions165.LevelRuleGearPending + 5, 5, 50).Kind,
                Is.EqualTo(HiredCompanions165.ArrivalKind.None), "a later rule is never taken for an old one");
        }

        [Test]
        public void ARowFromBeforeTheColumnReadsAsHiredOn12() =>
            Assert.That(new OfflineHiredCompanionRecord().LevelRule, Is.EqualTo(HiredCompanions165.LevelRuleRaisePending));

        [TestCase(5, false)]
        [TestCase(6, true)]
        [TestCase(50, true)]
        public void OnlyAboveLevelFiveComesBasicGear(int level, bool gear) =>
            Assert.That(HiredCompanions165.GetsBasicGear(level), Is.EqualTo(gear));

        [Test]
        public void BasicGearFillsOnlyTheEmptySlots()
        {
            // a raised companion wearing what its player gave it: a helm, a chest and a sword (an occupied slot is never
            // wanted; only the training weapon is taken out first, see below)
            eInventorySlot[] worn = [eInventorySlot.HeadArmor, eInventorySlot.TorsoArmor, eInventorySlot.RightHandWeapon];
            var wants = AutonomousStarterGear.PlanSlots(slot => worn.Contains(slot), eInventorySlot.RightHandWeapon,
                eObjectType.SlashingWeapon, 0, shieldBuild: true, savage: false);
            Assert.That(wants.Select(w => w.Slot), Is.EquivalentTo(new[]
            {
                eInventorySlot.LegsArmor, eInventorySlot.ArmsArmor, eInventorySlot.HandsArmor, eInventorySlot.FeetArmor,
                eInventorySlot.LeftHandWeapon,
            }));
            Assert.That(wants.Single(w => w.Slot == eInventorySlot.LeftHandWeapon).Shield, Is.True, "a shield for a sword-and-board class");
            // fully dressed: nothing to give
            Assert.That(AutonomousStarterGear.PlanSlots(_ => true, eInventorySlot.RightHandWeapon, eObjectType.SlashingWeapon, 0,
                true, false), Is.Empty);
        }

        /// <summary>The training weapon as GameBot.TryCreateConfiguredStarterWeapon saves it: a generated unique item,
        /// Creator "GameBot".</summary>
        private static DbInventoryItem TrainingWeapon() => new()
        {
            Creator = nameof(GameBot), UTemplate_Id = "4f1c2a9e-generated", ITemplate_Id = null, Name = "Short Sword",
        };

        [Test]
        public void TheTrainingWeaponCountsAsEmpty()
        {
            Assert.That(HiredCompanions165.IsTrainingWeapon(TrainingWeapon()), Is.True);
            Assert.That(HiredCompanions165.TrainingWeaponSlots, Is.EquivalentTo(new[]
                { eInventorySlot.RightHandWeapon, eInventorySlot.TwoHandWeapon, eInventorySlot.LeftHandWeapon }),
                "weapon slots only: never an armour slot or the bag");
            // with the training weapon taken out of its hand, the plan wants the hand again
            eInventorySlot[] worn = [eInventorySlot.TorsoArmor];
            var wants = AutonomousStarterGear.PlanSlots(slot => worn.Contains(slot), eInventorySlot.RightHandWeapon,
                eObjectType.SlashingWeapon, 0, shieldBuild: false, savage: false);
            Assert.That(wants.Select(w => w.Slot), Has.Member(eInventorySlot.RightHandWeapon));
        }

        [Test]
        public void AnyOtherWeaponIsNeverTakenForTheTrainingWeapon()
        {
            // a merchant or quest weapon the player handed over (an item template row), however low its level
            Assert.That(HiredCompanions165.IsTrainingWeapon(new DbInventoryItem { ITemplate_Id = "starter_short_sword", Creator = "" }), Is.False);
            // a crafted weapon (Creator: the crafter) and a unique loot drop (another creator)
            Assert.That(HiredCompanions165.IsTrainingWeapon(new DbInventoryItem { UTemplate_Id = "crafted-1", Creator = "Brennan" }), Is.False);
            Assert.That(HiredCompanions165.IsTrainingWeapon(new DbInventoryItem { UTemplate_Id = "loot-1", Creator = "forest giant" }), Is.False);
            // a "GameBot" creator on a template row, an empty one, and nothing at all
            Assert.That(HiredCompanions165.IsTrainingWeapon(new DbInventoryItem { ITemplate_Id = "short_sword", Creator = nameof(GameBot) }), Is.False);
            Assert.That(HiredCompanions165.IsTrainingWeapon(new DbInventoryItem { Creator = nameof(GameBot) }), Is.False);
            Assert.That(HiredCompanions165.IsTrainingWeapon(new DbInventoryItem { UTemplate_Id = "x", Creator = "gamebot" }), Is.False, "exact creator");
            Assert.That(HiredCompanions165.IsTrainingWeapon(null), Is.False);
        }

        [Test]
        public void GearThatCouldNotBeLookedUpIsTriedAgainAtTheNextSummon()
        {
            Assert.That(HiredCompanions165.RuleAfterArrival(basicGearDue: true, gearRetry: true), Is.EqualTo(HiredCompanions165.LevelRuleGearPending));
            Assert.That(HiredCompanions165.RuleAfterArrival(true, false), Is.EqualTo(HiredCompanions165.LevelRuleDone));
            Assert.That(HiredCompanions165.RuleAfterArrival(false, true), Is.EqualTo(HiredCompanions165.LevelRuleDone), "level 5: no gear due");
            // the retry is the gear alone, at its level now: never a second raise or training
            Assert.That(HiredCompanions165.PlanArrival(HiredCompanions165.LevelRuleGearPending, 30, 35),
                Is.EqualTo((HiredCompanions165.ArrivalKind.Gear, 30)));
            Assert.That(HiredCompanions165.PlanArrival(HiredCompanions165.LevelRuleGearPending, 5, 5).Kind,
                Is.EqualTo(HiredCompanions165.ArrivalKind.None));
        }

        [Test]
        public void ARaisedCompanionSpendsOnlyThePointsItGained()
        {
            // untrained at 5 (lines at 1), raised to 40: every point earned from 6 to 40
            int gained = GameBot.PlayerSpecPointTotal(10, 40, false) - GameBot.PlayerSpecPointTotal(10, 5, false);
            Assert.That(HiredCompanions165.RaisedSpecBudget(10, 5, false, 40, [(1, false), (1, false)]), Is.EqualTo(gained));
            // trained at 20 with points left unspent: those stay unspent, only the new ones are spent
            (int, bool)[] lines = [(15, false), (8, false)];
            int lineCost20 = lines.Sum(l => GameBot.LineCost(l.Item1, 20, l.Item2));
            int left20 = GameBot.PlayerSpecPointTotal(15, 20, false) - lineCost20;
            Assert.That(left20, Is.Positive);
            int left40 = GameBot.PlayerSpecPointTotal(15, 40, false) - lines.Sum(l => GameBot.LineCost(l.Item1, 40, l.Item2));
            Assert.That(HiredCompanions165.RaisedSpecBudget(15, 20, false, 40, lines), Is.EqualTo(left40 - left20));
            // an autotrain line's new free levels count as gained
            (int, bool)[] auto = [(10, true)];
            Assert.That(HiredCompanions165.RaisedSpecBudget(20, 20, false, 40, auto),
                Is.EqualTo(GameBot.PlayerSpecPointTotal(20, 40, false) - GameBot.PlayerSpecPointTotal(20, 20, false) +
                           GameBot.LineCost(10, 20, true) - GameBot.LineCost(10, 40, true)));
            Assert.That(GameBot.LineCost(10, 20, true), Is.GreaterThan(GameBot.LineCost(10, 40, true)));
        }

        [Test]
        public void NoRaiseNoBudget()
        {
            Assert.That(HiredCompanions165.RaisedSpecBudget(10, 30, false, 30, []), Is.Zero);
            Assert.That(HiredCompanions165.RaisedSpecBudget(10, 30, false, 20, []), Is.Zero);
            // more trained than a player could be at its old level (a hand-edited save): what it gains, never less than 0
            Assert.That(HiredCompanions165.RaisedSpecBudget(10, 10, false, 11, [(40, false)]), Is.Zero);
        }
    }
}
