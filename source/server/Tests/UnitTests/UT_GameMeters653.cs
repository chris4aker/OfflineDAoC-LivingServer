using System;
using System.IO;
using System.Linq;
using DOL.GS.Metrics.Meters;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Issue #653 (owner, 2026-11-26), Grafana Cloud kit 5: the bots-online gauge daoc.online.bot.count by type and
    /// realm (never counting real players), and the metrics interval read from both serverconfig.xml keys. Tests only.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_GameMeters653
    {
        [Test]
        public void TheGaugeCountsByTypeAndRealm()
        {
            var rows = BotMeterProvider.Count(new[]
            {
                (BotMeterProvider.Gamebot, eRealm.Albion),
                (BotMeterProvider.Gamebot, eRealm.Albion),
                (BotMeterProvider.Gamebot, eRealm.Hibernia),
                (BotMeterProvider.Companion, eRealm.Midgard),
                (BotMeterProvider.Companion, eRealm.Albion),
            });

            int Of(string type, string realm) => rows.Single(row => row.Type == type && row.Realm == realm).Count;
            Assert.That(Of("gamebot", "Albion"), Is.EqualTo(2));
            Assert.That(Of("gamebot", "Midgard"), Is.EqualTo(0));
            Assert.That(Of("gamebot", "Hibernia"), Is.EqualTo(1));
            Assert.That(Of("companion", "Albion"), Is.EqualTo(1));
            Assert.That(Of("companion", "Midgard"), Is.EqualTo(1));
            Assert.That(Of("companion", "Hibernia"), Is.EqualTo(0));
            Assert.That(rows.Count, Is.EqualTo(6), "six steady series; no 'other' realm without a realmless bot");
        }

        [Test]
        public void AnEmptyWorldStillReportsTheSixSeriesAtZero()
        {
            var rows = BotMeterProvider.Count(null);
            Assert.That(rows.Count, Is.EqualTo(6));
            Assert.That(rows.All(row => row.Count == 0));
            Assert.That(rows.Select(row => row.Type).Distinct(), Is.EquivalentTo(new[] { "gamebot", "companion" }));
            Assert.That(rows.Select(row => row.Realm).Distinct(), Is.EquivalentTo(new[] { "Albion", "Midgard", "Hibernia" }));
        }

        [Test]
        public void ABotWithNoRealmIsCountedAsOther_AndUnknownTypesNever()
        {
            var rows = BotMeterProvider.Count(new[]
            {
                (BotMeterProvider.Gamebot, eRealm.None),
                ("player", eRealm.Albion),
                ((string)null, eRealm.Midgard),
            });
            Assert.That(rows.Single(row => row.Type == "gamebot" && row.Realm == "other").Count, Is.EqualTo(1));
            Assert.That(rows.Where(row => row.Type != "gamebot").Sum(row => row.Count), Is.EqualTo(0));
            Assert.That(rows.Any(row => row.Type == "companion" && row.Realm == "other"), Is.False);
        }

        [TestCase(true, false, "gamebot")]   // autonomous world bot, Epic Special Forces, No Lifer buffbot
        [TestCase(true, true, "companion")]  // a player's hired companion
        [TestCase(false, false, "companion")] // /bot companion, /spawn and /raid helpers
        public void TheTypeTellsBotKindsApart(bool autonomous, bool hired, string expected)
        {
            Assert.That(BotMeterProvider.TypeOf(autonomous, hired), Is.EqualTo(expected));
        }

        [Test]
        public void RealPlayersAreNeverCounted()
        {
            // GameBot is a GameNPC, never a GamePlayer, so the sources the gauge reads hold only bots.
            Assert.That(typeof(GamePlayer).IsAssignableFrom(typeof(GameBot)), Is.False);
            Assert.That(typeof(BotMeterProvider).GetMethod("BotsInWorld",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.ReturnType,
                Is.EqualTo(typeof(System.Collections.Generic.IEnumerable<GameBot>)));
            Assert.That(BotMeterProvider.GaugeName, Is.EqualTo("daoc.online.bot.count"));
        }

        [TestCase("30s", null, 30_000)]          // the shipped key
        [TestCase(null, "2m", 120_000)]          // the key the code read before
        [TestCase("15s", "5m", 15_000)]          // both: the shipped key wins
        [TestCase(" 1h ", null, 3_600_000)]
        [TestCase(null, null, 60_000)]           // neither: 60 s
        [TestCase("", "", 60_000)]
        [TestCase("soon", null, 60_000)]         // unreadable: 60 s, the start goes on
        [TestCase("0s", null, 60_000)]           // under a second: 60 s
        public void TheIntervalReadsBothKeys(string metricsInterval, string metricsExportInterval, int expected)
        {
            Assert.That(GameServerConfiguration.MetricsIntervalMilliseconds(metricsInterval, metricsExportInterval),
                Is.EqualTo(expected));
        }

        [TestCase("<MetricsInterval>20s</MetricsInterval>", 20_000)]
        [TestCase("<MetricsExportInterval>45s</MetricsExportInterval>", 45_000)]
        [TestCase("<MetricsInterval>10s</MetricsInterval><MetricsExportInterval>45s</MetricsExportInterval>", 10_000)]
        [TestCase("", 60_000)]
        public void AServerConfigFileWithEitherKeyIsRead_AndMetricsStayOff(string keys, int expected)
        {
            string folder = Path.Combine(Path.GetTempPath(), "ls-meters-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var file = new FileInfo(Path.Combine(folder, "serverconfig.xml"));
                File.WriteAllText(file.FullName, $"<?xml version=\"1.0\" encoding=\"utf-8\"?><root><Server>{keys}</Server></root>");
                var config = new GameServerConfiguration();
                config.LoadFromXMLFile(file);
                Assert.That(config.MetricsExportInterval, Is.EqualTo(expected));
                Assert.That(config.MetricsEnabled, Is.False, "MetricsEnabled stays off by default (diagnostics)");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void TheShippedExampleConfigKeepsMetricsOffWithTheShippedKey()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            string path = null;
            for (var d = new DirectoryInfo(dir); d != null && path == null; d = d.Parent)
            {
                string candidate = Path.Combine(d.FullName, "CoreServer", "config", "serverconfig.example.xml");
                if (File.Exists(candidate))
                    path = candidate;
            }
            Assume.That(path, Is.Not.Null, "serverconfig.example.xml not found from the test folder");
            string text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("<MetricsEnabled>false</MetricsEnabled>"));
            Assert.That(text, Does.Contain("<MetricsInterval>"));
        }
    }
}
