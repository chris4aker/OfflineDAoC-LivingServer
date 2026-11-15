using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The lord of a battleground keep is taken together. Playtest 2026-11-04 (Thidranki, 210 level-22 bots, 45 minutes):
/// both gates fell, but every stormer walked to the lord on its own, one at a time, from wherever its last fight or its
/// release left it. In nine keep samples no attacker ever stood on the lord's floor, the lord stayed at 100% and the
/// only bots that reached him died to him alone. On live servers the realm rallied inside the breach, cleared what
/// came at it and went up to the lord as one group once enough were there.
///
/// Once the lord is exposed (no main gate stands closed, <see cref="GuardLord.ShieldedFromBots"/> false), a realm's
/// assaulting gamebots rally outside the breach (see below). They go up together when enough are there to win
/// against the lord and the guards with him, worked out from their real levels and health (<see cref="NeededAttackers"/>).
/// An attempt ends like a raid's: half its stormers fell, the lord healed back, or nothing happened to him for three
/// minutes; then the survivors rally again. A bot that died rejoins the rally instead of running back to him alone, and
/// bots that gather at the rally while an attempt runs go up in batches (<see cref="ReinforcementBatch"/>). The lord,
/// his guards and their respawns are untouched: only the bots decide better when to go.
///
/// Owner's PC, build 3b808b4 (Thidranki 2026-11-15, 10:12-10:58): all six doors of Thidranki Faste down, 30-46 bots of
/// each attacking realm inside, and in all eleven BG_KEEP_STATUS samples both rallies read "rallying 0/31-34"; no rally
/// ever went up. The point stood just inside the keep door (32475,38224,3728), under the lord's floor and 1,680 past the
/// outer gate: every bot crossed the courtyard alone, within the guards' reach (fighters and wizards 1,000), and 276
/// Midgard and Hibernian bots died to the keep's guards meanwhile. Only bots within 4,000 of the lord took the rally at
/// all, and nothing held them out of field fights. Now the rally stands outside the breach (the outermost broken main
/// gate), clear of the guards' posts (<see cref="RallyGuardClearance"/>); every assaulting bot of the realm goes there and
/// fights only what comes at the rally, as a wave's gatherers do; "enough" is never more than a wave of the realm
/// (<see cref="RallyNeeded"/>); a realm goes with half of that while the other attacking realm is storming (as #247's
/// waves cue each other); and the stormers keep the wave's discipline, fighting the guards that attack the group.
/// BG_RALLY_STATUS logs each realm's rally once a minute.
///
/// Owner's PC, build d64d005 (Thidranki 2026-11-15, 12:55-13:02): all six doors of Thidranki Faste down, Albion holding it,
/// 33-43 bots of each attacking realm inside. Both attacking realms rallied on one spot: every BG_RALLY_STATUS of both read
/// point=36133,38275,3368 gate=252000301, because the point depended only on the keep and the gate, and both realms take
/// the same breach. Midgard went up with 15 at 12:59:49 and regrouped after 1.0 minute, "half the stormers fell", with the
/// lord at 100% and nobody on his floor: of its ten deaths in that minute seven were to Hibernian bots, most of them
/// within 1,000 of the shared point (Unclesnore to Dawncaller at 36107,38242), one to a guard at the gate and two to Albion
/// bots. Now each realm's rally stands apart from the other attacking realm's (<see cref="RallySeparation"/>): the
/// candidates also lie 30° and 60° either side of the outward side, the realm prefers the ones toward its own arrival
/// (<see cref="RallyPreference"/>), and one too near the other realm's rally is passed over (the earlier rally keeps its
/// spot, the later one moves); with nothing apart, clear and walkable the rally stands where it did before
/// (<see cref="PlanRally"/>). There is no truce: realms that meet still fight. BG_RALLY_STATUS logs the rally's bearing
/// and its distance to the other realm's rally (apart=).
/// </summary>
public static class AutonomousBattlegroundKeepRally
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBattlegroundKeepRally));

    /// <summary>A bot this close to the rally point counts as rallied.</summary>
    public const float RallyRadius = 1_000f;
    /// <summary>...and no more than this above or below it (the field outside a gate slopes: 3,720 at Thidranki Faste's
    /// outer gate, 3,550 some 1,200 east of it).</summary>
    public const float RallyFloorHeight = 400f;
    /// <summary>A rallying bot stops walking this close to the rally point.</summary>
    public const float RallyHoldRadius = 250f;
    /// <summary>How far out from the breach the rally may stand, nearest first (<see cref="ChooseRallyCandidate"/>).</summary>
    public static readonly IReadOnlyList<float> BreachStandOffs = new[] { 900f, 1_200f, 1_500f, 1_800f };
    /// <summary>
    /// The rally keeps this far from every guard's post: a keep guard's aggro range is 1,000 (GameKeepGuard.SetAggression;
    /// the Armsmen and Wizards that killed the stormers of 3b808b4), plus the few steps one takes before it turns back.
    /// </summary>
    public const float RallyGuardClearance = 1_300f;
    /// <summary>A rally point must have a complete walk to the gate's front, this far out from it (as
    /// AutonomousWorldBotController.DoorFrontDistance); with no farther candidate the rally stands there.</summary>
    public const float BreachFrontDistance = 230f;
    /// <summary>Guards this close to the lord, and on his floor, fight beside him.</summary>
    public const float LordRoomRadius = 1_200f;
    public const float LordFloorHeight = 300f;
    /// <summary>A bot this close to the lord during an attempt is already in that fight.</summary>
    public const float InTheFightRadius = 900f;

    public const long CountMilliseconds = 5_000;
    /// <summary>After this long gathering, a rally goes up with half of what it needs (owner, 2026-11-04: the first cut of
    /// 5 minutes and 60% was too cautious).</summary>
    public const long RallyPatienceMilliseconds = 3 * 60_000;
    public const int PatientSharePercent = 50;
    /// <summary>After this long, a rally goes up with whoever is there (at least <see cref="MinimumRally"/>): the owner wants
    /// failures to come from the fight, not from the gathering (RealmRaidRecruitmentPolicy).</summary>
    public const long RallyLastCallMilliseconds = 6 * 60_000;
    public const int MinimumRally = AutonomousBattlegroundWave.MinimumWave;
    public const int MaximumRally = 60;
    /// <summary>An attempt in which the lord lost nothing for this long is over.</summary>
    public const long NoProgressMilliseconds = 3 * 60_000;

    // The strength model (NeededAttackers). Every number is from the base game unless marked as a model choice.
    /// <summary>Base miss chance 18%, 1.33% per level of difference (AttackComponent.GetMissChance).</summary>
    public const double BaseHitChance = 0.82, HitPerLevel = 1.0 / 75;
    /// <summary>Weapon skill moves 1% per level of difference (AttackComponent.CalculateDefensePenetration).</summary>
    public const double SpecPerLevel = 0.01;
    /// <summary>The lord: 40% resist to every damage type (GameKeepGuard.SetResists), +5% absorb (GuardLord.GetArmorAbsorb),
    /// and block/parry/evade against melee (GuardLord.SetBlockEvadeParryChance, about 15% overall for a mixed realm: a model
    /// choice).</summary>
    public const double LordTakes = 0.60 * 0.95 * 0.85;
    /// <summary>A keep guard below level 50: level/2+1 percent resists (GameKeepGuard.SetResists).</summary>
    public static double GuardTakes(int guardLevel) => 1 - Math.Clamp(guardLevel / 2 + 1, 0, 50) / 100.0;
    /// <summary>Square law: the attempt may lose this share of its stormers before the lord falls (the raid's wipe line).</summary>
    public const double AcceptableLosses = 0.5;
    /// <summary>Model choice: no safety margin on top of the square law. The owner wants keeps attempted more often and
    /// accepts that more attempts fail (2026-11-04; the first cut was 1.25).</summary>
    public const double Margin = 1.0;

    /// <summary>A real player at the rally counts as this many gamebots (owner, 2026-11-04).</summary>
    public const int PlayerWeight = 2;

    /// <summary>What the rally counts as present: its gamebots plus <see cref="PlayerWeight"/> per real player. Pure.</summary>
    public static int RallyStrength(int bots, int players) => Math.Max(0, bots) + Math.Max(0, players) * PlayerWeight;

    /// <summary>The reference weapon DPS of a level (BotWeaponStats.NormalDps, in tenths there).</summary>
    public static double WeaponDps(int level) => BotWeaponStats.NormalDps(level) / 10.0;

    /// <summary>Damage per second one fighter of <paramref name="attackerLevel"/> deals to a defender of
    /// <paramref name="defenderLevel"/>, before the defender's resists (same units for everyone: only ratios matter).</summary>
    public static double DamageRate(int attackerLevel, int defenderLevel)
    {
        int gap = attackerLevel - defenderLevel;
        double hit = Math.Clamp(BaseHitChance + gap * HitPerLevel, 0.25, 0.99);
        double spec = Math.Clamp(1 + gap * SpecPerLevel, 0.5, 1.5);
        return WeaponDps(attackerLevel) * hit * spec;
    }

    /// <summary>
    /// How many attackers of this average level and health it takes to kill the lord (and the guards fighting beside him)
    /// while losing at most half of them. Lanchester's square law for one strong defender against many: N attackers
    /// dealing d each must deal H (the defenders' health) before the defenders, dealing D, have killed N/2 of health h,
    /// which needs N &gt;= sqrt(H·D / (d·h·f(1 - f/2))) with f the acceptable losses. Pure.
    /// </summary>
    public static int NeededAttackers(int lordLevel, long lordHealth, int attackerLevel, int attackerHealth,
        int guards = 0, int guardLevel = 0, long guardHealthEach = 0)
    {
        if (lordHealth <= 0 || attackerLevel <= 0) return MinimumRally;
        attackerHealth = Math.Max(1, attackerHealth);
        // Seconds of one attacker's damage that the lord and his guards can absorb.
        double toKill = lordHealth / (DamageRate(attackerLevel, lordLevel) * LordTakes);
        double incoming = DamageRate(lordLevel, attackerLevel);
        if (guards > 0 && guardLevel > 0)
        {
            toKill += guards * Math.Max(0, guardHealthEach) / (DamageRate(attackerLevel, guardLevel) * GuardTakes(guardLevel));
            incoming += guards * DamageRate(guardLevel, attackerLevel);
        }
        double f = AcceptableLosses * (1 - AcceptableLosses / 2);
        double needed = Math.Sqrt(toKill * incoming / (attackerHealth * f)) * Margin;
        return Math.Clamp((int)Math.Ceiling(needed), MinimumRally, MaximumRally);
    }

    /// <summary>
    /// Whether a gathering rally goes up now: enough are there, or half of enough after the patience time or at once while
    /// another attacking realm storms the same lord (his guards are busy with them: AutonomousBattlegroundWave.WaveGoes), or
    /// whoever is there (at least <see cref="MinimumRally"/>) at the last call. Pure.
    /// </summary>
    public static bool ShouldStorm(int rallied, int needed, long gatheringMilliseconds, bool otherRealmStorming = false) =>
        rallied >= needed ||
        (gatheringMilliseconds >= RallyPatienceMilliseconds || otherRealmStorming) &&
        rallied >= Math.Max(MinimumRally, (needed * PatientSharePercent + 99) / 100) ||
        gatheringMilliseconds >= RallyLastCallMilliseconds && rallied >= MinimumRally;

    /// <summary>
    /// What a rally waits for: the strength model's number (<see cref="NeededAttackers"/>), but never more than a wave of
    /// the realm's bots inside (AutonomousBattlegroundWave.WaveSize, 60%). On 3b808b4 the model asked 31-34 of 30-46 bots,
    /// with each of them dying about every four minutes: that many never stand in one place. The owner takes more failed
    /// attempts over none (2026-11-04). Pure.
    /// </summary>
    public static int RallyNeeded(int modelled, int realmInside) =>
        Math.Clamp(Math.Min(modelled, AutonomousBattlegroundWave.WaveSize(Math.Max(0, realmInside))), MinimumRally, MaximumRally);

    /// <summary>
    /// The breach a realm rallies at: a broken (or open) main door, never a postern; the lowest door number first (the
    /// outer gate, 01, before the keep door behind it), then the one farther from the keep's centre, then the one nearer the
    /// realm's own arrival. -1 when no main door is down. Thidranki Faste: gate 252000301 (east, 1,244 from the centre), not
    /// keep door 252000302 under the lord, where the rally of 3b808b4 stood. Pure.
    /// </summary>
    public static int BreachIndex(
        IReadOnlyList<(int Number, bool PosternLike, bool Down, float FromCentre, float FromArrival)> doors)
    {
        int best = -1;
        for (int i = 0; i < doors.Count; i++)
        {
            var door = doors[i];
            if (!door.Down || door.PosternLike) continue;
            if (best < 0) { best = i; continue; }
            var held = doors[best];
            if (door.Number != held.Number ? door.Number < held.Number
                : door.FromCentre != held.FromCentre ? door.FromCentre > held.FromCentre
                : door.FromArrival < held.FromArrival)
                best = i;
        }
        return best;
    }

    /// <summary>A rally candidate <paramref name="standOff"/> out from the breach along its outward side, at the gate's height. Pure.</summary>
    public static Vector3 BreachAnchor(Vector3 gate, Vector2 outward, float standOff) =>
        new(gate.X + outward.X * standOff, gate.Y + outward.Y * standOff, gate.Z);

    /// <summary>
    /// Which of the walkable candidates (nearest the breach first) the rally takes: the first that keeps
    /// <see cref="RallyGuardClearance"/> from every guard's post, otherwise the one farthest from them. -1 when none. Pure.
    /// </summary>
    public static int ChooseRallyCandidate(IReadOnlyList<float> guardClearance)
    {
        int clearest = -1;
        for (int i = 0; i < guardClearance.Count; i++)
        {
            if (guardClearance[i] >= RallyGuardClearance) return i;
            if (clearest < 0 || guardClearance[i] > guardClearance[clearest]) clearest = i;
        }
        return clearest;
    }

    /// <summary>
    /// The directions the rally may stand in, in degrees off the breach's outward side (positive turns from the outward
    /// side toward its left, as <see cref="RallyDirection"/>), straight out first. 60° at the farthest stand-off is still
    /// 900 out from the wall; past that a candidate runs along the wall under its towers.
    /// </summary>
    public static readonly IReadOnlyList<float> RallyBearings = new[] { 0f, 30f, -30f, 60f, -60f };

    /// <summary>
    /// Two attacking realms' rallies stand at least this far apart: twice <see cref="RallyRadius"/>, so no bot is ever at
    /// both rallies, plus <see cref="RallyHoldRadius"/>, so a realm's holders (within that of their own point) stand
    /// 2,000 or more from the other point: beyond the 1,500 within which holders fire back at an enemy firing on them
    /// (AutonomousBattlegroundWave.GatherDefendRadius) and the 700 within which they fight whoever walks in
    /// (GatherCloseRadius). On d64d005 both realms' points were the same.
    /// </summary>
    public const float RallySeparation = 2 * RallyRadius + RallyHoldRadius;

    /// <summary>A placed rally candidate: its bearing (<see cref="RallyBearings"/>), stand-off, distance to the nearest guard
    /// post, and distance to the nearest other attacking realm's rally (float.MaxValue with none).</summary>
    public readonly record struct RallyCandidate(float Bearing, float StandOff, float GuardClearance, float FromOtherRally);

    /// <summary><paramref name="outward"/> turned by <paramref name="bearingDegrees"/> toward its left. Pure.</summary>
    public static Vector2 RallyDirection(Vector2 outward, float bearingDegrees)
    {
        double radians = bearingDegrees * Math.PI / 180;
        float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
        return new(outward.X * c - outward.Y * s, outward.X * s + outward.Y * c);
    }

    /// <summary>The realm's arrival as seen from the breach, in degrees off its outward side (the sense of
    /// <see cref="RallyDirection"/>), -180..180; 0 with no arrival. Pure.</summary>
    public static float ArrivalBearing(Vector3 gate, Vector2 outward, Vector3? arrival)
    {
        if (arrival is not { } at) return 0;
        Vector2 toward = new(at.X - gate.X, at.Y - gate.Y);
        if (toward.LengthSquared() < 1 || outward.LengthSquared() < 1e-6f) return 0;
        double cross = outward.X * toward.Y - outward.Y * toward.X;
        double dot = outward.X * toward.X + outward.Y * toward.Y;
        return (float)(Math.Atan2(cross, dot) * 180 / Math.PI);
    }

    /// <summary>The angle between two bearings, 0..180. Pure.</summary>
    public static float BearingGap(float a, float b)
    {
        float gap = Math.Abs(a - b) % 360;
        return gap > 180 ? 360 - gap : gap;
    }

    /// <summary>The flat distance from <paramref name="at"/> to the nearest of <paramref name="others"/>; float.MaxValue
    /// with none. Pure.</summary>
    public static float NearestOtherRally(Vector3 at, IReadOnlyList<Vector3> others)
    {
        float nearest = float.MaxValue;
        foreach (Vector3 other in others)
            nearest = Math.Min(nearest, Vector2.Distance(new(at.X, at.Y), new(other.X, other.Y)));
        return nearest;
    }

    /// <summary>
    /// The candidates a realm's rally may take, best first: only those <see cref="RallySeparation"/> from every other
    /// attacking realm's rally and <see cref="RallyGuardClearance"/> from every guard post; nearest the realm's own arrival
    /// bearing first, then nearest the breach. Empty when none qualifies (<see cref="PlanRally"/> then falls back). Pure.
    /// </summary>
    public static IReadOnlyList<int> RallyPreference(IReadOnlyList<RallyCandidate> candidates, float arrivalBearing) =>
        Enumerable.Range(0, candidates.Count)
            .Where(i => candidates[i].FromOtherRally >= RallySeparation && candidates[i].GuardClearance >= RallyGuardClearance)
            .OrderBy(i => BearingGap(candidates[i].Bearing, arrivalBearing))
            .ThenBy(i => candidates[i].StandOff)
            .ThenBy(i => i)
            .ToArray();

    /// <summary>Where <see cref="PlanRally"/> put a rally: the point (null when nothing outside the breach is walkable), its
    /// guard clearance, its bearing, its distance to the nearest other attacking realm's rally (float.MaxValue with none),
    /// and whether it is <see cref="RallySeparation"/> from them.</summary>
    public readonly record struct RallyPlan(Vector3? Point, float GuardClear, float Bearing, float FromOtherRally)
    {
        public bool Apart => FromOtherRally >= RallySeparation;
    }

    /// <summary>
    /// The rally point outside the breach at <paramref name="gate"/>: every bearing and stand-off is placed with
    /// <paramref name="floor"/> (the navmesh floor under a raw point, null when there is none to stand on); the first
    /// of <see cref="RallyPreference"/> with a complete walk to the gate's front (<paramref name="reachesGate"/>, asked
    /// lazily: an unreachable point is the most expensive query) is the rally. With none, as before d64d005: straight out
    /// only, the nearest clear of the guards or the clearest (<see cref="ChooseRallyCandidate"/>), even on another realm's
    /// rally. Pure but for the delegates.
    /// </summary>
    public static RallyPlan PlanRally(Vector3 gate, Vector2 outward, Vector3? arrival, IReadOnlyList<Vector3> otherRallies,
        Func<Vector3, Vector3?> floor, Func<Vector3, bool> reachesGate, Func<Vector3, float> clearance)
    {
        float arrivalBearing = ArrivalBearing(gate, outward, arrival);
        var points = new List<Vector3>();
        var candidates = new List<RallyCandidate>();
        foreach (float bearing in RallyBearings)
        {
            Vector2 direction = RallyDirection(outward, bearing);
            foreach (float standOff in BreachStandOffs)
            {
                if (floor(BreachAnchor(gate, direction, standOff)) is not { } at) continue;
                points.Add(at);
                candidates.Add(new RallyCandidate(bearing, standOff, clearance(at), NearestOtherRally(at, otherRallies)));
            }
        }
        var walkable = new Dictionary<int, bool>();
        bool Reaches(int i)
        {
            if (!walkable.TryGetValue(i, out bool ok)) walkable[i] = ok = reachesGate(points[i]);
            return ok;
        }
        RallyPlan Plan(int i) => new(points[i], candidates[i].GuardClearance, candidates[i].Bearing, candidates[i].FromOtherRally);
        foreach (int i in RallyPreference(candidates, arrivalBearing))
            if (Reaches(i)) return Plan(i);
        int[] straight = Enumerable.Range(0, candidates.Count).Where(i => candidates[i].Bearing == 0 && Reaches(i)).ToArray();
        int pick = ChooseRallyCandidate(straight.Select(i => candidates[i].GuardClearance).ToArray());
        return pick >= 0 ? Plan(straight[pick]) : new RallyPlan(null, 0, 0, float.MaxValue);
    }

    /// <summary>
    /// A gathering rally that stands too near another attacking realm's moves when the other was placed first (the earlier
    /// realm keeps its spot; equal times: the lower realm number keeps it), unless it already avoided that very point when it
    /// was placed (nothing apart could be found then). Pure.
    /// </summary>
    public static bool YieldsTo(float apart, long mineResolvedAt, eRealm mine, long otherResolvedAt, eRealm other, bool alreadyAvoided) =>
        apart < RallySeparation && !alreadyAvoided &&
        (otherResolvedAt < mineResolvedAt || otherResolvedAt == mineResolvedAt && other < mine);

    /// <summary>
    /// The keep fight is disciplined (a guard only when near or firing on the realm, AutonomousBattlegroundWave.WaveTakesGuard)
    /// while a door stands, and with the doors down while the realm rallies or storms. On 3b808b4 the gates-down storm took
    /// any guard in sight up to 5,000 away: 103 deaths "Fighting Armsman". Pure.
    /// </summary>
    public static bool DisciplinedKeepFight(bool gatesShut, Step step) => gatesShut || step != Step.None;

    /// <summary>A bot fights the guard hitting one of its own nearby (the going wave's rule) while a door stands or while it storms. Pure.</summary>
    public static bool AssistsAgainstGuards(bool gatesShut, Step step) => gatesShut || step == Step.Storm;

    /// <summary>
    /// A stormer on its way up takes an enemy (not the keep's guards, which the storm itself decides on) only when it is
    /// upon the bot or firing on the realm near it: the gatherers' rule (AutonomousBattlegroundWave.GathererMayEngage) with the
    /// bot itself as the staging point. Pure.
    /// </summary>
    public static bool StormerMayEngage(bool firingOnRealm, float targetToBot) =>
        AutonomousBattlegroundWave.GathererMayEngage(firingOnRealm, targetToBot, targetToBot);

    /// <summary>While an attempt runs, rallied bots go up in groups at least this big (never one at a time). Pure.</summary>
    public static int ReinforcementBatch(int needed) => Math.Max(4, (needed + 3) / 4);

    /// <summary>A rally nobody asked about for this long (the lord shielded again, or the realm away) starts over.</summary>
    public const long LapseMilliseconds = 60_000;
    public static bool IsLapsed(long lastAsked, long now) => lastAsked > 0 && now - lastAsked > LapseMilliseconds;

    /// <summary>Why an attempt is over (null while it goes on). Pure.</summary>
    public static string AttemptOver(int engaged, int alive, int lordPercent, int lowestLordPercent, long sinceProgressMilliseconds)
    {
        if (engaged > 0 && alive * 2 <= engaged) return "half the stormers fell";
        if (lowestLordPercent <= 85 && lordPercent >= 95) return "the lord healed back";
        if (sinceProgressMilliseconds >= NoProgressMilliseconds) return "no progress on the lord";
        return null;
    }

    public enum Step { None, Rally, Storm }

    public readonly record struct Order(Step Step, Vector3 Point, string Progress)
    {
        public bool Hold => Step == Step.Rally;
    }

    private sealed class Rally
    {
        public ushort Region;
        public eRealm Realm;
        public int KeepId;
        public eRealm KeepRealm;
        public int GateId;
        public Vector3? Point;
        public bool Storming;
        public long GatherSince, AttemptSince, LastProgress, NextCount, LastAsked, NextStatusLog;
        public int Rallied, RalliedPlayers, Needed, Modelled, Inside, Engaged, LowestPercent, OnLordFloor, AtLord;
        /// <summary>The rally point's distance to the nearest guard post (<see cref="RallyGuardClearance"/>).</summary>
        public float GuardClear;
        /// <summary>The rally point's bearing off the breach's outward side (<see cref="RallyBearings"/>).</summary>
        public float Bearing;
        /// <summary>When the point was placed, and the other attacking realms' rally points it was placed away from.</summary>
        public long ResolvedAt;
        public Vector3[] Avoided = Array.Empty<Vector3>();
        // Since the last BG_RALLY_STATUS: the realm's deaths here, and the fights its holders and stormers left alone.
        public int DiedToGuards, DiedOther, Refused, Dropped;
        /// <summary>The gamebots that went up. Each also holds a battleground-wave duty lease for this rally (docs/DUTY-LEASE.md).</summary>
        public readonly HashSet<long> Committed = new();
        public string LeaseOwner = string.Empty, KeepName = string.Empty;
    }

    /// <summary>The duty lease owner of a realm's rally in a battleground.</summary>
    public static string RallyLeaseOwner(ushort region, eRealm realm) => $"bg-rally:{region}:{(int)realm}";

    /// <summary>A gamebot goes up with the rally: it is committed, and its time is the rally's until the attempt ends or it dies.
    /// Real players are never leased; they only add weight to the count (PlayersAtRally).</summary>
    private static bool CommitBot(Rally rally, GameBot bot)
    {
        if (!rally.Committed.Add(bot.DatabaseID)) return false;
        BotDutyLeases.Claim(bot, eDutyLeaseKind.BattlegroundWave, rally.LeaseOwner, DutyLeaseReason.BattlegroundRally, rally.KeepName);
        return true;
    }

    private static readonly ConcurrentDictionary<(ushort Region, eRealm Realm), Rally> Rallies = new();

    // When each realm's rally was last seen storming, per region; read without the rallies' locks (no lock order between
    // realms). A storm not seen for 30 s (the attempt ended, or the realm left) no longer counts.
    private static readonly ConcurrentDictionary<(ushort Region, eRealm Realm), long> StormingSeen = new();
    public const long StormSeenMilliseconds = 30_000;
    public const long StatusLogMilliseconds = 60_000;

    private static bool OtherRealmStorming(ushort region, eRealm realm, eRealm holder, long now) =>
        StormingSeen.Any(pair => pair.Key.Region == region && pair.Key.Realm != realm && pair.Key.Realm != holder &&
                                 now - pair.Value <= StormSeenMilliseconds);

    // Each realm's rally point, per region, as last seen by Decide; read without the rallies' locks, as StormingSeen. A
    // point not asked about for LapseMilliseconds (the rally lapsed) no longer counts.
    private readonly record struct PlacedRally(int KeepId, Vector3 Point, long ResolvedAt, long Seen);
    private static readonly ConcurrentDictionary<(ushort Region, eRealm Realm), PlacedRally> RallyPoints = new();

    /// <summary>The other attacking realms' current rally points at this keep (not the holder's, not this realm's).</summary>
    private static List<(eRealm Realm, PlacedRally Placed)> OtherRallies(ushort region, eRealm realm, AbstractGameKeep keep, long now) =>
        RallyPoints.Where(pair => pair.Key.Region == region && pair.Key.Realm != realm && pair.Key.Realm != keep.Realm &&
                                  pair.Value.KeepId == keep.KeepID && now - pair.Value.Seen <= LapseMilliseconds)
            .Select(pair => (pair.Key.Realm, pair.Value)).ToList();

    /// <summary>
    /// This bot of the realm holds at its rally (not one of an attempt's stormers): <paramref name="point"/> is where. The
    /// controller's fight turn uses it to keep the holders out of fights that are not theirs.
    /// </summary>
    public static bool IsRallying(ushort region, eRealm realm, long botId, out Vector3 point)
    {
        point = default;
        if (!Rallies.TryGetValue((region, realm), out Rally rally)) return false;
        lock (rally)
        {
            if (rally.Point is not { } at || IsLapsed(rally.LastAsked, GameLoop.GameLoopTime) ||
                rally.Storming && rally.Committed.Contains(botId)) return false;
            point = at;
            return true;
        }
    }

    /// <summary>This bot is one of its realm's stormers on the way up to the lord.</summary>
    public static bool IsStorming(ushort region, eRealm realm, long botId)
    {
        if (!Rallies.TryGetValue((region, realm), out Rally rally)) return false;
        lock (rally)
            return rally.Storming && rally.Committed.Contains(botId) && !IsLapsed(rally.LastAsked, GameLoop.GameLoopTime);
    }

    /// <summary>Counts a death of the realm's bot in a battleground with a rally, for BG_RALLY_STATUS.</summary>
    public static void NoteDeath(ushort region, eRealm realm, bool killedByKeepGuard)
    {
        if (!Rallies.TryGetValue((region, realm), out Rally rally)) return;
        lock (rally)
        {
            if (rally.Point == null || IsLapsed(rally.LastAsked, GameLoop.GameLoopTime)) return;
            if (killedByKeepGuard) rally.DiedToGuards++;
            else rally.DiedOther++;
        }
    }

    /// <summary>Counts a fight a rally holder or stormer left alone (or broke off), for BG_RALLY_STATUS.</summary>
    public static void NoteHeldBack(ushort region, eRealm realm, bool dropped)
    {
        if (!Rallies.TryGetValue((region, realm), out Rally rally)) return;
        lock (rally)
        {
            if (dropped) rally.Dropped++;
            else rally.Refused++;
        }
    }

    /// <summary>
    /// What this assaulting bot does at an exposed lord: rally (hold at <see cref="Order.Point"/>) or storm him.
    /// <see cref="Step.None"/> when no rally point could be found (the ordinary storm then runs, as before).
    /// </summary>
    public static Order Decide(GameBot bot, BattlegroundBrackets.Bracket bracket, AbstractGameKeep keep, GameKeepGuard lord)
    {
        long now = GameLoop.GameLoopTime;
        Rally rally = Rallies.GetOrAdd((bracket.RegionId, bot.Realm),
            _ => new Rally { Region = bracket.RegionId, Realm = bot.Realm, GatherSince = now,
                LeaseOwner = RallyLeaseOwner(bracket.RegionId, bot.Realm) });
        lock (rally)
        {
            if (rally.KeepId != keep.KeepID || rally.KeepRealm != keep.Realm)
            {
                // A new keep holder: everything starts over.
                Reset(rally, now);
                rally.KeepId = keep.KeepID;
                rally.KeepRealm = keep.Realm;
                rally.KeepName = keep.Name ?? string.Empty;
                rally.Point = null;
            }
            else if (IsLapsed(rally.LastAsked, now))
                Reset(rally, now); // the gates were rebuilt (or nobody was here) meanwhile: a fresh rally, a fresh clock
            rally.LastAsked = now;
            GameLocation arrival = BattlegroundBrackets.BotArrival(bracket, bot.Realm);
            GameKeepDoor gate = Breach(keep, arrival);
            if (gate == null) return default;
            var others = OtherRallies(bracket.RegionId, bot.Realm, keep, now);
            bool place = rally.Point == null || rally.GateId != gate.DoorId;
            if (!place && !rally.Storming && rally.Point is { } placed)
                // Two realms placed at once (or the other moved onto this one): the later of the two moves (d64d005).
                place = others.Any(other => YieldsTo(Vector2.Distance(new(placed.X, placed.Y), new(other.Placed.Point.X, other.Placed.Point.Y)),
                    rally.ResolvedAt, bot.Realm, other.Placed.ResolvedAt, other.Realm, rally.Avoided.Contains(other.Placed.Point)));
            if (place)
            {
                rally.GateId = gate.DoorId;
                rally.Avoided = others.Select(other => other.Placed.Point).ToArray();
                RallyPlan plan = ResolvePoint(bracket.RegionId, keep, gate, arrival, rally.Avoided);
                (rally.Point, rally.GuardClear, rally.Bearing) = (plan.Point, plan.GuardClear, plan.Bearing);
                rally.ResolvedAt = now;
                if (plan.Point is { } at)
                    Log.Info($"BG_RALLY_PLACED battleground={bracket.Name} realm={GlobalConstants.RealmToName(bot.Realm)} keep=\"{keep.Name}\" " +
                             $"point={at.X:0},{at.Y:0},{at.Z:0} gate={rally.GateId} bearing={plan.Bearing:0} " +
                             $"apart={(plan.FromOtherRally >= float.MaxValue ? "none" : $"{plan.FromOtherRally:0}")} " +
                             $"separated={plan.Apart} others={string.Join(";", others.Select(other => $"{GlobalConstants.RealmToName(other.Realm)}@{other.Placed.Point.X:0},{other.Placed.Point.Y:0}"))}");
            }
            if (rally.Point is not { } point)
            {
                RallyPoints.TryRemove((bracket.RegionId, bot.Realm), out _);
                return default;
            }
            RallyPoints[(bracket.RegionId, bot.Realm)] = new PlacedRally(keep.KeepID, point, rally.ResolvedAt, now);
            if (now >= rally.NextCount)
            {
                rally.NextCount = now + CountMilliseconds;
                Count(rally, bot.Realm, bracket, keep, lord, point, now);
            }
            if (rally.Storming) StormingSeen[(bracket.RegionId, bot.Realm)] = now;
            if (rally.Storming && rally.Committed.Contains(bot.DatabaseID))
                return new Order(Step.Storm, point, $"{rally.Engaged} went up for {lord.Name}");
            if (rally.Storming && OnLordFloor(bot, lord))
            {
                // Already beside him (resurrected there, or fought its way up): it stays in the fight.
                CommitBot(rally, bot);
                rally.Engaged++;
                return new Order(Step.Storm, point, $"{rally.Engaged} went up for {lord.Name}");
            }
            bool atRally = AtRally(bot, point);
            if (rally.Storming)
            {
                if (atRally && rally.Rallied >= ReinforcementBatch(rally.Needed))
                {
                    int joined = Commit(rally, bot.Realm, bracket.RegionId, point);
                    Log.Info($"BG_RALLY_REINFORCE battleground={bracket.Name} realm={GlobalConstants.RealmToName(bot.Realm)} keep=\"{keep.Name}\" " +
                             $"joined={joined} engaged={rally.Engaged} lord={lord.HealthPercent}%");
                    if (rally.Committed.Contains(bot.DatabaseID))
                        return new Order(Step.Storm, point, $"{rally.Engaged} went up for {lord.Name}");
                }
                return new Order(Step.Rally, point,
                    $"{rally.Rallied} rallied, the next group goes up at {ReinforcementBatch(rally.Needed)}");
            }
            bool otherStorming = OtherRealmStorming(bracket.RegionId, bot.Realm, keep.Realm, now);
            if (atRally && ShouldStorm(rally.Rallied, rally.Needed, now - rally.GatherSince, otherStorming))
            {
                rally.Storming = true;
                StormingSeen[(bracket.RegionId, bot.Realm)] = now;
                rally.AttemptSince = rally.LastProgress = now;
                rally.LowestPercent = lord.HealthPercent;
                rally.Engaged = 0;
                int going = Commit(rally, bot.Realm, bracket.RegionId, point);
                int players = rally.RalliedPlayers;
                Log.Info($"BG_RALLY_STORM battleground={bracket.Name} realm={GlobalConstants.RealmToName(bot.Realm)} keep=\"{keep.Name}\" " +
                         $"going={going} players={players} needed={rally.Needed} modelled={rally.Modelled} inside={rally.Inside} " +
                         $"lordLevel={lord.Level} lord={lord.HealthPercent}% minutes={(now - rally.GatherSince) / 60_000.0:0.0} " +
                         $"withOtherRealm={otherStorming} rally={point.X:0},{point.Y:0},{point.Z:0} gate={rally.GateId}");
                if (rally.Committed.Contains(bot.DatabaseID))
                    return new Order(Step.Storm, point, $"{rally.Engaged} went up for {lord.Name}");
            }
            return new Order(Step.Rally, point, $"{rally.Rallied}/{rally.Needed} rallied");
        }
    }

    /// <summary>One line per realm with a rally in this battleground, for BG_KEEP_STATUS.</summary>
    public static string Describe(ushort region)
    {
        var parts = new List<string>();
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            if (!Rallies.TryGetValue((region, realm), out Rally rally) || rally.Point == null) continue;
            lock (rally)
                parts.Add(rally.Storming
                    ? $"{GlobalConstants.RealmToName(realm)}:storming {rally.Committed.Count}/{rally.Engaged} rallied={rally.Rallied}"
                    : $"{GlobalConstants.RealmToName(realm)}:rallying {rally.Rallied}/{rally.Needed}");
        }
        return string.Join(";", parts);
    }

    private static void Reset(Rally rally, long now)
    {
        if (rally.Storming) StormingSeen.TryRemove((rally.Region, rally.Realm), out _);
        rally.Storming = false;
        rally.GatherSince = now;
        rally.Committed.Clear();
        BotDutyLeases.ReleaseOwner(eDutyLeaseKind.BattlegroundWave, rally.LeaseOwner);
        rally.Engaged = 0;
        rally.NextCount = 0;
    }

    /// <summary>A living gamebot of the realm on its own battleground tour here (a player's group follows the player).</summary>
    private static bool IsMember(GameBot other, eRealm realm, ushort region) =>
        other.IsAlive && other.Realm == realm && other.CurrentRegionID == region && !BotDutyLeases.IsSpokenForByPlayer(other) &&
        AutonomousObjectiveAssignments.Is(other, eAutonomousObjectiveKind.Battleground);

    /// <summary>At the rally: near the point and on its floor (not up on the walls above it).</summary>
    private static bool AtRally(GameLiving living, Vector3 point) =>
        Math.Abs(living.Z - point.Z) <= RallyFloorHeight &&
        Vector2.DistanceSquared(new(living.X, living.Y), new(point.X, point.Y)) <= RallyRadius * RallyRadius;

    /// <summary>Beside the lord, on his floor (a rally right under his room is not in his fight).</summary>
    private static bool OnLordFloor(GameLiving living, GameLiving lord) =>
        Math.Abs(living.Z - lord.Z) <= LordFloorHeight && living.IsWithinRadius(lord, (int)InTheFightRadius);

    /// <summary>Every rallied bot of the realm joins the attempt.</summary>
    private static int Commit(Rally rally, eRealm realm, ushort region, Vector3 point)
    {
        int joined = 0;
        foreach (GameBot other in AutonomousBotRegistry.Snapshot())
        {
            if (!IsMember(other, realm, region) || !AtRally(other, point)) continue;
            if (CommitBot(rally, other)) joined++;
        }
        rally.Engaged += joined;
        rally.Rallied = 0;
        rally.RalliedPlayers = 0;
        return joined;
    }

    private static void Count(Rally rally, eRealm realm, BattlegroundBrackets.Bracket bracket, AbstractGameKeep keep,
        GameKeepGuard lord, Vector3 point, long now)
    {
        GameBot[] realmBots = AutonomousBotRegistry.Snapshot()
            .Where(other => IsMember(other, realm, bracket.RegionId)).ToArray();
        if (rally.Storming)
        {
            // The dead (and the released, gone to their bind point) leave the attempt; they rejoin the rally when back.
            var alive = new HashSet<long>(realmBots.Select(other => other.DatabaseID));
            rally.Committed.RemoveWhere(id => !alive.Contains(id));
            foreach (GameBot gone in BotDutyLeases.HoldersOf(eDutyLeaseKind.BattlegroundWave, rally.LeaseOwner))
                if (!alive.Contains(gone.DatabaseID))
                    BotDutyLeases.Release(gone, eDutyLeaseKind.BattlegroundWave, rally.LeaseOwner);
            if (lord.HealthPercent < rally.LowestPercent)
            {
                rally.LowestPercent = lord.HealthPercent;
                rally.LastProgress = now;
            }
            string over = AttemptOver(rally.Engaged, rally.Committed.Count, lord.HealthPercent, rally.LowestPercent, now - rally.LastProgress);
            if (over != null)
            {
                Log.Info($"BG_RALLY_REGROUP battleground={bracket.Name} realm={GlobalConstants.RealmToName(realm)} keep=\"{keep.Name}\" " +
                         $"reason=\"{over}\" engaged={rally.Engaged} standing={rally.Committed.Count} lord={lord.HealthPercent}% " +
                         $"lowest={rally.LowestPercent}% minutes={(now - rally.AttemptSince) / 60_000.0:0.0}");
                Reset(rally, now);
            }
        }
        GameBot[] rallied = realmBots.Where(other => !rally.Committed.Contains(other.DatabaseID) && AtRally(other, point)).ToArray();
        rally.Inside = realmBots.Length;
        // BG_KEEP_STATUS's own measures (attackersAtLord, attackersOnLordFloor), for this realm.
        rally.AtLord = realmBots.Count(other => other.IsWithinRadius(lord, 800));
        rally.OnLordFloor = realmBots.Count(other => other.IsWithinRadius(lord, 800) && Math.Abs(other.Z - lord.Z) < 200);
        rally.RalliedPlayers = PlayersAtRally(realm, bracket.RegionId, point);
        rally.Rallied = RallyStrength(rallied.Length, rally.RalliedPlayers);
        // "Enough" is worked out from the attackers who are there (or, before anyone is, the realm's bots in the battleground).
        GameBot[] sample = rallied.Length > 0 ? rallied : realmBots;
        int attackerLevel = sample.Length == 0 ? bracket.MaxLevel : (int)Math.Round(sample.Average(other => (double)other.Level));
        int attackerHealth = sample.Length == 0 ? 0 : (int)sample.Average(other => (double)other.MaxHealth);
        GameKeepGuard[] withLord = keep.Guards.Values.Where(guard => guard != lord && guard.IsAlive && guard is not GuardLord &&
            guard.CurrentRegionID == lord.CurrentRegionID && guard.IsWithinRadius(lord, (int)LordRoomRadius) &&
            Math.Abs(guard.Z - lord.Z) <= LordFloorHeight).ToArray();
        rally.Modelled = NeededAttackers(lord.Level, lord.Health, attackerLevel, attackerHealth, withLord.Length,
            withLord.Length == 0 ? 0 : (int)Math.Round(withLord.Average(guard => (double)guard.Level)),
            withLord.Length == 0 ? 0 : (long)withLord.Average(guard => (double)guard.MaxHealth));
        rally.Needed = RallyNeeded(rally.Modelled, rally.Inside);
        if (now >= rally.NextStatusLog)
        {
            rally.NextStatusLog = now + StatusLogMilliseconds;
            // How far the other attacking realm's rally stands (d64d005: both at one point).
            float apart = NearestOtherRally(point, OtherRallies(bracket.RegionId, realm, keep, now).Select(other => other.Placed.Point).ToArray());
            // Once a minute per realm (3b808b4: 46 minutes of "rallying 0/32" and nothing to say where the rally stood).
            Log.Info($"BG_RALLY_STATUS battleground={bracket.Name} realm={GlobalConstants.RealmToName(realm)} keep=\"{keep.Name}\" " +
                     $"state={(rally.Storming ? "storming" : "gathering")} rallying={rally.Rallied} players={rally.RalliedPlayers} " +
                     $"needed={rally.Needed} modelled={rally.Modelled} inside={rally.Inside} " +
                     $"point={point.X:0},{point.Y:0},{point.Z:0} gate={rally.GateId} guardClear={(rally.GuardClear >= float.MaxValue ? "none" : $"{rally.GuardClear:0}")} " +
                     $"bearing={rally.Bearing:0} apart={(apart >= float.MaxValue ? "none" : $"{apart:0}")} " +
                     $"waitedSeconds={(rally.Storming ? 0 : (now - rally.GatherSince) / 1000)} " +
                     $"otherStorming={OtherRealmStorming(bracket.RegionId, realm, keep.Realm, now)} " +
                     $"committed={rally.Committed.Count} engaged={rally.Engaged} onLordFloor={rally.OnLordFloor} atLord={rally.AtLord} " +
                     $"lord={lord.HealthPercent}% diedToGuards={rally.DiedToGuards} diedOther={rally.DiedOther} " +
                     $"fightsRefused={rally.Refused} chasesDropped={rally.Dropped}");
            rally.DiedToGuards = rally.DiedOther = rally.Refused = rally.Dropped = 0;
        }
    }

    /// <summary>Living real players of the realm standing at the rally (any privilege level, so a tester's GM character counts).</summary>
    private static int PlayersAtRally(eRealm realm, ushort region, Vector3 point)
    {
        Region here = WorldMgr.GetRegion(region);
        if (here == null) return 0;
        return ClientService.Instance.GetPlayersOfRegionAndRealm(here, realm)
            .Count(player => player != null && player.IsAlive && AtRally(player, point));
    }

    /// <summary>The breach the realm rallies at (<see cref="BreachIndex"/>).</summary>
    private static GameKeepDoor Breach(AbstractGameKeep keep, GameLocation arrival)
    {
        GameKeepDoor[] doors = keep.Doors.Values.Where(door => door.IsAttackableDoor).ToArray();
        var facts = new (int Number, bool PosternLike, bool Down, float FromCentre, float FromArrival)[doors.Length];
        for (int i = 0; i < doors.Length; i++)
        {
            GameKeepDoor door = doors[i];
            facts[i] = (AutonomousWorldBotController.SiegeDoorNumber(door), AutonomousWorldBotController.PosternLike(keep, door),
                !door.IsAlive || door.State == eDoorState.Open,
                Vector2.Distance(new(door.X, door.Y), new(keep.X, keep.Y)),
                arrival == null ? 0 : Vector2.Distance(new(door.X, door.Y), new(arrival.X, arrival.Y)));
        }
        int index = BreachIndex(facts);
        return index < 0 ? null : doors[index];
    }

    /// <summary>
    /// The rally point outside the breach, on the navmesh, with a complete walk from it to the gate's front, clear of the
    /// guards' posts where it can be and apart from the other attacking realms' rallies (<see cref="PlanRally"/>). Null
    /// when nothing outside the breach can be walked to.
    /// </summary>
    private static RallyPlan ResolvePoint(ushort region, AbstractGameKeep keep, GameKeepDoor gate, GameLocation arrival,
        IReadOnlyList<Vector3> otherRallies)
    {
        Vector3 gatePoint = new(gate.X, gate.Y, gate.Z);
        Vector2 centre = new(keep.X, keep.Y);
        // A door behind the outer gate faces that gate; any other door faces out (as AutonomousWorldBotController.DoorFront).
        Vector2? approach = AutonomousWorldBotController.InnerDoorApproach(keep, gate) is { } outer &&
            Vector2.Distance(outer, centre) > Vector2.Distance(new(gate.X, gate.Y), centre) ? outer : null;
        Vector2 outward = SiegePlacement.OutwardNormal(new(gate.X, gate.Y), gate.Heading, centre, approach);
        Region here = WorldMgr.GetRegion(region);
        Zone zone = here?.GetZone(gate.X, gate.Y);
        IPathfindingMgr nav = PathfindingProvider.Instance;
        var none = new RallyPlan(null, 0, 0, float.MaxValue);
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return none;
        Vector2[] posts = keep.Guards.Values.Where(guard => guard is not GuardLord && guard.SpawnPoint != null)
            .Select(guard => new Vector2(guard.SpawnPoint.X, guard.SpawnPoint.Y)).ToArray();
        float Clearance(Vector3 at) => posts.Length == 0 ? float.MaxValue
            : posts.Min(post => Vector2.Distance(post, new(at.X, at.Y)));
        Vector3 front = nav.GetClosestPoint(zone, BreachAnchor(gatePoint, outward, BreachFrontDistance), 120, 120, 256, nav.DefaultFilters)
                        ?? gatePoint;
        Vector3? Floor(Vector3 raw)
        {
            if (here.GetZone((int)raw.X, (int)raw.Y) != zone) return null;
            if (nav.GetClosestPoint(zone, raw, 200, 200, 600, nav.DefaultFilters) is not { } floor ||
                Vector2.Distance(new(floor.X, floor.Y), new(raw.X, raw.Y)) > 300 || Math.Abs(floor.Z - gate.Z) > 600)
                return null;
            return floor;
        }
        RallyPlan plan = PlanRally(gatePoint, outward, arrival == null ? null : new Vector3(arrival.X, arrival.Y, arrival.Z),
            otherRallies, Floor, at => AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, at, front), Clearance);
        if (plan.Point != null) return plan;
        // Nothing farther out can be walked to: right at the breach.
        return Math.Abs(front.Z - gate.Z) <= 200
            ? new RallyPlan(front, Clearance(front), 0, NearestOtherRally(front, otherRallies))
            : none;
    }
}
