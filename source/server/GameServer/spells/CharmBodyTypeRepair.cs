using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;

namespace DOL.GS.Spells
{
    /// <summary>
    /// #579: the body types the Minstrel's and the Mentalist's charms (and two other charms) may take, from DOL's public
    /// database (Eve-of-Darkness/db-public 764bda56, "Fix Charm Spell Types"), checked against the period sources.
    /// <see cref="CharmSpellHandler"/> reads a Charm spell's <c>AmnesiaChance</c> as <see cref="CharmSpellHandler.eCharmType"/>
    /// (0 any, 1 humanoid, 2 animal, 3 insect, 4 +animal, 5 +insect, 6 +magical, 7 +undead, 8 reptile; any other value skips
    /// the body-type check, so it charms anything).
    /// <para>Sources (also quoted in CharmSpellHandler.cs): the camelotvault Minstrel guide, April 2003 (Wayback 2003-12-03):
    /// Captivating Melodies humanoids, Enchanting adds animals, Attracting adds insects, Pleasurable, Enticing and Alluring
    /// add magical creatures. The camelotvault Mentalist guide (Wayback 2004-06-07) and the Herald's Mentalist class library:
    /// Illusory Enemy (4) humanoids, Fabricated (10) adds animals, Imaginary (17) insects, Phantom (25) magical, Ghostly (33)
    /// undead, Dream Enemy (42) any. Control Reptile (Beastcraft) and Superior Call(Insect) are narrowed to what they name;
    /// whether a 1.65 Hunter should have Control Reptile at all is #695.</para>
    /// <para>A one-time repair of saved world data, as <see cref="Keeps.FrontierKeepLevelRepair"/>: run at start-up before the
    /// skills load, recorded in <c>living_server_world_repairs</c>. A spell is matched by name and type (ids differ between
    /// databases) and changed only while it holds db-public's old value; a row already right, or set to anything else, is
    /// left as it is and logged. Each change writes a <c>CHARM_BODY_TYPE_REPAIRED</c> row with the old value, so it can be
    /// reverted by hand. No boss, no charm rule and no level cap changes.</para>
    /// </summary>
    public static class CharmBodyTypeRepair
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The name in <c>living_server_world_repairs</c>.</summary>
        public const string RepairName = "charm-body-types-1.2.3";

        public const string LogTag = "CHARM_BODY_TYPE_REPAIRED";

        public const string CharmType = "Charm";

        /// <summary>One charm: its name, the value db-public had before 764bda56 (what a world built from it holds) and after.</summary>
        public sealed record Fix(string Name, int OldValue, CharmSpellHandler.eCharmType NewValue, string Source);

        private const string Minstrel = "camelotvault Minstrel guide 2003 (Instruments)";
        private const string Mentalist = "camelotvault Mentalist guide 2004, Herald class library (Illusions)";

        public static readonly Fix[] Fixes =
        [
            new("Captivating Melodies", 0, CharmSpellHandler.eCharmType.Humanoid, Minstrel),
            new("Enchanting Melodies", 0, CharmSpellHandler.eCharmType.HumanoidAnimal, Minstrel),
            new("Attracting Melodies", 0, CharmSpellHandler.eCharmType.HumanoidAnimalInsect, Minstrel),
            new("Pleasurable Melodies", 0, CharmSpellHandler.eCharmType.HumanoidAnimalInsectMagical, Minstrel),
            new("Enticing Melodies", 0, CharmSpellHandler.eCharmType.HumanoidAnimalInsectMagical, Minstrel),
            new("Alluring Melodies", 0, CharmSpellHandler.eCharmType.HumanoidAnimalInsectMagical, Minstrel),
            new("Fabricated Enemy", 1, CharmSpellHandler.eCharmType.HumanoidAnimal, Mentalist),
            new("Imaginary Enemy", 1, CharmSpellHandler.eCharmType.HumanoidAnimalInsect, Mentalist),
            new("Phantom Enemy", 1, CharmSpellHandler.eCharmType.HumanoidAnimalInsectMagical, Mentalist),
            new("Ghostly Enemy", 1, CharmSpellHandler.eCharmType.HumanoidAnimalInsectMagicalUndead, Mentalist),
            new("Dream Enemy", 1, CharmSpellHandler.eCharmType.All, Mentalist),
            new("Control Reptile", 9, CharmSpellHandler.eCharmType.Reptile, "its name; 9 is no body type (#695)"),
            new("Superior Call(Insect)", 7, CharmSpellHandler.eCharmType.Insect, "its name"),
        ];

        private static readonly Dictionary<string, Fix> ByName = Fixes.ToDictionary(fix => fix.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>The fix for a spell of this name and type, or null. Pure.</summary>
        public static Fix For(string name, string type) =>
            name != null && string.Equals(type, CharmType, StringComparison.OrdinalIgnoreCase) && ByName.TryGetValue(name.Trim(), out Fix fix)
                ? fix
                : null;

        /// <summary>The value the spell goes to, or null when it stays: only db-public's old value is changed. Pure.</summary>
        public static int? Target(string name, string type, int current) =>
            For(name, type) is { } fix && current == fix.OldValue && current != (int)fix.NewValue ? (int)fix.NewValue : null;

        /// <summary>Whether this world's repair list already has the repair. Pure.</summary>
        public static bool AlreadyRan(string repairs) =>
            (repairs ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(RepairName, StringComparer.OrdinalIgnoreCase);

        /// <summary>The repair list with this repair added. Pure.</summary>
        public static string WithRepair(string repairs) =>
            AlreadyRan(repairs) ? repairs : string.IsNullOrWhiteSpace(repairs) ? RepairName : repairs.Trim() + "," + RepairName;

        public static string RepairedLine(int spellId, string name, int was, int now) =>
            $"{LogTag} spellId={spellId} spell=\"{name}\" amnesia={was}->{now} bodyTypes={(CharmSpellHandler.eCharmType)now} " +
            "reason=old-db-public-value";

        /// <summary>From the server start, before the skills load. Never stops the start.</summary>
        public static bool Run()
        {
            try
            {
                if (AlreadyRan(ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS))
                    return true;
                int changed = 0, kept = 0;
                foreach (DbSpell spell in DOLDB<DbSpell>.SelectObjects(DB.Column("Type").IsEqualTo(CharmType)))
                {
                    Fix fix = For(spell.Name, spell.Type);
                    if (fix == null)
                        continue;
                    int was = spell.AmnesiaChance;
                    int? target = Target(spell.Name, spell.Type, was);
                    if (target == null)
                    {
                        kept++;
                        log.Info($"{LogTag} spellId={spell.SpellID} spell=\"{spell.Name}\" amnesia={was}: " +
                            (was == (int)fix.NewValue ? "already right" : "not db-public's old value, left as it is"));
                        continue;
                    }
                    spell.AmnesiaChance = target.Value;
                    GameServer.Database.SaveObject(spell);
                    changed++;
                    log.Info(RepairedLine(spell.SpellID, spell.Name, was, target.Value));
                }
                log.Info($"{LogTag} done: {changed} changed, {kept} left as they were");
                MarkDone();
            }
            catch (Exception exception)
            {
                log.Error($"{LogTag} the charm body types could not be repaired; tried again next start", exception);
            }
            return true;
        }

        private static void MarkDone()
        {
            string done = WithRepair(ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS);
            DbServerProperty row = DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo(ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS_KEY));
            if (row != null)
            {
                row.Value = done;
                GameServer.Database.SaveObject(row);
            }
            ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS = done;
        }
    }
}
