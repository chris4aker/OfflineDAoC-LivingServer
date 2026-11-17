using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people, work package 0 (docs/BOTS-AS-PEOPLE-1.2.md, "One key for later"): the person key ("c:1234" today,
    /// "p:56" for a bot player later), its text form, and the merge rules written now so the bot-player model of 2.0 only
    /// calls them. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_BotPersonKey
    {
        [Test]
        public void A_key_is_written_and_read_back_as_text() => Assert.Multiple(() =>
        {
            Assert.That(BotPersonKey.Character(1234).ToString(), Is.EqualTo("c:1234"));
            Assert.That(BotPersonKey.Player(56).ToString(), Is.EqualTo("p:56"));
            Assert.That(BotPersonKey.Parse("c:1234"), Is.EqualTo(BotPersonKey.Character(1234)));
            Assert.That(BotPersonKey.Parse("p:56"), Is.EqualTo(BotPersonKey.Player(56)));
            Assert.That(BotPersonKey.Parse(BotPersonKey.Character(long.MaxValue).ToString()).Id, Is.EqualTo(long.MaxValue));
            Assert.That(BotPersonKey.Character(long.MaxValue).ToString().Length, Is.LessThanOrEqualTo(BotPersonKey.TextLength));
            Assert.That(BotPersonKey.None.ToString(), Is.Empty);
            Assert.That(BotPersonKey.Character(0).IsNone && BotPersonKey.Player(-3).IsNone, Is.True, "no id, no person");
            Assert.That(BotPersonKey.Character(7).IsCharacter && !BotPersonKey.Character(7).IsPlayer, Is.True);
        });

        [TestCase("")]
        [TestCase(null)]
        [TestCase("c:")]
        [TestCase("c:0")]
        [TestCase("c:-4")]
        [TestCase("C:12")]
        [TestCase("x:12")]
        [TestCase("c12")]
        [TestCase("c: 12")]
        [TestCase("c:12a")]
        [TestCase("c:+12")]
        [TestCase("c:99999999999999999999999")]
        public void Anything_else_is_not_a_key(string text)
        {
            Assert.That(BotPersonKey.TryParse(text, out BotPersonKey key), Is.False);
            Assert.That(key, Is.EqualTo(BotPersonKey.None));
            Assert.Throws<System.FormatException>(() => BotPersonKey.Parse(text));
        }

        [Test]
        public void Today_every_character_is_its_own_person()
        {
            Assert.That(BotPersonKey.Of(42), Is.EqualTo(BotPersonKey.Character(42)));
            Assert.That(BotPersonKey.Of(0).IsNone, Is.True);
            Assert.That(new[] { BotPersonKey.Player(1), BotPersonKey.Character(9), BotPersonKey.Character(2) }.OrderBy(k => k),
                Is.EqualTo(new[] { BotPersonKey.Character(2), BotPersonKey.Character(9), BotPersonKey.Player(1) }));
        }

        // ---- the merge rules -------------------------------------------------------------------------------------------

        private static readonly BotPersonKey Person = BotPersonKey.Player(5);

        private static OfflineBotSkillRecord Skill(long id, int tier, double reactions, double pulls, string updated = "2026-11-17T08:00:00Z", int realm = 1) =>
            new() { PersonKey = BotPersonKey.Of(id).ToString(), CharacterId = id, Realm = realm, Tier = tier, ReactionsXp = reactions, PullsXp = pulls, UpdatedUtc = updated };

        [Test]
        public void Skill_keeps_the_highest_experience_per_facet_and_the_highest_tier()
        {
            var merged = BotPersonKey.Merge(Person, [Skill(1, 0, 100, 5), Skill(2, 1, 20, 40, "2026-11-18T00:00:00Z", 2)]);
            Assert.That(merged.PersonKey, Is.EqualTo("p:5"));
            Assert.That((merged.ReactionsXp, merged.PullsXp, merged.Tier), Is.EqualTo((100.0, 40.0, 1)));
            Assert.That(merged.CharacterId, Is.EqualTo(1), "the main is the character with the most experience");
            Assert.That(merged.Realm, Is.EqualTo(1));
            Assert.That(merged.UpdatedUtc, Is.EqualTo("2026-11-18T00:00:00Z"), "the latest update");
            Assert.That(merged.IsPersisted, Is.False, "a new row");
            Assert.That(BotPersonKey.Merge(Person, [Skill(2, 1, 20, 40), Skill(1, 0, 100, 5)]).CharacterId, Is.EqualTo(1),
                "the order of the inputs does not matter");
            Assert.That(BotPersonKey.Merge(Person, System.Array.Empty<OfflineBotSkillRecord>()), Is.Null);
            foreach (BotSkillFacet facet in BotSkillFacets.All)
            {
                var row = new OfflineBotSkillRecord();
                row.SetExperience(facet, 3.5);
                Assert.That(row.Experience(facet), Is.EqualTo(3.5), facet.ToString());
                Assert.That(row.TotalExperience, Is.EqualTo(3.5), facet + " is one column");
            }
        }

        private static OfflineBotPlacesRecord Places(long id, int realm, string known, string half, string updated = "2026-11-17T08:00:00Z") =>
            new() { PersonKey = BotPersonKey.Of(id).ToString(), CharacterId = id, Realm = realm, Known = known, HalfKnown = half, UpdatedUtc = updated };

        [Test]
        public void Places_are_the_union_per_realm_and_half_known_only_where_none_knows()
        {
            string a = BotPeopleBits.With(BotPeopleBits.With("", 0), 9);    // places 0 and 9
            string b = BotPeopleBits.With("", 3);                            // place 3
            string halfA = BotPeopleBits.With("", 3);                        // A was told the way to 3, which B knows
            string halfB = BotPeopleBits.With("", 20);
            var merged = BotPersonKey.Merge(Person, [Places(1, 1, a, halfA), Places(2, 1, b, halfB, "2026-11-18T00:00:00Z"), Places(2, 2, b, "")]);
            Assert.That(merged.Select(r => r.Realm), Is.EqualTo(new[] { 1, 2 }), "one row per realm");
            var albion = merged[0];
            Assert.That(BotPeopleBits.Indexes(albion.Known), Is.EqualTo(new[] { 0, 3, 9 }));
            Assert.That(BotPeopleBits.Indexes(albion.HalfKnown), Is.EqualTo(new[] { 20 }), "3 is known, so no longer half known");
            Assert.That(albion.CharacterId, Is.EqualTo(2), "the latest update is the main");
            Assert.That(albion.PersonKey, Is.EqualTo("p:5"));
            Assert.That(BotPeopleBits.Indexes(merged[1].Known), Is.EqualTo(new[] { 3 }), "another realm's map stays its own");
        }

        [Test]
        public void Mechanics_seen_are_summed()
        {
            OfflineBotMechanicSeenRecord Seen(long id, string mechanic, int seen, string last) =>
                new() { PersonKey = BotPersonKey.Of(id).ToString(), CharacterId = id, Mechanic = mechanic, Seen = seen, LastSeenUtc = last };
            var merged = BotPersonKey.Merge(Person, [Seen(1, "Xanxicar/blast", 3, "2026-11-01"), Seen(2, "Xanxicar/blast", 4, "2026-11-10"),
                Seen(2, "Legion/blast", 1, "2026-11-02"), Seen(3, "Legion/blast", int.MaxValue, "2026-11-03")]);
            Assert.That(merged.Select(r => (r.Mechanic, r.Seen)), Is.EqualTo(new[] { ("Legion/blast", int.MaxValue), ("Xanxicar/blast", 7) }));
            Assert.That(merged[1].LastSeenUtc, Is.EqualTo("2026-11-10"));
            Assert.That(merged.All(r => r.PersonKey == "p:5"), Is.True);
        }

        [Test]
        public void Milestones_stay_per_character_and_are_rekeyed_to_the_person()
        {
            OfflineBotMilestoneRecord Arc(long id, string arc, string state, string started = "2026-11-01") =>
                new() { PersonKey = BotPersonKey.Of(id).ToString(), CharacterId = id, Arc = arc, State = state, Name = "Bot" + id, StartedUtc = started };
            var merged = BotPersonKey.Merge(Person, [Arc(1, "ding50", OfflineBotMilestoneRecord.Reached), Arc(2, "ding50", OfflineBotMilestoneRecord.Active),
                Arc(2, "rr5", OfflineBotMilestoneRecord.Dropped), Arc(2, "rr5", OfflineBotMilestoneRecord.Active, "2026-11-05")]);
            Assert.That(merged.Select(r => (r.CharacterId, r.Arc, r.State)), Is.EqualTo(new[]
            {
                (1L, "ding50", OfflineBotMilestoneRecord.Reached),
                (2L, "ding50", OfflineBotMilestoneRecord.Active),
                (2L, "rr5", OfflineBotMilestoneRecord.Active),
            }), "two alts can both chase 50; a duplicate keeps the furthest");
            Assert.That(merged.All(r => r.PersonKey == "p:5" && !r.IsPersisted), Is.True);
            Assert.That(merged[0].Name, Is.EqualTo("Bot1"), "the character's own name stays");
        }

        // ---- the bit sets ----------------------------------------------------------------------------------------------

        [Test]
        public void Bit_sets_are_short_hex_text() => Assert.Multiple(() =>
        {
            Assert.That(BotPeopleBits.With("", 0), Is.EqualTo("01"));
            Assert.That(BotPeopleBits.With("", 9), Is.EqualTo("0002"));
            Assert.That(BotPeopleBits.With("01", 7), Is.EqualTo("81"));
            Assert.That(BotPeopleBits.Has("0002", 9) && !BotPeopleBits.Has("0002", 8) && !BotPeopleBits.Has("0002", 400), Is.True);
            Assert.That(BotPeopleBits.Without("0002", 9), Is.Empty, "trailing zero bytes are left out");
            Assert.That(BotPeopleBits.Union("01", "0002"), Is.EqualTo("0102"));
            Assert.That(BotPeopleBits.Except("0302", "01"), Is.EqualTo("0202"));
            Assert.That(BotPeopleBits.Count("ff01"), Is.EqualTo(9));
            Assert.That(BotPeopleBits.Normalize("FF00"), Is.EqualTo("ff"));
            Assert.That(BotPeopleBits.Normalize("zz") + BotPeopleBits.Normalize("abc") + BotPeopleBits.Normalize(null), Is.Empty,
                "unreadable text is the empty set");
            Assert.That(BotPeopleBits.With("", BotPeopleBits.MaxBit).Length, Is.EqualTo(128));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => BotPeopleBits.With("", BotPeopleBits.MaxBit + 1));
        });
    }
}
