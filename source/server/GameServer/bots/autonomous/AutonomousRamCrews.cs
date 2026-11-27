using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// #572 step 2: ram crews on purpose (port of zomboden/OfflineDAoC-True165 <c>433740c6f</c>, AutonomousRamCrews.cs,
    /// GPL-3.0; owner 2026-11-27 on #572: "(a) port the ram seats", without the "second ram only at 16+ bots" rule and
    /// without the woodworking repair). A ram's reload and damage grow with the crew at it (GameSiegeRam.Crew: riders, or
    /// friendly gamebots within <see cref="GameSiegeRam.BotCrewRadius"/>, up to its 12 places; owner ruling 2026-10-07),
    /// but gamebots stood at a ram only by chance: the melee battered the gate by hand beside it or elsewhere. Now every
    /// working ram an autonomous gamebot operates at an enemy keep door is registered by its operator, and the attacking
    /// force's melee gamebots with no siege job take a seat at the registered ram with the most empty places (the nearest
    /// on a tie), walk to their own spot behind it and stay; only a blow at melee reach takes them off. Casters, archers and
    /// healers keep their jobs. A player's ram is never registered: it keeps the crew it had (#525: friendly gamebots that
    /// stand within 300 of it). Companions and the player are never seated.
    /// Cost: a lock and a small dictionary per ram; one seat lookup per crew member every 5 s; no area scans.
    /// </summary>
    public static class AutonomousRamCrews
    {
        /// <summary>A seat not renewed for this long is free again (the crew member left, died or was moved on).</summary>
        public const long SeatLeaseMilliseconds = 20_000;
        /// <summary>The first row of crew spots lies this far behind the ram, the second this much farther.</summary>
        public const int CrewInnerRadius = 120, CrewRowDepth = 80;
        /// <summary>Spots per row, across a 125-degree arc behind the ram.</summary>
        public const int SeatsPerRow = 6;
        /// <summary>A crew member looks for a seat at most this often.</summary>
        public const long LookMilliseconds = 5_000;
        /// <summary>A crew member looks for a seat only this near the keep it attacks.</summary>
        public const int LookRadius = 6_000;
        /// <summary>A crew spot must stay this near the ram once put on the navmesh (the ram counts its crew within 300).</summary>
        public const int SpotReach = 280;

        /// <summary>Pure rule: who crews a ram: a melee fighter. Casters and archers shoot the walls and defenders; the
        /// healers (support classes and shamans) keep the crew alive.</summary>
        public static bool IsCrewCandidate(bool rangedOrCaster, bool mainHealer) => !rangedOrCaster && !mainHealer;

        /// <summary>Pure rule: the ram a new crew member takes: the most empty places, the nearest on a tie; none when every
        /// ram is full. Index into <paramref name="rams"/>, or -1.</summary>
        public static int ChooseRam(IReadOnlyList<(int Free, long Distance)> rams)
        {
            int best = -1;
            for (int i = 0; i < rams.Count; i++)
            {
                if (rams[i].Free <= 0) continue;
                if (best < 0 || rams[i].Free > rams[best].Free ||
                    rams[i].Free == rams[best].Free && rams[i].Distance < rams[best].Distance)
                    best = i;
            }
            return best;
        }

        /// <summary>Pure rule: the lowest seat number not taken.</summary>
        public static int FirstFreeSeat(ICollection<int> taken)
        {
            int seat = 0;
            while (taken.Contains(seat)) seat++;
            return seat;
        }

        /// <summary>Pure rule: the seat's spot, behind the ram on the side away from the door, in rows of six across a
        /// 125-degree arc, every spot within the 300 the ram counts its crew in.</summary>
        public static Vector2 CrewSpot(Vector2 ram, Vector2 door, int seat)
        {
            Vector2 away = ram - door;
            away = away.LengthSquared() < 1 ? new Vector2(1, 0) : Vector2.Normalize(away);
            int row = Math.Max(0, seat) / SeatsPerRow, column = Math.Max(0, seat) % SeatsPerRow;
            float radius = CrewInnerRadius + row % 2 * CrewRowDepth;
            double angle = Math.Atan2(away.Y, away.X) + (column - 2.5) * (25 * Math.PI / 180) + row / 2 * 0.1;
            return ram + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
        }

        private sealed class Crew
        {
            public string KeepTargetId;
            public readonly Dictionary<int, (WeakReference<GameBot> Bot, long Until)> Seats = new();
        }

        private static readonly object Gate = new();
        private static readonly ConditionalWeakTable<GameSiegeRam, Crew> Crews = new();
        private static readonly Dictionary<string, List<WeakReference<GameSiegeRam>>> RamsByKeep = new(StringComparer.Ordinal);
        private const string SeatKey = "RamCrewSeat";

        /// <summary>A ram that can still break a door for this realm: alive, in the world, operated, not decayed.</summary>
        public static bool Working(GameSiegeRam ram, eRealm realm) =>
            ram != null && ram.IsAlive && ram.ObjectState == GameObject.eObjectState.Active && ram.Realm == realm &&
            ram.Owner != null && ram.Health > ram.DecayedHp;

        /// <summary>The operator's turn: its ram works for the force attacking this keep. Only autonomous gamebots call it, so
        /// a player's ram is never a seat.</summary>
        public static void Register(string keepTargetId, GameSiegeRam ram)
        {
            if (string.IsNullOrEmpty(keepTargetId) || ram == null) return;
            lock (Gate)
            {
                Crew crew = Crews.GetValue(ram, _ => new Crew());
                if (crew.KeepTargetId == keepTargetId) return;
                if (crew.KeepTargetId != null && RamsByKeep.TryGetValue(crew.KeepTargetId, out var old))
                    old.RemoveAll(entry => !entry.TryGetTarget(out GameSiegeRam known) || known == ram);
                crew.KeepTargetId = keepTargetId;
                crew.Seats.Clear();
                if (!RamsByKeep.TryGetValue(keepTargetId, out var rams)) RamsByKeep[keepTargetId] = rams = new();
                rams.RemoveAll(entry => !entry.TryGetTarget(out GameSiegeRam known) || known == ram ||
                                        known.ObjectState != GameObject.eObjectState.Active);
                rams.Add(new(ram));
            }
        }

        /// <summary>The working rams registered for this keep (for the log and tests).</summary>
        public static int RegisteredRams(string keepTargetId, eRealm realm)
        {
            lock (Gate)
                return RamsByKeep.TryGetValue(keepTargetId ?? "", out var rams)
                    ? rams.Count(entry => entry.TryGetTarget(out GameSiegeRam ram) && Working(ram, realm)) : 0;
        }

        /// <summary>The seats taken at this ram (live leases).</summary>
        public static int SeatsTaken(GameSiegeRam ram)
        {
            if (ram == null) return 0;
            lock (Gate)
            {
                if (!Crews.TryGetValue(ram, out Crew crew)) return 0;
                Prune(crew, GameLoop.GameLoopTime);
                return crew.Seats.Count;
            }
        }

        /// <summary>A free seat at the force's working ram with the most empty places (the nearest on a tie).</summary>
        public static bool TryTakeSeat(GameBot bot, string keepTargetId, out GameSiegeRam ram, out int seat)
        {
            ram = null; seat = -1;
            if (bot == null || string.IsNullOrEmpty(keepTargetId)) return false;
            long now = GameLoop.GameLoopTime;
            lock (Gate)
            {
                if (!RamsByKeep.TryGetValue(keepTargetId, out var rams)) return false;
                rams.RemoveAll(entry => !entry.TryGetTarget(out GameSiegeRam known) || known.ObjectState != GameObject.eObjectState.Active);
                if (rams.Count == 0) { RamsByKeep.Remove(keepTargetId); return false; }
                var open = new List<GameSiegeRam>();
                var facts = new List<(int Free, long Distance)>();
                foreach (var entry in rams)
                {
                    if (!entry.TryGetTarget(out GameSiegeRam candidate) || !Working(candidate, bot.Realm) ||
                        candidate.CurrentRegion != bot.CurrentRegion || !Crews.TryGetValue(candidate, out Crew crew)) continue;
                    Prune(crew, now);
                    open.Add(candidate);
                    facts.Add((candidate.MAX_PASSENGERS - crew.Seats.Count, (long)bot.GetDistanceTo(candidate)));
                }
                int chosenIndex = ChooseRam(facts);
                if (chosenIndex < 0) return false;
                ram = open[chosenIndex];
                Crew chosen = Crews.GetValue(ram, _ => new Crew());
                seat = FirstFreeSeat(chosen.Seats.Keys);
                chosen.Seats[seat] = (new WeakReference<GameBot>(bot), now + SeatLeaseMilliseconds);
                bot.TempProperties.SetProperty(SeatKey, ram);
                return true;
            }
        }

        /// <summary>The ram this bot holds a live seat at, else null (the lease is renewed).</summary>
        public static GameSiegeRam KeepSeat(GameBot bot, out int seat)
        {
            seat = -1;
            GameSiegeRam ram = bot?.TempProperties.GetProperty<GameSiegeRam>(SeatKey);
            if (ram == null) return null;
            long now = GameLoop.GameLoopTime;
            lock (Gate)
            {
                if (Crews.TryGetValue(ram, out Crew crew))
                {
                    foreach (var (index, entry) in crew.Seats)
                        if (entry.Bot.TryGetTarget(out GameBot seated) && seated == bot && entry.Until >= now)
                        {
                            seat = index;
                            break;
                        }
                    if (seat >= 0)
                    {
                        crew.Seats[seat] = crew.Seats[seat] with { Until = now + SeatLeaseMilliseconds };
                        return ram;
                    }
                }
            }
            bot.TempProperties.RemoveProperty(SeatKey);
            return null;
        }

        /// <summary>The bot gives its seat up.</summary>
        public static void Leave(GameBot bot)
        {
            GameSiegeRam ram = bot?.TempProperties.GetProperty<GameSiegeRam>(SeatKey);
            if (ram == null) return;
            bot.TempProperties.RemoveProperty(SeatKey);
            lock (Gate)
                if (Crews.TryGetValue(ram, out Crew crew))
                    foreach (int index in crew.Seats.Where(pair => !pair.Value.Bot.TryGetTarget(out GameBot seated) || seated == bot)
                                 .Select(pair => pair.Key).ToArray())
                        crew.Seats.Remove(index);
        }

        /// <summary>A crew member standing at its working ram: it holds its post like the operator does.</summary>
        public static bool AtSeat(GameBot bot) =>
            bot?.TempProperties.GetProperty<GameSiegeRam>(SeatKey) is { } ram && Working(ram, bot.Realm) &&
            ram.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(ram, GameSiegeRam.BotCrewRadius);

        private static void Prune(Crew crew, long now)
        {
            foreach (int index in crew.Seats.Where(pair => pair.Value.Until < now || !pair.Value.Bot.TryGetTarget(out GameBot seated) ||
                             !seated.IsAlive || seated.ObjectState != GameObject.eObjectState.Active)
                         .Select(pair => pair.Key).ToArray())
                crew.Seats.Remove(index);
        }
    }
}
