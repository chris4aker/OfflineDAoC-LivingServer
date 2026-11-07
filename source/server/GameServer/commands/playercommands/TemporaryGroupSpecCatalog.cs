using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Commands
{
    /// <summary>
    /// The builds a /spawn companion can be asked for (owner 2026-10-28: /spawn specs, and /spawn &lt;class&gt; &lt;spec&gt;
    /// for macros). Nothing here changes a build: the options are the class's existing builds, named after the spec line
    /// each one really trains highest (owner: "basing the spec name on what is actually being trained"). A family such as
    /// the Friar's staff builds holds variants that train different lines highest, so options are the distinct lines,
    /// and an option is spawned from one of the existing variants that trains it highest. Melee families are weapon
    /// setups (one-handed and shield, two-handed, dual wield...) and keep that name; their weapon type stays random.
    /// </summary>
    public static class TemporaryGroupSpecCatalog
    {
        /// <param name="Label">What the player sees and types: the class's own advanced spell line.</param>
        /// <param name="Line">The specialization the build trains highest (null for weapon setups); also accepted when typed.</param>
        public sealed record Option(string Label, eSpecType[] Families, bool WeaponSetup, string Line = null);

        /// <summary>
        /// The class's own advanced (specialization) spell line for a trained specialization (owner 2026-10-29: a
        /// Sorcerer's builds are Telekinesis, Disorientation and Domination, not the trainer's Matter, Body and Mind
        /// Magic). Taken from the spell line table; where that table only holds an internal "... Spec" name (Healer
        /// Mending Spec, Druid Nurture Spec) the specialization's own name is the real one and stays.
        /// </summary>
        private static readonly Dictionary<(eCharacterClass, string), string> SpecLines = new()
        {
            [(eCharacterClass.Sorcerer, Specs.Matter_Magic)] = "Telekinesis",
            [(eCharacterClass.Sorcerer, Specs.Body_Magic)] = "Disorientation",
            [(eCharacterClass.Sorcerer, Specs.Mind_Magic)] = "Domination",
            [(eCharacterClass.Cabalist, Specs.Matter_Magic)] = "Matter Manipulation",
            [(eCharacterClass.Cabalist, Specs.Body_Magic)] = "Essence Manipulation",
            [(eCharacterClass.Cabalist, Specs.Spirit_Magic)] = "Vivification",
            [(eCharacterClass.Theurgist, Specs.Earth_Magic)] = "Abrasion",
            [(eCharacterClass.Theurgist, Specs.Cold_Magic)] = "Refrigeration",
            [(eCharacterClass.Theurgist, Specs.Wind_Magic)] = "Vapormancy",
            [(eCharacterClass.Wizard, Specs.Earth_Magic)] = "Calefaction",
            [(eCharacterClass.Wizard, Specs.Cold_Magic)] = "Liquifaction",
            [(eCharacterClass.Wizard, Specs.Fire_Magic)] = "Pyromancy",
            [(eCharacterClass.Cleric, Specs.Rejuvenation)] = "Rebirth",
            [(eCharacterClass.Cleric, Specs.Enhancement)] = "Guardian Angel",
            [(eCharacterClass.Cleric, Specs.Smite)] = "Terrible Hammer",
            [(eCharacterClass.Friar, Specs.Rejuvenation)] = "Rebirth",
            [(eCharacterClass.Eldritch, Specs.Light)] = "Shadow Control",
            [(eCharacterClass.Eldritch, Specs.Mana)] = "Vacuumancy",
            [(eCharacterClass.Eldritch, Specs.Void)] = "Void Mastery",
            [(eCharacterClass.Enchanter, Specs.Light)] = "Bedazzling",
            [(eCharacterClass.Enchanter, Specs.Mana)] = "Empowering",
            [(eCharacterClass.Enchanter, Specs.Enchantments)] = "Enchantment Mastery",
            [(eCharacterClass.Mentalist, Specs.Light)] = "Illusions",
            [(eCharacterClass.Mentalist, Specs.Mana)] = "Holism",
            [(eCharacterClass.Mentalist, Specs.Mentalism)] = "Mind Mastery",
            [(eCharacterClass.Animist, Specs.Arboreal_Path)] = "Arboreal Mastery",
            [(eCharacterClass.Runemaster, Specs.Darkness)] = "Runes of Darkness",
            [(eCharacterClass.Runemaster, Specs.Suppression)] = "Runes of Suppression",
            [(eCharacterClass.Runemaster, Specs.Runecarving)] = "Runes of Destruction",
            [(eCharacterClass.Spiritmaster, Specs.Darkness)] = "Spirit Dimming",
            [(eCharacterClass.Spiritmaster, Specs.Suppression)] = "Spirit Suppression",
            [(eCharacterClass.Spiritmaster, Specs.Summoning)] = "Spirit Enhancement",
            [(eCharacterClass.Bonedancer, Specs.Darkness)] = "Bone Mystics",
            [(eCharacterClass.Bonedancer, Specs.Suppression)] = "Bone Guardians",
            [(eCharacterClass.Bonedancer, Specs.BoneArmy)] = "Bone Warriors",
        };

        public static string LineLabel(eCharacterClass characterClass, string trainedLine) =>
            trainedLine != null && SpecLines.TryGetValue((characterClass, trainedLine), out string label) ? label : trainedLine;

        private const int Samples = 200;
        private const int SpawnTries = 400;
        private static readonly ConcurrentDictionary<eCharacterClass, IReadOnlyList<Option>> Cache = new();

        public static bool IsWeaponSetup(eSpecType family) => family is eSpecType.OneHanded or eSpecType.OneHandAndShield or
            eSpecType.OneHandHybrid or eSpecType.TwoHandAndShield or eSpecType.TwoHandHybrid or eSpecType.TwoHanded or
            eSpecType.DualWield or eSpecType.DualWieldAndShield or eSpecType.LeftAxe or eSpecType.Mid or eSpecType.Ranged or
            eSpecType.Instrument;

        public static string SetupLabel(eCharacterClass characterClass, eSpecType family) => family switch
        {
            eSpecType.OneHanded or eSpecType.OneHandAndShield => "One Handed and Shield",
            eSpecType.OneHandHybrid => "One Handed Hybrid",
            // The Armsman has both: its hybrid trains the two-hander first and keeps a shield.
            eSpecType.TwoHandHybrid or eSpecType.TwoHandAndShield when characterClass == eCharacterClass.Armsman => "Two Handed Hybrid",
            eSpecType.TwoHandHybrid or eSpecType.TwoHandAndShield or eSpecType.TwoHanded or eSpecType.Mid => "Two Handed",
            eSpecType.DualWield when characterClass == eCharacterClass.Blademaster => Specs.Celtic_Dual,
            eSpecType.DualWield when characterClass == eCharacterClass.Savage => Specs.HandToHand,
            eSpecType.DualWield => "Dual Wield",
            eSpecType.DualWieldAndShield when characterClass == eCharacterClass.Blademaster => Specs.Celtic_Dual + " and Shield",
            eSpecType.DualWieldAndShield => "Dual Wield and Shield",
            eSpecType.LeftAxe => Specs.Left_Axe,
            _ => family.ToString(),
        };

        /// <summary>The spec line a build trains highest; on a tie, the one the build lists first.</summary>
        public static string TopLine(BotSpec spec)
        {
            string top = null;
            uint cap = 0;
            foreach (BotSpecLine line in spec?.SpecLines ?? [])
            {
                if (top == null || line.SpecCap > cap)
                {
                    top = line.Spec;
                    cap = line.SpecCap;
                }
            }
            return top;
        }

        /// <summary>The class's options, or none when it has a single build.</summary>
        public static IReadOnlyList<Option> For(eCharacterClass characterClass) => Cache.GetOrAdd(characterClass, Build);

        private static IReadOnlyList<Option> Build(eCharacterClass characterClass)
        {
            // A class with one build family still gets options when its variants train different lines highest
            // (owner 2026-10-29: Infiltrator, Minstrel, Scout, Nightshade, Ranger, Valewalker, Berserker...).
            IReadOnlyList<eSpecType> families = BotSpec.GetSpecializationChoices(characterClass);

            List<Option> options = new();
            Dictionary<string, List<eSpecType>> byLine = new();
            List<string> order = new();
            foreach (eSpecType family in families)
            {
                if (IsWeaponSetup(family))
                {
                    string label = SetupLabel(characterClass, family);
                    if (!options.Any(option => option.Label == label))
                        options.Add(new(label, [family], true));
                    continue;
                }
                for (int i = 0; i < Samples; i++)
                {
                    string line = TopLine(BotSpec.GetSpec(characterClass, family));
                    if (line == null)
                        continue;
                    if (!byLine.TryGetValue(line, out List<eSpecType> lineFamilies))
                    {
                        byLine[line] = lineFamilies = new();
                        order.Add(line);
                    }
                    if (!lineFamilies.Contains(family))
                        lineFamilies.Add(family);
                }
            }
            options.AddRange(order.Select(line => new Option(LineLabel(characterClass, line), byLine[line].ToArray(), false, line)));
            return options.Count < 2 ? [] : options;
        }

        public static bool TryResolve(eCharacterClass characterClass, string input, out Option option)
        {
            string wanted = Normalize(input);
            option = For(characterClass).FirstOrDefault(candidate => Normalize(candidate.Label) == wanted) ??
                     For(characterClass).FirstOrDefault(candidate => candidate.Line != null && Normalize(candidate.Line) == wanted);
            return option != null;
        }

        /// <summary>A new build for the option: a weapon setup as asked; otherwise one of the existing variants that trains the line highest.</summary>
        public static BotSpec Create(eCharacterClass characterClass, Option option)
        {
            if (option.WeaponSetup)
                return BotSpec.GetSpec(characterClass, option.Families[0]);
            BotSpec spec = null;
            for (int i = 0; i < SpawnTries; i++)
            {
                spec = BotSpec.GetSpec(characterClass, option.Families[Util.Random(option.Families.Length - 1)]);
                if (TopLine(spec) == (option.Line ?? option.Label))
                    return spec;
            }
            return spec;
        }

        public static string Describe(eCharacterClass characterClass) =>
            string.Join(", ", For(characterClass).Select(option => option.Label));

        public static string Normalize(string value) =>
            new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
