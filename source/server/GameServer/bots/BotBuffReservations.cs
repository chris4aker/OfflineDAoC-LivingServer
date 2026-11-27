using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>Short, group-local in-flight claims, not permanent buff assignments.</summary>
    public sealed class BotBuffReservations<T> where T : class
    {
        private readonly object _gate = new();
        private readonly Dictionary<(T Target, int Family), (T Caster, long Until)> _claims = new();

        public bool IsReserved(T target, int family, long now)
        {
            lock (_gate)
                return _claims.TryGetValue((target, family), out var claim) && claim.Until > now;
        }

        public bool TryReserve(T caster, T target, int family, long now, int castTime)
        {
            lock (_gate)
            {
                foreach (var key in _claims.Where(entry => entry.Value.Until <= now).Select(entry => entry.Key).ToArray())
                    _claims.Remove(key);
                if (_claims.ContainsKey((target, family))) return false;
                _claims[(target, family)] = (caster, now + Math.Clamp((long)castTime + 2000, 2000, 15000));
                return true;
            }
        }

        public void Release(T caster, T target, int family)
        {
            lock (_gate)
                if (_claims.TryGetValue((target, family), out var claim) && ReferenceEquals(claim.Caster, caster))
                    _claims.Remove((target, family));
        }

        public static T Choose(IEnumerable<T> candidates, Func<T, bool> eligible, Func<int, int> roll = null)
        {
            T result = null;
            int count = 0;
            foreach (T candidate in candidates)
                if (candidate != null && eligible(candidate) && (roll ?? Random.Shared.Next)(++count) == 0)
                    result = candidate;
            return result;
        }

        /// <summary>
        /// The eligible candidate of the lowest rank, one picked at random among equals (so two buffers of a group still
        /// spread over a rank). #571 step 4: who a player-group buffer buffs first.
        /// </summary>
        public static T ChooseBest(IEnumerable<T> candidates, Func<T, bool> eligible, Func<T, int> rank, Func<int, int> roll = null)
        {
            T result = null;
            int best = int.MaxValue, count = 0;
            foreach (T candidate in candidates)
            {
                if (candidate == null || !eligible(candidate))
                    continue;
                int r = rank(candidate);
                if (r > best)
                    continue;
                if (r < best)
                {
                    best = r;
                    count = 0;
                }
                if ((roll ?? Random.Shared.Next)(++count) == 0)
                    result = candidate;
            }
            return result;
        }
    }

    /// <summary>
    /// One caster's memory of the expired buffs it noticed (Bots as people WP2, docs/BOTS-AS-PEOPLE-1.2.md, item 2: "Buffs"):
    /// a person does not always renew a buff the moment it runs out. The first time a buff is seen missing on a target the
    /// caster decides once (BotSkillHands.BuffLapse: Casual 25% of expiries stay off 20-90 s, Average 12% for 10-45 s,
    /// Veteran 4% for 5-20 s; a quarter of the rate in a group with a real player; never in a dungeon or with a named
    /// in play, and a running lapse is dropped as soon as that changes or the scope refuses); the answer holds for the lapse and <see cref="DecisionMemoryMilliseconds"/> after, so the maintenance pass
    /// (every 1.75-5 s) does not roll again. Another buffer of the group may renew it meanwhile, as in a real group.
    /// </summary>
    public sealed class BotBuffLapses<T> where T : class
    {
        /// <summary>A decision is kept this long after its lapse ends (the buff is then renewed, not rolled for again).</summary>
        public const long DecisionMemoryMilliseconds = 30_000;
        /// <summary>At most this many decisions are kept (oldest dropped first by the prune).</summary>
        public const int MaxKept = 64;

        private readonly object _gate = new();
        private readonly Dictionary<(T Target, int Spell), (long Decided, long Until)> _decided = new();

        /// <summary>
        /// True while the caster lets this missing buff lapse. <paramref name="decide"/> (called once per expiry, with
        /// <paramref name="state"/>) returns the lapse in ms, 0 to renew at once. While a lapse runs,
        /// <paramref name="stillAllowed"/> is asked at every pass (the scope, a dungeon, a named in play: BotSkillHands.
        /// LapseStillAllowed); false drops the lapse and the buff is renewed now.
        /// </summary>
        public bool Waits<TState>(T target, int spellId, long now, TState state, Func<TState, long> decide,
            Func<TState, bool> stillAllowed = null)
        {
            if (target == null)
                return false;
            bool running = false;
            lock (_gate)
            {
                if (_decided.TryGetValue((target, spellId), out var decision) && now >= decision.Decided &&
                    now < decision.Until + DecisionMemoryMilliseconds)
                {
                    if (now >= decision.Until)
                        return false;
                    running = true;
                }
            }
            if (running)
            {
                if (stillAllowed == null || stillAllowed(state))
                    return true;
                lock (_gate)
                    _decided[(target, spellId)] = (now, now);
                return false;
            }
            long lapse = Math.Max(0, decide(state));
            lock (_gate)
            {
                if (_decided.Count >= MaxKept)
                    Prune(now);
                _decided[(target, spellId)] = (now, now + lapse);
            }
            return lapse > 0;
        }

        public int Count
        {
            get { lock (_gate) return _decided.Count; }
        }

        private void Prune(long now)
        {
            foreach (var key in _decided.Where(entry => now >= entry.Value.Until + DecisionMemoryMilliseconds || now < entry.Value.Decided)
                         .Select(entry => entry.Key).ToArray())
                _decided.Remove(key);
            if (_decided.Count >= MaxKept)
                _decided.Clear();
        }
    }
}
