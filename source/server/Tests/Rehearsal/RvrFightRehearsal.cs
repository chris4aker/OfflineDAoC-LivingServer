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
            RvrOpening Midgard = null);

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
            // the boards are built from the live bots by the server's own code, as a brain asks for one
            int boardsBefore = (int)RvrFightBoards.Builds;
            RvrFightBoard firstAlbionBoard = null, firstMidgardBoard = null;
            world.Run(seconds, w =>
            {
                scan.Tick(w.Now);
                actives.Tick(w.Now);
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
            string row = OpenFieldRow(world, scan, albion, midgard);
            return new OpenFieldResult(row, detail, world.ServiceErrors, Opening(scan, albion), Opening(scan, midgard));
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

        private static string OpenFieldRow(RvrFightWorld world, RvrFightScan scan, List<GameBot> albion, List<GameBot> midgard)
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
                   $"called_share=Alb:{Share(albion)} Mid:{Share(midgard)} held={held} duration={duration}";
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
        // Scenario 4 (#530 step 5b): solo gamebots at a battleground muster form groups

        public sealed record MusterResult(string Row, IReadOnlyList<string> Detail, int Errors, int[] PartySizes,
            int BoardMembersBefore, int[] BoardMembersAfter, int Placed, int OutsidersTaken, int Solos);

        /// <summary>
        /// Fourteen solo Albion gamebots (level 35, Caledonia's bracket, on battleground tours) stand at their wave's staging
        /// point, with a Midgard eight 2,200 away. Beside them stand four that must not be taken: a Midgard bot, two Albions
        /// already in a group, a level-25 Albion (another bracket) and one whose tour ends in two minutes. The coordinator's
        /// muster pass (AutonomousBotGroupCoordinator.FormMusterParties) runs once; the row shows the parties and each party's
        /// fight board against the eight, before (one solo bot) and after (the party).
        /// </summary>
        public static MusterResult MusterGroups(int seed)
        {
            using var world = new RvrFightWorld(seed, Repeatable);
            Region region = world.AddRegion(BattlegroundRegion, BattlegroundZone, "Caledonia flat stand-in (rehearsal)");
            const int sx = 40_000, sy = 40_000;
            var staging = new System.Numerics.Vector3(sx, sy, 0);
            string tour = DateTime.UtcNow.AddMinutes(40).ToString("O");
            GameBot Solo(eRealm realm, eCharacterClass c, byte level, string name, int x, int y, string expires)
            {
                GameBot bot = world.NewBot(realm, c, level, name, "Battleground");
                bot.PersistentRecord.ObjectiveExpiresUtc = expires;
                world.Place(bot, region, x, y, 0, 3072);
                return bot;
            }
            var solos = new List<GameBot>();
            for (int i = 0; i < 14; i++)
            {
                eCharacterClass c = AlbionPool[i % AlbionPool.Length];
                solos.Add(Solo(eRealm.Albion, c, 35, $"M{i + 1:00}{c}", sx - 300 + i % 5 * 150, sy - 300 + i / 5 * 200, tour));
            }
            var outsiders = new List<GameBot>
            {
                Solo(eRealm.Midgard, eCharacterClass.Warrior, 35, "XMidgard", sx + 100, sy + 100, tour),
                Solo(eRealm.Albion, eCharacterClass.Armsman, 25, "XLevel25", sx + 150, sy - 150, tour),
                Solo(eRealm.Albion, eCharacterClass.Cleric, 35, "XLeaving", sx - 150, sy + 150, DateTime.UtcNow.AddMinutes(2).ToString("O")),
            };
            List<GameBot> pair = [Solo(eRealm.Albion, eCharacterClass.Paladin, 35, "XPairA", sx + 50, sy - 50, tour),
                Solo(eRealm.Albion, eCharacterClass.Friar, 35, "XPairB", sx + 60, sy - 60, tour)];
            world.Form(pair.Cast<GameLiving>().ToList());
            outsiders.AddRange(pair);
            Eight(world, region, eRealm.Midgard, sx + 2_200, sy, 1024, 35, "E", out _);

            int before = RvrFightBoards.Get(solos[0])?.Members.Length ?? 0;
            int placed = AutonomousBotGroupCoordinator.FormMusterParties(
                AutonomousRvrEventLayer.BattlegroundObjectiveId(250_001), eRealm.Albion, staging, solos.Concat(outsiders).ToList(), world.Now, DateTime.UtcNow);
            Group[] parties = solos.Select(bot => bot.Group).Where(group => group != null).Distinct().ToArray();
            int[] sizes = parties.Select(group => group.GetMembersInTheGroup().Count).OrderByDescending(n => n).ToArray();
            int[] boards = parties.Select(group => RvrFightBoards.Get(group.GetMembersInTheGroup().OfType<GameBot>().First())?.Members.Length ?? 0)
                .OrderByDescending(n => n).ToArray();
            int taken = outsiders.Count(bot => bot.Group != null && parties.Contains(bot.Group));
            int solo = solos.Count(bot => bot.Group == null);
            string row = $"RVR_REHEARSAL bg_muster seed={seed} solos=14 Alb L35 placed={placed} parties={parties.Length} " +
                         $"sizes={string.Join("+", sizes)} board_members_before={before} board_members_after={string.Join("+", boards)} " +
                         $"outsiders_taken={taken} left_solo={solo}";
            var detail = new List<string>();
            foreach (Group group in parties)
                detail.Add($"party {AutonomousBotGroupCoordinator.MusterPartyOf(group.GetMembersInTheGroup().OfType<GameBot>().First())}: " +
                           string.Join(", ", group.GetMembersInTheGroup().Select(member => member.Name)));
            detail.AddRange(world.Errors.Take(10).Select(e => "error: " + e));
            int errors = world.ServiceErrors;
            foreach (Group group in parties)
            {
                RvrFightBoards.Forget(group);
                group.DisbandGroup();
            }
            return new MusterResult(row, detail, errors, sizes, before, boards, placed, taken, solo);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scenario 5 (#599 step 1): two Albion eights link into a battlegroup and meet a Midgard eight (16v8)

        public sealed record BattlegroupResult(string Row, IReadOnlyList<string> Detail, int Errors, string BattlegroupId,
            string LeadForce, IReadOnlyList<string> Linked, bool PairLinked, bool FarLinked, bool MidgardLinked, int[] RosterSizes,
            int MidgardInRosters, int AlbionAlive, int MidgardAlive, int BothCalledSeconds = 0, int SharedCallSeconds = 0,
            int FollowerBattlegroupPicks = 0, int FollowerPicks = 0, int BattlegroupCallSeconds = 0);

        /// <summary>
        /// Two Albion eights stand 1,800 apart on the Hadrian's Wall stand-in with a Midgard eight 2,600 east of the first. An
        /// Albion pair stands 1,500 from the first eight and a third Albion eight 9,000 west of it. The coordinator's
        /// battlegroup pulse and the support rosters run once (AutonomousBotGroupCoordinator.PulseBattlegroups,
        /// RealmWarbandSupport.Pulse); then the fight runs. The row shows who linked, each Albion bot's support roster and the
        /// bots standing at the end. The outcome is reported, not judged.
        /// </summary>
        public static BattlegroupResult Battlegroup(int seed, double seconds = 60)
        {
            using var world = new RvrFightWorld(seed, Repeatable);
            AutonomousBotGroupCoordinator.ResetBattlegroups();
            try
            {
                Region region = world.AddRegion(FrontierRegion, FrontierZone, "Hadrian's Wall flat stand-in (rehearsal)");
                List<GameBot> first = Eight(world, region, eRealm.Albion, 30_000, 30_000, 3072, 50, "A", out _);
                List<GameBot> second = Eight(world, region, eRealm.Albion, 28_200, 30_000, 3072, 50, "B", out _);
                List<GameBot> far = Eight(world, region, eRealm.Albion, 21_000, 30_000, 3072, 50, "F", out _);
                List<GameBot> pair = [world.NewBot(eRealm.Albion, eCharacterClass.Paladin, 50, "PPaladin"),
                    world.NewBot(eRealm.Albion, eCharacterClass.Friar, 50, "PFriar")];
                world.Place(pair[0], region, 30_000, 31_500, 0, 3072);
                world.Place(pair[1], region, 30_060, 31_500, 0, 3072);
                world.Form(pair.Cast<GameLiving>().ToList());
                List<GameBot> midgard = Eight(world, region, eRealm.Midgard, 32_600, 30_000, 1024, 50, "M", out _);

                List<AutonomousRvrEventLayer.BattlegroupChange> changes =
                    AutonomousBotGroupCoordinator.PulseBattlegroups(world.Now, force: true) ??
                    new List<AutonomousRvrEventLayer.BattlegroupChange>();
                RealmWarbandSupport.Pulse(world.Now, immediately: true);
                string ForceOf(GameBot bot) => AutonomousBotGroupCoordinator.RvrForceId(bot);
                AutonomousRvrEventLayer.BattlegroupView battlegroup = AutonomousRvrEventLayer.BattlegroupOf(ForceOf(first[0]));
                IReadOnlyList<string> linked = battlegroup?.Members ?? Array.Empty<string>();
                bool In(GameBot bot) => linked.Contains(ForceOf(bot));
                List<GameBot> albion = first.Concat(second).ToList();
                int[] rosters = albion.Select(bot => RealmWarbandSupport.Get(bot)?.Members.Length ?? 0).Distinct().OrderBy(n => n).ToArray();
                int midgardInRosters = albion.SelectMany(bot => RealmWarbandSupport.Get(bot)?.Members ?? Array.Empty<GameLiving>())
                    .Distinct().Count(member => member.Realm == eRealm.Midgard);

                var scan = new RvrFightScan(seed);
                scan.Add(albion);
                scan.Add(midgard);
                // #599 step 2: once a second, each group's published plan; the seconds both groups held a call, and those
                // they held the same one, and those the follower held it because it took the lead's call (CallFromBattlegroup: 0
                // by construction without the shared call, so a revert fails scenario 5).
                List<GameBot> lead = battlegroup != null && battlegroup.LeadForceId == ForceOf(second[0]) ? second : first;
                List<GameBot> follower = ReferenceEquals(lead, first) ? second : first;
                int bothCalled = 0, sharedCall = 0, battlegroupCall = 0;
                long nextSample = 0;
                var callLines = new List<string>();
                world.Run(seconds, w =>
                {
                    scan.Tick(w.Now);
                    if (w.Now < nextSample)
                        return;
                    nextSample = w.Now + 1_000;
                    RvrFightPlan leadPlan = PlanOf(lead), followerPlan = PlanOf(follower);
                    if (leadPlan?.CalledTarget == null || followerPlan?.CalledTarget == null)
                        return;
                    bothCalled++;
                    bool same = leadPlan.CalledTarget == followerPlan.CalledTarget;
                    if (same)
                        sharedCall++;
                    if (same && followerPlan.CallFromBattlegroup)
                        battlegroupCall++;
                    if (callLines.Count < 12)
                        callLines.Add($"call {S(w.Now)} lead={Short(leadPlan.CalledTarget)} follower={Short(followerPlan.CalledTarget)} " +
                                      $"from_battlegroup={followerPlan.CallFromBattlegroup} same={same}");
                });
                var followerPicks = scan.Picks.Where(p => follower.Contains(p.Bot)).ToList();
                int battlegroupPicks = followerPicks.Count(p => p.Rule == "battlegroup");

                int albionAlive = albion.Count(bot => bot.IsAlive), midgardAlive = midgard.Count(bot => bot.IsAlive);
                string row = $"RVR_REHEARSAL battlegroup seed={seed} sides=Alb8+Alb8 vs Mid8 battlegroup={battlegroup?.Id ?? "none"} " +
                             $"groups={linked.Count} heads={linked.Count * 8} pair_linked={In(pair[0])} far_linked={In(far[0])} " +
                             $"roster={string.Join("/", rosters)} midgard_in_roster={midgardInRosters} " +
                             $"shared_call={sharedCall}/{bothCalled}s battlegroup_call={battlegroupCall}s follower_first_pick=battlegroup:{battlegroupPicks}/{followerPicks.Count} " +
                             $"standing=Alb:{albionAlive}/16 Mid:{midgardAlive}/8 after={seconds:0}s";
                var detail = new List<string>();
                detail.AddRange(callLines);
                detail.AddRange(followerPicks.Take(8).Select(p => $"follower pick {S(p.At)} {p.Bot.Name} -> {Short(p.Target)} rule={p.Rule}"));
                detail.AddRange(changes.Select(change => $"{change.What} {change.Id} force={change.ForceId} why={change.Why}"));
                detail.AddRange(world.Recorder.Deaths.Select(d => $"death {S(d.At)} {d.Victim.Name} ({RvrRoles.For(ClassOf(d.Victim))}) by {d.Killer?.Name}"));
                detail.AddRange(world.Errors.Take(10).Select(e => "error: " + e));
                return new BattlegroupResult(row, detail, world.ServiceErrors, battlegroup?.Id, battlegroup?.LeadForceId, linked,
                    In(pair[0]), In(far[0]), AutonomousRvrEventLayer.BattlegroupOf(ForceOf(midgard[0])) != null, rosters, midgardInRosters,
                    albionAlive, midgardAlive, bothCalled, sharedCall, battlegroupPicks, followerPicks.Count, battlegroupCall);
            }
            finally
            {
                AutonomousBotGroupCoordinator.ResetBattlegroups();
                RealmWarbandSupport.Pulse(world.Now, immediately: true);
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scenario 6 (#599 step 3): three Albion eights, one far beyond the battlegroup link, form the realm's zerg (24v8)

        public sealed record ZergResult(string Row, IReadOnlyList<string> Detail, int Errors, string ZergId, string LeadForce,
            IReadOnlyList<string> Members, bool FarJoined, bool MidgardJoined, int[] RosterSizes, int MidgardInRosters,
            int AlbionAlive, int MidgardAlive, string FewerId);

        /// <summary>
        /// Three Albion eights on the Hadrian's Wall stand-in: two 1,800 apart and a third 12,000 west, beyond the battlegroup's
        /// 3,000 link. A Midgard eight stands 2,600 east of the first. The realm's online and free frontier gamebots are stood in at 72 (the
        /// rehearsal places no full roster), so the zerg's target is 24. The coordinator's battlegroup pulse and the support
        /// rosters run once (AutonomousBotGroupCoordinator.PulseBattlegroups, RealmWarbandSupport.Pulse); then the fight runs.
        /// A second pulse from scratch with the stand-in at 26 (a third is 8, under the floor of 16, the schedule off) shows
        /// what forms instead (FewerId). The row shows who joined, each Albion bot's support roster and the bots standing at the
        /// end. The outcome is reported, not judged.
        /// </summary>
        public static ZergResult Zerg(int seed, double seconds = 60)
        {
            using var world = new RvrFightWorld(seed, Repeatable);
            AutonomousBotGroupCoordinator.ResetBattlegroups();
            try
            {
                Region region = world.AddRegion(FrontierRegion, FrontierZone, "Hadrian's Wall flat stand-in (rehearsal)");
                List<GameBot> first = Eight(world, region, eRealm.Albion, 30_000, 30_000, 3072, 50, "A", out _);
                List<GameBot> second = Eight(world, region, eRealm.Albion, 28_200, 30_000, 3072, 50, "B", out _);
                List<GameBot> far = Eight(world, region, eRealm.Albion, 18_000, 30_000, 3072, 50, "F", out _);
                List<GameBot> midgard = Eight(world, region, eRealm.Midgard, 32_600, 30_000, 1024, 50, "M", out _);
                string ForceOf(GameBot bot) => AutonomousBotGroupCoordinator.RvrForceId(bot);

                var fewer = AutonomousBotGroupCoordinator.PulseBattlegroups(world.Now, force: true,
                    new Dictionary<eRealm, int> { [eRealm.Albion] = 26, [eRealm.Midgard] = 8 });
                string fewerId = AutonomousRvrEventLayer.BattlegroupOf(ForceOf(first[0]))?.Id ?? "none";
                AutonomousBotGroupCoordinator.ResetBattlegroups();

                List<AutonomousRvrEventLayer.BattlegroupChange> changes =
                    AutonomousBotGroupCoordinator.PulseBattlegroups(world.Now, force: true,
                        new Dictionary<eRealm, int> { [eRealm.Albion] = 72, [eRealm.Midgard] = 8 }) ??
                    new List<AutonomousRvrEventLayer.BattlegroupChange>();
                RealmWarbandSupport.Pulse(world.Now, immediately: true);
                AutonomousRvrEventLayer.BattlegroupView zerg = AutonomousRvrEventLayer.BattlegroupOf(ForceOf(first[0]));
                IReadOnlyList<string> members = zerg?.Members ?? Array.Empty<string>();
                List<GameBot> albion = first.Concat(second).Concat(far).ToList();
                int[] rosters = albion.Select(bot => RealmWarbandSupport.Get(bot)?.Members.Length ?? 0).Distinct().OrderBy(n => n).ToArray();
                int midgardInRosters = albion.SelectMany(bot => RealmWarbandSupport.Get(bot)?.Members ?? Array.Empty<GameLiving>())
                    .Distinct().Count(member => member.Realm == eRealm.Midgard);
                AutonomousRvrEventLayer.ZergOrder order = AutonomousRvrEventLayer.ZergOrderOf(ForceOf(first[0]));

                var scan = new RvrFightScan(seed);
                scan.Add(albion);
                scan.Add(midgard);
                world.Run(seconds, w => scan.Tick(w.Now));

                int albionAlive = albion.Count(bot => bot.IsAlive), midgardAlive = midgard.Count(bot => bot.IsAlive);
                string row = $"RVR_REHEARSAL zerg seed={seed} sides=Alb8+Alb8+Alb8 vs Mid8 free=72 zerg={zerg?.Id ?? "none"} " +
                             $"groups={members.Count} heads={members.Count * 8} far_joined={members.Contains(ForceOf(far[0]))} " +
                             $"gate={order?.Anchor?.SpotId ?? "-"} roster={string.Join("/", rosters)} midgard_in_roster={midgardInRosters} " +
                             $"free26={fewerId} standing=Alb:{albionAlive}/24 Mid:{midgardAlive}/8 after={seconds:0}s";
                var detail = new List<string>();
                detail.AddRange((fewer ?? new List<AutonomousRvrEventLayer.BattlegroupChange>())
                    .Select(change => $"free=26: {change.What} {change.Id} force={change.ForceId} why={change.Why}"));
                detail.AddRange(changes.Select(change => $"free=72: {change.What} {change.Id} force={change.ForceId} why={change.Why}"));
                detail.AddRange(world.Recorder.Deaths.Select(d => $"death {S(d.At)} {d.Victim.Name} ({RvrRoles.For(ClassOf(d.Victim))}) by {d.Killer?.Name}"));
                detail.AddRange(world.Errors.Take(10).Select(e => "error: " + e));
                return new ZergResult(row, detail, world.ServiceErrors, zerg?.IsZerg == true ? zerg.Id : null, zerg?.LeadForceId, members,
                    members.Contains(ForceOf(far[0])), AutonomousRvrEventLayer.BattlegroupOf(ForceOf(midgard[0])) != null, rosters,
                    midgardInRosters, albionAlive, midgardAlive, fewerId);
            }
            finally
            {
                AutonomousBotGroupCoordinator.ResetBattlegroups();
                RealmWarbandSupport.Pulse(world.Now, immediately: true);
            }
        }

        private static RvrFightPlan PlanOf(List<GameBot> side)
        {
            GameBot asker = side.FirstOrDefault(b => b.IsAlive);
            return asker == null ? null : RvrFightBoards.PeekPlan(asker);
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

        // ---------------------------------------------------------------------------------------------------------
        // #529 step 3: the fight board from contacts. Every brain is stopped, so only the rehearsal's scan (seeded) looks
        // and what the board lists is the contacts' doing alone.

        /// <summary>One board case: whether the board listed each named enemy, the refresh's cost, the row and its detail.</summary>
        public sealed record BoardCaseResult(string Row, IReadOnlyList<string> Detail, int Errors, IReadOnlyDictionary<string, bool> Listed,
            int Refreshes, int RefreshRays, int MaxRaysPerRefresh, int HitsBy);

        private static List<GameBot> AlbionTrio(RvrFightWorld world, Region region, string tag)
        {
            eCharacterClass[] classes = [eCharacterClass.Armsman, eCharacterClass.Cleric, eCharacterClass.Wizard];
            var trio = new List<GameBot>();
            for (int i = 0; i < classes.Length; i++)
            {
                GameBot bot = world.NewBot(eRealm.Albion, classes[i], 50, $"{tag}{i + 1:00}{classes[i]}");
                world.Place(bot, region, 30_000, 30_000 + i * 100, 0, 3072);
                trio.Add(bot);
            }
            world.Form(trio.Cast<GameLiving>().ToList());
            return trio;
        }

        private static bool Lists(RvrFightBoard board, GameLiving enemy) => board != null && board.Enemies.Any(e => e.Living == enemy);

        private static string BoardEnemies(RvrFightBoard board) => board == null ? "none" :
            $"from={(board.FromContacts ? "contacts" : "area")} enemies=[{string.Join(",", board.Enemies.Select(e => e.Living?.Name ?? "?"))}]";

        /// <summary>
        /// Scenario 4: an Albion trio is fighting a Midgard warrior (each holds it on its aggro list, so no member scans),
        /// and a Midgard healer walks up 800 away mid-fight and never hits anyone. With the in-combat refresh
        /// (<paramref name="combatRefresh"/>) the trio notices both and its board lists them; without it neither is anyone's
        /// contact (no hit, no call), so a board from contacts would refuse both: the step-3 caveat of #529.
        /// </summary>
        public static BoardCaseResult MidFightWatcher(int seed, bool combatRefresh, double seconds = 5)
        {
            using var world = new RvrFightWorld(seed);
            Region region = world.AddRegion(FrontierRegion, FrontierZone, "Hadrian's Wall flat stand-in (rehearsal)");
            List<GameBot> trio = AlbionTrio(world, region, "W");
            GameBot fighter = world.NewBot(eRealm.Midgard, eCharacterClass.Warrior, 50, "WFighterWarrior");
            world.Place(fighter, region, 30_150, 30_100, 0, 1024);
            foreach (GameBot bot in trio.Append(fighter))
                bot.Brain?.Stop();
            foreach (GameBot bot in trio)
                ((BotBrain)bot.Brain).AddToAggroList(fighter, 100);
            var scan = new RvrFightScan(seed) { CombatRefresh = combatRefresh };
            scan.Add(trio);
            world.Run(1, w => scan.Tick(w.Now));
            GameBot watcher = world.NewBot(eRealm.Midgard, eCharacterClass.Healer, 50, "WWatcherHealer");
            world.Place(watcher, region, 30_800, 30_100, 0, 1024);
            watcher.Brain?.Stop();
            world.Run(seconds, w => scan.Tick(w.Now));
            RvrFightBoard board = RvrFightBoards.Get(trio[0]);
            bool fighting = trio.All(b => ((BotBrain)b.Brain).HasAggro);
            int hitsBy = world.Recorder.Hits.Count(h => h.Attacker == watcher);
            var listed = new Dictionary<string, bool> { ["watcher"] = Lists(board, watcher), ["fighter"] = Lists(board, fighter) };
            var detail = new List<string>
            {
                $"board: {BoardEnemies(board)}; trio still fighting (no member scanned): {fighting}",
                $"in-combat refresh {(combatRefresh ? "on" : "off")}: looks {scan.Refreshes}, rays {scan.RefreshRays}, most rays in one look {scan.MaxRaysPerRefresh}",
            };
            detail.AddRange(world.Errors.Take(5).Select(e => "error: " + e));
            string row = $"RVR_REHEARSAL board_mid_fight seed={seed} refresh={(combatRefresh ? "on" : "off")} watcher_listed={listed["watcher"]} " +
                         $"fighter_listed={listed["fighter"]} watcher_hits={hitsBy} looks={scan.Refreshes} rays={scan.RefreshRays}";
            return new BoardCaseResult(row, detail, world.ServiceErrors, listed, scan.Refreshes, scan.RefreshRays, scan.MaxRaysPerRefresh, hitsBy);
        }

        /// <summary>
        /// Scenario 5: an Albion trio stands in the open; a Midgard warrior stands 1,200 east behind a wall nobody can see
        /// through, and a Midgard savage 800 west in the open. With the board from contacts
        /// (<paramref name="boardFromContacts"/>) the board lists the savage (noticed) and not the warrior (never seen);
        /// with the switch off the area search lists both, as before 1.2.3.
        /// </summary>
        public static BoardCaseResult BehindAWall(int seed, bool boardFromContacts, double seconds = 6)
        {
            using var world = new RvrFightWorld(seed, new Dictionary<string, object> { ["rvr_board_from_contacts"] = boardFromContacts });
            Region region = world.AddRegion(FrontierRegion, FrontierZone, "Hadrian's Wall flat stand-in (rehearsal)");
            world.Nav.Walls.Add(new FlatNavmesh.Wall(30_600, 28_000, 30_600, 32_000));
            List<GameBot> trio = AlbionTrio(world, region, "B");
            GameBot hidden = world.NewBot(eRealm.Midgard, eCharacterClass.Warrior, 50, "BHiddenWarrior");
            world.Place(hidden, region, 31_200, 30_100, 0, 1024);
            GameBot open = world.NewBot(eRealm.Midgard, eCharacterClass.Savage, 50, "BOpenSavage");
            world.Place(open, region, 29_200, 30_100, 0, 3072);
            foreach (GameBot bot in trio.Append(hidden).Append(open))
                bot.Brain?.Stop();
            var scan = new RvrFightScan(seed) { Engage = false };
            scan.Add(trio);
            world.Run(seconds, w => scan.Tick(w.Now));
            RvrFightBoard board = RvrFightBoards.Get(trio[0]);
            var listed = new Dictionary<string, bool> { ["hidden"] = Lists(board, hidden), ["open"] = Lists(board, open) };
            bool hiddenKnown = trio.Any(b => AutonomousWorldBotController.RvrContactTable.Knows(b.DatabaseID, RvrPartyKey.TargetKey(hidden), world.Now));
            var detail = new List<string>
            {
                $"board: {BoardEnemies(board)}; behind the wall a contact of anyone: {hiddenKnown}; notices {scan.Notices.Count}",
            };
            detail.AddRange(world.Errors.Take(5).Select(e => "error: " + e));
            string row = $"RVR_REHEARSAL board_behind_wall seed={seed} from={(boardFromContacts ? "contacts" : "area")} " +
                         $"hidden_listed={listed["hidden"]} open_listed={listed["open"]}";
            return new BoardCaseResult(row, detail, world.ServiceErrors, listed, scan.Refreshes, scan.RefreshRays, scan.MaxRaysPerRefresh, 0);
        }

        // ---------------------------------------------------------------------------------------------------------
        // #531 step 2c-2: Perfect Recovery and Vanish through the server's own spell, effect and stealth code. Every brain is
        // stopped, so only the abilities act: the rows show the bot-side executors work on live GameBots (the handlers are
        // written for players only).

        /// <summary>What the escape-and-rez case saw (each a yes/no the test asserts) and its row.</summary>
        public sealed record EscapeAndRezResult(string Row, IReadOnlyList<string> Detail, int Errors,
            bool Raised, int RaisedHealthPercent, long PrTimerMs, bool SecondRaisedByPr,
            bool Vanished, int EnemySeeRange, bool HiddenAfterHit, bool VanishEndedOnUnhide,
            bool RetreatVanished, bool IdleKeptVanishHidden, bool IdleUnhidesWithoutVanish,
            bool GamebotHiddenAfterMiss, bool GamebotUncoveredByHit, bool CompanionIsCompanion, bool CompanionUncoveredByMiss,
            bool TrueSightOn, long TrueSightTimerMs, double TrueSightChanceAt1200, double NoTrueSightChanceAt1200, int TrueSightCandidateRange);

        private static void Miss(GameLiving attacker, GameLiving victim) => victim.OnAttackedByEnemy(new AttackData
        {
            Attacker = attacker, Target = victim, AttackType = AttackData.eAttackType.MeleeOneHand,
            AttackResult = eAttackResult.Missed, Damage = 0, Interval = 3_000,
        });

        private static readonly System.Reflection.BindingFlags Private =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        private static void Hit(GameLiving attacker, GameLiving victim, int damage) => victim.OnAttackedByEnemy(new AttackData
        {
            Attacker = attacker, Target = victim, AttackType = AttackData.eAttackType.MeleeOneHand,
            AttackResult = eAttackResult.HitUnstyled, Damage = damage, Interval = 3_000,
        });

        /// <summary>
        /// Scenario 7: (a) a Midgard Healer, Warrior and Savage in a fight with an Albion Armsman; the Warrior falls, and the
        /// Healer's Perfect Recovery (through its own realm-ability turn) raises it at once at full health and goes on its
        /// 30-minute timer; then the Savage falls, and the timer keeps it down. (b) An Albion Infiltrator hit by two Midgard
        /// bots starts a run: its Vanish hides it beside them, no enemy's stealth rule sees it, a hit leaves it hidden, and
        /// its own unhide ends Vanish.
        /// </summary>
        public static EscapeAndRezResult EscapeAndRez(int seed)
        {
            using var world = new RvrFightWorld(seed);
            Region region = world.AddRegion(FrontierRegion, FrontierZone, "Hadrian's Wall flat stand-in (rehearsal)");
            var detail = new List<string>();

            // (a) Perfect Recovery
            GameBot healer = world.NewBot(eRealm.Midgard, eCharacterClass.Healer, 50, "RHealer");
            GameBot warrior = world.NewBot(eRealm.Midgard, eCharacterClass.Warrior, 50, "RWarrior");
            GameBot savage = world.NewBot(eRealm.Midgard, eCharacterClass.Savage, 50, "RSavage");
            GameBot armsman = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "RArmsman");
            world.Place(healer, region, 40_000, 40_000, 0, 1024);
            world.Place(warrior, region, 40_400, 40_000, 0, 1024);
            world.Place(savage, region, 40_400, 40_200, 0, 1024);
            world.Place(armsman, region, 40_550, 40_100, 0, 3072);
            world.Form(new List<GameLiving> { healer, warrior, savage });
            foreach (GameBot bot in new[] { healer, warrior, savage, armsman })
                bot.Brain?.Stop();
            new RvrFightActives().Grant(new[] { healer });
            var pr = healer.GetAbility(RvrRealmActives.PerfectRecovery) as DOL.GS.RealmAbilities.TimedRealmAbility;
            Hit(armsman, healer, 1);
            warrior.TakeDamage(armsman, eDamageType.Slash, warrior.Health + 10, 0);
            // past the realm-ability turn's once-a-second pace (a hit can wake the healer's turn)
            world.Run(1.1);
            bool warriorDown = !warrior.IsAlive;
            detail.Add($"pr before: tactics={BotBrain.RvrTacticsApplyTo(healer)} wanted={RvrRealmActives.Wanted(healer, RvrRealmActives.PerfectRecovery)} " +
                       $"target={RvrRealmActives.PerfectRecoveryTarget(healer)?.Name ?? "-"} warriorState={warrior.ObjectState} " +
                       $"warriorInGroup={healer.Group?.IsInTheGroup(warrior)} healerInCombat={healer.InCombat} casting={healer.IsCasting} " +
                       $"scope={RvrTacticsScope.Refusal(RvrTacticsScope.FactsOf(healer))}");
            AutonomousBotRealmAbilities.UseActives(healer);
            bool queued = healer.castingComponent.HasPendingSkillRequests;
            world.Run(0.05);
            string handlerSeen = $"{healer.castingComponent.SpellHandler?.Spell?.Name ?? "-"}/{healer.castingComponent.SpellHandler?.CastState}";
            world.Run(1.5);
            bool raised = warriorDown && warrior.IsAlive;
            detail.Add($"pr after: queued={queued} handler={handlerSeen} casting={healer.IsCasting} warriorAlive={warrior.IsAlive} " +
                       $"warriorState={warrior.ObjectState} disabled={(pr != null ? healer.GetSkillDisabledDuration(pr) : -1)}");
            long timer = pr != null ? healer.GetSkillDisabledDuration(pr) : 0;
            detail.Add($"pr: healer has it={pr != null} warrior down={warriorDown} raised={raised} health={warrior.HealthPercent}% timer={timer / 1000}s");
            savage.TakeDamage(armsman, eDamageType.Slash, savage.Health + 10, 0);
            world.Run(1.2);
            Hit(armsman, healer, 1);
            AutonomousBotRealmAbilities.UseActives(healer);
            world.Run(1.5);
            bool secondRaised = savage.IsAlive;
            detail.Add($"pr: savage down, raised by Perfect Recovery again={secondRaised}");

            // (b) Vanish
            GameBot infiltrator = world.NewBot(eRealm.Albion, eCharacterClass.Infiltrator, 50, "VInfiltrator");
            GameBot hunterA = world.NewBot(eRealm.Midgard, eCharacterClass.Berserker, 50, "VBerserker");
            GameBot hunterB = world.NewBot(eRealm.Midgard, eCharacterClass.Warrior, 50, "VWarrior");
            world.Place(infiltrator, region, 50_000, 50_000, 0, 1024);
            world.Place(hunterA, region, 50_100, 50_000, 0, 3072);
            world.Place(hunterB, region, 50_000, 50_100, 0, 0);
            foreach (GameBot bot in new[] { infiltrator, hunterA, hunterB })
                bot.Brain?.Stop();
            new RvrFightActives().Grant(new[] { infiltrator });
            Hit(hunterA, infiltrator, 100);
            Hit(hunterB, infiltrator, 100);
            bool fired = RvrRealmActives.TryVanishOnRetreat(infiltrator);
            world.Run(0.5);
            bool vanished = fired && RvrRealmActives.Vanished(infiltrator);
            int seeRange = DOL.GS.ServerRules.StealthRules.SeeRange(DOL.GS.ServerRules.StealthRules.LookOf(hunterA, infiltrator));
            Hit(hunterA, infiltrator, 100);
            world.Run(0.2);
            bool hiddenAfterHit = infiltrator.IsStealthed;
            infiltrator.Stealth(false);
            world.Run(0.5);
            bool endedOnUnhide = !infiltrator.effectListComponent.ContainsEffectForEffectType(eEffect.Vanish);
            detail.Add($"vanish: fired={fired} hidden={vanished} enemy see range={seeRange} hidden after a hit={hiddenAfterHit} " +
                       $"ended on unhide={endedOnUnhide}");

            // (c) the brain's own wiring: a retreat's start fires Vanish (BotBrain.BeginRetreat), and the idle unhide leaves a
            // vanished bot hidden while it unhides one without Vanish (BotBrain.TryEnterStealthWhileIdle)
            GameBot runner = world.NewBot(eRealm.Albion, eCharacterClass.Infiltrator, 50, "WInfiltrator");
            world.Place(runner, region, 52_000, 52_000, 0, 1024);
            runner.Brain?.Stop();
            new RvrFightActives().Grant(new[] { runner });
            Hit(hunterA, runner, 100);
            var runnerBrain = (BotBrain)runner.Brain;
            typeof(BotBrain).GetMethod("BeginRetreat", Private)!.Invoke(runnerBrain,
                new object[] { runner, 777L, new System.Numerics.Vector3(54_000, 52_000, 0), "solo" });
            world.Run(0.5);
            bool retreatVanished = RvrRealmActives.Vanished(runner);
            System.Reflection.MethodInfo idle = typeof(BotBrain).GetMethod("TryEnterStealthWhileIdle", Private)!;
            idle.Invoke(runnerBrain, null);
            bool idleKept = runner.IsStealthed;
            GameBot plain = world.NewBot(eRealm.Albion, eCharacterClass.Infiltrator, 50, "UInfiltrator");
            world.Place(plain, region, 56_000, 56_000, 0, 1024);
            plain.Brain?.Stop();
            plain.Flags |= GameNPC.eFlags.STEALTH;
            idle.Invoke((BotBrain)plain.Brain, null);
            bool idleUnhides = !plain.IsStealthed;
            detail.Add($"wiring: retreat vanished={retreatVanished} idle kept the vanished bot hidden={idleKept} " +
                       $"idle unhid one without Vanish={idleUnhides}");

            // (d) the uncover rule: an autonomous gamebot by the player's rule (a hit uncovers, a miss doesn't); a companion as
            // before (any attack uncovers it; #521)
            plain.Flags |= GameNPC.eFlags.STEALTH;
            GameBot enemy = world.NewBot(eRealm.Midgard, eCharacterClass.Warrior, 50, "UWarrior");
            world.Place(enemy, region, 56_100, 56_000, 0, 3072);
            enemy.Brain?.Stop();
            Miss(enemy, plain);
            bool hiddenAfterMiss = plain.IsStealthed;
            Hit(enemy, plain, 50);
            bool uncoveredByHit = !plain.IsStealthed;
            GameBot companion = world.NewBot(eRealm.Albion, eCharacterClass.Infiltrator, 50, "UCompanion");
            world.Place(companion, region, 56_000, 56_200, 0, 1024);
            companion.Brain?.Stop();
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(companion, true);
            bool isCompanion = DOL.GS.ServerRules.PvpRules.KindOf(companion) == DOL.GS.ServerRules.PvpCharacterKind.Companion;
            companion.Flags |= GameNPC.eFlags.STEALTH;
            Miss(enemy, companion);
            bool companionUncovered = !companion.IsStealthed;
            detail.Add($"uncover: gamebot hidden after a miss={hiddenAfterMiss} uncovered by a hit={uncoveredByHit} " +
                       $"companion kind ok={isCompanion} companion uncovered by a miss={companionUncovered}");

            // (e) True Sight: an enemy Infiltrator strikes a Hunter's group mate; the Hunter's turn puts True Sight on, and while it
            // runs the Hunter's stealth rule sees the hidden Infiltrator at 1,200 at once (owner answer 71A), its mate's doesn't
            GameBot hunter = world.NewBot(eRealm.Midgard, eCharacterClass.Hunter, 50, "THunter");
            GameBot mate = world.NewBot(eRealm.Midgard, eCharacterClass.Warrior, 50, "TWarrior");
            GameBot assassin = world.NewBot(eRealm.Albion, eCharacterClass.Infiltrator, 50, "TInfiltrator");
            world.Place(hunter, region, 60_000, 60_000, 0, 1024);
            world.Place(mate, region, 60_300, 60_000, 0, 1024);
            world.Place(assassin, region, 60_400, 60_000, 0, 3072);
            world.Form(new List<GameLiving> { hunter, mate });
            foreach (GameBot bot in new[] { hunter, mate, assassin })
                bot.Brain?.Stop();
            new RvrFightActives().Grant(new[] { hunter });
            var ts = hunter.GetAbility(RvrRealmActives.TrueSight) as DOL.GS.RealmAbilities.TimedRealmAbility;
            Hit(assassin, mate, 200);
            world.Run(1.1);
            AutonomousBotRealmAbilities.UseActives(hunter);
            world.Run(0.3);
            bool tsOn = hunter.effectListComponent.ContainsEffectForEffectType(eEffect.TrueSight);
            long tsTimer = ts != null ? hunter.GetSkillDisabledDuration(ts) : 0;
            assassin.MoveTo(region.ID, 61_200, 60_000, 0, 3072);
            assassin.Flags |= GameNPC.eFlags.STEALTH;
            world.Run(0.1);
            double tsChance = RvrStealthNotice.ChancePerSecond(AutonomousWorldBotController.StealthSighting(hunter, assassin, true), RvrBotAwareness.Normal);
            double mateChance = RvrStealthNotice.ChancePerSecond(AutonomousWorldBotController.StealthSighting(mate, assassin, true), RvrBotAwareness.Normal);
            int candidate = AutonomousWorldBotController.StealthCandidateRange(hunter);
            detail.Add($"true sight: has it={ts != null} on={tsOn} timer={tsTimer / 1000}s hunter chance at {(int)hunter.GetDistanceTo(assassin)}={tsChance} " +
                       $"mate chance={mateChance} candidate range={candidate}");
            detail.AddRange(world.Errors.Take(5).Select(e => "error: " + e));
            string row = $"RVR_REHEARSAL realm_abilities_escape_rez seed={seed} pr_raised={raised}:{warrior.HealthPercent}% " +
                         $"pr_timer={timer / 60_000}min pr_second={secondRaised} vanish={vanished} see={seeRange} " +
                         $"hidden_after_hit={hiddenAfterHit} vanish_ends_on_unhide={endedOnUnhide} retreat_vanish={retreatVanished} " +
                         $"idle_keeps={idleKept} miss_keeps_gamebot={hiddenAfterMiss} hit_uncovers_gamebot={uncoveredByHit} " +
                         $"miss_uncovers_companion={companionUncovered} ts={tsOn}:{tsTimer / 60_000}min ts_sees_1200={tsChance}";
            return new EscapeAndRezResult(row, detail, world.ServiceErrors, raised, warrior.HealthPercent, timer, secondRaised,
                vanished, seeRange, hiddenAfterHit, endedOnUnhide, retreatVanished, idleKept, idleUnhides,
                hiddenAfterMiss, uncoveredByHit, isCompanion, companionUncovered, tsOn, tsTimer, tsChance, mateChance, candidate);
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
