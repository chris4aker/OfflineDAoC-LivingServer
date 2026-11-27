using System;
using DOL.Database.Attributes;

namespace DOL.Database
{
    /// <summary>
    /// When each RolloverSchedulerService interval (hourly, daily, weekly, monthly) last rolled over. Port of
    /// OpenDAoC-Core e688e984e (replaces DbTaskRefreshInterval, which is kept only to seed this table once). Server run
    /// state, not player progress: the release world ships it empty and the server makes its rows at start.
    /// </summary>
    [DataTable(TableName = "ScheduledRollovers")]
    public class DbScheduledRollover : DataObject
    {
        private int _rolloverIntervalKey;
        private DateTime _lastRollover;

        [DataElement(AllowDbNull = false)]
        public DateTime LastRollover
        {
            get => _lastRollover;
            set
            {
                Dirty = true;
                _lastRollover = value;
            }
        }

        [DataElement(AllowDbNull = false, Unique = true)]
        public int RolloverIntervalKey
        {
            get => _rolloverIntervalKey;
            set
            {
                Dirty = true;
                _rolloverIntervalKey = value;
            }
        }
    }
}
