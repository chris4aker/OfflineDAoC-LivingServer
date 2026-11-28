using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>One notice, as RVR_NOTICE logs it.</summary>
    public sealed record RvrNoticeEvent(long At, GameBot Bot, GameLiving Target, int Distance, long SinceFirstSight, double Chance,
        string Factors, bool FromCall);

    /// <summary>
    /// #531: one bot's engage at the end of a scan: whom it took, by which rule (the plan's <c>called</c>, <c>mates</c>,
    /// <c>order</c>, or <c>spread</c> when the plan does not apply), and whom the spread pick of before 1.2.3
    /// (AutonomousRvrStaging.TargetIndex over the nearest) would have taken from the same targets.
    /// </summary>
    public sealed record RvrFirstPickEvent(long At, GameBot Bot, GameLiving Target, string Rule, GameLiving SpreadTarget);

    /// <summary>
    /// The frontier scan for the rehearsal (#520). The controller's own scan (AutonomousWorldBotController.TryEngageFrontierThreat
    /// and PerceiveRvrTargets) is private, returns at once without a navmesh and rolls Random.Shared, so it cannot run in
    /// CI or repeat by seed. This one reads the world the same way and calls the same public rules: the lookers
    /// (<see cref="RvrScanRoster"/>), the party scan with the observer cap and reaction delay
    /// (<see cref="AutonomousRvrPerception.ScanParty"/>), the spotter's call to its group
    /// (<see cref="AutonomousRvrPerception.CallRecipients"/>), each bot on its own scan cadence
    /// (<see cref="AutonomousFrontierThreatPolicy.IntervalMilliseconds"/>). The rolls come from the world's seed. A bot that
    /// may engage does what the controller does at the end of its scan: target, aggro, the AGGRO state. Stood in: target
    /// choice is the controller's own: the plan's first pick (<see cref="AutonomousWorldBotController.PlanFirstPick"/>, #531)
    /// where the field tactics apply, else the spread pick; the controller's stance gates are private and not stood in.
    /// Line of sight is the world's navmesh ray, as the controller's (clear everywhere unless a scenario stands a wall).
    /// #529 step 3: a fighting bot does not scan; it looks around by the controller's in-combat refresh instead
    /// (<see cref="RvrCombatRefresh"/>: every 2 s, within 2,500, at most four rays, the same notice rule, contacts only).
    /// Both the scan and the refresh run the notice rule in <c>Perceive</c>; only the scan engages, with the first pick.
    /// </summary>
    public sealed class RvrFightScan
    {
        private readonly AutonomousRvrPerception.Memory _memory = new();
        private readonly AutonomousRvrPerception.ObserverBudget _observers = new();
        private readonly Dictionary<long, long> _nextScan = new();
        private readonly Dictionary<long, long> _lastScan = new();
        private readonly Random _rolls;
        private readonly List<RvrNoticeEvent> _notices = new();
        private readonly List<RvrFirstPickEvent> _picks = new();
        private readonly HashSet<GameBot> _bots = new();
        private readonly Dictionary<long, long> _nextRefresh = new();
        private readonly Dictionary<long, int> _refreshTurn = new();

        /// <summary>#529 step 3: fighting bots look around (the controller's RvrCombatRefresh); as rvr_board_from_contacts by default.</summary>
        public bool CombatRefresh { get; set; } = Properties.RVR_BOARD_FROM_CONTACTS;
        /// <summary>How many in-combat looks were taken, and the rays they cast (the refresh's cost).</summary>
        public int Refreshes { get; private set; }
        public int RefreshRays { get; private set; }
        /// <summary>The most rays one look cast.</summary>
        public int MaxRaysPerRefresh { get; private set; }

        public bool Lookers { get; set; } = true;
        /// <summary>When false, a noticed enemy is logged but the bot does not engage (scenario 3 measures noticing only).</summary>
        public bool Engage { get; set; } = true;
        public IReadOnlyList<RvrNoticeEvent> Notices => _notices;
        /// <summary>Every engage the scan made, in order (#531).</summary>
        public IReadOnlyList<RvrFirstPickEvent> Picks => _picks;
        public RvrAwarenessProfile Profile { get; } = AutonomousRvrPerception.Profile(AutonomousRvrPerception.ParseAwareness(Properties.RVR_BOT_AWARENESS));

        public RvrFightScan(int seed)
        {
            _rolls = new Random(seed * 7919 + 17);
            // #529: the scan writes the server's contact table as PerceiveRvrTargets does, so the boards' shadow has numbers.
            AutonomousWorldBotController.RvrContactTable.Clear();
            AutonomousWorldBotController.RvrContactShadowTally.Clear();
            AutonomousWorldBotController.RvrContactShadowTally.TrackPairs = true;
        }

        private static void Note(GameBot bot, GameLiving target, RvrContactSource source, long now) =>
            AutonomousWorldBotController.RvrContactTable.Note(bot.DatabaseID, (int)bot.Realm, new RvrContactSighting(RvrPartyKey.TargetKey(target),
                RvrPartyKey.Of(target), (int)target.Realm, AutonomousWorldBotController.PerceivedKind(target), target.CurrentRegionID,
                target.X, target.Y, target.Z, target), source, now);

        public void Add(IEnumerable<GameBot> bots)
        {
            foreach (GameBot bot in bots)
                _bots.Add(bot);
        }

        public void Tick(long now)
        {
            foreach (GameBot bot in _bots.OrderBy(b => b.DatabaseID))
            {
                if (!bot.IsAlive || bot.ObjectState != GameObject.eObjectState.Active || bot.Brain is not BotBrain brain)
                    continue;
                if (_nextScan.TryGetValue(bot.DatabaseID, out long due) && now < due)
                    continue;
                _nextScan[bot.DatabaseID] = now + AutonomousFrontierThreatPolicy.IntervalMilliseconds(bot.DatabaseID);
                double interval = _lastScan.TryGetValue(bot.DatabaseID, out long last) ? now - last : AutonomousRvrPerception.DefaultScanIntervalMilliseconds;
                _lastScan[bot.DatabaseID] = now;
                if (Engage && (brain.HasAggro || bot.InCombat || bot.IsAttacking))
                {
                    if (CombatRefresh)
                        Refresh(bot, now);
                    continue;
                }
                if (!ScansNow(brain, bot, now))
                    continue;
                Scan(brain, bot, now, AutonomousRvrPerception.ClampScanInterval(interval));
            }
        }

        private bool ScansNow(BotBrain brain, GameBot bot, long now)
        {
            if (!Lookers || bot.Group == null)
                return true;
            var ids = new List<long>(8);
            bool fighting = false;
            foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
            {
                if (member is not GameBot mate || !mate.IsAlive || mate.CurrentRegionID != bot.CurrentRegionID)
                    continue;
                ids.Add(mate.DatabaseID);
                fighting |= mate != bot && (mate.InCombat || mate.IsAttacking);
            }
            bool isLooker = ids.Count > RvrScanRoster.LookersPerGroup && RvrScanRoster.IsLooker(bot.DatabaseID, ids, now);
            bool alerted = !isLooker && _memory.HasNews(bot.DatabaseID, now);
            bool planTarget = !isLooker && !alerted && !fighting && brain.PeekRvrPlan()?.CalledTarget != null;
            return RvrScanRoster.Scans(RvrScanRoster.Decide(true, ids.Count, isLooker, alerted, fighting, planTarget));
        }

        private void Scan(BotBrain brain, GameBot bot, long now, double interval)
        {
            int radius = AutonomousRvrPerception.ScanRadius(Profile, true, 5_000);
            var visible = new List<GameLiving>();
            foreach (GamePlayer player in bot.GetPlayersInRadius((ushort)radius))
                if (Enemy(bot, player) && Sees(bot, player))
                    visible.Add(player);
            foreach (GameNPC npc in bot.GetNPCsInRadius((ushort)radius))
                if (npc is GameBot && Enemy(bot, npc) && Sees(bot, npc))
                    visible.Add(npc);
            if (visible.Count == 0)
                return;
            List<GameLiving> engage = Perceive(bot, visible, now, interval);
            if (!Engage || engage.Count == 0)
                return;
            // as the controller orders its scan (nearest first), then picks: the plan in the field, else the spread
            GameLiving[] ordered = engage.OrderBy(bot.GetDistanceTo).ThenBy(t => t.ObjectID).ToArray();
            GameLiving spread = Spread(bot, ordered);
            GameLiving enemy = null;
            string rule = "spread";
            if (AutonomousWorldBotController.FirstPickApplies(bot))
            {
                enemy = AutonomousWorldBotController.PlanFirstPick(bot, ordered, t => t.TargetObject is GameLiving victim &&
                    victim.Realm == bot.Realm && (t.IsAttacking || t.IsCasting), now, out RvrFirstPickReason reason);
                rule = RvrFirstPick.Label(reason);
            }
            if (enemy == null)
            {
                enemy = spread;
                rule = "spread";
            }
            _picks.Add(new RvrFirstPickEvent(now, bot, enemy, rule, spread));
            bot.StopMoving();
            bot.TargetObject = enemy;
            brain.AddToAggroList(enemy, Math.Max(100, enemy.EffectiveLevel * 12));
            if (brain.HasAggro)
                brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
        }

        private static bool Sees(GameBot bot, GameLiving target) =>
            PathfindingProvider.Instance.HasLineOfSight(bot.CurrentZone, new(bot.X, bot.Y, bot.Z), new(target.X, target.Y, target.Z),
                PathfindingProvider.Instance.DefaultFilters);

        /// <summary>
        /// #529 step 3, as the controller's RefreshRvrContactsInCombat: every RvrCombatRefresh.IntervalMilliseconds the
        /// fighting bot looks at the enemy bots within RvrCombatRefresh.Radius it picks (RvrCombatRefresh.Pick: at most
        /// MaxRays rays) and runs the notice rule on those it sees. Contacts and calls only; nothing is engaged.
        /// </summary>
        private void Refresh(GameBot bot, long now)
        {
            if (_nextRefresh.TryGetValue(bot.DatabaseID, out long due) && now < due)
                return;
            _nextRefresh[bot.DatabaseID] = RvrCombatRefresh.NextDue(now, bot.DatabaseID);
            Refreshes++;
            var targets = new List<GameLiving>();
            foreach (GamePlayer player in bot.GetPlayersInRadius((ushort)RvrCombatRefresh.Radius))
                if (Enemy(bot, player))
                    targets.Add(player);
            foreach (GameNPC npc in bot.GetNPCsInRadius((ushort)RvrCombatRefresh.Radius))
                if (npc is GameBot && Enemy(bot, npc))
                    targets.Add(npc);
            targets = targets.OrderBy(t => t.ObjectID).ToList();
            var candidates = targets.Select((t, i) => new RvrRefreshCandidate(i, bot.GetDistanceTo(t), false)).ToList();
            int turn = _refreshTurn.TryGetValue(bot.DatabaseID, out int t0) ? t0 : 0;
            _refreshTurn[bot.DatabaseID] = turn + 1;
            var seen = new List<GameLiving>();
            int rays = 0;
            foreach (int index in RvrCombatRefresh.Pick(candidates, turn))
            {
                rays++;
                if (Sees(bot, targets[index]))
                    seen.Add(targets[index]);
            }
            RefreshRays += rays;
            MaxRaysPerRefresh = Math.Max(MaxRaysPerRefresh, rays);
            if (seen.Count > 0)
                Perceive(bot, seen, now, RvrCombatRefresh.IntervalMilliseconds);
        }

        /// <summary>The notice rule over what one look saw (the scan and the refresh): contacts, notices, calls; returns what may be engaged.</summary>
        private List<GameLiving> Perceive(GameBot bot, List<GameLiving> visible, long now, double interval)
        {
            var engage = new List<GameLiving>();
            foreach (var party in visible.GroupBy(RvrPartyKey.Of).OrderBy(g => g.Key))
            {
                GameLiving[] targets = party.OrderBy(t => t.ObjectID).ToArray();
                var members = targets.Select(t => new RvrPartyMember(RvrPartyKey.TargetKey(t),
                    new RvrSighting(bot.GetDistanceTo(t), t.InCombat, t.IsCasting, t.Group?.MemberCount ?? 1,
                        bot.IsObjectInFront(t, AutonomousRvrPerception.FacingConeDegrees), !t.IsMoving), t.X, t.Y, t.Z)).ToArray();
                var results = new List<RvrMemberOutcome>(members.Length);
                AutonomousRvrPerception.ScanParty(_memory, _observers, bot.DatabaseID, (int)bot.Realm, party.Key, members, now, Profile,
                    interval, _rolls.NextDouble(), () => Reaction(bot), AutonomousWorldBotController.MayAnswerRvrCall(bot), results);
                int primary = -1;
                var noticedKeys = new List<long>();
                var noticedTargets = new List<GameLiving>();
                for (int i = 0; i < results.Count; i++)
                {
                    RvrMemberOutcome outcome = results[i];
                    RvrContactSource source = RvrContacts.SourceOf(outcome.Result, outcome.Factors);
                    if (source != RvrContactSource.None)
                        Note(bot, targets[i], source, now);
                    if (outcome.Result == RvrScanResult.Engage || outcome.Result == RvrScanResult.Noticed && outcome.DelayMilliseconds <= 0)
                        engage.Add(targets[i]);
                    if (outcome.Result == RvrScanResult.Noticed)
                    {
                        noticedKeys.Add(outcome.Key);
                        noticedTargets.Add(targets[i]);
                        if ((outcome.Factors & RvrNoticeFactors.Party) == 0 && primary < 0)
                            primary = i;
                    }
                }
                if (primary < 0)
                    continue;
                RvrMemberOutcome seen = results[primary];
                _notices.Add(new RvrNoticeEvent(now, bot, targets[primary], (int)members[primary].Sighting.Distance, seen.SinceFirstSightMilliseconds,
                    seen.Chance, AutonomousRvrPerception.Describe(seen.Factors), false));
                if (Properties.RVR_BOT_SPOTTERS)
                    Call(bot, noticedKeys, noticedTargets, now);
            }
            return engage;
        }

        private void Call(GameBot spotter, List<long> keys, List<GameLiving> targets, long now)
        {
            var mates = (spotter.Group?.GetMembersInTheGroup() ?? new List<GameLiving>()).OfType<GameBot>()
                .Where(mate => AutonomousWorldBotController.CanHearRvrCall(mate, spotter))
                .ToDictionary(mate => mate.DatabaseID);
            if (mates.Count == 0)
                return;
            var groupMates = mates.Values.Select(mate => new RvrSpotterMate(mate.DatabaseID, mate.X, mate.Y, mate.Z, mate.IsAlive,
                mate.CurrentRegionID == spotter.CurrentRegionID, AutonomousWorldBotController.MayAnswerRvrCall(mate))).ToList();
            foreach (RvrCallee callee in AutonomousRvrPerception.CallRecipients(spotter.DatabaseID, spotter.X, spotter.Y, spotter.Z,
                         groupMates, [], Profile.SpotterRadius, _rolls.NextDouble, out _))
            {
                int delay = callee.ExtraDelayMilliseconds + Reaction(mates[callee.Id]);
                bool news = false;
                foreach (long key in keys)
                    news |= _memory.Alert(callee.Id, key, now, delay);
                if (news)
                    foreach (GameLiving target in targets)
                        Note(mates[callee.Id], target, RvrContactSource.Called, now);
            }
        }

        /// <summary>The spread pick of before 1.2.3 (SelectDistributedRvrTarget without the relic carrier): the bot's id
        /// modulo its party size over the nearest targets.</summary>
        public static GameLiving Spread(GameBot bot, IReadOnlyList<GameLiving> nearestFirst)
        {
            int warband = bot.Group?.GetMembersInTheGroup().OfType<GameBot>().Count() ?? 1;
            long key = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
            return nearestFirst[AutonomousRvrStaging.TargetIndex(key, nearestFirst.Count, warband)];
        }

        private int Reaction(GameBot bot) => AutonomousRvrPerception.ReactionDelayMilliseconds(bot.Level,
            AutonomousBotPersonality.Applies(bot) ? AutonomousBotPersonality.For(bot) : null, _rolls.NextDouble(), Profile);

        private static bool Enemy(GameBot bot, GameLiving target) => target != bot && target.IsAlive &&
            target.ObjectState == GameObject.eObjectState.Active && target.Realm != bot.Realm && target.Realm != eRealm.None &&
            !target.IsStealthed;
    }
}
