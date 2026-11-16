using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP2: every gamebot coin move is named without editing a call site. The explicit kind wins; otherwise
    /// the caller's file and member decide. The source scan finds every AutonomousBotEconomy.AddMoney/TrySpend call in
    /// the server and checks it classifies to a named kind, so a new call site must pass a kind or add a row.
    /// </summary>
    [TestFixture]
    public class UT_EconomyFlowClassifier
    {
        [Test]
        public void TheCallersExplicitKindWins()
        {
            Assert.That(EconomyFlowClassifier.Classify(EconomyFlowKind.Potion, "GameBot.cs", "TryAutoPickUpMoney"),
                Is.EqualTo(EconomyFlowKind.Potion));
        }

        [TestCase(@"C:\OfflineDAoC\source\server\GameServer\bots\GameBot.cs", "TryAutoPickUpMoney", EconomyFlowKind.LootCoin)]
        [TestCase("/home/x/GameServer/bots/autonomous/AutonomousBotGuildEconomy.cs", "PayLootShare", EconomyFlowKind.LootShare)]
        [TestCase("AutonomousBotEconomy.cs", "TrySellToVendor", EconomyFlowKind.VendorSale)]
        [TestCase("AutonomousBotEconomy.cs", "TryBuyUsefulVendorUpgrade", EconomyFlowKind.VendorGear)]
        [TestCase("GameBot.cs", "ContinueReturnJourney", EconomyFlowKind.StableTicket)]
        [TestCase("AutonomousWorldBotController.cs", "TryBeginFasterStableRoute", EconomyFlowKind.StableTicket)]
        [TestCase("AutonomousWorldBotController.cs", "HandleRealmRaidStableTravel", EconomyFlowKind.StableTicket)]
        [TestCase("AutonomousWorldBotController.FrontierTransport.cs", "TryFrontierTransport", EconomyFlowKind.Medallion)]
        [TestCase("BotSiegeRuntime.cs", "TryRestockRepairKits", EconomyFlowKind.RepairKit)]
        [TestCase("AutonomousWorldBotController.Siege.cs", "SupplySiegeItem", EconomyFlowKind.Siege)]
        [TestCase("AutonomousBotCosmetics.cs", "TryRestyle", EconomyFlowKind.Dye)]
        [TestCase("AutonomousBeggars.cs", "TryBegBot", EconomyFlowKind.BeggarGift)]
        [TestCase("AutonomousBeggars.cs", "TryReceiveMoney", EconomyFlowKind.BeggarGift)]
        [TestCase("RealmExchangeBroker.cs", "CreditSeller", EconomyFlowKind.ExchangeSale)]
        public void TodaysCallSitesAreNamed(string file, string member, EconomyFlowKind expected)
        {
            Assert.That(EconomyFlowClassifier.Classify(EconomyFlowKind.Unattributed, file, member), Is.EqualTo(expected));
        }

        [Test]
        public void AMovedMemberAndANewFileAreStillNamed()
        {
            Assert.That(EconomyFlowClassifier.Classify(EconomyFlowKind.Unattributed, "AutonomousWorldBotController.Stables.cs", "TryBeginFasterStableRoute"),
                Is.EqualTo(EconomyFlowKind.StableTicket), "by member");
            Assert.That(EconomyFlowClassifier.Classify(EconomyFlowKind.Unattributed, "BotPotions.cs", "Restock"),
                Is.EqualTo(EconomyFlowKind.Potion), "by file hint");
            Assert.That(EconomyFlowClassifier.Classify(EconomyFlowKind.Unattributed, "AutonomousWorldBotController.KillTasks.cs", "PayReward"),
                Is.EqualTo(EconomyFlowKind.KillTask), "True165's kill-task file");
        }

        [Test]
        public void AnUnknownCallerIsUnattributed_AndListedByName()
        {
            Assert.That(EconomyFlowClassifier.Classify(EconomyFlowKind.Unattributed, "Mystery.cs", "DoThing"), Is.EqualTo(EconomyFlowKind.Unattributed));
            Assert.That(EconomyFlowClassifier.Classify(EconomyFlowKind.Unattributed, "", ""), Is.EqualTo(EconomyFlowKind.Unattributed));
            Assert.That(EconomyFlowClassifier.Classify(EconomyFlowKind.Unattributed, null, null), Is.EqualTo(EconomyFlowKind.Unattributed));

            var ledger = new EconomyFlowLedger { BotInfo = _ => (2, 40) };
            ledger.Record(1, 70, EconomyFlowKind.Unattributed, @"D:\x\Mystery.cs", "DoThing");
            ledger.Record(1, -20, EconomyFlowKind.Unattributed, @"D:\x\Mystery.cs", "DoThing");
            EconomyFlowLedger.Window window = ledger.TakeWindow();
            Assert.That(window.Unattributed(5).Single(), Is.EqualTo(("Mystery.cs:DoThing", 70L, 20L, 2L)));
            var empty = new SupplyBuilder().Build(new DateTime(2026, 11, 15, 20, 0, 0, DateTimeKind.Utc));
            var hours = EconomyLedgerSnapshot.Compute(empty, empty, window, empty.TakenUtc);
            string all = EconomyLedgerSnapshot.FlowsLine(hours.Single(hour => hour.Realm == EconomyLedgerSnapshot.AllRealms), window);
            Assert.That(all, Does.Contain(" in_unattributed=70/1 out_unattributed=20/1"));
            Assert.That(all, Does.EndWith(" unattributed=\"Mystery.cs:DoThing in 70 out 20 x2\""));
        }

        [Test]
        public void TransfersAreTheMovesBetweenPurseAndTheRestOfTheSupply()
        {
            Assert.That(EconomyFlowClassifier.IsTransfer(EconomyFlowKind.BeggarGift), Is.True);
            Assert.That(EconomyFlowClassifier.IsTransfer(EconomyFlowKind.ExchangeSale), Is.True);
            Assert.That(EconomyFlowClassifier.IsTransfer(EconomyFlowKind.Guild), Is.True);
            foreach (EconomyFlowKind kind in new[] { EconomyFlowKind.LootCoin, EconomyFlowKind.VendorSale, EconomyFlowKind.VendorGear,
                         EconomyFlowKind.StableTicket, EconomyFlowKind.Siege, EconomyFlowKind.ExchangeTax, EconomyFlowKind.Unattributed })
                Assert.That(EconomyFlowClassifier.IsTransfer(kind), Is.False, kind.ToString());
            Assert.That(EconomyFlowClassifier.KeyName(EconomyFlowKind.LootCoin), Is.EqualTo("lootcoin"));
            Assert.That(EconomyFlowClassifier.KindCount, Is.EqualTo(Enum.GetValues<EconomyFlowKind>().Length));
        }

        // ---- the source scan ----

        private static readonly Regex Qualified = new(@"AutonomousBotEconomy\.(AddMoney|TrySpend)\(", RegexOptions.Compiled);
        private static readonly Regex Bare = new(@"(?<![\w.])(AddMoney|TrySpend)\(", RegexOptions.Compiled);
        private static readonly Regex Declaration = new(
            @"^\s*(?:(?:public|private|internal|protected|static|override|virtual|sealed|async|unsafe|new|extern|partial)\s+)+[\w<>\[\]?,. ()]*?\b(\w+)\s*(?:<[^>()]*>)?\s*\(",
            RegexOptions.Compiled);

        private static string GameServerRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null, "the repository root above the test directory");
            return Path.Combine(dir, "source", "server", "GameServer");
        }

        /// <summary>Every gamebot coin call in the server: file, line, enclosing member, and whether it names its kind.</summary>
        private static List<(string File, int Line, string Member, bool Explicit)> CallSites()
        {
            var sites = new List<(string, int, string, bool)>();
            foreach (string path in Directory.EnumerateFiles(GameServerRoot(), "*.cs", SearchOption.AllDirectories))
            {
                string file = Path.GetFileName(path);
                string[] lines = File.ReadAllLines(path);
                bool economy = file == "AutonomousBotEconomy.cs";
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    bool hit = Qualified.IsMatch(line) ||
                               economy && Bare.IsMatch(line) && !line.Contains("public static bool") && !line.TrimStart().StartsWith("///");
                    if (!hit || line.TrimStart().StartsWith("//"))
                        continue;
                    string member = null;
                    for (int j = i; j >= 0 && member == null; j--)
                    {
                        Match declaration = Declaration.Match(lines[j]);
                        if (declaration.Success && !lines[j].Split('(')[0].Contains('=') && !lines[j].TrimEnd().EndsWith(';'))
                            member = declaration.Groups[1].Value;
                    }
                    sites.Add((file, i + 1, member, line.Contains("EconomyFlowKind.")));
                }
            }
            return sites;
        }

        [Test]
        public void EveryCoinCallSiteInTheServerIsNamed()
        {
            var sites = CallSites();
            Assert.That(sites.Count, Is.GreaterThanOrEqualTo(25), "the scan finds today's ~25 call sites");
            var unnamed = sites.Where(site => !site.Explicit &&
                    EconomyFlowClassifier.Classify(EconomyFlowKind.Unattributed, site.File, site.Member) == EconomyFlowKind.Unattributed)
                .Select(site => $"{site.File}:{site.Line} in {site.Member ?? "?"}").ToArray();
            Assert.That(unnamed, Is.Empty,
                "pass an EconomyFlowKind at these calls or add them to EconomyFlowClassifier.KnownCallers");
        }

        [Test]
        public void EveryKnownCallerStillExists()
        {
            var sites = CallSites().Select(site => (site.File, site.Member)).ToHashSet();
            foreach (var known in EconomyFlowClassifier.KnownCallers.Keys)
                Assert.That(sites, Does.Contain(known), $"{known.File}:{known.Member} has no coin call any more; remove its row");
        }
    }
}
