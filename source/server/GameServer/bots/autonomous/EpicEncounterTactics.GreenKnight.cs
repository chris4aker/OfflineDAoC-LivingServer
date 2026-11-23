using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// The Green Knight's trees (Living Server 1.2.2, #412; tests only). His script (OFGreenKnight.cs, unchanged) grows a
    /// rotting downy felwood (800 health, level 47-49, STR 150 QUI 80: a swing every 2.88 s; aggro range 800, speed 225, no
    /// tether) at every living raider within 4,000 of him every 25-35 s while he has aggro, up to 70 a wave
    /// (EncounterFloodGuard "OFGreenKnight.Trees"), and the trees stay until they are killed. A tree takes one raider within
    /// 800 of it (CheckNpcAggro adds the first it finds), hits for 22-36 a second on mail and plate and 53-78 on cloth
    /// (UT_RaidRealStats122), silences a caster or healer it hits (94% a swing), and turns to the highest threat: damage, and
    /// half of every heal on the raider it hits (HealSpellHandler), counted less past 250 from the tree (a third at 1,500:
    /// StandardMobBrain). With those numbers 1.2.1's stack and peel lost every modelled attempt at every size 40-104.
    /// <para>1.2.2, tag and burn (UT_RaidSim122GreenKnight: the 1.2 rules and round 2 lose at every size to 104; this wins at
    /// 40 when the raid is buffed, and at 72-80 unbuffed):</para>
    /// <list type="bullet">
    /// <item>Casters and archers without an area spell stand <see cref="GreenKnightOutDistance"/> from him, opposite the
    /// healers (past the 4,000 his trees grow in): a cloth raider brings a tree it can't fight and is silenced by it
    /// ("gk-out").</item>
    /// <item>Healers stand together <see cref="GreenKnightHealDistance"/> from him away from his target (clear of Might of the
    /// Forest's 500 around it), heal his target first under <see cref="GreenKnightTankHealBelow"/>% (he hits for 1,000-1,350)
    /// and leave a raider a tree hits until it is under <see cref="GreenKnightTreedHealBelow"/>%: half a heal is threat on that
    /// tree and pulls it onto the healer (<see cref="GreenKnightTankPatient"/>, <see cref="GreenKnightHoldsHeal"/>;
    /// "gk-heal-spot").</item>
    /// <item>Area casters stand <see cref="GreenKnightBurnDistance"/> from him away from his target with
    /// <see cref="GreenKnightGuardsPerParty"/> melee of their party (guards), and burn: an area spell on the biggest clump of
    /// trees a fighter holds (its threat at least <see cref="GreenKnightTagThreat"/>) that the spell would not pull
    /// (<see cref="GreenKnightBurnTree"/>; "gk-burn"); else on him.</item>
    /// <item>Every melee raider but his target tags (<see cref="GreenKnightTagTree"/>): it finishes the tag on a tree that hits
    /// it, then takes a tree off a healer or caster within <see cref="GreenKnightPeelSoftReach"/>, then hits each tree near
    /// it that no fighter holds yet until its own threat is <see cref="GreenKnightTagThreat"/>, and goes back to him: the area
    /// casters burn the tagged trees, and it finishes those on itself once they are under half ("gk-tag", "gk-peel",
    /// "gk-finish"). Guards also kill the trees around the area casters; with no area caster left the melee kill their own
    /// trees.</item>
    /// </list>
    /// Log: EPIC_GK_TREES (every 30 s: alive, held, whom they hit), EPIC_TACTIC_BOT lines with the gk-* reasons. Autonomous
    /// gamebots only, as every tactic. The 1.2.1 round 2 helpers (<see cref="GreenKnightPeelTree"/>,
    /// <see cref="GreenKnightStackSpot"/>, <see cref="GreenKnightMeleeTree"/>) stay for the models' before/after.
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

        // ---- 1.2.2 tag and burn ---------------------------------------------------------------------------------------

        /// <summary>His script grows a tree at every raider within this of him (OFGreenKnight.SpawnTrees).</summary>
        public const int GreenKnightGrowRadius = 4_000;
        /// <summary>The casters and archers without an area spell stand this far from him (past <see cref="GreenKnightGrowRadius"/>,
        /// with room for his walks), and stay where they are once this far (<see cref="GreenKnightOutHold"/>).</summary>
        public const int GreenKnightOutDistance = 4_600, GreenKnightOutHold = 4_300;
        /// <summary>The healers stand this far from him, away from his target.</summary>
        public const int GreenKnightHealDistance = 850;
        /// <summary>The area casters and their guards stand this far from him, away from his target.</summary>
        public const int GreenKnightBurnDistance = 500;
        /// <summary>A raider within this of its spot is at it.</summary>
        public const int GreenKnightSpotSlack = 150;
        /// <summary>A melee raider has tagged a tree once its threat on it is this (three seconds of a melee raider's hits): an
        /// area caster's spells at range then add less than that.</summary>
        public const int GreenKnightTagThreat = 240;
        /// <summary>A melee raider tags the trees within this of itself.</summary>
        public const int GreenKnightTagReach = 300;
        /// <summary>A melee raider takes a tree off a healer, caster or archer within this of itself.</summary>
        public const int GreenKnightPeelSoftReach = 500;
        /// <summary>A guard kills the trees within this of itself (around the area casters).</summary>
        public const int GreenKnightGuardReach = 300;
        /// <summary>A melee raider finishes a tree on itself at or under this (percent): the area casters burned it (their
        /// spells stop before they would pull it, about 40% short of its death).</summary>
        public const int GreenKnightOwnBelow = 50;
        /// <summary>Melee of an area caster's party who stand with it (the first by object id).</summary>
        public const int GreenKnightGuardsPerParty = 2;
        /// <summary>The threat an area caster's next spell adds (Ebullient Blast 158 before resists, rounded up).</summary>
        public const int GreenKnightBurnThreat = 160;
        /// <summary>An area caster burns trees within this of itself (its spells' range).</summary>
        public const int GreenKnightSpellReach = 1_500;
        /// <summary>Healers heal his target first while it is under this (percent).</summary>
        public const int GreenKnightTankHealBelow = 98;
        /// <summary>A raider a tree hits is left unhealed until it is under this (percent).</summary>
        public const int GreenKnightTreedHealBelow = 50;

        /// <summary>
        /// A threat as a tree counts it (pure; StandardMobBrain.CleanUpAggroListAndGetHighestModifiedThreat): in full within
        /// 250 of the tree, past that less, a third at 1,500.
        /// </summary>
        public static double GreenKnightEffectiveThreat(double raw, double distance) =>
            distance <= 250 ? raw : raw * Math.Exp(Math.Log(1 / 3.0) / (1500 - 250) * (distance - 250));

        /// <summary>One tree as a melee raider of tag and burn sees it. <paramref name="HolderThreat"/>: the threat on it of
        /// the fighter it hits (0 when it hits a healer, caster or archer); <paramref name="OnSoft"/>: it hits a healer, caster
        /// or archer other than this raider.</summary>
        public readonly record struct TagView(long Id, double Distance, bool OnMe, double MyThreat, double HolderThreat, bool OnSoft,
            int HealthPercent);

        /// <summary>
        /// Which tree a melee raider takes (pure; an index into <paramref name="trees"/>, -1: none, on him). In order: the first
        /// tree that hits it within <see cref="GreenKnightTagReach"/> with its threat on it above 0 and under
        /// <see cref="GreenKnightTagThreat"/> (finish the tag); a tree on a healer, caster or archer within
        /// <see cref="GreenKnightPeelSoftReach"/>, the one at <paramref name="raiderId"/> modulo their count (the melee spread
        /// over them); the tree within <see cref="GreenKnightTagReach"/> that no fighter holds yet, one on itself first, then
        /// the nearest; then the most hurt tree on itself (a guard: or within <see cref="GreenKnightGuardReach"/>) at or under
        /// <see cref="GreenKnightOwnBelow"/>% (the area casters have burned it; a guard, or anyone with no area caster left:
        /// whatever its health).
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
        /// spell would not pull (its threat after <paramref name="spellThreat"/> more, as the tree counts it at its distance, still
        /// under the holder's), the one with the most of them within <paramref name="radius"/>; two at least.
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

        /// <summary>A heal on a raider a tree hits waits while it is at or over <see cref="GreenKnightTreedHealBelow"/>% (pure).</summary>
        public static bool GreenKnightHoldsHeal(int healthPercent, bool treeOnIt) => treeOnIt && healthPercent >= GreenKnightTreedHealBelow;

        /// <summary>
        /// A spot <paramref name="distance"/> from him away from his target (pure). A spot in use (<paramref name="previous"/>)
        /// is kept while it lies within 200 of that distance from him and clear of <see cref="GreenKnightHeatRadius"/> (and 50)
        /// around his target, so the healers do not run about when he turns to one of them for a moment.
        /// </summary>
        public static Vector3 GreenKnightAwaySpot(Vector3 boss, Vector3 target, float distance, Vector3? previous)
        {
            if (previous is Vector3 p)
            {
                float fromBoss = Vector2.Distance(new(p.X, p.Y), new(boss.X, boss.Y));
                float fromTarget = Vector2.Distance(new(p.X, p.Y), new(target.X, target.Y));
                if (Math.Abs(fromBoss - distance) <= 200 && fromTarget > GreenKnightHeatRadius + 50) return p;
            }
            Vector2 away = new(boss.X - target.X, boss.Y - target.Y);
            if (away.Length() < 1) away = new(1, 0);
            away = Vector2.Normalize(away) * distance;
            return new(boss.X + away.X, boss.Y + away.Y, boss.Z);
        }

        /// <summary>Where a caster or archer without an area spell stands (pure): <see cref="GreenKnightOutDistance"/> from him,
        /// opposite the healers' spot; null when it already stands <see cref="GreenKnightOutHold"/> or more from him.</summary>
        public static Vector3? GreenKnightOutSpot(Vector3 at, Vector3 boss, Vector3 healSpot)
        {
            if (Vector2.Distance(new(at.X, at.Y), new(boss.X, boss.Y)) >= GreenKnightOutHold) return null;
            Vector2 away = new(boss.X - healSpot.X, boss.Y - healSpot.Y);
            if (away.Length() < 1) away = new(-1, 0);
            away = Vector2.Normalize(away) * GreenKnightOutDistance;
            return new(boss.X + away.X, boss.Y + away.Y, at.Z);
        }

        [DungeonRules]
        private static IEnumerable<DungeonRule> GreenKnightRules() =>
        [
            new("GreenKnightTrees", [nameof(OFGreenKnight), nameof(GKTrees)], GreenKnightTagAndBurn)
            {
                Summary = "A tree grows at every raider within 4,000 every 25-35 s, stays, silences a caster or healer it hits and " +
                          $"turns to the most threat (half of each heal on its victim): casters and archers without an area spell stand " +
                          $"{GreenKnightOutDistance} from him, the healers together {GreenKnightHealDistance} away from his target (his " +
                          $"target first, a raider a tree hits only under {GreenKnightTreedHealBelow}%), the area casters " +
                          $"{GreenKnightBurnDistance} away with two melee of their party; the melee tag every tree ({GreenKnightTagThreat} " +
                          "threat), healers' first, and go back to him, and the area casters burn the tagged trees.",
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

        private static readonly Dictionary<GameNPC, long> LastGreenKnightTreeLog = new();

        /// <summary>Whom a tree hits, ranked for the peel.</summary>
        private static int TreeVictimRank(GameLiving victim, long now)
        {
            if (victim == null) return TreeOnOther;
            string role = RoleOf(victim);
            if (role == "caster" && victim is GameBot bot && AreaRadiusOf(bot, now) > 0) return TreeOnAreaCaster;
            if (role == "healer") return TreeOnHealer;
            return role is "caster" or "archer" ? TreeOnCaster : TreeOnOther;
        }

        private static readonly Dictionary<(GameNPC, int), (Vector3 Spot, long Tick)> GreenKnightSpots = new();
        private static readonly Dictionary<GameNPC, long> GreenKnightActive = new();

        /// <summary>His heal spot or burn spot (<paramref name="distance"/>), kept a minute per Green Knight.</summary>
        private static Vector3 GreenKnightSpotFor(GameNPC gk, int distance, long now)
        {
            Vector3 boss = new(gk.X, gk.Y, gk.Z);
            Vector3 target = gk.TargetObject is GameLiving t ? new(t.X, t.Y, t.Z) : boss;
            lock (GreenKnightSpots)
            {
                Vector3? previous = GreenKnightSpots.TryGetValue((gk, distance), out var s) && now - s.Tick < 60_000 ? s.Spot : null;
                Vector3 spot = GreenKnightAwaySpot(boss, target, distance, previous);
                if (GreenKnightSpots.Count > 32) GreenKnightSpots.Clear();
                GreenKnightSpots[(gk, distance)] = (spot, now);
                return spot;
            }
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

        /// <summary>
        /// The raider a healer heals first in his fight (SignalPatient asks): his target, while he fights a raider of the
        /// healer's realm and the healer is within 2,000 of it; null otherwise.
        /// </summary>
        public static GameLiving GreenKnightTankPatient(GameBot healer, long now)
        {
            if (healer == null || !healer.IsAutonomousWorldBot || ActiveGreenKnight(healer, now) is not GameNPC gk) return null;
            return gk.TargetObject is GameLiving his && his.IsAlive && his.Realm == healer.Realm && healer.IsWithinRadius(his, 2_000)
                ? his : null;
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

        /// <summary>The most hurt raid member under 85% in reach that no tree hits (or under half), for a held heal; or null.</summary>
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

        /// <summary>The fighter a tree hits and its threat on it (0 when it hits a healer, caster or archer, or nobody).</summary>
        private static double HolderThreat(GameNPC tree)
        {
            if (tree.TargetObject is not GameLiving v || BrainOf(tree) is not StandardMobBrain brain) return 0;
            return RoleOf(v) is "tank" or "melee" ? brain.GetBaseAggroAmount(v) : 0;
        }

        private static bool IsSoftRole(GameLiving v) => RoleOf(v) is "caster" or "healer" or "archer";

        /// <summary>One EPIC_GK_TREES line per Green Knight every 30 s while his trees live: how many, how many a fighter holds,
        /// and whom they hit.</summary>
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
            Log.Info($"EPIC_GK_TREES boss=\"{gk.Name}\" health={gk.HealthPercent} alive={trees.Length} held={held} " +
                     $"onAreaCasters={by[TreeOnAreaCaster]} onHealers={by[TreeOnHealer]} onCasters={by[TreeOnCaster]} onOthers={by[TreeOnOther]}");
        }

        /// <summary>A guard: a melee raider among the first <see cref="GreenKnightGuardsPerParty"/> melee (by object id) of a
        /// party with a living area caster near him.</summary>
        private static bool IsGreenKnightGuard(GameLiving raider, GameNPC gk, long now)
        {
            if (raider.Group is not Group g) return false;
            GameLiving[] members = g.GetMembersInTheGroup().Where(m => m != null && m.IsAlive).ToArray();
            if (!members.Any(m => m is GameBot b && RoleOf((GameLiving)b) == "caster" && AreaRadiusOf(b, now) > 0 && gk.IsWithinRadius(b, GreenKnightGrowRadius)))
                return false;
            return members.Where(m => RoleOf(m) == "melee" && m != gk.TargetObject).OrderBy(m => m.ObjectID)
                .Take(GreenKnightGuardsPerParty).Contains(raider);
        }

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
            Vector3 healSpot = GreenKnightSpotFor(gk, GreenKnightHealDistance, c.Now);
            bool healer = c.ClassId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)c.ClassId);
            if (healer)
                return Vector2.Distance(new(c.At.X, c.At.Y), new(healSpot.X, healSpot.Y)) > GreenKnightSpotSlack
                    ? new(BotAction.Reposition, c.Target, healSpot,
                        $"gk-heal-spot: healers together {GreenKnightHealDistance} from him, away from his target")
                    : null;
            if (c.AtRange)
            {
                if (raider is GameBot caster && AreaRadiusOf(caster, c.Now) is > 0 and var radius)
                {
                    Vector3 burnSpot = GreenKnightSpotFor(gk, GreenKnightBurnDistance, c.Now);
                    if (Vector2.Distance(new(c.At.X, c.At.Y), new(burnSpot.X, burnSpot.Y)) > GreenKnightSpotSlack)
                        return new(BotAction.Reposition, c.Target, burnSpot,
                            $"gk-burn-spot: area casters {GreenKnightBurnDistance} from him, away from his target");
                    BurnView[] burn = trees.Select(t => new BurnView(t.ObjectID, t.X, t.Y, raider.GetDistanceTo(t),
                        BrainOf(t)?.GetBaseAggroAmount(raider) ?? 0, HolderThreat(t))).ToArray();
                    int aim = GreenKnightBurnTree(burn, GreenKnightBurnThreat, Math.Max(0, radius - ClumpEdgeMargin));
                    if (aim >= 0)
                        return trees[aim] == c.Target ? new(BotAction.None, trees[aim], null, null)
                            : new(BotAction.Retarget, trees[aim], null,
                                $"gk-burn: area spell on the tagged trees at \"{trees[aim].Name}\" ({trees.Length} alive)");
                    return c.Target == gk ? new(BotAction.None, gk, null, null)
                        : new(BotAction.Retarget, gk, null, "gk-burn: no tagged clump; on him");
                }
                // A caster or archer without an area spell: out of his trees' reach.
                Vector3? outSpot = GreenKnightOutSpot(c.At, PosOf(gk), healSpot);
                return new(BotAction.StandOff, gk, outSpot,
                    $"gk-out: casters and archers without an area spell {GreenKnightOutDistance} from him (no tree grows past {GreenKnightGrowRadius})");
            }
            // The melee: tag.
            bool guard = IsGreenKnightGuard(raider, gk, c.Now);
            bool areaLeft = AreaCastersNear(gk, c.Now);
            TagView[] views = trees.Select(t =>
            {
                GameLiving v = t.TargetObject as GameLiving;
                return new TagView(t.ObjectID, raider.GetDistanceTo(t), v == raider, BrainOf(t)?.GetBaseAggroAmount(raider) ?? 0,
                    HolderThreat(t), v != null && v != raider && IsSoftRole(v), t.HealthPercent);
            }).ToArray();
            int pick = GreenKnightTagTree(views, raider.ObjectID, guard, areaLeft);
            if (pick >= 0)
            {
                GameNPC tree = trees[pick];
                if (tree == c.Target) return new(BotAction.None, tree, null, null);
                string why = views[pick].OnSoft ? "gk-peel: off a healer, caster or archer"
                    : views[pick].OnMe && views[pick].MyThreat > 0 ? "gk-tag: finishing the tag"
                    : views[pick].HolderThreat < GreenKnightTagThreat ? "gk-tag: no fighter holds it"
                    : guard ? "gk-guard: by the area casters"
                    : areaLeft ? "gk-finish: a burned tree on me" : "gk-finish: no area caster left; killing my own";
                return new(BotAction.Retarget, tree, null,
                    $"{why} (\"{tree.Name}\", {trees.Length} alive, {views.Count(v => v.HolderThreat >= GreenKnightTagThreat)} held)");
            }
            if (guard)
            {
                Vector3 burnSpot = GreenKnightSpotFor(gk, GreenKnightBurnDistance, c.Now);
                return new(BotAction.StandOff, gk,
                    Vector2.Distance(new(c.At.X, c.At.Y), new(burnSpot.X, burnSpot.Y)) > GreenKnightSpotSlack ? burnSpot : null,
                    "gk-guard: with the area casters");
            }
            return c.Target == gk ? new(BotAction.None, gk, null, null)
                : new(BotAction.Retarget, gk, null, "gk-tag: every tree near me is held; on him");
        }

        private static readonly Dictionary<GameNPC, (long Tick, bool Any)> GreenKnightAreaCasters = new();

        /// <summary>A living area caster of the raid within 4,000 of him (looked up once a second; the melee's fallback when
        /// none is left: kill their own trees).</summary>
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
    }
}
