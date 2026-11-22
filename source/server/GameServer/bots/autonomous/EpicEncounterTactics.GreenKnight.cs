using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// The Green Knight's trees (Living Server 1.2.1, #412; tests only). His script (OFGreenKnight.cs, unchanged) grows a
    /// rotting downy felwood (800 health, level 47-49, aggro range 800, speed 225, no tether) at every living raider within
    /// 4,000 of him every 25-35 s while he has aggro, up to 70 a wave (EncounterFloodGuard "OFGreenKnight.Trees", the
    /// dragon-albion period size), and the trees stay until they are killed. So each raider brings about 800 health of trees
    /// every 30 s (about 27 a second), up to 70 raiders. Owner's PC, build ccba364, 2026-11-16 (sweep 3, 19:53-20:23): two
    /// wipes in 29 minutes; that log has no tree count, so the cause is read from his script and the server's rules and
    /// measured in a model (UT_RaidSim121SummonersGreenKnight).
    /// <para>Round 2 (this file): a tree that hits a caster or a healer interrupts it (GameLiving.StartInterruptTimer: 94% a
    /// swing from a level 48 on a level 50, for the swing's length), so a wave silences every healer it lands on until the
    /// tree is killed or turns away; and a tree turns to whoever has the most threat on it (damage, and half of every heal on
    /// the raider it hits), so a caster's single-target shot or area spell pulls it onto the caster. Round 1's rule (#432:
    /// the melee outside his target's party kill the tree nearest them, their own first, while the casters and archers
    /// shoot the trees) left the healers' trees on them. Now:</para>
    /// <list type="bullet">
    /// <item>The casters, archers and healers stand together at one spot (<see cref="GreenKnightStackSpot"/>: 750 from him,
    /// on the side away from his target, clear of Might of the Forest's 500 around his target), so their trees grow there,
    /// one walk from the melee.</item>
    /// <item>Every melee raider but the one he fights peels (<see cref="GreenKnightPeelTree"/>): it hits a tree that is on an
    /// area caster, then one on a healer, then one on another caster or archer (the raid's melee spread over them by id), so
    /// the tree turns to it and follows it back to him; with none left on them it kills the trees on itself, the most hurt
    /// first, then goes back to him.</item>
    /// <item>Casters and archers without an area spell stay on him (a shot at a tree pulls it onto the shooter); area casters
    /// keep the catalog's area-on-clump rule (the peeled trees bunch on the melee around him).</item>
    /// </list>
    /// Log: EPIC_GK_TREES (every 30 s: alive and whom they hit), EPIC_TACTIC_BOT lines with "gk-peel", "gk-stack" and
    /// "gk-shots". Autonomous gamebots only, as every tactic.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>#432's reach: a melee raider took a tree within this of itself (kept for the model's before/after).</summary>
        public const int GreenKnightTreeReach = 800;
        /// <summary>A melee raider peels a tree within this of itself (the stack stands 750 from him).</summary>
        public const int GreenKnightPeelReach = 1_200;
        /// <summary>The casters, archers and healers stand this far from him.</summary>
        public const int GreenKnightStackDistance = 750;
        /// <summary>A raider within this of the stack spot is at it.</summary>
        public const int GreenKnightStackSlack = 150;
        /// <summary>Might of the Forest's radius around his target (OFGreenKnight.GreenKnightHeatDD: 500).</summary>
        public const int GreenKnightHeatRadius = 500;

        /// <summary>Whom a tree hits, as the peel ranks it: an area caster, a healer, another caster or archer, anyone else.</summary>
        public const int TreeOnAreaCaster = 0, TreeOnHealer = 1, TreeOnCaster = 2, TreeOnOther = 3;

        [DungeonRules]
        private static IEnumerable<DungeonRule> GreenKnightRules() =>
        [
            new("GreenKnightTrees", [nameof(OFGreenKnight), nameof(GKTrees)], GreenKnightTreesPeel)
            {
                Summary = "A tree grows at every raider every 25-35 s, stays, and silences a caster or healer it hits: the casters, " +
                          $"archers and healers stand together {GreenKnightStackDistance} from him away from his target, every melee " +
                          "raider but his target peels the trees off the area casters, then the healers, then the other casters " +
                          "(they follow it to him) and then kills its own, and only area casters hit the trees (a single shot pulls one).",
            },
        ];

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

        private static readonly Dictionary<GameNPC, (Vector3 Spot, long Tick)> GreenKnightStacks = new();
        private static readonly Dictionary<GameNPC, long> LastGreenKnightTreeLog = new();

        private static Vector3 GreenKnightStackFor(GameNPC gk, long now)
        {
            Vector3 boss = new(gk.X, gk.Y, gk.Z);
            Vector3 target = gk.TargetObject is GameLiving t ? new(t.X, t.Y, t.Z) : boss;
            lock (GreenKnightStacks)
            {
                Vector3? previous = GreenKnightStacks.TryGetValue(gk, out var s) && now - s.Tick < 60_000 ? s.Spot : null;
                Vector3 spot = GreenKnightStackSpot(boss, target, previous);
                if (GreenKnightStacks.Count > 16) GreenKnightStacks.Clear();
                GreenKnightStacks[gk] = (spot, now);
                return spot;
            }
        }

        /// <summary>Whom a tree hits, ranked for the peel.</summary>
        private static int TreeVictimRank(GameLiving victim, long now)
        {
            if (victim == null) return TreeOnOther;
            string role = RoleOf(victim);
            if (role == "caster" && victim is GameBot bot && AreaRadiusOf(bot, now) > 0) return TreeOnAreaCaster;
            if (role == "healer") return TreeOnHealer;
            return role is "caster" or "archer" ? TreeOnCaster : TreeOnOther;
        }

        /// <summary>One EPIC_GK_TREES line per Green Knight every 30 s while his trees live: how many, and whom they hit.</summary>
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
            foreach (GameNPC tree in trees) by[TreeVictimRank(tree.TargetObject as GameLiving, now)]++;
            Log.Info($"EPIC_GK_TREES boss=\"{gk.Name}\" health={gk.HealthPercent} alive={trees.Length} onAreaCasters={by[TreeOnAreaCaster]} " +
                     $"onHealers={by[TreeOnHealer]} onCasters={by[TreeOnCaster]} onOthers={by[TreeOnOther]}");
        }

        private static Advice? GreenKnightTreesPeel(RuleContext c)
        {
            GameNPC gk = c.Target is OFGreenKnight ? c.Target : c.Find(nameof(OFGreenKnight));
            if (gk == null || !gk.InCombat || !Present(gk)) return null;
            GameLiving raider = c.Raider;
            // The one he fights holds him (and a healer he turns to keeps healing where it stands).
            if (gk.TargetObject == raider) return null;
            GameNPC[] trees = c.All(nameof(GKTrees));
            if (trees.Length > 0) LogGreenKnightTrees(gk, trees, c.Now);
            if (c.AtRange)
            {
                Vector3 spot = GreenKnightStackFor(gk, c.Now);
                if (Vector2.Distance(new(c.At.X, c.At.Y), new(spot.X, spot.Y)) > GreenKnightStackSlack)
                    return new(BotAction.Reposition, c.Target, spot,
                        $"gk-stack: casters and healers together {GreenKnightStackDistance} from him, away from his target");
                bool healer = c.ClassId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)c.ClassId);
                if (healer || trees.Length == 0) return null;
                // Area casters: the catalog's clump rule. The others: a shot at a tree pulls it onto the shooter.
                if (raider is GameBot shooter && AreaRadiusOf(shooter, c.Now) > 0) return null;
                return c.Target == gk ? new(BotAction.None, gk, null, null)
                    : new(BotAction.Retarget, gk, null, "gk-shots: a shot at a tree pulls it onto the shooter; on him");
            }
            if (trees.Length == 0) return c.Target == gk ? null : new(BotAction.Retarget, gk, null, "gk-peel: no tree left; on him");
            TreeView[] views = trees.Select(t => new TreeView(t.ObjectID, (float)raider.GetDistanceTo(t), t.TargetObject == raider,
                t.HealthPercent, TreeVictimRank(t.TargetObject as GameLiving, c.Now))).ToArray();
            int pick = GreenKnightPeelTree(views, raider.ObjectID);
            if (pick < 0)
                return c.Target == gk ? null : new(BotAction.Retarget, gk, null, "gk-peel: no tree on a caster, a healer or me; on him");
            GameNPC tree = trees[pick];
            if (tree == c.Target) return new(BotAction.None, tree, null, null);
            string on = views[pick].OnMe ? "on me" : views[pick].VictimRank switch
            {
                TreeOnAreaCaster => "on an area caster",
                TreeOnHealer => "on a healer",
                _ => "on a caster or archer",
            };
            return new(BotAction.Retarget, tree, null,
                $"gk-peel: \"{tree.Name}\" {on} ({trees.Length} alive, {views.Count(v => v.VictimRank <= TreeOnCaster)} on casters and healers)");
        }
    }
}
