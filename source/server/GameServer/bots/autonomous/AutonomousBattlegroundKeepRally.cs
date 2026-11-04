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
/// assaulting gamebots rally just inside the innermost broken gate. They go up together when enough are there to win
/// against the lord and the guards with him, worked out from their real levels and health (<see cref="NeededAttackers"/>).
/// An attempt ends like a raid's: half its stormers fell, the lord healed back, or nothing happened to him for three
/// minutes; then the survivors rally again. A bot that died rejoins the rally instead of running back to him alone, and
/// bots that gather at the rally while an attempt runs go up in batches (<see cref="ReinforcementBatch"/>). The lord,
/// his guards and their respawns are untouched: only the bots decide better when to go.
/// </summary>
public static class AutonomousBattlegroundKeepRally
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBattlegroundKeepRally));

    /// <summary>A bot this close to the rally point counts as rallied.</summary>
    public const float RallyRadius = 1_000f;
    /// <summary>A rallying bot stops walking this close to the rally point.</summary>
    public const float RallyHoldRadius = 250f;
    /// <summary>How far past the gate (toward the lord, on the gate's floor) the rally stands.</summary>
    public const float RallyStepInside = 200f;
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

    /// <summary>Whether a gathering rally goes up now. Pure.</summary>
    public static bool ShouldStorm(int rallied, int needed, long gatheringMilliseconds) =>
        rallied >= needed ||
        gatheringMilliseconds >= RallyPatienceMilliseconds && rallied >= Math.Max(MinimumRally, (needed * PatientSharePercent + 99) / 100) ||
        gatheringMilliseconds >= RallyLastCallMilliseconds && rallied >= MinimumRally;

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

    /// <summary>The rally point: <see cref="RallyStepInside"/> past the gate toward the lord, on the gate's floor. Pure.</summary>
    public static Vector3 RallyAnchor(Vector3 gate, Vector3 lord)
    {
        Vector2 toward = new(lord.X - gate.X, lord.Y - gate.Y);
        float length = toward.Length();
        if (length < 1) return gate;
        Vector2 step = toward / length * Math.Min(RallyStepInside, length * 0.5f);
        return new Vector3(gate.X + step.X, gate.Y + step.Y, gate.Z);
    }

    public enum Step { None, Rally, Storm }

    public readonly record struct Order(Step Step, Vector3 Point, string Progress)
    {
        public bool Hold => Step == Step.Rally;
    }

    private sealed class Rally
    {
        public int KeepId;
        public eRealm KeepRealm;
        public int GateId;
        public Vector3? Point;
        public bool Storming;
        public long GatherSince, AttemptSince, LastProgress, NextCount, LastAsked;
        public int Rallied, RalliedPlayers, Needed, Engaged, LowestPercent;
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

    /// <summary>
    /// What this assaulting bot does at an exposed lord: rally (hold at <see cref="Order.Point"/>) or storm him.
    /// <see cref="Step.None"/> when no rally point could be found (the ordinary storm then runs, as before).
    /// </summary>
    public static Order Decide(GameBot bot, BattlegroundBrackets.Bracket bracket, AbstractGameKeep keep, GameKeepGuard lord)
    {
        long now = GameLoop.GameLoopTime;
        Rally rally = Rallies.GetOrAdd((bracket.RegionId, bot.Realm),
            _ => new Rally { GatherSince = now, LeaseOwner = RallyLeaseOwner(bracket.RegionId, bot.Realm) });
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
            GameKeepDoor gate = InnermostOpenGate(keep, lord);
            if (gate == null) return default;
            if (rally.Point == null || rally.GateId != gate.DoorId)
            {
                rally.GateId = gate.DoorId;
                rally.Point = ResolvePoint(bracket.RegionId, gate, lord);
            }
            if (rally.Point is not { } point) return default;
            if (now >= rally.NextCount)
            {
                rally.NextCount = now + CountMilliseconds;
                Count(rally, bot.Realm, bracket, keep, lord, point, now);
            }
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
            if (atRally && ShouldStorm(rally.Rallied, rally.Needed, now - rally.GatherSince))
            {
                rally.Storming = true;
                rally.AttemptSince = rally.LastProgress = now;
                rally.LowestPercent = lord.HealthPercent;
                rally.Engaged = 0;
                int going = Commit(rally, bot.Realm, bracket.RegionId, point);
                int players = rally.RalliedPlayers;
                Log.Info($"BG_RALLY_STORM battleground={bracket.Name} realm={GlobalConstants.RealmToName(bot.Realm)} keep=\"{keep.Name}\" " +
                         $"going={going} players={players} needed={rally.Needed} lordLevel={lord.Level} lord={lord.HealthPercent}% " +
                         $"minutes={(now - rally.GatherSince) / 60_000.0:0.0} rally={point.X:0},{point.Y:0},{point.Z:0}");
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

    /// <summary>At the rally: near the point and on its floor (the lord's room above the breach doesn't count).</summary>
    private static bool AtRally(GameLiving living, Vector3 point) =>
        Math.Abs(living.Z - point.Z) <= LordFloorHeight &&
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
        rally.RalliedPlayers = PlayersAtRally(realm, bracket.RegionId, point);
        rally.Rallied = RallyStrength(rallied.Length, rally.RalliedPlayers);
        // "Enough" is worked out from the attackers who are there (or, before anyone is, the realm's bots in the battleground).
        GameBot[] sample = rallied.Length > 0 ? rallied : realmBots;
        int attackerLevel = sample.Length == 0 ? bracket.MaxLevel : (int)Math.Round(sample.Average(other => (double)other.Level));
        int attackerHealth = sample.Length == 0 ? 0 : (int)sample.Average(other => (double)other.MaxHealth);
        GameKeepGuard[] withLord = keep.Guards.Values.Where(guard => guard != lord && guard.IsAlive && guard is not GuardLord &&
            guard.CurrentRegionID == lord.CurrentRegionID && guard.IsWithinRadius(lord, (int)LordRoomRadius) &&
            Math.Abs(guard.Z - lord.Z) <= LordFloorHeight).ToArray();
        rally.Needed = NeededAttackers(lord.Level, lord.Health, attackerLevel, attackerHealth, withLord.Length,
            withLord.Length == 0 ? 0 : (int)Math.Round(withLord.Average(guard => (double)guard.Level)),
            withLord.Length == 0 ? 0 : (long)withLord.Average(guard => (double)guard.MaxHealth));
    }

    /// <summary>Living real players of the realm standing at the rally (any privilege level, so a tester's GM character counts).</summary>
    private static int PlayersAtRally(eRealm realm, ushort region, Vector3 point)
    {
        Region here = WorldMgr.GetRegion(region);
        if (here == null) return 0;
        return ClientService.Instance.GetPlayersOfRegionAndRealm(here, realm)
            .Count(player => player != null && player.IsAlive && AtRally(player, point));
    }

    /// <summary>The broken (or open) main gate nearest the lord: the breach the realm goes up from.</summary>
    private static GameKeepDoor InnermostOpenGate(AbstractGameKeep keep, GameKeepGuard lord) =>
        keep.Doors.Values
            .Where(door => door.IsAttackableDoor && !door.IsPostern && (!door.IsAlive || door.State == eDoorState.Open))
            .OrderBy(door => door.GetDistanceTo(lord))
            .FirstOrDefault();

    private static Vector3? ResolvePoint(ushort region, GameKeepDoor gate, GameKeepGuard lord)
    {
        Vector3 gatePoint = new(gate.X, gate.Y, gate.Z);
        Vector3 raw = RallyAnchor(gatePoint, new(lord.X, lord.Y, lord.Z));
        Zone zone = WorldMgr.GetRegion(region)?.GetZone((int)raw.X, (int)raw.Y);
        IPathfindingMgr nav = PathfindingProvider.Instance;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return null;
        // On the gate's own floor: never snapped up onto a wall or the lord's floor.
        foreach (Vector3 candidate in new[] { raw, gatePoint })
        {
            Vector3? floor = nav.GetClosestPoint(zone, candidate, 150, 150, 200, nav.DefaultFilters);
            if (floor is { } found && Math.Abs(found.Z - gate.Z) <= 200)
                return found;
        }
        return null;
    }
}
