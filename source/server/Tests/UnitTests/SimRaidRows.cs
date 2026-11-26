using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// One simulated raid attempt written as the server's own RAID_* rows (#644 part 6: "the sims writing the same rows"), so a
    /// simulated pull can be read with the same parser (source/tools/living_report_sections/_raid.py, raid_postmortem.py) and
    /// compared line by line with a logged one. Every row goes through the server's pure formatters (RaidAttemptRows.Format*,
    /// RaidFightSampleRows.FormatSample) and the server's own helpers decide its values where one exists: the role (Role), the
    /// role and class counts, the death's source (Source, from the last hit in the 5 s before), the died/left split
    /// (SplitFallen), the sample's counts (RaidFightSampleRows.Count), whom the boss fights (TargetKind), why it changed its
    /// target (ThreatWhy), a mechanic's died and avoided (MechanicOutcome) and the role totals (RoleTotals). The minute budgets
    /// are the server's, on the sim's clock.
    /// <para>Logging only: a sim calls this beside what it already does, never draws a random number for it and never reads
    /// anything back, so its results are the same with and without it (UT_SimRaidRows644 checks that).</para>
    /// <para>What tells a sim's rows from a server's: <c>build=sim</c> on RAID_ATTEMPT. The attempt id is the server's
    /// <c>&lt;event&gt;.&lt;n&gt;</c> with the raid's real event id, so a sim and a logged pull sit under the same event and boss.
    /// The rows are those of a server with the diagnostics switch on (RAID_THREAT and RAID_ROLE_TOTALS) unless
    /// <c>diagnostics</c> is false. Every raider is a level 50 gamebot (no player, no companion, no Special Forces).</para>
    /// <para>Tests only: a sim is a model, not a game result (docs/RAID-ATTEMPT-ROWS.md, "Rows from the sims").</para>
    /// </summary>
    public sealed class SimRaidRows
    {
        /// <summary>RAID_ATTEMPT's build= of a simulated pull.</summary>
        public const string SimBuild = "sim";

        /// <summary>The logger name the server's rows carry (LivingRows.Emit).</summary>
        public const string Logger = "DOL.GS.LivingRows";

        /// <summary>The written log's clock starts here (a leap year, as raid_postmortem.py reads stamps).</summary>
        public static readonly DateTime Clock = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        /// <summary>One raider of the attempt, by its per-attempt index (no name).</summary>
        public sealed class Raider
        {
            public int Idx;
            public eCharacterClass ClassId;
            public string Class;
            public string Role;
            public bool Dead;
            public int Deaths;
            public long LastCombatMs = long.MinValue;
            public long DeathMs = -1;
            public RaidAttemptRows.LastHit? LastHit;
            public double DealtBoss, DealtAdds, Took, Healed, Overheal;
        }

        private sealed class Fire
        {
            public string Boss, Mechanic;
            public long At, Lands, Until, ReportAt;
            public int Radius;
            public bool Area, Called;
            public Raider[] Exposed, WithinAtLanding;
        }

        private readonly List<(long Ms, string Row)> _rows = new();
        private readonly List<(long Ms, string Boss, Raider Raider, long Damage)> _spellHits = new();
        private readonly List<Fire> _fires = new();
        private readonly Dictionary<string, (long Minute, int Count)> _budget = new(StringComparer.Ordinal);
        private readonly List<Raider> _raiders = new();
        private long _start, _nextSample;
        private bool _pulled, _ended, _threatSeen;
        private int _deaths, _threatTarget = -1;
        private long _threatSince;

        /// <param name="eventId">The raid's event id (AutonomousRealmRaid.Definitions), e.g. summoners-albion.</param>
        /// <param name="attemptNumber">The n of the attempt id (the sims use their seed + 1).</param>
        /// <param name="kind">dungeon, dragon or open, as the server's tracker has it.</param>
        /// <param name="region">The boss's region.</param>
        /// <param name="boss">The boss's name (its script's Name).</param>
        /// <param name="period">The raid's period size (SuggestedBots); 0 for none.</param>
        /// <param name="diagnostics">Write RAID_THREAT and RAID_ROLE_TOTALS, as a server with raid_diagnostic_rows on.</param>
        public SimRaidRows(string eventId, int attemptNumber, string kind, ushort region, string boss, int period, bool diagnostics = true)
        {
            Event = eventId;
            Attempt = string.Create(CultureInfo.InvariantCulture, $"{RaidAttemptRows.Token(eventId)}.{attemptNumber}");
            Kind = kind;
            Region = region;
            Boss = boss;
            Period = period;
            Diagnostics = diagnostics;
        }

        public string Event { get; }
        public string Attempt { get; }
        public string Kind { get; }
        public ushort Region { get; }
        public string Boss { get; }
        public int Period { get; }
        public bool Diagnostics { get; }
        public IReadOnlyList<Raider> Raiders => _raiders;
        public int Deaths => _deaths;
        public bool Ended => _ended;

        /// <summary>The sim's clock (ms since the pull) at the end; -1 before.</summary>
        public long EndMs { get; private set; } = -1;

        /// <summary>The rows in the order a log would hold them (by time; a row's own order within the same millisecond).</summary>
        public IReadOnlyList<string> Rows => Ordered().Select(r => r.Row).ToArray();

        private IEnumerable<(long Ms, string Row)> Ordered() => _rows.Select((r, i) => (r, i)).OrderBy(p => p.r.Ms).ThenBy(p => p.i).Select(p => p.r);

        private void Emit(long ms, string row) => _rows.Add((ms, row));

        private long T(long ms) => Math.Max(0, ms - _start) / 1000;

        /// <summary>The server's minute budget (RvrRecordBudget.Take), on the sim's clock.</summary>
        private bool Take(string tag, int perMinute, long ms)
        {
            long minute = ms / 60_000;
            (long Minute, int Count) w = _budget.TryGetValue(tag, out var seen) && seen.Minute == minute ? seen : (minute, 0);
            if (w.Count >= perMinute)
            {
                _budget[tag] = w;
                return false;
            }
            _budget[tag] = (minute, w.Count + 1);
            return true;
        }

        // ---- the raiders ---------------------------------------------------------------------------------------------------

        /// <summary>Adds raiders in order (index 1, 2, ...); the role is the server's (RaidAttemptRows.Role) for the class.</summary>
        public void Join(IEnumerable<eCharacterClass> classes)
        {
            foreach (eCharacterClass c in classes)
                _raiders.Add(new Raider { Idx = _raiders.Count + 1, ClassId = c, Class = c.ToString(), Role = RaidAttemptRows.Role(c) });
        }

        /// <summary>A hit on raider <paramref name="i"/> (0-based): its last hit (the death row's source), its damage taken, and, for
        /// a spell of <paramref name="caster"/>, a mechanic's hit.</summary>
        public void Hit(long ms, int i, AttackData.eAttackType type, double damage, int spellId = 0, eSpellType spellType = eSpellType.None,
            int radius = 0, string caster = null)
        {
            if (_ended || i < 0 || i >= _raiders.Count) return;
            Raider r = _raiders[i];
            r.LastHit = new RaidAttemptRows.LastHit(ms, false, type, spellId, spellType, radius);
            r.Took += Math.Max(0, damage);
            r.LastCombatMs = ms;
            if (type == AttackData.eAttackType.Spell && caster != null) _spellHits.Add((ms, caster, r, (long)Math.Round(Math.Max(0, damage))));
        }

        /// <summary>Raider <paramref name="i"/> (a pet's owner for its pet) dealt damage to the boss or to an add.</summary>
        public void Dealt(long ms, int i, double amount, bool onBoss)
        {
            if (_ended || i < 0 || i >= _raiders.Count || amount <= 0) return;
            Raider r = _raiders[i];
            if (onBoss) r.DealtBoss += amount;
            else r.DealtAdds += amount;
            r.LastCombatMs = ms;
        }

        /// <summary>Raider <paramref name="i"/> healed: what its heal restored and the rest of what it cast.</summary>
        public void Healed(int i, double effective, double overheal)
        {
            if (_ended || i < 0 || i >= _raiders.Count) return;
            _raiders[i].Healed += Math.Max(0, effective);
            _raiders[i].Overheal += Math.Max(0, overheal);
        }

        /// <summary>In combat as the game counts it (GameLiving.InCombat: hit or hitting in the last 10 s).</summary>
        public bool InCombat(int i, long ms) => i >= 0 && i < _raiders.Count && _raiders[i].LastCombatMs != long.MinValue &&
            _raiders[i].LastCombatMs + GameLiving.IN_COMBAT_DURATION >= ms;

        /// <summary>Living healing-class raiders (BotPartyRoles.IsHealingClass, as RAID_DEATH's healersIn=) other than
        /// <paramref name="victim"/> within the heal range; <paramref name="distance"/> gives a raider's distance to the victim.</summary>
        public int HealersIn(int victim, Func<int, double> distance)
        {
            int n = 0;
            for (int j = 0; j < _raiders.Count; j++)
            {
                if (j == victim || _raiders[j].Dead || !BotPartyRoles.IsHealingClass(_raiders[j].ClassId)) continue;
                double d = distance(j);
                if (d >= 0 && d <= RaidAttemptRows.HealRange) n++;
            }
            return n;
        }

        // ---- the rows ------------------------------------------------------------------------------------------------------

        /// <summary>RAID_ATTEMPT: the pull, with the living raiders.</summary>
        public void Pull(long ms = 0, int n = 1, string prev = null, long wipeToPullS = -1, long gatherS = -1)
        {
            if (_pulled) return;
            _pulled = true;
            _start = ms;
            _nextSample = ms;
            Raider[] size = _raiders.Where(r => !r.Dead).ToArray();
            if (Take(RaidAttemptRows.AttemptMarker, RaidAttemptRows.AttemptPerMinute, ms))
                Emit(ms, RaidAttemptRows.FormatAttempt(new RaidAttemptRows.AttemptFacts(Attempt, Event, Kind, Region, Boss, n, size.Length, Period,
                    0, RaidAttemptRows.RoleCounts(size.Select(r => r.Role)), RaidAttemptRows.ClassCounts(size.Select(r => r.Class)), 0,
                    RaidAttemptRows.Build(SimBuild), prev, wipeToPullS, gatherS)));
        }

        /// <summary>
        /// RAID_DEATH for raider <paramref name="i"/>: its source and spell from its last hit (RaidAttemptRows.Source), the
        /// killer's kind and name as the sim knows them (a monster's name, "-" for none), its distance to the boss (-1 unknown),
        /// the healers in range and how many were on it.
        /// </summary>
        public void Death(long ms, int i, string killerKind, string killer, int dist, int healersIn, int attackers, string mechanic = "-")
        {
            if (!_pulled || _ended || i < 0 || i >= _raiders.Count) return;
            Raider r = _raiders[i];
            r.Dead = true;
            r.DeathMs = ms;
            r.Deaths++;
            _deaths++;
            RaidAttemptRows.LastHit? hit = r.LastHit;
            r.LastHit = null;
            Emit(ms, RaidAttemptRows.FormatDeath(new RaidAttemptRows.DeathFacts(Attempt, T(ms), r.Idx, "gamebot", false, r.Class, r.Role, 50,
                killerKind, killer, RaidAttemptRows.Source(hit, ms), hit is { Fall: false } ? hit.Value.SpellId : 0, mechanic, dist, healersIn,
                attackers)));
        }

        /// <summary>True (and the next one is set) when a RAID_FIGHT_SAMPLE is due: at the pull, then every 15 s.</summary>
        public bool SampleDue(long ms)
        {
            if (!_pulled || _ended || ms < _nextSample) return false;
            _nextSample = ms - _nextSample < RaidFightSampleRows.IntervalMilliseconds ? _nextSample + RaidFightSampleRows.IntervalMilliseconds
                : ms + RaidFightSampleRows.IntervalMilliseconds;
            return true;
        }

        /// <summary>
        /// RAID_FIGHT_SAMPLE from the raiders as the sim sees them (RaidFightSampleRows.Count). <paramref name="everyoneEngaged"/>:
        /// a model without positions, where every living raider fights the boss, counts the living as engaged (Count needs a
        /// distance). phase and adds are "-", as on the server.
        /// </summary>
        public void Sample(long ms, int bossPct, RaidFightSampleRows.Raider[] raiders, string tgtKind, bool everyoneEngaged = false)
        {
            if (!_pulled || _ended) return;
            RaidFightSampleRows.Counts c = RaidFightSampleRows.Count(raiders, raiders.Length, new int[raiders.Length]);
            if (everyoneEngaged) c = c with { Engaged = c.Alive };
            if (Take(RaidFightSampleRows.AttemptBudgetTag(Attempt), RaidFightSampleRows.PerAttemptPerMinute, ms) &&
                Take(RaidFightSampleRows.SampleMarker, RaidFightSampleRows.OverallPerMinute, ms))
                Emit(ms, RaidFightSampleRows.FormatSample(new RaidFightSampleRows.SampleFacts(Attempt, T(ms), bossPct, null, -1, c, tgtKind)));
        }

        /// <summary>Whom the boss fights, as RAID_FIGHT_SAMPLE's tgtKind: raider <paramref name="i"/> by role, or none.</summary>
        public string TargetKind(int i) =>
            i < 0 || i >= _raiders.Count ? "none" : RaidFightSampleRows.TargetKind(true, true, _raiders[i].Role, false);

        /// <summary>
        /// A mechanic of <paramref name="boss"/> signalled now (RAID_MECHANIC_FIRE, written 6 s after it lands or at the end).
        /// <paramref name="area"/>: it has an area or names a target (the server's Fire.Area); then <paramref name="exposed"/> are
        /// the living raiders in it now and <paramref name="withinAtLanding"/> those in it when it lands (the sims' mechanics land
        /// at once or name the boss itself, so both are known at the call). Its hits are the raiders <see cref="Hit"/> notes as
        /// hit by a spell of the boss from 0.5 s before the landing to its aftermath's end.
        /// </summary>
        public void Mechanic(long ms, string boss, string mechanic, int radius, bool area, IEnumerable<int> exposed,
            IEnumerable<int> withinAtLanding, long leadMs, bool called = false)
        {
            if (!_pulled || _ended) return;
            long lands = ms + Math.Max(0, leadMs);
            long until = lands + EncounterSignals.AftermathMilliseconds;
            Raider[] Pick(IEnumerable<int> idx) => (idx ?? []).Where(i => i >= 0 && i < _raiders.Count && !_raiders[i].Dead)
                .Distinct().Select(i => _raiders[i]).ToArray();
            _fires.Add(new Fire
            {
                Boss = boss,
                Mechanic = mechanic,
                At = ms,
                Lands = lands,
                Until = until,
                ReportAt = Math.Max(until, lands + RaidAttemptRows.MechanicCountAfterMilliseconds),
                Radius = Math.Max(0, radius),
                Area = area,
                Called = called,
                Exposed = area ? Pick(exposed) : null,
                WithinAtLanding = area ? Pick(withinAtLanding) : null,
            });
        }

        /// <summary>Each step: the mechanic fires whose count is due are written.</summary>
        public void Pulse(long ms)
        {
            if (_fires.Count == 0) return;
            foreach (Fire f in _fires.Where(f => ms >= f.ReportAt).ToArray())
            {
                _fires.Remove(f);
                Report(f, ms);
            }
        }

        private void Report(Fire f, long ms)
        {
            var hits = _spellHits.Where(h => h.Boss == f.Boss && h.Ms >= f.Lands - RaidAttemptRows.MechanicHitLeadMilliseconds && h.Ms <= f.Until).ToArray();
            Raider[] hit = hits.Select(h => h.Raider).Distinct().ToArray();
            long damage = hits.Sum(h => h.Damage);
            Raider[] within = ms >= f.Lands ? f.WithinAtLanding : null;
            int exposed = -1, inside = -1, avoided = -1, died;
            if (f.Area)
            {
                exposed = f.Exposed.Length;
                inside = within?.Length ?? -1;
                (died, avoided) = RaidAttemptRows.MechanicOutcome(f.Exposed, within ?? Array.Empty<Raider>(), hit, r => r.Dead);
                if (within == null) avoided = -1;
            }
            else
                died = hit.Count(r => r.Dead);
            if (!Take(RaidAttemptRows.MechanicMarker, RaidAttemptRows.MechanicPerMinute, ms)) return;
            // reactMedianMs: the sims model no stepping out before a landing, so it is "-" (the server's, too, for a mechanic
            // that lands at once).
            Emit(ms, RaidAttemptRows.FormatMechanic(new RaidAttemptRows.MechanicFacts(Attempt, f.Boss, f.Mechanic, T(f.At), f.Radius, exposed,
                inside, hit.Length, died, avoided, f.Called, f.Lands - f.At, -1, damage)));
        }

        /// <summary>RAID_THREAT (diagnostics) when the boss's target changed to raider <paramref name="target"/> (-1 none).</summary>
        public void Threat(long ms, int target, bool mechanicActive = false)
        {
            if (!Diagnostics || !_pulled || _ended) return;
            if (_threatSeen && target == _threatTarget) return;
            int previous = _threatTarget;
            long since = _threatSeen ? _threatSince : _start;
            _threatSeen = true;
            _threatTarget = target;
            _threatSince = ms;
            if (previous < 0 && target < 0) return;
            bool previousDied = previous >= 0 && previous < _raiders.Count && _raiders[previous].Dead;
            if (!Take(RaidAttemptRows.ThreatMarker + "." + RaidAttemptRows.Token(Attempt), RaidAttemptRows.ThreatPerMinutePerBoss, ms)) return;
            Emit(ms, RaidAttemptRows.FormatThreat(new RaidAttemptRows.ThreatFacts(Attempt, Boss, T(ms), TargetKind(previous), TargetKind(target),
                target >= 0 && target < _raiders.Count ? "gamebot" : "-", RaidAttemptRows.ThreatWhy(previousDied, false, mechanicActive), ms - since)));
        }

        /// <summary>
        /// RAID_ATTEMPT_END (after the open mechanic fires, as the server's Close writes them first), then the four
        /// RAID_ROLE_TOTALS (diagnostics). fallen: the dead raiders (a sim's raiders never leave); died and left by SplitFallen.
        /// </summary>
        public void End(long ms, string outcome, int health, int lowest)
        {
            if (!_pulled || _ended) return;
            foreach (Fire f in _fires.ToArray()) Report(f, ms);
            _fires.Clear();
            _ended = true;
            EndMs = ms;
            int fallen = _raiders.Count(r => r.Dead);
            (int died, int left) = RaidAttemptRows.SplitFallen(_raiders, r => r.Dead, r => r.Deaths > 0);
            if (Take(RaidAttemptRows.EndMarker, RaidAttemptRows.EndPerMinute, ms))
                Emit(ms, RaidAttemptRows.FormatEnd(new RaidAttemptRows.EndFacts(Attempt, outcome, T(ms), _raiders.Count, fallen, died, left, _deaths,
                    health, lowest)));
            if (!Diagnostics) return;
            static long L(double v) => (long)Math.Round(v, MidpointRounding.AwayFromZero);
            foreach (RaidAttemptRows.RoleTotalsFacts f in RaidAttemptRows.RoleTotals(Attempt, _raiders.Select(r => new RaidAttemptRows.RaiderLine(
                         r.Role, r.Class, new RaiderTotals { DealtBoss = L(r.DealtBoss), DealtAdds = L(r.DealtAdds), Took = L(r.Took), Healed = L(r.Healed),
                             Overheal = L(r.Overheal) }, r.Deaths))))
                if (Take(RaidAttemptRows.RoleTotalsMarker, RaidAttemptRows.RoleTotalsPerMinute, ms))
                    Emit(ms, RaidAttemptRows.FormatRoleTotals(f));
        }

        // ---- the written log -----------------------------------------------------------------------------------------------

        /// <summary>A row with the server log's header, at <paramref name="ms"/> on the written log's clock.</summary>
        public static string Headed(long ms, string row) =>
            Clock.AddMilliseconds(ms).ToString("MM/dd HH:mm:ss", CultureInfo.InvariantCulture) + " | INF | " + Logger + " | " + row;

        /// <summary>The attempts as one log, one after another with a minute between them.</summary>
        public static List<string> Log(IEnumerable<SimRaidRows> attempts)
        {
            var lines = new List<string>();
            long offset = 0;
            foreach (SimRaidRows a in attempts)
            {
                long last = 0;
                foreach ((long ms, string row) in a.Ordered())
                {
                    lines.Add(Headed(offset + ms, row));
                    last = Math.Max(last, ms);
                }
                offset += Math.Max(last, a.EndMs) + 60_000;
            }
            return lines;
        }

        /// <summary>
        /// Writes the attempts to <c>raid-sim/&lt;name&gt;.raid.log</c> (RAID_SIM_DIR, else the test's work folder), for the
        /// server job's "The simulated raid rows read like a server log" step (raid_postmortem.py --check). Returns the path.
        /// </summary>
        public static string Write(string name, IEnumerable<SimRaidRows> attempts)
        {
            string folder = Environment.GetEnvironmentVariable("RAID_SIM_DIR") is { Length: > 0 } dir ? dir
                : Path.Combine(TestContext.CurrentContext.WorkDirectory, "raid-sim");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name + ".raid.log");
            File.WriteAllText(path, string.Join("\n", Log(attempts)) + "\n");
            return path;
        }
    }
}
