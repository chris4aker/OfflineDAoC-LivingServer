using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Errands (1.2, epic wave 2): a party of the raid sent away from the boss to do one job and come back, such as the team
    /// that goes down Jailer Vifil's tunnel to kill the two guardians at his jail, so every raider he jails walks free. An
    /// errand walks the party to its goal (fighting whatever attacks it on the way), clears what stands there, and walks back
    /// to the boss. No path there, no progress, or past its budget: the errand is dropped and the bot rejoins the fight.
    /// BotBrain asks <see cref="ThinkErrand"/> right after the displacement recovery, before any follow or rally order.
    /// Autonomous gamebots only. Also here: the party's calls for the dungeon rules' mechanics (<see cref="MechanicCall"/>).
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        public enum ErrandStage { Going, Clearing, Returning, Done }
        public enum ErrandOrder { Walk, Fight, Done }

        /// <summary>One errand's job: where it goes, what it clears there (the living ones), and the party's call.</summary>
        public sealed record ErrandJob(string Name, Vector3 Goal, Func<GameNPC, IEnumerable<GameNPC>> Targets, string Call = null);

        /// <summary>A bot's errand (pure state; <see cref="StepErrand"/> moves it on).</summary>
        public sealed class ErrandPlan
        {
            public ErrandJob Job;
            public GameNPC Boss;
            public ErrandStage Stage = ErrandStage.Going;
            public long Deadline;
            public long NextProgressCheck;
            public float DistanceAtCheck = float.MaxValue;
            public string Reason;
            /// <summary>The goal was cleared (the job is done, the party is on its way back or back).</summary>
            public bool Cleared;
            public Vector3 LastWalk;
        }

        /// <summary>What the bot sees this turn.</summary>
        public readonly record struct ErrandObservation(long Now, bool Alive, Vector3 Position, Vector3 Back, int TargetsLeft,
            bool UnderAttack, bool BossFighting);

        /// <summary>Within this of the goal the party clears; within <see cref="ErrandBackRadius"/> of the boss it is back.</summary>
        public const int ErrandGoalRadius = 700, ErrandBackRadius = 1_200;
        public const int ErrandProgressWindowMilliseconds = 12_000, ErrandMinimumProgress = 150;
        /// <summary>Clearing the goal may take this long.</summary>
        public const int ErrandClearMilliseconds = 150_000;

        /// <summary>The errand's budget for a walk of <paramref name="distance"/> units (as a displaced bot's walk back).</summary>
        public static long ErrandBudget(float distance) => EncounterDisplacementRecovery.WalkBudget(distance);

        /// <summary>The plan for an errand started at <paramref name="now"/> from <paramref name="from"/>.</summary>
        public static ErrandPlan StartErrandPlan(ErrandJob job, GameNPC boss, Vector3 from, long now)
        {
            float d = Vector2.Distance(new(from.X, from.Y), new(job.Goal.X, job.Goal.Y));
            return new ErrandPlan
            {
                Job = job, Boss = boss, Deadline = now + ErrandBudget(d), NextProgressCheck = now + ErrandProgressWindowMilliseconds,
                DistanceAtCheck = d,
            };
        }

        /// <summary>The errand's next move (pure).</summary>
        public static ErrandOrder StepErrand(ErrandPlan p, ErrandObservation o)
        {
            if (!o.Alive) return ErrandDone(p, "died");
            if (!o.BossFighting) return ErrandDone(p, "the fight is over");
            Vector2 at = new(o.Position.X, o.Position.Y);
            switch (p.Stage)
            {
                case ErrandStage.Going:
                {
                    float d = Vector2.Distance(at, new(p.Job.Goal.X, p.Job.Goal.Y));
                    if (d <= ErrandGoalRadius)
                    {
                        p.Stage = ErrandStage.Clearing;
                        p.Deadline = o.Now + ErrandClearMilliseconds;
                        return o.TargetsLeft > 0 || o.UnderAttack ? ErrandOrder.Fight : StepErrand(p, o);
                    }
                    if (o.Now >= p.Deadline) return ErrandDone(p, "the way there took too long");
                    if (o.UnderAttack) { RestartProgress(p, o.Now, d); return ErrandOrder.Fight; }
                    if (o.Now >= p.NextProgressCheck)
                    {
                        if (p.DistanceAtCheck - d < ErrandMinimumProgress) return ErrandDone(p, "no progress on the way");
                        RestartProgress(p, o.Now, d);
                    }
                    return ErrandOrder.Walk;
                }
                case ErrandStage.Clearing:
                    if (o.TargetsLeft > 0 || o.UnderAttack)
                        return o.Now >= p.Deadline ? ErrandDone(p, "could not clear it") : ErrandOrder.Fight;
                    p.Stage = ErrandStage.Returning;
                    float back = Vector2.Distance(at, new(o.Back.X, o.Back.Y));
                    p.Deadline = o.Now + ErrandBudget(back);
                    RestartProgress(p, o.Now, back);
                    p.Cleared = true;
                    return StepErrand(p, o);
                case ErrandStage.Returning:
                {
                    float d = Vector2.Distance(at, new(o.Back.X, o.Back.Y));
                    if (d <= ErrandBackRadius) return ErrandDone(p, "back at the fight");
                    if (o.Now >= p.Deadline) return ErrandDone(p, "the way back took too long");
                    if (o.UnderAttack) { RestartProgress(p, o.Now, d); return ErrandOrder.Fight; }
                    if (o.Now >= p.NextProgressCheck)
                    {
                        if (p.DistanceAtCheck - d < ErrandMinimumProgress) return ErrandDone(p, "no progress on the way back");
                        RestartProgress(p, o.Now, d);
                    }
                    return ErrandOrder.Walk;
                }
                default:
                    return ErrandOrder.Done;
            }
        }

        private static void RestartProgress(ErrandPlan p, long now, float distance)
        {
            p.NextProgressCheck = now + ErrandProgressWindowMilliseconds;
            p.DistanceAtCheck = distance;
        }

        private static ErrandOrder ErrandDone(ErrandPlan p, string reason)
        {
            p.Stage = ErrandStage.Done;
            p.Reason = reason;
            return ErrandOrder.Done;
        }

        // ---- the live registry -------------------------------------------------------------------------------------

        private static readonly ConcurrentDictionary<GameBot, ErrandPlan> Errands = new();

        /// <summary>The bot is on an errand now.</summary>
        public static bool OnErrand(GameBot bot) => bot != null && Errands.ContainsKey(bot);
        public static ErrandStage? ErrandStageOf(GameBot bot) => bot != null && Errands.TryGetValue(bot, out var p) ? p.Stage : null;
        public static void ForgetErrands() => Errands.Clear();

        /// <summary>Sends every autonomous bot of <paramref name="party"/> in the boss's region on the errand (those already on
        /// one keep it). Returns how many went.</summary>
        public static int StartErrand(Group party, GameNPC boss, ErrandJob job)
        {
            if (party == null || boss == null || job == null) return 0;
            int sent = 0;
            long now = GameLoop.GameLoopTime;
            foreach (GameLiving member in party.GetMembersInTheGroup())
            {
                if (member is not GameBot { IsAlive: true, IsAutonomousWorldBot: true } bot || bot.IsPlayerLedGroup ||
                    bot.CurrentRegionID != boss.CurrentRegionID || Errands.ContainsKey(bot))
                    continue;
                Errands[bot] = StartErrandPlan(job, boss, new Vector3(bot.X, bot.Y, bot.Z), now);
                (bot.Brain as BotBrain)?.RemoveFromAggroList(boss);
                if (bot.TargetObject == boss) { bot.StopAttack(); bot.TargetObject = null; }
                sent++;
            }
            if (sent > 0 && Log.IsInfoEnabled)
                Log.Info($"EPIC_TACTIC_ERRAND start job={job.Name} boss=\"{boss.Name}\" party=\"{party.LivingLeader?.Name}\" bots={sent}");
            return sent;
        }

        /// <summary>BotBrain's hook after the displacement recovery: true when the errand uses this turn.</summary>
        public static bool ThinkErrand(GameBot bot)
        {
            if (bot == null || Errands.IsEmpty || !Errands.TryGetValue(bot, out ErrandPlan p)) return false;
            if (bot.ObjectState != GameObject.eObjectState.Active || p.Boss == null || bot.CurrentRegionID != p.Boss.CurrentRegionID)
            {
                Errands.TryRemove(bot, out _);
                return false;
            }
            GameNPC[] left = (p.Job.Targets?.Invoke(p.Boss) ?? []).Where(n => Present(n)).ToArray();
            var o = new ErrandObservation(GameLoop.GameLoopTime, bot.IsAlive, new Vector3(bot.X, bot.Y, bot.Z),
                new Vector3(p.Boss.X, p.Boss.Y, p.Boss.Z), left.Length, ErrandUnderAttack(bot),
                p.Boss.IsAlive && !EpicEncounterMechanics.FightIsOver(p.Boss));
            switch (StepErrand(p, o))
            {
                case ErrandOrder.Walk:
                    Vector3 to = p.Stage == ErrandStage.Returning ? o.Back : p.Job.Goal;
                    if (bot.IsAttacking) bot.StopAttack();
                    if (!bot.IsMovingOnPath || Vector3.Distance(p.LastWalk, to) > 200)
                    {
                        p.LastWalk = to;
                        bot.PathTo(new Point3D((int)to.X, (int)to.Y, (int)to.Z), bot.MaxSpeed);
                    }
                    return true;
                case ErrandOrder.Fight:
                    // At the goal: the nearest thing to clear is the target, until it is dead.
                    if (p.Stage == ErrandStage.Clearing && left.Length > 0 &&
                        (bot.TargetObject is not GameLiving cur || !cur.IsAlive || Array.IndexOf(left, cur as GameNPC) < 0 && !ErrandUnderAttack(bot)))
                    {
                        GameNPC next = left.OrderBy(n => bot.GetDistanceTo(n)).First();
                        bot.TargetObject = next;
                        (bot.Brain as BotBrain)?.AddToAggroList(next, 1_000);
                    }
                    return false;
                default:
                    Errands.TryRemove(bot, out _);
                    if (Log.IsInfoEnabled)
                        Log.Info($"EPIC_TACTIC_ERRAND end job={p.Job.Name} bot=\"{bot.Name}\" reason=\"{p.Reason}\" cleared={p.Cleared} at={bot.X},{bot.Y},{bot.Z}");
                    return false;
            }
        }

        private static bool ErrandUnderAttack(GameBot bot)
        {
            if (!bot.InCombatInLast(3_000)) return false;
            var attackers = bot.attackComponent?.AttackerTracker?.Attackers;
            if (attackers == null) return false;
            foreach (GameLiving a in attackers)
                if (a != null && a.IsAlive && a.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(a, 900))
                    return true;
            return false;
        }

        // ---- the party's calls ---------------------------------------------------------------------------------------

        /// <summary>One call of a dungeon rule's topic per party this often (EncounterMechanicCalls books it).</summary>
        public const int MechanicCallGapMilliseconds = 45_000;

        public static bool MechanicCallDue(long lastSaid, long now) => lastSaid <= 0 || now - lastSaid >= MechanicCallGapMilliseconds;

        /// <summary>
        /// The party calls a mechanic in /group in the bot voice (combat_calls.lines, topic <paramref name="topic"/>; slots
        /// {boss} and {target}): only in a party with the real player, once per topic per party every
        /// <see cref="MechanicCallGapMilliseconds"/> (a rule's act calls it; a rule's decision names it in Advice.Call). The
        /// one speaker and booking is <see cref="EncounterMechanicCalls.Say"/>.
        /// </summary>
        internal static void MechanicCall(GameBot bot, string topic, GameNPC boss, GameObject other) =>
            EncounterMechanicCalls.Say(bot, topic, boss, other, MechanicCallGapMilliseconds);
    }
}
