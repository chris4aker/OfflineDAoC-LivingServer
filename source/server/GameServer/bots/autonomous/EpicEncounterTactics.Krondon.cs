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
            // ---- 1.2 big-boss plans (owner 2026-11-14; tests only). Catalog rows (hand notes): Fuladl's and Fulafeallan's
            // parts as required adds, Beran's barrels as a ground hazard, Lurfos's weapon strike as a spread, Jehrog after
            // Ulor, Scurceol's orbs one at a time.
            new(nameof(DrihtenElreden), [nameof(DrihtenElreden)], DrihtenPlan)
            {
                Summary = "His students (DrihtenBaf, the thrawn ogre bocere he calls in one at a time every 15-30 s) within " +
                          "2,500 die first; one that joins his fight is the casters', the melee stay on him, from behind (he " +
                          "evades and parries).",
                ClearFirstOf = DrihtenStudentClearsFirst,
            },
            new(nameof(DebenSeGecynde), [nameof(DebenSeGecynde)], DebenPlan)
            {
                Summary = "Every 45-70 s he calls 1-3 fighters and 1-3 mages: the party he fights holds him, the casters and " +
                          "archers kill the mages first, the other melee the fighters first.",
            },
            new(nameof(BeranSupplyMaster), [nameof(BeranSupplyMaster)], BeranPlan)
            {
                Summary = "His barrels (8 s, 1,200 heat in 1,000) are a catalog ground hazard everyone leaves; the Onstal " +
                          "Hyrde he yells for (2-4 every 35-65 s) are killed by everyone but the party he fights.",
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

        // ---- 1.2 big-boss plans (period: Salaris' Guide to Albion Epic Encounters, VN Boards 2003; tests only) ---------

        /// <summary>Drihten's students' PackageID (25 thrawn ogre bocere around him in the world database, 760-1,170 off).</summary>
        public const string DrihtenStudents = "DrihtenBaf";
        /// <summary>He calls students within this (DrihtenElredenBrain.BringHelp).</summary>
        public const int DrihtenCallReach = 2_500;

        private static GameNPC DrihtenStudentClearsFirst(GameNPC npc, IReadOnlyList<GameNPC> live)
        {
            if (npc == null || npc.PackageID != DrihtenStudents || npc is DrihtenElreden) return null;
            GameNPC drihten = BossNear(npc, live, nameof(DrihtenElreden), AddSearchRadius);
            return drihten != null && Flat(At(npc), new(drihten.SpawnPoint.X, drihten.SpawnPoint.Y, drihten.SpawnPoint.Z)) <= DrihtenCallReach
                ? drihten : null;
        }

        /// <summary>
        /// Drihten Elreden (period: "an ogre friar ... a significant number of styles. His students will stand by and watch,
        /// for the most part ... be careful for potential adds", VN 2003). The script calls one student at a time onto his
        /// target every 15-30 s: they die first (<see cref="DrihtenStudentClearsFirst"/>); one he brought in anyway is the
        /// casters' and archers' while the melee stay on him, from behind (period, FreddysHouse 2003-03-27: high evade and
        /// parry, his best styles follow them; flank him).
        /// </summary>
        private static Advice? DrihtenPlan(RuleContext c) =>
            OthersTakeTheAdds(c, n => n.PackageID == DrihtenStudents, DrihtenCallReach, "drihten-students", rangedOnly: true) ??
            MeleeFromBehind(c, "drihten-behind");

        /// <summary>
        /// Deben se Gecynde (period: "a military commander, who commands several ogre fighters and mages. The AI in this
        /// encounter is excellent", VN 2003). The script calls 1-3 DebenFighter (3,000 health) and 1-3 DebenMage (2,000, a 300
        /// heat or cold bolt every cast) beside him every 45-70 s. The party he fights holds him; casters and archers kill the
        /// mages first, the other melee the fighters first.
        /// </summary>
        private static Advice? DebenPlan(RuleContext c)
        {
            bool ranged = c.CanShootFlyers;
            return OthersTakeTheAdds(c, n => n is DebenMage or DebenFighter, 2_500, "deben-soldiers",
                n => (n is DebenMage) == ranged ? 0 : 1, anyState: true);
        }

        /// <summary>The name of the guards Beran yells for (plain GameNPCs, BeranSupplyMasterBrain.CallForHelp).</summary>
        public const string BeranGuards = "Onstal Hyrde";

        /// <summary>
        /// Beran the Supply Master (period: "ignites the barrels around the room, causing sections of the room to explode ...
        /// Not very difficult overall", VN 2003). The barrels are a catalog ground hazard (BarrelExplosive, 1,000: every raider
        /// leaves it in its 8 s, the party he fights too, and he follows); the Onstal Hyrde he yells for are killed by
        /// everyone but the party he fights.
        /// </summary>
        private static Advice? BeranPlan(RuleContext c) =>
            OthersTakeTheAdds(c, n => n.Name == BeranGuards, 2_500, "beran-guards", anyState: true);
    }
}
