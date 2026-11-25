using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    public sealed class GameLoopTickPacer
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        private const bool DYNAMIC_BUSY_WAIT_THRESHOLD = true;

        private readonly double _tickDuration;
        private readonly long _tickDurationTicks;
        private bool _running;
        private Thread _busyWaitThresholdThread;
        private int _busyWaitThreshold;
        private long _busyWaitThresholdTicks;

        private double _gameLoopTime;
        private long _nextTickDeadlineTicks;
        private Stopwatch _stopwatch;

        public GameLoopTickPacerStats Stats { get; private set; }

        public GameLoopTickPacer(double tickDuration)
        {
            if (tickDuration <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickDuration), "Tick duration must be a positive value.");

            _tickDuration = tickDuration;
            _tickDurationTicks = (long) (tickDuration * Stopwatch.Frequency / 1000.0);
        }

        public void Start()
        {
            if (_running)
                return;

            _running = true;

            if (DYNAMIC_BUSY_WAIT_THRESHOLD && Environment.ProcessorCount > 1)
            {
                _busyWaitThresholdThread = new(new ThreadStart(UpdateBusyWaitThreshold))
                {
                    Name = $"{GameLoop.THREAD_NAME}_BusyWaitThreshold",
                    Priority = ThreadPriority.AboveNormal,
                    IsBackground = true
                };
                _busyWaitThresholdThread.Start();
            }

            Stats = new([60000, 30000, 10000], 1000.0 / _tickDuration);
            _stopwatch = Stopwatch.StartNew();
            _nextTickDeadlineTicks = _tickDurationTicks;
        }

        public void Stop()
        {
            // Port of OpenDAoC-Core 7b31c6910: clear the flag even when no calibration thread was started.
            _running = false;

            if (_busyWaitThresholdThread == null)
                return;

            if (_busyWaitThresholdThread != Thread.CurrentThread && _busyWaitThresholdThread.IsAlive)
                _busyWaitThresholdThread.Interrupt(); // This thread sleeps for a long time, let's not wait for it to finish.

            _busyWaitThresholdThread = null;
        }

        /// <summary>
        /// Port of OpenDAoC-Core c955cc64c: ticks are paced against an absolute deadline. Before, the stopwatch was restarted
        /// every tick, so each tick's small overshoot (scheduler jitter) was thrown away and timers, buffs and respawns slowly
        /// fell behind the wall clock over a long session. Catch-up stays capped at one tick: a tick that runs more than a
        /// whole tick late resets the deadline instead of bursting through the missed ones.
        /// </summary>
        public long WaitForNextTick()
        {
            long ticksUntilDeadline = _nextTickDeadlineTicks - _stopwatch.ElapsedTicks;
            int busyWaitThreshold = _busyWaitThreshold;

            // Only sleep if enough time remains that the OS sleep overshoot won't carry us past the deadline.
            if (ticksUntilDeadline >= Volatile.Read(ref _busyWaitThresholdTicks))
            {
                int sleepFor = (int) (ticksUntilDeadline * 1000.0 / Stopwatch.Frequency) - busyWaitThreshold;

                if (sleepFor > 0)
                    Thread.Sleep(sleepFor);
            }

            while (_stopwatch.ElapsedTicks < _nextTickDeadlineTicks)
                Thread.SpinWait(10);

            long currentTicks = _stopwatch.ElapsedTicks;

            // Drop the missed ticks if this one ran more than a whole tick late.
            _nextTickDeadlineTicks = NextDeadline(_nextTickDeadlineTicks, currentTicks, _tickDurationTicks);

            // Game time advances by exactly one tick: things never run faster than intended.
            _gameLoopTime += _tickDuration;
            Stats.RecordTick(currentTicks * 1000.0 / Stopwatch.Frequency);
            return (long) _gameLoopTime;
        }

        /// <summary>The next deadline after a tick that finished at <paramref name="currentTicks"/> (for tests).</summary>
        public static long NextDeadline(long deadlineTicks, long currentTicks, long tickDurationTicks) =>
            currentTicks - deadlineTicks >= tickDurationTicks ? currentTicks + tickDurationTicks : deadlineTicks + tickDurationTicks;

        private void UpdateBusyWaitThreshold()
        {
            int maxIteration = 10;
            int sleepFor = 1;
            int pauseFor = 10000;
            Stopwatch stopwatch = new();
            stopwatch.Start();

            try
            {
                while (Volatile.Read(ref _running))
                {
                    double start;
                    double overSleptFor;
                    double highest = 0;

                    for (int i = 0; i < maxIteration; i++)
                    {
                        start = stopwatch.Elapsed.TotalMilliseconds;
                        Thread.Sleep(sleepFor);
                        overSleptFor = stopwatch.Elapsed.TotalMilliseconds - start - sleepFor;

                        if (highest < overSleptFor)
                            highest = overSleptFor;
                    }

                    _busyWaitThreshold = (int) Math.Max(0, Math.Ceiling(highest));
                    Volatile.Write(ref _busyWaitThresholdTicks, (long) (_busyWaitThreshold * Stopwatch.Frequency / 1000.0));
                    Thread.Sleep(pauseFor);
                }
            }
            catch (ThreadInterruptedException)
            {
                if (log.IsWarnEnabled)
                    log.Warn($"Thread \"{Thread.CurrentThread.Name}\" was interrupted");
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error($"Critical error encountered in {nameof(GameLoopTickPacer)}: {e}");
            }

            if (log.IsInfoEnabled)
                log.Info($"Thread \"{Thread.CurrentThread.Name}\" is stopping");
        }
    }
}
