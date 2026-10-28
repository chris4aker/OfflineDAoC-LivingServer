using System;

namespace DOL.GS.Quests
{
    /// <summary>
    /// How a data quest's names (a step's TargetName, the quest's StartName) match a spawn's name: ordinal, without case.
    /// The quest data spells many names differently from their spawns: "Path of the Renegade" wants "Arawnite Messenger",
    /// whose NpcTemplate names him "arawnite messenger", so his kill never counted. From lometur/HearthDAoC.
    /// </summary>
    public static class QuestNames
    {
        /// <summary>Same name but for case; spacing and punctuation still count, and null matches only null.</summary>
        public static bool Same(string questName, string objectName)
            => string.Equals(questName, objectName, StringComparison.OrdinalIgnoreCase);
    }
}
