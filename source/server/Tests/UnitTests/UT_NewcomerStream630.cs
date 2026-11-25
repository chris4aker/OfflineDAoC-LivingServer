using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using OfflineDaoc.Shared;
using Stream = DOL.GS.AutonomousNewcomerStream;
using Upgrade = DOL.GS.ServerProperties.LivingServerSettingsUpgrade;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Issue #630, the newcomer stream: below a realm's floor of gamebots in levels 1-19, new level-1 gamebots join at a capped
    /// rate, each paired with a level-50 retirement so the realm's roster stays the same size. Never class 63, no armour,
    /// nothing deleted, every exclusion holds, and the rows carry no names. Autonomous gamebots only. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_NewcomerStream630
    {
        private static readonly Stream.Settings On = new(true, 50, 6, true);

        // ---- the floor and the rate --------------------------------------------------------------------------------

        [Test]
        public void TheFloorAndTheRateScaleWithTheRealmsRoster()
        {
            Assert.That(Stream.Floor(50, 1000), Is.EqualTo(50));
            Assert.That(Stream.Floor(50, 100), Is.EqualTo(5));
            Assert.That(Stream.Floor(50, 3000), Is.EqualTo(150));
            Assert.That(Stream.Floor(50, 5), Is.EqualTo(1), "at least one");
            Assert.That(Stream.Floor(0, 1000), Is.EqualTo(0), "0 is off");
            Assert.That(Stream.Floor(50, 0), Is.EqualTo(0), "no roster, no floor");
            Assert.That(Stream.PerHour(6, 1000), Is.EqualTo(6));
            Assert.That(Stream.PerHour(6, 100), Is.EqualTo(1), "at least one an hour");
            Assert.That(Stream.PerHour(6, 3000), Is.EqualTo(18));
            Assert.That(Stream.Spacing(6), Is.EqualTo(TimeSpan.FromMinutes(10)));
        }

        [Test]
        public void ARealmTakesANewcomerOnlyBelowItsFloorAndAfterTheSpacing()
        {
            DateTime now = new(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc);
            var low = new Stream.RealmCount(eRealm.Albion, 1000, 49);
            Assert.That(Stream.Due(low, On, null, now), Is.True);
            Assert.That(Stream.Due(low with { Low = 50 }, On, null, now), Is.False, "at the floor");
            Assert.That(Stream.Due(low, On, now.AddMinutes(-9), now), Is.False, "six an hour: ten minutes apart");
            Assert.That(Stream.Due(low, On, now.AddMinutes(-10), now), Is.True);
            Assert.That(Stream.Due(low, On with { Newcomers = false }, null, now), Is.False);
            Assert.That(Stream.Due(low, On with { FloorAtBasis = 0 }, null, now), Is.False);
            Assert.That(Stream.Due(low, On with { PerHourAtBasis = 0 }, null, now), Is.False);
            Assert.That(Stream.Due(low with { Roster = 0, Low = 0 }, On, null, now), Is.False);
        }

        [Test]
        public void TheLowLevelsAreCountedPerRealm()
        {
            var roster = new[] { (1, 1), (1, 19), (1, 20), (1, 50), (2, 5), (3, 50) };
            Assert.That(Stream.Count(eRealm.Albion, roster), Is.EqualTo(new Stream.RealmCount(eRealm.Albion, 4, 2)));
            Assert.That(Stream.Count(eRealm.Midgard, roster), Is.EqualTo(new Stream.RealmCount(eRealm.Midgard, 1, 1)));
            Assert.That(Stream.Count(eRealm.Hibernia, roster), Is.EqualTo(new Stream.RealmCount(eRealm.Hibernia, 1, 0)));
        }

        // ---- the newcomer ------------------------------------------------------------------------------------------

        [Test]
        public void ANewcomerIsALevelOneRowQueuedAsTheLaunchersBatchQueuesOne()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identity = AutonomousBotIdentityGenerator.Generate(eRealm.Midgard, eGender.Female, names, new Random(630));
            OfflineWorldBotRecord row = Stream.NewcomerRecord(identity, DateTime.UtcNow);
            Assert.That((row.Level, row.Experience, row.RealmPoints, row.MoneyCopper), Is.EqualTo((1, 0L, 0L, 0L)));
            Assert.That((row.Activity, row.CurrentGoal, row.Health), Is.EqualTo((NewBotRow.QueuedActivity, NewBotRow.QueuedGoal, NewBotRow.StartingHealth)));
            Assert.That((row.Realm, row.Gender, row.Name), Is.EqualTo(((int)eRealm.Midgard, (int)eGender.Female, identity.Name)));
            Assert.That(row.ClassName, Is.Not.Empty, "a ClassName: never taken for a saved buffbot copy (BuffBotCopyRetirement)");
            Assert.That(row.IsRetired || row.IsOnline, Is.False);
            Assert.That(AutonomousPopulationController.IsFreshUnplacedLevelOneRecord(row), Is.True,
                "placed at its first login by the server's Classic/SI start placement");
        }

        [Test]
        public void NewcomersNeverPlayTheCustomClassAndKeepTheEraClassMix()
        {
            var random = new Random(63);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var classes = new List<int>();
            for (int i = 0; i < 3000; i++)
            {
                var identity = AutonomousBotIdentityGenerator.Generate(eRealm.Hibernia, i % 2 == 0 ? eGender.Male : eGender.Female, names, random);
                classes.Add(Stream.NewcomerRecord(identity, DateTime.UtcNow).ClassId);
            }
            Assert.That(classes, Has.None.EqualTo(63), "bots never play the custom class (BotCustomClassRule)");
            Assert.That(classes.All(BotCustomClassRule.BotsMayPlay), Is.True);
            Assert.That(classes.Distinct().Count(), Is.GreaterThan(8), "the realm's class mix, not one class");
            Assert.That(names, Has.Count.EqualTo(3000), "every name unique");
            var custom = new AutonomousBotIdentityGenerator.Identity("Sluaggy", eRealm.Hibernia, eGender.Male, (eCharacterClass)63, eRace.Celt);
            Assert.Throws<ArgumentException>(() => Stream.NewcomerRecord(custom, DateTime.UtcNow));
        }

        [Test]
        public void ANewcomerStartsWithoutArmourAndEarnsIt()
        {
            var identity = AutonomousBotIdentityGenerator.Generate(eRealm.Albion, eGender.Male, null, new Random(1));
            OfflineWorldBotRecord row = Stream.NewcomerRecord(identity, DateTime.UtcNow);
            Assert.That(AutonomousStarterGear.Qualifies(row.Level, arrivesTrainedNow: true, alreadyGiven: false, armorPiecesWorn: 0,
                eRealm.Albion, row.BindRegionId), Is.False, "never starter gear at level 1");
            Assert.That(row.MoneyCopper, Is.Zero);
        }

        // ---- retirement --------------------------------------------------------------------------------------------

        private static Stream.Facts Clear(int classId = 7) =>
            new(false, false, false, false, false, false, false, false, false, false, classId, true);

        [Test]
        public void EveryExclusionHolds()
        {
            Assert.That(Stream.Exclusions(Clear()), Is.EqualTo(Stream.Exclusion.None));
            Assert.That(Stream.Exclusions(Clear() with { GuildLeader = true }), Is.EqualTo(Stream.Exclusion.GuildLeader));
            Assert.That(Stream.Exclusions(Clear() with { InPlayerGuild = true }), Is.EqualTo(Stream.Exclusion.PlayerGuild));
            Assert.That(Stream.Exclusions(Clear() with { OnPlayerFriendsList = true }), Is.EqualTo(Stream.Exclusion.PlayerFriend));
            Assert.That(Stream.Exclusions(Clear() with { Grouped = true }), Is.EqualTo(Stream.Exclusion.Grouped), "the player's group or a raid party");
            Assert.That(Stream.Exclusions(Clear() with { InRaidOrDuty = true }), Is.EqualTo(Stream.Exclusion.Raid));
            Assert.That(Stream.Exclusions(Clear() with { OpenTrade = true }), Is.EqualTo(Stream.Exclusion.Trade));
            Assert.That(Stream.Exclusions(Clear() with { OpenCraftOrder = true }), Is.EqualTo(Stream.Exclusion.CraftOrder));
            Assert.That(Stream.Exclusions(Clear() with { Busy = true }), Is.EqualTo(Stream.Exclusion.Busy));
            Assert.That(Stream.Exclusions(Clear() with { PlayerNearby = true }), Is.EqualTo(Stream.Exclusion.NearPlayer));
            Assert.That(Stream.Exclusions(Clear() with { Beggar = true }), Is.EqualTo(Stream.Exclusion.Beggar));
            Assert.That(Stream.Exclusions(Clear(63)), Is.EqualTo(Stream.Exclusion.CustomClass));
            Assert.That(Stream.Exclusions(Clear() with { Gamebot = false }), Is.EqualTo(Stream.Exclusion.NotAGamebot),
                "a hired companion, helper, buffbot or Special Force is never retired");
            Assert.That(Stream.ExclusionNames.Select(e => e.Flag), Is.EquivalentTo(Enum.GetValues<Stream.Exclusion>().Where(f => f != 0)),
                "every exclusion has a row name");
        }

        [Test]
        public void TheLeastPlayedAndLeastConnectedRetireFirstWeightedTowardTheLongestIdle()
        {
            // Owner decision 2026-11-25: "least-played and least-connected first, weighted toward the longest-idle".
            DateTime now = new(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc);
            Stream.Candidate C(long id, int level, double idleHours, int friends = 0, Stream.GuildRole guild = Stream.GuildRole.None,
                Stream.Exclusion ex = Stream.Exclusion.None, eRealm realm = eRealm.Albion) =>
                new(id, realm, 2, level, now.AddHours(-idleHours), false, ex, friends, guild);
            Assert.That(Stream.PickRetiree([C(1, 30, 10), C(2, 30, 300)], eRealm.Albion, now).BotId, Is.EqualTo(2), "the longer idle");
            Assert.That(Stream.PickRetiree([C(1, 30, 100, friends: 3), C(2, 30, 100)], eRealm.Albion, now).BotId, Is.EqualTo(2), "fewer friends");
            Assert.That(Stream.PickRetiree([C(1, 30, 100, guild: Stream.GuildRole.Member), C(2, 30, 100)], eRealm.Albion, now).BotId,
                Is.EqualTo(2), "no guild role");
            Assert.That(Stream.PickRetiree([C(1, 30, 100, guild: Stream.GuildRole.Officer), C(2, 30, 100, guild: Stream.GuildRole.Member)],
                eRealm.Albion, now).BotId, Is.EqualTo(2), "an officer stays longer than a member");
            Assert.That(Stream.PickRetiree([C(1, 48, 100), C(2, 22, 100)], eRealm.Albion, now).BotId, Is.EqualTo(2), "less played");
            Assert.That(Stream.PickRetiree([C(1, 30, 100), C(2, 30, 100)], eRealm.Albion, now).BotId, Is.EqualTo(2), "a tie: the newer character");
            Assert.That(Stream.PickRetiree([C(1, 50, 700, friends: 1), C(2, 25, 24)], eRealm.Albion, now).BotId, Is.EqualTo(1),
                "weighted toward the longest idle: a month away outweighs being played and having a friend");
            Assert.That(Stream.RetireScore(C(1, 20, 0), now), Is.EqualTo(0));
            Assert.That(Stream.RetireScore(C(1, 20, 5000), now), Is.EqualTo(Stream.IdleHoursCap), "idle capped at 30 days");
            Assert.That(Stream.RetireScore(new Stream.Candidate(1, eRealm.Albion, 2, 20, DateTime.MinValue, false, Stream.Exclusion.None), now),
                Is.EqualTo(Stream.IdleHoursCap), "no recorded activity counts as idle the whole cap");
            Assert.That(Stream.PickRetiree([C(6, 50, 900, realm: eRealm.Midgard)], eRealm.Albion, now), Is.Null, "its own realm only");
            Assert.That(Stream.ParseUtc(now.ToString("O")), Is.EqualTo(now));
            Assert.That(Stream.ParseUtc(""), Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void ABotBelowTwentyNeverRetires()
        {
            DateTime now = DateTime.UtcNow;
            var lows = Enumerable.Range(1, 19).Select(level =>
                new Stream.Candidate(level, eRealm.Hibernia, 47, level, DateTime.MinValue, false, Stream.Exclusion.None)).ToArray();
            Assert.That(Stream.PickRetiree(lows, eRealm.Hibernia, now), Is.Null, "the band the stream refills stays put");
            var withTwenty = lows.Append(new Stream.Candidate(99, eRealm.Hibernia, 47, 20, now, false, Stream.Exclusion.None));
            Assert.That(Stream.PickRetiree(withTwenty, eRealm.Hibernia, now).BotId, Is.EqualTo(99), "20 is the lowest that may retire");
            Assert.That(Stream.RetireMinLevel, Is.EqualTo(20));
        }

        [Test]
        public void EveryProtectionHoldsAtAnyLevel()
        {
            DateTime now = DateTime.UtcNow;
            foreach ((Stream.Exclusion flag, string name) in Stream.ExclusionNames)
                foreach (int level in new[] { 20, 35, 50 })
                {
                    var protectedBot = new Stream.Candidate(1, eRealm.Albion, 2, level, DateTime.MinValue, false, flag);
                    var other = new Stream.Candidate(2, eRealm.Albion, 2, 30, now, false, Stream.Exclusion.None, Friends: 5,
                        Guild: Stream.GuildRole.Officer);
                    Assert.That(Stream.PickRetiree([protectedBot], eRealm.Albion, now), Is.Null, $"{name} at {level}");
                    Assert.That(Stream.PickRetiree([protectedBot, other], eRealm.Albion, now).BotId, Is.EqualTo(2),
                        $"{name} at {level}: protected even when it scores far higher");
                }
            var candidates = new[]
            {
                new Stream.Candidate(1, eRealm.Albion, 2, 40, now, false, Stream.Exclusion.GuildLeader),
                new Stream.Candidate(2, eRealm.Albion, 2, 40, now, false, Stream.Exclusion.Grouped | Stream.Exclusion.PlayerFriend),
                new Stream.Candidate(3, eRealm.Albion, 2, 12, now, false, Stream.Exclusion.Grouped),
            };
            Assert.That(Stream.ExcludedSummary(candidates, eRealm.Albion), Is.EqualTo("leader:1,friend:1,group:1"), "below 20 is not a candidate");
        }

        [Test]
        public void ARetireeIsKeptNeverDeleted()
        {
            var row = new OfflineWorldBotRecord { BotId = 9, Name = "Oldtimer", Level = 50, Realm = 1, ClassId = 6, ClassName = "Cleric",
                IsOnline = true, MoneyCopper = 123_456, Activity = "Hunting" };
            Assert.That(Stream.Retire(row), Is.True);
            Assert.That((row.IsRetired, row.IsOnline, row.MoneyCopper, row.Name, row.Level), Is.EqualTo((true, false, 123_456L, "Oldtimer", 50)));
            Assert.That(RetiredFromPlayMarker.IsRetiree(row.IsRetired, row.Activity), Is.True, "the launcher shows it as kept");
            Assert.That(Stream.Retire(row), Is.False, "once");
            Assert.That(RetiredFromPlayMarker.IsRetiree(true, "Deletion requested"), Is.False, "a deletion stays a deletion");
            Assert.That(RetiredFromPlayMarker.IsRetiree(false, row.Activity), Is.False);
        }

        [Test]
        public void TheStreamWritesAndDeletesNothingElse()
        {
            // Only these database calls are in the stream: a newcomer's AddObject and a retiree's SaveObject. No DeleteObject.
            string source = File.ReadAllText(Path.Combine(SourceRoot(), "source/server/GameServer/bots/autonomous/AutonomousNewcomerStream.cs"));
            Assert.That(source, Does.Not.Contain("DeleteObject"));
            Assert.That(source, Does.Not.Contain("AllowDelete"));
            Assert.That(source, Does.Not.Contain("DeleteBot"));
        }

        // ---- a simulated day -----------------------------------------------------------------------------------------

        private sealed class Bot
        {
            public OfflineWorldBotRecord Row;
            public int Items;
            public Stream.Exclusion Excluded;
        }

        [Test]
        public void AFreshWorldOfTwentyToThirtyFivesKeepsEachRealmsRosterTheSameSizeAtTheCappedRate()
        {
            var random = new Random(2026);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var world = new List<Bot>();
            long nextId = 1;
            DateTime start = new(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc);
            // Owner 2026-11-25: a fresh world has almost no 50s. Per realm 1,000 gamebots: 20 in levels 1-19 (floor 50) and
            // 980 at 20-35, none at 50; a third of the 20-35s protected.
            foreach (eRealm realm in Stream.Realms)
                for (int i = 0; i < 1000; i++)
                {
                    int level = i < 20 ? 1 + i % 19 : 20 + i % 16;
                    var exclusions = new[] { Stream.Exclusion.GuildLeader, Stream.Exclusion.Grouped, Stream.Exclusion.PlayerFriend, Stream.Exclusion.CraftOrder };
                    world.Add(new Bot
                    {
                        Row = new OfflineWorldBotRecord { BotId = nextId++, Realm = (int)realm, Level = level, ClassId = 2, ClassName = "x", Name = "B" + nextId,
                            LastMeaningfulProgressUtc = start.AddHours(-random.Next(1, 1000)).ToString("O") },
                        Items = 20,
                        Excluded = level >= 20 && i % 3 == 0 ? exclusions[i % exclusions.Length] : Stream.Exclusion.None,
                    });
                }
            int rows = world.Count;
            var lastAdded = new Dictionary<eRealm, DateTime>();
            var added = new Dictionary<eRealm, List<DateTime>>();
            var retiredIds = new HashSet<long>();
            for (int minute = 0; minute < 24 * 60; minute++)
            {
                DateTime now = start.AddMinutes(minute);
                foreach (eRealm realm in Stream.Realms)
                {
                    var live = world.Where(b => !b.Row.IsRetired).ToArray();
                    Stream.RealmCount count = Stream.Count(realm, live.Select(b => (b.Row.Realm, b.Row.Level)));
                    if (!Stream.Due(count, On, lastAdded.TryGetValue(realm, out DateTime last) ? last : null, now))
                        continue;
                    Stream.Candidate retiree = Stream.PickRetiree(live.Select(b =>
                        new Stream.Candidate(b.Row.BotId, (eRealm)b.Row.Realm, b.Row.ClassId, b.Row.Level,
                            Stream.ParseUtc(b.Row.LastMeaningfulProgressUtc), false, b.Excluded)), realm, now);
                    Assert.That(retiree, Is.Not.Null);
                    Bot old = world.Single(b => b.Row.BotId == retiree.BotId);
                    Assert.That(old.Excluded, Is.EqualTo(Stream.Exclusion.None));
                    var identity = AutonomousBotIdentityGenerator.Generate(realm, eGender.Male, names, random);
                    OfflineWorldBotRecord row = Stream.NewcomerRecord(identity, now);
                    row.BotId = nextId++;
                    world.Add(new Bot { Row = row });
                    Assert.That(Stream.Retire(old.Row), Is.True);
                    retiredIds.Add(old.Row.BotId);
                    lastAdded[realm] = now;
                    (added.TryGetValue(realm, out var list) ? list : added[realm] = new()).Add(now);
                }
            }
            foreach (eRealm realm in Stream.Realms)
            {
                Assert.That(world.Count(b => !b.Row.IsRetired && b.Row.Realm == (int)realm), Is.EqualTo(1000), $"{realm}: the roster stays the same size");
                List<DateTime> times = added[realm];
                Assert.That(times, Has.Count.EqualTo(30), $"{realm}: 30 newcomers lift 20 lows to the floor of 50");
                for (int i = 0; i < times.Count; i++)
                    Assert.That(times.Count(t => t >= times[i] && t < times[i].AddHours(1)), Is.LessThanOrEqualTo(6), $"{realm}: at most 6 an hour");
                Assert.That(world.Count(b => !b.Row.IsRetired && b.Row.Realm == (int)realm && b.Row.Level <= 19), Is.EqualTo(50), "stops at the floor");
            }
            Assert.That(world, Has.Count.EqualTo(rows + 90), "every row is kept: newcomers added, nobody removed");
            Assert.That(world.Where(b => retiredIds.Contains(b.Row.BotId)).All(b => b.Items == 20 && b.Row.IsRetired), Is.True,
                "retirees keep their items and stay in the records");
            Assert.That(world.Where(b => b.Excluded != Stream.Exclusion.None).Any(b => b.Row.IsRetired), Is.False, "no excluded bot retired");
            Assert.That(world.Where(b => b.Row.IsRetired).All(b => b.Row.Level is >= 20 and <= 35), Is.True,
                "with no 50s, 20-35s retire, one per newcomer; never below 20");
            Assert.That(retiredIds, Has.Count.EqualTo(90), "one retirement per newcomer");
        }

        // ---- rows ----------------------------------------------------------------------------------------------------

        [Test]
        public void TheRowsCarryNoNames()
        {
            var identity = AutonomousBotIdentityGenerator.Generate(eRealm.Albion, eGender.Male, null, new Random(5));
            OfflineWorldBotRecord row = Stream.NewcomerRecord(identity, DateTime.UtcNow);
            row.BotId = 77;
            var count = new Stream.RealmCount(eRealm.Albion, 1000, 31);
            string addedRow = Stream.AddedRow(count, On, row, 12);
            Assert.That(addedRow, Is.EqualTo($"POPULATION_NEWCOMER realm=Albion outcome=added id=77 class={identity.CharacterClass} " +
                                             $"race={identity.Race} level=1 low=31 floor=50 roster=1000 per_hour=6 retired=12"));
            Assert.That(addedRow, Does.Not.Contain(identity.Name));
            var retiree = new Stream.Candidate(12, eRealm.Albion, 7, 50, DateTime.UtcNow.AddHours(-30), true, Stream.Exclusion.None, 1,
                Stream.GuildRole.Member);
            string retiredRow = Stream.RetiredRow(retiree, 77, 300, "leader:3,group:10", DateTime.UtcNow);
            Assert.That(retiredRow, Does.StartWith("POPULATION_RETIRED realm=Albion id=12 class=Wizard level=50 online=1 idle_h=").And
                .EndWith(" friends=1 guild=member score=-330 newcomer=77 candidates=300 excluded=leader:3,group:10 order=least_played_connected_idle"));
            Assert.That(Stream.HeldRow(count, On, "no_retiree", "0"),
                Is.EqualTo("POPULATION_NEWCOMER realm=Albion outcome=held reason=no_retiree low=31 floor=50 roster=1000 per_hour=6 excluded=0"));
        }

        // ---- settings ------------------------------------------------------------------------------------------------

        private static readonly string[] Keys = ["population_newcomers", "population_newcomer_floor", "population_newcomer_per_hour", "population_retire"];

        [Test]
        public void TheSettingsAreOnByDefaultAndShipOn()
        {
            Dictionary<string, object> defaults = typeof(Properties).Assembly.GetTypes()
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Select(field => field.GetCustomAttribute<ServerPropertyAttribute>())
                .Where(attribute => attribute != null)
                .GroupBy(attribute => attribute.Key)
                .ToDictionary(group => group.Key, group => group.First().DefaultValue);
            Assert.That(defaults["population_newcomers"], Is.EqualTo(true));
            Assert.That(defaults["population_newcomer_floor"], Is.EqualTo(50));
            Assert.That(defaults["population_newcomer_per_hour"], Is.EqualTo(6));
            Assert.That(defaults["population_retire"], Is.EqualTo(true));
            Assert.That((Properties.POPULATION_NEWCOMERS, Properties.POPULATION_NEWCOMER_FLOOR, Properties.POPULATION_NEWCOMER_PER_HOUR, Properties.POPULATION_RETIRE),
                Is.EqualTo((true, 50, 6, true)), "the fields start at their defaults");
            Assert.That(Upgrade.Changes.Select(change => change.Key).Intersect(Keys), Is.Empty,
                "new keys: a world from an earlier build has no row, so it takes the code default without an upgrade line");
            string release = File.ReadAllText(Path.Combine(SourceRoot(), "source/tools/release-server-settings.json"));
            foreach ((string key, string value) in new[] { ("population_newcomers", "True"), ("population_newcomer_floor", "50"),
                         ("population_newcomer_per_hour", "6"), ("population_retire", "True") })
                Assert.That(release, Does.Contain($"\"{key}\": \"{value}\""), key + " ships at its default");
        }

        private static string SourceRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source/server/GameServer")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository source not found beside the tests.");
            return dir;
        }
    }
}
