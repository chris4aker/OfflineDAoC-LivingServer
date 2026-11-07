using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// The party's call for an encounter mechanic (wave 2, 1.2): the first time a bot of the real player's party plays a
    /// tactic in a fight ("only clerics and scouts on Funus", "kill the swarm first", "get out of the fire"), it says so in
    /// /group in the bot voice (combat_calls.lines, topics mech_*; slots {boss} and {monster}). One call a topic per party every
    /// <see cref="TopicGapMilliseconds"/>, and none within <see cref="AnyGapMilliseconds"/> of the party's last one. Bots in
    /// parties without a real player say nothing (nobody would read it).
    /// </summary>
    public static class EncounterMechanicCalls
    {
        private static readonly Logger Log = LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const long TopicGapMilliseconds = 120_000;
        public const long AnyGapMilliseconds = 8_000;

        /// <summary>The call topic for a tactic's reason (its leading tag), or null for none.</summary>
        public static string TopicFor(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return null;
            foreach (var (tag, topic) in Tags)
                if (reason.StartsWith(tag, StringComparison.Ordinal))
                    return topic;
            return null;
        }

        private static readonly (string Tag, string Topic)[] Tags =
        [
            ("funus-puzzle", "mech_funus"),
            ("sacristan-ambush", "mech_sacristan"),
            ("reckoner-room", "mech_reckoner_room"),
            ("sanguis-finish", "mech_sanguis"),
            ("cold-fire", "mech_cold_fire"),
            ("sphere-first", "mech_sphere"),
            ("prince-pets", "mech_prince_pets"),
            ("required-add", "mech_adds_first"),
            ("ranged-add", "mech_ranged_adds"),
            ("hazard", "mech_hazard"),
            ("out of \"", "mech_hazard"),
            ("fight-at-home", "mech_fight_home"),
        ];

        private static readonly ConcurrentDictionary<(long Party, string Topic), long> LastTopic = new();
        private static readonly ConcurrentDictionary<long, long> LastAny = new();

        /// <summary>The party may make this call now (and it is booked); pure but for the booking.</summary>
        public static bool Book(long party, string topic, long now)
        {
            if (topic == null) return false;
            if (LastAny.TryGetValue(party, out long any) && now - any < AnyGapMilliseconds) return false;
            if (LastTopic.TryGetValue((party, topic), out long last) && now - last < TopicGapMilliseconds) return false;
            if (LastTopic.Count > 2_048) LastTopic.Clear();
            LastTopic[(party, topic)] = now;
            LastAny[party] = now;
            return true;
        }

        public static void Reset()
        {
            LastTopic.Clear();
            LastAny.Clear();
        }

        /// <summary>TryHandle's hook: the advice a bot is about to play.</summary>
        public static void Consider(GameBot bot, GameNPC target, EpicEncounterTactics.Advice advice)
        {
            try
            {
                string topic = TopicFor(advice.Reason);
                Group group = bot?.Group;
                if (topic == null || group == null || !bot.IsAlive || !AutonomousLivingServer.GroupHasPlayer(group)) return;
                if (!Book(AutonomousBotGroupChatter.KeyOf(group), topic, GameLoop.GameLoopTime)) return;
                var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["boss"] = target?.Name ?? "the boss",
                    ["monster"] = advice.Target?.Name ?? target?.Name ?? "the add",
                };
                if (!AutonomousBotVoices.TryLine(bot, "combat", out AutonomousBotVoiceCorpus.Picked picked, topic: topic, slots: slots,
                        relax: true))
                    return;
                AutonomousBotGroupChatter.Send(bot, picked.Text, "none");
            }
            catch (Exception ex)
            {
                Log.Warn($"EPIC_MECHANIC_CALL failed: {ex.Message}");
            }
        }
    }
}
