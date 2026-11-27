using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS;
using DOL.GS.PacketHandler;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;

namespace DOL.UnitTests
{
    /// <summary>
    /// #528, RvR work 10 "after the kill" (docs/rvr-audit-1.2.3/war.md findings 5, 7, 10; roam.md finding 9): the move-on
    /// counts the player's death, inc calls name the siege's real attacker and a fuzzed real count, a killer emotes over a
    /// corpse at most once, and the hourly RVR_RANKS histogram. Pure rules; the wiring is the kill event in
    /// AutonomousBotRealmPointRewards (Award, PayGamebotsForPlayerKill). Tests only until a PC log shows it.
    /// </summary>
    [TestFixture]
    public class UT_RvrAfterTheKill
    {
        private const long Minute = 60_000;

        private static RvrCheckSpotCatalog Catalog()
        {
            var assembly = typeof(RvrCheckSpots).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("rvr-checkspots.json", StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            return RvrCheckSpotCatalog.Parse(reader.ReadToEnd());
        }

        // ---- 1. the move-on counts the player ----------------------------------------------------------------------------

        [Test]
        public void A_player_death_at_a_check_spot_starts_the_move_on()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrFrontierPoi amg = catalog.Spot("emain-amg");
            Assert.That(amg, Is.Not.Null);
            long moveOn = 3 * Minute;
            // A Hibernian stealther camping AMG (4-8 minutes) with no gamebot prey in view: the player is never tracked prey.
            RvrRoamWalk walk = RvrRoamCircuits.BeginSolo(amg, catalog, eRealm.Hibernia, "hib-stealther", true, moveOn, 0);
            while (walk.CurrentSpot != amg.Id)
                walk = RvrRoamCircuits.Advance(walk);
            RvrRoamStep arrived = RvrRoamCircuits.Step(walk, 1_000, true, false);
            Assert.That(arrived.Action, Is.EqualTo(RvrRoamAction.Check));
            walk = arrived.Walk;
            Assert.That(walk.DwellMilliseconds, Is.GreaterThan(moveOn + 30_000), "a stealther's dwell outlasts the move-on");

            long killAt = walk.ArrivedAt + 30_000;
            Assert.That(RvrRoamCircuits.CountsAsSpotKill(killAt, walk.ArrivedAt, 200f, amg.Radius), Is.True, "the player fell at the gate");
            RvrRoamWalk killed = RvrRoamCircuits.RecordKill(walk, killAt);

            long due = killAt + moveOn;
            Assert.That(RvrRoamCircuits.Step(killed, due, true, false).Action, Is.EqualTo(RvrRoamAction.MoveOn).Or.EqualTo(RvrRoamAction.Done),
                "it moves on within the move-on time of killing him");
            Assert.That(RvrRoamCircuits.Step(walk, due, true, false).Action, Is.EqualTo(RvrRoamAction.Check),
                "before #528 the same stealther kept camping the gate");
        }

        [Test]
        public void Only_a_death_at_the_spot_after_arrival_counts()
        {
            const float radius = 1_200;
            Assert.That(RvrRoamCircuits.CountsAsSpotKill(5_000, 0, 100f, radius), Is.False, "on the way there");
            Assert.That(RvrRoamCircuits.CountsAsSpotKill(5_000, 6_000, 100f, radius), Is.False, "before it arrived");
            Assert.That(RvrRoamCircuits.CountsAsSpotKill(7_000, 6_000, null, radius), Is.False, "another region");
            Assert.That(RvrRoamCircuits.CountsAsSpotKill(7_000, 6_000, radius + RvrRoamCircuits.KillReach + 1, radius), Is.False, "far from the spot");
            Assert.That(RvrRoamCircuits.CountsAsSpotKill(7_000, 6_000, radius + RvrRoamCircuits.KillReach, radius), Is.True, "a short chase past the spot");
        }

        // ---- 2. true inc calls --------------------------------------------------------------------------------------------

        private static Field.Facts Speaker(Field.WorldView world, string[] saw) =>
            new Field.Facts(4242, eRealm.Midgard, "Berserker", 50, "Odin's Gate", null, true, false, false, false, 1, false, false, false, false,
                null, null, 0, false, null, 3, 20, false, world) { SawAttacks = saw };

        private static List<Field.Said> Incs(Field.Facts facts, int draws = 3_000, int seed = 7)
        {
            var random = new Random(seed);
            var lines = new List<Field.Said>();
            for (int i = 0; i < draws; i++)
                if (Field.Line("faction", facts, null, random) is { Kind: "inc" } said)
                    lines.Add(said);
            return lines;
        }

        private static readonly Field.WorldView Sieged = new Field.WorldView(["Bledmeer Faste", "Glenlock Faste"], ["Bledmeer Faste"], [], [], 0, 0,
            "Odin's Gate") { Attacks = [new("Bledmeer Faste", eRealm.Hibernia, 20, 100, 50_000, 50_000)] };

        [Test]
        public void An_inc_names_the_sieges_attacker_and_a_count_near_the_real_one()
        {
            List<Field.Said> incs = Incs(Speaker(Sieged, ["Bledmeer Faste"]));
            Assert.That(incs, Is.Not.Empty);
            Assert.That(incs.Any(said => Regex.IsMatch(said.Text, @"\bhibs?\b")), Is.True, "the attacker is named");
            Assert.That(incs.Any(said => Regex.IsMatch(said.Text, @"\balbs?\b")), Is.False, "never the realm that is not there");
            int[] counts = incs.Select(said => Regex.Match(said.Text, @"\b(\d+)\b")).Where(match => match.Success)
                .Select(match => int.Parse(match.Groups[1].Value)).ToArray();
            Assert.That(counts, Is.Not.Empty);
            Assert.That(counts, Is.All.InRange(14, 26), "20 attackers, give or take 30%");
            Assert.That(counts.Distinct().Count(), Is.GreaterThan(1), "a little wrong, not the same every time");
        }

        [Test]
        public void A_bot_that_did_not_see_the_enemy_names_no_realm_or_count()
        {
            foreach (string[] saw in new[] { null, Array.Empty<string>() })
            {
                List<Field.Said> incs = Incs(Speaker(Sieged, saw));
                Assert.That(incs, Is.Not.Empty, "the realm still hears the keep is hit");
                Assert.That(incs.All(said => said.Text.Contains("bled")), Is.True);
                Assert.That(incs.Any(said => Regex.IsMatch(said.Text, @"\b(hibs?|albs?|zerg|rams)\b|\d")), Is.False,
                    "who and how many only from one who saw them");
            }
            // A keep in combat with no siege event: its attackers are unknown even to a bot standing there.
            var unknown = Sieged with { Attacks = [new("Bledmeer Faste", eRealm.None, 0, 100, 50_000, 50_000)] };
            Assert.That(Incs(Speaker(unknown, ["Bledmeer Faste"])).Any(said => Regex.IsMatch(said.Text, @"\b(hibs?|albs?)\b|\d")), Is.False);
        }

        [Test]
        public void Inc_counts_and_sight_are_pure_rules()
        {
            Assert.That(Field.IncCount(0, 0.5), Is.Zero);
            Assert.That(Field.IncCount(10, 0), Is.EqualTo(7));
            Assert.That(Field.IncCount(10, 0.999), Is.EqualTo(13));
            Assert.That(Field.IncCount(1, 0), Is.EqualTo(1), "at least one");
            var attacks = new[] { new Field.KeepAttack("A", eRealm.Albion, 5, 100, 0, 0), new Field.KeepAttack("B", eRealm.Albion, 5, 200, 0, 0) };
            Assert.That(Field.SeenAttacks(attacks, 100, Field.IncSightRadius, 0), Is.EqualTo(new[] { "A" }));
            Assert.That(Field.SeenAttacks(attacks, 100, Field.IncSightRadius + 1, 0), Is.Empty);
            Assert.That(Field.SeenAttacks(null, 100, 0, 0), Is.Empty);
        }

        // ---- 3. kill emotes -----------------------------------------------------------------------------------------------

        [Test]
        public void A_corpse_gets_at_most_one_emote()
        {
            var claims = new RvrAfterTheKill.CorpseClaims();
            object corpse = new(), other = new();
            Assert.That(claims.TryClaim(corpse, 10_000), Is.True);
            Assert.That(claims.TryClaim(corpse, 10_000), Is.False, "the same death");
            Assert.That(claims.TryClaim(corpse, 10_000 + RvrAfterTheKill.CorpseMilliseconds - 1), Is.False, "the same corpse");
            Assert.That(claims.TryClaim(other, 10_000), Is.True, "another corpse");
            Assert.That(claims.TryClaim(corpse, 10_000 + 5 * Minute), Is.True, "a later death is a new corpse");
            Assert.That(claims.TryClaim(null, 0), Is.False);
        }

        [Test]
        public void Kill_emotes_are_varied_taunts_with_a_bow_after_a_duel()
        {
            Assert.That(RvrAfterTheKill.Choose(false, RvrAfterTheKill.EmoteChance, 0), Is.Null, "with some chance only");
            var taunts = new HashSet<eEmote>();
            int bows = 0, duels = 0;
            for (int i = 0; i < 100; i++)
            {
                double pick = i / 100.0;
                eEmote? zerg = RvrAfterTheKill.Choose(false, 0, pick);
                Assert.That(zerg, Is.Not.Null.And.Not.EqualTo(eEmote.Bow), "no bow after a gank");
                taunts.Add(zerg!.Value);
                eEmote? duel = RvrAfterTheKill.Choose(true, 0, pick);
                duels++;
                if (duel == eEmote.Bow) bows++;
            }
            Assert.That(taunts, Does.Contain(eEmote.Laugh).And.Contain(eEmote.Rude));
            Assert.That(taunts.Count, Is.GreaterThanOrEqualTo(4), "not the same one every time");
            Assert.That(bows / (double)duels, Is.EqualTo(RvrAfterTheKill.DuelBowShare).Within(0.02), "mostly a bow after a one-on-one");
        }

        // ---- 4. the realm rank histogram ----------------------------------------------------------------------------------

        [Test]
        public void Rvr_ranks_counts_the_realm_by_rank()
        {
            Assert.That(RvrRecordPulse.RankOf(0), Is.EqualTo(1));
            Assert.That(RvrRecordPulse.RankOf(14), Is.EqualTo(2));
            Assert.That(RvrRecordPulse.RankOf(130), Is.EqualTo(RvrRecordPulse.TopRank), "capped");
            Assert.That(RvrRecordPulse.RankText(43), Is.EqualTo("5L3"));
            string line = RvrRecordPulse.RanksLine(99, eRealm.Albion, [0, 3, 14, 25, 25, 43], [25, 43]);
            Assert.That(line, Does.StartWith("RVR_RANKS t=99 realm="));
            Assert.That(line, Does.Contain(" bots=6 rr1=2 rr2=1 rr3=2 rr4=0 rr5=1 rr6=0"));
            Assert.That(line, Does.Contain(" median=2L4 level50=2 median50=3L5 top=5L3"));
            Assert.That(RvrRecordPulse.RanksLine(1, eRealm.Midgard, [], []), Does.Contain("bots=0").And.Contain("median=- level50=0 median50=- top=-"));
        }
    }
}
