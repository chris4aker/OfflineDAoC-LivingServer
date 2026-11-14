using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DOL.GS
{
    /// <summary>
    /// From Offline DAoC True 1.65 (shadowofze; its veteran review M6, 2026-11-11): knowing when to leave. Before this a bad pull always became a wipe: the only
    /// retreat came after deaths (AutonomousGroupDangerRetreat), and then came releases, bind walks, regroup walks and
    /// corpse walks. Period players called "RUN" early instead (Freddyshouse: "if a fight goes bad, let fighters die,
    /// retreat, and come back to rez rather than wipe"); the healer survives, the speed song goes up, the group runs back
    /// the way it came.
    ///
    /// The group coordinator checks a fighting party at most every 2 s from the group's fight board (no area search of
    /// its own) and publishes a retreat order here: a point back along the camp approach and an end time (20 s). The
    /// members' brains read the order lock-free: they stop fighting, drop their targets, put pets on follow, sprint and
    /// run (one route request each on the order), the performers play speed. The same plan type carries a solo bot's
    /// bail-out and a companion healer surviving to raise its player. No monster, level or combat number changes.
    /// </summary>
    public static class BotGroupRetreat
    {
        [DOL.GS.ServerProperties.ServerProperty("bots", "bot_veteran_retreat",
            "Knowing when to leave (from True 1.65): a gamebot party calls RUN before a wipe, a solo gamebot bails out of a fight it is losing badly, a solo caster steps back from a rooted monster, and a hired healer whose player died in a lost fight runs and comes back to raise. Off: bots fight on as before.", true)]
        public static bool Enabled = true;

        public const long CheckMilliseconds = 2_000;
        public const long RetreatMilliseconds = 20_000;
        /// <summary>A party that ran does not call RUN again for this long.</summary>
        public const long CooldownMilliseconds = 60_000;
        /// <summary>How far back along the approach the party runs (Prima/StandardMobBrain: monsters give up past 6,000).</summary>
        public const int RetreatDistance = 3_000;
        public const int MinimumRetreatDistance = 1_500;

        // RUN rules (pure, below)
        public const int TankLowHealthPercent = 50;
        public const int HealerLowPowerPercent = 30;
        public const int HealerEmptyPowerPercent = 10;
        public const int TankCriticalHealthPercent = 35;
        public const int NoHealerTankHealthPercent = 60;

        /// <summary>A retreat in progress: run to <see cref="Point"/> until <see cref="Until"/>.</summary>
        public sealed class Order
        {
            private static long _nextId;
            public long Id { get; } = Interlocked.Increment(ref _nextId);
            public ushort Region { get; init; }
            public Vector3 Point { get; init; }
            public long Until { get; init; }
            public string Reason { get; init; } = string.Empty;
        }

        private static readonly ConditionalWeakTable<Group, Order> Orders = new();

        public static void Publish(Group group, Order order)
        {
            if (group != null && order != null)
                Orders.AddOrUpdate(group, order);
        }

        public static void Clear(Group group)
        {
            if (group != null)
                Orders.Remove(group);
        }

        /// <summary>The group's retreat order while it runs, else null.</summary>
        public static Order Active(Group group, long now) =>
            group != null && Orders.TryGetValue(group, out Order order) && now < order.Until ? order : null;

        /// <summary>
        /// Pure: whether a fighting party calls RUN. (a) More uncontrolled monsters on the group than its living tanks
        /// plus one, no mezzer with power and a mez ready, and the fight already going wrong (a tank under half health or
        /// the healers low on power); (b) every healer nearly out of power with a tank under 35%; (c) every healer dead with
        /// more than one monster on the group and a tank hurt (or no tank left).
        /// </summary>
        public static bool ShouldRun(int uncontrolledOnGroup, int monstersOnGroup, int livingTanks, int livingHealers,
            int bestHealerPowerPercent, int lowestTankHealthPercent, bool mezzerReady)
        {
            if (monstersOnGroup <= 0)
                return false;
            int tankHealth = livingTanks > 0 ? lowestTankHealthPercent : 0;
            bool losing = tankHealth < TankLowHealthPercent || livingHealers > 0 && bestHealerPowerPercent < HealerLowPowerPercent;
            if (uncontrolledOnGroup > livingTanks + 1 && !mezzerReady && losing)
                return true;
            if (livingHealers > 0 && bestHealerPowerPercent < HealerEmptyPowerPercent && tankHealth < TankCriticalHealthPercent)
                return true;
            return livingHealers == 0 && monstersOnGroup > 1 && tankHealth < NoHealerTankHealthPercent;
        }

        /// <summary>
        /// Pure: where to run. Back toward <paramref name="home"/> (the party's rendezvous, the way it came) by
        /// <see cref="RetreatDistance"/> (all the way when home is closer); with no usable home, straight away from the
        /// monsters' centre.
        /// </summary>
        /// <summary>How far a friendly bindstone (its town and guards) may be for a losing bot or party to run to it.</summary>
        public const int SafeBindReach = 4_000;

        /// <summary>
        /// Pure (M6 / M11, completeness review 2026-11-11): where a losing solo bot or party runs. First the realm's
        /// nearest bindstone (a town with guards) when it is within <see cref="SafeBindReach"/>, then the camp's safe spot
        /// worked out ahead by the camp builder, else <paramref name="fallback"/> (back along the approach). A spot is
        /// only taken when the way to it leads away from the monsters (at least a right angle off the line to them), so
        /// the bot never runs through its pack to reach it.
        /// </summary>
        public static Vector3 SafePoint(Vector3 here, Vector3? bind, Vector3? campSafe, Vector3 monsters, Vector3 fallback)
        {
            if (bind is { } stone && Away(here, stone, monsters) &&
                Vector2.DistanceSquared(Flat(here), Flat(stone)) <= (float)SafeBindReach * SafeBindReach)
                return stone;
            if (campSafe is { } spot && Away(here, spot, monsters))
                return spot;
            return fallback;
        }

        private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

        private static bool Away(Vector3 here, Vector3 to, Vector3 monsters) =>
            Vector2.Dot(Flat(to) - Flat(here), Flat(monsters) - Flat(here)) <= 0;

        /// <summary>The realm's nearest bindstone in the region, or null.</summary>
        public static Vector3? NearestBind(ushort region, int x, int y, eRealm realm) =>
            BotReleaseBindPoints.Nearest(region, x, y, realm) is { } p ? new Vector3(p.X, p.Y, p.Z) : null;

        public static Vector3 RetreatPoint(Vector3 fight, Vector3? home, Vector3 monsters, int distance = RetreatDistance)
        {
            Vector2 from = new(fight.X, fight.Y);
            if (home is { } target)
            {
                Vector2 to = new(target.X, target.Y);
                float length = Vector2.Distance(from, to);
                if (length >= MinimumRetreatDistance)
                {
                    if (length <= distance)
                        return target;
                    Vector2 step = from + (to - from) / length * distance;
                    return new(step.X, step.Y, fight.Z);
                }
            }
            Vector2 away = from - new Vector2(monsters.X, monsters.Y);
            if (away.LengthSquared() < 1)
                away = new Vector2(1, 0);
            Vector2 point = from + Vector2.Normalize(away) * distance;
            return new(point.X, point.Y, fight.Z);
        }

        /// <summary>
        /// Pure (solo bail): a solo bot leaves a fight it is losing badly - under 30% health while its monster is over
        /// half, or two or more attackers and under half health - when it has no heal and no crowd control ready.
        /// </summary>
        public static bool SoloShouldBail(int healthPercent, int targetHealthPercent, int attackers, bool healReady,
            bool controlReady) =>
            !healReady && !controlReady &&
            (healthPercent < 30 && targetHealthPercent > 50 || attackers >= 2 && healthPercent < 50);

        /// <summary>
        /// Pure (companion healer): with its player dead, a companion that can raise leaves a fight that is lost - nobody
        /// else is left standing to hold the monsters, or they outnumber the fighters - and comes back to raise.
        /// </summary>
        public static bool CompanionHealerSurvives(bool playerDead, bool canResurrect, int monstersOnGroup,
            int otherLivingFighters) =>
            playerDead && canResurrect && monstersOnGroup > 0 && (otherLivingFighters == 0 || monstersOnGroup > otherLivingFighters);

        /// <summary>O3 / M6 caster step-back: at most once per this long.</summary>
        public const long StepBackMilliseconds = 20_000;
        /// <summary>A rooted or snared monster this close is stepped away from.</summary>
        public const int StepBackTriggerDistance = 450;

        /// <summary>
        /// Pure (caster step-back, owner O3: solo only, never grouped, once per 20 s, one straight step, no route request):
        /// after a root or snare lands on its own monster close by, a solo caster takes one step back to nuke range.
        /// </summary>
        public static bool CasterStepsBack(bool solo, bool caster, bool targetRootedOrSnared, int targetDistance,
            long sinceLastStep) =>
            solo && caster && targetRootedOrSnared && targetDistance <= StepBackTriggerDistance &&
            sinceLastStep >= StepBackMilliseconds;

        /// <summary>Pure: how far from the monster the step ends - inside nuke range, 600 to 1,200 units.</summary>
        public static int StepBackStandOff(int spellRange) => Math.Clamp(spellRange - 250, 600, 1_200);
    }
}
