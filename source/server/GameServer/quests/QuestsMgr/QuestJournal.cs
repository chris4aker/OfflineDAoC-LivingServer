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
    }
}
