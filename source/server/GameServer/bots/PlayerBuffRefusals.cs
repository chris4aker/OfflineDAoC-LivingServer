using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// The buffs a player refuses from bots (#661; owner, 2026-11-26). <c>/buff refuse &lt;short&gt;</c> (the short names
    /// <c>/buff</c> knows: ds, bt, da, ...) and a right-click cancel of a buff a bot cast on the player both refuse that
    /// buff type until <c>/buff allow &lt;short&gt;</c>. Every bot caster then leaves the type off the player and the
    /// player's pets: companion <c>/bot</c>s, hired and <c>/spawn</c> companions, autonomous gamebots, No Lifer buffbots and
    /// those bots' own pets. Players buffing players are unaffected, and nothing is refused by default (damage shield stays
    /// on for a player who never refuses it).
    /// <list type="bullet">
    /// <item>Saved on the character: one <c>DOLCharactersXCustomParam</c> row (<see cref="Param"/>, a comma list of short
    /// names), a saved-progress table that release packages empty (progress-policy.json).</item>
    /// <item>Bots check it when they pick a target (<see cref="BotBuffTargetPolicy.Benefits(GameLiving, Spell)"/>, the
    /// filter of every group, realm and pet buff pick in BotBrain), in the No Lifer buffbot plan
    /// (<c>NoLiferBuffBots.PlanBuffs</c>), and once more where a spell lands (<c>SpellHandler.StartSpell</c>), so a group
    /// buff a bot casts for its party skips the refusing player and pet.</item>
    /// </list>
    /// Real players only: gamebots and companions never refuse anything.
    /// </summary>
    public static class PlayerBuffRefusals
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(PlayerBuffRefusals));

        /// <summary>The character's saved refusals (DOLCharactersXCustomParam, a saved-progress table).</summary>
        public const string Param = "LivingServer.BuffRefusals";

        /// <summary>
        /// The short names <c>/buff</c> uses, in its order (commands/playercommands/buff.cs reads them from here). Every one
        /// can be refused.
        /// </summary>
        public static readonly (string Short, eSpellType Type)[] ShortNames =
        [
            ("str", eSpellType.StrengthBuff),
            ("con", eSpellType.ConstitutionBuff),
            ("dex", eSpellType.DexterityBuff),
            ("sc", eSpellType.StrengthConstitutionBuff),
            ("dq", eSpellType.DexterityQuicknessBuff),
            ("acu", eSpellType.AcuityBuff),
            ("af", eSpellType.BaseArmorFactorBuff),
            ("saf", eSpellType.SpecArmorFactorBuff),
            ("paf", eSpellType.PaladinArmorFactorBuff),
            ("abs", eSpellType.ArmorAbsorptionBuff),
            ("cel", eSpellType.CelerityBuff),
            ("has", eSpellType.CombatSpeedBuff),
            ("da", eSpellType.DamageAdd),
            ("ds", eSpellType.DamageShield),
            ("dam", eSpellType.MeleeDamageBuff),
            ("hreg", eSpellType.HealthRegenBuff),
            ("hot", eSpellType.HealOverTime),
            ("preg", eSpellType.PowerRegenBuff),
            ("pot", eSpellType.PowerOverTime),
            ("ereg", eSpellType.EnduranceRegenBuff),
            ("fat", eSpellType.FatigueConsumptionBuff),
            ("mez", eSpellType.MesmerizeDurationBuff),
            ("bt", eSpellType.Bladeturn),
            ("dproc", eSpellType.DefensiveProc),
            ("oproc", eSpellType.OffensiveProc),
            ("hcm", eSpellType.HeatColdMatterBuff),
            ("cold", eSpellType.ColdResistBuff),
            ("matt", eSpellType.MatterResistBuff),
            ("heat", eSpellType.HeatResistBuff),
            ("bse", eSpellType.BodySpiritEnergyBuff),
            ("body", eSpellType.BodyResistBuff),
            ("spir", eSpellType.SpiritResistBuff),
            ("ener", eSpellType.EnergyResistBuff),
        ];

        private static readonly Dictionary<string, eSpellType> ByShort =
            ShortNames.ToDictionary(entry => entry.Short, entry => entry.Type, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<eSpellType, string> ByType = ShortNames.ToDictionary(entry => entry.Type, entry => entry.Short);

        #region Names and the saved form (pure)

        /// <summary>The spell type of a <c>/buff</c> short name (any case). Pure.</summary>
        public static bool TryParse(string shortName, out eSpellType type)
        {
            type = default;
            return !string.IsNullOrWhiteSpace(shortName) && ByShort.TryGetValue(shortName.Trim(), out type);
        }

        /// <summary>The <c>/buff</c> short name of a type, or null when it has none (it can't be refused). Pure.</summary>
        public static string ShortNameOf(eSpellType type) => ByType.TryGetValue(type, out string name) ? name : null;

        /// <summary>Whether a type has a short name, so it can be refused. Pure.</summary>
        public static bool IsRefusable(eSpellType type) => ByType.ContainsKey(type);

        /// <summary>The saved form: short names in <c>/buff</c>'s order, comma separated ("da,ds,bt"). Pure.</summary>
        public static string Format(IEnumerable<eSpellType> types)
        {
            var set = new HashSet<eSpellType>(types ?? []);
            return string.Join(",", ShortNames.Where(entry => set.Contains(entry.Type)).Select(entry => entry.Short));
        }

        /// <summary>The saved form read back; a name this build doesn't know is dropped. Pure.</summary>
        public static HashSet<eSpellType> ParseStored(string value)
        {
            var set = new HashSet<eSpellType>();
            foreach (string part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (TryParse(part, out eSpellType type))
                    set.Add(type);
            return set;
        }

        /// <summary>What <c>/buff refuse|allow</c> asks for.</summary>
        public enum CommandKind
        {
            /// <summary>Not a refusal command: the ordinary <c>/buff</c> cast.</summary>
            None,
            /// <summary><c>/buff refuse</c> with nothing after it: list the refusals.</summary>
            List,
            Refuse,
            Allow,
        }

        /// <summary>A parsed <c>/buff refuse|allow</c>: the types named, the names not known, and "allow all".</summary>
        public sealed record Command(CommandKind Kind, IReadOnlyList<eSpellType> Types, IReadOnlyList<string> Unknown, bool All);

        /// <summary>
        /// Reads <c>/buff refuse [short[,short...]]</c> and <c>/buff allow &lt;short[,short...]|all&gt;</c> (names may also be
        /// separated by spaces). Anything else is <see cref="CommandKind.None"/>. Pure.
        /// </summary>
        public static Command ParseCommand(string[] args)
        {
            if (args == null || args.Length < 2)
                return new Command(CommandKind.None, [], [], false);
            string verb = args[1].Trim();
            bool refuse = verb.Equals("refuse", StringComparison.OrdinalIgnoreCase);
            bool allow = verb.Equals("allow", StringComparison.OrdinalIgnoreCase);
            if (!refuse && !allow)
                return new Command(CommandKind.None, [], [], false);
            string[] names = args.Skip(2)
                .SelectMany(arg => arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(name => name.Length > 0)
                .ToArray();
            if (names.Length == 0)
                return new Command(refuse ? CommandKind.List : CommandKind.Allow, [], [], false);
            bool all = allow && names.Any(name => name.Equals("all", StringComparison.OrdinalIgnoreCase));
            var types = new List<eSpellType>();
            var unknown = new List<string>();
            foreach (string name in names)
            {
                if (all && name.Equals("all", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (TryParse(name, out eSpellType type))
                {
                    if (!types.Contains(type))
                        types.Add(type);
                }
                else
                    unknown.Add(name);
            }
            return new Command(refuse ? CommandKind.Refuse : CommandKind.Allow, types, unknown, all);
        }

        #endregion

        #region Who refuses, who is a bot (live, no database)

        private sealed class Holder
        {
            public readonly object Lock = new();
            public volatile HashSet<eSpellType> Types = new();
            public volatile bool Loaded;
        }

        private static readonly ConditionalWeakTable<GamePlayer, Holder> Live = new();

        /// <summary>
        /// The real player whose refusals cover this target: the player, or the player whose pet it is (through a
        /// Bonedancer's or a pet's own sub-pets). Null for a bot, a bot's pet or a companion's pet. Pure walk, no database.
        /// </summary>
        public static GamePlayer RefuserOf(GameLiving target)
        {
            GameLiving current = target;
            for (int depth = 0; depth < 8 && current != null; depth++)
            {
                switch (current)
                {
                    case GamePlayer player:
                        return player;
                    case GameBot:
                        return null;
                    case GameNPC npc when npc.Brain is IControlledBrain brain:
                        current = brain.Owner;
                        continue;
                    default:
                        return null;
                }
            }
            return null;
        }

        /// <summary>
        /// Whether a caster is a bot: a gamebot, a companion or a buffbot (all <see cref="GameBot"/>), or a pet a bot owns.
        /// A player and a player's pet are not. Pure walk.
        /// </summary>
        public static bool IsBotCaster(GameLiving caster)
        {
            GameLiving current = caster;
            for (int depth = 0; depth < 8 && current != null; depth++)
            {
                switch (current)
                {
                    case GameBot:
                        return true;
                    case GameNPC npc when npc.Brain is IControlledBrain brain:
                        current = brain.Owner;
                        continue;
                    default:
                        return false;
                }
            }
            return false;
        }

        /// <summary>The player's refusals now (read from the character once, on first use).</summary>
        public static IReadOnlyCollection<eSpellType> Of(GamePlayer player)
        {
            if (player == null)
                return Array.Empty<eSpellType>();
            Holder holder = HolderOf(player);
            return holder.Types;
        }

        /// <summary>Whether this player refuses this type from bots.</summary>
        public static bool Refuses(GamePlayer player, eSpellType type) =>
            player != null && IsRefusable(type) && HolderOf(player).Types.Contains(type);

        /// <summary>Whether a bot should leave this type off this target (a refusing player, or that player's pet).</summary>
        public static bool Refuses(GameLiving target, eSpellType type) =>
            IsRefusable(type) && RefuserOf(target) is GamePlayer player && Refuses(player, type);

        /// <summary>Whether a helpful spell from this caster must not land on this target (SpellHandler.StartSpell).</summary>
        public static bool BlocksBotBuff(GameLiving caster, GameLiving target, Spell spell) =>
            spell != null && caster != null && target != null && IsRefusable(spell.SpellType) && IsBotCaster(caster) &&
            RefuserOf(target) is GamePlayer player && Refuses(player, spell.SpellType);

        private static Holder HolderOf(GamePlayer player)
        {
            Holder holder = Live.GetValue(player, _ => new Holder());
            if (!holder.Loaded)
            {
                lock (holder.Lock)
                {
                    if (!holder.Loaded)
                    {
                        string id = player.DBCharacter?.ObjectId;
                        if (!string.IsNullOrEmpty(id) && Database() is IObjectDatabase database)
                            holder.Types = Load(database, id);
                        holder.Loaded = true;
                    }
                }
            }
            return holder;
        }

        private static IObjectDatabase Database()
        {
            try
            {
                return GameServer.Instance == null ? null : GameServer.Database;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region Saved with the character

        /// <summary>The refusals saved for a character (empty when none, or unreadable).</summary>
        public static HashSet<eSpellType> Load(IObjectDatabase database, string characterId)
        {
            if (database == null || string.IsNullOrEmpty(characterId))
                return new HashSet<eSpellType>();
            try
            {
                DbCoreCharacterXCustomParam row = database.SelectObject<DbCoreCharacterXCustomParam>(
                    DB.Column("DOLCharactersObjectId").IsEqualTo(characterId).And(DB.Column("KeyName").IsEqualTo(Param)));
                return ParseStored(row?.Value);
            }
            catch (Exception exception)
            {
                Log.Warn($"BUFF_REFUSAL could not read the refusals of character {characterId}", exception);
                return new HashSet<eSpellType>();
            }
        }

        /// <summary>Saves a character's refusals: one row, removed when there are none. True when saved.</summary>
        public static bool Save(IObjectDatabase database, string characterId, IEnumerable<eSpellType> types)
        {
            if (database == null || string.IsNullOrEmpty(characterId))
                return false;
            string value = Format(types);
            try
            {
                DbCoreCharacterXCustomParam row = database.SelectObject<DbCoreCharacterXCustomParam>(
                    DB.Column("DOLCharactersObjectId").IsEqualTo(characterId).And(DB.Column("KeyName").IsEqualTo(Param)));
                if (value.Length == 0)
                    return row == null || database.DeleteObject(row);
                if (row == null)
                    return database.AddObject(new DbCoreCharacterXCustomParam(characterId, Param, value));
                row.Value = value;
                return database.SaveObject(row);
            }
            catch (Exception exception)
            {
                Log.Warn($"BUFF_REFUSAL could not save the refusals of character {characterId}", exception);
                return false;
            }
        }

        #endregion

        #region Changes

        /// <summary>Refuses types (saved with the character). Returns the ones that were not refused before.</summary>
        public static IReadOnlyList<eSpellType> Refuse(GamePlayer player, IEnumerable<eSpellType> types, string why) =>
            Change(player, types, refuse: true, why);

        /// <summary>Takes types again (saved with the character). Returns the ones that had been refused.</summary>
        public static IReadOnlyList<eSpellType> Allow(GamePlayer player, IEnumerable<eSpellType> types, string why) =>
            Change(player, types, refuse: false, why);

        private static IReadOnlyList<eSpellType> Change(GamePlayer player, IEnumerable<eSpellType> types, bool refuse, string why)
        {
            if (player == null)
                return [];
            Holder holder = HolderOf(player);
            var changed = new List<eSpellType>();
            HashSet<eSpellType> next;
            lock (holder.Lock)
            {
                next = new HashSet<eSpellType>(holder.Types);
                foreach (eSpellType type in types ?? [])
                {
                    if (!IsRefusable(type))
                        continue;
                    if (refuse ? next.Add(type) : next.Remove(type))
                        changed.Add(type);
                }
                if (changed.Count == 0)
                    return changed;
                holder.Types = next;
            }
            string id = player.DBCharacter?.ObjectId;
            if (!string.IsNullOrEmpty(id) && Database() is IObjectDatabase database)
                Save(database, id, next);
            if (Log.IsInfoEnabled)
                Log.Info($"BUFF_REFUSAL player=\"{player.Name}\" {(refuse ? "refused" : "allowed")}=\"{Format(changed)}\" " +
                         $"now=\"{Format(next)}\" why=\"{why}\"");
            return changed;
        }

        /// <summary>
        /// The player right-clicked an effect off (PlayerCancelsEffectHandler). A helpful spell a bot cast is refused until
        /// <c>/buff allow</c>, and the player is told once. True when it was refused now.
        /// </summary>
        public static bool OnPlayerCancelled(GamePlayer player, ISpellHandler handler)
        {
            Spell spell = handler?.Spell;
            if (player == null || spell == null || !handler.HasPositiveEffect || !IsRefusable(spell.SpellType) ||
                !IsBotCaster(handler.Caster))
                return false;
            if (Refuse(player, [spell.SpellType], $"cancelled {spell.Name} from {handler.Caster?.Name}").Count == 0)
                return false;
            player.Out?.SendMessage(CancelledLine(spell.Name, ShortNameOf(spell.SpellType)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return true;
        }

        /// <summary>What the player is told when a cancel refuses a buff (owner, 2026-11-26). Pure.</summary>
        public static string CancelledLine(string spellName, string shortName) =>
            $"You refused {spellName}; /buff allow {shortName} to take it again";

        /// <summary>Ends the bot-cast effects of these types on the player and the player's pets (after /buff refuse).</summary>
        public static int EndBotBuffs(GamePlayer player, IReadOnlyCollection<eSpellType> types)
        {
            if (player == null || types == null || types.Count == 0)
                return 0;
            int ended = 0;
            var targets = new List<GameLiving> { player };
            if (player.ControlledBrain?.Body != null)
            {
                var visited = new HashSet<IControlledBrain>();
                targets.AddRange(BotGroupPetBuffTargets.AttachedTree(player.ControlledBrain, player, visited));
            }
            foreach (GameLiving target in targets.Distinct())
            {
                List<ECSGameSpellEffect> effects = target.effectListComponent?.GetSpellEffects();
                if (effects == null)
                    continue;
                foreach (ECSGameSpellEffect effect in effects)
                {
                    ISpellHandler handler = effect?.SpellHandler;
                    if (handler?.Spell == null || !types.Contains(handler.Spell.SpellType) || !IsBotCaster(handler.Caster))
                        continue;
                    if (effect.End())
                        ended++;
                }
            }
            return ended;
        }

        #endregion

        #region What /buff refuse|allow says (pure)

        private static string Describe(IEnumerable<eSpellType> types) =>
            string.Join(", ", ShortNames.Where(entry => types.Contains(entry.Type)).Select(entry => $"{entry.Short} ({entry.Type})"));

        /// <summary>The list of refusals, as <c>/buff refuse</c> with no argument shows it. Pure.</summary>
        public static string ListLine(IReadOnlyCollection<eSpellType> refused) =>
            refused == null || refused.Count == 0
                ? "You refuse no buffs from bots. /buff refuse <short> stops bots casting one on you and your pet (/buff help lists the short names)."
                : $"You refuse these buffs from bots: {Describe(refused)}. They aren't cast on you or your pet. /buff allow <short> takes one again.";

        /// <summary>What <c>/buff refuse|allow</c> says after a change. Pure.</summary>
        public static IReadOnlyList<string> ResultLines(Command command, IReadOnlyCollection<eSpellType> changed,
            IReadOnlyCollection<eSpellType> nowRefused)
        {
            var lines = new List<string>();
            foreach (string name in command.Unknown)
                lines.Add($"\"{name}\" is not a buff short name. /buff help lists them.");
            if (command.Kind == CommandKind.Refuse)
            {
                if (changed.Count > 0)
                    lines.Add($"You refuse {Describe(changed)}: bots won't cast it on you or your pet. /buff allow " +
                              $"{Format(changed)} to take it again.");
                List<eSpellType> already = command.Types.Where(type => !changed.Contains(type)).ToList();
                if (already.Count > 0)
                    lines.Add($"You already refuse {Describe(already)}.");
            }
            else if (command.Kind == CommandKind.Allow)
            {
                if (changed.Count > 0)
                    lines.Add($"You take {Describe(changed)} again: bots may cast it on you and your pet.");
                List<eSpellType> notRefused = command.Types.Where(type => !changed.Contains(type)).ToList();
                if (notRefused.Count > 0)
                    lines.Add($"You weren't refusing {Describe(notRefused)}.");
                if (command.All && changed.Count == 0)
                    lines.Add("You weren't refusing any buffs.");
                if (!command.All && command.Types.Count == 0 && command.Unknown.Count == 0)
                    lines.Add("Usage: /buff allow <short[,short...]|all>. " + ListLine(nowRefused));
            }
            return lines;
        }

        #endregion
    }
}
