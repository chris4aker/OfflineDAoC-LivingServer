using System;
using System.IO;
using NLog;
using NLog.Config;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The shipped logconfig.xml (audit 2026-11-13): routine bot travel and goal lines go to detail.log only, everything
    /// else still reaches server.log, and every file has a retention limit.
    /// </summary>
    [TestFixture]
    public class UT_LogConfig
    {
        private static string ConfigPath =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "GameServer", "config", "logconfig.xml");

        [Test]
        public void RoutineBotLinesGoToTheDetailLogOnly()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ls-logconfig-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string xml = File.ReadAllText(ConfigPath).Replace("value=\"./logs\"", $"value=\"{folder.Replace('\\', '/')}\"");
                using var factory = new LogFactory();
                factory.Configuration = XmlLoggingConfiguration.CreateFromXmlString(xml, factory);
                Logger raid = factory.GetLogger("DOL.GS.AutonomousRealmRaid");
                Logger goals = factory.GetLogger("DOL.GS.AutonomousGoalDiagnostics");

                goals.Info("AUTONOMOUS_GOAL_ATTEMPT bot=\"Probe\" goal=camp");
                raid.Info("REALM_RAID_ENDED event=probe outcome=\"Boss defeated\"");
                goals.Warn("AUTONOMOUS_GOAL_ATTEMPT a warning still reaches warn.log");
                factory.Flush();
                factory.Shutdown();

                string server = File.ReadAllText(Path.Combine(folder, "server.log"));
                string detail = File.ReadAllText(Path.Combine(folder, "detail.log"));
                Assert.That(server, Does.Contain("REALM_RAID_ENDED"));
                Assert.That(server, Does.Not.Contain("goal=camp"), "routine lines stay out of server.log");
                Assert.That(detail, Does.Contain("goal=camp"));
                Assert.That(detail, Does.Not.Contain("REALM_RAID_ENDED"));
                Assert.That(File.ReadAllText(Path.Combine(folder, "warn.log")), Does.Contain("a warning still reaches warn.log"));
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (IOException) { }
            }
        }

        [Test]
        public void AnOlderCopyIsReplacedOnceAndKeptAsABackup()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ls-logconfig-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var file = new FileInfo(Path.Combine(folder, "logconfig.xml"));
                File.WriteAllText(file.FullName, "<nlog><!-- an old copy --></nlog>");

                Assert.That(DOL.GS.GameServer.UpgradeLogConfig(file), Is.True);
                Assert.That(File.ReadAllText(file.FullName), Does.Contain(DOL.GS.GameServer.LogConfigMarker));
                Assert.That(File.ReadAllText(file.FullName + ".before-2"), Does.Contain("an old copy"));
                Assert.That(DOL.GS.GameServer.UpgradeLogConfig(file), Is.False, "the current copy is left alone");
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (IOException) { }
            }
        }

        [Test]
        public void EveryLogFileHasARetentionLimit()
        {
            string xml = File.ReadAllText(ConfigPath);
            int files = xml.Split("xsi:type=\"File\"").Length - 1;
            int limited = xml.Split("maxArchiveDays=").Length - 1;
            Assert.That(files, Is.GreaterThanOrEqualTo(8));
            Assert.That(limited, Is.EqualTo(files));
            Assert.That(xml, Does.Contain(DOL.GS.GameServer.LogConfigMarker), "the marker the server upgrades older copies by");
        }
    }
}
