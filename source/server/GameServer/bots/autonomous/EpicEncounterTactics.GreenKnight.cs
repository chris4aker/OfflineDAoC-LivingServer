using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// The Green Knight's trees (Living Server 1.2.2, #412; tests only, and in its model a raid of 40 still loses). His
    /// script (OFGreenKnight.cs, unchanged) grows a rotting downy felwood (800 health, level 47-49, a swing every 2.88 s) at
    /// every living raider within 4,000 of him every 25-35 s while he has aggro, up to 70 a wave, never at a pet
    /// (EncounterTargets.InRadius); the trees stay until killed. A tree takes one living thing within 800 of it, pets
    /// included (StandardMobBrain.CheckNpcAggro), silences a caster or healer it hits (94% a swing) and turns to the highest
    /// threat (damage, and half of every heal on the one it hits: HealSpellHandler), counted less past 250 from it (a third
    /// at 1,500). At every 10% he walks to one of four points (GkTeleport), heals there, and then goes back for whoever has
    /// his aggro. Every 40-60 s he fights a random healer within 2,500 (PickHeal).
    /// <para>The plan, every raider in reach of what it does (owner, 2026-11-23: no benched roles):</para>
    /// <list type="bullet">
    /// <item>Spots are laid out from his position once, along the line away from his tank (<see cref="GreenKnightKeepsPlan"/>):
    /// kept while he walks, while he fights a healer he picked, and until he has moved 600 from them; each is snapped to the
    /// navmesh and must see him (<see cref="GreenKnightPickSpot"/>); a group's members stand on a small ring round it
    /// (<see cref="GreenKnightSlot"/>).</item>
    /// <item>Healers (not the melee hybrids) stand <see cref="GreenKnightHealDistance"/> from him, within the melee's peel
    /// reach and clear of Might of the Forest; the <see cref="GreenKnightTankHealerCount"/> nearest his target heal it first
    /// under <see cref="GreenKnightTankHealBelow"/>%; a raider a tree hits waits for a heal until under
    /// <see cref="GreenKnightTreedHealBelow"/>% ("gk-heal-spot").</item>
    /// <item>Area casters stand <see cref="GreenKnightBurnDistance"/> from him with one melee of their party and burn the
    /// trees a fighter has tagged, while their spell would not pull them ("gk-burn").</item>
    /// <item>Casters and archers without an area spell stand <see cref="GreenKnightShootDistance"/> from him and shoot the
    /// tagged trees their shots would not pull, the most hurt first, else him ("gk-shot").</item>
    /// <item>Theurgists stand <see cref="GreenKnightTheurgistDistance"/> from him and send their elementals at the trees on
    /// healers, casters and archers within their summon range, else at him ("gk-summon").</item>
    /// <item>The owner of a lasting pet (Necromancer, Cabalist and the like) stands <see cref="GreenKnightOutDistance"/> from
    /// him, opposite the healers, where no tree grows at it, and keeps its pet on the trees near the pet, else on him
    /// (pets follow their orders to 10,000: ControlledMobBrain.MAX_OWNER_FOLLOW_DIST; "gk-pet").</item>
    /// <item>The melee tag (<see cref="GreenKnightTagTree"/>): finish a tag on a tree hitting it, take one off a healer or
    /// caster within <see cref="GreenKnightPeelSoftReach"/>, tag untagged trees within <see cref="GreenKnightTagReach"/> to
    /// <see cref="GreenKnightTagThreat"/>, finish burned trees on itself under half, else him ("gk-tag", "gk-peel",
    /// "gk-finish").</item>
    /// <item>While he walks the raid holds its ground and keeps on the trees: he comes back to his target ("gk-hold").</item>
    /// </list>
    /// Log: EPIC_GK_TREES (every 30 s: alive, held, whom they hit), EPIC_TACTIC_BOT lines with the gk-* reasons. Autonomous
    /// gamebots only. UT_RaidSim122GreenKnight models it (tools/raid-sims/gk_mirror.py).
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>#432's reach: a melee raider took a tree within this of itself (kept for the model's before/after).</summary>
        public const int GreenKnightTreeReach = 800;
        /// <summary>A melee raider peels a tree within this of itself (the stack stands 750 from him).</summary>
        public const int GreenKnightPeelReach = 1_200;
        /// <summary>1.2.1 round 2: the casters, archers and healers stood this far from him.</summary>
        public const int GreenKnightStackDistance = 750;
        /// <summary>A raider within this of the stack spot is at it.</summary>
        public const int GreenKnightStackSlack = 150;
        /// <summary>Might of the Forest's radius around his target (OFGreenKnight.GreenKnightHeatDD: 500).</summary>
        public const int GreenKnightHeatRadius = 500;

        /// <summary>Whom a tree hits, as the peel ranks it: an area caster, a healer, another caster or archer, anyone else.</summary>
        public const int TreeOnAreaCaster = 0, TreeOnHealer = 1, TreeOnCaster = 2, TreeOnOther = 3;

        // ---- 1.2.2 tag and burn, every role in reach ------------------------------------------------------------------

        /// <summary>His script grows a tree at every raider within this of him (OFGreenKnight.SpawnTrees).</summary>
        public const int GreenKnightGrowRadius = 4_000;
        /// <summary>The healers stand this far from him: within the melee's 200 + 500 peel reach, 800 from his tank.</summary>
        public const int GreenKnightHealDistance = 650;
        /// <summary>The area casters and their guards stand this far from him.</summary>
        public const int GreenKnightBurnDistance = 700;
        /// <summary>The Theurgists stand this far from him (their summon reaches 2,000).</summary>
        public const int GreenKnightTheurgistDistance = 850;
        /// <summary>The casters and archers without an area spell stand this far from him (spells 1,500, bows 1,700).</summary>
        public const int GreenKnightShootDistance = 1_300;
        /// <summary>The owners of lasting pets stand this far from him, past <see cref="GreenKnightGrowRadius"/>, opposite the
        /// healers; and stay where they are once <see cref="GreenKnightOutHold"/> from him.</summary>
        public const int GreenKnightOutDistance = 4_600, GreenKnightOutHold = 4_300;
        /// <summary>A raider within this of its slot is at it.</summary>
        public const int GreenKnightSpotSlack = 150;
        /// <summary>A group's members stand on a ring this wide round its spot (six slots, the first on the spot).</summary>
        public const int GreenKnightSlotRing = 100;
        /// <summary>The spots are laid out again once he stands this far from where they were laid out (not while he walks
        /// or fights a healer he picked).</summary>
        public const int GreenKnightReplanDistance = 600;
        /// <summary>A melee raider has tagged a tree once its threat on it is this (three seconds of a melee raider's hits).</summary>
        public const int GreenKnightTagThreat = 240;
        /// <summary>A melee raider tags the trees within this of itself.</summary>
        public const int GreenKnightTagReach = 300;
        /// <summary>A melee raider takes a tree off a healer, caster or archer within this of itself.</summary>
        public const int GreenKnightPeelSoftReach = 500;
        /// <summary>A guard kills the trees within this of itself (around the area casters).</summary>
        public const int GreenKnightGuardReach = 300;
        /// <summary>A melee raider finishes a tree on itself at or under this (percent): the area casters burned it.</summary>
        public const int GreenKnightOwnBelow = 50;
        /// <summary>Melee of an area caster's party who stand with it (the first by object id).</summary>
        public const int GreenKnightGuardsPerParty = 1;
        /// <summary>The threat an area caster's next spell adds (Ebullient Blast 158 before resists, rounded up).</summary>
        public const int GreenKnightBurnThreat = 160;
        /// <summary>The threat a caster's or archer's next shots add (a nuke or two arrows, rounded up).</summary>
        public const int GreenKnightShotThreat = 300;
        /// <summary>An area caster burns trees within this of itself (its spells' range).</summary>
        public const int GreenKnightSpellReach = 1_500;
        /// <summary>A Theurgist's summon reaches this far (SummonTheurgistPet, range 2,000).</summary>
        public const int GreenKnightSummonReach = 2_000;
        /// <summary>This many healers, the nearest his target, heal it first (as Legion's tank healers).</summary>
        public const int GreenKnightTankHealerCount = 4;
        /// <summary>The tank healers heal his target first while it is under this (percent).</summary>
        public const int GreenKnightTankHealBelow = 98;
        /// <summary>A raider a tree hits is left unhealed until it is under this (percent).</summary>
        public const int GreenKnightTreedHealBelow = 35;

        /// <summary>A threat as a tree counts it (pure; StandardMobBrain.CleanUpAggroListAndGetHighestModifiedThreat): in full
        /// within 250 of the tree, past that less, a third at 1,500.</summary>
        public static double GreenKnightEffectiveThreat(double raw, double distance) =>
            distance <= 250 ? raw : raw * Math.Exp(Math.Log(1 / 3.0) / (1500 - 250) * (distance - 250));

        /// <summary>One tree as a melee raider (or pet) sees it. <paramref name="HolderThreat"/>: the threat on it of the
        /// fighter (tank, melee or pet) it hits, 0 otherwise; <paramref name="OnSoft"/>: it hits a healer, caster or archer
        /// other than this raider.</summary>
        public readonly record struct TagView(long Id, double Distance, bool OnMe, double MyThreat, double HolderThreat, bool OnSoft,
            int HealthPercent);

        /// <summary>
        /// Which tree a melee raider (or a pet) takes (pure; an index into <paramref name="trees"/>, -1: none, on him). In
        /// order: the first tree that hits it within <see cref="GreenKnightTagReach"/> with its threat on it above 0 and under
        /// <see cref="GreenKnightTagThreat"/> (finish the tag); a tree on a healer, caster or archer within
        /// <see cref="GreenKnightPeelSoftReach"/>, the one at <paramref name="raiderId"/> modulo their count; the tree within
        /// <see cref="GreenKnightTagReach"/> no fighter holds yet, one on itself first, then the nearest; then the most hurt
        /// tree on itself (a guard: or within <see cref="GreenKnightGuardReach"/>) at or under <see cref="GreenKnightOwnBelow"/>%
        /// (any health for a guard, or with no area caster left).
        /// </summary>
        public static int GreenKnightTagTree(IReadOnlyList<TagView> trees, long raiderId, bool guard, bool areaCastersLeft)
        {
            if (trees == null || trees.Count == 0) return -1;
            for (int i = 0; i < trees.Count; i++)
            {
                TagView t = trees[i];
                if (t.OnMe && t.MyThreat > 0 && t.MyThreat < GreenKnightTagThreat && t.Distance <= GreenKnightTagReach) return i;
            }
            var soft = new List<int>();
            for (int i = 0; i < trees.Count; i++)
                if (trees[i].OnSoft && trees[i].Distance <= GreenKnightPeelSoftReach) soft.Add(i);
            if (soft.Count > 0) return soft[(int)(Math.Abs(raiderId) % soft.Count)];
            int best = -1;
            for (int i = 0; i < trees.Count; i++)
            {
                TagView t = trees[i];
                if (t.Distance > GreenKnightTagReach || t.HolderThreat >= GreenKnightTagThreat) continue;
                if (best < 0) { best = i; continue; }
                TagView b = trees[best];
                int cmp = (t.OnMe ? 0 : 1).CompareTo(b.OnMe ? 0 : 1);
                if (cmp == 0) cmp = t.Distance.CompareTo(b.Distance);
                if (cmp < 0) best = i;
            }
            if (best >= 0) return best;
            int pick = -1;
            for (int i = 0; i < trees.Count; i++)
            {
                TagView t = trees[i];
                if (!guard && areaCastersLeft && t.HealthPercent > GreenKnightOwnBelow) continue;
                if (!(t.OnMe || guard && t.Distance <= GreenKnightGuardReach)) continue;
                if (pick < 0 || t.HealthPercent < trees[pick].HealthPercent) pick = i;
            }
            return pick;
        }

        /// <summary>One tree as an area caster sees it: where it is, how far, its own threat on it and the holding fighter's.</summary>
        public readonly record struct BurnView(long Id, double X, double Y, double Distance, double MyThreat, double HolderThreat);

        /// <summary>
        /// Where an area caster aims (pure; an index into <paramref name="trees"/>, -1: none, on him): among the trees within
        /// <see cref="GreenKnightSpellReach"/> that a fighter holds (<see cref="GreenKnightTagThreat"/> or more) and that its
        /// spell would not pull (its threat after <paramref name="spellThreat"/> more, as the tree counts it at its distance,
        /// still under the holder's), the one with the most of them within <paramref name="radius"/>; two at least.
        /// </summary>
        public static int GreenKnightBurnTree(IReadOnlyList<BurnView> trees, double spellThreat, double radius)
        {
            if (trees == null) return -1;
            var ok = new List<int>();
            for (int i = 0; i < trees.Count; i++)
            {
                BurnView t = trees[i];
                if (t.Distance > GreenKnightSpellReach || t.HolderThreat < GreenKnightTagThreat ||
                    GreenKnightEffectiveThreat(t.MyThreat + spellThreat, t.Distance) >= t.HolderThreat) continue;
                ok.Add(i);
            }
            int best = -1, hits = 0;
            foreach (int i in ok)
            {
                int c = 0;
                foreach (int j in ok)
                {
                    double dx = trees[j].X - trees[i].X, dy = trees[j].Y - trees[i].Y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= radius) c++;
                }
                if (c > hits) { best = i; hits = c; }
            }
            return hits >= 2 ? best : -1;
        }

        /// <summary>One tree as a caster or archer without an area spell sees it.</summary>
        public readonly record struct ShotView(long Id, double Distance, double MyThreat, double HolderThreat, int HealthPercent);

        /// <summary>
        /// Which tree a caster or archer without an area spell shoots (pure; an index, -1: none, on him): the most hurt within
        /// <paramref name="reach"/> that a fighter holds (<see cref="GreenKnightTagThreat"/> or more) and that its next shots
        /// (<paramref name="shotThreat"/> more, as the tree counts it at its distance) would not pull; the first on a tie.
        /// </summary>
        public static int GreenKnightShotTree(IReadOnlyList<ShotView> trees, double shotThreat, double reach)
        {
            if (trees == null) return -1;
            int pick = -1;
            for (int i = 0; i < trees.Count; i++)
            {
                ShotView t = trees[i];
                if (t.Distance > reach || t.HolderThreat < GreenKnightTagThreat ||
                    GreenKnightEffectiveThreat(t.MyThreat + shotThreat, t.Distance) >= t.HolderThreat) continue;
                if (pick < 0 || t.HealthPercent < trees[pick].HealthPercent) pick = i;
            }
            return pick;
        }

        /// <summary>
        /// Where a Theurgist sends its next elemental (pure; an index, -1: at him): the nearest tree within
        /// <see cref="GreenKnightSummonReach"/> that hits a healer, caster, archer or Theurgist (itself included; an elemental
        /// that hits it takes its threat); the first on a tie.
        /// </summary>
        public static int GreenKnightSummonTree(IReadOnlyList<(double Distance, bool OnSoft)> trees)
        {
            if (trees == null) return -1;
            int best = -1;
            for (int i = 0; i < trees.Count; i++)
            {
                if (!trees[i].OnSoft || trees[i].Distance > GreenKnightSummonReach) continue;
                if (best < 0 || trees[i].Distance < trees[best].Distance) best = i;
            }
            return best;
        }

        /// <summary>A heal on a raider a tree hits waits while it is at or over <see cref="GreenKnightTreedHealBelow"/>% (pure).</summary>
        public static bool GreenKnightHoldsHeal(int healthPercent, bool treeOnIt) => treeOnIt && healthPercent >= GreenKnightTreedHealBelow;

        /// <summary>The spots stay where they are (pure): there are spots, and he walks, or fights a healer he picked, or still
        /// stands within <see cref="GreenKnightReplanDistance"/> of where they were laid out.</summary>
        public static bool GreenKnightKeepsPlan(bool hasPlan, bool walking, bool fightsAHealer, float fromAnchor) =>
            hasPlan && (walking || fightsAHealer || fromAnchor <= GreenKnightReplanDistance);

        /// <summary>The line away from his target (pure; unit length; east when he stands on it).</summary>
        public static Vector2 GreenKnightAxis(Vector3 boss, Vector3 target)
        {
            Vector2 away = new(boss.X - target.X, boss.Y - target.Y);
            return away.Length() < 1 ? new(1, 0) : Vector2.Normalize(away);
        }

        /// <summary>The spots to try for one group (pure): <paramref name="distance"/> from <paramref name="anchor"/> along
        /// <paramref name="axis"/> (negative: the other way), then turned 30, -30, 60, -60, 90 and -90 degrees.</summary>
        public static Vector3[] GreenKnightSpotCandidates(Vector3 anchor, Vector2 axis, float distance)
        {
            float[] turns = [0, 30, -30, 60, -60, 90, -90];
            return turns.Select(deg =>
            {
                double a = deg * Math.PI / 180;
                Vector2 d = new((float)(axis.X * Math.Cos(a) - axis.Y * Math.Sin(a)), (float)(axis.X * Math.Sin(a) + axis.Y * Math.Cos(a)));
                return new Vector3(anchor.X + d.X * distance, anchor.Y + d.Y * distance, anchor.Z);
            }).ToArray();
        }

        /// <summary>The first candidate the navmesh has a floor for (<paramref name="snap"/>: the floor point, null for none)
        /// that sees him (<paramref name="sees"/>), snapped; the first candidate as it is when none does (pure).</summary>
        public static Vector3 GreenKnightPickSpot(IReadOnlyList<Vector3> candidates, Func<Vector3, Vector3?> snap, Func<Vector3, bool> sees)
        {
            foreach (Vector3 c in candidates)
                if (snap?.Invoke(c) is Vector3 floor && (sees == null || sees(floor)))
                    return floor;
            return candidates[0];
        }

        /// <summary>A raider's slot in its group (pure): the spot itself for index 0 (mod 7), else one of six points
        /// <see cref="GreenKnightSlotRing"/> round it.</summary>
        public static Vector3 GreenKnightSlot(Vector3 spot, long index)
        {
            int k = (int)(Math.Abs(index) % 7);
            if (k == 0) return spot;
            double a = (k - 1) * Math.PI / 3;
            return new(spot.X + (float)(Math.Cos(a) * GreenKnightSlotRing), spot.Y + (float)(Math.Sin(a) * GreenKnightSlotRing), spot.Z);
        }

        /// <summary>The <paramref name="count"/> healers nearest his target, the lower id first on a tie (pure).</summary>
        public static HashSet<long> GreenKnightTankHealers(IEnumerable<(long Id, double Distance)> healers, int count) =>
            healers.OrderBy(h => h.Distance).ThenBy(h => h.Id).Take(Math.Max(0, count)).Select(h => h.Id).ToHashSet();

        [DungeonRules]
        private static IEnumerable<DungeonRule> GreenKnightRules() =>
        [
            new("GreenKnightTrees", [nameof(OFGreenKnight), nameof(GKTrees)], GreenKnightTagAndBurn)
            {
                Summary = "A tree grows at every raider within 4,000 every 25-35 s, stays, silences a caster or healer it hits and " +
                          "turns to the most threat (half of each heal on its victim): every raider fights from where it reaches - " +
                          $"the healers {GreenKnightHealDistance} from him away from his tank (4 heal his target first; a raider a tree " +
                          $"hits only under {GreenKnightTreedHealBelow}%), the area casters {GreenKnightBurnDistance} with a melee guard " +
                          "burning tagged trees, the other casters and archers " + GreenKnightShootDistance + " shooting tagged trees or " +
                          "him, Theurgists' elementals on the trees on the soft raiders, pet owners " + GreenKnightOutDistance +
                          " out with their pets in; the melee tag every tree and go back to him; the raid holds while he walks.",
            },
        ];

        // ---- 1.2.1 round 2 (kept for the models' before/after; no rule uses them now) --------------------------------


        /// <summary>One tree as a melee raider sees it. <paramref name="VictimRank"/>: whom it hits (<see cref="TreeOnAreaCaster"/>
        /// .. <see cref="TreeOnOther"/>).</summary>
        public readonly record struct TreeView(long Id, float Distance, bool OnMe, int HealthPercent, int VictimRank = TreeOnOther);

        /// <summary>
        /// #432's choice (pure; kept for the model's before/after, no rule uses it now): one attacking the raider, else the
        /// nearest within <see cref="GreenKnightTreeReach"/>; ties the most hurt, then the lowest id; -1: stay on him.
        /// </summary>
        public static int GreenKnightMeleeTree(IReadOnlyList<TreeView> trees)
        {
            if (trees == null) return -1;
            int best = -1;
            for (int i = 0; i < trees.Count; i++)
            {
                TreeView t = trees[i];
                if (t.Distance > GreenKnightTreeReach) continue;
                if (best < 0) { best = i; continue; }
                TreeView b = trees[best];
                int cmp = (t.OnMe ? 0 : 1).CompareTo(b.OnMe ? 0 : 1);
                if (cmp == 0) cmp = t.Distance.CompareTo(b.Distance);
                if (cmp == 0) cmp = t.HealthPercent.CompareTo(b.HealthPercent);
                if (cmp == 0) cmp = t.Id.CompareTo(b.Id);
                if (cmp < 0) best = i;
            }
            return best;
        }

        /// <summary>
        /// Which tree a melee raider takes (pure; an index into <paramref name="trees"/>, -1: none, back on him). First one
        /// that hits a caster or a healer within <see cref="GreenKnightPeelReach"/>, by rank (area casters, then healers, then
        /// the other casters and archers); among those of the best rank, ordered by id, the one at
        /// <paramref name="raiderId"/> modulo their count, so the raid's melee take different trees. Else one hitting the
        /// raider itself, the most hurt first, then the lowest id.
        /// </summary>
        public static int GreenKnightPeelTree(IReadOnlyList<TreeView> trees, long raiderId)
        {
            if (trees == null || trees.Count == 0) return -1;
            int bestRank = int.MaxValue;
            foreach (TreeView t in trees)
                if (t.VictimRank <= TreeOnCaster && t.Distance <= GreenKnightPeelReach && !t.OnMe && t.VictimRank < bestRank)
                    bestRank = t.VictimRank;
            if (bestRank != int.MaxValue)
            {
                int[] soft = Enumerable.Range(0, trees.Count)
                    .Where(i => trees[i].VictimRank == bestRank && trees[i].Distance <= GreenKnightPeelReach && !trees[i].OnMe)
                    .OrderBy(i => trees[i].Id).ToArray();
                return soft[(int)(Math.Abs(raiderId) % soft.Length)];
            }
            int pick = -1;
            for (int i = 0; i < trees.Count; i++)
            {
                TreeView t = trees[i];
                if (!t.OnMe) continue;
                if (pick < 0 || t.HealthPercent < trees[pick].HealthPercent ||
                    t.HealthPercent == trees[pick].HealthPercent && t.Id < trees[pick].Id)
                    pick = i;
            }
            return pick;
        }

        /// <summary>
        /// Where the casters, archers and healers stand (pure): <see cref="GreenKnightStackDistance"/> from him on the side
        /// away from his target. A spot already in use (<paramref name="previous"/>) is kept while it is 500-1,100 from him
        /// and clear of <see cref="GreenKnightHeatRadius"/> around his target, so the stack does not run about when he turns
        /// to a healer for a moment.
        /// </summary>
        public static Vector3 GreenKnightStackSpot(Vector3 boss, Vector3 target, Vector3? previous)
        {
            if (previous is Vector3 p)
            {
                float fromBoss = Vector2.Distance(new(p.X, p.Y), new(boss.X, boss.Y));
                float fromTarget = Vector2.Distance(new(p.X, p.Y), new(target.X, target.Y));
                if (fromBoss >= 500 && fromBoss <= 1_100 && fromTarget > GreenKnightHeatRadius + 50) return p;
            }
            Vector2 away = new(boss.X - target.X, boss.Y - target.Y);
            if (away.Length() < 1) away = new(1, 0);
            away = Vector2.Normalize(away) * GreenKnightStackDistance;
            return new(boss.X + away.X, boss.Y + away.Y, boss.Z);
        }

        private static readonly Dictionary<GameNPC, long> LastGreenKnightTreeLog = new();

        /// <summary>Whom a tree hits, ranked for the log.</summary>
        private static int TreeVictimRank(GameLiving victim, long now)
        {
            if (victim == null) return TreeOnOther;
            string role = RoleOf(victim);
            if (role == "caster" && victim is GameBot bot && AreaRadiusOf(bot, now) > 0) return TreeOnAreaCaster;
            if (role == "healer") return TreeOnHealer;
            return role is "caster" or "archer" ? TreeOnCaster : TreeOnOther;
        }

        /// <summary>His spots, laid out from where he stood (<see cref="GreenKnightKeepsPlan"/>).</summary>
        private sealed class GreenKnightPlan
        {
            public Vector3 Anchor;
            public Vector2 Axis;
            public readonly Dictionary<int, Vector3> Spots = new();
        }

        private static readonly Dictionary<GameNPC, GreenKnightPlan> GreenKnightPlans = new();
        private static readonly Dictionary<GameNPC, long> GreenKnightActive = new();
        private static readonly Dictionary<GameNPC, (long Tick, GameLiving Target, HashSet<long> Healers)> GreenKnightTankTeams = new();

        private static bool GreenKnightWalks(GameNPC gk) => gk.Brain is OFGreenKnightBrain { IsWalking: true };

        /// <summary>His plan, laid out again only when <see cref="GreenKnightKeepsPlan"/> says so.</summary>
        private static GreenKnightPlan GreenKnightPlanFor(GameNPC gk)
        {
            Vector3 boss = PosOf(gk);
            GameLiving target = gk.TargetObject as GameLiving;
            bool fightsAHealer = target != null && IsSoftRole(target);
            lock (GreenKnightPlans)
            {
                GreenKnightPlans.TryGetValue(gk, out GreenKnightPlan plan);
                if (GreenKnightKeepsPlan(plan != null, GreenKnightWalks(gk), fightsAHealer,
                        plan == null ? 0 : Vector2.Distance(new(plan.Anchor.X, plan.Anchor.Y), new(boss.X, boss.Y))))
                    return plan;
                if (GreenKnightPlans.Count > 8) GreenKnightPlans.Clear();
                var next = new GreenKnightPlan
                {
                    Anchor = boss,
                    Axis = target != null && !fightsAHealer ? GreenKnightAxis(boss, PosOf(target)) : plan?.Axis ?? new Vector2(1, 0),
                };
                GreenKnightPlans[gk] = next;
                return next;
            }
        }

        /// <summary>A group's spot (<paramref name="distance"/> along the plan's axis; negative: the other way), on the navmesh
        /// and in his sight where the server has a mesh for the place (<see cref="GreenKnightPickSpot"/>), laid out once a plan.</summary>
        private static Vector3 GreenKnightSpot(GameNPC gk, GreenKnightPlan plan, int distance)
        {
            lock (GreenKnightPlans)
                if (plan.Spots.TryGetValue(distance, out Vector3 known)) return known;
            Vector3[] candidates = GreenKnightSpotCandidates(plan.Anchor, plan.Axis, distance);
            var nav = PathfindingProvider.Instance;
            Vector3 eye = plan.Anchor + new Vector3(0, 0, 60);
            Vector3 spot = GreenKnightPickSpot(candidates, c =>
            {
                Zone zone = gk.CurrentRegion?.GetZone((int)c.X, (int)c.Y);
                return zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone) ? c : nav.GetClosestPoint(zone, c, 128, 128, 512, nav.DefaultFilters);
            }, floor =>
            {
                Zone zone = gk.CurrentRegion?.GetZone((int)floor.X, (int)floor.Y);
                return zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone) ||
                       nav.HasLineOfSight(zone, floor + new Vector3(0, 0, 60), eye, nav.DefaultFilters);
            });
            lock (GreenKnightPlans) plan.Spots[distance] = spot;
            return spot;
        }

        /// <summary>The Green Knight whose fight a raider's tactic turn saw in the last 5 s within 4,000 of
        /// <paramref name="near"/>, or null (the healers' look-up, without searching the region on every heal).</summary>
        private static GameNPC ActiveGreenKnight(GameLiving near, long now)
        {
            lock (GreenKnightActive)
                foreach (var (gk, tick) in GreenKnightActive)
                    if (now - tick < 5_000 && gk.IsAlive && gk.InCombat && gk.CurrentRegionID == near.CurrentRegionID &&
                        gk.IsWithinRadius(near, GreenKnightGrowRadius))
                        return gk;
            return null;
        }

        private static void NoteGreenKnight(GameNPC gk, long now)
        {
            lock (GreenKnightActive)
            {
                if (GreenKnightActive.Count > 8) GreenKnightActive.Clear();
                GreenKnightActive[gk] = now;
            }
        }

        /// <summary>A tree of his that hits <paramref name="living"/>.</summary>
        private static bool TreeHits(GameLiving living)
        {
            foreach (GameLiving a in living.attackComponent.AttackerTracker.Attackers)
                if (a is GKTrees && a.IsAlive && a.TargetObject == living) return true;
            return false;
        }

        /// <summary>A healer that heals from range: the raid rules' shared test (<see cref="IsRangedHealerClass"/>; the Paladin,
        /// Friar and Warden fight in melee).</summary>
        private static bool GreenKnightRangedHealer(int classId) => IsRangedHealerClass(classId);

        /// <summary>
        /// The raider a healer heals first in his fight (SignalPatient asks): his target, when the healer is one of the
        /// <see cref="GreenKnightTankHealerCount"/> raid healers nearest it (chosen once a second); null otherwise.
        /// </summary>
        public static GameLiving GreenKnightTankPatient(GameBot healer, long now)
        {
            if (healer == null || !healer.IsAutonomousWorldBot || ActiveGreenKnight(healer, now) is not GameNPC gk) return null;
            if (gk.TargetObject is not GameLiving his || !his.IsAlive || his.Realm != healer.Realm || !healer.IsWithinRadius(his, 2_000))
                return null;
            HashSet<long> team;
            lock (GreenKnightTankTeams)
            {
                if (!GreenKnightTankTeams.TryGetValue(gk, out var hit) || hit.Target != his || now - hit.Tick >= 1_000)
                {
                    hit = (now, his, GreenKnightTankHealers(his.GetNPCsInRadius(2_000).OfType<GameBot>()
                        .Where(b => b.IsAlive && b != his && b.Realm == his.Realm && b.IsAutonomousWorldBot &&
                                    GreenKnightRangedHealer(b.CharacterClass?.ID ?? 0))
                        .Select(b => ((long)b.ObjectID, (double)b.GetDistanceTo(his))), GreenKnightTankHealerCount));
                    if (GreenKnightTankTeams.Count > 8) GreenKnightTankTeams.Clear();
                    GreenKnightTankTeams[gk] = hit;
                }
                team = hit.Healers;
            }
            return team.Contains(healer.ObjectID) ? his : null;
        }

        /// <summary>
        /// BotBrain.CheckHeals asks: true when <paramref name="patient"/> should wait for this heal (one of his trees hits it,
        /// it is at or over <see cref="GreenKnightTreedHealBelow"/>% and he does not fight it: <see cref="GreenKnightHoldsHeal"/>).
        /// </summary>
        public static bool GreenKnightHoldsHeal(GameBot healer, GameLiving patient)
        {
            if (healer == null || patient == null || !healer.IsAutonomousWorldBot) return false;
            long now = GameLoop.GameLoopTime;
            // His own target is healed first whatever hits it (GreenKnightTankPatient).
            if (ActiveGreenKnight(healer, now) is not GameNPC gk || gk.TargetObject == patient) return false;
            return GreenKnightHoldsHeal(patient.HealthPercent, TreeHits(patient));
        }

        /// <summary>The most hurt raid member under 85% in reach that no tree hits (or under the hold), for a held heal; or null.</summary>
        public static GameLiving GreenKnightOtherPatient(GameBot healer, GameLiving skip, Func<GameLiving, bool> inReach)
        {
            if (healer == null) return null;
            GameLiving best = null;
            IEnumerable<GameLiving> members = AutonomousRealmRaid.HasSharedSupport(healer)
                ? AutonomousRealmRaid.SupportMembers(healer)
                : healer.Group?.GetMembersInTheGroup() ?? Enumerable.Empty<GameLiving>();
            foreach (GameLiving m in members)
            {
                if (m == null || m == skip || !m.IsAlive || m.CurrentRegionID != healer.CurrentRegionID || m.HealthPercent >= 85 ||
                    !healer.IsWithinRadius(m, 2_000) || inReach != null && !inReach(m)) continue;
                if (GreenKnightHoldsHeal(m.HealthPercent, TreeHits(m))) continue;
                if (best == null || m.HealthPercent < best.HealthPercent) best = m;
            }
            return best;
        }

        private static StandardMobBrain BrainOf(GameNPC tree) => tree.Brain as StandardMobBrain;

        /// <summary>A fighter (tank, melee or a pet) a tree hits, and its threat on it (0 when it hits anyone else).</summary>
        private static double HolderThreat(GameNPC tree)
        {
            if (tree.TargetObject is not GameLiving v || BrainOf(tree) is not StandardMobBrain brain) return 0;
            return RoleOf(v) is "tank" or "melee" or "pet" ? brain.GetBaseAggroAmount(v) : 0;
        }

        private static bool IsSoftRole(GameLiving v) => RoleOf(v) is "caster" or "healer" or "archer";

        /// <summary>One EPIC_GK_TREES line per Green Knight every 30 s while his trees live.</summary>
        private static void LogGreenKnightTrees(GameNPC gk, GameNPC[] trees, long now)
        {
            lock (LastGreenKnightTreeLog)
            {
                if (LastGreenKnightTreeLog.TryGetValue(gk, out long last) && now - last < 30_000) return;
                if (LastGreenKnightTreeLog.Count > 16) LastGreenKnightTreeLog.Clear();
                LastGreenKnightTreeLog[gk] = now;
            }
            if (!Log.IsInfoEnabled) return;
            int[] by = new int[4];
            int held = 0;
            foreach (GameNPC tree in trees)
            {
                by[TreeVictimRank(tree.TargetObject as GameLiving, now)]++;
                if (HolderThreat(tree) >= GreenKnightTagThreat) held++;
            }
            Log.Info($"EPIC_GK_TREES boss=\"{gk.Name}\" health={gk.HealthPercent} walking={(GreenKnightWalks(gk) ? "yes" : "no")} alive={trees.Length} " +
                     $"held={held} onAreaCasters={by[TreeOnAreaCaster]} onHealers={by[TreeOnHealer]} onCasters={by[TreeOnCaster]} onOthers={by[TreeOnOther]}");
        }

        /// <summary>A guard: the first <see cref="GreenKnightGuardsPerParty"/> melee (by object id) of a party with a living
        /// area caster near him.</summary>
        private static bool IsGreenKnightGuard(GameLiving raider, GameNPC gk, long now)
        {
            if (raider.Group is not Group g) return false;
            GameLiving[] members = g.GetMembersInTheGroup().Where(m => m != null && m.IsAlive).ToArray();
            if (!members.Any(m => m is GameBot b && RoleOf((GameLiving)b) == "caster" && AreaRadiusOf(b, now) > 0 &&
                                  gk.IsWithinRadius(b, GreenKnightGrowRadius)))
                return false;
            return members.Where(m => RoleOf(m) == "melee" && m != gk.TargetObject).OrderBy(m => m.ObjectID)
                .Take(GreenKnightGuardsPerParty).Contains(raider);
        }

        /// <summary>The trees as <paramref name="who"/> (a raider or its pet) sees them, for <see cref="GreenKnightTagTree"/>.</summary>
        private static TagView[] TagViews(GameNPC[] trees, GameLiving who) => trees.Select(t =>
        {
            GameLiving v = t.TargetObject as GameLiving;
            return new TagView(t.ObjectID, who.GetDistanceTo(t), v == who, BrainOf(t)?.GetBaseAggroAmount(who) ?? 0,
                HolderThreat(t), v != null && v != who && IsSoftRole(v), t.HealthPercent);
        }).ToArray();

        /// <summary>To <paramref name="slot"/> when off it (Reposition), else null: once there a raider gets no new advice
        /// from its spot.</summary>
        private static Advice? ToSlot(RuleContext c, Vector3 slot, string reason) =>
            Vector2.Distance(new(c.At.X, c.At.Y), new(slot.X, slot.Y)) > GreenKnightSpotSlack
                ? new(BotAction.Reposition, c.Target, slot, reason)
                : null;

        private static Advice OnHim(RuleContext c, GameNPC gk, string reason) =>
            c.Target == gk ? new(BotAction.None, gk, null, null) : new(BotAction.Retarget, gk, null, reason);

        private static Advice? GreenKnightTagAndBurn(RuleContext c)
        {
            GameNPC gk = c.Target is OFGreenKnight ? c.Target : c.Find(nameof(OFGreenKnight));
            if (gk == null || !gk.InCombat || !Present(gk)) return null;
            NoteGreenKnight(gk, c.Now);
            GameLiving raider = c.Raider;
            // The one he fights holds him (and a healer he turns to keeps healing where it stands).
            if (gk.TargetObject == raider) return null;
            GameNPC[] trees = c.All(nameof(GKTrees));
            if (trees.Length > 0) LogGreenKnightTrees(gk, trees, c.Now);
            bool walking = GreenKnightWalks(gk);
            GreenKnightPlan plan = GreenKnightPlanFor(gk);
            var cls = (eCharacterClass)Math.Max(0, c.ClassId);
            if (GreenKnightRangedHealer(c.ClassId))
            {
                // Healers: together at their spot, each on its slot; nothing new once there (they heal as usual).
                Vector3 slot = GreenKnightSlot(GreenKnightSpot(gk, plan, GreenKnightHealDistance), raider.ObjectID);
                return ToSlot(c, slot, $"gk-heal-spot: healers {GreenKnightHealDistance} from him, away from his tank");
            }
            if (c.CanShootFlyers)
            {
                GameBot bot = raider as GameBot;
                // The owner of a lasting pet: out of the trees' reach, its pet in (no tree grows at a pet).
                if (cls != eCharacterClass.Theurgist && bot?.ControlledBrain?.Body is GameNPC pet && pet.IsAlive)
                {
                    GameNPC petOn = gk;
                    int pick = GreenKnightTagTree(TagViews(trees, pet), pet.ObjectID, true, AreaCastersNear(gk, c.Now));
                    if (pick >= 0) petOn = trees[pick];
                    Vector3? outAt = Vector2.Distance(new(c.At.X, c.At.Y), new(gk.X, gk.Y)) >= GreenKnightOutHold
                        ? null : GreenKnightSpot(gk, plan, -GreenKnightOutDistance);
                    return new(BotAction.StandOff, gk, outAt,
                        $"gk-pet: owner {GreenKnightOutDistance} out (no tree grows at a pet), its pet on \"{petOn.Name}\"", PetOn: petOn);
                }
                if (cls == eCharacterClass.Theurgist)
                {
                    if (ToSlot(c, GreenKnightSlot(GreenKnightSpot(gk, plan, GreenKnightTheurgistDistance), raider.ObjectID),
                            $"gk-summon-spot: Theurgists {GreenKnightTheurgistDistance} from him") is Advice go) return go;
                    int aim = GreenKnightSummonTree(trees.Select(t => ((double)raider.GetDistanceTo(t),
                        t.TargetObject is GameLiving v && (v == raider || IsSoftRole(v)))).ToArray());
                    if (aim >= 0)
                        return trees[aim] == c.Target ? new(BotAction.None, trees[aim], null, null)
                            : new(BotAction.Retarget, trees[aim], null, $"gk-summon: an elemental on \"{trees[aim].Name}\" (on a soft raider)");
                    return walking && !raider.IsWithinRadius(gk, GreenKnightSummonReach)
                        ? new(BotAction.StandOff, gk, null, "gk-hold: he walks to heal; he comes back to his target")
                        : OnHim(c, gk, "gk-summon: no tree on a soft raider; at him");
                }
                if (bot != null && AreaRadiusOf(bot, c.Now) is > 0 and var radius)
                {
                    if (ToSlot(c, GreenKnightSlot(GreenKnightSpot(gk, plan, GreenKnightBurnDistance), raider.ObjectID),
                            $"gk-burn-spot: area casters {GreenKnightBurnDistance} from him") is Advice go) return go;
                    BurnView[] burn = trees.Select(t => new BurnView(t.ObjectID, t.X, t.Y, raider.GetDistanceTo(t),
                        BrainOf(t)?.GetBaseAggroAmount(raider) ?? 0, HolderThreat(t))).ToArray();
                    int aim = GreenKnightBurnTree(burn, GreenKnightBurnThreat, Math.Max(0, radius - ClumpEdgeMargin));
                    if (aim >= 0)
                        return trees[aim] == c.Target ? new(BotAction.None, trees[aim], null, null)
                            : new(BotAction.Retarget, trees[aim], null, $"gk-burn: area spell on the tagged trees at \"{trees[aim].Name}\"");
                    return walking && !raider.IsWithinRadius(gk, GreenKnightSpellReach)
                        ? new(BotAction.StandOff, gk, null, "gk-hold: he walks to heal; he comes back to his target")
                        : OnHim(c, gk, "gk-burn: no tagged clump; on him");
                }
                // Casters and archers without an area spell: in reach, on the tagged trees they won't pull, else him.
                if (ToSlot(c, GreenKnightSlot(GreenKnightSpot(gk, plan, GreenKnightShootDistance), raider.ObjectID),
                        $"gk-shot-spot: casters and archers {GreenKnightShootDistance} from him") is Advice walk) return walk;
                int reach = RoleOf(raider) == "archer" ? 1_700 : GreenKnightSpellReach;
                int shot = GreenKnightShotTree(trees.Select(t => new ShotView(t.ObjectID, raider.GetDistanceTo(t),
                    BrainOf(t)?.GetBaseAggroAmount(raider) ?? 0, HolderThreat(t), t.HealthPercent)).ToArray(), GreenKnightShotThreat, reach);
                if (shot >= 0)
                    return trees[shot] == c.Target ? new(BotAction.None, trees[shot], null, null)
                        : new(BotAction.Retarget, trees[shot], null, $"gk-shot: the tagged \"{trees[shot].Name}\"");
                return walking && !raider.IsWithinRadius(gk, reach)
                    ? new(BotAction.StandOff, gk, null, "gk-hold: he walks to heal; he comes back to his target")
                    : OnHim(c, gk, "gk-shot: no tagged tree I won't pull; on him");
            }
            // The melee (the melee hybrids too): tag.
            bool guard = IsGreenKnightGuard(raider, gk, c.Now);
            bool areaLeft = AreaCastersNear(gk, c.Now);
            TagView[] views = TagViews(trees, raider);
            int take = GreenKnightTagTree(views, raider.ObjectID, guard, areaLeft);
            if (take >= 0)
            {
                GameNPC tree = trees[take];
                if (tree == c.Target) return new(BotAction.None, tree, null, null);
                string why = views[take].OnSoft ? "gk-peel: off a healer, caster or archer"
                    : views[take].OnMe && views[take].MyThreat > 0 && views[take].MyThreat < GreenKnightTagThreat ? "gk-tag: finishing the tag"
                    : views[take].HolderThreat < GreenKnightTagThreat ? "gk-tag: no fighter holds it"
                    : guard ? "gk-guard: by the area casters"
                    : areaLeft ? "gk-finish: a burned tree on me" : "gk-finish: no area caster left; killing my own";
                return new(BotAction.Retarget, tree, null,
                    $"{why} (\"{tree.Name}\", {trees.Length} alive, {views.Count(v => v.HolderThreat >= GreenKnightTagThreat)} held)");
            }
            if (guard)
            {
                Vector3 slot = GreenKnightSlot(GreenKnightSpot(gk, plan, GreenKnightBurnDistance), raider.ObjectID + 3);
                bool there = Vector2.Distance(new(c.At.X, c.At.Y), new(slot.X, slot.Y)) <= GreenKnightSpotSlack;
                return new(BotAction.StandOff, gk, there ? null : slot, "gk-guard: with the area casters");
            }
            if (walking) return new(BotAction.StandOff, gk, null, "gk-hold: he walks to heal; he comes back to his target");
            return OnHim(c, gk, "gk-tag: every tree near me is held; on him");
        }

        private static readonly Dictionary<GameNPC, (long Tick, bool Any)> GreenKnightAreaCasters = new();

        /// <summary>A living area caster of the raid within 4,000 of him (looked up once a second).</summary>
        private static bool AreaCastersNear(GameNPC gk, long now)
        {
            lock (GreenKnightAreaCasters)
                if (GreenKnightAreaCasters.TryGetValue(gk, out var hit) && now - hit.Tick < 1_000) return hit.Any;
            bool any = gk.GetNPCsInRadius(GreenKnightGrowRadius).OfType<GameBot>().Any(b => b.IsAlive && b.IsAutonomousWorldBot &&
                RoleOf((GameLiving)b) == "caster" && AreaRadiusOf(b, now) > 0);
            lock (GreenKnightAreaCasters)
            {
                if (GreenKnightAreaCasters.Count > 8) GreenKnightAreaCasters.Clear();
                GreenKnightAreaCasters[gk] = (now, any);
            }
            return any;
        }

        /// <summary>Forgets every Green Knight plan (tests).</summary>
        public static void ResetGreenKnightForTests()
        {
            lock (GreenKnightPlans) GreenKnightPlans.Clear();
            lock (GreenKnightActive) GreenKnightActive.Clear();
            lock (GreenKnightTankTeams) GreenKnightTankTeams.Clear();
            lock (GreenKnightAreaCasters) GreenKnightAreaCasters.Clear();
        }
    }
}
