using System;
using System.Collections.Generic;

namespace DOL.GS.Quests
{
    /// <summary>
    /// The quest journal's rules for GamePlayer.AddQuest and the quest starts. Upstream's AddQuest began
    /// "if (QuestList.Count > 25)" with no body, so the journal had no limit and the duplicate check after it ran only
    /// above 25 quests (and compared C# types, which every data quest shares). From lometur/HearthDAoC.
    /// </summary>
    public static class QuestJournal
    {
        /// <summary>The client's journal shows 25 quests (PacketLib168.JOURNAL_MAX_QUEST_COUNT).</summary>
        public const int Capacity = 25;

        public const string FullText = "Your quest journal is full. Finish or abandon a quest first.";

        public static bool IsFull(int questCount) => questCount >= Capacity;

        /// <summary>The same quest: data quests by ID, other quests by class (one class per scripted quest).</summary>
        public static bool Same(AbstractQuest a, AbstractQuest b) =>
            a is DataQuest x && b is DataQuest y ? x.ID == y.ID : a.GetType() == b.GetType();

        /// <summary>
        /// Active quests past the capacity. Owner 2026-10-30: the limit is checked when a quest is taken, so an older
        /// save can hold more than 25 active quests. None is aborted: they stay active (they still hear kills, hand-ins
        /// and whispers) and the journal lists them as slots free (PacketLib168.SendQuestListUpdate); the quests loaded
        /// first keep the slots they had.
        /// </summary>
        public static int WaitingCount(int questCount) => Math.Max(0, questCount - Capacity);

        /// <summary>The one line said at login when quests wait for a journal slot, or null when none does.</summary>
        public static string WaitingText(int questCount)
        {
            int waiting = WaitingCount(questCount);
            if (waiting == 0)
                return null;

            return $"Your journal shows {Capacity} of your {questCount} quests; the other {waiting} " +
                $"{(waiting == 1 ? "is" : "are")} still active and will be listed as you finish or abandon some.";
        }

        /// <summary>
        /// The lowest journal slot no quest holds. A freed slot queued in GamePlayer.AvailableQuestIndexes can have been
        /// taken since by a quest that moved in from past the capacity (SendQuestListUpdate), so AddQuest gives a slot
        /// only while no quest holds it (owner 2026-10-30; two quests on one slot showed as one).
        /// </summary>
        public static byte FreeIndex(IEnumerable<byte> usedIndexes)
        {
            var used = new HashSet<byte>(usedIndexes ?? Array.Empty<byte>());
            byte index = 0;
            while (index < byte.MaxValue && used.Contains(index))
                index++;
            return index;
        }
    }
}
