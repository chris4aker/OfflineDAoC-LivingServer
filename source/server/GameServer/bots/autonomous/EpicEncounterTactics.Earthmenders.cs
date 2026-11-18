using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Galladoria's four Earthmenders around the Corrupt Primal of Earth, with the original class puzzle and mutual heal on
    /// (owner 2026-11-18, "Tactic first, then decide"; the boss itself is unchanged).
    /// <para>How the menders heal (OlcasgeanAndPrimals.cs, the four brains' Think; EpicEncounterRestores.EarthmenderHealsTarget):
    /// every 500 ms a mender that is not casting asks for a heal on every NPC in its radius that passes the rule (the Earth
    /// primal, or another living mender below 100%), in the radius list's order. Its casting component starts the first
    /// request and keeps only the last as its queued spell, so each mender heals the first and the last of them, about
    /// 2,000 x 0.6-1.0 each, 2.7 s a cast. A mender that is neither the first nor the last hurt one in a healer's list gets
    /// nothing from it, and a mender never heals itself.</para>
    /// <para>The tactic: (1) the menders' heals are watched (each mender's cast and its target, as a player sees them); the
    /// mender the fewest heals land on is the one to kill, and every raider who hurts all four (animists, valewalkers) goes
    /// there. (2) Its own class group stays on it. (3) The other menders are decoys: half of each decoy's class group keeps
    /// hitting it, so it stays hurt and keeps drawing its healers' first casts, and the other half keeps the Earth primal hurt,
    /// so one of each mender's two casts goes to him. (4) A dead mender's class group fights the primal. With b1b6846's roster
    /// the simulation (UT_EarthmenderTactic) has this kill one mender where the old split killed none, and not all four.</para>
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>The menders in the catalog's order (EpicEncounterRestores.Earthmender's order).</summary>
        public static readonly string[] EarthmenderTypes = ["GuardianEarthmender", "MagicalEarthmender", "NaturalEarthmender", "ShadowyEarthmender"];

        /// <summary>Heals counted this far back when the tactic picks the mender to kill.</summary>
        public const long EarthmenderHealWindowMs = 15_000;
        /// <summary>The mender being killed stays the target until another gets this many fewer heals in the window.</summary>
        public const int EarthmenderSwitchMargin = 2;
        /// <summary>The heal's spell id (the four brains' EarthmenderHeal).</summary>
        public const int EarthmenderHealSpellId = 11722;

        [DungeonRules]
        private static IEnumerable<DungeonRule> GalladoriaEarthmenderRules() =>
        [
            new("EarthmenderHeals", ["EarthPrimal", .. EarthmenderTypes], EarthmendersByHeals)
            {
                Summary = "With the class puzzle and mutual heal on: the raid watches the menders' heals and kills the one the " +
                          "fewest heals land on; animists and valewalkers go there, its class group stays on it. Half of each " +
                          "other mender's class group keeps that mender hurt (it draws its healers' first casts), the other " +
                          "half keeps the Earth primal hurt (one of each mender's two casts goes to him). A dead mender's group " +
                          "fights the primal.",
            },
        ];

        /// <summary>What the tactic knows about one mender.</summary>
        public readonly record struct MenderView(EpicEncounterRestores.Earthmender Kind, bool Alive, int HealthPercent, int HealsSeen);

        /// <summary>
        /// The mender to kill (pure): the living one with the fewest heals seen, then the lowest health, then the catalog's
        /// order; <paramref name="current"/> stays while no other has <see cref="EarthmenderSwitchMargin"/> fewer heals.
        /// Null when none lives.
        /// </summary>
        public static EpicEncounterRestores.Earthmender? EarthmenderToKill(IReadOnlyList<MenderView> menders,
            EpicEncounterRestores.Earthmender? current)
        {
            MenderView[] alive = menders.Where(m => m.Alive).ToArray();
            if (alive.Length == 0) return null;
            MenderView best = alive.OrderBy(m => m.HealsSeen).ThenBy(m => m.HealthPercent).ThenBy(m => (int)m.Kind).First();
            if (current is { } cur && alive.FirstOrDefault(m => m.Kind == cur) is { Alive: true } held &&
                held.HealsSeen - best.HealsSeen < EarthmenderSwitchMargin)
                return cur;
            return best.Kind;
        }

        /// <summary>The menders a class hurts (EpicEncounterRestores.EarthmenderAccepts: the boss's own rule).</summary>
        public static EpicEncounterRestores.Earthmender[] MendersHurtBy(int classId) =>
            Enum.GetValues<EpicEncounterRestores.Earthmender>().Where(k => EpicEncounterRestores.EarthmenderAccepts(k, classId)).ToArray();

        /// <summary>Half of a decoy's class group goes to the primal: a fixed half, by the raider's id.</summary>
        public static bool OnPrimalSquad(int raiderId) => unchecked((uint)raiderId * 2654435761u) >> 31 == 1;

        /// <summary>
        /// One raider's job (pure): the mender it fights, or null for the Earth primal. A class that hurts all four takes
        /// <paramref name="kill"/>; a class that hurts one takes it when it is <paramref name="kill"/>, the primal when it is
        /// dead, and when it is a decoy half the group stays on it and half keeps the primal hurt (only while
        /// <paramref name="primalHurtable"/>: out of his tether he takes no damage, and everyone stays on a mender); a class that
        /// hurts none fights the primal.
        /// </summary>
        public static EpicEncounterRestores.Earthmender? EarthmenderJob(int classId, int raiderId, IReadOnlyList<MenderView> menders,
            EpicEncounterRestores.Earthmender? kill, bool primalHurtable)
        {
            EpicEncounterRestores.Earthmender[] mine = MendersHurtBy(classId).Where(k => menders.Any(m => m.Kind == k && m.Alive)).ToArray();
            if (mine.Length == 0) return null;
            if (kill is { } k0 && mine.Contains(k0)) return k0;
            if (mine.Length > 1) return mine[(int)((uint)raiderId % (uint)mine.Length)];
            return primalHurtable && OnPrimalSquad(raiderId) ? null : mine[0];
        }

        // ---- watching the heals ---------------------------------------------------------------------------------------

        /// <summary>
        /// The heals seen landing on each mender: a heal counts once, when a mender's cast starts (its casting component's
        /// current spell changes to the Earthmender heal), on that spell's target. Keyed by object id; settable time for tests.
        /// </summary>
        public sealed class EarthmenderHealWatch
        {
            private readonly Dictionary<int, object> _lastCast = new();
            private readonly Dictionary<int, List<long>> _heals = new();
            private readonly object _lock = new();

            /// <summary>Healer <paramref name="healer"/>'s current cast is <paramref name="cast"/> (null: none) on
            /// <paramref name="target"/>: counted once per cast.</summary>
            public void Note(int healer, object cast, int target, long now)
            {
                lock (_lock)
                {
                    _lastCast.TryGetValue(healer, out object last);
                    if (ReferenceEquals(last, cast)) return;
                    _lastCast[healer] = cast;
                    if (cast == null || target == 0) return;
                    if (!_heals.TryGetValue(target, out List<long> list)) _heals[target] = list = new();
                    list.Add(now);
                    list.RemoveAll(t => now - t > EarthmenderHealWindowMs);
                }
            }

            /// <summary>Heals that started on <paramref name="target"/> in the last <see cref="EarthmenderHealWindowMs"/>.</summary>
            public int Seen(int target, long now)
            {
                lock (_lock)
                    return _heals.TryGetValue(target, out List<long> list) ? list.Count(t => now - t <= EarthmenderHealWindowMs) : 0;
            }

            public void Clear()
            {
                lock (_lock)
                {
                    _lastCast.Clear();
                    _heals.Clear();
                }
            }
        }

        public static readonly EarthmenderHealWatch EarthmenderHeals = new();
        private static readonly object EarthmenderPlanLock = new();
        private static readonly Dictionary<int, (EpicEncounterRestores.Earthmender? Kill, long Logged, long Sampled)> EarthmenderPlans = new();

        /// <summary>The heal a mender is casting now and its target (its casting component; null when none).</summary>
        public static Func<GameNPC, (object Cast, GameNPC Target)> EarthmenderCast = mender =>
            mender?.castingComponent?.SpellHandler is { } h && h.Spell?.ID == EarthmenderHealSpellId ? (h, h.Target as GameNPC) : (null, null);

        /// <summary>The Earth primal is outside his tether (he takes no damage there). Settable for tests.</summary>
        public static Func<GameNPC, bool> EarthPrimalOutOfTether = AutonomousTetherReset.IsOutOfTether;

        private static Advice? EarthmendersByHeals(RuleContext c)
        {
            if (!EpicEncounterMechanics.AllowEarthmenderClassPuzzle || !EpicEncounterMechanics.AllowEarthmenderMutualHeal || c.ClassId <= 0)
                return null;
            IReadOnlyList<GameNPC> live = c.Live ?? [];
            GameNPC target = c.Target;
            GameNPC primal = IsOfType(target, "EarthPrimal") ? target
                : live.FirstOrDefault(n => Present(n) && IsOfType(n, "EarthPrimal") && n.CurrentRegionID == target.CurrentRegionID &&
                                           target.IsWithinRadius(n, AddSearchRadius));
            if (!Present(primal)) return null;
            var menders = new GameNPC[EarthmenderTypes.Length];
            for (int i = 0; i < menders.Length; i++)
                menders[i] = live.FirstOrDefault(n => Present(n) && IsOfType(n, EarthmenderTypes[i]) && n.CurrentRegionID == primal.CurrentRegionID &&
                                                      primal.IsWithinRadius(n, AddSearchRadius));
            if (menders.All(m => m == null)) return null;
            if (!primal.InCombat && !menders.Any(m => m != null && m.InCombat)) return null;

            long now = c.Now;
            EpicEncounterRestores.Earthmender? kill;
            MenderView[] views;
            lock (EarthmenderPlanLock)
            {
                EarthmenderPlans.TryGetValue(primal.ObjectID, out var plan);
                if (now - plan.Sampled >= 250 || now < plan.Sampled)
                {
                    foreach (GameNPC healer in menders)
                    {
                        if (healer == null) continue;
                        (object cast, GameNPC on) = EarthmenderCast(healer);
                        EarthmenderHeals.Note(healer.ObjectID, cast, on?.ObjectID ?? 0, now);
                    }
                    plan.Sampled = now;
                }
                views = Enumerable.Range(0, menders.Length).Select(i => new MenderView((EpicEncounterRestores.Earthmender)i, menders[i] != null,
                    menders[i]?.HealthPercent ?? 0, menders[i] == null ? 0 : EarthmenderHeals.Seen(menders[i].ObjectID, now))).ToArray();
                kill = EarthmenderToKill(views, plan.Kill);
                if (kill != plan.Kill || now - plan.Logged >= 60_000 || now < plan.Logged)
                {
                    plan.Logged = now;
                    string line = EarthmenderPlanLine(primal.CurrentRegionID, primal.Name, kill is { } k ? menders[(int)k].Name : null, views,
                        !EarthPrimalOutOfTether(primal));
                    try { Log.Info(line); }
                    catch (Exception) { } // a logger that isn't up (the tests)
                }
                plan.Kill = kill;
                EarthmenderPlans[primal.ObjectID] = plan;
            }

            bool primalHurtable = !EarthPrimalOutOfTether(primal);
            EpicEncounterRestores.Earthmender? job = EarthmenderJob(c.ClassId, c.Raider.ObjectID, views, kill, primalHurtable);
            GameNPC to = job is { } j ? menders[(int)j] : primal;
            if (to == null) return null;
            string why = job == null ? "keep the Earth primal hurt: one of each mender's two heals goes to him"
                : job == kill ? $"kill \"{to.Name}\": the fewest heals land on it ({views[(int)job].HealsSeen} in {EarthmenderHealWindowMs / 1000} s)"
                : $"decoy \"{to.Name}\": keep it hurt so its healers' first casts go to it";
            return new(to == target ? BotAction.None : BotAction.Retarget, to, null, $"earthmenders: {why}");
        }

        /// <summary>The plan's log line (pure): written when the mender to kill changes, and once a minute.</summary>
        public static string EarthmenderPlanLine(ushort region, string primal, string kill, IReadOnlyList<MenderView> views, bool primalHurtable) =>
            $"REALM_EXPEDITION_TACTIC region={region} encounter=\"{primal}\" tactic=galladoria/earthmenders kill=\"{kill ?? "none"}\" " +
            $"heals=\"{string.Join(",", views.Select(v => $"{v.Kind}:{(v.Alive ? v.HealsSeen.ToString() : "dead")}"))}\" " +
            $"health=\"{string.Join(",", views.Where(v => v.Alive).Select(v => $"{v.Kind}:{v.HealthPercent}"))}\" " +
            $"assign=\"kill: animists, valewalkers and its own class group; decoys: " +
            (primalHurtable ? "half of each group on its mender, half on the primal" : "each group on its mender (the primal is out of his tether)") +
            "; dead menders' groups: the primal\"";

        /// <summary>Forget the plans and the heals seen (tests).</summary>
        public static void ResetEarthmenderTacticForTests()
        {
            EarthmenderHeals.Clear();
            lock (EarthmenderPlanLock) EarthmenderPlans.Clear();
        }
    }
}
