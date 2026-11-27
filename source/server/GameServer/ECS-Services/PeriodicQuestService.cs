using System;
using System.Collections.Generic;
using System.Reflection;
using DOL.GS.Quests;
using DOL.Logging;
using static DOL.GS.RolloverSchedulerService;

namespace DOL.GS
{
    /// <summary>
    /// Daily, weekly and monthly quests become available again at each rollover. Port of OpenDAoC-Core e688e984e with
    /// 7df3fb4af, 1b512ad2a, 5ad7dfb9c, ebc6a16d6 and 7384fed04; replaces DailyQuestService, WeeklyQuestService and
    /// MonthlyQuestService. Players online at the rollover get their finished quests cleared then; a player who was
    /// offline gets it at login when a rollover passed since they last played (in place of the old unindexed
    /// "Name LIKE '%DailyQuest%'" delete over every character). Players only: GameBots and companions hold no quests.
    /// </summary>
    public static class PeriodicQuestService
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        // The quest kinds and their intervals (supports up to 32). Known without the database, so the rules below are
        // pure; Initialize only subscribes them to the scheduler.
        private static readonly List<QuestRegistration> _registrations = new()
        {
            Registration<Quests.DailyQuest>(IntervalKey.Daily, 0),
            Registration<Quests.WeeklyQuest>(IntervalKey.Weekly, 1),
            Registration<Quests.MonthlyQuest>(IntervalKey.Monthly, 2),
        };

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            RolloverSchedulerService.Instance.Subscribe(IntervalKey.Daily, ResetQuests<Quests.DailyQuest>);
            RolloverSchedulerService.Instance.Subscribe(IntervalKey.Weekly, ResetQuests<Quests.WeeklyQuest>);
            RolloverSchedulerService.Instance.Subscribe(IntervalKey.Monthly, ResetQuests<Quests.MonthlyQuest>);
        }

        /// <summary>
        /// Called from GamePlayer.AddToWorld, which runs at login and again at every region change. Our fix of a flaw
        /// upstream has too: PreviousLoginDate stays the same for the whole session, so after any midnight every zone
        /// change cleared the dailies finished since, and they could be done again on demand. The player now remembers
        /// when the periodic quests were last checked (here or at a rollover while online), and only a rollover after
        /// both that and the last login counts.
        /// </summary>
        public static void OnPlayerJoin(GamePlayer player)
        {
            if (player == null || !_initialized)
                return;

            DateTime checkedAt = player.PeriodicQuestsCheckedAt;
            uint triggeredMask = CheckOnEntry(ref checkedAt, player.PreviousLoginDate, DateTime.Now, RolloverSchedulerService.Instance.GetLastRollover);
            player.PeriodicQuestsCheckedAt = checkedAt;

            if (triggeredMask == 0)
                return;

            player.RemoveFinishedQuests(static (quest, mask) => Matches(quest, mask), triggeredMask);
        }

        /// <summary>
        /// The quest kinds to reset when the player enters the world, and the new "checked at" time (pure): a kind is
        /// reset when its interval rolled over after both the last login and the last check.
        /// </summary>
        public static uint CheckOnEntry(ref DateTime checkedAt, DateTime previousLogin, DateTime now, Func<IntervalKey, DateTime> lastRollover)
        {
            DateTime since = checkedAt > previousLogin ? checkedAt : previousLogin;
            checkedAt = now;
            return TriggeredMask(since, lastRollover);
        }

        /// <summary>The intervals that rolled over after <paramref name="since"/>, one bit per quest kind (pure).</summary>
        public static uint TriggeredMask(DateTime since, Func<IntervalKey, DateTime> lastRollover)
        {
            uint triggeredMask = 0;

            for (int i = 0; i < _registrations.Count; i++)
            {
                QuestRegistration reg = _registrations[i];

                if (since < lastRollover(reg.Interval))
                    triggeredMask |= reg.BitMask;
            }

            return triggeredMask;
        }

        /// <summary>The bit of one interval's quest kind in a mask.</summary>
        public static uint MaskOf(IntervalKey interval)
        {
            foreach (QuestRegistration reg in _registrations)
            {
                if (reg.Interval == interval)
                    return reg.BitMask;
            }

            return 0;
        }

        /// <summary>Whether a finished quest is one of the kinds in the mask.</summary>
        public static bool Matches(AbstractQuest quest, uint mask)
        {
            for (int i = 0; i < _registrations.Count; i++)
            {
                QuestRegistration reg = _registrations[i];

                if ((mask & reg.BitMask) != 0 && reg.IsMatch(quest))
                    return true;
            }

            return false;
        }

        private static QuestRegistration Registration<T>(IntervalKey intervalKey, int bit) =>
            new(intervalKey, 1u << bit, static quest => quest is T);

        private static void ResetQuests<T>()
        {
            List<GamePlayer> players = ClientService.Instance.GetPlayers();
            GameLoop.ExecuteForEach(players, players.Count, ResetPlayerQuests<T>);
        }

        private static void ResetPlayerQuests<T>(GamePlayer player)
        {
            try
            {
                // Checked now: a zone change after this rollover doesn't reset the quests finished since again.
                player.PeriodicQuestsCheckedAt = DateTime.Now;
                player.RemoveFinishedQuests(static quest => quest is T);
                QuestIndicators.Refresh(player); // the givers show their knot again
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error($"{nameof(ResetPlayerQuests)}<{typeof(T).Name}> failed (Player: {player})", e);
            }
        }

        private record struct QuestRegistration(IntervalKey Interval, uint BitMask, Func<AbstractQuest, bool> IsMatch) { }
    }
}
