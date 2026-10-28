using System;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

// Delivery items: no second copy of a carried item, the item handed back after a hand-over, the first step's item on
// accept (not for a delivery back to the giver), and "id;N" turn-ins. From lometur/HearthDAoC.
[TestFixture]
public sealed class UT_DataQuestDeliveryItem
{
    private static readonly string[] Nothing = Array.Empty<string>();
    private const string Target = "Lady Elaine";
    private const string Giver = "Master Edric";

    [Test]
    public void NotCarriedAndNotBeingHandedIsHanded()
        => Assert.That(QuestDeliveryItems.ShouldHand("cq_supplies", new[] { "other_item" }, Nothing), Is.True);

    [Test]
    public void NothingCarriedIsHanded()
        => Assert.That(QuestDeliveryItems.ShouldHand("cq_supplies", Nothing, Nothing), Is.True);

    [Test]
    public void CarriedIsNotHandedAgain()
        => Assert.That(QuestDeliveryItems.ShouldHand("cq_supplies", new[] { "other_item", "cq_supplies" }, Nothing), Is.False);

    [Test]
    public void BeingHandedByTheFinishingStepIsNotHandedAgain()
        => Assert.That(QuestDeliveryItems.ShouldHand("cq_supplies", Nothing, new[] { "cq_supplies" }), Is.False);

    [Test]
    public void AnotherItemBeingHandedDoesNotMatter()
        => Assert.That(QuestDeliveryItems.ShouldHand("cq_supplies", Nothing, new[] { "cq_letter" }), Is.True);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void AnEmptyTemplateHandsNothing(string template)
        => Assert.That(QuestDeliveryItems.ShouldHand(template, Nothing, Nothing), Is.False);

    [Test]
    public void TemplateIdsCompareWithoutCaseAndSpaces()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.ShouldHand("CQ_Supplies", new[] { "cq_supplies" }, Nothing), Is.False);
            Assert.That(QuestDeliveryItems.ShouldHand("cq_supplies", Nothing, new[] { "CQ_SUPPLIES" }), Is.False);
            Assert.That(QuestDeliveryItems.ShouldHand(" cq_supplies ", new[] { "cq_supplies" }, Nothing), Is.False);
        });
    }

    [Test]
    public void ALongerIdIsNotTheSameItem()
        => Assert.That(QuestDeliveryItems.ShouldHand("cq_supplies", new[] { "cq_supplies_2" }, Nothing), Is.True);

    [Test]
    public void NullListsAreEmpty()
        => Assert.That(QuestDeliveryItems.ShouldHand("cq_supplies", null, null), Is.True);

    // "Path of the Renegade", stage 4 -> 5: the player gives the Arawnite orders to Omis (Deliver), who writes between
    // the lines and gives them back for Captain Rhodri (DeliverFinish of the same item). The orders are still in the
    // backpack while the step advances and leave it afterwards, so the new copy must be handed.
    [Test]
    public void DeliverXThenDeliverXHandsTheNewCopy()
    {
        var backpack = QuestDeliveryItems.CarriedIds(new[] { ("cq_arawnite_orders", 1, 1), ("cq_bread", 1, 0) });
        Assert.That(QuestDeliveryItems.ShouldHand("cq_arawnite_orders", backpack, Nothing), Is.True);
    }

    [Test]
    public void AnotherCopyOfTheItemHandedOverStillCounts()
    {
        var backpack = QuestDeliveryItems.CarriedIds(new[] { ("cq_arawnite_orders", 1, 1), ("cq_arawnite_orders", 1, 0) });
        Assert.That(QuestDeliveryItems.ShouldHand("cq_arawnite_orders", backpack, Nothing), Is.False);
    }

    [Test]
    public void TheItemHandedOverIsNotCarried()
        => Assert.That(QuestDeliveryItems.CarriedIds(new[] { ("cq_orders", 1, 1), ("cq_letter", 1, 0) }), Is.EqualTo(new[] { "cq_letter" }));

    // DataQuest hands a stack over whole (RemoveItem takes the item), so it passes the whole count: no copy is carried.
    [Test]
    public void AStackHandedOverWholeIsNotCarried()
        => Assert.That(QuestDeliveryItems.CarriedIds(new[] { ("cq_orders", 3, 3) }), Is.Empty);

    // Only a partial hand-over, which DataQuest never makes, leaves copies that count.
    [Test]
    public void TheCopiesLeftOfAStackAreCarried()
        => Assert.That(QuestDeliveryItems.CarriedIds(new[] { ("cq_orders", 3, 1) }), Is.EqualTo(new[] { "cq_orders" }));

    [Test]
    public void AnItemNotHandedOverIsCarriedWhateverItsCount()
        => Assert.That(QuestDeliveryItems.CarriedIds(new[] { ("cq_a", 1, 0), ("cq_b", 20, 0), ("cq_c", 0, 0) }), Is.EqualTo(new[] { "cq_a", "cq_b", "cq_c" }));

    [Test]
    public void ANullBackpackCarriesNothing()
        => Assert.That(QuestDeliveryItems.CarriedIds(null), Is.Empty);

    [Test]
    public void AFirstDeliveryStepHandsItsItem()
        => Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_document", "", "" }, Nothing, Target, Giver), Is.EqualTo("cq_document"));

    [Test]
    public void TheFirstStepItemIsReturnedTrimmed()
        => Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { " cq_document " }, Nothing, Target, Giver), Is.EqualTo("cq_document"));

    [Test]
    public void OnlyTheFirstEntryCounts()
        => Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "", "cq_document" }, Nothing, Target, Giver), Is.Null);

    [Test]
    public void AFirstStepThatIsNotADeliveryHandsNothing()
        => Assert.That(QuestDeliveryItems.FirstStepItem(false, new[] { "cq_document" }, Nothing, Target, Giver), Is.Null);

    [Test]
    public void AFirstStepItemAlreadyCarriedIsNotHandedAgain()
        => Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_document" }, new[] { "CQ_Document" }, Target, Giver), Is.Null);

    [Test]
    public void NoStepItemsHandNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.FirstStepItem(true, null, Nothing, Target, Giver), Is.Null);
            Assert.That(QuestDeliveryItems.FirstStepItem(true, Array.Empty<string>(), Nothing, Target, Giver), Is.Null);
            Assert.That(QuestDeliveryItems.FirstStepItem(true, new string[] { null }, Nothing, Target, Giver), Is.Null);
        });
    }

    // 20098 "Sveabone Hilt Sword": Gridash says "Run along and buy me a bronze short sword", and step 1 is the delivery
    // of cq_bronze_short_sword to Gridash.
    [Test]
    public void AFirstDeliveryBackToTheGiverHandsNothing()
        => Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_bronze_short_sword", "cq_sveawolf_tooth", "" }, Nothing, "Gridash", "Gridash"), Is.Null);

    [Test]
    public void TheGiverIsFoundWithoutCaseAndWithoutTheRegion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_red_dagger_map" }, Nothing, "audun", "Audun"), Is.Null);
            Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_red_dagger_map" }, Nothing, "Audun;101", "Audun"), Is.Null);
            Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_red_dagger_map" }, Nothing, " Audun ", "AUDUN"), Is.Null);
        });
    }

    [Test]
    public void ADeliveryToSomeoneElseIsHanded()
        => Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_letter" }, Nothing, "Gyda", "Gridash"), Is.EqualTo("cq_letter"));

    [Test]
    public void ANameThatOnlyStartsLikeTheGiversIsSomeoneElse()
        => Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_letter" }, Nothing, "Gridash the Elder", "Gridash"), Is.EqualTo("cq_letter"));

    [Test]
    public void AnUnknownTargetOrGiverIsNotTheGiver()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_letter" }, Nothing, null, Giver), Is.EqualTo("cq_letter"));
            Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_letter" }, Nothing, "", ""), Is.EqualTo("cq_letter"));
            Assert.That(QuestDeliveryItems.FirstStepItem(true, new[] { "cq_letter" }, Nothing, Target, null), Is.EqualTo("cq_letter"));
        });
    }

    // ClassicQuests.GiveDeliveryItem re-hands a delivery item every refresh; a first delivery back to the giver is one
    // the player brings, so it must be skipped.
    [TestCase(20098, "Gridash", "Gridash")] // Sveabone Hilt Sword: "Run along and buy me a bronze short sword"
    [TestCase(20781, "Duana", "Duana")]     // Seek the Moonstone C (20781-20784): the collar an earlier quest gave
    [TestCase(21008, "Audun", "Audun")]     // An End to the Daggers (21008-21013): the Red Dagger Map
    public void AFirstDeliveryBackToTheGiverIsAnItemThePlayerBrings(int quest, string target, string giver)
        => Assert.That(QuestDeliveryItems.BringsToGiver(1, target, giver), Is.True, $"quest {quest}");

    [Test]
    public void TheRefreshFindsTheGiverWithoutCaseAndWithoutTheRegion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.BringsToGiver(1, "audun", "Audun"), Is.True);
            Assert.That(QuestDeliveryItems.BringsToGiver(1, "Audun;101", "Audun"), Is.True);
            Assert.That(QuestDeliveryItems.BringsToGiver(1, " Audun ", "AUDUN"), Is.True);
        });
    }

    // 21008's step 4 takes Audun the note that Arica's kill (step 2) handed: the quest gave it, so a lost one is handed
    // again, as upstream does.
    [Test]
    public void ALaterDeliveryBackToTheGiverIsHandedOnRefresh()
        => Assert.That(QuestDeliveryItems.BringsToGiver(4, "Audun", "Audun"), Is.False);

    [Test]
    public void AFirstDeliveryToSomeoneElseIsHandedOnRefresh()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.BringsToGiver(1, "Gyda", "Gridash"), Is.False);
            Assert.That(QuestDeliveryItems.BringsToGiver(1, "Gridash the Elder", "Gridash"), Is.False);
            Assert.That(QuestDeliveryItems.BringsToGiver(1, null, Giver), Is.False);
            Assert.That(QuestDeliveryItems.BringsToGiver(1, "", ""), Is.False);
            Assert.That(QuestDeliveryItems.BringsToGiver(1, Target, null), Is.False);
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NoStepIsTheFirstStep(int step)
        => Assert.That(QuestDeliveryItems.BringsToGiver(step, "Gridash", "Gridash"), Is.False);

    private const string Stone = "cq_crediac_stone";
    private static readonly (string, int)[] NoOthers = Array.Empty<(string, int)>();

    [Test]
    public void AnEntryWithACountNeedsThatMany()
        => Assert.That(QuestDeliveryItems.CollectEntry("a;2"), Is.EqualTo(("a", 2)));

    [Test]
    public void ALargerCountIsReadToo()
        => Assert.That(QuestDeliveryItems.CollectEntry("cq_pelt;12"), Is.EqualTo(("cq_pelt", 12)));

    [Test]
    public void AnEntryWithoutACountNeedsOne()
        => Assert.That(QuestDeliveryItems.CollectEntry("a"), Is.EqualTo(("a", 1)));

    // Upstream compares the whole entry: anything that isn't "id;N" with N >= 2 stays as it is.
    [TestCase("a;1")]
    [TestCase("a;0")]
    [TestCase("a;x")]
    [TestCase("a;-2")]
    [TestCase("a;")]
    [TestCase(";2")]
    [TestCase(" ;2")]
    [TestCase(" a ")]
    [TestCase("")]
    public void AnyOtherEntryIsReadAsUpstreamReadsIt(string entry)
        => Assert.That(QuestDeliveryItems.CollectEntry(entry), Is.EqualTo((entry, 1)));

    [Test]
    public void ANullEntryNeedsOneOfNothing()
        => Assert.That(QuestDeliveryItems.CollectEntry(null), Is.EqualTo(((string)null, 1)));

    [TestCase("a; 2")]
    [TestCase("a ;2")]
    [TestCase(" a ; 2 ")]
    public void SpacesAroundTheIdAndTheCountAreTrimmed(string entry)
        => Assert.That(QuestDeliveryItems.CollectEntry(entry), Is.EqualTo(("a", 2)));

    [Test]
    public void TheLevel11EntryNeedsBothStones()
        => Assert.That(QuestDeliveryItems.CollectEntry("cq_crediac_stone;2"), Is.EqualTo((Stone, 2)));

    [Test]
    public void TheCollectItemRuleIsUpstreamsContainsWithoutCase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.IsCollectItem(Stone, Stone), Is.True);
            Assert.That(QuestDeliveryItems.IsCollectItem("CQ_Crediac_Stone", Stone), Is.True);
            Assert.That(QuestDeliveryItems.IsCollectItem(Stone, "CQ_CREDIAC_STONE"), Is.True);
            Assert.That(QuestDeliveryItems.IsCollectItem(Stone, "cq_crediac"), Is.True);
            Assert.That(QuestDeliveryItems.IsCollectItem("cq_crediac", Stone), Is.False);
            Assert.That(QuestDeliveryItems.IsCollectItem("cq_bread", Stone), Is.False);
        });
    }

    [Test]
    public void NoIdIsNoCollectItem()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.IsCollectItem(Stone, null), Is.False);
            Assert.That(QuestDeliveryItems.IsCollectItem(Stone, ""), Is.False);
            Assert.That(QuestDeliveryItems.IsCollectItem(null, Stone), Is.False);
        });
    }

    // Level 11, stage 4: the player hands Omis one stone and carries the other.
    [Test]
    public void BothStonesAreEnoughAndTheOtherOneIsTaken()
        => Assert.That(QuestDeliveryItems.OthersToTake(new[] { ("cq_bread", 1), (Stone, 1) }, Stone, 2, 1), Is.EqualTo(new[] { 0, 1 }));

    [Test]
    public void OneStoneIsNotEnough()
        => Assert.That(QuestDeliveryItems.OthersToTake(new[] { ("cq_bread", 1) }, Stone, 2, 1), Is.Null);

    [Test]
    public void NothingElseCarriedIsNotEnough()
        => Assert.That(QuestDeliveryItems.OthersToTake(NoOthers, Stone, 2, 1), Is.Null);

    [Test]
    public void OthersMatchByTheSameRuleAsTheItemHandedOver()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.OthersToTake(new[] { ("CQ_Crediac_Stone", 1) }, Stone, 2, 1), Is.EqualTo(new[] { 1 }));
            Assert.That(QuestDeliveryItems.OthersToTake(new[] { ("cq_crediac", 1) }, Stone, 2, 1), Is.Null);
        });
    }

    [Test]
    public void OnlyTheCopiesNeededAreTaken()
        => Assert.That(QuestDeliveryItems.OthersToTake(new[] { (Stone, 1), (Stone, 1) }, Stone, 2, 1), Is.EqualTo(new[] { 1, 0 }));

    [Test]
    public void AStackCountsForItsCopiesAndLosesOnlyThoseWanted()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.OthersToTake(new[] { ("cq_pelt", 5) }, "cq_pelt", 3, 1), Is.EqualTo(new[] { 2 }));
            Assert.That(QuestDeliveryItems.OthersToTake(new[] { ("cq_pelt", 1), ("cq_pelt", 4) }, "cq_pelt", 4, 1), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(QuestDeliveryItems.OthersToTake(new[] { ("cq_pelt", 1) }, "cq_pelt", 3, 1), Is.Null);
        });
    }

    [Test]
    public void TheItemHandedOverCountsForItsCopiesAndAtLeastOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.OthersToTake(NoOthers, "cq_pelt", 2, 2), Is.Empty);
            Assert.That(QuestDeliveryItems.OthersToTake(new[] { ("cq_pelt", 1) }, "cq_pelt", 2, 0), Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public void AStepThatNeedsOneTakesNothingMore()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDeliveryItems.OthersToTake(new[] { (Stone, 1) }, Stone, 1, 1), Is.EqualTo(new[] { 0 }));
            Assert.That(QuestDeliveryItems.OthersToTake(null, Stone, 1, 1), Is.Empty);
        });
    }

    [Test]
    public void NoOtherItemsAreTooFewForTwo()
        => Assert.That(QuestDeliveryItems.OthersToTake(null, Stone, 2, 1), Is.Null);
}
