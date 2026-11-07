using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>One bounded route planner per expedition, not one full-world scan per member.</summary>
    public sealed class RealmRaidDungeonRoute
    {
        private readonly ushort _region;
        private readonly string[] _finalTypes;
        private readonly Vector3 _trigger;
        private readonly string[] _objectives;
        private readonly eRealm _realm;
        // Darkness Falls: certified waypoint chain from the realm's entrance to a named encounter.
        private Vector3[] _chain;
        private int _chainIndex;
        private GameNPC _chainTarget;
        private readonly HashSet<GameNPC> _cleared = new();
        // Named encounters defeated this expedition, by name: several scripted bosses come back as new objects (Lich Lord
        // Ilron, Lich Lord Sanguis, Spindler Broodmother were each fought twice in run 10).
        private readonly HashSet<string> _clearedNames = new(StringComparer.Ordinal);
        private long _routeStartTick;
        /// <summary>
        /// An epic dungeon raid clears every monster for its first hour, then goes for its named encounters and the
        /// final one only (monsters on the way are fought when they attack). With trash coming back the list never
        /// emptied: Caer Sidi's raid killed Soul Reckoner, Host and Dorinakka and then cleared custodians and crypt
        /// knights for two hours, never reaching Apocalypse (run 10, 2026-10-15).
        /// </summary>
        public const long EpicTrashPhaseMilliseconds = 60 * 60_000;

        // Run 14 (2026-10-16): Galladoria's Corrupt Primal of Earth took no damage for five minutes from 30 raiders and was
        // skipped: its four Earthmenders (script-spawned, not named) healed it, and after the first hour the raid only
        // targets named encounters. Adds a script spawns beside the front are part of the fight; they come first.
        public const float EncounterAddRadius = 3_000f;
        // Only while the encounter that spawned it is still alive nearby. Run 17 (2026-10-17): Lich Lord Sanguis's blood
        // mages kept spawning after he died, the raid farmed them at the old front for 40 minutes and never went on to
        // Apocalypse.
        public static bool IsEncounterAdd(GameNPC npc, Vector3 front, IReadOnlyCollection<GameNPC> livingEncounters) =>
            npc.LoadedFromScript && npc is not IGameEpicNpc &&
            Vector3.DistanceSquared(front, new(npc.X, npc.Y, npc.Z)) <= EncounterAddRadius * EncounterAddRadius &&
            (livingEncounters.Any(boss => boss.CurrentRegionID == npc.CurrentRegionID && boss.IsWithinRadius(npc, (int)EncounterAddRadius)) ||
             // A boss's death burst (Spindler Broodmother's brood) is cleared at its body although the boss is dead.
             EpicEncounterTactics.IsDeathAdd(npc));
        private readonly Dictionary<GameNPC, long> _routeRetry = new();
        private readonly Dictionary<string, GameNPC> _finals = new();
        private GameNPC _target;
        private int _probeCursor;
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private long _targetProgressTick, _targetStartTick, _holdSince, _nextHoldLog;
        // Lowest health points seen (not percent: a percent of Galladoria's 125,000-health Corrupt Primal of Fire is 1,250
        // points, and a few raiders wearing him down counted as no damage and he was skipped, run 23).
        private int _targetLowestHealth;
        // Health at the last look: a boss that heals itself (Easmarach takes an eighth back on the waterfall top each time he
        // falls to 30%) never goes below its earlier lowest while the raid wears him down from 42% to 35%, and was skipped
        // "no damage for five minutes" with the raid hitting him (play test, 2026-11-07).
        private int _targetLastHealth;

        /// <summary>The target took damage since the last look (pure): below its lowest yet, or below the last look's health
        /// (a boss that healed itself being worn down again: Easmarach's eighth on the waterfall top, Grand Summoner Govannon's
        /// full heal when he turns demon at 50%, Funus's full heal from a stray hit). "No damage for five minutes" means that:
        /// a target the raid keeps hurting is fought on (a fight it can't win ends in a wipe or the raid's time-out).</summary>
        public static bool TargetTookDamage(int health, int lowest, int last) => health < lowest || last > 0 && health < last;
        // Final encounter muster (see MusterForFinal).
        private GameNPC _musterTarget;
        private Vector3 _musterPoint;
        private long _musterSince, _nextMusterLog, _musterEngagedTick;
        private bool _musterEngaged;
        // Nobody stood at the muster since this tick (0 = someone is there).
        private long _musterEmptySince;
        private bool _musterUnreachable;
        private readonly PullContact _pulls = new();
        // Early pulls: when each encounter's was last logged, and how many went to the debug log since the last one logged.
        private readonly Dictionary<string, long> _earlyPullLogged = new(StringComparer.Ordinal);
        private int _quietEarlyPulls;
        // Per encounter, kept while the raid turns to another one and back (Krondon's Beran and Scurceol, 2026-11-05).
        private readonly Dictionary<GameNPC, MusterProgress> _musterProgress = new();
        // The log line of the reason a muster set its encounter aside (pull unreached, stalled); null for an empty muster.
        private string _setAsideLine;
        /// <summary>This raid has a Leeroy (AutonomousRaidLeeroy): he charges the first final encounter early, once.</summary>
        public bool LeeroyRolled { get; set; }
        /// <summary>
        /// A play test's focus (/epictest): the names or class names of the only encounters this route goes for (and the adds
        /// around them), with no first-hour trash clear; null for every real raid.
        /// </summary>
        public string[] Focus { get; set; }
        /// <summary>The route goes for this monster under its play-test focus (pure; name or class name).</summary>
        public static bool InFocus(string[] focus, string name, string typeName) =>
            focus != null && (Array.IndexOf(focus, name) >= 0 || Array.IndexOf(focus, typeName) >= 0);
        private bool _leeroyDone;
        /// <summary>The raider who charged, or null.</summary>
        public string LeeroyName { get; private set; }

        /// <summary>
        /// 2026-10-20: Tuscaran Glacier's raid held "Mustering for Elder Council Birghir (0/xx)" for an hour while Elder
        /// Icelord Suttung, beside the way there, killed 115 of its raiders; it never fought back and timed out. A raid
        /// now turns on an epic encounter that is fighting its raiders, and gives up a muster nobody has reached for
        /// <see cref="MusterUnreachableMilliseconds"/> (the encounter is set aside and tried again later).
        /// </summary>
        public const long MusterUnreachableMilliseconds = 20 * 60_000;
        public static bool MusterUnreachable(long emptySince, long now) =>
            emptySince > 0 && now - emptySince >= MusterUnreachableMilliseconds;

        /// <summary>
        /// Marfach Caverns, 2026-11-05: the raid mustered 36-76 for the Ancient Black Oak and was sent in 91 times in 3.5
        /// hours; the Oak never entered combat and no raider came within 1,000 units of it (228 one-minute samples), so
        /// every pull ended "left combat" without a wipe, the muster refilled at once and the next pull went out. Nothing
        /// set it aside (the muster was never empty, waiting is not a stall) and the raid timed out with one named kill.
        /// A pull after which the encounter never entered combat reached nobody; after this many in a row the
        /// encounter is set aside like an unreachable muster and tried again later.
        /// </summary>
        public const int MaximumPullsWithoutContact = 3;
        public static bool PullsNeverReached(int pullsWithoutContact) => pullsWithoutContact >= MaximumPullsWithoutContact;

        /// <summary>Whether the raid's pulls on one encounter reach it (<see cref="MaximumPullsWithoutContact"/>).</summary>
        /// <summary>
        /// Krondon, 2026-11-05 (build 8d7baa3): the raid mustered for Beran the Supply Master for over an hour with 2-5 of
        /// the 50 it needed while 70-77 of its 101 stood 4,000 units away in a fight with Scurceol Hyrde (immune while his
        /// Lyft Miht orbs live; 42 raiders killed by him). The muster was never empty, so it was never set aside, and every
        /// 15 minutes the raid turned on Scurceol again for five minutes and came back to a fresh muster clock. A muster
        /// that has not gained a raider for this long, and still has fewer than it needs, is set aside; the clock follows
        /// the encounter across those interruptions.
        /// </summary>
        public const long MusterStalledMilliseconds = 20 * 60_000;

        /// <summary>How far one encounter's muster has got: its best count and since when.</summary>
        public sealed class MusterProgress
        {
            public int Best { get; private set; } = -1;
            public long BestSince { get; private set; }
            /// <summary>The muster now has <paramref name="mustered"/> of <paramref name="needed"/>; true when it has stalled
            /// (short of what it needs, and no better than its best for <see cref="MusterStalledMilliseconds"/>).</summary>
            public bool Observe(int mustered, int needed, long now)
            {
                if (mustered > Best) { Best = mustered; BestSince = now; }
                return mustered < needed && now - BestSince >= MusterStalledMilliseconds;
            }
        }

        public sealed class PullContact
        {
            /// <summary>The current pull brought the encounter into combat.</summary>
            public bool Contact { get; private set; }
            /// <summary>Pulls in a row that ended without the encounter ever entering combat.</summary>
            public int PullsWithoutContact { get; private set; }
            /// <summary>A new encounter: nothing counted yet.</summary>
            public void Reset() { Contact = false; PullsWithoutContact = 0; }
            /// <summary>A pull goes in.</summary>
            public void Begin() => Contact = false;
            /// <summary>The encounter is in combat with the raid.</summary>
            public void SawCombat() { Contact = true; PullsWithoutContact = 0; }
            /// <summary>The pull ended (the encounter is out of combat); true when the encounter is to be set aside. A wipe is
            /// a failed attempt, never a pull that reached nobody.</summary>
            public bool Ended(bool wiped)
            {
                if (wiped || Contact) return false;
                return PullsNeverReached(++PullsWithoutContact);
            }
        }

        /// <summary>
        /// Owner 2026-10-19: epic encounters stay at full strength ("high stakes and wow factor") but a wipe is not
        /// the end. Run 25: Galladoria's Conservator (level 77) went to 45%, wiped the raid and reset to 100%; the
        /// second try reached 42% and he was skipped with nothing to show for it. Now a wipe on a named encounter is
        /// a failed attempt: the raid regroups (the fallen release to the hub and walk back, the living rest and
        /// rebuff), then goes in again together. After <see cref="MaximumFailedAttempts"/> failed attempts on the
        /// same encounter the raid is defeated and goes home; the named encounters it killed still count.
        /// </summary>
        public const int MaximumFailedAttempts = 2;
        // 2026-10-20: Galladoria's raid went back at the Organic-Energy Mechanism five minutes after 152 of 156 fell and did
        // worse (47% against 31%); a regroup now waits for 70% of the roster and at least five minutes.
        public const long RegroupMinimumMilliseconds = 5 * 60_000;
        public const double RegroupShare = 0.7, RegroupPatientShare = 0.4;
        private readonly Dictionary<string, int> _failedAttempts = new(StringComparer.Ordinal);
        // Everyone who stood in the current attempt (raiders arriving during the fight join it).
        private readonly HashSet<GameLiving> _engaged = new();
        private int _attemptLowestPercent = 100;
        private bool _regrouping;
        private long _regroupSince;
        private readonly Queue<(string Name, int Failures)> _wipes = new();
        /// <summary>The encounter that defeated the raid (two failed attempts); null while it can still win.</summary>
        public string DefeatedAt { get; private set; }
        public bool Defeated => DefeatedAt != null;
        /// <summary>Named encounters this expedition killed (partial success when it is defeated later).</summary>
        public IReadOnlyCollection<string> NamedDefeated => _clearedNames;
        public int FailedAttempts(string name) => _failedAttempts.GetValueOrDefault(name);

        /// <summary>
        /// An attempt on a named encounter failed when at least eight raiders were in it and half of them fell (dead,
        /// released or gone from the dungeon), or when the encounter healed back to 95% after being taken to 85% or
        /// lower (it reset: nobody was left fighting it).
        /// </summary>
        public static bool IsWipe(int engaged, int fallen, int healthPercent, int lowestPercent) =>
            engaged >= MinimumRaidersAtTarget && (fallen * 2 >= engaged || healthPercent >= 95 && lowestPercent <= 85);

        /// <summary>
        /// A failed attempt counts against the raid (a wipe: one of its two tries, Special Forces called) only when it was the
        /// raid's: at least half the raid was in it, or half the raid fell. Aidon the Archwizard, 2026-11-05: 31 raiders were
        /// drawn into a fight before the muster and all fell, while 56 others were elsewhere; that was counted as the raid's
        /// first wipe. Now it is a lost skirmish: the raid regroups and rests, but no attempt is used.
        /// </summary>
        public static bool IsWipe(int engaged, int fallen, int healthPercent, int lowestPercent, int raidAtStart) =>
            IsWipe(engaged, fallen, healthPercent, lowestPercent) &&
            (raidAtStart <= 0 || engaged * 2 >= raidAtStart || fallen * 2 >= raidAtStart);

        /// <summary>Raiders a regroup waits for: 70% of the raid's roster (40% after 20 minutes), 8 to 150.</summary>
        public static int RegroupNeeded(int roster, long regroupMilliseconds) =>
            Math.Max(MinimumRaidersAtTarget, Math.Min(FinalMusterMaximum, (int)Math.Ceiling(roster *
                (regroupMilliseconds >= FinalMusterPatienceMilliseconds ? RegroupPatientShare : RegroupShare))));

        /// <summary>The next wipe the raid has not reacted to yet (announcement, reinforcements).</summary>
        public bool TryTakeWipe(out string name, out int failures)
        {
            if (_wipes.TryDequeue(out var wipe)) { name = wipe.Name; failures = wipe.Failures; return true; }
            name = null; failures = 0;
            return false;
        }

        /// <summary>A live epic encounter (not the current target, not yet defeated) that is fighting one of the raiders.</summary>
        private GameNPC HostileEncounter(GameNPC[] live, GameLiving[] participants)
        {
            var raiders = participants.Where(p => p.IsAlive && p.CurrentRegionID == _region).ToHashSet();
            long now = GameLoop.GameLoopTime;
            // An encounter just set aside for taking no damage is not switched back to because it attacks: Krondon,
            // 2026-10-21, Scurceol Hyrde (immune while his four Lyft Miht orbs live) was skipped and re-picked every
            // minute for over an hour, so the raid never turned to the orbs.
            // An encounter whose tactic sends the raid to the current target is no switch (Scurceol Hyrde attacks the raid
            // while it shoots his orbs; EpicEncounterTactics).
            // A hazard that burns the raid (Orshom's Fire) is no encounter to turn on either.
            return live.FirstOrDefault(n => n != _target && n is IGameEpicNpc && n.IsAlive && n.InCombat &&
                                            !EpicEncounterTactics.Covers(n, _target, live) && !EpicEncounterTactics.IsHazard(n) &&
                                            _routeRetry.GetValueOrDefault(n) <= now &&
                                            n.ObjectState == GameObject.eObjectState.Active && !_clearedNames.Contains(n.Name) &&
                                            n.TargetObject is GameLiving victim && raiders.Contains(victim));
        }

        private bool IsMusteredEncounter(GameNPC npc) =>
            (npc is IGameEpicNpc || _finalTypes.Contains(npc.GetType().Name)) &&
            // A boss's required adds are fought as part of its fight, without a muster of their own (EpicEncounterTactics).
            !EpicEncounterTactics.IsRequiredAddOfLiveBoss(npc, _live);
        // The region's NPCs as of this turn (AdvanceCore), for the tactic checks.
        private IReadOnlyList<GameNPC> _live = [];

        // The raid's living members inside when the attempt began (IsWipe's raid share), and the participants of this turn.
        private int _attemptRaidSize;
        private GameLiving[] _attemptParticipants;

        private void BeginAttempt(GameLiving[] raiders, long now)
        {
            _musterEngaged = true;
            _musterEngagedTick = now;
            _engaged.Clear();
            _engaged.UnionWith(raiders);
            _attemptRaidSize = _attemptParticipants?.Count(p => p.IsAlive && p.CurrentRegionID == _region) ?? 0;
            _attemptLowestPercent = _target.HealthPercent;
            _pulls.Begin();
            // A pull is progress: the next muster for this encounter starts its own count.
            _musterProgress.Remove(_target);
            // A reset encounter is back at full health: damage from there is progress again (the lowest health seen
            // was the last attempt's).
            _targetLowestHealth = _targetLastHealth = _target.Health;
            _targetProgressTick = now;
            _regrouping = false;
        }

        /// <summary>Ends the current attempt; true when it was a wipe (the raid now regroups or is defeated).</summary>
        private bool EndAttempt(long now, string how)
        {
            _musterEngaged = false;
            int engaged = _engaged.Count;
            int fallen = _engaged.Count(p => !p.IsAlive || p.CurrentRegionID != _region || p is GameBot { IsReturningAfterRelease: true });
            _engaged.Clear();
            if (!IsWipe(engaged, fallen, _target.HealthPercent, _attemptLowestPercent))
                return false;
            if (!IsWipe(engaged, fallen, _target.HealthPercent, _attemptLowestPercent, _attemptRaidSize))
            {
                // Lost skirmish: rest and muster again, but it was not the raid's attempt.
                log.Warn($"REALM_EXPEDITION_SKIRMISH_LOST region={_region} target=\"{_target.Name}\" engaged={engaged} fallen={fallen} " +
                         $"raid={_attemptRaidSize} health={_target.HealthPercent} lowest={_attemptLowestPercent} how=\"{how}\" " +
                         $"reason=\"less than half the raid was in it and less than half fell; no attempt used\"");
                _regrouping = true;
                _regroupSince = _musterSince = now;
                _targetProgressTick = now;
                return false;
            }
            int failures = _failedAttempts[_target.Name] = FailedAttempts(_target.Name) + 1;
            _wipes.Enqueue((_target.Name, failures));
            log.Warn($"REALM_EXPEDITION_WIPE region={_region} target=\"{_target.Name}\" level={_target.Level} failures={failures} " +
                     $"of={MaximumFailedAttempts} engaged={engaged} fallen={fallen} health={_target.HealthPercent} lowest={_attemptLowestPercent} how=\"{how}\"");
            if (failures >= MaximumFailedAttempts)
            {
                DefeatedAt = _target.Name;
                Hold = true;
                Status = $"Defeated at {_target.Name}";
                return true;
            }
            _regrouping = true;
            _regroupSince = _musterSince = now;
            _targetProgressTick = now;
            return true;
        }

        public const long TargetNoProgressMilliseconds = 5 * 60_000;
        public const long BlockedTargetRetryMilliseconds = 15 * 60_000;
        public const long HoldReportMilliseconds = 10 * 60_000;
        public const long BlockedEndMilliseconds = 20 * 60_000;
        private long _blockedSince;

        /// <summary>
        /// No live encounter reachable from the front or from any party for 20 minutes.
        /// Tuscaran (Oct 3-4) held 300 bots on "Checking blocked room approaches" for over
        /// six hours after the raid dropped to a lower level it could not climb back from.
        /// </summary>
        public bool Blocked { get; private set; }

        public static bool BlockedTooLong(long blockedSince, long now) =>
            blockedSince > 0 && now - blockedSince >= BlockedEndMilliseconds;

        /// <summary>
        /// A route target nobody has damaged for five minutes is set aside so the
        /// expedition moves on (Caer Sidi and Tuscaran held at their opening area
        /// for 3.5 hours on 2026-10-02). Final bosses are never set aside.
        /// </summary>
        public static bool ShouldSkipTarget(bool finalBoss, long sinceProgressMilliseconds) =>
            !finalBoss && sinceProgressMilliseconds >= TargetNoProgressMilliseconds;
        /// <summary>
        /// The front plus up to <paramref name="max"/> party positions, skipping any within
        /// 150 units across and 64 up/down of one already kept (a party standing 96 units
        /// above the front is a different floor and is kept).
        /// </summary>
        public static Vector3[] DistinctAnchors(Vector3 front, IEnumerable<Vector3> parties, int max)
        {
            var kept = new List<Vector3> { front };
            foreach (Vector3 p in parties)
            {
                if (kept.Count > max) break;
                if (kept.Any(k => Vector2.DistanceSquared(new(k.X, k.Y), new(p.X, p.Y)) <= 150 * 150 &&
                        MathF.Abs(k.Z - p.Z) <= 64))
                    continue;
                kept.Add(p);
            }
            return kept.ToArray();
        }
        public DbZonePoint Entrance { get; }
        public Vector3 Front { get; private set; }
        public Vector3 Destination { get; private set; }
        public string TargetName { get; private set; } = "Encounter staging";
        public int TargetLevel { get; private set; } = 50;
        public bool Hold { get; private set; } = true;
        public bool Complete { get; private set; }
        public string Status { get; private set; } = "Entering dungeon";
        public GameNPC[] FinalBosses => _finals.Values.ToArray();

        // Ordinary flying patrols need not occupy a melee-floor polygon.
        // Never defer a boss, a scripted subclass, or a grounded room guard.
        public const int RangedFlyerReach = 1200;
        public static bool IsOutOfRangedReach(GameNPC.eFlags flags, int monsterZ, int floorZ) =>
            AutonomousPveTargetPolicy.IsUnreachableFlyer(flags, monsterZ, floorZ) && Math.Abs(monsterZ - floorZ) > RangedFlyerReach;

        public static bool CanDeferAirbornePatrol(Type type, GameNPC.eFlags flags) =>
            type == typeof(GameEpicNPC) && (flags & GameNPC.eFlags.FLYING) != 0;

        /// <summary>How far from a required add its ranged raiders stand at most: inside the usual 1,500 spell range, with room to spare.</summary>
        public const int RequiredAddRangedReach = 1_400;

        /// <summary>
        /// Krondon, 2026-11-06 (01:49 and 02:16): Scurceol Hyrde's tactic sent the raid to his four Lyft Miht orbs, which float
        /// 230-580 above him (450-790 above the room floor). The ground-route gate (an approach within 256 of the target's height) failed
        /// for every orb, they were set aside a minute at a time, and the raid walked off to Fuladl; no orb was attacked
        /// and Scurceol was never fought. A required add is not walked to: its ranged raiders stand on a floor spot the raid
        /// can reach, within <see cref="RequiredAddRangedReach"/> of it and with sight to the floor under it. Tried: the
        /// floor under the add, the boss's floor, and two points between them. False, with why, when none works.
        /// </summary>
        /// <param name="reach">The approach the raid can walk to for a floor point, or null when none is proven.</param>
        public static bool TryRangedStand(IPathfindingMgr nav, Zone zone, Vector3 add, Vector3 boss, Func<Vector3, Vector3?> reach,
            out Vector3 stand, out string why)
        {
            stand = default;
            if (nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            { why = "no navigation mesh here"; return false; }
            Vector3? under = nav.GetClosestPoint(zone, add, 128, 128, RangedFlyerReach, nav.DefaultFilters);
            var floors = new List<Vector3>();
            void Add(Vector3? p)
            {
                if (p is Vector3 v && !floors.Any(f => Vector3.DistanceSquared(f, v) < 48 * 48)) floors.Add(v);
            }
            Add(under);
            Add(nav.GetClosestPoint(zone, boss, 64, 64, 256, nav.DefaultFilters));
            var level = new Vector3(add.X, add.Y, boss.Z);
            foreach (float t in new[] { 0.67f, 0.33f })
                Add(nav.GetClosestPoint(zone, Vector3.Lerp(boss, level, t), 96, 96, 256, nav.DefaultFilters));
            if (floors.Count == 0)
            { why = "no floor under it or near its boss"; return false; }
            int reached = 0, inRange = 0;
            foreach (Vector3 floor in floors)
            {
                if (reach(floor) is not Vector3 approach) continue;
                reached++;
                if (Vector3.Distance(approach, add) > RequiredAddRangedReach) continue;
                inRange++;
                // The mesh ray runs along the floor: sight to the floor under the add (or to the spot below it at the
                // stand's height when it hovers over nothing).
                Vector3 sight = under ?? new Vector3(add.X, add.Y, approach.Z);
                if (Vector3.DistanceSquared(approach, sight) > 1 && !nav.HasLineOfSight(zone, approach, sight, nav.DefaultFilters)) continue;
                stand = approach;
                why = $"ranged from the floor {(int)Vector3.Distance(approach, add)} away";
                return true;
            }
            why = reached == 0 ? $"none of {floors.Count} floor spots under or near it is reachable from the raid"
                : inRange == 0 ? $"no reachable floor spot within {RequiredAddRangedReach} of it"
                : $"no reachable floor spot within {RequiredAddRangedReach} has line of sight to it";
            return false;
        }

        private RealmRaidDungeonRoute(ushort region, DbZonePoint entrance, Vector3 entry, Vector3 trigger, string[] finals,
            string[] objectives = null, eRealm realm = eRealm.None)
        {
            _region = region; Entrance = entrance; Front = Destination = entry; _trigger = trigger; _finalTypes = finals;
            _objectives = objectives; _realm = realm;
        }

        /// <summary>
        /// The expedition's entrance and its first floor inside. Realm epic dungeons take any
        /// exterior entrance. A neutral expedition (Summoner's Hall, Darkness Falls) takes its own
        /// realm's entrance in its home region, directly or through one dungeon (Summoner's Hall
        /// is entered from Hall of the Corrupt, Dodens Gruva or Marfach Caverns), nearest to the
        /// muster hub first; <see cref="Entrance"/> is then the edge out of the home region, where
        /// the expedition stages.
        /// </summary>
        /// <summary>Why the last TryCreate found no entrance: each candidate and the check it failed (raid start log).</summary>
        public static string LastFailure { get; private set; } = "";

        public static bool TryCreate(ushort region, Vector3 trigger, string[] finals, out RealmRaidDungeonRoute route,
            eRealm realm = eRealm.None, ushort homeRegion = 0, string[] objectives = null, Vector3? hub = null)
        {
            using var aiProfile = AiProfile.Scope(AiSystem.RouteRaidDungeon); // AI_PROFILE: measures only
            route = null;
            var nav = PathfindingProvider.Instance;
            var why = new List<string>();
            LastFailure = "pathfinding unavailable";
            if (!nav.IsAvailable) return false;
            var crossings = AutonomousWorldBotController.RealmEventCrossings();
            IEnumerable<(DbZonePoint Outer, DbZonePoint Inner)> candidates = homeRegion == 0
                ? crossings.Where(e => e.TargetRegion == region).Select(e => (e, e))
                : RealmRaidNeutralEvents.Entrances(crossings, realm, homeRegion, region)
                    .OrderBy(pair => RealmRaidNeutralEvents.EntrancePreference(realm, pair.Outer.Id))
                    .ThenBy(pair => hub.HasValue ? Vector2.DistanceSquared(new(pair.Outer.SourceX, pair.Outer.SourceY), new(hub.Value.X, hub.Value.Y)) : 0);
            foreach (var (outer, inner) in candidates)
            {
                Zone outside = WorldMgr.GetRegion(outer.SourceRegion)?.GetZone(outer.SourceX, outer.SourceY);
                Zone inside = WorldMgr.GetRegion(region)?.GetZone(inner.TargetX, inner.TargetY);
                string id = $"zp{outer.Id}/{inner.Id}";
                if (outside == null || outside.IsDungeon || inside == null || !nav.HasNavmesh(inside))
                { why.Add($"{id}: {(outside == null ? "no outside zone" : outside.IsDungeon ? "outside is a dungeon" : inside == null ? "no inside zone" : "no inside navmesh")}"); continue; }
                if (homeRegion != 0 && !(region == AutonomousDarknessFallsPolicy.RegionId
                        ? AutonomousDarknessFallsNavigation.IsHomeEntrance(inner)
                        : AutonomousDungeonGoalCatalog.CanUseEntrance(inner, region, (int)trigger.X, (int)trigger.Y)))
                { why.Add($"{id}: not this realm's entrance"); continue; }
                if (homeRegion != 0 && !RealmRaidStaging.TryDungeonPost(nav, outside, new(outer.SourceX, outer.SourceY, outer.SourceZ), 0, [], out _))
                { why.Add($"{id}: no staging post outside"); continue; }
                Vector3 raw = new(inner.TargetX, inner.TargetY, inner.TargetZ);
                Vector3? floor = nav.GetClosestPoint(inside, raw, 48, 48, 64, nav.DefaultFilters);
                if (!floor.HasValue) { why.Add($"{id}: no navmesh floor at the inside arrival point"); continue; }
                if (!AutonomousRendezvousNavigation.HasLocalExit(nav, inside, floor.Value)) { why.Add($"{id}: inside arrival has no local exit"); continue; }
                // A neutral dungeon is too large for one entrance-to-final-boss proof (Darkness
                // Falls); the route proves each named encounter from the raid's positions instead.
                if ((homeRegion == 0 || !RealmRaidNeutralEvents.IsNeutralRegion(region)) &&
                    !AutonomousZonePointApproach.TryResolve(nav, inside, floor.Value, trigger, 220, out _))
                { why.Add($"{id}: no approach to the encounter"); continue; }
                route = new(region, outer, floor.Value, trigger, finals, objectives, realm);
                route.ObserveCompletion();
                LastFailure = "";
                return true;
            }
            LastFailure = why.Count == 0 ? "no entrance crossing into the region for this realm" : string.Join("; ", why);
            return false;
        }

        /// <summary>
        /// Moves the front along the chain: the next waypoint once formed parties stand within 300
        /// of the current one. At the end the encounter becomes the ordinary target. False when no
        /// chain is being walked.
        /// </summary>
        private bool AdvanceChain(GameLiving[] inside, long now)
        {
            if (_chain == null) return false;
            if (_chainTarget?.IsAlive != true || _chainTarget.ObjectState != GameObject.eObjectState.Active)
            { _chain = null; _chainTarget = null; return false; }
            while (_chainIndex < _chain.Length - 1 &&
                   inside.Count(p => p.IsWithinRadius(new Point3D((int)_chain[_chainIndex].X, (int)_chain[_chainIndex].Y, (int)_chain[_chainIndex].Z), 300)) >= 8)
                _chainIndex++;
            Front = Destination = _chain[_chainIndex];
            if (_chainIndex == _chain.Length - 1)
            {
                _target = _chainTarget; _chain = null; _chainTarget = null;
                // A fresh start: the chain's target kept the previous target's start, so Prince Ba'alorien
                // inherited Prince Abdin's absence allowance and was skipped at 100% with the raid arriving (2026-10-14).
                _targetProgressTick = _targetStartTick = now; _targetLowestHealth = _targetLastHealth = _target.Health;
                _targetClosest = float.MaxValue; _targetApproachTick = now;
                Hold = false; Status = "Clearing " + _target.Name;
                return true;
            }
            Hold = false;
            Status = $"Advancing to {TargetName} (waypoint {_chainIndex + 1}/{_chain.Length})";
            return true;
        }

        /// <summary>
        /// A Darkness Falls encounter reached through a certified ordinary-spawn route: the proofs give,
        /// per realm, a chain of proven legs from the realm's entrance to a spawn's approach; the
        /// encounter must then be a short proven leg from that approach (nearest proofs first).
        /// </summary>
        private bool TryDarknessFallsChain(IPathfindingMgr nav, Zone zone, Vector3 encounter, out Vector3[] chain) =>
            TryDarknessFallsChain(nav, zone, _realm, encounter, out chain);

        /// <summary>The same certified chain for one realm's member (late raid members walk it to their post).</summary>
        public static bool TryDarknessFallsChain(IPathfindingMgr nav, Zone zone, eRealm realm, Vector3 encounter, out Vector3[] chain)
        {
            using var aiProfile = AiProfile.Scope(AiSystem.RouteDarknessFallsChain); // AI_PROFILE: measures only
            // The answer depends on the realm, the post and the mesh only, never on the asking bot. Every
            // raider whose post was not one leg away re-ran the full search (8 approaches x ~38 candidate
            // points, ~300 path queries, ~1 s) on every AI turn; the Prince Abdin raid on 2026-10-12 made
            // 577 such turns. Failures are remembered too: they repeat until the post moves.
            if (nav is not LocalPathfindingMgr || zone == null)
                return SearchDarknessFallsChain(nav, zone, realm, encounter, out chain);
            long now = GameLoop.GameLoopTime;
            var key = (realm, zone.ID, (int)encounter.X >> 6, (int)encounter.Y >> 6, (int)encounter.Z >> 6, NavigationGeometryRevision.Read(zone));
            if (DarknessFallsChains.TryGetValue(key, out var known) && now < known.Until)
            {
                chain = known.Chain;
                return chain != null;
            }
            // A failure holds for every post near it. The raid front moves a few dozen units at a time, so the exact
            // post rarely repeats: on 2026-10-17 every Prince Abdin raider re-ran the failing ~300-query search
            // (1-5 s) on each AI turn and the server lagged.
            var area = (realm, zone.ID, (int)encounter.X >> DarknessFallsChainFailureCellShift, (int)encounter.Y >> DarknessFallsChainFailureCellShift,
                (int)encounter.Z >> DarknessFallsChainFailureCellShift, key.Item6);
            if (DarknessFallsChainFailures.TryGetValue(area, out long failedUntil) && now < failedUntil)
            {
                chain = null;
                return false;
            }
            bool found = SearchDarknessFallsChain(nav, zone, realm, encounter, out chain);
            if (DarknessFallsChains.Count > 4_096) DarknessFallsChains.Clear();
            DarknessFallsChains[key] = (found ? chain : null, now + (found ? DarknessFallsChainMemoryMilliseconds : DarknessFallsChainFailureMemoryMilliseconds));
            if (!found)
            {
                if (DarknessFallsChainFailures.Count > 4_096) DarknessFallsChainFailures.Clear();
                DarknessFallsChainFailures[area] = now + DarknessFallsChainFailureMemoryMilliseconds;
            }
            return found;
        }

        public const long DarknessFallsChainMemoryMilliseconds = 120_000;
        public const long DarknessFallsChainFailureMemoryMilliseconds = 60_000;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(eRealm, ushort, int, int, int, long), (Vector3[] Chain, long Until)>
            DarknessFallsChains = new();
        /// <summary>Failures are shared by posts in the same 512-unit cell.</summary>
        public const int DarknessFallsChainFailureCellShift = 9;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(eRealm, ushort, int, int, int, long), long>
            DarknessFallsChainFailures = new();

        private static bool SearchDarknessFallsChain(IPathfindingMgr nav, Zone zone, eRealm realm, Vector3 encounter, out Vector3[] chain)
        {
            chain = null;
            var _realm = realm;
            if (_realm == eRealm.None) return false;
            var nearest = AutonomousDarknessFallsNavigation.SnapshotCertifiedProofs()
                .Where(proof => proof.Approach is { Length: 3 } && proof.Routes != null)
                .Select(proof => (Proof: proof, Approach: new Vector3(proof.Approach[0], proof.Approach[1], proof.Approach[2]),
                    Route: proof.Routes.FirstOrDefault(route => route.Realm == _realm && route.InWaypoints is { Length: >= 2 })))
                .Where(item => item.Route != null && Vector3.DistanceSquared(item.Approach, encounter) <=
                    DarknessFallsChainReach * DarknessFallsChainReach)
                .OrderBy(item => Vector3.DistanceSquared(item.Approach, encounter))
                .Take(8);
            foreach (var (_, approach, route) in nearest)
            {
                if (!AutonomousZonePointApproach.TryResolve(nav, zone, approach, encounter, 220, out Vector3 final)) continue;
                chain = route.InWaypoints.Select(point => new Vector3(point[0], point[1], point[2])).Append(final).ToArray();
                return true;
            }
            return false;
        }

        /// <summary>
        /// The furthest chain waypoint (final approach first) that a party leader inside the dungeon has a
        /// complete corridor to; -1 when none of them can reach any waypoint.
        /// </summary>
        private int ReachableChainStart(IPathfindingMgr nav, Vector3[] chain, GameLiving[] inside)
        {
            // Where the parties stand, not the front: a stale front can sit on the unreachable waypoint itself.
            Vector3[] anchors = DistinctAnchors(Front, inside
                .Where(p => p.Group == null || p.Group.LivingLeader == p)
                .Select(p => new Vector3(p.X, p.Y, p.Z)), 3);
            Vector3[] leaders = anchors.Length > 1 ? anchors.Skip(1).ToArray() : anchors;
            for (int i = chain.Length - 1; i >= 0; i--)
            foreach (Vector3 from in leaders)
            {
                Zone zone = WorldMgr.GetRegion(_region)?.GetZone((int)from.X, (int)from.Y);
                if (zone != null && nav.HasNavmesh(zone) &&
                    AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, from, chain[i]))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Raiders a final encounter waits for before it is pulled: half of the living raid, at least
        /// <see cref="MinimumRaidersAtTarget"/> and at most 150; after 20 minutes of mustering a third will do.
        /// </summary>
        public static int FinalMusterNeeded(int livingRaiders, long musterMilliseconds) =>
            Math.Max(MinimumRaidersAtTarget, Math.Min(FinalMusterMaximum,
                (int)(livingRaiders * (musterMilliseconds >= FinalMusterPatienceMilliseconds ? 0.33 : 0.5))));
        public const int FinalMusterMaximum = 150;
        public const long FinalMusterPatienceMilliseconds = 20 * 60_000;
        public const int FinalMusterRadius = 1_500;
        /// <summary>How long a pull-back from an early-engaging encounter lasts at most (it has leashed home well before).</summary>
        public const long BreakOffMilliseconds = 90_000;
        public const long BreakOffRepeatMilliseconds = 5 * 60_000;
        private readonly Dictionary<GameNPC, long> _lastBreakOff = new();
        public const float FinalMusterStandOff = 1_400f;
        public const long FinalEngageGraceMilliseconds = 2 * 60_000;

        /// <summary>
        /// Darkness Falls run 2026-10-13: the Albion raid reached Legion once eight raiders stood at the last
        /// waypoint and was then pulled in by twos and threes as the rest arrived; it wiped, released to Camelot
        /// and Legion reset to full health a minute after the last raider fell. A final encounter not yet in a
        /// fight now waits at a muster point on the walked approach, outside its aggro range, until enough of
        /// the raid stands there (<see cref="FinalMusterNeeded"/>); then the whole muster goes in together. After a
        /// wipe the boss leaves combat and the raid musters again. True while the raid is holding.
        /// </summary>
        private bool MusterForFinal(GameLiving[] participants, long now)
        {
            // The muster was placed while the encounter was out chasing raiders; it is home again, so the muster follows it
            // (Aidon the Archwizard, 2026-11-05: the muster went 2,600 from his home, on the ramp the chase had reached).
            if (_musterTarget == _target && !_target.InCombat && MusterReference(_target) is var home &&
                Vector3.Distance(home, _musterReference) > MusterFollowDistance)
            {
                Vector3 old = _musterPoint;
                if (TryPointApproach(home, participants, out Vector3 approach)) Front = approach;
                _musterPoint = EpicEncounterTactics.KeepInsideFor(_target, MusterPoint(participants));
                log.Info($"REALM_EXPEDITION_MUSTER_MOVED region={_region} target=\"{_target.Name}\" " +
                         $"from={(int)old.X},{(int)old.Y},{(int)old.Z} to={(int)_musterPoint.X},{(int)_musterPoint.Y},{(int)_musterPoint.Z} " +
                         $"reason=\"the encounter is back at {(int)home.X},{(int)home.Y},{(int)home.Z}\"");
            }
            // The front is where every pull walks to. One resolved while the encounter was out chasing raiders stays there
            // when it walks home (Aidon the Archwizard, 2026-11-05: three full musters went in to 31686,35683,14888, the
            // approach from the 22:38 switch, while he stood at home 1,979 away; none entered combat and he was set aside).
            if (_musterTarget == _target && now >= _nextFrontCheck &&
                FrontIsStale(Front, MusterReference(_target), _target.InCombat))
            {
                _nextFrontCheck = now + FrontRefreshMilliseconds;
                RefreshFront(participants, now);
            }
            if (_musterTarget != _target)
            {
                _musterTarget = _target;
                _musterSince = now;
                _musterEngaged = false;
                _regrouping = false;
                _engaged.Clear();
                _musterEmptySince = 0;
                _musterUnreachable = false;
                _setAsideLine = null;
                _pulls.Reset();
                // Away from its home (out chasing raiders), the raid musters for it at home: the front is the approach there.
                Vector3 reference = MusterReference(_target);
                if (Vector3.Distance(reference, new Vector3(_target.X, _target.Y, _target.Z)) > 1 &&
                    TryPointApproach(reference, participants, out Vector3 homeApproach))
                    Front = homeApproach;
                _musterPoint = EpicEncounterTactics.KeepInsideFor(_target, MusterPoint(participants));
            }
            // While the raid regroups, the survivors may still be fighting: arrivals wait at the muster point rather than
            // going in by twos and threes, and the whole regroup goes in together.
            _attemptParticipants = participants;
            // A break-off in progress: the raid holds at the muster while the encounter leashes home.
            if (EpicEncounterTactics.BrokenOff(_target, now) != null)
            {
                if (!_target.InCombat) EpicEncounterTactics.EndBreakOff(_target);
                else
                {
                    Destination = _musterPoint; Hold = true;
                    Status = $"Pulling back from {_target.Name} to the muster";
                    _targetProgressTick = now;
                    return true;
                }
            }
            if (_target.InCombat && !_regrouping)
            {
                // A fight in progress is never interrupted. One the raid walked into without a muster (the encounter
                // aggroed on the approach) is answered as a whole (EpicEncounterTactics.ChooseEarlyPull): everyone in at once,
                // or everyone back to the muster, never piecemeal (Aidon the Archwizard, 2026-11-05).
                GameLiving[] inside = participants.Where(p => p.IsAlive && p.CurrentRegionID == _region).ToArray();
                GameLiving[] fighting = inside.Where(p => p.IsWithinRadius(_target, TargetPresenceRadius)).ToArray();
                if (!_musterEngaged)
                {
                    bool resets = EpicEncounterTactics.ResetsWhenLeft(_target);
                    // Pulling back only shakes it off when the muster lies past its tether (Fuladl, Krondon 2026-11-06: tether
                    // 3,800, muster 1,400 from his spawn; he would follow the pull-back to the muster).
                    int tether = EpicEncounterTactics.LeashOf(_target)?.TetherRange is int t && t > 0 ? t : _target.TetherRange;
                    float musterFromSpawn = Vector3.Distance(_musterPoint, new Vector3(_target.SpawnPoint.X, _target.SpawnPoint.Y, _target.SpawnPoint.Z));
                    bool shakesOff = EpicEncounterTactics.BreakOffShakesOff(resets, tether, musterFromSpawn);
                    var choice = EpicEncounterTactics.ChooseEarlyPull(fighting.Length, inside.Length, shakesOff);
                    // Pulled back from it a moment ago and it is still on the raid: it chases, so everyone commits now.
                    if (choice == EpicEncounterTactics.EarlyPull.Disengage && _lastBreakOff.TryGetValue(_target, out long last) &&
                        now - last < BreakOffRepeatMilliseconds)
                        choice = EpicEncounterTactics.EarlyPull.Commit;
                    // Logged for a raid's final or a catalogued boss (at most every ten minutes each), and for every pull-back;
                    // a Darkness Falls named or an epic trash pack that is simply fought goes to the debug log (2026-11-06: 53
                    // lines in four hours, one per named).
                    bool isFinal = _finalTypes.Contains(_target.GetType().Name);
                    long since = _earlyPullLogged.TryGetValue(_target.Name, out long logged) ? now - logged : -1;
                    string line = $"REALM_EXPEDITION_EARLY_PULL region={_region} target=\"{_target.Name}\" near={fighting.Length} " +
                                  $"inside={inside.Length} resets={resets} tether={tether} musterFromSpawn={(int)musterFromSpawn} decision={choice} health={_target.HealthPercent} " +
                                  $"muster={(int)_musterPoint.X},{(int)_musterPoint.Y},{(int)_musterPoint.Z}";
                    if (EpicEncounterTactics.EarlyPullWorthLogging(choice, isFinal, EpicEncounterTactics.LeashOf(_target) != null, since))
                    {
                        _earlyPullLogged[_target.Name] = now;
                        log.Warn(line + $" final={isFinal} quietSinceLast={_quietEarlyPulls}");
                        _quietEarlyPulls = 0;
                    }
                    else
                    {
                        _quietEarlyPulls++;
                        if (log.IsDebugEnabled) log.Debug(line);
                    }
                    if (choice == EpicEncounterTactics.EarlyPull.Disengage)
                    {
                        EpicEncounterTactics.BreakOff(_target, _musterPoint, now + BreakOffMilliseconds);
                        _lastBreakOff[_target] = now;
                        Destination = _musterPoint; Hold = true;
                        Status = $"Pulling back from {_target.Name} to the muster";
                        _targetProgressTick = now;
                        return true;
                    }
                    // Commit: the whole raid is in this attempt, and goes to the fight now.
                    BeginAttempt(inside, now);
                    Destination = Front; Hold = false;
                    _nextTargetCall = 0;
                }
                else
                    _engaged.UnionWith(fighting);
                _pulls.SawCombat();
                _attemptLowestPercent = Math.Min(_attemptLowestPercent, _target.HealthPercent);
                return false;
            }
            // The raid needs time to cross from the muster point to the boss (run 5: the engage repeated every ten
            // seconds while Govannon was still out of combat); only after that is "out of combat" a wipe.
            if (_musterEngaged && now - _musterEngagedTick < FinalEngageGraceMilliseconds)
                return false;
            if (_musterEngaged)
            {
                // Out of combat after the pull: wiped or broke off. Muster again from scratch.
                _musterSince = now;
                bool wiped = EndAttempt(now, _pulls.Contact ? "left combat" : "never entered combat");
                if (wiped && Defeated)
                    return true;
                // Not even a fight: the pull never reached the encounter (Marfach's Ancient Black Oak, 91 pulls).
                if (_pulls.Ended(wiped))
                {
                    _setAsideLine = $"REALM_EXPEDITION_PULL_UNREACHED region={_region} target=\"{_target.Name}\" pulls={_pulls.PullsWithoutContact} " +
                        $"at={_target.X},{_target.Y},{_target.Z} front={(int)Front.X},{(int)Front.Y},{(int)Front.Z} " +
                        $"muster={(int)_musterPoint.X},{(int)_musterPoint.Y},{(int)_musterPoint.Z} " +
                        $"frontDistance={(int)Vector3.Distance(Front, new Vector3(_target.X, _target.Y, _target.Z))} " +
                        $"closest={(int)participants.Where(p => p.IsAlive && p.CurrentRegionID == _region).Select(p => (float)p.GetDistanceTo(_target)).DefaultIfEmpty(-1).Min()} " +
                        $"reason=\"the encounter never entered combat after {_pulls.PullsWithoutContact} pulls; set aside and tried again later\"";
                    _musterUnreachable = true;
                    return true;
                }
            }
            int living = participants.Count(p => p.IsAlive);
            long regroupMilliseconds = now - _regroupSince;
            int needed = _regrouping ? RegroupNeeded(participants.Length, regroupMilliseconds) : FinalMusterNeeded(living, now - _musterSince);
            var point = new Point3D((int)_musterPoint.X, (int)_musterPoint.Y, (int)_musterPoint.Z);
            GameLiving[] musteredRaiders = participants.Where(p => p.IsAlive && p.CurrentRegionID == _region &&
                                                   (p.IsWithinRadius(point, FinalMusterRadius) || p.IsWithinRadius(_target, TargetPresenceRadius))).ToArray();
            int mustered = musteredRaiders.Length;
            if (mustered > 0) _musterEmptySince = 0;
            else if (_musterEmptySince == 0) _musterEmptySince = now;
            else if (MusterUnreachable(_musterEmptySince, now))
            {
                _musterUnreachable = true;
                return true;
            }
            if (!_musterProgress.TryGetValue(_target, out MusterProgress progress))
                _musterProgress[_target] = progress = new();
            if (progress.Observe(mustered, needed, now))
            {
                _setAsideLine = $"REALM_EXPEDITION_MUSTER_STALLED region={_region} target=\"{_target.Name}\" mustered={mustered} needed={needed} " +
                    $"best={progress.Best} minutes={(now - progress.BestSince) / 60_000} living={living} " +
                    $"muster={(int)_musterPoint.X},{(int)_musterPoint.Y},{(int)_musterPoint.Z} body={MainBody(participants)} " +
                    $"pinnedBy=\"{PinnedBy(participants)}\" " +
                    $"reason=\"the muster gained nobody for {MusterStalledMilliseconds / 60_000} minutes; set aside and tried again later\"";
                _musterUnreachable = true;
                return true;
            }
            // After a wipe the survivors rest and rebuff for a few minutes even when enough are already there.
            bool rested = !_regrouping || regroupMilliseconds >= RegroupMinimumMilliseconds;
            // Leeroy (bot_living_leeroy): part of the raid is here, not all of it, and he can't wait. The boss is in the fight,
            // so the whole muster goes in after him now, as an attempt like any other. A raid that arrives all at once gets
            // him too: he jumps its pull.
            if (LeeroyRolled && !_leeroyDone &&
                AutonomousRaidLeeroy.GoesNow(AutonomousRaidLeeroy.ShouldCharge(mustered, needed, now - _musterSince, FailedAttempts(_target.Name), _regrouping) ||
                                             rested && AutonomousRaidLeeroy.JumpsThePull(mustered, needed, FailedAttempts(_target.Name), _regrouping), mustered))
            {
                _leeroyDone = true;
                if (AutonomousRaidLeeroy.Pick(musteredRaiders) is GameBot leeroy &&
                    AutonomousRaidLeeroy.Charge(leeroy, _target, musteredRaiders, _target.CurrentZone?.Description ?? _target.Name))
                {
                    LeeroyName = leeroy.Name;
                    BeginAttempt(musteredRaiders, now);
                    Destination = Front;
                    log.Info($"REALM_EXPEDITION_LEEROY region={_region} target=\"{_target.Name}\" bot=\"{leeroy.Name}\" mustered={mustered} " +
                             $"needed={needed} living={living} minutes={(now - _musterSince) / 60_000.0:0.0}");
                    return false;
                }
            }
            if (mustered >= needed && rested)
            {
                bool retry = _regrouping;
                BeginAttempt(musteredRaiders, now);
                Destination = Front;
                log.Info($"REALM_EXPEDITION_FINAL_ENGAGE region={_region} target=\"{_target.Name}\" mustered={mustered} needed={needed} " +
                         $"living={living} minutes={(now - _musterSince) / 60_000.0:0.0} attempt={FailedAttempts(_target.Name) + 1} retry={retry}");
                return false;
            }
            Destination = _musterPoint;
            Hold = true;
            Status = _regrouping
                ? $"Regrouping for {_target.Name} after a wipe ({mustered}/{needed}, attempt {FailedAttempts(_target.Name) + 1} of {MaximumFailedAttempts})"
                : $"Mustering for {_target.Name} ({mustered}/{needed})";
            // Waiting for the raid is not a stalled fight.
            _targetProgressTick = now;
            if (now >= _nextMusterLog)
            {
                _nextMusterLog = now + 2 * 60_000;
                log.Info($"REALM_EXPEDITION_FINAL_MUSTER region={_region} target=\"{_target.Name}\" mustered={mustered} needed={needed} " +
                         $"living={living} at={(int)_musterPoint.X},{(int)_musterPoint.Y},{(int)_musterPoint.Z} minutes={(now - _musterSince) / 60_000.0:0.0}");
            }
            return true;
        }

        /// <summary>A front further than this from an encounter out of combat is not where the encounter is: pulls that walk
        /// there never reach it (about the aggro range of the big epics, and well past any approach the resolver snaps to).</summary>
        public const float FrontStaleDistance = 1_000f;
        /// <summary>The front is checked at most this often, and a failed approach search waits five times as long.</summary>
        public const long FrontRefreshMilliseconds = 60_000;
        private long _nextFrontCheck;

        /// <summary>True when the front no longer leads to the encounter: it is out of combat (at home, or wherever it
        /// stopped) and the front is more than <see cref="FrontStaleDistance"/> from it. In a fight the encounter moves
        /// with the raid and the front is left alone.</summary>
        public static bool FrontIsStale(Vector3 front, Vector3 encounter, bool encounterInCombat) =>
            !encounterInCombat && Vector3.Distance(front, encounter) > FrontStaleDistance;

        /// <summary>Resolves the front again from where the encounter stands now (rate-limited by
        /// <see cref="FrontRefreshMilliseconds"/>). A pull already walking out goes to the new front; a muster still
        /// waiting is placed again on the new approach. Pulls that went to the old front never
        /// had a chance to reach the encounter, so they no longer count toward setting it aside.</summary>
        private void RefreshFront(GameLiving[] participants, long now)
        {
            Vector3 old = Front;
            // Where it stands, or its home when it is out of it and walking back (as for the muster).
            Vector3 at = MusterReference(_target);
            // Only an approach proven from the raid replaces the front: the raw point may be off the walked mesh (the end
            // of a Darkness Falls waypoint chain is the front because no single path reaches the encounter).
            bool resolved = TryPointApproach(at, participants, out Vector3 approach);
            if (resolved)
            {
                Front = approach;
                if (_musterEngaged) Destination = Front;
                else _musterPoint = EpicEncounterTactics.KeepInsideFor(_target, MusterPoint(participants));
            }
            else _nextFrontCheck = now + 5 * FrontRefreshMilliseconds;
            int unreached = _pulls.PullsWithoutContact;
            // Only a front that now leads to the encounter wipes the slate; otherwise the set-aside still comes.
            if (!_pulls.Contact && !FrontIsStale(Front, at, false)) _pulls.Reset();
            log.Warn($"REALM_EXPEDITION_FRONT_REFRESHED region={_region} target=\"{_target.Name}\" at={(int)at.X},{(int)at.Y},{(int)at.Z} " +
                     $"from={(int)old.X},{(int)old.Y},{(int)old.Z} to={(int)Front.X},{(int)Front.Y},{(int)Front.Z} " +
                     $"was={(int)Vector3.Distance(old, at)} now={(int)Vector3.Distance(Front, at)} approach={(resolved ? "resolved" : "unresolved, front kept")} " +
                     $"engaged={_musterEngaged} unreachedPulls={unreached} " +
                     $"muster={(int)_musterPoint.X},{(int)_musterPoint.Y},{(int)_musterPoint.Z} " +
                     $"reason=\"the front was resolved while the encounter was elsewhere; pulls there never reach it\"");
        }

        /// <summary>Moved further than this from where the muster was measured, the encounter gets a new muster.</summary>
        public const float MusterFollowDistance = 600f;
        private Vector3 _musterReference;

        /// <summary>
        /// Where a muster is measured from: the encounter, or its spawn when it is out of its home (a leashed boss chasing
        /// raiders walks home and resets, so the raid musters for it there).
        /// </summary>
        public static Vector3 MusterReference(Vector3 position, Vector3 spawn, int tetherRange, bool resets) =>
            resets && tetherRange > 0 && Vector3.Distance(position, spawn) > Math.Min(tetherRange / 2f, MusterFollowDistance)
                ? spawn : position;

        private static Vector3 MusterReference(GameNPC npc) => MusterReference(new(npc.X, npc.Y, npc.Z),
            new(npc.SpawnPoint.X, npc.SpawnPoint.Y, npc.SpawnPoint.Z),
            EpicEncounterTactics.LeashOf(npc)?.TetherRange is int t && t > 0 ? t : npc.TetherRange, EpicEncounterTactics.ResetsWhenLeft(npc));

        /// <summary>
        /// The point on the walked path from the raid's main body to the encounter's approach that lies
        /// <see cref="FinalMusterStandOff"/> from the encounter (or the main body itself when it is closer).
        /// </summary>
        private Vector3 MusterPoint(GameLiving[] participants)
        {
            var nav = PathfindingProvider.Instance;
            Vector3 boss = MusterReference(_target);
            _musterReference = boss;
            GameLiving[] inside = participants.Where(p => p.IsAlive && p.CurrentRegionID == _region).ToArray();
            Zone zone = WorldMgr.GetRegion(_region)?.GetZone((int)Front.X, (int)Front.Y);
            if (!TryRegroupFront(inside, out Vector3 body) || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return Front;
            if (Vector3.Distance(body, boss) <= FinalMusterStandOff + 200)
                return body;
            // Searched from the encounter's approach toward the raid, so the path always starts at the encounter even
            // when it is too long to finish. From the raid's side a partial path ended wherever the search gave up:
            // Legion's muster in Darkness Falls was placed 19,000 units from him and nobody ever reached it (run 9).
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[256];
            PathfindingResult path = nav.GetPathStraight(zone, Front, body, nav.DefaultFilters, nodes);
            if (path.Status == PathfindingStatus.PathFound || path.Status == PathfindingStatus.PartialPathFound)
            {
                for (int i = 1; i < path.NodeCount; i++)
                {
                    Vector3 a = nodes[i - 1].Position, b = nodes[i].Position;
                    if (Vector3.Distance(b, boss) < FinalMusterStandOff) continue;
                    // a inside the stand-off circle, b outside: the crossing point on a-b.
                    float lo = 0, hi = 1;
                    for (int step = 0; step < 12; step++)
                    {
                        float mid = (lo + hi) / 2;
                        if (Vector3.Distance(Vector3.Lerp(a, b, mid), boss) >= FinalMusterStandOff) hi = mid; else lo = mid;
                    }
                    Vector3? floor = nav.GetClosestPoint(zone, Vector3.Lerp(a, b, hi), 64, 64, 128, nav.DefaultFilters);
                    return floor ?? b;
                }
                if (path.NodeCount > 0) return nodes[path.NodeCount - 1].Position;
            }
            return Front;
        }

        public const long CallTargetAfterMilliseconds = 90_000;
        public const long FrontRegroupMilliseconds = 3 * 60_000;
        private long _frontAbsentSince;
        // The raiders' closest approach to an unreached front: while they keep closing in, the front is not given up.
        private float _frontClosest = float.MaxValue;
        private Vector3 _frontClosestFor;

        /// <summary>The living raider with the most living raiders within 1,000 units (the raid's main body).</summary>
        private static bool TryRegroupFront(GameLiving[] inside, out Vector3 front)
        {
            front = default;
            GameLiving best = null;
            int bestCount = 0;
            foreach (GameLiving candidate in inside.Where(p => p.Group == null || p.Group.LivingLeader == p))
            {
                int count = inside.Count(p => p.IsWithinRadius(candidate, 1000));
                if (count > bestCount) { best = candidate; bestCount = count; }
            }
            if (best == null || bestCount < MinimumRaidersAtTarget) return false;
            front = new(best.X, best.Y, best.Z);
            return true;
        }

        // The certified file holds ordinary spawns only; the nearest Albion approach is 6,100 units
        // from High Lord Saeor, 5,500 from Oro, 3,000 from Ba'alorien, and Nahemah's 199-unit one is a
        // floor above. With a 2,500 reach late raiders found no chain, failed their direct path and were
        // pocket-escaped out of Darkness Falls (run 12). The last leg is still a complete-corridor check.
        // (Nahemah's spawn moved off her unconnected dais onto the floor in front of it on 2026-10-13.)
        public const float DarknessFallsChainReach = 8_000f;
        public const int TargetPresenceRadius = 2_000;
        public const int MinimumRaidersAtTarget = 8;
        public const long TargetAbsencePauseLimitMilliseconds = 20 * 60_000;
        public const float TargetApproachStepUnits = 300f;
        private int _healthSample = -1;
        private GameLiving _healthSampleTarget;
        private long _nextHealLog;

        /// <summary>
        /// REALM_EXPEDITION_TARGET_HEALED: the raid's target got 5% or more of its health back since the last pulse, with
        /// what the fight looked like then. Hurionthex (Galladoria) went 76% -> 96% in half a minute with 270 raiders
        /// beside him, and none of the known resets explained it (2026-10-15).
        /// </summary>
        private void ReportHealthJump(GameLiving[] participants, long now)
        {
            int health = _target.HealthPercent;
            if (_healthSampleTarget != _target) { _healthSampleTarget = _target; _healthSample = health; return; }
            int before = _healthSample;
            _healthSample = health;
            if (health - before < 5 || now < _nextHealLog) return;
            _nextHealLog = now + 10_000;
            int reach = participants.Count(p => p.IsAlive && p.TargetObject == _target && p.IsWithinRadius(_target, 600));
            int targeting = participants.Count(p => p.IsAlive && p.TargetObject == _target);
            var npc = _target as GameNPC;
            string effects = string.Join(",", _target.effectListComponent.GetEffects().Select(e => e.SpellHandler?.Spell?.Name ?? e.EffectType.ToString()).Distinct().Take(8));
            log.Warn($"REALM_EXPEDITION_TARGET_HEALED region={_region} target=\"{_target.Name}\" from={before} to={health} inCombat={_target.InCombat} " +
                     $"lastHitAgoMs={(_target.LastAttackedByEnemyTick > 0 ? now - _target.LastAttackedByEnemyTick : -1)} " +
                     $"aggro={(npc?.Brain as DOL.AI.Brain.StandardMobBrain)?.AggroCount ?? -1} targeting={targeting} inReach={reach} " +
                     $"returning={npc?.IsReturningToSpawnPoint} fromSpawn={(npc == null ? -1 : (int)npc.GetDistanceTo(npc.SpawnPoint))} " +
                     $"model={npc?.Model} effects=\"{effects}\"");
        }
        public const long TargetApproachStallMilliseconds = 3 * 60_000;
        private float _targetClosest = float.MaxValue;
        private long _targetApproachTick;
        private long _nextTargetCall;
        public const long BusyCallAfterMilliseconds = 3 * 60_000;
        public const int ThinAttackTargeting = 8;

        /// <summary>
        /// An encounter's hazard, not a monster to clear: Galladoria's Watery Vortexes float over the water primal's pool,
        /// are out of reach, come back every six minutes and hit hard. Taken as the primal's adds, they were the raid's
        /// target over and over; three were skipped for no damage and the raid fell from 230 inside to 28 (run 22).
        /// </summary>
        private static bool IsEncounterHazard(GameNPC npc) =>
            npc.Brain?.GetType().Name is "VortexBrain" or "WaterfallAntipassBrain" ||
            // A catalog hazard (Orshom's Fire: immune but to cold, 500 every 3 s within 300 of it; EpicEncounterTactics).
            EpicEncounterTactics.IsHazard(npc);
        public const int ThinAttackNear = 16;

        /// <summary>
        /// The raid calls its target: when the encounter has taken no damage for 90 s although raid
        /// members stand near it (Summoner Lossren and Council Nokkvi skipped at 100%, 2026-10-06),
        /// idle living members within 1,500 units are ordered to attack it, at most every 30 s.
        /// Ordinary combat, healing and group defense take it from there.
        /// </summary>
        private void CallTarget(GameLiving[] participants, long now)
        {
            if (_target == null || now < _nextTargetCall) return;
            // A target losing health slowly to a handful while the raid stands round it is called too: Galladoria's
            // Guardian Earthmender (60,000 health, stationary) went from 100% to 73% in 11 minutes with 20-190 raiders
            // within 1,000 and 1-7 of them on it, and the no-damage call never came (run 22). Idle raiders only.
            bool thin = false;
            if (now - _targetProgressTick < CallTargetAfterMilliseconds)
            {
                int onIt = 0, around = 0;
                foreach (GameLiving p in participants)
                {
                    if (!p.IsAlive || p.CurrentRegionID != _target.CurrentRegionID) continue;
                    if (p.TargetObject == _target) onIt++;
                    if (p.IsWithinRadius(_target, 1500)) around++;
                }
                thin = onIt < ThinAttackTargeting && around >= ThinAttackNear;
                if (!thin) return;
            }
            _nextTargetCall = now + 30_000;
            int called = 0;
            // After three idle minutes the call also takes raiders already fighting the encounter's trash: in
            // Darkness Falls the succubi and ambassadors around Princess Nahemah kept every one of 65 raiders
            // busy for five minutes and she was skipped at 100% (run 17). Some of the fighters switch; the
            // rest keep the adds off them.
            // A thin attack takes busy fighters too: the target keeps losing a little health, so the idle clock never runs out
            // (run 27: 207 raiders by the Magical Earthmender, 100 on the Corrupt Primal of Earth it heals, 2 on it).
            bool takeBusy = thin || now - _targetProgressTick >= BusyCallAfterMilliseconds;
            int busyLeft = takeBusy ? Math.Max(MinimumRaidersAtTarget, participants.Count(p => p.IsAlive && p.IsWithinRadius(_target, 1500)) / 3) : 0;
            foreach (GameLiving member in participants)
            {
                if (member is not GameBot bot || !bot.IsAlive || bot.CurrentRegionID != _target.CurrentRegionID ||
                    !bot.IsWithinRadius(_target, 1500) || bot.TargetObject == _target ||
                    bot.Brain is not DOL.AI.Brain.BotBrain brain || !GameServer.ServerRules.IsAllowedToAttack(bot, _target, true))
                    continue;
                // A flying required add (Scurceol Hyrde's orbs) is called for the raiders who can shoot it only.
                if (AutonomousPveTargetPolicy.IsUnreachableFlyer(_target.Flags, _target.Z, bot.Z) && !EpicEncounterTactics.CanShootFlyers(bot))
                    continue;
                if (bot.InCombat || bot.IsAttacking)
                {
                    if (busyLeft <= 0) continue;
                    busyLeft--;
                }
                bot.TargetObject = _target;
                brain.AddToAggroList(_target, bot.InCombat || bot.IsAttacking ? 10_000 : 1);
                brain.FSM.SetCurrentState(DOL.GS.eFSMStateType.AGGRO);
                brain.NextThinkTick = GameLoop.GameLoopTime; // act on the call now, not at the next idle think
                called++;
            }
            if (called > 0)
                log.Info($"REALM_EXPEDITION_TARGET_CALLED region={_region} target=\"{_target.Name}\" health={_target.HealthPercent} called={called} " +
                         $"idleSeconds={(now - _targetProgressTick) / 1000}");
        }

        public void ObserveCompletion()
        {
            foreach (var npc in WorldMgr.GetRegion(_region)?.Objects.OfType<GameNPC>() ?? [])
                if (_finalTypes.Contains(npc.GetType().Name) && (!_finals.TryGetValue(npc.GetType().Name, out var old) || old.IsAlive))
                    _finals[npc.GetType().Name] = npc;
            if (_finalTypes.All(type => _finals.TryGetValue(type, out var boss) && !boss.IsAlive))
            { Complete = true; Status = "Final encounter defeated"; }
        }

        // Run 21 (2026-10-17): 70+ raiders fought Spindler Broodmother for over 30 minutes and nothing showed how she
        // fared. Once a minute: the current target's health, whether it is in combat, and the raiders around it.
        private long _nextTargetStatusLog;

        public void Advance(GameLiving[] participants, long now)
        {
            AdvanceCore(participants, now);
            GameNPC target = _target;
            if (target != null && target.IsAlive && now >= _nextTargetStatusLog)
            {
                _nextTargetStatusLog = now + 60_000;
                int near = participants.Count(p => p.IsAlive && p.CurrentRegionID == target.CurrentRegionID && p.IsWithinRadius(target, 1_000));
                int onIt = participants.Count(p => p.IsAlive && p.TargetObject == target);
                // What the raiders by the target fight instead (run 26: 205 raiders within 1,000 of a Magical Earthmender, 3 on it).
                string busyWith = string.Join(",", participants
                    .Where(p => p.IsAlive && p.TargetObject is GameLiving other && other != target && p.IsWithinRadius(target, 1_000))
                    .GroupBy(p => p.TargetObject.Name).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Key}:{g.Count()}"));
                log.Info($"REALM_EXPEDITION_TARGET_STATUS region={_region} target=\"{target.Name}\" level={target.Level} " +
                         $"health={target.HealthPercent}% inCombat={target.InCombat} raidersWithin1000={near} targeting={onIt} " +
                         $"at={target.X},{target.Y},{target.Z} spawnDistance={(int)target.GetDistanceTo(target.SpawnPoint)} busyWith=\"{busyWith}\"");
            }
            if (!Hold || Complete) { _holdSince = 0; return; }
            if (_holdSince == 0) { _holdSince = now; return; }
            if (now - _holdSince < HoldReportMilliseconds || now < _nextHoldLog) return;
            _nextHoldLog = now + HoldReportMilliseconds;
            int inside = participants.Count(p => p.IsAlive && p.CurrentRegionID == _region);
            log.Warn($"REALM_EXPEDITION_HOLD region={_region} minutes={(now - _holdSince) / 60_000} status=\"{Status}\" " +
                     $"front={(int)Front.X},{(int)Front.Y},{(int)Front.Z} target=\"{TargetName}\" inside={inside}" +
                     (_region == 60 ? $" apoc={ApocInitializator.DescribeState()}" : "") +
                     // Every raid dungeon (Krondon 2026-11-05: were Scurceol Hyrde's orbs alive, and why weren't they targets?).
                     (_region == 191 ? $" galladoria={DescribeLiveEncounters()}" : $" encounters={DescribeLiveEncounters()}") +
                     $" pinnedBy=\"{PinnedBy(participants)}\"");
        }

        /// <summary>The raid's main body (the leader with most raiders within 1,000), or "-" when it has none.</summary>
        private string MainBody(GameLiving[] participants) =>
            TryRegroupFront(participants.Where(p => p.IsAlive && p.CurrentRegionID == _region).ToArray(), out Vector3 body)
                ? $"{(int)body.X},{(int)body.Y},{(int)body.Z}" : "-";

        /// <summary>Epic encounters other than the target that raiders are fighting, with how many: what holds the raid.</summary>
        private string PinnedBy(GameLiving[] participants) => string.Join(",", participants
            .Where(p => p.IsAlive && p.CurrentRegionID == _region && p.TargetObject is GameNPC { IsAlive: true } npc && npc is IGameEpicNpc && npc != _target)
            .GroupBy(p => p.TargetObject.Name).OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key}:{g.Count()}"));

        // Run 28 (2026-10-20): Galladoria's raid waited 30 minutes at the Olcasgean staging with three primals dead and the
        // Water primal never a target. Every living encounter NPC of the region, and why the raid passes it by.
        private string DescribeLiveEncounters()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (GameNPC npc in WorldMgr.GetRegion(_region)?.Objects.OfType<GameNPC>() ?? [])
            {
                if (npc is not IGameEpicNpc || !npc.IsAlive) continue;
                string why = npc.ObjectState != GameObject.eObjectState.Active ? "inactive" :
                    npc.Realm != eRealm.None ? $"realm{(int)npc.Realm}" :
                    (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) != 0 ? $"flags{(int)npc.Flags}" :
                    _cleared.Contains(npc) ? "cleared" :
                    _clearedNames.Contains(npc.Name) && !_finalTypes.Contains(npc.GetType().Name) ? "clearedName" :
                    IsEncounterHazard(npc) ? "hazard" :
                    _routeRetry.TryGetValue(npc, out long retry) ? $"retry{(retry - GameLoop.GameLoopTime) / 1000}s" : "candidate";
                parts.Add($"{npc.Name}@{npc.X},{npc.Y},{npc.Z}:{npc.HealthPercent}%:{why}");
            }
            return $"deadPrimals={DOL.AI.Brain.OIBrain.DeadPrimalsCount} [{string.Join("; ", parts)}]";
        }

        /// <summary>
        /// Where the raid fights an encounter it turns on (REALM_EXPEDITION_TARGET_SWITCH): a walkable approach within 220
        /// units of it, proven from the front or a party leader, as for every other target. The switch used the encounter's
        /// own position: Marfach's Ancient Black Oak stands at 30091,37620,15049 and no party ever got a formation post
        /// there (all 535 camps the raid handed out lay 1,600-2,200 units away, at the muster), so the raid never walked
        /// in (2026-11-05). False, with the raw position, when no approach resolves.
        /// </summary>
        private bool TryEncounterApproach(GameNPC npc, GameLiving[] participants, out Vector3 approach) =>
            TryPointApproach(new(npc.X, npc.Y, npc.Z), participants, out approach);

        private bool TryPointApproach(Vector3 raw, GameLiving[] participants, out Vector3 approach)
        {
            approach = raw;
            var nav = PathfindingProvider.Instance;
            if (!nav.IsAvailable) return false;
            Vector3[] anchors = DistinctAnchors(Front, participants
                .Where(p => p.IsAlive && p.CurrentRegionID == _region && (p.Group == null || p.Group.LivingLeader == p))
                .Select(p => new Vector3(p.X, p.Y, p.Z)), 6);
            foreach (Vector3 anchor in anchors)
            {
                Zone zone = WorldMgr.GetRegion(_region)?.GetZone((int)anchor.X, (int)anchor.Y);
                if (zone != null && nav.HasNavmesh(zone) && AutonomousZonePointApproach.TryResolve(nav, zone, anchor, raw, 220, out Vector3 found))
                { approach = found; return true; }
            }
            return false;
        }

        /// <summary>The current target's tactic sends the raid to another encounter (EpicEncounterTactics): a required add
        /// of the boss, or the boss an encounter dies with. Logged as REALM_EXPEDITION_TACTIC for the run reports.</summary>
        private void SwitchByTactic(GameNPC to, string reason, GameLiving[] participants, long now, Vector3? stand = null)
        {
            Vector3 approach;
            bool resolved;
            if (stand is Vector3 ranged) { approach = ranged; resolved = true; }
            else
            {
                resolved = TryEncounterApproach(to, participants, out approach);
                // A flying add is fought from the floor under it (the approach search only looks 256 up or down).
                if (!resolved && (to.Flags & GameNPC.eFlags.FLYING) != 0)
                {
                    var nav = PathfindingProvider.Instance;
                    Zone zone = WorldMgr.GetRegion(_region)?.GetZone(to.X, to.Y);
                    if (zone != null && nav.IsAvailable && nav.HasNavmesh(zone) &&
                        nav.GetClosestPoint(zone, new Vector3(to.X, to.Y, to.Z), 128, 128, RangedFlyerReach, nav.DefaultFilters) is Vector3 below)
                    { approach = below; resolved = true; }
                }
            }
            GameNPC owner = EpicEncounterTactics.RequiredAddOwner(to, _live ?? []) ?? _target;
            log.Info($"REALM_EXPEDITION_TACTIC region={_region} from=\"{_target?.Name}\" to=\"{to.Name}\" health={to.HealthPercent} " +
                     $"front={(int)approach.X},{(int)approach.Y},{(int)approach.Z} approach={(stand != null ? "ranged-stand" : resolved ? "resolved" : "raw")} " +
                     $"reason=\"{reason}\" tactic={EpicEncounterTactics.For(owner)?.Encounter}");
            _target = to; TargetName = to.Name; TargetLevel = to.Level;
            Front = Destination = approach;
            _targetProgressTick = _targetStartTick = now; _targetLowestHealth = _targetLastHealth = to.Health;
            _targetClosest = float.MaxValue; _targetApproachTick = now;
            Hold = false; Status = "Clearing " + to.Name;
        }

        /// <summary>
        /// The required add of <paramref name="boss"/> the raid fights next, and the floor spot it fights it from
        /// (<see cref="TryRangedStand"/>): <paramref name="preferred"/> first, then the others nearest the front. Adds with
        /// no stand are set aside a minute each. False, with every add's reason, when none has one.
        /// </summary>
        private bool TryRequiredAddStand(GameNPC boss, GameNPC preferred, GameLiving[] participants, long now,
            out GameNPC add, out Vector3 stand, out string why)
        {
            add = null; stand = default;
            static Vector3 At(GameObject o) => new(o.X, o.Y, o.Z);
            GameNPC[] adds = EpicEncounterTactics.ShieldingAdds(boss, _live ?? [])
                .Where(a => _routeRetry.GetValueOrDefault(a) <= now)
                .OrderBy(a => a == preferred ? 0 : 1).ThenBy(a => Vector3.DistanceSquared(Front, At(a))).ToArray();
            if (adds.Length == 0) { why = "every required add was set aside a moment ago"; return false; }
            var nav = PathfindingProvider.Instance;
            Vector3[] anchors = DistinctAnchors(Front, participants
                .Where(p => p.IsAlive && p.CurrentRegionID == _region && (p.Group == null || p.Group.LivingLeader == p))
                .Select(At), 6);
            Vector3? Reach(Vector3 floor)
            {
                foreach (Vector3 anchor in anchors)
                {
                    Zone anchorZone = WorldMgr.GetRegion(_region)?.GetZone((int)anchor.X, (int)anchor.Y);
                    if (anchorZone != null && nav.HasNavmesh(anchorZone) &&
                        AutonomousZonePointApproach.TryResolve(nav, anchorZone, anchor, floor, 220, out Vector3 approach))
                        return approach;
                }
                return null;
            }
            var reasons = new List<string>();
            foreach (GameNPC a in adds)
            {
                Zone zone = WorldMgr.GetRegion(_region)?.GetZone(a.X, a.Y);
                if (TryRangedStand(nav, zone, At(a), At(boss), Reach, out stand, out string found))
                {
                    add = a; why = found;
                    EpicEncounterTactics.SetStand(boss, stand);
                    return true;
                }
                reasons.Add($"{a.Name}@{a.X},{a.Y},{a.Z}: {found}");
                _routeRetry[a] = now + 60_000;
            }
            why = string.Join("; ", reasons);
            return false;
        }

        /// <summary>No required add of <paramref name="boss"/> can be fought (no ranged raider sees one from the floor):
        /// the boss's tactic is set aside like an unreachable target, and tried again later.</summary>
        private void SetTacticAside(GameNPC boss, string why, long now)
        {
            log.Warn($"REALM_EXPEDITION_TACTIC_ASIDE region={_region} encounter=\"{boss.Name}\" at={boss.X},{boss.Y},{boss.Z} " +
                     $"tactic={EpicEncounterTactics.For(boss)?.Encounter} minutes={BlockedTargetRetryMilliseconds / 60_000} reason=\"{why}\"");
            _routeRetry[boss] = now + BlockedTargetRetryMilliseconds;
            _musterProgress.Remove(boss);
            if (_musterTarget == boss) _musterTarget = null;
            if (_target == boss || EpicEncounterTactics.RequiredAddOwner(_target, _live ?? []) == boss) _target = null;
        }

        private void AdvanceCore(GameLiving[] participants, long now)
        {
            if (Complete || Defeated) return;
            if (_routeStartTick == 0) _routeStartTick = now;
            GameNPC[] live = WorldMgr.GetRegion(_region)?.Objects.OfType<GameNPC>().ToArray() ?? [];
            _live = live;
            foreach (GameNPC stale in _routeRetry.Keys.Where(n => !n.IsAlive || n.ObjectState != GameObject.eObjectState.Active).ToArray())
                _routeRetry.Remove(stale);
            foreach (GameNPC npc in live.Where(n => _finalTypes.Contains(n.GetType().Name)))
                if (!_finals.TryGetValue(npc.GetType().Name, out var old) || old.IsAlive) _finals[npc.GetType().Name] = npc;
            if (_target != null && !_target.IsAlive)
            {
                log.Info($"REALM_EXPEDITION_TARGET_DEFEATED region={_region} target=\"{_target.Name}\" level={_target.Level} " +
                         $"minutes={(now - _targetStartTick) / 60_000.0:0.0}");
                _cleared.Add(_target);
                _musterProgress.Remove(_target);
                if (_target is IGameEpicNpc) _clearedNames.Add(_target.Name);
                _target = null;
            }
            bool finalDeaths = _finalTypes.All(type => _finals.TryGetValue(type, out var boss) && !boss.IsAlive);
            if (finalDeaths) { Complete = true; Status = "Final encounter defeated"; return; }
            if (_target?.IsAlive == true && _target.ObjectState == GameObject.eObjectState.Active &&
                EpicEncounterTactics.RouteTarget(_target, live, Front, out string tacticReason) is { } tacticTarget && tacticTarget != _target)
            {
                // A required add is fought from a floor spot its ranged raiders can reach and see it from, never walked to.
                if (EpicEncounterTactics.RequiredAddOwner(tacticTarget, live) is { } addOwner)
                {
                    if (TryRequiredAddStand(addOwner, tacticTarget, participants, now, out GameNPC add, out Vector3 stand, out string why))
                        SwitchByTactic(add, $"{tacticReason}; {why}", participants, now, stand);
                    else SetTacticAside(addOwner, why, now);
                }
                else SwitchByTactic(tacticTarget, tacticReason, participants, now);
            }
            if (_target?.IsAlive == true && _target.ObjectState == GameObject.eObjectState.Active)
            {
                if (TargetTookDamage(_target.Health, _targetLowestHealth, _targetLastHealth)) _targetProgressTick = now;
                _targetLowestHealth = Math.Min(_targetLowestHealth, _target.Health);
                _targetLastHealth = _target.Health;
                ReportHealthJump(participants, now);
                // The no-damage clock runs only while the raid is actually at its target. A raid
                // wiped or regrouping elsewhere skipped Prince Ba'alorien and Princess Nahemah at
                // 100% (run 11). Paused at most 20 minutes per target, so an unreachable one still goes.
                int near = participants.Count(p => p.IsAlive && p.CurrentRegionID == _region &&
                                                   p.IsWithinRadius(_target, TargetPresenceRadius));
                // Still closing in also pauses it: a raid walking a long way to its target is not stalled.
                float closest = participants.Where(p => p.IsAlive && p.CurrentRegionID == _region)
                    .Select(p => (float)p.GetDistanceTo(_target)).DefaultIfEmpty(float.MaxValue).Min();
                if (closest < _targetClosest - TargetApproachStepUnits) { _targetClosest = closest; _targetApproachTick = now; }
                bool approaching = now - _targetApproachTick < TargetApproachStallMilliseconds;
                if (near < MinimumRaidersAtTarget && (now - _targetStartTick < TargetAbsencePauseLimitMilliseconds || approaching))
                    _targetProgressTick = Math.Max(_targetProgressTick, now - 1_000);
                // A tethered boss outside its tether is immune while the raid leads it home: not a stall.
                if (_target is GameNPC tethered && (AutonomousTetherReset.IsOutOfTether(tethered) || EpicEncounterTactics.OutsideHome(tethered)))
                {
                    _targetProgressTick = Math.Max(_targetProgressTick, now - 1_000);
                    Hold = false; Status = $"Drawing {_target.Name} back to its lair ({_target.HealthPercent}%)"; return;
                }
                // Never switch a live grind objective because another party has not
                // arrived, but never hold the whole expedition on a target nobody damages.
                // An epic encounter fighting our raiders comes before one we are still gathering for.
                if (!_target.InCombat && HostileEncounter(live, participants) is { } hostile)
                {
                    GameNPC attacker = EpicEncounterTactics.RouteTarget(hostile, live, Front, out string hostileTactic);
                    if (attacker != hostile)
                        log.Info($"REALM_EXPEDITION_TACTIC region={_region} encounter=\"{hostile.Name}\" fights=\"{attacker.Name}\" " +
                                 $"reason=\"{hostileTactic}\" tactic={EpicEncounterTactics.For(hostile)?.Encounter}");
                    bool resolved = TryEncounterApproach(attacker, participants, out Vector3 approach);
                    log.Warn($"REALM_EXPEDITION_TARGET_SWITCH region={_region} from=\"{_target.Name}\" to=\"{attacker.Name}\" level={attacker.Level} " +
                             $"front={(int)approach.X},{(int)approach.Y},{(int)approach.Z} approach={(resolved ? "resolved" : "raw")} " +
                             $"reason=\"it is fighting the raid\"");
                    _target = attacker; TargetName = attacker.Name; TargetLevel = attacker.Level;
                    Front = Destination = approach;
                    _targetProgressTick = _targetStartTick = now; _targetLowestHealth = _targetLastHealth = attacker.Health;
                    _targetClosest = float.MaxValue; _targetApproachTick = now;
                    Hold = false; Status = "Clearing " + attacker.Name; return;
                }
                if (IsMusteredEncounter(_target) && MusterForFinal(participants, now))
                {
                    if (!_musterUnreachable) return;
                    if (_setAsideLine != null)
                        log.Warn(_setAsideLine);
                    else
                        log.Warn($"REALM_EXPEDITION_MUSTER_UNREACHABLE region={_region} target=\"{_target.Name}\" " +
                                 $"at={(int)_musterPoint.X},{(int)_musterPoint.Y},{(int)_musterPoint.Z} minutes={MusterUnreachableMilliseconds / 60_000} " +
                                 $"reason=\"nobody reached the muster; the encounter is set aside and tried again later\"");
                    _routeRetry[_target] = now + BlockedTargetRetryMilliseconds;
                    _musterProgress.Remove(_target);
                    _musterTarget = null;
                    _target = null;
                }
                if (_target != null)
                {
                bool stalled = ShouldSkipTarget(_finalTypes.Contains(_target.GetType().Name), now - _targetProgressTick);
                // A named encounter that stopped taking damage because the raid around it fell is a wipe, not a
                // logistics problem: the raid regroups (or is defeated) instead of walking past it. While the raid
                // regroups, the few still fighting it are not a stall either.
                if (stalled && IsMusteredEncounter(_target) && (_regrouping || _musterEngaged && EndAttempt(now, "no damage for five minutes")))
                {
                    _targetProgressTick = now;
                    if (Defeated) return;
                    stalled = false;
                }
                if (!stalled)
                {
                    CallTarget(participants, now);
                    Hold = false; Status = $"Clearing {_target.Name} ({_target.HealthPercent}%)"; return;
                }
                log.Warn($"REALM_EXPEDITION_TARGET_SKIPPED region={_region} target=\"{_target.Name}\" at={_target.X},{_target.Y},{_target.Z} " +
                         $"health={_target.HealthPercent} front={(int)Front.X},{(int)Front.Y},{(int)Front.Z} near={near} reason=\"no damage for five minutes\"");
                _routeRetry[_target] = now + BlockedTargetRetryMilliseconds;
                _musterProgress.Remove(_target);
                }
            }
            _target = null;
            GameLiving[] inside = participants.Where(p => p.IsAlive && p.CurrentRegionID == _region).ToArray();
            if (AdvanceChain(inside, now)) return;
            bool atFront = inside.Any(p => p.IsWithinRadius(new Point3D((int)Front.X, (int)Front.Y, (int)Front.Z), 1000));
            if (inside.Length >= 8 && !atFront)
            {
                // The front can be left on a spot nobody reaches: Tuscaran Glacier's griffon icewing
                // was skipped and its approach stayed the front, so 268 raiders inside "waited for
                // formed parties" for eight hours (2026-10-12). After a few minutes the front moves to
                // where most raiders stand, and the next encounter is searched from there.
                if (_frontAbsentSince == 0) _frontAbsentSince = now;
                // A long climb is not a stall: Caer Sidi's final staging is three proven path legs up from the raid, and
                // the front was moved back down after three minutes with the raid still on its way (run 22).
                if (_frontClosestFor != Front) { _frontClosestFor = Front; _frontClosest = float.MaxValue; }
                float closestToFront = float.MaxValue;
                foreach (GameLiving p in inside)
                    closestToFront = Math.Min(closestToFront, Vector3.Distance(new(p.X, p.Y, p.Z), Front));
                if (closestToFront < _frontClosest - TargetApproachStepUnits)
                {
                    _frontClosest = closestToFront;
                    _frontAbsentSince = now;
                }
                if (now - _frontAbsentSince >= FrontRegroupMilliseconds && TryRegroupFront(inside, out Vector3 regrouped))
                {
                    log.Warn($"REALM_EXPEDITION_FRONT_REGROUP region={_region} from={(int)Front.X},{(int)Front.Y},{(int)Front.Z} " +
                             $"to={(int)regrouped.X},{(int)regrouped.Y},{(int)regrouped.Z} inside={inside.Length} " +
                             $"reason=\"nobody reached the front for {(now - _frontAbsentSince) / 60_000} minutes\"");
                    Front = Destination = regrouped;
                    _frontAbsentSince = 0;
                    atFront = true;
                }
            }
            else _frontAbsentSince = 0;
            if (inside.Length < 8 || !atFront)
            { Hold = true; Destination = Front; Status = "Waiting for formed parties inside"; return; }
            var nav = PathfindingProvider.Instance;
            Zone zone = WorldMgr.GetRegion(_region)?.GetZone((int)Front.X, (int)Front.Y);
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            { Hold = true; Status = "Blocked: dungeon navigation unavailable"; return; }
            GameNPC[] encounters = live.Where(n => n is IGameEpicNpc && n.IsAlive && n.ObjectState == GameObject.eObjectState.Active).ToArray();
            GameNPC[] candidates = live.Where(n => n.IsAlive && n.ObjectState == GameObject.eObjectState.Active && n.Realm == eRealm.None &&
                n.Level > 0 && n is not GameBot && n is not GameSummonedPet && n.Brain is not IControlledBrain &&
                n is not GameMerchant && n is not GameTrainer && n is not GameTeleporter && n is not GameTaxi &&
                ((n.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0 || EpicEncounterTactics.FinishableUntargetable(n)) &&
                !_cleared.Contains(n) &&
                // A pet class's charm body (a copy of an overland monster it is charming) is no dungeon monster: a
                // Hibernian raid stood four minutes by a "grogan" in Galladoria that none of it could attack (run 22).
                n.TempProperties.GetProperty<bool>(AutonomousPetSupport.SyntheticCharmPetProperty) != true &&
                !IsEncounterHazard(n) &&
                !(n is IGameEpicNpc && _clearedNames.Contains(n.Name) && !_finalTypes.Contains(n.GetType().Name)) &&
                // A neutral expedition goes for its named encounters only; monsters on the way are
                // fought when they attack (Darkness Falls holds 2,465 spawns).
                // A play test's focus leaves out the other named encounters (and the final, unless named) while they are out of
                // combat; the way in is cleared as in any raid (a focus that skipped the first-hour clear left the Caer Sidi raid
                // fighting the entrance's monsters and never reaching the horsemen's room), and a named that fights the raid is
                // fought (the Host killed 159 raiders on the way up while the focus left him out, 2026-11-07).
                !(Focus != null && (n is IGameEpicNpc || _finalTypes.Contains(n.GetType().Name)) && !n.InCombat &&
                  !InFocus(Focus, n.Name, n.GetType().Name)) &&
                (_objectives == null ? now - _routeStartTick < EpicTrashPhaseMilliseconds || n is IGameEpicNpc || _finalTypes.Contains(n.GetType().Name) ||
                        IsEncounterAdd(n, Front, encounters)
                    : RealmRaidNeutralEvents.IsObjective(_objectives, n.Name)))
                .OrderBy(n => _finalTypes.Contains(n.GetType().Name) ? 1 : 0)
                .ThenBy(n => IsEncounterAdd(n, Front, encounters) ? 0 : 1)
                .ThenBy(n => Vector3.DistanceSquared(Front, new(n.X, n.Y, n.Z))).ToArray();
            // Probe from the front and from where the parties actually stand, near or far:
            // Tuscaran's front once sat on a pad under the floor its parties stood on.
            Vector3[] anchors = DistinctAnchors(Front, inside
                    .Where(p => p.Group == null || p.Group.LivingLeader == p)
                    .Select(p => new Vector3(p.X, p.Y, p.Z)), 6);
            int probes = 0;
            // Resume after the last examined candidate. With more blocked
            // spawns than the per-minute probe budget, always starting at zero
            // would retry the same expired failures and starve later rooms.
            for (int examined = 0; examined < candidates.Length && probes < 3; examined++)
            {
                GameNPC npc = candidates[_probeCursor % candidates.Length];
                _probeCursor = (_probeCursor + 1) % candidates.Length;
                if (_routeRetry.GetValueOrDefault(npc) > now) continue;
                // A boss shielded by its adds, or one that dies with another, is fought through its tactic (EpicEncounterTactics).
                if (EpicEncounterTactics.RouteTarget(npc, live, Front, out string candidateTactic) is { } tactical && tactical != npc)
                {
                    log.Info($"REALM_EXPEDITION_TACTIC region={_region} encounter=\"{npc.Name}\" fights=\"{tactical.Name}\" " +
                             $"reason=\"{candidateTactic}\" tactic={EpicEncounterTactics.For(npc)?.Encounter}");
                    npc = tactical;
                    // A required add set aside a minute is replaced by another of its boss's adds below.
                    if (_routeRetry.GetValueOrDefault(npc) > now && EpicEncounterTactics.RequiredAddOwner(npc, live) == null) continue;
                }
                // A required add (Scurceol's floating orbs) never goes through the ground-route gate below: before its boss's
                // fight is on the raid musters for the boss; once it is on, the add is fought from a ranged floor stand.
                if (EpicEncounterTactics.RequiredAddOwner(npc, live) is { } addOwner)
                {
                    if (_routeRetry.GetValueOrDefault(addOwner) > now) continue;
                    if (!EpicEncounterTactics.FightOn(addOwner, live)) npc = addOwner;
                    else
                    {
                        probes++;
                        if (TryRequiredAddStand(addOwner, npc, participants, now, out GameNPC add, out Vector3 stand, out string why))
                        {
                            SwitchByTactic(add, $"required-add of \"{addOwner.Name}\"; {why}", participants, now, stand);
                            _probeCursor = 0; _blockedSince = 0;
                            return;
                        }
                        SetTacticAside(addOwner, why, now);
                        continue;
                    }
                }
                probes++;
                Vector3 raw = AutonomousDungeonGoalCatalog.TryGet(npc, out var point) ? point.Position : new(npc.X, npc.Y, npc.Z);
                // A flyer is approached on the floor under it (the approach search only looks 256 up or down).
                if ((npc.Flags & GameNPC.eFlags.FLYING) != 0 &&
                    nav.GetClosestPoint(zone, raw, 128, 128, RangedFlyerReach, nav.DefaultFilters) is Vector3 below && raw.Z - below.Z > 256)
                    raw = below;
                Vector3 approach = default;
                bool reachable = false;
                foreach (Vector3 anchor in anchors)
                {
                    Zone anchorZone = WorldMgr.GetRegion(_region)?.GetZone((int)anchor.X, (int)anchor.Y) ?? zone;
                    if (nav.HasNavmesh(anchorZone) && AutonomousZonePointApproach.TryResolve(nav, anchorZone, anchor, raw, 220, out approach))
                    { reachable = true; break; }
                }
                if (!reachable && _region == AutonomousDarknessFallsPolicy.RegionId && _objectives != null &&
                    TryDarknessFallsChain(nav, zone, raw, out Vector3[] chain))
                {
                    // Too far for one proven path: walk the certified waypoint chain to it. Run 18: the chain
                    // always began at its first waypoint by the realm entrance; a raid already down on Oro's floor
                    // (one-way drops) could not path back up, and 300 raiders looped on NoPath for half an hour.
                    // Start at the furthest waypoint the parties can reach; with none, set the target aside.
                    int start = ReachableChainStart(nav, chain, inside);
                    if (start < 0)
                    { _routeRetry[npc] = now + 60_000; continue; }
                    _chain = chain; _chainIndex = start; _chainTarget = npc;
                    TargetName = npc.Name; TargetLevel = npc.Level; _probeCursor = 0; _blockedSince = 0;
                    AdvanceChain(inside, now);
                    return;
                }
                if (!reachable)
                { _routeRetry[npc] = now + 60_000; continue; }
                // A flyer hovering far above its floor approach cannot be meleed, but one within spell and bow reach is
                // shot down from below (2026-10-16: Galladoria's Corrupt Primal of Air circles 690 above the floor and never
                // fights; skipped, it never died, and Olcasgean wakes only when all four primals are dead).
                if (IsOutOfRangedReach(npc.Flags, npc.Z, (int)approach.Z))
                { _routeRetry[npc] = now + BlockedTargetRetryMilliseconds; continue; }
                _target = npc; Front = Destination = approach; TargetName = npc.Name; TargetLevel = npc.Level;
                _targetProgressTick = _targetStartTick = now; _targetLowestHealth = _targetLastHealth = npc.Health;
                _targetClosest = float.MaxValue; _targetApproachTick = now;
                _probeCursor = 0; _blockedSince = 0;
                Hold = false; Status = "Clearing " + npc.Name; return;
            }
            Hold = true;
            if (candidates.Any(n => !_routeRetry.ContainsKey(n) || !CanDeferAirbornePatrol(n.GetType(), n.Flags)))
            {
                Status = "Checking blocked room approaches; no false completion";
                if (_blockedSince == 0) _blockedSince = now;
                if (BlockedTooLong(_blockedSince, now) && !Blocked)
                {
                    Blocked = true;
                    log.Warn($"REALM_EXPEDITION_ROUTE_BLOCKED region={_region} front={(int)Front.X},{(int)Front.Y},{(int)Front.Z} " +
                             $"remaining={candidates.Length} nearest=\"{candidates.FirstOrDefault()?.Name}\" at={candidates.FirstOrDefault()?.X},{candidates.FirstOrDefault()?.Y},{candidates.FirstOrDefault()?.Z} " +
                             $"reason=\"no remaining encounter reachable from the front or any party for 20 minutes\"");
                }
                return;
            }
            _blockedSince = 0;
            // The encounter controller must see a real party at its native trigger.
            // Waiting through dialogue/phase timers is not an empty-camp failure.
            if (AutonomousZonePointApproach.TryResolve(nav, zone, Front, _trigger, 220, out Vector3 trigger))
                Front = Destination = trigger;
            TargetName = "Final encounter staging";
            Status = candidates.Length == 0 ? "Waiting for the next scripted encounter phase" :
                $"Waiting for the next scripted encounter phase; {candidates.Length} airborne patrols remain";
        }
    }
}
