using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Epic wave 2 (1.2): the raid plays the mechanics a script announces (EncounterSignals) and the position rules of the
    /// dragons, Xanxicar, Legion and Galladoria. Asked first in <see cref="Decide(GameLiving, GameNPC, IReadOnlyList{GameNPC}, int, bool, long, int)"/>
    /// (after a hazard target and a break-off), so a raider steps out of a glare before any other rule sends it anywhere.
    /// <list type="bullet">
    /// <item><b>Glare</b> (a hit around a raider the boss picked: the dragons' stare, 1,000; Xanxicar's glare, 450): everyone
    /// within its circle but the picked raider and the raider the boss is fighting steps out of it, away from him; healers
    /// heal him first (<see cref="SignalPatient"/>).</item>
    /// <item><b>Around the boss</b> (the dragons' look-around stun and breath, 2,000): casters, archers and healers step out
    /// past it until it has landed; melee can't make it in time and take it, the healers outside keep them up.</item>
    /// <item><b>Attack from within</b> (<see cref="AttackRules"/>): a boss that takes damage only from attackers within a
    /// radius of its spawn (Conservator 800, Easmarach his tether) or of itself (Legion 1,400): a raider farther out steps in.</item>
    /// <item><b>Safe spots</b> (Olcasgean): two parties hold the two spots the whole fight, each on the body that stands there.</item>
    /// <item><b>Kill heals</b> (Legion heals 2.5% per raider he kills): healers near him heal anyone under half first.</item>
    /// </list>
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>A raider this far inside a signal's circle (or this close outside it) steps out to this far past it.</summary>
        public const int SignalMargin = 100, SignalStepOut = 250;
        /// <summary>Signals of bosses this close to a raider apply to it, whatever it is fighting (a dragon's add, a messenger).</summary>
        public const int SignalReach = AddSearchRadius;

        /// <summary>The mechanics around the boss that casters, archers and healers step out of (melee take them).</summary>
        public static readonly string[] StepOutAroundBoss = [EncounterSignals.Stun, EncounterSignals.Breath];

        /// <summary>A boss that takes damage only from attackers within <paramref name="Radius"/> of its spawn
        /// (<paramref name="FromSpawn"/>) or of itself, while <paramref name="Switch"/> is on for it (null: always).</summary>
        public sealed record AttackRule(string Boss, int Radius, bool FromSpawn, string Switch);

        /// <summary>The bosses' close-range rules (scripts: Conservator.TakeDamage 800 from spawn; Easmarach.TakeDamage his tether
        /// from spawn, 2,500; Legion.TakeDamage 1,400 from him, always on).</summary>
        public static readonly List<AttackRule> AttackRules =
        [
            new("Conservator", 800, true, nameof(EpicEncounterMechanics.AllowPositionPuzzle)),
            new("Easmarach", 2_500, true, nameof(EpicEncounterMechanics.AllowPositionPuzzle)),
            new("Legion", 1_400, false, null),
        ];

        /// <summary>Two spots a raider must stand on at once, each beside one of the encounter's bodies.</summary>
        public sealed record SafeSpots(string Boss, string[] Bodies, Vector3[] Spots, int Radius, string Switch);

        /// <summary>Olcasgean (OlcasgeanAndPrimals.cs, OlcasgeanBrain.Think): unless a raider stands within 200 of 40,170,62,600
        /// (where his copy stands) and one within 200 of 39,237,62,644 (where he stands), he casts his Root DD without pause.</summary>
        public static readonly List<SafeSpots> SafeSpotRules =
        [
            new("Olcasgean", ["Olcasgean2", "Olcasgean"], [new(40170, 62600, 11681), new(39237, 62644, 11685)], 200,
                nameof(EpicEncounterMechanics.AllowPositionPuzzle)),
        ];

        /// <summary>Bosses that heal by each raider they kill (Legion 2.5%): healers near them heal anyone under
        /// <see cref="KillHealPatientPercent"/> first.</summary>
        public static readonly List<string> KillHealBosses = ["Legion", "HighLordSaeor"];
        public const int KillHealPatientPercent = 50;

        /// <summary>The raiders of the encounter around <paramref name="boss"/> whose parties the safe spots are given to (tests
        /// replace it; the game: the bots among the NPCs near the boss, plus the raider asking).</summary>
        public static Func<GameNPC, IReadOnlyList<GameNPC>, IEnumerable<GameLiving>> RaidersNear = (boss, live) =>
            live.OfType<GameBot>().Where(b => b.IsAlive && b.CurrentRegionID == boss.CurrentRegionID && boss.IsWithinRadius(b, 5_000));

        private static bool RuleOn(string switchName, string boss) =>
            string.IsNullOrEmpty(switchName) || EpicEncounterMechanics.On(switchName, boss);

        private static bool AtRange(bool canShootFlyers, int classId) =>
            canShootFlyers || classId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)classId);

        /// <summary>Some wave-2 rule may apply to this raider and target (TryHandle asks it before skipping a target with no
        /// catalog row: a dragon's add while the dragon stares at someone).</summary>
        public static bool HasWaveTwoRule(GameLiving raider, GameNPC target, long now) =>
            target != null && (AttackRules.Any(r => r.Boss == target.GetType().Name) ||
                               SafeSpotRules.Any(r => r.Bodies.Contains(target.GetType().Name)) ||
                               NearSignals(raider, now).Length > 0);

        private static EncounterSignal[] NearSignals(GameLiving raider, long now) =>
            raider == null ? [] : EncounterSignals.AllActive(now).Where(s => s.Boss.CurrentRegionID == raider.CurrentRegionID &&
                s.Boss.IsWithinRadius(raider, SignalReach)).ToArray();

        /// <summary>The wave-2 rules, in order; null: none applies (Decide goes on with the catalog's rules).</summary>
        private static Advice? DecideWaveTwo(GameLiving raider, GameNPC target, IReadOnlyList<GameNPC> live, int partyIndex,
            bool canShootFlyers, long now, int classId)
        {
            Vector3 at = new(raider.X, raider.Y, raider.Z);
            foreach (EncounterSignal s in NearSignals(raider, now))
                if (SignalStep(raider, at, s, canShootFlyers, classId) is Vector3 clear)
                    return new(BotAction.Reposition, target, clear,
                        $"{s.Mechanic} of \"{s.Boss.Name}\": out of {s.Radius}" + (s.AroundTarget ? $" around \"{s.Target?.Name}\"" : ""));
            if (SafeSpotAdvice(raider, target, live, partyIndex, now) is Advice spot) return spot;
            if (AttackFromWithin(raider, target, at) is Vector3 inside)
                return new(BotAction.Reposition, target, inside, $"close range: \"{target.Name}\" takes damage only from within reach");
            // Galladoria's Fire primal is a dungeon rule (EpicEncounterTactics.Galladoria.cs), asked right after these.
            return null;
        }

        /// <summary>
        /// Where a raider at <paramref name="at"/> steps to for signal <paramref name="s"/>, or null when it stays: a glare's
        /// own raider and the one the boss fights stay (the tank holds the boss); everyone else within its circle steps out,
        /// away from the picked raider. Around the boss, only those who fight at range step out.
        /// </summary>
        public static Vector3? SignalStep(GameLiving raider, Vector3 at, EncounterSignal s, bool canShootFlyers, int classId)
        {
            if (s == null || s.Radius <= 0) return null;
            Vector3 center = s.Center;
            Vector2 flat = new(at.X, at.Y), c = new(center.X, center.Y);
            float d = Vector2.Distance(flat, c);
            if (d > s.Radius + SignalMargin) return null;
            if (s.AroundTarget)
            {
                if (s.Target == null || raider == s.Target || raider == s.Boss.TargetObject) return null;
            }
            else if (Array.IndexOf(StepOutAroundBoss, s.Mechanic) < 0 || !AtRange(canShootFlyers, classId)) return null;
            Vector2 away = d >= 1 ? Vector2.Normalize(flat - c)
                : Vector2.Distance(new(s.Boss.X, s.Boss.Y), c) >= 1 ? Vector2.Normalize(c - new Vector2(s.Boss.X, s.Boss.Y)) : new(1, 0);
            Vector2 to = c + away * (s.Radius + SignalStepOut);
            return new Vector3(to.X, to.Y, at.Z);
        }

        // ---- attack from within -------------------------------------------------------------------------------------

        /// <summary>A raider farther than the radius less this from the rule's centre steps in to the radius less
        /// <see cref="AttackStepIn"/>.</summary>
        public const int AttackMargin = 150, AttackStepIn = 300;

        public static AttackRule AttackRuleFor(GameNPC boss) => boss == null ? null :
            AttackRules.FirstOrDefault(r => r.Boss == boss.GetType().Name && RuleOn(r.Switch, r.Boss));

        /// <summary>Where a raider at <paramref name="at"/> steps in to so its hits count (pure), or null when it is close enough.</summary>
        public static Vector3? StepIn(Vector3 at, Vector3 center, int radius)
        {
            Vector2 flat = new(at.X, at.Y), c = new(center.X, center.Y);
            float d = Vector2.Distance(flat, c);
            if (radius <= 0 || d <= radius - AttackMargin) return null;
            Vector2 to = c + Vector2.Normalize(flat - c) * Math.Max(0, radius - AttackStepIn);
            return new Vector3(to.X, to.Y, at.Z);
        }

        private static Vector3? AttackFromWithin(GameLiving raider, GameNPC target, Vector3 at)
        {
            if (AttackRuleFor(target) is not { } rule) return null;
            // A wounded raider stepping out of Legion's reach is not called back in (EpicEncounterTactics.DarknessFalls.cs).
            if (target is DOL.GS.Scripts.Legion && LegionWoundedAdvice(at, raider.HealthPercent, target.TargetObject == raider, target) != null)
                return null;
            Vector3 center = rule.FromSpawn ? new(target.SpawnPoint.X, target.SpawnPoint.Y, target.SpawnPoint.Z) : new(target.X, target.Y, target.Z);
            // A boss drawn away from its spawn is led home by the fight-at-home rule; melee beside it are not called back.
            if (rule.FromSpawn && Vector2.Distance(new(target.X, target.Y), new(center.X, center.Y)) > rule.Radius) return null;
            return StepIn(at, center, rule.Radius);
        }

        // ---- safe spots ---------------------------------------------------------------------------------------------

        public static SafeSpots SafeSpotsFor(GameNPC body) => body == null ? null :
            SafeSpotRules.FirstOrDefault(r => r.Bodies.Contains(body.GetType().Name) && RuleOn(r.Switch, r.Boss));

        /// <summary>Which spot a party holds (pure): the two lowest party indexes of the raid hold spot 0 and 1; with one
        /// party, its members alternate by <paramref name="memberId"/>; -1: it fights where it likes.</summary>
        public static int SpotOf(int partyIndex, int memberId, IEnumerable<int> raidParties)
        {
            int[] parties = raidParties.Append(partyIndex).Distinct().OrderBy(p => p).Take(2).ToArray();
            if (parties.Length == 1) return memberId & 1;
            return Array.IndexOf(parties, partyIndex);
        }

        private static Advice? SafeSpotAdvice(GameLiving raider, GameNPC target, IReadOnlyList<GameNPC> live, int partyIndex, long now)
        {
            if (SafeSpotsFor(target) is not { } rule) return null;
            GameNPC[] bodies = rule.Bodies.Select(b => live.FirstOrDefault(n => Present(n) && n.GetType().Name == b &&
                n.CurrentRegionID == target.CurrentRegionID)).ToArray();
            // Asleep (before the primals die) the spots mean nothing.
            if (!bodies.Any(b => b != null && (b.InCombat || b.TargetObject != null))) return null;
            IEnumerable<int> parties = RaidersNear(target, live).Where(r => r != raider).Select(PartyIndex);
            int spot = SpotOf(partyIndex, raider.ObjectID, parties);
            if (spot < 0 || spot >= rule.Spots.Length) return null;
            Vector3 place = rule.Spots[spot];
            GameNPC body = bodies[spot];
            if (Vector2.Distance(new(raider.X, raider.Y), new(place.X, place.Y)) > rule.Radius - 60)
                return new(BotAction.Reposition, body ?? target, place, $"safe spot {spot + 1} of \"{target.Name}\": hold it");
            if (body != null && body != target)
                return new(BotAction.Retarget, body, null, $"safe spot {spot + 1}: fight \"{body.Name}\" from it");
            return null;
        }

        // ---- healers ------------------------------------------------------------------------------------------------

        /// <summary>
        /// A healer takes <paramref name="candidate"/> over <paramref name="current"/> (pure): a marked raider (a glare's,
        /// one under a kill-heal boss below half, one inside a blast that lands) under <paramref name="belowPercent"/> comes
        /// first, unless the current pick is a lower emergency (under 40%).
        /// </summary>
        public static bool PreferMarked(int candidateHealth, int belowPercent, int? currentHealth) =>
            candidateHealth < belowPercent && (currentHealth is not int cur || !(cur < 40 && cur < candidateHealth));

        /// <summary>The raider a healer should heal first because of a mechanic, or null (BotBrain.CheckHeals asks).</summary>
        public static GameLiving SignalPatient(GameBot healer, GameLiving current, Func<GameLiving, bool> inReach)
        {
            if (healer == null) return null;
            long now = GameLoop.GameLoopTime;
            GameLiving best = null;
            int? bestHealth = current?.HealthPercent;
            void Consider(GameLiving living, int below)
            {
                if (living == null || living == current || !living.IsAlive || living.CurrentRegionID != healer.CurrentRegionID ||
                    living.Realm != healer.Realm || !healer.IsWithinRadius(living, 2_000) || inReach != null && !inReach(living)) return;
                if (!PreferMarked(living.HealthPercent, below, bestHealth)) return;
                best = living; bestHealth = living.HealthPercent;
            }
            foreach (EncounterSignal s in NearSignals(healer, now))
            {
                if (s.AroundTarget) Consider(s.Target, 100);
                else if (s.Mechanic == EncounterSignals.Stomp && healer.Group != null)
                    // Xanxicar's stomp: the party that holds him takes it; its healer tops it up first.
                    foreach (GameLiving m in healer.Group.GetMembersInTheGroup())
                        if (s.Boss.IsWithinRadius(m, s.Radius)) Consider(m, 100);
            }
            // Legion and High Lord Saeor are in Darkness Falls: only there is the look-up worth a healer's turn.
            if (healer.Group != null && healer.CurrentRegionID == AutonomousDarknessFallsPolicy.RegionId)
                foreach (GameNPC boss in healer.GetNPCsInRadius(3_000).Where(n => n.IsAlive && n.InCombat && KillHealBosses.Contains(n.GetType().Name)))
                    foreach (GameLiving m in healer.Group.GetMembersInTheGroup())
                        if (boss.IsWithinRadius(m, 2_000)) Consider(m, KillHealPatientPercent);
            return best;
        }
    }
}
