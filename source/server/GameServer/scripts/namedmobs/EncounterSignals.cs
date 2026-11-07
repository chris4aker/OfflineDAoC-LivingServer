using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>One mechanic an encounter script has announced (1.2, epic wave 2).</summary>
    /// <param name="Boss">The encounter that does it.</param>
    /// <param name="Mechanic">What it is (<see cref="EncounterSignals"/>' names: glare, stun, breath, stomp, messengers, ...).</param>
    /// <param name="Target">The raider it picked (a glare's), or null for one that hits around the boss.</param>
    /// <param name="Radius">Its area: around <paramref name="Target"/> when <paramref name="AroundTarget"/>, else around the boss.</param>
    /// <param name="At">When the script announced it.</param>
    /// <param name="Lands">When it hits (the announcement's delay or cast time).</param>
    /// <param name="Until">When the raid can stop reacting to it (a little after it lands).</param>
    public sealed record EncounterSignal(GameNPC Boss, string Mechanic, GameLiving Target, int Radius, bool AroundTarget,
        long At, long Lands, long Until)
    {
        /// <summary>The middle of its area now (a glare follows its raider until it lands).</summary>
        public Vector3 Center => AroundTarget && Target != null ? new(Target.X, Target.Y, Target.Z) : new(Boss.X, Boss.Y, Boss.Z);
        public bool ActiveAt(long now) => now >= At && now < Until;
        public bool Landed(long now) => now >= Lands;
    }

    /// <summary>
    /// What an encounter script is about to do, for the raid AI (1.2, epic wave 2). The scripts announced their big hits to
    /// real players only ("Golestandt stares at X and prepares a massive attack", "looks mindfully around", "bellows in
    /// rage"); a bot raid never heard them. Now the script also calls <see cref="Raise"/> with the same facts: what, who, how
    /// far, and when it lands. The raid's tactics read the live signals (EpicEncounterTactics.Signals: step out of a glare's
    /// circle, ranged out of a stun or a breath, healers on the stared raider), the party calls it in /group
    /// (EncounterCalls), and <see cref="Raised"/> lets the lore engine fire the boss's matching atmosphere line
    /// (<see cref="LoreTrigger"/>). Nothing here changes what the script does.
    /// </summary>
    public static class EncounterSignals
    {
        public const string Glare = "glare";
        public const string Stun = "stun";
        public const string Breath = "breath";
        public const string Stomp = "stomp";
        public const string Messengers = "messengers";
        public const string Retreat = "retreat";
        public const string SafeSpots = "safeSpots";

        // Lore-only moments (1.2, the lore-mechanic triggers): said through Moment, never Raise. The raid has nothing to
        // react to in them; only the lore engine listens.
        /// <summary>Suttung goes into his berserker stance.</summary>
        public const string Berserk = "berserk";
        /// <summary>Kvasir's primal scream (his mezz is 2 s away).</summary>
        public const string Scream = "scream";
        /// <summary>A boss heals itself to full from a hit it does not accept (Funus's weapon puzzle).</summary>
        public const string Healed = "healed";
        /// <summary>Damage to one twin heals the other (King Tuscar and Queen Kula).</summary>
        public const string TwinHeal = "twinHeal";

        /// <summary>The same moment of the same boss is said at most once in this long (a twin heal comes with every hit).</summary>
        public const int MomentGapMilliseconds = 5_000;

        private static readonly Dictionary<(GameNPC, string), long> LastMoment = new();

        /// <summary>
        /// A lore-only moment (<see cref="Berserk"/>, <see cref="Scream"/>, <see cref="Healed"/>, <see cref="TwinHeal"/>): the
        /// lore engine's hook (LoreBossWatch). Unlike <see cref="Raised"/> it reaches no tactic and no /group call.
        /// </summary>
        public static event Action<GameNPC, string> Moment;

        /// <summary>The script says a lore-only moment happened. At most once per <see cref="MomentGapMilliseconds"/> per boss and moment.</summary>
        public static bool Note(GameNPC boss, string moment) => Note(boss, moment, GameLoop.GameLoopTime);

        public static bool Note(GameNPC boss, string moment, long now)
        {
            if (boss == null || string.IsNullOrEmpty(moment) || Moment == null) return false;
            lock (Sync)
            {
                if (LastMoment.TryGetValue((boss, moment), out long last) && now - last < MomentGapMilliseconds && now >= last) return false;
                if (LastMoment.Count > 256) LastMoment.Clear();
                LastMoment[(boss, moment)] = now;
            }
            try { Moment?.Invoke(boss, moment); }
            catch (Exception) { /* a listener's fault never breaks the script */ }
            return true;
        }

        /// <summary>The raid keeps reacting this long after a signal lands (a cast that lands a beat late, a stun's pulse).</summary>
        public const int AftermathMilliseconds = 1_500;

        private static readonly object Sync = new();
        private static readonly Dictionary<GameNPC, List<EncounterSignal>> Live = new();

        /// <summary>Every signal as it is raised (the lore engine's hook; tests).</summary>
        public static event Action<EncounterSignal> Raised;

        /// <summary>
        /// The script announces <paramref name="mechanic"/>: it lands in <paramref name="landsInMilliseconds"/> and hits
        /// <paramref name="radius"/> around <paramref name="target"/> (<paramref name="aroundTarget"/>) or around the boss.
        /// <paramref name="holdMilliseconds"/> (0: <see cref="AftermathMilliseconds"/>) is how long after it lands the raid keeps reacting.
        /// </summary>
        public static EncounterSignal Raise(GameNPC boss, string mechanic, GameLiving target, int radius, bool aroundTarget,
            int landsInMilliseconds, int holdMilliseconds = 0) =>
            Raise(boss, mechanic, target, radius, aroundTarget, landsInMilliseconds, holdMilliseconds, GameLoop.GameLoopTime);

        public static EncounterSignal Raise(GameNPC boss, string mechanic, GameLiving target, int radius, bool aroundTarget,
            int landsInMilliseconds, int holdMilliseconds, long now)
        {
            if (boss == null || string.IsNullOrEmpty(mechanic)) return null;
            long lands = now + Math.Max(0, landsInMilliseconds);
            var signal = new EncounterSignal(boss, mechanic, target, Math.Max(0, radius), aroundTarget, now, lands,
                lands + (holdMilliseconds > 0 ? holdMilliseconds : AftermathMilliseconds));
            lock (Sync)
            {
                if (!Live.TryGetValue(boss, out var list)) Live[boss] = list = new List<EncounterSignal>(4);
                list.RemoveAll(s => s.Until <= now || s.Mechanic == mechanic && s.Target == target);
                list.Add(signal);
                if (Live.Count > 64)
                    foreach (GameNPC stale in Live.Where(p => p.Value.All(s => s.Until <= now)).Select(p => p.Key).ToArray())
                        Live.Remove(stale);
            }
            try { Raised?.Invoke(signal); }
            catch (Exception) { /* a listener's fault never breaks the script */ }
            try { EncounterCalls.OnSignal(signal, now); }
            catch (Exception) { /* nor a call-out's */ }
            return signal;
        }

        /// <summary>The boss's signals in force at <paramref name="now"/>.</summary>
        public static EncounterSignal[] Active(GameNPC boss, long now)
        {
            if (boss == null) return [];
            lock (Sync)
            {
                if (!Live.TryGetValue(boss, out var list)) return [];
                list.RemoveAll(s => s.Until <= now);
                return list.Where(s => s.ActiveAt(now)).ToArray();
            }
        }

        public static EncounterSignal Active(GameNPC boss, string mechanic, long now) =>
            Active(boss, now).FirstOrDefault(s => s.Mechanic == mechanic);

        /// <summary>Every signal in force anywhere (healers ask which raider a glare picked).</summary>
        public static EncounterSignal[] AllActive(long now)
        {
            lock (Sync)
            {
                // Asked on every bot's turn: nothing to allocate while no boss has announced anything.
                if (Live.Count == 0) return [];
                var all = new List<EncounterSignal>();
                List<GameNPC> done = null;
                foreach (var (boss, list) in Live)
                {
                    list.RemoveAll(s => s.Until <= now);
                    if (list.Count == 0) (done ??= new()).Add(boss);
                    else all.AddRange(list.Where(s => s.ActiveAt(now)));
                }
                if (done != null) foreach (GameNPC boss in done) Live.Remove(boss);
                return all.ToArray();
            }
        }

        /// <summary>The boss's fight is over (reset, death): its signals go.</summary>
        public static void Clear(GameNPC boss)
        {
            if (boss == null) return;
            lock (Sync) Live.Remove(boss);
        }

        /// <summary>Tests: no signals at all.</summary>
        public static void ClearAll()
        {
            lock (Sync)
            {
                Live.Clear();
                LastMoment.Clear();
            }
        }

        /// <summary>
        /// The lore engine's trigger for a signal or a moment (LoreFightRules.MechanicTriggers, 1.2): the files'
        /// <c>yells.phases</c> and <c>atmosphere.phases</c> use these names. LoreFightRules.SignalTriggers adds the aliases
        /// ("stare" for a glare).
        /// </summary>
        public static string LoreTrigger(string mechanic) => mechanic switch
        {
            Glare => "glare",
            Breath => "breath",
            Stun => "stun",
            Stomp => "stomp",
            Messengers => "messengers",
            Retreat => "retreat",
            SafeSpots => "safeSpots",
            Berserk => "berserk",
            Scream => "scream",
            Healed => "healed",
            TwinHeal => "twinHeal",
            _ => mechanic
        };
    }
}
