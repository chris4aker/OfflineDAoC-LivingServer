using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS;
using DOL.GS.Commands;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafter replies and the crafter roster, from the owner's PC (build b1b6846, 2026-11-18, Leofrunn, an Assistant
    /// Weaponcrafter at 499): a "what do you make?" answer cut mid-word at 160 characters, a "can you make a fine alloy needle
    /// mace" answered with a different stock piece, an agreed stock sale that lapsed before the crafter's break, and
    /// /lscraft craft saying only "Crafter roster: registered.". Tests only.
    /// </summary>
    [TestFixture]
    public class UT_CrafterRepliesFit
    {
        private static readonly DateTime Now = new(2026, 11, 18, 12, 35, 0, DateTimeKind.Utc);

        private static CraftRecipeInfo Weapon(string name, int level, int productLevel) =>
            new($"r_{name}".Replace(' ', '_'), eCraftingSkill.WeaponCrafting, level, $"p_{name}".Replace(' ', '_'), name,
                (int)eObjectType.CrushingWeapon, (int)eInventorySlot.RightHandWeapon, productLevel, 1, true, false,
                [new CraftMaterial("bars", "bars", 0, 4, 400, 1)]);

        private static readonly CraftRecipeInfo[] Book =
        [
            Weapon("Fine Alloy Archmace", 460, 26), Weapon("Fine Alloy Bardiche", 460, 26), Weapon("Fine Alloy Needle Mace", 460, 26),
            Weapon("Fine Alloy Exceptional Mace", 460, 26), Weapon("Alloy Needle Mace", 360, 21),
        ];

        private static CrafterTalk.StockPiece Stock(string name, int quality, long price, string id = "obj1") =>
            new(id, name, quality, 26, price, (int)eObjectType.CrushingWeapon, (int)eInventorySlot.RightHandWeapon);

        /// <summary>Leofrunn as the log shows him: out hunting, about an hour to his break.</summary>
        private static CrafterTalk.CrafterFacts Leofrunn(params CrafterTalk.StockPiece[] stock) =>
            new("Leofrunn", eRealm.Albion, $"{(int)eCraftingSkill.WeaponCrafting}|499", eCraftingSkill.WeaponCrafting, 499,
                "Assistant Weaponcrafter", Book, stock, 62, false, 60, "Avalon Marsh", 40);

        private static CrafterTalk.CustomerFacts Testrunner(bool near = false) =>
            new("player:p1", "Testrunner", CrafterCustomers.Standing.Stranger, 0, false, near);

        private static VC _corpus;

        private static VC Corpus => _corpus ??= LoadCorpus();

        private static VC LoadCorpus()
        {
            var assembly = typeof(VC).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                using Stream stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        }

        /// <summary>True when the line ends at a whole word of the source (or its "..." after one).</summary>
        private static bool EndsAtAWord(string line, string source)
        {
            string body = line.EndsWith("...", StringComparison.Ordinal) ? line[..^3] : line;
            body = body.TrimEnd('.', ',', '!', '?');
            if (body.Length == 0)
                return false;
            int at = source.IndexOf(body, StringComparison.Ordinal);
            if (at < 0)
                return true; // styled beyond recognition: the cut itself is checked by Fit's own test
            int end = at + body.Length;
            return end >= source.Length || !char.IsLetterOrDigit(source[end]);
        }

        [Test]
        public void ALongLineIsCutAtAWholeWord()
        {
            string log = "Assistant Weaponcrafter here, 499 weaponcraft. i make fine alloy archmace, fine alloy bardiche, fine alloy needle mace " +
                         "up to lvl 26. in stock: fine alloy exceptional mace 96% 62s";
            Assert.That(log.Length, Is.GreaterThan(VC.MaxLength));
            string fitted = VC.Fit(log);
            TestContext.Out.WriteLine(fitted);
            Assert.That(fitted.Length, Is.LessThanOrEqualTo(VC.MaxLength));
            Assert.That(fitted, Does.EndWith("..."));
            Assert.That(fitted, Does.Not.Contain("except..."), "never mid-word");
            Assert.That(EndsAtAWord(fitted, log), Is.True, fitted);
            Assert.That(VC.Fit("short line"), Is.EqualTo("short line"));

            // The style layer (every voice) ends at a word too.
            var random = new Random(7);
            foreach (VC.Voice voice in Corpus.Voices)
                for (int i = 0; i < 20; i++)
                {
                    string styled = VC.Style(log, voice, random);
                    Assert.That(styled.Length, Is.LessThanOrEqualTo(VC.MaxLength), voice.Name);
                    Assert.That(styled, Does.Not.EndWith("except").And.Not.EndWith("except..."), voice.Name);
                }
        }

        [Test]
        public void WhatDoYouMakeFitsOneTellAndListsTheStockInWholePieces()
        {
            CrafterTalk.Reply reply = CrafterTalk.Answer(Leofrunn(Stock("Fine Alloy Exceptional Mace", 96, 6_200)), Testrunner(), "what do you make?",
                null, [], Now);
            TestContext.Out.WriteLine(reply.Text + "\n" + reply.More);
            Assert.That(reply.Act, Is.EqualTo(CrafterTalk.Act.Summary));
            Assert.That(reply.Text.Length, Is.LessThanOrEqualTo(CrafterTalk.TellRoom - CrafterTalk.StyleRoom), reply.Text);
            Assert.That(reply.Text, Does.Contain("fine alloy archmace, bardiche"), "the shared material is said once");
            Assert.That(reply.Text, Does.Contain("1 piece in stock"));
            Assert.That(reply.More, Is.EqualTo("in stock: fine alloy exceptional mace 96% 62s"), "the whole piece, in a second line");

            // Every summary line of the corpus, filled with these slots, fits a tell with room for the style.
            VC.Line[] lines = Corpus.Lines.Where(l => l.Kind == "craft_reply" && l.Topic is "craft_summary" or "craft_summary_stock").ToArray();
            Assert.That(lines, Is.Not.Empty);
            foreach (VC.Line line in lines)
            {
                Assert.That(VC.TryFill(line.Text, reply.Slots, out string filled), Is.True, line.Id);
                Assert.That(filled.Length, Is.LessThanOrEqualTo(CrafterTalk.TellRoom - CrafterTalk.StyleRoom), filled);
            }
        }

        [Test]
        public void ManyLongItemsAndPiecesStillFitWithoutCuttingAName()
        {
            CrafterTalk.StockPiece[] stock =
            [
                Stock("Fine Alloy Exceptional Mace", 96, 6_200, "a"), Stock("Fine Alloy Needle Mace", 99, 7_450, "b"),
                Stock("Fine Alloy Archmace", 97, 12_345, "c"), Stock("Fine Alloy Bardiche", 98, 9_900, "d"),
            ];
            var crafter = Leofrunn(stock) with { Title = "Junior Apprentice Weaponcrafter" };
            CrafterTalk.Reply reply = CrafterTalk.Answer(crafter, Testrunner(), "what do you make?", null, [], Now);
            TestContext.Out.WriteLine(reply.Text + "\n" + reply.More);
            Assert.That(reply.Text.Length, Is.LessThanOrEqualTo(CrafterTalk.TellRoom - CrafterTalk.StyleRoom));
            Assert.That(reply.More, Is.Not.Null);
            Assert.That(reply.More.Length, Is.LessThanOrEqualTo(CrafterTalk.TellRoom - CrafterTalk.StyleRoom));
            Assert.That(reply.More, Does.EndWith("more"), "the pieces not named are counted");
            foreach (string said in reply.More["in stock: ".Length..].Split(", ").Where(s => !s.StartsWith('+')))
                Assert.That(stock.Any(p => said.StartsWith(p.Name.ToLowerInvariant() + " " + p.Quality + "%", StringComparison.Ordinal)), said);
            Assert.That(CrafterTalk.ItemList(["fine alloy archmace", "fine alloy bardiche", "fine alloy needle mace"], 25),
                Is.EqualTo("fine alloy archmace"), "whole names only");
        }

        [Test]
        public void TheNamedItemIsQuotedNotADifferentStockPiece()
        {
            CrafterTalk.Reply reply = CrafterTalk.Answer(Leofrunn(Stock("Fine Alloy Exceptional Mace", 96, 6_200)), Testrunner(),
                "can you make a fine alloy needle mace", null, [], Now);
            TestContext.Out.WriteLine(reply.Text);
            Assert.That(reply.Act, Is.EqualTo(CrafterTalk.Act.Quote));
            Assert.That(reply.Deal.Kind, Is.EqualTo(CrafterTalk.DealKind.Order), "a commission, not the stock piece");
            Assert.That(reply.Deal.Recipes.Single().ProductName, Is.EqualTo("Fine Alloy Needle Mace"));
            Assert.That(reply.Text, Does.Contain("fine alloy needle mace"));

            // The asked piece in stock is still offered first.
            CrafterTalk.Reply held = CrafterTalk.Answer(Leofrunn(Stock("Fine Alloy Needle Mace", 98, 7_000)), Testrunner(),
                "can you make a fine alloy needle mace", null, [], Now);
            Assert.That(held.Deal.Kind, Is.EqualTo(CrafterTalk.DealKind.Stock));
            Assert.That(held.Deal.Piece.Name, Is.EqualTo("Fine Alloy Needle Mace"));
        }

        [Test]
        public void AnAgreedStockSaleStandsUntilTheCraftersBreak()
        {
            var crafter = Leofrunn(Stock("Fine Alloy Exceptional Mace", 96, 6_200));
            CrafterTalk.Reply quote = CrafterTalk.Answer(crafter, Testrunner(), "wtb fine alloy exceptional mace", null, [], Now);
            Assert.That(quote.Deal.Kind, Is.EqualTo(CrafterTalk.DealKind.Stock));
            CrafterTalk.Reply deal = CrafterTalk.Answer(crafter, Testrunner(), "deal", quote.Deal, [], Now);
            Assert.That(deal.Topic, Is.EqualTo("craft_stock_meet"));
            Assert.That(deal.Deal.UntilUtc, Is.GreaterThanOrEqualTo(Now.AddMinutes(crafter.MinutesToBreak)), "it stands past the break it names");

            // An hour later, at the break with the player near: "yes" opens the trade.
            DateTime atBreak = Now.AddMinutes(65);
            var there = crafter with { AtBreak = true, MinutesToBreak = 0 };
            CrafterTalk.Reply yes = CrafterTalk.Answer(there, Testrunner(near: true), "yes", deal.Deal, [], atBreak);
            Assert.That(yes?.Act, Is.EqualTo(CrafterTalk.Act.OpenTrade));
        }

        [Test]
        public void TheCraftPartListsTheCraftersInTheGame()
        {
            static CrafterState State(long id, string name, eRealm realm, eCraftingSkill order, int skill, int level) =>
                new(id, name, realm, level, order, new Dictionary<eCraftingSkill, int> { [order] = skill }, 0, 0, CrafterSpendPolicy.Default);
            CraftEconomyTestCommandHandler.RosterEntry[] roster =
            [
                new(State(3364, "Leofrunn", eRealm.Albion, eCraftingSkill.WeaponCrafting, 499, 50), true, "Avalon Marsh", false),
                new(State(5581, "Wolfsoul", eRealm.Albion, eCraftingSkill.SpellCrafting, 65, 24), true, "Thidranki", true),
                new(State(9, "", eRealm.Albion, eCraftingSkill.Tailoring, 300, 0), false, "", false),
                new(State(3936, "Steelhide", eRealm.Midgard, eCraftingSkill.WeaponCrafting, 720, 40), true, "Jordheim", true),
            ];
            string[] lines = CraftEconomyTestCommandHandler.RosterLines(roster);
            foreach (string line in lines)
                TestContext.Out.WriteLine(line);
            Assert.That(lines[0], Does.Contain("4 crafter gamebots, 3 in the game"));
            Assert.That(lines, Has.Some.Contains("Albion: 3 crafters, 2 in the game"));
            Assert.That(lines, Has.Some.EqualTo("    Leofrunn: weaponcraft 499, level 50, Avalon Marsh, on a task."));
            Assert.That(lines, Has.Some.EqualTo("    Wolfsoul: spellcraft 65, level 24, Thidranki, on a break."));
            Assert.That(lines, Has.Some.Contains("Steelhide: weaponcraft 720"));
            Assert.That(Array.FindIndex(lines, l => l.Contains("Leofrunn")), Is.LessThan(Array.FindIndex(lines, l => l.Contains("Wolfsoul"))),
                "the best skill first");
            Assert.That(lines, Has.None.EqualTo("Crafter roster: registered."));
            Assert.That(CraftEconomyTestCommandHandler.RosterLines([])[0], Does.StartWith("Crafter roster: no crafter gamebots"));
        }
    }
}
