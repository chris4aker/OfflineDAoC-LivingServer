using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Raid tactics for epic encounters, fed by the encounter catalog (docs/encounters, hand-notes.json "aiTactics", written
    /// into <see cref="Catalog"/> by source/tools/epic_encounter_catalog.py). Three generic rules, no boss is named here:
    /// <list type="bullet">
    /// <item><b>Required adds</b>: a boss that takes no damage while its adds live (Scurceol Hyrde's four Lyft Miht orbs). The
    /// raid's target is one of the adds while any lives; each party takes the add its index points at; nobody hits the boss
    /// until the last add is dead; a raider who can't reach its add (melee under a flying orb) stands off the boss.</item>
    /// <item><b>Fight at home</b>: a boss fought only within a radius of its spawn (Black Lady, 1,100 inside her 1,500 tether).
    /// Past it the raid stops hitting it and whoever it chases leads it home, before its tether resets it.</item>
    /// <item><b>Dies with</b>: an encounter that dies when another does (the Ancient Black Oak with Black Lady). The raid
    /// turns on the other one instead.</item>
    /// </list>
    /// Autonomous gamebots only: companion bots and player-led groups are not steered.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(EpicEncounterTactics));

        /// <param name="Boss">The encounter's class name.</param>
        /// <param name="Encounter">Its catalog id.</param>
        /// <param name="RequiredAdds">Class names of the adds that shield it (empty: none).</param>
        /// <param name="FightAtHomeRadius">Fight it only this close to its spawn (0: no limit).</param>
        /// <param name="DiesWith">Class name of the encounter whose death kills this one (null: none).</param>
        public sealed record Tactic(string Boss, string Encounter, string[] RequiredAdds, int FightAtHomeRadius, string DiesWith)
        {
            public bool HasRequiredAdds => RequiredAdds is { Length: > 0 };
        }

        private static readonly Dictionary<string, Tactic> Extra = new(StringComparer.Ordinal);
        private static Dictionary<string, Tactic> _byType;

        private static Dictionary<string, Tactic> ByType
        {
            get
            {
                if (_byType != null) return _byType;
                var map = new Dictionary<string, Tactic>(StringComparer.Ordinal);
                foreach (Tactic t in Catalog) map[t.Boss] = t;
                foreach (var kv in Extra) map[kv.Key] = kv.Value;
                return _byType = map;
            }
        }

        /// <summary>The catalog's tactics (generated) plus any added by <see cref="Register"/>.</summary>
        public static IReadOnlyCollection<Tactic> All => ByType.Values;

        /// <summary>Adds a tactic for a class (tests; a later step may load more).</summary>
        public static void Register(Tactic tactic)
        {
            Extra[tactic.Boss] = tactic;
            _byType = null;
        }

        public static void Unregister(string boss)
        {
            Extra.Remove(boss);
            _byType = null;
        }

        public static Tactic For(GameNPC npc) => npc == null ? null : ByType.GetValueOrDefault(npc.GetType().Name);

        /// <summary>Within this distance of the boss an add counts as its (two copies of a dungeon never share a region,
        /// but a boss's adds can sit at the corners of a large room).</summary>
        public const int AddSearchRadius = 6_000;

        private static bool Present(GameNPC npc) => npc != null && npc.IsAlive && npc.ObjectState == GameObject.eObjectState.Active;

        /// <summary>The living adds that shield <paramref name="boss"/>, in the catalog's order (a stable order, so every
        /// party's index points at the same add while they live).</summary>
        public static GameNPC[] LiveRequiredAdds(GameNPC boss, IEnumerable<GameNPC> live)
        {
            Tactic t = For(boss);
            if (t is not { HasRequiredAdds: true } || !Present(boss)) return [];
            return live.Where(n => Present(n) && n != boss && n.CurrentRegionID == boss.CurrentRegionID &&
                                   Array.IndexOf(t.RequiredAdds, n.GetType().Name) >= 0 &&
                                   boss.IsWithinRadius(n, AddSearchRadius))
                .OrderBy(n => Array.IndexOf(t.RequiredAdds, n.GetType().Name)).ThenBy(n => n.ObjectID).ToArray();
        }

        /// <summary>The boss whose death kills <paramref name="npc"/>, while it lives.</summary>
        public static GameNPC DiesWithOwner(GameNPC npc, IEnumerable<GameNPC> live)
        {
            Tactic t = For(npc);
            if (t?.DiesWith == null || !Present(npc)) return null;
            return live.FirstOrDefault(n => Present(n) && n.CurrentRegionID == npc.CurrentRegionID && n.GetType().Name == t.DiesWith);
        }

        /// <summary>Which add a party takes: its index modulo the adds alive.</summary>
        public static int AssignedIndex(int partyIndex, int liveAdds) => liveAdds <= 0 ? -1 : (int)((uint)partyIndex % (uint)liveAdds);

        /// <summary>The boss is further from its spawn than its tactic allows.</summary>
        public static bool OutsideHome(int fightAtHomeRadius, double distanceFromSpawn) =>
            fightAtHomeRadius > 0 && distanceFromSpawn > fightAtHomeRadius;

        public static bool OutsideHome(GameNPC npc) => For(npc) is { } t && Present(npc) &&
            OutsideHome(t.FightAtHomeRadius, npc.GetDistanceTo(npc.SpawnPoint));

        /// <summary>What the raid route fights instead of <paramref name="chosen"/>: the boss it dies with, or the add that
        /// shields it nearest the raid's front; <paramref name="chosen"/> itself when no tactic applies.</summary>
        public static GameNPC RouteTarget(GameNPC chosen, IReadOnlyList<GameNPC> live, Vector3 front, out string reason)
        {
            reason = null;
            if (!Present(chosen)) return chosen;
            if (DiesWithOwner(chosen, live) is { } owner)
            {
                reason = $"dies-with \"{owner.Name}\"";
                chosen = owner;
            }
            GameNPC[] adds = LiveRequiredAdds(chosen, live);
            if (adds.Length == 0) return chosen;
            GameNPC nearest = adds.OrderBy(a => Vector3.DistanceSquared(front, new Vector3(a.X, a.Y, a.Z))).First();
            reason = (reason == null ? "" : reason + "; ") + $"required-adds of \"{chosen.Name}\" alive={adds.Length}";
            return nearest;
        }

        /// <summary>Fighting <paramref name="current"/> is how the raid fights <paramref name="encounter"/>: it is one of its
        /// living required adds, or the boss it dies with.</summary>
        public static bool Covers(GameNPC encounter, GameNPC current, IReadOnlyList<GameNPC> live) =>
            current != null && For(encounter) != null &&
            (DiesWithOwner(encounter, live) == current || Array.IndexOf(LiveRequiredAdds(encounter, live), current) >= 0);

        /// <summary>True when <paramref name="npc"/> is one of the required adds of a living boss (the route fights it as
        /// part of that boss's fight: no muster of its own).</summary>
        public static bool IsRequiredAddOfLiveBoss(GameNPC npc, IReadOnlyList<GameNPC> live) =>
            Present(npc) && live.Any(b => Present(b) && For(b) is { HasRequiredAdds: true } t &&
                                          Array.IndexOf(t.RequiredAdds, npc.GetType().Name) >= 0 &&
                                          b.CurrentRegionID == npc.CurrentRegionID && b.IsWithinRadius(npc, AddSearchRadius));

        public enum BotAction
        {
            /// <summary>No tactic: fight as usual.</summary>
            None,
            /// <summary>Attack another encounter instead (<see cref="Advice.Target"/>).</summary>
            Retarget,
            /// <summary>Don't hit this target; stand at <see cref="Advice.StandAt"/> (under its assigned add) if given.</summary>
            StandOff,
            /// <summary>Don't hit it; it is chasing this raider, who leads it back to its spawn.</summary>
            LeadHome
        }

        public readonly record struct Advice(BotAction Action, GameNPC Target, Vector3? StandAt, string Reason);

        /// <summary>
        /// The tactic for one raider about to attack <paramref name="target"/>. <paramref name="partyIndex"/> spreads the
        /// parties over the required adds; <paramref name="canShootFlyers"/> says whether this raider can hit an add in the air.
        /// </summary>
        public static Advice Decide(GameLiving raider, GameNPC target, IReadOnlyList<GameNPC> live, int partyIndex, bool canShootFlyers)
        {
            if (raider == null || !Present(target)) return default;
            if (DiesWithOwner(target, live) is { } owner)
                return new(BotAction.Retarget, owner, null, $"dies-with \"{owner.Name}\"");
            GameNPC[] adds = LiveRequiredAdds(target, live);
            if (adds.Length > 0)
            {
                GameNPC add = adds[AssignedIndex(partyIndex, adds.Length)];
                bool flying = (add.Flags & GameNPC.eFlags.FLYING) != 0 &&
                              AutonomousPveTargetPolicy.IsUnreachableFlyer(add.Flags, add.Z, raider.Z);
                if (!flying || canShootFlyers)
                    return new(BotAction.Retarget, add, null, $"required-add {Array.IndexOf(adds, add) + 1}/{adds.Length} of \"{target.Name}\"");
                return new(BotAction.StandOff, add, new Vector3(add.X, add.Y, raider.Z),
                    $"required-add {Array.IndexOf(adds, add) + 1}/{adds.Length} of \"{target.Name}\" is out of melee reach");
            }
            if (OutsideHome(target) && !AutonomousTetherReset.IsOutOfTether(target))
                return new(target.TargetObject == raider ? BotAction.LeadHome : BotAction.StandOff, target, null,
                    $"fight-at-home {(int)target.GetDistanceTo(target.SpawnPoint)}>{For(target).FightAtHomeRadius}");
            return default;
        }

        public static bool CanShootFlyers(GameBot bot)
        {
            if (bot?.CharacterClass == null) return false;
            var c = (eCharacterClass)bot.CharacterClass.ID;
            return BotSpellPower.IsOffensiveCaster(bot) || BotRangedCombat.IsDedicatedArcher(c);
        }

        /// <summary>The party's index for <see cref="AssignedIndex"/>: its leader's object id (stable while it lives).</summary>
        public static int PartyIndex(GameLiving member) => member?.Group?.LivingLeader?.ObjectID ?? member?.ObjectID ?? 0;

        private static readonly Dictionary<GameNPC, (long Tick, List<GameNPC> Npcs)> NearbyCache = new();

        /// <summary>The NPCs around a tactic's encounter, looked up at most once a second per encounter (a raid of 200 asks
        /// on every brain turn).</summary>
        private static IReadOnlyList<GameNPC> Nearby(GameNPC target)
        {
            long now = GameLoop.GameLoopTime;
            lock (NearbyCache)
            {
                if (NearbyCache.TryGetValue(target, out var hit) && now - hit.Tick < 1_000) return hit.Npcs;
            }
            List<GameNPC> npcs = target.GetNPCsInRadius((ushort)AddSearchRadius).ToList();
            if (!npcs.Contains(target)) npcs.Add(target);
            lock (NearbyCache)
            {
                if (NearbyCache.Count > 64) NearbyCache.Clear();
                NearbyCache[target] = (now, npcs);
            }
            return npcs;
        }

        private static long _nextLog;
        private static readonly Dictionary<(int, string), long> LastDecision = new();

        /// <summary>
        /// BotBrain's hook, right after it picks <paramref name="target"/>: true when this turn is used (standing off or
        /// leading home); false to fight on, possibly at a new <see cref="GameObject.TargetObject"/>.
        /// </summary>
        public static bool TryHandle(GameBot bot, GameNPC target)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup || For(target) == null) return false;
            IReadOnlyList<GameNPC> live = Nearby(target);
            Advice advice = Decide(bot, target, live, PartyIndex(bot), CanShootFlyers(bot));
            switch (advice.Action)
            {
                case BotAction.Retarget:
                    bot.TargetObject = advice.Target;
                    (bot.Brain as BotBrain)?.AddToAggroList(advice.Target, 1_000);
                    LogDecision(bot, target, advice);
                    return false;
                case BotAction.StandOff:
                case BotAction.LeadHome:
                    if (bot.IsCasting && bot.castingComponent?.SpellHandler?.Spell?.IsHarmful == true) bot.StopCurrentSpellcast();
                    bot.StopAttack();
                    (bot.Brain as BotBrain)?.RemoveFromAggroList(target);
                    bot.TargetObject = null;
                    if (bot.ControlledBrain?.Body?.TargetObject == target) bot.ControlledBrain.Disengage();
                    if (advice.Action == BotAction.LeadHome)
                        bot.PathTo(new Point3D(target.SpawnPoint.X, target.SpawnPoint.Y, target.SpawnPoint.Z), bot.MaxSpeed);
                    else if (advice.StandAt is Vector3 at && !bot.IsWithinRadius(new Point3D((int)at.X, (int)at.Y, (int)at.Z), 300))
                        bot.PathTo(new Point3D((int)at.X, (int)at.Y, (int)at.Z), bot.MaxSpeed);
                    LogDecision(bot, target, advice);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>One line per bot and decision kind a minute, and at most one a second overall.</summary>
        private static void LogDecision(GameBot bot, GameNPC target, Advice advice)
        {
            long now = GameLoop.GameLoopTime;
            var key = (bot.ObjectID, advice.Action + advice.Target?.Name);
            lock (LastDecision)
            {
                if (LastDecision.TryGetValue(key, out long last) && now - last < 60_000) return;
                if (now < _nextLog) return;
                LastDecision[key] = now;
                _nextLog = now + 1_000;
                if (LastDecision.Count > 4_000) LastDecision.Clear();
            }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_TACTIC_BOT bot={bot.Name} region={target.CurrentRegionID} target=\"{target.Name}\" " +
                         $"action={advice.Action} to=\"{advice.Target?.Name}\" reason=\"{advice.Reason}\" " +
                         $"encounter={For(target)?.Encounter}");
        }
    }
}
