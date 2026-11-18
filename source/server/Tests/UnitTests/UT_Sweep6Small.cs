using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Two small fixes from the owner's PC log of build b1b6846 (2026-11-18, sweep 6): the group read once in
    /// BotBrain.TryResurrectGroupMember, and one rule for a Savage's weapon upgrade on every buy path. Tests only: not yet
    /// seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public sealed class UT_Sweep6Small
    {
        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        private static string Method(string source, string signature)
        {
            int start = source.IndexOf(signature, System.StringComparison.Ordinal);
            Assume.That(start, Is.GreaterThanOrEqualTo(0), signature);
            int end = source.IndexOf("\n        #endregion", start, System.StringComparison.Ordinal);
            int next = source.IndexOf("\n        private ", start + signature.Length, System.StringComparison.Ordinal);
            if (next > 0 && (end < 0 || next < end)) end = next;
            return source[start..end];
        }

        [Test]
        public void TheResurrectionReadsTheBotsGroupOnce()
        {
            // 07:08:17, two NullReferenceExceptions at BotBrain.cs line 6160 (Thanturbuffs, Grimizrad): Body.Group was checked,
            // then read again for its members after the group was disbanded. GameLiving.Group is not virtual, so no test double
            // can disband it between two reads; the method is read instead.
            string method = Method(Source("bots", "BotBrain.cs"), "private bool TryResurrectGroupMember()");
            Assert.That(Regex.Matches(method, @"Body\.Group\b").Count, Is.EqualTo(1), method);
            Assert.That(method, Does.Contain("Group group = Body.Group;"));
            Assert.That(method, Does.Contain("group == null ||"), "the later check is on the same read");
        }

        [Test]
        public void ASavageBuysOnlyAWeaponTheUndressGuardWouldLetItWear()
        {
            // 06:54:51: Fre (a Savage) bought a "Lifetaker Greatsword" for 39,863 copper; its weapon policy rated it above the
            // worn "Sturdy Greatsword", the set score fell 14,934 -> 14,923, and the guard refused it (ECON_UNDRESS_GUARD
            // event="blocked"). The Exchange's buy side asked the weapon policy only.
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotEconomy.SavageWeaponUpgrade(weaponGain: 0.4, gearValuation: true, scoredSlot: true, setGain: -11),
                    Is.False, "Fre's buy");
                Assert.That(AutonomousBotEconomy.SavageWeaponUpgrade(0.4, true, true, 0), Is.True, "the guard lets an equal score through");
                Assert.That(AutonomousBotEconomy.SavageWeaponUpgrade(0.4, true, true, 25), Is.True);
                Assert.That(AutonomousBotEconomy.SavageWeaponUpgrade(0.4, false, true, -11), Is.True, "bot_crafters off: the weapon policy, as before");
                Assert.That(AutonomousBotEconomy.SavageWeaponUpgrade(0, true, true, 50), Is.False, "never a weapon its policy does not want");
            });
            string economy = Source("bots", "autonomous", "AutonomousBotEconomy.cs");
            // Both the Exchange's buy side (ScorePurchase) and the loot and merchant side (TryGetEquipmentUpgrade) ask it.
            Assert.That(Regex.Matches(economy, @"SavageWeaponUpgrade\(").Count, Is.GreaterThanOrEqualTo(3), "the rule and its two callers");
            int from = economy.IndexOf("private static PurchaseCandidate ScorePurchase(", System.StringComparison.Ordinal);
            int to = economy.IndexOf("public static int EquipmentValue(DbInventoryItem item)", System.StringComparison.Ordinal);
            Assume.That(from, Is.GreaterThan(0).And.LessThan(to));
            string score = economy[from..to];
            Assert.That(score, Does.Contain("SavageWeaponUpgrade(gain, gearValuation,"));
            Assert.That(score, Does.Not.Contain("if (gain > 0)\n"), "no buy on the weapon policy alone");
        }
    }
}
