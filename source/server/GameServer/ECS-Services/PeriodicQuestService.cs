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
        private static readonly List<QuestRegistration> _registrations = new();

        public static void Initialize()
        {
            if (_registrations.Count > 0)
                return;

            RegisterQuest<Quests.DailyQuest>(IntervalKey.Daily);
            RegisterQuest<Quests.WeeklyQuest>(IntervalKey.Weekly);
            RegisterQuest<Quests.MonthlyQuest>(IntervalKey.Monthly);
        }

        public static void OnPlayerJoin(GamePlayer player)
        {
            if (player == null || _registrations.Count == 0)
                return;

            uint triggeredMask = TriggeredMask(player.PreviousLoginDate, RolloverSchedulerService.Instance.GetLastRollover);

            if (triggeredMask == 0)
                return;

            player.RemoveFinishedQuests(static (quest, mask) => Matches(quest, mask), triggeredMask);
        }

        /// <summary>The intervals that rolled over after the player last played, one bit per registration (pure).</summary>
        public static uint TriggeredMask(DateTime previousLogin, Func<IntervalKey, DateTime> lastRollover)
        {
            uint triggeredMask = 0;

            for (int i = 0; i < _registrations.Count; i++)
            {
                QuestRegistration reg = _registrations[i];

                if (previousLogin < lastRollover(reg.Interval))
                    triggeredMask |= reg.BitMask;
            }

            return triggeredMask;
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

        private static void RegisterQuest<T>(IntervalKey intervalKey)
        {
            // Supports up to 32 different intervals.
            RolloverSchedulerService.Instance.Subscribe(intervalKey, ResetQuests<T>);
            _registrations.Add(new(intervalKey, 1u << _registrations.Count, static quest => quest is T));
        }

        private static void ResetQuests<T>()
        {
            List<GamePlayer> players = ClientService.Instance.GetPlayers();
            GameLoop.ExecuteForEach(players, players.Count, ResetPlayerQuests<T>);
        }

        private static void ResetPlayerQuests<T>(GamePlayer player)
        {
            try
            {
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
