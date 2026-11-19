using System;

namespace DOL.GS
{
    /// <summary>
    /// A required-adds phase that makes no progress (Living Server 1.2, sweep 6d; information only: nothing here changes a
    /// fight or the raid). Owner's PC, build b1b6846, 2026-11-18 (Galladoria): from 11:59 to the sweep cap at 12:47 the raid
    /// fought the Earth primal's four Earthmenders, dealt them 1.44M (six times their combined 240,000 health) and killed
    /// none; every TARGET_STATUS read 99-100%, and nothing said so until the 6-hour cap. The route feeds this the raid's
    /// damage on the live required adds once a minute (from EpicFightLedger); when no add has died for
    /// <see cref="Minutes"/> minutes while that damage exceeds their combined health, it logs REALM_EXPEDITION_NO_PROGRESS
    /// once for the phase. A phase starts again when the boss changes or an add dies (fewer live adds).
    /// </summary>
    public sealed class RealmRaidNoProgress
    {
        /// <summary>Minutes with no add death before the line.</summary>
        public const int Minutes = 20;

        private string _owner;
        private int _adds;
        private long _since;
        private long _dealt;
        private bool _logged;

        public string Owner => _owner;
        public long Since => _since;
        public long Dealt => _dealt;

        /// <summary>
        /// One minute of the phase: <paramref name="owner"/>'s <paramref name="liveAdds"/> live required adds, their combined
        /// <paramref name="combinedHealth"/> (maximum health) and what the raid dealt them this minute. True once per phase,
        /// when the line is due. A null owner or no live adds ends the phase.
        /// </summary>
        public bool Observe(string owner, int liveAdds, long combinedHealth, long dealtThisMinute, long now)
        {
            if (owner == null || liveAdds <= 0)
            {
                _owner = null;
                return false;
            }
            if (owner != _owner || liveAdds < _adds)
            {
                _owner = owner;
                _since = now;
                _dealt = 0;
                _logged = false;
            }
            _adds = liveAdds;
            _dealt += Math.Max(0, dealtThisMinute);
            if (_logged || !Stalled(now - _since, _dealt, combinedHealth)) return false;
            _logged = true;
            return true;
        }

        /// <summary>No progress (pure): <see cref="Minutes"/> or more with no add death while the raid dealt the adds more
        /// than their combined health.</summary>
        public static bool Stalled(long sinceMilliseconds, long dealt, long combinedHealth) =>
            sinceMilliseconds >= Minutes * 60_000L && combinedHealth > 0 && dealt > combinedHealth;

        /// <summary>The REALM_EXPEDITION_NO_PROGRESS line (pure).</summary>
        public static string Line(ushort region, string owner, string target, int adds, long combinedHealth, long dealt, long minutes) =>
            $"REALM_EXPEDITION_NO_PROGRESS region={region} boss=\"{owner}\" target=\"{target}\" adds={adds} health={combinedHealth} " +
            $"dealt={dealt} minutes={minutes} reason=\"no required add has died for {minutes} minutes while the raid dealt them " +
            $"more than their combined health; information only, nothing changes\"";
    }
}
