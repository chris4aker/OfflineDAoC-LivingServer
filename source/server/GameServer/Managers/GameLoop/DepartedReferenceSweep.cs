using System;
using System.Diagnostics;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Every few minutes, each living in the world forgets the objects that have left it (<see cref="GameLiving.ForgetDeparted"/>).
    /// Live run 2026-10-22: 2,424 scheduled bot logouts left 1,500 deleted bots in memory, held by monsters' last interrupter
    /// and remembered attack targets, and Windows Event handles grew with them (about 1,000 an hour). Runs on the game loop
    /// thread between service stages (from the minute report), so no service is touching these fields at the same time.
    /// </summary>
    public static class DepartedReferenceSweep
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const int IntervalMinutes = 2;
        public const int ReportMinutes = 30;
        private static int _minutes;
        private static long _livings, _failures, _ticks;

        /// <summary>Called once a minute from the game loop's work report.</summary>
        public static void Minute()
        {
            _minutes++;
            if (_minutes % IntervalMinutes == 0)
                Sweep();
            if (_minutes % ReportMinutes == 0)
            {
                Log.Info($"SERVER_DEPARTED_SWEEP sweeps={ReportMinutes / IntervalMinutes} livings={_livings} failures={_failures} " +
                         $"totalMs={_ticks * 1000 / Stopwatch.Frequency}");
                _livings = _failures = _ticks = 0;
            }
        }

        private static void Sweep()
        {
            using var aiProfile = AiProfile.Scope(AiSystem.SweepDepartedReferences); // AI_PROFILE: measures only
            long started = Stopwatch.GetTimestamp();
            foreach (Region region in WorldMgr.GetAllRegions())
            {
                GameObject[] objects = region?.Objects;
                if (objects == null) continue;
                for (int i = 0; i < objects.Length; i++)
                {
                    if (objects[i] is not GameLiving living || living.ObjectState != GameObject.eObjectState.Active) continue;
                    try
                    {
                        living.ForgetDeparted();
                        _livings++;
                    }
                    catch (Exception exception)
                    {
                        if (_failures++ == 0) Log.Warn($"SERVER_DEPARTED_SWEEP failed for {living.Name}", exception);
                    }
                }
            }
            _ticks += Stopwatch.GetTimestamp() - started;
        }
    }
}
