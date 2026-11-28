using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The holders of the open-field fight boards (work package 0 of docs/RVR-FIELD-1.2.md): one per group and region (only
/// the main body's region has a stance: <see cref="RvrFightBoard.MainBody"/>), as
/// BotBrain's FightBoards are for <see cref="GroupFightBoard"/>. A board is built only when a consumer asks
/// (<see cref="Get"/>), at most once a second per holder, by the first asker and under that holder's lock; everyone
/// else gets the published snapshot. <see cref="Peek"/> never builds, for callers inside the group coordinator's lock.
/// The builder takes no global lock: what it needs from the coordinator or the event layer is copied into the group's
/// <see cref="RvrGroupState"/> when it changes (<see cref="UpdateState"/>).
///
/// Nothing asks in work package 0, so no board is built and the game loop does no extra work.
/// </summary>
public static class RvrFightBoards
{
    private static readonly Logger Log = LoggerManager.Create(typeof(RvrFightBoards));

    /// <summary>A board in contact is logged at most this often per group and region ...</summary>
    public const long LogGapMilliseconds = 10_000;
    /// <summary>... and a change of stance sooner, but not more often than this.</summary>
    public const long LogStanceGapMilliseconds = 2_000;

    /// <summary>
    /// One group's board for one region: the published snapshot, the lock its rebuild takes, and its log clock. The
    /// builder is passed in, so the once-a-second rule can be tested without a game world.
    /// </summary>
    public sealed class Holder
    {
        private readonly object _lock = new();
        private volatile RvrFightBoard _board;
        private long _builds;
        private long _lastLogAt = long.MinValue / 2;
        private RvrStance _lastLogStance = RvrStance.Engage;

        /// <summary>The published board, however old; null before the first build.</summary>
        public RvrFightBoard Board => _board;

        /// <summary>How many boards this holder built.</summary>
        public long Builds => Interlocked.Read(ref _builds);

        /// <summary>
        /// The board at <paramref name="now"/> for <paramref name="region"/>: the published one while it is fresh,
        /// otherwise <paramref name="build"/> (given the board before it, or null) runs once under the holder's lock and
        /// its result is published.
        /// </summary>
        public RvrFightBoard Get(long now, ushort region, Func<RvrFightBoard, RvrFightBoard> build) =>
            Get(now, region, build, out _);

        /// <inheritdoc cref="Get(long, ushort, Func{RvrFightBoard, RvrFightBoard})"/>
        public RvrFightBoard Get(long now, ushort region, Func<RvrFightBoard, RvrFightBoard> build, out bool built)
        {
            built = false;
            RvrFightBoard board = _board;
            if (board != null && board.IsFresh(now, region))
                return board;
            lock (_lock)
            {
                board = _board;
                if (board != null && board.IsFresh(now, region))
                    return board;
                board = build(board);
                if (board == null)
                    return null;
                _board = board;
                Interlocked.Increment(ref _builds);
                built = true;
                return board;
            }
        }

        /// <summary>The published board when it is at most <see cref="RvrFightBoard.PeekMaxAgeMilliseconds"/> old, else null. Never builds.</summary>
        public RvrFightBoard Peek(long now)
        {
            RvrFightBoard board = _board;
            return board != null && RvrFightBoard.PeekFresh(now, board.BuiltAt) ? board : null;
        }

        /// <summary>Whether this just-built board gets its RVR_FIGHT_BOARD line, and notes that it did.</summary>
        public bool TakeLogTurn(RvrFightBoard board)
        {
            lock (_lock)
            {
                if (!ShouldLog(board.BuiltAt, _lastLogAt, board.Enemies.Length, board.Plan.Stance != _lastLogStance))
                    return false;
                _lastLogAt = board.BuiltAt;
                _lastLogStance = board.Plan.Stance;
                return true;
            }
        }
    }

    /// <summary>
    /// The rate limit of RVR_FIGHT_BOARD. A board with no enemy in view is never logged; one with enemies at most every
    /// <see cref="LogGapMilliseconds"/>, or after <see cref="LogStanceGapMilliseconds"/> when the stance changed. Pure.
    /// </summary>
    public static bool ShouldLog(long now, long lastLogAt, int enemies, bool stanceChanged) =>
        enemies > 0 && now - lastLogAt >= (stanceChanged ? LogStanceGapMilliseconds : LogGapMilliseconds);

    private sealed class GroupEntry
    {
        public RvrGroupState State = RvrGroupState.Empty;
        public readonly ConcurrentDictionary<ushort, Holder> Regions = new();
    }

    // Keyed by the Group, or by the bot itself when it is solo; the entry goes when its key does.
    private static readonly ConditionalWeakTable<object, GroupEntry> Groups = new();
    private static long _builds;

    /// <summary>How many boards were built since start (a load counter for the logs and tests).</summary>
    public static long Builds => Interlocked.Read(ref _builds);

    /// <summary>
    /// The board holders (one per group and region) across the live groups, for SERVER_COLLECTION_CENSUS (#731). Walks
    /// the weak table, so it's for the census's pool thread, never the game loop.
    /// </summary>
    internal static long HolderCount()
    {
        long count = 0;
        foreach (var pair in Groups)
            count += pair.Value.Regions.Count;
        return count;
    }

    /// <summary>What a living's boards and stored facts are keyed by: its group, or itself when it has none.</summary>
    public static object KeyOf(GameLiving living) => (object)living?.Group ?? living;

    /// <summary>
    /// The fight board of <paramref name="asker"/>'s group (or of the asker alone), at most a second old; the asker
    /// rebuilds it when it is older. Callers check <see cref="RvrTacticsScope"/> first: the board itself does not.
    /// Never call this while holding the group coordinator's lock (use <see cref="Peek"/>).
    /// </summary>
    public static RvrFightBoard Get(GameLiving asker)
    {
        if (asker == null)
            return null;
        GroupEntry entry = Groups.GetValue(KeyOf(asker), static _ => new GroupEntry());
        ushort region = asker.CurrentRegionID;
        Holder holder = entry.Regions.GetOrAdd(region, static _ => new Holder());
        long now = GameLoop.GameLoopTime;
        RvrFightBoard board = holder.Get(now, region, previous => BuildBoard(asker, now, previous, entry), out bool built);
        if (built)
        {
            Interlocked.Increment(ref _builds);
            if (Log.IsInfoEnabled && holder.TakeLogTurn(board))
                Log.Info(DescribeBoard(asker, board, holder.Builds));
            // #505: a board in contact keeps its group's fight open (logging only).
            if (board != null && board.InContact)
                RvrFights.OnBoard(asker, board);
        }
        return board;
    }

    /// <summary>
    /// The group's current board for <paramref name="asker"/>'s region when it is at most two seconds old, else null.
    /// Never built here: safe under the group coordinator's lock.
    /// </summary>
    public static RvrFightBoard Peek(GameLiving asker)
    {
        if (asker == null || !Groups.TryGetValue(KeyOf(asker), out GroupEntry entry) ||
            !entry.Regions.TryGetValue(asker.CurrentRegionID, out Holder holder))
            return null;
        return holder.Peek(GameLoop.GameLoopTime);
    }

    /// <summary>The plan on the peeked board, or null (see <see cref="Peek"/>).</summary>
    public static RvrFightPlan PeekPlan(GameLiving asker) => Peek(asker)?.Plan;

    /// <summary>
    /// #599 step 2: the called target of the battlegroup lead group that <paramref name="asker"/>'s group follows, read
    /// from the lead's published board for the asker's region when it is at most two seconds old; null when the group
    /// follows no lead (RealmWarbandSupport.BattlegroupLeadOf), the lead has no fresh board there or calls nothing, or the
    /// call is dead. Never builds and takes no lock: safe inside a board build and under the coordinator's lock.
    /// </summary>
    public static GameLiving BattlegroupCallOf(GameLiving asker, long now)
    {
        Group lead = RealmWarbandSupport.BattlegroupLeadOf(asker?.Group);
        if (lead == null || ReferenceEquals(lead, asker.Group) || !Groups.TryGetValue(lead, out GroupEntry entry) ||
            !entry.Regions.TryGetValue(asker.CurrentRegionID, out Holder holder))
            return null;
        GameLiving call = holder.Peek(now)?.Plan?.CalledTarget;
        return call is { IsAlive: true } && call.CurrentRegionID == asker.CurrentRegionID ? call : null;
    }

    /// <summary>The stored facts of a group (the Group object) or a solo bot (the bot); Empty when nothing was stored.</summary>
    public static RvrGroupState StateOf(object groupOrSoloBot) =>
        groupOrSoloBot != null && Groups.TryGetValue(groupOrSoloBot, out GroupEntry entry)
            ? Volatile.Read(ref entry.State) : RvrGroupState.Empty;

    /// <summary>
    /// Changes the stored facts of a group (the Group object) or a solo bot (the bot): <paramref name="change"/> gets
    /// the current record and returns the new one (it may run more than once when two callers race, so it must be
    /// pure). The next board built carries the result. Takes no lock; safe from any thread, the coordinator included.
    /// </summary>
    public static RvrGroupState UpdateState(object groupOrSoloBot, Func<RvrGroupState, RvrGroupState> change)
    {
        if (groupOrSoloBot == null || change == null)
            return RvrGroupState.Empty;
        GroupEntry entry = Groups.GetValue(groupOrSoloBot, static _ => new GroupEntry());
        while (true)
        {
            RvrGroupState current = Volatile.Read(ref entry.State);
            RvrGroupState next = change(current) ?? RvrGroupState.Empty;
            if (ReferenceEquals(next, current) || ReferenceEquals(Interlocked.CompareExchange(ref entry.State, next, current), current))
                return next;
        }
    }

    /// <summary>Forgets a group's boards and stored facts (a disbanded group; tests).</summary>
    public static void Forget(object groupOrSoloBot)
    {
        if (groupOrSoloBot != null)
            Groups.Remove(groupOrSoloBot);
    }

    /// <summary>
    /// The skill tier a group gets when nobody stored one: from its leader's id and personality and the
    /// rvr_bot_awareness setting (<see cref="RvrGroupSkill"/>). The group coordinator may store one from the session id
    /// instead, when the group forms.
    /// </summary>
    public static RvrSkillTier DefaultSkillTier(GameLiving asker)
    {
        GameBot leader = asker?.Group?.LivingLeader as GameBot ?? asker as GameBot;
        long id = leader != null && leader.DatabaseID > 0 ? leader.DatabaseID : asker?.ObjectID ?? 0;
        BotPersonality? personality = AutonomousBotPersonality.Applies(leader) ? AutonomousBotPersonality.For(leader) : null;
        return RvrGroupSkill.For(id, personality, AutonomousRvrPerception.ParseAwareness(Properties.RVR_BOT_AWARENESS));
    }

    // #529: the board keys an enemy as RvR perception and the contact table do, so the shadow can look it up.
    private static long CombatantKey(GameLiving living) => RvrPartyKey.TargetKey(living);

    /// <summary>An enemy player, bot or pet of theirs that the board lists: alive, in the world, of another realm, not stealthed.</summary>
    public static bool ListsEnemy(eRealm realm, GameLiving living) =>
        living != null && living.IsAlive && living.ObjectState == GameObject.eObjectState.Active &&
        living.Realm != realm && living.Realm != eRealm.None && !living.IsStealthed && BotPvpCrowdControl.PlayerLike(living);

    /// <summary>Copies one living into a board record. <paramref name="side"/> and <paramref name="sideTargets"/> are the
    /// board's group (members and pets) and what they have targeted; both null for a member.</summary>
    private static RvrCombatant Snapshot(GameLiving living, HashSet<GameLiving> side, HashSet<GameLiving> sideTargets)
    {
        eCharacterClass characterClass = living switch
        {
            GameBot { CharacterClass: not null } bot => (eCharacterClass)bot.CharacterClass.ID,
            GamePlayer { CharacterClass: not null } player => (eCharacterClass)player.CharacterClass.ID,
            _ => eCharacterClass.Unknown,
        };
        bool pet = living is GameNPC and not GameBot;
        GameLiving target = living.TargetObject as GameLiving;
        RvrControlState control = RvrControlState.None;
        if (living.IsMezzed)
            control |= RvrControlState.Mezzed;
        else if (BotBrain.IsBreakablyControlled(living))
            control |= RvrControlState.Rooted;
        if (living.IsStunned)
            control |= RvrControlState.Stunned;
        return new RvrCombatant
        {
            Living = living,
            Key = CombatantKey(living),
            Realm = living.Realm,
            Class = characterClass,
            Role = pet ? RvrRole.Pet : RvrRoles.For(characterClass),
            IsPlayer = living is GamePlayer,
            IsPet = pet,
            Level = living.Level,
            HealthPercent = living.HealthPercent,
            UsesPower = living.MaxMana > 0,
            PowerPercent = living.ManaPercent,
            Casting = living.IsCasting,
            Fighting = living.IsAttacking || living.InCombat,
            Control = control,
            Target = target,
            TargetRealm = target?.Realm ?? eRealm.None,
            TargetsGroup = side != null && target != null && side.Contains(target),
            TargetedByGroup = sideTargets != null && sideTargets.Contains(living),
            X = living.X,
            Y = living.Y,
            Z = living.Z,
        };
    }

    private static RvrFightBoard BuildBoard(GameLiving asker, long now, RvrFightBoard previous, GroupEntry entry)
    {
        // AI_PROFILE: the whole build, the one area search included.
        using var aiProfile = AiProfile.Scope(AiSystem.RvrFightBoard);

        List<GameLiving> members = new(8);
        bool mainBody = true;
        if (asker.Group != null)
        {
            // One stance per group: the board of a region other than the main body's (most living members, the
            // leader's region on a tie) gets no stance and does not move the group's stance runtime.
            GameLiving leader = asker.Group.LivingLeader;
            var spots = new List<RvrMemberSpot>(8);
            foreach (GameLiving member in asker.Group.GetMembersInTheGroup())
            {
                if (member?.IsAlive != true)
                    continue;
                spots.Add(new RvrMemberSpot(member.CurrentRegionID, member.X, member.Y, member == leader));
                if (member.CurrentRegionID == asker.CurrentRegionID)
                    members.Add(member);
            }
            mainBody = spots.Count == 0 || RvrEngagement.MainBody(spots).Region == asker.CurrentRegionID;
        }
        else if (asker.IsAlive)
            members.Add(asker);

        var side = new HashSet<GameLiving>(members);
        var sideTargets = new HashSet<GameLiving>();
        var friendly = new List<RvrCombatant>(members.Count + 2);
        foreach (GameLiving member in members)
        {
            friendly.Add(Snapshot(member, null, null));
            if (member.TargetObject is GameLiving target)
                sideTargets.Add(target);
        }
        foreach (GameLiving member in members)
            if (member.ControlledBrain?.Body is GameLiving pet && pet.IsAlive && side.Add(pet))
            {
                friendly.Add(Snapshot(pet, null, null));
                if (pet.TargetObject is GameLiving petTarget)
                    sideTargets.Add(petTarget);
            }

        // #529 step 3: a group of autonomous gamebots lists what its members know of (their contacts); a group with a player
        // in it, or with the switch off, lists everyone within the scan radius (the one area search per group and second).
        List<long> owners = ContactOwners(members);
        bool fromContacts = UsesContacts(Properties.RVR_BOARD_FROM_CONTACTS, owners);
        var hostile = new List<RvrCombatant>();
        List<GameLiving> area = null;
        if (members.Count > 0)
        {
            if (fromContacts)
            {
                foreach (GameLiving enemy in ContactEnemies(asker, owners, now))
                    hostile.Add(Snapshot(enemy, side, sideTargets));
            }
            else
            {
                area = AreaEnemies(asker);
                foreach (GameLiving enemy in area)
                    hostile.Add(Snapshot(enemy, side, sideTargets));
            }
        }

        // #529 step 2: the shadow. Which enemies within the scan radius did any member actually know of? From step 3 on it
        // still searches the area (once per build, as the board did before), so a PC log compares what the board now leaves
        // out. Logs only, and nothing the shadow does may stop the board being built (review of #741).
        try
        {
            if (owners != null)
                ShadowContacts(asker, owners, area ?? AreaEnemies(asker), now, fromContacts);
        }
        catch (Exception exception)
        {
            NoteShadowFailure(exception);
        }

        // The tier is given once and kept (a later change of leader does not re-roll it).
        RvrGroupState state = Volatile.Read(ref entry.State);
        if (!state.SkillTier.HasValue)
        {
            RvrSkillTier tier = DefaultSkillTier(asker);
            state = UpdateState(KeyOf(asker), current => current.SkillTier.HasValue ? current : current with { SkillTier = tier });
        }

        RvrFightBoard board = RvrFightBoard.Build(now, asker.CurrentRegionID, asker.Realm, friendly, hostile) with
        {
            State = state, MainBody = mainBody, BattlegroupCall = BattlegroupCallOf(asker, now), FromContacts = fromContacts,
        };
        return board with { Plan = RvrFightPlanner.Next(previous?.Plan, board, state, RvrTacticsSwitches.Current, now) };
    }

    /// <summary>
    /// #529 step 2 (piece B in shadow): checks each enemy the area search listed against the contacts of the group's
    /// autonomous gamebots (RvrContacts) and tallies it in RvrContactShadow: RVR_CONTACT_SHADOW once a minute per realm,
    /// RVR_CONTACT_UNKNOWN for a few "listed but unknown" pairs. A group with a player in it is left out (its player has
    /// his own eyes; its bots are his companions or helpers, which keep no contacts). The shadow changes nothing on the
    /// board; from step 3 on (board=contacts) its unknown enemies are the ones the board left out.
    /// </summary>
    private static long _shadowFailures;

    /// <summary>How many shadow passes threw (and were dropped) since start.</summary>
    public static long ShadowFailures => Interlocked.Read(ref _shadowFailures);

    private static void NoteShadowFailure(Exception exception)
    {
        long failures = Interlocked.Increment(ref _shadowFailures);
        // The shadow threw and was dropped, and the board was built without it. The first and every 1,000th failure are logged.
        if (failures == 1 || failures % 1_000 == 0)
            Log.Warn($"RVR_CONTACT_SHADOW_FAILED failures={failures}", exception);
    }

    /// <summary>
    /// The database ids of the group's members whose contacts count (autonomous gamebots, not companions or helpers), or
    /// null when a player is among them (his group keeps the area search: he has his own eyes) or none keeps contacts.
    /// </summary>
    private static List<long> ContactOwners(List<GameLiving> members)
    {
        if (members == null || members.Count == 0)
            return null;
        var owners = new List<long>(members.Count);
        foreach (GameLiving member in members)
        {
            if (member is GamePlayer)
                return null;
            if (member is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsPlayerLedGroup: false, IsHiredCompanion: false } bot &&
                bot.DatabaseID > 0)
                owners.Add(bot.DatabaseID);
        }
        return owners.Count > 0 ? owners : null;
    }

    /// <summary>#529 step 3: whether a board lists its group's contacts: the switch is on and the group keeps contacts. Pure.</summary>
    public static bool UsesContacts(bool boardFromContacts, IReadOnlyList<long> owners) =>
        boardFromContacts && owners is { Count: > 0 };

    /// <summary>The area search (before 1.2.3 the board's list; now the switch-off path and the shadow's): every enemy within the scan radius.</summary>
    private static List<GameLiving> AreaEnemies(GameLiving asker)
    {
        var enemies = new List<GameLiving>();
        eRealm realm = asker.Realm;
        foreach (GamePlayer player in asker.GetPlayersInRadius(RvrFightBoard.ScanRadius))
            if (ListsEnemy(realm, player))
                enemies.Add(player);
        foreach (GameNPC npc in asker.GetNPCsInRadius(RvrFightBoard.ScanRadius))
            if (ListsEnemy(realm, npc))
                enemies.Add(npc);
        return enemies;
    }

    /// <summary>
    /// #529 step 3: the enemies the group knows of (RvrContacts.Group: any member's contact at most 15 s old), as they
    /// are now: still listable (alive, in the world, not stealthed), in the asker's region and within the scan radius.
    /// </summary>
    private static List<GameLiving> ContactEnemies(GameLiving asker, IReadOnlyList<long> owners, long now)
    {
        var enemies = new List<GameLiving>();
        eRealm realm = asker.Realm;
        foreach (RvrContact contact in AutonomousWorldBotController.RvrContactTable.Group(owners, now))
            if (contact.Subject is GameLiving enemy && ListsEnemy(realm, enemy) && enemy.CurrentRegionID == asker.CurrentRegionID &&
                asker.IsWithinRadius(enemy, RvrFightBoard.ScanRadius))
                enemies.Add(enemy);
        return enemies;
    }

    private static void ShadowContacts(GameLiving asker, List<long> owners, List<GameLiving> area, long now, bool fromContacts)
    {
        if (asker == null || owners == null || owners.Count == 0)
            return;
        RvrContacts contacts = AutonomousWorldBotController.RvrContactTable;
        var listed = new List<RvrShadowListing>(area.Count);
        foreach (GameLiving enemy in area)
        {
            bool known = contacts.KnownByAny(owners, CombatantKey(enemy), now, out RvrContact freshest);
            listed.Add(new RvrShadowListing(CombatantKey(enemy), known, known && freshest.AgeMilliseconds(now) > RvrContactShadow.StaleAfterMilliseconds,
                enemy is GamePlayer, (int)asker.GetDistanceTo(enemy)));
        }
        // The shadow's pairs and the sampled rows use the same group key: the party key (RvrPartyKey.Of), which stays
        // with the group when its leader changes.
        long partyKey = RvrPartyKey.Of(asker);
        AutonomousWorldBotController.RvrContactShadowTally.BoardSource = fromContacts ? RvrContactShadow.FromContacts : RvrContactShadow.FromArea;
        List<RvrShadowListing> samples = AutonomousWorldBotController.RvrContactShadowTally.Board((int)asker.Realm, partyKey, now,
            listed, out List<string> lines);
        if (!Log.IsInfoEnabled)
            return;
        if (lines != null)
            foreach (string line in lines)
                Log.Info(line);
        if (samples == null)
            return;
        // boardGroup= is RVR_FIGHT_BOARD's group= (the leader's id), to join the two rows.
        GameLiving leader = asker.Group?.LivingLeader ?? asker;
        long boardGroup = leader is GameBot leaderBot ? leaderBot.DatabaseID : leader.ObjectID;
        string board = fromContacts ? RvrContactShadow.FromContacts : RvrContactShadow.FromArea;
        foreach (RvrShadowListing sample in samples)
        {
            GameLiving enemy = null;
            foreach (GameLiving listedEnemy in area)
                if (CombatantKey(listedEnemy) == sample.Target)
                {
                    enemy = listedEnemy;
                    break;
                }
            if (enemy == null)
                continue;
            // One enemy within the board's scan radius that no member of the group noticed, was told of or was hit by (from
            // step 3 on, with board=contacts, one the board left out).
            Log.Info($"RVR_CONTACT_UNKNOWN group={partyKey} boardGroup={boardGroup} realm={GlobalConstants.RealmToName(asker.Realm)} target=\"{enemy.Name}\" " +
                     $"kind={AutonomousWorldBotController.PerceivedKind(enemy)} party={RvrPartyKey.Label(RvrPartyKey.Of(enemy))} " +
                     $"distance={sample.Distance} members={owners.Count} board={board} region={asker.CurrentRegionID}");
        }
    }

    /// <summary>The RVR_FIGHT_BOARD line: the group (by its leader), the region, what it sees, both strengths, the stance and the tier.</summary>
    public static string DescribeBoard(GameLiving asker, RvrFightBoard board, long holderBuilds)
    {
        GameLiving leader = asker?.Group?.LivingLeader ?? asker;
        long groupId = leader is GameBot bot ? bot.DatabaseID : leader?.ObjectID ?? 0;
        return $"RVR_FIGHT_BOARD group={groupId} leader=\"{leader?.Name}\" realm={GlobalConstants.RealmToName(board.Realm)} " +
               $"region={board.RegionId} main={board.MainBody} members={board.Friendly.Count} enemies={board.Enemy.Count} enemyPets={board.Enemy.Pets} " +
               $"friendlyHealers={board.Friendly.Healers} friendlyLevel={board.Friendly.AverageLevel} " +
               $"friendlyHealth={board.Friendly.HealthPercent} friendlyPower={board.Friendly.PowerPercent} " +
               $"enemyHealers={board.Enemy.Healers} enemyLevel={board.Enemy.AverageLevel} enemyHealth={board.Enemy.HealthPercent} " +
               $"enemyPower={board.Enemy.PowerPercent} contact={board.InContact} thirdParty={board.ThirdPartyFight} " +
               $"stance={board.Plan.Stance} skill={board.Plan.SkillTier} wipes={board.State.WipesWithin(board.BuiltAt)} " +
               $"from={(board.FromContacts ? RvrContactShadow.FromContacts : RvrContactShadow.FromArea)} builds={holderBuilds}";
    }
}
