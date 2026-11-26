using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using Upgrade = DOL.GS.ServerProperties.LivingServerSettingsUpgrade;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Issue #641 (owner, 2026-11-25): the product is "DAoC - Living Server". The welcome messages and the server's default
    /// name say so; a world that kept the old untouched welcome text moves once, and one whose text was edited keeps it.
    /// Tests only.
    /// </summary>
    [TestFixture]
    public class UT_Rebrand641
    {
        private static string Default(string field) =>
            (string)typeof(Properties).GetField(field)!.GetCustomAttribute<ServerPropertyAttribute>()!.DefaultValue;

        [TestCase("MOTD", "motd")]
        [TestCase("STARTING_MSG", "starting_msg")]
        public void TheWelcomeMessageNamesTheProductAndUntouchedOldTextMovesOnce(string field, string key)
        {
            string now = Default(field);
            Assert.That(now, Does.Contain("DAoC - Living Server"));
            Assert.That(now, Does.Not.Contain("Offline DAoC"));

            Upgrade.Change change = Upgrade.Changes.Single(c => c.Key == key);
            Assert.That(change.Since, Is.EqualTo("1.2.3"));
            Assert.That(change.NewDefault, Is.EqualTo(now), "the upgrade ends on the code default");
            Assert.That(change.OldDefault, Does.Contain("Offline DAoC"));

            var untouched = new Dictionary<string, (string, string)> { [key] = (change.OldDefault, change.OldDefault) };
            Assert.That(Upgrade.PlanChanges("1.2.2", untouched).Select(c => c.NewDefault), Is.EqualTo(new[] { now }));
            var edited = new Dictionary<string, (string, string)> { [key] = ("Welcome to my own world.", change.OldDefault) };
            Assert.That(Upgrade.PlanChanges("1.2.2", edited), Is.Empty, "a player's own text stays");
            Assert.That(Upgrade.PlanChanges("1.2.3", untouched), Is.Empty, "it runs once");
        }

        [Test]
        public void TheServerIsNamedForTheProductByDefault()
        {
            Assert.That(new GameServerConfiguration().ServerName, Is.EqualTo("DAoC - Living Server"));
        }
    }
}
