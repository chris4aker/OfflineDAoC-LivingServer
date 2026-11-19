using System.Linq;
using NUnit.Framework;
using OfflineDaoc.Shared;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Owner rule 2026-11-17: bots never play the custom class. A gamebot that already is a Sluaghbinder is kept (record,
    /// items, IsRetired untouched) but left out of the roster the population logs in from.
    /// </summary>
    [TestFixture]
    public class UT_CustomClassBotRetirement
    {
        private static OfflineWorldBotRecord Bot(long id, int classId, string name = "Bot", bool retired = false) =>
            new() { BotId = id, ClassId = classId, Name = name, Level = 50, Realm = 3, IsRetired = retired, Activity = "Questing" };

        [Test]
        public void TheLoginRosterLeavesOutOnlyTheCustomClass()
        {
            OfflineWorldBotRecord[] roster = [Bot(1, 47), Bot(2, 63, "Sluaggy"), Bot(3, 48)];
            Assert.That(CustomClassBotRetirement.LoginRoster(roster).Select(r => r.BotId), Is.EqualTo(new long[] { 1, 3 }));
            Assert.That(roster, Has.Length.EqualTo(3), "nothing is removed from the roster itself");
        }

        [Test]
        public void ARosterWithoutTheClassIsUsedAsItIs()
        {
            OfflineWorldBotRecord[] roster = [Bot(1, 47), Bot(3, 48)];
            Assert.That(CustomClassBotRetirement.LoginRoster(roster), Is.SameAs(roster));
            Assert.That(CustomClassBotRetirement.LoginRoster(null), Is.Empty);
        }

        [Test]
        public void KeptBotsAreLoggedOnceAndMarkedNeverDeleted()
        {
            OfflineWorldBotRecord sluagh = Bot(2, 63, "Sluaggy");
            OfflineWorldBotRecord[] kept = CustomClassBotRetirement.Kept([Bot(1, 47), sluagh, Bot(4, 63, "Gone", retired: true)]);
            Assert.That(kept, Is.EqualTo(new[] { sluagh }), "a bot the launcher already asked to delete is not ours to mark");
            Assert.That(CustomClassBotRetirement.LogLine(sluagh), Does.StartWith(
                "BOT_CUSTOM_CLASS_RETIRED bot=\"Sluaggy\" id=2 class=Sluaghbinder level=50 realm=Hibernia: kept with its items"));
            Assert.That(CustomClassBotRetirement.MarkActivity(sluagh), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(sluagh.Activity, Is.EqualTo(BotCustomClassRule.RetiredActivity));
                Assert.That(sluagh.IsRetired, Is.False, "IsRetired is the launcher's delete request: never set");
                Assert.That(sluagh.IsOnline, Is.False);
            });
            Assert.That(CustomClassBotRetirement.MarkActivity(sluagh), Is.False, "the next start finds nothing to save");
        }
    }
}
