using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// From Offline DAoC True 1.65 (shadowofze; its veteran review M5, 2026-11-11): one shared picture of a group's fight, rebuilt at most once a second by whichever
    /// member asks first. Before this every member worked the same fight out on its own: add control, the root and Bard
    /// mez looks, the add count and the tank protection search each ran their own area search (1,800-2,600 units) once a
    /// second or more, so an eight-man fight made 20-40 area searches a second that all found the same few monsters.
    ///
    /// The board is built from what the game already keeps: each member's (and pet's) attacker list, target and aggro
    /// list, plus ONE area search per group every 2 s for a monster running in that has not hit anyone yet. It holds the
    /// monsters on the group, what the members are targeting and really hitting, who is being hit in melee and the
    /// counts the bail-out check needs. Readers get an immutable snapshot (nothing in it changes after it is published),
    /// so bots thinking in parallel share it without locks; only the rebuild takes the group's lock.
    /// Solo bots (a pet class mezzing adds) get a board of their own, keyed by the bot.
    /// Living Server 1.2: only the retreat rules (BotBrain.Retreat, the group RUN) read it; 1.2's add control, roots and
    /// tank peel keep their own searches.
    /// </summary>
    public sealed class GroupFightBoard
    {
        public const long RebuildMilliseconds = 1_000;
        /// <summary>The one area search per group (for adds running in before their first blow).</summary>
        public const int ScanRadius = 2_000;
        /// <summary>... at most this often; boards built in between re-read the NPCs the last search found.</summary>
        // one second (owner 2026-11-11: a tank or a guarding pet must see an add running at the healer before it lands;
        // still one search per group, not one per member per think)
        public const long ScanIntervalMilliseconds = 1_000;

        public long BuiltAt { get; init; }
        public ushort RegionId { get; init; }
        /// <summary>Living members in the board's region (the bot itself when solo).</summary>
        public GameLiving[] Members { get; init; } = [];
        public HashSet<GameLiving> MemberSet { get; init; } = new();
        /// <summary>Members' living pets (controlled brains).</summary>
        public HashSet<GameLiving> Pets { get; init; } = new();
        /// <summary>What members and pets have targeted.</summary>
        public HashSet<GameLiving> SelectedTargets { get; init; } = new();
        /// <summary>Monsters a fighting member, or a pet, has targeted.</summary>
        public HashSet<GameLiving> ActiveTargets { get; init; } = new();
        /// <summary>What a member or pet is really hitting right now (swinging in reach, or a harmful cast at it).</summary>
        public HashSet<GameLiving> AttackedTargets { get; init; } = new();
        /// <summary>What every pet of the group targets (owned pets and owner-linked elementals and bombers).</summary>
        public HashSet<GameLiving> PetTargets { get; init; } = new();
        /// <summary>Living NPCs that target a member or a member's pet (monsters, guards, enemy-realm bots).</summary>
        public GameNPC[] NpcsOnGroup { get; init; } = [];
        /// <summary>Every living enemy targeting a member or pet: the NPCs above and players from the attacker lists.</summary>
        public GameLiving[] Threats { get; init; } = [];
        /// <summary>Members an enemy is hitting in melee (from their attacker lists).</summary>
        public HashSet<GameLiving> HitInMelee { get; init; } = new();
        /// <summary>Monsters (not bots, realm none) whose target is a member (pets not counted).</summary>
        public int MonstersOnGroup { get; init; }
        /// <summary>... of those, not mesmerized, stunned or rooted.</summary>
        public int UncontrolledOnGroup { get; init; }

        public bool IsFresh(long now, ushort region) => now - BuiltAt < RebuildMilliseconds && region == RegionId;

        /// <summary>Pure: a board built at <paramref name="builtAt"/> is rebuilt from this time on.</summary>
        public static bool NeedsRebuild(long now, long builtAt, bool sameRegion) =>
            !sameRegion || now - builtAt >= RebuildMilliseconds;
    }

    public partial class BotBrain
    {
        private sealed class FightBoardHolder
        {
            public volatile GroupFightBoard Board;
            public readonly object Lock = new();
            public GameNPC[] LastScan = [];
            public long LastScanTick = long.MinValue / 2;
            public ushort LastScanRegion;
        }

        /// <summary>
        /// One board per group and region (performance review 2026-11-11): a member fighting in another region (walking
        /// back from its bind) keeps a board and scan timer of its own instead of rebuilding the camp's board, and re-running
        /// its area search, on every alternate ask.
        /// </summary>
        private static readonly ConditionalWeakTable<object, ConcurrentDictionary<ushort, FightBoardHolder>> FightBoards = new();
        private static long _fightBoardBuilds;

        /// <summary>How many boards were built since start (a load counter for the logs and tests).</summary>
        public static long FightBoardBuilds => System.Threading.Interlocked.Read(ref _fightBoardBuilds);

        /// <summary>The fight board of this bot's group (or of the bot alone), at most a second old.</summary>
        internal GroupFightBoard FightBoard() => GetFightBoard(Body);

        /// <summary>The fight board for <paramref name="asker"/>'s group, rebuilt by the asker when it is a second old.</summary>
        public static GroupFightBoard GetFightBoard(GameLiving asker)
        {
            if (asker == null)
                return null;
            object key = (object)asker.Group ?? asker;
            FightBoardHolder holder = FightBoards.GetValue(key, _ => new ConcurrentDictionary<ushort, FightBoardHolder>())
                .GetOrAdd(asker.CurrentRegionID, _ => new FightBoardHolder());
            long now = GameLoop.GameLoopTime;
            GroupFightBoard board = holder.Board;
            if (board != null && board.IsFresh(now, asker.CurrentRegionID))
                return board;
            lock (holder.Lock)
            {
                board = holder.Board;
                if (board != null && board.IsFresh(now, asker.CurrentRegionID))
                    return board;
                board = BuildFightBoard(asker, now, holder);
                holder.Board = board;
                System.Threading.Interlocked.Increment(ref _fightBoardBuilds);
                return board;
            }
        }

        /// <summary>
        /// The area search keeps every living NPC in range, not only ones that already have a target (combat review
        /// 2026-11-11): a monster that aggroes just after the search and is still running at the group is then on the next
        /// board (at most a second later) instead of only after its first blow or the next search.
        /// </summary>
        public static bool KeepInScan(GameNPC npc) => npc?.IsAlive == true;

        /// <summary>A stored NPC from the area search is on the group now: it targets a member or a member's pet.</summary>
        public static bool ScannedNpcOnGroup(GameNPC npc, HashSet<GameLiving> members, HashSet<GameLiving> pets) =>
            npc?.TargetObject is GameLiving victim && (members.Contains(victim) || pets.Contains(victim));

        /// <summary>How old a board <see cref="PeekFightBoard"/> still hands out (members rebuild it every second in a fight).</summary>
        public const long PeekMaxAgeMilliseconds = 2_000;

        /// <summary>
        /// The group's current board for <paramref name="asker"/>'s region when it is at most
        /// <see cref="PeekMaxAgeMilliseconds"/> old, else null - never rebuilt here (performance review 2026-11-11: the
        /// group coordinator reads it under its global lock, where a rebuild could run the area search).
        /// </summary>
        /// <summary>A member (or pet) hitting this monster now: swinging within reach of it, or casting a harmful spell at it.</summary>
        private static bool BoardAttacksTarget(GameLiving member, GameLiving target) =>
            member.TargetObject == target &&
            (member.IsAttacking && member.attackComponent != null &&
                member.IsWithinRadius(target, member.attackComponent.AttackRange + 50) ||
             member.IsCasting && member.castingComponent?.SpellHandler is { Spell.IsHarmful: true } handler &&
                handler.Target == target);

        /// <summary>A live attacker that is on <paramref name="member"/>, fighting, and in melee reach of it.</summary>
        private static bool BoardMeleeAttackerOn(GameLiving attacker, GameLiving member) =>
            attacker != null && attacker.IsAlive && attacker.TargetObject == member &&
            (attacker.IsAttacking || attacker.InCombat) && attacker.IsWithinRadius(member, attacker.MeleeAttackRange + 50);

        public static GroupFightBoard PeekFightBoard(GameLiving asker)
        {
            if (asker == null)
                return null;
            object key = (object)asker.Group ?? asker;
            if (!FightBoards.TryGetValue(key, out var regions) || !regions.TryGetValue(asker.CurrentRegionID, out FightBoardHolder holder))
                return null;
            GroupFightBoard board = holder.Board;
            return board != null && PeekFresh(GameLoop.GameLoopTime, board.BuiltAt) ? board : null;
        }

        /// <summary>Pure: a board built at <paramref name="builtAt"/> may still be peeked at.</summary>
        public static bool PeekFresh(long now, long builtAt) => now - builtAt <= PeekMaxAgeMilliseconds;

        private static GroupFightBoard BuildFightBoard(GameLiving asker, long now, FightBoardHolder holder)
        {
            List<GameLiving> members = new(8);
            if (asker.Group != null)
            {
                foreach (GameLiving member in asker.Group.GetMembersInTheGroup())
                    if (member?.IsAlive == true && member.CurrentRegion == asker.CurrentRegion)
                        members.Add(member);
            }
            else if (asker.IsAlive)
                members.Add(asker);

            var memberSet = new HashSet<GameLiving>(members);
            var pets = new HashSet<GameLiving>();
            var selected = new HashSet<GameLiving>();
            var active = new HashSet<GameLiving>();
            var attacked = new HashSet<GameLiving>();
            var petTargets = new HashSet<GameLiving>();
            var hitInMelee = new HashSet<GameLiving>();
            var candidates = new HashSet<GameLiving>();

            foreach (GameLiving member in members)
            {
                if (member.TargetObject is GameLiving target && target.IsAlive)
                {
                    selected.Add(target);
                    candidates.Add(target);
                    if ((member.IsAttacking || member.InCombat) && target is GameNPC)
                        active.Add(target);
                    if (BoardAttacksTarget(member, target))
                        attacked.Add(target);
                }
                if (member.attackComponent != null)
                    foreach (GameLiving attacker in member.attackComponent.AttackerTracker.Attackers)
                    {
                        if (attacker == null)
                            continue;
                        candidates.Add(attacker);
                        if (BoardMeleeAttackerOn(attacker, member))
                            hitInMelee.Add(member);
                    }
                if (member is GameBot { Brain: BotBrain memberBrain })
                    foreach (GameLiving aggro in memberBrain.AggroList.Keys)
                        candidates.Add(aggro);
                if (member.ControlledBrain?.Body is GameLiving pet && pet.IsAlive)
                {
                    pets.Add(pet);
                    if (pet.TargetObject is GameLiving petTarget && petTarget.IsAlive)
                    {
                        selected.Add(petTarget);
                        petTargets.Add(petTarget);
                        candidates.Add(petTarget);
                        if (petTarget is GameNPC)
                            active.Add(petTarget);
                        if (BoardAttacksTarget(pet, petTarget))
                            attacked.Add(petTarget);
                    }
                    if (pet.attackComponent != null)
                        foreach (GameLiving attacker in pet.attackComponent.AttackerTracker.Attackers)
                            if (attacker != null)
                                candidates.Add(attacker);
                }
            }

            // the one area search per group (at most every ScanIntervalMilliseconds; the board in between re-reads the
            // NPCs that search found, so one that aggroes after the search still shows): monsters running at a member
            // before their first blow, and pets with no owner link (Theurgist elementals, bombers) whose brain names a
            // member as owner
            IEnumerable<GameNPC> scanned = holder.LastScan;
            if (members.Count > 0 && (now - holder.LastScanTick >= GroupFightBoard.ScanIntervalMilliseconds ||
                                      holder.LastScanRegion != asker.CurrentRegionID))
            {
                List<GameNPC> found = new();
                foreach (GameNPC npc in asker.GetNPCsInRadius(GroupFightBoard.ScanRadius))
                    if (KeepInScan(npc))
                        found.Add(npc);
                holder.LastScan = found.ToArray();
                holder.LastScanTick = now;
                holder.LastScanRegion = asker.CurrentRegionID;
                scanned = holder.LastScan;
            }
            foreach (GameNPC npc in scanned)
            {
                if (npc?.IsAlive != true)
                    continue;
                if (npc.Brain is IControlledBrain petBrain && petBrain.GetLivingOwner() is GameLiving owner &&
                    memberSet.Contains(owner))
                {
                    if (npc.TargetObject is GameLiving elementalTarget && elementalTarget.IsAlive)
                        petTargets.Add(elementalTarget);
                    continue;
                }
                if (ScannedNpcOnGroup(npc, memberSet, pets))
                    candidates.Add(npc);
            }

            List<GameNPC> npcsOnGroup = new();
            List<GameLiving> threats = new();
            int monsters = 0, uncontrolled = 0;
            foreach (GameLiving candidate in candidates)
            {
                if (candidate == null || !candidate.IsAlive || candidate.ObjectState != GameObject.eObjectState.Active ||
                    candidate.CurrentRegion != asker.CurrentRegion || memberSet.Contains(candidate) || pets.Contains(candidate) ||
                    candidate.TargetObject is not GameLiving victim ||
                    !(memberSet.Contains(victim) || pets.Contains(victim)))
                    continue;
                threats.Add(candidate);
                if (candidate is not GameNPC npc)
                    continue;
                npcsOnGroup.Add(npc);
                if (npc is GameBot || npc.Realm != eRealm.None || !memberSet.Contains(victim))
                    continue;
                monsters++;
                if (!npc.IsMezzed && !npc.IsStunned && !IsBreakablyControlled(npc))
                    uncontrolled++;
            }

            return new GroupFightBoard
            {
                BuiltAt = now,
                RegionId = asker.CurrentRegionID,
                Members = members.ToArray(),
                MemberSet = memberSet,
                Pets = pets,
                SelectedTargets = selected,
                ActiveTargets = active,
                AttackedTargets = attacked,
                PetTargets = petTargets,
                NpcsOnGroup = npcsOnGroup.ToArray(),
                Threats = threats.ToArray(),
                HitInMelee = hitInMelee,
                MonstersOnGroup = monsters,
                UncontrolledOnGroup = uncontrolled,
            };
        }
    }
}
