using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Living Server 1.2 (owner 2026-11-15, "Arrive with starter gear": "The launcher gives each bot common merchant-quality
    /// gear for its level, as if it had leveled normally"). Owner's PC, build 0026378, Thidranki 13:45-14:00: 183 of 410 bot
    /// deaths had armorPieces=0. The rules here are pure; the merchant lookup and the equipping run on the live server.
    /// </summary>
    [TestFixture]
    public class UT_StarterGearForTrainedArrivals
    {
        // ---- who gets it ----

        [TestCase(20)]
        [TestCase(24)]
        [TestCase(50)]
        public void ABotThatArrivesTrainedWithNoArmourGetsIt(int level)
        {
            Assert.That(AutonomousStarterGear.Qualifies(level, arrivesTrainedNow: true, alreadyGiven: false, armorPiecesWorn: 0,
                eRealm.Albion, bindRegionId: 10), Is.True);
        }

        [Test]
        public void AnOrdinaryLevelOneStarterStillEarnsItsArmour()
        {
            // Owner's rule: gamebots start without armour and earn it. "+ Lv.1" bots never arrive trained.
            Assert.That(GameBot.ArrivesTrained(1, 1, neverSaved: true), Is.False);
            Assert.That(AutonomousStarterGear.Qualifies(1, false, false, 0, eRealm.Albion, 1), Is.False);
            Assert.That(AutonomousStarterGear.Qualifies(1, true, false, 0, eRealm.Albion, 10), Is.False, "never at level 1");
        }

        [Test]
        public void ABotThatLeveledUpByPlayingFromAStartingAreaIsLeftAlone()
        {
            // Bound in its Classic or Shrouded Isles starting area, where "+ Lv.1" put it: it earns its armour.
            foreach ((eRealm realm, int startRegion) in new[]
                     { (eRealm.Albion, 1), (eRealm.Albion, 51), (eRealm.Midgard, 100), (eRealm.Midgard, 151),
                       (eRealm.Hibernia, 200), (eRealm.Hibernia, 181) })
                Assert.That(AutonomousStarterGear.Qualifies(22, false, false, 0, realm, startRegion), Is.False,
                    $"{realm} bound in region {startRegion}");
        }

        [Test]
        public void APlusLvTwentyBotSavedBeforeThisRuleGetsItOnceIfItStillWearsNoArmour()
        {
            Assert.That(AutonomousStarterGear.Qualifies(22, false, false, 0, eRealm.Albion, 10), Is.True);
            Assert.That(AutonomousStarterGear.Qualifies(23, false, false, 0, eRealm.Midgard, 101), Is.True);
            Assert.That(AutonomousStarterGear.Qualifies(21, false, false, 0, eRealm.Hibernia, 201), Is.True);
            Assert.That(AutonomousStarterGear.Qualifies(22, false, false, 0, eRealm.Albion, 101), Is.False, "another realm's capital");
            Assert.That(AutonomousStarterGear.Qualifies(22, false, false, 0, eRealm.Albion, 0), Is.False, "no bind");
        }

        [Test]
        public void ItIsNeverGivenTwiceAndNeverOverArmourTheBotAlreadyWears()
        {
            Assert.That(AutonomousStarterGear.Qualifies(22, true, alreadyGiven: true, 0, eRealm.Albion, 10), Is.False);
            Assert.That(AutonomousStarterGear.Qualifies(22, false, alreadyGiven: true, 0, eRealm.Albion, 10), Is.False);
            Assert.That(AutonomousStarterGear.Qualifies(22, true, false, armorPiecesWorn: 1, eRealm.Albion, 10), Is.False,
                "it found or bought a piece: what it has stays as it is");
            Assert.That(AutonomousStarterGear.Qualifies(50, true, false, armorPiecesWorn: 6, eRealm.Albion, 10), Is.False,
                "a \"+ Lv.50\" bot already wears its level-50 set");
        }

        [Test]
        public void TheLauncherCapitalsAreTheOnesTheLauncherBindsIn()
        {
            Assert.That(AutonomousStarterGear.LauncherCapitalRegion(eRealm.Albion), Is.EqualTo(10));
            Assert.That(AutonomousStarterGear.LauncherCapitalRegion(eRealm.Midgard), Is.EqualTo(101));
            Assert.That(AutonomousStarterGear.LauncherCapitalRegion(eRealm.Hibernia), Is.EqualTo(201));
            Assert.That(AutonomousStarterGear.LauncherCapitalRegion(eRealm.None), Is.EqualTo(-1));

            string mainForm = Path.Combine(RepoRoot(), "source", "tools", "OfflineDaoc.Launcher", "MainForm.cs");
            if (!File.Exists(mainForm))
                Assert.Ignore("Launcher source not found beside the tests.");
            string source = File.ReadAllText(mainForm);
            Assert.That(source, Does.Contain("1 => new(10, "), "Albion's capital start (CapitalBotStartingLocation)");
            Assert.That(source, Does.Contain("2 => new(101, "), "Midgard's capital start");
            Assert.That(source, Does.Contain("3 => new(201, "), "Hibernia's capital start");
            Assert.That(source, Does.Contain("BindRegionId, BindX, BindY, BindZ"), "the launcher binds where it starts a bot");
        }

        // ---- the marker ----

        [Test]
        public void TheMarkerRoundTripsBesideTheTrainedLevelAndRealmPoints()
        {
            string saved = "trained-level|22|realm-points|3" + AutonomousStarterGear.MarkerSuffix(true);
            Assert.That(saved, Is.EqualTo("trained-level|22|realm-points|3|starter-gear|1"));
            Assert.That(AutonomousStarterGear.HasMarker(saved), Is.True);
            Assert.That(GameBot.ParseTrainedRealmPoints(saved), Is.EqualTo(3), "realm points still read");
            Assert.That(saved.Split('|')[1], Is.EqualTo("22"), "the trained level is still the second field");

            Assert.That(AutonomousStarterGear.MarkerSuffix(false), Is.Empty);
            Assert.That(AutonomousStarterGear.HasMarker("trained-level|22|realm-points|0"), Is.False);
            Assert.That(AutonomousStarterGear.HasMarker(""), Is.False);
            Assert.That(AutonomousStarterGear.HasMarker(null), Is.False);
            Assert.That(AutonomousStarterGear.HasMarker("trained-level|1"), Is.False);
        }

        [Test]
        public void ArmourWornCountsOnlyTheSixArmourSlots()
        {
            var worn = new Dictionary<eInventorySlot, DbInventoryItem>
            {
                [eInventorySlot.RightHandWeapon] = new DbInventoryItem(),
                [eInventorySlot.DistanceWeapon] = new DbInventoryItem(),
                [eInventorySlot.Cloak] = new DbInventoryItem(),
            };
            Assert.That(AutonomousStarterGear.ArmorPiecesWorn(slot => worn.GetValueOrDefault(slot)), Is.Zero,
                "a starter weapon, a bow and a cloak are not armour");
            worn[eInventorySlot.HandsArmor] = new DbInventoryItem();
            worn[eInventorySlot.TorsoArmor] = new DbInventoryItem();
            Assert.That(AutonomousStarterGear.ArmorPiecesWorn(slot => worn.GetValueOrDefault(slot)), Is.EqualTo(2));
        }

        // ---- which slots ----

        private static readonly Func<eInventorySlot, bool> Nothing = _ => false;

        [Test]
        public void ASwordAndBoardBuildWantsSixArmourSlotsAWeaponAndAShield()
        {
            var wants = AutonomousStarterGear.PlanSlots(Nothing, eInventorySlot.RightHandWeapon, eObjectType.SlashingWeapon,
                0, shieldBuild: true, savage: false);
            Assert.That(wants.Where(w => w.IsArmor).Select(w => w.Slot), Is.EquivalentTo(AutonomousStarterGear.ArmorSlots));
            Assert.That(wants, Has.Member(new AutonomousStarterGear.SlotWant(eInventorySlot.RightHandWeapon, eObjectType.SlashingWeapon, false)));
            Assert.That(wants, Has.Member(new AutonomousStarterGear.SlotWant(eInventorySlot.LeftHandWeapon, 0, true)));
            Assert.That(wants.Count, Is.EqualTo(8));
        }

        [Test]
        public void ATwoHandedOrCasterBuildWantsNoOffhand()
        {
            var wants = AutonomousStarterGear.PlanSlots(Nothing, eInventorySlot.TwoHandWeapon, eObjectType.Staff,
                0, shieldBuild: true, savage: false);
            Assert.That(wants.Select(w => w.Slot), Has.No.Member(eInventorySlot.LeftHandWeapon));
            Assert.That(wants, Has.Member(new AutonomousStarterGear.SlotWant(eInventorySlot.TwoHandWeapon, eObjectType.Staff, false)));
        }

        [Test]
        public void ADualWieldBuildWantsItsOffhandWeaponNotAShield()
        {
            var wants = AutonomousStarterGear.PlanSlots(Nothing, eInventorySlot.RightHandWeapon, eObjectType.ThrustWeapon,
                eObjectType.ThrustWeapon, shieldBuild: false, savage: false);
            Assert.That(wants, Has.Member(new AutonomousStarterGear.SlotWant(eInventorySlot.LeftHandWeapon, eObjectType.ThrustWeapon, false)));
            Assert.That(wants.Any(w => w.Shield), Is.False);
        }

        [Test]
        public void OccupiedSlotsAreNeverWantedAndASavageKeepsItsOwnWeapons()
        {
            var occupied = new HashSet<eInventorySlot> { eInventorySlot.RightHandWeapon, eInventorySlot.HeadArmor };
            var wants = AutonomousStarterGear.PlanSlots(occupied.Contains, eInventorySlot.RightHandWeapon, eObjectType.SlashingWeapon,
                0, shieldBuild: true, savage: false);
            Assert.That(wants.Select(w => w.Slot), Has.No.Member(eInventorySlot.RightHandWeapon));
            Assert.That(wants.Select(w => w.Slot), Has.No.Member(eInventorySlot.HeadArmor));

            var savage = AutonomousStarterGear.PlanSlots(Nothing, eInventorySlot.RightHandWeapon, eObjectType.HandToHand,
                eObjectType.HandToHand, false, savage: true);
            Assert.That(savage.All(w => w.IsArmor), Is.True);
            Assert.That(savage.Count, Is.EqualTo(6));
        }

        // ---- which items ----

        private static DbItemTemplate Plain(string id, eObjectType type, int itemType, int level, int price = 1000, int hand = 0) =>
            new() { Id_nb = id, Name = id, Object_Type = (int)type, Item_Type = itemType, Level = level, Price = price, Hand = hand,
                    Quality = 85, DPS_AF = level * 2, SPD_ABS = 30 };

        [Test]
        public void OnlyPlainCoinPricedEquipmentIsMerchantStarterGear()
        {
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(Plain("jerkin", eObjectType.Studded, Slot.TORSO, 20)), Is.True);
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(Plain("buckler", eObjectType.Shield, Slot.LEFTHAND, 20)), Is.True);
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(Plain("sword", eObjectType.SlashingWeapon, Slot.RIGHTHAND, 20)), Is.True);

            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(Plain("free", eObjectType.Studded, Slot.TORSO, 20, price: 0)), Is.False, "not sold for coin");
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(Plain("arrows", eObjectType.Arrow, 40, 20)), Is.False);
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(Plain("ring", eObjectType.Magical, Slot.LEFTRING, 20)), Is.False, "jewellery is not starter gear");
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(null), Is.False);

            DbItemTemplate magical = Plain("magic", eObjectType.Studded, Slot.TORSO, 20);
            magical.Bonus1 = 5;
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(magical), Is.False, "a stat bonus is magical");
            DbItemTemplate bonus = Plain("bonus", eObjectType.Studded, Slot.TORSO, 20);
            bonus.Bonus = 10;
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(bonus), Is.False);
            DbItemTemplate proc = Plain("proc", eObjectType.SlashingWeapon, Slot.RIGHTHAND, 20);
            proc.ProcSpellID = 1234;
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(proc), Is.False);
            DbItemTemplate charged = Plain("charged", eObjectType.SlashingWeapon, Slot.RIGHTHAND, 20);
            charged.SpellID = 99;
            charged.Charges = 5;
            Assert.That(AutonomousStarterGear.IsCommonMerchantGear(charged), Is.False);
        }

        [Test]
        public void AnItemFitsOnlyItsSlotAndTheBotsLevel()
        {
            var torso = new AutonomousStarterGear.SlotWant(eInventorySlot.TorsoArmor, 0, false);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("t20", eObjectType.Chain, Slot.TORSO, 20), torso, 22), Is.True);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("t25", eObjectType.Chain, Slot.TORSO, 25), torso, 22), Is.False, "above its level");
            Assert.That(AutonomousStarterGear.FitsWant(Plain("legs", eObjectType.Chain, Slot.LEGS, 20), torso, 22), Is.False, "another slot");
            DbItemTemplate required = Plain("req", eObjectType.Chain, Slot.TORSO, 20);
            required.LevelRequirement = 23;
            Assert.That(AutonomousStarterGear.FitsWant(required, torso, 22), Is.False, "level requirement");

            var shield = new AutonomousStarterGear.SlotWant(eInventorySlot.LeftHandWeapon, 0, true);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("shield", eObjectType.Shield, Slot.LEFTHAND, 20), shield, 22), Is.True);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("dagger", eObjectType.ThrustWeapon, Slot.LEFTHAND, 20, hand: 2), shield, 22), Is.False);

            var mainHand = new AutonomousStarterGear.SlotWant(eInventorySlot.RightHandWeapon, eObjectType.SlashingWeapon, false);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("sword", eObjectType.SlashingWeapon, Slot.RIGHTHAND, 20), mainHand, 22), Is.True);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("mace", eObjectType.CrushingWeapon, Slot.RIGHTHAND, 20), mainHand, 22), Is.False, "not its build's weapon");
            Assert.That(AutonomousStarterGear.FitsWant(Plain("great", eObjectType.SlashingWeapon, Slot.TWOHAND, 20, hand: 1), mainHand, 22), Is.False);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("left", eObjectType.SlashingWeapon, Slot.LEFTHAND, 20, hand: 2), mainHand, 22), Is.False,
                "a left-hand weapon stays out of the main hand");

            var twoHand = new AutonomousStarterGear.SlotWant(eInventorySlot.TwoHandWeapon, eObjectType.Staff, false);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("staff", eObjectType.Staff, Slot.TWOHAND, 20, hand: 1), twoHand, 22), Is.True);

            var offhand = new AutonomousStarterGear.SlotWant(eInventorySlot.LeftHandWeapon, eObjectType.ThrustWeapon, false);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("dirk", eObjectType.ThrustWeapon, Slot.LEFTHAND, 20, hand: 2), offhand, 22), Is.True);
            Assert.That(AutonomousStarterGear.FitsWant(Plain("rapier", eObjectType.ThrustWeapon, Slot.RIGHTHAND, 20), offhand, 22), Is.False,
                "a main-hand-only weapon cannot be held in the left hand");
        }

        [Test]
        public void TheBestCandidateWinsAndATieDoesNotDependOnMerchantOrder()
        {
            var items = new[] { ("b", 5), ("a", 9), ("c", 9) };
            Assert.That(AutonomousStarterGear.PickBest(items.Select(i => Tuple.Create(i.Item1, i.Item2)), t => t.Item2, t => t.Item1)!.Item1,
                Is.EqualTo("a"));
            Assert.That(AutonomousStarterGear.PickBest(items.Reverse().Select(i => Tuple.Create(i.Item1, i.Item2)), t => t.Item2, t => t.Item1)!.Item1,
                Is.EqualTo("a"));
            Assert.That(AutonomousStarterGear.PickBest(Array.Empty<Tuple<string, int>>(), t => t.Item2, t => t.Item1), Is.Null);
        }

        [Test]
        public void TheLogNamesEachPieceWithItsSlotAndLevel()
        {
            var pieces = new List<(eInventorySlot, DbItemTemplate)>
            {
                (eInventorySlot.TorsoArmor, Plain("Studded Jerkin", eObjectType.Studded, Slot.TORSO, 20)),
                (eInventorySlot.RightHandWeapon, new DbItemTemplate { Name = "Long \"Sword\"", Level = 21 }),
            };
            Assert.That(AutonomousStarterGear.Describe(pieces),
                Is.EqualTo("Torso:Studded Jerkin(L20), RightHand:Long 'Sword'(L21)"));
            Assert.That(AutonomousStarterGear.Describe(null), Is.Empty);
        }

        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "CHANGELOG-LIVING-SERVER.md")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            return dir;
        }
    }
}
