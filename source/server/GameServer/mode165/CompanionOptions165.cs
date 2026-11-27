using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DOL.AI.Brain;

// From shadowofze's True 1.65 v0.7 (f68cc75), via True165, GPL-3.0. Living Server offers only the rows its own AI reads
// (see Generics); the storage, the protocol (hire op 14) and every stored value are True 1.65's.

namespace DOL.GS
{
    /// <summary>
    /// A hired companion's own choices, set by the player in the new client's Pets / Songs tab and kept in the hire
    /// record until changed (owner 2026-11-05, reset-day S33 / C17): charm on or off (Sorcerer, Minstrel and any class
    /// that has learned a charm; a hired Hunter keeps its wolf), the Cabalist / Enchanter pet, the Bonedancer's sub-pets
    /// (their level total within the commander's 75), and the song or chant held in a fight, standing around and
    /// travelling (Bard, Minstrel, Skald, Paladin). "Automatic" is the bot's own choice. Gamebots ignore all of this.
    ///
    /// Protocol (hire op 14, one per option row, then one with kind 0xFF): [0] row, [1] kind (1 charm, 2 pet, 3 sub-pet,
    /// 4 song), [2] slot, [3] chosen index, [4] number of choices, [5..6] used u16, [7..8] cap u16, [9..] choice names,
    /// each zero-ended. Verbs: charm (row) on|off, pet (row) (i), subpet (row) (slot 1-3) (i), song (row) (slot) (i)
    /// (song slot 1 in a fight, 2 standing around, 3 travelling).
    ///
    /// Kinds 5 and up are generic (client protocol, 2026-11-05): names[0] is the row's caption, the rest the choices,
    /// [3] the chosen index into the choices without the caption; the client sends "option (row) (kind) (slot) (i)".
    /// Each is one class AI choice from the 2026-11-05 class audit (see Generics); choice 0 is always "Automatic".
    /// 2026-11-11: kind 29 is the pure casters' "Save power" row (key savepower, third choice Strict; kind 26 stays for
    /// everyone else), kind 30 "Attack spells" (casters); every Role row (13-15) has a fourth choice, 3 "Melee and heal".
    /// </summary>
    public static class CompanionOptions165
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(CompanionOptions165));

        public const byte KindCharm = 1, KindPet = 2, KindSubPet = 3, KindSong = 4;
        public const byte KindMainShroom = 19, KindShroomDamage = 20, KindShroomVents = 21, KindShroomAblative = 22,
            KindShroomSpore = 23, KindShroomBurst = 24, KindShroomHeal = 25;
        public const byte KindAggro = 5, KindHealAt = 6, KindDebuff = 7, KindBolts = 8, KindBerserk = 9, KindGuard = 10,
            KindHunterPet = 11, KindStealthOpen = 12, KindHealerRole = 13, KindShamanRole = 14, KindFriarRole = 15,
            KindWardenRole = 46,
            KindLifeTransfer = 16, KindAmnesia = 17, KindPerform = 18, KindSavePower = 26, KindPositioning = 27,
            KindDebuffLine = 28, KindSavePowerCaster = 29, KindAttackSpells = 30;
        /// <summary>Companion controls (veteran review M7 and the owner's twist option, 2026-11-11; CompanionControls165).</summary>
        public const byte KindTwist = 40, KindStance = 41, KindCrowdControl = 42, KindPetJob = 43, KindWeapon = 44,
            KindStag = 47, KindCriticalShot = 48, KindOpeningStun = 49, KindEngage = 50, KindSlamSwap = 51,
            KindTripleWield = 52;
        /// <summary>How the companion talks (owner 2026-11-18 09:14: one voice; the True 1.65 formal register is an option).</summary>
        public const byte KindSpeech = 53;
        /// <summary>#571 step 4 (True165 1ed3b24, its kind 53, which is Speech here; True165's 53 and 54 stay reserved): who
        /// a buffer buffs first (BotBuffTargetPolicy.TargetRank).</summary>
        public const byte KindBuffPriority = 55;
        /// <summary>The stored key of the "Buff priority" row (True165's).</summary>
        public const string BuffPriorityKey = "buffpriority";
        /// <summary>The fourth Role choice of every healer's Role row (owner 2026-11-11; CompanionSpellOptions165).</summary>
        public const string MeleeHealChoice = "Melee and heal, no attack spells (melee interrupts casts)";
        public static readonly HashSet<string> Verbs = ["charm", "pet", "subpet", "song", "option"];

        /// <summary>A generic AI choice row: its kind, stored key, caption, choices (0 = Automatic) and who gets it. Values,
        /// when set, is the stored value of each choice (Living Server offers a subset of True 1.65's choices on some rows
        /// and keeps True 1.65's stored numbers, so a record written by either reads the same).</summary>
        private sealed record Generic(byte Kind, string Key, string Caption, string[] Choices, Func<GameBot, bool> Applies,
            int[] Values = null)
        {
            public int ValueOf(int choice) => Values == null ? choice : Values[choice];
            public int ChoiceOf(int value) => Values == null ? value : Math.Max(0, Array.IndexOf(Values, value));
        }

        // Living Server port (owner plan 2026-11-16, step 2): only the rows whose choice a Living Server rule reads are
        // offered. Every one of them is read where the companion casts (CompanionSpellOptions165.Blocks, from
        // GameBot.CastSpell) or where its stance is decided (CompanionEngagementMode.DefensiveLeader). True 1.65's other rows
        // (taunt, heal at, berserk, guard, open from stealth, Animist shrooms, perform, positioning, twist, crowd control by
        // role, pet jobs, weapon, Stag, Critical Shot, Engage, Triple Wield, shield swap, opening stun, the pet, sub-pet
        // and song choices) steer True 1.65 veteran AI that Living Server 1.2 does not have, so they are not offered: a row
        // that changes nothing would mislead. Their kind numbers stay reserved for the protocol.
        private static readonly Generic[] Generics =
        [
            new(KindDebuff, "debuff", "Debuff before nuking", ["Automatic", "Never"],
                bot => (bot.HarmfulSpells ?? []).Concat(bot.InstantHarmfulSpells ?? []).Any(BotCasterPriority.IsDebuff) ||
                       DebuffLines(bot).Count > 0, [0, 2]),
            new(KindBolts, "bolts", "Bolts", ["Automatic", "Nukes only (no bolts)"], bot => bot.CanCastBolts),
            new(KindHunterPet, "hunterpet", "Pet", ["Automatic (on)", "Off"],
                bot => (eCharacterClass)bot.CharacterClass.ID == eCharacterClass.Hunter),
            // Roles (one stored key "role"): Living Server offers True 1.65's fourth choice, "Melee and heal" (a Shaman:
            // "melee and conserve power for heals"), stored as 3 as there
            new(KindHealerRole, "role", "Role", ["Automatic (heal and buff)", MeleeHealChoice],
                bot => CompanionSpellOptions165.UsesHealerRoleRow((eCharacterClass)bot.CharacterClass.ID), [0, 3]),
            new(KindShamanRole, "role", "Role", ["Automatic (melee and cast)", MeleeHealChoice],
                bot => (eCharacterClass)bot.CharacterClass.ID == eCharacterClass.Shaman, [0, 3]),
            new(KindFriarRole, "role", "Role", ["Automatic (fight and heal)", MeleeHealChoice],
                bot => (eCharacterClass)bot.CharacterClass.ID == eCharacterClass.Friar, [0, 3]),
            new(KindLifeTransfer, "lifetransfer", "Life transfer", ["Automatic", "Off"],
                bot => bot.Spells?.Any(spell => spell.SpellType == eSpellType.LifeTransfer) == true),
            new(KindAmnesia, "amnesia", "Amnesia", ["Automatic", "Off"],
                bot => bot.Spells?.Any(spell => spell.SpellType == eSpellType.Amnesia) == true),
            // casters and hybrids: Strict - no damage spells under 50% power unless the monster is under 30% (stored as 2)
            new(KindSavePower, "savepower", "Save power", ["Automatic", "Strict (no nukes under 50% power)"],
                bot => HasAttackSpell(bot) && !IsCaster(bot), [0, 2]),
            new(KindSavePowerCaster, "savepower", "Save power", ["Automatic", "Strict (no nukes or area under 50%)"],
                bot => HasAttackSpell(bot) && IsCaster(bot), [0, 2]),
            new(KindAttackSpells, "attackspells", "Attack spells", ["Automatic", "Area spells only (no single-target nukes)",
                    "None - buffs only (no damage or debuffs)"],
                bot => HasAttackSpell(bot) && IsCaster(bot)),
            // a stance of its own ("tank aggressive, healer defensive"); Automatic follows the player's /aggressive or
            // /defensive (Living Server has no /passive, True 1.65's fourth choice)
            new(KindStance, "stance", "Stance", ["Automatic (the group's stance)", "Aggressive", "Defensive"], _ => true),
            // crowd control: Off controls nothing (no mez or stun); True 1.65's Main CC and Adds only steer its veteran CC AI
            // how it talks (CompanionChat165.IsFormal): like any 2003 player in its own writing style, or True 1.65's formal
            // companion, which then keeps to its companion lines (no gamebot chat: one voice either way)
            new(KindSpeech, "speech", "Speech", ["Automatic (talks like a player)", "Formal (the True 1.65 companion)"], _ => true),
            new(KindCrowdControl, "cc", "Crowd control", ["Automatic", "Off (no mez or stun)"],
                bot => bot.CanCastCrowdControlSpells || bot.Spells?.Any(spell => spell?.SpellType is eSpellType.Mesmerize or
                    eSpellType.Mez or eSpellType.Stun) == true, [0, 3]),
            // #571 step 4 (True165 1ed3b24): who a buffer with single-target concentration buffs buffs first. Automatic: by job
            // (tanks, melee, healers, casters), base dexterity and power regen to healers and casters first
            new(KindBuffPriority, BuffPriorityKey, "Buff priority",
                ["Automatic (by job)", "Front line first", "Casters and healers first"], KnowsRealmConcentrationBuff),
        ];

        /// <summary>Whether a companion knows a single-target concentration buff it keeps up on its groupmates (the "Buff
        /// priority" row is offered then).</summary>
        public static bool KnowsRealmConcentrationBuff(GameBot bot) =>
            (bot.MiscSpells ?? []).Concat(bot.InstantMiscSpells ?? []).Any(spell => spell != null && spell.Level <= bot.Level &&
                spell.IsConcentration && spell.Target == eSpellTarget.REALM && BotBrain.IsMaintainableClassBuff(spell));

        /// <summary>A generic AI option's stored key and choices (choice 0 Automatic), or null for an unknown kind.</summary>
        /// <summary>The value a choice (from 0) of a generic row is stored as (True 1.65's numbers), or -1 for an unknown
        /// kind or choice.</summary>
        public static int StoredValue(byte kind, int choice) =>
            Generics.FirstOrDefault(g => g.Kind == kind) is { } option && choice >= 0 && choice < option.Choices.Length
                ? option.ValueOf(choice) : -1;

        public static (string Key, string[] Choices)? Option(byte kind) =>
            Generics.FirstOrDefault(g => g.Kind == kind) is { } option ? (option.Key, option.Choices) : null;

        /// <summary>A generic AI option's caption (the row's name in the AI Options tab), or null for an unknown kind.</summary>
        public static string Caption(byte kind) => Generics.FirstOrDefault(g => g.Kind == kind)?.Caption;

        private static bool HasAttackSpell(GameBot bot) =>
            HasDamageSpell((bot.HarmfulSpells ?? []).Concat(bot.InstantHarmfulSpells ?? []).Concat(bot.BoltSpells ?? []));

        private static bool IsCaster(GameBot bot) =>
            bot.CharacterClass != null && CompanionSpellOptions165.IsCasterClass((eCharacterClass)bot.CharacterClass.ID);

        /// <summary>Whether a spell list holds a damage spell (the "Save power" row is offered then).</summary>
        public static bool HasDamageSpell(IEnumerable<Spell> spells) => spells.Any(BotCasterPriority.IsDamage);

        /// <summary>Pure (Q7, veteran review 2026-11-11): a Cleric, Druid or Healer whose Role row is set to anything but
        /// Automatic - heal first then attack, attack, or melee and heal - takes part in the player's fights.</summary>
        public static bool HealerRoleFights(eCharacterClass characterClass, int roleChoice) =>
            roleChoice is 1 or 2 or 3 &&
            characterClass is eCharacterClass.Cleric or eCharacterClass.Druid or eCharacterClass.Healer;

        /// <summary>The player's choice for a generic AI option (0 = Automatic; always 0 for gamebots).</summary>
        public static int Choice(GameBot bot, string key) =>
            Get(bot, key) is { } v && int.TryParse(v, out int i) ? i : 0;

        // ---- debuffs (owner 2026-11-08: "pick which debuffs are used before nuking as long as they stack on a target, or
        // choose just one, or just two, or none ... if debuffs before nuking is on")

        /// <summary>One debuff the companion knows, per kind of effect (debuffs of different kinds stack on a target):
        /// its stored key and its highest-rank spell (for a Necromancer, the servant command).</summary>
        public sealed record DebuffLine(string Key, Spell Spell);

        /// <summary>The stored key of a debuff's kind: its effect, damage type and single/area shape.</summary>
        public static string DebuffLineKey(Spell payload) =>
            $"{(int)payload.SpellType}.{payload.DamageType}.{(payload.Radius > 0 ? "a" : "s")}";

        private static Spell DebuffPayload(Spell spell) =>
            spell?.SpellType == eSpellType.PetSpell && spell.SubSpellID > 0 ? SkillBase.GetSpellByID(spell.SubSpellID) : spell;

        private static bool IsChoosableDebuff(Spell spell, Spell payload) =>
            payload != null && (spell.SpellType == eSpellType.PetSpell
                ? BotBrain.IsServantDebuff(payload) && payload.SpellType is not (eSpellType.SpeedDecrease or eSpellType.UnbreakableSpeedDecrease)
                : BotCasterPriority.IsDebuff(spell));

        /// <summary>A Necromancer's servant command that carries a choosable debuff, or a choosable debuff itself.</summary>
        public static bool IsChoosableDebuffSpell(Spell spell) => spell != null && IsChoosableDebuff(spell, DebuffPayload(spell));

        /// <summary>The debuffs the companion can choose between, one per kind (its best rank), in level order.</summary>
        public static List<DebuffLine> DebuffLines(GameBot bot)
        {
            if (bot == null)
                return [];
            return (bot.HarmfulSpells ?? []).Concat(bot.InstantHarmfulSpells ?? []).Concat(bot.Spells ?? [])
                .Where(spell => spell != null && spell.Level <= bot.Level && spell.IsHarmful)
                .DistinctBy(spell => spell.ID)
                .Select(spell => (Spell: spell, Payload: DebuffPayload(spell)))
                .Where(entry => IsChoosableDebuff(entry.Spell, entry.Payload))
                .GroupBy(entry => DebuffLineKey(entry.Payload))
                .Select(group => new DebuffLine(group.Key, group.OrderByDescending(entry => entry.Spell.Level).First().Spell))
                .OrderBy(line => line.Spell.Level).ThenBy(line => line.Key, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The player's choice for one debuff: 0 Automatic, 1 Use, 2 Skip (always 0 for gamebots).</summary>
        public static int DebuffLineChoice(GameBot bot, Spell spellOrCommand) =>
            DebuffPayload(spellOrCommand) is { } payload && Get(bot, "dl." + DebuffLineKey(payload)) is { } v &&
            int.TryParse(v, out int i) ? i : 0;

        /// <summary>How many debuffs the player marked "Use" (0: the companion picks its own).</summary>
        public static int PickedDebuffCount(GameBot bot) =>
            HiredCompanions165.HiredRecord(bot) is { } hired ? Read(hired).Count(p => p.Key.StartsWith("dl.", StringComparison.Ordinal) && p.Value == "1") : 0;

        /// <summary>
        /// Pure: may this debuff be cast before nuking? master = "Debuff before nuking" (0 Automatic, 1 Always, 2 Never),
        /// line = this debuff's choice (0 Automatic, 1 Use, 2 Skip). With any debuff marked Use, only those are cast.
        /// </summary>
        public static bool DebuffAllowed(int master, int line, bool anyPicked) =>
            master != 2 && line != 2 && (!anyPicked || line == 1);

        // ---- storage: "key=value;key=value" in OfflineHiredCompanionRecord.Options

        /// <summary>The last parsed options of each hire record, keyed by the exact Options string it was parsed from
        /// (performance review 2026-11-11: every one of the 42+ option reads rebuilt this table). A changed Options
        /// string - Set, a reload - is a different string reference, so the next read parses again.</summary>
        private sealed record ParsedOptions(string Source, IReadOnlyDictionary<string, string> Map);

        private sealed class ParsedSlot
        {
            public ParsedOptions Value;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<OfflineHiredCompanionRecord, ParsedSlot>
            ParsedCache = new();

        private static readonly IReadOnlyDictionary<string, string> NoOptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Pure: "key=value;key=value" to a case-insensitive table (later keys win).</summary>
        public static Dictionary<string, string> Parse(string options)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in (options ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0)
                    map[pair[..eq]] = pair[(eq + 1)..];
            }
            return map;
        }

        /// <summary>The record's options, parsed once per Options string (read-only: Set works on its own copy).</summary>
        private static IReadOnlyDictionary<string, string> Read(OfflineHiredCompanionRecord hired)
        {
            if (hired == null)
                return NoOptions;
            string source = hired.Options ?? string.Empty;
            ParsedSlot slot = ParsedCache.GetOrCreateValue(hired);
            // one immutable (source, table) pair swapped as a whole: a reader never pairs a table with another string
            ParsedOptions cached = Volatile.Read(ref slot.Value);
            if (cached != null && ReferenceEquals(cached.Source, source))
                return cached.Map;
            IReadOnlyDictionary<string, string> map = Parse(source);
            Volatile.Write(ref slot.Value, new ParsedOptions(source, map));
            return map;
        }

        /// <summary>Whether the companion wears its cloak's hood up (/hood name; owner 2026-11-10: remembered).</summary>
        public static bool HoodUp(OfflineHiredCompanionRecord hired) =>
            Read(hired).TryGetValue("hood", out string value) && value == "1";

        public static void SetHoodUp(OfflineHiredCompanionRecord hired, bool up)
        {
            if (hired != null)
                Set(hired, "hood", up ? "1" : null);
        }

        private static string Get(GameBot bot, string key) =>
            HiredCompanions165.HiredRecord(bot) is { } hired && Read(hired).TryGetValue(key, out string value) ? value : null;

        private static void Set(OfflineHiredCompanionRecord hired, string key, string value)
        {
            Dictionary<string, string> map = Parse(hired.Options);
            if (value == null) map.Remove(key); else map[key] = value;
            hired.Options = string.Join(';', map.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
            HiredCompanions165.SaveRecord(hired);
        }

        // ---- what the AI reads

        /// <summary>
        /// False only when the player switched a hired companion's charming off. A hired Hunter's charming starts OFF (owner
        /// 2026-11-06: it keeps its wolf unless the player turns charming on); gamebots are unchanged.
        /// </summary>
        public static bool CharmAllowed(GameBot bot) => CharmAllowed(Get(bot, "charm"), IsHiredHunter(bot));

        /// <summary>Pure: the stored charm choice ("1" on, "0" off, null never set) for a Hunter or any other class.</summary>
        public static bool CharmAllowed(string stored, bool hiredHunter) => hiredHunter ? stored == "1" : stored != "0";

        private static bool IsHiredHunter(GameBot bot) =>
            bot?.CharacterClass?.ID == (int)eCharacterClass.Hunter && HiredCompanions165.IsHired(bot);

        /// <summary>The spell line of the pet the player chose (Cabalist, Enchanter), or null for the bot's own choice.</summary>
        public static string PetLine(GameBot bot) => HasPetChoice(bot) ? Get(bot, "pet") : null;

        /// <summary>
        /// The classes whose pets are a real choice (owner 2026-11-05): the Cabalist's simulacrums and the Enchanter's
        /// Underhill pets each have their own perks, the Theurgist picks an element. A Bonedancer's commander, a
        /// Spiritmaster's or a Necromancer's pet is always the best rank - no choice (a choice stored by the build that
        /// offered one is ignored).
        /// </summary>
        public static bool HasPetChoice(GameBot bot) =>
            bot?.CharacterClass != null &&
            (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Cabalist or eCharacterClass.Enchanter or eCharacterClass.Theurgist;

        /// <summary>The song or chant type chosen for a situation (1 fight, 2 standing around, 3 travelling), or null.</summary>
        public static eSpellType? Song(GameBot bot, int slot) =>
            Get(bot, $"song{slot}") is { } v && int.TryParse(v, out int type) ? (eSpellType)type : null;

        /// <summary>The Bonedancer's sub-pet plan: per slot null (bot's choice), -1 (none) or a sub-pet type.</summary>
        public static int?[] SubPets(GameBot bot)
        {
            var plan = new int?[3];
            for (int i = 0; i < 3; i++)
                plan[i] = Get(bot, $"sub{i + 1}") is { } v && int.TryParse(v, out int type) ? type : null;
            return plan;
        }

        // ---- what the window offers

        /// <summary>Ids: the spell behind each name (0 none), for the client's spell tooltips (owner 2026-11-08).</summary>
        private sealed record Row(byte Kind, byte Slot, int Chosen, List<string> Choices, int Used, int Cap, List<int> Ids = null);

        // a Hunter too, once it has learned a charm (owner 2026-11-06: charming on = no wolf, off = its wolf)
        private static bool CanCharm(GameBot bot) => bot.CharacterClass != null &&
            ((eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Sorcerer or eCharacterClass.Minstrel || LearnedCharm(bot));

        /// <summary>The companion knows a charm spell at its level (True 1.65's Charm165.LearnedCharm, as a test).</summary>
        public static bool LearnedCharm(GameBot bot) =>
            (bot?.Spells ?? []).Concat(bot?.HarmfulSpells ?? []).Concat(bot?.InstantHarmfulSpells ?? [])
            .Any(spell => spell != null && spell.SpellType == eSpellType.Charm && spell.Level <= bot.Level);

        // (True 1.65's pet, sub-pet and song rows - PetLines, SubPetKinds, Songs - steer pet and song AI Living Server does
        // not have; their stored keys are read by nothing here, so the rows are not offered.)

        private static List<Row> Rows(GameBot bot)
        {
            var rows = new List<Row>();
            if (bot?.CharacterClass == null)
                return rows;
            if (CanCharm(bot))
                rows.Add(new Row(KindCharm, 0, CharmAllowed(bot) ? 1 : 0, ["Off", "On"], 0, 0));
            foreach (Generic option in Generics)
            {
                if (!option.Applies(bot))
                    continue;
                rows.Add(new Row(option.Kind, 0, Math.Clamp(option.ChoiceOf(Choice(bot, option.Key)), 0, option.Choices.Length - 1),
                    [option.Caption, .. option.Choices], 0, 0));
                // under "Debuff before nuking": one row per debuff it knows (its best rank), slot = its place in the list;
                // names[0] is the debuff's name (the caption), Ids[0] its spell for the tooltip
                if (option.Kind == KindDebuff)
                {
                    List<DebuffLine> lines = DebuffLines(bot);
                    for (int i = 0; i < lines.Count && i < 250; i++)
                        rows.Add(new Row(KindDebuffLine, (byte)(i + 1), Math.Clamp(DebuffLineChoice(bot, lines[i].Spell), 0, 2),
                            [lines[i].Spell.Name, "Automatic", "Use", "Skip"], 0, 0, [lines[i].Spell.ID, 0, 0, 0]));
                }
            }
            return rows;
        }

        /// <summary>One AI Options row as the chat list shows it (/hire options): its caption, choices and the chosen one.</summary>
        public sealed record OptionRow(byte Kind, byte Slot, string Caption, IReadOnlyList<string> Choices, int Chosen);

        /// <summary>The rows the window shows, for the chat list (the original client without its overlay). The charm,
        /// pet, sub-pet and song rows get a caption here; the generic rows carry their own as names[0].</summary>
        public static IReadOnlyList<OptionRow> TextRows(GameBot bot)
        {
            var list = new List<OptionRow>();
            foreach (Row row in Rows(bot))
            {
                switch (row.Kind)
                {
                    case KindCharm:
                        list.Add(new OptionRow(row.Kind, row.Slot, "Charm", row.Choices, row.Chosen));
                        break;
                    case KindPet:
                        list.Add(new OptionRow(row.Kind, row.Slot, "Pet", row.Choices, row.Chosen));
                        break;
                    case KindSubPet:
                        list.Add(new OptionRow(row.Kind, row.Slot, $"Sub-pet {row.Slot} ({row.Used} of {row.Cap} levels)", row.Choices, row.Chosen));
                        break;
                    case KindSong:
                        list.Add(new OptionRow(row.Kind, row.Slot, row.Slot switch { 1 => "Song in a fight", 2 => "Song standing around", _ => "Song traveling" },
                            row.Choices, row.Chosen));
                        break;
                    default:
                        // generic rows: names[0] is the caption (a debuff line's own name), the rest the choices
                        list.Add(new OptionRow(row.Kind, row.Slot, row.Choices.Count > 0 ? row.Choices[0] : "?", row.Choices.Skip(1).ToList(), row.Chosen));
                        break;
                }
            }
            return list;
        }

        /// <summary>Pure: the window's own words for choosing <paramref name="choice"/> (from 0) on a row: the verb, then
        /// its arguments after the companion's row (as Handle reads them).</summary>
        public static string[] CommandFor(OptionRow row, int choice) => row.Kind switch
        {
            KindCharm => ["charm", choice == 1 ? "on" : "off"],
            KindPet => ["pet", choice.ToString()],
            KindSubPet => ["subpet", row.Slot.ToString(), choice.ToString()],
            KindSong => ["song", row.Slot.ToString(), choice.ToString()],
            _ => ["option", row.Kind.ToString(), row.Slot.ToString(), choice.ToString()],
        };

        public static void Send(GamePlayer player, int row, GameBot bot)
        {
            foreach (Row option in Rows(bot))
            {
                var body = new List<byte> { (byte)row, option.Kind, option.Slot, (byte)Math.Clamp(option.Chosen, 0, 255),
                    (byte)Math.Min(option.Choices.Count, 255),
                    (byte)option.Used, (byte)(option.Used >> 8), (byte)option.Cap, (byte)(option.Cap >> 8) };
                foreach (string name in option.Choices.Take(255))
                {
                    body.AddRange(Encoding.ASCII.GetBytes(name.Length > 60 ? name[..60] : name));
                    body.Add(0);
                }
                // then, for rows with spells, one u32 spell id per name (0 = none) for the tooltips (owner 2026-11-08);
                // older clients stop reading after the names
                if (option.Ids != null)
                    for (int i = 0; i < Math.Min(option.Choices.Count, 255); i++)
                    {
                        int id = i < option.Ids.Count ? option.Ids[i] : 0;
                        body.AddRange([(byte)id, (byte)(id >> 8), (byte)(id >> 16), (byte)(id >> 24)]);
                    }
                Hire165.Send(player, 14, body.ToArray());
            }
            Hire165.Send(player, 14, [(byte)row, 0xFF]);
        }

        // ---- the verbs

        /// <summary>Applies a choice; null when done, otherwise the problem to show.</summary>
        public static string Handle(GameBot bot, OfflineHiredCompanionRecord hired, string verb, string[] args)
        {
            if (bot == null || hired == null)
                return "Summon them first: they are waiting (dismissed).";
            string Arg(int i) => args.Length > i ? args[i] : string.Empty;
            int Int(int i) => int.TryParse(Arg(i), out int v) ? v : -1;
            switch (verb)
            {
                case "charm":
                {
                    if (!CanCharm(bot)) return $"{bot.Name} does not charm.";
                    bool on = Arg(3).Equals("on", StringComparison.OrdinalIgnoreCase) || Arg(3).Equals("yes", StringComparison.OrdinalIgnoreCase);
                    bool hunter = IsHiredHunter(bot);
                    // a Hunter's default is off, so its "on" is stored; every other class's default is on
                    Set(hired, "charm", on ? (hunter ? "1" : null) : (hunter ? null : "0"));
                    // switched off with a charmed pet: it is let go now
                    if (!on && bot.ControlledBrain?.Body is GameNPC charmed && charmed is not GameSummonedPet)
                        bot.CommandNpcRelease();
                    return null;
                }
                case "option" when Int(3) == KindDebuffLine:
                {
                    int slot = Int(4), i = Int(5);
                    List<DebuffLine> lines = DebuffLines(bot);
                    if (slot < 1 || slot > lines.Count || i is < 0 or > 2) return "Choose a debuff.";
                    Set(hired, "dl." + lines[slot - 1].Key, i == 0 ? null : i.ToString());
                    return null;
                }
                case "option":
                {
                    int kind = Int(3), i = Int(5);
                    Generic option = Generics.FirstOrDefault(g => g.Kind == kind && g.Applies(bot));
                    if (option == null || i < 0 || i >= option.Choices.Length)
                        return "Choose an option.";
                    int value = option.ValueOf(i);
                    Set(hired, option.Key, value == 0 ? null : value.ToString());
                    // the Hunter's pet switched off: the wolf that is out goes now
                    if (option.Kind == KindHunterPet && value == 1 && bot.ControlledBrain != null)
                        bot.CommandNpcRelease();
                    Log.Info($"COMPANION_OPTION_165 bot=\"{bot.Name}\" id={bot.DatabaseID} {option.Key}={value}");
                    return null;
                }
                case "pet" or "subpet" or "song":
                    return "That choice is not offered on this server.";
            }
            return null;
        }
    }
}
