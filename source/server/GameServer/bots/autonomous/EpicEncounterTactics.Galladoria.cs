using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    public static partial class EpicEncounterTactics
    {
        [DungeonRules]
        private static IEnumerable<DungeonRule> GalladoriaRules() =>
        [
            new("FirePrimal", ["FirePrimal"], FirePrimalTrail)
            {
                Summary = "Melee fight the Water or Earth primal while casters and archers kill the Fire primal; the last one, melee fight it in the fire.",
            },
            new(nameof(SpindlerBroodmother), [nameof(SpindlerBroodmother)], SpindlerBurn)
            {
                Summary = "Below 25% the raid burns her: the add parties stay on her newborns, everyone else fights her. The add " +
                          "parties are sized to her spawn rate and keep her druids and wardens out; the casters, archers and " +
                          "healers of the other parties fight from posts 700 apart around her, so a mesmerize catches one post.",
            },
            new(nameof(Easmarach), [nameof(Easmarach)], EasmarachOnTheFalls)
            {
                Summary = "Below 30% he retreats to the waterfall top and heals: casters, archers and healers shoot him from the " +
                          "floor below (six spots the raid reached on 2026-11-06), melee wait at his spawn. While his push behind " +
                          "the falls is on, the route kills him before Galladoria's last room.",
                Route = EasmarachBeforeTheLastRoom,
            },
        ];

        // ---- Spindler Broodmother: the burn, the add parties' size and make-up, the mesmerize spread -------------------

        /// <summary>Below this the raid burns her (PC log e3b8238: both wipes came from about 25%, at 15% and 22%; in their
        /// last minutes the raid dealt 8,000-12,000 a minute to her newborns and 400-1,400 to her).</summary>
        public const int SpindlerBurnBelow = 25;
        /// <summary>Her newborns (SpindlerBroodmotherBrain.SpawnSplinder): 1 or 2 (1.5 on average: the loop draws its bound
        /// again each pass) every 25-35 s (a 10 s timer, then a 15-25 s reset), so about 3 a minute, 8,000 health each.</summary>
        public const double SpindlerNewbornsPerMinute = 3.0;
        public const int SpindlerNewbornHealth = 8_000;
        /// <summary>What one raider dealt a newborn in a minute (PC log e3b8238's EPIC_FIGHT_LEDGER: 350-700 per attacker,
        /// mesmerizes included), and how many of an add party's eight fight (its healers keep it up).</summary>
        public const int SpindlerRaiderDamagePerMinute = 500, SpindlerFightersPerParty = 6;
        /// <summary>The add parties never take more than this share of the raid's parties.</summary>
        public const int SpindlerAddPartyShareMax = 45;
        /// <summary>Her newborns hunt these first (SBAddsBrain: bard, druid, mentalist, warden at 400 aggro); the druids and
        /// wardens are the raid's main healers and curers (PC log e3b8238, deaths in attempt 1: druid 17, warden 14), so the
        /// add parties are picked without them where the raid allows.</summary>
        public static readonly eCharacterClass[] SpindlerKeepOutOfAddParties = [eCharacterClass.Druid, eCharacterClass.Warden];
        /// <summary>Her mesmerize lands on everyone within 300 of a raider (SpindlerBroodmotherBrain.BossMezz): the spread
        /// posts stand this far apart (twice that, and room for the raiders around a post)...</summary>
        public const int SpindlerSpreadSpacing = 700;
        /// <summary>...on two rings this far from her, 650 apart so a post of one lies 700 from its neighbours on the other
        /// (inside the 1,500 the raid's spells reach her from, and the 2,000 its heals reach her melee from).</summary>
        public static readonly int[] SpindlerSpreadRings = [750, 1_400];

        /// <summary>
        /// How many add parties (pure): enough that their kill rate matches her spawn rate
        /// (<see cref="SpindlerNewbornsPerMinute"/> x <see cref="SpindlerNewbornHealth"/> over
        /// <see cref="SpindlerRaiderDamagePerMinute"/> x <see cref="SpindlerFightersPerParty"/>: 24,000 / 3,000 = 8), at most
        /// <see cref="SpindlerAddPartyShareMax"/>% of the parties and at least one (two from four parties). The 1.2 rule sent
        /// 2 of the 14 parties PC log e3b8238 had, a kill rate of about a third of her spawn rate.
        /// </summary>
        public static int SpindlerAddPartyCount(int parties)
        {
            if (parties <= 1) return 0;
            int wanted = (int)Math.Ceiling(SpindlerNewbornsPerMinute * SpindlerNewbornHealth /
                                           (SpindlerRaiderDamagePerMinute * (double)SpindlerFightersPerParty));
            int cap = Math.Max(1, parties * SpindlerAddPartyShareMax / 100);
            return Math.Clamp(wanted, Math.Min(parties >= 4 ? 2 : 1, cap), cap);
        }

        /// <summary>The raider is of a class kept out of her add parties.</summary>
        public static bool KeptOutOfSpindlerAddParties(int classId) =>
            Array.IndexOf(SpindlerKeepOutOfAddParties, (eCharacterClass)classId) >= 0;

        /// <summary>The burn is on (pure): she fights at or below <see cref="SpindlerBurnBelow"/>%.</summary>
        public static bool SpindlerBurning(bool inCombat, int healthPercent) => inCombat && healthPercent <= SpindlerBurnBelow;

        /// <summary>
        /// Below <see cref="SpindlerBurnBelow"/>% the add parties keep to her newborns and everyone else fights her (the
        /// catalog's area-on-the-clump and peel rules would send casters and melee to the newborns, which killed the burn in
        /// both PC wipes). Above it, no opinion: the catalog's rules as before.
        /// </summary>
        private static Advice? SpindlerBurn(RuleContext c)
        {
            if (c.Target is not SpindlerBroodmother boss || !SpindlerBurning(boss.InCombat, boss.HealthPercent)) return null;
            bool healer = c.ClassId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)c.ClassId);
            if (!healer && For(boss) is { AddParties: > 0 } t && IsAddParty(c.Raider, t.AddParties, c.Now, boss) &&
                AddPartyTarget(LiveRangedAdds(boss, c.Live ?? [])) is { } newborn)
                return new Advice(BotAction.Retarget, newborn, null, $"burn below {SpindlerBurnBelow}%: the add party keeps her newborns off");
            return new Advice(BotAction.None, boss, null, $"burn below {SpindlerBurnBelow}%: everyone on her");
        }

        /// <summary>The mesmerize spread of a boss's fight: its posts' spacing and rings, or null (only Spindler Broodmother).</summary>
        public static (int Spacing, int[] Rings)? MezSpreadOf(GameNPC boss) =>
            boss is SpindlerBroodmother ? (SpindlerSpreadSpacing, SpindlerSpreadRings) : null;

        // ---- Easmarach: the waterfall top, and the falls before the last room ------------------------------------------

        /// <summary>Where he retreats below 30% (EasmarachBrain.Think point1) and heals an eighth of his health: the top of the
        /// waterfall, about 1,100 above his spawn; no walked path reaches it (2026-11-06: NoPathFound to 37823,50345,10930).</summary>
        public static readonly Vector3 EasmarachFallsTop = new(37811, 50342, 10958);
        /// <summary>His spawn on the floor below (the melee wait here; he takes damage only from within 2,500 of it).</summary>
        public static readonly Vector3 EasmarachSpawn = new(37918, 50523, 9865);

        /// <summary>
        /// The floor spots below the waterfall top that raiders reached and stood on while he was up there (REALM_EXPEDITION
        /// and AUTONOMOUS_ROUTE_RECOVERY lines of the 2026-11-06 run): each within 1,250 of the top (in spell and bow range)
        /// and within his 2,500 of his spawn (in his close range). DoTs cast from there were on him; the raid's casters
        /// lost him because they tried to walk up to him instead.
        /// </summary>
        public static readonly Vector3[] EasmarachFallsStands =
        [
            new(37786, 50247, 9809), new(37953, 50323, 9822), new(37663, 50168, 9809),
            new(37578, 50122, 9805), new(37480, 50070, 9801), new(37437, 50308, 9874),
        ];

        /// <summary>A raider within this of its spot shoots from there.</summary>
        public const int EasmarachStandArrive = 200;
        /// <summary>He is "on the falls": this close to the top (flat), and this far above his spawn.</summary>
        public const int EasmarachTopRadius = 400, EasmarachTopHeight = 600;

        /// <summary>He stands on the waterfall top (pure).</summary>
        public static bool OnTheFalls(Vector3 easmarach) =>
            Flat(easmarach, EasmarachFallsTop) <= EasmarachTopRadius && easmarach.Z >= EasmarachSpawn.Z + EasmarachTopHeight;

        /// <summary>The spot a party shoots him from while he is on the falls.</summary>
        public static Vector3 EasmarachStand(int partyIndex) =>
            EasmarachFallsStands[AssignedIndex(partyIndex, EasmarachFallsStands.Length)];

        private static Advice? EasmarachOnTheFalls(RuleContext c) =>
            EasmarachFallsAdvice(c.At, PosOf(c.Target), c.Target, c.PartyIndex, c.AtRange);

        /// <summary>
        /// Below 30% Easmarach clears his aggro, walks up to the waterfall top and heals an eighth of his health each time he
        /// gets back there under 30% (EasmarachBrain). Nobody can walk up: casters, archers and healers go to their party's spot
        /// on the floor below and fight him from there (a spell or arrow reaches 1,250 up); melee wait at his spawn, where he
        /// comes back down to fight. Null while he is not up there (the fight as usual).
        /// </summary>
        public static Advice? EasmarachFallsAdvice(Vector3 raider, Vector3 easmarach, GameNPC boss, int partyIndex, bool atRange)
        {
            if (!OnTheFalls(easmarach)) return null;
            if (!atRange)
                return new(BotAction.StandOff, boss, Flat(raider, EasmarachSpawn) > EasmarachStandArrive ? EasmarachSpawn : null,
                    "easmarach-falls: on the waterfall top only spells and arrows reach him; melee wait at his spawn");
            Vector3 stand = EasmarachStand(partyIndex);
            if (Flat(raider, stand) > EasmarachStandArrive)
                return new(BotAction.Reposition, boss, stand, "easmarach-falls: to the floor spot below the waterfall top");
            return new(BotAction.None, boss, null, "easmarach-falls: shoot him on the waterfall top from below", "epic_falls");
        }

        /// <summary>Galladoria's last room, past the falls: the bridge's event, the primals, Olcasgean and his room's adds.</summary>
        public static readonly string[] GalladoriaLastRoom =
        [
            nameof(OlcasgeanInitializator), nameof(Olcasgean), nameof(Olcasgean2), "AirPrimal", "WaterPrimal", "FirePrimal",
            "EarthPrimal", "GuardianEarthmender", "MagicalEarthmender", "NaturalEarthmender", "ShadowyEarthmender", "Vortex",
            nameof(WaterfallAntipass), "TrailOfFire",
        ];

        /// <summary>
        /// With his push behind the falls on (AllowDisplacement for Easmarach: anyone within 1,000 of 37,653,52,843 while he
        /// lives is moved to 40,067,50,494), the way to the last room is shut until he dies ("With the death of the Easmarach,
        /// the current of the falls reduces significantly"). So the route never sends the raid there while he lives: a
        /// last-room encounter it chose gives way to him.
        /// </summary>
        private static GameNPC EasmarachBeforeTheLastRoom(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (chosen == null || chosen.CurrentRegionID != GalladoriaRegion || !InLastRoom(chosen)) return null;
            if (live?.OfType<Easmarach>().FirstOrDefault(e => Present(e) && e.CurrentRegionID == chosen.CurrentRegionID &&
                                                            EpicEncounterMechanics.AllowDisplacementFor(e)) is not { } easmarach)
                return null;
            reason = $"\"{easmarach.Name}\"'s current pushes the raid back from the falls while he lives";
            return easmarach;
        }

        public const ushort GalladoriaRegion = 191;

        /// <summary>The NPC is one of the last room's encounters (its class or a base class is listed).</summary>
        public static bool InLastRoom(GameNPC npc)
        {
            for (Type t = npc?.GetType(); t != null && t != typeof(GameNPC); t = t.BaseType)
                if (Array.IndexOf(GalladoriaLastRoom, t.Name) >= 0) return true;
            return false;
        }

        /// <summary>The Corrupt Primals of Galladoria's last room (the Fire primal's melee go to these while one lives).</summary>
        public static readonly string[] GalladoriaPrimals = ["WaterPrimal", "EarthPrimal", "AirPrimal", "FirePrimal"];

        /// <summary>
        /// The Fire primal walks its loop through the room without stopping and drops a trail of fire where it stands every
        /// 2-3 s; each patch burns everything within its radius every 2 s for 6 s (with AllowFullFirePatches, the original
        /// 220 in 450; else 120 in 300). Anyone in melee reach stands in the newest patch, so the hazard rule would send melee
        /// out and back for ever. Melee fight another living primal (one they can reach: the Air primal flies) and leave
        /// the Fire primal to casters and archers, who step out of the patches (its hazard rule). When it is the last one
        /// melee fight it and take the fire, and the healers keep them up.
        /// </summary>
        private static Advice? FirePrimalTrail(RuleContext c)
        {
            GameLiving raider = c.Raider;
            GameNPC target = c.Target;
            IReadOnlyList<GameNPC> live = c.Live ?? [];
            if (c.AtRange) return null;
            GameNPC other = live.Where(n => Present(n) && n != target && n.CurrentRegionID == target.CurrentRegionID &&
                                            n.GetType().Name is "WaterPrimal" or "EarthPrimal" && target.IsWithinRadius(n, AddSearchRadius))
                .OrderBy(n => raider.GetDistanceTo(n)).FirstOrDefault();
            if (other != null)
                return new(BotAction.Retarget, other, null, $"fire trail: melee take \"{other.Name}\", casters and archers the Fire primal");
            // The last primal: melee fight it in the fire (no step-out loop).
            return new(BotAction.None, target, null, "fire trail: the last primal, melee fight it in the fire");
        }
    }
}
