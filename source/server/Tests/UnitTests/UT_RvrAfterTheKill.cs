using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
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

        private static Field.Facts Speaker(Field.WorldView world, Field.SeenAttack[] saw) =>
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
            List<Field.Said> incs = Incs(Speaker(Sieged, [new("Bledmeer Faste", 20)]));
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
            foreach (Field.SeenAttack[] saw in new[] { null, Array.Empty<Field.SeenAttack>() })
            {
                List<Field.Said> incs = Incs(Speaker(Sieged, saw));
                Assert.That(incs, Is.Not.Empty, "the realm still hears the keep is hit");
                Assert.That(incs.All(said => said.Text.Contains("bled")), Is.True);
                Assert.That(incs.Any(said => Regex.IsMatch(said.Text, @"\b(hibs?|albs?|zerg|rams)\b|\d")), Is.False,
                    "who and how many only from one who saw them");
            }
            // A keep in combat with no siege event: its attackers are unknown even to a bot standing there.
            var unknown = Sieged with { Attacks = [new("Bledmeer Faste", eRealm.None, 0, 100, 50_000, 50_000)] };
            Assert.That(Incs(Speaker(unknown, [new("Bledmeer Faste", 4)])).Any(said => Regex.IsMatch(said.Text, @"\b(hibs?|albs?)\b|\d")), Is.False);
        }

        [Test]
        public void An_inc_counts_only_the_attackers_the_bot_knows_of()
        {
            // The event counts 20 at the keep, but this bot has seen 5 of them: it calls about 5, and no zerg.
            List<Field.Said> incs = Incs(Speaker(Sieged, [new("Bledmeer Faste", 5)]));
            int[] counts = incs.Select(said => Regex.Match(said.Text, @"\b(\d+)\b")).Where(match => match.Success)
                .Select(match => int.Parse(match.Groups[1].Value)).ToArray();
            Assert.That(counts, Is.Not.Empty);
            Assert.That(counts, Is.All.InRange(3, 7), "5 known, give or take 30%");
            Assert.That(incs.Any(said => said.Text.Contains("zerg")), Is.False, "a handful is no zerg");
            // Knowing more than the event counted: the event's count caps it.
            int[] capped = Incs(Speaker(Sieged, [new("Bledmeer Faste", 60)])).Select(said => Regex.Match(said.Text, @"\b(\d+)\b"))
                .Where(match => match.Success).Select(match => int.Parse(match.Groups[1].Value)).ToArray();
            Assert.That(capped, Is.All.InRange(14, 26));
        }

        [Test]
        public void Rams_are_called_only_when_a_ram_is_at_the_keep()
        {
            Field.SeenAttack[] saw = [new("Bledmeer Faste", 20)];
            Assert.That(Incs(Speaker(Sieged, saw)).Any(said => said.Text.Contains("rams")), Is.False, "no ram there");
            var rammed = Sieged with { Attacks = [new("Bledmeer Faste", eRealm.Hibernia, 20, 100, 50_000, 50_000, Rams: true)] };
            Assert.That(Incs(Speaker(rammed, saw)).Any(said => said.Text.Contains("rams on")), Is.True, "a ram at the doors");
            Assert.That(Incs(Speaker(rammed, null)).Any(said => said.Text.Contains("rams")), Is.False, "only one who saw it");
        }

        private static RvrContact Contact(long target, eRealm realm, RvrPerceivedKind kind, ushort region, int x, int y) =>
            new(1, (int)eRealm.Midgard, target, target, (int)realm, kind, region, x, y, 0, 0, 0, RvrContactSource.Noticed,
                RvrContactSource.Noticed, null);

        [Test]
        public void Inc_counts_and_sight_are_pure_rules()
        {
            Assert.That(Field.IncCount(0, 0.5), Is.Zero);
            Assert.That(Field.IncCount(10, 0), Is.EqualTo(7));
            Assert.That(Field.IncCount(10, 0.999), Is.EqualTo(13));
            Assert.That(Field.IncCount(1, 0), Is.EqualTo(1), "at least one");
            Assert.That(Field.IncReal(20, 5), Is.EqualTo(5), "what the bot knows");
            Assert.That(Field.IncReal(3, 5), Is.EqualTo(3), "never more than the event counted");
            Assert.That(Field.IncReal(0, 5), Is.EqualTo(5), "the event counted none: what the bot knows");
            Assert.That(Field.IncReal(20, 0), Is.Zero, "the bot knows none: no count");

            var attacks = new[] { new Field.KeepAttack("A", eRealm.Albion, 5, 100, 0, 0), new Field.KeepAttack("B", eRealm.Albion, 5, 200, 0, 0) };
            RvrContact[] contacts =
            [
                Contact(1, eRealm.Albion, RvrPerceivedKind.Player, 100, 100, 0),
                Contact(2, eRealm.Albion, RvrPerceivedKind.EnemyBot, 100, 0, 200),
                Contact(3, eRealm.Albion, RvrPerceivedKind.PlayerPet, 100, 0, 0),        // a pet is no head
                Contact(4, eRealm.Hibernia, RvrPerceivedKind.Player, 100, 0, 0),         // another realm
                Contact(5, eRealm.Albion, RvrPerceivedKind.Player, 101, 0, 0),           // another region
                Contact(6, eRealm.Albion, RvrPerceivedKind.Player, 100, Field.IncSightRadius + 1, 0), // far from the keep
            ];
            Assert.That(Field.KnownAttackers(attacks[0], contacts), Is.EqualTo(2));
            Assert.That(Field.SeenAttacks(attacks, 100, Field.IncSightRadius, 0, contacts), Is.EqualTo(new[] { new Field.SeenAttack("A", 2) }));
            Assert.That(Field.SeenAttacks(attacks, 100, Field.IncSightRadius + 1, 0, contacts), Is.Empty, "too far to see the keep");
            Assert.That(Field.SeenAttacks(attacks, 100, 0, 0, []), Is.Empty, "near the keep, but it saw none of them");
            Assert.That(Field.SeenAttacks(attacks, 100, 0, 0, [Contact(4, eRealm.Hibernia, RvrPerceivedKind.Player, 100, 0, 0)]), Is.Empty,
                "only the attacking realm counts");
            Assert.That(Field.SeenAttacks(null, 100, 0, 0, contacts), Is.Empty);
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

        // ---- 1b. the kill event's wiring ----------------------------------------------------------------------------------

        private static readonly FieldInfo Boxes =
            typeof(AutonomousWorldBotController).GetField("RvrRoamBoxes", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static void SetRealm(GameObject living, eRealm realm) =>
            typeof(GameObject).GetField("<Realm>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(living, realm);

        // A gamebot built without its constructor: a companion unless made autonomous (no world, no brain).
        private static GameBot Bot(eRealm realm, bool autonomous)
        {
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(bot, autonomous);
            SetRealm(bot, realm);
            return bot;
        }

        private static AutonomousWorldBotController.RvrRoamBox Walking(GameBot bot, RvrRoamWalk walk)
        {
            var box = new AutonomousWorldBotController.RvrRoamBox { Walk = walk };
            ((ConditionalWeakTable<object, AutonomousWorldBotController.RvrRoamBox>)Boxes.GetValue(null)!).AddOrUpdate(bot, box);
            return box;
        }

        private static RvrRoamWalk AtAmg(RvrCheckSpotCatalog catalog, RvrFrontierPoi amg)
        {
            RvrRoamWalk walk = RvrRoamCircuits.BeginSolo(amg, catalog, eRealm.Hibernia, "hib-stealther", true, 3 * Minute, 0);
            while (walk.CurrentSpot != amg.Id)
                walk = RvrRoamCircuits.Advance(walk);
            return RvrRoamCircuits.Step(walk, 1_000, true, false).Walk;
        }

        [Test]
        public void The_kill_event_reaches_the_walkers_box_and_never_a_companions()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrFrontierPoi amg = catalog.Spot("emain-amg");
            bool previous = Properties.RVR_ROAM_CHECKS;
            Properties.RVR_ROAM_CHECKS = true;
            try
            {
                GameBot walker = Bot(eRealm.Hibernia, autonomous: true), companion = Bot(eRealm.Hibernia, autonomous: false);
                RvrRoamWalk walk = AtAmg(catalog, amg);
                var walkerBox = Walking(walker, walk);
                var companionBox = Walking(companion, walk);
                var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
                SetRealm(player, eRealm.Albion);

                RvrAfterTheKill.Killed(player, new Dictionary<GameLiving, double> { [walker] = 10, [companion] = 5 }, walk.ArrivedAt + 30_000);
                Assert.That(walkerBox.Kills, Has.Count.EqualTo(1), "the walker hears the player's death");
                Assert.That(walkerBox.Kills[0].IsPlayer, Is.True);
                Assert.That(companionBox.Kills, Is.Empty, "a companion is no walker");
                RvrAfterTheKill.Killed(player, new Dictionary<GameLiving, double> { [walker] = 10 }, walk.ArrivedAt + 31_000);
                Assert.That(walkerBox.Kills, Has.Count.EqualTo(1), "one death is kept once");

                Assert.That(AutonomousWorldBotController.IsRoamWalker(walker, eRealm.Albion), Is.True);
                Assert.That(AutonomousWorldBotController.IsRoamWalker(walker, eRealm.Hibernia), Is.False, "never its own realm's death");
                Assert.That(AutonomousWorldBotController.IsRoamWalker(companion, eRealm.Albion), Is.False, "companions never move on");
                Assert.That(AutonomousWorldBotController.IsRoamWalker(player, eRealm.Hibernia), Is.False, "players never");
                Assert.That(RvrAfterTheKill.MayEmote(companion, player), Is.False, "companions never emote");
            }
            finally
            {
                Properties.RVR_ROAM_CHECKS = previous;
            }
        }

        [Test]
        public void The_look_counts_each_kept_death_once_and_forgets_them()
        {
            RvrCheckSpotCatalog catalog = Catalog();
            RvrFrontierPoi amg = catalog.Spot("emain-amg");
            RvrRoamWalk walk = AtAmg(catalog, amg);
            var box = new AutonomousWorldBotController.RvrRoamBox { Walk = walk };
            long at = walk.ArrivedAt + 30_000;
            var atGate = new AutonomousWorldBotController.RvrRoamKill(0, 11, true, at, amg.Region, amg.Position.X, amg.Position.Y);
            Assert.That(AutonomousWorldBotController.NoteKillOn(box, atGate), Is.True);
            Assert.That(AutonomousWorldBotController.NoteKillOn(box, atGate), Is.False, "the same death once");
            Assert.That(AutonomousWorldBotController.NoteKillOn(box,
                atGate with { VictimKey = 12, IsPlayer = false, X = amg.Position.X + amg.Radius + RvrRoamCircuits.KillReach + 100 }), Is.True);
            Assert.That(AutonomousWorldBotController.NoteKillOn(box, atGate with { VictimKey = 13, At = walk.ArrivedAt - 1 }), Is.True);

            RvrRoamWalk after = AutonomousWorldBotController.TakeRoamKills(box, walk, amg, at + 1_000);
            Assert.That(after.KillsHere, Is.EqualTo(walk.KillsHere + 1), "only the death at the gate after arrival");
            Assert.That(after.KillAt, Is.EqualTo(at + 1_000), "the move-on clock starts");
            Assert.That(box.PlayerKillsHere, Is.EqualTo(1));
            Assert.That(box.Kills, Is.Empty, "taken");
            Assert.That(RvrRoamCircuits.Step(after, at + 1_000 + after.MoveOnMilliseconds, true, false).Action,
                Is.EqualTo(RvrRoamAction.MoveOn).Or.EqualTo(RvrRoamAction.Done), "it moves on after killing the player");

            AutonomousWorldBotController.NoteKillOn(box, atGate);
            AutonomousWorldBotController.ForgetSpotKills(box);
            Assert.That(box.Kills, Is.Empty);
            Assert.That(box.PlayerKillsHere, Is.Zero, "a new spot starts with no player kills");
            Assert.That(AutonomousWorldBotController.NoteKillOn(new AutonomousWorldBotController.RvrRoamBox(), atGate), Is.False,
                "no walk, nothing kept");
        }

        private static string Source(string relative)
        {
            string root = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 10 && root != null && !File.Exists(Path.Combine(root, relative)); i++)
                root = Path.GetDirectoryName(root);
            if (root == null || !File.Exists(Path.Combine(root, relative)))
                Assert.Fail($"{relative} not found above the test folder.");
            return Regex.Replace(File.ReadAllText(Path.Combine(root, relative)), @"\s+", " ");
        }

        [Test]
        public void The_kill_event_is_wired_into_both_death_paths_and_the_look()
        {
            const string Auto = "source/server/GameServer/bots/autonomous/";
            string rewards = Source(Auto + "AutonomousBotRealmPointRewards.cs");
            string tactics = Source(Auto + "AutonomousWorldBotController.RvrTactics.cs");
            string after = Source(Auto + "RvrAfterTheKill.cs");
            string field = Source(Auto + "AutonomousChatFieldTalk.cs");
            string events = Source(Auto + "AutonomousRvrEventLayer.cs");
            Assert.Multiple(() =>
            {
                Assert.That(rewards, Does.Contain("RvrAfterTheKill.Killed(killedBot, hostileContributors, now);"), "a gamebot's death");
                Assert.That(rewards, Does.Contain("RvrAfterTheKill.Killed(killedPlayer, hostile, GameLoop.GameLoopTime);"), "a player's death");
                int kill = rewards.IndexOf("RvrAfterTheKill.Killed(killedPlayer", StringComparison.Ordinal);
                int worth = rewards.IndexOf("WorthRealmPoints(killedPlayer.DeathTime", StringComparison.Ordinal);
                Assert.That(kill, Is.LessThan(worth), "the move-on hears a death worth no realm points too");
                Assert.That(after, Does.Contain("AutonomousWorldBotController.NoteRvrRoamKill(victim, hostile.Keys, now);"));
                Assert.That(tactics, Does.Contain("walk = TakeRoamKills(box, walk, spot, now);"), "the look takes the kills");
                Assert.That(Regex.Matches(tactics, @"ForgetSpotKills\(box\);").Count, Is.GreaterThanOrEqualTo(3),
                    "a new walk, an advance past an unusable spot and a step on forget the old spot's kills");
                Assert.That(field, Does.Contain("SawAttacks = SeenAttacks(World(bot.Realm).Attacks, bot.CurrentRegionID, bot.X, bot.Y, " +
                    "AutonomousWorldBotController.RvrContactTable.Of(bot.DatabaseID, GameLoop.GameLoopTime)),"), "the live speaker's sight");
                Assert.That(events, Does.Contain("{ AttackersPresent = Attendance(e, e.AttackerRealm, nowTick) }"), "the siege's attendance");
            });
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
