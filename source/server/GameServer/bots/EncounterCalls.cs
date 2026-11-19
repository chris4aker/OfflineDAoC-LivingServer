using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// The party's call for a boss mechanic (1.2, epic wave 2): when a script raises a signal (EncounterSignals), a bot in
    /// each party that has the real player in it says what to do in /group, in the 2003 voice ("{target} has the stare, get
    /// off him", "stun inc, casters back", "runners! kill the messengers"). Lines: combat_calls.lines, topics
    /// <c>epic_&lt;mechanic&gt;</c> with slots {target} and {boss}. One call per party and mechanic every
    /// <see cref="GapMilliseconds"/>. /spawn companions don't talk (TryLine needs an autonomous gamebot); a gamebot groupmate does.
    /// </summary>
    public static class EncounterCalls
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(EncounterCalls));

        public const int GapMilliseconds = 8_000;
        /// <summary>Parties whose real player is this close to the boss hear the call.</summary>
        public const int CallRadius = 6_000;

        private static readonly ConcurrentDictionary<(Group, string), long> Last = new();

        /// <summary>The corpus topic of a mechanic's call.</summary>
        public static string Topic(string mechanic) => "epic_" + mechanic;

        /// <summary>A party may call this mechanic again (pure; <paramref name="last"/> -1: never called).</summary>
        public static bool Due(long last, long now) => last < 0 || now - last >= GapMilliseconds;

        internal static void OnSignal(EncounterSignal signal, long now)
        {
            if (signal?.Boss == null || !AutonomousBotVoices.Enabled) return;
            IEnumerable<GamePlayer> players;
            try { players = signal.Boss.GetPlayersInRadius(CallRadius).OfType<GamePlayer>().ToArray(); }
            catch (Exception) { return; }
            foreach (Group group in players.Select(p => p.Group).Where(g => g != null).Distinct())
                Say(group, signal, now);
        }

        private static void Say(Group group, EncounterSignal signal, long now)
        {
            try
            {
                if (!AutonomousLivingServer.GroupHasPlayer(group)) return;
                var key = (group, signal.Mechanic);
                if (!Due(Last.TryGetValue(key, out long last) ? last : -1, now)) return;
                GameBot speaker = Speaker(group, signal.Target);
                if (speaker == null) return;
                var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["target"] = signal.Target?.Name ?? "someone",
                    ["boss"] = signal.Boss.Name,
                };
                if (!AutonomousBotVoices.TryLine(speaker, "combat", out AutonomousBotVoiceCorpus.Picked picked, topic: Topic(signal.Mechanic),
                        slots: slots, relax: true))
                    return;
                Last[key] = now;
                if (Last.Count > 512) Last.Clear();
                AutonomousBotGroupChatter.Send(speaker, picked.Text, "none");
            }
            catch (Exception ex)
            {
                Log.Warn($"EPIC_CALL failed mechanic={signal?.Mechanic}: {ex.Message}");
            }
        }

        /// <summary>The party's leader when it is a gamebot (not the one the mechanic picked), else another gamebot of it.</summary>
        private static GameBot Speaker(Group group, GameLiving picked)
        {
            if (group.LivingLeader is GameBot { IsAlive: true, IsAutonomousWorldBot: true } lead && lead != picked) return lead;
            foreach (GameLiving m in group.GetMembersInTheGroup())
                if (m is GameBot { IsAlive: true, IsAutonomousWorldBot: true } b && b != picked) return b;
            return null;
        }
    }
}
