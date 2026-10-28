using System;
using System.Collections.Generic;
using System.Globalization;

namespace DOL.GS.Quests
{
    /// <summary>
    /// When a data quest hands a Deliver or DeliverFinish step's item, and what a turn-in takes. From lometur/HearthDAoC.
    /// - A delivery step's item is handed only if the player doesn't carry one ("Traveler's Way -- Supply Run" gave two
    ///   bundles). The copy being handed over doesn't count, so a step that hands the same item back still does.
    /// - Accepting a quest whose first step is a delivery hands that item, unless the delivery goes back to the giver,
    ///   who wants the player to bring it (20098 "Sveabone Hilt Sword").
    /// - A CollectItemTemplate entry "id;N" (N >= 2) needs N of the item.
    /// Item ids compare without case, names with <see cref="QuestNames.Same"/>.
    /// </summary>
    public static class QuestDeliveryItems
    {
        /// <summary>True when <paramref name="template"/> should be handed: not empty, not carried, and not handed by the
        /// step just finishing (<paramref name="beingHanded"/>).</summary>
        public static bool ShouldHand(string template, IEnumerable<string> carried, IEnumerable<string> beingHanded)
        {
            string wanted = template?.Trim();
            if (string.IsNullOrEmpty(wanted))
                return false;

            return !Contains(carried, wanted) && !Contains(beingHanded, wanted);
        }

        /// <summary>The backpack's item ids that count as carried, from (id, count, copies being handed over): an item
        /// counts unless the hand-over takes all its copies. DataQuest removes the whole item handed over.</summary>
        public static List<string> CarriedIds(IEnumerable<(string Id, int Count, int HandedOver)> backpack)
        {
            List<string> ids = new();
            if (backpack == null)
                return ids;

            foreach ((string id, int count, int handedOver) in backpack)
            {
                if (handedOver <= 0 || count > handedOver)
                    ids.Add(id);
            }

            return ids;
        }

        /// <summary>The item accepting a quest hands for its first step (trimmed), or null: step 1 is a delivery, it has an
        /// item, its target (TargetName's first entry, the part before ';') isn't the giver, and the player carries none.</summary>
        public static string FirstStepItem(bool firstStepIsDelivery, IReadOnlyList<string> stepItemTemplates, IEnumerable<string> carried,
            string firstTargetName, string giverName)
        {
            if (!firstStepIsDelivery || stepItemTemplates == null || stepItemTemplates.Count == 0)
                return null;

            if (IsGiver(firstTargetName, giverName))
                return null;

            string template = stepItemTemplates[0]?.Trim();
            return ShouldHand(template, carried, null) ? template : null;
        }

        /// <summary>True when the delivery-item refresh (ClassicQuests.GiveDeliveryItem) must leave the step alone: step 1
        /// goes back to the giver (<paramref name="giverName"/>, the quest's StartName), so the player brings the item.</summary>
        public static bool BringsToGiver(int step, string targetName, string giverName)
            => step == 1 && IsGiver(targetName, giverName);

        /// <summary>A CollectItemTemplate entry's id and count: "id;N" with N >= 2 needs N (both parts trimmed). Any other
        /// entry is returned as it is, needing one, as upstream reads it.</summary>
        public static (string Id, int Needed) CollectEntry(string entry)
        {
            int semicolon = entry?.LastIndexOf(';') ?? -1;
            if (semicolon >= 0
                && int.TryParse(entry[(semicolon + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int needed)
                && needed >= 2)
            {
                string id = entry[..semicolon].Trim();
                if (id.Length > 0)
                    return (id, needed);
            }

            return (entry, 1);
        }

        /// <summary>Upstream's collect rule: the lowercased item id contains the lowercased entry id. An empty id is no item.</summary>
        public static bool IsCollectItem(string itemId, string id)
            => !string.IsNullOrEmpty(id) && itemId != null && itemId.ToLower().Contains(id.ToLower());

        /// <summary>The copies to take from each other backpack item (id, count) for a step needing <paramref name="needed"/>
        /// of <paramref name="id"/>, or null when there are too few. The item handed over counts for
        /// <paramref name="handedCount"/> copies (at least one); a stack loses only the copies wanted.</summary>
        public static int[] OthersToTake(IReadOnlyList<(string Id, int Count)> others, string id, int needed, int handedCount)
        {
            int[] take = new int[others?.Count ?? 0];
            int wanted = needed - Math.Max(handedCount, 1);
            for (int i = 0; i < take.Length && wanted > 0; i++)
            {
                if (!IsCollectItem(others[i].Id, id))
                    continue;

                take[i] = Math.Min(wanted, Math.Max(others[i].Count, 1));
                wanted -= take[i];
            }

            return wanted > 0 ? null : take;
        }

        private static bool IsGiver(string targetName, string giverName)
        {
            string target = targetName?.Split(';')[0].Trim();
            return !string.IsNullOrEmpty(target) && QuestNames.Same(target, giverName?.Trim());
        }

        private static bool Contains(IEnumerable<string> ids, string wanted)
        {
            if (ids == null)
                return false;

            foreach (string id in ids)
            {
                if (string.Equals(id?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
