using System;
using System.Collections.Generic;
using System.Globalization;
using DOL.AI.Brain;

namespace DOL.GS;

// ---- Raid fight samples and the raid funnel, #644 parts 2-3 ------------------------------------------------------------
//
// Logging only, like the part 1 rows (LivingRows.Raid.cs): nothing here changes what a raider, a boss or the raid AI does.
// No game state is written, no target, path or aggro is set, no timer is started and no random number is drawn. No player,
// account or bot name is in any row (raiders are counted by role; the boss is the attempt's id).
//
//   RAID_FIGHT_SAMPLE  every 15 s while an attempt is live (dungeon, dragon or open-world boss), from the attempt's own raiders
//                      (RaidAttempt's members: no scan of the world). At most 4 a minute per attempt and 30 a minute overall,
//                      both through LivingRows.Take (drops counted in RVR_HOUR). About 240 an hour per live attempt.
//   RAID_FUNNEL        twice per raid: stage=start at REALM_RAID_STARTED, stage=end at REALM_RAID_ENDED. No budget (2 a raid).
//
// Left out on purpose ("-" in the row, never a guess):
//   phase=  no boss script exposes its phase in a common way (the dragons' flights are out of combat, not fight phases).
//   adds=   only the Green Knight and the ranged-add parties count their adds (EPIC_GK_TREES, EPIC_RANGED_ADDS); a generic count
//           needs a scan around the boss, which this row does not do.

/// <summary>The pure formatters and helpers of RAID_FIGHT_SAMPLE and RAID_FUNNEL (#644 parts 2-3), and the sampler.</summary>
public static class RaidFightSampleRows
{
    public const string SampleMarker = "RAID_FIGHT_SAMPLE";
    public const string FunnelMarker = "RAID_FUNNEL";

    /// <summary>A sample every 15 s of a live attempt.</summary>
    public const long IntervalMilliseconds = 15_000;

    /// <summary>The raid pulse looks for due samples this often (so a sample is at most 5 s late).</summary>
    public const long CheckMilliseconds = 5_000;

    public const int PerAttemptPerMinute = 4;
    public const int OverallPerMinute = 30;

    /// <summary>A raider is engaged when it is alive, in combat and within this distance of the boss.</summary>
    public const int EngagedRange = 1_500;

    /// <summary>A healer is low on power under this percentage.</summary>
    public const int LowPowerPercent = 10;

    /// <summary>The per-attempt budget's tag (one per attempt; forgotten when the attempt ends). Pure.</summary>
    public static string AttemptBudgetTag(string attemptId) => SampleMarker + "." + RaidAttemptRows.Token(attemptId);

    /// <summary>
    /// Both budgets (#644): the attempt's 4 a minute, then the 30 a minute over all attempts. A refused row is counted under the
    /// budget that refused it (RvrRecordBudget, RVR_HOUR's dropped=).
    /// </summary>
    public static bool TakeBudget(string attemptBudgetTag) =>
        LivingRows.Take(attemptBudgetTag, PerAttemptPerMinute) && LivingRows.Take(SampleMarker, OverallPerMinute);

    // ---- what one raider looks like at a sample ------------------------------------------------------------------------

    /// <summary>
    /// One raider at a sample, read once (never a name). <c>Dist</c> is to the boss, -1 when not in its region or unknown;
    /// <c>Mana</c> is the power percentage, -1 when unknown.
    /// </summary>
    public struct Raider
    {
        public string Role;
        public bool Alive;
        public bool InRegion;
        public bool InCombat;
        public int Dist;
        public int Mana;
        public bool Mezzed;
        public bool Stunned;
    }

    /// <summary>What a sample counts over the attempt's raiders. Pure.</summary>
    public readonly record struct Counts(string Alive, string Engaged, int HealerPow, int HealersLow, int MedDist, int Mezzed, int Stunned);

    private static int RoleSlot(string role) => role switch
    {
        "tank" => 0,
        "healer" => 1,
        "buffer" => 2,
        "attacker" => 3,
        _ => -1,
    };

    private static string Split(int t, int h, int b, int a) => string.Create(CultureInfo.InvariantCulture, $"{t}:{h}:{b}:{a}");

    /// <summary>
    /// The sample's counts (pure). Only raiders alive and in the attempt's region count; a raider with no role is in no
    /// T:H:B:A slot but still in medDist, mezzed and stunned. <paramref name="distScratch"/> is a buffer at least
    /// <paramref name="n"/> long (the attempt's own, allocated once): the median distance sorts it in place.
    /// healerPow: the mean power % of the living healers (the healer role, the Bard included), -1 with none (or none
    /// known). healersLow: those under <see cref="LowPowerPercent"/>. medDist: -1 with no distance known.
    /// </summary>
    public static Counts Count(Raider[] raiders, int n, int[] distScratch)
    {
        int at = 0, ht = 0, bt = 0, att = 0;
        int et = 0, eh = 0, eb = 0, ea = 0;
        long powerSum = 0;
        int powerN = 0, low = 0, dists = 0, mezzed = 0, stunned = 0;
        for (int i = 0; i < n; i++)
        {
            ref Raider r = ref raiders[i];
            if (!r.Alive || !r.InRegion) continue;
            int slot = RoleSlot(r.Role);
            bool engaged = r.InCombat && r.Dist >= 0 && r.Dist <= EngagedRange;
            switch (slot)
            {
                case 0: at++; if (engaged) et++; break;
                case 1: ht++; if (engaged) eh++; break;
                case 2: bt++; if (engaged) eb++; break;
                case 3: att++; if (engaged) ea++; break;
            }
            if (slot == 1 && r.Mana >= 0)
            {
                powerSum += r.Mana;
                powerN++;
                if (r.Mana < LowPowerPercent) low++;
            }
            if (r.Dist >= 0 && distScratch != null && dists < distScratch.Length) distScratch[dists++] = r.Dist;
            if (r.Mezzed) mezzed++;
            if (r.Stunned) stunned++;
        }
        return new Counts(Split(at, ht, bt, att), Split(et, eh, eb, ea), MeanPercent(powerSum, powerN), low,
            Median(distScratch, dists), mezzed, stunned);
    }

    /// <summary>The rounded mean, -1 for none. Pure.</summary>
    public static int MeanPercent(long sum, int n) => n <= 0 ? -1 : (int)Math.Round(sum / (double)n, MidpointRounding.AwayFromZero);

    /// <summary>The median of the first <paramref name="n"/> values (sorted in place); the lower middle for an even count; -1 for none. Pure.</summary>
    public static int Median(int[] values, int n)
    {
        if (values == null || n <= 0) return -1;
        n = Math.Min(n, values.Length);
        Array.Sort(values, 0, n);
        return values[(n - 1) / 2];
    }

    /// <summary>
    /// What the boss is fighting (pure): none (no target), tank / healer / buffer / attacker (a raider of the attempt, by role;
    /// raider for one with no class), pet (a pet of a raider of the attempt), other (anyone or anything else).
    /// </summary>
    public static string TargetKind(bool hasTarget, bool raider, string raiderRole, bool raidersPet)
    {
        if (!hasTarget) return "none";
        if (raider) return RoleSlot(raiderRole) >= 0 ? raiderRole : "raider";
        return raidersPet ? "pet" : "other";
    }

    /// <summary>onTank: 1 when the boss fights a tank of the attempt, 0 when it fights anyone else, "-" with no target. Pure.</summary>
    public static string OnTank(string targetKind) => targetKind switch
    {
        null or "none" or "-" => "-",
        "tank" => "1",
        _ => "0",
    };

    private static string Num(int value) => value < 0 ? "-" : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>What a RAID_FIGHT_SAMPLE row says. A negative number is unknown ("-"); Phase and Adds null are unknown.</summary>
    public sealed record SampleFacts(string Attempt, long T, int BossPct, string Phase, int Adds, Counts Counts, string TgtKind);

    /// <summary>
    /// <c>RAID_FIGHT_SAMPLE attempt= t= bossPct= phase= adds= alive=T:H:B:A engaged=T:H:B:A healerPow= healersLow= onTank= tgtKind=
    /// medDist= mezzed= stunned=</c>. Pure.
    /// </summary>
    public static string FormatSample(SampleFacts f) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{SampleMarker} attempt={RaidAttemptRows.Token(f.Attempt)} t={Math.Max(0, f.T)} bossPct={Num(f.BossPct)} " +
            $"phase={RaidAttemptRows.Token(f.Phase)} adds={Num(f.Adds)} alive={RaidAttemptRows.Token(f.Counts.Alive)} " +
            $"engaged={RaidAttemptRows.Token(f.Counts.Engaged)} healerPow={Num(f.Counts.HealerPow)} healersLow={f.Counts.HealersLow} " +
            $"onTank={OnTank(f.TgtKind)} tgtKind={RaidAttemptRows.Token(f.TgtKind)} medDist={Num(f.Counts.MedDist)} " +
            $"mezzed={f.Counts.Mezzed} stunned={f.Counts.Stunned}");

    // ---- the funnel ----------------------------------------------------------------------------------------------------

    /// <summary>What a RAID_FUNNEL row says. A negative number is unknown ("-").</summary>
    public sealed record FunnelFacts(string Event, string Stage, int Asked, int Recruited, int Parties, int AtHub, int Staged, int AtBoss,
        RaidFunnel.Losses Lost, long HubS, long StageS, long BossS);

    /// <summary><c>route:n,travel:n,died:n,logout:n,released:n,other:n</c>. Pure.</summary>
    public static string FormatLost(RaidFunnel.Losses l) =>
        string.Create(CultureInfo.InvariantCulture,
            $"route:{l.Route},travel:{l.Travel},died:{l.Died},logout:{l.Logout},released:{l.Released},other:{l.Other}");

    private static string Secs(long value) => value < 0 ? "-" : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>RAID_FUNNEL event= stage=start|end asked= recruited= parties= atHub= staged= atBoss= lost=route:n,... hubS= stageS= bossS=</c>.
    /// Pure.
    /// </summary>
    public static string FormatFunnel(FunnelFacts f) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{FunnelMarker} event={RaidAttemptRows.Token(f.Event)} stage={RaidAttemptRows.Token(f.Stage)} asked={Num(f.Asked)} " +
            $"recruited={Num(f.Recruited)} parties={Num(f.Parties)} atHub={Num(f.AtHub)} staged={Num(f.Staged)} atBoss={Num(f.AtBoss)} " +
            $"lost={FormatLost(f.Lost)} hubS={Secs(f.HubS)} stageS={Secs(f.StageS)} bossS={Secs(f.BossS)}");

    /// <summary>
    /// The funnel's three legs in seconds (pure; -1 unknown): hubS created to hub departure; stageS hub departure to the start
    /// (or to <paramref name="now"/> when the raid never started); bossS the start to the first pull.
    /// </summary>
    public static (long HubS, long StageS, long BossS) Legs(long created, long hubDeparted, long started, long firstPull, long now)
    {
        long hub = hubDeparted > 0 && hubDeparted >= created ? (hubDeparted - created) / 1000 : -1;
        long stageEnd = started > 0 ? started : now;
        long stage = hubDeparted > 0 && stageEnd >= hubDeparted ? (stageEnd - hubDeparted) / 1000 : -1;
        long boss = started > 0 && firstPull >= started ? (firstPull - started) / 1000 : -1;
        return (hub, stage, boss);
    }

    // ---- the sampler (never throws) ---------------------------------------------------------------------------------------

    /// <summary>
    /// A RAID_FIGHT_SAMPLE of <paramref name="attempt"/> when one is due (every <see cref="IntervalMilliseconds"/> from its
    /// pull) and both budgets allow it. Reads only the attempt's raiders and its boss. Called from the raid pulse.
    /// </summary>
    public static void SampleIfDue(RaidAttempt attempt, long now, string phase = null)
    {
        if (attempt == null || !LivingRows.Enabled) return;
        try
        {
            if (!attempt.SampleDue(now, IntervalMilliseconds)) return;
            if (!TakeBudget(attempt.SampleBudgetTag)) return;
            LivingRows.Emit(FormatSample(Read(attempt, now, phase)));
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>Reads the attempt's raiders and boss into the sample's facts (the attempt's own buffers).</summary>
    internal static SampleFacts Read(RaidAttempt attempt, long now, string phase)
    {
        GameNPC boss = attempt.Boss;
        int bossPct = -1;
        GameLiving target = null;
        try
        {
            if (boss != null)
            {
                bossPct = boss.IsAlive ? boss.HealthPercent : 0;
                target = boss.IsAlive ? boss.TargetObject as GameLiving : null;
            }
        }
        catch
        {
            bossPct = -1;
        }
        Raider[] buffer = attempt.SampleBuffer(out GameLiving[] members, out int[] dists);
        int n = members.Length;
        for (int i = 0; i < n; i++) buffer[i] = ReadRaider(members[i], boss, attempt.Region);
        Counts counts = Count(buffer, n, dists);
        string tgtKind = "none";
        if (target != null)
        {
            bool raider = attempt.IsMember(target);
            GameLiving owner = raider ? null : LivingRows.PetOwner(target);
            tgtKind = TargetKind(true, raider, raider ? RaidAttemptRows.RoleOf(target) : null, owner != null && attempt.IsMember(owner));
        }
        return new SampleFacts(attempt.Id, (now - attempt.Start) / 1000, bossPct, phase, -1, counts, tgtKind);
    }

    private static Raider ReadRaider(GameLiving living, GameNPC boss, ushort region)
    {
        var r = new Raider { Role = "-", Dist = -1, Mana = -1 };
        if (living == null) return r;
        try
        {
            r.Alive = living.IsAlive;
            r.InRegion = living.CurrentRegionID == region;
            if (!r.Alive || !r.InRegion) return r;
            r.Role = RaidAttemptRows.RoleOf(living);
            r.Dist = RaidAttemptRows.Distance(living, boss);
            r.InCombat = living.InCombat;
            r.Mana = living.MaxMana > 0 ? living.ManaPercent : -1;
            r.Mezzed = living.IsMezzed;
            r.Stunned = living.IsStunned;
        }
        catch
        {
            // Logging only: what was read stays.
        }
        return r;
    }
}

/// <summary>
/// One raid's funnel (#644 part 2): who dropped out on the way to the fight and why, and the most at each stage. Each reason
/// counts distinct raiders (a raider can be in two: route-failed, then released). Called only under the raid lock.
/// </summary>
public sealed class RaidFunnel
{
    /// <summary>The drop-out counts of RAID_FUNNEL's lost= field.</summary>
    public readonly record struct Losses(int Route, int Travel, int Died, int Logout, int Released, int Other);

    private readonly HashSet<long> _route = [];
    private readonly HashSet<long> _travel = [];
    private readonly HashSet<long> _died = [];
    private readonly HashSet<long> _logout = [];
    private readonly HashSet<long> _other = [];
    private readonly HashSet<long> _deadNow = [];

    public int MostParties { get; private set; }
    public int MostAtHub { get; private set; }
    public long StartedTick { get; set; }
    public bool StartWritten { get; set; }

    /// <summary>A gamebot of the raid whose route to the hub failed (REALM_RAID_HUB_ROUTE_FAILED).</summary>
    public void RouteFailed(long bot) => _route.Add(bot);

    /// <summary>A gamebot of the raid whose stable travel was cancelled (REALM_RAID_TRAVEL_CANCEL).</summary>
    public void TravelCancelled(long bot) => _travel.Add(bot);

    /// <summary>A party left the raid: its members that logged off count as logout, the rest as other.</summary>
    public void PartyLeft(IEnumerable<(long Bot, bool LoggedOff)> members)
    {
        foreach (var (bot, loggedOff) in members ?? [])
            (loggedOff ? _logout : _other).Add(bot);
    }

    /// <summary>The pulse's look at the raid's party sizes and, before the hub departure, those at the hub.</summary>
    public void Observe(int parties, int atHub, bool beforeDeparture)
    {
        MostParties = Math.Max(MostParties, parties);
        if (beforeDeparture) MostAtHub = Math.Max(MostAtHub, atHub);
    }

    /// <summary>
    /// The pulse's look at one gamebot: a raider newly seen dead while in no live attempt died outside a pull (on the way in,
    /// at staging or between pulls). Deaths inside a pull are RAID_DEATH's.
    /// </summary>
    public void ObserveLife(long bot, bool dead, bool inLiveAttempt)
    {
        if (!dead)
        {
            _deadNow.Remove(bot);
            return;
        }
        if (_deadNow.Add(bot) && !inLiveAttempt) _died.Add(bot);
    }

    public Losses Lost(int released) => new(_route.Count, _travel.Count, _died.Count, _logout.Count, Math.Max(0, released), _other.Count);
}
