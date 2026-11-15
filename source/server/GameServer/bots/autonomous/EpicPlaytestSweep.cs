using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.Events;
using DOL.GS.Commands;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// The play test's sweep (/epictest sweep, 1.2; owner 2026-11-13: "run through every dungeon and epic encounter in each
    /// realm yourself through a true playtest without losing access"). It runs every raid once, one after another, on the
    /// server alone: a locked screen or a dropped link to the PC can't stop it, and each raid's own clocks end it (the
    /// play test's hour without progress). Each raid is the /epictest start path at its period size.
    /// A start may name the raids and their order (a comma list), run one lane per realm side by side (<c>parallel</c>,
    /// bots only) and set the per-raid cap (<c>cap=</c>minutes or off); 2026-11-15, for a night that has to fit the
    /// raid bar, the big three's re-runs and the open-world bosses (docs/OVERNIGHT-SWEEP-1.2.md).
    ///
    /// The guest: the GM who started the sweep, if asked, takes a seat in every raid as a player would (the raid leader's
    /// invite, accepted). Their realm is set to the raid's for it and put back when the sweep ends (as /player realm does),
    /// so one character can sit in every realm's raids. Nobody moves the guest's keys, so between raids and on the march the
    /// server keeps them with their party ("tag-along"): away from it for 20 seconds (thrown, jailed, left behind), they are
    /// moved back to its leader; dead for 30 seconds, they are raised there. Every such step is logged (EPIC_SWEEP_GUEST), so
    /// a result with a guest says what the server did for them. Test only: nothing here runs unless a GM starts it.
    ///
    /// What the sweep changes on its guest (the account's level, the character's realm) is saved before it is changed
    /// (<see cref="EpicPlaytestSweepGuestRecord"/>), so a server stopped mid-sweep, even force-killed, can't strand the
    /// account: the next start, or the account's next login, puts it back (2026-11-14: a sweep was running when the
    /// launcher force-closed the server, and the owner's GM account came back a plain player). A graceful stop ends the
    /// sweep first, so its own restore runs.
    /// </summary>
    public static class EpicPlaytestSweep
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(EpicPlaytestSweep));
        private static readonly object Sync = new();

        public const long PulseMs = 10_000;
        public const long PauseBetweenMs = 3 * 60_000;
        /// <summary>A raid the sweep started is stopped after this long, whatever its own clocks say (the default;
        /// <c>cap=</c> on the start changes it for that sweep).</summary>
        public const long RaidCapMs = 4 * 60 * 60_000L;
        /// <summary>The smallest cap a start may ask for: a raid needs its muster and walk before it fights.</summary>
        public const int MinimumCapMinutes = 30;
        /// <summary>How far apart a parallel sweep's lanes start their first raids, so three musters don't land in one tick.</summary>
        public const long LaneStaggerMs = 60_000;
        public const long GuestAwayMs = 20_000;
        public const long GuestDeadMs = 30_000;
        public const float GuestAwayRange = 3_000;
        /// <summary>A raid whose start failed (its boss respawning, no bots free) is tried again after the rest, this often.</summary>
        public const int Attempts = 3;

        private sealed class Step
        {
            public string Id;
            public int Tries;
            public string LastFailure;
        }

        /// <summary>
        /// One queue of raids run one after another. A sweep has one lane, or with <c>parallel</c> one per realm
        /// (2026-11-15: the night's list is over 30 hours one raid at a time; each realm raids with its own bots, so the
        /// realms run side by side and no two sweep raids ever ask one realm's bots at once).
        /// </summary>
        private sealed class Lane
        {
            public readonly List<Step> Queue = [];
            public string Name;
            public Step Current;
            public long CurrentStarted, NextStartAt;
            /// <summary>The last EPIC_SWEEP_HELD logged for this lane (logged again only when it changes).</summary>
            public string Held;
        }

        private static readonly List<Lane> Lanes = [];
        private static readonly List<string> Results = [];
        private static long _capMs = RaidCapMs;
        private static bool _parallel;
        private static long _nextPulse, _guestAwaySince, _guestDeadSince, _nextSeatTry;
        /// <summary>The raid the guest sits in: the only lane's (a guest sweep has one lane).</summary>
        private static Step GuestRaid => Lanes.Count == 1 ? Lanes[0].Current : null;
        // A seat the guest did not get yet (the raid had no party with room when it started): tried again every
        // SeatRetryMs until the raid goes in (2026-11-13, Gjalpinulva: "no_party" at the start, and the guest was left).
        private static AutonomousRealmRaid.Definition _seatPendingFor;
        private static RealmRaidMuster.Hub _seatPendingHub;
        public const long SeatRetryMs = 10_000;
        private static GamePlayer _guest;
        private static eRealm _guestHomeRealm;
        /// <summary>The guest's account level before the sweep (0: not lowered).</summary>
        private static uint _guestHomePrivLevel;
        /// <summary>The guest's account and character, as its saved record names them.</summary>
        private static string _guestAccount, _guestCharacterId, _guestName;
        private static bool _running;

        /// <summary>The order (pure): the definitions' own order, Darkness Falls last (owner 2026-11-13: it was the one raid without raid AI then; it keeps the last place).</summary>
        public static string[] Order(IEnumerable<string> ids) =>
            ids.OrderBy(id => id.StartsWith("darkness-", StringComparison.Ordinal) ? 1 : 0).ToArray();

        /// <summary>The ids a start names (pure): "all", "openworld" (the open-world big bosses' raids, RealmRaidOpenWorld), or a
        /// comma list; unknown ids come back in <paramref name="unknown"/>.</summary>
        public static string[] Parse(string word, IReadOnlyCollection<string> known, out string[] unknown)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                unknown = [];
                return Order(known);
            }
            if (word.Equals(RealmRaidOpenWorld.SweepWord, StringComparison.OrdinalIgnoreCase))
            {
                unknown = [];
                return Order(known.Where(RealmRaidOpenWorld.Is));
            }
            string[] asked = word.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            unknown = asked.Where(a => !known.Contains(a)).ToArray();
            return Order(asked.Where(known.Contains).Distinct());
        }

        /// <summary>
        /// The lanes a sweep runs (pure): one lane with every raid, or with <paramref name="parallel"/> one lane per realm in
        /// the order the realms first appear, each keeping the raids' own order (Darkness Falls stays last in its lane).
        /// </summary>
        public static (string Name, string[] Ids)[] SplitLanes(string[] ordered, Func<string, eRealm> realmOf, bool parallel)
        {
            if (!parallel) return [("sweep", ordered)];
            return ordered.GroupBy(realmOf)
                .Select(g => (GlobalConstants.RealmToName(g.Key), g.ToArray()))
                .ToArray();
        }

        /// <summary>
        /// The per-raid cap a start asks for (pure): null (no <c>cap=</c>: the default four hours), "off" (none: the raid's
        /// own stall clock ends it) or whole minutes of at least <see cref="MinimumCapMinutes"/>. 0 is no cap.
        /// </summary>
        public static bool TryParseCap(string value, out long capMs)
        {
            capMs = RaidCapMs;
            if (value == null) return true;
            if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                capMs = 0;
                return true;
            }
            if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int minutes) ||
                minutes < MinimumCapMinutes)
                return false;
            capMs = minutes * 60_000L;
            return true;
        }

        /// <summary>
        /// Where <c>add</c> puts each raid (pure): with <paramref name="parallel"/> its realm's lane (a new lane when that realm
        /// has none yet), else the one lane, in the order given (Darkness Falls last). A raid the sweep already holds, queued
        /// or running, is left out (<paramref name="skipped"/>): the night's commands may be typed twice without running a
        /// raid twice.
        /// </summary>
        public static (string Lane, string Id)[] PlanAdd(string[] ordered, string[] alreadyIn, string onlyLane,
            Func<string, eRealm> realmOf, bool parallel, out string[] skipped)
        {
            var held = new HashSet<string>(alreadyIn);
            skipped = ordered.Where(held.Contains).ToArray();
            return ordered.Where(id => !held.Contains(id))
                .Select(id => (parallel ? GlobalConstants.RealmToName(realmOf(id)) : onlyLane, id))
                .ToArray();
        }

        /// <summary>Why a start with a guest is refused (pure), or null: a guest sits in one raid at a time, so a parallel sweep runs bots only.</summary>
        public static string GuestRefusal(bool parallel, bool guest) =>
            parallel && guest ? "A parallel sweep runs bots only (a guest sits in one raid at a time): start it without guest." : null;

        /// <summary>Whether a sweep raid that has run this long is stopped by the cap (pure; a cap of 0 never stops one).</summary>
        public static bool CapReached(long capMs, long ranMs) => capMs > 0 && ranMs >= capMs;

        /// <summary>
        /// The party member the guest is moved to (pure): the one with the most party members within 2,000 units in its
        /// region, the leader first on a tie.
        /// </summary>
        public static int Anchor((ushort Region, int X, int Y, bool Leader)[] members)
        {
            int best = 0, bestNear = -1;
            for (int i = 0; i < members.Length; i++)
            {
                int near = members.Count(m => m.Region == members[i].Region &&
                    (long)(m.X - members[i].X) * (m.X - members[i].X) + (long)(m.Y - members[i].Y) * (m.Y - members[i].Y) <= 2_000L * 2_000L);
                if (near > bestNear || near == bestNear && members[i].Leader)
                {
                    best = i;
                    bestNear = near;
                }
            }
            return best;
        }

        /// <summary>How far from the raid's front a raider still counts as at it.</summary>
        public const float GuestFrontRange = 3_000;

        /// <summary>
        /// The raider the guest is moved to while its raid runs (pure): a living one in the region of the raid's front
        /// (2026-11-14, summoners-albion: the guest was moved to Ravooooora, who had died and released to the hub in
        /// region 1, while the raid fought in the dungeon). Among the raiders within <see cref="GuestFrontRange"/> of the
        /// front: the raid leader, else the guest's own party first, then the one with the most such raiders near it, then
        /// the nearest to the front. With nobody at the front, the raider in its region nearest to it (the head of the
        /// march). -1 when no living raider is in the front's region: the guest then stays where it is.
        /// </summary>
        public static int GuestAnchor((ushort Region, int X, int Y, bool Alive, bool RaidLeader, bool OwnParty)[] raiders,
            ushort frontRegion, int frontX, int frontY)
        {
            static long Dist2(int x1, int y1, int x2, int y2) => (long)(x1 - x2) * (x1 - x2) + (long)(y1 - y2) * (y1 - y2);
            long frontRange2 = (long)GuestFrontRange * (long)GuestFrontRange;
            int[] inRegion = Enumerable.Range(0, raiders.Length)
                .Where(i => raiders[i].Alive && raiders[i].Region == frontRegion).ToArray();
            if (inRegion.Length == 0) return -1;
            int[] atFront = inRegion.Where(i => Dist2(raiders[i].X, raiders[i].Y, frontX, frontY) <= frontRange2).ToArray();
            if (atFront.Length == 0)
                return inRegion.OrderBy(i => Dist2(raiders[i].X, raiders[i].Y, frontX, frontY)).First();
            foreach (int i in atFront)
                if (raiders[i].RaidLeader) return i;
            return atFront
                .OrderByDescending(i => raiders[i].OwnParty)
                .ThenByDescending(i => atFront.Count(j => Dist2(raiders[i].X, raiders[i].Y, raiders[j].X, raiders[j].Y) <= 2_000L * 2_000L))
                .ThenBy(i => Dist2(raiders[i].X, raiders[i].Y, frontX, frontY))
                .First();
        }

        /// <summary>How far above or below the anchor a snapped landing may be: more is another floor.</summary>
        public const float LandingMaxRise = 64;

        /// <summary>
        /// Where the guest lands beside its anchor (pure): the point 40 units off it snapped to the navmesh, when the snap
        /// found floor on the anchor's level that connects to the anchor; otherwise the anchor's own position. An unsnapped
        /// point beside the anchor dropped the guest through the world in dungeons (2026-11-14, five times and more).
        /// </summary>
        public static Vector3 Landing(Vector3 anchor, bool snapped, Vector3 snappedBeside, bool connected) =>
            snapped && connected && Math.Abs(snappedBeside.Z - anchor.Z) <= LandingMaxRise ? snappedBeside : anchor;

        /// <summary>
        /// What tag-along does with the guest now (pure): nothing, "move" to the party, or "raise" there. A guest within
        /// <see cref="GuestAwayRange"/> of the boss (<paramref name="nearFight"/>) is with the raid even when its party
        /// stands farther back (2026-11-13, Golestandt: the guest was pulled out of spell range every 20 seconds to a
        /// healer standing 3,000 units from the dragon).
        /// </summary>
        public static string GuestAction(bool alive, bool inGroup, bool sameRegion, float distance, long awayFor, long deadFor,
            bool nearFight = false)
        {
            if (!inGroup) return null;
            if (!alive) return deadFor >= GuestDeadMs ? "raise" : null;
            if (IsAway(sameRegion, distance, nearFight) && awayFor >= GuestAwayMs) return "move";
            return null;
        }

        /// <summary>Whether the guest counts as away from its raid (pure).</summary>
        public static bool IsAway(bool sameRegion, float distance, bool nearFight) =>
            !nearFight && (!sameRegion || distance > GuestAwayRange);

        /// <summary>
        /// The level a client's commands are checked at (ScriptMgr.HandleCommand): the sweep's guest keeps the GM commands
        /// of the level it had, while it plays as a player.
        /// </summary>
        public static uint CommandPrivLevel(GameClient client)
        {
            uint level = client?.Account?.PrivLevel ?? 0;
            lock (Sync)
                return _running && _guestHomePrivLevel > level && client?.Player != null && client.Player == _guest ? _guestHomePrivLevel : level;
        }

        public static bool Running
        {
            get { lock (Sync) return _running; }
        }

        public static string Start(string word, GamePlayer guest) => Start(word, guest, false, RaidCapMs);

        /// <summary>
        /// Starts a sweep. <paramref name="parallel"/>: one lane per realm, side by side (bots only: a guest sits in one raid
        /// at a time). <paramref name="capMs"/>: the per-raid cap, 0 for none.
        /// </summary>
        public static string Start(string word, GamePlayer guest, bool parallel, long capMs)
        {
            lock (Sync)
            {
                if (_running) return "A sweep is already running: /epictest sweep status, or /epictest sweep stop.";
                if (GuestRefusal(parallel, guest != null) is string refusal) return refusal;
                string[] ids = Parse(word, AutonomousRealmRaid.Definitions.Select(d => d.Id).ToArray(), out string[] unknown);
                if (unknown.Length > 0) return $"No raid {string.Join(", ", unknown)}; /epictest list shows them.";
                if (ids.Length == 0) return "No raids to sweep.";
                var lanes = SplitLanes(ids, id => AutonomousRealmRaid.Definitions.First(d => d.Id == id).Realm, parallel);
                eRealm homeRealm = guest?.Realm ?? eRealm.None;
                uint homePrivLevel = 0;
                // A GM can't be attacked, takes no fall damage and is left out of every boss mechanic that picks players
                // (AbstractServerRules.IsAllowedToAttack, CanTakeFallDamage, the scripts' PrivLevel == Player checks): the
                // guest plays as a player for the sweep (2026-11-13: Manawatch stood in Golestandt's breath at full health).
                // Their GM commands stay (CommandPrivLevel); the level comes back when the sweep ends. Saved first
                // (BeginGuest): a sweep that never ends can't strand the account.
                if (guest?.Client?.Account is { } account)
                {
                    if (!BeginGuest(GameServer.Database, account, guest.DBCharacter?.ObjectId, guest.Name, ref homeRealm, out homePrivLevel))
                        return "The sweep did not start: what it changes on its guest (account level, realm) could not be saved first. " +
                               "Try again, or start it without a guest.";
                    if (guest.Realm != homeRealm) guest.Realm = homeRealm;
                    if (homePrivLevel > 0 && guest.IsStealthed) guest.Stealth(false);
                }
                Lanes.Clear();
                long start = GameLoop.GameLoopTime;
                for (int i = 0; i < lanes.Length; i++)
                {
                    var lane = new Lane { Name = lanes[i].Name, NextStartAt = start + i * LaneStaggerMs };
                    lane.Queue.AddRange(lanes[i].Ids.Select(id => new Step { Id = id }));
                    Lanes.Add(lane);
                }
                Results.Clear();
                _capMs = capMs;
                _parallel = parallel;
                _guest = guest;
                _guestHomeRealm = homeRealm;
                _guestHomePrivLevel = homePrivLevel;
                _guestAccount = guest?.Client?.Account?.Name;
                _guestCharacterId = guest?.DBCharacter?.ObjectId;
                _guestName = guest?.Name;
                _running = true;
                string capWord = capMs > 0 ? (capMs / 60_000).ToString() : "off";
                Log.Info($"EPIC_SWEEP_START raids={ids.Length} lanes={lanes.Length} cap={capWord} " +
                         $"order={string.Join("|", lanes.Select(l => string.Join(",", l.Ids)))} guest=\"{guest?.Name}\" guestRealm={_guestHomeRealm} " +
                         $"guestLevel={guest?.Level} guestPlaysAsPlayer={_guestHomePrivLevel > 0} guestHomePrivLevel={_guestHomePrivLevel}");
                string capText = capMs > 0 ? $"each stopped after {capWord} minutes at most" : "no cap (each raid's own clocks end it)";
                return lanes.Length == 1
                    ? $"Sweep started: {ids.Length} raids, one after another ({string.Join(", ", ids)}); {capText}" +
                      (guest != null ? $"; {guest.Name} takes a seat in each." : "; bots only.")
                    : $"Sweep started: {ids.Length} raids in {lanes.Length} lanes side by side, one raid at a time in each " +
                      $"({string.Join("; ", lanes.Select(l => $"{l.Name}: {string.Join(", ", l.Ids)}"))}); {capText}; bots only.";
            }
        }

        public static string Stop(string why)
        {
            string[] current;
            lock (Sync)
            {
                if (!_running) return "No sweep is running.";
                current = Lanes.Where(l => l.Current != null).Select(l => l.Current.Id).ToArray();
                _running = false;
            }
            foreach (string id in current)
                if (AutonomousRealmRaid.IsRunning(id))
                    AutonomousRealmRaid.Stop(id, out _);
            Finish(why);
            return $"Sweep stopped ({why}).";
        }

        /// <summary>
        /// Adds raids to the running sweep, after those already waiting in their lane (2026-11-15: the night's list is over 800
        /// characters, too long for one line in the game client's chat, so it is typed as a start and a few adds).
        /// </summary>
        public static string Add(string word)
        {
            lock (Sync)
            {
                if (!_running) return "No sweep is running: /epictest sweep start first.";
                if (string.IsNullOrWhiteSpace(word) || word.Equals("all", StringComparison.OrdinalIgnoreCase))
                    return "Name the raids to add (event,event or openworld).";
                string[] ids = Parse(word, AutonomousRealmRaid.Definitions.Select(d => d.Id).ToArray(), out string[] unknown);
                if (unknown.Length > 0) return $"No raid {string.Join(", ", unknown)}; /epictest list shows them. Nothing was added.";
                string[] held = Lanes.SelectMany(l => l.Queue.Select(q => q.Id).Concat(l.Current != null ? new[] { l.Current.Id } : Array.Empty<string>())).ToArray();
                var plan = PlanAdd(ids, held, Lanes[0].Name, id => AutonomousRealmRaid.Definitions.First(d => d.Id == id).Realm, _parallel,
                    out string[] skipped);
                long now = GameLoop.GameLoopTime;
                foreach ((string laneName, string id) in plan)
                {
                    Lane lane = Lanes.FirstOrDefault(l => l.Name == laneName);
                    if (lane == null)
                    {
                        lane = new Lane { Name = laneName, NextStartAt = now };
                        Lanes.Add(lane);
                    }
                    lane.Queue.Add(new Step { Id = id });
                }
                Log.Info($"EPIC_SWEEP_ADD added={plan.Length} skipped={skipped.Length} order={string.Join(",", plan.Select(p => p.Id))} " +
                         $"lanes={string.Join("|", Lanes.Select(l => $"{l.Name}:{l.Queue.Count}"))}");
                return $"Added {plan.Length} raids" + (_parallel ? " to their realms' lanes" : "") +
                       (skipped.Length > 0 ? $"; already in the sweep: {string.Join(", ", skipped)}" : "") +
                       $". Waiting now: {Lanes.Sum(l => l.Queue.Count)}.";
            }
        }

        /// <summary>Ends the raid running now and goes on with the next.</summary>
        public static string Skip() => Skip(null);

        /// <summary>
        /// Ends a sweep raid running now and its lane goes on with the next: <paramref name="eventId"/>, or with none the only
        /// one running (a parallel sweep with several running asks which).
        /// </summary>
        public static string Skip(string eventId)
        {
            string[] running;
            lock (Sync) running = _running ? Lanes.Where(l => l.Current != null).Select(l => l.Current.Id).ToArray() : [];
            string current = SkipTarget(running, eventId, out string refusal);
            if (current == null) return refusal;
            AutonomousRealmRaid.Stop(current, out string reason);
            return $"{current}: {reason} The sweep goes on with the next.";
        }

        /// <summary>Which running sweep raid a skip ends (pure), or null and why not.</summary>
        public static string SkipTarget(string[] running, string eventId, out string refusal)
        {
            refusal = null;
            if (running.Length == 0) { refusal = "No sweep raid is running."; return null; }
            if (!string.IsNullOrEmpty(eventId))
            {
                if (running.Contains(eventId)) return eventId;
                refusal = $"{eventId} is not a sweep raid running now ({string.Join(", ", running)}).";
                return null;
            }
            if (running.Length == 1) return running[0];
            refusal = $"Several sweep raids run now ({string.Join(", ", running)}): /epictest sweep skip <event>.";
            return null;
        }

        public static string[] Status()
        {
            lock (Sync)
            {
                if (!_running) return ["No sweep is running.", .. Results];
                long now = GameLoop.GameLoopTime;
                string Now(Lane l) => l.Current != null ? $"{l.Current.Id} ({(now - l.CurrentStarted) / 60_000} min)" : "between raids";
                string head = $"Sweep: {Results.Count} done, {Lanes.Sum(l => l.Queue.Count)} waiting; now " +
                              (Lanes.Count == 1 ? Now(Lanes[0]) : string.Join("; ", Lanes.Select(l => $"{l.Name} {Now(l)}"))) +
                              $"; cap {(_capMs > 0 ? $"{_capMs / 60_000} min" : "off")}" +
                              (_guest != null ? $"; guest {_guest.Name}" : "");
                return [head, .. Results];
            }
        }

        /// <summary>From AutonomousLivingServer's pulse.</summary>
        public static void Pulse(long now)
        {
            if (now < _nextPulse) return;
            _nextPulse = now + PulseMs;
            Lane[] lanes;
            lock (Sync)
            {
                if (!_running) return;
                lanes = Lanes.ToArray();
            }
            foreach (Lane lane in lanes)
                PulseLane(lane, now);
            bool done;
            lock (Sync)
            {
                done = _running && Lanes.All(l => l.Current == null && l.Queue.Count == 0);
                if (done) _running = false;
            }
            if (done) Finish("every raid ran");
        }

        private static void PulseLane(Lane lane, long now)
        {
            Step current;
            lock (Sync) current = lane.Current;
            if (current != null)
            {
                bool running = AutonomousRealmRaid.IsRunning(current.Id);
                if (running && CapReached(_capMs, now - lane.CurrentStarted))
                {
                    AutonomousRealmRaid.Stop(current.Id, out _);
                    Log.Info($"EPIC_SWEEP_CAP event={current.Id} minutes={(now - lane.CurrentStarted) / 60_000} cap={_capMs / 60_000} lane={lane.Name}");
                    running = false;
                }
                if (running)
                {
                    if (_guest != null)
                    {
                        RetrySeat(now);
                        TagAlong(now);
                    }
                    return;
                }
                // The raid ended (REALM_RAID_ENDED has its outcome).
                string line = $"{current.Id}: ended after {(now - lane.CurrentStarted) / 60_000} min";
                int done, left;
                lock (Sync)
                {
                    Results.Add(line);
                    lane.Current = null;
                    lane.NextStartAt = now + PauseBetweenMs;
                    done = Results.Count;
                    left = Lanes.Sum(l => l.Queue.Count);
                }
                Log.Info($"EPIC_SWEEP_RAID_DONE event={current.Id} minutes={(now - lane.CurrentStarted) / 60_000} done={done} left={left} lane={lane.Name}");
                if (_guest != null) LeaveParty();
                return;
            }
            if (now < lane.NextStartAt) return;
            string[] queued;
            lock (Sync)
            {
                if (lane.Queue.Count == 0) return;
                queued = lane.Queue.Select(q => q.Id).ToArray();
            }
            // Summoner's Hall and Darkness Falls are one dungeon for all three realms: a lane does not send its raid into one
            // another realm's raid holds now (theirs would fight each other, and a shared final boss credits one realm only).
            var raiding = AutonomousRealmRaid.Definitions.Where(d => AutonomousRealmRaid.IsRunning(d.Id))
                .Select(d => (d.Id, d.Realm, d.Region)).ToArray();
            int pick = NextStartable(queued, RealmAndRegionOf, raiding);
            string held = pick == 0 ? null : HeldLine(queued, pick, raiding);
            if (held != lane.Held)
            {
                lane.Held = held;
                if (held != null) Log.Info($"EPIC_SWEEP_HELD lane={lane.Name} {held}");
            }
            if (pick < 0) return;
            Step next;
            lock (Sync)
            {
                next = lane.Queue.FirstOrDefault(q => q.Id == queued[pick]);
                if (next == null) return;
                lane.Queue.Remove(next);
            }
            StartNext(lane, next, now);
        }

        private static (eRealm Realm, ushort Region) RealmAndRegionOf(string id)
        {
            var d = AutonomousRealmRaid.Definitions.First(x => x.Id == id);
            return (d.Realm, d.Region);
        }

        /// <summary>
        /// The first queued raid a lane may start now (pure), or -1: one whose region no raid of another realm holds
        /// (<paramref name="running"/>: every raid running now, the sweep's and the realms' own).
        /// </summary>
        public static int NextStartable(string[] queued, Func<string, (eRealm Realm, ushort Region)> of,
            (string Id, eRealm Realm, ushort Region)[] running)
        {
            for (int i = 0; i < queued.Length; i++)
            {
                var (realm, region) = of(queued[i]);
                if (!running.Any(r => r.Region == region && r.Realm != realm)) return i;
            }
            return -1;
        }

        /// <summary>The EPIC_SWEEP_HELD details (pure): which raid waits, for whom, and what goes first meanwhile.</summary>
        public static string HeldLine(string[] queued, int pick, (string Id, eRealm Realm, ushort Region)[] running)
        {
            string first = queued[0];
            var (realm, region) = RealmAndRegionOf(first);
            string by = running.Where(r => r.Region == region && r.Realm != realm).Select(r => r.Id).FirstOrDefault() ?? "-";
            return $"event={first} region={region} by={by} next={(pick < 0 ? "wait" : queued[pick])}";
        }

        private static void StartNext(Lane lane, Step step, long now)
        {
            var definition = AutonomousRealmRaid.Definitions.First(d => d.Id == step.Id);
            step.Tries++;
            if (AutonomousRealmRaid.IsRunning(step.Id))
            {
                Requeue(lane, step, "already running (a raid of the realm's own)", now);
                return;
            }
            AutonomousRealmRaid.ResetCooldown(step.Id);
            int size = EpicPlaytestCommandHandler.SizeFor("era", definition.SuggestedBots);
            if (!AutonomousRealmRaid.Start(definition.Id, definition.Realm, out string reason, true, size, null, false))
            {
                Requeue(lane, step, reason, now);
                return;
            }
            lock (Sync)
            {
                lane.Current = step;
                lane.CurrentStarted = now;
                _guestAwaySince = _guestDeadSince = 0;
                _seatPendingFor = null;
                _nextSeatTry = now + SeatRetryMs;
            }
            int moved = 0;
            if (AutonomousRealmRaid.TryGetHub(definition.Id, out var hub))
            {
                GameBot[] recruits = AutonomousRealmRaid.ReservedRecruits(definition.Id);
                moved = recruits.Length;
                EpicPlaytestCommandHandler.MoveTo(definition.Id, hub.Name, hub.Region, hub.Center, recruits);
            }
            Log.Info($"EPIC_SWEEP_RAID_START event={definition.Id} name=\"{definition.Name}\" realm={definition.Realm} size={size} try={step.Tries} " +
                     $"toHub={moved} reason=\"{reason}\" lane={lane.Name}");
            SeatGuest(definition, hub);
        }

        private static void Requeue(Lane lane, Step step, string reason, long now)
        {
            step.LastFailure = reason;
            bool again = step.Tries < Attempts;
            lock (Sync)
            {
                if (again) lane.Queue.Add(step);
                else Results.Add($"{step.Id}: not run ({reason})");
                // Nothing started: try the next one soon, not after the full pause.
                lane.NextStartAt = now + 30_000;
            }
            Log.Info($"EPIC_SWEEP_RAID_NOT_STARTED event={step.Id} try={step.Tries} again={again} reason=\"{reason}\" lane={lane.Name}");
        }

        private static void SeatGuest(AutonomousRealmRaid.Definition definition, RealmRaidMuster.Hub hub)
        {
            GamePlayer guest;
            lock (Sync) guest = _guest;
            if (guest?.Client == null || guest.ObjectState != GameObject.eObjectState.Active) return;
            ClientService.Instance.Post(static state =>
            {
                (GamePlayer guest, AutonomousRealmRaid.Definition definition, RealmRaidMuster.Hub hub) = state;
                guest.Group?.RemoveMember(guest);
                if (!guest.IsAlive) Raise(guest);
                // The raid's realm only while the guest has a seat in it: a guest left with another realm where it stands
                // is killed by that land's guards (2026-11-13: Manawatch, made Midgard at Yarley's farm, died to a Royal
                // Minstrel 8 seconds after the seat failed).
                eRealm before = guest.Realm;
                guest.Realm = definition.Realm;
                bool seated = AutonomousBotGroupCoordinator.TrySeatRaidGuest(definition.Id, guest, out string why);
                if (!seated) guest.Realm = before;
                else
                {
                    if (before != definition.Realm)
                        guest.Out.SendMessage($"Play test: your realm is {GlobalConstants.RealmToName(definition.Realm)} for this raid.",
                            eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    if (hub != null)
                        guest.MoveTo(hub.Region, (int)hub.Center.X, (int)hub.Center.Y, (int)hub.Center.Z, guest.Heading);
                }
                lock (Sync)
                {
                    bool retry = !seated && SeatWorthRetrying(why);
                    _seatPendingFor = retry ? definition : null;
                    _seatPendingHub = retry ? hub : null;
                }
                Log.Info($"EPIC_SWEEP_GUEST event={definition.Id} step=seat player=\"{guest.Name}\" seated={seated} why={why ?? "-"} " +
                         $"realm={guest.Realm} level={guest.Level} hub=\"{hub?.Name}\"");
            }, (guest, definition, hub));
        }

        /// <summary>Whether a failed seat may come free later in the same raid (pure): parties still forming or refilling.</summary>
        public static bool SeatWorthRetrying(string why) => why is "no_party" or "no_seat";

        private static void RetrySeat(long now)
        {
            AutonomousRealmRaid.Definition definition;
            RealmRaidMuster.Hub hub;
            lock (Sync)
            {
                // A guest who joined a party meanwhile (the raid leader's own invite, say) keeps it.
                if (_guest?.Group != null) _seatPendingFor = null;
                definition = _seatPendingFor;
                hub = _seatPendingHub;
                if (definition == null || GuestRaid?.Id != definition.Id || now < _nextSeatTry) return;
                _nextSeatTry = now + SeatRetryMs;
            }
            SeatGuest(definition, hub);
        }

        private static void TagAlong(long now)
        {
            GamePlayer guest;
            lock (Sync) guest = _guest;
            if (guest?.Client == null || guest.ObjectState != GameObject.eObjectState.Active || guest.Group == null) return;
            GameLiving anchor = AnchorFor(guest, GuestRaid?.Id);
            if (anchor == null) return;
            bool sameRegion = anchor.CurrentRegionID == guest.CurrentRegionID;
            float distance = sameRegion ? guest.GetDistanceTo(anchor) : float.MaxValue;
            GameNPC boss = AutonomousRealmRaid.BossOf(GuestRaid?.Id);
            bool nearFight = guest.IsAlive && boss != null && boss.IsAlive && boss.ObjectState == GameObject.eObjectState.Active &&
                boss.CurrentRegionID == guest.CurrentRegionID && guest.IsWithinRadius(boss, (int)GuestAwayRange);
            bool away = IsAway(sameRegion, distance, nearFight);
            if (!guest.IsAlive) { if (_guestDeadSince == 0) _guestDeadSince = now; }
            else _guestDeadSince = 0;
            if (guest.IsAlive && away) { if (_guestAwaySince == 0) _guestAwaySince = now; }
            else _guestAwaySince = 0;
            string action = GuestAction(guest.IsAlive, true, sameRegion, distance,
                _guestAwaySince == 0 ? 0 : now - _guestAwaySince, _guestDeadSince == 0 ? 0 : now - _guestDeadSince, nearFight);
            if (action == null) return;
            string eventId = GuestRaid?.Id;
            _guestAwaySince = _guestDeadSince = 0;
            ClientService.Instance.Post(static state =>
            {
                (GamePlayer guest, GameLiving anchor, string action, string eventId, float distance) = state;
                // The anchor may have died or left since the pulse picked it: nobody to land beside, so wait for the next.
                if (!anchor.IsAlive || anchor.ObjectState != GameObject.eObjectState.Active) return;
                if (action == "raise") Raise(guest);
                Vector3 landing = LandingBeside(anchor, out bool snapped);
                guest.MoveTo(anchor.CurrentRegionID, (int)landing.X, (int)landing.Y, (int)landing.Z, anchor.Heading);
                Log.Info($"EPIC_SWEEP_GUEST event={eventId} step={action} player=\"{guest.Name}\" to=\"{anchor.Name}\" " +
                         $"region={anchor.CurrentRegionID} distance={(distance == float.MaxValue ? "other_region" : ((int)distance).ToString())} " +
                         $"at={(int)landing.X},{(int)landing.Y},{(int)landing.Z} landing={(snapped ? "mesh" : "anchor")}");
            }, (guest, anchor, action, eventId, distance));
        }

        /// <summary>
        /// The raider the guest follows: while its raid runs, a living raider with the raid's front (<see cref="GuestAnchor"/>),
        /// never a dead one or one released to the hub; with no raid to ask, the cluster of its own party's living members.
        /// </summary>
        private static GameLiving AnchorFor(GamePlayer guest, string raidId)
        {
            Group group = guest.Group;
            if (group == null) return null;
            if (AutonomousRealmRaid.TryGuestFront(raidId, out bool hasFront, out ushort region, out Vector3 front, out var raiders) && hasFront)
            {
                var own = new HashSet<GameLiving>(group.GetMembersInTheGroup());
                int i = GuestAnchor(raiders.Select(r => (r.Bot.CurrentRegionID, r.Bot.X, r.Bot.Y, r.Bot.IsAlive, r.RaidLeader, own.Contains(r.Bot))).ToArray(),
                    region, (int)front.X, (int)front.Y);
                return i < 0 ? null : raiders[i].Bot;
            }
            GameBot[] alive = group.GetMembersInTheGroup().OfType<GameBot>().Where(b => b.IsAlive && b.ObjectState == GameObject.eObjectState.Active).ToArray();
            // The party's cluster, not its leader alone: a leader who died and released stands at its bind point while the
            // rest fight on (2026-11-13, Golestandt: the guest was moved 55,000 units away to a released leader).
            return alive.Length == 0 ? null : alive[Anchor(alive.Select(b => (b.CurrentRegionID, b.X, b.Y, b == group.LivingLeader)).ToArray())];
        }

        /// <summary>The guest's landing beside its anchor, on the navmesh's floor (<see cref="Landing"/>).</summary>
        private static Vector3 LandingBeside(GameLiving anchor, out bool snapped)
        {
            var exact = new Vector3(anchor.X, anchor.Y, anchor.Z);
            var beside = new Vector3(anchor.X + 40, anchor.Y + 40, anchor.Z);
            Zone zone = anchor.CurrentRegion?.GetZone(anchor.X, anchor.Y);
            IPathfindingMgr nav = PathfindingProvider.Instance;
            snapped = zone != null && nav.IsAvailable && nav.HasNavmesh(zone) && nav.TrySnapToMesh(zone, ref beside, 128f);
            bool connected = snapped && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, beside, exact);
            Vector3 landing = Landing(exact, snapped, beside, connected);
            snapped = landing != exact;
            return landing;
        }

        /// <summary>As /player rez does it.</summary>
        private static void Raise(GamePlayer player)
        {
            if (player.IsAlive) return;
            player.Health = player.MaxHealth;
            player.Mana = player.MaxMana;
            player.Endurance = player.MaxEndurance;
            player.StopReleaseTimer();
            player.Out.SendPlayerRevive(player);
            player.Out.SendStatusUpdate();
            player.Notify(GamePlayerEvent.Revive, player);
        }

        private static void LeaveParty()
        {
            GamePlayer guest;
            lock (Sync) guest = _guest;
            if (guest?.Group == null) return;
            ClientService.Instance.Post(static g => g.Group?.RemoveMember(g), guest);
        }

        private static void Finish(string why, bool now = false)
        {
            GamePlayer guest;
            eRealm home;
            uint homePrivLevel;
            string accountName, characterId, characterName;
            string[] results;
            lock (Sync)
            {
                _running = false;
                guest = _guest;
                home = _guestHomeRealm;
                homePrivLevel = _guestHomePrivLevel;
                accountName = _guestAccount;
                characterId = _guestCharacterId;
                characterName = _guestName;
                _guest = null;
                _guestHomePrivLevel = 0;
                _guestAccount = _guestCharacterId = _guestName = null;
                _seatPendingFor = null;
                results = Results.ToArray();
            }
            IObjectDatabase database = GameServer.Database;
            GamePlayer live = LiveGuest(guest, characterId, characterName);
            if (accountName != null && live == null)
            {
                // The guest logged off: its saved account and character are all there is, so they are put back there.
                EpicPlaytestSweepGuestRecord.RestoreStored(database, false, "sweep_end_offline", accountName);
            }
            else if (live != null)
            {
                DbAccount account = live.Client?.Account;
                // The account level comes back now, so the GM commands do too (saved: a stop right after can't lose it).
                RaiseAccount(database, account, homePrivLevel);
                if (now)
                {
                    // The server is stopping: the game loop runs nothing posted any more.
                    if (home != eRealm.None && live.Realm != home) live.Realm = home;
                    EndGuest(database, account, homePrivLevel, () => SaveCharacter(live));
                }
                else
                {
                    ClientService.Instance.Post(static state =>
                    {
                        (GamePlayer guest, eRealm home, uint homePrivLevel, DbAccount account) = state;
                        guest.Group?.RemoveMember(guest);
                        if (!guest.IsAlive) Raise(guest);
                        if (home != eRealm.None && guest.Realm != home)
                        {
                            guest.Realm = home;
                            guest.Out.SendMessage($"Play test over: your realm is {GlobalConstants.RealmToName(home)} again.",
                                eChatType.CT_System, eChatLoc.CL_SystemWindow);
                        }
                        EndGuest(GameServer.Database, account, homePrivLevel, () => SaveCharacter(guest));
                    }, (live, home, homePrivLevel, account));
                }
            }
            Log.Info($"EPIC_SWEEP_END why=\"{why}\" results={results.Length} guest=\"{characterName}\" guestRealm={home} guestPrivLevel={homePrivLevel} " +
                     $"guestOnline={live != null} | {string.Join(" | ", results)}");
        }

        /// <summary>The guest's character as it is in the game now: the sweep's own object, or the same character logged in again.</summary>
        private static GamePlayer LiveGuest(GamePlayer guest, string characterId, string characterName)
        {
            if (guest?.Client != null && guest.ObjectState == GameObject.eObjectState.Active) return guest;
            if (string.IsNullOrEmpty(characterName)) return null;
            GamePlayer again = ClientService.Instance.GetPlayerByExactName(characterName);
            return again?.Client != null && again.ObjectState == GameObject.eObjectState.Active &&
                   (characterId == null || again.DBCharacter?.ObjectId == characterId) ? again : null;
        }

        private static bool SaveCharacter(GamePlayer player)
        {
            try
            {
                player.SaveIntoDatabase();
                return true;
            }
            catch (Exception exception)
            {
                Log.Warn($"EPIC_SWEEP_GUEST could not save {player?.Name}; the record stays for the next start or login", exception);
                return false;
            }
        }

        private static void RaiseAccount(IObjectDatabase database, DbAccount account, uint homePrivLevel)
        {
            if (account == null) return;
            uint raised = EpicPlaytestSweepGuestRecord.RaisedPrivLevel(account.PrivLevel, homePrivLevel);
            if (raised == account.PrivLevel) return;
            account.PrivLevel = raised;
            try
            {
                database?.SaveObject(account);
            }
            catch (Exception exception)
            {
                Log.Warn($"EPIC_SWEEP_GUEST could not save account {account.Name}; the record stays for the next start or login", exception);
            }
        }

        /// <summary>
        /// The guest's start (the account object, not the player, so tests can run it): a record an earlier sweep left is
        /// put back first, so this record holds the true home values (<paramref name="homeRealm"/> comes back as the
        /// character's home realm); then the record is saved, and only then is a GM account lowered to Player.
        /// <paramref name="homePrivLevel"/>: the level it was lowered from, 0 when it was not. False when the record could
        /// not be saved: nothing was changed, and the sweep must not lower the account.
        /// </summary>
        public static bool BeginGuest(IObjectDatabase database, DbAccount account, string characterId, string characterName,
            ref eRealm homeRealm, out uint homePrivLevel)
        {
            homePrivLevel = 0;
            if (account == null) return true;
            EpicPlaytestSweepGuestRecord.Entry stale = EpicPlaytestSweepGuestRecord.Read(database, account.Name);
            if (stale != null)
            {
                if (stale.CharacterId != null && stale.CharacterId == characterId && stale.HomeRealm != eRealm.None)
                    homeRealm = stale.HomeRealm;
                EpicPlaytestSweepGuestRecord.RestoreStored(database, false, "sweep_start", live: account);
            }
            uint home = account.PrivLevel > (uint)ePrivLevel.Player ? account.PrivLevel : 0;
            if (!EpicPlaytestSweepGuestRecord.Write(database, new EpicPlaytestSweepGuestRecord.Entry(account.Name, home, characterId, characterName, homeRealm)))
                return false;
            if (home > 0) account.PrivLevel = (uint)ePrivLevel.Player;
            homePrivLevel = home;
            return true;
        }

        /// <summary>
        /// The guest's normal end: the account level back (raised only) and saved; the record is cleared once the
        /// character is saved with its home realm (<paramref name="saveCharacter"/>), else it stays for the next start or
        /// login. Returns whether the record was cleared.
        /// </summary>
        public static bool EndGuest(IObjectDatabase database, DbAccount account, uint homePrivLevel, Func<bool> saveCharacter)
        {
            if (account == null) return false;
            RaiseAccount(database, account, homePrivLevel);
            if (saveCharacter != null && !saveCharacter()) return false;
            return EpicPlaytestSweepGuestRecord.Clear(database, account.Name, account);
        }

        /// <summary>
        /// Server start, before the port opens: a sweep the last run never ended (the server stopped or was killed while
        /// it ran) left its record, and nothing runs now, so its guest's account level and realm are put back.
        /// </summary>
        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            int restored = EpicPlaytestSweepGuestRecord.RestoreStored(GameServer.Database, Running, "server_start");
            if (restored > 0)
                Log.Info($"EPIC_SWEEP_GUEST_RESTORED at start: {restored} account(s) a sweep left changed were put back");
        }

        /// <summary>An account logging in (LoginRequestHandler) with a record while no sweep runs is put back first.</summary>
        public static void RestoreAtLogin(DbAccount account)
        {
            if (account == null) return;
            try
            {
                EpicPlaytestSweepGuestRecord.RestoreStored(GameServer.Database, Running, "login", live: account);
            }
            catch (Exception exception)
            {
                Log.Warn($"EPIC_SWEEP_GUEST_RECORD could not restore {account.Name} at login", exception);
            }
        }

        /// <summary>
        /// A graceful stop ends a running sweep, so its own restore runs (the game loop has stopped: done here, not
        /// posted). The raid itself is not stopped: the server is going down.
        /// </summary>
        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            lock (Sync)
            {
                if (!_running) return;
                _running = false;
            }
            try
            {
                Finish("server stopping", now: true);
            }
            catch (Exception exception)
            {
                Log.Warn("EPIC_SWEEP_END could not finish the sweep at the stop; the record stays for the next start", exception);
            }
        }
    }
}
