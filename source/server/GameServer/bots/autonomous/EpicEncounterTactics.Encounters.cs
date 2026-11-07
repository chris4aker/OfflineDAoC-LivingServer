using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DOL.GS
{
    /// <summary>An encounter rule of <see cref="EpicEncounterTactics"/> for the targets of these types (or their subclasses).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class EncounterRuleAttribute(params Type[] targets) : Attribute
    {
        public Type[] Targets { get; } = targets;
    }

    /// <summary>A route rule of <see cref="EpicEncounterTactics"/>: what the raid route fights instead of a chosen monster.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class EncounterRouteRuleAttribute : Attribute { }

    public static partial class EpicEncounterTactics
    {
        /// <summary>What a rule sees: the raider about to attack <paramref name="Target"/>, the NPCs around it, its party's
        /// index, whether it fights at range, the time and its class.</summary>
        public readonly record struct RuleContext(GameLiving Raider, GameNPC Target, IReadOnlyList<GameNPC> Live, int PartyIndex,
            bool CanShootFlyers, long Now, int ClassId);

        // Encounter rules (wave 2, 1.2): one partial file per dungeon (EpicEncounterTactics.CaerSidi.cs, .Krondon.cs, ...)
        // declares its rules as static methods with [EncounterRule(typeof(Boss), ...)] (signature Advice? (RuleContext)) and
        // [EncounterRouteRule] (signature GameNPC (GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)). They
        // are found once by reflection, so a dungeon's file adds its rules without editing this class's other files. A rule
        // returns null when it has no opinion; an advice it returns is final (Action None included: fight on as usual).

        private sealed record Rule(Type[] Targets, Func<RuleContext, Advice?> Decide, string Name);

        private delegate GameNPC RouteRule(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason);

        private static Rule[] _rules;
        private static RouteRule[] _routeRules;

        private static Rule[] Rules
        {
            get
            {
                if (_rules != null) return _rules;
                var found = new List<Rule>();
                foreach (MethodInfo m in typeof(EpicEncounterTactics).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (m.GetCustomAttribute<EncounterRuleAttribute>() is { } a)
                        found.Add(new Rule(a.Targets, m.CreateDelegate<Func<RuleContext, Advice?>>(), m.Name));
                return _rules = found.OrderBy(r => r.Name, StringComparer.Ordinal).ToArray();
            }
        }

        private static RouteRule[] RouteRules => _routeRules ??= typeof(EpicEncounterTactics)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.GetCustomAttribute<EncounterRouteRuleAttribute>() != null)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => m.CreateDelegate<RouteRule>()).ToArray();

        /// <summary>The names of the encounter rules in force (tests, logs).</summary>
        public static string[] EncounterRuleNames => Rules.Select(r => r.Name).ToArray();

        /// <summary>Some encounter rule is written for this target's type.</summary>
        public static bool HasEncounterRule(GameNPC target) =>
            target != null && Rules.Any(r => r.Targets.Any(t => t.IsInstanceOfType(target)));

        /// <summary>The first encounter rule's advice for this raider and target, or null.</summary>
        public static Advice? EncounterAdvice(RuleContext c)
        {
            if (c.Target == null) return null;
            foreach (Rule rule in Rules)
            {
                if (!rule.Targets.Any(t => t.IsInstanceOfType(c.Target))) continue;
                if (rule.Decide(c) is Advice advice) return advice;
            }
            return null;
        }

        /// <summary>What the route fights instead of <paramref name="chosen"/> by an encounter's route rule (null: none).</summary>
        public static GameNPC EncounterRoute(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (chosen == null || live == null) return null;
            foreach (RouteRule rule in RouteRules)
                if (rule(chosen, live, out reason) is { } other && other != chosen)
                    return other;
            reason = null;
            return null;
        }

        /// <summary>A raider's position.</summary>
        private static System.Numerics.Vector3 At(GameObject o) => new(o.X, o.Y, o.Z);

        /// <summary>The nearest living NPC of type <typeparamref name="T"/> among <paramref name="live"/> in the region of
        /// <paramref name="near"/> within <paramref name="radius"/>, or null.</summary>
        private static T NearestLive<T>(GameObject near, IEnumerable<GameNPC> live, int radius) where T : GameNPC =>
            live?.OfType<T>().Where(n => Present(n) && n.CurrentRegionID == near.CurrentRegionID && near.IsWithinRadius(n, radius))
                .OrderBy(n => near.GetDistanceTo(n)).FirstOrDefault();

        /// <summary>A point <paramref name="distance"/> from <paramref name="from"/>, on the raider's side of it (away from it).</summary>
        public static System.Numerics.Vector3 AwayFrom(System.Numerics.Vector3 raider, System.Numerics.Vector3 from, float distance)
        {
            var d = new System.Numerics.Vector2(raider.X - from.X, raider.Y - from.Y);
            var dir = d.Length() < 1 ? new System.Numerics.Vector2(1, 0) : System.Numerics.Vector2.Normalize(d);
            var p = new System.Numerics.Vector2(from.X, from.Y) + dir * distance;
            return new System.Numerics.Vector3(p.X, p.Y, raider.Z);
        }

        private static float Flat(System.Numerics.Vector3 a, System.Numerics.Vector3 b) =>
            System.Numerics.Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
    }
}
