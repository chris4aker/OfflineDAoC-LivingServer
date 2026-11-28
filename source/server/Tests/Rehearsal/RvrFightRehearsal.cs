using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// The RvR fight rehearsal (#520; docs/rvr-audit-1.2.3/evidence.md F2, F6, section 5.2): three scenarios on
    /// <see cref="RvrFightWorld"/>, each printing one report row. A row is a report, not a gate (evidence.md 5.3): it
    /// becomes a gate only once an owner's PC log agrees with it. Same seed, same row.
    /// </summary>
    public static class RvrFightRehearsal
    {
        public const double FightSeconds = 120;

        /// <summary>
        /// The settings that make a fight repeat by seed. The server's own frontier scan rolls its notices, reaction delays
        /// and calls from Random.Shared, and the bots' strafe and circle habits do too; so in the fight scenarios enemy
        /// gamebots are acquired as before the perception rule (rvr_bot_perception_bots off) and the movement habits are off.
        /// Noticing is rehearsed in scenario 3 and by the rehearsal's own scan (<see cref="RvrFightScan"/>, seeded rolls).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, object> Repeatable = new Dictionary<string, object>
        {
            ["rvr_bot_perception_bots"] = false,
            ["bot_movement_habits"] = false,
            ["bot_move_strafe"] = false,
        };

        /// <summary>The Albion frontier's Hadrian's Wall (region 1, zone 12), as a flat stand-in.</summary>
        public const ushort FrontierRegion = 1, FrontierZone = 12;
        /// <summary>Caledonia (region 250), the battleground whose Caer Caledon fell once in eight PC builds (F6).</summary>
        public const ushort BattlegroundRegion = 250, BattlegroundZone = 250;

        private static readonly eCharacterClass[] AlbionPool =
        [
            eCharacterClass.Armsman, eCharacterClass.Mercenary, eCharacterClass.Mercenary, eCharacterClass.Paladin,
            eCharacterClass.Cleric, eCharacterClass.Cleric, eCharacterClass.Sorcerer, eCharacterClass.Minstrel,
            eCharacterClass.Theurgist, eCharacterClass.Wizard, eCharacterClass.Friar, eCharacterClass.Reaver,
        ];

        private static readonly eCharacterClass[] MidgardPool =
        [
            eCharacterClass.Warrior, eCharacterClass.Savage, eCharacterClass.Savage, eCharacterClass.Berserker,
            eCharacterClass.Healer, eCharacterClass.Healer, eCharacterClass.Skald, eCharacterClass.Shaman,
            eCharacterClass.Runemaster, eCharacterClass.Thane, eCharacterClass.Runemaster, eCharacterClass.Hunter,
        ];

        /// <summary>
        /// The realm's eight as the coordinator builds one: RvrRosterTemplates.Build over a pool of real gamebots (the
        /// leader first), placed in a loose cluster around (<paramref name="x"/>, <paramref name="y"/>) and formed into a group.
        /// </summary>
        public static List<GameBot> Eight(RvrFightWorld world, Region region, eRealm realm, int x, int y, ushort heading, byte level,
            string tag, out RvrRoster roster)
        {
            eCharacterClass[] classes = realm == eRealm.Albion ? AlbionPool : MidgardPool;
            List<GameBot> pool = classes.Select((c, i) => world.NewBot(realm, c, level, $"{tag}{i + 1:00}{c}")).ToList();
            RvrRosterCandidate Candidate(GameBot bot, int index) => new(index, (eCharacterClass)bot.CharacterClass.ID, bot.Level,
                RvrFightKits.Heals((eCharacterClass)bot.CharacterClass.ID) || (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Minstrel or eCharacterClass.Skald,
                bot.BotSpec?.EraRole);
            GameBot leader = pool[0];
            List<GameBot> rest = pool.Skip(1).ToList();
            roster = RvrRosterTemplates.Build(realm, Candidate(leader, -1), rest.Select(Candidate).ToArray(), 7,
                AutonomousRvrPetCap.CapFor(8, Properties.RVR_GROUP_PET_CAP), Properties.RVR_ROSTER_BUILD_ROLES);
            var members = new List<GameBot> { leader };
            members.AddRange(roster.Picked.Select(i => rest[i]));
            for (int i = 0; i < members.Count; i++)
            {
                // two ranks: tanks and melee in front, healers and casters 250 behind
                RvrRole role = RvrRoles.For((eCharacterClass)members[i].CharacterClass.ID);
                int back = role is RvrRole.Tank or RvrRole.Melee ? 0 : 250;
                // heading 1024 faces west (-x), 3072 east (+x): the back rank stands behind, away from the enemy
                world.Place(members[i], region, x + (heading == 1024 ? back : -back), y - 300 + i * 85, 0, heading);
            }
            world.Form(members.Cast<GameLiving>().ToList());
            return members;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scenario 1: two bot-only eights meet in the open

        /// <summary>
        /// #531: one side's opening, from its members' first engages within <see cref="OpeningMilliseconds"/> of the side's
        /// first: the first pick (its target and rule), how many distinct enemies the members opened on, and how many the
        /// spread pick of before 1.2.3 would have opened on from the same scans. Null when the side never engaged by scan.
        /// </summary>
        public sealed record RvrOpening(GameLiving FirstTarget, string FirstRule, int Members, int OpenedOn, int SpreadOpenedOn);

        public sealed record OpenFieldResult(string Row, IReadOnlyList<string> Detail, int Errors, RvrOpening Albion = null,
            RvrOpening Midgard = null, RvrFightDepth Depth = null);

        /// <summary>How long after a side's first engage its members' first engages count as its opening.</summary>
        public const long OpeningMilliseconds = 10_000;

        public static RvrOpening Opening(RvrFightScan scan, List<GameBot> side)
        {
            var picks = scan.Picks.Where(p => side.Contains(p.Bot)).ToList();
            if (picks.Count == 0)
                return null;
            long start = picks[0].At;
            var opening = picks.Where(p => p.At - start <= OpeningMilliseconds).GroupBy(p => p.Bot).Select(g => g.First()).ToList();
            return new RvrOpening(picks[0].Target, picks[0].Rule, opening.Count, opening.Select(p => p.Target).Distinct().Count(),
                opening.Select(p => p.SpreadTarget).Distinct().Count());
        }

        private static string OpeningText(RvrOpening opening) => opening == null ? "none" :
            $"{RvrRoles.For(ClassOf(opening.FirstTarget))}:{Short(opening.FirstTarget)} rule={opening.FirstRule} " +
            $"opened_on={opening.OpenedOn}/{opening.Members} (spread {opening.SpreadOpenedOn})";

        public static OpenFieldResult OpenField(int seed, double seconds = FightSeconds, int gap = 2_600)
        {
            using var world = new RvrFightWorld(seed, Repeatable);
            Region region = world.AddRegion(FrontierRegion, FrontierZone, "Hadrian's Wall flat stand-in (rehearsal)");
            List<GameBot> albion = Eight(world, region, eRealm.Albion, 30_000, 30_000, 3072, 50, "A", out RvrRoster albionRoster);
            List<GameBot> midgard = Eight(world, region, eRealm.Midgard, 30_000 + gap, 30_000, 1024, 50, "M", out RvrRoster midgardRoster);
            var scan = new RvrFightScan(seed);
            scan.Add(albion);
            scan.Add(midgard);
            // #531 step 2c-1: the realm abilities the eights' builds buy
            var actives = new RvrFightActives();
            actives.Grant(albion.Concat(midgard));
            // #531 step 2e: how deep each side's casters and healers stand behind their tanks
            var depth = new RvrFightDepth();
            // the boards are built from the live bots by the server's own code, as a brain asks for one
            int boardsBefore = (int)RvrFightBoards.Builds;
            RvrFightBoard firstAlbionBoard = null, firstMidgardBoard = null;
            world.Run(seconds, w =>
            {
                scan.Tick(w.Now);
                actives.Tick(w.Now);
                depth.Tick(w.Now, new[] { albion, midgard });
                firstAlbionBoard ??= ContactBoard(albion);
                firstMidgardBoard ??= ContactBoard(midgard);
            });
            var detail = new List<string>
            {
                $"albion roster: {Describe(albionRoster)}",
                $"midgard roster: {Describe(midgardRoster)}",
                $"boards built from live bots: {RvrFightBoards.Builds - boardsBefore}; first albion board in contact: {BoardLine(firstAlbionBoard)}; first midgard board in contact: {BoardLine(firstMidgardBoard)}",
                $"casts started {world.Recorder.CastsStarted}, finished {world.Recorder.CastsFinished}; hits {world.Recorder.Hits.Count}",
                $"contact shadow (#529, board-listed enemies no member knew of): albion {Shadow(eRealm.Albion)}; midgard {Shadow(eRealm.Midgard)}",
            };
            detail.AddRange(albion.Concat(midgard).Select(State));
            // #531: each engage the scan made, by which rule, and what the spread pick of before would have taken
            detail.AddRange(scan.Picks.Take(24).Select(p => $"pick {S(p.At)} {p.Bot.Name} -> {Short(p.Target)} rule={p.Rule} spread_would={Short(p.SpreadTarget)}"));
            detail.AddRange(world.Recorder.Mezzes.Select(m => $"mez {S(m.At)} {m.Caster.Name} -> {m.Target.Name} at {m.TargetHealthPercent}% : {(m.Landed == true ? "landed" : m.Landed == false ? "refused" : "?")}"));
            detail.AddRange(world.Recorder.Deaths.Select(d => $"death {S(d.At)} {d.Victim.Name} ({RvrRoles.For(ClassOf(d.Victim))}) by {d.Killer?.Name}"));
            detail.AddRange(world.Errors.Take(10).Select(e => "error: " + e));
            detail.Add(actives.Row(world.Seed, world.Recorder));
            string row = OpenFieldRow(world, scan, albion, midgard, depth);
            return new OpenFieldResult(row, detail, world.ServiceErrors, Opening(scan, albion), Opening(scan, midgard), depth);
        }

        private static string Shadow(eRealm realm)
        {
            RvrShadowTotals t = AutonomousWorldBotController.RvrContactShadowTally.Totals((int)realm);
            return $"pairs {t.PairsUnknown}/{t.Pairs} unknown ({t.PairsUnknownShare:0.##}), enemy-seconds {t.Unknown}/{t.Listed} ({t.UnknownShare:0.##})";
        }

        private static RvrFightBoard ContactBoard(List<GameBot> side)
        {
            GameBot asker = side.FirstOrDefault(b => b.IsAlive);
            RvrFightBoard board = asker == null ? null : RvrFightBoards.Peek(asker);
            return board is { InContact: true } ? board : null;
        }

        private static string BoardLine(RvrFightBoard board) => board == null ? "none" :
            $"t={S(board.BuiltAt)} members={board.Friendly.Count} enemies={board.Enemy.Count} stance={board.Plan.Stance} called={board.Plan.CalledTarget?.Name ?? "-"} cc={board.Plan.CrowdControl.Length}";

        private static string Describe(RvrRoster roster) =>
            $"{roster.Template} filled={string.Join(",", roster.Filled.Select(f => $"{f.Role}:{f.Class}"))} missing={string.Join(",", roster.Missing)} cc={roster.CrowdControlLabel}";

        private static string OpenFieldRow(RvrFightWorld world, RvrFightScan scan, List<GameBot> albion, List<GameBot> midgard,
            RvrFightDepth depth)
        {
            RvrFightRecorder r = world.Recorder;
            string Notice(List<GameBot> side)
            {
                RvrNoticeEvent first = scan.Notices.FirstOrDefault(n => side.Contains(n.Bot));
                return first == null ? "never" : $"{S(first.At)} {Short(first.Bot)}->{Short(first.Target)} at {first.Distance}";
            }
            RvrMezAttempt firstMez = r.Mezzes.FirstOrDefault(m => m.Landed == true);
            var firstControl = r.ControlStarts.FirstOrDefault();
            string cc = firstMez != null ? $"{S(firstMez.At)} mez {Short(firstMez.Caster)}->{Short(firstMez.Target)}"
                : firstControl.Target != null ? $"{S(firstControl.At)} {Short(firstControl.Target)}" : "none";
            int refused = r.Mezzes.Count(m => m.Refused);
            int refusedHurtBot = r.Mezzes.Count(m => m.Refused && !m.TargetIsPlayer && m.TargetHealthPercent < 75);
            string deaths = r.Deaths.Count == 0 ? "none" : string.Join(" ", r.Deaths.Take(3).Select(d => $"{RvrRoles.For(ClassOf(d.Victim))}({Side(d.Victim)})@{S(d.At)}"));
            string Share(List<GameBot> side)
            {
                var hits = r.Hits.Where(h => side.Contains(h.Attacker as GameBot)).ToList();
                int total = hits.Sum(h => h.Amount);
                return total == 0 ? "-" : $"{100.0 * hits.Where(h => h.OnCalledTarget).Sum(h => h.Amount) / total:0}%";
            }
            // #531 step 2a: each side's first area mez that landed, how many it caught, and whether it came before the
            // side's first hit (the opener on the charge)
            string Opener(List<GameBot> side)
            {
                RvrMezAttempt first = r.Mezzes.FirstOrDefault(m => m.Landed == true && m.Radius > 0 && side.Contains(m.Caster as GameBot));
                if (first == null)
                    return "none";
                RvrHit firstHit = r.Hits.FirstOrDefault(h => side.Contains(h.Attacker as GameBot));
                string when = firstHit == null || first.At <= firstHit.At ? "before_first_hit" : "after_first_hit";
                return $"{S(first.At)}:{Short(first.Caster)}x{first.Caught}:{when}";
            }
            int albAlive = albion.Count(b => b.IsAlive), midAlive = midgard.Count(b => b.IsAlive);
            string held = albAlive == midAlive ? $"none ({albAlive}-{midAlive})" : albAlive > midAlive ? $"Albion ({albAlive}-{midAlive})" : $"Midgard ({midAlive}-{albAlive})";
            long firstHit = r.Hits.Count > 0 ? r.Hits[0].At : 0;
            long end = r.Deaths.Count > 0 && (albAlive == 0 || midAlive == 0) ? r.Deaths[^1].At : world.Now;
            string duration = r.Hits.Count == 0 ? "no fight" : $"{(end - firstHit) / 1000.0:0}s";
            return $"RVR_REHEARSAL open_field seed={world.Seed} sides=Alb8 vs Mid8 noticed=Alb:{Notice(albion)} Mid:{Notice(midgard)} " +
                   $"first_cc={cc} opener=Alb:{Opener(albion)} Mid:{Opener(midgard)} mez_refused={refused} (bot<75%={refusedHurtBot}) " +
                   $"first_deaths={deaths} first_pick=Alb:{OpeningText(Opening(scan, albion))} Mid:{OpeningText(Opening(scan, midgard))} " +
                   $"called_share=Alb:{Share(albion)} Mid:{Share(midgard)} depth=Alb:{depth.Text(eRealm.Albion)} Mid:{depth.Text(eRealm.Midgard)} " +
                   $"held={held} duration={duration}";
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scenario 2: a 24-bot storm on a battleground lord's floor

        public sealed record StormResult(string Row, IReadOnlyList<string> Detail, int Errors, int LordHealthPercentAtEnd);

        /// <summary>
        /// Three Albion eights (level 35, Caledonia's bracket) on the floor of Caer Caledon's Midgard lord, the doors already
        /// down. Each stormer does what the battleground storm does once it is on the lord's floor
        /// (AutonomousWorldBotController.Battleground: within <see cref="AutonomousBattlegroundKeepRally.LordStrikeReach"/> it
        /// targets and aggroes the lord; farther it walks on to him); the bots' brains do the rest. The lord is a real
        /// <see cref="GuardLord"/> (his TakeDamage range rule, his health times three), with a stand-in keep (no doors, so not
        /// shielded) and a stand-in level.
        /// </summary>
        public static StormResult Storm(int seed, double seconds = FightSeconds, byte lordLevel = 40, byte botLevel = 35)
        {
            using var world = new RvrFightWorld(seed, Repeatable);
            Region region = world.AddRegion(BattlegroundRegion, BattlegroundZone, "Caledonia flat stand-in (rehearsal)");
            const int lordX = 40_000, lordY = 40_000;
            RehearsalLord lord = RehearsalLord.Make(region);
            lord.Name = "Lord Caledon (stand-in)";
            lord.Realm = eRealm.Midgard;
            lord.Level = lordLevel;
            world.Place(lord, region, lordX, lordY, 0, 1024);
            int damageRadius = lord.DamageRadius;
            int maxHealth = lord.MaxHealth;
            var stormers = new List<GameBot>();
            for (int g = 0; g < 3; g++)
                stormers.AddRange(Eight(world, region, eRealm.Albion, lordX - 650 - g * 60, lordY - 700 + g * 700, 3072, botLevel, $"S{g}", out _));
            var samples = new List<(long At, int Health)>();
            long nextSample = world.Now;
            long nextOrder = world.Now;
            world.Run(seconds, w =>
            {
                if (w.Now >= nextSample)
                {
                    samples.Add((w.Now, lord.IsAlive ? lord.HealthPercent : 0));
                    nextSample += 10_000;
                }
                if (w.Now < nextOrder || !lord.IsAlive)
                    return;
                nextOrder += 1_000;
                foreach (GameBot bot in stormers)
                    StormStep(bot, lord);
            });
            samples.Add((world.Now, lord.IsAlive ? lord.HealthPercent : 0));
            RvrFightRecorder r = world.Recorder;
            int onLord = r.Hits.Where(h => h.Target == lord).Sum(h => h.Amount);
            int hitters = r.Hits.Where(h => h.Target == lord).Select(h => h.Attacker).Distinct().Count();
            int dead = stormers.Count(b => !b.IsAlive);
            string curve = string.Join(" ", samples.Select(s => $"{S(s.At)}:{s.Health}%"));
            string row = $"RVR_REHEARSAL bg_storm seed={seed} stormers=24 Alb L{botLevel} lord=Mid L{lordLevel} maxHealth={maxHealth} " +
                         $"damage_radius={damageRadius} lord_health={curve} damage_on_lord={onLord} from={hitters}/24 " +
                         $"stormers_dead={dead} lord_{(lord.IsAlive ? "alive" : "killed")}";
            var detail = new List<string> { $"casts started {r.CastsStarted}, finished {r.CastsFinished}; hits {r.Hits.Count}" };
            detail.Add($"lord: brain={lord.Brain?.GetType().Name} level={lord.Level} hp={lord.HealthPercent}%");
            detail.AddRange(stormers.Take(8).Select(State));
            detail.AddRange(world.Errors.Take(10).Select(e => "error: " + e));
            return new StormResult(row, detail, world.ServiceErrors, lord.IsAlive ? lord.HealthPercent : 0);
        }

        private static void StormStep(GameBot bot, GuardLord lord)
        {
            if (!bot.IsAlive || bot.Brain is not BotBrain brain)
                return;
            if (brain.HasAggro && bot.TargetObject == lord)
                return;
            RvrLordFormation.Role role = RvrLordFormation.RoleOf(ClassOf(bot), bot.CharacterClass.ClassType == eClassType.ListCaster);
            float reach = AutonomousBattlegroundKeepRally.LordStrikeReach(lord, RvrLordFormation.StandsBack(role));
            if (bot.IsWithinRadius(lord, (int)reach))
            {
                bot.StopMoving();
                bot.TargetObject = lord;
                brain.AddToAggroList(lord, Math.Max(100, lord.EffectiveLevel * 12));
                brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                return;
            }
            if (bot.IsMoving)
                return;
            double dx = lord.X - bot.X, dy = lord.Y - bot.Y, d = Math.Sqrt(dx * dx + dy * dy);
            double stop = Math.Max(50, reach - 60);
            bot.WalkTo(new System.Numerics.Vector3((float)(lord.X - dx / d * stop), (float)(lord.Y - dy / d * stop), lord.Z), bot.MaxSpeed);
        }

        /// <summary>
        /// A battleground keep's lord with a stand-in keep: a real <see cref="GameKeep"/> object holding no doors (they are down)
        /// and no database row. At his death the stand-in keep is let go first (GuardLord.Die would reset a real keep).
        /// </summary>
        public sealed class RehearsalLord : GuardLord
        {
            public static RehearsalLord Make(Region region)
            {
                var lord = new RehearsalLord();
                var keep = (GameKeep)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameKeep));
                keep.Doors = new Dictionary<string, GameKeepDoor>();
                byte baseLevel = BattlegroundBrackets.ForRegion(BattlegroundRegion)?.MaxLevel ?? 35;
                keep.DBKeep = new DOL.Database.DbKeep
                {
                    KeepID = 250_001, Name = "Caer Caledon (stand-in)", Region = BattlegroundRegion, Realm = (byte)eRealm.Midgard,
                    OriginalRealm = (int)eRealm.Midgard, BaseLevel = baseLevel, Level = 1,
                };
                keep.BaseLevel = baseLevel;
                typeof(AbstractGameKeep).GetProperty("CurrentRegion")?.SetValue(keep, region);
                var component = (GameKeepComponent)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameKeepComponent));
                component.Keep = keep;
                lord.Component = component;
                return lord;
            }

            public override void Die(GameObject killer)
            {
                Component = null;
                base.Die(killer);
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scenario 3: a stand-in player in range of a bot group, the lookers' cap on

        public sealed record NoticeResult(string Row, IReadOnlyList<string> Detail, int Errors, bool MezRefusedOnHurtBot, bool MezLandedOnHurtPlayer);

        /// <summary>
        /// A Midgard eight stands in the open and an Albion player (a real GamePlayer on a recording connection) stands still
        /// at each distance for up to <paramref name="seconds"/>, <paramref name="trials"/> times; the row gives the median time
        /// from first sight to the first notice against the perception table's median (MedianSecondsToNotice: one bot looking
        /// alone at a plain, moving enemy). Then the planted defect: the eight's mezzer casts the same mez on a group mate's
        /// enemy-realm stand-in bot and on the player, both at 70% health.
        /// </summary>
        public static NoticeResult PlayerNotice(int seed, int[] distances = null, int trials = 6, double seconds = FightSeconds)
        {
            distances ??= [1_500, 2_500, 3_000];
            var world = new RvrFightWorld(seed, new Dictionary<string, object> { ["rvr_bot_group_lookers"] = true });
            Region region = world.AddRegion(FrontierRegion, FrontierZone, "Hadrian's Wall flat stand-in (rehearsal)");
            List<GameBot> eight = Eight(world, region, eRealm.Midgard, 30_000, 30_000, 1024, 50, "N", out _);
            // the eight only looks: its brains are stopped, so the server's own scan (Random.Shared) never engages the player
            // and the time to notice is the rule's alone
            foreach (GameBot bot in eight)
                bot.Brain?.Stop();
            bool lookers = Properties.RVR_BOT_GROUP_LOOKERS;
            StandInPlayer player = StandInPlayer.Make(region, eRealm.Albion, eCharacterClass.Armsman, 50, "Standin");
            if (!player.Enter(region, 30_000 - distances[0], 30_000))
                throw new InvalidOperationException("the stand-in player did not enter the region");
            world.Track(player);
            world.Run(1);
            var detail = new List<string>();
            var parts = new List<string>();
            int index = 0;
            foreach (int distance in distances)
            {
                var times = new List<double>();
                for (int t = 0; t < trials; t++)
                {
                    player.MoveInRegionTo(30_000 - distance, 30_000);
                    world.Run(1); // the subzone takes the move
                    var scan = new RvrFightScan(seed * 1_000 + index++) { Engage = false, Lookers = lookers };
                    scan.Add(eight);
                    long start = world.Now;
                    double? noticed = null;
                    int ticks = (int)(seconds * RvrFightWorld.TicksPerSecond);
                    for (int i = 0; i < ticks && noticed == null; i++)
                    {
                        world.Tick();
                        scan.Tick(world.Now);
                        RvrNoticeEvent n = scan.Notices.FirstOrDefault(e => e.Target == player);
                        if (n != null)
                            noticed = (n.At - start) / 1000.0;
                    }
                    if (noticed != null)
                        times.Add(noticed.Value);
                    world.Run(16); // past the forget time, so the next trial starts unnoticed
                }
                double table = AutonomousRvrPerception.MedianSecondsToNotice(distance, AutonomousRvrPerception.Profile(
                    AutonomousRvrPerception.ParseAwareness(Properties.RVR_BOT_AWARENESS)));
                string median = times.Count == 0 ? "never" : $"{Median(times):0.0}s";
                parts.Add($"d{distance}:table={table:0.0}s rehearsal={median} noticed={times.Count}/{trials}");
                detail.Add($"distance {distance}: times {string.Join(",", times.Select(x => x.ToString("0.0", CultureInfo.InvariantCulture)))}");
            }
            int errors = world.ServiceErrors;
            detail.AddRange(world.Errors.Take(10).Select(e => "error: " + e));
            world.Dispose();
            // the planted defect, in a quiet world of its own: the same mez on a hurt gamebot and on a hurt player
            (RvrMezAttempt onBot, RvrMezAttempt onPlayer, int parityErrors) = MezParity(seed, detail);
            bool botRefused = onBot?.Landed == false;
            bool playerLanded = onPlayer?.Landed == true;
            string row = $"RVR_REHEARSAL player_notice seed={seed} group=Mid8 lookers={(lookers ? "on" : "off")} " +
                         $"player=Alb standing_still {string.Join(" ", parts)} " +
                         $"mez_parity=bot@{onBot?.TargetHealthPercent}%:{Outcome(onBot)} player@{onPlayer?.TargetHealthPercent}%:{Outcome(onPlayer)}";
            return new NoticeResult(row, detail, errors + parityErrors, botRefused, playerLanded);
        }

        /// <summary>
        /// A Midgard Healer (the eight's crowd controller) casts the same stand-in mez through its own CastSpell on an Albion
        /// gamebot and on an Albion player, each held at 70% health; nobody else acts (their brains are stopped).
        /// </summary>
        public static (RvrMezAttempt OnBot, RvrMezAttempt OnPlayer, int Errors) MezParity(int seed, List<string> detail = null)
        {
            using var world = new RvrFightWorld(seed);
            Region region = world.AddRegion(FrontierRegion, FrontierZone, "Hadrian's Wall flat stand-in (rehearsal)");
            GameBot mezzer = world.NewBot(eRealm.Midgard, eCharacterClass.Healer, 50, "ParityHealer");
            world.Place(mezzer, region, 30_000, 30_000, 0, 1024);
            GameBot hurtBot = world.NewBot(eRealm.Albion, eCharacterClass.Mercenary, 50, "ParityMercenary");
            world.Place(hurtBot, region, 29_400, 30_200, 0, 3072);
            StandInPlayer player = StandInPlayer.Make(region, eRealm.Albion, eCharacterClass.Armsman, 50, "ParityPlayer");
            player.Enter(region, 29_400, 29_800);
            world.Track(player);
            mezzer.Brain?.Stop();
            hurtBot.Brain?.Stop();
            world.Run(1);
            RvrMezAttempt onBot = MezAt70(world, mezzer, hurtBot);
            world.Run(1);
            RvrMezAttempt onPlayer = MezAt70(world, mezzer, player);
            // the control: the same mez on a second gamebot at full health (a resist roll, not the rule, would refuse it)
            GameBot fullBot = world.NewBot(eRealm.Albion, eCharacterClass.Mercenary, 50, "ParityControl");
            world.Place(fullBot, region, 29_400, 30_400, 0, 3072);
            fullBot.Brain?.Stop();
            world.Run(1);
            RvrMezAttempt onFull = MezAt(world, mezzer, fullBot, 100);
            detail?.Add($"parity control: bot@100%:{Outcome(onFull)}");
            detail?.AddRange(world.Recorder.Mezzes.Select(m => $"parity mez {S(m.At)} {m.Caster.Name} -> {m.Target.Name} at {m.TargetHealthPercent}%: {Outcome(m)}"));
            detail?.AddRange(world.Errors.Take(5).Select(e => "parity error: " + e));
            return (onBot, onPlayer, world.ServiceErrors);
        }

        private static string Outcome(RvrMezAttempt attempt) => attempt?.Landed switch { true => "landed", false => "refused", _ => "no-cast" };

        /// <summary>The mezzer casts its mez on the target at 70% health through the bot's own CastSpell; null if no cast finished.</summary>
        private static RvrMezAttempt MezAt70(RvrFightWorld world, GameBot mezzer, GameLiving target) => MezAt(world, mezzer, target, 70);

        private static RvrMezAttempt MezAt(RvrFightWorld world, GameBot mezzer, GameLiving target, int percent)
        {
            Spell mez = mezzer.Spells.First(sp => sp.SpellType == eSpellType.Mesmerize);
            target.Health = target.MaxHealth * percent / 100;
            mezzer.TargetObject = target;
            int before = world.Recorder.Mezzes.Count;
            for (int attempt = 0; attempt < 5 && !world.Recorder.Mezzes.Skip(before).Any(m => m.Target == target); attempt++)
            {
                target.Health = target.MaxHealth * percent / 100;
                mezzer.Mana = mezzer.MaxMana;
                mezzer.TargetObject = target;
                mezzer.CastSpell(mez, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells), false);
                world.Run(4);
            }
            world.Run(0.2);
            return world.Recorder.Mezzes.Skip(before).FirstOrDefault(m => m.Target == target);
        }

        private static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
        }

        /// <summary>One bot's state at the end, for the detail lines.</summary>
        public static string State(GameBot bot)
        {
            var brain = bot.Brain as BotBrain;
            return $"{bot.Name}: alive={bot.IsAlive} hp={bot.HealthPercent}% mana={bot.ManaPercent}% at={bot.X},{bot.Y} group={bot.Group?.MemberCount ?? 0} " +
                   $"brain={bot.Brain?.GetType().Name} active={bot.Brain?.IsActive} state={brain?.FSM?.GetCurrentState()?.StateType} aggro={brain?.HasAggro} " +
                   $"target={bot.TargetObject?.Name} attacking={bot.IsAttacking} casting={bot.IsCasting} spells={bot.Spells?.Count ?? 0} " +
                   $"weapon={bot.ActiveWeapon?.Name ?? "-"} speed={bot.MaxSpeed} activity=\"{bot.PersistentRecord?.Activity}\" phase=\"{bot.PersistentRecord?.ObjectivePhase}\" " +
                   $"objective={bot.PersistentRecord?.ObjectiveKind} move=[{(bot.movementComponent as NpcMovementComponent)?.DescribeRouteState()}]";
        }

        internal static eCharacterClass ClassOf(GameLiving living) => living switch
        {
            GameBot { CharacterClass: not null } bot => (eCharacterClass)bot.CharacterClass.ID,
            GamePlayer { CharacterClass: not null } player => (eCharacterClass)player.CharacterClass.ID,
            _ => eCharacterClass.Unknown,
        };

        private static string Side(GameLiving living) => living.Realm switch { eRealm.Albion => "Alb", eRealm.Midgard => "Mid", eRealm.Hibernia => "Hib", _ => "-" };
        private static string Short(GameLiving living) => living == null ? "-" : $"{Side(living)}.{ClassOf(living)}";
        internal static string S(long at) => ((at - RvrFightWorld.Start) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";
    }
}
