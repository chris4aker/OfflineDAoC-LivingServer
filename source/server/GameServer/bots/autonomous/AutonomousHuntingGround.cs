using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #570 step 3: a solo autonomous gamebot keeps a hunting ground, as a 2001-2003 player levelled through one zone and
    /// its neighbours at a time instead of crossing the realm between every camp. This class holds the rules;
    /// <see cref="AutonomousWorldBotController"/> applies them (AutonomousWorldBotController.HuntingGround.cs):
    /// <list type="bullet">
    /// <item>the solo camp draw weighs the bot's hunting zone x8, its neighbouring zones x3, the rest of the region x1 and
    /// other regions x0.25, among the camps that already passed every other filter (crowded camps are left out before
    /// this, so camp competition still comes first);</item>
    /// <item>at a camp whose own monster is gone the bot hunts the area around it (2,600 units) instead of leaving after
    /// the empty-camp wait: any monster that cons green up to the bot's solo ceiling and passes every other filter;</item>
    /// <item>a solo bot pulls the monster standing apart from the others first (Prima's Warden: "soloing antisocial mobs
    /// that don't call for help"), as parties already do;</item>
    /// <item>healers, bards and minstrels, and the archers, prefer blue targets to yellow; the con ceiling is unchanged;</item>
    /// <item>once 6 of its last 8 kills at a camp conned green it moves up a spot: the next plan takes a camp of blue or
    /// harder typical con when one exists, which the zone weights keep near.</item>
    /// </list>
    /// Only autonomous gamebots without a group, outdoors and outside Darkness Falls. Parties, realm raids, hired and
    /// /bot companions, buffbots, players and monsters are untouched. Every rule is a table lookup or a few comparisons
    /// on work that already happens: no new scans, no route requests, no special trips, no combat, XP or coin numbers.
    /// </summary>
    public static class AutonomousHuntingGround
    {
        [ServerProperty("bots", "bot_hunting_grounds",
            "Solo gamebots keep a hunting ground (#570): the camp draw prefers the bot's own zone and its neighbours, a bot hunts the area around a camp whose monster is gone, pulls the monster standing apart first, healers, bards and archers prefer blue targets, and a bot moves up a spot when most of its kills con green.", true)]
        public static bool Enabled = true;

        // ---------------------------------------------------------------- zone preference

        public const double SameZoneFactor = 8;
        public const double NeighbourZoneFactor = 3;
        public const double SameRegionFactor = 1;
        public const double OtherRegionFactor = 0.25;

        /// <summary>
        /// Pure rule: the draw multiplier for a camp, given the bot's hunting zone (region 0 = none yet: every camp 1).
        /// </summary>
        public static double ZoneFactor(ushort homeRegion, ushort homeZone, ushort campRegion, ushort campZone, bool neighbour)
        {
            if (homeRegion == 0)
                return 1;
            if (campRegion != homeRegion)
                return OtherRegionFactor;
            if (campZone == homeZone)
                return SameZoneFactor;
            return neighbour ? NeighbourZoneFactor : SameRegionFactor;
        }

        /// <summary>Pure rule: a weight times its zone factor, never below 1 (a legal camp always keeps a chance).</summary>
        public static int Weigh(int weight, double factor) => Math.Max(1, (int)Math.Round(Math.Max(1, weight) * factor));

        private static readonly ConcurrentDictionary<(ushort, ushort), bool> Neighbours = new();

        /// <summary>Two zones of a region that share an edge (worked out once per pair, then a dictionary lookup).</summary>
        public static bool AreNeighbours(Zone a, Zone b)
        {
            if (a == null || b == null || a == b || a.ZoneRegion != b.ZoneRegion)
                return false;
            (ushort, ushort) key = a.ID < b.ID ? (a.ID, b.ID) : (b.ID, a.ID);
            return Neighbours.GetOrAdd(key, _ => AutonomousZoneItinerary.TryGetSharedEdge(a, b, out _));
        }

        // ---------------------------------------------------------------- per-bot state (memory only)

        private sealed class State
        {
            public readonly object Gate = new();
            public ushort HomeRegion;
            public ushort HomeZone;
            public string CampId;
            public int KillFill;     // kills counted at this camp, up to KillWindow
            public byte GreenBits;   // the last KillWindow kills, 1 = green or easier
            public bool MoveUp;
        }

        private static readonly ConcurrentDictionary<long, State> States = new();

        /// <summary>The bot's hunting ground (region 0 when it has none yet).</summary>
        public static (ushort Region, ushort Zone) Home(long botId)
        {
            if (!States.TryGetValue(botId, out State state))
                return default;
            lock (state.Gate)
                return (state.HomeRegion, state.HomeZone);
        }

        /// <summary>A solo bot chose an outdoor camp: that zone is its hunting ground, and the kill count starts over
        /// when the camp changed.</summary>
        public static void NoteSoloCamp(long botId, string campId, ushort region, ushort zone)
        {
            if (botId <= 0 || region == 0 || string.IsNullOrEmpty(campId))
                return;
            State state = States.GetOrAdd(botId, _ => new State());
            lock (state.Gate)
            {
                state.HomeRegion = region;
                state.HomeZone = zone;
                if (!string.Equals(state.CampId, campId, StringComparison.Ordinal))
                {
                    state.CampId = campId;
                    state.KillFill = 0;
                    state.GreenBits = 0;
                    state.MoveUp = false;
                }
            }
        }

        /// <summary>Forget a bot (tests; a deleted or retired bot).</summary>
        public static void Forget(long botId) => States.TryRemove(botId, out _);

        // ---------------------------------------------------------------- moving up

        public const int KillWindow = 8;
        public const int GreenKillsToMoveUp = 6;

        /// <summary>Pure rule: the window of the last kills is full and most of them conned green.</summary>
        public static bool ShouldMoveUp(int fill, byte greenBits) =>
            fill >= KillWindow && BitOperations.PopCount(greenBits) >= GreenKillsToMoveUp;

        /// <summary>Pure rule: the green-kill window after one more kill.</summary>
        public static (int Fill, byte Bits) AddKill(int fill, byte bits, bool green) =>
            (Math.Min(KillWindow, fill + 1), (byte)((bits << 1) | (green ? 1 : 0)));

        /// <summary>
        /// Pure state step (tests and <see cref="RecordKill(GameBot, GameNPC)"/>): one kill at <paramref name="camp"/>;
        /// true when this kill turned the move-up flag on.
        /// </summary>
        public static bool RecordKill(long botId, string camp, bool green)
        {
            if (!Enabled || botId <= 0 || string.IsNullOrEmpty(camp) || !States.TryGetValue(botId, out State state))
                return false;
            lock (state.Gate)
            {
                if (!string.Equals(state.CampId, camp, StringComparison.Ordinal))
                    return false;
                (state.KillFill, state.GreenBits) = AddKill(state.KillFill, state.GreenBits, green);
                if (!ShouldMoveUp(state.KillFill, state.GreenBits))
                    return false;
                state.MoveUp = true;
                state.KillFill = 0;
                state.GreenBits = 0;
                return true;
            }
        }

        /// <summary>A solo kill credited to an autonomous gamebot (from the kill award): counts toward moving up when it is
        /// at the bot's current camp.</summary>
        public static void RecordKill(GameBot bot, GameNPC killed)
        {
            if (!Enabled || bot == null || killed == null || bot.Group != null || !bot.IsAutonomousWorldBot ||
                bot.DatabaseID <= 0)
                return;
            bool green = ConLevels.GetConColor(bot.GetConLevel(killed)) <= ConColor.GREEN;
            RecordKill(bot.DatabaseID, bot.PersistentRecord?.CurrentCampId, green);
        }

        /// <summary>True once (then cleared) when most recent kills at the current camp conned green.</summary>
        public static bool TakeMoveUp(long botId)
        {
            if (!States.TryGetValue(botId, out State state))
                return false;
            lock (state.Gate)
            {
                if (!state.MoveUp)
                    return false;
                state.MoveUp = false;
                return true;
            }
        }

        /// <summary>The typical con a moved-up bot's next camp needs (when any legal camp has it).</summary>
        public const ConColor MoveUpMinimumCon = ConColor.BLUE;

        /// <summary>Pure rule: after a move-up, the camps of blue or harder typical con, or all of them when none is.</summary>
        public static T[] MoveUpCamps<T>(IReadOnlyCollection<T> legal, Func<T, ConColor> typicalCon)
        {
            var up = new List<T>(legal.Count);
            foreach (T camp in legal)
                if (typicalCon(camp) >= MoveUpMinimumCon)
                    up.Add(camp);
            if (up.Count > 0)
                return up.ToArray();
            var all = new T[legal.Count];
            int index = 0;
            foreach (T camp in legal)
                all[index++] = camp;
            return all;
        }

        // ---------------------------------------------------------------- area hunting

        /// <summary>How far from its camp anchor a solo bot hunts other monsters once the camp's own monster is gone.</summary>
        public const int CampAreaRadius = 2_600;

        /// <summary>Pure rule: area hunting is for solo bots outdoors, outside Darkness Falls.</summary>
        public static bool AreaHuntingApplies(bool enabled, bool solo, bool dungeon, bool darknessFalls) =>
            enabled && solo && !dungeon && !darknessFalls;

        public static bool WithinCampArea(int campX, int campY, int x, int y)
        {
            long dx = (long)x - campX, dy = (long)y - campY;
            return dx * dx + dy * dy <= (long)CampAreaRadius * CampAreaRadius;
        }

        /// <summary>Pure rule: an area monster cons green up to the bot's solo ceiling (never grey: no experience, never
        /// above what the planner would give this bot alone).</summary>
        public static bool AreaConAllowed(ConColor con, ConColor ceiling) =>
            con != ConColor.UNKNOWN && con >= ConColor.GREEN && con <= ceiling;

        // ---------------------------------------------------------------- solo pull order

        /// <summary>Candidates (in the cheap order) that get the "standing apart" check.</summary>
        public const int IsolationCandidates = 8;

        /// <summary>The extra reach of a pull path around a monster's aggro range.</summary>
        public const int PullPathMargin = 200;

        /// <summary>The social reach used for a monster that does not attack on sight (its friends still help it).</summary>
        public const int MinimumSocialRange = 300;

        /// <summary>
        /// Pure rule: how many monsters of <paramref name="pool"/> stand within each candidate's reach (its aggro range, at
        /// least <see cref="MinimumSocialRange"/>, plus <see cref="PullPathMargin"/>). A candidate does not count itself.
        /// </summary>
        public static int[] NeighbourCounts(Vector3[] candidates, int[] aggroRanges, Vector3[] pool)
        {
            int[] counts = new int[candidates.Length];
            for (int i = 0; i < candidates.Length; i++)
            {
                float reach = Math.Max(MinimumSocialRange, aggroRanges[i]) + PullPathMargin;
                float reachSquared = reach * reach;
                foreach (Vector3 other in pool)
                {
                    float d = Vector3.DistanceSquared(candidates[i], other);
                    if (d > 1 && d <= reachSquared)
                        counts[i]++;
                }
            }
            return counts;
        }

        /// <summary>
        /// Pure rule: the candidates' indexes by tier first (a grey stays behind every non-grey, as the caller sorted
        /// them), then fewest neighbours, keeping the given order among equals.
        /// </summary>
        public static int[] IsolationOrder(int[] neighbourCounts, int[] tiers = null)
        {
            int[] order = new int[neighbourCounts.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            int Tier(int index) => tiers == null ? 0 : tiers[index];
            // insertion sort: stable and allocation-free for at most eight entries
            for (int i = 1; i < order.Length; i++)
            {
                int current = order[i];
                int j = i - 1;
                while (j >= 0 && (Tier(order[j]) > Tier(current) ||
                       Tier(order[j]) == Tier(current) && neighbourCounts[order[j]] > neighbourCounts[current]))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = current;
            }
            return order;
        }

        /// <summary>
        /// The sorted solo candidates with the first <see cref="IsolationCandidates"/> reordered so the one standing apart
        /// from the other live monsters around (<paramref name="nearby"/>, the scan the search already made) comes first.
        /// <paramref name="tier"/> keeps the caller's grey-last order. The rest keep their order. Lazy: the expensive
        /// route checks after it still stop at the first good target.
        /// </summary>
        public static IEnumerable<GameNPC> PreferIsolated(IEnumerable<GameNPC> sorted, IReadOnlyList<GameNPC> nearby,
            Func<GameNPC, int> tier)
        {
            using IEnumerator<GameNPC> e = sorted.GetEnumerator();
            var head = new List<GameNPC>(IsolationCandidates);
            while (head.Count < IsolationCandidates && e.MoveNext())
                head.Add(e.Current);
            if (head.Count > 1)
            {
                var pool = new List<Vector3>(nearby.Count);
                foreach (GameNPC npc in nearby)
                    if (npc != null && npc.IsAlive && npc.Realm == eRealm.None && npc.Brain != null &&
                        (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0 && npc is not GameSummonedPet)
                        pool.Add(new Vector3(npc.X, npc.Y, npc.Z));
                var positions = new Vector3[head.Count];
                var ranges = new int[head.Count];
                var tiers = new int[head.Count];
                for (int i = 0; i < head.Count; i++)
                {
                    positions[i] = new Vector3(head[i].X, head[i].Y, head[i].Z);
                    ranges[i] = (head[i].Brain as IOldAggressiveBrain)?.AggroRange ?? 0;
                    tiers[i] = tier?.Invoke(head[i]) ?? 0;
                }
                foreach (int index in IsolationOrder(NeighbourCounts(positions, ranges, pool.ToArray()), tiers))
                    yield return head[index];
            }
            else if (head.Count == 1)
                yield return head[0];
            while (e.MoveNext())
                yield return e.Current;
        }

        // ---------------------------------------------------------------- con preference by class

        /// <summary>
        /// Pure rule: the con a solo bot of this class prefers among targets it may pull (Prima's 2002 class sections:
        /// Healer "stay away from the yellow, orange, red or purple con mobs", Shaman "yellow mobs will often kill you",
        /// Minstrel "Solo blues", Scout "Hunt mobs that con blue and green", soloing "not really recommended for Bards").
        /// Druids go with the healers, Hunters and Rangers with the Scout. Everyone else, Clerics and Friars included
        /// (Cleric: "you can easily solo yellows"), keeps yellow first. The con ceiling itself is unchanged.
        /// </summary>
        public static ConColor PreferredSoloCon(eCharacterClass characterClass) => characterClass switch
        {
            eCharacterClass.Shaman or eCharacterClass.Healer or eCharacterClass.Druid or eCharacterClass.Bard or
                eCharacterClass.Minstrel or eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger
                => ConColor.BLUE,
            _ => ConColor.YELLOW,
        };

        /// <summary>Pure rule: the con part of the target sort (220 units of walking per con step from the preferred con;
        /// with yellow preferred, exactly the sort solo bots always had).</summary>
        public static int ConSortPenalty(ConColor con, ConColor preferred) => Math.Abs((int)con - (int)preferred) * 220;
    }
}
