using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Krondon's encounter rules (wave 2, 1.2): Orshom's Fire, which only cold damages, is put out by the raid's cold
    /// casters. The catalog rows (Scurceol's orbs, Orshom and Orylle, Krevo Ricik's ghost) come from the hand notes. Also
    /// here: spells a bot never wastes on a target immune to their damage type (any dungeon).
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        [DungeonRules]
        private static IEnumerable<DungeonRule> KrondonRules() =>
        [
            new(nameof(Orshom), [nameof(OrshomFire), nameof(Orshom), nameof(Orylle)], ColdPutsOutOrshomsFire)
            {
                Summary = "Raiders with a cold spell put Orshom's Fire out from outside it (only cold damages it).",
            },
        ];

        /// <summary>Monsters (by class name) that take damage only of these types; any other damage type does nothing.
        /// Orshom's Fire: cold only. Sys'sro's pit monsters: no type a raider deals.</summary>
        public static readonly IReadOnlyDictionary<string, eDamageType[]> OnlyDamagedBy = new Dictionary<string, eDamageType[]>(StringComparer.Ordinal)
        {
            ["OrshomFire"] = [eDamageType.Cold],
            ["PitMonster"] = [],
        };

        /// <summary>A damage spell of <paramref name="spell"/>'s type does nothing to <paramref name="target"/>: the bot
        /// casts another one, or none (BotBrain.CanCastOffensiveSpell).</summary>
        public static bool SpellWasted(GameLiving target, Spell spell) =>
            target != null && spell is { Damage: > 0 } && OnlyDamagedBy.TryGetValue(target.GetType().Name, out eDamageType[] types) &&
            Array.IndexOf(types, spell.DamageType) < 0;

        /// <summary>The raider has a damage spell of this type it can cast (bots; settable for tests).</summary>
        public static Func<GameLiving, eDamageType, bool> DealsDamageType { get; set; } = DefaultDealsDamageType;

        public static bool DefaultDealsDamageType(GameLiving raider, eDamageType type)
        {
            if (raider is not GameNPC npc) return false;
            GameBot bot = npc as GameBot;
            IEnumerable<Spell> spells = (npc.HarmfulSpells ?? []).Concat(npc.InstantHarmfulSpells ?? []).Concat(bot?.BoltSpells ?? []);
            return spells.Any(s => s != null && s.Damage > 0 && s.DamageType == type && (bot == null || s.Level <= bot.Level));
        }

        /// <summary>
        /// Orshom's Fire (10,000 health, only cold damages it) burns 500 heat in a 300 radius every 3 s, and while it burns
        /// Orylle throws a raider into it every 15-20 s. Raiders with a cold damage spell put it out: they turn on it from
        /// outside its radius (stepping out first if they stand in it) and cast only their cold spells at it
        /// (<see cref="SpellWasted"/>). Everyone else keeps the catalog's hazard rule: never hit it, never stand in it.
        /// </summary>
        private static Advice? ColdPutsOutOrshomsFire(RuleContext c)
        {
            if (!DealsDamageType(c.Raider, eDamageType.Cold)) return null;
            OrshomFire fire = c.Target as OrshomFire ?? NearestLive<OrshomFire>(c.Target, c.Live, AddSearchRadius);
            if (!Present(fire)) return null;
            if (!c.Live.Any(n => Present(n) && n is Orshom or Orylle && n.CurrentRegionID == fire.CurrentRegionID)) return null;
            int radius = HazardRadiusOf(fire) is > 0 and var r ? r : 300;
            if (HazardEscape(At(c.Raider), At(fire), radius) is Vector3 outOfIt)
                return new(BotAction.Reposition, fire, outOfIt, $"cold-fire: out of \"{fire.Name}\" first, then cold spells at it");
            if (c.Target != fire)
                return new(BotAction.Retarget, fire, null, $"cold-fire: only cold damages \"{fire.Name}\"; put it out");
            return new(BotAction.None, fire, null, "cold-fire: cold spells at the fire");
        }
    }
}
