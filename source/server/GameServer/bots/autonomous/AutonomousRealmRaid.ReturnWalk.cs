using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// A return wave walks in together (owner's decision, 2026-11-17: "Return waves clear together"). After a wave goes in at
    /// the way in (RealmRaidReturnWave.Next), its raiders no longer walk on to the raid one by one: they walk the cleared route
    /// as one group, step by step, wait for most of the wave at each step, kill what has come back on the walk before going
    /// on, and are helped when they are stuck. The rules are RealmRaidReturnWave's (pure); this is the raid's side of them.
    /// Tests only until seen in the owner's PC log (REALM_RAID_RETURN_WAVE step=...).
    /// </summary>
    public static partial class AutonomousRealmRaid
    {
        private sealed class ReturnWalk
        {
            public int Number;
            public readonly HashSet<long> Members = new();
            /// <summary>Parties of the main body sent back to meet the wave (RealmRaidReturnWave.Help.BodyMeets).</summary>
            public readonly HashSet<Group> Helpers = new();
            public long HelpersSince;
            /// <summary>Monsters the wave could not damage (RealmRaidReturnWave.GiveUpTarget): fought only if they attack.</summary>
            public readonly HashSet<GameNPC> SetAside = new();
            public Vector3[] Steps = [];
            public Vector3 Goal;
            public float WalkLength;
            public int Step, BestStep;
            public long Formed, StepSince, LastMove;
            public float Closest = float.MaxValue;
            public GameNPC Target;
            public int TargetHealth;
            public long TargetDamage;
            public int Kills, Helps, MostMembers;
            public bool StuckLogged;
            public View MemberView, HelperView;
            /// <summary>Raiders who joined far behind the wave and walk its cleared steps one at a time to catch up with it
            /// (RealmRaidReturnWave.JoinStep): the step each walks to now. Not counted in the wave's own "together".</summary>
            public readonly Dictionary<long, int> Catching = new();
            /// <summary>When the wave's step became its step, and its kills there (RealmRaidReturnWave.StillBlocks).</summary>
            public long StepEntered;
            public int StepKills;
            public bool CapLogged;
            public readonly Dictionary<int, View> CatchingViews = new();
        }

        /// <summary>The raid's destination moved this far: the wave's steps are worked out again.</summary>
        private const float WalkGoalMoved = 500f;

        /// <summary>Return waves walk this raid's route together (RealmRaidReturnWave.WalkTogether): an epic or raid dungeon's own
        /// route, while the raid is not fighting a gate on the way.</summary>
        private static bool WalksTogether(Raid raid) =>
            RealmRaidReturnWave.On && RealmRaidReturnWave.WalkTogether && raid.DungeonRoute is { WalksInWaves: true, Entrance: not null } &&
            raid.Transit == null;

        /// <summary>Under the raid lock: raiders who went in at the way in walk with the wave (a new one, or the one already
        /// walking: they follow it on the walk it has cleared).</summary>
        private static void JoinWalk(Raid raid, IReadOnlyList<GameBot> bots, long now)
        {
            if (bots.Count == 0) return;
            string id = raid.Definition.Id;
            ReturnWalk walk = raid.Walk;
            if (walk == null)
            {
                walk = raid.Walk = new ReturnWalk { Number = ++raid.WalksFormed, Formed = now, LastMove = now, StepEntered = now };
                foreach (GameBot b in bots) walk.Members.Add(b.DatabaseID);
                BuildSteps(raid, walk, now);
                walk.MostMembers = walk.Members.Count;
                Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=formed wave={walk.Number} members={walk.Members.Count} " +
                         $"steps={walk.Steps.Length} walk={(int)walk.WalkLength} goal=\"{raid.DungeonRoute.TargetName}\"");
                return;
            }
            // Owner's PC, build 453e01a, 2026-11-17: 82 who went in at Tuscaran's way in joined a wave at its step 35 of 49, which
            // no corridor reached from there. One far behind the wave walks its cleared steps from where it stands, one at a time.
            int joined = 0, behind = 0, from = int.MaxValue;
            foreach (GameBot b in bots)
            {
                if (!walk.Members.Add(b.DatabaseID)) continue;
                joined++;
                int step = RealmRaidReturnWave.JoinStep(walk.Steps, new Vector3(b.X, b.Y, b.Z),
                    b.CurrentRegionID == raid.DungeonRoute.Region, walk.Step);
                if (step >= walk.Step) continue;
                walk.Catching[b.DatabaseID] = step;
                behind++;
                from = Math.Min(from, step);
            }
            if (joined == 0) return;
            walk.MostMembers = Math.Max(walk.MostMembers, walk.Members.Count);
            Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=joined wave={walk.Number} joined={joined} members={walk.Members.Count} " +
                     $"at={walk.Step + 1}/{walk.Steps.Length} catching={behind}" + (behind > 0 ? $" from={from + 1}" : ""));
        }

        /// <summary>Under the raid lock: the wave's steps, from where raiders come in to where the raid's parties stand
        /// (RealmRaidReturnWave.WalkSteps). Built again when that moved; the wave goes on from the step nearest the one it
        /// was walking to, never back to the way in.</summary>
        private static void BuildSteps(Raid raid, ReturnWalk walk, long now)
        {
            RealmRaidDungeonRoute route = raid.DungeonRoute;
            Vector3 goal = route.Destination;
            Vector3[] steps = RealmRaidReturnWave.WalkSteps(route.ReturnWalk(), goal);
            int step = walk.Steps.Length > 0 ? RealmRaidReturnWave.Nearest(steps, walk.Steps[walk.Step]) : 0;
            // Raiders catching up go on from the new step nearest the one they were walking to, behind the wave.
            RealmRaidReturnWave.RemapCatching(walk.Steps, steps, walk.Catching, step);
            walk.Steps = steps;
            walk.Goal = goal;
            walk.Step = walk.BestStep = step;
            // A new step: what holds it is judged from now (RealmRaidReturnWave.StillBlocks).
            walk.StepEntered = now;
            walk.StepKills = 0;
            walk.CapLogged = false;
            walk.StepSince = 0;
            walk.Closest = float.MaxValue;
            float length = 0;
            for (int i = 1; i < steps.Length; i++) length += Vector3.Distance(steps[i - 1], steps[i]);
            walk.WalkLength = length;
            RefreshWalkViews(raid, walk);
        }

        private static void RefreshWalkViews(Raid raid, ReturnWalk walk)
        {
            RealmRaidDungeonRoute route = raid.DungeonRoute;
            Vector3 at = walk.Steps.Length == 0 ? route.Destination : walk.Steps[walk.Step];
            var camp = new AutonomousBotGroupCoordinator.SharedCamp($"realm-event-{raid.Definition.Id}", raid.Definition.Name,
                raid.Definition.Name, route.Region, (int)at.X, (int)at.Y, (int)at.Z, true, false, 50);
            walk.MemberView = new(raid.Definition.Id,
                $"RAID — walking back in with the return wave (step {walk.Step + 1} of {walk.Steps.Length})", camp, true);
            walk.HelperView = new(raid.Definition.Id, "RAID — going back to meet the return wave", camp, true);
            walk.CatchingViews.Clear();
        }

        /// <summary>Under the raid lock: the view of a raider catching up with the wave, to its own step.</summary>
        private static View CatchingView(Raid raid, ReturnWalk walk, int step)
        {
            if (walk.CatchingViews.TryGetValue(step, out View view)) return view;
            Vector3 at = walk.Steps[step];
            var camp = new AutonomousBotGroupCoordinator.SharedCamp($"realm-event-{raid.Definition.Id}", raid.Definition.Name,
                raid.Definition.Name, raid.DungeonRoute.Region, (int)at.X, (int)at.Y, (int)at.Z, true, false, 50);
            return walk.CatchingViews[step] = new(raid.Definition.Id,
                $"RAID — catching up with the return wave (step {step + 1} of {walk.Steps.Length}; the wave is at {walk.Step + 1})", camp, true);
        }

        /// <summary>Under the raid lock: the wave has no raider of its own left walking (all fell, reached the raid or met the
        /// main body) while some still catch up behind it: they are the wave now, from the step of the furthest of them.</summary>
        private static bool RebaseOnCatching(Raid raid, ReturnWalk walk, long now, string why)
        {
            walk.Catching.Keys.Where(m => !walk.Members.Contains(m)).ToList().ForEach(m => walk.Catching.Remove(m));
            int to = RealmRaidReturnWave.Rebase(walk.Catching);
            if (to < 0) return false;
            // BestStep is kept: walking these steps again is no headway on the stall clock.
            walk.Step = to;
            walk.StepEntered = now;
            walk.StepKills = 0;
            walk.StepSince = 0;
            walk.Closest = float.MaxValue;
            walk.Target = null;
            walk.Helpers.Clear();
            walk.LastMove = now;
            RefreshWalkViews(raid, walk);
            Log.Info($"REALM_RAID_RETURN_WAVE event={raid.Definition.Id} step=rebased wave={walk.Number} at={walk.Step + 1}/{walk.Steps.Length} " +
                     $"members={walk.Members.Count} catching={walk.Catching.Count} reason=\"{why}; the raiders catching up are the wave now\"");
            return true;
        }

        /// <summary>Under the raid lock: the view of a raider walking with the wave, or of one of the main body sent back to meet
        /// it; null for everyone else.</summary>
        private static View WalkView(Raid raid, Group group, GameBot bot)
        {
            ReturnWalk walk = raid.Walk;
            if (walk == null || !bot.IsAlive) return null;
            if (walk.Members.Contains(bot.DatabaseID))
                return walk.Catching.TryGetValue(bot.DatabaseID, out int step) && step < walk.Steps.Length
                    ? CatchingView(raid, walk, step) : walk.MemberView;
            if (walk.Helpers.Contains(group) && bot.CurrentRegionID == raid.DungeonRoute.Region) return walk.HelperView;
            return null;
        }

        private static void WalkHeadway(Raid raid, ReturnWalk walk, long now)
        {
            walk.LastMove = now;
            walk.StuckLogged = false;
            raid.LastHeadwayTick = now;
        }

        private static void EndWalk(Raid raid)
        {
            raid.Walk = null;
        }

        /// <summary>
        /// Under the raid lock, each pulse of a started dungeon raid: the walking wave's turn (RealmRaidReturnWave.Walk). It waits
        /// at its step until most of it stands there, fights what is in its way, goes on, and has arrived at the raid; stuck, it is
        /// helped (RealmRaidReturnWave.ChooseHelp).
        /// </summary>
        private static void PulseReturnWalk(Raid raid, long now)
        {
            ReturnWalk walk = raid.Walk;
            if (walk == null) return;
            using var aiProfile = AiProfile.Scope("raid.return_wave_walk"); // AI_PROFILE: measures only
            RealmRaidDungeonRoute route = raid.DungeonRoute;
            string id = raid.Definition.Id;
            long minutes = (now - walk.Formed) / 60_000;
            if (!WalksTogether(raid) || route.Complete || route.Defeated)
            {
                Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=ended wave={walk.Number} kills={walk.Kills} minutes={minutes} " +
                         $"reason=\"{(raid.Transit != null ? "the raid fights a gate on its way" : route.Complete || route.Defeated ? "the route is over" : "walking together is off")}\"");
                EndWalk(raid);
                return;
            }
            var byId = new Dictionary<long, GameBot>();
            foreach (GameBot b in raid.Support.OfType<GameBot>()) byId[b.DatabaseID] = b;
            // One who fell is on its way back again (it releases and gathers at the way in for the next wave).
            walk.Members.RemoveWhere(m => !byId.TryGetValue(m, out GameBot b) || !b.IsAlive);
            GameBot[] members = walk.Members.Select(m => byId[m]).ToArray();
            if (members.Length == 0)
            {
                walk.Catching.Clear();
                Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=lost wave={walk.Number} most={walk.MostMembers} kills={walk.Kills} " +
                         $"minutes={minutes} reason=\"every raider of the wave fell; they gather at the way in again\"");
                EndWalk(raid);
                return;
            }
            if (walk.Steps.Length == 0 || Vector3.DistanceSquared(route.Destination, walk.Goal) > WalkGoalMoved * WalkGoalMoved)
                BuildSteps(raid, walk, now);
            ushort region = route.Region;
            // A raider of the wave already standing with the raid (moved there by its stranded regroup) is there: it leaves the walk.
            if (walk.Step < walk.Steps.Length - 1)
            {
                foreach (GameBot b in members.Where(b => b.CurrentRegionID == region &&
                             RealmRaidReturnWave.WithTheRaid(walk.Steps, walk.Step, new Vector3(b.X, b.Y, b.Z))))
                {
                    walk.Members.Remove(b.DatabaseID);
                    walk.Catching.Remove(b.DatabaseID);
                }
                if (walk.Members.Count < members.Length)
                {
                    Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=ahead wave={walk.Number} reached={members.Length - walk.Members.Count} " +
                             $"members={walk.Members.Count} at={walk.Step + 1}/{walk.Steps.Length} reason=\"already with the raid; they leave the walk\"");
                    members = walk.Members.Select(m => byId[m]).ToArray();
                    if (members.Length == 0) { EndWalk(raid); return; }
                }
            }
            // Raiders catching up go on to their next step when they stand at it (or at a later one); at the wave's step they
            // walk with it. Their walking is movement, not the wave's headway.
            foreach (long m in walk.Catching.Keys.ToArray())
            {
                if (!walk.Members.Contains(m)) { walk.Catching.Remove(m); continue; }
                GameBot b = byId[m];
                int latest = b.CurrentRegionID == region ? RealmRaidReturnWave.LatestStepAt(walk.Steps, new Vector3(b.X, b.Y, b.Z)) : -1;
                int was = walk.Catching[m], next = RealmRaidReturnWave.CatchUp(was, latest, walk.Step);
                if (next == was) continue;
                walk.LastMove = now;
                if (next < 0) walk.Catching.Remove(m);
                else walk.Catching[m] = next;
            }
            // The wave itself: its raiders not catching up. None left: the furthest catching up are the wave now.
            if (members.All(b => walk.Catching.ContainsKey(b.DatabaseID)))
                RebaseOnCatching(raid, walk, now, "no raider of the wave walks at its step");
            members = members.Where(b => !walk.Catching.ContainsKey(b.DatabaseID)).ToArray();
            Vector3 stepAt = walk.Steps[walk.Step];
            var stepPoint = new Point3D((int)stepAt.X, (int)stepAt.Y, (int)stepAt.Z);
            GameBot[] inside = members.Where(b => b.CurrentRegionID == region).ToArray();
            Vector3 middle = stepAt;
            if (inside.Length > 0)
            {
                middle = Vector3.Zero;
                foreach (GameBot b in inside) middle += new Vector3(b.X, b.Y, b.Z);
                middle /= inside.Length;
            }
            int atStep = inside.Count(b => b.IsWithinRadius(stepPoint, RealmRaidReturnWave.AtStepRadius));
            if (atStep > 0 && walk.StepSince == 0) walk.StepSince = now;
            long stepWaited = walk.StepSince == 0 ? 0 : now - walk.StepSince;
            float closest = inside.Length == 0 ? float.MaxValue : inside.Min(b => (float)b.GetDistanceTo(stepPoint));
            if (closest < walk.Closest - RealmRaidReturnWave.CloserUnits) { walk.Closest = closest; walk.LastMove = now; }

            // The main body sent back to meet the wave: met, gone back to an attempt, or out of time.
            bool raidFighting = route.InAttempt || route.NamedTarget?.InCombat == true;
            if (walk.Helpers.Count > 0 && (raidFighting || now - walk.HelpersSince >= RealmRaidReturnWave.HelpMilliseconds))
                walk.Helpers.Clear();
            if (walk.Helpers.Count > 0)
            {
                GameBot[] helpers = raid.Parties.Where(p => walk.Helpers.Contains(p.Key)).SelectMany(p => p.Value.Members)
                    .Where(b => b.IsAlive && b.CurrentRegionID == region && !walk.Members.Contains(b.DatabaseID)).ToArray();
                int near = helpers.Count(b => b.IsWithinRadius(stepPoint, (int)RealmRaidReturnWave.MeetRadius));
                if (RealmRaidReturnWave.Met(near, helpers.Length))
                {
                    WalkHeadway(raid, walk, now);
                    Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=met wave={walk.Number} helpers={helpers.Length} near={near} " +
                             $"living={members.Length} kills={walk.Kills} at={walk.Step + 1}/{walk.Steps.Length} minutes={minutes} " +
                             $"reason=\"the main body came back to meet it; they go on to the raid together\"");
                    foreach (GameBot b in members) walk.Members.Remove(b.DatabaseID);
                    if (!RebaseOnCatching(raid, walk, now, "the wave met the main body")) EndWalk(raid);
                    return;
                }
            }

            // The monster it fights: killed, damaged, or left when it can't be damaged.
            if (walk.Target != null)
            {
                GameNPC target = walk.Target;
                if (!target.IsAlive || target.ObjectState != GameObject.eObjectState.Active)
                {
                    walk.Kills++;
                    walk.StepKills++;
                    walk.Target = null;
                    WalkHeadway(raid, walk, now);
                }
                else if (target.Health < walk.TargetHealth)
                {
                    walk.TargetHealth = target.Health;
                    walk.TargetDamage = walk.LastMove = now;
                }
                else if (RealmRaidReturnWave.GiveUpTarget(walk.TargetDamage, now) ||
                         target.CurrentRegionID != region || !target.IsWithinRadius(new Point3D((int)middle.X, (int)middle.Y, (int)middle.Z),
                             (int)(RealmRaidReturnWave.LookAheadUnits * 1.5f)))
                {
                    walk.SetAside.Add(target);
                    walk.Target = null;
                    Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=left wave={walk.Number} target=\"{target.Name}\" " +
                             $"health={target.HealthPercent} at={target.X},{target.Y},{target.Z} " +
                             $"reason=\"no damage for {RealmRaidReturnWave.TargetGiveUpMilliseconds / 60_000} minutes or out of reach; fought if it attacks\"");
                }
            }
            walk.SetAside.RemoveWhere(n => !n.IsAlive);

            int idle = raid.Support.Count(b => b.IsAlive && b.CurrentRegionID == region && !b.InCombat &&
                                               b is GameBot g && !walk.Members.Contains(g.DatabaseID));
            bool bodyComing = walk.Helpers.Count > 0;
            bool bodyCanCome = RealmRaidReturnWave.BodyCanCome(idle, raidFighting);
            if (walk.Target != null) CallWave(walk, members, walk.Target);
            else
            {
                GameNPC ahead = InTheWay(raid, walk, middle, region);
                if (walk.StepKills >= RealmRaidReturnWave.StepKillCap && ahead == null && !walk.CapLogged)
                {
                    walk.CapLogged = true;
                    Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=passable wave={walk.Number} at={walk.Step + 1}/{walk.Steps.Length} " +
                             $"kills_at_step={walk.StepKills} reason=\"{RealmRaidReturnWave.StepKillCap} kills at this step; monsters off the walk no longer hold it\"");
                }
                bool last = walk.Step >= walk.Steps.Length - 1;
                switch (RealmRaidReturnWave.Walk(members.Length, atStep, stepWaited, ahead != null, last, bodyCanCome, bodyComing))
                {
                    case RealmRaidReturnWave.WaveMove.Fight:
                        walk.Target = ahead;
                        walk.TargetHealth = ahead.Health;
                        walk.TargetDamage = now;
                        int called = CallWave(walk, members, ahead);
                        Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=fighting wave={walk.Number} target=\"{ahead.Name}\" level={ahead.Level} " +
                                 $"at={ahead.X},{ahead.Y},{ahead.Z} respawned={ahead.SpawnTick > 0} " +
                                 $"living={members.Length} atStep={atStep} called={called} kills={walk.Kills} step_at={walk.Step + 1}/{walk.Steps.Length}");
                        break;
                    case RealmRaidReturnWave.WaveMove.CallHelp:
                        if (!bodyComing && SendHelpers(raid, walk, members.Length, stepPoint, now) is var (parties, helpers) && parties > 0)
                        {
                            walk.Helps++;
                            Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=helped wave={walk.Number} sent=0 parties={parties} helpers={helpers} " +
                                     $"living={members.Length} monster=\"{ahead.Name}\" at={walk.Step + 1}/{walk.Steps.Length} " +
                                     $"reason=\"too few to clear the monster in its way; the main body comes back to meet it\"");
                        }
                        break;
                    case RealmRaidReturnWave.WaveMove.Advance:
                        walk.Step++;
                        walk.StepEntered = now;
                        walk.StepKills = 0;
                        walk.CapLogged = false;
                        walk.StepSince = 0;
                        walk.Closest = float.MaxValue;
                        if (RealmRaidReturnWave.Headway(walk.Step, walk.BestStep)) { walk.BestStep = walk.Step; WalkHeadway(raid, walk, now); }
                        RefreshWalkViews(raid, walk);
                        Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=moving wave={walk.Number} at={walk.Step + 1}/{walk.Steps.Length} " +
                                 $"living={members.Length} together={atStep} kills={walk.Kills} minutes={minutes}");
                        break;
                    case RealmRaidReturnWave.WaveMove.Arrived:
                        WalkHeadway(raid, walk, now);
                        Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=cleared wave={walk.Number} living={members.Length} most={walk.MostMembers} " +
                                 $"kills={walk.Kills} steps={walk.Steps.Length} helps={walk.Helps} minutes={minutes} " +
                                 $"reason=\"the wave reached the raid together\"");
                        foreach (GameBot b in members) walk.Members.Remove(b.DatabaseID);
                        if (!RebaseOnCatching(raid, walk, now, "the wave reached the raid")) EndWalk(raid);
                        return;
                }
            }

            if (!RealmRaidReturnWave.Stuck(walk.LastMove, now)) return;
            RealmRaidReturnWave.Help help = RealmRaidReturnWave.ChooseHelp(raid.ReturnWaitingNow, bodyCanCome && !bodyComing, walk.Helps);
            if (help == RealmRaidReturnWave.Help.None)
            {
                if (!walk.StuckLogged)
                    Log.Warn($"REALM_RAID_RETURN_WAVE event={id} step=stuck wave={walk.Number} at={walk.Step + 1}/{walk.Steps.Length} " +
                             $"living={members.Length} atStep={atStep} inside={inside.Length} kills={walk.Kills} helps={walk.Helps} " +
                             $"middle={(int)middle.X},{(int)middle.Y},{(int)middle.Z} " +
                             $"reason=\"no headway for {RealmRaidReturnWave.StuckMilliseconds / 60_000} minutes and no help left; the stall clock decides\"");
                walk.StuckLogged = true;
                walk.LastMove = now;
                return;
            }
            Log.Warn($"REALM_RAID_RETURN_WAVE event={id} step=stuck wave={walk.Number} at={walk.Step + 1}/{walk.Steps.Length} " +
                     $"living={members.Length} atStep={atStep} inside={inside.Length} kills={walk.Kills} helps={walk.Helps} " +
                     $"middle={(int)middle.X},{(int)middle.Y},{(int)middle.Z} target=\"{walk.Target?.Name}\" " +
                     $"reason=\"no headway for {RealmRaidReturnWave.StuckMilliseconds / 60_000} minutes\"");
            walk.Helps++;
            walk.LastMove = now;
            int sent = 0, sentParties = 0, sentHelpers = 0;
            if ((help & RealmRaidReturnWave.Help.SendWaiting) != 0)
            {
                // The raiders waiting at the way in go after it now; they join it on the walk it has cleared (PulseReturnWave).
                sent = raid.ReturnWaitingNow;
                raid.ReturnGathering = false;
                raid.ReturnWaitingSince = 0;
                raid.ReturnOpenUntil = now + RealmRaidReturnWave.OpenMilliseconds;
            }
            if ((help & RealmRaidReturnWave.Help.BodyMeets) != 0)
                (sentParties, sentHelpers) = SendHelpers(raid, walk, members.Length, stepPoint, now);
            Log.Info($"REALM_RAID_RETURN_WAVE event={id} step=helped wave={walk.Number} sent={sent} parties={sentParties} helpers={sentHelpers} " +
                     $"living={members.Length} helps={walk.Helps} of={RealmRaidReturnWave.MaximumHelps} " +
                     $"reason=\"{(sent > 0 ? "the raiders waiting at the way in go after it" : "")}{(sent > 0 && sentParties > 0 ? "; " : "")}{(sentParties > 0 ? "the main body comes back to meet it" : "")}\"");
        }

        /// <summary>Under the raid lock: whole idle parties of the main body, nearest the wave first, come back to meet it
        /// (RealmRaidReturnWave.HelpersWanted).</summary>
        private static (int Parties, int Helpers) SendHelpers(Raid raid, ReturnWalk walk, int waveLiving, Point3D at, long now)
        {
            ushort region = raid.DungeonRoute.Region;
            int wanted = RealmRaidReturnWave.HelpersWanted(waveLiving), helpers = 0, parties = 0;
            var candidates = raid.Parties
                .Select(p => (Group: p.Key, Idle: p.Value.Members.Where(b => b.IsAlive && b.CurrentRegionID == region && !b.InCombat &&
                    !walk.Members.Contains(b.DatabaseID)).ToArray()))
                .Where(p => p.Idle.Length > 0 && !walk.Helpers.Contains(p.Group))
                .OrderBy(p => p.Idle.Min(b => b.GetDistanceTo(at)));
            foreach (var (group, idle) in candidates)
            {
                if (helpers >= wanted) break;
                walk.Helpers.Add(group);
                helpers += idle.Length;
                parties++;
            }
            if (parties > 0) walk.HelpersSince = now;
            return (parties, helpers);
        }

        /// <summary>Under the raid lock: the wave's living raiders near its monster attack it (as the route's target call does:
        /// ordinary combat, healing and group defense take it from there). Raiders already fighting something else keep at it.</summary>
        private static int CallWave(ReturnWalk walk, GameBot[] members, GameNPC target)
        {
            int called = 0;
            foreach (GameBot bot in members)
            {
                if (!bot.IsAlive || bot.CurrentRegionID != target.CurrentRegionID || bot.TargetObject == target ||
                    !bot.IsWithinRadius(target, RealmRaidReturnWave.CallRadius) || bot.Brain is not BotBrain brain ||
                    (bot.InCombat || bot.IsAttacking) && bot.TargetObject is GameLiving { IsAlive: true } ||
                    !GameServer.ServerRules.IsAllowedToAttack(bot, target, true))
                    continue;
                bot.TargetObject = target;
                brain.AddToAggroList(target, 1);
                brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                brain.NextThinkTick = GameLoop.GameLoopTime;
                called++;
            }
            return called;
        }

        /// <summary>Under the raid lock: the monster in the wave's way nearest its middle (RealmRaidReturnWave.InTheWay), or null.
        /// Ordinary monsters only: named, bosses, an encounter's script adds and anything near a living named are the raid's.</summary>
        private static GameNPC InTheWay(Raid raid, ReturnWalk walk, Vector3 middle, ushort region)
        {
            Vector3[] legs = walk.Steps[Math.Max(0, walk.Step - 1)..Math.Min(walk.Steps.Length, walk.Step + 2)];
            float look = RealmRaidReturnWave.LookAheadUnits;
            List<GameNPC> near = WorldMgr.GetNPCsCloseToSpot(region, (int)middle.X, (int)middle.Y, (int)middle.Z,
                (ushort)(look + RealmRaidReturnWave.EpicClearance));
            GameNPC[] finals = raid.DungeonRoute.FinalBosses;
            GameNPC[] named = near.Where(n => n.IsAlive && (n is IGameEpicNpc || finals.Contains(n))).ToArray();
            GameNPC best = null;
            float bestDistance = float.MaxValue;
            foreach (GameNPC n in near)
            {
                if (!IsWalkTrash(n) || finals.Contains(n)) continue;
                Vector3 at = new(n.X, n.Y, n.Z);
                float distance = Vector2.Distance(new(at.X, at.Y), new(middle.X, middle.Y));
                if (distance > look) continue;
                int aggro = n.Brain is StandardMobBrain mob ? mob.AggroRange : 0;
                bool byNamed = named.Any(e => e.IsWithinRadius(n, (int)RealmRaidReturnWave.EpicClearance));
                bool onWalk = RealmRaidDungeonRoute.OnWalkBack(legs, at, aggro);
                if (!RealmRaidReturnWave.InTheWay(byNamed, walk.SetAside.Contains(n), onWalk, aggro > 0, distance, at.Z - middle.Z)) continue;
                // Clear what blocks the way when the wave reached this step, then go on: no farming what comes back (Tur Suil).
                bool attacking = n.InCombat && n.TargetObject is GameBot victim && walk.Members.Contains(victim.DatabaseID);
                if (!RealmRaidReturnWave.StillBlocks(attacking, n.SpawnTick > walk.StepEntered, walk.StepKills, onWalk, distance, aggro)) continue;
                if (distance < bestDistance) { best = n; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>An ordinary dungeon monster a wave may fight (the route's own candidate rules, without the named).</summary>
        private static bool IsWalkTrash(GameNPC n) =>
            n.IsAlive && n.ObjectState == GameObject.eObjectState.Active && n.Realm == eRealm.None && n.Level > 0 &&
            n is not GameBot && n is not GameSummonedPet && n.Brain is not IControlledBrain && n is not IGameEpicNpc &&
            n is not GameMerchant && n is not GameTrainer && n is not GameTeleporter && n is not GameTaxi &&
            (n.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0 && !n.LoadedFromScript &&
            n.TempProperties.GetProperty<bool>(AutonomousPetSupport.SyntheticCharmPetProperty) != true;
    }
}
