using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    // Group-local, short-lived claims. No database work or additional timers.
    public static class BotGroupSupport
    {
        public static bool CanFinishResurrectionBeforeRelease(long castStarted, long deadline, long now, int castTime) =>
            castStarted > 0 && castStarted <= deadline && now >= castStarted &&
            now < castStarted + Math.Clamp((long)castTime + 2000, 2000, 60000);

        public static bool HasCorpseLineOfSight(GameBot caster, GameLiving corpse) =>
            caster.CurrentZone?.IsDungeon != true ||
            !PathfindingProvider.Instance.IsAvailable || !PathfindingProvider.Instance.HasNavmesh(caster.CurrentZone) ||
            PathfindingProvider.Instance.HasLineOfSight(caster.CurrentZone,
                new(caster.X, caster.Y, caster.Z), new(corpse.X, corpse.Y, corpse.Z),
                PathfindingProvider.Instance.DefaultFilters);
        private sealed class State
        {
            public readonly object Sync = new();
            public readonly CompanionRaidResurrectionReservations<GameLiving> Resurrection = new();
            public readonly Dictionary<GameBot, HealClaim> Heals = new();
        }
        private sealed record HealClaim(GameLiving Target, Spell Spell, long Started, long Expires);
        private static readonly ConditionalWeakTable<object, State> States = new();

        public static int CombatResurrectionBudget(int healers, int criticalMembers) =>
            Math.Max(0, Math.Min(healers / 2, healers - Math.Max(1, criticalMembers)));

        public static double ProjectedHealthPercent(int health, int maxHealth, double incoming) =>
            100.0 * (health + Math.Max(0, incoming)) / Math.Max(1, maxHealth);

        private static bool UnderAttack(GameBot bot) => bot.IsBeingInterruptedByOther ||
            GameLoop.GameLoopTime - bot.LastAttackedByEnemyTick < 3000 ||
            bot.attackComponent.AttackerTracker.Attackers.Any(a => a.IsAlive && a.TargetObject == bot);

        public static bool ContinueResurrection(GameBot bot)
        {
            if (bot.Group != null && bot.IsAlive && !bot.IsCrowdControlled && !bot.IsSilenced && !UnderAttack(bot))
                return true;
            bot.StopCurrentSpellcast();
            CancelResurrection(bot);
            return false;
        }

        public static void CancelResurrection(GameBot bot)
        {
            if (bot.Group != null && States.TryGetValue(AutonomousRealmRaid.SupportScope(bot), out State state))
                state.Resurrection.ReleaseCaster(bot);
        }

        /// <summary>How far from the resurrector a hostile counts as a danger to a raised player: they stand where the caster is.</summary>
        public const int RaiseDangerRadius = 1_200;

        /// <summary>
        /// Whether a real player may be raised now. Play test 2026-10-28: a level 50 cleric raised a level 1 player in a
        /// level 25 camp; a leech hit the raised player before the rez damage immunity could heal it back (that effect heals
        /// after the hit, so a killing blow still kills) and he died again in two seconds, twice. Gamebot corpses keep the
        /// combat rez budget; a human player is raised once nothing near the caster is fighting or would aggro. Pure.
        /// </summary>
        public static bool SafeToRaisePlayer(int hostilesFightingNear, int aggressiveHostilesNear) =>
            hostilesFightingNear == 0 && aggressiveHostilesNear == 0;

        /// <summary>The nearest hostile beside the caster that would hit a player raised there (null when it is clear).</summary>
        public static GameNPC ThreatToRaisedPlayer(GameBot bot)
        {
            GameNPC nearest = null;
            int fighting = 0, aggressive = 0;
            foreach (GameNPC npc in bot.GetNPCsInRadius(RaiseDangerRadius))
            {
                if (npc == null || npc == bot || npc is GameBot || !npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active ||
                    (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) != 0 || npc.Realm != eRealm.None ||
                    !GameServer.ServerRules.IsAllowedToAttack(bot, npc, true))
                    continue;
                bool inFight = npc.InCombat;
                bool wouldAggro = npc.Brain is DOL.AI.Brain.StandardMobBrain brain && brain.AggroLevel > 0 && brain.AggroRange > 0 &&
                    npc.IsWithinRadius(bot, brain.AggroRange + 300);
                if (!inFight && !wouldAggro) continue;
                if (inFight) fighting++; else aggressive++;
                if (nearest == null || bot.GetDistanceTo(npc) < bot.GetDistanceTo(nearest)) nearest = npc;
            }
            return SafeToRaisePlayer(fighting, aggressive) ? null : nearest;
        }

        public const int DeclinedRezPauseMs = 60_000;

        /// <summary>Whether a resurrection was declined within the last minute (0: never). Pure.</summary>
        public static bool RecentlyDeclined(long declinedAt, long now) => declinedAt > 0 && now - declinedAt < DeclinedRezPauseMs;

        public static GameLiving ReserveResurrection(GameBot bot, Spell spell)
        {
            if (bot.Group == null || !bot.IsAlive || bot.IsCasting || bot.IsCrowdControlled || bot.IsSilenced || UnderAttack(bot))
                return null;
            int range = bot.castingComponent.CalculateSpellRange(spell);
            // A wounded party elsewhere in the dungeon cannot consume this
            // room's healer budget. The shared reservation still prevents two
            // parties assigning a resurrector to the same corpse.
            var members = AutonomousRealmRaid.SupportMembers(bot)
                .Where(m => m.ObjectState == GameObject.eObjectState.Active &&
                    m.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(m, range)).ToArray();
            bool combat = members.Any(m => m.IsAlive && m.InCombat);
            int healers = members.OfType<GameBot>().Count(b => b.IsAlive && b.CanCastHealSpells &&
                b.CurrentRegionID == bot.CurrentRegionID && !b.IsCrowdControlled && !b.IsSilenced && !UnderAttack(b) && b.ManaPercent >= 20);
            // Keep at least half the available healers healing during combat.
            int critical = members.Count(m => m.IsAlive && m.CurrentRegionID == bot.CurrentRegionID && m.HealthPercent < 35);
            int budget = combat ? CombatResurrectionBudget(healers, critical) : healers;
            // The group can be disbanded by another worker mid-check (overnight run 2026-10-20: a null scope key).
            object scope = AutonomousRealmRaid.SupportScope(bot);
            if (scope == null) return null;
            State state = States.GetOrCreateValue(scope);
            foreach (GameLiving corpse in members.Where(m => !m.IsAlive && m.ObjectState == GameObject.eObjectState.Active &&
                m.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(m, range)).OrderBy(m => m is GamePlayer ? 0 : 1).ThenBy(bot.GetDistanceTo))
            {
                if (!HasCorpseLineOfSight(bot, corpse)) continue;
                if (corpse.TempProperties.GetProperty<GameLiving>("RESURRECT_CASTER") != null) continue;
                // A player who just said no (to release, or to wait for someone else) isn't asked again for a minute.
                if (RecentlyDeclined(corpse.TempProperties.GetProperty<long>(DOL.GS.Spells.ResurrectSpellHandler.RESURRECT_DECLINED_AT_PROPERTY),
                        GameLoop.GameLoopTime)) continue;
                int cost = (int)(bot.MaxMana * Math.Max(.1f, .5f + (corpse.Level - bot.Level) / (float)Math.Max(1, (int)bot.Level)));
                if (bot.Mana < cost) continue;
                if (state.Resurrection.TryReserve(bot, corpse, GameLoop.GameLoopTime, spell.CastTime,
                    true, combat, combat ? budget + 1 : healers, false, bot.CanCastHealSpells)) return corpse;
            }
            return null;
        }

        /// <summary>
        /// Diagnostic only: the first check that stops this bot reserving a resurrection of
        /// <paramref name="corpse"/>. Mirrors ReserveResurrection without reserving anything.
        /// </summary>
        /// <summary>A resurrector with power and a waiting corpse in range pauses chant and song upkeep.</summary>
        public static bool ResurrectionOutranksUpkeep(bool hasResurrection, bool grouped, bool enoughPower, bool corpseWaitingInRange) =>
            hasResurrection && grouped && enoughPower && corpseWaitingInRange;

        public static string DescribeResurrectionBlock(GameBot bot, Spell spell, GameLiving corpse)
        {
            if (bot.IsCasting) return "casting another spell";
            if (bot.IsCrowdControlled || bot.IsSilenced) return "crowd controlled or silenced";
            if (UnderAttack(bot)) return "under attack";
            if (!HasCorpseLineOfSight(bot, corpse)) return "no line of sight to the corpse";
            if (corpse.TempProperties.GetProperty<GameLiving>("RESURRECT_CASTER") != null) return "another resurrection is already pending on the corpse";
            int cost = (int)(bot.MaxMana * Math.Max(.1f, .5f + (corpse.Level - bot.Level) / (float)Math.Max(1, (int)bot.Level)));
            if (bot.Mana < cost) return $"not enough power for the reservation ({bot.Mana}/{cost} of {bot.MaxMana})";
            return "the shared reservation refused (another resurrector holds it, or healers are needed for combat)";
        }

        public static void CancelHeal(GameBot bot)
        {
            if (bot.Group != null && States.TryGetValue(AutonomousRealmRaid.SupportScope(bot), out State state))
                lock (state.Sync) state.Heals.Remove(bot);
        }

        public static GameLiving ReserveHeal(GameBot bot, Spell spell, GameLiving preferred)
        {
            if ((!CompanionRaid.IsMember(bot) && !AutonomousRealmRaid.HasSharedSupport(bot)) || !spell.IsHealing || spell.Target != eSpellTarget.REALM || spell.IsInstantCast || spell.Duration > 0)
                return preferred;
            object scope = AutonomousRealmRaid.SupportScope(bot);
            State state = States.GetOrCreateValue(scope);
            int range = bot.castingComponent.CalculateSpellRange(spell);
            var candidates = AutonomousRealmRaid.SupportMembers(bot).Concat(AutonomousRealmRaid.SupportPets(bot, range)).Concat(
                TemporaryCompanionPetHealing.TriageTargets(bot, range).Cast<GameLiving>()).Distinct().ToArray();
            lock (state.Sync)
            {
                long now = GameLoop.GameLoopTime;
                foreach (var entry in state.Heals.ToArray())
                    if (entry.Value.Expires <= now || !entry.Key.IsAlive || !ReferenceEquals(AutonomousRealmRaid.SupportScope(entry.Key), scope) ||
                        now - entry.Value.Started > 500 && entry.Key.castingComponent.SpellHandler?.Spell != entry.Value.Spell)
                        state.Heals.Remove(entry.Key);
                state.Heals.Remove(bot);
                GameLiving best = null;
                double bestScore = double.MaxValue;
                foreach (GameLiving target in candidates)
                {
                    if (!target.IsAlive || target.CurrentRegionID != bot.CurrentRegionID || !bot.IsWithinRadius(target, range) || target.HealthPercent >= 80) continue;
                    double incoming = state.Heals.Where(p => p.Value.Target == target).Sum(p => Math.Max(0, GameBot.HealAmount(p.Value.Spell, target)));
                    double score = ProjectedHealthPercent(target.Health, target.MaxHealth, incoming);
                    if (score >= 100) continue;
                    if (score < bestScore || score == bestScore && target == preferred) { best = target; bestScore = score; }
                }
                if (best != null)
                    state.Heals[bot] = new(best, spell, now, now + Math.Clamp(spell.CastTime + 500, 1000, 15000));
                return best;
            }
        }
    }
}
