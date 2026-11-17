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

        /// <summary>
        /// The monster came back to life after this route began (pure): its SpawnTick, set by every respawn
        /// (GameNPC.RespawnTimerCallback) and 0 for a monster still on its first life, is later than the route's start.
        /// </summary>
        public static bool RespawnedSinceRouteStart(long spawnTick, long routeStartTick) =>
            routeStartTick > 0 && spawnTick > routeStartTick;

        /// <summary>
        /// The first-hour sweep takes this monster (pure): the sweep is on, and the monster has not come back since the route
        /// began. Owner's PC, 2026-11-15 (build 1dc66b6, Krondon): after Drihten Elreden died at 03:35 the raid killed only
        /// thrawn ogre bocere in his rooms (42 bocere spawns, 180 s respawn) until the log ended at 03:48: 101 kills in 12
        /// minutes, every one the walk-nearest monster to the front, because the ones it had killed were back before it had
        /// finished the rest. A monster that came back is no sweep target: it is fought when it attacks, and it is still a
        /// target as a boss's clear-first monster, an encounter's add or an objective.
        /// Except on the way back (owner, 2026-11-15): one that came back on the walk from where the raid comes in from its
        /// staging post to the current target, or within its aggro range of that walk (<see cref="OnWalkBack"/>), is cleared
        /// again, because raiders coming back after a wipe walk that way. Only the ones off that walk are left out.
        /// </summary>
        public static bool SweepTarget(long now, long routeStartTick, long spawnTick, IReadOnlyList<Vector3> walkBack = null,
            Vector3 at = default, int aggroRange = 0) =>
            now - routeStartTick < EpicTrashPhaseMilliseconds &&
            (!RespawnedSinceRouteStart(spawnTick, routeStartTick) || OnWalkBack(walkBack, at, aggroRange));

        /// <summary>
        /// The route is in its first-hour trash clear (pure): an epic dungeon route (no neutral objectives) that has begun and
        /// is less than <see cref="EpicTrashPhaseMilliseconds"/> old. Owner's PC, 2026-11-16 (build 88b6ab3, sweep): Tur Suil's
        /// raid cleared trash from 10:47 to 11:47 (114 kills, no raider lost, no named targeted yet) and the 60-minute stall clock,
        /// started at the same moment, ended it at 11:47:56 just as the clear finished. Owner's decision (2026-11-16, lifting the
        /// raid-code freeze for this): the clear counts as progress, so the stall clock starts when it ends.
        /// </summary>
        public static bool InTrashPhase(long now, long routeStartTick, bool hasObjectives) =>
            !hasObjectives && routeStartTick > 0 && now - routeStartTick < EpicTrashPhaseMilliseconds;

        /// <summary>This route is in its first-hour trash clear now (<see cref="InTrashPhase"/>).</summary>
        public bool ClearingTrash(long now) => InTrashPhase(now, _routeStartTick, _objectives != null);

        /// <summary>
        /// A monster at <paramref name="at"/> stands on the walk (within <see cref="RealmRaidRouteGate.Margin"/> of it, on its
        /// floor) or within its <paramref name="aggroRange"/> of it (pure; <see cref="RealmRaidRouteGate.Blocks"/>). A monster
        /// that does not aggro counts only when it stands on the walk.
        /// </summary>
        public static bool OnWalkBack(IReadOnlyList<Vector3> walkBack, Vector3 at, int aggroRange) =>
            RealmRaidRouteGate.Blocks(walkBack, at, Math.Max(aggroRange, 1));

        /// <summary>
        /// The walk from <paramref name="arrival"/> (where raiders come in from their staging post) to <paramref name="front"/>
        /// (the current target's approach), pure: the path's corners between the two ends. Without a path (none found, or the
        /// ends in different zones) it is the straight line, so a monster near it is cleared rather than left in the way.
        /// </summary>
        public static Vector3[] WalkBackOf(Vector3 arrival, Vector3 front, IReadOnlyList<Vector3> corners)
        {
            var walk = new List<Vector3> { arrival };
            if (corners != null)
                foreach (Vector3 corner in corners)
                    if (Vector3.DistanceSquared(walk[^1], corner) > 1f) walk.Add(corner);
            if (Vector3.DistanceSquared(walk[^1], front) > 64 * 64) walk.Add(front);
            return walk.ToArray();
        }

        /// <summary>The walk back to the front (<see cref="WalkBackOf"/>), found again only once the front has moved.</summary>
        private Vector3[] WalkBack(IPathfindingMgr nav)
        {
            if (_walkBack != null && Vector3.DistanceSquared(_walkBackFront, Front) <= 64 * 64) return _walkBack;
            _walkBackFront = Front;
            Region region = WorldMgr.GetRegion(_region);
            Zone from = region?.GetZone((int)_arrival.X, (int)_arrival.Y), to = region?.GetZone((int)Front.X, (int)Front.Y);
            Vector3[] corners = null;
            if (from != null && from == to && nav.IsAvailable && nav.HasNavmesh(from))
            {
                Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[256];
                PathfindingResult path = nav.GetPathStraight(from, _arrival, Front, nav.DefaultFilters, nodes);
                if (path.Status is PathfindingStatus.PathFound or PathfindingStatus.PartialPathFound)
                {
                    corners = new Vector3[Math.Min(path.NodeCount, nodes.Length)];
                    for (int i = 0; i < corners.Length; i++) corners[i] = nodes[i].Position;
                }
            }
            return _walkBack = WalkBackOf(_arrival, Front, corners);
        }

        // Where raiders come in (the route's first front, the inside arrival of the crossing from the staging post), and the
        // walk from there to the front, worked out again when the front moves (WalkBack).
        private readonly Vector3 _arrival;
        private Vector3[] _walkBack;
        private Vector3 _walkBackFront;
        private long _nextRespawnsLeftLog;

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
        // When a tactic first held each monster's pull (EpicEncounterTactics.PullWait). A hold that lasts longer than
        // PullWaitLimitMilliseconds is dropped, so an add the raid can never reach can't keep a boss waiting for ever.
        private readonly Dictionary<GameNPC, long> _pullWaitSince = new();
        public const long PullWaitLimitMilliseconds = 20 * 60_000;

        /// <summary>The tactic's reason to hold <paramref name="npc"/>'s pull (EpicEncounterTactics.PullWait), or null once
        /// it has held for <see cref="PullWaitLimitMilliseconds"/>: the raid then pulls as it would without the tactic.</summary>
        private string HeldByTactic(GameNPC npc, IReadOnlyList<GameNPC> live, long now)
        {
            string why = EpicEncounterTactics.PullWait(npc, live, now);
            if (why == null) { _pullWaitSince.Remove(npc); return null; }
            if (!_pullWaitSince.TryGetValue(npc, out long since)) { _pullWaitSince[npc] = now; return why; }
            if (!PullWaitExpired(since, now)) return why;
            if (since != long.MinValue)
            {
                _pullWaitSince[npc] = long.MinValue;
                log.Info($"REALM_EXPEDITION_PULL_WAIT_EXPIRED region={_region} target=\"{npc.Name}\" minutes={(now - since) / 60_000} reason=\"{why}\"");
            }
            return null;
        }

        /// <summary>A tactic's hold that began at <paramref name="since"/> no longer holds (pure; long.MinValue = already expired).</summary>
        public static bool PullWaitExpired(long since, long now) => since == long.MinValue || now - since >= PullWaitLimitMilliseconds;
        private readonly Dictionary<string, GameNPC> _finals = new();
        private GameNPC _target;
        private int _probeCursor;
        // From rambodolphin13/DividedRealms (RealmRaidDungeonRoute.cs, DR-dev-0.1g), whose overnight 4,500-bot run saw the
        // next-encounter search spend 7.7 s in one game tick after "olcasar deepstalker" died in Galladoria: a candidate no
        // party can reach is tried from up to six anchors, each up to about forty corridor searches, three candidates a pass.
        // The search now stops once it has spent its time for this pass and goes on from the same candidate and anchor on the
        // next one.
        public const double ProbeBudgetMilliseconds = 100;
        private GameNPC _probeResumeNpc;
        private int _probeResumeAnchor;

        /// <summary>The next-encounter search has used its time for this pass (pure).</summary>
        public static bool ProbeBudgetSpent(double elapsedMilliseconds) => elapsedMilliseconds >= ProbeBudgetMilliseconds;

        /// <summary>The anchor a candidate's search starts from: where it stopped last pass for the same candidate, else the
        /// first (pure).</summary>
        public static int FirstProbeAnchor(bool sameCandidate, int resumeAnchor, int anchors) =>
            sameCandidate ? Math.Clamp(resumeAnchor, 0, anchors) : 0;
        private long _nextClearFirstLog;
        /// <summary>When the raid began mustering for its final encounter (0: not yet; Darkness Falls' late objectives).</summary>
        private long _finalMusterSince;
        private long _nextReclearCheck;
        private readonly HashSet<GameNPC> _lateObjectivesLogged = new();
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private long _targetProgressTick, _targetStartTick, _holdSince, _nextHoldLog;
        // Lowest health points seen (not percent: a percent of Galladoria's 125,000-health Corrupt Primal of Fire is 1,250
        // points, and a few raiders wearing him down counted as no damage and he was skipped, run 23).
        private int _targetLowestHealth;
        // Health at the last look: a boss that heals itself (Easmarach takes an eighth back on the waterfall top each time he
        // falls to 30%) never goes below its earlier lowest while the raid wears him down from 42% to 35%, and was skipped
        // "no damage for five minutes" with the raid hitting him (play test, 2026-11-07).
        private int _targetLastHealth;

        /// <summary>The named a raid turns on (pure): with no target, the one attacking it, if any; while it fights a lesser
        /// named on its way (an epic NPC, not a boss), an epic boss attacking it. Otherwise the old rule decides (it switches to
        /// an attacker only when the target itself is out of combat). Play test duo4, 2026-11-07: Caer Sidi's raid fought
        /// cursed lacula, black revenant and the archlich on the lower floor while the Host killed 52 of it, and wiped.</summary>
        public static GameNPC TurnsOnAttacker(GameNPC target, GameNPC attacker) =>
            target == null ? attacker : attacker is GameEpicBoss && target is not GameEpicBoss && attacker != target ? attacker : null;

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
        // Everyone in the attempt seen fallen during it (dead, released or gone), though rezzed or sent back in since.
        private readonly HashSet<GameLiving> _fellInAttempt = new();
        private int _attemptLowestPercent = 100;
        private bool _regrouping;
        private long _regroupSince;
        private readonly Queue<(string Name, int Failures, int Engaged, int Fallen)> _wipes = new();
        /// <summary>The encounter that defeated the raid (two failed attempts); null while it can still win.</summary>
        public string DefeatedAt { get; private set; }
        public bool Defeated => DefeatedAt != null;
        /// <summary>Named encounters this expedition killed (partial success when it is defeated later).</summary>
        public IReadOnlyCollection<string> NamedDefeated => _clearedNames;
        public int FailedAttempts(string name) => _failedAttempts.GetValueOrDefault(name);

        /// <summary>Named and bosses down, wipes so far, and the named or boss the raid fights now (the raid's stall rule).</summary>
        public int NamedDefeatedCount => _clearedNames.Count;
        public int WipesSoFar => _failedAttempts.Values.Sum();
        public GameNPC NamedTarget => _target is IGameEpicNpc && _target.IsAlive ? _target : null;

        /// <summary>
        /// An attempt on a named encounter failed when at least eight raiders were in it and half of them fell (dead,
        /// released or gone from the dungeon), or when the encounter healed back to 95% after being taken to
        /// <see cref="ResetWipeLowestPercent"/> or lower (it reset: nobody was left fighting it).
        /// </summary>
        public static bool IsWipe(int engaged, int fallen, int healthPercent, int lowestPercent) =>
            engaged >= MinimumRaidersAtTarget && (fallen * 2 >= engaged || healthPercent >= 95 && lowestPercent <= ResetWipeLowestPercent);

        /// <summary>How low a reset encounter must have been for the reset to count as the raid's wipe: 70% (play test
        /// 2026-11-07: a calcified manes, a named trash monster on Caer Sidi's way, taken to 84% and left by 91 of 95 living
        /// raiders, was counted as the raid's first wipe; the next such brush would have defeated a raid that had lost nobody).
        /// A boss the raid wore down and then lost (Spindler Broodmother from 42%) still counts.</summary>
        public const int ResetWipeLowestPercent = 70;

        /// <summary>
        /// A failed attempt counts against the raid (a wipe: one of its two tries, Special Forces called) only when it was the
        /// raid's: at least half the raid was in it, or half the raid fell. Aidon the Archwizard, 2026-11-05: 31 raiders were
        /// drawn into a fight before the muster and all fell, while 56 others were elsewhere; that was counted as the raid's
        /// first wipe. Now it is a lost skirmish: the raid regroups and rests, but no attempt is used.
        /// </summary>
        public static bool IsWipe(int engaged, int fallen, int healthPercent, int lowestPercent, int raidAtStart) =>
            IsWipe(engaged, fallen, healthPercent, lowestPercent) &&
            (raidAtStart <= 0 || engaged * 2 >= raidAtStart || fallen * 2 >= raidAtStart);

        /// <summary>
        /// The raid's size for <see cref="IsWipe(int, int, int, int, int)"/>: its living members wherever they are, not only
        /// those inside the dungeon. Caer Sidi, 2026-11-12 (owner's PC, 152 raiders): after a real wipe at Host most of the
        /// raid had released and was walking back, so only a few stood inside; eight of them pulled Host early, six fell,
        /// and against the handful inside that was "half the raid": it was scored as the second failed attempt and the raid
        /// was defeated with 141 of 152 alive. Raiders on their way back are still the raid.
        /// </summary>
        public static int AttemptRaidSize(int livingInside, int livingAnywhere) => Math.Max(livingInside, livingAnywhere);

        /// <summary>
        /// How many of an attempt's raiders fell in it (pure): those fallen now and those seen fallen during it. Tuscaran
        /// Glacier, 2026-11-15 (owner's PC, build 1dc66b6, 100 Midgard raiders): 85 raiders died 104 times on Icelord Kvasir in
        /// 18 minutes, but the return waves had sent the released ones back in by the time he reset (82% to 100%), so the
        /// count at the end found almost nobody fallen and the attempt ended with no wipe and no line at all.
        /// </summary>
        public static int AttemptFallen<T>(IEnumerable<T> engaged, ICollection<T> fellDuring, Func<T, bool> fallenNow) =>
            engaged?.Count(p => fellDuring?.Contains(p) == true || fallenNow(p)) ?? 0;

        private bool FallenNow(GameLiving p) =>
            !p.IsAlive || p.CurrentRegionID != _region || p is GameBot { IsReturningAfterRelease: true };

        /// <summary>Remembers the attempt's raiders who are fallen this turn.</summary>
        private void NoteFallen()
        {
            foreach (GameLiving p in _engaged)
                if (FallenNow(p)) _fellInAttempt.Add(p);
        }

        /// <summary>
        /// The encounter reset while the raid still stood: it healed back to 95% after being taken to
        /// <see cref="ResetWipeLowestPercent"/> or lower, and fewer than a quarter of the raiders in the attempt fell.
        /// Darkness Falls, 2026-11-12 (owner's PC, Midgard, 80 raiders): High Lord Oro was taken to 59% while the raid
        /// fought the inquisitors and knights around him, nobody stayed on him and he reset; three raiders had fallen and
        /// it was scored as the raid's first wipe of two. The first <see cref="FreeEncounterResets"/> such resets of an
        /// encounter use no attempt: the raid musters again and goes in together. After that they count, so an encounter
        /// the raid can never hold still ends the raid.
        /// </summary>
        public static bool IsResetWithRaidStanding(int engaged, int fallen, int healthPercent, int lowestPercent) =>
            engaged >= MinimumRaidersAtTarget && fallen * 4 < engaged && healthPercent >= 95 && lowestPercent <= ResetWipeLowestPercent;
        public const int FreeEncounterResets = 2;

        /// <summary>
        /// The attempt's encounter was killed and has come back since the attempt's pull (pure): its SpawnTick, set by every
        /// respawn (GameNPC.Spawn), is later than the pull. Then that attempt was won, and nothing about the new life (at full
        /// health, out of combat) is a reset or a wipe. Owner's PC, 2026-11-16 (build ccba364, /epictest sweep, Darkness
        /// Falls Albion): Chthonic Knight Ukobat, on the raid's trail, was killed about nine times; each time he came back
        /// (3-4 minutes later) the route still held the attempt of his last life open and scored his 100% against that life's
        /// low: two free resets (22:37:13 lowest=18, 22:46:23 lowest=66), then REALM_EXPEDITION_WIPE failures=1 engaged=54
        /// fallen=0 lowest=14 (22:55:28) and failures=2 engaged=68 fallen=2 lowest=68 (23:07:42), and the raid ended
        /// "Defeated at Chthonic Knight Ukobat" with 87 of 88 alive. Each followed a REALM_EXPEDITION_TARGET_DEFEATED.
        /// </summary>
        public static bool KilledSinceAttempt(long attemptStartTick, long spawnTick) =>
            attemptStartTick > 0 && spawnTick > attemptStartTick;
        private readonly Dictionary<string, int> _freeResets = new(StringComparer.Ordinal);

        /// <summary>Raiders a regroup waits for: 70% of the raid's roster (40% after 20 minutes), 8 to 150.</summary>
        public static int RegroupNeeded(int roster, long regroupMilliseconds) =>
            Math.Max(MinimumRaidersAtTarget, Math.Min(FinalMusterMaximum, (int)Math.Ceiling(roster *
                (regroupMilliseconds >= FinalMusterPatienceMilliseconds ? RegroupPatientShare : RegroupShare))));

        /// <summary>The next wipe the raid has not reacted to yet (announcement, reinforcements).</summary>
        public bool TryTakeWipe(out string name, out int failures) => TryTakeWipe(out name, out failures, out _, out _);

        /// <summary>The next wipe, with the raiders who were in it and how many of them fell (the REALM_RAID_WIPE line).</summary>
        public bool TryTakeWipe(out string name, out int failures, out int engaged, out int fallen)
        {
            if (_wipes.TryDequeue(out var wipe))
            {
                name = wipe.Name; failures = wipe.Failures; engaged = wipe.Engaged; fallen = wipe.Fallen;
                return true;
            }
            name = null; failures = 0; engaged = 0; fallen = 0;
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
            // Never a ground hazard or a boss's required add: PC 2026-11-09 (forced Krondon), the raid turned on Orshom's Fire
            // (a fire pit only Cold hurts) and 100 raiders burned in it; and 11 s after the tactic sent it to a floor stand for
            // one of Scurceol Hyrde's orbs it switched to another orb "fighting the raid", with its raw spot in the air as the
            // front (111 route recoveries aimed into the air). Both are fought through their tactic, not as an encounter.
            // A boss comes before a lesser named (the Host before the trash around him, play test duo4).
            return live.Where(n => n != _target && n is IGameEpicNpc && n.IsAlive && n.InCombat &&
                                   !EpicEncounterTactics.Covers(n, _target, live) &&
                                   !IsEncounterHazard(n) && !EpicEncounterTactics.IsRequiredAddOfLiveBoss(n, live) &&
                                   _routeRetry.GetValueOrDefault(n) <= now &&
                                   n.ObjectState == GameObject.eObjectState.Active && !_clearedNames.Contains(n.Name) &&
                                   n.TargetObject is GameLiving victim && raiders.Contains(victim))
                       .OrderByDescending(n => n is GameEpicBoss).FirstOrDefault();
        }

        private bool IsMusteredEncounter(GameNPC npc) =>
            (npc is IGameEpicNpc || _finalTypes.Contains(npc.GetType().Name)) && !IsEncounterHazard(npc) &&
            // A boss's required adds are fought as part of its fight, without a muster of their own (EpicEncounterTactics).
            !EpicEncounterTactics.IsRequiredAddOfLiveBoss(npc, _live);
        // The region's NPCs as of this turn (AdvanceCore), for the tactic checks.
        private IReadOnlyList<GameNPC> _live = [];

        // The raid's living members when the attempt began (IsWipe's raid share; AttemptRaidSize), and the participants of this turn.
        private int _attemptRaidSize;
        private GameLiving[] _attemptParticipants;

        /// <summary>The boss is fighting the few raiders it caught before the muster (EpicEncounterTactics.EarlyPull.Hold):
        /// they fight it, the rest of the raid gathers at the muster and nobody else is sent in until enough are there.</summary>
        private bool _skirmishHeld;

        private void HoldForSkirmish(long now)
        {
            Destination = _musterPoint; Hold = true;
            Status = $"Holding at the muster while {_target.Name} fights the raiders it caught";
            // It is in a fight: the pull reached it. Waiting for the raid is not a stalled fight.
            _pulls.SawCombat();
            _targetProgressTick = now;
        }

        private void BeginAttempt(GameLiving[] raiders, long now)
        {
            _skirmishHeld = false;
            _musterEngaged = true;
            _musterEngagedTick = now;
            _engaged.Clear();
            _fellInAttempt.Clear();
            _engaged.UnionWith(raiders);
            _attemptRaidSize = AttemptRaidSize(_attemptParticipants?.Count(p => p.IsAlive && p.CurrentRegionID == _region) ?? 0,
                _attemptParticipants?.Count(p => p.IsAlive) ?? 0);
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
            _skirmishHeld = false;
            int engaged = _engaged.Count;
            int fallen = AttemptFallen(_engaged, _fellInAttempt, FallenNow);
            _engaged.Clear();
            _fellInAttempt.Clear();
            if (!IsWipe(engaged, fallen, _target.HealthPercent, _attemptLowestPercent))
            {
                // Not a wipe: said so, with the counts (2026-11-15: Kvasir's attempt ended with no line at all).
                log.Info($"REALM_EXPEDITION_ATTEMPT_ENDED region={_region} target=\"{_target.Name}\" engaged={engaged} fallen={fallen} " +
                         $"health={_target.HealthPercent} lowest={_attemptLowestPercent} how=\"{how}\" wipe=False");
                return false;
            }
            if (IsResetWithRaidStanding(engaged, fallen, _target.HealthPercent, _attemptLowestPercent) &&
                _freeResets.GetValueOrDefault(_target.Name) < FreeEncounterResets)
            {
                int resets = _freeResets[_target.Name] = _freeResets.GetValueOrDefault(_target.Name) + 1;
                log.Warn($"REALM_EXPEDITION_ENCOUNTER_RESET region={_region} target=\"{_target.Name}\" engaged={engaged} fallen={fallen} " +
                         $"health={_target.HealthPercent} lowest={_attemptLowestPercent} how=\"{how}\" resets={resets} of={FreeEncounterResets} " +
                         $"reason=\"the encounter reset while the raid still stood; the raid musters again and no attempt is used\"");
                _regrouping = true;
                _regroupSince = _musterSince = now;
                _targetProgressTick = now;
                return false;
            }
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
            _wipes.Enqueue((_target.Name, failures, engaged, fallen));
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
        /// Owner's PC, 2026-11-11 (build e90bf21, Krondon): for 75 minutes only 2-11 raiders stood near Scurceol Hyrde's orbs,
        /// none took damage, and every five minutes the route skipped the orb "no damage for five minutes" (near=11, 8, 6),
        /// took the next one and moved the raid's stand, so the raid never gathered at any. An add of a boss whose adds die
        /// in order (EpicEncounterTactics AddsInOrder) is the whole raid's target: the no-damage clock waits until a third of
        /// the raiders inside (at least a party) stand near it (at most 20 minutes, TargetAbsencePauseLimitMilliseconds).
        /// </summary>
        public static bool RequiredAddMusterShort(int near, int inside) =>
            near < Math.Max(MinimumRaidersAtTarget, (int)Math.Ceiling(inside / 3.0));

        /// <summary>A stalled add of a boss whose adds die in order is not swapped for the next one: the boss's encounter is
        /// set aside instead, and the raid comes back to the same add (pure).</summary>
        public static bool RequiredAddStays(bool addOfOrderedBoss) => addOfOrderedBoss;

        /// <summary>The living boss whose adds die in order and of which <paramref name="target"/> is one, or null.</summary>
        private static GameNPC OrderedAddOwner(GameNPC target, IReadOnlyList<GameNPC> live) =>
            EpicEncounterTactics.RequiredAddOwner(target, live) is { } owner && EpicEncounterTactics.For(owner)?.AddsInOrder == true
                ? owner : null;
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
        /// <summary>The dungeon region the route runs in.</summary>
        public ushort Region => _region;
        public Vector3 Destination { get; private set; }
        public string TargetName { get; private set; } = "Encounter staging";
        public int TargetLevel { get; private set; } = 50;
        public bool Hold { get; private set; } = true;
        /// <summary>Set by the raid each pulse: raiders stand at the way in, waiting to come back in together
        /// (RealmRaidReturnWave). The muster's "nobody reached it" clock waits for them.</summary>
        public bool ReturnGathering { get; set; }
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
        /// <summary>A ranged stand is never closer than this to the boss (Scurceol's melee reach is 350)...</summary>
        public const int RequiredAddStandMinimumBossDistance = 400;
        /// <summary>...and is looked for first beyond this (his aggro range is 600), so the casters don't stand in his fight.</summary>
        public const int RequiredAddStandBossClearance = 700;
        /// <summary>A stand is on the raid's floor around the boss: within this of his floor's height. On the real Krondon mesh the
        /// platform is 17,050-17,250 (his floor 17,178) and the orbs' perch slopes start at 17,307: those slopes see their orb
        /// but nobody can walk onto them, and every walk tried on one costs a corridor search.</summary>
        public const int RequiredAddStandFloorBand = 112;
        /// <summary>The sight line runs from a raider's eyes, this far over the stand, to this far over the add's own spot.</summary>
        public const int SightEyeHeight = 60, SightAimHeight = 20;
        /// <summary>Walkable mesh within this band over the sight line means solid ground there (a ledge, a perch, a rock top).</summary>
        public const int SightSolidBand = 400;
        /// <summary>The mesh's own height error: a surface less than this over the line does not block it.</summary>
        public const int SightTolerance = 8;
        /// <summary>The sight line is sampled this often (world units across).</summary>
        public const int SightSampleStep = 32;
        /// <summary>Where the floor ray toward the add ends, the floor beyond must fall away by more than this (a drop, not a wall).</summary>
        public const int SightDropStep = 64;
        /// <summary>How far below a drop's edge the ground under it is looked for.</summary>
        public const int SightDropDepth = 3_000;
        /// <summary>At most this many stands with sight are tried against the raid's walk (each try is a corridor search).</summary>
        public const int RequiredAddStandReachTries = 8;

        /// <summary>
        /// Krondon, 2026-11-06 (01:49 and 02:16): Scurceol Hyrde's tactic sent the raid to his four Lyft Miht orbs, which float
        /// 230-580 above him. The ground-route gate failed for every orb, and the raid walked off to Fuladl. 1.1.2 fought them from a
        /// floor spot instead, but tested sight with the mesh's floor ray to the floor under the orb. The real mesh (probe
        /// UT_ScurceolOrbStandProbe, 2026-11-08) shows why that never worked: each orb hangs 48-55 over the top of its own rock
        /// perch, 400-530 across a pit (floor 900 lower) from the platform the raid stands on. A floor ray can't cross a pit, so
        /// 0 of the 277-321 reachable floor spots within 1,400 of each orb passed (PC soak: "no reachable floor spot within 1400
        /// has line of sight to it", three times; one orb of four died). The probe's 3-D line from the platform found 53, 254,
        /// 1-35 and 154 clear spots for the four orbs.
        /// Now: floor spots on rings around the add (every 50 out to 1,400, every 10 degrees) and the old four, within reach
        /// of it, on the boss's floor (<see cref="RequiredAddStandFloorBand"/>) and not on the boss, are tried nearest the add first (those beyond <see cref="RequiredAddStandBossClearance"/>
        /// of the boss before the rest); a spot needs <see cref="HasSightToAirborne"/>, then a walk from the raid.
        /// False, with why, when none works.
        /// </summary>
        /// <param name="reach">The approach the raid can walk to for a floor point (only approaches the filter accepts), or null
        /// when none is proven.</param>
        public static bool TryRangedStand(IPathfindingMgr nav, Zone zone, Vector3 add, Vector3 boss,
            Func<Vector3, Func<Vector3, bool>, Vector3?> reach, out Vector3 stand, out string why)
        {
            stand = default;
            if (nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            { why = "no navigation mesh here"; return false; }
            var floors = new List<Vector3>();
            void Add(Vector3? p)
            {
                if (p is Vector3 v && !floors.Any(f => Vector3.DistanceSquared(f, v) < 48 * 48)) floors.Add(v);
            }
            Add(nav.GetClosestPoint(zone, add, 128, 128, RangedFlyerReach, nav.DefaultFilters));
            Vector3? bossFloor = nav.GetClosestPoint(zone, boss, 64, 64, 256, nav.DefaultFilters);
            Add(bossFloor);
            var level = new Vector3(add.X, add.Y, boss.Z);
            foreach (float t in new[] { 0.67f, 0.33f })
                Add(nav.GetClosestPoint(zone, Vector3.Lerp(boss, level, t), 96, 96, 256, nav.DefaultFilters));
            // The raid's floor around the add: the boss's own height, where the raid fights.
            float floorZ = bossFloor?.Z ?? boss.Z;
            // Every 50 out and every 10 degrees: on the real mesh orb 3's clear spots are a few patches of the platform's far edge,
            // and a sparser ring (every 100, every 15 degrees) found one.
            for (int radius = 200; radius <= RequiredAddRangedReach; radius += 50)
            for (int degrees = 0; degrees < 360; degrees += 10)
            {
                float a = degrees * MathF.PI / 180f;
                Add(nav.GetClosestPoint(zone, new Vector3(add.X + MathF.Cos(a) * radius, add.Y + MathF.Sin(a) * radius, floorZ),
                    48, 48, 320, nav.DefaultFilters));
            }
            if (floors.Count == 0)
            { why = "no floor under it or near its boss"; return false; }
            bool Allowed(Vector3 p) => Vector3.Distance(p, add) <= RequiredAddRangedReach &&
                                       Vector3.Distance(p, boss) >= RequiredAddStandMinimumBossDistance &&
                                       MathF.Abs(p.Z - floorZ) <= RequiredAddStandFloorBand;
            Vector3[] ordered = floors.Where(Allowed)
                .OrderBy(p => Vector3.Distance(p, boss) < RequiredAddStandBossClearance ? 1 : 0)
                .ThenBy(p => Vector3.DistanceSquared(p, add)).ToArray();
            if (ordered.Length == 0)
            { why = $"none of {floors.Count} floor spots is within {RequiredAddRangedReach} of it, off its boss and on his floor"; return false; }
            bool Accept(Vector3 p) => Allowed(p) && HasSightToAirborne(nav, zone, p, add);
            int sighted = 0, tried = 0;
            foreach (Vector3 floor in ordered)
            {
                if (!HasSightToAirborne(nav, zone, floor, add)) continue;
                sighted++;
                if (reach(floor, Accept) is Vector3 approach)
                {
                    stand = approach;
                    why = $"ranged from the floor {(int)Vector3.Distance(approach, add)} away, {(int)Vector3.Distance(approach, boss)} from its boss";
                    return true;
                }
                if (++tried >= RequiredAddStandReachTries) break;
            }
            why = sighted == 0 ? $"none of {ordered.Length} floor spots within {RequiredAddRangedReach} has a clear line to it"
                : $"none of the {tried} nearest floor spots with a clear line to it is reachable from the raid";
            return false;
        }

        /// <summary>
        /// Whether a raider on <paramref name="floor"/> sees <paramref name="add"/> in the air, judged from the mesh (the server has
        /// no other geometry). The mesh holds only walkable surfaces, so two checks:
        /// 1. The 3-D line from the eyes to the add passes under no walkable surface (within <see cref="SightSolidBand"/> over it):
        ///    the top of a ledge, a wall or the add's own perch in the way.
        /// 2. A wall up to the ceiling has no top: the floor ray toward the spot under the add either gets there, or the floor ends
        ///    at a drop, where the ground beyond lies more than <see cref="SightDropStep"/> lower (the pit, not a wall).
        /// </summary>
        public static bool HasSightToAirborne(IPathfindingMgr nav, Zone zone, Vector3 floor, Vector3 add)
        {
            Vector3 eye = floor + new Vector3(0, 0, SightEyeHeight), aim = add + new Vector3(0, 0, SightAimHeight);
            float across = Vector2.Distance(new(eye.X, eye.Y), new(aim.X, aim.Y));
            int samples = Math.Max(1, (int)(across / SightSampleStep));
            // From the add's end first: its perch blocks most lines, so a blocked line is found in a few queries.
            for (int k = samples - 1; k >= 1; k--)
            {
                Vector3 at = Vector3.Lerp(eye, aim, k / (float)samples);
                if (nav.GetClosestPointInBounds(zone, at, new Vector3(-4, -4, SightTolerance), new Vector3(4, 4, SightSolidBand),
                        nav.DefaultFilters) != null)
                    return false;
            }
            var under = new Vector3(add.X, add.Y, floor.Z);
            if (across < 1 || nav.HasLineOfSight(zone, floor, under, nav.DefaultFilters)) return true;
            // Where the floor ray stops (to a 64th of the way, 5-20 units)...
            float reached = 0, stopped = 1;
            for (int i = 0; i < 6; i++)
            {
                float mid = (reached + stopped) / 2;
                if (nav.HasLineOfSight(zone, floor, Vector3.Lerp(floor, under, mid), nav.DefaultFilters)) reached = mid; else stopped = mid;
            }
            // ...and just past it, the highest ground from the floor's height down.
            Vector3 dir = Vector3.Normalize(under - floor);
            Vector3 past = Vector3.Lerp(floor, under, stopped) + dir * 32;
            Vector3? beyond = nav.GetClosestPointInBounds(zone, past, new Vector3(-8, -8, -SightDropDepth), new Vector3(8, 8, SightDropStep),
                nav.DefaultFilters);
            return beyond is Vector3 ground && ground.Z < floor.Z - SightDropStep;
        }

        private RealmRaidDungeonRoute(ushort region, DbZonePoint entrance, Vector3 entry, Vector3 trigger, string[] finals,
            string[] objectives = null, eRealm realm = eRealm.None) : this(region, entrance, entry, trigger, finals, objectives, realm, null, null)
        {
        }

        private RealmRaidDungeonRoute(ushort region, DbZonePoint entrance, Vector3 entry, Vector3 trigger, string[] finals,
            string[] objectives, eRealm realm, DbZonePoint inner, GameNPC[] gates)
        {
            _region = region; Entrance = entrance; Front = Destination = _arrival = entry; _trigger = trigger; _finalTypes = finals;
            _objectives = objectives; _realm = realm; InnerEntrance = inner ?? entrance; _gates = gates;
        }

        /// <summary>The zone point into the raid's own dungeon: <see cref="Entrance"/> itself, or for a raid that walks
        /// through another dungeon (Summoner's Hall, Crystal Cave) the way out of that one into its own.</summary>
        public DbZonePoint InnerEntrance { get; }

        // A route gate (RealmRaidRouteGate): the living bosses on the raid's walk through another dungeon. The route is then
        // that dungeon's, its objectives are these bosses and it is complete once they are all dead.
        private readonly GameNPC[] _gates;
        public bool IsGate => _gates != null;
        public IReadOnlyList<GameNPC> Gates => _gates ?? [];
        private bool FinalsDown() => _gates != null
            ? _gates.All(g => !g.IsAlive)
            : _finalTypes.All(type => _finals.TryGetValue(type, out var boss) && !boss.IsAlive);

        /// <summary>
        /// The bosses on <paramref name="main"/>'s walk through the dungeon its way in leads through (RealmRaidRouteGate), as
        /// a route of their own in that dungeon; false when the raid walks through no other dungeon or nothing living blocks
        /// the walk. <paramref name="detail"/> says what was found (or why nothing was looked at).
        /// </summary>
        public static bool TryCreateGate(RealmRaidDungeonRoute main, out RealmRaidDungeonRoute gate, out string detail)
        {
            gate = null;
            DbZonePoint outer = main?.Entrance, inner = main?.InnerEntrance;
            if (outer == null || inner == null || outer.TargetRegion == main._region || inner.SourceRegion != outer.TargetRegion)
            { detail = "no dungeon on the way in"; return false; }
            ushort through = outer.TargetRegion;
            Region region = WorldMgr.GetRegion(through);
            var nav = PathfindingProvider.Instance;
            Vector3 arrival = new(outer.TargetX, outer.TargetY, outer.TargetZ), exit = new(inner.SourceX, inner.SourceY, inner.SourceZ);
            Zone zone = region?.GetZone((int)arrival.X, (int)arrival.Y), exitZone = region?.GetZone((int)exit.X, (int)exit.Y);
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) { detail = "no navigation mesh on the way"; return false; }
            Vector3 from = nav.GetClosestPoint(zone, arrival, 48, 48, 64, nav.DefaultFilters) ?? arrival;
            Vector3 to = exitZone != null && nav.HasNavmesh(exitZone)
                ? nav.GetClosestPoint(exitZone, exit, 128, 128, 256, nav.DefaultFilters) ?? exit : exit;
            var walk = new List<Vector3> { from };
            if (exitZone == zone)
            {
                Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[256];
                PathfindingResult path = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
                if (path.Status is PathfindingStatus.PathFound or PathfindingStatus.PartialPathFound)
                    for (int i = 0; i < Math.Min(path.NodeCount, nodes.Length); i++) walk.Add(nodes[i].Position);
            }
            if (Vector3.DistanceSquared(walk[^1], to) > 64 * 64) walk.Add(to);
            var blocking = new List<GameNPC>();
            foreach (GameNPC npc in region.Objects.OfType<GameNPC>())
            {
                if (npc is not GameEpicBoss || !npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active || npc.Realm != eRealm.None ||
                    (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) != 0 ||
                    npc.Brain is not StandardMobBrain { AggroRange: > 0 } brain)
                    continue;
                if (RealmRaidRouteGate.Blocks(walk, new(npc.SpawnPoint.X, npc.SpawnPoint.Y, npc.SpawnPoint.Z), brain.AggroRange) ||
                    RealmRaidRouteGate.Blocks(walk, new(npc.X, npc.Y, npc.Z), brain.AggroRange))
                    blocking.Add(npc);
            }
            detail = $"walk={walk.Count} points from {(int)from.X},{(int)from.Y},{(int)from.Z} to {(int)to.X},{(int)to.Y},{(int)to.Z}";
            if (blocking.Count == 0) return false;
            GameNPC[] gates = blocking.ToArray();
            gate = new(through, outer, from, to, [], gates.Select(g => g.Name).Distinct(StringComparer.Ordinal).ToArray(), main._realm,
                inner, gates) { Focus = null };
            detail += $" bosses=\"{string.Join(",", gates.Select(g => $"{g.Name}@{g.X},{g.Y},{g.Z}"))}\"";
            return true;
        }

        /// <summary>The raid comes back to this route after fighting a gate on its way (RealmRaidRouteGate): the clocks that
        /// measure waiting and stalls start again, so the time spent at the gate is not counted against this route.</summary>
        public void Resumed(long now)
        {
            _frontAbsentSince = 0; _musterEmptySince = 0; _formedWaitSince = 0; _holdSince = 0; _blockedSince = 0;
            _targetProgressTick = _targetApproachTick = _musterSince = now;
            if (_regrouping) _regroupSince = now;
            _musterProgress.Clear();
            _routeDeaths.Clear();
            _standingInside.Clear();
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
                // Through an approach dungeon (Summoner's Hall from Hall of the Corrupt): the walk from where the outer crossing
                // lands to the inner crossing must exist (DividedRealms, night 2026-11-04: region 277 had no such walk on its
                // mesh, and 146 raiders were held there 72 minutes before the raid ended unreached).
                if (outer.Id != inner.Id && !ApproachDungeonConnects(nav, outer, inner))
                { why.Add($"{id}: no proven walk through region {outer.TargetRegion} to its crossing"); continue; }
                Vector3 raw = new(inner.TargetX, inner.TargetY, inner.TargetZ);
                Vector3? floor = nav.GetClosestPoint(inside, raw, 48, 48, 64, nav.DefaultFilters);
                if (!floor.HasValue) { why.Add($"{id}: no navmesh floor at the inside arrival point"); continue; }
                if (!AutonomousRendezvousNavigation.HasLocalExit(nav, inside, floor.Value)) { why.Add($"{id}: inside arrival has no local exit"); continue; }
                // A neutral dungeon is too large for one entrance-to-final-boss proof (Darkness
                // Falls); the route proves each named encounter from the raid's positions instead.
                if ((homeRegion == 0 || !RealmRaidNeutralEvents.IsNeutralRegion(region)) &&
                    !AutonomousZonePointApproach.TryResolve(nav, inside, floor.Value, trigger, 220, out _))
                { why.Add($"{id}: no approach to the encounter"); continue; }
                route = new(region, outer, floor.Value, trigger, finals, objectives, realm, inner, null);
                route.ObserveCompletion();
                LastFailure = "";
                return true;
            }
            LastFailure = why.Count == 0 ? "no entrance crossing into the region for this realm" : string.Join("; ", why);
            return false;
        }

        /// <summary>A walk exists through the approach dungeon from where <paramref name="outer"/> lands to where
        /// <paramref name="inner"/> leaves it (DividedRealms). Proven inside one zone's mesh; crossings in two zones of the
        /// region are left to the bots' router.</summary>
        private static bool ApproachDungeonConnects(IPathfindingMgr nav, DbZonePoint outer, DbZonePoint inner)
        {
            Region through = WorldMgr.GetRegion(outer.TargetRegion);
            Zone arrivalZone = through?.GetZone(outer.TargetX, outer.TargetY);
            Zone exitZone = through?.GetZone(inner.SourceX, inner.SourceY);
            if (arrivalZone == null || exitZone == null || !nav.HasNavmesh(arrivalZone))
                return false;
            if (arrivalZone != exitZone)
                return true;
            Vector3? arrival = nav.GetClosestPoint(arrivalZone, new(outer.TargetX, outer.TargetY, outer.TargetZ), 64, 64, 128, nav.DefaultFilters);
            return arrival.HasValue && AutonomousZonePointApproach.TryResolve(nav, arrivalZone, arrival.Value,
                new(inner.SourceX, inner.SourceY, inner.SourceZ), 220, out _);
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
                // Floors first (1.2 DF raid wave): a proof on the post's own floor before a nearer one on the floor above. By
                // plain distance Prince Asmoien's nearest proofs (2,418) stood 2,353 above him and High Lord Baln's (4,856)
                // 4,838 above; a proof on Asmoien's own floor (an umbral aegis 3,014 away, 197 up) came last.
                .OrderBy(item => WalkEstimate(encounter, item.Approach))
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
        /// Raiders a final encounter waits for before it is pulled: 70% of the living raid, at least
        /// <see cref="MinimumRaidersAtTarget"/> and at most 150; after 20 minutes of mustering half will do.
        /// It was half, and a third after 20 minutes, until 1.2 build 2. Owner's PC, 2026-11-12: raids sized at 125% of
        /// the period's went in with half of themselves and the rest arrived during the fight (Summoner Roesia 35 of 80,
        /// Torst 30, Norsob the Annihilator 20, High Lord Baln 40): each attempt was a smaller raid than the one sent.
        /// </summary>
        public static int FinalMusterNeeded(int livingRaiders, long musterMilliseconds) =>
            Math.Max(MinimumRaidersAtTarget, Math.Min(FinalMusterMaximum,
                musterMilliseconds >= FinalMusterPatienceMilliseconds ? livingRaiders / 2 : livingRaiders * 7 / 10));
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
            // The encounter died and came back since this attempt's pull: that attempt was won, and the new life is a new
            // muster (KilledSinceAttempt; Chthonic Knight Ukobat, 2026-11-16).
            if (_musterTarget == _target && _musterEngaged && KilledSinceAttempt(_musterEngagedTick, _target.SpawnTick))
            {
                log.Info($"REALM_EXPEDITION_ATTEMPT_ENDED region={_region} target=\"{_target.Name}\" engaged={_engaged.Count} " +
                         $"health={_target.HealthPercent} lowest={_attemptLowestPercent} how=\"killed; it came back since\" wipe=False");
                _musterTarget = null;
            }
            // The muster was placed while the encounter was out chasing raiders; it is home again, so the muster follows it
            // (Aidon the Archwizard, 2026-11-05: the muster went 2,600 from his home, on the ramp the chase had reached).
            if (_musterTarget == _target && !_target.InCombat && MusterReference(_target) is var home &&
                Vector3.Distance(home, _musterReference) > MusterFollowDistance)
            {
                Vector3 old = _musterPoint;
                if (TryPointApproach(home, participants, out Vector3 approach)) Front = approach;
                _musterPoint = GuardedMuster(participants);
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
                if (_finalMusterSince == 0 && _finalTypes.Contains(_target.GetType().Name)) _finalMusterSince = now;
                _musterEngaged = false;
                _skirmishHeld = false;
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
                _musterPoint = GuardedMuster(participants);
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
                if (!_musterEngaged || _skirmishHeld)
                {
                    bool resets = EpicEncounterTactics.ResetsWhenLeft(_target);
                    // Pulling back only shakes it off when the muster lies past its tether (Fuladl, Krondon 2026-11-06: tether
                    // 3,800, muster 1,400 from his spawn; he would follow the pull-back to the muster).
                    int tether = EpicEncounterTactics.LeashOf(_target)?.TetherRange is int t && t > 0 ? t : _target.TetherRange;
                    float musterFromSpawn = Vector3.Distance(_musterPoint, new Vector3(_target.SpawnPoint.X, _target.SpawnPoint.Y, _target.SpawnPoint.Z));
                    bool shakesOff = EpicEncounterTactics.BreakOffShakesOff(resets, tether, musterFromSpawn);
                    // Pulled back from it a moment ago and it is still on the raid: it chases.
                    bool pulledBack = _lastBreakOff.TryGetValue(_target, out long last) && now - last < BreakOffRepeatMilliseconds;
                    // The whole living raid, raiders walking back after a wipe included (AttemptRaidSize), and the muster it
                    // would wait for (70%; half after 20 minutes): a handful inside is never "the raid".
                    int livingRaid = participants.Count(p => p.IsAlive);
                    int musterNeeded = FinalMusterNeeded(livingRaid, now - _musterSince);
                    var choice = EpicEncounterTactics.ChooseEarlyPull(fighting.Length, inside.Length, livingRaid, shakesOff, pulledBack,
                        musterNeeded);
                    if (_skirmishHeld)
                    {
                        _attemptLowestPercent = Math.Min(_attemptLowestPercent, _target.HealthPercent);
                        if (choice != EpicEncounterTactics.EarlyPull.Commit)
                        {
                            // Still too few: whoever it reaches fights it, the rest keep gathering at the muster.
                            _engaged.UnionWith(fighting);
                            NoteFallen();
                            HoldForSkirmish(now);
                            return true;
                        }
                        // Enough of the raid has gathered while it fought the few it caught: everyone goes in now.
                        _skirmishHeld = false;
                        _engaged.UnionWith(inside);
                        _attemptRaidSize = Math.Max(_attemptRaidSize, livingRaid);
                        Destination = Front; Hold = false;
                        _nextTargetCall = 0;
                        log.Warn($"REALM_EXPEDITION_EARLY_PULL region={_region} target=\"{_target.Name}\" near={fighting.Length} " +
                                 $"inside={inside.Length} raid={livingRaid} needed={musterNeeded} decision=Commit health={_target.HealthPercent} " +
                                 $"reason=\"the raid gathered while the few it caught held it; everyone goes in\"");
                        _pulls.SawCombat();
                        return false;
                    }
                    // Logged for a raid's final or a catalogued boss (at most every ten minutes each), and for every pull-back;
                    // a Darkness Falls named or an epic trash pack that is simply fought goes to the debug log (2026-11-06: 53
                    // lines in four hours, one per named).
                    bool isFinal = _finalTypes.Contains(_target.GetType().Name);
                    long since = _earlyPullLogged.TryGetValue(_target.Name, out long logged) ? now - logged : -1;
                    string line = $"REALM_EXPEDITION_EARLY_PULL region={_region} target=\"{_target.Name}\" near={fighting.Length} " +
                                  $"inside={inside.Length} raid={livingRaid} needed={musterNeeded} resets={resets} tether={tether} " +
                                  $"musterFromSpawn={(int)musterFromSpawn} decision={choice} health={_target.HealthPercent} " +
                                  $"muster={(int)_musterPoint.X},{(int)_musterPoint.Y},{(int)_musterPoint.Z}";
                    if (choice == EpicEncounterTactics.EarlyPull.Hold ||
                        EpicEncounterTactics.EarlyPullWorthLogging(choice, isFinal, EpicEncounterTactics.LeashOf(_target) != null, since))
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
                    if (choice == EpicEncounterTactics.EarlyPull.Hold)
                    {
                        // The raiders it caught are in this fight (scored as a skirmish unless half the raid falls in it); the rest
                        // are not fed in by twos and threes, they gather at the muster.
                        BeginAttempt(fighting, now);
                        _skirmishHeld = true;
                        HoldForSkirmish(now);
                        return true;
                    }
                    // Commit: the whole raid is in this attempt, and goes to the fight now.
                    BeginAttempt(inside, now);
                    Destination = Front; Hold = false;
                    _nextTargetCall = 0;
                }
                else
                    _engaged.UnionWith(fighting);
                NoteFallen();
                _pulls.SawCombat();
                _attemptLowestPercent = Math.Min(_attemptLowestPercent, _target.HealthPercent);
                return false;
            }
            // The raid needs time to cross from the muster point to the boss (run 5: the engage repeated every ten
            // seconds while Govannon was still out of combat); only after that is "out of combat" a wipe.
            // A held skirmish sent nobody across: its end is judged at once.
            if (_musterEngaged && !_skirmishHeld && now - _musterEngagedTick < FinalEngageGraceMilliseconds)
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
            // Raiders waiting at the way in for their return wave are on their way: the muster is not unreachable.
            if (mustered > 0 || ReturnGathering) _musterEmptySince = 0;
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
            // The tactic holds the pull (EpicEncounterTactics.PullWait): the raid waits at the muster; waiting is no stall.
            if (HeldByTactic(_target, _live, now) is string pullWait)
            {
                Destination = _musterPoint; Hold = true; _targetProgressTick = now;
                Status = $"Waiting to pull {_target.Name}: {pullWait} ({mustered}/{needed})";
                if (now >= _nextMusterLog)
                {
                    _nextMusterLog = now + 2 * 60_000;
                    log.Info($"REALM_EXPEDITION_PULL_WAIT region={_region} target=\"{_target.Name}\" mustered={mustered} needed={needed} " +
                             $"at={(int)_musterPoint.X},{(int)_musterPoint.Y},{(int)_musterPoint.Z} reason=\"{pullWait}\"");
                }
                return true;
            }
            // Darkness Falls (1.2 DF raid wave): a monster that would join this fight came back while the raid mustered
            // (Legion's antechamber respawns in 20-30 minutes; Hibernia's muster for him took 100): it is cleared before the pull.
            // So for a boss whose rule says so anywhere (EpicEncounterTactics.ClearsAgainBeforePull): owner's PC, build 1dc66b6,
            // 2026-11-15, the Crypt Lord joined a court fight at 02:56, and after that skirmish the raid mustered for him again
            // with most of his court alive (every court monster joins him) until two wipes ended the raid (03:00, 03:04).
            if ((_region == AutonomousDarknessFallsPolicy.RegionId || EpicEncounterTactics.ClearsAgainBeforePull(_target)) &&
                !_target.InCombat && now >= _nextReclearCheck &&
                (_nextReclearCheck = now + 10_000) > 0 &&
                (_live ?? []).Where(n => n != _target && _routeRetry.GetValueOrDefault(n) <= now &&
                                         EpicEncounterTactics.ClearFirstOwner(n, _live) == _target)
                    .OrderBy(n => WalkEstimate(Front, new(n.X, n.Y, n.Z))).FirstOrDefault() is { } back)
            {
                SwitchByTactic(back, $"clear-first of \"{_target.Name}\" is up again: cleared before the pull", participants, now);
                return true;
            }
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
                else _musterPoint = GuardedMuster(participants);
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

        private static Vector3 MusterReference(GameNPC npc) => EpicEncounterTactics.MusterAnchor(npc) ?? MusterReference(new(npc.X, npc.Y, npc.Z),
            new(npc.SpawnPoint.X, npc.SpawnPoint.Y, npc.SpawnPoint.Z),
            EpicEncounterTactics.LeashOf(npc)?.TetherRange is int t && t > 0 ? t : npc.TetherRange, EpicEncounterTactics.ResetsWhenLeft(npc));

        /// <summary>The muster for the current target: inside its barrier room (EpicEncounterTactics.KeepInsideFor), and
        /// never behind a living boss's barrier (Easmarach's falls, owner's PC 2026-11-15: a muster at 37,086,55,050 behind
        /// his push held the raid for eight minutes with nobody able to reach it): then in front of it, where the barrier
        /// puts raiders (EpicEncounterTactics.OutOfLiveBarrier).</summary>
        private Vector3 GuardedMuster(GameLiving[] participants) =>
            EpicEncounterTactics.OutOfLiveBarrier(_region, EpicEncounterTactics.KeepInsideFor(_target, MusterPoint(participants)), _live);

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
                return GuardMuster(Front, null, boss);
            // Searched from the encounter's approach toward the raid, so the path always starts at the encounter even
            // when it is too long to finish. From the raid's side a partial path ended wherever the search gave up:
            // Legion's muster in Darkness Falls was placed 19,000 units from him and nobody ever reached it (run 9).
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[256];
            PathfindingResult path = nav.GetPathStraight(zone, Front, body, nav.DefaultFilters, nodes);
            bool found = path.Status == PathfindingStatus.PathFound || path.Status == PathfindingStatus.PartialPathFound;
            Vector3[] walk = found ? new Vector3[Math.Min(path.NodeCount, nodes.Length)] : [];
            for (int i = 0; i < walk.Length; i++) walk[i] = nodes[i].Position;
            Vector3? Floor(Vector3 p) => nav.GetClosestPoint(zone, p, 64, 64, 128, nav.DefaultFilters);
            if (Vector3.Distance(body, boss) <= FinalMusterStandOff + 200)
                return GuardMuster(body, walk, boss, Floor);
            if (found)
            {
                for (int i = 1; i < walk.Length; i++)
                {
                    Vector3 a = walk[i - 1], b = walk[i];
                    if (Vector3.Distance(b, boss) < FinalMusterStandOff) continue;
                    // a inside the stand-off circle, b outside: the crossing point on a-b.
                    float lo = 0, hi = 1;
                    for (int step = 0; step < 12; step++)
                    {
                        float mid = (lo + hi) / 2;
                        if (Vector3.Distance(Vector3.Lerp(a, b, mid), boss) >= FinalMusterStandOff) hi = mid; else lo = mid;
                    }
                    return GuardMuster(Floor(Vector3.Lerp(a, b, hi)) ?? b, walk, boss, Floor);
                }
                if (walk.Length > 0) return GuardMuster(walk[^1], walk, boss, Floor);
            }
            return GuardMuster(Front, walk, boss, Floor);
        }

        /// <summary>How far outside a living boss's aggro range (other than the encounter mustered for) a muster must lie.</summary>
        public const float UntargetedBossMargin = 300f;
        /// <summary>The walk from the encounter towards the raid is sampled this often for a safe muster.</summary>
        public const float MusterSafeStep = 150f;

        /// <summary>A living boss's aggro sphere (<see cref="UntargetedBossMargin"/> included) a muster must stay out of.</summary>
        public readonly record struct BossDanger(string Name, Vector3 At, float Radius);

        public static bool InBossDanger(Vector3 point, IEnumerable<BossDanger> dangers) =>
            dangers.Any(d => Vector3.DistanceSquared(point, d.At) <= d.Radius * d.Radius);

        /// <summary>
        /// The first point along <paramref name="walk"/> (the path from the encounter's approach towards the raid's main body),
        /// at least <paramref name="standOff"/> from the encounter and every <see cref="MusterSafeStep"/>, that lies outside
        /// every danger; then <paramref name="fallback"/> (the main body) when that is safe; else null.
        /// </summary>
        public static Vector3? SafeMusterAlong(IReadOnlyList<Vector3> walk, Vector3 encounter, float standOff,
            IReadOnlyList<BossDanger> dangers, Vector3? fallback, Func<Vector3, Vector3?> floor = null)
        {
            if (walk != null)
                for (int i = 1; i < walk.Count; i++)
                {
                    Vector3 a = walk[i - 1], b = walk[i];
                    float length = Vector3.Distance(a, b);
                    int steps = Math.Max(1, (int)MathF.Ceiling(length / MusterSafeStep));
                    for (int k = 0; k <= steps; k++)
                    {
                        Vector3 raw = Vector3.Lerp(a, b, k / (float)steps);
                        if (Vector3.Distance(raw, encounter) < standOff) continue;
                        Vector3 p = floor?.Invoke(raw) ?? raw;
                        if (!InBossDanger(p, dangers)) return p;
                    }
                }
            return fallback is Vector3 f && !InBossDanger(f, dangers) ? f : null;
        }

        /// <summary>
        /// Every living boss in the dungeon other than the encounter mustered for (its own adds, the encounters it is linked
        /// with and ground hazards are part of its fight) and its aggro sphere: at its spawn, and where it stands if elsewhere.
        /// </summary>
        private List<BossDanger> UntargetedBossDangers()
        {
            var dangers = new List<BossDanger>();
            foreach (GameNPC npc in _live)
            {
                if (npc is not GameEpicBoss || npc == _target || !npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active ||
                    npc.Realm != eRealm.None || (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) != 0 ||
                    npc.Brain is not DOL.AI.Brain.StandardMobBrain { AggroRange: > 0 } brain || IsEncounterHazard(npc) ||
                    EpicEncounterTactics.Covers(npc, _target, _live) || EpicEncounterTactics.IsRequiredAddOfLiveBoss(npc, _live))
                    continue;
                float radius = brain.AggroRange + UntargetedBossMargin;
                var spawn = new Vector3(npc.SpawnPoint.X, npc.SpawnPoint.Y, npc.SpawnPoint.Z);
                dangers.Add(new(npc.Name, spawn, radius));
                var at = new Vector3(npc.X, npc.Y, npc.Z);
                if (Vector3.DistanceSquared(at, spawn) > 64 * 64) dangers.Add(new(npc.Name, at, radius));
            }
            dangers.AddRange(RuleMusterDangers(_live));
            return dangers;
        }

        /// <summary>
        /// The spots a dungeon rule keeps musters from (EpicEncounterTactics.MusterDangersOf), the encounter mustered for
        /// included: owner's PC, build 1dc66b6, 2026-11-15, the raid's muster for the Crypt Lord was 27,960,40,280, 115 from his
        /// patrol and 194 from one of his court, and he and his court walked into it (wipes at 03:00 and 03:04).
        /// </summary>
        public static IEnumerable<BossDanger> RuleMusterDangers(IReadOnlyList<GameNPC> live)
        {
            foreach (GameNPC npc in live ?? [])
                foreach ((Vector3 at, float radius) in EpicEncounterTactics.MusterDangersOf(npc, live))
                    yield return new(npc.Name, at, radius);
        }

        /// <summary>
        /// A muster is never placed in a living boss's aggro range (plus <see cref="UntargetedBossMargin"/>) unless it is the
        /// encounter the raid musters for. Owner's PC, 2026-11-14 (build e3b8238): Tuscaran Glacier's muster for a hrimthursa
        /// harrower went to 15025,50189, 76 units from Icelord Agmundr's spawn (aggro 600), after he had been set aside; 69
        /// raiders gathered there and 80 died to him and 29 to his adds. Moved further back along the walk to the raid when
        /// it lands in one; kept (with a warning) only when no point of the walk is safe.
        /// </summary>
        private Vector3 GuardMuster(Vector3 chosen, IReadOnlyList<Vector3> walk, Vector3 encounter, Func<Vector3, Vector3?> floor = null)
        {
            List<BossDanger> dangers = UntargetedBossDangers();
            if (!InBossDanger(chosen, dangers)) return chosen;
            string by = string.Join(",", dangers.Where(d => InBossDanger(chosen, [d])).Select(d => d.Name).Distinct());
            Vector3? safe = SafeMusterAlong(walk, encounter, FinalMusterStandOff, dangers, null, floor);
            if (safe is Vector3 moved)
            {
                log.Warn($"REALM_EXPEDITION_MUSTER_GUARDED region={_region} target=\"{_target?.Name}\" " +
                         $"from={(int)chosen.X},{(int)chosen.Y},{(int)chosen.Z} to={(int)moved.X},{(int)moved.Y},{(int)moved.Z} " +
                         $"reason=\"the muster was in the aggro range of {by}; moved back along the walk to the raid\"");
                return moved;
            }
            log.Warn($"REALM_EXPEDITION_MUSTER_UNSAFE region={_region} target=\"{_target?.Name}\" at={(int)chosen.X},{(int)chosen.Y},{(int)chosen.Z} " +
                     $"reason=\"in the aggro range of {by} and no point of the walk to the raid is clear of it\"");
            return chosen;
        }

        public const long CallTargetAfterMilliseconds = 90_000;
        public const long FrontRegroupMilliseconds = 3 * 60_000;
        private long _frontAbsentSince;
        // The raiders' closest approach to an unreached front: while they keep closing in, the front is not given up.
        private float _frontClosest = float.MaxValue;
        private Vector3 _frontClosestFor;

        /// <summary>Height within which a monster counts as on the raid's floor (the approach search's own band).</summary>
        public const float SameFloorBand = 256f;
        /// <summary>Units across a unit of height beyond <see cref="SameFloorBand"/> is weighed as (see <see cref="WalkEstimate"/>).</summary>
        public const float FloorChangeWeight = 8f;

        /// <summary>
        /// How far the raid's next target is, as the raid walks (pure): the distance across, plus <see cref="FloorChangeWeight"/>
        /// per unit of height past <see cref="SameFloorBand"/>. The route took the nearest target in a straight line, and in
        /// Krondon a monster on the floor 1,500 below is "near" but a long walk round (PC test 2026-11-09, build b72716e): the
        /// raid went from the thrawn ogres at Z 14,600 to an atelic ooze 600 across and 1,500 down (02:30), back up to a
        /// foreman (02:34) and down to another ooze (02:38), with nobody within 1,000 of the target for 3-4 minutes each time,
        /// while packs on its own floor were reached in under a minute. With a weight of 8, a monster a floor away
        /// (1,250 past the band: 10,000) comes after everything within 10,000 on the raid's floor, so the raid clears the
        /// floor it stands on before it changes floors.
        /// </summary>
        public static float WalkEstimate(Vector3 from, Vector3 to) =>
            Vector2.Distance(new(from.X, from.Y), new(to.X, to.Y)) + FloorChangeWeight * Math.Max(0f, MathF.Abs(to.Z - from.Z) - SameFloorBand);

        /// <summary>How long the route waits for formed parties at its front before it goes on from the raid's main body.</summary>
        public const long FormedPartiesWaitMilliseconds = 6 * 60_000;
        /// <summary>After that wait a main body of this many will do (a party), instead of <see cref="MinimumRaidersAtTarget"/>.</summary>
        public const int FormedWaitMinimumBody = 4;
        private long _formedWaitSince, _formedWaitLast, _nextFormedWaitLog;
        public const long FormedWaitGapMilliseconds = 30_000;

        public static bool FormedWaitTooLong(long waitingSince, long now) =>
            waitingSince > 0 && now - waitingSince >= FormedPartiesWaitMilliseconds;

        /// <summary>The living raider with the most living raiders within 1,000 units (the raid's main body).</summary>
        private static bool TryRegroupFront(GameLiving[] inside, out Vector3 front) =>
            TryRegroupFront(inside, out front, MinimumRaidersAtTarget);

        private static bool TryRegroupFront(GameLiving[] inside, out Vector3 front, int minimum)
        {
            front = default;
            GameLiving best = null;
            int bestCount = 0;
            foreach (GameLiving candidate in inside.Where(p => p.Group == null || p.Group.LivingLeader == p))
            {
                int count = inside.Count(p => p.IsWithinRadius(candidate, 1000));
                if (count > bestCount) { best = candidate; bestCount = count; }
            }
            if (best == null || bestCount < minimum) return false;
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
        // The ground hazards of the tactics (Orshom's Fire) are hazards too (EpicEncounterTactics).
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
            if (FinalsDown())
            { Complete = true; Status = IsGate ? "The way is clear" : "Final encounter defeated"; }
        }

        // Run 21 (2026-10-17): 70+ raiders fought Spindler Broodmother for over 30 minutes and nothing showed how she
        // fared. Once a minute: the current target's health, whether it is in combat, and the raiders around it.
        private long _nextTargetStatusLog;

        /// <summary>How many raiders could not act (mesmerized, stunned) over the samples of one report (diagnostics;
        /// pure). Play test duo29, 2026-11-10: in the second minute at Spindler Broodmother 25 of about 110 living raiders
        /// hit her and 11 hit a newborn, with 87 targeting her; she mesmerizes everyone within 300 of a random raider for
        /// 60 seconds every half minute, and nothing in the log counted it.</summary>
        public struct ControlTally
        {
            public int Samples, MezzedPeak, MezzedSum, StunnedPeak;
            public void Add(int mezzed, int stunned)
            {
                Samples++;
                MezzedSum += mezzed;
                if (mezzed > MezzedPeak) MezzedPeak = mezzed;
                if (stunned > StunnedPeak) StunnedPeak = stunned;
            }
            public readonly int MezzedAverage => Samples == 0 ? 0 : (int)Math.Round(MezzedSum / (double)Samples);
        }
        /// <summary>The raid's mesmerized and stunned are counted this often while its target fights.</summary>
        public const long ControlSampleMilliseconds = 5_000;
        private ControlTally _control;
        private long _nextControlSample;

        public void Advance(GameLiving[] participants, long now)
        {
            AdvanceCore(participants, now);
            // Darkness Falls (1.2): the way the raid walked, for late members with no certified chain to their post.
            if (_region == AutonomousDarknessFallsPolicy.RegionId && _objectives != null && !Complete && !Defeated)
                AutonomousDarknessFallsRaidTrail.Observe(this, _realm, participants, Front, now);
            if (!Complete && !Defeated) ObserveRouteDeaths(participants, now);
            GameNPC target = _target;
            if (target != null && target.IsAlive && target.InCombat && now >= _nextControlSample)
            {
                _nextControlSample = now + ControlSampleMilliseconds;
                _control.Add(participants.Count(p => p.IsAlive && p.CurrentRegionID == target.CurrentRegionID && p.IsMezzed),
                    participants.Count(p => p.IsAlive && p.CurrentRegionID == target.CurrentRegionID && p.IsStunned));
            }
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
                         $"at={target.X},{target.Y},{target.Z} spawnDistance={(int)target.GetDistanceTo(target.SpawnPoint)} busyWith=\"{busyWith}\" " +
                         $"mezzedAvg={_control.MezzedAverage} mezzedPeak={_control.MezzedPeak} stunnedPeak={_control.StunnedPeak} samples={_control.Samples}");
                _control = default;
                // Where the raid's damage went in that minute, and what hurt it (diagnostics).
                EpicFightLedger.Report(_region, target.Name, 60);
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

        /// <summary>Raid-wide deaths outside an attempt are judged over this window.</summary>
        public const long RouteDeathWindowMilliseconds = 4 * 60_000;
        /// <summary>A boss this close to where the raiders fell (their centre) is the one that killed them.</summary>
        public const float RouteKillerRadius = 2_500f;
        private readonly HashSet<GameLiving> _standingInside = new();
        private readonly Queue<(long At, Vector3 Where)> _routeDeaths = new();

        /// <summary>
        /// Raiders falling outside an attempt (at a muster, on the walk) are the raid's wipe when, within
        /// <see cref="RouteDeathWindowMilliseconds"/>, half of those inside fell and it was the raid's fight by PR #193's rule
        /// (half the living raid was there, or half of it fell). <paramref name="standingInside"/>: raiders alive inside now;
        /// <paramref name="livingRaid"/>: alive anywhere now.
        /// </summary>
        public static bool IsRouteWipe(int fallen, int standingInside, int livingRaid) =>
            IsWipe(standingInside + fallen, fallen, 100, 100, livingRaid + fallen);

        /// <summary>
        /// Raid-wide deaths outside an attempt count as a wipe once per fight (pure): <see cref="IsRouteWipe"/> as before (half
        /// of the raiders inside fell within the window, so at least half of those at the fight did too), and the fight was not
        /// already counted: the boss that scored the last such wipe has not left combat since (<paramref name="fightAlreadyCounted"/>). Owner's PC, 2026-11-16 (build ccba364, Tuscaran Glacier): one unplanned
        /// fight with Queen Kula and King Tuscar during a regroup was counted twice in 111 s as wipes at King Tuscar
        /// (20:39:59 engaged=129 fallen=65, 20:41:50 engaged=76 fallen=38, both lowest=100), and the second ended the raid.
        /// </summary>
        public static bool CountsRouteWipe(int fallen, int standingInside, int livingRaid, bool fightAlreadyCounted) =>
            !fightAlreadyCounted && IsRouteWipe(fallen, standingInside, livingRaid);

        /// <summary>The boss the last raid-wide wipe outside an attempt was scored at, while that fight goes on.</summary>
        private GameNPC _routeWipeKiller;

        /// <summary>
        /// Owner's PC, 2026-11-14 (build e3b8238): Icelord Agmundr, set aside after a stalled muster, killed 80 raiders at the
        /// harrower's muster beside his spawn in two minutes, and nothing scored it: no attempt was open, so the raid ended on
        /// the stall clock with "wipes=1". Deaths of raiders inside while no attempt is open are now counted; a raid-wide
        /// loss to a boss (the living boss in combat nearest to where they fell) is a wipe at that boss, with its regroup and
        /// Special Forces call, and a second one defeats the raid as any second wipe does.
        /// </summary>
        private void ObserveRouteDeaths(GameLiving[] participants, long now)
        {
            foreach (GameLiving p in _standingInside)
                if (!p.IsAlive && !_musterEngaged) _routeDeaths.Enqueue((now, new Vector3(p.X, p.Y, p.Z)));
            _standingInside.Clear();
            foreach (GameLiving p in participants)
                if (p.IsAlive && p.CurrentRegionID == _region) _standingInside.Add(p);
            while (_routeDeaths.Count > 0 && now - _routeDeaths.Peek().At > RouteDeathWindowMilliseconds) _routeDeaths.Dequeue();
            // The fight a route wipe was scored in ends when its boss leaves combat (or dies).
            if (_routeWipeKiller != null && (!_routeWipeKiller.IsAlive || !_routeWipeKiller.InCombat)) _routeWipeKiller = null;
            if (_musterEngaged) { _routeDeaths.Clear(); return; }
            int fallen = _routeDeaths.Count;
            if (fallen < MinimumRaidersAtTarget) return;
            int livingRaid = participants.Count(p => p.IsAlive);
            if (!IsRouteWipe(fallen, _standingInside.Count, livingRaid)) return;
            Vector3 centre = default;
            foreach (var d in _routeDeaths) centre += d.Where;
            centre /= fallen;
            GameNPC killer = _live.OfType<GameEpicBoss>()
                .Where(n => n.IsAlive && n.InCombat && n.Realm == eRealm.None && n.CurrentRegionID == _region &&
                            Vector3.Distance(new Vector3(n.X, n.Y, n.Z), centre) <= RouteKillerRadius)
                .OrderBy(n => Vector3.DistanceSquared(new Vector3(n.X, n.Y, n.Z), centre)).FirstOrDefault();
            _routeDeaths.Clear();
            if (killer != null && !CountsRouteWipe(fallen, _standingInside.Count, livingRaid, _routeWipeKiller != null))
            {
                log.Warn($"REALM_EXPEDITION_ROUTE_LOSSES region={_region} fallen={fallen} inside={_standingInside.Count} raid={livingRaid} " +
                         $"killer=\"{killer.Name}\" at={(int)centre.X},{(int)centre.Y},{(int)centre.Z} target=\"{TargetName}\" " +
                         $"reason=\"this fight was already counted as a wipe at {_routeWipeKiller?.Name}; no attempt used\"");
                return;
            }
            if (killer == null)
            {
                log.Warn($"REALM_EXPEDITION_ROUTE_LOSSES region={_region} fallen={fallen} inside={_standingInside.Count} raid={livingRaid} " +
                         $"at={(int)centre.X},{(int)centre.Y},{(int)centre.Z} target=\"{TargetName}\" " +
                         $"reason=\"half the raid inside fell outside an attempt and no boss was fighting there; no attempt used\"");
                return;
            }
            _routeWipeKiller = killer;
            int failures = _failedAttempts[killer.Name] = FailedAttempts(killer.Name) + 1;
            _wipes.Enqueue((killer.Name, failures, _standingInside.Count + fallen, fallen));
            log.Warn($"REALM_EXPEDITION_WIPE region={_region} target=\"{killer.Name}\" level={killer.Level} failures={failures} " +
                     $"of={MaximumFailedAttempts} engaged={_standingInside.Count + fallen} fallen={fallen} health={killer.HealthPercent} " +
                     $"lowest={killer.HealthPercent} how=\"raid-wide deaths outside an attempt\" raidTarget=\"{TargetName}\" " +
                     $"at={(int)centre.X},{(int)centre.Y},{(int)centre.Z}");
            if (failures >= MaximumFailedAttempts)
            {
                DefeatedAt = killer.Name;
                Hold = true;
                Status = $"Defeated at {killer.Name}";
                return;
            }
            _regrouping = true;
            _regroupSince = _musterSince = now;
            _targetProgressTick = now;
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
        private long _nextTrailGateCheck;
        public const long TrailGateCheckMilliseconds = 60_000;

        /// <summary>
        /// Darkness Falls (1.2, build 4): an epic monster the raid has killed that is alive again on the raid's own trail
        /// (AutonomousDarknessFallsRaidTrail, the way every late member, return wave and Special Force walks) is the raid's
        /// next target while it is not fighting,
        /// so the raid clears it with a muster instead of its late members meeting it a few at a time. Owner's PC,
        /// 2026-11-14 (build e3b8238): Gatekeeper Dommel (level 65, aggro 500, 40-minute respawn) at the central crossing
        /// 39793,32004 killed 108 Hibernian raiders in seven batches (08:11, 10:04, 11:09, 11:18, 12:06, 12:47, 13:05),
        /// each after he had come back behind the raid, all at his own spot while the raid's target was High Lord Oro or
        /// Legion. The epic-respawn hold (RealmRaidRouteGate.HoldEpicRespawn) now keeps him down once the raid has killed
        /// him; this covers one that came back anyway (a respawn under ten minutes, or the hold switched off). True when
        /// the raid turned on one this turn.
        /// </summary>
        private bool TrailGate(GameNPC[] live, GameLiving[] participants, long now)
        {
            if (_region != AutonomousDarknessFallsPolicy.RegionId || _realm == eRealm.None || !RealmRaidRouteGate.On ||
                now < _nextTrailGateCheck || _target?.InCombat == true) return false;
            _nextTrailGateCheck = now + TrailGateCheckMilliseconds;
            if (!AutonomousDarknessFallsRaidTrail.TryPoints(_realm, out Vector3[] trail)) return false;
            GameNPC gate = null;
            foreach (GameNPC npc in live)
            {
                // Only one the raid has already killed (it came back behind the raid): a monster alive since before the raid
                // came stood where the lead raider walked and was answered then, or it is not on the walk.
                if (npc == _target || npc is not IGameEpicNpc || !_clearedNames.Contains(npc.Name) || !npc.IsAlive || npc.InCombat ||
                    npc.ObjectState != GameObject.eObjectState.Active || npc.Realm != eRealm.None ||
                    (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) != 0 ||
                    npc.Brain is not StandardMobBrain { AggroRange: > 0 } brain ||
                    _finalTypes.Contains(npc.GetType().Name) || IsEncounterHazard(npc) ||
                    EpicEncounterTactics.IsRequiredAddOfLiveBoss(npc, live) || _routeRetry.GetValueOrDefault(npc) > now)
                    continue;
                if (!RealmRaidRouteGate.Blocks(trail, new Vector3(npc.X, npc.Y, npc.Z), brain.AggroRange)) continue;
                gate = npc;
                break;
            }
            if (gate == null) return false;
            log.Warn($"REALM_EXPEDITION_TRAIL_GATE region={_region} target=\"{gate.Name}\" level={gate.Level} at={gate.X},{gate.Y},{gate.Z} " +
                     $"aggro={((StandardMobBrain)gate.Brain).AggroRange} trail={trail.Length} from=\"{TargetName}\" " +
                     $"reason=\"a living epic stands on the raid's trail; the raid clears it before its late members walk past it\"");
            TurnOn(gate, participants, now, "it stands on the raid's trail, where late members walk");
            return true;
        }

        private void TurnOn(GameNPC attacking, GameLiving[] participants, long now, string reason)
        {
            bool resolved = TryEncounterApproach(attacking, participants, out Vector3 approach);
            log.Warn($"REALM_EXPEDITION_TARGET_SWITCH region={_region} from=\"{TargetName}\" to=\"{attacking.Name}\" level={attacking.Level} " +
                     $"front={(int)approach.X},{(int)approach.Y},{(int)approach.Z} approach={(resolved ? "resolved" : "raw")} " +
                     $"reason=\"{reason}\"");
            _target = attacking; TargetName = attacking.Name; TargetLevel = attacking.Level;
            Front = Destination = approach;
            _targetProgressTick = _targetStartTick = now; _targetLowestHealth = _targetLastHealth = attacking.Health;
            _targetClosest = float.MaxValue; _targetApproachTick = now;
            Hold = false; Status = "Clearing " + attacking.Name;
        }

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
            Vector3? Reach(Vector3 floor, Func<Vector3, bool> accept)
            {
                foreach (Vector3 anchor in anchors)
                {
                    Zone anchorZone = WorldMgr.GetRegion(_region)?.GetZone((int)anchor.X, (int)anchor.Y);
                    if (anchorZone != null && nav.HasNavmesh(anchorZone) &&
                        AutonomousZonePointApproach.TryResolve(nav, anchorZone, anchor, floor, 220, out Vector3 approach, accept))
                        return approach;
                }
                return null;
            }
            // Every add gets its own stand: its party's shooters fight it from there (EpicEncounterTactics.Decide). In 1.1.2 only the
            // route's add had one, and on the 2026-11-08 container run the shooters given the other three orbs walked under them.
            var reasons = new List<string>();
            foreach (GameNPC a in adds)
            {
                Zone zone = WorldMgr.GetRegion(_region)?.GetZone(a.X, a.Y);
                if (TryRangedStand(nav, zone, At(a), At(boss), Reach, out Vector3 own, out string found))
                {
                    EpicEncounterTactics.SetStand(a, own);
                    if (add == null) { add = a; stand = own; reasons.Insert(0, found); }
                    continue;
                }
                reasons.Add($"{a.Name}@{a.X},{a.Y},{a.Z}: {found}");
                _routeRetry[a] = now + 60_000;
            }
            why = string.Join("; ", reasons);
            if (add == null) return false;
            EpicEncounterTactics.SetStand(boss, stand);
            return true;
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

        /// <summary>
        /// Darkness Falls (1.2): once the raid musters for its final encounter, an objective out of combat that is far from
        /// the front (one that respawned or came up since) is left (AutonomousDarknessFallsRaidRoute.LeavesForFinal).
        /// Hibernia's raid, 2026-11-14: Prince Asmoien came up while it mustered for Legion and High Lord Oro's copy respawned;
        /// it walked to both and back (10:39-11:30) and was then still mustering at 13:09.
        /// </summary>
        private bool LeftForTheFinal(GameNPC n, long now)
        {
            if (_region != AutonomousDarknessFallsPolicy.RegionId || _objectives == null || _finalTypes.Contains(n.GetType().Name) ||
                !RealmRaidNeutralEvents.IsObjective(_objectives, n.Name)) return false;
            if (!AutonomousDarknessFallsRaidRoute.LeavesForFinal(_finalMusterSince > 0, n.InCombat,
                    WalkEstimate(Front, new Vector3(n.X, n.Y, n.Z)))) return false;
            if (_lateObjectivesLogged.Add(n))
                log.Info($"REALM_EXPEDITION_LATE_OBJECTIVE_LEFT region={_region} target=\"{n.Name}\" at={n.X},{n.Y},{n.Z} " +
                         $"front={(int)Front.X},{(int)Front.Y},{(int)Front.Z} reason=\"the raid musters for its final; it came up since\"");
            return true;
        }

        private void AdvanceCore(GameLiving[] participants, long now)
        {
            if (Complete || Defeated) return;
            if (_routeStartTick == 0) _routeStartTick = now;
            GameNPC[] live = WorldMgr.GetRegion(_region)?.Objects.OfType<GameNPC>().ToArray() ?? [];
            _live = live;
            EpicFightLedger.Watch(_region, now);
            // Raiders step out of a ground hazard and no battle post goes in one (EpicEncounterTactics.ObserveHazards).
            EpicEncounterTactics.ObserveHazards(live);
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
            if (FinalsDown()) { Complete = true; Status = IsGate ? "The way is clear" : "Final encounter defeated"; return; }
            if (TrailGate(live, participants, now)) return;
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
            // A boss attacking the raid while it fights a lesser named on its way is fought first (play test duo4).
            if (_target?.IsAlive == true && _target is not GameEpicBoss &&
                TurnsOnAttacker(_target, HostileEncounter(live, participants)) is { } boss)
            {
                TurnOn(boss, participants, now, "a boss is fighting the raid while it clears a lesser named");
                return;
            }
            if (_target?.IsAlive == true && _target.ObjectState == GameObject.eObjectState.Active)
            {
                // A boss that blasts the room (Xanxicar): its withdraw spots on the floor towards the way in.
                EpicEncounterTactics.EnsureBlastPath(_target,
                    Entrance is { } way && way.TargetRegion == _region ? new Vector3(way.TargetX, way.TargetY, way.TargetZ) : null);
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
                // One of a boss's adds that die in order (Scurceol Hyrde's orbs): the whole raid commits to it, so the clock
                // waits for a third of the raiders inside to stand near it, not only for a party (RequiredAddMusterShort).
                GameNPC orderedOwner = OrderedAddOwner(_target, live);
                int insideNow = orderedOwner == null ? 0 : participants.Count(p => p.IsAlive && p.CurrentRegionID == _region);
                if ((near < MinimumRaidersAtTarget || orderedOwner != null && RequiredAddMusterShort(near, insideNow)) &&
                    (now - _targetStartTick < TargetAbsencePauseLimitMilliseconds || approaching))
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
                    // Never a switch to the target itself: the monster fighting the raid is answered (PC log e3b8238: "Crypt
                    // Lord -> Crypt Lord" every 10 s while his dark lights killed the raid).
                    if (attacker == _target) attacker = hostile;
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
                // An add of a boss whose adds die in order is never swapped for the next one (each swap moved the raid's
                // stand): the boss's whole fight is set aside, and the raid comes back to this same add (RequiredAddStays).
                if (RequiredAddStays(orderedOwner != null))
                {
                    log.Warn($"REALM_EXPEDITION_TARGET_SKIPPED region={_region} target=\"{_target.Name}\" at={_target.X},{_target.Y},{_target.Z} " +
                             $"health={_target.HealthPercent} front={(int)Front.X},{(int)Front.Y},{(int)Front.Z} near={near} inside={insideNow} " +
                             $"encounter=\"{orderedOwner.Name}\" reason=\"no damage for five minutes with the raid at it; its encounter is set " +
                             $"aside and the raid comes back to this add, not the next one\"");
                    SetTacticAside(orderedOwner, $"no damage to {_target.Name} for five minutes ({near} raiders near it)", now);
                    return;
                }
                log.Warn($"REALM_EXPEDITION_TARGET_SKIPPED region={_region} target=\"{_target.Name}\" at={_target.X},{_target.Y},{_target.Z} " +
                         $"health={_target.HealthPercent} front={(int)Front.X},{(int)Front.Y},{(int)Front.Z} near={near} reason=\"no damage for five minutes\"");
                _routeRetry[_target] = now + BlockedTargetRetryMilliseconds;
                _musterProgress.Remove(_target);
                }
            }
            _target = null;
            // A named that attacks the raid while it has no target (walking to its next encounter or its final staging) is
            // fought, as it is while the raid gathers for another: play test 2026-11-07, Caer Sidi's raid walked to the
            // horsemen's room past the Host, who killed 40 of it with nobody turning on him.
            if (TurnsOnAttacker(_target, HostileEncounter(live, participants)) is { } attacking)
            {
                TurnOn(attacking, participants, now, "it is fighting the raid on its way");
                return;
            }
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
            {
                // One unbroken wait: a pulse that went elsewhere in between (a target, a fight) starts it again.
                if (_formedWaitSince == 0 || now - _formedWaitLast > FormedWaitGapMilliseconds) _formedWaitSince = now;
                _formedWaitLast = now;
                // Bounded (owner's PC, 2026-11-14, build e3b8238): Tuscaran Glacier held "Waiting for formed parties inside"
                // from 07:45 to 07:57 with 83 raiders inside and none at the front, because the three-minute regroup above
                // starts again whenever someone gets a little closer. After the limit the front moves to the raid's main body
                // (any party's worth there), and the route goes on from where the raid stands.
                if (FormedWaitTooLong(_formedWaitSince, now) && inside.Length >= MinimumRaidersAtTarget &&
                    TryRegroupFront(inside, out Vector3 body, FormedWaitMinimumBody))
                {
                    log.Warn($"REALM_EXPEDITION_FRONT_REGROUP region={_region} from={(int)Front.X},{(int)Front.Y},{(int)Front.Z} " +
                             $"to={(int)body.X},{(int)body.Y},{(int)body.Z} inside={inside.Length} " +
                             $"reason=\"waited {(now - _formedWaitSince) / 60_000} minutes for formed parties at the front; the route goes on from the main body\"");
                    Front = Destination = body;
                    _frontAbsentSince = 0;
                    _formedWaitSince = 0;
                }
                else
                {
                    if (FormedWaitTooLong(_formedWaitSince, now) && now >= _nextFormedWaitLog)
                    {
                        _nextFormedWaitLog = now + FormedPartiesWaitMilliseconds;
                        int living = participants.Count(p => p.IsAlive);
                        log.Warn($"REALM_EXPEDITION_FORMED_WAIT region={_region} minutes={(now - _formedWaitSince) / 60_000} inside={inside.Length} " +
                                 $"living={living} outside={participants.Count(p => p.IsAlive && p.CurrentRegionID != _region)} " +
                                 $"front={(int)Front.X},{(int)Front.Y},{(int)Front.Z} " +
                                 $"reason=\"too few raiders inside to go on; the rest are outside or on their way back\"");
                    }
                    Hold = true; Destination = Front; Status = "Waiting for formed parties inside"; return;
                }
            }
            _formedWaitSince = 0;
            var nav = PathfindingProvider.Instance;
            Zone zone = WorldMgr.GetRegion(_region)?.GetZone((int)Front.X, (int)Front.Y);
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            { Hold = true; Status = "Blocked: dungeon navigation unavailable"; return; }
            GameNPC[] encounters = live.Where(n => n is IGameEpicNpc && n.IsAlive && n.ObjectState == GameObject.eObjectState.Active).ToArray();
            // Monsters that would join a boss's fight (EpicEncounterTactics.ClearFirst: Spindler Broodmother's adult spindlers)
            // are killed before that boss: each is a target although another of its name is already dead, and although a play
            // test's focus names only the boss.
            Dictionary<GameNPC, GameNPC> clearFirst = live.Select(n => (Add: n, Boss: EpicEncounterTactics.ClearFirstOwner(n, live)))
                .Where(pair => pair.Boss != null).ToDictionary(pair => pair.Add, pair => pair.Boss);
            // The first hour's sweep clears again what came back on the raid's walk back to the front (SweepTarget).
            Vector3[] walkBack = _objectives == null && now - _routeStartTick < EpicTrashPhaseMilliseconds ? WalkBack(nav) : null;
            GameNPC[] candidates = live.Where(n => n.IsAlive && n.ObjectState == GameObject.eObjectState.Active && n.Realm == eRealm.None &&
                n.Level > 0 && n is not GameBot && n is not GameSummonedPet && n.Brain is not IControlledBrain &&
                n is not GameMerchant && n is not GameTrainer && n is not GameTeleporter && n is not GameTaxi &&
                ((n.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0 || EpicEncounterTactics.FinishableUntargetable(n)) &&
                !_cleared.Contains(n) &&
                // A pet class's charm body (a copy of an overland monster it is charming) is no dungeon monster: a
                // Hibernian raid stood four minutes by a "grogan" in Galladoria that none of it could attack (run 22).
                n.TempProperties.GetProperty<bool>(AutonomousPetSupport.SyntheticCharmPetProperty) != true &&
                !IsEncounterHazard(n) &&
                // An objective raid's cleared objective is done by name, whatever its class: High Lord Oro's copies share his
                // name and are no epic NPCs, so a respawned copy was taken for the objective again (Hibernia's Darkness Falls
                // raid, 2026-11-14: "High Lord Oro" three times, each a walk of the whole raid back down).
                !((n is IGameEpicNpc || _objectives != null && RealmRaidNeutralEvents.IsObjective(_objectives, n.Name)) &&
                  _clearedNames.Contains(n.Name) && !_finalTypes.Contains(n.GetType().Name) && !clearFirst.ContainsKey(n)) &&
                !LeftForTheFinal(n, now) &&
                // A neutral expedition goes for its named encounters only; monsters on the way are
                // fought when they attack (Darkness Falls holds 2,465 spawns).
                // A play test's focus leaves out the other named encounters (and the final, unless named) while they are out of
                // combat; the way in is cleared as in any raid (a focus that skipped the first-hour clear left the Caer Sidi raid
                // fighting the entrance's monsters and never reaching the horsemen's room), and a named that fights the raid is
                // fought (the Host killed 159 raiders on the way up while the focus left him out, 2026-11-07).
                !(Focus != null && (n is IGameEpicNpc || _finalTypes.Contains(n.GetType().Name)) && !n.InCombat &&
                  !InFocus(Focus, n.Name, n.GetType().Name) &&
                  !(clearFirst.TryGetValue(n, out GameNPC focusBoss) && InFocus(Focus, focusBoss.Name, focusBoss.GetType().Name))) &&
                // The first hour's sweep never clears a monster that came back since the route began, unless it stands on
                // (or within its aggro of) the walk back from the way in to the front (SweepTarget).
                (_objectives == null ? SweepTarget(now, _routeStartTick, n.SpawnTick, walkBack, new(n.X, n.Y, n.Z),
                        n.Brain is StandardMobBrain { AggroRange: var aggro } ? aggro : 0) || n is IGameEpicNpc || _finalTypes.Contains(n.GetType().Name) ||
                        IsEncounterAdd(n, Front, encounters) || clearFirst.ContainsKey(n)
                    // A monster that would join an objective's fight (High Lord Baln's wing) is killed first in an objective
                    // raid too: without it among the targets the objective waited for it for ever.
                    : RealmRaidNeutralEvents.IsObjective(_objectives, n.Name) || clearFirst.ContainsKey(n)))
                .OrderBy(n => _finalTypes.Contains(n.GetType().Name) ? 1 : 0)
                // An objective raid takes a boss's clear-first monsters in its walk order (they stand by the boss), not before
                // everything else: High Lord Baln's Dukes would have drawn the Darkness Falls raid straight to his wing.
                .ThenBy(n => IsEncounterAdd(n, Front, encounters) || _objectives == null && clearFirst.ContainsKey(n) ? 0 : 1)
                .ThenBy(n => WalkEstimate(Front, new(n.X, n.Y, n.Z))).ToArray();
            // Once a minute while the sweep runs: the monsters it left out because they came back (SweepTarget).
            if (_objectives == null && now >= _nextRespawnsLeftLog && now - _routeStartTick < EpicTrashPhaseMilliseconds)
            {
                _nextRespawnsLeftLog = now + 60_000;
                var picked = candidates.ToHashSet();
                GameNPC[] back = live.Where(n => n.IsAlive && n.ObjectState == GameObject.eObjectState.Active && n.Realm == eRealm.None &&
                        n is not IGameEpicNpc && n is not GameBot && n is not GameSummonedPet && !picked.Contains(n) &&
                        RespawnedSinceRouteStart(n.SpawnTick, _routeStartTick))
                    .OrderBy(n => WalkEstimate(Front, new(n.X, n.Y, n.Z))).ToArray();
                // Came back too but still targets: on the walk back, or a clear-first monster or an encounter add.
                int onWalk = candidates.Count(n => n is not IGameEpicNpc && RespawnedSinceRouteStart(n.SpawnTick, _routeStartTick));
                if (back.Length > 0)
                    log.Info($"REALM_EXPEDITION_RESPAWNS_LEFT region={_region} left={back.Length} nearest=\"{back[0].Name}\" " +
                             $"at={back[0].X},{back[0].Y},{back[0].Z} candidates={candidates.Length} keptRespawned={onWalk} " +
                             $"walk={walkBack?.Length ?? 0} " +
                             $"reason=\"came back since the route began, off the walk from the way in to the front; the first-hour sweep does not clear them again (fought if they attack)\"");
            }
            // A gate whose bosses can't be taken as targets (gone, or no longer attackable) has nothing left to fight: the
            // raid goes on to its own dungeon instead of waiting at the way out.
            if (IsGate && candidates.Length == 0 && !Blocked)
            {
                Blocked = true;
                log.Warn($"REALM_EXPEDITION_ROUTE_GATE_EMPTY region={_region} gates=\"{string.Join(",", Gates.Select(g => g.Name))}\" " +
                         $"reason=\"no boss of the gate is a target; the raid goes on\"");
                return;
            }
            // Probe from the front and from where the parties actually stand, near or far:
            // Tuscaran's front once sat on a pad under the floor its parties stood on.
            Vector3[] anchors = DistinctAnchors(Front, inside
                    .Where(p => p.Group == null || p.Group.LivingLeader == p)
                    .Select(p => new Vector3(p.X, p.Y, p.Z)), 6);
            int probes = 0;
            long probeStart = System.Diagnostics.Stopwatch.GetTimestamp();
            // Resume after the last examined candidate. With more blocked
            // spawns than the per-minute probe budget, always starting at zero
            // would retry the same expired failures and starve later rooms.
            for (int examined = 0; examined < candidates.Length && probes < 3; examined++)
            {
                // Out of time for this pass: the next pass goes on with the next candidate (DividedRealms).
                if (examined > 0 && ProbeBudgetSpent(System.Diagnostics.Stopwatch.GetElapsedTime(probeStart).TotalMilliseconds)) break;
                GameNPC npc = candidates[_probeCursor % candidates.Length];
                _probeCursor = (_probeCursor + 1) % candidates.Length;
                if (_routeRetry.GetValueOrDefault(npc) > now) continue;
                // Not now by its tactic (the Crypt Lord's patrol near this court monster, the Host off his ambush, a perched
                // flyer): another target is taken meanwhile (EpicEncounterTactics.PullWait).
                if (HeldByTactic(npc, live, now) != null) continue;
                // A boss waits while a monster that would join its fight can still be killed first (unless it is fighting already).
                if (!npc.InCombat && clearFirst.Any(pair => pair.Value == npc && _routeRetry.GetValueOrDefault(pair.Key) <= now))
                {
                    if (now >= _nextClearFirstLog)
                    {
                        _nextClearFirstLog = now + 60_000;
                        log.Info($"REALM_EXPEDITION_CLEAR_FIRST region={_region} boss=\"{npc.Name}\" left={clearFirst.Count(pair => pair.Value == npc)} " +
                                 $"names=\"{string.Join(",", clearFirst.Where(pair => pair.Value == npc).Select(pair => pair.Key.Name).Distinct())}\"");
                    }
                    continue;
                }
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
                bool reachable = false, outOfTime = false;
                int firstAnchor = FirstProbeAnchor(ReferenceEquals(_probeResumeNpc, npc), _probeResumeAnchor, anchors.Length);
                _probeResumeNpc = null;
                for (int a = firstAnchor; a < anchors.Length; a++)
                {
                    Vector3 anchor = anchors[a];
                    Zone anchorZone = WorldMgr.GetRegion(_region)?.GetZone((int)anchor.X, (int)anchor.Y) ?? zone;
                    if (nav.HasNavmesh(anchorZone) && AutonomousZonePointApproach.TryResolve(nav, anchorZone, anchor, raw, 220, out approach))
                    { reachable = true; break; }
                    if (a + 1 < anchors.Length && ProbeBudgetSpent(System.Diagnostics.Stopwatch.GetElapsedTime(probeStart).TotalMilliseconds))
                    { _probeResumeNpc = npc; _probeResumeAnchor = a + 1; outOfTime = true; break; }
                }
                if (outOfTime)
                {
                    // The same candidate again on the next pass, from its next anchor; it is not set aside as unreachable.
                    _probeCursor = (_probeCursor + candidates.Length - 1) % candidates.Length;
                    Hold = true; Destination = Front; Status = "Scouting the way to " + npc.Name;
                    return;
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
                // Never a target behind a living boss's barrier (Easmarach's falls): the walk there ends in the barrier's push,
                // again and again (owner's PC, 2026-11-15). One the dungeon rule did not turn into the boss (it fights already)
                // waits until the barrier is down; the boss itself is a target in front of it.
                if (EpicEncounterTactics.BehindLiveBarrier(_region, approach, live) || EpicEncounterTactics.BehindLiveBarrier(_region, raw, live))
                {
                    log.Info($"REALM_EXPEDITION_BARRED region={_region} target=\"{npc.Name}\" at={(int)raw.X},{(int)raw.Y},{(int)raw.Z} " +
                             $"reason=\"behind a living boss's barrier; fought once it is down\"");
                    _routeRetry[npc] = now + 60_000;
                    continue;
                }
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
            // Not while the trigger lies behind a living boss's barrier (Galladoria's bridge, past Easmarach's falls).
            if (!EpicEncounterTactics.BehindLiveBarrier(_region, _trigger, live) &&
                AutonomousZonePointApproach.TryResolve(nav, zone, Front, _trigger, 220, out Vector3 trigger))
                Front = Destination = trigger;
            TargetName = "Final encounter staging";
            Status = candidates.Length == 0 ? "Waiting for the next scripted encounter phase" :
                $"Waiting for the next scripted encounter phase; {candidates.Length} airborne patrols remain";
        }
    }
}
