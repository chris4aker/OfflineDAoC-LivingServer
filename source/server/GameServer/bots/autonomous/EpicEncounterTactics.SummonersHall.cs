using System;
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
            // ---- 1.2 big-boss plans (owner 2026-11-14; tests only) ----
            new("SummonersInsideTether", [nameof(SummonerRoesia), nameof(SummonerCunovinda), nameof(SummonerLossren),
                nameof(GrandSummonerGovannon), nameof(TorturedSouls), nameof(ExplodeUndead)], SummonerInsideTether)
            {
                Summary = $"Every summoner takes no damage past her tether (1,300 from her spawn) and resets there: the raid " +
                          $"fights within {SummonerInnerRadius} of her spawn (a raider further out walks in), so whoever she " +
                          "chases keeps her inside it; the one she chases out leads her home (catalog fight at home, 1,000).",
            },
            new("SummonerLossrenGhoul", [nameof(SummonerLossren), nameof(TorturedSouls)], LossrenGhoulFirst)
            {
                Summary = "While the infected ghoul lives every caster and archer shoots it (it bursts for 1,000 in 500 where " +
                          "it reaches); then they clear the tortured souls (catalog ranged adds, area spells on the clumps) " +
                          "while the melee stay on Lossren.",
            },
            new(nameof(GrandSummonerGovannon), [nameof(GrandSummonerGovannon)], GovannonBehindInHisForm)
            {
                Summary = "In his own form (before the demon) the melee fight him from behind (he parries and evades); the " +
                          "demon's ring and Shroud rules (catalog) take over at 50%.",
            },
        ];

        // ---- the summoners' tether (1.2, PC log e3b8238 2026-11-14) ------------------------------------------------------

        /// <summary>The raid stands within this of a summoner's spawn while it fights her (her tether is 1,300, her melee reach
        /// 350; a raider walking in goes to <see cref="SummonerInnerRadius"/> less <see cref="CloseInInset"/>).</summary>
        public const int SummonerInnerRadius = 900;
        /// <summary>A soul or a ghoul this close to a summoner is hers.</summary>
        public const int SummonerAddReach = 3_000;

        private static readonly string[] Summoners =
            [nameof(SummonerRoesia), nameof(SummonerCunovinda), nameof(SummonerLossren), nameof(GrandSummonerGovannon)];

        /// <summary>
        /// The summoners' tether (PC log e3b8238, 2026-11-14, the Hibernia raid at Lossren 13:41-13:48): the muster stood
        /// 1,388 from her spawn (a final muster stands 1,400 off; her tether is 1,300), so she ran out to the raiders who
        /// opened on her from there, was 1,302-1,545 from her spawn within 10 s (AUTONOMOUS_TETHER_RESET 16 times), took
        /// no damage out there and reset to full: 55 engaged, 55 fell, lowest 77%. Now a raider fighting a summoner or her
        /// souls and ghoul that stands further than <see cref="SummonerInnerRadius"/> from her spawn walks in first (her
        /// target is left to the catalog's fight-at-home rule, which leads her home). Govannon's walkers are fought where
        /// they walk (they must die before his altar).
        /// </summary>
        private static Advice? SummonerInsideTether(RuleContext c)
        {
            GameNPC target = c.Target;
            GameNPC summoner = IsSummoner(target) ? target
                : (c.Live ?? []).Where(n => Present(n) && IsSummoner(n) && n.CurrentRegionID == target.CurrentRegionID &&
                                            target.IsWithinRadius(n, SummonerAddReach))
                    .OrderBy(n => n.GetDistanceTo(target)).FirstOrDefault();
            if (summoner == null || summoner.TargetObject == c.Raider) return null;
            Vector3 home = new(summoner.SpawnPoint.X, summoner.SpawnPoint.Y, summoner.SpawnPoint.Z);
            return SummonerStepIn(c.At, home) is Vector3 inside
                ? new(BotAction.Reposition, c.Target, inside,
                    $"summoner-tether: \"{summoner.Name}\" takes no damage past her tether; fight within {SummonerInnerRadius} of her home")
                : null;
        }

        private static bool IsSummoner(GameNPC npc) => Summoners.Any(s => IsOfType(npc, s));

        /// <summary>Where a raider at <paramref name="raider"/> stands to fight a summoner whose home is <paramref name="home"/>
        /// (pure): null within <see cref="SummonerInnerRadius"/>, else that far in less <see cref="CloseInInset"/>, on its side.</summary>
        public static Vector3? SummonerStepIn(Vector3 raider, Vector3 home) => CloseIn(raider, home, SummonerInnerRadius);

        /// <summary>While the infected ghoul lives, casters and archers shoot it (period, ToT Raids 2006: "casters clear the souls
        /// easily. All melee attack Lossren"; the ghoul is the script's).</summary>
        private static Advice? LossrenGhoulFirst(RuleContext c)
        {
            if (!c.CanShootFlyers) return null;
            GameNPC target = c.Target;
            GameLiving raider = c.Raider;
            GameNPC ghoul = (c.Live ?? []).OfType<ExplodeUndead>().Where(n => Present(n) && n.CurrentRegionID == target.CurrentRegionID &&
                                                                             target.IsWithinRadius(n, SummonerAddReach))
                .OrderBy(n => raider.GetDistanceTo(n)).FirstOrDefault();
            return ghoul == null ? null : new Advice(BotAction.Retarget, ghoul, null, "ranged-add: the infected ghoul first (it bursts for 1,000 in 500)");
        }

        /// <summary>Govannon in his own form: melee from behind (period: "He parries, so it's good to keep most attackers behind
        /// him", VN 2003; Allakhazam 2002: "standing behind him is key"). In demon form (model 636) the catalog's ring rules play.</summary>
        private static Advice? GovannonBehindInHisForm(RuleContext c) =>
            c.Target.Model == 636 ? null : MeleeFromBehind(c, "govannon-behind");

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
            var caught = new List<GameLiving>();
            IEnumerable<GameLiving> near = target.GetNPCsInRadius((ushort)spell.Radius).Cast<GameLiving>()
                .Concat(target.GetPlayersInRadius((ushort)spell.Radius));
            foreach (GameLiving l in near.Append(target).Distinct())
            {
                if (l == null || !l.IsAlive || l == boss || l.Realm == boss.Realm) continue;
                within++;
                caught.Add(l);
                string role = RoleOf(l);
                byRole[role] = byRole.TryGetValue(role, out int n) ? n + 1 : 1;
                if (names.Count < 24) names.Add($"{l.Name}:{role}:{(int)target.GetDistanceTo(l)}");
            }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_SPREAD_LANDED boss=\"{boss.Name}\" health={boss.HealthPercent} spell=\"{spell.Name}\" radius={spell.Radius} " +
                         $"target={Who(target)} dist={(int)boss.GetDistanceTo(target)} within={within} " +
                         $"sentOut={SentOutCount(boss, caught)} moving={caught.Count(c => c != target && c.IsMoving)} " +
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
