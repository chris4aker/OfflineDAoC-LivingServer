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
            // ---- Living Server 1.2: every big boss in (owner 2026-11-14) ----------------------------------------------
            new("PrimalOrder", ["AirPrimal", "WaterPrimal"], PrimalsInTheRoom)
            {
                Summary = "The primals in the period's order (Water, Fire, the Earthmenders and Earth, Air last): the route " +
                          "takes the next one alive. Below 30% the Water primal is immune while it walks to its pool and " +
                          "heals: nobody hits it until it fights again. The Air primal flies its ring 690 up: casters and " +
                          "archers shoot it from the room's middle, melee fight another primal; pets do 5 to it, so they don't.",
                Route = PrimalOrderRoute,
                NoPets = npc => IsOfType(npc, "AirPrimal"),
            },
            new(nameof(Olcasgean2), [nameof(Olcasgean2)], OneBodyOfOlcasgean)
            {
                Summary = "The two bodies share one health pool and each answers its own attackers with Olcasgean's Root (450, " +
                          "at most once a second): only the party holding the copy's safe spot fights the copy, everyone " +
                          "else fights Olcasgean himself.",
            },
            new(nameof(Hurionthex), [nameof(Hurionthex)], HurionthexForms)
            {
                Summary = "In his Granidon form (damage shield, 120 back per hit) the melee outside his target's party step " +
                          "back and the pets leave him until the form changes; casters fight on. His monkey form's Black " +
                          "Plague (350 around his target) is the catalog's spread.",
                NoPets = HurionthexShielded,
            },
            new(nameof(OrganicEnergyMechanism), [nameof(OrganicEnergyMechanism)], MechanismFromRange)
            {
                Summary = "The Mechanism never attacks; its Shield of World Soul hits back 150 per swing: casters and " +
                          "archers kill it from range, melee and pets kill its bottom feeders (3-5 every 25-35 s) and never " +
                          "swing at it (the period's tactic: \"never target the middle mechanism ... RANGE attack and GTAoE\").",
                NoPets = _ => true,
            },
            new(nameof(GiantSporiteCluster), [nameof(GiantSporiteCluster), "GSCAdds"], SporiteCircle)
            {
                Summary = "Eight bodies, one health pool: the raid fights inside one small circle around the cluster (casters " +
                          "and healers within 450 of it), area spells on the clump, any body is the boss.",
                Route = SporiteCopyRoute,
            },
            new(nameof(OlcasarGeomancer), [nameof(OlcasarGeomancer), "OGAdds"], OlcasarMinions)
            {
                Summary = "A slain minion rises again at its killer's feet: the party a minion fights holds it, everyone else " +
                          "kills the Geomancer (his death removes them all).",
                Route = OlcasarMinionRoute,
                RoutesInCombat = true,
            },
        ];

        // ---- Galladoria's last room: the primals ---------------------------------------------------------------------

        /// <summary>
        /// The order the period's raids took them in. Thredh's guide (Guinevere, May 2003): Water, Fire, Air, the Earthmenders
        /// (Magical first), Earth; the earlier US plan (VN Boards "Galladoria Notes", Jan 2003): Water first, then the menders
        /// one at a time and Earth. The first live kill (VN Boards, 7 May 2003, about 150 raiders) held Water and Earth in the
        /// safe spots while everyone else killed Fire, then the menders, Earth and Water, Air last. Here: Water, Fire, the
        /// Earthmenders and Earth (the Earth primal's own tactic sends the raid to its menders first), Air last; meanwhile the
        /// Fire primal's melee hold the Water and Earth primals (FirePrimalTrail). The route takes the first one alive.
        /// </summary>
        public static readonly string[] PrimalKillOrder = ["WaterPrimal", "FirePrimal", "EarthPrimal", "AirPrimal"];

        /// <summary>The middle of the Air primal's ring (AirPrimalBrain.Think: eleven points 1,050-1,230 around it, 690 up; the
        /// middle of their extent), where a shooter has it within 1,440 at every point of the ring.</summary>
        public static readonly Vector3 AirPrimalRingCentre = new(39589, 62547, 11685);
        /// <summary>A shooter this close (3D) to the Air primal shoots it.</summary>
        public const int AirPrimalShootReach = 1_450;

        /// <summary>The Water primal is walking to its pool or healing there (WaterPrimalBrain.dontattack: it takes no damage).
        /// Settable for tests.</summary>
        public static Func<GameNPC, bool> WaterPrimalRetreating = _ => DOL.AI.Brain.WaterPrimalBrain.dontattack;

        private static int PrimalRank(GameNPC npc) => npc == null ? -1 : Array.FindIndex(PrimalKillOrder, t => IsOfType(npc, t));

        /// <summary>The primal the route takes instead of <paramref name="chosen"/> (pure but for the lookup): the first of
        /// <see cref="PrimalKillOrder"/> alive in the room before it; null when it is the next.</summary>
        private static GameNPC PrimalOrderRoute(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            int rank = PrimalRank(chosen);
            if (rank <= 0 || chosen.CurrentRegionID != GalladoriaRegion || live == null) return null;
            GameNPC first = live.Where(n => Present(n) && PrimalRank(n) >= 0 && PrimalRank(n) < rank &&
                                            n.CurrentRegionID == chosen.CurrentRegionID && n.IsWithinRadius(chosen, AddSearchRadius))
                .OrderBy(PrimalRank).FirstOrDefault();
            if (first != null) reason = $"the primals in the period's order: \"{first.Name}\" before \"{chosen.Name}\"";
            return first;
        }

        private static Advice? PrimalsInTheRoom(RuleContext c)
        {
            if (IsOfType(c.Target, "WaterPrimal"))
                return WaterPrimalAdvice(c.Target, WaterPrimalRetreating(c.Target));
            if (!IsOfType(c.Target, "AirPrimal")) return null;
            GameNPC other = c.CanShootFlyers ? null : (c.Live ?? []).Where(n => Present(n) && n != c.Target && PrimalRank(n) >= 0 &&
                    !IsOfType(n, "AirPrimal") && n.CurrentRegionID == c.Target.CurrentRegionID && c.Target.IsWithinRadius(n, AddSearchRadius))
                .OrderBy(PrimalRank).FirstOrDefault();
            return AirPrimalAdvice(c.At, PosOf(c.Target), c.Target, c.CanShootFlyers, other);
        }

        /// <summary>The Water primal's retreat (pure): while it is immune nobody hits it (the raid waits where it stands).</summary>
        public static Advice? WaterPrimalAdvice(GameNPC water, bool retreating) => retreating
            ? new(BotAction.StandOff, water, null, "water primal: immune while it walks to its pool and heals; wait for it")
            : null;

        /// <summary>
        /// The Air primal (pure but for the advice's target): 900 health, it flies its ring 690 above the floor, never comes
        /// down and never melees; from the ring's middle a shooter has it within 1,450 at every point of the ring. Shooters in
        /// reach shoot; the others walk to the middle. Melee can't reach it: they fight <paramref name="other"/>, the next
        /// primal alive, or wait in the middle.
        /// </summary>
        public static Advice? AirPrimalAdvice(Vector3 at, Vector3 air, GameNPC boss, bool canShoot, GameNPC other)
        {
            if (canShoot)
                return Vector3.Distance(at, air) <= AirPrimalShootReach
                    ? new(BotAction.None, boss, null, "air primal: shoot it as it flies its ring")
                    : new(BotAction.Reposition, boss, AirPrimalRingCentre, "air primal: to the room's middle, it passes within reach");
            if (other != null)
                return new(BotAction.Retarget, other, null, $"air primal flies out of melee reach: melee on \"{other.Name}\"");
            return new(BotAction.StandOff, boss, Flat(at, AirPrimalRingCentre) > EasmarachStandArrive ? AirPrimalRingCentre : null,
                "air primal flies out of melee reach: melee wait in the room's middle");
        }

        // ---- Olcasgean's second body ---------------------------------------------------------------------------------

        /// <summary>A raider on the copy fights Olcasgean himself, unless its party holds the copy's safe spot (Signals.cs,
        /// SafeSpotAdvice, which sends it there and onto the copy).</summary>
        private static Advice? OneBodyOfOlcasgean(RuleContext c)
        {
            if (NearestOfType(c.Target, c.Live, nameof(Olcasgean)) is not { } main) return null;
            if (SafeSpotsFor(c.Target) is { } rule)
            {
                IEnumerable<int> parties = RaidersNear(c.Target, c.Live ?? []).Where(r => r != c.Raider).Select(PartyIndex);
                int spot = SpotOf(c.PartyIndex, c.Raider.ObjectID, parties);
                if (spot >= 0 && spot < rule.Bodies.Length && rule.Bodies[spot] == nameof(Olcasgean2)) return null;
            }
            return OneBodyAdvice(main);
        }

        /// <summary>The copy's attackers go to the main body (pure).</summary>
        public static Advice OneBodyAdvice(GameNPC main) =>
            new(BotAction.Retarget, main, null, "one body: the copy shares his health and roots whoever hits it; fight Olcasgean");

        // ---- Hurionthex ---------------------------------------------------------------------------------------------

        /// <summary>His Granidon form's model (HurionthexBrain.FormGranidon) and the gap the melee keep from him in it.</summary>
        public const int GranidonModel = 925, GranidonStepBack = 450;

        /// <summary>The boss has a damage shield up (DamageShield spells are FocusShield effects, EffectHelper). Settable for tests.</summary>
        public static Func<GameNPC, bool> HasDamageShield = npc =>
            npc?.effectListComponent?.ContainsEffectForEffectType(eEffect.FocusShield) == true;

        private static bool HurionthexShielded(GameNPC boss) => boss != null && (boss.Model == GranidonModel || HasDamageShield(boss));

        private static Advice? HurionthexForms(RuleContext c) =>
            HurionthexAdvice(c.At, PosOf(c.Target), c.Target, HurionthexShielded(c.Target), c.AtRange, c.Holds);

        /// <summary>Hurionthex (pure but for the advice's target): shielded, a melee raider outside his target's party steps
        /// back out of reach and waits; otherwise no opinion.</summary>
        public static Advice? HurionthexAdvice(Vector3 at, Vector3 boss, GameNPC hurionthex, bool shielded, bool atRange, bool holds)
        {
            if (!shielded || atRange || holds) return null;
            return new(BotAction.StandOff, hurionthex, Flat(at, boss) < GranidonStepBack ? PointAway(at, boss, GranidonStepBack + 50) : null,
                "hurionthex: Granidon form's damage shield; melee step back until the form changes");
        }

        // ---- the Organic-Energy Mechanism ------------------------------------------------------------------------------

        private static Advice? MechanismFromRange(RuleContext c)
        {
            GameNPC[] feeders = LiveOfType(c.Live, "OEMAdd").Where(f => f.CurrentRegionID == c.Target.CurrentRegionID && c.Target.IsWithinRadius(f, AddSearchRadius))
                .OrderBy(f => c.Raider.GetDistanceTo(f)).ThenBy(f => f.ObjectID).ToArray();
            return MechanismAdvice(c.At, PosOf(c.Target), c.Target, c.AtRange, feeders,
                f => f.TargetObject is GameLiving v && IsSoftTarget(v));
        }

        /// <summary>The Mechanism stands still and never attacks; the melee keep this far from it when no feeder is left.</summary>
        public const int MechanismStandOff = 500;

        /// <summary>
        /// The Mechanism (pure but for the advice's target): casters, archers and healers fight it as usual. Melee take a
        /// bottom feeder (one on a caster or healer first, else the nearest: <paramref name="feeders"/> come nearest first)
        /// and with none alive stand back out of its reach, never swinging at its shield.
        /// </summary>
        public static Advice? MechanismAdvice(Vector3 at, Vector3 mech, GameNPC mechanism, bool atRange, GameNPC[] feeders,
            Func<GameNPC, bool> onSoftTarget)
        {
            if (atRange) return null;
            GameNPC feeder = feeders.FirstOrDefault(onSoftTarget) ?? feeders.FirstOrDefault();
            if (feeder != null)
                return new(BotAction.Retarget, feeder, null, "mechanism: melee on its bottom feeders, never on its shield");
            return new(BotAction.StandOff, mechanism, Flat(at, mech) < MechanismStandOff ? PointAway(at, mech, MechanismStandOff + 50) : null,
                "mechanism: melee keep off its Shield of World Soul; casters and archers kill it");
        }

        // ---- the Giant Sporite Cluster ---------------------------------------------------------------------------------

        /// <summary>Casters and healers fight from within this of the cluster (the period: "a small radius 300 circle ... no
        /// one leaves circle"; "if we split up the sporites just pick off the casters"), and step in to this when further.</summary>
        public const int SporiteCircleRadius = 450, SporiteCircleStepIn = 300;

        private static Advice? SporiteCircle(RuleContext c)
        {
            GameNPC cluster = NearestOfType(c.Target, c.Live, nameof(GiantSporiteCluster));
            if (cluster == null || !cluster.InCombat) return null;
            return SporiteCircleAdvice(c.At, PosOf(cluster), c.Target, c.AtRange);
        }

        /// <summary>The circle (pure but for the advice's target): a raider at range further than
        /// <see cref="SporiteCircleRadius"/> from the cluster steps in.</summary>
        public static Advice? SporiteCircleAdvice(Vector3 at, Vector3 cluster, GameNPC target, bool atRange)
        {
            if (!atRange || Flat(at, cluster) <= SporiteCircleRadius) return null;
            return new(BotAction.Reposition, target, PointAway(at, cluster, SporiteCircleStepIn),
                $"sporite cluster: everyone in one circle within {SporiteCircleRadius} of the cluster");
        }

        /// <summary>A copy chosen by the route is the cluster's fight (one health pool).</summary>
        private static GameNPC SporiteCopyRoute(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (chosen == null || !IsOfType(chosen, "GSCAdds")) return null;
            GameNPC cluster = live?.OfType<GiantSporiteCluster>().FirstOrDefault(n => Present(n) && n.CurrentRegionID == chosen.CurrentRegionID &&
                                                                                   n.IsWithinRadius(chosen, AddSearchRadius));
            if (cluster != null) reason = "one health pool: the copy is the cluster's fight";
            return cluster;
        }

        // ---- Olcasar Geomancer ---------------------------------------------------------------------------------------

        private static Advice? OlcasarMinions(RuleContext c)
        {
            if (!IsOfType(c.Target, "OGAdds") || NearestOfType(c.Target, c.Live, nameof(OlcasarGeomancer)) is not { } olcasar) return null;
            return OlcasarMinionAdvice(c.Target, olcasar, c.Target.TargetObject is GameLiving victim && SameParty(c.Raider, victim));
        }

        /// <summary>A minion (pure): the party it fights holds it (the period: "just kept one group on his pets"), everyone else
        /// fights the Geomancer.</summary>
        public static Advice OlcasarMinionAdvice(GameNPC minion, GameNPC olcasar, bool itFightsMyParty) => itFightsMyParty
            ? new(BotAction.None, minion, null, "olcasar: this party holds the minion that fights it")
            : new(BotAction.Retarget, olcasar, null, "olcasar: a slain minion rises again; kill the Geomancer");

        private static GameNPC OlcasarMinionRoute(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (chosen == null || !IsOfType(chosen, "OGAdds")) return null;
            GameNPC olcasar = live?.OfType<OlcasarGeomancer>().FirstOrDefault(n => Present(n) && n.CurrentRegionID == chosen.CurrentRegionID &&
                                                                                 n.IsWithinRadius(chosen, AddSearchRadius));
            if (olcasar != null) reason = "a slain minion rises again at its killer: fight the Geomancer";
            return olcasar;
        }

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
        /// last-room encounter it chose, or any monster that stands or spawns behind the falls (<see cref="BarredByTheFalls"/>),
        /// gives way to him.
        /// <para>Owner's PC, 2026-11-15 (build 1dc66b6, /epictest sweep): the gate looked only at the last room's classes.
        /// After Spindler Broodmother the route took a "deep shrouder" (an epic monster of the wing behind the falls, at
        /// 37,016,56,449) and mustered for it at 37,086,55,050; the raid walked into the push 165 times in eight minutes
        /// (78% fall damage each), mustered=0 needed=78, and never fought Easmarach.</para>
        /// </summary>
        private static GameNPC EasmarachBeforeTheLastRoom(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (chosen == null || chosen is Easmarach || chosen.CurrentRegionID != GalladoriaRegion) return null;
            if (!InLastRoom(chosen) && !BarredByTheFalls(PosOf(chosen)) &&
                !BarredByTheFalls(new Vector3(chosen.SpawnPoint.X, chosen.SpawnPoint.Y, chosen.SpawnPoint.Z)))
                return null;
            if (FallsBarrierOn(live, chosen.CurrentRegionID) is not { } easmarach) return null;
            reason = $"\"{easmarach.Name}\"'s current pushes the raid back from the falls while he lives";
            return easmarach;
        }

        // ---- The falls barrier: nothing behind it while he lives ------------------------------------------------------

        /// <summary>The middle of his push (EasmarachBrain.Think, "you shall not pass!"): anyone within
        /// <see cref="FallsPushRadius"/> of it while he lives is moved to <see cref="FallsPushLanding"/>.</summary>
        public static readonly Vector3 FallsPushCentre = new(37653, 52843, 10758);
        public const int FallsPushRadius = 1_000;
        /// <summary>Where the push puts a raider: 40,067,50,494 (the script's 11,708 is in the air; the navmesh floor there is
        /// 9,762, EPIC_DISPLACEMENT landing= on 2026-11-15). In front of the falls, 2,149 from his spawn.</summary>
        public static readonly Vector3 FallsPushLanding = new(40067, 50494, 9762);
        /// <summary>Where the 2026-11-15 raid walked into the push (EPIC_DISPLACEMENT from=) and the muster behind it that it
        /// was walking to: the corridor through the falls runs from the first to the second.</summary>
        public static readonly Vector3 FallsCorridorIn = new(38376, 52481, 10277), FallsCorridorOut = new(37086, 55050, 10925);
        /// <summary>The wing behind the falls lies within these X (the deep shrouders, the bridge, the primals and Olcasgean:
        /// 37,016 to 41,580). The Spindler hall (X about 21,000) and Xaga (27,482) share its Y but not its X.</summary>
        public const int FallsWingMinX = 33_000, FallsWingMaxX = 45_000;

        /// <summary>The point is in his push (flat, so a raider above or below the circle counts too: the route never sends
        /// anyone near it).</summary>
        public static bool InFallsPush(Vector3 p) => Flat(p, FallsPushCentre) <= FallsPushRadius;

        /// <summary>The point lies in the wing behind the falls (pure): within <see cref="FallsWingMinX"/> and
        /// <see cref="FallsWingMaxX"/>, past the line through the push's middle across the corridor.</summary>
        public static bool BehindTheFalls(Vector3 p)
        {
            if (p.X < FallsWingMinX || p.X > FallsWingMaxX) return false;
            Vector2 through = Vector2.Normalize(new(FallsCorridorOut.X - FallsCorridorIn.X, FallsCorridorOut.Y - FallsCorridorIn.Y));
            return Vector2.Dot(new(p.X - FallsPushCentre.X, p.Y - FallsPushCentre.Y), through) > 0;
        }

        /// <summary>The point can't be reached while his push is on: in it, or behind it (pure).</summary>
        public static bool BarredByTheFalls(Vector3 p) => InFallsPush(p) || BehindTheFalls(p);

        /// <summary>The living Easmarach whose push is on in <paramref name="region"/>, or null (dead, gone, or the owner's
        /// switch AllowDisplacement@Easmarach off).</summary>
        public static GameNPC FallsBarrierOn(IReadOnlyList<GameNPC> live, ushort region) =>
            region != GalladoriaRegion ? null
                : live?.OfType<Easmarach>().FirstOrDefault(e => Present(e) && e.CurrentRegionID == region &&
                                                               EpicEncounterMechanics.AllowDisplacementFor(e));

        /// <summary>A point the raid route must not send anyone to: behind a live boss's barrier (Easmarach's falls).</summary>
        public static bool BehindLiveBarrier(ushort region, Vector3 point, IReadOnlyList<GameNPC> live) =>
            region == GalladoriaRegion && BarredByTheFalls(point) && FallsBarrierOn(live, region) != null;

        /// <summary>Where the route sends the raid instead of <paramref name="point"/> (a muster, a staging): the point itself,
        /// or, behind a live barrier, the floor in front of it where the barrier puts raiders.</summary>
        public static Vector3 OutOfLiveBarrier(ushort region, Vector3 point, IReadOnlyList<GameNPC> live) =>
            BehindLiveBarrier(region, point, live) ? FallsPushLanding : point;

        /// <summary>The walk back from <paramref name="from"/> to <paramref name="to"/> goes into the falls (pure): the end is
        /// in the push or behind it, or the straight way passes through the push.</summary>
        public static bool WalksIntoTheFalls(Vector3 from, Vector3 to)
        {
            if (BarredByTheFalls(to)) return true;
            Vector2 a = new(from.X, from.Y), b = new(to.X, to.Y), c = new(FallsPushCentre.X, FallsPushCentre.Y);
            Vector2 ab = b - a;
            float t = ab.LengthSquared() < 1 ? 0 : Math.Clamp(Vector2.Dot(c - a, ab) / ab.LengthSquared(), 0, 1);
            return Vector2.Distance(a + ab * t, c) <= FallsPushRadius;
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
