using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Crafting 2.0 WP7: the money-supply governor (ours). Every hour it reads WP2's ledger row of each realm
    /// (<c>offline_economy_ledger</c>: the gamebots' coin and every faucet and sink of the hour) and keeps the growth of
    /// the money supply inside the band the economy rehearsal's gates use (<see cref="Gates"/>: at most 400% a week for an
    /// aged world, at most 100 gold a bot-hour for a fresh one; fixtures/calibration.json, pinned by UT_BotSinks).
    /// <para>It treats the sinks as a whole: its input is the hour's net, every faucet less every sink (the dyes, the
    /// vendor gear, the tickets, the repairs, everything the ledger names), so a big sink elsewhere (the owner's PC ledger
    /// of 2026-11-15 had dye at about 47.8 million copper an hour against vendor gear's 1.15 million) leaves less for the
    /// governor to do. Its levers are the luxury sink's intensity per realm (0-1; <see cref="BotLuxury"/>) and, when a
    /// realm is over the band's limit even so, WP5's commission multiplier (<see cref="QuartermasterCommissions.GovernorMultiplier"/>,
    /// halved while over, 1 otherwise). It aims at half the limit (<see cref="Target"/>); with the supply shrinking it
    /// halves the luxury every hour. It starts at no luxury and moves at most 0.2 an hour (the ledger is an hour behind). It runs only while <c>bot_luxury_spending</c> (and <c>bot_crafters</c>) is on; turned
    /// off, it puts the commission multiplier back to 1 and forgets its state. One ECON_GOVERNOR line per realm and hour.</para>
    /// </summary>
    public sealed class SinkGovernor : IEconomyStore
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(SinkGovernor));
        public static readonly SinkGovernor Instance = new();

        /// <summary>The growth band: the rehearsal's gates (EconomyRehearsal.BoundedSupplyGrowth).</summary>
        public readonly record struct Band(double WeekPercentMax, double PerBotHourGoldMax);

        /// <summary>The rehearsal's gates (fixtures/calibration.json "gates"); UT_BotSinks pins that they agree.</summary>
        public static readonly Band Gates = new(400, 100);

        /// <summary>The share of the band's limit the governor aims at.</summary>
        public const double Target = 0.5;
        /// <summary>How far the intensity moves per hour per unit of the smoothed ratio's distance from the target.</summary>
        public const double Gain = 0.5;
        /// <summary>The weight of a new hour in the smoothed ratio.</summary>
        public const double Smoothing = 0.5;
        /// <summary>The luxury intensity before the first hour is read: none (no data, no luxury).</summary>
        public const double StartIntensity = 0;
        /// <summary>The most the intensity moves in an hour (the ledger is an hour behind, so a bigger step overshoots).</summary>
        public const double MaxStepPerHour = 0.2;
        /// <summary>An hour's ratio counts at most this far from 0 (one wild hour must not swing the governor).</summary>
        public const double RatioClamp = 2;
        /// <summary>A world is "aged" (judged by its weekly percent) once its bots hold 100 gold each on average.</summary>
        public const long AgedCopperPerBot = 1_000_000;
        /// <summary>WP5's commission multiplier while a realm is over the band's limit.</summary>
        public const double CommissionMultiplierWhenOver = 0.5;

        /// <summary>The governor's state for one realm (pure).</summary>
        public readonly record struct State(double Smoothed, double Intensity, int Hours)
        {
            public static State Start => new(0, StartIntensity, 0);
            public bool Over => Hours > 0 && Smoothed >= 1;
        }

        /// <summary>
        /// Pure: an hour's growth as a share of the band's limit (1 = at the limit, negative = shrinking): the weekly
        /// percent for an aged world (and the per-bot-hour rate if that is higher), the per-bot-hour rate for a fresh one.
        /// </summary>
        public static double Ratio(long supplyCopper, long netCopper, int bots, Band band)
        {
            double perBot = bots > 0 && band.PerBotHourGoldMax > 0 ? netCopper / 10_000.0 / bots / band.PerBotHourGoldMax : 0;
            bool aged = supplyCopper > 0 && bots > 0 && supplyCopper >= AgedCopperPerBot * bots;
            if (!aged || band.WeekPercentMax <= 0)
                return perBot;
            double week = netCopper * 168.0 * 100.0 / supplyCopper / band.WeekPercentMax;
            return Math.Max(week, perBot);
        }

        /// <summary>Pure: the next hour's state from this hour's ratio.</summary>
        public static State Step(State state, double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio))
                return state;
            ratio = Math.Clamp(ratio, -RatioClamp, RatioClamp);
            double smoothed = state.Hours == 0 ? ratio : state.Smoothed + Smoothing * (ratio - state.Smoothed);
            double intensity = smoothed < 0
                ? state.Intensity * 0.5
                : state.Intensity + Math.Clamp(Gain * (smoothed - Target), -MaxStepPerHour, MaxStepPerHour);
            return new State(smoothed, Math.Clamp(intensity, 0, 1), state.Hours + 1);
        }

        /// <summary>Pure: WP5's commission multiplier for the realms' states.</summary>
        public static double CommissionMultiplier(IEnumerable<State> realms) =>
            (realms ?? Enumerable.Empty<State>()).Any(state => state.Over) ? CommissionMultiplierWhenOver : 1.0;

        // ---- the server's governor ----

        private readonly State[] _states = Enumerable.Repeat(State.Start, 4).ToArray();
        private readonly string[] _lastHour = new string[4];
        private readonly object _gate = new();
        private int _reading;
        private long _nextCheck;
        private bool _active;

        public string Name => "money-supply governor (WP7)";

        /// <summary>The luxury intensity of a realm (0 while bot_luxury_spending is off).</summary>
        public double Intensity(eRealm realm)
        {
            if (!BotSinks.LuxuryOn)
                return 0;
            lock (_gate)
                return _states[BotSinks.Slot(realm)].Intensity;
        }

        public State StateOf(eRealm realm)
        {
            lock (_gate)
                return _states[BotSinks.Slot(realm)];
        }

        public void Load() { }

        public void Pulse(long now)
        {
            if (!BotSinks.LuxuryOn)
            {
                if (_active)
                    Deactivate();
                return;
            }
            _active = true;
            if (now < Volatile.Read(ref _nextCheck) || Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
                return;
            Volatile.Write(ref _nextCheck, now + 5 * 60_000);
            // The table is read off the game loop, at most every five minutes; a new hour row is applied once.
            Task.Run(() =>
            {
                try { ReadLedger(DateTime.UtcNow); }
                catch (Exception exception)
                {
                    try { if (Log.IsWarnEnabled) Log.Warn("Money-supply governor: could not read the economy ledger", exception); }
                    catch { }
                }
                finally { Volatile.Write(ref _reading, 0); }
            });
        }

        public void Flush(bool wait) { }

        private void Deactivate()
        {
            _active = false;
            QuartermasterCommissions.GovernorMultiplier = 1.0;
            lock (_gate)
                for (int i = 0; i < _states.Length; i++)
                {
                    _states[i] = State.Start;
                    _lastHour[i] = null;
                }
        }

        private void ReadLedger(DateTime utcNow)
        {
            string since = EconomyLedgerSnapshot.Stamp(utcNow.AddHours(-3));
            IList<OfflineEconomyLedgerRecord> rows = DOLDB<OfflineEconomyLedgerRecord>.SelectObjects(DB.Column("HourUtc").IsGreaterOrEqualTo(since));
            foreach (OfflineEconomyLedgerRecord row in rows.Where(r => r.Realm is >= 1 and <= 3)
                         .GroupBy(r => r.Realm).Select(g => g.OrderBy(r => r.HourUtc, StringComparer.Ordinal).Last()))
                Apply(row);
        }

        /// <summary>Applies one ledger row (once per realm and hour). Public for tests and the rehearsal's checks.</summary>
        public bool Apply(OfflineEconomyLedgerRecord row)
        {
            if (row == null || row.Realm is < 1 or > 3)
                return false;
            long net = Sum(row.FaucetsJson) - Sum(row.SinksJson);
            return Apply((eRealm)row.Realm, row.HourUtc, row.BotCoin, net, row.Bots);
        }

        public bool Apply(eRealm realm, string hourUtc, long supplyCopper, long netCopper, int bots)
        {
            int slot = BotSinks.Slot(realm);
            State next;
            double ratio = Ratio(supplyCopper, netCopper, bots, Gates);
            lock (_gate)
            {
                if (slot == 0 || hourUtc != null && _lastHour[slot] == hourUtc)
                    return false;
                _lastHour[slot] = hourUtc;
                _active = true;
                next = _states[slot] = Step(_states[slot], ratio);
                QuartermasterCommissions.GovernorMultiplier = CommissionMultiplier(_states.Skip(1));
            }
            if (Log.IsInfoEnabled)
                Log.Info(Line(realm, hourUtc, supplyCopper, netCopper, bots, ratio, next, QuartermasterCommissions.GovernorMultiplier));
            return true;
        }

        /// <summary>The ECON_GOVERNOR line.</summary>
        public static string Line(eRealm realm, string hourUtc, long supplyCopper, long netCopper, int bots, double ratio, State state,
            double commissionMultiplier) =>
            EconomyLog.Line(EconomyLog.EconGovernor, ("hour", hourUtc ?? string.Empty), ("realm", realm), ("botCoin", supplyCopper),
                ("netCopper", netCopper), ("bots", bots), ("ratio", Math.Round(ratio, 3)), ("smoothed", Math.Round(state.Smoothed, 3)),
                ("target", Target), ("luxury", Math.Round(state.Intensity, 3)), ("commissions", commissionMultiplier),
                ("over", state.Over), ("weekPercentMax", Gates.WeekPercentMax), ("perBotHourGoldMax", Gates.PerBotHourGoldMax));

        /// <summary>The copper total of a ledger row's faucets or sinks (a JSON object of kind to copper).</summary>
        public static long Sum(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return 0;
            try
            {
                Dictionary<string, long> kinds = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
                return kinds?.Values.Sum() ?? 0;
            }
            catch (JsonException)
            {
                return 0;
            }
        }

        /// <summary>Tests: back to the start state, the commission multiplier 1.</summary>
        public void ResetForTests()
        {
            Deactivate();
            Volatile.Write(ref _nextCheck, 0);
        }
    }
}
