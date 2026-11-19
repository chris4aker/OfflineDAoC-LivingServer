using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>Which share table typed a bot: the 12% one every bot had before Crafting 2.0, or the share at its creation.</summary>
    public enum CrafterCohort { Legacy, ShareAtCreation }

    /// <summary>A crafter's spending policy (the model's fix for "crafters reinvest every coin").</summary>
    public sealed record CrafterSpendPolicy(double MaxReinvestShareOfProfit, long CushionCopper)
    {
        public static readonly CrafterSpendPolicy Default = new(0.5, 0);
    }

    /// <summary>What the rest of the economy may know about a crafter gamebot.</summary>
    public sealed record CrafterState(long BotId, string Name, eRealm Realm, int BotLevel, eCraftingSkill Order,
        IReadOnlyDictionary<eCraftingSkill, int> Skills, long PurseCopper, long LifetimeNetCopper, CrafterSpendPolicy Policy);

    /// <summary>
    /// The crafter gamebots (WP4 implements it). Only autonomous gamebots of the Crafter personality are crafters, and
    /// only while bot_crafters is on; companions and players never are.
    /// </summary>
    public interface IBotCrafterRoster
    {
        bool IsCrafter(long botId);
        bool TryGet(long botId, out CrafterState state);
        IEnumerable<CrafterState> InRealm(eRealm realm);
        /// <summary>A crafter of the realm with at least this skill for a service (a repair, an imbue), or null.</summary>
        CrafterState FindServiceCrafter(eRealm realm, eCraftingSkill skill, int minSkill, Random random);
    }

    /// <summary>The registered roster; nobody is a crafter until WP4 registers one.</summary>
    public static class BotCrafters
    {
        private static volatile IBotCrafterRoster _roster = NullRoster.Instance;

        public static IBotCrafterRoster Roster
        {
            get => _roster;
            set => _roster = value ?? NullRoster.Instance;
        }

        private sealed class NullRoster : IBotCrafterRoster
        {
            internal static readonly NullRoster Instance = new();
            public bool IsCrafter(long botId) => false;
            public bool TryGet(long botId, out CrafterState state) { state = null; return false; }
            public IEnumerable<CrafterState> InRealm(eRealm realm) => Array.Empty<CrafterState>();
            public CrafterState FindServiceCrafter(eRealm realm, eCraftingSkill skill, int minSkill, Random random) => null;
        }
    }

    /// <summary>
    /// The storage format of a gamebot's crafting skills in <c>offline_world_bots.SerializedCraftingSkills</c>:
    /// "skill|value;skill|value", the primary craft first. From rambodolphin13's DividedRealms
    /// (AutonomousBotCrafting.ParseSkills/SerializeSkills/Join/Gain, 93c25b69), GPL-3.0, unchanged but for the cap
    /// parameter, so the ported bot crafting (WP4) and the economy read one format. Pure. Nothing writes the column
    /// until WP4; while it is empty the economy's dead "trained crafter" branches stay dead.
    /// </summary>
    public static class BotCraftingSkills
    {
        /// <summary>Reads the skills in saved order; the first is the primary craft. Unknown, zero and repeated ids are dropped.</summary>
        public static List<KeyValuePair<eCraftingSkill, int>> Parse(string saved)
        {
            var skills = new List<KeyValuePair<eCraftingSkill, int>>();
            foreach (string entry in (saved ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split('|');
                if (parts.Length == 2 && int.TryParse(parts[0], out int id) && int.TryParse(parts[1], out int value) &&
                    Enum.IsDefined(typeof(eCraftingSkill), id) && id != 0 && skills.All(pair => pair.Key != (eCraftingSkill)id))
                    skills.Add(new((eCraftingSkill)id, value));
            }
            return skills;
        }

        public static string Serialize(IEnumerable<KeyValuePair<eCraftingSkill, int>> skills) =>
            string.Join(";", skills.Select(pair => $"{(int)pair.Key}|{pair.Value}"));

        public static int Value(string saved, eCraftingSkill skill) =>
            Parse(saved).FirstOrDefault(pair => pair.Key == skill).Value;

        public static eCraftingSkill Primary(string saved) =>
            Parse(saved).Select(pair => pair.Key).FirstOrDefault();

        /// <summary>Joining an order: the primary craft first, then what the order's master teaches, each at 1.</summary>
        public static string Join(eCraftingSkill primary, IEnumerable<eCraftingSkill> taught)
        {
            var skills = new List<KeyValuePair<eCraftingSkill, int>> { new(primary, 1) };
            foreach (eCraftingSkill skill in taught ?? Enumerable.Empty<eCraftingSkill>())
                if (skill != primary)
                    skills.Add(new(skill, 1));
            return Serialize(skills);
        }

        /// <summary>
        /// <paramref name="points"/> more in a skill (1 in DR; WP4's pace may give more); a secondary (not
        /// <paramref name="primaryWork"/>) stops at <paramref name="secondaryCap"/>. An unknown skill changes nothing.
        /// </summary>
        public static string Gain(string saved, eCraftingSkill skill, bool primaryWork, int points = 1,
            int secondaryCap = Crafting.CraftRules.SubSkillCap)
        {
            List<KeyValuePair<eCraftingSkill, int>> skills = Parse(saved);
            int index = skills.FindIndex(pair => pair.Key == skill);
            if (index < 0 || points <= 0)
                return saved;
            int value = skills[index].Value;
            if (!primaryWork && value >= secondaryCap)
                return saved;
            int next = primaryWork ? value + points : Math.Min(secondaryCap, value + points);
            skills[index] = new(skill, next);
            return Serialize(skills);
        }
    }
}
