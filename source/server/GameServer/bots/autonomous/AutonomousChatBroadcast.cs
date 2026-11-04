using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS;

/// <summary>
/// Gamebots' /broadcast in towns (Beta 1.1.3): the 2003 place for "WTS ..." macros, "lfg 24 ranger" and recruiting
/// (docs/bot-voices/CHAT-BUDGETS.md). A bot broadcasts as the /broadcast command does for a player: with the server's
/// broadcast_type (area by default), only from a place that allows it and only to players there. Its own budget is the
/// "capital" or "town" channel. A seller's line may repeat as a macro every few minutes, as players' macros did.
/// </summary>
public static class AutonomousChatBroadcast
{
    /// <summary>The players who would hear this bot's /broadcast now (none outside a broadcast area with broadcast_type 1).</summary>
    public static List<GamePlayer> Listeners(GameBot bot)
    {
        var heard = new List<GamePlayer>();
        if (bot?.CurrentRegion == null || bot.CurrentZone == null)
            return heard;
        List<GamePlayer> region = ClientService.Instance.GetPlayersOfRegion(bot.CurrentRegion);
        if (region.Count == 0)
            return heard;
        switch (ServerProperties.Properties.BROADCAST_TYPE)
        {
            case 1:
                IList<IArea> areas;
                try
                {
                    areas = bot.CurrentAreas;
                }
                catch (Exception)
                {
                    return heard;
                }
                foreach (IArea area in areas)
                {
                    if (area is not AbstractArea { CanBroadcast: true })
                        continue;
                    foreach (GamePlayer player in region)
                        if (player.Realm == bot.Realm && player.ObjectState == GameObject.eObjectState.Active && player.CurrentAreas.Contains(area) &&
                            !heard.Contains(player))
                            heard.Add(player);
                }
                return heard;
            case 2:
                heard.AddRange(region.Where(player => player.Realm == bot.Realm && bot.IsWithinRadius(player, WorldMgr.VISIBILITY_DISTANCE)));
                return heard;
            case 3:
                heard.AddRange(region.Where(player => player.Realm == bot.Realm && player.CurrentZone == bot.CurrentZone));
                return heard;
            case 4:
                heard.AddRange(region.Where(player => player.Realm == bot.Realm));
                return heard;
            default:
                // Realm or server wide: no town feel to it, so gamebots don't use /broadcast then.
                return heard;
        }
    }

    /// <summary>The channel a bot's /broadcast is paced by: its capital's or another town's.</summary>
    public static AutonomousChatBudget.Channel ChannelOf(GameBot bot) =>
        AutonomousBotVoices.IsCapitalRegion(bot.CurrentRegionID) ? AutonomousChatBudget.Channel.Capital : AutonomousChatBudget.Channel.Town;

    /// <summary>The meter key of a bot's broadcast place: its zone.</summary>
    public static long KeyOf(GameBot bot) => ((long)bot.CurrentRegionID << 16) | (bot.CurrentZone?.ID ?? 0);

    /// <summary>Sends a bot's /broadcast to the players who hear it, as the /broadcast command formats it, and counts it.</summary>
    public static bool Send(GameBot bot, string text, List<GamePlayer> listeners = null)
    {
        string safe = (text ?? string.Empty).Trim();
        if (bot == null || safe.Length == 0)
            return false;
        if (safe.Length > 240)
            safe = safe[..240];
        listeners ??= Listeners(bot);
        if (listeners.Count == 0)
            return false;
        foreach (GamePlayer player in listeners)
            player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client?.Account?.Language ?? "EN", "Scripts.Players.Broadcast.Message", bot.Name, safe),
                eChatType.CT_Broadcast, eChatLoc.CL_ChatWindow);
        AutonomousChatBudget.Meter.Count(ChannelOf(bot), KeyOf(bot), GameLoop.GameLoopTime);
        BotChatTranscript.Bot("broadcast", bot.Name, bot.Realm, safe);
        return true;
    }

    /// <summary>
    /// A macro: the same WTS line again every few minutes for a while (the 2003 texture rule: every 3-8 minutes for 15-40
    /// minutes, jittered), while the seller stays and someone hears it. Each repeat waits its turn under the ceiling.
    /// </summary>
    public static void StartMacro(GameBot bot, string text, AutonomousChatBudget.Settings settings, Random random)
    {
        if (bot == null || string.IsNullOrEmpty(text) || random.NextDouble() >= settings.MacroChance)
            return;
        long until = GameLoop.GameLoopTime + 1_000L * (settings.MacroLastMinS + random.Next(Math.Max(1, settings.MacroLastMaxS - settings.MacroLastMinS)));
        ushort region = bot.CurrentRegionID;
        int Repeat() => 1_000 * (settings.MacroRepeatMinS + random.Next(Math.Max(1, settings.MacroRepeatMaxS - settings.MacroRepeatMinS)));
        _ = new ECSGameTimer(bot, _ =>
        {
            long now = GameLoop.GameLoopTime;
            if (now > until || bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAlive || bot.CurrentRegionID != region || bot.InCombat)
                return 0;
            AutonomousChatBudget.Channel channel = ChannelOf(bot);
            List<GamePlayer> listeners = Listeners(bot);
            if (listeners.Count == 0)
                return Repeat();
            var band = AutonomousChatBudget.LiveBand(channel, AutonomousBotChatCoordinator.RealmBots(bot.Realm));
            if (AutonomousChatBudget.Meter.PerMinute(channel, KeyOf(bot), now) < band.Ceiling && Send(bot, text, listeners))
                AutonomousChatWhy.Record(bot, "broadcast", "macro", "said", "macro", 1, "a seller's macro, again", text);
            return Repeat();
        }, Repeat());
    }
}
