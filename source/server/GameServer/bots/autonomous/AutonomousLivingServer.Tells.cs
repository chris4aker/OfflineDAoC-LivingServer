using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Tells a bot starts (the talking update). Now and then, about once every 25-45 minutes at most, a bot sends the player a
/// private message: a friend saying hello or thanks for an old group, a friend passing on realm news, a guildmate's small
/// talk, a friend offering a low-level player a hand, or a solo bot of about the same level in the same zone asking to duo.
/// Each tell is the start of a thread (AutonomousChatThreads): the player's answer gets a fitting reply, and "sure" to a duo
/// offer gets "ok, invite me" (the player invites; a bot cannot invite a player). Needs bot voices and bot memory (an answer
/// to the player's /lfg needs voices only).
/// </summary>
public static partial class AutonomousLivingServer
{
    public const long TellFirstMinMs = 4 * 60_000;
    public const long TellFirstSpreadMs = 6 * 60_000;
    public const long TellGapMinMs = 25 * 60_000;
    public const long TellGapSpreadMs = 20 * 60_000;
    public const long TellPairCooldownMs = 6 * 60 * 60_000L;
    /// <summary>The invitation recorded with a duo or help offer, so "sure" is answered with how to group.</summary>
    public const string AskGroupInvite = "ask_group";

    private static readonly Dictionary<string, long> NextTellTo = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(long BotId, string Player), long> LastTellPair = new();

    public static bool TellsOn => AutonomousBotVoices.Enabled && Properties.BOT_VOICES_TELLS;

    /// <summary>When the next bot tell to a player may come: a few minutes after they log in, then 25-45 minutes apart.</summary>
    public static long NextTellGap(bool first, Random random) => first
        ? TellFirstMinMs + (long)(random.NextDouble() * TellFirstSpreadMs)
        : TellGapMinMs + (long)(random.NextDouble() * TellGapSpreadMs);

    /// <summary>A solo bot asks to duo only a player of about its level, alone, in the same zone and not far off.</summary>
    public static bool FitsGroupOffer(int botLevel, int playerLevel, bool playerGrouped, bool sameZone, double distance) =>
        !playerGrouped && sameZone && playerLevel >= 5 && Math.Abs(botLevel - playerLevel) <= 2 && distance <= 6_000;

    /// <summary>A friend offers help to a player at least eight levels below it, under level 20, nearby.</summary>
    public static bool FitsHelpOffer(int botLevel, int playerLevel, bool sameZone, double distance) =>
        sameZone && playerLevel < 20 && botLevel >= playerLevel + 8 && distance <= 4_000;

    /// <summary>GM test (/lstest tell): the player's tell gap is skipped, so a bot starts a tell within a few seconds if one fits.</summary>
    public static void TellSoon(GamePlayer player)
    {
        lock (NextTellTo)
            NextTellTo[player.Name] = 0;
    }

    private static void TryStartTell(GamePlayer player, long now)
    {
        if (!TellsOn)
            return;
        lock (NextTellTo)
        {
            if (!NextTellTo.TryGetValue(player.Name, out long next))
            {
                NextTellTo[player.Name] = now + NextTellGap(true, Random.Shared);
                return;
            }
            if (now < next)
                return;
            NextTellTo[player.Name] = now + NextTellGap(false, Random.Shared);
        }
        if (!player.IsAlive || player.InCombat)
            return;
        foreach ((GameBot bot, string topic, IReadOnlyDictionary<string, string> slots, string[] flags) in TellCandidates(player))
        {
            if (!AutonomousBotVoices.TryLine(bot, "tell", out VC.Picked picked, topic: topic, slots: slots, flags: flags))
                continue;
            lock (LastTellPair)
                LastTellPair[(bot.DatabaseID, player.Name)] = now;
            // Only a duo offer is answered with how to group; "need directions?" is a real question (review 2026-10-26).
            string invites = topic == "group_offer" && picked.Line?.Invites == "ask" ? AskGroupInvite : picked.Line?.Invites;
            Later(bot, 0, () => CanTalk(bot) && player.ObjectState == GameObject.eObjectState.Active, () => Tell(bot, player, picked.Text, invites));
            return;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex HereWords = new(
        @"\b(here|same area|same mobs|soloing|same zone|out here)\b|\{zone\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>A duo-offer line that says "here" fits only a bot in the player's zone.</summary>
    // (Needs voices and bot_voices_tells, not memory: the bot answers a player's /lfg, it doesn't recall them.)
    public static bool OfferFits(string lineText, bool sameZone) => sameZone || !HereWords.IsMatch(lineText ?? string.Empty);

    /// <summary>
    /// The player looked for a group in /lfg: a solo bot of about the same level (any zone, the same zone first) may send a
    /// tell offering to group, a few seconds later. Once per bot and player in six hours.
    /// </summary>
    public static void OnPlayerLfg(GamePlayer player, string message)
    {
        AutonomousBeggars.OnPlayerAsked(player, message, "lfg");
        if (!TellsOn || player?.Client == null || player.Group != null)
            return;
        long now = GameLoop.GameLoopTime;
        GameBot[] bots = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.Realm == player.Realm && CanTalk(bot) && bot.Group == null && !bot.IsOnStableMasterRoute &&
                          BotGroupInvite.DutyOf(bot) == BotGroupInvite.Duty.None && Math.Abs(bot.Level - player.Level) <= 2)
            .Where(bot =>
            {
                lock (LastTellPair)
                    return !LastTellPair.TryGetValue((bot.DatabaseID, player.Name), out long told) || now - told >= TellPairCooldownMs;
            })
            .Where(bot => !TryRecall(bot, player, out OfflineBotMemoryRecord row) || MoodOf(row) != "cool")
            .OrderByDescending(bot => bot.CurrentZone == player.CurrentZone)
            .ThenBy(_ => Random.Shared.Next())
            .Take(4).ToArray();
        int offers = Random.Shared.NextDouble() < 0.3 ? 2 : 1;
        int delay = 6_000;
        foreach (GameBot bot in bots)
        {
            if (offers == 0 || Random.Shared.NextDouble() >= 0.75)
                continue;
            bool sameZone = bot.CurrentZone == player.CurrentZone;
            bool known = TryRecall(bot, player, out OfflineBotMemoryRecord row);
            IReadOnlyDictionary<string, string> slots = known ? MemorySlots(row, player)
                : new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player.Name };
            if (!AutonomousBotVoices.TryLine(bot, "tell", out VC.Picked picked, topic: "group_offer", slots: slots,
                    flags: known ? ["knows_player"] : null, filter: line => OfferFits(line.Text, sameZone)))
                continue;
            lock (LastTellPair)
                LastTellPair[(bot.DatabaseID, player.Name)] = now;
            delay += 3_000 + Random.Shared.Next(12_000);
            string invites = picked.Line?.Invites == "ask" ? AskGroupInvite : picked.Line?.Invites;
            Later(bot, delay, () => CanTalk(bot) && bot.Group == null && player.ObjectState == GameObject.eObjectState.Active && player.Group == null,
                () => Tell(bot, player, picked.Text, invites));
            offers--;
        }
    }

    /// <summary>The bots that could start a tell with this player now and what about, best first (a little shuffled).</summary>
    private static IEnumerable<(GameBot Bot, string Topic, IReadOnlyDictionary<string, string> Slots, string[] Flags)> TellCandidates(GamePlayer player)
    {
        long now = GameLoop.GameLoopTime;
        bool Free(GameBot bot)
        {
            if (!CanTalk(bot) || bot.Realm != player.Realm || bot.Group != null && bot.Group == player.Group || bot.IsOnStableMasterRoute)
                return false;
            lock (LastTellPair)
                return !LastTellPair.TryGetValue((bot.DatabaseID, player.Name), out long told) || now - told >= TellPairCooldownMs;
        }
        bool Near(GameBot bot) => bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE * 2);
        double Distance(GameBot bot) => bot.CurrentRegion == player.CurrentRegion ? bot.GetDistanceTo(player) : double.MaxValue;
        var options = new List<(double Score, GameBot Bot, string Topic, IReadOnlyDictionary<string, string> Slots, string[] Flags)>();
        // Lines about levelling ("dinged yet?") only for a player under the cap (in-game check 2026-10-27: a level 50 was asked).
        bool leveling = player.Level < GamePlayer.MAX_LEVEL;
        string[] Known = leveling ? ["knows_player", "player_leveling"] : ["knows_player"];
        string[] Stranger = leveling ? ["player_leveling"] : [];

        // Friends: hello, thanks for the group, news, a hand for a low-level friend. Not one standing next to the player (it
        // greets in /say instead).
        foreach (OfflineBotMemoryRecord row in Memories.Where(r => string.Equals(r.PlayerName, player.Name, StringComparison.OrdinalIgnoreCase) &&
                                                                   MoodOf(r) == "friendly" && r.Grouped + r.Rezzed >= 1))
        {
            if (!AutonomousBotRegistry.TryGet(row.BotId, out GameBot bot) || !Free(bot) || Near(bot))
                continue;
            Dictionary<string, string> slots = MemorySlots(row, player);
            string[] flags = Known;
            double score = Random.Shared.NextDouble() + Math.Min(3, row.Grouped + row.Rezzed) * 0.2;
            if (FitsHelpOffer(bot.Level, player.Level, bot.CurrentZone == player.CurrentZone, Distance(bot)))
                options.Add((score + 0.6, bot, "help_offer", slots, flags));
            options.Add((score + 0.3, bot, "news_tell", slots, flags));
            if (row.LastTopic == "group")
                options.Add((score + 0.2, bot, "after_group", slots, flags));
            options.Add((score, bot, "hello_friend", slots, flags));
        }

        // Guildmates (bot guilds), not just friends.
        if (player.Guild != null && !AutonomousBotGuildSocial.CountsAsGuildless(player.Guild))
            foreach (GameBot bot in AutonomousBotRegistry.Snapshot().Where(b => b.Guild == player.Guild && Free(b) && !Near(b)).Take(12))
                options.Add((Random.Shared.NextDouble(), bot, "guildmate",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player.Name }, Known));

        // A solo bot nearby asks to duo.
        if (player.Group == null && player.CurrentZone != null)
            foreach (GameBot bot in player.GetNPCsInRadius(6_000).OfType<GameBot>())
            {
                if (!Free(bot) || bot.Group != null || BotGroupInvite.DutyOf(bot) != BotGroupInvite.Duty.None ||
                    !FitsGroupOffer(bot.Level, player.Level, false, bot.CurrentZone == player.CurrentZone, Distance(bot)))
                    continue;
                bool known = TryRecall(bot, player, out OfflineBotMemoryRecord row);
                if (known && MoodOf(row) == "cool")
                    continue;
                options.Add((Random.Shared.NextDouble() + 0.4, bot, "group_offer",
                    known ? MemorySlots(row, player) : new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player.Name },
                    known ? Known : Stranger));
            }

        return options.OrderByDescending(o => o.Score).Take(10).Select(o => (o.Bot, o.Topic, o.Slots, o.Flags)).ToArray();
    }
}
