using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;
using DOL.GS.Spells;

namespace DOL.GS;

// ---- Raid mechanics, threat and role totals, #644 parts 3-5 ------------------------------------------------------------
//
// Logging only, like the part 1 and 2 rows: nothing here changes what a raider, a boss or the raid AI does. No game state is
// written, no target, path or aggro is set, no timer is started and no random number is drawn. No player, account or bot
// name is in any row (raiders are counted, or named by role; monster names are written).
//
//   RAID_MECHANIC_FIRE  one per encounter signal of a live attempt's boss (or of a monster in its region) and its aftermath:
//                       EncounterSignals.Raised (the signals the raid AI reacts to) and EncounterSignals.Measured (the
//                       measure-only signals #644 decision d added to the bosses that had none). On by default; at most 20 a
//                       minute.
//   RAID_THREAT         each change of a live attempt's boss's target. Diagnostics switch (decision c); at most 20 a minute
//                       per attempt (one boss).
//   RAID_ROLE_TOTALS    one per role at each attempt's end. Diagnostics switch (decision c); at most 40 a minute.
//
// The diagnostics switch is the server property autonomous_diagnostics / raid_diagnostic_rows (off; player installs never
// turn it on). With it off the hit path is part 1's: one empty check outside raids, one lookup per hit during an attempt.

/// <summary>The diagnostics switch of RAID_THREAT and RAID_ROLE_TOTALS (#644 decision c). Off by default; logs only.</summary>
public static class RaidDiagnosticRows
{
    [ServerProperty("autonomous_diagnostics", "raid_diagnostic_rows",
        "RAID_THREAT (each change of a raid boss's target) and RAID_ROLE_TOTALS (damage, healing, crowd control and power-outs by role at each raid attempt's end) in server.log (issue #644). Diagnostic only, for test and sweep runs; never changes a fight.",
        false)]
    public static bool RAID_DIAGNOSTIC_ROWS = false;

    /// <summary>Tests turn the rows on without the database.</summary>
    public static bool? ForTests;

    public static bool On => ForTests ?? RAID_DIAGNOSTIC_ROWS;
}

/// <summary>
/// The measure-only mechanic signals of the raid bosses that had none (#644 decision d): Legion, Grand Summoner Govannon, the
/// Green Knight, Spindler Broodmother, Queen Kula and King Tuscar. The scripts call these beside what they already do; nothing
/// here changes the spell, its timer, its target or the boss (EncounterSignals.Measure: no tactic, call or lore hears it).
/// </summary>
public static class RaidBossMechanics
{
    /// <summary>
    /// The boss tried a mechanic's spell: when the cast started (<paramref name="cast"/>, the script's own CastSpell result), a
    /// measure-only signal. Its area: the spell's radius, around the boss's target for a ranged spell, else around the boss; a
    /// single-target spell (radius 0) names the target. It lands after the spell's cast time.
    /// </summary>
    public static void Cast(bool cast, GameNPC boss, string mechanic, Spell spell)
    {
        if (!cast || boss == null || spell == null) return;
        try
        {
            GameLiving target = boss.TargetObject as GameLiving;
            bool aroundTarget = spell.Range > 0 && target != null;
            EncounterSignals.Measure(boss, mechanic, aroundTarget || spell.Radius <= 0 ? target : null, spell.Radius, aroundTarget,
                Math.Max(0, spell.CastTime), 0, LivingRows.Now());
        }
        catch
        {
            // A measurement never breaks the script.
        }
    }

    /// <summary>The boss calls a wave of adds or a summon now (no area: the row counts its hits and deaths only).</summary>
    public static void Spawn(GameNPC boss, string mechanic)
    {
        if (boss == null) return;
        try
        {
            EncounterSignals.Measure(boss, mechanic, null, 0, false, 0, 0, LivingRows.Now());
        }
        catch
        {
            // A measurement never breaks the script.
        }
    }
}

/// <summary>One raider's sums for RAID_ROLE_TOTALS (#644 part 5).</summary>
public sealed class RaiderTotals
{
    public long DealtBoss, DealtAdds, Took, Healed, Overheal;
    public int Cc, Interrupts;
    public bool PowerOut;
}

public static partial class RaidAttemptRows
{
    public const string MechanicMarker = "RAID_MECHANIC_FIRE";
    public const string ThreatMarker = "RAID_THREAT";
    public const string RoleTotalsMarker = "RAID_ROLE_TOTALS";
    public const int MechanicPerMinute = 20;
    public const int ThreatPerMinutePerBoss = 20;
    public const int RoleTotalsPerMinute = 40;

    /// <summary>A mechanic's dead and hits are counted until this long after it lands (EPIC_BLAST_LANDED's count-after).</summary>
    public const long MechanicCountAfterMilliseconds = 6_000;

    /// <summary>A spell hit from the boss counts as the mechanic's from this long before it lands.</summary>
    public const long MechanicHitLeadMilliseconds = 500;

    /// <summary>Raiders exposed at the call are looked at this often until it lands (reactMedianMs: when each stepped out).</summary>
    public const long ReactCheckMilliseconds = 100;

    /// <summary>A taunt on the boss this recent explains its next target change (why=taunt).</summary>
    public const long TauntMemoryMilliseconds = 3_000;

    private static readonly object LiveSync = new();
    private static RaidAttempt[] _live = [];

    /// <summary>The live attempts by boss (a taunt on a boss; a mechanic of a boss). Empty outside raids.</summary>
    internal static readonly ConcurrentDictionary<GameNPC, RaidAttempt> ByBoss = new(ReferenceEqualityComparer.Instance);

    /// <summary>The live attempts (a snapshot array: the per-tick pulse iterates it without allocating).</summary>
    public static RaidAttempt[] LiveAttempts => Volatile.Read(ref _live);

    internal static void Register(RaidAttempt attempt)
    {
        if (attempt == null) return;
        lock (LiveSync)
        {
            if (Array.IndexOf(_live, attempt) < 0) Volatile.Write(ref _live, [.. _live, attempt]);
            if (attempt.Boss != null) ByBoss[attempt.Boss] = attempt;
        }
        RaidMechanicRows.EnsureSubscribed();
    }

    internal static void Unregister(RaidAttempt attempt)
    {
        if (attempt == null) return;
        lock (LiveSync)
        {
            Volatile.Write(ref _live, _live.Where(a => !ReferenceEquals(a, attempt)).ToArray());
            if (attempt.Boss != null) ByBoss.TryRemove(new KeyValuePair<GameNPC, RaidAttempt>(attempt.Boss, attempt));
        }
    }

    private static void ResetLiveForTests()
    {
        lock (LiveSync)
        {
            Volatile.Write(ref _live, []);
            ByBoss.Clear();
        }
    }

    /// <summary>
    /// Every game tick from the raid pulse (under the raid lock): the live attempts' mechanic fires (their land and their
    /// count-after) and, behind the diagnostics switch, their boss's target changes. Nothing to do outside raids.
    /// </summary>
    public static void PulseLive(long now)
    {
        RaidAttempt[] live = LiveAttempts;
        if (live.Length == 0) return;
        bool diagnostics = RaidDiagnosticRows.On;
        foreach (RaidAttempt attempt in live)
        {
            try
            {
                attempt.PulseFires(now);
                if (diagnostics) attempt.PulseThreat(now);
            }
            catch
            {
                // Logging only.
            }
        }
    }

    /// <summary>The live attempt a signal belongs to: its boss's, else the one in the signaller's region (an add's), else null.</summary>
    internal static RaidAttempt AttemptOf(GameNPC signaller)
    {
        if (signaller == null) return null;
        if (ByBoss.TryGetValue(signaller, out RaidAttempt attempt)) return attempt;
        ushort region = Region(signaller);
        foreach (RaidAttempt a in LiveAttempts)
            if (a.Region == region) return a;
        return null;
    }

    // ---- the diagnostics hooks (behind the switch; never throw) ---------------------------------------------------------

    /// <summary>The raider behind a hit or a cast: itself, or a pet's owner when that owner is in a live attempt.</summary>
    private static RaidAttempt RaiderAttempt(GameLiving actor, out GameLiving raider)
    {
        raider = null;
        if (actor == null) return null;
        if (ByRaider.TryGetValue(actor, out RaidAttempt attempt)) { raider = actor; return attempt; }
        if (actor is GameNPC and not GameBot && LivingRows.PetOwner(actor) is { } owner && ByRaider.TryGetValue(owner, out attempt))
        {
            raider = owner;
            return attempt;
        }
        return null;
    }

    /// <summary>A hit on a monster (diagnostics): the raider's damage dealt, and a taunt on a boss.</summary>
    private static void NoteMonsterHit(GameLiving victim, AttackData ad)
    {
        RaidAttempt attempt = RaiderAttempt(ad.Attacker, out GameLiving raider);
        if (attempt != null) attempt.NoteDealt(raider, victim, ad.Damage + ad.CriticalDamage);
        if (ad.SpellHandler is TauntSpellHandler && victim is GameNPC npc && ByBoss.TryGetValue(npc, out RaidAttempt bossAttempt))
            bossAttempt.NoteTaunt(raider ?? ad.Attacker, LivingRows.Now());
    }

    /// <summary>A heal (HealSpellHandler.HealTarget): healed and overheal of a raider healer (diagnostics).</summary>
    public static void NoteHeal(GameLiving caster, int amount, int effective)
    {
        if (ByRaider.IsEmpty || caster == null || amount <= 0 || !RaidDiagnosticRows.On) return;
        try
        {
            RaiderAttempt(caster, out GameLiving raider)?.NoteHealDone(raider, Math.Max(0, effective), Math.Max(0, amount - Math.Max(0, effective)));
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>A mez or stun applied (AbstractCCSpellHandler, after its immunity checks) by a raider on a monster (diagnostics).</summary>
    public static void NoteCc(GameLiving caster, GameLiving target)
    {
        if (ByRaider.IsEmpty || caster == null || target == null || !RaidDiagnosticRows.On) return;
        try
        {
            if (ByRaider.ContainsKey(target)) return;
            RaiderAttempt(caster, out GameLiving raider)?.NoteCcDone(raider);
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>A monster's cast lost to an interrupt (SpellHandler's half-cast check): the raider who interrupted it (diagnostics).</summary>
    public static void NoteInterrupt(GameLiving interrupter, GameLiving caster)
    {
        if (ByRaider.IsEmpty || interrupter == null || caster == null || !RaidDiagnosticRows.On) return;
        try
        {
            if (ByRaider.ContainsKey(caster)) return;
            RaiderAttempt(interrupter, out GameLiving raider)?.NoteInterruptDone(raider);
        }
        catch
        {
            // Logging only.
        }
    }

    // ---- pure helpers and formatters ------------------------------------------------------------------------------------

    /// <summary>
    /// A mechanic's died and avoided (pure). died: raiders within its area at the landing or hit by it, dead at the count.
    /// avoided: raiders exposed at the call, alive, neither within at the landing nor hit.
    /// </summary>
    public static (int Died, int Avoided) MechanicOutcome<T>(IReadOnlyCollection<T> exposed, IReadOnlyCollection<T> within,
        IReadOnlyCollection<T> hit, Func<T, bool> dead) where T : class
    {
        var struck = new HashSet<T>(within ?? (IReadOnlyCollection<T>)[], (IEqualityComparer<T>)(object)ReferenceEqualityComparer.Instance);
        foreach (T h in hit ?? []) struck.Add(h);
        int died = struck.Count(r => dead(r));
        int avoided = (exposed ?? []).Count(r => !dead(r) && !struck.Contains(r));
        return (died, avoided);
    }

    /// <summary>The median, -1 for none (pure).</summary>
    public static long MedianOf(IReadOnlyList<long> values)
    {
        if (values == null || values.Count == 0) return -1;
        long[] sorted = values.OrderBy(v => v).ToArray();
        return sorted[(sorted.Length - 1) / 2];
    }

    /// <summary>What a RAID_MECHANIC_FIRE row says. A negative count is unknown ("-").</summary>
    public sealed record MechanicFacts(string Attempt, string Boss, string Mechanic, long T, int Radius, int ExposedAtCall, int Within,
        int Hit, int Died, int Avoided, bool Called, long LeadMs, long ReactMedianMs, long Damage);

    private static string Num(long value) => value < 0 ? "-" : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>RAID_MECHANIC_FIRE attempt= boss="" mechanic= t= radius= exposedAtCall= within= hit= died= avoided= called= leadMs=
    /// reactMedianMs= damage=</c>. Pure.
    /// </summary>
    public static string FormatMechanic(MechanicFacts f) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{MechanicMarker} attempt={Token(f.Attempt)} boss=\"{Quoted(f.Boss)}\" mechanic={Token(f.Mechanic)} t={Math.Max(0, f.T)} " +
            $"radius={Math.Max(0, f.Radius)} exposedAtCall={Num(f.ExposedAtCall)} within={Num(f.Within)} hit={Num(f.Hit)} died={Num(f.Died)} " +
            $"avoided={Num(f.Avoided)} called={(f.Called ? 1 : 0)} leadMs={Num(f.LeadMs)} reactMedianMs={Num(f.ReactMedianMs)} damage={Num(f.Damage)}");

    /// <summary>
    /// Why the boss changed its target (pure): death (its last target died), taunt (the new target taunted it in the last
    /// <see cref="TauntMemoryMilliseconds"/>), mechanic (one of its signals was in force), else threat.
    /// </summary>
    public static string ThreatWhy(bool previousDied, bool tauntedByNew, bool mechanicActive) =>
        previousDied ? "death" : tauntedByNew ? "taunt" : mechanicActive ? "mechanic" : "threat";

    /// <summary>What a RAID_THREAT row says.</summary>
    public sealed record ThreatFacts(string Attempt, string Boss, long T, string From, string To, string ToKind, string Why, long HeldMs);

    /// <summary><c>RAID_THREAT attempt= boss="" t= from= to= toKind= why= heldMs=</c>. Pure.</summary>
    public static string FormatThreat(ThreatFacts f) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{ThreatMarker} attempt={Token(f.Attempt)} boss=\"{Quoted(f.Boss)}\" t={Math.Max(0, f.T)} from={Token(f.From)} to={Token(f.To)} " +
            $"toKind={Token(f.ToKind)} why={Token(f.Why)} heldMs={Math.Max(0, f.HeldMs)}");

    /// <summary>One raider as the role totals see it.</summary>
    public readonly record struct RaiderLine(string Role, string Class, RaiderTotals Totals, int Deaths);

    /// <summary>What a RAID_ROLE_TOTALS row says.</summary>
    public sealed record RoleTotalsFacts(string Attempt, string Role, int Raiders, long DealtBoss, long DealtAdds, long Took, long Healed,
        long Overheal, int Deaths, int Cc, int Interrupts, int PowerOuts, string Classes);

    public static readonly string[] Roles = ["tank", "healer", "buffer", "attacker"];

    /// <summary>
    /// The four roles' totals from the attempt's raiders (pure). classes: per class of the role, most raiders first then by
    /// name, <c>Class:raiders/dealt/healed</c> (dealt to the boss and adds together); "-" for none.
    /// </summary>
    public static RoleTotalsFacts[] RoleTotals(string attempt, IEnumerable<RaiderLine> raiders)
    {
        RaiderLine[] all = (raiders ?? []).ToArray();
        var rows = new List<RoleTotalsFacts>(4);
        foreach (string role in Roles)
        {
            RaiderLine[] mine = all.Where(r => r.Role == role).ToArray();
            static long Dealt(RaiderTotals t) => t == null ? 0 : t.DealtBoss + t.DealtAdds;
            string classes = mine.Length == 0 ? "-" : string.Join(",", mine.GroupBy(r => Token(r.Class), StringComparer.Ordinal)
                .Select(g => (Name: g.Key, N: g.Count(), Dealt: g.Sum(r => Dealt(r.Totals)), Healed: g.Sum(r => r.Totals?.Healed ?? 0)))
                .OrderByDescending(c => c.N).ThenBy(c => c.Name, StringComparer.Ordinal)
                .Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Name}:{c.N}/{c.Dealt}/{c.Healed}")));
            rows.Add(new RoleTotalsFacts(attempt, role, mine.Length, mine.Sum(r => r.Totals?.DealtBoss ?? 0),
                mine.Sum(r => r.Totals?.DealtAdds ?? 0), mine.Sum(r => r.Totals?.Took ?? 0), mine.Sum(r => r.Totals?.Healed ?? 0),
                mine.Sum(r => r.Totals?.Overheal ?? 0), mine.Sum(r => r.Deaths), mine.Sum(r => r.Totals?.Cc ?? 0),
                mine.Sum(r => r.Totals?.Interrupts ?? 0), mine.Count(r => r.Totals?.PowerOut == true), classes));
        }
        return rows.ToArray();
    }

    /// <summary>
    /// <c>RAID_ROLE_TOTALS attempt= role= raiders= dealtBoss= dealtAdds= took= healed= overheal= deaths= cc= interrupts= powerOuts=
    /// classes=</c>. Pure.
    /// </summary>
    public static string FormatRoleTotals(RoleTotalsFacts f) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{RoleTotalsMarker} attempt={Token(f.Attempt)} role={Token(f.Role)} raiders={f.Raiders} dealtBoss={f.DealtBoss} " +
            $"dealtAdds={f.DealtAdds} took={f.Took} healed={f.Healed} overheal={f.Overheal} deaths={f.Deaths} cc={f.Cc} " +
            $"interrupts={f.Interrupts} powerOuts={f.PowerOuts} classes={Token(f.Classes)}");
}

/// <summary>RAID_MECHANIC_FIRE's ears: the signals the raid AI reacts to and the measure-only ones (#644 part 3).</summary>
public static class RaidMechanicRows
{
    private static int _subscribed;

    /// <summary>Subscribes once, at the first attempt (no cost before any raid).</summary>
    public static void EnsureSubscribed()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 1) return;
        EncounterSignals.Raised += signal => OnSignal(signal, true);
        EncounterSignals.Measured += signal => OnSignal(signal, false);
    }

    /// <summary>A signal: a mechanic fire of the live attempt it belongs to (none outside raids).</summary>
    public static void OnSignal(EncounterSignal signal, bool called)
    {
        if (signal?.Boss == null || !LivingRows.Enabled) return;
        try
        {
            RaidAttemptRows.AttemptOf(signal.Boss)?.AddFire(signal, called);
        }
        catch
        {
            // Logging only.
        }
    }
}

/// <summary>The mechanic fires, threat and role totals of one live attempt (#644 parts 3-5). Every member takes the attempt's lock.</summary>
public sealed partial class RaidAttempt
{
    /// <summary>Every death of each raider in the attempt (RAID_ROLE_TOTALS' deaths). Under the lock.</summary>
    private Dictionary<GameLiving, int> DeathCounts { get; } = new(ReferenceEqualityComparer.Instance);

    private Dictionary<GameLiving, RaiderTotals> _totals;
    private List<Fire> _fires;
    private GameLiving _threatTarget;
    private bool _threatSeen;
    private long _threatSince;
    private GameLiving _tauntBy;
    private long _tauntAt;
    private string _threatBudgetTag;

    private sealed class Fire
    {
        public EncounterSignal Signal;
        public bool Called;
        public bool Area;
        public HashSet<GameLiving> Exposed;
        public HashSet<GameLiving> Within;
        public bool Snapped;
        public readonly HashSet<GameLiving> Hit = new(ReferenceEqualityComparer.Instance);
        public readonly HashSet<GameLiving> Out = new(ReferenceEqualityComparer.Instance);
        public readonly List<long> ReactMs = new();
        public long Damage;
        public long NextReactCheck;
        public long ReportAt;
    }

    private RaiderTotals TotalsLocked(GameLiving raider)
    {
        _totals ??= new Dictionary<GameLiving, RaiderTotals>(ReferenceEqualityComparer.Instance);
        if (!_totals.TryGetValue(raider, out RaiderTotals t)) _totals[raider] = t = new RaiderTotals();
        return t;
    }

    /// <summary>A copy of one raider's sums so far (diagnostics), or null when none were kept.</summary>
    public RaiderTotals TotalsFor(GameLiving raider)
    {
        lock (_sync)
        {
            if (raider == null || _totals == null || !_totals.TryGetValue(raider, out RaiderTotals t)) return null;
            return new RaiderTotals { DealtBoss = t.DealtBoss, DealtAdds = t.DealtAdds, Took = t.Took, Healed = t.Healed, Overheal = t.Overheal,
                Cc = t.Cc, Interrupts = t.Interrupts, PowerOut = t.PowerOut };
        }
    }

    // ---- the hit, heal, cc and interrupt notes ---------------------------------------------------------------------------

    /// <summary>A hit on a raider of this attempt (under the lock): a mechanic's hit, and its damage taken (diagnostics).</summary>
    private void NoteTakenLocked(GameLiving victim, AttackData ad, long now)
    {
        int amount = Math.Max(0, ad.Damage + ad.CriticalDamage);
        if (_fires is { Count: > 0 } && ad.AttackType == AttackData.eAttackType.Spell && ad.Attacker is GameNPC caster)
            foreach (Fire f in _fires)
                if (ReferenceEquals(caster, f.Signal.Boss) && now >= f.Signal.Lands - RaidAttemptRows.MechanicHitLeadMilliseconds &&
                    now <= f.Signal.Until)
                {
                    f.Hit.Add(victim);
                    f.Damage += amount;
                }
        if (RaidDiagnosticRows.On) TotalsLocked(victim).Took += amount;
    }

    internal void NoteDealt(GameLiving raider, GameLiving victim, int amount)
    {
        if (raider == null || amount <= 0) return;
        lock (_sync)
        {
            if (_closed || !_index.ContainsKey(raider)) return;
            RaiderTotals t = TotalsLocked(raider);
            if (ReferenceEquals(victim, Boss)) t.DealtBoss += amount;
            else t.DealtAdds += amount;
        }
    }

    internal void NoteHealDone(GameLiving raider, int effective, int overheal)
    {
        if (raider == null) return;
        lock (_sync)
        {
            if (_closed || !_index.ContainsKey(raider)) return;
            RaiderTotals t = TotalsLocked(raider);
            t.Healed += effective;
            t.Overheal += overheal;
        }
    }

    internal void NoteCcDone(GameLiving raider)
    {
        if (raider == null) return;
        lock (_sync)
            if (!_closed && _index.ContainsKey(raider)) TotalsLocked(raider).Cc++;
    }

    internal void NoteInterruptDone(GameLiving raider)
    {
        if (raider == null) return;
        lock (_sync)
            if (!_closed && _index.ContainsKey(raider)) TotalsLocked(raider).Interrupts++;
    }

    /// <summary>A raider seen at 0% power at a fight sample (diagnostics).</summary>
    public void NotePowerOut(GameLiving raider)
    {
        if (raider == null) return;
        lock (_sync)
            if (!_closed && _index.ContainsKey(raider)) TotalsLocked(raider).PowerOut = true;
    }

    internal void NoteTaunt(GameLiving taunter, long now)
    {
        lock (_sync)
        {
            _tauntBy = taunter;
            _tauntAt = now;
        }
    }

    // ---- RAID_ROLE_TOTALS ------------------------------------------------------------------------------------------------

    /// <summary>The attempt's four RAID_ROLE_TOTALS rows (diagnostics; after it ended).</summary>
    public void WriteRoleTotals()
    {
        RaidAttemptRows.RaiderLine[] lines;
        lock (_sync)
            lines = _index.Keys.Select(r => new RaidAttemptRows.RaiderLine(RaidAttemptRows.RoleOf(r), RaidAttemptRows.ClassName(r),
                _totals != null && _totals.TryGetValue(r, out RaiderTotals t) ? t : null, DeathCounts.GetValueOrDefault(r))).ToArray();
        foreach (RaidAttemptRows.RoleTotalsFacts f in RaidAttemptRows.RoleTotals(Id, lines))
            if (LivingRows.Take(RaidAttemptRows.RoleTotalsMarker, RaidAttemptRows.RoleTotalsPerMinute))
                LivingRows.Emit(RaidAttemptRows.FormatRoleTotals(f));
    }

    // ---- RAID_MECHANIC_FIRE ----------------------------------------------------------------------------------------------

    private static bool Inside(GameLiving raider, System.Numerics.Vector3 center, int radius)
    {
        try
        {
            float dx = raider.X - center.X, dy = raider.Y - center.Y, dz = raider.Z - center.Z;
            return dx * dx + dy * dy + dz * dz <= (float)radius * radius;
        }
        catch
        {
            return false;
        }
    }

    private HashSet<GameLiving> InArea(EncounterSignal s, GameLiving[] members)
    {
        var set = new HashSet<GameLiving>(ReferenceEqualityComparer.Instance);
        if (s.Radius <= 0)
        {
            if (s.Target != null && Array.IndexOf(members, s.Target) >= 0 && RaidAttemptRows.Alive(s.Target)) set.Add(s.Target);
            return set;
        }
        System.Numerics.Vector3 center = s.Center;
        foreach (GameLiving m in members)
            if (RaidAttemptRows.Alive(m) && RaidAttemptRows.Region(m) == Region && Inside(m, center, s.Radius)) set.Add(m);
        return set;
    }

    /// <summary>A signal of this attempt's fight: who was exposed when it was called.</summary>
    internal void AddFire(EncounterSignal signal, bool called)
    {
        GameLiving[] members;
        lock (_sync)
        {
            if (_closed) return;
            members = [.. _index.Keys];
        }
        bool area = signal.Radius > 0 || signal.Target != null;
        var fire = new Fire
        {
            Signal = signal,
            Called = called,
            Area = area,
            Exposed = area ? InArea(signal, members) : null,
            ReportAt = Math.Max(signal.Until, signal.Lands + RaidAttemptRows.MechanicCountAfterMilliseconds),
        };
        lock (_sync)
        {
            if (_closed) return;
            (_fires ??= new List<Fire>(2)).Add(fire);
        }
    }

    /// <summary>Each tick: the exposed raiders stepping out before the landing, the landing's snapshot, and the row at the count.</summary>
    internal void PulseFires(long now)
    {
        Fire[] fires;
        GameLiving[] members;
        lock (_sync)
        {
            if (_fires is not { Count: > 0 }) return;
            fires = _fires.ToArray();
            members = _members ??= [.. _index.Keys];
        }
        List<Fire> done = null;
        foreach (Fire f in fires)
        {
            EncounterSignal s = f.Signal;
            if (f.Area && !f.Snapped && s.Radius > 0 && now < s.Lands && now >= f.NextReactCheck)
            {
                f.NextReactCheck = now + RaidAttemptRows.ReactCheckMilliseconds;
                System.Numerics.Vector3 center = s.Center;
                foreach (GameLiving r in f.Exposed)
                    if (!f.Out.Contains(r) && RaidAttemptRows.Alive(r) && !Inside(r, center, s.Radius))
                    {
                        f.Out.Add(r);
                        f.ReactMs.Add(now - s.At);
                    }
            }
            if (f.Area && !f.Snapped && now >= s.Lands)
            {
                f.Snapped = true;
                f.Within = InArea(s, members);
            }
            if (now >= f.ReportAt) (done ??= new()).Add(f);
        }
        if (done == null) return;
        lock (_sync) foreach (Fire f in done) _fires.Remove(f);
        foreach (Fire f in done) Report(f);
    }

    private void Report(Fire f)
    {
        EncounterSignal s = f.Signal;
        HashSet<GameLiving> hit;
        long damage;
        lock (_sync)
        {
            hit = [.. f.Hit];
            damage = f.Damage;
        }
        int within = -1, exposed = -1, avoided = -1, died;
        if (f.Area)
        {
            exposed = f.Exposed.Count;
            within = f.Within?.Count ?? -1;
            (died, avoided) = RaidAttemptRows.MechanicOutcome(f.Exposed, (IReadOnlyCollection<GameLiving>)f.Within ?? [], hit,
                r => !RaidAttemptRows.Alive(r));
            if (f.Within == null) avoided = -1;
        }
        else
            died = hit.Count(r => !RaidAttemptRows.Alive(r));
        string bossName;
        try { bossName = s.Boss.Name; } catch { bossName = "-"; }
        if (!LivingRows.Take(RaidAttemptRows.MechanicMarker, RaidAttemptRows.MechanicPerMinute)) return;
        LivingRows.Emit(RaidAttemptRows.FormatMechanic(new RaidAttemptRows.MechanicFacts(Id, bossName, s.Mechanic, (s.At - Start) / 1000,
            s.Radius, exposed, within, hit.Count, died, avoided, f.Called, s.Lands - s.At,
            f.Area && s.Radius > 0 ? RaidAttemptRows.MedianOf(f.ReactMs) : -1, damage)));
    }

    // ---- RAID_THREAT -----------------------------------------------------------------------------------------------------

    private string KindOfTarget(GameLiving target, out string kind)
    {
        kind = "-";
        if (target == null) return "none";
        bool raider = IsMember(target);
        GameLiving owner = raider ? null : LivingRows.PetOwner(target);
        kind = LivingRows.Kind(target);
        if (owner != null && IsMember(owner)) kind = "pet";
        return RaidFightSampleRows.TargetKind(true, raider, raider ? RaidAttemptRows.RoleOf(target) : null, owner != null && IsMember(owner));
    }

    /// <summary>Each tick (diagnostics): a RAID_THREAT row when the boss's target changed.</summary>
    internal void PulseThreat(long now)
    {
        GameNPC boss = Boss;
        if (boss == null) return;
        GameLiving target;
        try { target = boss.IsAlive ? boss.TargetObject as GameLiving : null; } catch { return; }
        GameLiving previous;
        long since;
        bool taunted;
        lock (_sync)
        {
            if (_closed) return;
            if (_threatSeen && ReferenceEquals(target, _threatTarget)) return;
            previous = _threatTarget;
            since = _threatSeen ? _threatSince : Start;
            _threatSeen = true;
            _threatTarget = target;
            _threatSince = now;
            taunted = target != null && ReferenceEquals(_tauntBy, target) && now - _tauntAt <= RaidAttemptRows.TauntMemoryMilliseconds;
        }
        if (previous == null && target == null) return;
        string from = KindOfTarget(previous, out _);
        string to = KindOfTarget(target, out string toKind);
        bool previousDied = previous != null && !RaidAttemptRows.Alive(previous);
        bool mechanic;
        try { mechanic = EncounterSignals.Active(boss, now).Length > 0; } catch { mechanic = false; }
        _threatBudgetTag ??= RaidAttemptRows.ThreatMarker + "." + RaidAttemptRows.Token(Id);
        if (!LivingRows.Take(_threatBudgetTag, RaidAttemptRows.ThreatPerMinutePerBoss)) return;
        LivingRows.Emit(RaidAttemptRows.FormatThreat(new RaidAttemptRows.ThreatFacts(Id, BossName, (now - Start) / 1000, from, to, toKind,
            RaidAttemptRows.ThreatWhy(previousDied, taunted, mechanic), now - since)));
    }

    /// <summary>At close: the open mechanic fires are written with what is known, and the threat budget goes.</summary>
    private void CloseExtras(long now)
    {
        Fire[] open;
        lock (_sync)
        {
            open = _fires?.ToArray() ?? [];
            _fires?.Clear();
        }
        foreach (Fire f in open)
        {
            try { Report(f); } catch { /* logging only */ }
        }
        if (_threatBudgetTag != null) RvrRecordBudget.Forget(_threatBudgetTag);
    }
}
