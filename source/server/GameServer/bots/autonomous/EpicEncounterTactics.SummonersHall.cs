using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Summoner's Hall's raid rules (1.2, epic wave 2). Grand Summoner Govannon's sacrifice and demon, his demon-form spread
    /// and the Shade of Aelfgar are catalog tactics (hand-notes "aiTactics"): casters and archers kill the walkers before
    /// they reach his altar, and if both get there the whole raid kills the Shade (it sends raiders up to the gallery).
    /// Code rule here: Summoner Lossren's infected ghoul crawls toward one raider and bursts (Plague, 1,000 in 500), so the
    /// raider it crawls toward walks away from it while the casters kill it (a catalog ranged add), and one of her tortured
    /// souls is left alive so that she calls no new wave (and so no new ghoul).
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
            new("SummonerLossrenKeptSoul", [nameof(SummonerLossren), nameof(TorturedSouls), nameof(ExplodeUndead)], LossrenKeptSoulAdvice)
            {
                Summary = "One of her tortured souls is left alive (she calls her next 18-25 only when none of hers lives, and " +
                          "every 20 that die bring an infected ghoul): no bot, pet or area spell hits it, the ranged-add and " +
                          "clump lists skip it, and a raider on it goes back to Lossren. One crowd-control bot of the raid " +
                          "keeps it mesmerized (period, Eden: mesmerize her adds, no area damage), recast before it wakes; " +
                          "failing a mesmerizer a stun, then a root damage doesn't break; with none it is only left alone.",
                LeaveAlive = IsLossrensKeptSoul,
                Control = LossrenKeptSoulHold,
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

        // ---- Lossren's kept soul (1.2, owner's PC log, build 1dc66b6, 2026-11-15) -----------------------------------------

        /// <summary>Each Lossren's kept soul: the one tortured soul of hers the raid leaves alive.</summary>
        private static readonly Dictionary<GameNPC, GameNPC> KeptSouls = new();

        /// <summary>
        /// Lossren's script calls her next wave (18-25 tortured souls) every 5-8 s only while none of hers lives, and every 20
        /// of hers that die bring an infected ghoul (1,000 in 500 where it reaches). Owner's PC, build 1dc66b6, 2026-11-15, the
        /// Hibernia sweep raid: the raid cleared each wave, so she called 14 waves in five minutes (attempt 1) and 18 in ten
        /// (attempt 2); the souls killed 45 of the 73 fallen and 53 of the 97 (up to 21 souls on one raider), she 27 and 38,
        /// and one minute's ghoul bursts hit 80 raiders and pets for 66,748; the melee fell first, the healers trying to
        /// resurrect had 2-6% power from the third minute on, and both attempts ended with nobody left on her ("left combat",
        /// lowest 63% and 20%). Now one soul of hers is left alive: no bot, pet or area spell hits it and a raider on it goes
        /// back to her (a caster or archer to a living ghoul), so after the first wave no new one comes, and at most one ghoul.
        /// The script is unchanged; a soul that dies anyway (a real player's hit) is replaced by one of the next wave.
        /// </summary>
        private static Advice? LossrenKeptSoulAdvice(RuleContext c)
        {
            GameNPC lossren = c.Target is SummonerLossren ? c.Target : LossrenOf(c.Target, c.Live);
            if (lossren == null || !Present(lossren)) return null;
            GameNPC kept = KeepSoul(lossren, c.Live);
            if (kept == null || c.Target != kept) return null;
            // A caster or archer takes the living ghoul (the next rule's choice), everyone else Lossren.
            GameNPC to = LossrenGhoulFirst(c with { Target = lossren })?.Target ?? lossren;
            return new(BotAction.Retarget, to, null,
                $"lossren-kept-soul: \"{kept.Name}\" is left alive (her next wave comes only when none of hers lives); on \"{to.Name}\"");
        }

        /// <summary>The Lossren whose soul or ghoul <paramref name="add"/> is: its spawner, else the nearest within
        /// <see cref="SummonerAddReach"/>.</summary>
        private static GameNPC LossrenOf(GameNPC add, IReadOnlyList<GameNPC> live)
        {
            if (add is TorturedSouls { OwnerBrain.Body: SummonerLossren owner }) return owner;
            return (live ?? []).Where(n => n is SummonerLossren && Present(n) && n.CurrentRegionID == add.CurrentRegionID &&
                                           add.IsWithinRadius(n, SummonerAddReach))
                .OrderBy(n => n.GetDistanceTo(add)).FirstOrDefault();
        }

        /// <summary>Which soul is kept: one with no damage over time on it (a tick would wake it from its mesmerize), then the
        /// healthiest (a monster under 75% resists a mesmerize), ties the lowest object id (every soul of a new wave is whole).</summary>
        public static GameNPC ChooseKeptSoul(IEnumerable<GameNPC> souls) =>
            (souls ?? []).Where(s => s != null)
                .OrderBy(s => s.effectListComponent?.ContainsEffectForEffectType(eEffect.DamageOverTime) == true ? 1 : 0)
                .ThenByDescending(s => s.HealthPercent).ThenBy(s => s.ObjectID).FirstOrDefault();

        /// <summary>
        /// <paramref name="lossren"/>'s kept soul: the one chosen before while it lives, else one of her living souls in
        /// <paramref name="live"/> (<see cref="ChooseKeptSoul"/>), else null. Logs EPIC_LOSSREN_KEPT_SOUL when it chooses.
        /// </summary>
        public static GameNPC KeepSoul(GameNPC lossren, IEnumerable<GameNPC> live)
        {
            if (lossren == null) return null;
            lock (KeptSouls)
                if (KeptSouls.TryGetValue(lossren, out GameNPC kept) && Present(kept)) return kept;
            GameNPC[] hers = (live ?? []).Where(n => n is TorturedSouls soul && Present(n) && n.CurrentRegionID == lossren.CurrentRegionID &&
                                                    lossren.IsWithinRadius(n, SummonerAddReach) &&
                                                    (soul.OwnerBrain?.Body is not GameNPC owner || owner == lossren)).ToArray();
            GameNPC chosen = ChooseKeptSoul(hers);
            lock (KeptSouls)
            {
                if (KeptSouls.TryGetValue(lossren, out GameNPC raced) && Present(raced)) return raced;
                if (chosen == null)
                {
                    KeptSouls.Remove(lossren);
                    return null;
                }
                if (KeptSouls.Count > 16)
                    foreach (GameNPC stale in KeptSouls.Where(p => !Present(p.Key) || !Present(p.Value)).Select(p => p.Key).ToArray())
                        KeptSouls.Remove(stale);
                KeptSouls[lossren] = chosen;
            }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_LOSSREN_KEPT_SOUL boss=\"{lossren.Name}\" region={lossren.CurrentRegionID} health={lossren.HealthPercent} " +
                         $"soul=\"{chosen.Name}\" id={chosen.ObjectID} hers={hers.Length} at={chosen.X},{chosen.Y},{chosen.Z}");
            return chosen;
        }

        /// <summary>The rule's <see cref="DungeonRule.LeaveAlive"/>: <paramref name="npc"/> is a living Lossren's kept soul.</summary>
        private static bool IsLossrensKeptSoul(GameNPC npc)
        {
            if (npc is not TorturedSouls || !Present(npc)) return false;
            lock (KeptSouls)
                foreach (var (lossren, kept) in KeptSouls)
                    if (kept == npc && Present(lossren)) return true;
            return false;
        }

        public static void ResetKeptSoulsForTests()
        {
            lock (KeptSouls) KeptSouls.Clear();
            lock (KeptSoulHolders) KeptSoulHolders.Clear();
        }

        // ---- the kept soul held mesmerized (owner approved 2026-11-15) ---------------------------------------------------

        /// <summary>The hold is cast again when it has at most the spell's cast time and this much left (it must land before
        /// the soul wakes).</summary>
        public const int KeptSoulRecastLeadMilliseconds = 2_000;
        /// <summary>A holder whose tactic turn has not come for this long gives up its place.</summary>
        public const long KeptSoulHolderStaleMilliseconds = 10_000;

        private sealed class SoulHolder
        {
            public GameLiving Bot;
            public int Rank;
            public long Seen;
        }

        /// <summary>Each Lossren's one crowd-control raider, who keeps her kept soul held.</summary>
        private static readonly Dictionary<GameNPC, SoulHolder> KeptSoulHolders = new();

        /// <summary>
        /// How well <paramref name="spell"/> holds the kept soul (pure; lower is better): 0 a single-target mesmerize (the
        /// period's way, Eden: "mesmerize her adds, no area damage"), 1 a stun, 2 a root that damage doesn't break; -1 not a
        /// hold (an area spell, one with damage or a sub spell, a root damage breaks, anything else).
        /// </summary>
        public static int HoldRank(Spell spell)
        {
            if (spell == null || spell.Target != eSpellTarget.ENEMY || spell.Radius > 0 || spell.Range <= 0 || spell.Damage > 0 ||
                spell.SubSpellID > 0)
                return -1;
            return spell.SpellType switch
            {
                eSpellType.Mesmerize or eSpellType.Mez => 0,
                eSpellType.Stun => 1,
                eSpellType.UnbreakableSpeedDecrease when spell.Value >= 99 => 2,
                _ => -1,
            };
        }

        /// <summary>The bot's best hold up to its level (pure): the best <see cref="HoldRank"/>, then the longest, then the
        /// highest level; null when it has none.</summary>
        public static Spell BestHoldSpell(IEnumerable<Spell> spells, int level) =>
            (spells ?? []).Where(s => s != null && s.Level <= level && HoldRank(s) >= 0)
                .OrderBy(HoldRank).ThenByDescending(s => s.Duration).ThenByDescending(s => s.Level).FirstOrDefault();

        /// <summary>The hold is to be cast now (pure): it has <paramref name="heldMilliseconds"/> left (0: not held).</summary>
        public static bool HoldDue(long heldMilliseconds, int castTimeMilliseconds) =>
            heldMilliseconds <= Math.Max(0, castTimeMilliseconds) + KeptSoulRecastLeadMilliseconds;

        /// <summary>How long <paramref name="npc"/> stays held: its mesmerize or stun, and for a root holder its root.</summary>
        private static long HeldMilliseconds(GameNPC npc, int rank)
        {
            long Left(eEffect type) => EffectListService.GetEffectOnTarget(npc, type)?.GetRemainingTimeForClient() ?? 0;
            long held = Math.Max(npc.IsMezzed ? Left(eEffect.Mez) : 0, npc.IsStunned ? Left(eEffect.Stun) : 0);
            return rank == 2 ? Math.Max(held, Left(eEffect.MovementSpeedDebuff)) : held;
        }

        /// <summary>
        /// Whether <paramref name="raider"/>, whose best hold has <paramref name="rank"/>, is <paramref name="lossren"/>'s one
        /// holder now: it already is (its turn keeps the place), there is none fit (none, gone, held itself, or not seen for
        /// <see cref="KeptSoulHolderStaleMilliseconds"/>), or its hold is better (a mesmerizer takes over from a stunner).
        /// Logs EPIC_LOSSREN_SOUL_HOLDER when the holder changes.
        /// </summary>
        public static bool HoldsKeptSoul(GameNPC lossren, GameLiving raider, int rank, long now)
        {
            if (lossren == null || raider == null || rank < 0) return false;
            lock (KeptSoulHolders)
            {
                if (KeptSoulHolders.TryGetValue(lossren, out SoulHolder h) && h.Bot == raider)
                {
                    h.Seen = now;
                    h.Rank = rank;
                    return true;
                }
                bool fit = h != null && h.Bot.IsAlive && h.Bot.ObjectState == GameObject.eObjectState.Active &&
                           h.Bot.CurrentRegionID == lossren.CurrentRegionID && !h.Bot.IsCrowdControlled &&
                           now - h.Seen <= KeptSoulHolderStaleMilliseconds;
                if (fit && h.Rank <= rank) return false;
                if (KeptSoulHolders.Count > 16)
                    foreach (GameNPC stale in KeptSoulHolders.Keys.Where(k => !Present(k)).ToArray())
                        KeptSoulHolders.Remove(stale);
                KeptSoulHolders[lossren] = new SoulHolder { Bot = raider, Rank = rank, Seen = now };
            }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_LOSSREN_SOUL_HOLDER boss=\"{lossren.Name}\" region={lossren.CurrentRegionID} bot=\"{raider.Name}\" " +
                         $"rank={rank} ({(rank == 0 ? "mesmerize" : rank == 1 ? "stun" : "root")})");
            return true;
        }

        /// <summary>
        /// The rule's <see cref="DungeonRule.Control"/> (owner approved 2026-11-15): the raid's one crowd-control bot keeps
        /// Lossren's kept soul held, the period way (Eden: "mesmerize her adds, no area damage"), and never damages it. A
        /// mesmerizer is preferred, failing one a stun, then a root damage doesn't break (<see cref="HoldRank"/>); its hold is
        /// cast again before it ends (<see cref="HoldDue"/>), the holder walking into range first. With no such bot in the
        /// raid the soul is only left alone. True while the holder spends its turn on it (walking, casting).
        /// </summary>
        private static bool LossrenKeptSoulHold(GameBot bot, GameNPC target, IReadOnlyList<GameNPC> live)
        {
            if (bot == null || !bot.IsAlive) return false;
            GameNPC lossren = target is SummonerLossren ? target : LossrenOf(target, live);
            if (lossren == null || !Present(lossren)) return false;
            GameNPC kept = KeepSoul(lossren, live);
            if (kept == null) return false;
            Spell spell = BestHoldSpell(bot.Spells, bot.Level);
            int rank = HoldRank(spell);
            if (rank < 0 || !HoldsKeptSoul(lossren, bot, rank, GameLoop.GameLoopTime)) return false;
            // Casting its hold on the soul: the turn is the cast's (a swing or another spell would cut it).
            if (bot.IsCasting)
                return bot.castingComponent?.SpellHandler is { } cast && HoldRank(cast.Spell) >= 0 && cast.Target == kept;
            if (!HoldDue(HeldMilliseconds(kept, rank), spell.CastTime)) return false;
            if (!bot.IsWithinRadius(kept, Math.Max(1, spell.CalculateEffectiveRange(bot))))
            {
                bot.PathTo(new Point3D(kept.X, kept.Y, kept.Z), bot.MaxSpeed);
                return true;
            }
            return (bot.Brain as BotBrain)?.HoldForEncounter(kept, spell) == true;
        }
    }
}
