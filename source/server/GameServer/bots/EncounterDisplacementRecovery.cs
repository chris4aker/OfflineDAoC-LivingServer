using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// A bot moved by an encounter script gets back to its raid (1.2, the epic "wow" pass). One path for both kinds of bot:
    /// the player's companion bots (/spawn, /raid) and autonomous gamebots (raids, bot-led groups), both run BotBrain, which
    /// calls <see cref="Think"/> first on each turn. Real players recover by themselves (only their heal priority and the
    /// call-outs come from here).
    /// <list type="bullet">
    /// <item>Thrown into the air: the bot hangs at the spot for the fall's time, lands on the floor the navmesh has under it
    /// and takes the player's fall damage (<see cref="EncounterDisplacement.FallDamagePercent"/>).</item>
    /// <item>Then it walks back to its party (the companion's player, its party leader, else where it stood) along the
    /// navmesh. Something attacking it on the way is fought (the normal brain gets the turn).</item>
    /// <item>A jail: it waits, fighting whatever attacks it, until the jail's rule frees it (<see cref="RegisterJail"/>;
    /// default: the boss is dead or out of the fight), then walks back.</item>
    /// <item>The fair fallback: no navmesh path back, no progress for <see cref="ProgressWindowMilliseconds"/>, past its
    /// walk budget, or a jail held past <see cref="JailMaxMilliseconds"/>: a short recall (<see cref="RecallMilliseconds"/>
    /// standing still, then it is moved to the floor at its party). A bot is never left stuck.</item>
    /// </list>
    /// Healers prefer a displaced raider (<see cref="PreferPatient"/>); the party calls it in /group in the bot voice.
    /// </summary>
    public static class EncounterDisplacementRecovery
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const int ProgressWindowMilliseconds = 10_000;
        public const int MinimumProgress = 150;
        public const int ArriveRadius = 450;
        public const int FightRadius = 900;
        public const int MinimumWalkMilliseconds = 20_000;
        public const int MaximumWalkMilliseconds = 180_000;
        public const int JailMaxMilliseconds = 180_000;
        public const int RecallMilliseconds = 5_000;
        public const int AssumedSpeed = 180;
        /// <summary>A heal target counts as displaced this long after it was moved.</summary>
        public const int HealPriorityMilliseconds = 20_000;

        public enum Stage { Airborne, Landed, Walking, Jailed, Recalling, Done }

        public enum OrderKind
        {
            /// <summary>Use the turn, do nothing (in the air, waiting in the jail, channelling the recall).</summary>
            Hold,
            /// <summary>Put the bot on <see cref="Order.Point"/> and apply <see cref="Order.FallPercent"/>.</summary>
            Land,
            /// <summary>Path to <see cref="Order.Point"/>.</summary>
            Walk,
            /// <summary>Let the normal brain have the turn (fight what attacks it).</summary>
            Fight,
            /// <summary>Move the bot to <see cref="Order.Point"/> (the fallback); recovery is over.</summary>
            Recall,
            /// <summary>Recovery is over; the normal brain takes over.</summary>
            Done,
        }

        public readonly record struct Order(OrderKind Kind, Vector3 Point = default, int FallPercent = 0, string Reason = null);

        /// <summary>What the bot sees this turn. <paramref name="Corridor"/> is asked only when a path back is needed.
        /// <paramref name="HazardEscape"/>: the bot stands in a hazard (Orshom's Fire, where Orylle throws) and this is the way
        /// out; it never counts as back with the raid while it stands in one (wave 2, 1.2).</summary>
        /// <param name="Held">Mesmerized, stunned or rooted (Queen Kula's frost circle): it can't walk, which is no stall.</param>
        public readonly record struct Observation(long Now, bool Alive, Vector3 Position, Vector3 ReturnPoint, bool UnderAttack,
            bool Released, bool AtFight, Func<Vector3, Vector3, bool> Corridor, bool Held = false, Vector3? HazardEscape = null);

        /// <summary>A walk back held by crowd control ends in the fallback this long after it started at the latest.</summary>
        public const int MaximumHeldWalkMilliseconds = 300_000;

        /// <summary>One bot's recovery (pure state; <see cref="Step"/> moves it on).</summary>
        public sealed class Plan
        {
            public DisplacementKind Kind;
            public Stage Stage;
            public long LandAt;
            public Vector3 Landing;
            public int FallPercent;
            public bool OnMesh = true;
            public Vector3 ReturnPoint;
            public long Deadline;
            /// <summary>The latest the walk may end, however long crowd control held it (<see cref="MaximumHeldWalkMilliseconds"/>).</summary>
            public long HardDeadline;
            public long NextProgressCheck;
            public float DistanceAtCheck;
            public long RecallAt;
            public string Reason;
            public bool Spoke;

            /// <summary>The plan for a bot moved at <paramref name="now"/>; it lands at once unless it is in the air.</summary>
            public static Plan For(DisplacementKind kind, long now, Vector3 landing, int fallHeight, int safeFallLevel, bool onMesh) => new()
            {
                Kind = kind,
                Stage = Stage.Airborne,
                LandAt = now + (fallHeight > EncounterDisplacement.FallThreshold ? EncounterDisplacement.FallMilliseconds(fallHeight) : 0),
                Landing = landing,
                FallPercent = fallHeight > EncounterDisplacement.FallThreshold ? EncounterDisplacement.FallDamagePercent(fallHeight, safeFallLevel) : 0,
                OnMesh = onMesh,
            };
        }

        /// <summary>The walk budget for <paramref name="distance"/> units: twice the walk at <see cref="AssumedSpeed"/>, at
        /// least <see cref="MinimumWalkMilliseconds"/>, at most <see cref="MaximumWalkMilliseconds"/>.</summary>
        public static long WalkBudget(float distance) =>
            Math.Clamp((long)(distance / AssumedSpeed * 2_000) + 10_000, MinimumWalkMilliseconds, MaximumWalkMilliseconds);

        /// <summary>The recovery's next move (pure).</summary>
        public static Order Step(Plan p, Observation o)
        {
            if (!o.Alive)
                return Finish(p, "died");
            p.ReturnPoint = o.ReturnPoint;
            switch (p.Stage)
            {
                case Stage.Airborne:
                    if (o.Now < p.LandAt) return new(OrderKind.Hold);
                    p.Stage = Stage.Landed;
                    return new(OrderKind.Land, p.Landing, p.FallPercent);

                case Stage.Landed:
                    if (p.Kind == DisplacementKind.Pull)
                        return Finish(p, "nothing to recover from");
                    // A barrier put it back where it must stay (inside the horsemen's room, in front of the falls): no walk
                    // back to where it stood, which is the barrier itself. The raid's orders keep it inside from here
                    // (EpicEncounterTactics.KeepInsideFor); a bot sent to the barrier again and again is logged.
                    if (p.Kind == DisplacementKind.Barrier)
                        return Finish(p, "kept inside");
                    if (p.Kind == DisplacementKind.Jail)
                    {
                        p.Stage = Stage.Jailed;
                        p.Deadline = o.Now + JailMaxMilliseconds;
                        return new(OrderKind.Hold, Reason: "jailed");
                    }
                    return StartBack(p, o);

                case Stage.Jailed:
                    if (o.Released) return StartBack(p, o);
                    if (o.Now >= p.Deadline) return StartRecall(p, o, "held in the jail too long");
                    return o.UnderAttack ? new(OrderKind.Fight) : new(OrderKind.Hold, Reason: "jailed");

                case Stage.Walking:
                {
                    float distance = Vector3.Distance(o.Position, o.ReturnPoint);
                    if (distance <= ArriveRadius || o.AtFight)
                        return o.HazardEscape is Vector3 outOfIt ? new(OrderKind.Walk, outOfIt, Reason: "out of the hazard first")
                            : Finish(p, "back with the raid");
                    if (o.Now >= p.Deadline) return StartRecall(p, o, "walk back took too long");
                    if (o.Held)
                    {
                        // Mesmerized or rooted where it landed (Kula's throne): it waits it out like a player, then walks on.
                        if (o.Now >= p.HardDeadline) return StartRecall(p, o, "held too long");
                        p.NextProgressCheck = o.Now + ProgressWindowMilliseconds;
                        p.DistanceAtCheck = distance;
                        p.Deadline = Math.Min(p.HardDeadline, Math.Max(p.Deadline, o.Now + WalkBudget(distance)));
                        return new(OrderKind.Hold, Reason: "held");
                    }
                    if (o.UnderAttack)
                    {
                        // Fighting on the way is no stall: the progress window and the walk budget start again when it walks on.
                        p.NextProgressCheck = o.Now + ProgressWindowMilliseconds;
                        p.DistanceAtCheck = distance;
                        p.Deadline = Math.Max(p.Deadline, o.Now + WalkBudget(distance));
                        return new(OrderKind.Fight);
                    }
                    if (o.Now >= p.NextProgressCheck)
                    {
                        if (p.DistanceAtCheck - distance < MinimumProgress) return StartRecall(p, o, "no progress walking back");
                        p.DistanceAtCheck = distance;
                        p.NextProgressCheck = o.Now + ProgressWindowMilliseconds;
                    }
                    return new(OrderKind.Walk, o.ReturnPoint);
                }

                case Stage.Recalling:
                    if (o.Now < p.RecallAt) return new(OrderKind.Hold, Reason: p.Reason);
                    p.Stage = Stage.Done;
                    return new(OrderKind.Recall, o.ReturnPoint, Reason: p.Reason);

                default:
                    return new(OrderKind.Done, Reason: p.Reason);
            }
        }

        private static Order StartBack(Plan p, Observation o)
        {
            float distance = Vector3.Distance(o.Position, o.ReturnPoint);
            if ((distance <= ArriveRadius || o.AtFight) && o.HazardEscape is Vector3 outOfIt)
            {
                // Landed by the raid but in a hazard (Orylle throws into Orshom's Fire): out of it first, then it is back.
                p.Stage = Stage.Walking;
                p.Deadline = o.Now + MinimumWalkMilliseconds;
                p.NextProgressCheck = o.Now + ProgressWindowMilliseconds;
                p.DistanceAtCheck = distance;
                return new(OrderKind.Walk, outOfIt, Reason: "out of the hazard first");
            }
            if (distance <= ArriveRadius || o.AtFight) return Finish(p, "landed by the raid");
            if (!p.OnMesh) return StartRecall(p, o, "landed off the navmesh");
            if (o.Corridor != null && !o.Corridor(o.Position, o.ReturnPoint)) return StartRecall(p, o, "no path back");
            p.Stage = Stage.Walking;
            p.Deadline = o.Now + WalkBudget(distance);
            p.HardDeadline = o.Now + MaximumHeldWalkMilliseconds;
            p.NextProgressCheck = o.Now + ProgressWindowMilliseconds;
            p.DistanceAtCheck = distance;
            return new(OrderKind.Walk, o.ReturnPoint);
        }

        private static Order StartRecall(Plan p, Observation o, string reason)
        {
            p.Stage = Stage.Recalling;
            p.RecallAt = o.Now + RecallMilliseconds;
            p.Reason = reason;
            return new(OrderKind.Hold, Reason: reason);
        }

        private static Order Finish(Plan p, string reason)
        {
            p.Stage = Stage.Done;
            p.Reason = reason;
            return new(OrderKind.Done, Reason: reason);
        }

        // ---- Jails ---------------------------------------------------------------------------------------------------

        /// <summary>How a jail ends: <paramref name="Released"/>(bot, source) true frees the bot; NeedsRescue: the raid
        /// (or the bot) must kill something to get out.</summary>
        public sealed record JailRule(Func<GameLiving, GameObject, bool> Released, bool NeedsRescue, string How);

        private static readonly ConcurrentDictionary<string, JailRule> Jails = new(StringComparer.Ordinal)
        {
            // Jailer Vifil (Tuscaran Glacier): the jail is the tunnel mouth by 16631,58683, where his two tunnel guardians
            // (JailerAdd) stand from the pull on. No timer and no release in the script: the jailed raider gets out by
            // killing the guardians (or the fight ends) and walks back through the tunnel.
            ["Jailer"] = new((living, _) => !living.GetNPCsInRadius(1_200).Any(n => n is JailerAdd { IsAlive: true }), true,
                "kill the two tunnel guardians, then walk back through the tunnel"),
        };

        /// <summary>Sets how an encounter's jail ends (tests, later encounters).</summary>
        public static void RegisterJail(string encounter, JailRule rule)
        {
            if (string.IsNullOrEmpty(encounter)) return;
            if (rule == null) Jails.TryRemove(encounter, out _);
            else Jails[encounter] = rule;
        }

        public static JailRule JailFor(string encounter) =>
            encounter != null && Jails.TryGetValue(encounter, out JailRule rule) ? rule : null;

        public static bool JailNeedsRescue(string encounter) => JailFor(encounter)?.NeedsRescue == true;

        /// <summary>The jail is over: its own rule, or the boss is dead or out of the fight.</summary>
        public static bool JailReleased(GameLiving bot, GameObject source, string encounter)
        {
            GameNPC boss = source as GameNPC;
            if (boss == null || !boss.IsAlive || EpicEncounterMechanics.FightIsOver(boss)) return true;
            JailRule rule = JailFor(encounter);
            return rule?.Released?.Invoke(bot, source) == true;
        }

        // ---- The live registry -----------------------------------------------------------------------------------------

        private sealed class Record
        {
            public DisplacementEvent Event;
            public Plan Plan;
            public Vector3 LastWalkTarget;
        }

        private static readonly ConcurrentDictionary<GameBot, Record> Active = new();
        // Everyone (players too) moved lately, for heal priority and call-outs: living -> event.
        private static readonly ConcurrentDictionary<GameLiving, DisplacementEvent> Recent = new();

        /// <summary>The bot is recovering from a displacement now.</summary>
        public static bool IsRecovering(GameBot bot) => bot != null && Active.ContainsKey(bot);

        /// <summary>The bot's recovery stage (tests, logs); null when it is not recovering.</summary>
        public static Stage? StageOf(GameBot bot) => bot != null && Active.TryGetValue(bot, out Record r) ? r.Plan.Stage : null;

        /// <summary>The raider was moved by an encounter in the last <see cref="HealPriorityMilliseconds"/>.</summary>
        public static bool WasDisplaced(GameLiving living, long now) => living != null && Recent.TryGetValue(living, out var e) &&
            now - e.At <= HealPriorityMilliseconds;

        /// <summary>EncounterDisplacement's hook: a bot was just moved.</summary>
        internal static void Begin(GameBot bot, DisplacementEvent e)
        {
            foreach (GameBot gone in Active.Keys.Where(b => b.ObjectState != GameObject.eObjectState.Active).ToList())
                Active.TryRemove(gone, out _);
            int safeFall = 0;
            try { safeFall = bot.GetAbilityLevel(Abilities.SafeFall); } catch (Exception) { }
            Active[bot] = new Record
            {
                Event = e,
                Plan = Plan.For(e.Kind, e.At, e.Landing, e.FallHeight, safeFall, e.OnMesh),
            };
        }

        /// <summary>EncounterDisplacement's hook: anyone was just moved (heal priority, the party's call).</summary>
        internal static void Observe(DisplacementEvent e)
        {
            if (Recent.Count > 512)
                foreach (var old in Recent.Where(p => e.At - p.Value.At > HealPriorityMilliseconds).Select(p => p.Key).ToList())
                    Recent.TryRemove(old, out _);
            Recent[e.Living] = e;
            if (e.Kind == DisplacementKind.Barrier && CountBounce(e) is var bounces && bounces >= BounceWarnCount && Log.IsWarnEnabled)
                Log.Warn($"EPIC_DISPLACEMENT_BOUNCED living=\"{e.Living?.Name}\" bot={e.IsBot} encounter={e.Encounter} " +
                         $"bounces={bounces} within={BounceWindowMilliseconds / 1000}s at={(int)e.From.X},{(int)e.From.Y},{(int)e.From.Z} " +
                         $"reason=\"sent to the barrier again and again: its orders lie past it\"");
            if (e.Kind is DisplacementKind.Pull or DisplacementKind.Barrier) return;
            CallOut(e.Living, e.Kind == DisplacementKind.Jail ? "displaced_jail" : "displaced_heal", e);
        }

        // ---- Barrier bounces -------------------------------------------------------------------------------------------

        /// <summary>A raider moved back by a barrier this many times within <see cref="BounceWindowMilliseconds"/> is logged
        /// (EPIC_DISPLACEMENT_BOUNCED): something keeps sending it at the barrier.</summary>
        public const int BounceWarnCount = 3;
        public const long BounceWindowMilliseconds = 120_000;
        private static readonly ConcurrentDictionary<(GameLiving, string), List<long>> Bounces = new();

        /// <summary>Counts a barrier move; how many this raider had from this encounter in the window, this one included.</summary>
        public static int CountBounce(DisplacementEvent e)
        {
            if (e.Living == null) return 0;
            if (Bounces.Count > 512) Bounces.Clear();
            List<long> times = Bounces.GetOrAdd((e.Living, e.Encounter ?? ""), _ => new List<long>());
            lock (times)
            {
                times.RemoveAll(t => e.At - t > BounceWindowMilliseconds);
                times.Add(e.At);
                return times.Count;
            }
        }

        /// <summary>The barrier moves of this raider by this encounter in the window ending at <paramref name="now"/> (tests, logs).</summary>
        public static int BouncesOf(GameLiving living, string encounter, long now)
        {
            if (living == null || !Bounces.TryGetValue((living, encounter ?? ""), out List<long> times)) return 0;
            lock (times) return times.Count(t => now - t <= BounceWindowMilliseconds);
        }

        /// <summary>Forgets a bot (it left the world); tests.</summary>
        public static void Forget(GameBot bot)
        {
            if (bot == null) return;
            Active.TryRemove(bot, out _);
            Recent.TryRemove(bot, out _);
        }

        public static void ForgetAll()
        {
            Active.Clear();
            Recent.Clear();
            Bounces.Clear();
        }

        /// <summary>BotBrain's first hook on each turn: true when the turn is used by the recovery.</summary>
        public static bool Think(GameBot bot)
        {
            if (bot == null || !Active.TryGetValue(bot, out Record r)) return false;
            if (bot.ObjectState != GameObject.eObjectState.Active || bot.CurrentRegionID != r.Event.Region)
            {
                Active.TryRemove(bot, out _);
                return false;
            }
            Order order = Step(r.Plan, Observe(bot, r));
            return Execute(bot, r, order);
        }

        private static Observation Observe(GameBot bot, Record r)
        {
            var position = new Vector3(bot.X, bot.Y, bot.Z);
            GameNPC boss = r.Event.Source as GameNPC;
            bool atFight = r.Plan.Stage == Stage.Walking && boss is { IsAlive: true } && bot.CurrentRegionID == boss.CurrentRegionID &&
                bot.IsWithinRadius(boss, FightRadius);
            bool released = r.Plan.Stage == Stage.Jailed && JailReleased(bot, r.Event.Source, r.Event.Encounter);
            return new Observation(GameLoop.GameLoopTime, bot.IsAlive, position, ReturnPoint(bot, r.Event), UnderAttack(bot),
                released, atFight, (from, to) => HasPath(bot, from, to), IsHeld(bot), HazardEscapeFor(bot));
        }

        private static bool Execute(GameBot bot, Record r, Order order)
        {
            switch (order.Kind)
            {
                case OrderKind.Hold:
                    if (bot.IsMoving) bot.StopMoving();
                    if (!r.Plan.Spoke && r.Plan.Stage == Stage.Recalling)
                    {
                        r.Plan.Spoke = true;
                        CallOut(bot, "displaced_stuck", r.Event, self: true);
                        if (Log.IsInfoEnabled)
                            Log.Info($"EPIC_DISPLACEMENT_FALLBACK bot=\"{bot.Name}\" encounter={r.Event.Encounter} reason=\"{r.Plan.Reason}\" " +
                                     $"at={bot.X},{bot.Y},{bot.Z} recall_in={RecallMilliseconds}ms");
                    }
                    return true;
                case OrderKind.Land:
                    Land(bot, r, order);
                    return true;
                case OrderKind.Walk:
                    if (!bot.IsMovingOnPath || Vector3.Distance(r.LastWalkTarget, order.Point) > 200)
                    {
                        r.LastWalkTarget = order.Point;
                        bot.PathTo(order.Point, bot.MaxSpeed);
                    }
                    return true;
                case OrderKind.Fight:
                    return false;
                case OrderKind.Recall:
                    Recall(bot, r, order.Point);
                    Active.TryRemove(bot, out _);
                    return true;
                default:
                    Active.TryRemove(bot, out _);
                    if (Log.IsInfoEnabled)
                        Log.Info($"EPIC_DISPLACEMENT_RECOVERED bot=\"{bot.Name}\" encounter={r.Event.Encounter} kind={r.Event.Kind} " +
                                 $"reason=\"{order.Reason}\" after={GameLoop.GameLoopTime - r.Event.At}ms at={bot.X},{bot.Y},{bot.Z}");
                    return false;
            }
        }

        private static void Land(GameBot bot, Record r, Order order)
        {
            if (r.Event.FallHeight > EncounterDisplacement.FallThreshold)
                bot.MoveInRegion(bot.CurrentRegionID, (int)Math.Round(order.Point.X), (int)Math.Round(order.Point.Y),
                    (int)Math.Round(order.Point.Z), bot.Heading, true);
            if (order.FallPercent > 0 && bot.IsAlive)
            {
                // The player's rule (GamePlayer.CalcFallDamage): endurance and a share of health.
                bot.Endurance -= bot.MaxEndurance * order.FallPercent / 100;
                int damage = (int)(0.01 * order.FallPercent * (bot.MaxHealth - 1));
                if (damage > 0) bot.TakeDamage(null, eDamageType.Falling, damage, 0);
            }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_DISPLACEMENT_LANDED bot=\"{bot.Name}\" encounter={r.Event.Encounter} at={bot.X},{bot.Y},{bot.Z} " +
                         $"fall={r.Event.FallHeight} damage={order.FallPercent}% health={bot.HealthPercent}%");
            if (bot.IsAlive && r.Event.Kind != DisplacementKind.Jail && r.Event.Kind is not (DisplacementKind.Pull or DisplacementKind.Barrier))
                CallOut(bot, "displaced_self", r.Event, self: true);
        }

        private static void Recall(GameBot bot, Record r, Vector3 point)
        {
            Vector3 to = point;
            Zone zone = bot.CurrentRegion?.GetZone((int)point.X, (int)point.Y);
            var nav = PathfindingProvider.Instance;
            if (zone != null && nav.IsAvailable && nav.HasNavmesh(zone))
            {
                Vector3 snapped = point;
                if (nav.TrySnapToMesh(zone, ref snapped, 300)) to = snapped;
            }
            bot.StopMoving();
            bool moved = bot.MoveInRegion(bot.CurrentRegionID, (int)Math.Round(to.X), (int)Math.Round(to.Y), (int)Math.Round(to.Z), bot.Heading, true);
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_DISPLACEMENT_RECALLED bot=\"{bot.Name}\" encounter={r.Event.Encounter} reason=\"{r.Plan.Reason}\" " +
                         $"to={(int)to.X},{(int)to.Y},{(int)to.Z} moved={(moved ? "yes" : "no")}");
        }

        /// <summary>Where a displaced bot goes back to: the companion's player, else its party leader (when neither was moved
        /// themselves and both are in the region near the fight), else the raid's front in a raid dungeon near the spot it
        /// stood, else that spot.</summary>
        public static Vector3 ReturnPoint(GameBot bot, DisplacementEvent e)
        {
            const float Near = 6_000;
            long now = GameLoop.GameLoopTime;
            bool Usable(GameLiving l) => l != null && l != bot && l.IsAlive && l.ObjectState == GameObject.eObjectState.Active &&
                l.CurrentRegionID == e.Region && !WasDisplaced(l, now) &&
                Vector3.Distance(new Vector3(l.X, l.Y, l.Z), e.From) <= Near;
            if (bot.IsTemporaryGroupHelper && Usable(bot.Owner))
                return new Vector3(bot.Owner.X, bot.Owner.Y, bot.Owner.Z);
            GameLiving leader = bot.Group?.LivingLeader;
            if (Usable(leader))
                return new Vector3(leader.X, leader.Y, leader.Z);
            if (AutonomousRealmRaid.TryDungeonRegroupPoint(bot, out ushort region, out Vector3 front) && region == e.Region &&
                Vector3.Distance(front, e.From) <= Near)
                return front;
            return e.From;
        }

        /// <summary>Mesmerized, stunned, or rooted (a speed debuff that leaves it almost no speed).</summary>
        private static bool IsHeld(GameBot bot) => bot.IsMezzed || bot.IsStunned ||
            bot.effectListComponent?.ContainsEffectForEffectType(eEffect.MovementSpeedDebuff) == true && bot.MaxSpeed <= 20;

        private static bool UnderAttack(GameBot bot)
        {
            if (!bot.InCombatInLast(3_000)) return false;
            var attackers = bot.attackComponent?.AttackerTracker?.Attackers;
            if (attackers == null) return false;
            foreach (GameLiving a in attackers)
                // A hazard that hurts it (Sys'sro's pit monsters, immune to everything) is walked away from, not fought.
                if (a != null && a.IsAlive && a.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(a, 800) &&
                    !(a is GameNPC n && EpicEncounterTactics.IsHazard(n)))
                    return true;
            return false;
        }

        /// <summary>The way out of the nearest live hazard (EpicEncounterTactics: Orshom's Fire, Sys'sro's pit monsters) the bot
        /// stands in, or null.</summary>
        private static Vector3? HazardEscapeFor(GameBot bot)
        {
            var at = new Vector3(bot.X, bot.Y, bot.Z);
            foreach (GameNPC npc in bot.GetNPCsInRadius(1_000))
                if (npc.IsAlive && EpicEncounterTactics.IsHazard(npc) &&
                    EpicEncounterTactics.HazardEscape(at, new Vector3(npc.X, npc.Y, npc.Z), EpicEncounterTactics.HazardRadiusOf(npc)) is Vector3 escape)
                    return escape;
            return null;
        }

        private static bool HasPath(GameBot bot, Vector3 from, Vector3 to)
        {
            Zone zone = bot.CurrentZone;
            var nav = PathfindingProvider.Instance;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
            Zone toZone = bot.CurrentRegion?.GetZone((int)to.X, (int)to.Y);
            if (toZone != zone) return true; // across a zone seam: the bot mover plans it; the progress check catches a failure
            try { return AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, from, to); }
            catch (Exception ex)
            {
                Log.Warn($"EPIC_DISPLACEMENT corridor check failed bot=\"{bot.Name}\": {ex.Message}");
                return false;
            }
        }

        // ---- Healers -------------------------------------------------------------------------------------------------

        /// <summary>
        /// A healer takes <paramref name="candidate"/> over <paramref name="current"/>: a displaced raider under 90% comes first,
        /// unless the current pick is an emergency (under 40%) lower than it and was not displaced itself (pure).
        /// </summary>
        public static bool PreferPatient(bool candidateDisplaced, int candidateHealth, bool currentDisplaced, int? currentHealth)
        {
            if (!candidateDisplaced || candidateHealth >= 90) return false;
            if (currentHealth is not int cur) return true;
            if (currentDisplaced) return candidateHealth < cur;
            return !(cur < 40 && cur < candidateHealth);
        }

        /// <summary>The displaced raider of this healer's group or raid it should heal first, or null.</summary>
        public static GameLiving DisplacedPatient(GameBot healer, GameLiving current, Func<GameLiving, bool> inReach)
        {
            if (healer == null || Recent.IsEmpty) return null;
            long now = GameLoop.GameLoopTime;
            GameLiving best = null;
            foreach (var (living, e) in Recent)
            {
                if (now - e.At > HealPriorityMilliseconds || living == null || living == current || !living.IsAlive) continue;
                if (living.CurrentRegionID != healer.CurrentRegionID || !healer.IsWithinRadius(living, 2_000)) continue;
                if (!SameSide(healer, living) || inReach != null && !inReach(living)) continue;
                GameLiving against = best ?? current;
                bool againstDisplaced = against != null && WasDisplaced(against, now);
                if (PreferPatient(true, living.HealthPercent, againstDisplaced, against?.HealthPercent))
                    best = living;
            }
            return best;
        }

        private static bool SameSide(GameBot healer, GameLiving living)
        {
            if (living.Realm != healer.Realm) return false;
            if (healer.Group != null && healer.Group.IsInTheGroup(living)) return true;
            if (living is GameBot other && AutonomousRealmRaid.SameExpedition(healer, other)) return true;
            return AutonomousRealmRaid.HasSharedSupport(healer) && AutonomousRealmRaid.SupportMembers(healer).Contains(living);
        }

        // ---- Call-outs -------------------------------------------------------------------------------------------------

        private static readonly ConcurrentDictionary<Group, long> LastCall = new();
        public const int CallGapMilliseconds = 8_000;

        /// <summary>
        /// The party calls it in /group (seen only in a party with the real player; AutonomousBotGroupChatter.Send): a
        /// groupmate ("X got tossed, heals on X", "X is in the jail") or the bot itself ("got tossed, omw back", the
        /// fallback's "no way back from here, porting"). Lines: combat_calls.lines, topics displaced_*. One call a group
        /// every <see cref="CallGapMilliseconds"/>.
        /// </summary>
        private static void CallOut(GameLiving victim, string topic, DisplacementEvent e, bool self = false)
        {
            try
            {
                Group group = victim?.Group;
                if (group == null || !AutonomousLivingServer.GroupHasPlayer(group)) return;
                long now = GameLoop.GameLoopTime;
                if (LastCall.TryGetValue(group, out long last) && now - last < CallGapMilliseconds) return;
                GameBot speaker = self ? victim as GameBot : Speaker(group, victim, now);
                if (speaker == null || !speaker.IsAutonomousWorldBot || !speaker.IsAlive) return;
                var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["target"] = victim.Name,
                    ["boss"] = e.Source?.Name ?? "the boss",
                };
                // The encounter's own line first (Orylle's fire, Sys'sro's pit, Moran's wind), else the general one.
                AutonomousBotVoiceCorpus.Picked picked = default;
                bool found = EncounterTopic(topic, e.Encounter) is { } own &&
                             AutonomousBotVoices.TryLine(speaker, "combat", out picked, topic: own, slots: slots, relax: true);
                if (!found && !AutonomousBotVoices.TryLine(speaker, "combat", out picked, topic: topic, slots: slots, relax: true))
                    return;
                LastCall[group] = now;
                AutonomousBotGroupChatter.Send(speaker, picked.Text, "none");
            }
            catch (Exception ex)
            {
                Log.Warn($"EPIC_DISPLACEMENT call-out failed: {ex.Message}");
            }
        }

        /// <summary>Encounters whose throw has its own lines (topic + "_" + suffix), else null.</summary>
        public static readonly IReadOnlyDictionary<string, string> EncounterTopics = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Orylle"] = "fire", ["SyssroRuthless"] = "pit", ["MoranTheMighty"] = "wind", ["BaneOfHope"] = "bane",
        };

        public static string EncounterTopic(string topic, string encounter) =>
            topic is "displaced_heal" or "displaced_self" && encounter != null && EncounterTopics.TryGetValue(encounter, out string suffix)
                ? topic + "_" + suffix : null;

        /// <summary>Who calls it for the party: its leader when that is a bot that was not moved, else another bot of it.</summary>
        private static GameBot Speaker(Group group, GameLiving victim, long now)
        {
            if (group.LivingLeader is GameBot { IsAlive: true } lead && lead != victim && !WasDisplaced(lead, now)) return lead;
            foreach (GameLiving m in group.GetMembersInTheGroup())
                if (m is GameBot { IsAlive: true, IsAutonomousWorldBot: true } b && b != victim && !WasDisplaced(b, now))
                    return b;
            return null;
        }
    }
}
