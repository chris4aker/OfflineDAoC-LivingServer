using System;
using System.Collections.Generic;
using System.Linq;
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
    ///
    /// The guest: the GM who started the sweep, if asked, takes a seat in every raid as a player would (the raid leader's
    /// invite, accepted). Their realm is set to the raid's for it and put back when the sweep ends (as /player realm does),
    /// so one character can sit in every realm's raids. Nobody moves the guest's keys, so between raids and on the march the
    /// server keeps them with their party ("tag-along"): away from it for 20 seconds (thrown, jailed, left behind), they are
    /// moved back to its leader; dead for 30 seconds, they are raised there. Every such step is logged (EPIC_SWEEP_GUEST), so
    /// a result with a guest says what the server did for them. Test only: nothing here runs unless a GM starts it.
    /// </summary>
    public static class EpicPlaytestSweep
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(EpicPlaytestSweep));
        private static readonly object Sync = new();

        public const long PulseMs = 10_000;
        public const long PauseBetweenMs = 3 * 60_000;
        /// <summary>A raid the sweep started is stopped after this long, whatever its own clocks say.</summary>
        public const long RaidCapMs = 4 * 60 * 60_000L;
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

        private static readonly List<Step> Queue = [];
        private static readonly List<string> Results = [];
        private static Step _current;
        private static long _currentStarted, _nextStartAt, _nextPulse, _guestAwaySince, _guestDeadSince;
        private static GamePlayer _guest;
        private static eRealm _guestHomeRealm;
        private static bool _running;

        /// <summary>The order (pure): the definitions' own order, Darkness Falls last (it has no raid AI; owner 2026-11-13).</summary>
        public static string[] Order(IEnumerable<string> ids) =>
            ids.OrderBy(id => id.StartsWith("darkness-", StringComparison.Ordinal) ? 1 : 0).ToArray();

        /// <summary>The ids a start names (pure): "all", or a comma list; unknown ids come back in <paramref name="unknown"/>.</summary>
        public static string[] Parse(string word, IReadOnlyCollection<string> known, out string[] unknown)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                unknown = [];
                return Order(known);
            }
            string[] asked = word.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            unknown = asked.Where(a => !known.Contains(a)).ToArray();
            return Order(asked.Where(known.Contains).Distinct());
        }

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

        /// <summary>What tag-along does with the guest now (pure): nothing, "move" to the party leader, or "raise" there.</summary>
        public static string GuestAction(bool alive, bool inGroup, bool sameRegion, float distance, long awayFor, long deadFor)
        {
            if (!inGroup) return null;
            if (!alive) return deadFor >= GuestDeadMs ? "raise" : null;
            if ((!sameRegion || distance > GuestAwayRange) && awayFor >= GuestAwayMs) return "move";
            return null;
        }

        public static bool Running
        {
            get { lock (Sync) return _running; }
        }

        public static string Start(string word, GamePlayer guest)
        {
            lock (Sync)
            {
                if (_running) return "A sweep is already running: /epictest sweep status, or /epictest sweep stop.";
                string[] ids = Parse(word, AutonomousRealmRaid.Definitions.Select(d => d.Id).ToArray(), out string[] unknown);
                if (unknown.Length > 0) return $"No raid {string.Join(", ", unknown)}; /epictest list shows them.";
                if (ids.Length == 0) return "No raids to sweep.";
                Queue.Clear();
                Queue.AddRange(ids.Select(id => new Step { Id = id }));
                Results.Clear();
                _current = null;
                _nextStartAt = GameLoop.GameLoopTime;
                _guest = guest;
                _guestHomeRealm = guest?.Realm ?? eRealm.None;
                _running = true;
                Log.Info($"EPIC_SWEEP_START raids={ids.Length} order={string.Join(",", ids)} guest=\"{guest?.Name}\" guestRealm={_guestHomeRealm} " +
                         $"guestLevel={guest?.Level}");
                return $"Sweep started: {ids.Length} raids, one after another ({string.Join(", ", ids)})" +
                       (guest != null ? $"; {guest.Name} takes a seat in each." : "; bots only.");
            }
        }

        public static string Stop(string why)
        {
            string current;
            lock (Sync)
            {
                if (!_running) return "No sweep is running.";
                current = _current?.Id;
                _running = false;
            }
            if (current != null && AutonomousRealmRaid.IsRunning(current))
                AutonomousRealmRaid.Stop(current, out _);
            Finish(why);
            return $"Sweep stopped ({why}).";
        }

        /// <summary>Ends the raid running now and goes on with the next.</summary>
        public static string Skip()
        {
            string current;
            lock (Sync) current = _running ? _current?.Id : null;
            if (current == null) return "No sweep raid is running.";
            AutonomousRealmRaid.Stop(current, out string reason);
            return $"{current}: {reason} The sweep goes on with the next.";
        }

        public static string[] Status()
        {
            lock (Sync)
            {
                var lines = new List<string>
                {
                    _running
                        ? $"Sweep: {Results.Count} done, {Queue.Count} waiting; now {_current?.Id ?? "between raids"}" +
                          (_current != null ? $" ({(GameLoop.GameLoopTime - _currentStarted) / 60_000} min)" : "") +
                          (_guest != null ? $"; guest {_guest.Name}" : "")
                        : "No sweep is running."
                };
                lines.AddRange(Results);
                return lines.ToArray();
            }
        }

        /// <summary>From AutonomousLivingServer's pulse.</summary>
        public static void Pulse(long now)
        {
            if (now < _nextPulse) return;
            _nextPulse = now + PulseMs;
            Step current;
            lock (Sync)
            {
                if (!_running) return;
                current = _current;
            }
            if (current != null)
            {
                bool running = AutonomousRealmRaid.IsRunning(current.Id);
                if (running && now - _currentStarted >= RaidCapMs)
                {
                    AutonomousRealmRaid.Stop(current.Id, out _);
                    Log.Info($"EPIC_SWEEP_CAP event={current.Id} minutes={(now - _currentStarted) / 60_000}");
                    running = false;
                }
                if (running)
                {
                    TagAlong(now);
                    return;
                }
                // The raid ended (REALM_RAID_ENDED has its outcome).
                string line = $"{current.Id}: ended after {(now - _currentStarted) / 60_000} min";
                lock (Sync)
                {
                    Results.Add(line);
                    _current = null;
                    _nextStartAt = now + PauseBetweenMs;
                }
                Log.Info($"EPIC_SWEEP_RAID_DONE event={current.Id} minutes={(now - _currentStarted) / 60_000} done={Results.Count} left={Queue.Count}");
                LeaveParty();
                return;
            }
            if (now < _nextStartAt) return;
            Step next;
            lock (Sync)
            {
                if (Queue.Count == 0)
                {
                    _running = false;
                    next = null;
                }
                else
                {
                    next = Queue[0];
                    Queue.RemoveAt(0);
                }
            }
            if (next == null)
            {
                Finish("every raid ran");
                return;
            }
            StartNext(next, now);
        }

        private static void StartNext(Step step, long now)
        {
            var definition = AutonomousRealmRaid.Definitions.First(d => d.Id == step.Id);
            step.Tries++;
            if (AutonomousRealmRaid.IsRunning(step.Id))
            {
                Requeue(step, "already running (a raid of the realm's own)", now);
                return;
            }
            AutonomousRealmRaid.ResetCooldown(step.Id);
            int size = EpicPlaytestCommandHandler.SizeFor("era", definition.SuggestedBots);
            if (!AutonomousRealmRaid.Start(definition.Id, definition.Realm, out string reason, true, size, null, false))
            {
                Requeue(step, reason, now);
                return;
            }
            lock (Sync)
            {
                _current = step;
                _currentStarted = now;
                _guestAwaySince = _guestDeadSince = 0;
            }
            int moved = 0;
            if (AutonomousRealmRaid.TryGetHub(definition.Id, out var hub))
            {
                GameBot[] recruits = AutonomousRealmRaid.ReservedRecruits(definition.Id);
                moved = recruits.Length;
                EpicPlaytestCommandHandler.MoveTo(definition.Id, hub.Name, hub.Region, hub.Center, recruits);
            }
            Log.Info($"EPIC_SWEEP_RAID_START event={definition.Id} name=\"{definition.Name}\" realm={definition.Realm} size={size} try={step.Tries} " +
                     $"toHub={moved} reason=\"{reason}\"");
            SeatGuest(definition, hub);
        }

        private static void Requeue(Step step, string reason, long now)
        {
            step.LastFailure = reason;
            bool again = step.Tries < Attempts;
            lock (Sync)
            {
                if (again) Queue.Add(step);
                else Results.Add($"{step.Id}: not run ({reason})");
                // Nothing started: try the next one soon, not after the full pause.
                _nextStartAt = now + 30_000;
            }
            Log.Info($"EPIC_SWEEP_RAID_NOT_STARTED event={step.Id} try={step.Tries} again={again} reason=\"{reason}\"");
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
                if (guest.Realm != definition.Realm)
                {
                    guest.Realm = definition.Realm;
                    guest.Out.SendMessage($"Play test: your realm is {GlobalConstants.RealmToName(definition.Realm)} for this raid.",
                        eChatType.CT_System, eChatLoc.CL_SystemWindow);
                }
                bool seated = AutonomousBotGroupCoordinator.TrySeatRaidGuest(definition.Id, guest, out string why);
                if (seated && hub != null)
                    guest.MoveTo(hub.Region, (int)hub.Center.X, (int)hub.Center.Y, (int)hub.Center.Z, guest.Heading);
                Log.Info($"EPIC_SWEEP_GUEST event={definition.Id} step=seat player=\"{guest.Name}\" seated={seated} why={why ?? "-"} " +
                         $"realm={guest.Realm} level={guest.Level} hub=\"{hub?.Name}\"");
            }, (guest, definition, hub));
        }

        private static void TagAlong(long now)
        {
            GamePlayer guest;
            lock (Sync) guest = _guest;
            if (guest?.Client == null || guest.ObjectState != GameObject.eObjectState.Active || guest.Group == null) return;
            GameBot[] alive = guest.Group.GetMembersInTheGroup().OfType<GameBot>().Where(b => b.IsAlive && b.ObjectState == GameObject.eObjectState.Active).ToArray();
            // The party's cluster, not its leader alone: a leader who died and released stands at its bind point while the
            // rest fight on (2026-11-13, Golestandt: the guest was moved 55,000 units away to a released leader).
            GameLiving anchor = alive.Length == 0 ? null : alive[Anchor(alive.Select(b => (b.CurrentRegionID, b.X, b.Y, b == guest.Group.LivingLeader)).ToArray())];
            if (anchor == null) return;
            bool sameRegion = anchor.CurrentRegionID == guest.CurrentRegionID;
            float distance = sameRegion ? guest.GetDistanceTo(anchor) : float.MaxValue;
            bool away = !sameRegion || distance > GuestAwayRange;
            if (!guest.IsAlive) { if (_guestDeadSince == 0) _guestDeadSince = now; }
            else _guestDeadSince = 0;
            if (guest.IsAlive && away) { if (_guestAwaySince == 0) _guestAwaySince = now; }
            else _guestAwaySince = 0;
            string action = GuestAction(guest.IsAlive, true, sameRegion, distance,
                _guestAwaySince == 0 ? 0 : now - _guestAwaySince, _guestDeadSince == 0 ? 0 : now - _guestDeadSince);
            if (action == null) return;
            string eventId = _current?.Id;
            _guestAwaySince = _guestDeadSince = 0;
            ClientService.Instance.Post(static state =>
            {
                (GamePlayer guest, GameLiving anchor, string action, string eventId, float distance) = state;
                if (action == "raise") Raise(guest);
                guest.MoveTo(anchor.CurrentRegionID, anchor.X + 40, anchor.Y + 40, anchor.Z, anchor.Heading);
                Log.Info($"EPIC_SWEEP_GUEST event={eventId} step={action} player=\"{guest.Name}\" to=\"{anchor.Name}\" " +
                         $"region={anchor.CurrentRegionID} distance={(distance == float.MaxValue ? "other_region" : ((int)distance).ToString())}");
            }, (guest, anchor, action, eventId, distance));
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

        private static void Finish(string why)
        {
            GamePlayer guest;
            eRealm home;
            string[] results;
            lock (Sync)
            {
                _running = false;
                guest = _guest;
                home = _guestHomeRealm;
                _guest = null;
                results = Results.ToArray();
            }
            if (guest?.Client != null && guest.ObjectState == GameObject.eObjectState.Active)
            {
                ClientService.Instance.Post(static state =>
                {
                    (GamePlayer guest, eRealm home) = state;
                    guest.Group?.RemoveMember(guest);
                    if (!guest.IsAlive) Raise(guest);
                    if (home != eRealm.None && guest.Realm != home)
                    {
                        guest.Realm = home;
                        guest.Out.SendMessage($"Play test over: your realm is {GlobalConstants.RealmToName(home)} again.",
                            eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    }
                }, (guest, home));
            }
            Log.Info($"EPIC_SWEEP_END why=\"{why}\" results={results.Length} guest=\"{guest?.Name}\" guestRealm={home} | {string.Join(" | ", results)}");
        }
    }
}
