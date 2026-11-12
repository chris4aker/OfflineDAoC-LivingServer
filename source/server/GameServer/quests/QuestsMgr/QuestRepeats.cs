namespace DOL.GS.Quests
{
    /// <summary>
    /// When a finished data quest is offered again (QUESTING.md defect 5, Living Server 1.2). DataQuest keeps a finished
    /// quest at step 0 with its count of finishes, and the old check read AbstractQuest.IsDoingQuest(), true for any step
    /// but -1, so a quest written to be done several times (MaxCount 2 or more) was never offered after its first finish.
    /// MaxCount 0 means "no limit" in the row format, but every quest with it has only ever been offered once here, and
    /// whether the classic rows use 0 for one-time quests needs the world (docs/QUESTING.md): 0 and 1 stay offered once.
    /// </summary>
    public static class QuestRepeats
    {
        /// <summary>Whether a quest at this step, finished this many times, may be offered again.</summary>
        public static bool OffersAgain(int step, int timesDone, int maxCount) =>
            step <= 0 && (timesDone <= 0 || maxCount > 1 && timesDone < maxCount);
    }
}
