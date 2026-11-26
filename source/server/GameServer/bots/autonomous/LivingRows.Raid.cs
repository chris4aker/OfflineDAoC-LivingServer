using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS;

// ---- Raid attempt telemetry, part 1 (issue #644) ----------------------------------------------------------------------
//
// Logging only, like the set A rows (LivingRows.cs): nothing here changes what a raider, a boss or the raid AI does. No game
// state is written, no target, path or aggro is set, no timer is started and no random number is drawn. Every row is one
// stable name with key=value fields, never a player, account or bot name (monster names are written: they are no people).
//
//   RAID_ATTEMPT       each pull of a dungeon encounter (RealmRaidDungeonRoute.BeginAttempt) or a dragon / open-world boss
//                      (RealmRaidDragonFight's attempts). At most 10 a minute: a few an hour per live raid.
//   RAID_ATTEMPT_END   each end of such an attempt, beside the existing REALM_EXPEDITION_* / REALM_RAID_DRAGON_* rows.
//                      At most 10 a minute.
//   RAID_DEATH         each death of a raider (player, companion or gamebot) in a live attempt. No cap during an attempt
//                      (owner, 2026-11-25, #644 decision a): a 160-raider wipe writes 160 rows.
//
// Every row carries attempt=<event>.<n> (n counts the event's attempts since the server started), so one grep of an id gives
// a whole attempt. The four existing attempt-end rows carry the same attempt= at their end (#644 decision b).

/// <summary>The pure formatters and helpers of the raid attempt rows (#644 part 1), and the hooks the server calls.</summary>
public static partial class RaidAttemptRows
{
    public const string AttemptMarker = "RAID_ATTEMPT";
    public const string EndMarker = "RAID_ATTEMPT_END";
    public const string DeathMarker = "RAID_DEATH";
    public const int AttemptPerMinute = 10;
    public const int EndPerMinute = 10;

    /// <summary>The distance a healer counts as "in range" of a dying raider (healersIn=): the usual heal range.</summary>
    public const int HealRange = 2_000;

    /// <summary>A hit counts as the death's source when it landed this recently before the death.</summary>
    public const long LastHitMilliseconds = 5_000;

    /// <summary>Raiders in a live attempt and the attempt they are in. Read on every hit and death: empty outside raids.</summary>
    internal static readonly ConcurrentDictionary<GameLiving, RaidAttempt> ByRaider = new(ReferenceEqualityComparer.Instance);

    private static readonly Dictionary<string, int> Sequence = new(StringComparer.Ordinal);

    /// <summary>Whether any attempt is live (one check on the hit path).</summary>
    public static bool AnyLive => !ByRaider.IsEmpty;

    // ---- the attempt id ----------------------------------------------------------------------------------------------

    /// <summary>The next attempt id of <paramref name="eventId"/>: <c>&lt;event&gt;.&lt;n&gt;</c>, n from 1. Thread-safe.</summary>
    public static string NextId(string eventId)
    {
        string key = Token(eventId);
        lock (Sequence)
        {
            int n = Sequence[key] = Sequence.GetValueOrDefault(key) + 1;
            return string.Create(CultureInfo.InvariantCulture, $"{key}.{n}");
        }
    }

    /// <summary>Forgets every id sequence and live attempt (tests).</summary>
    public static void ResetForTests()
    {
        lock (Sequence) Sequence.Clear();
        ByRaider.Clear();
        ResetLiveForTests();
    }

    // ---- pure helpers ------------------------------------------------------------------------------------------------

    /// <summary>A value as one key=value token: no spaces, no quotes; "-" for none. Pure.</summary>
    public static string Token(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        return string.Concat(value.Trim().Select(c => char.IsWhiteSpace(c) ? '_' : c == '"' ? '\'' : c));
    }

    /// <summary>A monster's or boss's name for a quoted field. Pure.</summary>
    public static string Quoted(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Replace('"', '\'');

    /// <summary>
    /// A raider's role in the rows (pure): tank (BotPartyRoles.For gives Tank, the Paladin included), healer (a healing class,
    /// or the Bard), buffer (the Minstrel and the Skald: the speed, power and song support of their realms), attacker (every
    /// other class). "-" for no class.
    /// </summary>
    public static string Role(eCharacterClass characterClass)
    {
        if ((int)characterClass <= 0) return "-";
        if (BotPartyRoles.For(characterClass) == BotPartyRole.Tank) return "tank";
        if (BotPartyRoles.IsHealingClass(characterClass) || characterClass == eCharacterClass.Bard) return "healer";
        if (characterClass is eCharacterClass.Minstrel or eCharacterClass.Skald) return "buffer";
        return "attacker";
    }

    /// <summary>Role counts as <c>T:H:B:A</c> (tank, healer, buffer, attacker; a raider with no class is not counted). Pure.</summary>
    public static string RoleCounts(IEnumerable<string> roles)
    {
        int t = 0, h = 0, b = 0, a = 0;
        foreach (string role in roles ?? [])
            switch (role)
            {
                case "tank": t++; break;
                case "healer": h++; break;
                case "buffer": b++; break;
                case "attacker": a++; break;
            }
        return string.Create(CultureInfo.InvariantCulture, $"{t}:{h}:{b}:{a}");
    }

    /// <summary>Class counts, most first then by name: <c>Cleric:12,Armsman:8</c>; "-" for none. Pure.</summary>
    public static string ClassCounts(IEnumerable<string> classNames)
    {
        var counts = (classNames ?? []).Where(n => !string.IsNullOrWhiteSpace(n) && n != "-").GroupBy(Token, StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Count: g.Count())).OrderByDescending(p => p.Count).ThenBy(p => p.Name, StringComparer.Ordinal).ToArray();
        return counts.Length == 0 ? "-" : string.Join(",", counts.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name}:{p.Count}")));
    }

    /// <summary>The size as a percentage of the period size, rounded; -1 when there is no period size. Pure.</summary>
    public static int SizePercent(int size, int period) => period <= 0 ? -1 : (int)Math.Round(size * 100.0 / period, MidpointRounding.AwayFromZero);

    /// <summary>The build: the first seven characters of the commit, or "local". Pure.</summary>
    public static string Build(string commit) =>
        string.IsNullOrWhiteSpace(commit) ? "local" : commit == "local" || commit.Length <= 7 ? Token(commit) : Token(commit[..7]);

    /// <summary>
    /// The outcome code of a dungeon attempt, from the decision RealmRaidDungeonRoute.EndAttempt already made (pure): wipe (a
    /// counted wipe), reset (a free reset; or no wipe, the encounter reset to full), skirmish (a lost skirmish), killed (no
    /// wipe: the encounter died and came back), or broke-off (no wipe: the raid left combat or never entered it, or the
    /// encounter took no damage for five minutes).
    /// </summary>
    public static string DungeonOutcome(bool wipe, bool freeReset, bool skirmish, string how)
    {
        if (wipe) return "wipe";
        if (freeReset) return "reset";
        if (skirmish) return "skirmish";
        if (how != null && how.StartsWith("killed", StringComparison.Ordinal)) return "killed";
        if (how == "reset to full") return "reset";
        return "broke-off";
    }

    /// <summary>The outcome code of a dragon or open-world attempt (RealmRaidDragonFight's verdict). Pure.</summary>
    public static string DragonOutcome(bool wipe) => wipe ? "wipe" : "broke-off";

    /// <summary>
    /// The outcome code of an attempt still open when its raid ended (pure): killed when the boss is dead; else from the
    /// raid's end reason (AutonomousRealmRaid.End): route-blocked, stalled, timeout (the four-hour window), or stopped (the
    /// player stopped it, the encounter went away, anything else).
    /// </summary>
    public static string RaidEndOutcome(string reason, bool bossDead)
    {
        if (bossDead) return "killed";
        if (reason == null) return "stopped";
        if (reason.StartsWith("Route blocked", StringComparison.Ordinal)) return "route-blocked";
        if (reason.StartsWith("Stalled", StringComparison.Ordinal)) return "stalled";
        if (reason.Contains("four-hour", StringComparison.Ordinal)) return "timeout";
        return "stopped";
    }

    /// <summary>
    /// The died/left split of an attempt's raiders (pure): <c>died</c> counts the raiders in it who died at least once during it;
    /// <c>left</c> the raiders counted fallen (dead, out of the region, walking back after a release) who never died in it.
    /// RealmRaidDungeonRoute's <c>fallen</c> mixes the two (FallenNow).
    /// </summary>
    public static (int Died, int Left) SplitFallen<T>(IEnumerable<T> engaged, Func<T, bool> fallen, Func<T, bool> died)
    {
        int d = 0, l = 0;
        foreach (T raider in engaged ?? [])
        {
            if (died != null && died(raider)) d++;
            else if (fallen != null && fallen(raider)) l++;
        }
        return (d, l);
    }

    /// <summary>The last hit on a raider, as the death row tells its source.</summary>
    public readonly record struct LastHit(long Tick, bool Fall, AttackData.eAttackType AttackType, int SpellId, eSpellType SpellType, int Radius);

    /// <summary>
    /// The death's source from the last hit (pure): fall (falling damage), dot (a damage-over-time spell), aoe (a spell with a
    /// radius), spell, melee, or other (a ranged shot, another kind of hit, or no hit in the last <see cref="LastHitMilliseconds"/>).
    /// </summary>
    public static string Source(LastHit? hit, long now)
    {
        if (hit is not { } h || now - h.Tick > LastHitMilliseconds) return "other";
        if (h.Fall) return "fall";
        if (h.AttackType == AttackData.eAttackType.Spell)
            return h.SpellType == eSpellType.DamageOverTime ? "dot" : h.Radius > 0 ? "aoe" : "spell";
        return h.AttackType is AttackData.eAttackType.MeleeOneHand or AttackData.eAttackType.MeleeDualWield or AttackData.eAttackType.MeleeTwoHand
            ? "melee" : "other";
    }

    /// <summary>
    /// The killer's kind (pure): none (no killer, no fall), env (a fall, or the raider itself), boss (the attempt's boss), pet
    /// (a monster's or a raider's pet), raider (a player or a bot), add (any other monster).
    /// </summary>
    public static string KillerKind(object killer, object victim, object boss, bool fell, bool pet)
    {
        if (killer == null) return fell ? "env" : "none";
        if (ReferenceEquals(killer, victim)) return "env";
        if (boss != null && ReferenceEquals(killer, boss)) return "boss";
        if (pet) return "pet";
        if (killer is GamePlayer or GameBot) return "raider";
        return "add";
    }

    private static string Num(int value) => value < 0 ? "-" : value.ToString(CultureInfo.InvariantCulture);

    // ---- the rows ------------------------------------------------------------------------------------------------------

    /// <summary>What a RAID_ATTEMPT row says. <c>Prev</c> null: no failed attempt before it (no recovery fields).</summary>
    public sealed record AttemptFacts(string Attempt, string Event, string Kind, ushort Region, string Boss, int N, int Size, int Period,
        int Sf, string Roles, string Classes, int Players, string Build, string Prev = null, long WipeToPullS = -1, long GatherS = -1);

    /// <summary>
    /// <c>RAID_ATTEMPT attempt= event= kind= region= boss="" n= size= period= sizePct= sf= roles=T:H:B:A classes= players= build=
    /// [prev= wipeToPullS=] [gatherS=]</c>. Pure.
    /// </summary>
    public static string FormatAttempt(AttemptFacts f) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{AttemptMarker} attempt={Token(f.Attempt)} event={Token(f.Event)} kind={Token(f.Kind)} region={f.Region} boss=\"{Quoted(f.Boss)}\" " +
            $"n={f.N} size={f.Size} period={Num(f.Period <= 0 ? -1 : f.Period)} sizePct={Num(SizePercent(f.Size, f.Period))} sf={f.Sf} " +
            $"roles={Token(f.Roles)} classes={Token(f.Classes)} players={f.Players} build={Token(f.Build)}") +
        (f.Prev != null ? string.Create(CultureInfo.InvariantCulture, $" prev={Token(f.Prev)} wipeToPullS={Math.Max(0, f.WipeToPullS)}") : "") +
        (f.GatherS >= 0 ? string.Create(CultureInfo.InvariantCulture, $" gatherS={f.GatherS}") : "");

    /// <summary>What a RAID_ATTEMPT_END row says. A negative count is unknown ("-").</summary>
    public sealed record EndFacts(string Attempt, string Outcome, long Secs, int Engaged, int Fallen, int Died, int Left, int Deaths,
        int Health, int Lowest);

    /// <summary><c>RAID_ATTEMPT_END attempt= outcome= secs= engaged= fallen= died= left= deaths= health= lowest=</c>. Pure.</summary>
    public static string FormatEnd(EndFacts f) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{EndMarker} attempt={Token(f.Attempt)} outcome={Token(f.Outcome)} secs={Math.Max(0, f.Secs)} engaged={Num(f.Engaged)} " +
            $"fallen={Num(f.Fallen)} died={Num(f.Died)} left={Num(f.Left)} deaths={Num(f.Deaths)} health={Num(f.Health)} lowest={Num(f.Lowest)}");

    /// <summary>What a RAID_DEATH row says. No names: the raider is its per-attempt index, kind, class and role.</summary>
    public sealed record DeathFacts(string Attempt, long T, int Idx, string Kind, bool Player, string Class, string Role, int Level,
        string KillerKind, string Killer, string Source, int SpellId, string Mechanic, int Dist, int HealersIn, int Attackers);

    /// <summary>
    /// <c>RAID_DEATH attempt= t= idx= kind= player= class= role= level= killerKind= killer="" source= spellId= mechanic= dist=
    /// healersIn= attackers=</c>. Pure.
    /// </summary>
    public static string FormatDeath(DeathFacts f) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{DeathMarker} attempt={Token(f.Attempt)} t={Math.Max(0, f.T)} idx={f.Idx} kind={Token(f.Kind)} player={(f.Player ? 1 : 0)} " +
            $"class={Token(f.Class)} role={Token(f.Role)} level={f.Level} killerKind={Token(f.KillerKind)} killer=\"{Quoted(f.Killer)}\" " +
            $"source={Token(f.Source)} spellId={f.SpellId} mechanic={Token(f.Mechanic)} dist={Num(f.Dist)} healersIn={f.HealersIn} " +
            $"attackers={f.Attackers}");

    // ---- hooks (never throw) ---------------------------------------------------------------------------------------------

    /// <summary>A hit on <paramref name="victim"/> (GameLiving.TakeDamage(AttackData)): kept as its last hit when it is in a live attempt.</summary>
    public static void NoteHit(GameLiving victim, AttackData ad)
    {
        if (ByRaider.IsEmpty || victim == null || ad == null) return;
        try
        {
            if (ByRaider.TryGetValue(victim, out RaidAttempt attempt))
            {
                Spell spell = ad.SpellHandler?.Spell;
                long now = LivingRows.Now();
                attempt.NoteHit(victim, new LastHit(now, false, ad.AttackType, spell?.ID ?? 0, spell?.SpellType ?? eSpellType.None,
                    spell?.Radius ?? 0), ad, now);
                return;
            }
            // #644 parts 4-5 (behind the diagnostics switch): a hit on a monster, for RAID_ROLE_TOTALS and RAID_THREAT's taunts.
            if (RaidDiagnosticRows.On) NoteMonsterHit(victim, ad);
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>Falling damage on <paramref name="victim"/> (GameLiving.TakeDamage with eDamageType.Falling).</summary>
    public static void NoteFall(GameLiving victim)
    {
        if (ByRaider.IsEmpty || victim == null) return;
        try
        {
            if (ByRaider.TryGetValue(victim, out RaidAttempt attempt))
                attempt.NoteHit(victim, new LastHit(LivingRows.Now(), true, AttackData.eAttackType.Unknown, 0, eSpellType.None, 0));
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>A death (GamePlayer / GameBot ProcessDeath, before the death clears anything): RAID_DEATH when the raider is in a live attempt.</summary>
    public static void NoteDeath(GameLiving victim, GameObject killer)
    {
        if (ByRaider.IsEmpty || victim == null) return;
        try
        {
            if (ByRaider.TryGetValue(victim, out RaidAttempt attempt))
                attempt.Died(victim, killer, LivingRows.Now());
        }
        catch
        {
            // Logging only.
        }
    }

    // ---- reading a living (never throws) ----------------------------------------------------------------------------------

    internal static int ClassId(GameLiving living)
    {
        try { return EncounterTargets.ClassId(living); } catch { return 0; }
    }

    internal static string ClassName(GameLiving living)
    {
        try { return EncounterTargets.ClassOf(living)?.Name ?? "-"; } catch { return "-"; }
    }

    internal static string RoleOf(GameLiving living) => Role((eCharacterClass)ClassId(living));

    internal static bool IsPet(GameObject living)
    {
        try { return living is GameNPC { Brain: IControlledBrain } and not GameBot; } catch { return false; }
    }

    internal static bool Alive(GameLiving living)
    {
        try { return living.IsAlive; } catch { return false; }
    }

    internal static ushort Region(GameLiving living)
    {
        try { return living.CurrentRegionID; } catch { return 0; }
    }

    internal static int Distance(GameLiving a, GameObject b)
    {
        try
        {
            if (a == null || b == null || a.CurrentRegionID != b.CurrentRegionID) return -1;
            return (int)a.GetDistanceTo(b);
        }
        catch
        {
            return -1;
        }
    }

    internal static int Attackers(GameLiving living)
    {
        try { return living?.attackComponent?.AttackerTracker?.Attackers?.Count() ?? 0; } catch { return 0; }
    }

    internal static string Mechanics(GameNPC boss, long now)
    {
        try
        {
            if (boss == null) return "-";
            string[] active = EncounterSignals.Active(boss, now).Select(s => Token(s.Mechanic)).Distinct().ToArray();
            return active.Length == 0 ? "-" : string.Join("+", active);
        }
        catch
        {
            return "-";
        }
    }
}

/// <summary>
/// One live raid attempt (#644): its raiders by per-attempt index, who died in it and their last hits. Written by the raid pulse
/// (Join) and by the hit and death hooks on game-loop threads, so every member read and write takes the attempt's lock.
/// </summary>
public sealed partial class RaidAttempt
{
    private readonly object _sync = new();
    private readonly Dictionary<GameLiving, int> _index = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<GameLiving> _died = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GameLiving, RaidAttemptRows.LastHit> _lastHits = new(ReferenceEqualityComparer.Instance);
    private int _deaths;
    private bool _closed;

    public RaidAttempt(string id, string eventId, string kind, ushort region, GameNPC boss, string bossName, long start)
    {
        Id = id;
        Event = eventId;
        Kind = kind;
        Region = region;
        Boss = boss;
        BossName = bossName;
        Start = start;
    }

    public string Id { get; }
    public string Event { get; }
    public string Kind { get; }
    public ushort Region { get; }
    public GameNPC Boss { get; }
    public string BossName { get; }
    public long Start { get; }

    /// <summary>RAID_DEATH rows written (every death, a raider dying twice counts twice).</summary>
    public int Deaths
    {
        get { lock (_sync) return _deaths; }
    }

    public int Members
    {
        get { lock (_sync) return _index.Count; }
    }

    public bool Closed
    {
        get { lock (_sync) return _closed; }
    }

    /// <summary>Adds raiders (each keeps the first index it got) and points the death hook at this attempt.</summary>
    public void Join(IEnumerable<GameLiving> raiders)
    {
        if (raiders == null) return;
        lock (_sync)
        {
            if (_closed) return;
            foreach (GameLiving raider in raiders)
            {
                if (raider == null || _index.ContainsKey(raider)) continue;
                _index[raider] = _index.Count + 1;
                RaidAttemptRows.ByRaider[raider] = this;
                _members = null;
            }
        }
    }

    // ---- RAID_FIGHT_SAMPLE's state (#644 part 2): the buffers are the attempt's own, grown only when raiders join -------

    private GameLiving[] _members;
    private RaidFightSampleRows.Raider[] _sampleRaiders = [];
    private int[] _sampleDists = [];
    private long _nextSample = long.MinValue;
    private string _sampleBudgetTag;

    /// <summary>The attempt's own budget tag for RAID_FIGHT_SAMPLE (4 a minute).</summary>
    public string SampleBudgetTag => _sampleBudgetTag ??= RaidFightSampleRows.AttemptBudgetTag(Id);

    /// <summary>True (and the next one is set) when a sample is due: at the pull, then every <paramref name="interval"/>.</summary>
    public bool SampleDue(long now, long interval)
    {
        lock (_sync)
        {
            if (_closed) return false;
            if (_nextSample == long.MinValue) _nextSample = Start;
            if (now < _nextSample) return false;
            // On time: the next is one interval on from this one's slot; late (a slow pulse): one interval from now.
            _nextSample = now - _nextSample < interval ? _nextSample + interval : now + interval;
            return true;
        }
    }

    /// <summary>The members (rebuilt only after a join) and the sample buffers sized to them (grown only after a join).</summary>
    public RaidFightSampleRows.Raider[] SampleBuffer(out GameLiving[] members, out int[] dists)
    {
        lock (_sync)
        {
            members = _members ??= [.. _index.Keys];
            if (_sampleRaiders.Length < members.Length)
            {
                _sampleRaiders = new RaidFightSampleRows.Raider[members.Length];
                _sampleDists = new int[members.Length];
            }
            dists = _sampleDists;
            return _sampleRaiders;
        }
    }

    public bool IsMember(GameLiving raider)
    {
        lock (_sync) return raider != null && _index.ContainsKey(raider);
    }

    /// <summary>Whether this raider died at least once in the attempt.</summary>
    public bool HasDied(GameLiving raider)
    {
        lock (_sync) return raider != null && _died.Contains(raider);
    }

    /// <summary>The raiders who died at least once (a snapshot).</summary>
    public GameLiving[] DiedMembers()
    {
        lock (_sync) return _died.ToArray();
    }

    internal void NoteHit(GameLiving victim, RaidAttemptRows.LastHit hit, AttackData ad = null, long now = 0)
    {
        lock (_sync)
        {
            if (_closed) return;
            _lastHits[victim] = hit;
            // #644 parts 3-5: a mechanic's hits, and the raider's damage taken (diagnostics).
            if (ad != null) NoteTakenLocked(victim, ad, now);
        }
    }

    /// <summary>A raider of this attempt died: one RAID_DEATH row, no cap (#644 decision a).</summary>
    internal void Died(GameLiving victim, GameObject killer, long now)
    {
        int idx;
        RaidAttemptRows.LastHit? hit;
        GameLiving[] members;
        lock (_sync)
        {
            if (_closed || !_index.TryGetValue(victim, out idx)) return;
            _died.Add(victim);
            _deaths++;
            DeathCounts[victim] = DeathCounts.GetValueOrDefault(victim) + 1;
            hit = _lastHits.TryGetValue(victim, out var h) ? h : null;
            _lastHits.Remove(victim);
            members = _index.Keys.ToArray();
        }
        bool fell = hit is { Fall: true } && now - hit.Value.Tick <= RaidAttemptRows.LastHitMilliseconds;
        bool pet = RaidAttemptRows.IsPet(killer);
        bool monster = killer is GameNPC and not GameBot;
        GameLiving petOwner = pet ? LivingRows.PetOwner(killer as GameLiving) : null;
        // A monster's name (a boss, an add or a monster's pet), never a person's or a raider's pet's.
        string killerName = monster && petOwner is not (GamePlayer or GameBot) ? killer.Name : "-";
        ushort region = RaidAttemptRows.Region(victim);
        int healers = 0;
        foreach (GameLiving m in members)
        {
            if (ReferenceEquals(m, victim) || !RaidAttemptRows.Alive(m) || RaidAttemptRows.Region(m) != region) continue;
            int c = RaidAttemptRows.ClassId(m);
            if (c <= 0 || !BotPartyRoles.IsHealingClass((eCharacterClass)c)) continue;
            int d = RaidAttemptRows.Distance(victim, m);
            if (d >= 0 && d <= RaidAttemptRows.HealRange) healers++;
        }
        int level;
        try { level = victim.Level; } catch { level = 0; }
        LivingRows.Emit(RaidAttemptRows.FormatDeath(new RaidAttemptRows.DeathFacts(Id, (now - Start) / 1000, idx, LivingRows.Kind(victim),
            victim is GamePlayer, RaidAttemptRows.ClassName(victim), RaidAttemptRows.RoleOf(victim), level,
            RaidAttemptRows.KillerKind(killer, victim, Boss, fell, pet), killerName, RaidAttemptRows.Source(hit, now),
            hit is { Fall: false } ? hit.Value.SpellId : 0, RaidAttemptRows.Mechanics(Boss, now), RaidAttemptRows.Distance(victim, Boss),
            healers, RaidAttemptRows.Attackers(victim))));
    }

    /// <summary>Ends the attempt: no more deaths are written for it, and its raiders leave the death hook.</summary>
    internal void Close()
    {
        GameLiving[] members;
        lock (_sync)
        {
            if (_closed) return;
            _closed = true;
            members = _index.Keys.ToArray();
            _lastHits.Clear();
            _members = null;
            _sampleRaiders = [];
            _sampleDists = [];
        }
        // The attempt's RAID_FIGHT_SAMPLE budget window goes with it (#644 part 2).
        if (_sampleBudgetTag != null) RvrRecordBudget.Forget(_sampleBudgetTag);
        // #644 parts 3-5: its mechanic fires still open are written with what is known, and it leaves the live list.
        CloseExtras(LivingRows.Now());
        RaidAttemptRows.Unregister(this);
        foreach (GameLiving raider in members)
            RaidAttemptRows.ByRaider.TryRemove(new KeyValuePair<GameLiving, RaidAttempt>(raider, this));
    }
}

/// <summary>
/// The attempts of one raid route or one dragon / open-world fight (#644): writes RAID_ATTEMPT at each pull and
/// RAID_ATTEMPT_END at each end, and keeps the live attempt the death hook writes RAID_DEATH for. Called from the raid pulse.
/// </summary>
public sealed class RaidAttemptTracker
{
    private readonly Func<IEnumerable<GameLiving>> _guests;
    private readonly Dictionary<string, int> _pulls = new(StringComparer.Ordinal);
    private string _lastOutcome;
    private long _lastEnd;

    /// <param name="eventId">The raid's event id (AutonomousRealmRaid.Definition.Id).</param>
    /// <param name="kind">dungeon, dragon or open.</param>
    /// <param name="period">The raid's period size (SuggestedBots); 0 for none (a route gate's boss in another dungeon).</param>
    /// <param name="guests">The raid's parties' members who are not its gamebots (the player, companions); null for none.</param>
    public RaidAttemptTracker(string eventId, string kind, int period, Func<IEnumerable<GameLiving>> guests = null)
    {
        EventId = eventId;
        Kind = kind;
        Period = period;
        _guests = guests;
    }

    public string EventId { get; }
    public string Kind { get; }
    public int Period { get; }

    /// <summary>The live attempt, or null.</summary>
    public RaidAttempt Current { get; private set; }

    /// <summary>The last attempt's id once it ended (null before any).</summary>
    public string LastId { get; private set; }

    /// <summary>The id the existing attempt rows carry: the live attempt's, else the last one's, else "-".</summary>
    public string CurrentId => Current?.Id ?? LastId ?? "-";

    private int _mostMembers;

    /// <summary>The most raiders any of this tracker's attempts had (RAID_FUNNEL's atBoss; #644 part 2).</summary>
    public int MostMembers => Math.Max(_mostMembers, Current?.Members ?? 0);

    /// <summary>When the first attempt began (RAID_FUNNEL's bossS), or -1 before any.</summary>
    public long FirstBegin { get; private set; } = -1;

    /// <summary>A RAID_FIGHT_SAMPLE of the live attempt when one is due (#644 part 2). Called from the raid pulse; never throws.</summary>
    public void Sample(long now) => RaidFightSampleRows.SampleIfDue(Current, now);

    private GameLiving[] GuestsIn(ushort region)
    {
        try
        {
            return _guests?.Invoke()?.Where(g => g != null && RaidAttemptRows.Alive(g) && RaidAttemptRows.Region(g) == region).ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// A pull: <paramref name="raiders"/> are in the attempt (and the raid's guests in the region); <paramref name="raid"/> is the
    /// raid's roster (its living members in the region are the size). <paramref name="gatherMilliseconds"/>: how long the
    /// muster, staging or regroup waited before it (negative: unknown). An attempt still open is ended first as "stopped".
    /// </summary>
    public RaidAttempt Begin(GameNPC boss, ushort region, IEnumerable<GameLiving> raiders, IEnumerable<GameLiving> raid, long now,
        long gatherMilliseconds)
    {
        try
        {
            if (Current != null)
                End("stopped", Current.Members, -1, -1, -1, -1, -1, now);
            string bossName = boss?.Name;
            GameLiving[] guests = GuestsIn(region);
            GameLiving[] size = (raid ?? []).Where(p => p != null && RaidAttemptRows.Alive(p) && RaidAttemptRows.Region(p) == region)
                .Concat(guests).Distinct<GameLiving>(ReferenceEqualityComparer.Instance).ToArray();
            string id = RaidAttemptRows.NextId(EventId);
            string pullKey = bossName ?? "-";
            int n = _pulls[pullKey] = _pulls.GetValueOrDefault(pullKey) + 1;
            var attempt = new RaidAttempt(id, EventId, Kind, region, boss, bossName, now);
            attempt.Join((raiders ?? []).Concat(guests));
            RaidAttemptRows.Register(attempt);
            Current = attempt;
            if (FirstBegin < 0) FirstBegin = now;
            bool afterFailure = LastId != null && _lastOutcome is "wipe" or "reset" or "skirmish";
            if (LivingRows.Enabled && LivingRows.Take(RaidAttemptRows.AttemptMarker, RaidAttemptRows.AttemptPerMinute))
                LivingRows.Emit(RaidAttemptRows.FormatAttempt(new RaidAttemptRows.AttemptFacts(id, EventId, Kind, region, bossName, n,
                    size.Length, Period, size.Count(p => p is GameBot { IsEpicSpecialForce: true }),
                    RaidAttemptRows.RoleCounts(size.Select(RaidAttemptRows.RoleOf)), RaidAttemptRows.ClassCounts(size.Select(RaidAttemptRows.ClassName)),
                    size.Count(p => p is GamePlayer), RaidAttemptRows.Build(SafeCommit()),
                    afterFailure ? LastId : null, afterFailure ? (now - _lastEnd) / 1000 : -1,
                    gatherMilliseconds >= 0 ? gatherMilliseconds / 1000 : -1)));
            return attempt;
        }
        catch
        {
            // Logging only.
            return Current;
        }
    }

    private static string SafeCommit()
    {
        try { return ForkBuildInfo.Commit; } catch { return "local"; }
    }

    /// <summary>Raiders who joined the live attempt during the fight (and the raid's guests now in its region).</summary>
    public void Join(IEnumerable<GameLiving> raiders)
    {
        RaidAttempt attempt = Current;
        if (attempt == null) return;
        try
        {
            attempt.Join((raiders ?? []).Concat(GuestsIn(attempt.Region)));
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>
    /// Ends the live attempt with its outcome and counts (negative: unknown) and writes RAID_ATTEMPT_END; returns its id, or
    /// <see cref="CurrentId"/> when none was live.
    /// </summary>
    public string End(string outcome, int engaged, int fallen, int died, int left, int health, int lowest, long now)
    {
        RaidAttempt attempt = Current;
        if (attempt == null) return CurrentId;
        Current = null;
        LastId = attempt.Id;
        _mostMembers = Math.Max(_mostMembers, attempt.Members);
        _lastOutcome = outcome;
        _lastEnd = now;
        try
        {
            attempt.Close();
            if (LivingRows.Enabled && LivingRows.Take(RaidAttemptRows.EndMarker, RaidAttemptRows.EndPerMinute))
                LivingRows.Emit(RaidAttemptRows.FormatEnd(new RaidAttemptRows.EndFacts(attempt.Id, outcome, (now - attempt.Start) / 1000,
                    engaged, fallen, died, left, attempt.Deaths, health, lowest)));
            // #644 part 5 (diagnostics switch): one RAID_ROLE_TOTALS a role.
            if (RaidDiagnosticRows.On) attempt.WriteRoleTotals();
        }
        catch
        {
            // Logging only.
        }
        return attempt.Id;
    }
}
