using System;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// #568 C2 and C3 (owner decision 2026-11-26), ported from zomboden/OfflineDAoC-True165 a95ef07c4 (last 9422a13, GPL-3.0,
    /// bots/BotControlSpells.cs): crowd control on the bot's own target used like a veteran player. Against a monster no
    /// mez, root or nearsight on the kill target (the next hit breaks it, or it only slows the kill); a snare only on a
    /// runner; a stun only to interrupt a cast or peel the monster off a non-tank; a long-reuse stun only in an emergency.
    /// Against a player-like enemy (PvP) the old control stays. Snare-nukes count as damage. Adds keep their own rules
    /// (TryPveAddControl, TryBardPveAddMez, TryShamanPveAddRoot and the raid CC posts are not read through this).
    /// True165 gates its rules behind Classic165Mode; Living Server has no such mode, so these are always on
    /// (owner 2026-11-26: no switch). Gamebots and companions alike; real players are not affected.
    /// </summary>
    public static class BotControlSpells
    {
        public enum ControlKind
        {
            None,
            Mez,
            Root,
            Snare,
            Stun,
            Nearsight,
        }

        /// <summary>A monster under this health that moves away from its fight is a runner.</summary>
        public const int RunnerHealthPercent = 35;
        /// <summary>A reuse of a minute or more (Paralyzing Glance, Hypnotic Harmony) is an emergency spell.</summary>
        public const int LongReuseMilliseconds = 60_000;
        /// <summary>A plain nuke doing at least this share of the snare-nuke's damage is "similar power".</summary>
        public const double PlainNukeDamageShare = 0.8;

        /// <summary>The kind of control a spell's own type gives (no bomber payload lookup).</summary>
        public static ControlKind KindOfType(Spell spell) => spell?.SpellType switch
        {
            eSpellType.Mesmerize or eSpellType.Mez => ControlKind.Mez,
            eSpellType.Stun => ControlKind.Stun,
            eSpellType.Nearsight => ControlKind.Nearsight,
            eSpellType.SpeedDecrease or eSpellType.UnbreakableSpeedDecrease =>
                spell.Value >= 99 ? ControlKind.Root : ControlKind.Snare,
            _ => ControlKind.None,
        };

        /// <summary>The kind of control a spell gives; an Animist bomber is classified by its payload.</summary>
        public static ControlKind KindWith(Spell spell, Func<int, Spell> resolve)
        {
            if (spell?.SpellType == eSpellType.Bomber)
                return spell.SubSpellID > 0 && resolve != null ? KindOfType(resolve(spell.SubSpellID)) : ControlKind.None;
            return KindOfType(spell);
        }

        private sealed class KindBox
        {
            public readonly ControlKind Kind;
            public KindBox(ControlKind kind) => Kind = kind;
        }

        // the bomber payload kind cached on each bomber Spell (thread-safe, dies with the Spell)
        private static readonly ConditionalWeakTable<Spell, KindBox> BomberKinds = new();

        /// <summary>The kind of control a spell gives, using the loaded spell table for bomber payloads.</summary>
        public static ControlKind Kind(Spell spell)
        {
            if (spell?.SpellType != eSpellType.Bomber)
                return KindOfType(spell);
            if (spell.SubSpellID <= 0)
                return ControlKind.None;
            if (BomberKinds.TryGetValue(spell, out KindBox box))
                return box.Kind;
            return BomberKinds.GetValue(spell,
                bomber => new KindBox(KindOfType(SkillBase.GetSpellByID(bomber.SubSpellID)))).Kind;
        }

        public static bool IsControl(Spell spell) => Kind(spell) != ControlKind.None;
        public static bool IsControlWith(Spell spell, Func<int, Spell> resolve) => KindWith(spell, resolve) != ControlKind.None;

        /// <summary>A bomber carrying a control payload (Animist Vines, Constraining..Binding Spirit roots).</summary>
        public static bool IsControlBomber(Spell spell) =>
            spell?.SpellType == eSpellType.Bomber && Kind(spell) != ControlKind.None;

        public static bool IsControlBomberWith(Spell spell, Func<int, Spell> resolve) =>
            spell?.SpellType == eSpellType.Bomber && KindWith(spell, resolve) != ControlKind.None;

        public static bool IsStun(Spell spell) => spell?.SpellType == eSpellType.Stun;

        public static bool IsLongReuse(Spell spell) =>
            spell != null && spell.HasRecastDelay && spell.RecastDelay >= LongReuseMilliseconds;

        /// <summary>
        /// Pure rule (C2): control on the bot's own (attacked) target. Against a player-like enemy anything goes (PvP keeps
        /// its control). Against a monster: never a mez, root or nearsight; a snare only on a runner; a stun only when the
        /// stun rule says so.
        /// </summary>
        public static bool AllowsOnOwnTarget(ControlKind kind, bool playerLikeTarget, bool runner, bool stunWorthwhile) =>
            kind switch
            {
                ControlKind.None => true,
                _ when playerLikeTarget => true,
                ControlKind.Snare => runner,
                ControlKind.Stun => stunWorthwhile,
                _ => false,
            };

        /// <summary>Pure rule: a runner moves, is low on health and is not in melee with whoever it fights.</summary>
        public static bool IsRunner(bool moving, int healthPercent, bool inMeleeWithVictim) =>
            moving && healthPercent < RunnerHealthPercent && !inMeleeWithVictim;

        /// <summary>
        /// Pure rule: a stun interrupts a cast or peels a monster off a non-tank, never filler; a long-reuse stun only in
        /// an emergency (three or more adds on the group).
        /// </summary>
        public static bool StunWorthwhile(bool targetCasting, bool peelsNonTank, bool longReuse, bool emergency) =>
            (targetCasting || peelsNonTank) && (!longReuse || emergency);

        /// <summary>
        /// Rule (C5): while a caster holds its damage for the tank or pet only real debuffs and an interrupt/peel stun stay;
        /// snares, roots, mez and nearsight go too, except against a player-like enemy (PvP keeps its control).
        /// </summary>
        public static bool HoldDamageStrips(Spell spell, bool playerLikeTarget) =>
            BotCasterPriority.IsDamage(spell) || !playerLikeTarget && IsControl(spell) && !IsStun(spell);

        /// <summary>
        /// Pure rule (C3): with a groupmate (or pet) holding the target, a plain single-target nuke of similar power is
        /// cast instead of a snare-nuke (the snare is wasted on a held monster).
        /// </summary>
        public static bool PrefersPlainNuke(Spell snareNuke, Spell plain, bool groupHoldsTarget) =>
            groupHoldsTarget && snareNuke?.SpellType == eSpellType.DamageSpeedDecrease && plain != null &&
            plain.SpellType is eSpellType.DirectDamage or eSpellType.Bolt && plain.Radius <= 0 && snareNuke.Radius <= 0 &&
            plain.Damage >= snareNuke.Damage * PlainNukeDamageShare;
    }
}
