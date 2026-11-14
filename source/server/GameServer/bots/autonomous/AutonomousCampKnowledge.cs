using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// From Offline DAoC True 1.65 (shadowofze; its veteran review M11, 2026-11-11): what a 2003 player knew about a hunting spot before walking there, worked out by
    /// the background camp builder once per catalog generation (no work on the game thread, nothing per bot turn):
    /// how tightly the monsters stand (pack factor), how many cast spells, what kind of creature they are and how wide
    /// the spot is. A camp choice reads it with a few multiplies:
    /// <list type="bullet">
    /// <item>a party without crowd control, and a solo bot, draws dense camps less often (adds kill);</item>
    /// <item>pure melee classes draw caster-heavy camps less often (Prima Hero: avoid spell-casting monsters);</item>
    /// <item>a class draws its favourite prey more often (Prima Hunter: animals and insects; Spiritmaster: reptiles and
    /// insects; Nightshade: animals, best for piercing);</item>
    /// <item>solo dungeons depend on the class: pet classes and stealthers a little more often, support classes rarely;</item>
    /// <item>a solo bot draws camps weak to its own damage more often (the monsters' racial resists; a Void Eldritch on
    /// humanoids, a thrust Infiltrator on animals).</item>
    /// </list>
    /// No con ceiling, combat, XP or coin number changes; only how often an already-legal camp is drawn.
    /// </summary>
    public static class AutonomousCampKnowledge
    {
        [ServerProperty("bots", "bot_camp_knowledge",
            "Veteran review M11 (2026-11-11): camp choices weigh pack density, caster share and creature type (worked out by the background camp builder), and solo dungeon chances follow the class.", true)]
        public static bool Enabled = true;

        /// <summary>
        /// Camp traits from the background builder (default: unknown, every factor 1). M11/M6 (completeness review
        /// 2026-11-11): a pull spot at the camp's edge and a safe spot beyond it, on the side with the fewest monsters
        /// (<see cref="Spots"/>), when <see cref="HasSpots"/>.
        /// </summary>
        public readonly record struct Traits(float PackFactor, float CasterShare, byte BodyType, int Radius, int Measured,
            int Race = 0, ushort WeakTo = 0, Vector3 Pull = default, Vector3 Safe = default, bool HasSpots = false)
        {
            public bool Known => Measured > 0;
        }

        // ---------------------------------------------------------------- pull spot and safe spot (M11 / M6)

        /// <summary>How far past the camp's widest monster its safe spot lies.</summary>
        public const int SafeSpotMargin = 1_000;

        /// <summary>A monster this close to a candidate safe spot, or to the way out to it, counts against that side.</summary>
        public const int SafeSpotClearance = 700;

        private static readonly Vector2[] SpotDirections = BuildSpotDirections();

        private static Vector2[] BuildSpotDirections()
        {
            var directions = new Vector2[8];
            for (int i = 0; i < directions.Length; i++)
                directions[i] = new Vector2(MathF.Cos(i * MathF.PI / 4), MathF.Sin(i * MathF.PI / 4));
            return directions;
        }

        /// <summary>
        /// Pure rule: of eight directions from the camp's anchor, the side with the fewest monsters (the camp's own and
        /// the aggressive ones around it) near its safe spot - <see cref="SafeSpotMargin"/> past the camp's widest
        /// monster - or near the way out to it. The pull spot is the camp's edge on that side (where a puller tags and
        /// runs back to), the safe spot where a losing bot or party runs. Ties go to the first direction (east).
        /// </summary>
        public static void Spots(Vector3 anchor, int radius, IReadOnlyList<Vector3> monsters, out Vector3 pull, out Vector3 safe)
        {
            float edge = Math.Max(MinimumSocialRange, radius);
            float far = edge + SafeSpotMargin;
            float clearanceSquared = (float)SafeSpotClearance * SafeSpotClearance;
            Vector2 centre = new(anchor.X, anchor.Y);
            int best = 0, bestCount = int.MaxValue;
            for (int d = 0; d < SpotDirections.Length; d++)
            {
                Vector2 safeSpot = centre + SpotDirections[d] * far;
                Vector2 wayOut = centre + SpotDirections[d] * (edge + far) / 2;
                int count = 0;
                if (monsters != null)
                    foreach (Vector3 m in monsters)
                    {
                        Vector2 p = new(m.X, m.Y);
                        if (Vector2.DistanceSquared(p, safeSpot) <= clearanceSquared ||
                            Vector2.DistanceSquared(p, wayOut) <= clearanceSquared)
                            count++;
                    }
                if (count < bestCount)
                {
                    best = d;
                    bestCount = count;
                }
            }
            Vector2 pullAt = centre + SpotDirections[best] * edge;
            Vector2 safeAt = centre + SpotDirections[best] * far;
            pull = new Vector3(pullAt.X, pullAt.Y, anchor.Z);
            safe = new Vector3(safeAt.X, safeAt.Y, anchor.Z);
        }

        /// <summary>The traits with the camp's pull and safe spots worked out from these monsters.</summary>
        public static Traits WithSpots(Traits traits, Vector3 anchor, IReadOnlyList<Vector3> monsters)
        {
            Spots(anchor, traits.Radius, monsters, out Vector3 pull, out Vector3 safe);
            return traits with { Pull = pull, Safe = safe, HasSpots = true };
        }

        /// <summary>One monster as the builder sees it.</summary>
        public readonly record struct Member(Vector3 Position, int AggroRange, bool Caster, byte BodyType, int Race = 0);

        /// <summary>A pack this dense (other monsters within each one's reach, on average) counts as dense.</summary>
        public const float DensePack = 2f;

        /// <summary>The social reach used for a monster that does not attack on sight.</summary>
        public const int MinimumSocialRange = 300;

        /// <summary>
        /// Pure rule: the traits of a camp's monsters around its anchor. Pack factor is the average number of other
        /// members within each member's reach (aggro range, at least <see cref="MinimumSocialRange"/>); caster share the
        /// fraction with harmful spells; body type the most common one (0 when none is a majority of a third); radius the
        /// farthest member from the anchor.
        /// </summary>
        public static Traits Measure(IReadOnlyList<Member> members, Vector3 anchor)
        {
            if (members == null || members.Count == 0)
                return default;
            int neighbours = 0, casters = 0;
            var races = new Dictionary<int, int>();
            float farthestSquared = 0;
            Span<int> bodies = stackalloc int[16];
            for (int i = 0; i < members.Count; i++)
            {
                Member m = members[i];
                float reach = Math.Max(MinimumSocialRange, m.AggroRange);
                float reachSquared = reach * reach;
                for (int j = 0; j < members.Count; j++)
                    if (i != j && Vector3.DistanceSquared(m.Position, members[j].Position) <= reachSquared)
                        neighbours++;
                if (m.Caster)
                    casters++;
                if (m.BodyType < bodies.Length)
                    bodies[m.BodyType]++;
                if (m.Race > 0)
                    races[m.Race] = races.GetValueOrDefault(m.Race) + 1;
                float d = Vector2.DistanceSquared(new Vector2(m.Position.X, m.Position.Y), new Vector2(anchor.X, anchor.Y));
                if (d > farthestSquared)
                    farthestSquared = d;
            }
            int bestBody = 0, bestCount = 0;
            for (int b = 1; b < bodies.Length; b++)
                if (bodies[b] > bestCount)
                {
                    bestCount = bodies[b];
                    bestBody = b;
                }
            if (bestCount * 3 < members.Count)
                bestBody = 0;
            int bestRace = 0, bestRaceCount = 0;
            foreach ((int race, int count) in races)
                if (count > bestRaceCount || count == bestRaceCount && race < bestRace)
                {
                    bestRaceCount = count;
                    bestRace = race;
                }
            if (bestRaceCount * 2 <= members.Count)
                bestRace = 0;
            return new Traits((float)neighbours / members.Count, (float)casters / members.Count, (byte)bestBody,
                (int)MathF.Sqrt(farthestSquared), members.Count, bestRace);
        }

        // ---------------------------------------------------------------- resist weakness

        /// <summary>A racial resist this low (the server's own Race table) counts as a weakness.</summary>
        public const int WeakResist = -10;

        private static readonly eDamageType[] ResistedDamage =
        [
            eDamageType.Crush, eDamageType.Slash, eDamageType.Thrust, eDamageType.Body, eDamageType.Cold,
            eDamageType.Energy, eDamageType.Heat, eDamageType.Matter, eDamageType.Spirit,
        ];

        /// <summary>Pure rule: one bit per damage type (bit = the eDamageType value) whose resist is a weakness.</summary>
        public static ushort WeaknessMask(Func<eDamageType, int> resist)
        {
            if (resist == null)
                return 0;
            ushort mask = 0;
            foreach (eDamageType type in ResistedDamage)
                if (resist(type) <= WeakResist)
                    mask |= (ushort)(1 << (int)type);
            return mask;
        }

        /// <summary>Pure rule: the camp is weak to this damage type.</summary>
        public static bool IsWeakTo(Traits traits, eDamageType damage) =>
            damage is >= eDamageType.Crush and <= eDamageType.Spirit && (traits.WeakTo & (1 << (int)damage)) != 0;

        /// <summary>The camp's weaknesses from its main race (the server's Race table; 0 when no race is a majority).</summary>
        public static Traits WithWeaknesses(Traits traits) => traits.Race <= 0 ? traits : traits with
        {
            WeakTo = WeaknessMask(type => SkillBase.GetRaceResist(traits.Race, (eResist)GameLiving.GetResistTypeForDamage(type))),
        };

        /// <summary>Pure rule: classes whose own damage is mostly spells (their best nuke decides the damage type).</summary>
        public static bool DamagesWithSpells(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Wizard or eCharacterClass.Sorcerer or eCharacterClass.Theurgist or eCharacterClass.Cabalist or
            eCharacterClass.Necromancer or eCharacterClass.Eldritch or eCharacterClass.Enchanter or
            eCharacterClass.Mentalist or eCharacterClass.Animist or eCharacterClass.Runemaster or
            eCharacterClass.Spiritmaster or eCharacterClass.Bonedancer;

        /// <summary>
        /// The bot's main damage type for a camp draw: casters their highest-level damaging spell, everyone else the
        /// weapon in hand (Natural when unknown: no weakness bonus). Read once per camp plan, not per candidate.
        /// </summary>
        public static eDamageType MainDamageType(GameBot bot)
        {
            if (bot == null)
                return eDamageType.Natural;
            if (DamagesWithSpells((eCharacterClass)bot.CharacterClass.ID) && bot.HarmfulSpells is { } spells)
            {
                Spell best = null;
                foreach (Spell spell in spells.ToArray())
                    if (spell != null && spell.Damage > 0 && spell.DamageType is >= eDamageType.Body and <= eDamageType.Spirit &&
                        (best == null || spell.Level > best.Level))
                        best = spell;
                if (best != null)
                    return best.DamageType;
            }
            int weapon = bot.ActiveWeapon?.Type_Damage ?? 0;
            return weapon is >= 1 and <= 3 ? (eDamageType)weapon : eDamageType.Natural;
        }

        // ---------------------------------------------------------------- class tables (Prima class sections)

        private const byte Animal = 1, Insect = 7, Reptile = 9;

        /// <summary>Pure rule: the creature types this class hunts best (empty for most classes).</summary>
        public static bool IsFavouritePrey(eCharacterClass characterClass, byte bodyType) => characterClass switch
        {
            eCharacterClass.Hunter => bodyType is Animal or Insect,
            eCharacterClass.Spiritmaster => bodyType is Reptile or Insect,
            eCharacterClass.Nightshade => bodyType is Animal,
            _ => false,
        };

        /// <summary>Pure rule: classes with no magic of their own, for whom caster camps are the hard ones.</summary>
        public static bool IsPureMelee(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Armsman or eCharacterClass.Mercenary or eCharacterClass.Warrior or eCharacterClass.Berserker or
            eCharacterClass.Savage or eCharacterClass.Hero or eCharacterClass.Blademaster;

        /// <summary>Pure rule: the draw multiplier of a camp for a solo bot of this class.</summary>
        public static double SoloFactor(eCharacterClass characterClass, Traits traits,
            eDamageType mainDamage = eDamageType.Natural)
        {
            if (!traits.Known)
                return 1;
            double factor = 1;
            if (IsWeakTo(traits, mainDamage))
                factor *= 1.5;
            if (traits.BodyType != 0 && IsFavouritePrey(characterClass, traits.BodyType))
                factor *= 1.5;
            bool melee = IsPureMelee(characterClass);
            if (melee && traits.CasterShare > 0.5f)
                factor *= 0.5;
            if (traits.PackFactor >= DensePack)
                factor *= melee ? 0.5 : 0.75;
            return factor;
        }

        /// <summary>Pure rule: the draw multiplier of a camp for a party (dense camps are for parties with crowd control).</summary>
        public static double GroupFactor(bool partyHasCrowdControl, Traits traits) =>
            traits.Known && !partyHasCrowdControl && traits.PackFactor >= DensePack ? 0.5 : 1;

        /// <summary>
        /// Pure rule (permille): how often a solo bot of this class picks a dungeon when both kinds of camp are open. Pet
        /// classes and stealthers 15%, support classes 3% (Prima Healer "really shouldn't solo past level 4"), everyone
        /// else the old 10%.
        /// </summary>
        public static int SoloDungeonPermille(eCharacterClass characterClass) => characterClass switch
        {
            eCharacterClass.Cabalist or eCharacterClass.Theurgist or eCharacterClass.Necromancer or
                eCharacterClass.Enchanter or eCharacterClass.Animist or eCharacterClass.Spiritmaster or
                eCharacterClass.Bonedancer or eCharacterClass.Infiltrator or eCharacterClass.Nightshade or
                eCharacterClass.Shadowblade or eCharacterClass.Scout or eCharacterClass.Ranger or
                eCharacterClass.Hunter => 150,
            eCharacterClass.Cleric or eCharacterClass.Friar or eCharacterClass.Healer or eCharacterClass.Shaman or
                eCharacterClass.Druid or eCharacterClass.Bard => 30,
            _ => AutonomousBotDecisionEngine.SoloDungeonPreferencePermille,
        };
    }
}
