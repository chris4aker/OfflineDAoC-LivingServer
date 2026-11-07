using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// The party's call for an encounter mechanic (wave 2, 1.2): when a bot of the real player's party plays a tactic in a
    /// fight ("only clerics and scouts on Funus", "kill the swarm first", "dont touch the king, all on kula"), it says so in
    /// /group in the bot voice (combat_calls.lines; slots {boss}, {monster} and {target}). The topic is the dungeon rule's own
    /// (<see cref="EpicEncounterTactics.Advice.Call"/>, topics epic_*), else the one for the tactic reason's leading tag
    /// (<see cref="TopicFor"/>, topics mech_*). The one booking for both (merged in wave 2): a rule's call once a topic per
    /// party every <see cref="RuleTopicGapMilliseconds"/>, a tag's every <see cref="TopicGapMilliseconds"/>, and no call
    /// within <see cref="AnyGapMilliseconds"/> of the party's last one. Bots in parties without a real player say nothing
    /// (nobody would read it). The calls for a script's announced mechanics (a glare, a stun: EncounterCalls) are booked
    /// apart, so a mechanic call never holds back a warning.
    /// </summary>
    public static class EncounterMechanicCalls
    {
        private static readonly Logger Log = LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const long TopicGapMilliseconds = 120_000;
        /// <summary>A dungeon rule's call (a timed mechanic: Kvasir's scream every minute or so) may come this often.</summary>
        public const long RuleTopicGapMilliseconds = EpicEncounterTactics.MechanicCallGapMilliseconds;
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
        public static bool Book(long party, string topic, long now, long topicGap = TopicGapMilliseconds)
        {
            if (topic == null) return false;
            if (LastAny.TryGetValue(party, out long any) && now - any < AnyGapMilliseconds) return false;
            if (LastTopic.TryGetValue((party, topic), out long last) && now - last < topicGap) return false;
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

        /// <summary>The topic and its gap for an advice (pure): the rule's own call, else its reason's tag; null: no call.</summary>
        public static (string Topic, long Gap)? CallFor(EpicEncounterTactics.Advice advice) =>
            advice.Call != null ? (advice.Call, RuleTopicGapMilliseconds)
            : TopicFor(advice.Reason) is { } tag ? (tag, TopicGapMilliseconds) : null;

        /// <summary>TryHandle's hook: the advice a bot is about to play in <paramref name="boss"/>'s fight.</summary>
        public static void Consider(GameBot bot, GameNPC boss, EpicEncounterTactics.Advice advice)
        {
            if (CallFor(advice) is not { } call) return;
            Say(bot, call.Topic, boss, advice.Target, call.Gap);
        }

        /// <summary>
        /// The party calls <paramref name="topic"/> in /group (a gamebot of the real player's party; booked as above).
        /// <paramref name="other"/>: what the call is about besides the boss (the add, the partner, a raider), slots {monster}
        /// and {target}.
        /// </summary>
        public static void Say(GameBot bot, string topic, GameNPC boss, GameObject other, long topicGap = TopicGapMilliseconds)
        {
            try
            {
                Group group = bot?.Group;
                if (topic == null || group == null || !bot.IsAlive || !bot.IsAutonomousWorldBot || !AutonomousLivingServer.GroupHasPlayer(group))
                    return;
                if (!Book(AutonomousBotGroupChatter.KeyOf(group), topic, GameLoop.GameLoopTime, topicGap)) return;
                string otherName = other?.Name ?? boss?.Name;
                var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["boss"] = boss?.Name ?? "the boss",
                    ["monster"] = otherName ?? "the add",
                    ["target"] = otherName ?? "it",
                };
                if (!AutonomousBotVoices.TryLine(bot, "combat", out AutonomousBotVoiceCorpus.Picked picked, topic: topic, slots: slots,
                        relax: true))
                    return;
                AutonomousBotGroupChatter.Send(bot, picked.Text, "none");
            }
            catch (Exception ex)
            {
                Log.Warn($"EPIC_MECHANIC_CALL {topic} failed: {ex.Message}");
            }
        }
    }
}
