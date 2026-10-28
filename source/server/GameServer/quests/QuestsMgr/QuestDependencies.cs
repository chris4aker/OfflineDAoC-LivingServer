using System;
using System.Collections.Generic;
using System.Globalization;

namespace DOL.GS.Quests
{
    /// <summary>
    /// The forms of a data quest's QuestDependency entries ('|'-separated; every entry must be met). From lometur/HearthDAoC.
    /// <list type="bullet">
    /// <item><c>Name</c>: a finished quest has that name (a finished scripted quest of that name counts too).</item>
    /// <item><c>#20188</c>: the data quest with that ID is finished.</item>
    /// <item><c>#20157/20469</c>: at least one of these is finished.</item>
    /// <item><c>!#20478</c> or <c>!#20478/21324</c>: none of these is active or finished.</item>
    /// </list>
    /// The epic chains reuse names (the Guild of Shadows 25 and 30 are both "Regal Nobility"), so a name can't say which
    /// step comes before; an ID can.
    /// </summary>
    public static class QuestDependencies
    {
        /// <summary>Whether an entry is an ID entry: it starts with "#" or "!#" (spaces around it don't count).</summary>
        public static bool IsIdEntry(string entry)
        {
            string text = entry?.Trim() ?? string.Empty;
            return text.StartsWith("#", StringComparison.Ordinal) || text.StartsWith("!#", StringComparison.Ordinal);
        }

        /// <summary>The IDs of an ID entry, and whether it closes the quest (the "!#" form). False for a name and for a
        /// malformed ID entry (no ID, a part that isn't a whole number above 0).</summary>
        public static bool TryParseIds(string entry, out int[] ids, out bool closes)
        {
            ids = Array.Empty<int>();
            closes = false;
            string text = entry?.Trim() ?? string.Empty;
            bool closing;
            if (text.StartsWith("!#", StringComparison.Ordinal))
            {
                closing = true;
                text = text.Substring(2);
            }
            else if (text.StartsWith("#", StringComparison.Ordinal))
            {
                closing = false;
                text = text.Substring(1);
            }
            else
                return false;
            string[] parts = text.Split('/');
            var parsed = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out parsed[i]) || parsed[i] <= 0)
                    return false;
            }
            ids = parsed;
            closes = closing;
            return true;
        }

        /// <summary>Whether an entry can ever be met: a name that isn't blank, or a well-formed ID entry.</summary>
        public static bool IsValid(string entry) =>
            IsIdEntry(entry) ? TryParseIds(entry, out _, out _) : !string.IsNullOrWhiteSpace(entry);

        /// <summary>Whether one entry is met. Names compare without case, as upstream did; a malformed ID entry is
        /// never met.</summary>
        public static bool IsMet(string entry, ICollection<string> finishedNames, ICollection<int> finishedIds, ICollection<int> activeIds)
        {
            if (!IsIdEntry(entry))
            {
                if (string.IsNullOrWhiteSpace(entry))
                    return false;
                foreach (string name in finishedNames)
                {
                    if (string.Equals(name, entry, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            if (!TryParseIds(entry, out int[] ids, out bool closes))
                return false;
            foreach (int id in ids)
            {
                if (closes && (finishedIds.Contains(id) || activeIds.Contains(id)))
                    return false;
                if (!closes && finishedIds.Contains(id))
                    return true;
            }
            return closes;
        }

        /// <summary>Whether every entry is met (no entries: met).</summary>
        public static bool AreMet(IEnumerable<string> entries, ICollection<string> finishedNames, ICollection<int> finishedIds, ICollection<int> activeIds)
        {
            foreach (string entry in entries)
            {
                if (!IsMet(entry, finishedNames, finishedIds, activeIds))
                    return false;
            }
            return true;
        }
    }
}
