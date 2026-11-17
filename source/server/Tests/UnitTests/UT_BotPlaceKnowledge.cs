using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people, work package 3 (docs/BOTS-AS-PEOPLE-1.2.md, item 4): the places a gamebot knows. Seeding by level
    /// (owner decision 8: capital, home town, own zones below its level, the border keep from 20; dungeons unknown),
    /// learning on arrival, told the way (half known), the saved bits and the save round trip. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotPlaceKnowledge
    {
        private static readonly BotPlaceCatalog Catalog = BotPlaceCatalog.Current;
        private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

        private BotPeopleStore _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = BotPeopleStore.UseForTests(new BotPeopleStore(() => null));
            BotPlaceKnowledge.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            BotPeopleStore.UseForTests(_previous);
            BotPlaceKnowledge.ResetForTests();
        }

        private static AutonomousChatPlaces.Place P(BotPlaceKey key) => Catalog.PlaceOf(key);

        private static IEnumerable<BotPlaceKey> OfRealm(eRealm realm, string type) =>
            Catalog.All.Where(key => Catalog.IsOfRealm(key, realm) && BotPlaceCatalog.IsType(P(key), type));

        private static BotPlaceKey K(string name)
        {
            BotPlaceKey key = BotPlaceKey.Of(name);
            Assert.That(key.IsNone, Is.False, name + " is a place");
            return key;
        }

        // ---- the saved bits --------------------------------------------------------------------------------------------

        [Test]
        public void Every_place_of_places_json_has_a_saved_bit_that_never_moves()
        {
            Assert.That(AutonomousChatPlaces.All, Is.Not.Empty);
            Assert.That(Catalog.Unlisted, Is.Empty, "a new place is appended to BotPlaceKnowledge.BitOrder");
            Assert.That(Catalog.All.Count, Is.EqualTo(AutonomousChatPlaces.All.Length));
            Assert.That(BotPlaceKnowledge.BitOrder, Is.Unique);
            Assert.That(BotPlaceKnowledge.BitOrder.Length - 1, Is.LessThanOrEqualTo(BotPeopleBits.MaxBit));
            // Pinned: saved worlds read these bits. Never reorder the list.
            Assert.That(BotPlaceKey.Of("camelot").Bit, Is.EqualTo(0));
            Assert.That(BotPlaceKey.Of("Lyonesse").Bit, Is.EqualTo(11));
            Assert.That(BotPlaceKey.Of("mularn").Bit, Is.EqualTo(68));
            Assert.That(BotPlaceKey.Of("darkness falls").Bit, Is.EqualTo(139));
            Assert.That(BotPlaceKey.Of("caledonia").Bit, Is.EqualTo(143));
        }

        [Test]
        public void A_place_key_is_none_by_default_and_reads_its_name()
        {
            Assert.That(default(BotPlaceKey).IsNone, Is.True);
            Assert.That(BotPlaceKey.None.Bit, Is.EqualTo(-1));
            Assert.That(BotPlaceKey.Of("no such place").IsNone, Is.True);
            Assert.That(BotPlaceKey.Of(null).IsNone, Is.True);
            Assert.That(BotPlaceKey.FromBit(-1).IsNone && BotPlaceKey.FromBit(9999).IsNone, Is.True);
            Assert.That(K("Tir na Nog").Name, Is.EqualTo("tir na nog"));
            Assert.That(K("tir na nog"), Is.EqualTo(BotPlaceKey.FromBit(2)));
            Assert.That(K("lyonesse").ToString(), Is.EqualTo("lyonesse"));
        }

        // ---- seeding (owner decision 8) --------------------------------------------------------------------------------

        [TestCaseSource(nameof(Realms))]
        public void A_level_30_knows_the_1_to_29_zones_of_its_realm_and_no_dungeon(eRealm realm)
        {
            BotPlaceKey home = Catalog.StartTown(realm);
            BotPlaceSet seed = BotPlaceKnowledge.Seed(30, realm, home, Catalog);
            foreach (BotPlaceKey zone in OfRealm(realm, "zone"))
            {
                int[] levels = P(zone).Levels;
                if (levels[1] <= 29)
                    Assert.That(seed.Knows(zone), Is.True, $"{zone} ({levels[0]}-{levels[1]}) was levelled through");
                if (levels[0] >= 30)
                    Assert.That(seed.Knows(zone), Is.False, $"{zone} ({levels[0]}-{levels[1]}) is new ground");
            }
            Assert.That(seed.Knows(Catalog.Capital(realm)), Is.True, "its capital");
            Assert.That(seed.Knows(home), Is.True, "its home town");
            Assert.That(seed.Knows(Catalog.BorderKeep(realm)), Is.True, "its border keep from 20");
            foreach (BotPlaceKey dungeon in Catalog.All.Where(key => BotPlaceCatalog.IsType(P(key), "dungeon")))
                Assert.That(seed.Knows(dungeon), Is.False, $"{dungeon}: dungeons are unknown until visited");
            foreach (BotPlaceKey key in seed.KnownPlaces)
            {
                Assert.That(Catalog.IsOfRealm(key, realm), Is.True, $"{key}: only its own realm's places");
                Assert.That(BotPlaceCatalog.IsType(P(key), "frontier") || BotPlaceCatalog.IsType(P(key), "battleground"), Is.False,
                    $"{key}: the frontier is new ground (only the keep is known)");
            }
            Assert.That(seed.HalfKnownCount, Is.Zero);
        }

        [Test]
        public void The_usual_level_is_the_middle_of_the_zones_range()
        {
            BotPlaceSet seed = BotPlaceKnowledge.Seed(30, eRealm.Albion, BotPlaceKey.None, Catalog);
            Assert.That(seed.Knows(K("cornwall")), Is.True, "Cornwall 18-31: levelled through by 30");
            Assert.That(seed.Knows(K("avalon isle")), Is.False, "Avalon Isle 25-35: the next band");
            Assert.That(seed.Knows(K("llyn barfog")), Is.False, "Llyn Barfog 21-63: its usual level is 42");
            Assert.That(seed.Knows(K("cornwall station")), Is.True, "the towns of a known zone");
            Assert.That(seed.Knows(K("wearyall")), Is.False, "the towns of an unknown zone");
            Assert.That(BotPlaceKnowledge.LevelledThrough([18, 31], 25), Is.True);
            Assert.That(BotPlaceKnowledge.LevelledThrough([18, 31], 24), Is.False);
            Assert.That(BotPlaceKnowledge.LevelledThrough(null, 50), Is.False);
        }

        [TestCaseSource(nameof(Realms))]
        public void The_border_keep_is_known_from_level_20(eRealm realm)
        {
            BotPlaceKey keep = Catalog.BorderKeep(realm);
            Assert.That(keep.IsNone, Is.False);
            // Druim Ligen stands in Connacht (places.json), so a Hibernian who levelled through Connacht knows it already.
            bool inLevelledZone = BotPlaceKnowledge.Seed(19, realm, BotPlaceKey.None, Catalog).KnownPlaces
                .Any(key => BotPlaceCatalog.IsType(P(key), "zone") && P(key).Zones.Intersect(P(keep).Zones).Any());
            Assert.That(inLevelledZone, Is.EqualTo(realm == eRealm.Hibernia));
            Assert.That(BotPlaceKnowledge.Seed(19, realm, BotPlaceKey.None, Catalog).Knows(keep), Is.EqualTo(inLevelledZone));
            Assert.That(BotPlaceKnowledge.Seed(11, realm, BotPlaceKey.None, Catalog).Knows(keep), Is.False);
            Assert.That(BotPlaceKnowledge.Seed(20, realm, BotPlaceKey.None, Catalog).Knows(keep), Is.True);
        }

        [Test]
        public void The_border_keeps_are_the_staging_ones()
        {
            Assert.That(Catalog.BorderKeep(eRealm.Albion).Name, Is.EqualTo("castle sauvage"));
            Assert.That(Catalog.BorderKeep(eRealm.Midgard).Name, Is.EqualTo("svasud faste"));
            Assert.That(Catalog.BorderKeep(eRealm.Hibernia).Name, Is.EqualTo("druim ligen"));
            Assert.That(Catalog.Capital(eRealm.Midgard).Name, Is.EqualTo("jordheim"));
            Assert.That(Catalog.BorderKeep(eRealm.None).IsNone, Is.True);
        }

        [TestCaseSource(nameof(Realms))]
        public void A_level_1_knows_its_capital_and_home_town_and_the_rest_grows_with_its_level(eRealm realm)
        {
            BotPlaceKey home = Catalog.StartTown(realm);
            BotPlaceSet first = BotPlaceKnowledge.Seed(1, realm, home, Catalog);
            Assert.That(first.KnownPlaces, Is.EquivalentTo(new[] { Catalog.Capital(realm), home }));
            BotPlaceSet previous = first;
            for (int level = 2; level <= 50; level++)
            {
                BotPlaceSet seed = BotPlaceKnowledge.Seed(level, realm, home, Catalog);
                Assert.That(previous.NewIn(seed).Count() + previous.KnownCount, Is.EqualTo(seed.KnownCount), $"level {level} keeps level {level - 1}'s");
                Assert.That(previous.KnownPlaces.All(seed.Knows), Is.True, $"level {level} knows all of level {level - 1}");
                previous = seed;
            }
            Assert.That(previous.KnownCount, Is.GreaterThan(first.KnownCount + 10), "a 50 knows most of its realm's zones");
            Assert.That(BotPlaceKnowledge.Seed(50, eRealm.None, home, Catalog), Is.EqualTo(BotPlaceSet.Empty));
        }

        [Test]
        public void The_home_town_is_a_town_of_the_bind_zone_else_the_start_town()
        {
            string[] vale = ["mularn", "haggerfel", "fort veldon"];
            var picked = new HashSet<string>();
            for (long id = 1; id <= 60; id++)
            {
                BotPlaceKey home = BotPlaceKnowledge.HomeTown(eRealm.Midgard, "Vale of Mularn", id, Catalog);
                Assert.That(vale, Does.Contain(home.Name));
                Assert.That(BotPlaceKnowledge.HomeTown(eRealm.Midgard, "Vale of Mularn", id, Catalog), Is.EqualTo(home), "fixed from the id");
                picked.Add(home.Name);
            }
            Assert.That(picked.Count, Is.EqualTo(3), "spread over the zone's towns");
            Assert.That(BotPlaceKnowledge.HomeTown(eRealm.Albion, "City of Camelot", 5, Catalog).Name, Is.EqualTo("cotswold"));
            Assert.That(BotPlaceKnowledge.HomeTown(eRealm.Hibernia, null, 5, Catalog).Name, Is.EqualTo("mag mell"));
            Assert.That(BotPlaceKnowledge.HomeTown(eRealm.Albion, "Vale of Mularn", 5, Catalog).Name, Is.EqualTo("cotswold"),
                "never another realm's town");
            Assert.That(BotPlaceKnowledge.HomeTown(eRealm.Hibernia, "Domnann", 5, Catalog).Name, Is.EqualTo("grove of domnann"));
        }

        // ---- the set ---------------------------------------------------------------------------------------------------

        [Test]
        public void Told_is_half_known_until_visited_and_a_known_place_stays_known()
        {
            BotPlaceKey krondon = K("krondon"), lyonesse = K("lyonesse");
            BotPlaceSet set = BotPlaceSet.Empty.Learn(krondon, BotPlaceLearning.Told);
            Assert.That(set.Knowing(krondon), Is.EqualTo(BotPlaceKnowing.HalfKnown));
            Assert.That(set.Knows(krondon), Is.False);
            set = set.Learn(krondon, BotPlaceLearning.Visited);
            Assert.That(set.Knowing(krondon), Is.EqualTo(BotPlaceKnowing.Known));
            Assert.That(set.HalfKnownCount, Is.Zero, "visiting clears the half-known bit");
            Assert.That(set.Learn(krondon, BotPlaceLearning.Told), Is.EqualTo(set), "told the way to a known place changes nothing");
            Assert.That(set.Knowing(lyonesse), Is.EqualTo(BotPlaceKnowing.Unknown));
            Assert.That(set.Knowing(BotPlaceKey.None), Is.EqualTo(BotPlaceKnowing.Unknown));
            Assert.That(set.Learn(BotPlaceKey.None, BotPlaceLearning.Visited), Is.EqualTo(set));

            var row = new BotPlaceSet(set.Known, BotPeopleBits.With(string.Empty, lyonesse.Bit));
            BotPlaceSet joined = row.Union(BotPlaceSet.Empty.Learn(lyonesse, BotPlaceLearning.Seeded));
            Assert.That(joined.Knows(lyonesse) && joined.HalfKnownCount == 0, Is.True, "known in one side wins over half known");
            Assert.That(new BotPlaceSet("zz", null), Is.EqualTo(BotPlaceSet.Empty), "unreadable bits are the empty set");
        }

        [Test]
        public void Arriving_in_a_zone_learns_the_zone_and_its_towns_a_region_its_dungeon()
        {
            List<BotPlaceKey> vale = BotPlaceKnowledge.NewAt(BotPlaceSet.Empty, "Vale of Mularn", "Midgard", Catalog);
            Assert.That(vale.Select(key => key.Name), Is.EquivalentTo(new[] { "vale of mularn", "mularn", "haggerfel", "fort veldon" }));
            List<BotPlaceKey> spindel = BotPlaceKnowledge.NewAt(BotPlaceSet.Empty, "Spindelhalla", "Spindelhalla", Catalog);
            Assert.That(spindel.Select(key => key.Name), Is.EqualTo(new[] { "spindelhalla" }), "a place found twice is learned once");
            BotPlaceSet known = BotPlaceSet.Of(vale);
            Assert.That(BotPlaceKnowledge.NewAt(known, "Vale of Mularn", null, Catalog), Is.Empty);
            Assert.That(BotPlaceKnowledge.NewAt(known, "Nowhere Special", null, Catalog), Is.Empty);
            Assert.That(BotPlaceKnowledge.NewAt(known, null, null, Catalog), Is.Empty);
        }

        // ---- the live rules on plain facts (the store, seeding, arrival) -----------------------------------------------

        private static BotPlaceSubject Albion(long id, int level) =>
            new(BotPersonKey.Character(id), id, "Testbot" + id, eRealm.Albion, level, BindZone: "Camelot Hills");

        [Test]
        public void A_bot_is_seeded_at_first_sight_and_learns_on_arrival()
        {
            BotPlaceSubject bot = Albion(77, 30);
            BotPlaceSet seeded = BotPlaceKnowledge.Ensure(bot);
            Assert.That(seeded.Knows(K("salisbury plains")) && seeded.Knows(K("castle sauvage")), Is.True);
            Assert.That(BotPeopleStore.Current.Places.TryGet((bot.Person, (int)eRealm.Albion), out OfflineBotPlacesRecord row), Is.True);
            Assert.That(row.PersonKey, Is.EqualTo("c:77"));
            Assert.That(row.CharacterId, Is.EqualTo(77));
            Assert.That(row.Known, Is.EqualTo(seeded.Known));
            Assert.That(seeded.Knows(K("cotswold")) || seeded.Knows(K("prydwen")), Is.True, "its home town in Camelot Hills");

            BotPlaceKey barrows = K("stonehenge barrows");
            Assert.That(seeded.Knows(barrows), Is.False, "a dungeon is unknown until visited");
            List<BotPlaceKey> learned = BotPlaceKnowledge.Arrive(bot, "Stonehenge Barrows", "Stonehenge Barrows");
            Assert.That(learned, Is.EqualTo(new[] { barrows }));
            Assert.That(BotPlaceKnowledge.Ensure(bot).Knows(barrows), Is.True);
            Assert.That(BotPlaceKnowledge.Arrive(bot, "Stonehenge Barrows", "Stonehenge Barrows"), Is.Empty, "known once");
            Assert.That(BotPlaceKnowledge.Arrive(bot, "Salisbury Plains", null), Is.Empty, "a seeded zone is not new");
            Assert.That(BotPeopleStore.Current.Counts()[1], Is.EqualTo(1), "one row per person and realm");
        }

        [Test]
        public void A_bot_that_levels_is_given_its_new_levels_zones()
        {
            BotPlaceKey avalonIsle = K("avalon isle");
            Assert.That(BotPlaceKnowledge.Ensure(Albion(78, 30)).Knows(avalonIsle), Is.False);
            BotPlaceSet later = BotPlaceKnowledge.Ensure(Albion(78, 31));
            Assert.That(later.Knows(avalonIsle), Is.True, "Avalon Isle 25-35 at 31");
            Assert.That(BotPlaceKnowledge.Ensure(Albion(78, 31)), Is.EqualTo(later));
        }

        [Test]
        public void Told_the_way_is_saved_half_known_until_it_goes_there()
        {
            BotPlaceSubject bot = Albion(79, 45);
            BotPlaceKey krondon = K("krondon");
            Assert.That(BotPlaceKnowledge.Learn(bot, krondon, BotPlaceLearning.Told), Is.True);
            Assert.That(BotPlaceKnowledge.Learn(bot, krondon, BotPlaceLearning.Told), Is.False, "nothing new");
            Assert.That(BotPlaceKnowledge.Ensure(bot).Knowing(krondon), Is.EqualTo(BotPlaceKnowing.HalfKnown));
            BotPlaceKnowledge.Arrive(bot, "Krondon", "Krondon");
            Assert.That(BotPlaceKnowledge.Ensure(bot).Knowing(krondon), Is.EqualTo(BotPlaceKnowing.Known));
            Assert.That(BotPlaceKnowledge.Learn(new BotPlaceSubject(default, 0, "Nobody", eRealm.Albion, 30), krondon,
                BotPlaceLearning.Visited), Is.False, "no person, no record");
        }

        [Test]
        public void Anything_that_is_not_a_saved_gamebot_knows_the_whole_map()
        {
            Assert.That(BotPlaceKnowledge.Knows((GameBot)null, K("caer sidi")), Is.True, "today's route");
            Assert.That(BotPlaceKnowledge.Knows((GameBot)null, "Caer Sidi"), Is.True);
            Assert.That(BotPlaceKnowledge.KnownBy(null), Is.EqualTo(BotPlaceSet.Empty));
            Assert.That(BotPlaceKnowledge.Learn((GameBot)null, K("caer sidi"), BotPlaceLearning.Visited), Is.False);
            Assert.That(BotPlaceKnowledge.Ensure(default), Is.EqualTo(BotPlaceSet.Empty));
            Assert.That(BotPeopleStore.Current.Counts()[1], Is.Zero);
        }

        [Test]
        public void The_places_are_saved_to_sqlite_and_read_back()
        {
            string folder = TemporarySqlite.NewFolder("botplaces");
            try
            {
                var database = ObjectDatabase.GetObjectDatabase(DOL.Database.Connection.EConnectionType.DATABASE_SQLITE,
                    $"Data Source={Path.Combine(folder, "places.db")};Pooling=False");
                foreach (Type type in BotPeopleTables.RecordTypes)
                    database.RegisterDataObject(type);
                BotPeopleStore.UseForTests(new BotPeopleStore(() => database));
                BotPlaceSubject a = Albion(81, 30);
                var b = new BotPlaceSubject(BotPersonKey.Character(82), 82, "Testmid", eRealm.Midgard, 12, "Vale of Mularn");
                BotPlaceKnowledge.Ensure(a);
                BotPlaceKnowledge.Arrive(a, "Lyonesse", null);
                BotPlaceKnowledge.Learn(a, K("krondon"), BotPlaceLearning.Told);
                BotPlaceKnowledge.Arrive(b, "Spindelhalla", "Spindelhalla");
                BotPlaceSet savedA = BotPlaceKnowledge.Ensure(a), savedB = BotPlaceKnowledge.Ensure(b);
                BotPeopleStore.Current.Flush(wait: true);
                BotPlaceKnowledge.Arrive(a, "Avalon Isle", null);  // a change after the first insert
                BotPeopleStore.Current.Flush(wait: true);

                var reloaded = new BotPeopleStore(() => database);
                reloaded.LoadFrom(database);
                BotPeopleStore.UseForTests(reloaded);
                BotPlaceKnowledge.ResetForTests();
                BotPlaceSet backA = BotPlaceKnowledge.Ensure(a), backB = BotPlaceKnowledge.Ensure(b);
                Assert.That(reloaded.Counts()[1], Is.EqualTo(2));
                Assert.That(backA, Is.EqualTo(savedA.Learn(K("avalon isle"), BotPlaceLearning.Visited).Learn(K("wearyall"), BotPlaceLearning.Visited)),
                    "the zone and its town, learned after the first insert");
                Assert.That(backA.Knows(K("lyonesse")) && backA.HalfKnows(K("krondon")), Is.True);
                Assert.That(backB, Is.EqualTo(savedB));
                Assert.That(backB.Knows(K("spindelhalla")) && backB.Knows(K("mularn")) && !backB.Knows(K("lyonesse")), Is.True);
                Assert.That(reloaded.Places.TryGet((b.Person, (int)eRealm.Midgard), out OfflineBotPlacesRecord row) &&
                            row.CharacterId == 82 && row.UpdatedUtc.Length > 0, Is.True);
            }
            finally
            {
                TemporarySqlite.DeleteFolder(folder);
            }
        }

        // ---- the Explorers' zones seen lately (moved, unchanged) -------------------------------------------------------

        [Test]
        public void The_explorers_recent_zones_live_here_and_still_answer_the_personality()
        {
            DateTime now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
            const long id = 990_017;
            Assert.That(AutonomousBotPersonality.ZoneMemoryCapacity, Is.EqualTo(32));
            Assert.That(AutonomousBotPersonality.ZoneFreshAfter, Is.EqualTo(TimeSpan.FromHours(6)));
            AutonomousBotPersonality.RememberZoneAt(id, "Lyonesse", now);
            Assert.That(BotPlaceKnowledge.IsRecentZoneFresh(id, "lyonesse", now.AddHours(1)), Is.False);
            Assert.That(AutonomousBotPersonality.IsZoneFresh(id, "Lyonesse", now.AddHours(7)), Is.True);
            Assert.That(BotPeopleStore.Current.Counts()[1], Is.Zero, "choosing a camp is not arriving: nothing is learned");
        }

        // ---- log lines and /lswhy --------------------------------------------------------------------------------------

        [Test]
        public void The_log_lines_name_how_and_what()
        {
            Assert.That(BotPlaceKnowledge.LearnedLine("Aldwin", 12, eRealm.Albion, 30, BotPlaceLearning.Visited, "lyonesse", 1, "Lyonesse"),
                Is.EqualTo("BOT_PLACE_LEARNED how=visited bot=Aldwin id=12 realm=Albion level=30 place=\"lyonesse\" zone=\"Lyonesse\""));
            Assert.That(BotPlaceKnowledge.LearnedLine("Aldwin", 12, eRealm.Albion, 30, BotPlaceLearning.Seeded, "first sight", 17, null),
                Is.EqualTo("BOT_PLACE_LEARNED how=seeded bot=Aldwin id=12 realm=Albion level=30 places=17 at=\"first sight\""));
            Assert.That(BotPlaceKnowledge.HowName(BotPlaceLearning.Told), Is.EqualTo("told"));
            string budget = BotPlaceKnowledge.BudgetLine(300, 40, 12, 1, 900, 50, 30, 21, 50,
                new Dictionary<eRealm, double> { [eRealm.Albion] = 18.25 });
            Assert.That(budget, Is.EqualTo("BOT_PLACE_BUDGET seconds=300 checks=40 learned=visited:12,told:1,seeded:900 " +
                                           "bots_seeded=50 lines=30 suppressed=21 rows=50 known_avg=alb:18.3,mid:0.0,hib:0.0"));
        }

        [Test]
        public void Lswhy_way_lists_the_places_by_kind()
        {
            BotPlaceSet set = BotPlaceKnowledge.Seed(35, eRealm.Midgard, K("mularn"), Catalog).Learn(K("spindelhalla"), BotPlaceLearning.Visited)
                .Learn(K("trollheim"), BotPlaceLearning.Told);
            List<string> lines = BotPlaceKnowledge.DescribeLines(set, eRealm.Midgard, 35, Catalog);
            Assert.That(lines[0], Does.StartWith($"Places (Midgard): knows {set.KnownCount} of {Catalog.All.Count}, half knows 1."));
            Assert.That(lines.Single(line => line.StartsWith("Zones:")), Does.Contain("vale of mularn").And.Contain("myrkwood").And.Not.Contain("raumarik"));
            Assert.That(lines.Single(line => line.StartsWith("Capital and towns:")), Does.Contain("jordheim").And.Contain("svasud faste"));
            Assert.That(lines.Single(line => line.StartsWith("Dungeons:")), Is.EqualTo("Dungeons: spindelhalla"));
            Assert.That(lines.Last(), Is.EqualTo("Told the way to: trollheim"));
        }
    }
}
