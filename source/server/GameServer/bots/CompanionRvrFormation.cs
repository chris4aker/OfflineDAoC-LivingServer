using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #665: the player's group spreads into an attack formation once it is fighting enemy bots in the frontier, the
    /// battlegrounds, Darkness Falls and the neutral battle dungeons (FarmVibes: once the enemy is engaged the companions
    /// should spread out instead of staying bunched on the player).
    ///
    /// Owner answers (#665, 2026-11-26 22:23):
    /// - shape: the period backline. Tanks in front; casters behind; healers behind and more than 350 apart;
    /// - bound: the player's position. Companions never stand far from the player (<see cref="PlayerBound"/>);
    /// - kiting: positioning first; hybrid and healer kiting is a later step.
    /// Who: the bots of the player's group, as #664 (CompanionRvrEngage.GroupLeader and Joins). Autonomous gamebot groups
    /// keep their own positioning (#531); PvE is unchanged.
    ///
    /// The shape is RvrPositioning's (one movement engine, #532): the period's "Learn to scatter as soon as you see any
    /// incomming forces" (FH Aug 2003) and "Keep more than 350 units apart" (Uthgard guide 2011), laid against the enemy's
    /// centre with no deliberate mistakes (the player's own group plays it straight), then each spot pulled to within
    /// <see cref="PlayerBound"/> of the player. The walk is the gamebots' proven-leg walk (BotBrain.CompanionFormation).
    /// Pure rules here, the group's read below. Tests only until seen in a PC log.
    /// </summary>
    public static class CompanionRvrFormation
    {
        public const string PropertyKey = "bot_companion_rvr_formation";

        public static bool On => Properties.BOT_COMPANION_RVR_FORMATION;

        /// <summary>No spot is farther than this from the player (owner answer 2): the /defensive bound. A design value.</summary>
        public const int PlayerBound = CompanionRvrEngage.DefensiveBound;

        /// <summary>The skill tier the layout is laid for. Its reaction band delays the walk; its mistakes are off.</summary>
        public const RvrSkillTier Tier = RvrSkillTier.Veteran;

        /// <summary>A member of the group this close to an enemy is in contact (no longer "incoming"). A design value.</summary>
        public const int ContactRange = 500;

        /// <summary>How far around the player the group's members and the enemy's fight are read.</summary>
        public const int ReadRadius = BotBrain.GROUP_DEFENSE_ASSIST_RADIUS;

        /// <summary>The group's read is rebuilt at most this often (one per group, shared by its bots).</summary>
        public const long ReadMilliseconds = 1_000;

        /// <summary>The formation runs for this bot: the switch is on, the player is in a #663 place and the bot joins. Pure.</summary>
        public static bool Applies(bool on, bool warZone, bool joins) => on && warZone && joins;

        /// <summary>The bot walks for a place only from a back stand (casters and healers); tanks and melee fight as now. Pure.</summary>
        public static bool Places(RvrStand stand) => stand == RvrStand.Back;

        /// <summary>A spot pulled onto the circle of <paramref name="bound"/> around the player when it lies outside. Pure.</summary>
        public static Vector2 Clamp(Vector2 spot, Vector2 player, float bound)
        {
            Vector2 offset = spot - player;
            float length = offset.Length();
            return length <= bound || length <= 0 ? spot : player + offset * (bound / length);
        }

        /// <summary>
        /// The group's places: RvrPositioning.Layout against the enemy's centre, with no deliberate mistakes, and every
        /// back spot pulled to within <paramref name="bound"/> of the player. Pure.
        /// </summary>
        public static ImmutableArray<RvrSlot> Layout(IReadOnlyList<RvrPositioning.Placed> members, Vector2 enemy, Vector2 player,
            bool incoming, long now, float bound = PlayerBound)
        {
            ImmutableArray<RvrSlot> slots = RvrPositioning.Layout(members, enemy, Tier, incoming, now, errors: false);
            ImmutableArray<RvrSlot>.Builder clamped = ImmutableArray.CreateBuilder<RvrSlot>(slots.Length);
            foreach (RvrSlot slot in slots)
                clamped.Add(Places(slot.Stand) ? slot with { Spot = Clamp(slot.Spot, player, bound) } : slot);
            return clamped.MoveToImmutable();
        }

        /// <summary>Incoming: the group sees the enemy fight coming but nobody of it is within <see cref="ContactRange"/> of an
        /// enemy yet (the wider scatter). Pure.</summary>
        public static bool Incoming(IReadOnlyList<Vector2> members, IReadOnlyList<Vector2> enemies)
        {
            if (enemies == null || enemies.Count == 0) return false;
            foreach (Vector2 member in members)
                foreach (Vector2 enemy in enemies)
                    if (Vector2.Distance(member, enemy) <= ContactRange)
                        return false;
            return true;
        }

        // ---- the group's read (one per player's group, at most every ReadMilliseconds) ----------------------------------

        /// <summary>The group's fight as one read saw it: the layout, the tanks' line, the front's bearing, incoming, the
        /// player's spot, and each back member with whether it counts as a healer.</summary>
        public sealed class Fight
        {
            public long At;
            public ImmutableArray<RvrSlot> Slots;
            public Vector2? Line;
            public double Front;
            public bool Incoming;
            public Vector2 Player;
            public Vector2 Enemy;
            public int Enemies;
            public readonly List<(GameLiving Member, Vector2 Position, bool Healer)> Backs = new();

            public RvrSlot SlotOf(GameLiving member)
            {
                foreach (RvrSlot slot in Slots)
                    if (ReferenceEquals(slot.Member, member))
                        return slot;
                return default;
            }
        }

        private sealed class Holder
        {
            public Fight Last;
            public long BuiltAt = long.MinValue / 2;
        }

        private static readonly ConditionalWeakTable<GamePlayer, Holder> Holders = new();

        /// <summary>The formation runs for this bot of <paramref name="leader"/>'s group now (switch, place, joins).</summary>
        public static bool Applies(GameBot bot, GamePlayer leader) =>
            bot != null && leader != null &&
            Applies(On, AutonomousWorldBotController.IsBattleground(leader), CompanionRvrEngage.Joins(bot, leader));

        /// <summary>
        /// The group's fight now, or null when it isn't fighting an enemy-realm foe: read at most every
        /// <see cref="ReadMilliseconds"/> and shared by every bot of the group.
        /// </summary>
        public static Fight Read(GamePlayer leader, long now)
        {
            if (leader?.Group == null || !leader.IsAlive || leader.CurrentRegion == null) return null;
            Holder holder = Holders.GetOrCreateValue(leader);
            lock (holder)
            {
                if (now - holder.BuiltAt < ReadMilliseconds) return holder.Last;
                holder.BuiltAt = now;
                holder.Last = Build(leader, now);
                return holder.Last;
            }
        }

        /// <summary>Forget the group's read (tests).</summary>
        public static void Reset(GamePlayer leader)
        {
            if (leader != null) Holders.Remove(leader);
        }

        private static Fight Build(GamePlayer leader, long now)
        {
            // Who stands: the player and the bots of his group near him. Only bots that join are placed; the rest (the
            // player, a passive, /bot stay or /bot hold bot) stand where they are and count for the line.
            var people = new List<GameLiving>(8);
            foreach (GameLiving member in leader.Group.GetMembersInTheGroup())
                if (member != null && member.IsAlive && member.ObjectState == GameObject.eObjectState.Active &&
                    member.CurrentRegionID == leader.CurrentRegionID && leader.IsWithinRadius(member, ReadRadius) &&
                    (member == leader || member is GameBot bot && CompanionRvrEngage.GroupLeader(bot) == leader))
                    people.Add(member);
            if (people.Count < 2) return null;

            // The enemy's fight: what the group is attacking or casting at, what is hitting it, the group's own pick (#664),
            // and the rest of each such enemy's own group.
            var enemies = new HashSet<GameLiving>();
            void Consider(GameLiving living)
            {
                if (living != null && living.IsAlive && !living.IsStealthed && living.ObjectState == GameObject.eObjectState.Active &&
                    living.CurrentRegionID == leader.CurrentRegionID && CompanionPvpEngagement.Enemy(leader, living) &&
                    leader.IsWithinRadius(living, ReadRadius))
                    enemies.Add(living);
            }
            foreach (GameLiving member in people)
            {
                if ((member.IsAttacking || member.IsCasting) && member.TargetObject is GameLiving target)
                    Consider(target);
                ICollection<GameLiving> attackers = member.attackComponent?.AttackerTracker?.Attackers;
                if (attackers != null)
                    foreach (GameLiving attacker in attackers)
                        Consider(attacker);
            }
            Consider(CompanionRvrEngage.CurrentPick(leader));
            if (enemies.Count == 0) return null;
            foreach (GameLiving enemy in new List<GameLiving>(enemies))
                if (CompanionPvpEngagement.Character(enemy)?.Group is Group theirs)
                    foreach (GameLiving mate in theirs.GetMembersInTheGroup())
                        Consider(mate);

            var placed = new List<RvrPositioning.Placed>(people.Count);
            var positions = new List<(Vector2, RvrStand)>(people.Count);
            var memberPoints = new List<Vector2>(people.Count);
            foreach (GameLiving member in people)
            {
                eCharacterClass characterClass = member switch
                {
                    GameBot { CharacterClass: not null } bot => (eCharacterClass)bot.CharacterClass.ID,
                    GamePlayer { CharacterClass: not null } player => (eCharacterClass)player.CharacterClass.ID,
                    _ => eCharacterClass.Unknown,
                };
                RvrRole role = RvrRoles.For(characterClass);
                RvrStand stand = RvrPositioning.StandFor(role, characterClass);
                // A bot that stays out keeps its spot: it is not laid on the arc (it still counts for the tanks' line).
                if (Places(stand) && (member is not GameBot bot || !CompanionRvrEngage.Joins(bot, leader)))
                    stand = RvrStand.None;
                var combatant = new RvrCombatant
                {
                    Living = member, Key = RvrPartyKey.TargetKey(member), Realm = member.Realm, Class = characterClass,
                    Role = role, IsPlayer = member is GamePlayer, Level = member.Level, HealthPercent = member.HealthPercent,
                    X = member.X, Y = member.Y, Z = member.Z,
                };
                placed.Add(new RvrPositioning.Placed(combatant, stand));
                positions.Add((new Vector2(member.X, member.Y), stand));
                memberPoints.Add(new Vector2(member.X, member.Y));
            }

            Vector2 centre = Vector2.Zero;
            var enemyPoints = new List<Vector2>(enemies.Count);
            foreach (GameLiving enemy in enemies)
            {
                var point = new Vector2(enemy.X, enemy.Y);
                enemyPoints.Add(point);
                centre += point;
            }
            centre /= enemies.Count;
            var playerPoint = new Vector2(leader.X, leader.Y);
            bool incoming = Incoming(memberPoints, enemyPoints);
            var fight = new Fight
            {
                At = now,
                Slots = Layout(placed, centre, playerPoint, incoming, now),
                Line = RvrPositioning.Line(positions),
                Incoming = incoming,
                Player = playerPoint,
                Enemy = centre,
                Enemies = enemies.Count,
            };
            Vector2 groupCentre = Vector2.Zero;
            foreach (Vector2 point in memberPoints) groupCentre += point;
            groupCentre /= memberPoints.Count;
            fight.Front = RvrPositioning.Bearing(fight.Line ?? groupCentre, centre);
            foreach (RvrSlot slot in fight.Slots)
                if (Places(slot.Stand))
                    fight.Backs.Add((slot.Member, new Vector2(slot.Member.X, slot.Member.Y), slot.Healer));
            return fight;
        }
    }
}
