using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using DOL.AI.Brain;
using DOL.Logging;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #664 step 1, the world read for <see cref="CompanionRvrEngage"/>: the player's group looks as one party on the
    /// AutonomousRvrPerception rules (one memory per group, so what one of them notices the group knows), sizes the fight
    /// up before it opens, picks, and logs COMPANION_RVR_ENGAGE. Called from CompanionPvpEngagement.Observe.
    /// </summary>
    public static partial class CompanionRvrEngage
    {
        private sealed class Party
        {
            public readonly AutonomousRvrPerception.Memory Memory = new();
            public readonly AutonomousRvrPerception.ObserverBudget Observers = new();
            public long NextLook, LastLook;
            public GameLiving Pick;
            public bool Held;
            public readonly List<GameLiving> Ready = new();
            public readonly List<GameLiving> Known = new();
            public string LastCall;
            public GameLiving LastLogged;
        }

        private static readonly ConditionalWeakTable<GamePlayer, Party> Parties = new();
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The perception rolls (0 to 1). Tests may replace it to make the notice curve certain.</summary>
        public static Func<double> Roll = Util.RandomDouble;

        /// <summary>The group pick runs for this player now (the switch, a #663 place).</summary>
        public static bool Applies(GamePlayer leader) =>
            leader != null && Applies(On, AutonomousWorldBotController.IsBattleground(leader));

        /// <summary>
        /// The real player whose group this bot is in: a befriended gamebot's group leader, a /spawn, /raid or /bot
        /// companion's owner, a hired companion's player; null when the bot is not in that player's group.
        /// </summary>
        public static GamePlayer GroupLeader(GameBot bot)
        {
            GamePlayer leader = bot?.PlayerGroupLeader ?? bot?.Owner;
            return leader != null && bot.Group != null && bot.Group == leader.Group &&
                bot.Group.IsInTheGroup(bot) && bot.Group.IsInTheGroup(leader) ? leader : null;
        }

        /// <summary>This bot of <paramref name="leader"/>'s group joins the group pick.</summary>
        public static bool Joins(GameBot bot, GamePlayer leader)
        {
            if (bot == null || leader == null) return false;
            eFSMStateType? state = (bot.Brain as BotBrain)?.FSM?.GetCurrentState()?.StateType;
            // /bot stay puts a /bot companion in IDLE; other bots pass through IDLE in their own work, so only theirs counts.
            bool botCompanion = !bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper;
            return Joins(GroupLeader(bot) == leader, bot.Stance == eBotStance.Passive, state == eFSMStateType.PASSIVE,
                botCompanion && state == eFSMStateType.IDLE);
        }

        /// <summary>
        /// The group's own pick is <paramref name="target"/> and it is within /defensive's bound of the player, so a
        /// /defensive companion (CompanionEngagementMode.Allows) may fight it beyond the PvE 350.
        /// </summary>
        public static bool Engaged(GamePlayer leader, GameLiving target)
        {
            if (target == null || !Applies(leader) || !Parties.TryGetValue(leader, out Party party)) return false;
            lock (party)
                return party.Pick == target && target.CurrentRegionID == leader.CurrentRegionID &&
                    leader.IsWithinRadius(target, DefensiveBound);
        }

        /// <summary>
        /// The group's pick now, or null: looks (at most every <see cref="LookIntervalMilliseconds"/>), then keeps or
        /// changes the pick by the rules. <paramref name="defensive"/> is the player's /defensive.
        /// </summary>
        public static GameLiving Choose(GamePlayer leader, bool defensive)
        {
            if (!Applies(leader) || leader.IsAlive != true || leader.Group == null) return null;
            Party party = Parties.GetOrCreateValue(leader);
            int bound = Bound(defensive);
            long now = GameLoop.GameLoopTime;
            lock (party)
            {
                if (now >= party.NextLook)
                {
                    double interval = party.LastLook > 0 ? now - party.LastLook : LookIntervalMilliseconds;
                    party.LastLook = now;
                    party.NextLook = now + LookIntervalMilliseconds;
                    Look(leader, party, now, interval);
                    Decide(leader, party, bound, defensive);
                }
                else if (party.Pick != null && !StillGood(leader, party.Pick, bound))
                    party.Pick = null;
                return party.Held ? null : party.Pick;
            }
        }

        /// <summary>The group's pick as it stands (null while none, or while it holds off a losing fight).</summary>
        public static GameLiving CurrentPick(GamePlayer leader)
        {
            if (leader == null || !Parties.TryGetValue(leader, out Party party)) return null;
            lock (party) return party.Held ? null : party.Pick;
        }

        /// <summary>The group holds off a fight it would clearly lose (its last look's size-up said so).</summary>
        public static bool Holding(GamePlayer leader)
        {
            if (leader == null || !Parties.TryGetValue(leader, out Party party)) return false;
            lock (party) return party.Held;
        }

        /// <summary>Forget the group's pick and memory (the player changed stance or left).</summary>
        public static void Reset(GamePlayer player)
        {
            if (player != null) Parties.Remove(player);
        }

        private static bool StillGood(GamePlayer leader, GameLiving target, int bound) =>
            target.IsAlive && target.ObjectState == GameObject.eObjectState.Active &&
            target.CurrentRegionID == leader.CurrentRegionID && leader.IsWithinRadius(target, bound) &&
            CompanionPvpEngagement.Enemy(leader, target);

        /// <summary>Who looks: the player and his bots near him, alive, in his region.</summary>
        private static List<GameLiving> Lookers(GamePlayer leader)
        {
            var lookers = new List<GameLiving>(8) { leader };
            foreach (GameLiving member in leader.Group.GetMembersInTheGroup())
                if (member is GameBot bot && bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active &&
                    bot.CurrentRegionID == leader.CurrentRegionID && GroupLeader(bot) == leader &&
                    leader.IsWithinRadius(bot, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS))
                    lookers.Add(bot);
            return lookers;
        }

        private static void Look(GamePlayer leader, Party party, long now, double interval)
        {
            party.Ready.Clear();
            party.Known.Clear();
            RvrBotAwareness awareness = AutonomousRvrPerception.ParseAwareness(Properties.RVR_BOT_AWARENESS);
            RvrAwarenessProfile profile = AutonomousRvrPerception.Profile(awareness);
            List<GameLiving> lookers = Lookers(leader);
            int radius = Math.Min(profile.OuterRange, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS + profile.AlwaysNoticeRadius);
            Func<int> reaction = () => AutonomousRvrPerception.ReactionDelayMilliseconds(leader.Level, null, Roll(), profile);
            var groups = new Dictionary<long, (List<GameLiving> Targets, List<RvrPartyMember> Members)>();
            // The attack rules from a bot's seat, as the /raid scan asks them (SelectRoamer).
            GameLiving attacker = lookers.OfType<GameBot>().FirstOrDefault() ?? (GameLiving)leader;
            foreach (GameNPC npc in leader.GetNPCsInRadius((ushort)radius))
            {
                if (!CompanionPvpEngagement.Enemy(leader, npc) || !npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active ||
                    npc.CurrentRegionID != leader.CurrentRegionID || !GameServer.ServerRules.IsAllowedToAttack(attacker, npc, true))
                    continue;
                long key = RvrPartyKey.TargetKey(npc);
                if (npc.IsStealthed)
                {
                    // The close-range rule only, from the nearest bot that sees it (a call about a stealther is never shared:
                    // only who noticed it can see it, so the group engages only through that bot's eyes).
                    GameBot near = lookers.OfType<GameBot>().Where(bot => bot.IsWithinRadius(npc, RvrStealthNotice.MaxRange))
                        .OrderBy(bot => bot.GetDistanceTo(npc)).FirstOrDefault();
                    if (near == null || !BotSiegeRuntime.Visible(near, npc)) continue;
                    double chance = RvrStealthNotice.ChancePerScan(
                        AutonomousWorldBotController.StealthSighting(near, npc, lineOfSight: true), awareness, interval);
                    RvrScanOutcome outcome = AutonomousRvrPerception.ScanStealthed(party.Memory, 0, key, now, chance, Roll(), reaction);
                    if (outcome.Result is RvrScanResult.Engage || outcome.Result is RvrScanResult.Noticed && outcome.DelayMilliseconds <= 0)
                        party.Ready.Add(npc);
                    if (outcome.Result is RvrScanResult.Engage or RvrScanResult.Noticed or RvrScanResult.Reacting)
                        party.Known.Add(npc);
                    continue;
                }
                // The nearest of the group that has it in line of sight sees it (spotting by groupmates).
                GameLiving eyes = null;
                foreach (GameLiving looker in lookers.OrderBy(l => l.GetDistanceTo(npc)).Take(LookersPerEnemy))
                {
                    if (looker.GetDistanceTo(npc) > profile.OuterRange) break;
                    if (BotSiegeRuntime.Visible(looker, npc)) { eyes = looker; break; }
                }
                if (eyes == null) continue;
                long partyKey = RvrPartyKey.Of(npc);
                if (!groups.TryGetValue(partyKey, out var entry))
                    groups[partyKey] = entry = (new List<GameLiving>(2), new List<RvrPartyMember>(2));
                entry.Targets.Add(npc);
                entry.Members.Add(new RvrPartyMember(key, new RvrSighting(eyes.GetDistanceTo(npc), npc.InCombat, npc.IsCasting,
                    npc.Group?.MemberCount ?? 1, eyes.IsObjectInFront(npc, AutonomousRvrPerception.FacingConeDegrees), !npc.IsMoving),
                    npc.X, npc.Y, npc.Z));
            }
            var results = new List<RvrMemberOutcome>();
            foreach (KeyValuePair<long, (List<GameLiving> Targets, List<RvrPartyMember> Members)> pair in groups)
            {
                var entry = pair.Value;
                results.Clear();
                AutonomousRvrPerception.ScanParty(party.Memory, party.Observers, 0, (int)leader.Realm, pair.Key, entry.Members, now,
                    profile, interval, Roll(), reaction, mayAnswerCall: true, results);
                for (int i = 0; i < results.Count; i++)
                {
                    RvrMemberOutcome outcome = results[i];
                    if (outcome.Result is RvrScanResult.Engage || outcome.Result is RvrScanResult.Noticed && outcome.DelayMilliseconds <= 0)
                        party.Ready.Add(entry.Targets[i]);
                    if (outcome.Result is RvrScanResult.Engage or RvrScanResult.Noticed or RvrScanResult.Reacting)
                        party.Known.Add(entry.Targets[i]);
                }
            }
        }

        private static CompanionRvrCandidate Candidate(GamePlayer leader, GameLiving target, bool ready)
        {
            GameLiving owner = CompanionPvpEngagement.Character(target);
            RvrRole role = owner != target ? RvrRole.Pet :
                owner is GameBot bot && bot.CharacterClass != null ? RvrRoles.For((eCharacterClass)bot.CharacterClass.ID) : RvrRole.Unknown;
            return new CompanionRvrCandidate(Rank(role), ready, BotBrain.IsBreakablyControlled(target),
                target.HealthPercent, (int)leader.GetDistanceTo(target));
        }

        private static void Decide(GamePlayer leader, Party party, int bound, bool defensive)
        {
            if (party.Pick != null && !StillGood(leader, party.Pick, bound)) party.Pick = null;
            var targets = party.Ready;
            var candidates = new List<CompanionRvrCandidate>(targets.Count);
            foreach (GameLiving target in targets)
                candidates.Add(Candidate(leader, target, true));
            int current = party.Pick == null ? -1 : targets.IndexOf(party.Pick);
            int chosen = Pick(candidates, bound, current);
            GameLiving pick = chosen >= 0 ? targets[chosen] : null;
            if (pick == null)
            {
                party.Pick = null;
                party.Held = false;
                return;
            }
            // Opening a fight (no pick yet): size it up. A group already on its pick goes on.
            RvrSightDecision decision = default;
            bool sized = party.Pick == null;
            if (sized)
            {
                decision = SizeUp(Facts(leader, party.Known));
                party.Held = decision.Call == RvrSightCall.BackOff;
                if (party.Held)
                {
                    Note(leader, party, "hold", pick, candidates[chosen], decision, defensive);
                    return;
                }
            }
            bool changed = party.Pick != pick;
            party.Pick = pick;
            party.Held = false;
            if (changed)
                Note(leader, party, "engage", pick, candidates[chosen], decision, defensive, sized);
        }

        /// <summary>The size-up facts: the player and his group within RvrSightSizeUp.AllyRadius against the enemy people the group has noticed.</summary>
        private static RvrSightFacts Facts(GamePlayer leader, List<GameLiving> known)
        {
            int allies = 1, levels = leader.Level;
            foreach (GameLiving member in leader.Group.GetMembersInTheGroup())
                if (member != leader && member.IsAlive && member.CurrentRegionID == leader.CurrentRegionID &&
                    leader.IsWithinRadius(member, RvrSightSizeUp.AllyRadius))
                {
                    allies++;
                    levels += member.Level;
                }
            int enemies = 0, healers = 0, enemyLevels = 0, top = 0;
            foreach (GameLiving enemy in known)
            {
                if (enemy is not GameBot bot) continue; // people only, as #662 counts them
                enemies++;
                enemyLevels += bot.Level;
                top = Math.Max(top, bot.Level);
                if (bot.CharacterClass != null && RvrRoles.For((eCharacterClass)bot.CharacterClass.ID) == RvrRole.Healer) healers++;
            }
            return new RvrSightFacts
            {
                OwnLevel = leader.Level,
                Allies = allies,
                Fighters = allies,
                AllyAverageLevel = levels / allies,
                Enemies = enemies,
                EnemyHealers = healers,
                EnemyAverageLevel = enemies > 0 ? enemyLevels / enemies : 0,
                EnemyTopLevel = top,
            };
        }

        private static void Note(GamePlayer leader, Party party, string call, GameLiving target, in CompanionRvrCandidate candidate,
            in RvrSightDecision decision, bool defensive, bool sized = true)
        {
            if (!Logs(call, target, party.LastCall, party.LastLogged)) return;
            party.LastCall = call;
            party.LastLogged = target;
            if (!Log.IsInfoEnabled) return;
            int size = leader.Group?.MemberCount ?? 1;
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            string odds = sized ? decision.Odds.ToString("0.##", invariant) : "-";
            string bar = sized ? decision.Line.ToString("0.##", invariant) : "-";
            string why = sized ? decision.Reason : "switch";
            string kind = CompanionPvpEngagement.Character(target) == target ? "bot" : "pet";
            string stance = defensive ? "defensive" : "aggressive";
            // The player's group engaged an enemy on its own in RvR, or held off a fight it would clearly lose (#664).
            Log.Info($"COMPANION_RVR_ENGAGE leader=\"{leader.Name}\" call={call} target=\"{target.Name}\" kind={kind} " +
                     $"rank={candidate.Rank} fromPlayer={candidate.FromPlayer} stance={stance} size={size} " +
                     $"known={party.Known.Count} odds={odds} bar={bar} why={why} region={leader.CurrentRegionID}");
        }
    }
}
