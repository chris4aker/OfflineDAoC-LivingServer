using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// The curers' post (1.2, owner 2026-11-11: "Tactics only for now"; no number of the encounter changes).
    /// <para>Why: Spindler Broodmother mesmerizes everyone within 300 of one raider, picked at random among those within
    /// 1,500 of her, for 60 seconds, about every 30 seconds. The raid fights her as one pile under her (seen on the owner's
    /// PC, 2026-11-11: the sleep icons came up over the whole raid at once), so a cast catches nearly everyone, the
    /// raiders who cure a mesmerize included, and the few curers it missed (about five at a time: RAID_CURE_MEZZ) lift it
    /// more slowly than she casts it. The played world's raids wiped there at 22% and 52% that day.</para>
    /// <para>First try (play test posts1, same day, PC played-world copy): every party's whole back line at its own post.
    /// About half the raid stood mesmerized instead of four fifths, but her newly-born spindlers, which hunt bards,
    /// mentalists, druids and wardens, followed the back line to nine posts and no longer bunched for the area spells: 42
    /// were alive at once (earlier runs: 2 to 8) and the raid wiped at 21%.</para>
    /// <para>What: a tactic with <see cref="Tactic.CurerPostSpacing"/> sends half of the raid's curers (every second one,
    /// by its id) to one post beside the fight, further from the melee than her mesmerize reaches; everyone else fights as
    /// before, in one place, so the newborns still bunch. A cast on the pile leaves the post's curers awake: they cure the
    /// pile's curers first (RaidCureMezz), and those the rest. A cast on the post leaves the pile's curers awake.</para>
    /// <para>How: <see cref="ThinkPartyPost"/> runs on every brain turn of an autonomous raider (from
    /// <see cref="ThinkBlast"/>), because a healer has no hostile target and never reaches <see cref="TryHandle"/>. A post
    /// curer further than <see cref="PartyPostTolerance"/> from the post walks there between casts; at the post the turn is
    /// its own. The post is the first floor spot of a ring around the boss, anchored where she stood when the fight was
    /// first seen; it moves, at most every eight seconds, when she has moved <see cref="PartyPostReanchor"/> from that spot
    /// (she walks to the raider she teleports away).</para>
    /// Autonomous gamebots in a realm raid only.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>The inner ring lies the spacing plus this from the boss: the melee stand within about 200 of her, so a
        /// post on it stays further than her mesmerize's 300 from every one of them while she stands within
        /// <see cref="PartyPostReanchor"/> of the ring's anchor (380 + 280 - 150 - 200 = 310).</summary>
        public const int PartyPostMeleeClear = 280;
        /// <summary>The rings lie the spacing plus this apart.</summary>
        public const int PartyPostRingGap = 20;
        /// <summary>Two rings: with a spacing of 380 they lie 660 and 1,060 from her (10 and 17 posts), inside the 1,500 her
        /// own spell and the raid's spells, heals and cures reach.</summary>
        public const int PartyPostRings = 2;
        /// <summary>A raider within this of its post is at it.</summary>
        public const int PartyPostTolerance = 200;
        /// <summary>The ring moves once the boss stands this far from where it was anchored, at most once every
        /// <see cref="PartyPostReanchorMilliseconds"/> (while she walks, the back line waits for her to stop).</summary>
        public const int PartyPostReanchor = 150;
        public const long PartyPostReanchorMilliseconds = 8_000;
        /// <summary>Raiders within this of the boss take their post.</summary>
        public const int PartyPostReach = 2_600;
        /// <summary>A boss nobody has fought for this long has no posts any more.</summary>
        public const long PartyPostWatchMilliseconds = 10_000;
        /// <summary>A candidate spot whose floor lies further than this from it has no floor (a wall, a pit).</summary>
        public const int PartyPostFloorSnap = 96;
        /// <summary>With fewer posts than this in the boss's sight, spots with a walked corridor to her count too.</summary>
        public const int PartyPostMinimum = 4;

        /// <summary>
        /// The candidate posts around <paramref name="center"/> (pure): <see cref="PartyPostRings"/> rings, the inner one
        /// first, each with as many spots as lie at least <paramref name="spacing"/> apart on it, the outer ring turned half
        /// a step against the inner one.
        /// </summary>
        public static Vector3[] PostRing(Vector3 center, int spacing)
        {
            if (spacing <= 0) return [];
            var spots = new List<Vector3>();
            for (int ring = 0; ring < PartyPostRings; ring++)
            {
                float radius = spacing + PartyPostMeleeClear + ring * (spacing + PartyPostRingGap);
                int count = (int)Math.Floor(2 * Math.PI * radius / spacing);
                while (count > 1 && 2 * radius * Math.Sin(Math.PI / count) < spacing) count--;
                double turn = ring % 2 == 0 ? 0 : Math.PI / count;
                for (int i = 0; i < count; i++)
                {
                    double a = turn + 2 * Math.PI * i / count;
                    spots.Add(new Vector3(center.X + radius * (float)Math.Cos(a), center.Y + radius * (float)Math.Sin(a), center.Z));
                }
            }
            return spots.ToArray();
        }

        /// <summary>
        /// The posts among <paramref name="candidates"/> (pure): in their order, each kept when it lies at least
        /// <paramref name="spacing"/> from every post kept before it (the floor moves a candidate, so two can come close).
        /// </summary>
        public static Vector3[] PickPosts(IReadOnlyList<Vector3> candidates, int spacing)
        {
            var kept = new List<Vector3>();
            foreach (Vector3 c in candidates ?? [])
                if (kept.All(k => Vector2.Distance(new(k.X, k.Y), new(c.X, c.Y)) >= spacing))
                    kept.Add(c);
            return kept.ToArray();
        }

        /// <summary>The ring is anchored anew (pure): it has no anchor yet, or the boss has moved
        /// <see cref="PartyPostReanchor"/> from it and the ring is <see cref="PartyPostReanchorMilliseconds"/> old.</summary>
        public static bool PostsNeedAnchor(Vector3? anchor, Vector3 boss, long anchoredMillisecondsAgo) =>
            anchor is not Vector3 a || anchoredMillisecondsAgo >= PartyPostReanchorMilliseconds &&
            Vector2.Distance(new(a.X, a.Y), new(boss.X, boss.Y)) > PartyPostReanchor;

        public enum PostStep { CarryOn, Walk }

        /// <summary>What a post curer does about the post this turn (pure): at it, mid-cast or unable to move it
        /// carries on; else it walks there.</summary>
        public static PostStep StepToPost(float distanceToPost, bool casting, bool cannotMove) =>
            distanceToPost <= PartyPostTolerance || casting || cannotMove ? PostStep.CarryOn : PostStep.Walk;

        /// <summary>The bosses with a curers' post whose fight is on, with when a raider last fought them.</summary>
        private static readonly ConcurrentDictionary<GameNPC, long> PostWatch = new();
        private static readonly Dictionary<GameNPC, (Vector3 Anchor, Vector3[] Posts, long Tick)> PostRings = new();

        /// <summary>A raider fights <paramref name="target"/>: if its tactic has a curers' post, it is on.</summary>
        private static void WatchPartyPosts(GameNPC target, long now)
        {
            if (For(target) is { CurerPostSpacing: > 0 } && target.InCombat) PostWatch[target] = now;
        }

        /// <summary>The floor posts around <paramref name="center"/>: the ring's candidates that have floor, are far enough
        /// from her and stand in her sight along the floor (a straight walk to her, so nothing stands between a caster there
        /// and her); with fewer than <see cref="PartyPostMinimum"/> of those, the ones with a walked corridor to her too.</summary>
        private static Vector3[] FloorPosts(GameNPC boss, Vector3 center, int spacing)
        {
            var nav = PathfindingProvider.Instance;
            Zone zone = boss.CurrentZone;
            if (zone == null || nav is not { IsAvailable: true } || !nav.HasNavmesh(zone) ||
                nav.GetClosestPoint(zone, center, 128, 128, 256, nav.DefaultFilters) is not Vector3 floor)
                return [];
            var inSight = new List<Vector3>();
            var reachable = new List<Vector3>();
            foreach (Vector3 probe in PostRing(center, spacing))
            {
                if (nav.GetClosestPoint(zone, probe, PartyPostFloorSnap, PartyPostFloorSnap, 400, nav.DefaultFilters) is not Vector3 at ||
                    Flat(at, center) < spacing + PartyPostMeleeClear - PartyPostFloorSnap)
                    continue;
                if (nav.HasLineOfSight(zone, at, floor, nav.DefaultFilters)) inSight.Add(at);
                else if (AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, floor, at)) reachable.Add(at);
            }
            Vector3[] posts = PickPosts(inSight, spacing);
            return posts.Length >= PartyPostMinimum ? posts : PickPosts(inSight.Concat(reachable).ToArray(), spacing);
        }

        /// <summary>The posts of <paramref name="boss"/>'s fight now (the ring is built once per anchor).</summary>
        private static Vector3[] PartyPosts(GameNPC boss, int spacing, long now)
        {
            Vector3 at = At(boss);
            lock (PostRings)
            {
                if (PostRings.TryGetValue(boss, out var ring) && !PostsNeedAnchor(ring.Anchor, at, now - ring.Tick)) return ring.Posts;
                Vector3[] posts = FloorPosts(boss, at, spacing);
                if (PostRings.Count > 64) PostRings.Clear();
                PostRings[boss] = (at, posts, now);
                Log.Info($"EPIC_PARTY_POSTS boss=\"{boss.Name}\" region={boss.CurrentRegionID} anchor={(int)at.X},{(int)at.Y},{(int)at.Z} " +
                         $"spacing={spacing} candidates={PostRing(at, spacing).Length} posts={posts.Length}");
                return posts;
            }
        }

        /// <summary>Which curers stand at the post (pure): every second one by its id; the others stay with the raid.</summary>
        public static bool StandsAtCurerPost(bool canCureMesmerize, long id) => canCureMesmerize && (id & 1) == 0;

        /// <summary>
        /// A boss with a curers' post is fought near the bot: a post curer of the raid walks to the post, between casts, and
        /// holds its turn while it walks. False: at the post (or not concerned), the turn is its own.
        /// </summary>
        private static bool ThinkPartyPost(GameBot bot, long now)
        {
            foreach (var (boss, seen) in PostWatch)
            {
                if (!Present(boss) || !boss.InCombat || now - seen > PartyPostWatchMilliseconds)
                {
                    PostWatch.TryRemove(boss, out _);
                    lock (PostRings) PostRings.Remove(boss);
                    continue;
                }
                if (boss.CurrentRegionID != bot.CurrentRegionID || For(boss) is not { CurerPostSpacing: > 0 } tactic ||
                    !bot.IsWithinRadius(boss, PartyPostReach))
                    continue;
                if (!StandsAtCurerPost(bot.CureMezz != null, bot.ObjectID) || AutonomousRealmRaid.RaidOf(bot.Group) == null) return false;
                Vector3[] posts = PartyPosts(boss, tactic.CurerPostSpacing, now);
                if (posts.Length == 0) return false;
                Vector3 post = posts[0];
                bool cannotMove = bot.IsMezzed || bot.IsStunned;
                if (StepToPost(Flat(At(bot), post), bot.IsCasting, cannotMove) == PostStep.CarryOn) return false;
                bot.StopAttack();
                if (bot.FollowTarget != null) bot.StopFollowing();
                if (!bot.IsMovingOnPath) bot.PathTo(new Point3D((int)post.X, (int)post.Y, (int)post.Z), bot.MaxSpeed);
                LogDecision(bot, boss, new(BotAction.Reposition, boss, post,
                    $"curers' post of \"{boss.Name}\": every second curer stands {tactic.CurerPostSpacing} clear of the raid"));
                return true;
            }
            return false;
        }

        public static void ResetPartyPostsForTests()
        {
            PostWatch.Clear();
            lock (PostRings) PostRings.Clear();
        }
    }
}
