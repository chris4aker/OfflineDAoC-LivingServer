using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP8: what a crafter gamebot honestly offers a player (CrafterQuotes, pure): the request read from chat,
    /// only what its skill makes surely, the best match, the board's price with the customer's discount, the time.
    /// Tests only: nothing here was seen in game.
    /// </summary>
    [TestFixture]
    public class UT_CrafterQuotes
    {
        internal static int Ac => (int)eCraftingSkill.ArmorCrafting;

        internal static CraftRecipeInfo Piece(string name, int level, int productLevel, eObjectType type, eInventorySlot slot, long material = 500,
            eCraftingSkill skill = eCraftingSkill.ArmorCrafting, int realm = 1) =>
            new($"r_{name}_{level}".Replace(' ', '_'), skill, level, $"p_{name}_{level}".Replace(' ', '_'), name, (int)type, (int)slot, productLevel,
                realm, true, false, [new CraftMaterial("bars", "bars", 0, 4, material / 4, 1)]);

        /// <summary>A small armorcraft book: studded and chain pieces at three levels.</summary>
        internal static List<CraftRecipeInfo> Book()
        {
            var book = new List<CraftRecipeInfo>();
            foreach ((int level, int productLevel) in new[] { (100, 10), (200, 20), (300, 30), (400, 40) })
            foreach ((string material, eObjectType type) in new[] { ("Studded", eObjectType.Studded), ("Chain", eObjectType.Chain) })
            foreach ((string piece, eInventorySlot slot) in new[]
                     {
                         (type == eObjectType.Chain ? "Hauberk" : "Jerkin", eInventorySlot.TorsoArmor), ("Leggings", eInventorySlot.LegsArmor),
                         ("Sleeves", eInventorySlot.ArmsArmor), (type == eObjectType.Chain ? "Coif" : "Helm", eInventorySlot.HeadArmor),
                         ("Gloves", eInventorySlot.HandsArmor), ("Boots", eInventorySlot.FeetArmor),
                     })
                book.Add(Piece($"{material} {piece}", level, productLevel, type, slot, 200 + level * 5));
            return book;
        }

        [Test]
        public void ARequestIsReadFromPlayerChat()
        {
            CrafterQuotes.Request r = CrafterQuotes.Parse("wtb 99% chain hauberk lvl 23");
            Assert.That(r.Quality, Is.EqualTo(99));
            Assert.That(r.Level, Is.EqualTo(23));
            Assert.That(r.ObjectType, Is.EqualTo((int)eObjectType.Chain));
            Assert.That(r.Slot, Is.EqualTo((int)eInventorySlot.TorsoArmor));
            Assert.That(r.Set, Is.False);

            CrafterQuotes.Request set = CrafterQuotes.Parse("can you make a full studded set for my 20?");
            Assert.That(set.Set, Is.True);
            Assert.That(set.Level, Is.EqualTo(20));
            Assert.That(set.ObjectType, Is.EqualTo((int)eObjectType.Studded));

            Assert.That(CrafterQuotes.Parse("lf sc").Craft, Is.EqualTo(eCraftingSkill.SpellCrafting));
            Assert.That(CrafterQuotes.Parse("can you make something?").Names, Is.False);
        }

        [Test]
        public void OnlyWhatTheSkillMakesSurelyIsOffered()
        {
            string skills = $"{Ac}|310";
            IReadOnlyList<CraftRecipeInfo> makeable = CrafterQuotes.Makeable(Book(), skills, eRealm.Albion);
            Assert.That(makeable, Is.Not.Empty);
            Assert.That(makeable.All(r => CraftRules.ChanceToMake(310, r.Level) >= AutonomousBotCraftOrders.MinimumMakeChance), Is.True);
            Assert.That(makeable.Any(r => r.Level == 400), Is.False, "a recipe 90 over its skill is not promised");
            Assert.That(CrafterQuotes.Makeable(Book(), $"{Ac}|310", eRealm.Midgard), Is.Empty, "Albion's recipes are not made for another realm");
            Assert.That(CrafterQuotes.Makeable(Book(), $"{(int)eCraftingSkill.Tailoring}|500", eRealm.Albion), Is.Empty, "a tailor makes no armorcraft");
        }

        [Test]
        public void TheBestMatchFitsTypeSlotAndLevel()
        {
            IReadOnlyList<CraftRecipeInfo> makeable = CrafterQuotes.Makeable(Book(), $"{Ac}|310", eRealm.Albion);
            CraftRecipeInfo best = CrafterQuotes.Best(CrafterQuotes.Parse("wtb chain hauberk lvl 21"), makeable);
            Assert.That(best.ProductName, Is.EqualTo("Chain Hauberk"));
            Assert.That(best.ProductLevel, Is.EqualTo(20));
            Assert.That(CrafterQuotes.Best(CrafterQuotes.Parse("wtb plate helm lvl 20"), makeable), Is.Null, "no plate in the book");
            Assert.That(CrafterQuotes.Best(CrafterQuotes.Parse("wtb chain coif lvl 45"), makeable), Is.Null, "beyond its skill");
            Assert.That(CrafterQuotes.Best(CrafterQuotes.Parse("need a wooden spoon"), makeable), Is.Null, "words matching nothing are another item");

            IReadOnlyList<CraftRecipeInfo> set = CrafterQuotes.BestSet(CrafterQuotes.Parse("studded set lvl 30"), makeable);
            Assert.That(set.Select(r => r.ItemType), Is.EquivalentTo(CrafterQuotes.SetSlots));
            Assert.That(set.All(r => r.ObjectType == (int)eObjectType.Studded && r.ProductLevel == 30), Is.True);
        }

        [Test]
        public void ThePriceIsTheBoardsQuoteLessTheStandingDiscount()
        {
            CraftRecipeInfo recipe = Piece("Chain Hauberk", 200, 20, eObjectType.Chain, eInventorySlot.TorsoArmor, 2000);
            CrafterQuotes.Offer stranger = CrafterQuotes.Price(recipe, $"{Ac}|310", 0, false);
            Assert.That(stranger.Materials, Is.EqualTo(2000));
            Assert.That(stranger.Price, Is.EqualTo(AutonomousBotCraftOrders.Quote(2000)), "a stranger pays what a gamebot buyer pays");
            Assert.That(CrafterQuotes.Price(recipe, $"{Ac}|310", 10, false).Fee, Is.EqualTo(stranger.Fee * 90 / 100));
            Assert.That(CrafterQuotes.Price(recipe, $"{Ac}|310", 0, true).Price, Is.EqualTo(2000), "a guildmate pays materials only");
            Assert.That(CrafterQuotes.Price(recipe, $"{Ac}|310", 90, false).Fee, Is.EqualTo(stranger.Fee / 2), "a discount never passes half the fee");
        }

        [Test]
        public void TheTimeIsTheNextBreakAndTheWork()
        {
            Assert.That(CrafterQuotes.MinutesUntilReady(30, 60, 1), Is.EqualTo(31));
            Assert.That(CrafterQuotes.MinutesUntilReady(0, 60, 6, taskMinutes: 60), Is.EqualTo(66), "a set takes two trips");
            Assert.That(CrafterQuotes.TimeText(31), Is.EqualTo("about 35 min"));
            Assert.That(CrafterQuotes.TimeText(66), Is.EqualTo("an hour or so"));
        }
    }

    /// <summary>Crafting 2.0 WP8: the bounded haggle (CrafterHaggle, pure). Tests only.</summary>
    [TestFixture]
    public class UT_CrafterHaggle
    {
        [Test]
        public void AnOfferAtTheAskOrCloseIsTaken()
        {
            Assert.That(CrafterHaggle.Answer(10_000, 10_000, 7_000, 1, false, 50).Verdict, Is.EqualTo(CrafterHaggle.Verdict.Accept));
            CrafterHaggle.Result close = CrafterHaggle.Answer(10_000, 9_200, 7_000, 1, false, 50);
            Assert.That(close.Verdict, Is.EqualTo(CrafterHaggle.Verdict.Accept));
            Assert.That(close.Price, Is.EqualTo(9_200));
        }

        [Test]
        public void ALowOfferGetsACounterNeverBelowTheFloor()
        {
            CrafterHaggle.Result counter = CrafterHaggle.Answer(10_000, 7_500, 7_000, 1, false, 50);
            Assert.That(counter.Verdict, Is.EqualTo(CrafterHaggle.Verdict.Counter));
            Assert.That(counter.Price, Is.InRange(7_000, 9_999));
            for (long offer = 0; offer <= 10_000; offer += 250)
                for (int round = 1; round <= 4; round++)
                    foreach (bool friendly in new[] { false, true })
                    {
                        CrafterHaggle.Result result = CrafterHaggle.Answer(10_000, offer, 7_000, round, friendly, 30);
                        Assert.That(result.Price, Is.GreaterThanOrEqualTo(7_000), $"offer {offer} round {round}");
                        Assert.That(result.Price, Is.LessThanOrEqualTo(10_000));
                    }
        }

        [Test]
        public void AFriendComesDownFurtherAndTakesAnyFairOffer()
        {
            long stranger = CrafterHaggle.Answer(10_000, 7_500, 7_000, 1, false, 50).Price;
            CrafterHaggle.Result friend = CrafterHaggle.Answer(10_000, 7_500, 7_000, 1, true, 50);
            Assert.That(friend.Verdict, Is.EqualTo(CrafterHaggle.Verdict.Accept), "a friend takes any offer above its costs");
            Assert.That(CrafterHaggle.Answer(10_000, 6_000, 7_000, 1, true, 50).Price, Is.LessThan(stranger));
        }

        [Test]
        public void AnInsultingOfferIsRefusedAndTheHaggleEnds()
        {
            Assert.That(CrafterHaggle.Answer(10_000, 1_000, 7_000, 1, false, 50).Verdict, Is.EqualTo(CrafterHaggle.Verdict.Refuse));
            Assert.That(CrafterHaggle.Answer(10_000, 1_000, 7_000, CrafterHaggle.MaxRounds, false, 50).Verdict, Is.EqualTo(CrafterHaggle.Verdict.Walk));
            Assert.That(CrafterHaggle.Answer(10_000, 9_000, 7_000, CrafterHaggle.MaxRounds + 1, false, 50).Verdict, Is.EqualTo(CrafterHaggle.Verdict.Walk));
        }

        [Test]
        public void OffersAreReadAsPlayersWriteThem()
        {
            Assert.That(CrafterHaggle.ParseCopper("1g 50s?"), Is.EqualTo(15_000));
            Assert.That(CrafterHaggle.ParseCopper("how about 80s"), Is.EqualTo(8_000));
            Assert.That(CrafterHaggle.ParseCopper("2.5g"), Is.EqualTo(25_000));
            Assert.That(CrafterHaggle.ParseCopper("would you take 90", askCopper: 12_000), Is.EqualTo(900_000));
            Assert.That(CrafterHaggle.ParseCopper("would you take 90", askCopper: 9_000), Is.EqualTo(9_000));
            Assert.That(CrafterHaggle.ParseCopper("ok deal"), Is.Null);
            Assert.That(CrafterHaggle.Floor(1000, 300, false), Is.EqualTo(1150));
            Assert.That(CrafterHaggle.Floor(1000, 300, true), Is.EqualTo(1300));
        }
    }
}
