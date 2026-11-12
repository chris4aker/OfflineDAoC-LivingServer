using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Policy = DOL.GS.RealmRaidRecruitmentPolicy;
using Tales = DOL.GS.AutonomousTales;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The tale, then the call (Living Server 1.2, the owner's idea, 2026-11-12; AutonomousRealmRaid.Call.cs): a raid's tale
    /// is told before the raid forms, the realm is called when it ends, and a tale that cannot start or runs long never costs
    /// the raid. These tests cover the pure rules and the written tales. Nothing here is a played check.
    /// </summary>
    [TestFixture]
    public class UT_RaidCall
    {
        private static readonly (string Id, eRealm Realm)[] Raids =
        [
            ("epic-albion", eRealm.Albion), ("epic-midgard", eRealm.Midgard), ("epic-hibernia", eRealm.Hibernia),
            ("dragon-albion", eRealm.Albion), ("dragon-midgard", eRealm.Midgard), ("dragon-hibernia", eRealm.Hibernia),
            ("summoners-albion", eRealm.Albion), ("summoners-midgard", eRealm.Midgard), ("summoners-hibernia", eRealm.Hibernia),
            ("darkness-albion", eRealm.Albion), ("darkness-midgard", eRealm.Midgard), ("darkness-hibernia", eRealm.Hibernia),
            ("dungeon-albion-avalon", eRealm.Albion), ("dungeon-albion-corrupt", eRealm.Albion), ("dungeon-albion-crystal", eRealm.Albion),
            ("dungeon-albion-krondon", eRealm.Albion), ("dungeon-midgard-dodens", eRealm.Midgard), ("dungeon-midgard-iarnvidiur", eRealm.Midgard),
            ("dungeon-midgard-trollheim", eRealm.Midgard), ("dungeon-hibernia-marfach", eRealm.Hibernia), ("dungeon-hibernia-tursuil", eRealm.Hibernia),
        ];

        private static VC Load()
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

        [Test]
        public void ARaidWaitsForItsTaleButNeverForLong()
        {
            const long asked = 1_000_000;
            Assert.Multiple(() =>
            {
                // No tale yet: the raid waits up to the start patience, then is called without it.
                Assert.That(Policy.CallStep(asked, asked, false, 0), Is.EqualTo(Policy.RaidCallStep.Wait));
                Assert.That(Policy.CallStep(asked + Policy.TaleStartPatienceMs - 1, asked, false, 0), Is.EqualTo(Policy.RaidCallStep.Wait));
                Assert.That(Policy.CallStep(asked + Policy.TaleStartPatienceMs, asked, false, 0), Is.EqualTo(Policy.RaidCallStep.CallWithoutTale));
                // The tale runs: the raid waits for its end (the tales call it), but not past the longest tale.
                long started = asked + 90_000;
                Assert.That(Policy.CallStep(asked + Policy.TaleStartPatienceMs + 1, asked, true, started), Is.EqualTo(Policy.RaidCallStep.Wait),
                    "a running tale is not given up because it started late");
                Assert.That(Policy.CallStep(started + Policy.TaleMaxMs - 1, asked, true, started), Is.EqualTo(Policy.RaidCallStep.Wait));
                Assert.That(Policy.CallStep(started + Policy.TaleMaxMs, asked, true, started), Is.EqualTo(Policy.RaidCallStep.CallTaleRanLong));
                // The whole wait is bounded: a raid is called at most patience + longest tale after it came due.
                Assert.That(Policy.TaleStartPatienceMs + Policy.TaleMaxMs, Is.LessThanOrEqualTo(20 * 60_000L));
            });
        }

        [Test]
        public void TheCallNamesTheRealmTheRaidAndWhereToGather()
        {
            string line = Policy.CallLine("Hibernia", "Galladoria", "Dalniver's service settlement");
            Assert.That(line, Does.Contain("Hibernia").And.Contain("Galladoria").And.Contain("Dalniver's service settlement"));
            Assert.That(line, Does.StartWith("The horn sounds"));
            Assert.That(Policy.CallHornSound, Is.GreaterThan(0), "the call has a horn by default");
            Assert.That(Policy.TaleFirst, Is.True, "the tale comes first by default");
        }

        [Test]
        public void RaidsComeEveryFourHoursAndAnUntouchedWorldMovesToIt()
        {
            Assert.That(Policy.ScheduleMinutes, Is.EqualTo(240));
            var untouched = new Dictionary<string, (string Value, string DefaultValue)> { ["realm_raid_schedule_minutes"] = ("150", "150") };
            var planned = DOL.GS.ServerProperties.LivingServerSettingsUpgrade.PlanChanges("1.1.2.1", untouched);
            Assert.That(planned.Select(c => (c.Key, c.NewDefault)), Is.EqualTo(new[] { ("realm_raid_schedule_minutes", "240") }));
            Assert.That(DOL.GS.ServerProperties.LivingServerSettingsUpgrade.Plan("1.2", untouched), Is.Empty, "once only");
            var chosen = new Dictionary<string, (string Value, string DefaultValue)> { ["realm_raid_schedule_minutes"] = ("90", "150") };
            Assert.That(DOL.GS.ServerProperties.LivingServerSettingsUpgrade.Plan("1.1.2.1", chosen), Is.Empty, "the player's own interval is kept");
        }

        [Test]
        public void EveryRaidHasATaleOfItsOwnInItsRealm()
        {
            VC corpus = Load();
            VC.Scene[] tales = corpus.Scenes.Where(Tales.IsTale).ToArray();
            Assert.Multiple(() =>
            {
                foreach ((string id, eRealm realm) in Raids)
                {
                    string[] lores = Tales.LoreIdsForRaid(id, realm);
                    Assert.That(lores, Is.Not.Empty, $"{id}: no boss's tale leads to this raid");
                    Assert.That(tales.Any(t => VC.RealmFits(t.Realm, realm) && lores.Contains(Tales.LoreIdOf(t.Topic))), Is.True,
                        $"{id}: {GlobalConstants.RealmToName(realm)} has no tale of {string.Join(" or ", lores)} to tell before the raid");
                }
            });
            Assert.That(Tales.LoreIdsForRaid("dungeon-hibernia-marfach", eRealm.Hibernia),
                Is.EquivalentTo(new[] { "avartack-the-champion", "black-lady", "chieftain-caimheul" }), "Marfach has three bosses with a tale");
            Assert.That(Tales.LoreIdsForRaid("no-such-raid", eRealm.Albion), Is.Empty);
            Assert.That(Tales.LoreIdsForRaid(null, eRealm.Albion), Is.Empty);
        }
    }
}
