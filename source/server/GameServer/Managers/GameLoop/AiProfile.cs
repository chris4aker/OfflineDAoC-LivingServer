using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    // Living Server 1.2, AI_PROFILE: the per-system cost profiler ("measure before adding", audit 2026-11-03 §8.4).
    //
    // Borrowed, with thanks, from two GPL-3.0 forks of Offline DAoC:
    // - lometur's HearthDAoC bot CPU meter (scripts/hearthdaoc/BotCpuMeter.cs, #137/#148/#150,
    //   refs/forks/lometur/cpu-meter-alloc-dash): one padded long[] slot per thread, written with plain adds by its own
    //   thread only and summed by the reader with Volatile.Read, so the hot path takes no lock and allocates nothing;
    //   cumulative totals with a window as the difference of two snapshots; and the split of brain turns by bot kind.
    // - stefanrows's BotThinkProfiler (bots/autonomous/BotThinkProfiler.cs, bug 56, refs/forks/stefanrows/main): the
    //   disposable Scope struct with two timestamps, the measured lock that only times a contended enter
    //   (EnterMeasured / MeasuredLock), and the once-a-minute summary line.
    //
    // What is ours: self time (each scope's own time, not counting the scopes inside it), per-minute tick percentiles split
    // into serial planning and parallel brain time, the minute report, /lsprofile and the CSV file.
    //
    // It only measures. Nothing here changes what a bot decides or when it runs.

    /// <summary>
    /// The AI profiler. Wrap a piece of AI work in <c>using (AiProfile.Scope(AiSystem.X)) { ... }</c> (or
    /// <c>using var _ = AiProfile.Scope(...)</c> at the top of a method). Each scope adds one call, its total time and its
    /// self time (total less the scopes nested in it) to its system, in the calling thread's own counters. Once a minute
    /// the game loop adds every thread's counters together and writes one AI_PROFILE line (see
    /// <see cref="AiProfileMinute"/>). Switched by the server property <c>ai_profile</c> (on by default); off, a scope is
    /// one bool read.
    /// </summary>
    public static class AiProfile
    {
        /// <summary>How many systems fit: the built-in ones plus names registered by <see cref="Scope(string)"/>.</summary>
        public const int MaxSystems = 128;

        // A thread's slot: Padding unused longs, then Fields longs per system, then Padding again, so two threads' slots
        // never share a cache line (lometur's layout).
        public const int Fields = 4;
        public const int CallsField = 0;
        public const int TotalField = 1;
        public const int SelfField = 2;
        public const int MaxField = 3;
        public const int Padding = 8;
        public const int SlotLength = Padding + MaxSystems * Fields + Padding;

        /// <summary>How often the report is written.</summary>
        public const int ReportSeconds = 60;

        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// The switch. A plain static bool that every scope reads; the game loop copies the <c>ai_profile</c> server
        /// property into it at the end of each tick (<see cref="EndTick"/>). Tests may set it directly.
        /// </summary>
        public static bool Enabled;

        // This thread's counters, written only by this thread with plain adds. Null until its first scope.
        [ThreadStatic] private static long[] _slot;

        // The time of the scopes that ended inside the scope now open on this thread (its "children"). A scope saves the
        // value on entry, starts it at zero, and on exit takes its own self time as total minus children, then adds its
        // total to the saved value for its parent.
        [ThreadStatic] private static long _childTicks;

        private sealed class SlotOwner
        {
            public readonly Thread Thread;
            public readonly long[] Slot;
            public SlotOwner(Thread thread, long[] slot) { Thread = thread; Slot = slot; }
        }

        // Every thread that ever timed a scope, under SlotsLock. A thread that has ended is folded into Retired at the next
        // merge, so the list does not grow with thread-pool churn and the totals never go backwards.
        private static readonly List<SlotOwner> Slots = new();
        private static readonly Lock SlotsLock = new();
        private static readonly long[] Retired = new long[SlotLength];

        // ---- The hot path ----------------------------------------------------------------------------------------------

        /// <summary>A timed piece of work. A default value (made while the profiler was off) does nothing.</summary>
        public readonly struct ProfileScope : IDisposable
        {
            private readonly long _start;
            private readonly long _savedChildTicks;
            private readonly int _system;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ProfileScope(int system)
            {
                _system = system;
                _savedChildTicks = _childTicks;
                _childTicks = 0;
                _start = Stopwatch.GetTimestamp();
            }

            /// <summary>True when this scope is being timed.</summary>
            public bool IsActive => _start != 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                if (_start != 0)
                    End(_system, _start, _savedChildTicks);
            }
        }

        /// <summary>Times a built-in system until the scope is disposed.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProfileScope Scope(AiSystem system) => Enabled ? new ProfileScope((int)system) : default;

        /// <summary>Times a built-in system only when <paramref name="condition"/> holds (for example only the outermost
        /// call of a recursive planner).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProfileScope ScopeIf(bool condition, AiSystem system) =>
            condition && Enabled ? new ProfileScope((int)system) : default;

        /// <summary>
        /// Times any named system: a built-in dotted name (see <see cref="AiSystemNames"/>) or a new one, which gets a free
        /// slot the first time it is seen. Costs one dictionary lookup more than the <see cref="AiSystem"/> overload, so
        /// prefer that one on hot paths.
        /// </summary>
        public static ProfileScope Scope(string system) => Enabled ? new ProfileScope(AiSystemNames.Resolve(system)) : default;

        // The end of a scope: its total and self time, then four plain adds to this thread's slot.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void End(int system, long start, long savedChildTicks)
        {
            long total = Stopwatch.GetTimestamp() - start;
            long self = total - _childTicks;
            _childTicks = savedChildTicks + total;
            Add(system, total, self < 0 ? 0 : self);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Add(int system, long total, long self)
        {
            long[] slot = _slot ?? RegisterSlot();
            int at = Padding + system * Fields;
            slot[at + CallsField]++;
            slot[at + TotalField] += total;
            slot[at + SelfField] += self;
            if (total > slot[at + MaxField])
                slot[at + MaxField] = total;
        }

        /// <summary>
        /// Adds a duration that was measured already (Stopwatch ticks) as one call of <paramref name="system"/> with no
        /// scopes inside it, and takes it out of the open scope's self time. Used for the native navmesh queries and the
        /// lock waits.
        /// </summary>
        public static void AddLeaf(AiSystem system, long elapsedTicks)
        {
            if (!Enabled || elapsedTicks < 0)
                return;
            _childTicks += elapsedTicks;
            Add((int)system, elapsedTicks, elapsedTicks);
        }

        /// <summary>A timestamp to pass to <see cref="AddLeafSince"/>; 0 while off.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Timestamp() => Enabled ? Stopwatch.GetTimestamp() : 0;

        /// <summary>Adds the time since <paramref name="started"/> (from <see cref="Timestamp"/>) as a leaf call.</summary>
        public static void AddLeafSince(AiSystem system, long started)
        {
            if (started != 0)
                AddLeaf(system, Stopwatch.GetTimestamp() - started);
        }

        // A thread's first scope: its slot, added to the list under the lock, once per thread.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long[] RegisterSlot()
        {
            long[] slot = new long[SlotLength];
            lock (SlotsLock)
                Slots.Add(new SlotOwner(Thread.CurrentThread, slot));
            _slot = slot;
            return slot;
        }

        /// <summary>The system a brain turn is charged to, by the kind of its body (lometur's kinds, simplified): an
        /// autonomous gamebot, any other bot (companions and helpers), a pet, or any other NPC.</summary>
        public static AiSystem ThinkKind(ABrain brain, GameNPC body) => body switch
        {
            // First: a GameBot's BotBrain is itself an IControlledBrain.
            GameBot bot => bot.IsAutonomousWorldBot ? AiSystem.ThinkGamebot : AiSystem.ThinkCompanion,
            _ when brain is IControlledBrain => AiSystem.ThinkPet,
            _ => AiSystem.ThinkNpc,
        };

        // ---- Measured locks (stefanrows's MeasuredLock) ----------------------------------------------------------------

        /// <summary>
        /// A lock taken by <see cref="Lock(object, AiSystem)"/>; disposing it releases the lock. A ref struct (review of
        /// PR #67), so like <c>lock</c> it cannot be held across an <c>await</c> (the compiler refuses, as it does with
        /// CS1996 for <c>lock</c>), and it cannot be boxed, stored in a field or captured by a lambda. A default value
        /// releases nothing.
        /// </summary>
        public readonly ref struct HeldLock
        {
            private readonly object _gate;
            internal HeldLock(object gate) => _gate = gate;

            public void Dispose()
            {
                if (_gate != null)
                    Monitor.Exit(_gate);
            }
        }

        /// <summary>
        /// Refused at build time: a <see cref="System.Threading.Lock"/> is not a monitor, and passing one here as an
        /// object would lock its object header instead of the lock itself (what <c>lock (gate)</c> on that type does is
        /// <c>gate.EnterScope()</c>). Use <c>lock (gate)</c> for that type.
        /// </summary>
        [Obsolete("A System.Threading.Lock is not a monitor: use lock (gate) for it, not AiProfile.Lock.", error: true)]
        public static HeldLock Lock(System.Threading.Lock gate, AiSystem waitSystem) =>
            throw new NotSupportedException("AiProfile.Lock takes a monitor object, not a System.Threading.Lock.");

        /// <summary>
        /// Takes the same monitor as <c>lock (gate)</c>, with the same reentrancy, and adds any time spent waiting for it
        /// to <paramref name="waitSystem"/>. An uncontended enter is one <see cref="Monitor.TryEnter(object)"/> and no
        /// timestamps. Use as <c>using (AiProfile.Lock(gate, AiSystem.LockX)) { ... }</c> in place of
        /// <c>lock (gate) { ... }</c>.
        /// </summary>
        public static HeldLock Lock(object gate, AiSystem waitSystem)
        {
            if (!Monitor.TryEnter(gate))
            {
                if (Enabled)
                {
                    long started = Stopwatch.GetTimestamp();
                    Monitor.Enter(gate);
                    try { AddLeaf(waitSystem, Stopwatch.GetTimestamp() - started); }
                    catch (Exception) { /* the profiler must never leave a lock held */ }
                }
                else
                    Monitor.Enter(gate);
            }
            return new HeldLock(gate);
        }

        // ---- The tick (game-loop thread only) --------------------------------------------------------------------------

        private static long _tickStart;
        private static long _tickSerialTicks;
        private static long _tickParallelTicks;
        private static long _nextReport;
        private static readonly AiProfileTicks Ticks = new(AiProfileTicks.DefaultCapacity);

        /// <summary>The last finished minute, for <c>/lsprofile</c>; null until the first one.</summary>
        public static AiProfileMinute LastMinute { get; private set; }

        /// <summary>The length of the last game-loop tick in ms (0 while off). The planned deferral queue reads this.</summary>
        public static double LastTickMs { get; private set; }

        /// <summary>The serial planning time of the last tick in ms (0 while off).</summary>
        public static double LastSerialMs { get; private set; }

        /// <summary>
        /// The seam for the AI budget (audit §8.4 step 2), not used yet: the ms this tick has run so far, on the game-loop
        /// thread. A deferral queue in the serial planning would compare it with <c>ai_budget_ms</c> through
        /// <see cref="AutonomousAiBudget.MaximumDecisionsThisTick"/>. 0 while off.
        /// </summary>
        public static double ElapsedThisTickMs() =>
            _tickStart == 0 ? 0 : Stopwatch.GetElapsedTime(_tickStart).TotalMilliseconds;

        /// <summary>Called by GameLoop before the services of a tick run.</summary>
        public static void BeginTick()
        {
            _tickSerialTicks = 0;
            _tickParallelTicks = 0;
            _tickStart = Enabled ? Stopwatch.GetTimestamp() : 0;
        }

        /// <summary>NpcService: the serial planning that started at <paramref name="started"/> (a <see cref="Timestamp"/>)
        /// has ended.</summary>
        public static void EndSerial(long started)
        {
            if (started != 0)
                _tickSerialTicks += Stopwatch.GetTimestamp() - started;
        }

        /// <summary>NpcService: the parallel brain pass that started at <paramref name="started"/> has ended.</summary>
        public static void EndParallel(long started)
        {
            if (started != 0)
                _tickParallelTicks += Stopwatch.GetTimestamp() - started;
        }

        /// <summary>
        /// Called by GameLoop after the services of a tick ran: records the tick, follows the <c>ai_profile</c> switch,
        /// and once a minute merges every thread's counters and writes the report. Never throws into the game loop.
        /// </summary>
        public static void EndTick()
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                if (_tickStart != 0 && Enabled)
                {
                    double tick = ToMilliseconds(now - _tickStart);
                    double serial = ToMilliseconds(_tickSerialTicks);
                    Ticks.Add(tick, serial, ToMilliseconds(_tickParallelTicks));
                    LastTickMs = tick;
                    LastSerialMs = serial;
                }
                _tickStart = 0;

                bool wanted = AutonomousPopulationProperties.AI_PROFILE;
                if (wanted != Enabled)
                {
                    Volatile.Write(ref Enabled, wanted);
                    if (wanted)
                        StartWindow(now);
                    else
                    {
                        LastTickMs = LastSerialMs = 0;
                        Ticks.Clear();
                    }
                    return;
                }

                if (!Enabled)
                    return;
                if (_nextReport == 0)
                {
                    StartWindow(now);
                    return;
                }
                if (now < _nextReport)
                    return;

                AiProfileMinute minute = TakeMinute(now, DateTime.UtcNow);
                _nextReport = now + ReportSeconds * Stopwatch.Frequency;
                Report(minute);
            }
            catch (Exception exception)
            {
                // Measurement must never stop the game loop. Try again in a minute.
                _nextReport = Stopwatch.GetTimestamp() + ReportSeconds * Stopwatch.Frequency;
                try { Log.Warn("AI_PROFILE could not be written", exception); } catch (Exception) { }
            }
        }

        // Starts a fresh window: the totals so far become the baseline and the tick samples are cleared.
        private static void StartWindow(long now)
        {
            Merger.Baseline();
            Ticks.Clear();
            _windowStart = now;
            _nextReport = now + ReportSeconds * Stopwatch.Frequency;
        }

        private static long _windowStart;
        private static readonly AiProfileMerger Merger = new();

        /// <summary>
        /// Ends the current window: the difference of every system's totals since the last call, the tick percentiles of
        /// the window, and the start of the next window. Called once a minute on the game loop; tests call it directly.
        /// </summary>
        public static AiProfileMinute TakeMinute(long nowTimestamp, DateTime utc)
        {
            AiProfileSystemStat[] systems = Merger.TakeWindow();
            double seconds = _windowStart == 0 ? ReportSeconds : ToMilliseconds(nowTimestamp - _windowStart) / 1000.0;
            AiProfileMinute minute = new(utc, seconds, Ticks.Summarize(), systems);
            Ticks.Clear();
            _windowStart = nowTimestamp;
            LastMinute = minute;
            return minute;
        }

        private static void Report(AiProfileMinute minute)
        {
            if (Log.IsInfoEnabled)
                Log.Info(minute.SummaryLine());
            // The audit's serial budget (§9) is only reported, in warn.log too, never enforced.
            string over = minute.OverBudget();
            if (over.Length > 0 && Log.IsWarnEnabled)
                Log.Warn($"AI_PROFILE_BUDGET over={over} (serial planning budget: {AiProfileMinute.SerialBudgetMs} ms a tick, {AiProfileMinute.PulseBudgetMs} ms a pulse; measured only)");
            AiProfileCsv.Append(minute);
        }

        public static double ToMilliseconds(long stopwatchTicks) => stopwatchTicks * 1000.0 / Stopwatch.Frequency;

        // ---- Reading the slots (the merger) ----------------------------------------------------------------------------

        /// <summary>
        /// Copies every live thread's slot (and the folded totals of ended threads) into <paramref name="sum"/>, taking the
        /// largest per-call maximum across threads and setting each thread's maximum back to zero. Reads the other threads'
        /// longs with <see cref="Volatile.Read(ref long)"/>; aligned 64-bit reads are whole on the 64-bit server. A maximum
        /// written by its thread between the read and the reset is lost for that minute, which only makes one max a little
        /// low.
        /// </summary>
        internal static void SumSlots(long[] sum)
        {
            Array.Clear(sum);
            lock (SlotsLock)
            {
                for (int i = Slots.Count - 1; i >= 0; i--)
                {
                    SlotOwner owner = Slots[i];
                    if (owner.Thread.IsAlive)
                        continue;
                    // An ended thread writes no more: fold its totals into Retired once and forget it.
                    for (int at = Padding; at < SlotLength - Padding; at += Fields)
                    {
                        Retired[at + CallsField] += owner.Slot[at + CallsField];
                        Retired[at + TotalField] += owner.Slot[at + TotalField];
                        Retired[at + SelfField] += owner.Slot[at + SelfField];
                        Retired[at + MaxField] = Math.Max(Retired[at + MaxField], owner.Slot[at + MaxField]);
                    }
                    Slots.RemoveAt(i);
                }

                AddSlot(sum, Retired);
                foreach (SlotOwner owner in Slots)
                    AddSlot(sum, owner.Slot);
            }
        }

        // Adds one slot's totals to the sum and takes (and clears) its per-call maximum.
        private static void AddSlot(long[] sum, long[] slot)
        {
            for (int at = Padding; at < SlotLength - Padding; at += Fields)
            {
                sum[at + CallsField] += Volatile.Read(ref slot[at + CallsField]);
                sum[at + TotalField] += Volatile.Read(ref slot[at + TotalField]);
                sum[at + SelfField] += Volatile.Read(ref slot[at + SelfField]);
                long max = Interlocked.Exchange(ref slot[at + MaxField], 0);
                if (max > sum[at + MaxField])
                    sum[at + MaxField] = max;
            }
        }

        /// <summary>The number of threads with a live slot (tests).</summary>
        public static int ThreadSlotCount
        {
            get { lock (SlotsLock) return Slots.Count; }
        }
    }

    /// <summary>
    /// Turns the cumulative per-thread totals into per-window numbers: a window is the difference between two sums, so
    /// no thread's counter is ever reset (except the per-call maximum).
    /// </summary>
    public sealed class AiProfileMerger
    {
        private long[] _previous = new long[AiProfile.SlotLength];
        private long[] _current = new long[AiProfile.SlotLength];

        /// <summary>Makes the totals so far the baseline, so the next window counts only what comes after.</summary>
        public void Baseline()
        {
            AiProfile.SumSlots(_previous);
        }

        /// <summary>The systems that ran in the window since the last call (or baseline), biggest self time first.</summary>
        public AiProfileSystemStat[] TakeWindow()
        {
            AiProfile.SumSlots(_current);
            AiProfileSystemStat[] stats = Difference(_previous, _current);
            (_previous, _current) = (_current, _previous);
            return stats;
        }

        /// <summary>The per-system difference of two sums laid out as a slot. Public for the tests.</summary>
        public static AiProfileSystemStat[] Difference(long[] before, long[] after)
        {
            var stats = new List<AiProfileSystemStat>();
            for (int system = 0; system < AiProfile.MaxSystems; system++)
            {
                int at = AiProfile.Padding + system * AiProfile.Fields;
                long calls = after[at + AiProfile.CallsField] - before[at + AiProfile.CallsField];
                if (calls <= 0)
                    continue;
                long total = after[at + AiProfile.TotalField] - before[at + AiProfile.TotalField];
                long self = after[at + AiProfile.SelfField] - before[at + AiProfile.SelfField];
                stats.Add(new AiProfileSystemStat(AiSystemNames.Of(system) ?? $"system{system}", system, calls,
                    AiProfile.ToMilliseconds(total), AiProfile.ToMilliseconds(self),
                    AiProfile.ToMilliseconds(after[at + AiProfile.MaxField])));
            }
            stats.Sort((a, b) => b.SelfMs.CompareTo(a.SelfMs));
            return stats.ToArray();
        }
    }
}
