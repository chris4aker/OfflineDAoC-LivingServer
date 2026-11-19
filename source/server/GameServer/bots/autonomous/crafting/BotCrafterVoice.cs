using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.CraftingEconomy;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// What a crafter gamebot says about its work (Crafting 2.0 WP4, design doc §6 "The voice"), under the truth rule: a line
/// that names stock needs the stock.
/// <list type="bullet">
/// <item>A /trade advert after a session names a piece the crafter really made and still holds, its real quality and
/// the price it would ask (checked again when the line is said, a few seconds later; gone stock, no line).</item>
/// <item>A new crafting title is said in /guild (or /say near a player) as the bot reaches it.</item>
/// </list>
/// Rates follow bot_crafter_chat_rate (0 = silent) and the realm's /trade budget; with bot voices on the lines come from
/// the corpus (crafting.lines: @needs has_craft_stock / crafter_title_up), otherwise from a few fixed ones.
/// </summary>
public static class BotCrafterVoice
{
    /// <summary>Share of sessions with a listable piece that end in an advert, at chat rate 1.</summary>
    public const double AdvertChance = 0.3;
    /// <summary>Share of title rungs said, at chat rate 1.</summary>
    public const double TitleChance = 0.7;
    /// <summary>One crafter advert per realm in this many milliseconds.</summary>
    public const long RealmAdvertRestMs = 6 * 60_000;

    private static readonly ConcurrentDictionary<eRealm, long> LastAdvert = new();

    public static double ChatRate => Math.Clamp(CraftingEconomyProperties.BOT_CRAFTER_CHAT_RATE, 0.0, 3.0);

    /// <summary>A price as players wrote it: "3g", "2g 50s", "75s", "40c", and from a thousand gold in platinum, "9p", "1p 500g"
    /// (chat audit LOG-036: "paying up to 9275g").</summary>
    public static string PriceText(long copper)
    {
        copper = Math.Max(0, copper);
        long platinum = copper / 10_000_000;
        if (platinum > 0)
        {
            long goldLeft = copper / 10_000 % 1000;
            return goldLeft >= 100 && platinum < 10 ? $"{platinum}p {goldLeft / 100 * 100}g" : $"{platinum}p";
        }
        long gold = copper / 10_000, silver = copper / 100 % 100;
        if (gold > 0)
            return silver > 0 && gold < 10 ? $"{gold}g {silver}s" : $"{gold}g";
        return silver > 0 ? $"{silver}s" : $"{copper}c";
    }

    /// <summary>The fixed advert (bot voices off): the real piece, its real quality and price.</summary>
    public static string FixedAdvert(string stock, int quality, string price, int roll) => (Math.Abs(roll) % 3) switch
    {
        0 => $"wts {stock} {quality}%, {price}, pst",
        1 => $"selling {stock}, {quality}% quality, {price}",
        _ => $"fresh off the forge: {stock} {quality}%, {price} obo",
    };

    public static string FixedTitleLine(string title, int roll) => (Math.Abs(roll) % 3) switch
    {
        0 => $"finally {title.ToLowerInvariant()}",
        1 => $"{title} at last, all those materials paid off",
        _ => $"just made {title.ToLowerInvariant()}!",
    };

    /// <summary>Whether this held piece may be advertised now: the crafter's own work, still in its bag or posted by it.</summary>
    public static bool StillHolds(GameBot bot, DbInventoryItem item)
    {
        if (bot?.Inventory == null || item == null)
            return false;
        if (bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).Contains(item))
            return AutonomousBotCrafting.IsListableWork(bot, item);
        // Posted already: it is still the crafter's stock until it sells.
        return item.OwnerID == AutonomousBotEconomy.GetOwnerId(bot.DatabaseID) && RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot);
    }

    /// <summary>After a session with a listable piece: now and then a true /trade advert for it.</summary>
    public static void AfterSession(GameBot bot, DbInventoryItem best)
    {
        if (best == null || ChatRate <= 0 || !AutonomousLivingServer.CanTalk(bot) || !AutonomousLivingServer.RealmHasPlayer(bot.Realm) ||
            Random.Shared.NextDouble() >= AdvertChance * ChatRate)
            return;
        AutonomousLivingServer.Later(bot, 4_000 + Random.Shared.Next(12_000), () => AutonomousLivingServer.CanTalk(bot) && StillHolds(bot, best),
            () => Advertise(bot, best));
    }

    private static void Advertise(GameBot bot, DbInventoryItem item)
    {
        long now = GameLoop.GameLoopTime;
        int realmBots = AutonomousBotRegistry.CountByRealm().GetValueOrDefault(bot.Realm);
        if (LastAdvert.TryGetValue(bot.Realm, out long last) && now - last < RealmAdvertRestMs ||
            AutonomousChatBudget.Meter.PerMinute(AutonomousChatBudget.Channel.Trade, (long)bot.Realm, now) >=
            AutonomousChatBudget.LiveBand(AutonomousChatBudget.Channel.Trade, realmBots).Target)
            return;
        LastAdvert[bot.Realm] = now;
        string stock = item.Name.ToLowerInvariant();
        long priceCopper = RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) && item.SellPrice > 0
            ? item.SellPrice : AutonomousBotEconomy.RecommendListingPrice(item);
        string price = PriceText(priceCopper);
        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["stock"] = stock, ["item"] = stock, ["quality"] = item.Quality.ToString(), ["price"] = price,
        };
        string text = AutonomousBotVoices.TryLine(bot, "trade", out VC.Picked picked, flags: ["has_craft_stock"],
                requireNeed: "has_craft_stock", slots: slots)
            ? picked.Text
            : FixedAdvert(stock, item.Quality, price, Random.Shared.Next(3));
        SendTrade(bot, text);
    }

    /// <summary>A crafting title rung reached: said now and then in /guild, or /say near a player.</summary>
    public static void OnTitle(GameBot bot, eCraftingSkill order, int rung, string title)
    {
        if (ChatRate <= 0 || bot == null || Random.Shared.NextDouble() >= TitleChance * ChatRate)
            return;
        AutonomousLivingServer.Later(bot, 3_000 + Random.Shared.Next(9_000), () => AutonomousLivingServer.CanTalk(bot), () =>
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["title"] = title, ["skill"] = rung.ToString() };
            string text = AutonomousBotVoices.TryLine(bot, "craft", out VC.Picked picked, flags: ["crafter_title_up"],
                    requireNeed: "crafter_title_up", slots: slots)
                ? picked.Text
                : FixedTitleLine(title, Random.Shared.Next(3));
            AutonomousLivingServer.Speak(bot, bot.Guild != null && AutonomousBotGuildSocial.HasListener(bot.Guild)
                ? AutonomousLivingServer.Channel.Guild : AutonomousLivingServer.Channel.Say, text);
        });
    }

    /// <summary>A line in the bot's realm /trade channel, formatted and counted as the chat coordinator's own /trade lines.</summary>
    internal static void SendTrade(GameBot bot, string text)
    {
        string safe = VC.ForNow((text ?? string.Empty).Trim());
        if (safe.Length == 0)
            return;
        if (safe.Length > 240)
            safe = safe[..240];
        string formatted = $"[Trade] {bot.Name}: {safe}";
        foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRealm(bot.Realm))
            player.Out.SendMessage(formatted, eChatType.CT_Trade, eChatLoc.CL_ChatWindow);
        BotChatTranscript.Bot("trade", bot.Name, bot.Realm, safe);
        long now = GameLoop.GameLoopTime;
        AutonomousChatBudget.Meter.Count(AutonomousChatBudget.Channel.Trade, (long)bot.Realm, now);
        AutonomousChatExchange.NoteLine("trade:" + (int)bot.Realm, bot.Name, now);
    }

    public static void ResetForTests() => LastAdvert.Clear();
}
