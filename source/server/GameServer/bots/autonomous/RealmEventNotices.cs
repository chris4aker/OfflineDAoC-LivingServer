using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>Bounded event chat; producers never send packets while holding event locks.</summary>
    public static class RealmEventNotices
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<(string, eRealm), (string Text, long Expires, string Topic, IReadOnlyDictionary<string, string> Slots)> Pending = new();
        private static readonly Dictionary<eRealm, long> Next = new();
        /// <summary>When each event was last told to each realm, so world news (AutonomousChatNews) doesn't tell it twice.</summary>
        private static readonly Dictionary<(string, eRealm), long> Told = new();
        public const long ToldMemoryMs = 3 * 60_000;

        /// <summary>
        /// The talking update: an event notice may name its news topic and slots ("siege_defend", {keep}, {enemy}). With bot
        /// voices on, a bot tells it in its own words ("they are hitting the door at Caer Benowyc, need defenders") and others
        /// react; the written notice is only the fallback. Topic "-" marks an event the world news tells instead (a relic set
        /// in a shrine), so its notice is dropped while voices are on.
        /// </summary>
        public static void Queue(string id, eRealm realm, string text, string newsTopic = null, IReadOnlyDictionary<string, string> newsSlots = null)
        {
            if (realm == eRealm.None || string.IsNullOrWhiteSpace(text)) return;
            lock (Sync)
            {
                if (Pending.Count >= 32 && !Pending.ContainsKey((id, realm))) return;
                Pending[(id, realm)] = (text, GameLoop.GameLoopTime + 5 * 60_000, newsTopic, newsSlots);
            }
        }

        /// <summary>Slots for a keep event: the keep's name as players say it, and the enemy realm.</summary>
        public static Dictionary<string, string> KeepSlots(string objectiveName, eRealm enemy)
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["keep"] = AutonomousRvrEventLayer.Place(objectiveName) };
            if (enemy != eRealm.None)
                slots["enemy"] = GlobalConstants.RealmToName(enemy);
            return slots;
        }

        /// <summary>Whether this realm heard about this event in the last few minutes.</summary>
        public static bool VoicedRecently(string id, eRealm realm, long now)
        {
            lock (Sync)
                return Told.TryGetValue((id, realm), out long when) && now - when < ToldMemoryMs;
        }

        /// <summary>
        /// A bot just told this realm about the event in its own words (AutonomousChatNews): a notice about it still waiting
        /// for its turn (one that a bot could have said, so with a news topic) is not sent as well.
        /// </summary>
        public static void ToldByNews(string id, eRealm realm, long now)
        {
            if (string.IsNullOrEmpty(id))
                return;
            lock (Sync)
            {
                if (Pending.TryGetValue((id, realm), out var pending) && !string.IsNullOrEmpty(pending.Topic))
                    Pending.Remove((id, realm));
                Told[(id, realm)] = now;
            }
        }

        // Realm-wide one-line announcements at the top of the screen (no sound, not in the chat box),
        // for the moment a siege begins. Sent from Pulse, never while an event lock is held.
        private static readonly Dictionary<(string, eRealm), (string Text, AnnouncementKind Kind)> PendingScreen = new();

        public static void QueueScreen(string id, eRealm realm, string text, AnnouncementKind kind)
        {
            if (realm == eRealm.None || string.IsNullOrWhiteSpace(text) || !GameWideAnnouncements.Enabled(kind)) return;
            lock (Sync)
            {
                if (PendingScreen.Count < 32 || PendingScreen.ContainsKey((id, realm)))
                    PendingScreen[(id, realm)] = (text, kind);
            }
        }

        private static void FlushScreen()
        {
            KeyValuePair<(string, eRealm), (string Text, AnnouncementKind Kind)>[] screen;
            lock (Sync)
            {
                if (PendingScreen.Count == 0) return;
                screen = PendingScreen.ToArray();
                PendingScreen.Clear();
            }
            foreach (var notice in screen)
                if (GameWideAnnouncements.Enabled(notice.Value.Kind))
                    foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRealm(notice.Key.Item2))
                    player.Out.SendMessage(notice.Value.Text, PacketHandler.eChatType.CT_ScreenCenter, PacketHandler.eChatLoc.CL_SystemWindow);
        }

        public static void Pulse(long now)
        {
            FlushScreen();
            GameWideAnnouncements.Flush();
            AutonomousChatNews.Pulse(now);
            bool voices = AutonomousBotVoices.Enabled;
            List<(string EventId, eRealm Realm, string Text, string Topic, IReadOnlyDictionary<string, string> Slots)> outgoing = new();
            lock (Sync)
            {
                foreach (var entry in Pending.ToArray())
                {
                    if (entry.Value.Expires < now) { Pending.Remove(entry.Key); continue; }
                    eRealm realm = entry.Key.Item2;
                    if (voices && entry.Value.Topic == "-") { Pending.Remove(entry.Key); continue; }
                    if (Next.GetValueOrDefault(realm) > now) continue;
                    Next[realm] = now + 60_000;
                    Pending.Remove(entry.Key);
                    outgoing.Add((entry.Key.Item1, realm, entry.Value.Text, entry.Value.Topic, entry.Value.Slots));
                    Told[entry.Key] = now;
                }
                foreach (var old in Told.Where(pair => now - pair.Value > ToldMemoryMs).Select(pair => pair.Key).ToList())
                    Told.Remove(old);
            }
            outgoing.RemoveAll(notice => !ClientService.Instance.GetPlayersOfRealm(notice.Realm).Any());
            if (outgoing.Count == 0) return;
            // Only performed when a rate-limited announcement is due, not on
            // every bot turn. Use a real committed speaker when one exists.
            var forces = AutonomousRvrEventLayer.ForceTargets();
            GameBot[] bots = AutonomousBotRegistry.Snapshot();
            foreach (var notice in outgoing)
            {
                GameBot speaker = null;
                GameBot fallback = null;
                int realmCandidates = 0;
                int candidates = 0;
                foreach (GameBot bot in bots)
                {
                    if (bot.Realm != notice.Realm || !bot.IsAlive || bot.IsTemporaryGroupHelper ||
                        bot.ObjectState != GameObject.eObjectState.Active) continue;
                    if (Random.Shared.Next(++realmCandidates) == 0) fallback = bot;
                    string force = bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";
                    if (AutonomousRealmRaid.GetView(bot.Group)?.EventId != notice.EventId &&
                        (!forces.TryGetValue(force, out string target) || target != notice.EventId)) continue;
                    if (Random.Shared.Next(++candidates) == 0) speaker = bot;
                }
                // The talking update: a bot tells it in its own words (a bot taking part says it as a witness), and others react.
                if (voices && !string.IsNullOrEmpty(notice.Topic) && notice.Topic != "-" &&
                    AutonomousBotChatCoordinator.TrySayEventNews(speaker ?? fallback, speaker != null, notice.Topic, notice.Slots))
                    continue;
                GameBot herald = speaker ?? fallback;
                AutonomousBotChatCoordinator.BroadcastFaction(herald?.Name ?? "Realm herald", notice.Realm,
                    herald != null ? AutonomousBotVoices.StyleFor(herald, notice.Text) : notice.Text);
            }
        }
    }
}
