using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Summoner Pain's split camps (Living Server 1.2, owner's decision 2026-11-18: "Add a split-camp tactic"; tests only).
    /// <para>Why: owner's PC, build b1b6846, 2026-11-18 16:37-16:41 (Summoner's Hall, Midgard): Summoner Roesia's Summoner
    /// Pain (spirit damage over time, 150 every 3 s for 36 s, radius 1,000 around a random raider within 2,000, recast every
    /// 20 s) hit 71 raiders for 124,913 damage in the first minute (669 hits), because the raid stood together; 84 died to
    /// her and the attempt wiped 77 of 77.</para>
    /// <para>What: while a boss that carries a spell named <see cref="SummonerPainSpell"/> is fought (gated on the spell, not
    /// the name: Roesia is the only one in the three halls today), the raid's parties are split evenly between two camps on
    /// opposite sides of her spawn, <see cref="PainCampFromSpawn"/> out (inside her home and fight-at-home radius of 1,000,
    /// outside her aggro of 600), so the camps lie about 1,840 apart (the owner asked for about 1,600; the melee need the rest) and one cast (radius 1,000) centred in one camp never
    /// reaches the other. Each camp has healers (<see cref="AssignPainCamps"/>). A party's casters, archers and healers fight
    /// from its camp's post; its melee fight her from her side facing their camp (<see cref="PainCampMeleeSpot"/>), so a cast
    /// on one camp's back line reaches only its own half. A cast on the melee still reaches all the melee and the camp they
    /// face (melee all stand within about 400 of each other): the model in UT_RaidSweep6f puts the average at a little over
    /// half of the raid per cast, against all of it before.</para>
    /// <para>She is unchanged: her script, Summoner Pain, her health and damage are not touched. Bots cure the damage over
    /// time with their cure poison (BotBrain; Summoner Pain is a DamageOverTime spell, which CurePoison removes), now logged
    /// as RAID_CURE_POISON (RaidCureLog).</para>
    /// Autonomous gamebots in a realm raid only.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>The spell the camps answer (SummonerRoesia.cs).</summary>
        public const string SummonerPainSpell = "Summoner Pain";
        /// <summary>Each camp's post lies this far from her spawn: inside her fight-at-home radius (1,000) with
        /// <see cref="PainCampTolerance"/> to spare, outside her aggro (600), and far enough out that the melee on her other
        /// side (<see cref="RingRadius"/> from her) stand more than her Pain's 1,000 from this camp's back line
        /// (920 - 50 + 190 = 1,060). At 800 (camps 1,600 apart) they stood within it, and a cast on either camp reached the
        /// other camp's melee too (the model in UT_RaidSweep6f: 50 of 72 per cast instead of about 41).</summary>
        public const int PainCampFromSpawn = 920;
        /// <summary>A camp raider within this of its post is at it.</summary>
        public const int PainCampTolerance = 50;
        /// <summary>Raiders within this of the boss take their camp.</summary>
        public const int PainCampReach = 2_600;
        /// <summary>A boss nobody has fought for this long has no camps any more.</summary>
        public const long PainCampWatchMilliseconds = 10_000;
        /// <summary>The camps' axis is tried at this many turns (15 degrees apart) until both posts have floor in her sight.</summary>
        public const int PainCampTurns = 12;

        /// <summary>The boss carries Summoner Pain (pure: its spells' names).</summary>
        public static bool HasPainSpell(IEnumerable<string> spellNames) =>
            (spellNames ?? []).Any(n => string.Equals(n, SummonerPainSpell, StringComparison.OrdinalIgnoreCase));

        private static bool HasSummonerPain(GameNPC npc)
        {
            List<Spell> spells = npc?.Spells;
            if (spells == null) return false;
            lock (spells) return HasPainSpell(spells.Where(s => s != null).Select(s => s.Name));
        }

        /// <summary>The camps fit (pure): both posts lie inside the boss's fight-at-home radius with the tolerance to spare,
        /// outside its aggro, and further apart than the Pain's radius plus the tolerance on each side.</summary>
        public static bool PainCampsFit(int fightAtHomeRadius, int aggroRange, int fromSpawn, int painRadius) =>
            fightAtHomeRadius > 0 && fromSpawn + PainCampTolerance <= fightAtHomeRadius &&
            fromSpawn - PainCampTolerance > aggroRange && 2 * (fromSpawn - PainCampTolerance) > painRadius;

        /// <summary>
        /// The two camp posts (pure): <paramref name="fromSpawn"/> from <paramref name="spawn"/> on opposite sides, on the line
        /// across the raid's way in (perpendicular to the spawn's direction to <paramref name="raid"/>, the raid's middle),
        /// turned by <paramref name="turnRadians"/>.
        /// </summary>
        public static (Vector3 A, Vector3 B) PainCampPair(Vector3 spawn, Vector3 raid, double turnRadians, float fromSpawn)
        {
            Vector2 toward = new(raid.X - spawn.X, raid.Y - spawn.Y);
            double baseAngle = toward.LengthSquared() < 1f ? 0 : Math.Atan2(toward.Y, toward.X);
            double a = baseAngle + Math.PI / 2 + turnRadians;
            var d = new Vector3((float)Math.Cos(a) * fromSpawn, (float)Math.Sin(a) * fromSpawn, 0);
            return (spawn + d, spawn - d);
        }

        /// <summary>
        /// Which camp each party takes (pure; 0 or 1), in the raid's order: every second party, then, when one camp has no
        /// healer, a party with healers from the other camp (one that leaves it a healer) swaps with one without from this
        /// camp, or moves when there is none to swap. <paramref name="healersPerParty"/>: each party's living healers.
        /// </summary>
        public static int[] AssignPainCamps(IReadOnlyList<int> healersPerParty)
        {
            int n = healersPerParty?.Count ?? 0;
            var camp = new int[n];
            for (int i = 0; i < n; i++) camp[i] = i % 2;
            for (int c = 0; c < 2; c++)
            {
                int Healers(int k) => Enumerable.Range(0, n).Where(i => camp[i] == k).Sum(i => healersPerParty[i]);
                if (Healers(c) > 0 || Healers(1 - c) == 0) continue;
                int other = 1 - c;
                int give = Enumerable.Range(0, n).Where(i => camp[i] == other && healersPerParty[i] > 0 &&
                                                             Healers(other) - healersPerParty[i] > 0)
                    .DefaultIfEmpty(-1).First();
                if (give < 0) continue;
                int take = Enumerable.Range(0, n).Where(i => camp[i] == c && healersPerParty[i] == 0).DefaultIfEmpty(-1).First();
                camp[give] = c;
                if (take >= 0) camp[take] = other;
            }
            return camp;
        }

        /// <summary>Where a camp's melee fight the boss (pure): <paramref name="radius"/> from her, on her side facing their
        /// camp.</summary>
        public static Vector3 PainCampMeleeSpot(Vector3 boss, Vector3 camp, float radius)
        {
            Vector2 d = new(camp.X - boss.X, camp.Y - boss.Y);
            if (d.LengthSquared() < 1f) return boss;
            d = Vector2.Normalize(d) * radius;
            return new Vector3(boss.X + d.X, boss.Y + d.Y, boss.Z);
        }

        private sealed class PainCampPlan
        {
            public string RaidId;
            public Vector3 A, B;
            public readonly Dictionary<Group, int> Camps = new();
        }

        private static readonly ConcurrentDictionary<GameNPC, long> PainWatch = new();
        private static readonly Dictionary<GameNPC, PainCampPlan> PainPlans = new();

        /// <summary>A raider fights <paramref name="target"/>: if it carries Summoner Pain, its camps are on.</summary>
        private static void WatchPainCamps(GameNPC target, long now)
        {
            if (target.InCombat && For(target) is { FightAtHomeRadius: > 0 } && HasSummonerPain(target))
            {
                if (PainWatch.Count > 64) PainWatch.Clear();
                PainWatch[target] = now;
            }
        }

        private static int PainRadiusOf(GameNPC boss)
        {
            List<Spell> spells = boss.Spells;
            if (spells == null) return 0;
            lock (spells)
                return (int)(spells.Where(s => s != null && string.Equals(s.Name, SummonerPainSpell, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Radius).DefaultIfEmpty(0).Max());
        }

        /// <summary>The camp plan of <paramref name="boss"/>'s fight for this raid (built once; parties arriving later go to
        /// the camp with fewer parties). Null when the camps do not fit her room.</summary>
        private static PainCampPlan PainPlanFor(GameNPC boss, string raidId, IReadOnlyList<Group> parties)
        {
            lock (PainPlans)
            {
                if (PainPlans.TryGetValue(boss, out PainCampPlan plan) && plan == null) return null;
                if (plan != null && plan.RaidId == raidId)
                {
                    foreach (Group g in parties)
                        if (!plan.Camps.ContainsKey(g))
                            plan.Camps[g] = plan.Camps.Values.Count(c => c == 0) <= plan.Camps.Values.Count(c => c == 1) ? 0 : 1;
                    return plan;
                }
                Tactic tactic = For(boss);
                int aggro = boss.Brain is DOL.AI.Brain.StandardMobBrain mob ? mob.AggroRange : 0;
                int pain = PainRadiusOf(boss);
                if (tactic == null || !PainCampsFit(tactic.FightAtHomeRadius, aggro, PainCampFromSpawn, pain))
                {
                    PainPlans[boss] = plan = null;
                    Log.Info($"REALM_EXPEDITION_TACTIC region={boss.CurrentRegionID} encounter=\"{boss.Name}\" tactic=summoner-pain/camps " +
                             $"camps=none reason=\"the camps do not fit: home {tactic?.FightAtHomeRadius} aggro {aggro} pain {pain}\"");
                    return null;
                }
                Vector3 spawn = SpawnOf(boss);
                var members = parties.SelectMany(g => g.GetMembersInTheGroup()).Where(m => m.IsAlive && m.CurrentRegionID == boss.CurrentRegionID).ToArray();
                Vector3 middle = spawn;
                if (members.Length > 0)
                {
                    middle = Vector3.Zero;
                    foreach (GameLiving m in members) middle += At(m);
                    middle /= members.Length;
                }
                (Vector3 A, Vector3 B) pair = PainCampPair(spawn, middle, 0, PainCampFromSpawn);
                var nav = PathfindingProvider.Instance;
                Zone zone = boss.CurrentZone;
                if (zone != null && nav is { IsAvailable: true } && nav.HasNavmesh(zone) &&
                    nav.GetClosestPoint(zone, spawn, 128, 128, 256, nav.DefaultFilters) is Vector3 floor)
                {
                    for (int turn = 0; turn < PainCampTurns; turn++)
                    {
                        var raw = PainCampPair(spawn, middle, turn * Math.PI / PainCampTurns, PainCampFromSpawn);
                        if (nav.GetClosestPoint(zone, raw.A, PartyPostFloorSnap, PartyPostFloorSnap, 400, nav.DefaultFilters) is Vector3 a &&
                            nav.GetClosestPoint(zone, raw.B, PartyPostFloorSnap, PartyPostFloorSnap, 400, nav.DefaultFilters) is Vector3 b &&
                            nav.HasLineOfSight(zone, a, floor, nav.DefaultFilters) && nav.HasLineOfSight(zone, b, floor, nav.DefaultFilters) &&
                            Flat(a, spawn) + PainCampTolerance <= tactic.FightAtHomeRadius && Flat(b, spawn) + PainCampTolerance <= tactic.FightAtHomeRadius)
                        {
                            pair = (a, b);
                            break;
                        }
                    }
                }
                plan = new PainCampPlan { RaidId = raidId, A = pair.A, B = pair.B };
                int[] healers = parties.Select(g => g.GetMembersInTheGroup().Count(m => m.IsAlive && IsHealer(m))).ToArray();
                int[] camps = AssignPainCamps(healers);
                for (int i = 0; i < parties.Count; i++) plan.Camps[parties[i]] = camps[i];
                if (PainPlans.Count > 64) PainPlans.Clear();
                PainPlans[boss] = plan;
                string Side(int c) => string.Join(",", Enumerable.Range(0, parties.Count).Where(i => camps[i] == c)
                    .Select(i => $"{parties[i].LivingLeader?.Name}:{healers[i]}h"));
                Log.Info($"REALM_EXPEDITION_TACTIC region={boss.CurrentRegionID} encounter=\"{boss.Name}\" tactic=summoner-pain/camps " +
                         $"a={(int)pair.A.X},{(int)pair.A.Y},{(int)pair.A.Z} b={(int)pair.B.X},{(int)pair.B.Y},{(int)pair.B.Z} " +
                         $"apart={(int)Flat(pair.A, pair.B)} fromSpawn={PainCampFromSpawn} pain={pain} " +
                         $"partiesA={camps.Count(c => c == 0)} partiesB={camps.Count(c => c == 1)} " +
                         $"healersA={Enumerable.Range(0, camps.Length).Where(i => camps[i] == 0).Sum(i => healers[i])} " +
                         $"healersB={Enumerable.Range(0, camps.Length).Where(i => camps[i] == 1).Sum(i => healers[i])} " +
                         $"campA=\"{Side(0)}\" campB=\"{Side(1)}\" " +
                         $"reason=\"two camps on opposite sides of her: one {SummonerPainSpell} ({pain}) cannot reach both\"");
                return plan;
            }
        }

        /// <summary>The camp post of <paramref name="bot"/>'s party in <paramref name="boss"/>'s fight (null: none).</summary>
        private static Vector3? PainCampOf(GameBot bot, GameNPC boss)
        {
            if (bot?.Group == null || !PainWatch.ContainsKey(boss)) return null;
            if (AutonomousRealmRaid.RaidOf(bot.Group) is not { } raid) return null;
            PainCampPlan plan = PainPlanFor(boss, raid.Id, raid.Parties);
            if (plan == null || !plan.Camps.TryGetValue(bot.Group, out int camp)) return null;
            return camp == 0 ? plan.A : plan.B;
        }

        /// <summary>The raider stands at its party's camp post (SummonerInsideTether leaves it there).</summary>
        private static bool AtPainCamp(GameLiving raider, GameNPC boss) =>
            raider is GameBot bot && PainCampOf(bot, boss) is Vector3 post && Flat(At(bot), post) <= PainCampTolerance * 2;

        /// <summary>
        /// BotBrain's turn (from <see cref="ThinkBlast"/>; a healer has no hostile target): a caster, archer or healer of the
        /// fight walks to its party's camp post, between casts. False: at the post (or not concerned), the turn is its own.
        /// </summary>
        private static bool ThinkPainCamp(GameBot bot, long now)
        {
            foreach (var (boss, seen) in PainWatch)
            {
                if (!Present(boss) || !boss.InCombat || now - seen > PainCampWatchMilliseconds)
                {
                    PainWatch.TryRemove(boss, out _);
                    continue;
                }
                if (boss.CurrentRegionID != bot.CurrentRegionID || !bot.IsWithinRadius(boss, PainCampReach) || boss.TargetObject == bot)
                    continue;
                if (!(CanShootFlyers(bot) || IsHealer(bot))) return false;
                if (PainCampOf(bot, boss) is not Vector3 post) return false;
                if (Flat(At(bot), post) <= PainCampTolerance || bot.IsCasting || bot.IsMezzed || bot.IsStunned) return false;
                if (bot.FollowTarget != null) bot.StopFollowing();
                if (!bot.IsMovingOnPath) bot.PathTo(new Point3D((int)post.X, (int)post.Y, (int)post.Z), bot.MaxSpeed);
                LogDecision(bot, boss, new(BotAction.Reposition, boss, post,
                    $"pain camps of \"{boss.Name}\": the party's back line at its camp, {PainCampFromSpawn} from her spawn"));
                return true;
            }
            return false;
        }

        /// <summary>The dungeon rule: a melee raider of the fight takes her side facing its camp; a back-line raider not at its
        /// camp walks there (one at it fights on, and the summoners' inner-radius rule leaves it there).</summary>
        private static Advice? PainCampAdvice(RuleContext c)
        {
            GameNPC boss = c.Target;
            if (c.Raider is not GameBot bot || !HasSummonerPain(boss) || boss.TargetObject == bot) return null;
            if (PainCampOf(bot, boss) is not Vector3 post) return null;
            Vector3 at = c.At;
            if (c.AtRange)
                return Flat(at, post) <= PainCampTolerance * 2 ? null
                    : new(BotAction.Reposition, boss, post, $"pain camps: the back line fights from its camp ({PainCampFromSpawn} from her spawn)");
            Vector3 spot = PainCampMeleeSpot(At(boss), post, RingRadius);
            return Flat(at, spot) <= RingTolerance ? null
                : new(BotAction.Reposition, boss, spot, "pain camps: melee on her side facing their camp");
        }

        public static void ResetPainCampsForTests()
        {
            PainWatch.Clear();
            lock (PainPlans) PainPlans.Clear();
        }
    }
}
