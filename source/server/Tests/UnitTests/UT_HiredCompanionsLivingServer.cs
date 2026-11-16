using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Hired companions (ported from shadowofze's True 1.65 v0.7, f68cc75, via True165, GPL-3.0) as Living Server runs
    /// them: the switch, the level cap, names, the AI Options words for the chat list, the cast filter's gate, the 'H'
    /// protocol's opt-in, and the economy ledger's player-side sink for the hire cost. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_HiredCompanionsLivingServer
    {
        [TearDown]
        public void Reset()
        {
            HiredCompanionsMode.Override = null;
            CompanionNames165.LookupForTests = null;
        }

        [Test]
        public void TheSwitchDefaultsOn()
        {
            Assert.That(HiredCompanionsMode.HIRED_COMPANIONS, Is.True, "owner 2026-11-16: Living Server features default on");
            HiredCompanionsMode.Override = false;
            Assert.That(HiredCompanionsMode.IsOn, Is.False);
        }

        [Test]
        public void ACompanionNeverPassesItsPlayersLevel() => Assert.Multiple(() =>
        {
            long full = GamePlayer.GetExperienceAmountForLevel(10) - 1;
            Assert.That(HiredCompanions165.CappedExperience(500, 0, 9, 10), Is.EqualTo(500), "below the cap: all of it");
            Assert.That(HiredCompanions165.CappedExperience(long.MaxValue / 4, full - 10, 10, 10), Is.EqualTo(10), "at the cap: fills the bar");
            Assert.That(HiredCompanions165.CappedExperience(500, full, 10, 10), Is.Zero, "a full bar waits");
            Assert.That(HiredCompanions165.CappedExperience(500, 0, 11, 10), Is.Zero, "its player lost a level");
            Assert.That(HiredCompanions165.CappedExperience(500, 0, 30, null), Is.EqualTo(500), "player not known: no cap");
        });

        [Test]
        public void SlotsAndCostsAreTrue165s() => Assert.Multiple(() =>
        {
            Assert.That(Hire165.SlotLevels, Is.EqualTo(new byte[] { 5, 10, 15, 20, 30, 40, 45 }));
            Assert.That(HiredCompanions165.OpenSlots(4), Is.Zero);
            Assert.That(HiredCompanions165.OpenSlots(5), Is.EqualTo(1));
            Assert.That(HiredCompanions165.OpenSlots(50), Is.EqualTo(7));
            Assert.That(HiredCompanions165.NextSlotLevel(12), Is.EqualTo(15));
            Assert.That(HiredCompanions165.CostCopper[0], Is.Zero, "the first is free");
            Assert.That(HiredCompanions165.CostCopper.Skip(1), Is.All.GreaterThan(0));
        });

        [Test]
        public void ANameIsClaimedOnceAndReleased()
        {
            var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Osbert" };
            CompanionNames165.LookupForTests = name => taken.Contains(name);
            Assert.That(CompanionNames165.TryClaim("osbert"), Is.False, "a player or bot wears it");
            Assert.That(CompanionNames165.TryClaim("Isolde"), Is.True);
            Assert.That(CompanionNames165.TryClaim("ISOLDE"), Is.False, "claimed by a hire in progress");
            CompanionNames165.Release("Isolde");
            Assert.That(CompanionNames165.IsTaken("Isolde"), Is.False);
            Assert.That(Hire165.NameProblem("Al"), Does.Contain("3 to 20"));
            Assert.That(Hire165.NameProblem("Two Words"), Does.Contain("one word"));
        }

        [Test]
        public void TheChatListPressesTheWindowsOwnButtons() => Assert.Multiple(() =>
        {
            var charm = new CompanionOptions165.OptionRow(CompanionOptions165.KindCharm, 0, "Charm", ["Off", "On"], 1);
            Assert.That(CompanionOptions165.CommandFor(charm, 0), Is.EqualTo(new[] { "charm", "off" }));
            var stance = new CompanionOptions165.OptionRow(CompanionOptions165.KindStance, 0, "Stance", ["a", "b", "c"], 0);
            Assert.That(CompanionOptions165.CommandFor(stance, 2), Is.EqualTo(new[] { "option", "41", "0", "2" }));
            var debuff = new CompanionOptions165.OptionRow(CompanionOptions165.KindDebuffLine, 3, "Vortex", ["Automatic", "Use", "Skip"], 0);
            Assert.That(CompanionOptions165.CommandFor(debuff, 2), Is.EqualTo(new[] { "option", "28", "3", "2" }));
        });

        [Test]
        public void EveryOptionReaderIsAutomaticForOtherBots() => Assert.Multiple(() =>
        {
            Assert.That(CompanionOptions165.Choice(null, "stance"), Is.Zero);
            Assert.That(CompanionSpellOptions165.Blocks(null, null), Is.False);
            Assert.That(CompanionOptions165.CharmAllowed(null), Is.True, "a gamebot charms as before");
            Assert.That(CompanionControls165.CrowdControl(null), Is.EqualTo(CompanionControls165.CcAutomatic));
        });

        [Test]
        public void NoClientGetsHirePacketsUnlessItSaysSo()
        {
            Assert.That(Hire165.ReadsProtocol(null), Is.False);
            Assert.DoesNotThrow(() => Hire165.Send(null, 4, [1, 2, 3]), "no client: nothing sent");
        }

        [Test]
        public void TheHireCostIsAPlayerSideSink()
        {
            var flows = new EconomyFlowLedger { BotInfo = _ => (1, 20) };
            flows.RecordPlayer(1, -20_000, EconomyFlowKind.CompanionHire);
            flows.RecordPlayer(1, -300, EconomyFlowKind.Repair);
            var before = new SupplyBuilder().Wallet(1, 50_000).Build(new System.DateTime(2026, 11, 16, 20, 0, 0, System.DateTimeKind.Utc));
            var after = new SupplyBuilder().Wallet(1, 29_700).Build(new System.DateTime(2026, 11, 16, 21, 0, 0, System.DateTimeKind.Utc));
            EconomyLedgerSnapshot.RealmHour albion = EconomyLedgerSnapshot.Compute(before, after, flows.TakeWindow(), after.TakenUtc)
                .Single(hour => hour.Realm == 1);
            Assert.Multiple(() =>
            {
                Assert.That(albion.DeltaSupply, Is.EqualTo(-20_300));
                Assert.That(albion.Flows, Is.EqualTo(-20_300), "both named sinks are flows");
                Assert.That(albion.Residual, Is.Zero, "the ledger saw every copper the player paid");
                Assert.That(albion.SinkKinds["companionhire"], Is.EqualTo(20_000));
                Assert.That(albion.SinkKinds["repair"], Is.EqualTo(300));
                Assert.That(albion.BotFlows, Is.Zero, "no bot purse moved");
                Assert.That(albion.BotResidual, Is.Zero);
            });
            Assert.That(EconomyFlowClassifier.IsTransfer(EconomyFlowKind.CompanionHire), Is.False);
            Assert.That(EconomyFlowClassifier.KeyName(EconomyFlowKind.CompanionHire), Is.EqualTo("companionhire"));
        }

        [Test]
        public void TheReleaseBuildEmptiesTheHireTable()
        {
            string root = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && root != null && !System.IO.File.Exists(System.IO.Path.Combine(root, "source/tools/OfflineDaoc.ProgressImport/progress-policy.json")); i++)
                root = System.IO.Path.GetDirectoryName(root);
            if (root == null || !System.IO.File.Exists(System.IO.Path.Combine(root, "source/tools/OfflineDaoc.ProgressImport/progress-policy.json")))
                Assert.Ignore("Repository not found beside the tests.");
            string policy = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "source/tools/OfflineDaoc.ProgressImport/progress-policy.json"));
            Assert.That(policy, Does.Contain("\"offline_hired_companions\""));
            string passport = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "source/tools/OfflineDaoc.Passport/PassportEngine.cs"));
            Assert.That(passport, Does.Contain("[\"offline_hired_companions\"] = new(PassportDecision.StaysInWorld"));
        }
    }
}
