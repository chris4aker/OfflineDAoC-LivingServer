using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// The Green Knight's trees (Living Server 1.2.1, #412; tests only). His script (OFGreenKnight.cs, unchanged) grows a
    /// rotting downy felwood (800 health, level 47-49) at every living raider within 4,000 of him every 25-35 s, up to 70 a
    /// wave (EncounterFloodGuard), and the trees stay until they are killed. So each raider brings about 800 health of trees
    /// every 30 s, about 27 a second, whatever the raid's size: a raid whose tree killers deal less than that per raider
    /// collects trees until they bury it. The catalog gave the trees to the casters and archers only (about a third of an
    /// Albion raid) while every melee raider stayed on him with a tree of its own beating on it. Owner's PC, build ccba364,
    /// 2026-11-16 (sweep 3, 19:53-20:23): two wipes in 29 minutes. That log has no tree count (no EPIC_RANGED_ADDS line was
    /// quoted), so the cause is an inference from his script, measured in a model (UT_RaidSim121SummonersGreenKnight).
    /// <para>Here: his target's party keeps him (he has no enrage and his own heal is 400 at each 10%, so time on the trees
    /// costs the raid nothing but time), the healers heal, the casters and archers keep the catalog's ranged-add rule, and
    /// every other melee raider kills the tree nearest it within <see cref="GreenKnightTreeReach"/> (its own first) before it
    /// goes back to him. Log: the catalog's EPIC_RANGED_ADDS boss="Green Knight" alive= (every 30 s) and EPIC_TACTIC_BOT
    /// lines with "gk-trees". Autonomous gamebots only, as every tactic.</para>
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>A melee raider takes a tree within this of itself (a tree grows at a raider's feet and attacks within 800).</summary>
        public const int GreenKnightTreeReach = 800;

        [DungeonRules]
        private static IEnumerable<DungeonRule> GreenKnightRules() =>
        [
            new("GreenKnightTrees", [nameof(OFGreenKnight)], GreenKnightTreesFirst)
            {
                Summary = "A tree grows at every raider every 25-35 s and stays: his target's party keeps him, the casters and archers " +
                          $"take the trees (catalog), and every other melee raider kills the tree nearest it within {GreenKnightTreeReach} " +
                          "(its own first) before it goes back to him.",
            },
        ];

        /// <summary>One tree as the melee choice sees it.</summary>
        public readonly record struct TreeView(long Id, float Distance, bool OnMe, int HealthPercent);

        /// <summary>
        /// Which tree a melee raider outside his target's party kills (pure; an index into <paramref name="trees"/>, -1: none,
        /// stay on him): one attacking the raider, else the nearest within <see cref="GreenKnightTreeReach"/>; ties the most hurt,
        /// then the lowest id.
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

        private static Advice? GreenKnightTreesFirst(RuleContext c)
        {
            if (c.AtRange || c.Holds || !c.Target.InCombat) return null;
            GameNPC[] trees = c.All(nameof(GKTrees));
            if (trees.Length == 0) return null;
            GameLiving raider = c.Raider;
            TreeView[] views = trees.Select(t => new TreeView(t.ObjectID, (float)raider.GetDistanceTo(t), t.TargetObject == raider,
                t.HealthPercent)).ToArray();
            int pick = GreenKnightMeleeTree(views);
            if (pick < 0) return null;
            GameNPC tree = trees[pick];
            return new(BotAction.Retarget, tree, null,
                $"gk-trees: \"{tree.Name}\" {(views[pick].OnMe ? "is on me" : $"{(int)views[pick].Distance} away")} " +
                $"({trees.Length} alive); his target's party keeps him");
        }
    }
}
