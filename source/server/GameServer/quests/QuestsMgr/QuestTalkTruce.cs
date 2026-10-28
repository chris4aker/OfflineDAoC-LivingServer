using System;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS.Quests
{
    /// <summary>
    /// Some quests send the player to talk to, whisper, or hand items to a monster: Tiomoid the Siabra Gurite in Lough
    /// Gur (Lost Townfolk of Bran Llyr), Trefor and Curmie (Occupation of Lough Gur), Shiake (Mother's Children), the
    /// XP-item turn-in NPCs of the Shrouded Isles tribes. Their faction is kill-on-sight by default, or they have no
    /// faction and attack anyone, so the player was attacked on arrival or turned away with a dirty look and the quest
    /// could not go on (quest audit 2026-10-13: 67 such NPCs). While a player is on a step that talks to, whispers to
    /// or hands items to one of them, or can take one of its quests, that NPC does not aggro the player and talks to
    /// them. Everyone else meets the NPC as before, and a player who attacks it gets a fight.
    /// </summary>
    public static class QuestTalkTruce
    {
        private static readonly Lazy<HashSet<string>> _questNpcNames = new(LoadQuestNpcNames);

        /// <summary>Called from mob aggro checks: cheap for every NPC that is not a data quest's talk target.</summary>
        public static bool Holds(GameNPC npc, GamePlayer player)
        {
            if (npc == null || player == null || npc.Realm != eRealm.None || !_questNpcNames.Value.Contains(npc.Name))
                return false;

            foreach (var pair in player.QuestList) // not .Keys: that copies the keys on every aggro check
            {
                if (pair.Key is DataQuest dataQuest && TalksTo(dataQuest.StepType) &&
                    QuestNames.Same(dataQuest.TargetName, npc.Name) && (dataQuest.TargetRegion == 0 || dataQuest.TargetRegion == npc.CurrentRegionID))
                    return true;
            }

            // A quest it gives. Level range only: the full qualification check reads the database.
            List<DataQuest> given = npc.DataQuestList;
            for (int i = 0; i < given.Count; i++)
            {
                DataQuest quest = i < given.Count ? given[i] : null;
                if (quest?.DBDataQuest == null || quest.StartType == DataQuest.eStartType.KillComplete)
                    continue;
                if (player.Level >= quest.DBDataQuest.MinLevel && player.Level <= quest.DBDataQuest.MaxLevel)
                    return true;
            }

            return false;
        }

        public static bool TalksTo(DataQuest.eStepType step) => step is DataQuest.eStepType.Deliver or DataQuest.eStepType.DeliverFinish or
            DataQuest.eStepType.Interact or DataQuest.eStepType.InteractFinish or DataQuest.eStepType.Whisper or
            DataQuest.eStepType.WhisperFinish or DataQuest.eStepType.Collect or DataQuest.eStepType.CollectFinish;

        /// <summary>Start NPCs and talk/whisper/hand-in targets of every data quest (names compared without case, as DataQuest does).</summary>
        private static HashSet<string> LoadQuestNpcNames()
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

            foreach (DbDataQuest quest in GameServer.Database.SelectAllObjects<DbDataQuest>())
            {
                if (!string.IsNullOrEmpty(quest.StartName))
                    names.Add(quest.StartName);

                string[] types = (quest.StepType ?? string.Empty).Split('|');
                string[] targets = (quest.TargetName ?? string.Empty).Split('|');

                for (int i = 0; i < Math.Min(types.Length, targets.Length); i++)
                {
                    if (!byte.TryParse(types[i], out byte type) || !TalksTo((DataQuest.eStepType) type))
                        continue;

                    string name = targets[i].Split(';')[0];
                    if (name.Length > 0)
                        names.Add(name);
                }
            }

            return names;
        }
    }
}
