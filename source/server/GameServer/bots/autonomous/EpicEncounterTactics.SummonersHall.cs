using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Summoner's Hall's raid rules (1.2, epic wave 2). Grand Summoner Govannon's sacrifice and demon, his demon-form spread
    /// and the Shade of Aelfgar are catalog tactics (hand-notes "aiTactics"): casters and archers kill the walkers before
    /// they reach his altar, and if both get there the whole raid kills the Shade (it sends raiders up to the gallery).
    /// Code rule here: Summoner Lossren's infected ghoul crawls toward one raider and bursts (Plague, 1,000 in 500), so the
    /// raider it crawls toward walks away from it while the casters kill it (a catalog ranged add).
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>The ghoul's target walks away once it is this close (it moves at 110 and bursts at 200).</summary>
        public const int GhoulKiteRadius = 650;

        [DungeonRules]
        private static IEnumerable<DungeonRule> SummonersHallRules() =>
        [
            new("SummonerLossren", ["SummonerLossren", "TorturedSouls", "ExplodeUndead"], LossrenGhoulKite)
            {
                Summary = "The raider the infected ghoul crawls toward walks away from it; casters kill it (ranged add).",
            },
        ];

        // ---- logs: who a spread boss fights, and where its area hit lands (owner 2026-11-08: "log his target first") ----

        private static readonly Dictionary<GameNPC, (GameLiving Target, long Since)> SpreadTargets = new();
        private static readonly HashSet<GameNPC> SpreadCastWatch = new();

        /// <summary>A raider's role for the logs: pet, tank, healer, caster, archer or melee.</summary>
        public static string RoleOf(GameLiving living)
        {
            if (living is not GameBot && living is GameNPC { Brain: DOL.AI.Brain.IControlledBrain }) return "pet"; // a gamebot's BotBrain is a controlled brain too
            int c = EncounterTargets.ClassId(living);
            if (c <= 0) return living is GameNPC ? "npc" : "other";
            var cls = (eCharacterClass)c;
            if (BotPartyRoles.IsHealingClass(cls) && BotPartyRoles.For(cls) != BotPartyRole.Tank) return "healer";
            if (BotPartyRoles.For(cls) == BotPartyRole.Tank) return "tank";
            if (BotRangedCombat.IsDedicatedArcher(cls)) return "archer";
            if (living is GameBot bot && BotSpellPower.IsOffensiveCaster(bot)) return "caster";
            return "melee";
        }

        private static string Who(GameLiving l) =>
            $"\"{l?.Name}\" class={(EncounterTargets.ClassOf(l)?.Name ?? "-")} role={RoleOf(l)} party=\"{l?.Group?.LivingLeader?.Name}\"";

        /// <summary>Logs the boss's target each time it changes while its spread is on (EPIC_SPREAD_TARGET), and once per boss
        /// starts watching its casts for the area hit's landing (EPIC_SPREAD_LANDED).</summary>
        internal static void TraceSpreadTarget(GameNPC boss, GameLiving target, int radius, long now)
        {
            if (boss == null || target == null) return;
            bool watch;
            (GameLiving Target, long Since) last;
            lock (SpreadTargets)
            {
                if (SpreadTargets.TryGetValue(boss, out last) && last.Target == target) return;
                if (SpreadTargets.Count > 64) SpreadTargets.Clear();
                SpreadTargets[boss] = (target, now);
                watch = SpreadCastWatch.Add(boss);
            }
            if (watch)
                DOL.Events.GameEventMgr.AddHandler(boss, DOL.Events.GameLivingEvent.CastFinished, OnSpreadBossCast);
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_SPREAD_TARGET boss=\"{boss.Name}\" health={boss.HealthPercent} target={Who(target)} " +
                         $"dist={(int)boss.GetDistanceTo(target)} prev=\"{last.Target?.Name}\" prevHeldMs={(last.Target == null ? -1 : now - last.Since)}");
        }

        private static void OnSpreadBossCast(DOL.Events.DOLEvent e, object sender, System.EventArgs args)
        {
            if (sender is not GameNPC boss || args is not DOL.Events.CastingEventArgs cast || cast.SpellHandler?.Spell is not Spell spell ||
                For(boss) is not { SpreadRadius: > 0 } t || spell.Radius != t.SpreadRadius || cast.Target is not GameLiving target)
                return;
            var byRole = new SortedDictionary<string, int>();
            var names = new List<string>();
            int within = 0;
            IEnumerable<GameLiving> near = target.GetNPCsInRadius((ushort)spell.Radius).Cast<GameLiving>()
                .Concat(target.GetPlayersInRadius((ushort)spell.Radius));
            foreach (GameLiving l in near.Append(target).Distinct())
            {
                if (l == null || !l.IsAlive || l == boss || l.Realm == boss.Realm) continue;
                within++;
                string role = RoleOf(l);
                byRole[role] = byRole.TryGetValue(role, out int n) ? n + 1 : 1;
                if (names.Count < 24) names.Add($"{l.Name}:{role}:{(int)target.GetDistanceTo(l)}");
            }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_SPREAD_LANDED boss=\"{boss.Name}\" health={boss.HealthPercent} spell=\"{spell.Name}\" radius={spell.Radius} " +
                         $"target={Who(target)} dist={(int)boss.GetDistanceTo(target)} within={within} " +
                         $"byRole={string.Join(",", byRole.Select(kv => kv.Key + ":" + kv.Value))} who=\"{string.Join(" ", names)}\"");
        }

        private static readonly Dictionary<GameNPC, long> LastTauntLog = new();

        /// <summary>One EPIC_TACTIC_TAUNT line per boss every 5 s.</summary>
        private static void LogTaunt(GameBot tank, GameNPC boss, bool spellCast)
        {
            long now = GameLoop.GameLoopTime;
            lock (LastTauntLog)
            {
                if (LastTauntLog.TryGetValue(boss, out long last) && now - last < 5_000) return;
                if (LastTauntLog.Count > 64) LastTauntLog.Clear();
                LastTauntLog[boss] = now;
            }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_TACTIC_TAUNT boss=\"{boss.Name}\" tank={Who(tank)} dist={(int)boss.GetDistanceTo(tank)} " +
                         $"spell={spellCast} style=\"{tank.styleComponent?.NextCombatStyle?.Name}\" his target={Who(boss.TargetObject as GameLiving)}");
        }

        private static Advice? LossrenGhoulKite(RuleContext c)
        {
            foreach (GameNPC ghoul in c.All("ExplodeUndead"))
                if (ghoul.TargetObject == c.Raider && AwayFrom(c.At, PosOf(ghoul), GhoulKiteRadius) is Vector3 away)
                    return new(BotAction.Reposition, c.Target, away, $"the ghoul crawls at me: away from it ({GhoulKiteRadius})", "epic_ghoul_kite");
            return default;
        }
    }
}
