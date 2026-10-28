using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Every PvE party needs a healer, and healers are the scarce role: in the 2026-10-27 evening run no healer ever timed
    /// out waiting for a party, while non-healers did at about 100 an hour (most of them Hibernian level 50s, the realm with
    /// the fewest healers). A non-healer is given a group task only while the healers waiting at its level could carry the
    /// queue; otherwise it solos (and still gets a group task at the next task boundary if healers turn up). Healers always
    /// may group. The same rule holds for every realm.
    /// </summary>
    public static class AutonomousGroupSupply
    {
        /// <summary>Non-healers one waiting healer can carry (a five-man is a healer and four others).</summary>
        public const int OthersPerHealer = 5;
        /// <summary>Non-healers allowed to queue with no healer waiting, so a party can start the moment one comes.</summary>
        public const int Slack = 5;

        private static readonly ConcurrentDictionary<(eRealm Realm, int Bucket), (int Healers, int Others)> Waiting = new();

        /// <summary>Level 50s together; below that ten-level buckets (formation takes levels within five of the leader).</summary>
        public static int Bucket(int level) => level >= 50 ? 50 : Math.Max(0, level) / 10 * 10;

        /// <summary>Whether another non-healer may join the queue. Pure.</summary>
        public static bool RoomForNonHealer(int healersWaiting, int othersWaiting) =>
            othersWaiting < OthersPerHealer * Math.Max(0, healersWaiting) + Slack;

        /// <summary>Called by each group-PvE formation pass with the bots still waiting for a party.</summary>
        public static void Update(IEnumerable<GameBot> waiting)
        {
            var counts = new Dictionary<(eRealm, int), (int Healers, int Others)>();
            foreach (GameBot bot in waiting)
            {
                if (bot?.CharacterClass == null)
                    continue;
                var key = (bot.Realm, Bucket(bot.Level));
                counts.TryGetValue(key, out var count);
                counts[key] = IsHealer(bot) ? (count.Healers + 1, count.Others) : (count.Healers, count.Others + 1);
            }
            Waiting.Clear();
            foreach (var pair in counts)
                Waiting[pair.Key] = pair.Value;
        }

        public static bool IsHealer(GameBot bot) =>
            bot?.CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)bot.CharacterClass.ID);

        /// <summary>Whether this bot should take a group task now. True before the first formation pass has counted.</summary>
        public static bool ShouldQueue(GameBot bot)
        {
            if (bot == null || IsHealer(bot) || Waiting.IsEmpty)
                return true;
            var key = (bot.Realm, Bucket(bot.Level));
            Waiting.TryGetValue(key, out var count);
            if (!RoomForNonHealer(count.Healers, count.Others))
                return false;
            // Counted at once, so one allocation pass can't queue a whole bucket before the next formation pass counts again.
            Waiting.AddOrUpdate(key, (0, 1), (_, value) => (value.Healers, value.Others + 1));
            return true;
        }

        public static void ResetForTests() => Waiting.Clear();
    }
}
