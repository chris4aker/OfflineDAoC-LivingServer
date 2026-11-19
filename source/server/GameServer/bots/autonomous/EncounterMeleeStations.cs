using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// A melee position order for one encounter (1.2, owner 2026-11-07, Grand Summoner Govannon's demon): a tactic gives a
    /// bot a follow distance it keeps from its target while it fights it, instead of closing to its usual 75, for a few
    /// seconds at a time (the tactic renews it each turn it still applies, so it ends by itself when the phase does).
    /// GameBot.StickMinimumRange reads it. Nothing else changes: the bot still attacks, from that distance.
    /// </summary>
    public static class EncounterMeleeStations
    {
        /// <summary>How long one order lasts without being renewed.</summary>
        public const long OrderMilliseconds = 5_000;

        private readonly record struct Order(GameObject Target, int MinimumFollow, long Until);

        private static readonly Dictionary<GameLiving, Order> Orders = new();
        // Read on every bot's follow turn: no lock while nobody holds an order (the usual case).
        private static int _count;

        /// <summary>Keep <paramref name="minimumFollow"/> from <paramref name="target"/> while fighting it, until
        /// <paramref name="now"/> + <see cref="OrderMilliseconds"/>.</summary>
        public static void Hold(GameLiving bot, GameObject target, int minimumFollow, long now)
        {
            if (bot == null || target == null) return;
            lock (Orders)
            {
                if (Orders.Count > 2_048) Orders.Clear();
                Orders[bot] = new(target, minimumFollow, now + OrderMilliseconds);
                System.Threading.Volatile.Write(ref _count, Orders.Count);
            }
        }

        /// <summary>The follow distance the bot keeps from its current target, or null for its own.</summary>
        public static int? MinimumFollowFor(GameLiving bot, GameObject currentTarget, long now)
        {
            if (bot == null || currentTarget == null || System.Threading.Volatile.Read(ref _count) == 0) return null;
            lock (Orders)
            {
                if (!Orders.TryGetValue(bot, out Order order)) return null;
                if (order.Until < now)
                {
                    Orders.Remove(bot);
                    System.Threading.Volatile.Write(ref _count, Orders.Count);
                    return null;
                }
                return order.Target == currentTarget ? order.MinimumFollow : null;
            }
        }

        /// <summary>Some raider holding an order of <paramref name="minimumFollow"/> from <paramref name="target"/> still stands
        /// within the area hit's reach of <paramref name="anchor"/> (the melee are not behind him yet).</summary>
        public static bool AnyInReach(GameObject target, Vector3 anchor, int minimumFollow, int radius, int margin, long now)
        {
            lock (Orders)
                foreach (var (raider, order) in Orders)
                    if (order.Target == target && order.MinimumFollow == minimumFollow && order.Until >= now && raider.IsAlive &&
                        InReach(new(raider.X, raider.Y, raider.Z), anchor, radius, margin))
                        return true;
            return false;
        }

        /// <summary>A bot already following <paramref name="target"/> closer than its order follows it again at the order's
        /// distance (an attack started earlier keeps its old follow distance until then).</summary>
        public static void Apply(GameLiving raider, GameObject target, int minimumFollow)
        {
            if (raider is not GameNPC npc || npc.movementComponent == null || npc.FollowTarget != target) return;
            if (npc.movementComponent.MinFollowDistance < minimumFollow)
                npc.Follow(target, minimumFollow, npc.StickMaximumRange);
        }

        public static void ResetForTests()
        {
            lock (Orders)
            {
                Orders.Clear();
                System.Threading.Volatile.Write(ref _count, 0);
            }
        }

        /// <summary>
        /// The spot behind <paramref name="boss"/> as seen from his target (<paramref name="anchor"/>), at
        /// <paramref name="behind"/> from the boss on the line from the anchor through him (pure). A raider there is
        /// <c>behind + |boss - anchor|</c> from the anchor: clear of an area hit around it once that passes its radius.
        /// </summary>
        public static Vector3 BehindSpot(Vector3 boss, Vector3 anchor, float behind, float raiderZ)
        {
            Vector2 b = new(boss.X, boss.Y), a = new(anchor.X, anchor.Y);
            Vector2 away = Vector2.Distance(b, a) >= 1 ? Vector2.Normalize(b - a) : new Vector2(1, 0);
            Vector2 at = b + away * behind;
            return new Vector3(at.X, at.Y, raiderZ);
        }

        // ---- the melee ring (owner 2026-11-08, Govannon's demon: "melee spread over 3 ring spots") -------------------

        /// <summary>
        /// Ring spot <paramref name="slot"/> of <paramref name="spots"/> around <paramref name="boss"/> (pure): at
        /// <paramref name="radius"/> from him, <c>360 / spots</c> degrees apart, starting at <paramref name="angle"/> (radians).
        /// Three spots at 190 lie 329 apart, so an area hit of 300 around a raider at one spot misses the other two.
        /// </summary>
        public static Vector3 RingSpot(Vector3 boss, float angle, int slot, int spots, float radius, float raiderZ)
        {
            spots = Math.Max(1, spots);
            float a = angle + (float)(2 * Math.PI) * (((slot % spots) + spots) % spots) / spots;
            return new Vector3(boss.X + radius * MathF.Cos(a), boss.Y + radius * MathF.Sin(a), raiderZ);
        }

        /// <summary>The ring's slot of a party (pure): its place in the raid's party order, spread over the spots.</summary>
        public static int RingSlotFor(int partyOrder, int spots) => spots <= 0 ? 0 : (int)((uint)partyOrder % (uint)spots);

        /// <summary>A ring's orientation is kept this long after it was last asked for, then chosen again.</summary>
        public const long RingMemoryMilliseconds = 30_000;

        private static readonly Dictionary<object, (float Angle, long Seen)> RingAngles = new();

        /// <summary>The ring's orientation around <paramref name="boss"/>: fixed for the fight (so a target swap does not
        /// send the melee around him), chosen when first asked for: spot 0 towards <paramref name="firstAnchor"/> (his target
        /// then). His target keeps 185 from him in the ring, so an area hit of 300 on it reaches spot 0 only: the other two lie
        /// about 325 from it (with spot 0 behind him instead, two spots would lie about 190 from his target, inside it).</summary>
        public static float RingAngle(object boss, Vector3 bossAt, Vector3 firstAnchor, long now)
        {
            lock (RingAngles)
            {
                if (RingAngles.TryGetValue(boss, out var r) && now - r.Seen <= RingMemoryMilliseconds)
                {
                    RingAngles[boss] = (r.Angle, now);
                    return r.Angle;
                }
                Vector2 away = new(firstAnchor.X - bossAt.X, firstAnchor.Y - bossAt.Y);
                float angle = away.LengthSquared() >= 1 ? MathF.Atan2(away.Y, away.X) : 0f;
                if (RingAngles.Count > 64) RingAngles.Clear();
                RingAngles[boss] = (angle, now);
                return angle;
            }
        }

        private static readonly Dictionary<Group, (int Order, long Tick)> PartyOrders = new();

        /// <summary>The party's place in its raid's party order (cached for 10 s), or -1 outside a raid.</summary>
        public static int PartyOrder(Group group, long now)
        {
            if (group == null) return -1;
            lock (PartyOrders)
                if (PartyOrders.TryGetValue(group, out var hit) && now - hit.Tick < 10_000) return hit.Order;
            int order = -1;
            if (AutonomousRealmRaid.RaidOf(group) is { } raid) order = Array.IndexOf(raid.Parties, group);
            lock (PartyOrders)
            {
                if (PartyOrders.Count > 512) PartyOrders.Clear();
                PartyOrders[group] = (order, now);
            }
            return order;
        }

        public static void ResetRingForTests()
        {
            lock (RingAngles) RingAngles.Clear();
            lock (PartyOrders) PartyOrders.Clear();
        }

        /// <summary>A raider within <paramref name="radius"/> + <paramref name="margin"/> of the anchor (flat) is in the
        /// area hit's reach (pure).</summary>
        public static bool InReach(Vector3 raider, Vector3 anchor, int radius, int margin) =>
            Vector2.Distance(new(raider.X, raider.Y), new(anchor.X, anchor.Y)) <= radius + margin;
    }
}
