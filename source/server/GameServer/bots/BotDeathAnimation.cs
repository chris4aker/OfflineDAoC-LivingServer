using System.Collections.Generic;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// A dead bot falls in the original client (1.2.1; owner 2026-11-21: "Death animations need fixed absolutely, or PvP and
    /// RvR will look horrible"). Setting bot_death_animation, on by default.
    ///
    /// A dead bot (companion, hired companion, gamebot: all GameBot) stays in the world at 0 health until it is raised or
    /// released (GameBot.RetainCorpseForResurrection). The original client plays an NPC's fall only when the object is
    /// removed with type 0 ("leave the corpse to fade"), which PacketLib168.SendObjectRemove never sends for a GameBot: a
    /// type-0 removal at the death spot left a second dead copy when the release moved the bot to its bind (owner
    /// 2026-10-30). So:
    /// - At the death each original client in range gets the type-0 removal (the fall; the client keeps a fading corpse),
    ///   and from then until the bot has health again that client gets nothing that would create, update or remove it:
    ///   no create, no update, no equipment, no removal (so nothing at the death spot when the release moves it), and the
    ///   world refresh skips it. A player who comes into range meanwhile gets nothing, as for a monster's corpse.
    /// - When it has health again (raised, released, revived) the hidden state ends and those clients forget it
    ///   (PlayerObjectCache), so the raise's own create, or the next world refresh, creates it standing where it now is.
    /// The new client (handshake build 0, see GameClient.HandshakeBuild) and any client not known to be the original get
    /// exactly what they got before. Real players and monsters are untouched.
    /// </summary>
    public static class BotDeathAnimation
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BotDeathAnimation));

        public static bool On => Properties.BOT_DEATH_ANIMATION;

        // ---- pure decisions (UT_BotDeathAnimation)

        /// <summary>Pure: the original game client, by its handshake build. Living Server's new client sends 0; a client whose
        /// build was not read is not known to be the original and keeps the old behaviour.</summary>
        public static bool IsOriginalClient(bool buildRead, ushort build) => buildRead && build != 0;

        /// <summary>Pure: at a bot's death, this viewer gets the fall (a type-0 removal).</summary>
        public static bool SendsFall(bool settingOn, bool originalClient, bool playing, bool canDetect) =>
            settingOn && originalClient && playing && canDetect;

        /// <summary>Pure: the death hides the bot from original clients (it is marked whatever viewers are in range, so a
        /// player who comes into range while it lies dead gets nothing either).</summary>
        public static bool MarksAtDeath(bool settingOn) => settingOn;

        /// <summary>Pure: a create, update, equipment update or removal of the bot is held back from this client. Only while
        /// the fall was shown and the bot is still dead, and only for the original client.</summary>
        public static bool Holds(bool fallShown, bool botDead, bool originalClient) => fallShown && botDead && originalClient;

        /// <summary>Pure: the bot's health went from none to some while hidden: the clients forget it, to create it again.</summary>
        public static bool EndsAtRaise(bool fallShown, bool wasDead, bool aliveNow) => fallShown && wasDead && aliveNow;

        // ---- the server's side

        /// <summary>The bot is held back from this client now.</summary>
        public static bool HidesFrom(GameObject obj, GameClient client) =>
            obj is GameBot bot && client != null && Holds(bot.DeathFallShown, bot.Health <= 0, client.IsOriginalGameClient);

        public static bool HidesFrom(GameObject obj, GamePlayer player) => HidesFrom(obj, player?.Client);

        /// <summary>The world refresh's check (ClientService.UpdateNpcs), traced only when it holds the bot back.</summary>
        public const string WorldRefresh = "world refresh";

        /// <summary>At a packet that creates, updates or removes <paramref name="obj"/> for <paramref name="client"/>: true
        /// when it is held back. Traced for /lstest deathanim.</summary>
        public static bool Blocks(GameClient client, GameObject obj, string packet)
        {
            if (obj is not GameBot bot)
                return false;
            bool held = HidesFrom(bot, client);
            if (_watch != null && (held || packet != WorldRefresh))
                Trace(client, bot, packet, !held);
            return held;
        }

        /// <summary>Pure: the fall, sent a tick after the death, still goes out: the bot is still hidden (not raised, released or
        /// revived in between) and still in the world.</summary>
        public static bool FallStillDue(bool fallShown, bool botDead, bool active) => fallShown && botDead && active;

        /// <summary>GameBot.ProcessDeath, after the death itself.</summary>
        /// <remarks>The fall goes out on the next tick, not now. An object update is queued as UDP and, for a client without UDP,
        /// written after the tick's TCP packets (PacketProcessor.SendPendingPackets drains the TCP queue first). An update the
        /// death queued in this tick (its health at 0) then reached the original client after the fall, as an update of an
        /// object it had just let fall (playtester bot-death, CI 2026-11-22). Hidden first, so nothing new is queued; the
        /// updates already queued go out at the end of this tick, and the fall after them.</remarks>
        public static void OnDied(GameBot bot)
        {
            if (bot == null || bot.ObjectState != GameObject.eObjectState.Active || !MarksAtDeath(On))
                return;

            // Hidden first, so no refresh between the death and the fall sends it again.
            bot.DeathFallShown = true;
            _ = new ECSGameTimer(bot, _ =>
            {
                SendFall(bot);
                return 0;
            }, 1);
        }

        private static void SendFall(GameBot bot)
        {
            if (!FallStillDue(bot.DeathFallShown, bot.Health <= 0, bot.ObjectState == GameObject.eObjectState.Active))
                return;
            int fell = 0;
            foreach (GamePlayer viewer in bot.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                GameClient client = viewer.Client;
                if (client?.Out is not PacketLib168 packets ||
                    !SendsFall(true, client.IsOriginalGameClient, client.ClientState is GameClient.eClientState.Playing, viewer.CanDetect(bot)))
                    continue;
                packets.SendObjectRemoveLeavingCorpse(bot);
                fell++;
                if (_watch != null)
                    Trace(client, bot, "RemoveObject type 0 (the fall)", true);
            }
            if (Log.IsInfoEnabled)
                Log.Info($"BOT_DEATH_ANIM fall bot=\"{bot.Name}\" id={bot.ObjectID} original_clients={fell}");
        }

        /// <summary>GameBot.Health, when a hidden bot has health again: no longer hidden, and the original clients in range
        /// forget it so it is created again standing (by the raise's own move, or the next world refresh).</summary>
        public static void OnRaised(GameBot bot)
        {
            bot.DeathFallShown = false;
            int forgot = 0;
            foreach (GamePlayer viewer in bot.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                GameClient client = viewer.Client;
                if (client == null || !client.IsOriginalGameClient)
                    continue;
                lock (viewer.PlayerObjectCache.NpcUpdateCacheLock)
                {
                    viewer.PlayerObjectCache.NpcUpdateCache.Remove(bot);
                }
                forgot++;
                if (_watch != null)
                    Trace(client, bot, "raised: shown again (the next create stands it up)", true);
            }
            if (Log.IsInfoEnabled)
                Log.Info($"BOT_DEATH_ANIM raised bot=\"{bot.Name}\" id={bot.ObjectID} original_clients={forgot}");
        }

        // ---- /lstest deathanim: each packet about one bot to one GM's client, in chat and in the log

        private sealed class Watch
        {
            public GameClient Client;
            public GameBot Bot;
            public long Until;
            public int Lines;
            public readonly Dictionary<string, (long Last, int Skipped)> Seen = new();
        }

        private static volatile Watch _watch;
        public const int WatchMilliseconds = 120_000;
        private const int MaxLines = 80;

        public static GameBot Watched => _watch is { } watch && GameLoop.GameLoopTime < watch.Until ? watch.Bot : null;

        public static void StartWatch(GameClient client, GameBot bot) =>
            _watch = new Watch { Client = client, Bot = bot, Until = GameLoop.GameLoopTime + WatchMilliseconds };

        public static void StopWatch() => _watch = null;

        private static void Trace(GameClient client, GameBot bot, string packet, bool sent)
        {
            Watch watch = _watch;
            if (watch == null || watch.Client != client || watch.Bot != bot)
                return;
            long now = GameLoop.GameLoopTime;
            if (now >= watch.Until || watch.Lines >= MaxLines)
            {
                _watch = null;
                return;
            }
            string key = (sent ? "sent " : "held ") + packet;
            int skipped;
            lock (watch.Seen)
            {
                // The same packet at most once in 10 s (updates come every few hundred ms); the count says how many more.
                if (watch.Seen.TryGetValue(key, out var seen) && now - seen.Last < 10_000)
                {
                    watch.Seen[key] = (seen.Last, seen.Skipped + 1);
                    return;
                }
                skipped = seen.Skipped;
                watch.Seen[key] = (now, 0);
                watch.Lines++;
            }
            string line = $"deathanim: {key} (bot health {bot.Health}){(skipped > 0 ? $" (+{skipped} more like it before)" : "")}";
            if (Log.IsInfoEnabled)
                Log.Info($"BOT_DEATH_ANIM_TRACE gm=\"{client.Player?.Name}\" bot=\"{bot.Name}\" id={bot.ObjectID} {(sent ? "SENT" : "HELD")} {packet} health={bot.Health}");
            client.Out?.SendMessage(line, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
