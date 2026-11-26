using System;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>What an attack spell is for, as the playbook sees it (worked out once when the spell list is built).</summary>
    public enum PlaybookSpellKind : byte
    {
        /// <summary>Anything the playbook does not rank on its own (summons, utility): cast only when nothing better is left.</summary>
        Other,
        /// <summary>A stat, attack-speed, armor or disease debuff.</summary>
        Debuff,
        /// <summary>A magic resist debuff for a damage type the caster's own attack spells deal.</summary>
        ResistDebuff,
        /// <summary>A magic resist debuff for a damage type none of the caster's attack spells deal (Void Eldritch's
        /// body, spirit and energy debuffs under its cold nukes): skipped unless the player picked it.</summary>
        OffResistDebuff,
        /// <summary>A slash, crush or thrust resist debuff: for groups and for casters who melee.</summary>
        MeleeResistDebuff,
        Dot,
        Lifedrain,
        Nuke,
        SnareNuke,
        AreaNuke,
        Bolt,
        /// <summary>Snare, nearsight, stun and the like (their own rules decide when they may go).</summary>
        Control,
        /// <summary>A Theurgist elemental or an Animist turret: the pet IS the damage, so it keeps the share of casts it had
        /// before the playbook (<see cref="BotPlaybook.PetSummonTurn"/>); its own pet rules decide the rest.</summary>
        PetSummon,
    }

    /// <summary>The fight as the playbook needs it, read once per cast decision (all cheap fields).</summary>
    public readonly struct PlaybookContext
    {
        public int HealthPercent { get; init; }
        public int PowerPercent { get; init; }
        public int TargetHealthPercent { get; init; }
        /// <summary>A fight that lasts: a player-like enemy, grouped, or a monster of the caster's level or above.</summary>
        public bool FightLasts { get; init; }
        /// <summary>How many attack spells this bot has started on this target so far.</summary>
        public int CastsOnTarget { get; init; }
        /// <summary>The bot's pet is the one being hit.</summary>
        public bool PetUnderAttack { get; init; }
        /// <summary>A snare is worth it: an enemy player, or a monster coming at a solo caster, not slowed yet.</summary>
        public bool SnareWanted { get; init; }
    }

    /// <summary>
    /// #568 C6 (owner decision 2026-11-26), ported from zomboden/OfflineDAoC-True165 66f782d8d (GPL-3.0, bots/BotPlaybook.cs;
    /// its veteran review M1, 2026-11-11): the "veteran playbook". Instead of a random pick among the attack spells that are
    /// legal now, each bot (gamebot or hired companion) keeps a small table worked out once whenever its spell list is
    /// built (level, training, respec: GameBot.SortSpells) and reads it in order on each cast:
    /// <list type="number">
    /// <item>a lifetap when hurt (under 60% health) or when the pet is being hit (Prima, Bonedancer: "lifetap at ~60% HP or
    /// when a pet is aggroed");</item>
    /// <item>the bolt as the opener (Prima, Wizard: "Bolt, Blast, Root, Run, Blast, Blast"; Wizards and Theurgists bolt only
    /// as the opener, "forget bolts in large battles");</item>
    /// <item>a snare nuke on a runner or on a monster closing on a solo caster;</item>
    /// <item>the DoT on a fight that lasts while the target is at half health or more, re-applied only when it expired
    /// (Prima, Cabalist: "throw your strongest DoT at the monster, then drain it");</item>
    /// <item>the best single-target nuke by damage per cast - a Cabalist's drain first - and under 30% power the nuke with
    /// the most damage per power point;</item>
    /// <item>area nukes only when nothing single-target is left (the 2+ monster case is ChooseAreaDamage's, before this);</item>
    /// <item>then debuffs, control and anything else, highest level first.</item>
    /// </list>
    /// Resist debuffs count only for the caster's own damage types. No spell, damage, cost or range number changes: only
    /// the order of casts that were already legal. The spells come from what the bot knows, which its era build
    /// (docs/ERA-BUILDS-1.2.md) decides, so no separate spell list is kept. The skill-mistake layer (BotSkillMistakes) still
    /// sits on top as the human variation. The companion's AI Options still filter the list before it gets here.
    /// The 60/50/30% thresholds are True165's reading of Prima; they are not yet checked against a scan of the guides.
    /// </summary>
    public sealed class BotPlaybook
    {
        /// <summary>Under this much health a lifetap goes before anything else.</summary>
        public const int LifetapBelowHealthPercent = 60;
        /// <summary>Under this much power the nuke with the most damage per power point goes, not the biggest.</summary>
        public const int CheapNukeBelowPowerPercent = 30;
        /// <summary>A DoT goes only while the target is at or above this much health (it would not finish its ticks).</summary>
        public const int DotMinTargetHealthPercent = 50;
        /// <summary>A spell that failed to start is passed over for this long, so one stuck spell cannot freeze the rotation.</summary>
        public const long FailedSpellPassMilliseconds = 3_000;

        private readonly Dictionary<int, PlaybookSpellKind> _kinds;

        /// <summary>The level the table was built for; a different level means it is rebuilt.</summary>
        public int Level { get; }
        /// <summary>One bit per magic damage type (1 &lt;&lt; eDamageType) dealt by the bot's attack spells.</summary>
        public int DamageTypeMask { get; }
        /// <summary>The Cabalist drains rather than nukes (Prima: debuff, DoT, then drain).</summary>
        public bool PrefersDrain { get; }
        /// <summary>Wizards and Theurgists bolt only as the opener.</summary>
        public bool BoltOpenerOnly { get; }

        private BotPlaybook(int level, int mask, bool prefersDrain, bool boltOpenerOnly, Dictionary<int, PlaybookSpellKind> kinds)
        {
            Level = level;
            DamageTypeMask = mask;
            PrefersDrain = prefersDrain;
            BoltOpenerOnly = boltOpenerOnly;
            _kinds = kinds;
            KindOfFunc = KindOf;
        }

        /// <summary>KindOf as one delegate made once per playbook (no allocation on each cast decision).</summary>
        public Func<Spell, PlaybookSpellKind> KindOfFunc { get; }

        /// <summary>Builds the table from the bot's known spells (any rank; spells above <paramref name="level"/> are ignored).</summary>
        public static BotPlaybook Build(IEnumerable<Spell> spells, int level, eCharacterClass characterClass)
        {
            int mask = 0;
            var known = new List<Spell>();
            if (spells != null)
                foreach (Spell spell in spells)
                {
                    if (spell == null || spell.Level > level)
                        continue;
                    known.Add(spell);
                    if (spell.IsHarmful && BotCasterPriority.IsDamage(spell))
                        mask |= DamageBit(spell.DamageType);
                }
            var kinds = new Dictionary<int, PlaybookSpellKind>(known.Count);
            foreach (Spell spell in known)
                kinds[spell.ID] = Classify(spell, mask);
            return new BotPlaybook(level, mask, characterClass == eCharacterClass.Cabalist,
                characterClass is eCharacterClass.Wizard or eCharacterClass.Theurgist, kinds);
        }

        public static int DamageBit(eDamageType type) => (int)type is >= 0 and < 31 ? 1 << (int)type : 0;

        /// <summary>The damage type a resist debuff lowers, or Natural for anything else.</summary>
        public static eDamageType ResistDebuffType(Spell spell) => spell?.SpellType switch
        {
            eSpellType.BodyResistDebuff => eDamageType.Body,
            eSpellType.ColdResistDebuff => eDamageType.Cold,
            eSpellType.EnergyResistDebuff => eDamageType.Energy,
            eSpellType.HeatResistDebuff => eDamageType.Heat,
            eSpellType.MatterResistDebuff => eDamageType.Matter,
            eSpellType.SpiritResistDebuff => eDamageType.Spirit,
            eSpellType.SlashResistDebuff => eDamageType.Slash,
            eSpellType.CrushResistDebuff => eDamageType.Crush,
            eSpellType.ThrustResistDebuff => eDamageType.Thrust,
            _ => eDamageType.Natural,
        };

        /// <summary>Pure: what the spell is for, given the damage types the caster's own attack spells deal.</summary>
        public static PlaybookSpellKind Classify(Spell spell, int damageTypeMask)
        {
            if (spell == null)
                return PlaybookSpellKind.Other;
            if (AnimistSingleTargetPolicy.IsTurretSummon(spell) || spell.SpellType == eSpellType.SummonTheurgistPet)
                return PlaybookSpellKind.PetSummon;
            if (BotCasterPriority.IsDebuff(spell))
            {
                eDamageType resist = ResistDebuffType(spell);
                if (resist == eDamageType.Natural)
                    return PlaybookSpellKind.Debuff;
                if (resist is eDamageType.Slash or eDamageType.Crush or eDamageType.Thrust)
                    return PlaybookSpellKind.MeleeResistDebuff;
                return (damageTypeMask & DamageBit(resist)) != 0 ? PlaybookSpellKind.ResistDebuff : PlaybookSpellKind.OffResistDebuff;
            }
            if (BotCasterPriority.IsDamage(spell))
            {
                if (spell.SpellType == eSpellType.Bolt)
                    return PlaybookSpellKind.Bolt;
                if (spell.SpellType == eSpellType.DamageOverTime)
                    return PlaybookSpellKind.Dot;
                if (spell.SpellType == eSpellType.Lifedrain)
                    return spell.Radius > 0 ? PlaybookSpellKind.AreaNuke : PlaybookSpellKind.Lifedrain;
                if (spell.SpellType == eSpellType.DamageSpeedDecrease)
                    return spell.Radius > 0 ? PlaybookSpellKind.AreaNuke : PlaybookSpellKind.SnareNuke;
                return spell.Radius > 0 || spell.IsPBAoE ? PlaybookSpellKind.AreaNuke : PlaybookSpellKind.Nuke;
            }
            if (BotControlSpells.IsControl(spell) || BotControlSpells.IsStun(spell) ||
                spell.SpellType is eSpellType.SpeedDecrease or eSpellType.Nearsight)
                return PlaybookSpellKind.Control;
            return PlaybookSpellKind.Other;
        }

        /// <summary>The kind worked out at build time (spells added since are classified on the spot).</summary>
        public PlaybookSpellKind KindOf(Spell spell) =>
            spell != null && _kinds.TryGetValue(spell.ID, out PlaybookSpellKind kind) ? kind : Classify(spell, DamageTypeMask);

        /// <summary>
        /// Pure: is this debuff worth a cast for this caster? Resist debuffs only for its own damage types (melee resist
        /// debuffs when grouped or when it melees), unless the player picked it in the companion's AI Options.
        /// </summary>
        public static bool DebuffFits(PlaybookSpellKind kind, bool grouped, bool meleeUser, bool playerPicked) => kind switch
        {
            PlaybookSpellKind.OffResistDebuff => playerPicked,
            PlaybookSpellKind.MeleeResistDebuff => playerPicked || grouped || meleeUser,
            _ => true,
        };

        /// <summary>
        /// Pure: the opening debuff - one for the caster's own damage type first, then stat/armor/disease debuffs, then a
        /// melee resist debuff; highest level first within each; never one that does not fit (DebuffFits).
        /// </summary>
        public static Spell ChooseOpeningDebuff(IReadOnlyList<Spell> candidates, Func<Spell, PlaybookSpellKind> kindOf,
            bool grouped, bool meleeUser, Func<Spell, bool> playerPicked)
        {
            Spell best = null;
            int bestRank = int.MaxValue;
            if (candidates == null)
                return null;
            foreach (Spell spell in candidates)
            {
                if (!BotCasterPriority.IsDebuff(spell))
                    continue;
                PlaybookSpellKind kind = kindOf(spell);
                bool picked = playerPicked?.Invoke(spell) == true;
                if (!DebuffFits(kind, grouped, meleeUser, picked))
                    continue;
                int rank = picked ? 0 : kind switch
                {
                    PlaybookSpellKind.ResistDebuff => 1,
                    PlaybookSpellKind.Debuff => 2,
                    _ => 3,
                };
                if (best == null || rank < bestRank || rank == bestRank && Higher(spell, best))
                {
                    best = spell;
                    bestRank = rank;
                }
            }
            return best;
        }

        private static bool Higher(Spell a, Spell b) => a.Level > b.Level || a.Level == b.Level && a.ID > b.ID;

        /// <summary>
        /// Pure: a pet summon among the candidates keeps the chance it had under the old random pick (summons out of all
        /// candidates; <paramref name="roll"/> is 0 .. candidates - 1). A Theurgist's elementals and an Animist's turrets
        /// are their damage; the playbook orders only the spells cast by hand. The three-pets rhythm is another lane's.
        /// </summary>
        public static bool PetSummonTurn(int summons, int candidates, int roll) =>
            summons > 0 && candidates > 0 && roll >= 0 && roll < summons;

        /// <summary>
        /// Pure: the attack spell to cast from the legal candidates, in playbook order (see the class summary). Returns null
        /// only for an empty list. <paramref name="passOver"/> marks a spell that just failed to start: it is used only when
        /// nothing else is left.
        /// </summary>
        public static Spell ChooseAttack(IReadOnlyList<Spell> candidates, in PlaybookContext context,
            Func<Spell, PlaybookSpellKind> kindOf, Func<Spell, int> powerCost, bool prefersDrain, bool boltOpenerOnly,
            Func<Spell, bool> passOver = null)
        {
            if (candidates == null || candidates.Count == 0)
                return null;
            Spell pick = ChooseFrom(candidates, context, kindOf, powerCost, prefersDrain, boltOpenerOnly, passOver);
            return pick ?? ChooseFrom(candidates, context, kindOf, powerCost, prefersDrain, boltOpenerOnly, null);
        }

        private static Spell ChooseFrom(IReadOnlyList<Spell> candidates, in PlaybookContext context,
            Func<Spell, PlaybookSpellKind> kindOf, Func<Spell, int> powerCost, bool prefersDrain, bool boltOpenerOnly,
            Func<Spell, bool> passOver)
        {
            Spell lifedrain = null, bolt = null, snareNuke = null, dot = null, nuke = null, cheapNuke = null, area = null, rest = null;
            int restRank = int.MaxValue;
            double cheapValue = double.MinValue;
            bool lowPower = context.PowerPercent < CheapNukeBelowPowerPercent;
            foreach (Spell spell in candidates)
            {
                if (spell == null || passOver?.Invoke(spell) == true)
                    continue;
                PlaybookSpellKind kind = kindOf(spell);
                switch (kind)
                {
                    case PlaybookSpellKind.Lifedrain:
                        if (MoreDamage(spell, lifedrain, powerCost)) lifedrain = spell;
                        break;
                    case PlaybookSpellKind.Bolt:
                        if (MoreDamage(spell, bolt, powerCost)) bolt = spell;
                        break;
                    case PlaybookSpellKind.SnareNuke:
                        if (MoreDamage(spell, snareNuke, powerCost)) snareNuke = spell;
                        break;
                    case PlaybookSpellKind.Dot:
                        if (MoreDamage(spell, dot, powerCost)) dot = spell;
                        break;
                    case PlaybookSpellKind.AreaNuke:
                        if (MoreDamage(spell, area, powerCost)) area = spell;
                        break;
                    case PlaybookSpellKind.Nuke:
                        break;
                    case PlaybookSpellKind.OffResistDebuff:
                        continue;   // filtered by the caller unless the player picked it; never a filler
                    default:
                        int rank = kind switch
                        {
                            PlaybookSpellKind.ResistDebuff => 0,
                            PlaybookSpellKind.Debuff => 1,
                            PlaybookSpellKind.MeleeResistDebuff => 2,
                            PlaybookSpellKind.Control => 3,
                            _ => 4,
                        };
                        if (rest == null || rank < restRank || rank == restRank && Higher(spell, rest))
                        {
                            rest = spell;
                            restRank = rank;
                        }
                        continue;
                }
                // the single-target nuke pool: plain nukes, drains, snare nukes and the bolt
                if (kind is PlaybookSpellKind.Nuke or PlaybookSpellKind.Lifedrain or PlaybookSpellKind.SnareNuke or
                    PlaybookSpellKind.Bolt)
                {
                    if (kind == PlaybookSpellKind.Bolt && boltOpenerOnly && context.CastsOnTarget > 0)
                        continue;
                    if (kind == PlaybookSpellKind.Nuke && MoreDamage(spell, nuke, powerCost))
                        nuke = spell;
                    double perPower = spell.Damage / Math.Max(1, powerCost(spell));
                    if (perPower > cheapValue)
                    {
                        cheapValue = perPower;
                        cheapNuke = spell;
                    }
                }
            }

            // 1. hurt, or the pet is being hit: the lifetap
            if (lifedrain != null && (context.HealthPercent < LifetapBelowHealthPercent || context.PetUnderAttack))
                return lifedrain;
            // 2. the bolt opens the fight
            if (bolt != null && context.CastsOnTarget == 0)
                return bolt;
            // 3. a runner or a monster closing on a solo caster: the snare nuke
            if (snareNuke != null && context.SnareWanted)
                return snareNuke;
            // 4. the DoT on a fight that lasts (only offered when it is not on the target already)
            if (dot != null && context.FightLasts && context.TargetHealthPercent >= DotMinTargetHealthPercent)
                return dot;
            // 5. the best single-target nuke (a Cabalist's drain first); low on power, the most damage per power point
            if (lowPower && cheapNuke != null)
                return cheapNuke;
            if (prefersDrain && lifedrain != null)
                return lifedrain;
            Spell single = Best(Best(Best(nuke, lifedrain, powerCost), snareNuke, powerCost),
                bolt != null && (!boltOpenerOnly || context.CastsOnTarget == 0) ? bolt : null, powerCost);
            if (single != null)
                return single;
            // 6. only area damage left (ChooseAreaDamage took the 2+ monster case before this)
            if (area != null)
                return area;
            // 7. a DoT whatever the fight, then a bolt held as opener-only (better than no damage at all)
            if (dot != null)
                return dot;
            if (bolt != null)
                return bolt;
            // 8. debuffs, control, the rest
            return rest;
        }

        private static Spell Best(Spell a, Spell b, Func<Spell, int> powerCost) =>
            a == null ? b : b == null ? a : MoreDamage(b, a, powerCost) ? b : a;

        /// <summary>More damage per cast; ties go to the cheaper spell, then the shorter cast, then the higher level.</summary>
        public static bool MoreDamage(Spell candidate, Spell current, Func<Spell, int> powerCost)
        {
            if (current == null)
                return true;
            if (candidate.Damage != current.Damage)
                return candidate.Damage > current.Damage;
            int a = powerCost(candidate), b = powerCost(current);
            if (a != b)
                return a < b;
            if (candidate.CastTime != current.CastTime)
                return candidate.CastTime < current.CastTime;
            return Higher(candidate, current);
        }

        /// <summary>Pure: a bolt lands only on a target nobody is meleeing - not the tank (Q2) and not the caster itself
        /// (period: a bolt is blocked by a target engaged in melee).</summary>
        public static bool BoltLands(int othersMeleeingTarget, bool targetMeleeingCaster) =>
            othersMeleeingTarget <= 0 && !targetMeleeingCaster;
    }
}
