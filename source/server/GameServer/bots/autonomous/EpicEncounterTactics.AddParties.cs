using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// Spindler Broodmother's newborns, round 3 (owner 2026-11-08: "AoE on her spot + 2-3 newborn-only parties"; no numbers
    /// change). Two catalog fields, only Spindler sets them:
    /// <list type="bullet">
    /// <item><see cref="Tactic.AreaOnClump"/>: a raider with an area damage spell (targeted, point blank or ground) takes as its
    /// target the boss or the ranged add where the most of them stand within that spell's radius (her newborns appear within
    /// 50-80 of her and run to the raid's healers, where they bunch). The shared caster AI (BotBrain.AreaSpells) then sees
    /// two or more in reach and casts the area spell; point blank casters walk in as they already do.</item>
    /// <item><see cref="Tactic.AddParties"/>: 2-3 whole parties of the raid (2 up to 14 parties, 3 above), the last ones in
    /// the raid's order that have a healer, work only the ranged adds: the ones on the raid's healers and casters first,
    /// the one with the least health among those. Their healers keep them up. Everyone else stays on the boss.</item>
    /// </list>
    /// Autonomous gamebots only, through <see cref="Decide"/>.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>How many add parties a raid of <paramref name="parties"/> sends (pure): 2 up to 14 parties, then 3, never
        /// more than <paramref name="max"/> or a third of the raid.</summary>
        public static int AddPartyCount(int parties, int max)
        {
            if (parties <= 1 || max <= 0) return 0;
            int n = parties > 14 ? 3 : 2;
            return Math.Max(1, Math.Min(Math.Min(n, max), parties / 3));
        }

        /// <summary>Which parties (indexes in the raid's order) go on the adds (pure): from the end of the order, parties with a
        /// healer first, then any.</summary>
        public static int[] AddPartyPicks(IReadOnlyList<bool> hasHealer, int count)
        {
            var picks = new List<int>(count);
            for (int i = hasHealer.Count - 1; i >= 0 && picks.Count < count; i--)
                if (hasHealer[i]) picks.Add(i);
            for (int i = hasHealer.Count - 1; i >= 0 && picks.Count < count; i--)
                if (!picks.Contains(i)) picks.Add(i);
            return picks.ToArray();
        }

        private static readonly Dictionary<Group, (bool AddParty, long Tick)> AddPartyCache = new();

        /// <summary>The raider's party is one of its raid's add parties (cached for 10 s a party).</summary>
        public static bool IsAddParty(GameLiving raider, int max, long now)
        {
            Group group = raider?.Group;
            if (group == null) return false;
            lock (AddPartyCache)
                if (AddPartyCache.TryGetValue(group, out var hit) && now - hit.Tick < 10_000) return hit.AddParty;
            bool mine = false;
            if (AutonomousRealmRaid.RaidOf(group) is { } raid)
            {
                bool[] healers = raid.Parties.Select(p => p.GetMembersInTheGroup().Any(m => m.IsAlive && IsHealer(m))).ToArray();
                int[] picks = AddPartyPicks(healers, AddPartyCount(raid.Parties.Length, max));
                mine = Array.IndexOf(picks, Array.IndexOf(raid.Parties, group)) >= 0;
            }
            lock (AddPartyCache)
            {
                if (AddPartyCache.Count > 512) AddPartyCache.Clear();
                AddPartyCache[group] = (mine, now);
            }
            return mine;
        }

        /// <summary>The ranged add an add party takes (pure but for reading the adds): among those on a healer or caster (where
        /// the raid's healers stand), else among all, the one with the least health left.</summary>
        public static GameNPC AddPartyTarget(GameNPC[] adds, Func<GameLiving, bool> isSoft = null, Func<GameNPC, int> healthPercent = null)
        {
            if (adds is not { Length: > 0 }) return null;
            isSoft ??= IsSoftTarget;
            GameNPC[] onSoft = adds.Where(a => a.TargetObject is GameLiving v && isSoft(v)).ToArray();
            return FocusedRangedAdd(onSoft.Length > 0 ? onSoft : adds, healthPercent);
        }

        /// <summary>A clump counts what stands within the spell's radius less this (an edge margin, as BotAoePolicy).</summary>
        public const int ClumpEdgeMargin = 40;

        /// <summary>
        /// The clump centre (pure): among <paramref name="points"/> (index 0 the boss), the one with the most points within
        /// <paramref name="radius"/> - <see cref="ClumpEdgeMargin"/> (flat), ties to the lower index (the boss first). Returns
        /// its index and how many it reaches.
        /// </summary>
        public static (int Index, int Hits) BestClump(IReadOnlyList<Vector3> points, int radius)
        {
            float r = Math.Max(1, radius - ClumpEdgeMargin);
            int best = -1, bestHits = 0;
            for (int i = 0; i < points.Count; i++)
            {
                int hits = 0;
                for (int j = 0; j < points.Count; j++)
                    if (Vector2.Distance(new(points[i].X, points[i].Y), new(points[j].X, points[j].Y)) <= r) hits++;
                if (hits > bestHits) { best = i; bestHits = hits; }
            }
            return (best, bestHits);
        }

        private sealed class AreaKit { public int Radius; public long Tick; }
        private static readonly ConditionalWeakTable<GameBot, AreaKit> AreaKits = new();

        /// <summary>The largest radius of the bot's area damage spells at its level (0: none), read once a minute; an Animist
        /// plants its shrooms at its target, so it counts with at least 350.</summary>
        public static int AreaRadiusOf(GameBot bot, long now)
        {
            if (bot == null) return 0;
            AreaKit kit = AreaKits.GetOrCreateValue(bot);
            if (kit.Tick > 0 && now - kit.Tick < 60_000) return kit.Radius;
            int radius = (bot.HarmfulSpells ?? Enumerable.Empty<Spell>()).Concat(bot.InstantHarmfulSpells ?? Enumerable.Empty<Spell>())
                .Where(s => s != null && s.Radius > 0 && s.Level <= bot.Level && BotCasterPriority.IsDamage(s))
                .Select(s => s.Radius).DefaultIfEmpty(0).Max();
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Animist) radius = Math.Max(radius, 350);
            kit.Radius = Math.Min(radius, 1_000);
            kit.Tick = now;
            return kit.Radius;
        }

        /// <summary>
        /// An area caster's target on an <see cref="Tactic.AreaOnClump"/> boss: the boss or the ranged add where its area spell
        /// reaches the most (two or more). Retarget when that is not its target; "fight on" (None) when it already is; null
        /// when the raider has no area spell or nothing stands two together.
        /// </summary>
        private static Advice? AreaClumpAdvice(GameLiving raider, GameNPC boss, GameNPC[] adds, long now)
        {
            if (raider is not GameBot bot || AreaRadiusOf(bot, now) is not (> 0 and var radius)) return null;
            var all = new List<GameNPC>(adds.Length + 1) { boss };
            all.AddRange(adds.Where(a => a != boss && Present(a)));
            (int index, int hits) = BestClump(all.Select(n => new Vector3(n.X, n.Y, n.Z)).ToArray(), radius);
            if (index < 0 || hits < 2) return null;
            GameNPC centre = all[index];
            if (bot.TargetObject == centre) return new(BotAction.None, centre, null, null);
            return new(BotAction.Retarget, centre, null,
                $"aoe: on the clump at \"{centre.Name}\" ({hits} within {radius - ClumpEdgeMargin})");
        }

        private static readonly Dictionary<GameNPC, long> LastRangedAddsLog = new();

        /// <summary>One EPIC_RANGED_ADDS line per boss every 30 s while its ranged adds live: its health and how many are alive.</summary>
        private static void LogRangedAdds(GameNPC boss, int alive, long now)
        {
            lock (LastRangedAddsLog)
            {
                if (LastRangedAddsLog.TryGetValue(boss, out long last) && now - last < 30_000) return;
                if (LastRangedAddsLog.Count > 64) LastRangedAddsLog.Clear();
                LastRangedAddsLog[boss] = now;
            }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_RANGED_ADDS boss=\"{boss.Name}\" region={boss.CurrentRegionID} health={boss.HealthPercent} alive={alive}");
        }

        public static void ResetAddPartiesForTests()
        {
            lock (AddPartyCache) AddPartyCache.Clear();
            lock (LastRangedAddsLog) LastRangedAddsLog.Clear();
        }
    }
}
