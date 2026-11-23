using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// 1.2.3, from a r/daoc report: no endurance regeneration at all, sitting, with Tireless and a Minstrel song. Before 1.2.3 a
    /// ServerProperty value the server couldn't read left its field at the C# default, 0 for the regen multipliers, and
    /// EnduranceRegenerationAmountCalculator multiplies the whole tick (buffs and realm abilities included) by it.
    /// </summary>
    [TestFixture]
    public class UT_ServerPropertyValue123
    {
        [Test]
        public void ReadableValuesReadAsBefore()
        {
            Assert.Multiple(() =>
            {
                Assert.That(ServerPropertyValue.ReadOrDefault("1", 1.0, out bool a), Is.EqualTo(1.0)); Assert.That(a, Is.False);
                Assert.That(ServerPropertyValue.ReadOrDefault("1.5", 1.0, out _), Is.EqualTo(1.5));
                Assert.That(ServerPropertyValue.ReadOrDefault(" 2.25 ", 1.0, out _), Is.EqualTo(2.25));
                Assert.That(ServerPropertyValue.ReadOrDefault("0", 1.0, out bool zero), Is.EqualTo(0.0), "a real 0 is the player's choice");
                Assert.That(zero, Is.False);
                Assert.That(ServerPropertyValue.ReadOrDefault("1,000", 1.0, out _), Is.EqualTo(1000.0), "a thousands separator stays one");
                Assert.That(ServerPropertyValue.ReadOrDefault("300", 5, out _), Is.EqualTo(300));
                Assert.That(ServerPropertyValue.ReadOrDefault("True", false, out _), Is.EqualTo(true));
                Assert.That(ServerPropertyValue.ReadOrDefault("false", true, out _), Is.EqualTo(false));
                Assert.That(ServerPropertyValue.ReadOrDefault("", "x", out bool s), Is.EqualTo("")); Assert.That(s, Is.False);
                Assert.That(ServerPropertyValue.ReadOrDefault("1-19:3", "", out _), Is.EqualTo("1-19:3"));
            });
        }

        [Test]
        public void ACommaDecimalIsReadAsThatNumber()
        {
            // en-US alone read "1,0" as 10 and "0,5" as 5 (the comma as a thousands separator).
            Assert.Multiple(() =>
            {
                Assert.That(ServerPropertyValue.ReadOrDefault("1,0", 1.0, out bool fell), Is.EqualTo(1.0)); Assert.That(fell, Is.False);
                Assert.That(ServerPropertyValue.ReadOrDefault("0,5", 1.0, out _), Is.EqualTo(0.5));
                Assert.That(ServerPropertyValue.ReadOrDefault("1,25", 1.0, out _), Is.EqualTo(1.25));
                Assert.That(ServerPropertyValue.ReadOrDefault("-1,5", 1.0, out _), Is.EqualTo(-1.5));
                Assert.That(ServerPropertyValue.ReadOrDefault("1,5", 1, out bool intFell), Is.EqualTo(1), "only decimal settings read a comma decimal");
                Assert.That(intFell, Is.True);
            });
        }

        [Test]
        public void AnUnreadableValueUsesTheAttributesDefault()
        {
            Assert.Multiple(() =>
            {
                foreach (string bad in new[] { "", " ", "1.0x", "fast", "1.0,5" })
                {
                    Assert.That(ServerPropertyValue.ReadOrDefault(bad, 1.0, out bool fell), Is.EqualTo(1.0), bad);
                    Assert.That(fell, Is.True, bad);
                }
                Assert.That(ServerPropertyValue.ReadOrDefault(null, 1.5, out bool none), Is.EqualTo(1.5), "a NULL row");
                Assert.That(none, Is.True);
                Assert.That(ServerPropertyValue.ReadOrDefault("maybe", true, out _), Is.EqualTo(true));
                Assert.That(ServerPropertyValue.ReadOrDefault("lots", 30, out _), Is.EqualTo(30));
            });
        }

        [Test]
        public void TheReadingDoesNotDependOnTheMachinesNumberFormat()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(ServerPropertyValue.ReadOrDefault("1.5", 1.0, out _), Is.EqualTo(1.5));
                Assert.That(ServerPropertyValue.ReadOrDefault("1,5", 1.0, out _), Is.EqualTo(1.5));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Test]
        public void TheRegenMultipliersStartAtTheirDefaultsAndLoadFallsBack()
        {
            foreach (string name in new[] { "HEALTH_REGEN_AMOUNT_MODIFIER", "ENDURANCE_REGEN_AMOUNT_MODIFIER", "MANA_REGEN_AMOUNT_MODIFIER" })
            {
                FieldInfo field = typeof(Properties).GetField(name, BindingFlags.Public | BindingFlags.Static)!;
                var attribute = (ServerPropertyAttribute)field.GetCustomAttribute(typeof(ServerPropertyAttribute))!;
                Assert.That(attribute.DefaultValue, Is.GreaterThan(0.0), name);
            }

            string root = TestContext.CurrentContext.TestDirectory;
            const string Source = "source/server/GameServer/serverproperty/ServerProperties.cs";
            for (int i = 0; i < 8 && root != null && !File.Exists(Path.Combine(root, Source)); i++)
                root = Path.GetDirectoryName(root);
            if (root == null || !File.Exists(Path.Combine(root, Source)))
                Assert.Ignore("Repository not found beside the tests.");
            string text = File.ReadAllText(Path.Combine(root, Source));
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("public static double HEALTH_REGEN_AMOUNT_MODIFIER = 1.0;"));
                Assert.That(text, Does.Contain("public static double ENDURANCE_REGEN_AMOUNT_MODIFIER = 1.0;"));
                Assert.That(text, Does.Contain("public static double MANA_REGEN_AMOUNT_MODIFIER = 1.5;"));
                Assert.That(text, Does.Contain("ServerPropertyValue.ReadOrDefault(prop.Value, attrib.DefaultValue, out bool fellBack)"));
                Assert.That(text, Does.Contain("SERVER_PROPERTY_UNREADABLE"));
                Assert.That(text, Does.Not.Contain("field.SetValue(null, Convert.ChangeType(prop.Value"), "the old read that left 0 behind");
            });
        }
    }
}
