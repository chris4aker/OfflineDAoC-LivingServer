using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #529 (RvR work 11, piece B, steps 1-2): the contact table as a facade written by the scan, calls and hits, and the
    /// fight board's shadow against it. Unit tests only: nothing here has been seen in game.
    /// </summary>
    [TestFixture]
    public class UT_RvrContacts529
    {
        private const int Albion = 1, Midgard = 2, Hibernia = 3;
        private const ushort Emain = 163;

        private static RvrContactSighting At(long target, int x, int y, int realm = Midgard, RvrPerceivedKind kind = RvrPerceivedKind.EnemyBot,
            long party = 0, ushort region = Emain) =>
            new(target, party == 0 ? target : party, realm, kind, region, x, y, 0);

        // ---- step 1: the table -----------------------------------------------------------------------------------------

        [Test]
        public void A_write_is_read_back_with_its_sources_place_and_times()
        {
            var table = new RvrContacts();
            table.Note(10, Albion, At(500, 100, 200), RvrContactSource.Noticed, 1_000);
            table.Note(10, Albion, At(500, 300, 400), RvrContactSource.Hit, 4_000);
            Assert.That(table.TryGet(10, 500, 5_000, out RvrContact contact), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(contact.FirstSeen, Is.EqualTo(1_000));
                Assert.That(contact.LastSeen, Is.EqualTo(4_000));
                Assert.That((contact.X, contact.Y), Is.EqualTo((300, 400)));
                Assert.That(contact.Sources, Is.EqualTo(RvrContactSource.Noticed | RvrContactSource.Hit));
                Assert.That(contact.LastSource, Is.EqualTo(RvrContactSource.Hit));
                Assert.That(contact.Firsthand, Is.True);
                Assert.That(contact.OwnerRealm, Is.EqualTo(Albion));
                Assert.That(table.TryGet(11, 500, 5_000, out _), Is.False, "another bot knows nothing of it");
            });
        }

        [Test]
        public void An_older_write_adds_its_source_but_does_not_move_the_contact()
        {
            var table = new RvrContacts();
            table.Note(10, Albion, At(500, 300, 400), RvrContactSource.Noticed, 5_000);
            table.Note(10, Albion, At(500, 1, 1), RvrContactSource.Called, 4_000);
            table.TryGet(10, 500, 5_000, out RvrContact contact);
            Assert.That((contact.X, contact.LastSeen, contact.LastSource), Is.EqualTo((300, 5_000L, RvrContactSource.Noticed)));
            Assert.That(contact.Sources.HasFlag(RvrContactSource.Called), Is.True);
        }

        [Test]
        public void Nothing_is_written_without_a_source_or_an_owner()
        {
            var table = new RvrContacts();
            table.Note(10, Albion, At(500, 0, 0), RvrContactSource.None, 1_000);
            table.Note(0, Albion, At(500, 0, 0), RvrContactSource.Noticed, 1_000);
            Assert.That(table.Writes, Is.EqualTo(0));
            Assert.That(table.TryGet(10, 500, 1_000, out _), Is.False);
        }

        [Test]
        public void Confidence_halves_every_thirty_seconds_and_the_contact_is_forgotten_after_two_minutes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrContacts.Confidence(0), Is.EqualTo(1.0));
                Assert.That(RvrContacts.Confidence(RvrContacts.HalfLifeMilliseconds), Is.EqualTo(0.5).Within(1e-9));
                Assert.That(RvrContacts.Confidence(2 * RvrContacts.HalfLifeMilliseconds), Is.EqualTo(0.25).Within(1e-9));
                Assert.That(RvrContacts.Confidence(RvrContacts.ForgetAfterMilliseconds + 1), Is.EqualTo(0));
            });
            var table = new RvrContacts();
            table.Note(10, Albion, At(500, 0, 0), RvrContactSource.Noticed, 1_000);
            Assert.That(table.TryGet(10, 500, 1_000 + RvrContacts.ForgetAfterMilliseconds, out _), Is.True);
            Assert.That(table.TryGet(10, 500, 1_001 + RvrContacts.ForgetAfterMilliseconds, out _), Is.False);
            // seen again after it was forgotten: a new first sight, and the old sources are gone
            long later = 2_000 + RvrContacts.ForgetAfterMilliseconds;
            table.Note(10, Albion, At(500, 0, 0), RvrContactSource.Called, later);
            table.TryGet(10, 500, later, out RvrContact again);
            Assert.That((again.FirstSeen, again.Sources), Is.EqualTo((later, RvrContactSource.Called)));
            Assert.That(again.Firsthand, Is.False, "told, not seen");
        }

        [Test]
        public void The_group_question_takes_the_freshest_contact_of_any_member()
        {
            var table = new RvrContacts();
            table.Note(10, Albion, At(500, 100, 0), RvrContactSource.Noticed, 1_000);
            table.Note(11, Albion, At(500, 900, 0), RvrContactSource.Called, 3_000);
            Assert.Multiple(() =>
            {
                Assert.That(table.KnownByAny(new long[] { 10, 11, 12 }, 500, 4_000, out RvrContact freshest), Is.True);
                Assert.That((freshest.Owner, freshest.X), Is.EqualTo((11L, 900)));
                Assert.That(table.KnownByAny(new long[] { 12, 13 }, 500, 4_000, out _), Is.False);
                Assert.That(table.KnownByAny(new long[] { 10 }, 500, 4_000, out _, maxAge: 2_000), Is.False, "too old for the asker");
                Assert.That(table.KnownByAny(null, 500, 4_000, out _), Is.False);
            });
        }

        [Test]
        public void A_bots_contacts_are_filtered_by_age()
        {
            var table = new RvrContacts();
            table.Note(10, Albion, At(500, 0, 0), RvrContactSource.Noticed, 1_000);
            table.Note(10, Albion, At(501, 0, 0), RvrContactSource.Noticed, 9_000);
            Assert.That(table.Of(10, 10_000).Select(c => c.Target), Is.EquivalentTo(new long[] { 500, 501 }));
            Assert.That(table.Of(10, 10_000, maxAge: 3_000).Select(c => c.Target), Is.EquivalentTo(new long[] { 501 }));
            Assert.That(table.Of(99, 10_000), Is.Empty);
        }

        [Test]
        public void Proposals_put_what_the_bot_saw_itself_first_then_the_freshest_then_the_nearest()
        {
            var table = new RvrContacts();
            table.Note(10, Albion, At(1, 2_000, 0), RvrContactSource.Called, 9_000);      // told, freshest
            table.Note(10, Albion, At(2, 1_500, 0), RvrContactSource.Noticed, 5_000);     // seen, older
            table.Note(10, Albion, At(3, 500, 0), RvrContactSource.Noticed, 8_000);       // seen, fresher
            table.Note(10, Albion, At(4, 100, 0), RvrContactSource.Noticed, 8_000);       // seen, as fresh, nearer
            table.Note(10, Albion, At(5, 0, 0, region: 1), RvrContactSource.Noticed, 9_000); // another region
            List<RvrContact> picks = table.Proposals(10, Emain, 0, 0, 10_000, maxAge: 30_000);
            Assert.That(picks.Select(c => c.Target), Is.EqualTo(new long[] { 4, 3, 2, 1 }));
            Assert.That(table.Proposals(10, Emain, 0, 0, 10_000, maxAge: 3_000).Select(c => c.Target), Is.EqualTo(new long[] { 4, 3, 1 }));
        }

        [Test]
        public void The_realm_union_is_one_row_per_enemy_with_its_latest_place_and_every_way_it_was_known()
        {
            var table = new RvrContacts();
            table.Note(10, Albion, At(500, 100, 0), RvrContactSource.Noticed, 1_000);
            table.Note(11, Albion, At(500, 700, 0), RvrContactSource.Called, 2_000);
            table.Note(20, Hibernia, At(500, 50, 0), RvrContactSource.Hit, 3_000);
            table.Note(10, Albion, At(600, 0, 0, region: 1), RvrContactSource.Noticed, 3_000);
            List<RvrContact> albion = table.Realm(Albion, Emain, 4_000, maxAge: 60_000);
            Assert.That(albion, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(albion[0].Target, Is.EqualTo(500));
                Assert.That((albion[0].X, albion[0].LastSeen, albion[0].FirstSeen), Is.EqualTo((700, 2_000L, 1_000L)));
                Assert.That(albion[0].Sources, Is.EqualTo(RvrContactSource.Noticed | RvrContactSource.Called));
                Assert.That(albion[0].Owner, Is.EqualTo(0));
                Assert.That(table.Realm(Hibernia, Emain, 4_000, 60_000).Single().Sources, Is.EqualTo(RvrContactSource.Hit));
                Assert.That(table.Realm(Albion, Emain, 4_000, maxAge: 1_000), Is.Empty);
                Assert.That(table.Realm(Midgard, Emain, 4_000, 60_000), Is.Empty, "the player is invisible to nobody but nobody saw him");
            });
        }

        [Test]
        public void Near_counts_heads_players_and_parties_a_realm_has_seen_close_to_a_keep()
        {
            var table = new RvrContacts();
            table.Note(10, Albion, At(1, 1_000, 0, kind: RvrPerceivedKind.Player, party: 77), RvrContactSource.Noticed, 1_000);
            table.Note(10, Albion, At(2, 1_200, 0, party: 77), RvrContactSource.Noticed, 1_000);
            table.Note(11, Albion, At(3, 1_500, 0, party: 88), RvrContactSource.Called, 2_000);
            table.Note(11, Albion, At(4, 1_100, 0, kind: RvrPerceivedKind.EnemyBotPet, party: 88), RvrContactSource.Noticed, 2_000);
            table.Note(11, Albion, At(5, 9_000, 0), RvrContactSource.Noticed, 2_000);                // too far
            table.Note(11, Albion, At(6, 1_000, 0, realm: Hibernia), RvrContactSource.Noticed, 2_000);
            RvrContactCount mids = table.Near(Albion, Midgard, Emain, 0, 0, 2_000, 3_000, maxAge: 60_000);
            Assert.That(mids, Is.EqualTo(new RvrContactCount(3, 1, 2, 2_000)));
            Assert.That(table.Near(Albion, 0, Emain, 0, 0, 2_000, 3_000, 60_000).Heads, Is.EqualTo(4), "every enemy realm");
            Assert.That(table.Near(Albion, Midgard, Emain, 0, 0, 2_000, 3_000, maxAge: 1_500).Heads, Is.EqualTo(1), "only the fresh one");
            Assert.That(table.Near(Midgard, Albion, Emain, 0, 0, 2_000, 3_000, 60_000).Heads, Is.EqualTo(0));
        }

        [Test]
        public void A_scan_result_maps_to_one_source()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrContacts.SourceOf(RvrScanResult.Noticed, RvrNoticeFactors.Close), Is.EqualTo(RvrContactSource.Noticed));
                Assert.That(RvrContacts.SourceOf(RvrScanResult.Engage, RvrNoticeFactors.None), Is.EqualTo(RvrContactSource.Noticed));
                Assert.That(RvrContacts.SourceOf(RvrScanResult.Engage, RvrNoticeFactors.Alerted), Is.EqualTo(RvrContactSource.Called));
                Assert.That(RvrContacts.SourceOf(RvrScanResult.Reacting, RvrNoticeFactors.Alerted), Is.EqualTo(RvrContactSource.Called));
                Assert.That(RvrContacts.SourceOf(RvrScanResult.Reacting, RvrNoticeFactors.Stealth), Is.EqualTo(RvrContactSource.Noticed));
                Assert.That(RvrContacts.SourceOf(RvrScanResult.Unseen, RvrNoticeFactors.Facing), Is.EqualTo(RvrContactSource.None));
                Assert.That(RvrContacts.SourceOf(RvrScanResult.Capped, RvrNoticeFactors.None), Is.EqualTo(RvrContactSource.None));
                Assert.That(RvrContacts.SourceWithoutRule(true, false), Is.EqualTo(RvrContactSource.Attacked));
                Assert.That(RvrContacts.SourceWithoutRule(false, true), Is.EqualTo(RvrContactSource.Attacked));
                Assert.That(RvrContacts.SourceWithoutRule(false, false), Is.EqualTo(RvrContactSource.Acquired));
            });
        }

        [Test]
        public void The_table_takes_parallel_writers()
        {
            var table = new RvrContacts();
            System.Threading.Tasks.Parallel.For(0, 4_000, i =>
                table.Note(1 + i % 40, Albion, At(1_000 + i % 25, i, 0), RvrContactSource.Noticed, 1_000 + i % 7));
            Assert.That(table.Writes, Is.EqualTo(4_000));
            Assert.That(table.Owners, Is.EqualTo(40));
            Assert.That(table.Realm(Albion, Emain, 2_000, 60_000), Has.Count.EqualTo(25));
        }

        [Test]
        public void The_sweep_forgets_old_rows_and_drops_empty_tables()
        {
            var table = new RvrContacts();
            table.Note(10, Albion, At(500, 0, 0), RvrContactSource.Noticed, 1_000);
            table.Note(10, Albion, At(501, 0, 0), RvrContactSource.Noticed, 1_000);
            table.Note(11, Albion, At(500, 0, 0), RvrContactSource.Called, 60_000);
            Assert.That((table.Owners, table.Count, table.RealmRows), Is.EqualTo((2, 5L, 2)));
            // the next write after the forget time sweeps: bot 10's rows are gone, its table too; bot 11's stays
            long later = 1_000 + RvrContacts.ForgetAfterMilliseconds + 20_000;
            table.Note(12, Albion, At(600, 0, 0), RvrContactSource.Noticed, later);
            Assert.Multiple(() =>
            {
                Assert.That(table.Owners, Is.EqualTo(2), "bots 11 and 12");
                Assert.That(table.Of(10, later), Is.Empty);
                Assert.That(table.TryGet(11, 500, later, out _), Is.True);
                Assert.That(table.RealmRows, Is.EqualTo(2), "500 (seen by 11 at 60 s) and 600");
                Assert.That(table.Count, Is.EqualTo(4L));
            });
            table.Forget(11);
            Assert.That(table.Owners, Is.EqualTo(1));
        }

        [Test]
        public void A_write_racing_the_sweep_is_never_lost()
        {
            // Bot 10's only row is old; bot 20's write triggers the sweep that drops bot 10's table, while bot 10 writes again.
            long old = 1_000, now = 1_000 + RvrContacts.ForgetAfterMilliseconds + 20_000;
            for (int round = 0; round < 500; round++)
            {
                var table = new RvrContacts();
                table.Note(10, Albion, At(500, 0, 0), RvrContactSource.Noticed, old);
                table.Note(30, Albion, At(700, 0, 0), RvrContactSource.Noticed, old);
                System.Threading.Tasks.Parallel.Invoke(
                    () => table.Note(10, Albion, At(501, 0, 0), RvrContactSource.Noticed, now),
                    () => table.Note(20, Albion, At(502, 0, 0), RvrContactSource.Noticed, now),
                    () => table.Note(30, Albion, At(700, 0, 0), RvrContactSource.Noticed, now));
                Assert.That(table.TryGet(10, 501, now, out _), Is.True, $"round {round}: bot 10's new write");
                Assert.That(table.TryGet(30, 700, now, out _), Is.True, $"round {round}: bot 30's refreshed row");
                Assert.That(table.Realm(Albion, Emain, now, 1_000).Select(c => c.Target), Is.EquivalentTo(new long[] { 501, 502, 700 }),
                    $"round {round}: the realm union");
            }
        }

        [Test]
        public void The_alarm_hears_fresh_contacts_of_living_visible_enemies_in_the_bots_region_only()
        {
            const long now = 10_000;
            var contacts = new List<RvrContact>();
            var subjects = new Dictionary<long, RvrAlarmSubject?>();
            void Add(long target, long lastSeen, RvrAlarmSubject? subject)
            {
                contacts.Add(new RvrContact(10, Albion, target, target, Midgard, RvrPerceivedKind.Player, Emain, 0, 0, 0, lastSeen, lastSeen,
                    RvrContactSource.Noticed, RvrContactSource.Noticed, null));
                subjects[target] = subject;
            }
            RvrAlarmSubject Ok = new(true, true, false, Emain, Midgard);
            Add(1, now, Ok);                                                  // heard
            Add(2, now - RvrContacts.AlarmHearsMilliseconds, Ok);              // heard: exactly 3 s
            Add(3, now - RvrContacts.AlarmHearsMilliseconds - 1, Ok);          // too old
            Add(4, now, Ok with { Stealthed = true });                         // a stealther is never called
            Add(5, now, Ok with { Alive = false });                            // dead
            Add(6, now, Ok with { Active = false });                           // left the world
            Add(7, now, Ok with { Region = 1 });                               // another region
            Add(8, now, Ok with { Realm = Albion });                           // now of the bot's realm
            Add(9, now, null);                                                 // gone
            List<RvrContact> heard = RvrContacts.AlarmHeard(contacts, now, Emain, Albion, c => subjects[c.Target]);
            Assert.That(heard.Select(c => c.Target), Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(RvrContacts.AlarmHeard(null, now, Emain, Albion, c => Ok), Is.Empty);
        }

        // ---- step 2: the shadow ----------------------------------------------------------------------------------------

        private static RvrShadowListing L(long target, bool known, bool player = false, bool stale = false) =>
            new(target, known, stale, player, 1_000);

        [Test]
        public void The_shadow_counts_enemy_seconds_and_pairs_a_pair_known_once_in_the_window_is_known()
        {
            var shadow = new RvrContactShadow { TrackPairs = true };
            shadow.Board(Albion, 1, 0, new[] { L(500, false), L(501, false, player: true), L(502, true, stale: true) }, out _);
            shadow.Board(Albion, 1, 1_000, new[] { L(500, true), L(501, false, player: true) }, out _);
            shadow.Board(Albion, 2, 1_000, new[] { L(500, false) }, out _); // another group never knew it
            RvrShadowTotals t = shadow.Totals(Albion);
            Assert.Multiple(() =>
            {
                Assert.That((t.Listed, t.Known, t.Stale, t.Unknown, t.UnknownPlayers), Is.EqualTo((6L, 2L, 1L, 4L, 2L)));
                Assert.That((t.Pairs, t.PairsUnknown, t.PairsUnknownPlayers), Is.EqualTo((4, 2, 1)));
                Assert.That(t.PairsUnknownShare, Is.EqualTo(0.5));
                Assert.That(t.UnknownShare, Is.EqualTo(4 / 6.0).Within(1e-9));
                Assert.That(shadow.Totals(Midgard), Is.EqualTo(default(RvrShadowTotals)));
            });
        }

        [Test]
        public void The_shadow_logs_once_a_minute_per_realm_and_samples_the_first_unknown_listing_of_a_pair()
        {
            var shadow = new RvrContactShadow();
            List<RvrShadowListing> first = shadow.Board(Midgard, 1, 0, new[] { L(500, false), L(501, true) }, out List<string> none);
            List<RvrShadowListing> again = shadow.Board(Midgard, 1, 1_000, new[] { L(500, false) }, out _);
            Assert.Multiple(() =>
            {
                Assert.That(none, Is.Null);
                Assert.That(first.Select(s => s.Target), Is.EqualTo(new long[] { 500 }));
                Assert.That(again, Is.Null, "a pair is sampled once a window");
                Assert.That(shadow.TakeLines(59_999), Is.Null);
            });
            List<string> lines = shadow.TakeLines(RvrContactShadow.WindowMilliseconds);
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines[0], Does.StartWith("RVR_CONTACT_SHADOW realm=Midgard windowS=60 listed=3 known=1 stale=0 unknown=2 "));
            Assert.That(lines[0], Does.Contain("pairs=2 pairsUnknown=1 pairsUnknownPlayers=0 pairsUnknownShare=0.5"));
            Assert.That(shadow.TakeLines(RvrContactShadow.WindowMilliseconds + 1), Is.Null, "the window was closed");
            // the next window samples the pair again
            Assert.That(shadow.Board(Midgard, 1, 61_000, new[] { L(500, false) }, out _), Has.Count.EqualTo(1));
            // without TrackPairs the totals keep enemy-seconds only
            Assert.That(shadow.Totals(Midgard).Listed, Is.EqualTo(4));
        }

        [Test]
        public void Samples_are_capped_per_realm_and_window()
        {
            var shadow = new RvrContactShadow();
            var listed = Enumerable.Range(1, 50).Select(i => L(i, false)).ToArray();
            Assert.That(shadow.Board(Albion, 1, 0, listed, out _), Has.Count.EqualTo(RvrContactShadow.SamplesPerWindow));
            Assert.That(shadow.Board(Albion, 2, 0, listed, out _), Is.Null);
            Assert.That(shadow.Board(Hibernia, 3, 0, listed, out _), Has.Count.EqualTo(RvrContactShadow.SamplesPerWindow));
        }

        // ---- the wiring (source checks: the scan, the call, the hit and the board are the only writers and readers) -----

        private static string GameServerRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return Path.Combine(dir, "source", "server", "GameServer");
        }

        private static string Code(params string[] parts) => File.ReadAllText(Path.Combine(new[] { GameServerRoot() }.Concat(parts).ToArray()));

        [Test]
        public void Only_the_perception_file_writes_contacts_in_the_game()
        {
            string root = GameServerRoot();
            string[] writers = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"RvrContactTable\.Note\("))
                .Select(Path.GetFileName).ToArray();
            Assert.That(writers, Is.EqualTo(new[] { "AutonomousWorldBotController.RvrPerception.cs" }));
            string perception = Code("bots", "autonomous", "AutonomousWorldBotController.RvrPerception.cs");
            // the scan's three paths, the stealth rule and the call write; the hit comes in from the brain
            Assert.That(Regex.Matches(perception, @"NoteRvrContact\(").Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(perception, Does.Contain("NoteRvrContact(mates[callee.Id], enemy, RvrContactSource.Called, now)"));
            Assert.That(perception, Does.Contain("IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsPlayerLedGroup: false, IsHiredCompanion: false"),
                "a hit on a companion, a helper or a bot in a player's group writes nothing");
            Assert.That(Code("bots", "BotBrain.cs"), Does.Contain("AutonomousWorldBotController.NoteRvrHit(BotBody, ad?.Attacker)"));
        }

        [Test]
        public void Only_the_scan_the_board_and_the_census_read_the_table_in_the_game()
        {
            string root = GameServerRoot();
            string[] readers = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bRvrContact(Table|ShadowTally)\b"))
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.That(readers, Is.EqualTo(new[] { "AutonomousChatFieldTalk.cs", "AutonomousWorldBotController.RvrFirstPick.cs",
                    "AutonomousWorldBotController.RvrPerception.cs", "RvrFightBoards.cs", "ServerMemoryDiagnostics.cs" }),
                "a new reader of the contact table is a new decision from it: add it here with its step (#529)");
            // #528 (#740 review): an inc call counts only the attackers its speaker knows of, from its own contacts.
            string field = Code("bots", "autonomous", "AutonomousChatFieldTalk.cs");
            Assert.That(Regex.Matches(field, @"RvrContactTable\.(Of|KnownByAny|Realm|Near|Proposals|TryGet|Knows)\(").Cast<Match>().Select(m => m.Value),
                Is.EqualTo(new[] { "RvrContactTable.Of(" }), "the inc call reads only the speaker's own contacts");
            // #531 step 1: the plan's first pick reads the bot's own proposals, nothing else
            string firstPick = Code("bots", "autonomous", "AutonomousWorldBotController.RvrFirstPick.cs");
            Assert.That(Regex.Matches(firstPick, @"RvrContactTable\.(Of|KnownByAny|Realm|Near|Proposals|TryGet|Knows|Group)\(").Cast<Match>().Select(m => m.Value),
                Is.EqualTo(new[] { "RvrContactTable.Proposals(" }), "the first pick picks from the scan's proposals (#531)");
            string perception = Code("bots", "autonomous", "AutonomousWorldBotController.RvrPerception.cs");
            Assert.That(Regex.Matches(perception, @"RvrContactTable\.(Of|KnownByAny|Realm|Near|Proposals|TryGet|Knows)\(").Cast<Match>().Select(m => m.Value),
                Is.EqualTo(new[] { "RvrContactTable.Of(" }), "the alarm is the scan's only reader in steps 1-2");
            string boards = Code("bots", "autonomous", "RvrFightBoards.cs");
            Assert.That(Regex.Matches(boards, @"contacts\.(Of|KnownByAny|Realm|Near|Proposals|TryGet|Knows)\(").Cast<Match>().Select(m => m.Value),
                Is.EqualTo(new[] { "contacts.KnownByAny(" }), "the board's shadow reads only the group question");
            Assert.That(Code("Managers", "GameLoop", "ServerMemoryDiagnostics.cs"),
                Does.Contain("RegisterCache(\"rvrContacts\", () => AutonomousWorldBotController.RvrContactTable.Count);"));
        }

        [Test]
        public void The_board_keys_an_enemy_as_perception_and_the_table_do()
        {
            Assert.That(Code("bots", "autonomous", "RvrFightBoards.cs"),
                Does.Contain("private static long CombatantKey(GameLiving living) => RvrPartyKey.TargetKey(living);"));
            Assert.That(Code("bots", "autonomous", "AutonomousWorldBotController.RvrPerception.cs"),
                Does.Contain("private static long RvrTargetKey(GameLiving target) => RvrPartyKey.TargetKey(target);"));
        }

        [Test]
        public void The_alarm_hears_contacts_not_the_area_search()
        {
            string scan = Code("bots", "autonomous", "AutonomousWorldBotController.FrontierThreat.cs");
            Assert.That(scan, Does.Not.Contain("ObserveEnemies(bot, rvr.StealthNotice ? nearby"));
            Assert.That(scan, Does.Contain("AutonomousLivingServer.ObserveEnemies(bot, RvrAlarmHears(bot, scanNow));"));
            Assert.That(scan.IndexOf("RvrAlarmHears(bot, scanNow)", StringComparison.Ordinal),
                Is.GreaterThan(scan.IndexOf("visibleTargets = PerceiveRvrTargets(bot, visibleTargets, rvr);", StringComparison.Ordinal)),
                "after the notice rule wrote this scan's contacts");
            Assert.That(Regex.Matches(scan, @"ObserveEnemies\(").Count, Is.EqualTo(1));
        }

        [Test]
        public void The_board_runs_the_shadow_and_lists_as_before()
        {
            string boards = Code("bots", "autonomous", "RvrFightBoards.cs");
            Assert.That(boards, Does.Contain("ShadowContacts(asker, members, hostile, now);"));
            Assert.That(Regex.IsMatch(boards, @"try\s*\{\s*ShadowContacts\(asker, members, hostile, now\);\s*\}\s*catch \(Exception exception\)"),
                Is.True, "the shadow cannot break the board build");
            Assert.That(boards, Does.Contain("RVR_CONTACT_UNKNOWN group={partyKey} boardGroup={boardGroup}"),
                "the sampled rows use the shadow's group key");
            Assert.That(boards, Does.Contain("RvrContactShadowTally.Board((int)asker.Realm, partyKey, now,"));
            // the area search still decides what the board lists (step 3 switches it, not this step)
            Assert.That(boards, Does.Contain("foreach (GamePlayer player in asker.GetPlayersInRadius(RvrFightBoard.ScanRadius))"));
            Assert.That(boards.IndexOf("ShadowContacts(asker, members, hostile, now);", StringComparison.Ordinal),
                Is.LessThan(boards.IndexOf("RvrFightBoard.Build(now, asker.CurrentRegionID", StringComparison.Ordinal)));
        }
    }
}
