namespace DOL.GS.Quests
{
    /// <summary>
    /// A data quest's level window (MinLevel..MaxLevel, both inclusive). It decides who is offered the quest:
    /// DataQuest.CheckQuestQualification, and a Collection quest's turn-ins, which are offers every time (such a
    /// quest is never in the journal). Owner 2026-10-30: it also gated delivery and collect hand-ins of a quest the
    /// player already held, so a player who outlevelled a quest could not finish it while its kill, interact and
    /// whisper steps went on. A held quest's steps are not level-checked any more.
    /// </summary>
    public static class QuestLevelWindow
    {
        public static bool Offers(int playerLevel, int minLevel, int maxLevel) =>
            playerLevel >= minLevel && playerLevel <= maxLevel;
    }
}
