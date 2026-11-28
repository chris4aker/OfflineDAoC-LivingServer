using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.Logging;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #664 step 1, the world read for <see cref="CompanionRvrEngage"/>: the player's group looks as one party on the
    /// AutonomousRvrPerception rules (one memory per group, so what one of them notices the group knows), sizes the fight
    /// up before it opens, picks, and logs COMPANION_RVR_ENGAGE. Called from CompanionPvpEngagement.Observe.
    /// Step 2: the pick is kept through a short spell out of sight and let go past the leash; <see cref="MayFight(GameLiving,
    /// GameLiving)"/> is the per-bot gate for /hold and the leash (CompanionEngagementMode.Allows asks it).
    /// </summary>
    public static partial class CompanionRvrEngage
    {
        private sealed class Party
        {
            public readonly AutonomousRvrPerception.Memory Memory = new();
            public readonly AutonomousRvrPerception.ObserverBudget Observers = new();
            public long NextLook, LastLook;
            public GameLiving Pick;
            public long PickSeenAt;
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
            int bound = Bound(defensive), leash = Leash(defensive);
            long now = GameLoop.GameLoopTime;
            lock (party)
            {
                if (now >= party.NextLook)
                {
                    double interval = party.LastLook > 0 ? now - party.LastLook : LookIntervalMilliseconds;
                    party.LastLook = now;
                    party.NextLook = now + LookIntervalMilliseconds;
                    Look(leader, party, now, interval);
                    Decide(leader, party, bound, leash, defensive, now);
                }
                else if (party.Pick != null && !StillGood(leader, party.Pick, leash))
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

        private static bool StillGood(GamePlayer leader, GameLiving target, int leash) =>
            Alive(leader, target) && leader.IsWithinRadius(target, leash);

        private static bool Alive(GamePlayer leader, GameLiving target) =>
            target.IsAlive && target.ObjectState == GameObject.eObjectState.Active &&
            target.CurrentRegionID == leader.CurrentRegionID && CompanionPvpEngagement.Enemy(leader, target);

        /// <summary>The bot, its pet and its owner bot: who "me" is for /hold's self-defence.</summary>
        private static bool HitsUs(GameBot bot, GameLiving actor, GameLiving target)
        {
            if (target == null) return false;
            GameLiving pet = bot.ControlledBrain?.Body;
            return Hits(target, actor) || actor != bot && Hits(target, bot) || pet != null && pet != actor && Hits(target, pet);
        }

        /// <summary><paramref name="enemy"/> is hitting <paramref name="victim"/>: it is among the victim's attackers, or it
        /// has the victim targeted and is attacking or casting.</summary>
        private static bool Hits(GameLiving enemy, GameLiving victim) =>
            victim != null && enemy.IsAlive &&
            (victim.attackComponent?.AttackerTracker?.ContainsAttacker(enemy) == true ||
             enemy.TargetObject == victim && (enemy.IsAttacking || enemy.IsCasting));

        /// <summary>
        /// <paramref name="target"/> is hitting someone of the player's group near him (the player, a bot, a pet of
        /// theirs), or hit one lately (CompanionPvpEngagement.Threatens).
        /// </summary>
        public static bool HittingGroup(GamePlayer leader, GameLiving target)
        {
            if (leader?.Group == null || target == null) return false;
            if (CompanionPvpEngagement.Threatens(leader, target)) return true;
            foreach (GameLiving member in leader.Group.GetMembersInTheGroup())
            {
                if (member == null || member.CurrentRegionID != leader.CurrentRegionID) continue;
                if (Hits(target, member) || member.ControlledBrain?.Body is GameLiving pet && Hits(target, pet)) return true;
            }
            return false;
        }

        /// <summary>Someone of the player's group near him is fighting an enemy-realm foe: attacking or casting at one, or
        /// hit by one (so a new pick is not an opening, <see cref="Opening"/>).</summary>
        public static bool InEnemyFight(GamePlayer leader)
        {
            if (leader?.Group == null) return false;
            foreach (GameLiving member in leader.Group.GetMembersInTheGroup())
            {
                if (member == null || !member.IsAlive || member.CurrentRegionID != leader.CurrentRegionID ||
                    !leader.IsWithinRadius(member, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS))
                    continue;
                if ((member.IsAttacking || member.IsCasting) && member.TargetObject is GameLiving target &&
                    CompanionPvpEngagement.Enemy(leader, target) && target.IsAlive)
                    return true;
                ICollection<GameLiving> attackers = member.attackComponent?.AttackerTracker?.Attackers;
                if (attackers != null)
                    foreach (GameLiving attacker in attackers)
                        if (attacker != null && attacker.IsAlive && CompanionPvpEngagement.Enemy(leader, attacker))
                            return true;
            }
            return false;
        }

        /// <summary>The bot (a pet's owner bot, or the bot itself) whose group rules govern <paramref name="actor"/>.</summary>
        private static GameBot OwnerBot(GameLiving actor)
        {
            for (int depth = 0; depth < 8 && actor != null; depth++)
            {
                if (actor is GameBot bot) return bot;
                actor = (actor as GameNPC)?.Brain is IControlledBrain controlled ? controlled.Owner : null;
            }
            return null;
        }

        /// <summary>The player whose group's /hold or leash governs <paramref name="actor"/> (a bot of his group or its
        /// pet), or null: not in a player's group, or neither /hold nor a #663 place applies.</summary>
        public static GamePlayer Restricting(GameLiving actor)
        {
            GameBot bot = OwnerBot(actor);
            GamePlayer leader = bot == null ? null : GroupLeader(bot);
            return leader != null && (CompanionEngagementMode.PlayerHolding(leader) || Applies(leader)) ? leader : null;
        }

        /// <summary>
        /// Step 2's per-bot gate (<see cref="MayFight(bool, bool, bool, bool, int, int, bool, bool)"/>): /hold, then the
        /// drop leash. True for anything these rules don't cover (a bot outside a player's group, PvE, monsters).
        /// </summary>
        public static bool MayFight(GameLiving actor, GameLiving target)
        {
            if (target == null) return true;
            GameBot bot = OwnerBot(actor);
            GamePlayer leader = bot == null ? null : GroupLeader(bot);
            if (leader == null) return true;
            // A friend (a groupmate a healer targets, a realm guard or merchant) is never what these rules are about.
            if (target.Realm != eRealm.None && target.Realm == bot.Realm) return true;
            if (CompanionEngagementMode.PlayerHolding(leader))
                return MayFight(true, HitsUs(bot, actor, target), false, false, 0, 0, false, false);
            if (!Applies(leader) || !CompanionPvpEngagement.Enemy(leader, target)) return true;
            int leash = Leash(CompanionEngagementMode.PlayerDefensive(leader));
            int fromPlayer = target.CurrentRegionID == leader.CurrentRegionID ? leader.GetDistanceTo(target) : int.MaxValue;
            if (fromPlayer <= leash) return true;
            return MayFight(false, HitsUs(bot, actor, target), true, true, fromPlayer, leash,
                PlayerLedPullCoordinator.FindLeaderTarget(leader) == target, HittingGroup(leader, target));
        }

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

        private static void Decide(GamePlayer leader, Party party, int bound, int leash, bool defensive, long now)
        {
            // Step 2: past the leash the pick is let go (the bots let go of it too: MayFight).
            if (party.Pick != null && !StillGood(leader, party.Pick, leash))
            {
                if (Alive(leader, party.Pick))
                    Drop(leader, party, "leash", defensive);
                party.Pick = null;
            }
            // Never an enemy in a safe or bind area or under hostile guards (review of e0027909): the guards would join.
            var targets = new List<GameLiving>(party.Ready.Count + 1);
            foreach (GameLiving target in party.Ready)
                if (MayOpenOn(InSafeArea(target), UnderHostileGuards(leader, target)))
                    targets.Add(target);
            if (party.Pick != null)
            {
                bool seen = targets.Contains(party.Pick);
                if (seen)
                    party.PickSeenAt = now;
                // Step 2 (review of #765): a pick out of sight for a look or two is kept, not dropped and opened again.
                else if (!party.Pick.IsStealthed && KeepsOutOfSight(false, now, party.PickSeenAt) &&
                         MayOpenOn(InSafeArea(party.Pick), UnderHostileGuards(leader, party.Pick)))
                    targets.Add(party.Pick);
                else
                {
                    Drop(leader, party, "sight", defensive);
                    party.Pick = null;
                }
            }
            var candidates = new List<CompanionRvrCandidate>(targets.Count);
            foreach (GameLiving target in targets)
                candidates.Add(Candidate(leader, target, true));
            int current = party.Pick == null ? -1 : targets.IndexOf(party.Pick);
            // Opening a fight (no pick and nobody fighting): a held enemy is never taken (review of e0027909: mez and run),
            // and the fight is sized up first. Mid-fight a held enemy is taken only when it is the last one left.
            bool opening = Opening(party.Pick != null, InEnemyFight(leader));
            int chosen = Pick(candidates, bound, current, opening: opening, leash: leash);
            GameLiving pick = chosen >= 0 ? targets[chosen] : null;
            if (pick == null)
            {
                party.Pick = null;
                party.Held = false;
                return;
            }
            if (pick != party.Pick)
                party.PickSeenAt = now;
            // Opening a fight: size it up. A group already fighting goes on.
            RvrSightDecision decision = default;
            bool sized = opening;
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

        /// <summary>The enemy stands in a safe or bind area (as the autonomous scan's IsSafeArea).</summary>
        private static bool InSafeArea(GameLiving target) => AutonomousWorldBotController.IsSafeArea(target);

        /// <summary>A live keep or tower guard of a realm hostile to the player covers the enemy (GuardCover of its aggro range).</summary>
        private static bool UnderHostileGuards(GamePlayer leader, GameLiving target)
        {
            foreach (GameNPC npc in target.GetNPCsInRadius((ushort)MaxGuardCover))
                if (npc is GameKeepGuard guard && guard.IsAlive && guard.ObjectState == GameObject.eObjectState.Active &&
                    guard.Realm != leader.Realm && guard.Realm != eRealm.None &&
                    target.IsWithinRadius(guard, GuardCover((guard.Brain as IOldAggressiveBrain)?.AggroRange ?? 0)))
                    return true;
            return false;
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

        /// <summary>The group let its pick go (step 2): <paramref name="why"/> is leash (past the leash) or sight (out of
        /// sight longer than the memory).</summary>
        private static void Drop(GamePlayer leader, Party party, string why, bool defensive)
        {
            GameLiving target = party.Pick;
            if (target == null || !Logs("drop", target, party.LastCall, party.LastLogged)) return;
            party.LastCall = "drop";
            party.LastLogged = target;
            if (!Log.IsInfoEnabled) return;
            int size = leader.Group?.MemberCount ?? 1;
            string kind = CompanionPvpEngagement.Character(target) == target ? "bot" : "pet";
            int fromPlayer = target.CurrentRegionID == leader.CurrentRegionID ? (int)leader.GetDistanceTo(target) : -1;
            // The player's group engaged an enemy on its own in RvR, held off a fight it would clearly lose, or let its pick go (#664).
            Log.Info($"COMPANION_RVR_ENGAGE leader=\"{leader.Name}\" call=drop target=\"{target.Name}\" kind={kind} " +
                     $"rank=- fromPlayer={fromPlayer} stance={(defensive ? "defensive" : "aggressive")} size={size} " +
                     $"known={party.Known.Count} odds=- bar=- why={why} region={leader.CurrentRegionID}");
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
            // The player's group engaged an enemy on its own in RvR, held off a fight it would clearly lose, or let its pick go (#664).
            Log.Info($"COMPANION_RVR_ENGAGE leader=\"{leader.Name}\" call={call} target=\"{target.Name}\" kind={kind} " +
                     $"rank={candidate.Rank} fromPlayer={candidate.FromPlayer} stance={stance} size={size} " +
                     $"known={party.Known.Count} odds={odds} bar={bar} why={why} region={leader.CurrentRegionID}");
        }
    }
}
