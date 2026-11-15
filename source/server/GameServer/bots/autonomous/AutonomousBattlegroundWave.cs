using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// A realm's battleground assault goes in as one wave. Run 5 (2026-10-14): in all four battlegrounds about half of the
/// bots fought the other realms in the open field (median 3,700-5,600 units from the central keep) and at most a
/// handful of any realm stood at the keep, so the gates were broken and rebuilt for hours and no lord was ever
/// touched. Now the realm's bots gather at a staging point on their side of the keep and assault together once most
/// of them are there (or, after four minutes or while another realm's wave goes in, with half of them); when the wave
/// is spent they gather again. A gatherer fights only what comes at it, and BG_WAVE_STATUS logs each realm's wave
/// once a minute.
/// </summary>
public static class AutonomousBattlegroundWave
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBattlegroundWave));

    /// <summary>
    /// How far out from the central keep a realm gathers. Was 2,500, inside the keep's reach: on the owner's PC (build
    /// 3d67b6f, Thidranki, 2026-11-15) the keep's guards killed 45 Midgard bots 1,512-3,109 from the keep (median 2,230),
    /// 20 of them within 900 of Midgard's own staging point, and Midgard never sent a wave.
    /// </summary>
    public const float StagingDistance = 3_500f;
    public const int MinimumWave = 8;
    public const long GatherPatienceMilliseconds = 4 * 60_000;
    public const long WaveSpentCheckMilliseconds = 3 * 60_000;
    public const int WaveSpentNearKeep = 4;
    /// <summary>After <see cref="GatherPatienceMilliseconds"/> a wave goes with half the realm's bots inside, never fewer than this.</summary>
    public const int MinimumPatienceWave = 4;
    /// <summary>A gatherer counts as gathered within this of the staging point.</summary>
    public const int GatheredRadius = 900;
    /// <summary>An enemy this close to the staging point has walked into the gathered realm: it is fought.</summary>
    public const float GatherCloseRadius = 700f;
    /// <summary>An enemy firing on the realm is fought back while it stays this close to the staging point...</summary>
    public const float GatherDefendRadius = 1_500f;
    /// <summary>...or this close to the gatherer itself (on the way to the staging point).</summary>
    public const float GatherSelfDefenseRadius = 1_000f;
    public const long StatusLogMilliseconds = 60_000;

    private sealed class Wave
    {
        public Vector3? Staging;
        public long GatherSince, GoingSince, NextCount, NextTotalCount, NextStatusLog;
        public bool Going;
        public int Total, Gathered, NearKeep, GatheredInCombat, HolderNearKeep;
        // Fights a gatherer did not pick (TryEngageFrontierThreat) or dropped (a chase away from the staging point).
        public int Refused, Dropped;
    }

    private static readonly ConcurrentDictionary<(ushort Region, eRealm Realm), Wave> Waves = new();

    /// <summary>
    /// Too few of the realm's bots in the battleground for any wave (fewer than <see cref="MinimumWave"/>): they roam the
    /// field instead of the keep. The owner's 2026-10-17 session: Thidranki held 1-2 bots per realm (the roster has 47
    /// bots of levels 20-24); they waited at the staging point for a wave of eight that never came, and single bots
    /// that strayed into the keep's guards announced sieges that went nowhere.
    /// </summary>
    public static bool TooFewForAssault(eRealm realm, BattlegroundBrackets.Bracket bracket)
    {
        if (bracket == null) return false;
        long now = GameLoop.GameLoopTime;
        Wave wave = Waves.GetOrAdd((bracket.RegionId, realm), _ => new Wave { GatherSince = now });
        lock (wave)
        {
            if (now >= wave.NextTotalCount)
            {
                wave.NextTotalCount = now + 5_000;
                wave.Total = AutonomousBotRegistry.Snapshot().Count(other => other.IsAlive && other.Realm == realm &&
                    other.CurrentRegionID == bracket.RegionId);
            }
            return IsTooFew(wave.Total);
        }
    }

    public static bool IsTooFew(int realmBotsInside) => realmBotsInside < MinimumWave;

    /// <summary>A wave goes in at this many gathered: 60% of the realm's bots inside, at least <see cref="MinimumWave"/>.</summary>
    public static int WaveSize(int realmBotsInside) => Math.Max(MinimumWave, realmBotsInside * 6 / 10);

    /// <summary>
    /// After <see cref="GatherPatienceMilliseconds"/> the wave goes with half the realm's bots inside (rounded up), at
    /// least <see cref="MinimumPatienceWave"/>. Was a fixed <see cref="MinimumWave"/>: with 11-14 of a realm inside and
    /// each of them dying about every 3.5 minutes (owner's PC, build 3d67b6f, Thidranki 2026-11-15), eight never stood
    /// at the staging point together and no wave went in for 16 minutes.
    /// </summary>
    public static int PatienceWaveSize(int realmBotsInside) => Math.Max(MinimumPatienceWave, (realmBotsInside + 1) / 2);

    /// <summary>
    /// The wave goes in: most of the realm is gathered, or half of it after the patience time, or half of it while another
    /// realm's wave is going in on the same keep (the holder's guards and defenders are busy with them). Pure.
    /// </summary>
    public static bool WaveGoes(int gathered, int realmBotsInside, long waitedMilliseconds, bool otherRealmGoing = false) =>
        gathered >= WaveSize(realmBotsInside) ||
        (waitedMilliseconds >= GatherPatienceMilliseconds || otherRealmGoing) && gathered >= PatienceWaveSize(realmBotsInside);

    /// <summary>
    /// While the gates stand, a going wave takes a keep guard only when it is near (or firing on the realm), and otherwise
    /// marches on with the wave to the gate. Before, every bot ran at its own nearest guard up to 5,000 away: the
    /// Hibernian wave of 07:49:41 (owner's PC, build 3d67b6f) lost ten bots to guards 1,100-2,900 from the keep in three
    /// minutes and never reached the gate (BG_STORM_TRACE toGate=3528, BG_WAVE_SPENT nearKeep=0). Pure.
    /// </summary>
    public static bool WaveTakesGuard(bool gatesShut, bool isLord, float distance, bool firingOnRealm) =>
        !gatesShut || isLord || firingOnRealm || distance <= WaveGuardReach;

    /// <summary>See <see cref="WaveTakesGuard"/>.</summary>
    public const float WaveGuardReach = 1_200f;

    // When each realm's going wave was last seen, per region; read without the waves' locks (no lock order between
    // realms). A wave not seen going for 30 s (spent, or its realm took the keep) no longer counts.
    private static readonly ConcurrentDictionary<(ushort Region, eRealm Realm), long> GoingSeen = new();

    private static bool OtherRealmGoing(ushort regionId, eRealm realm, eRealm holder, long now) =>
        GoingSeen.Any(pair => pair.Key.Region == regionId && pair.Key.Realm != realm && pair.Key.Realm != holder &&
                              now - pair.Value <= 30_000);

    /// <summary>
    /// Whether a gathering bot takes this fight: an enemy that walked into the gathered realm, or one firing on the realm
    /// near the staging point or near this bot. Anything else (the holder's defenders on the walls, another realm's
    /// gatherers, a guard, an enemy that broke off) is left alone until the wave goes. Pure.
    /// </summary>
    public static bool GathererMayEngage(bool firingOnRealm, float targetToStaging, float targetToBot) =>
        targetToStaging <= GatherCloseRadius ||
        firingOnRealm && (targetToStaging <= GatherDefendRadius || targetToBot <= GatherSelfDefenseRadius);

    /// <summary>The raw staging point (before it is settled on the navmesh): from the keep toward the realm's arrival. Pure.</summary>
    public static Vector3? StagingRaw(Vector3 keep, Vector3 arrival)
    {
        Vector3 toward = arrival - keep;
        float length = toward.Length();
        if (length < 1) return null;
        return keep + toward / length * Math.Min(StagingDistance, length * 0.8f);
    }

    /// <summary>
    /// The realm's wave is gathering (not going) in this region: <paramref name="staging"/> is where. Used to keep the
    /// gatherers out of fights that are not theirs while in combat (the controller's fight turn runs before the wave's).
    /// </summary>
    public static bool IsGathering(ushort regionId, eRealm realm, out Vector3 staging)
    {
        staging = default;
        if (!Waves.TryGetValue((regionId, realm), out Wave wave)) return false;
        lock (wave)
        {
            if (wave.Going || !wave.Staging.HasValue) return false;
            staging = wave.Staging.Value;
            return true;
        }
    }

    /// <summary>Counts a fight a gatherer left alone, for BG_WAVE_STATUS.</summary>
    public static void NoteHeldBack(ushort regionId, eRealm realm, bool dropped)
    {
        if (!Waves.TryGetValue((regionId, realm), out Wave wave)) return;
        lock (wave)
        {
            if (dropped) wave.Dropped++;
            else wave.Refused++;
        }
    }

    /// <summary>
    /// True while this bot should gather at <paramref name="staging"/> instead of assaulting; false when its realm's
    /// wave is going in (or no staging point could be found, or every gate is already down), so the ordinary assault runs.
    /// Called for every assaulting bot each turn, in a fight or not, so BG_WAVE_STATUS is written once a minute per realm.
    /// </summary>
    public static bool ShouldGather(GameBot bot, BattlegroundBrackets.Bracket bracket, AbstractGameKeep central, out Vector3 staging,
        out string progress)
    {
        staging = default; progress = string.Empty;
        long now = GameLoop.GameLoopTime;
        Wave wave = Waves.GetOrAdd((bracket.RegionId, bot.Realm), _ => new Wave { GatherSince = now });
        lock (wave)
        {
            // Gates down: everyone goes for the lord. In the owner's 2026-10-14 session both gates of Dun Abermenai stood
            // broken for over an hour at a time while the realms kept gathering for waves, and the lord was never touched.
            bool gatesDown = !central.Doors.Values.Any(door => door.IsAlive && door.IsAttackableDoor && door.State == eDoorState.Closed);
            wave.Staging ??= StagingPoint(bot, bracket, central);
            if (now >= wave.NextCount)
            {
                wave.NextCount = now + 5_000;
                var keepPoint = new Point3D(central.X, central.Y, central.Z);
                GameBot[] inside = AutonomousBotRegistry.Snapshot().Where(other => other.IsAlive &&
                    other.CurrentRegionID == bracket.RegionId).ToArray();
                GameBot[] realmBots = inside.Where(other => other.Realm == bot.Realm).ToArray();
                wave.HolderNearKeep = central.Realm == eRealm.None ? 0
                    : inside.Count(other => other.Realm == central.Realm && other.IsWithinRadius(keepPoint, 2_500));
                wave.Total = realmBots.Length;
                wave.NearKeep = realmBots.Count(other => other.IsWithinRadius(keepPoint, 2_500));
                if (wave.Staging is { } point)
                {
                    var stagePoint = new Point3D((int)point.X, (int)point.Y, (int)point.Z);
                    GameBot[] gathered = realmBots.Where(other => other.IsWithinRadius(stagePoint, GatheredRadius)).ToArray();
                    wave.Gathered = gathered.Length;
                    wave.GatheredInCombat = gathered.Count(other => other.InCombat);
                }
            }
            bool going;
            if (gatesDown || !wave.Staging.HasValue) going = true;
            else if (wave.Going)
            {
                if (now - wave.GoingSince >= WaveSpentCheckMilliseconds && wave.NearKeep < WaveSpentNearKeep)
                {
                    wave.Going = false;
                    wave.GatherSince = now;
                    GoingSeen.TryRemove((bracket.RegionId, bot.Realm), out _);
                    Log.Info($"BG_WAVE_SPENT battleground={bracket.Name} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                             $"nearKeep={wave.NearKeep} inside={wave.Total}");
                    going = false;
                }
                else going = true;
            }
            else going = false;
            bool otherGoing = !going && OtherRealmGoing(bracket.RegionId, bot.Realm, central.Realm, now);
            if (!going && WaveGoes(wave.Gathered, wave.Total, now - wave.GatherSince, otherGoing))
            {
                wave.Going = true;
                wave.GoingSince = now;
                going = true;
                Log.Info($"BG_WAVE_ASSAULT battleground={bracket.Name} realm={GlobalConstants.RealmToName(bot.Realm)} keep=\"{central.Name}\" " +
                         $"gathered={wave.Gathered} needed={WaveSize(wave.Total)} patienceNeeded={PatienceWaveSize(wave.Total)} inside={wave.Total} " +
                         $"minutes={(now - wave.GatherSince) / 60_000.0:0.0} withOtherRealm={otherGoing}");
            }
            if (now >= wave.NextStatusLog)
            {
                wave.NextStatusLog = now + StatusLogMilliseconds;
                string at = wave.Staging is { } s ? $"{s.X:0},{s.Y:0},{s.Z:0}" : "none";
                // Once a minute per realm (owner's PC, build 3d67b6f: 16 minutes of "Gathering for the assault" and nothing
                // in the log to say how many had gathered).
                Log.Info($"BG_WAVE_STATUS battleground={bracket.Name} realm={GlobalConstants.RealmToName(bot.Realm)} keep=\"{central.Name}\" " +
                         $"inside={wave.Total} gathered={wave.Gathered} gatheredInCombat={wave.GatheredInCombat} needed={WaveSize(wave.Total)} " +
                         $"patienceNeeded={PatienceWaveSize(wave.Total)} waitedSeconds={(wave.Going ? 0 : (now - wave.GatherSince) / 1000)} " +
                         $"staging={at} nearKeep={wave.NearKeep} holderNearKeep={wave.HolderNearKeep} going={(gatesDown ? "gates-down" : !wave.Staging.HasValue ? "no-staging" : wave.Going ? "yes" : "no")} " +
                         $"goingSeconds={(wave.Going ? (now - wave.GoingSince) / 1000 : 0)} fightsRefused={wave.Refused} chasesDropped={wave.Dropped}");
                wave.Refused = 0;
                wave.Dropped = 0;
            }
            if (wave.Going && !gatesDown) GoingSeen[(bracket.RegionId, bot.Realm)] = now;
            if (going) return false;
            staging = wave.Staging.Value;
            progress = $"{wave.Gathered}/{WaveSize(wave.Total)} gathered";
            return true;
        }
    }

    /// <summary>On the realm's side of the keep: from the keep toward the realm's field arrival, on the navmesh.</summary>
    private static Vector3? StagingPoint(GameBot bot, BattlegroundBrackets.Bracket bracket, AbstractGameKeep central)
    {
        GameLocation arrival = BattlegroundBrackets.BotArrival(bracket, bot.Realm);
        if (arrival == null) return null;
        if (StagingRaw(new(central.X, central.Y, central.Z), new(arrival.X, arrival.Y, arrival.Z)) is not { } raw) return null;
        Zone zone = WorldMgr.GetRegion(bracket.RegionId)?.GetZone((int)raw.X, (int)raw.Y);
        IPathfindingMgr nav = PathfindingProvider.Instance;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return null;
        return nav.GetClosestPoint(zone, raw, 600, 600, 2_000, nav.DefaultFilters);
    }
}
