using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.GS.PacketHandler;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Coordinates scarce, short bot conversations. Faction text stays broad;
/// local text is allowed to mention the speaker's live task and surroundings.
/// </summary>
public static class AutonomousBotChatCoordinator
{
    private static readonly object Sync = new();
    private static readonly Dictionary<eRealm, long> LastFactionAmbient = new();
    private static readonly Dictionary<ushort, long> LastLocalAmbient = new();
    // With bot voices on, the gap between unprompted lines varies (2-4 min in /faction, 1-2 min in /say) instead
    // of a fixed 3 min and 90 s, so chat comes in uneven bursts and lulls like people's (0.36-se.4).
    private static readonly Dictionary<eRealm, long> FactionGap = new();
    private static readonly Dictionary<ushort, long> LocalGap = new();
    private static readonly Dictionary<eRealm, long> LastFactionPlayerResponse = new();
    /// <summary>When a player of the realm last spoke in /faction. Unprompted lines wait <see cref="PlayerQuietMs"/> after it, so
    /// they aren't read as answers (live test 2026-10-18: "lf group, minstrel 50" right after "mind if i hunt near cotswold?").</summary>
    private static readonly Dictionary<eRealm, long> LastPlayerFactionLine = new();
    public const int PlayerQuietMs = 15_000;

    public static bool QuietAfterPlayer(long lastPlayerLine, long now) => lastPlayerLine > 0 && now - lastPlayerLine < PlayerQuietMs;
    private static readonly Dictionary<ushort, long> LastLocalPlayerResponse = new();
    private static readonly Dictionary<string, long> LastTellResponse = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] FactionOpeners =
    [
        "How fares everyone across the realm?", "Any groups forming today?", "The roads seem lively today.",
        "Anyone heading out for a longer hunt?", "I may look for company later.", "Good hunting, everyone.",
        "Has the frontier been active?", "Anyone trading useful equipment today?", "Stay sharp out there.",
        "Which hunting grounds are treating everyone well?", "Any useful class advice to share today?",
        "Has anyone found a dependable camp for their level?", "The realm feels lively today.",
        "If anyone needs a group, say where you are headed.", "Remember to visit your trainer after leveling.",
        "Are any dungeon groups planning a careful run?", "Safe roads and good hunting to everyone.",
        "What is everyone working on today?",
    ];

    private static readonly string[] FactionReplies =
    [
        "Things seem steady from where I am.", "I have seen a few adventurers moving around.",
        "I may be available once I finish up here.", "That sounds worth keeping in mind.",
        "Safe travels if you head that way.", "I have heard similar talk around the realm.",
        "There should be others interested before long.", "I will keep an eye out while I finish this task.",
        "Sharing the location may help someone nearby.", "A balanced group should handle that well.",
        "Checking your trainer and equipment is always worthwhile.", "Good luck with the next pull.",
    ];

    public static bool TryStartAmbient(GameBot starter, AutonomousBotChat.Context context)
    {
        if (!Eligible(starter))
            return false;
        if (!AutonomousBotVoices.Enabled)
            return TryStartAmbientLegacy(starter, context);
        return TryStartBudgeted(starter, context);
    }

    /// <summary>A channel a bot could start a line in now, with its band and what it carries.</summary>
    [ThreadStatic]
    private static AmbientChoice[] _ambientOptions;

    private readonly record struct AmbientChoice(AutonomousChatBudget.Channel Channel, long Key, (double Floor, double Target, double Ceiling) Band,
        double Measured);

    /// <summary>
    /// Beta 1.1.3, the ambient budget: a bot with something to say picks the channel it can be heard in that runs furthest
    /// under its target (/faction, the realm's /lfg or /trade, /say near a player, /broadcast in a town), and starts a line
    /// there when that channel's gap is over, at once when it runs under its floor, and never above its ceiling. The targets
    /// scale with the hour and with the realm's bots online, without a cap (chat-budget.json, docs/bot-voices/CHAT-BUDGETS.md).
    /// </summary>
    private static bool TryStartBudgeted(GameBot starter, AutonomousBotChat.Context context)
    {
        long now = GameLoop.GameLoopTime;
        int realmBots = RealmBots(starter.Realm);
        var meter = AutonomousChatBudget.Meter;
        AmbientChoice[] options = _ambientOptions ??= new AmbientChoice[5];
        int count = 0;
        void Add(AutonomousChatBudget.Channel channel, long key)
        {
            var band = AutonomousChatBudget.LiveBand(channel, realmBots);
            if (band.Target > 0.0005)
                options[count++] = new AmbientChoice(channel, key, band, meter.PerMinute(channel, key, now));
        }
        // A running debate scene owns the realm's /faction for its duration (AutonomousBotDebates); a player's line there is
        // not followed by an unprompted one for a moment, so it isn't read as an answer.
        bool factionAudience = HasFactionAudience(starter.Realm);
        bool factionFree = factionAudience && !AutonomousBotDebates.IsRunning(starter.Realm) &&
                           !QuietAfterPlayer(LastPlayerFactionLine.GetValueOrDefault(starter.Realm), now);
        if (factionFree)
        {
            Add(AutonomousChatBudget.Channel.Faction, (long)starter.Realm);
            Add(AutonomousChatBudget.Channel.Lfg, (long)starter.Realm);
            Add(AutonomousChatBudget.Channel.Trade, (long)starter.Realm);
        }
        if (HasLocalAudience(starter))
            Add(AutonomousChatBudget.Channel.Say, starter.CurrentRegionID);
        List<GamePlayer> broadcastListeners = null;
        if (BroadcastsHere(starter) && (broadcastListeners = AutonomousChatBroadcast.Listeners(starter)).Count > 0)
            Add(AutonomousChatBroadcast.ChannelOf(starter), AutonomousChatBroadcast.KeyOf(starter));
        if (count == 0)
            return false;
        // The channel furthest under its target goes first; the rest are tried in turn when its gap isn't over.
        var random = Random.Shared;
        for (int tries = 0; tries < count; tries++)
        {
            double total = 0;
            for (int i = 0; i < count; i++)
                total += Weight(options[i]);
            double draw = random.NextDouble() * total;
            int pick = 0;
            for (; pick < count - 1; pick++)
            {
                if (draw < Weight(options[pick]))
                    break;
                draw -= Weight(options[pick]);
            }
            AmbientChoice choice = options[pick];
            options[pick] = options[count - 1];
            options[count - 1] = choice with { Band = (0, 0, 0) };
            var verdict = meter.TryStart(choice.Channel, choice.Key, now, choice.Band, random);
            if (verdict == AutonomousChatBudget.Verdict.HoldCeiling)
            {
                AutonomousChatWhy.CountHeld(choice.Channel, AutonomousChatWhy.Held.Ceiling);
                continue;
            }
            if (verdict == AutonomousChatBudget.Verdict.HoldGap)
            {
                AutonomousChatWhy.CountHeld(choice.Channel, AutonomousChatWhy.Held.Gap);
                continue;
            }
            if (SpeakIn(starter, context, choice, verdict == AutonomousChatBudget.Verdict.SpeakUnderFloor, broadcastListeners))
                return true;
            AutonomousChatWhy.CountHeld(choice.Channel, AutonomousChatWhy.Held.NoLine);
        }
        return false;

        static double Weight(AmbientChoice option) => AutonomousChatBudget.ChoiceWeight(option.Band.Target, option.Measured);
    }

    /// <summary>Whether a bot might /broadcast where it stands (the listeners then say whether it is a broadcast area): not in the
    /// frontier or a dungeon, and only where a player is in the region.</summary>
    private static bool BroadcastsHere(GameBot bot) =>
        bot.CurrentRegion != null && !bot.CurrentRegion.IsRvR && bot.CurrentZone?.IsRvR != true && !AutonomousBotVoices.InDungeon(bot) &&
        ClientService.Instance.GetPlayersOfRegion(bot.CurrentRegion).Count > 0;

    /// <summary>Says a bot's unprompted line in the channel the budget chose. False when it has nothing fitting to say there.</summary>
    private static bool SpeakIn(GameBot starter, AutonomousBotChat.Context context, AmbientChoice choice, bool underFloor, List<GamePlayer> listeners)
    {
        string reason = (underFloor ? "the channel ran under its floor" : "its turn in the channel's gap") +
                        $" ({choice.Measured:0.#}/min, target {choice.Band.Target:0.#}, ceiling {choice.Band.Ceiling:0.#})";
        switch (choice.Channel)
        {
            case AutonomousChatBudget.Channel.Lfg:
            case AutonomousChatBudget.Channel.Trade:
            {
                string kind = choice.Channel == AutonomousChatBudget.Channel.Lfg ? "lfg" : "trade";
                // Beta 1.1.3, round 3: mostly a field line from the bot's real level, class, camp and bags.
                if (TryField(starter, kind, choice, reason))
                    return true;
                // A written lfg or trade line is never a crafted one (round 3).
                if (!AutonomousBotVoices.TryLine(starter, kind, out VC.Picked picked, filter: AmbientLineFits(false)) || !Fresh(choice, picked.Text))
                    return false;
                SendRealmChannel(starter, kind, picked.Text);
                Note(kind + ":" + starter.Realm, starter, picked.Text, picked.Line?.Invites);
                AutonomousChatWhy.Record(starter, kind, "ambient", "said", RuleOf(picked.Line, kind), VC.Specificity(picked.Line), reason, picked.Text, picked.Line?.Id);
                return true;
            }
            case AutonomousChatBudget.Channel.Capital:
            case AutonomousChatBudget.Channel.Town:
            {
                // 2003 town broadcast was 70-85% task: WTS, LFG, recruiting (research, texture rule 8).
                if (TryField(starter, "broadcast", choice, reason, listeners))
                    return true;
                double roll = Random.Shared.NextDouble();
                string kind = roll < 0.5 ? "trade" : roll < 0.75 ? "lfg" : "ambient";
                if (!AutonomousBotVoices.TryLine(starter, kind, out VC.Picked picked, flags: ["in_town"], filter: AmbientLineFits(false)) &&
                    !AutonomousBotVoices.TryLine(starter, kind == "ambient" ? "trade" : "ambient", out picked, flags: ["in_town"], filter: AmbientLineFits(false)))
                    return false;
                if (!Fresh(choice, picked.Text) || !AutonomousChatBroadcast.Send(starter, picked.Text, listeners))
                    return false;
                AutonomousChatWhy.Record(starter, "broadcast", "ambient", "said", RuleOf(picked.Line, kind), VC.Specificity(picked.Line), reason, picked.Text, picked.Line?.Id);
                if (picked.Line?.Kind == "trade")
                    AutonomousChatBroadcast.StartMacro(starter, picked.Text, AutonomousChatBudget.Current, Random.Shared);
                return true;
            }
            default:
            {
                bool faction = choice.Channel == AutonomousChatBudget.Channel.Faction;
                if (TryField(starter, faction ? "faction" : "say", choice, reason))
                    return true;
                string opening = BuildAmbientOpening(starter, context, faction, out VC.Picked picked);
                if (string.IsNullOrWhiteSpace(opening) || !Fresh(choice, opening))
                    return false;
                if (faction)
                    BroadcastFaction(starter.Name, starter.Realm, opening);
                else if (!SayLine(starter, opening))
                    return false;
                AutonomousChatWhy.Record(starter, faction ? "faction" : "say", "ambient", "said", RuleOf(picked?.Line, "ambient"), VC.Specificity(picked?.Line),
                    reason, opening, picked?.Line?.Id);
                Note(faction ? AutonomousChatThreads.Faction(starter.Realm) : AutonomousChatThreads.Say(starter.CurrentRegionID), starter, opening,
                    picked?.Line?.Invites);
                SendCorrection(starter, faction, picked?.Correction);
                ScheduleBotReplies(starter, opening, faction, false, picked?.Line);
                return true;
            }
        }
    }

    /// <summary>
    /// Ambient lines a channel may carry (Beta 1.1.3 filler rules): lore musings and crafted lines only as a rare /faction
    /// opener, the Realm Exchange only when someone asks where to buy or sell. Drawn once per opener.
    /// </summary>
    internal static Func<VC.Line, bool> AmbientLineFits(bool faction) => AutonomousChatFiller.AmbientFilter(faction, Random.Shared);

    /// <summary>
    /// A field line (Beta 1.1.3, blind round 3): short and functional, from the bot's real state and the realm's real keeps and
    /// relics (AutonomousChatFieldTalk), for most unprompted lines. False when the draw goes to the corpus or the facts give
    /// nothing to say.
    /// </summary>
    private static bool TryField(GameBot starter, string channel, AmbientChoice choice, string reason, List<GamePlayer> listeners = null)
    {
        var random = Random.Shared;
        if (!AutonomousBotVoices.Enabled || random.NextDouble() >= AutonomousChatFieldTalk.Share(channel))
            return false;
        AutonomousChatFieldTalk.Said said = AutonomousChatFieldTalk.Line(channel, AutonomousChatFieldTalk.Of(starter, random),
            AutonomousChatFieldTalk.MemoryOf(starter.Realm), random);
        if (said == null)
            return false;
        string text = AutonomousChatIdiolect.Apply(said.Text, starter.DatabaseID, false, random, terse: true);
        if (!Fresh(choice, text))
            return false;
        switch (channel)
        {
            case "lfg":
            case "trade":
                SendRealmChannel(starter, channel, text);
                Note(channel + ":" + starter.Realm, starter, text, null);
                break;
            case "broadcast":
                if (!AutonomousChatBroadcast.Send(starter, text, listeners))
                    return false;
                if (said.Kind == "wts")
                    AutonomousChatBroadcast.StartMacro(starter, text, AutonomousChatBudget.Current, random);
                break;
            case "faction":
                BroadcastFaction(starter.Name, starter.Realm, text);
                Note(AutonomousChatThreads.Faction(starter.Realm), starter, text, null);
                break;
            default:
                if (!SayLine(starter, text))
                    return false;
                Note(AutonomousChatThreads.Say(starter.CurrentRegionID), starter, text, null);
                break;
        }
        AutonomousChatWhy.Record(starter, channel, "ambient", "said", "field " + said.Kind, AutonomousChatReplyFit.Known, reason, text, null);
        if (channel is "faction" or "say")
            ScheduleFieldAnswer(starter, said, channel == "faction");
        return true;
    }

    /// <summary>
    /// One short, true answer to a field line from another bot now and then: "who has beno?" gets "mids", "inc bled" gets
    /// "omw", "ding 30" gets "gz". Scans a slice of the roster, not all of it.
    /// </summary>
    private static void ScheduleFieldAnswer(GameBot starter, AutonomousChatFieldTalk.Said said, bool faction)
    {
        var random = Random.Shared;
        if (said.Answer == null || random.NextDouble() >= AutonomousChatFieldTalk.AnswerChance(said.Kind))
            return;
        GameBot[] roster = AutonomousBotRegistry.Snapshot();
        if (roster.Length < 2)
            return;
        GameBot responder = null;
        for (int i = 0, at = random.Next(roster.Length); i < Math.Min(roster.Length, 96); i++, at = (at + 1) % roster.Length)
        {
            GameBot bot = roster[at];
            if (bot != starter && bot.Realm == starter.Realm && Eligible(bot) &&
                (faction || bot.CurrentRegion == starter.CurrentRegion && bot.IsWithinRadius(starter, WorldMgr.SAY_DISTANCE)))
            {
                responder = bot;
                break;
            }
        }
        if (responder == null)
            return;
        string answer = AutonomousChatIdiolect.Apply(said.Answer, responder.DatabaseID, false, random, terse: true);
        Schedule(responder, ReplyDelayMs(answer, 0, random), () =>
        {
            if (faction && HasFactionAudience(responder.Realm))
                BroadcastFaction(responder.Name, responder.Realm, answer);
            else if (!faction && HasLocalAudience(responder))
                SayLine(responder, answer);
            else
                return;
            AutonomousChatWhy.Record(responder, faction ? "faction" : "say", said.Text, "said", "field answer " + said.Kind, AutonomousChatReplyFit.Known,
                "a short true answer to a field line", answer, null);
        });
    }

    /// <summary>A line that is not a near-copy of one the channel carried in the last half hour (Beta 1.1.3).</summary>
    private static bool Fresh(AmbientChoice choice, string text)
    {
        if (AutonomousChatFiller.Recent.TryAdd(((int)choice.Channel << 24) ^ choice.Key, text, GameLoop.GameLoopTime))
            return true;
        AutonomousChatWhy.CountHeld(choice.Channel, AutonomousChatWhy.Held.Duplicate);
        return false;
    }

    /// <summary>A bot's /say line, counted against the region's /say budget.</summary>
    internal static bool SayLine(GameBot bot, string text)
    {
        if (bot == null || !bot.Say(text))
            return false;
        AutonomousChatBudget.Meter.Count(AutonomousChatBudget.Channel.Say, bot.CurrentRegionID, GameLoop.GameLoopTime);
        return true;
    }

    private static bool TryStartAmbientLegacy(GameBot starter, AutonomousBotChat.Context context)
    {
        if (!Eligible(starter))
            return false;

        // A running debate scene owns the realm's /faction for its duration (AutonomousBotDebates).
        bool factionAudience = HasFactionAudience(starter.Realm) && !AutonomousBotDebates.IsRunning(starter.Realm);
        bool localAudience = HasLocalAudience(starter);
        if (!factionAudience && !localAudience)
            return false;

        bool faction = factionAudience && (!localAudience || Random.Shared.NextDouble() < 0.24);
        bool voices = AutonomousBotVoices.Enabled;
        lock (Sync)
        {
            long now = GameLoop.GameLoopTime;
            if (faction)
            {
                if (QuietAfterPlayer(LastPlayerFactionLine.GetValueOrDefault(starter.Realm), now))
                    return false;
                long gap = FactionGap.GetValueOrDefault(starter.Realm, 180_000);
                if (LastFactionAmbient.TryGetValue(starter.Realm, out long last) && now - last < gap)
                    return false;
                LastFactionAmbient[starter.Realm] = now;
                FactionGap[starter.Realm] = 180_000;
            }
            else
            {
                long gap = LocalGap.GetValueOrDefault(starter.CurrentRegionID, 90_000);
                if (LastLocalAmbient.TryGetValue(starter.CurrentRegionID, out long last) && now - last < gap)
                    return false;
                LastLocalAmbient[starter.CurrentRegionID] = now;
                LocalGap[starter.CurrentRegionID] = 90_000;
            }
        }

        string opening = BuildAmbientOpening(starter, context, faction, out VC.Picked picked);
        // The talking update: with voices on, a bot with nothing true to say stays quiet rather than falling back to the old
        // formal lines ("How fares everyone across the realm?").
        if (string.IsNullOrWhiteSpace(opening))
            return false;
        // The talking update: most "lfg" and "trade" lines go to the realm's /lfg and /trade channels, as players used them.
        string channel = faction && voices ? picked?.Line?.Kind : null;
        if (channel is "lfg" or "trade" && Random.Shared.NextDouble() < 0.7)
        {
            SendRealmChannel(starter, channel, opening);
            Note(channel + ":" + starter.Realm, starter, opening, picked?.Line?.Invites);
            AutonomousChatWhy.Record(starter, channel, "ambient", "said", RuleOf(picked?.Line, "ambient"), VC.Specificity(picked?.Line),
                "its turn in the realm's gap; an " + channel + " line goes to its channel", opening, picked?.Line?.Id);
            return true;
        }
        if (faction)
            BroadcastFaction(starter.Name, starter.Realm, opening);
        else if (!starter.Say(opening))
            return false;
        AutonomousChatWhy.Record(starter, faction ? "faction" : "say", "ambient", "said", RuleOf(picked?.Line, "ambient"), VC.Specificity(picked?.Line),
            faction ? "its turn in the realm's /faction gap" : "its turn in the region's /say gap, a player is near", opening, picked?.Line?.Id);
        Note(faction ? AutonomousChatThreads.Faction(starter.Realm) : AutonomousChatThreads.Say(starter.CurrentRegionID), starter, opening,
            picked?.Line?.Invites);
        SendCorrection(starter, faction, picked?.Correction);

        ScheduleBotReplies(starter, opening, faction, false, picked?.Line);
        return true;
    }

    /// <summary>
    /// The talking update: a bot tells /faction about an event in its own words (a "news" corpus line of the topic, filled with
    /// the event's names), and others react ("again??", "omw"). <paramref name="witness"/>: the speaker was there. False when
    /// no line fits or nobody can say it (the caller sends its written notice instead).
    /// </summary>
    public static bool TrySayEventNews(GameBot preferred, bool witness, string topic, IReadOnlyDictionary<string, string> slots)
    {
        if (!AutonomousBotVoices.Enabled || string.IsNullOrEmpty(topic) || preferred == null || !HasFactionAudience(preferred.Realm))
            return false;
        GameBot speaker = Eligible(preferred) ? preferred : null;
        if (speaker == null)
        {
            witness = false;
            speaker = AutonomousBotRegistry.Snapshot().Where(bot => bot.Realm == preferred.Realm && Eligible(bot))
                .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        }
        return speaker != null && SayNews(speaker, witness, topic, slots);
    }

    /// <summary>World news no realm notice told (AutonomousChatNews): said by a bot that was near it if there is one.</summary>
    public static bool TellNews(ChatNewsItem item)
    {
        if (item == null || !AutonomousBotVoices.Enabled || !HasFactionAudience(item.Realm))
            return false;
        GameBot[] bots = AutonomousBotRegistry.Snapshot().Where(bot => bot.Realm == item.Realm && Eligible(bot)).ToArray();
        GameBot witness = item.RegionId == 0 ? null : bots
            .Where(bot => bot.CurrentRegionID == item.RegionId &&
                          (long)(bot.X - item.X) * (bot.X - item.X) + (long)(bot.Y - item.Y) * (bot.Y - item.Y) < 8_000L * 8_000)
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        GameBot speaker = witness ?? bots
            // Frontier news is told by a frontier regular, the rest by anyone.
            .OrderByDescending(bot => item.Topic.StartsWith("keep") || item.Topic.StartsWith("relic") || item.Topic.StartsWith("df")
                ? bot.Level >= 35 ? 1 : 0 : 0)
            .ThenBy(_ => Random.Shared.Next()).FirstOrDefault();
        return speaker != null && SayNews(speaker, witness != null, item.Topic, item.Slots());
    }

    private static bool SayNews(GameBot speaker, bool witness, string topic, IReadOnlyDictionary<string, string> slots)
    {
        if (!AutonomousBotVoices.TryLine(speaker, "news", out VC.Picked picked, topic: topic, slots: slots,
                flags: witness ? ["witness"] : null, withoutFlags: witness ? null : ["witness"]))
            return false;
        lock (Sync)
            LastFactionAmbient[speaker.Realm] = GameLoop.GameLoopTime;
        BroadcastFaction(speaker.Name, speaker.Realm, picked.Text);
        Note(AutonomousChatThreads.Faction(speaker.Realm), speaker, picked.Text, picked.Line?.Invites);
        AutonomousChatWhy.Record(speaker, "faction", "news " + topic, "said", RuleOf(picked.Line, "news"), VC.Specificity(picked.Line),
            witness ? "it was there" : "it heard the news", picked.Text, picked.Line?.Id);
        SendCorrection(speaker, true, picked.Correction);
        // Beta 1.1.3: a big moment (a keep, a relic, a raid's end, Darkness Falls) draws a burst of reactions, and the capital hears it.
        if (AutonomousChatBursts.BurstsOn(topic))
            AutonomousChatBursts.React(speaker, speaker.Realm, topic, picked.Text, ["news_" + topic, "news"], slots, AutonomousChatBursts.ReachesTowns(topic));
        else
            ScheduleBotReplies(speaker, picked.Text, true, false, picked.Line);
        return true;
    }

    /// <summary>
    /// The gap between bot-started /faction lines the ambient budget gives (Beta 1.1.3): a minute over the channel's target
    /// for the realm's bots online and the hour, varied, rising without a cap with more bots (Beta 1.1.1 capped it at about
    /// 390 a realm), scaled by bot_voices_chat_rate (0 = none). Pure but for the budget file.
    /// </summary>
    public static long FactionGapMs(int realmBotsOnline, int localHour, double rate, Random random) =>
        AutonomousChatBudget.GapMs(AutonomousChatBudget.Band(AutonomousChatBudget.Current, AutonomousChatBudget.Channel.Faction, realmBotsOnline,
            localHour, false, rate).Target, random);

    /// <summary>The gap between bot-started /say lines near a player the ambient budget gives. Pure but for the budget file.</summary>
    public static long LocalGapMs(int realmBotsOnline, int localHour, double rate, Random random) =>
        AutonomousChatBudget.GapMs(AutonomousChatBudget.Band(AutonomousChatBudget.Current, AutonomousChatBudget.Channel.Say, realmBotsOnline,
            localHour, false, rate).Target, random);

    /// <summary>How busy chat is by the hour (the budget's day curve, prime time 1).</summary>
    public static double HourFactor(int hour) => AutonomousChatBudget.HourFactor(AutonomousChatBudget.Current, hour, false);

    private static readonly Dictionary<eRealm, (int Count, long At)> RealmCounts = new();

    /// <summary>A bot's typing speed, fixed from its id and personality (chat-budget.json wpm_min/wpm_max).</summary>
    internal static int Wpm(GameBot bot) =>
        AutonomousChatBudget.TypingWpm(AutonomousChatBudget.Current, bot?.DatabaseID ?? 0, bot == null ? BotPersonalityType.Leveler : AutonomousBotPersonality.For(bot).Type);

    /// <summary>The realm's gamebots online (cached 30 s).</summary>
    internal static int RealmBots(eRealm realm) => RealmBotsOnline(realm);

    private static int RealmBotsOnline(eRealm realm)
    {
        long now = GameLoop.GameLoopTime;
        lock (RealmCounts)
        {
            if (RealmCounts.TryGetValue(realm, out var cached) && now - cached.At < 30_000)
                return cached.Count;
        }
        int count = AutonomousBotRegistry.CountByRealm().GetValueOrDefault(realm);
        lock (RealmCounts)
            RealmCounts[realm] = (count, now);
        return count;
    }

    /// <summary>A bot's line in its realm's /lfg or /trade channel, formatted as the server's /lfg and /trade commands do.</summary>
    private static void SendRealmChannel(GameBot bot, string channel, string text)
    {
        string safe = (text ?? string.Empty).Trim();
        if (safe.Length == 0)
            return;
        if (safe.Length > 240)
            safe = safe[..240];
        bool lfg = channel == "lfg";
        string formatted = lfg ? $"[LFG] {bot.Name} ({bot.Level}, {bot.CharacterClass?.Name ?? bot.ClassName}): {safe}" : $"[Trade] {bot.Name}: {safe}";
        foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRealm(bot.Realm))
            player.Out.SendMessage(formatted, lfg ? eChatType.CT_LFG : eChatType.CT_Trade, eChatLoc.CL_ChatWindow);
        BotChatTranscript.Bot(channel, bot.Name, bot.Realm, safe);
        AutonomousChatBudget.Meter.Count(lfg ? AutonomousChatBudget.Channel.Lfg : AutonomousChatBudget.Channel.Trade, (long)bot.Realm, GameLoop.GameLoopTime);
    }

    /// <summary>Remembers what a bot said in a channel, so the player's next line can follow it up (AutonomousChatThreads).</summary>
    internal static void Note(string channel, GameBot bot, string text, string invites, string to = null) =>
        AutonomousChatThreads.Log.Record(channel, bot?.DatabaseID ?? 0, bot?.Name, text, invites, GameLoop.GameLoopTime, to);

    /// <summary>Where a conversation thread is: realm chat, /say near the player, or the player's /group.</summary>
    internal enum ThreadScope { Faction, Say, Group }

    /// <summary>Whether a bot is in the conversation's audience: anyone in /faction, near the player in /say, a groupmate in /group.</summary>
    private static bool Hears(GameBot bot, GamePlayer player, ThreadScope scope) => scope switch
    {
        ThreadScope.Faction => true,
        ThreadScope.Say => bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE),
        _ => bot.Group != null && bot.Group == player.Group,
    };

    /// <summary>
    /// The talking update: a player's line read as part of the conversation before anything else. "why?", "really?", "where?"
    /// right after a bot's line is answered by that bot, or by the bot named ("Aldwin why?"); an answer to a bot's open
    /// question is thanked by the asker ("ty, heading there"); "ty" right after a bot answered the player goes to that bot, a
    /// laugh after a bot's joke to the joker. Other bots leave it be. False when the line starts nothing of the kind.
    /// </summary>
    internal static bool TryThreadReply(GamePlayer player, string message, string channel, ThreadScope scope)
    {
        if (!AutonomousBotVoices.Enabled || player == null || string.IsNullOrEmpty(channel))
            return false;
        using AutonomousChatFiller.ReplyScope fillerScope = AutonomousChatFiller.Replying(message, channel, GameLoop.GameLoopTime);
        long now = GameLoop.GameLoopTime;
        GameBot Speaker(long id) => BotById(id, player.Realm) is { } bot && Hears(bot, player, scope) ? bot : null;
        var named = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player.Name };
        // "Aldwin why?", "rly Aldwin?": a follow-up said to a bot by name, about that bot's last line (here or elsewhere, in the
        // last few minutes).
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot().Where(b => b.Realm == player.Realm && ContainsMention(message, b.Name)).Take(2))
        {
            string unnamed = AutonomousChatThreads.WithoutName(message, bot.Name);
            string namedWord = AutonomousChatThreads.FollowUpWord(unnamed);
            if (namedWord == "y")
                namedWord = "why";
            ChatThreadLog.Said theirs = namedWord == null ? null : AutonomousChatThreads.Log.LastBy(bot.DatabaseID, now, AutonomousChatThreads.TellMemoryMs);
            if (theirs == null || theirs.Closed || !Eligible(bot) || !Hears(bot, player, scope))
                continue;
            // "Aldric 50 yet?" is a question to Aldric, not a check on its last line (review 2026-10-26: it drew "yep, thats it").
            if (namedWord == "confirm" && !AutonomousChatThreads.ConfirmFits(unnamed, theirs.Text))
                continue;
            string answer = FollowUpReply(bot, player, namedWord, theirs);
            if (answer == null)
                continue;
            SendThreadLine(bot, player, scope, channel, answer, "", "thread followup_" + namedWord);
            return true;
        }
        // Beta 1.1.3: "how about 45?", "too much", "lol no" right after a bot's price ("60g for the set"): the seller answers with a
        // price or a no.
        if (AutonomousChatThreads.Log.Last(channel, now, AutonomousChatThreads.FollowUpWindowMs) is { Closed: false } priced &&
            AutonomousChatContext.Price(priced.Text) is { } asking && AutonomousChatContext.CounterOffer(message, asking, Random.Shared) is { } counter &&
            Speaker(priced.BotId) is { } seller)
        {
            SendThreadLine(seller, player, scope, channel, AutonomousBotVoices.StyleFor(seller, counter), "", "context counter");
            return true;
        }
        // "x", "me", "inv pls" right after a bot's "lfm vendo need a tank pst": that bot says whether it will group, with the real
        // reasons (the holdout check: "x" read as nothing).
        if (AutonomousChatThreads.JoinsCall(message) &&
            AutonomousChatThreads.Log.Last(channel, now, AutonomousChatThreads.FollowUpWindowMs) is { Closed: false } call &&
            AutonomousChatThreads.CallsForGroup(call.Text) && Speaker(call.BotId) is { } caller)
        {
            AutonomousChatThreads.Log.Close(call);
            SendThreadLine(caller, player, scope, channel, AutonomousBotVoices.StyleFor(caller, JoinReply(caller, player, Random.Shared, call.Text)), "none", "join");
            return true;
        }
        string word = AutonomousChatThreads.FollowUpWord(message);
        // "y" after a question is a yes (an answer, below); after a remark it is "why".
        if (word == "y")
            word = AutonomousChatThreads.Log.Last(channel, now, AutonomousChatThreads.FollowUpWindowMs) is { Asked: true } ? null : "why";
        if (word == "confirm" && !(AutonomousChatThreads.Log.Last(channel, now, AutonomousChatThreads.FollowUpWindowMs) is { } checkedLine &&
                                   AutonomousChatThreads.ConfirmFits(message, checkedLine.Text)))
            word = null;
        if (word != null)
        {
            ChatThreadLog.Said last = AutonomousChatThreads.Log.Last(channel, now, AutonomousChatThreads.FollowUpWindowMs);
            GameBot bot = last == null || last.Closed ? null : Speaker(last.BotId);
            string reply = bot == null ? null : FollowUpReply(bot, player, word, last);
            if (reply == null)
                return false;
            SendThreadLine(bot, player, scope, channel, reply, "", "thread followup_" + word);
            return true;
        }
        eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
        // "no thanks im good" to a bot's offer ("want some buffs?") is an answer to it, not thanks (the audit: it drew "yw").
        if (AutonomousChatThreads.SaysYes(message) == false &&
            AutonomousChatThreads.Log.OpenQuestion(channel, now, AutonomousChatThreads.AnswerWindowMs) is { } offered &&
            Speaker(offered.BotId) is { } offerer)
        {
            AutonomousChatThreads.Log.Close(offered);
            if (Random.Shared.NextDouble() < 0.7)
                foreach (string answers in AutonomousChatThreads.ClosingSets(offered, message))
                    if (AutonomousBotVoices.TryLine(offerer, "closing", out VC.Picked fine, answers: answers, relax: true, slots: named, filter: NotAPromise))
                    {
                        SendThreadLine(offerer, player, scope, channel, fine.Text, "", "thread decline");
                        break;
                    }
            return true;
        }
        // "ty" right after a bot answered the player is for that bot: it says "np" (before, a realm-wide "ty" went only to
        // groupmates, so the bot that had just helped said nothing).
        if (intent == eAutonomousChatIntent.Thanks && !message.Contains('?') &&
            AutonomousChatThreads.Log.LastTo(channel, player.Name, now, AutonomousChatThreads.ThanksWindowMs) is { } helped &&
            Speaker(helped.BotId) is { } helper)
        {
            // "ty ty" for the bot's "gz on the ding", "ty again" after its "np": the exchange is done (the holdout check).
            if (AutonomousChatThreads.EndsAnExchange(helped.Text))
                return true;
            if (Random.Shared.NextDouble() < 0.7 && AutonomousBotVoices.TryLine(helper, "reply", out VC.Picked np,
                    AutonomousBotVoices.IntentName(eAutonomousChatIntent.Thanks), relax: true, slots: named))
                SendThreadLine(helper, player, scope, channel, np.Text, "", "thread thanks");
            return true;
        }
        // A laugh right after a bot's joke, boast or complaint is for that bot: it may say something back, others leave it.
        if (intent == eAutonomousChatIntent.Laugh &&
            AutonomousChatThreads.Log.Last(channel, now, AutonomousChatThreads.LaughWindowMs) is { } funny &&
            funny.Family is "joke" or "boast" or "complaint" or "chat" && Speaker(funny.BotId) is { } joker)
        {
            if (Random.Shared.NextDouble() < 0.35 && AutonomousBotVoices.TryLine(joker, "reply", out VC.Picked back,
                    AutonomousBotVoices.IntentName(eAutonomousChatIntent.Laugh), relax: true, slots: named,
                    filter: line => AutonomousChatThreads.FitsOwnJoke(line.Text)))
                SendThreadLine(joker, player, scope, channel, back.Text, "", "thread laugh");
            return true;
        }
        ChatThreadLog.Said question = AutonomousChatThreads.Log.OpenQuestion(channel, now, AutonomousChatThreads.AnswerWindowMs);
        if (question == null || !AutonomousChatThreads.ReadsAsAnswer(message, intent))
            return false;
        // A line that names another bot is said to that bot, not an answer to this one.
        if (AutonomousBotRegistry.Snapshot().Any(other => other.Realm == player.Realm && other.DatabaseID != question.BotId &&
                                                         ContainsMention(message, other.Name)))
            return false;
        AutonomousChatThreads.Log.Close(question);
        GameBot asker = Speaker(question.BotId);
        if (asker == null || Random.Shared.NextDouble() >= 0.85)
            return true;
        // "sec need pow" to "ready?": the asker waits (the audit: it drew "oh nice ty").
        if (AutonomousChatShortTalk.QuestionShape(message) == "player_wait" &&
            AutonomousBotVoices.TryLine(asker, "reply", out VC.Picked wait, answers: "player_wait", relax: true, slots: named))
        {
            SendThreadLine(asker, player, scope, channel, wait.Text, "", "thread wait");
            return true;
        }
        foreach (string answers in AutonomousChatThreads.ClosingSets(question, message))
            if (AutonomousBotVoices.TryLine(asker, "closing", out VC.Picked closing, answers: answers, relax: true, slots: named, filter: NotAPromise))
            {
                SendThreadLine(asker, player, scope, channel, closing.Text, "", "thread answer");
                break;
            }
        return true;
    }

    /// <summary>A bot's answer to "why?", "really?", "how?", "what?", "who?" or "where?" after its own line, or null.</summary>
    public static string FollowUpReply(GameBot bot, GamePlayer player, string word, ChatThreadLog.Said last)
    {
        if (word == "where")
            // Beta 1.1.3: "where?" after a line that named a place asks where that place is (blind round 1: "where?" after "try the
            // bone camp in Muspelheim" drew where the bot stood).
            return AutonomousBotVoices.StyleFor(bot, AutonomousChatThreads.WhereIsIt(last?.Text, AutonomousBotVoices.CapitalOf(bot.Realm), Random.Shared) ??
                                                     AutonomousChatLiveAnswers.WhereIAm(bot, player, Random.Shared));
        string family = last?.Family;
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player?.Name };
        if (!string.IsNullOrEmpty(family) && AutonomousBotVoices.TryLine(bot, "reply", out VC.Picked fit, answers: "followup_" + word,
                slots: slots, relax: true, filter: line => line.Topic == family))
            return fit.Text;
        return AutonomousBotVoices.TryLine(bot, "reply", out VC.Picked any, answers: "followup_" + word, slots: slots, relax: true,
            filter: line => line.Topic is "general" or "")
            ? any.Text
            : null;
    }

    private static GameBot BotById(long id, eRealm realm) => id <= 0 ? null
        : AutonomousBotRegistry.Snapshot().FirstOrDefault(bot => bot.DatabaseID == id && bot.Realm == realm && Eligible(bot));

    /// <summary>Sends a bot's line in a thread's channel after a typing pause, and remembers it.</summary>
    private static void SendThreadLine(GameBot bot, GamePlayer player, ThreadScope scope, string channel, string text, string invites,
        string rule = "thread")
    {
        _ = new ECSGameTimer(bot, _ =>
        {
            if (!Eligible(bot))
                return 0;
            if (scope == ThreadScope.Faction && HasFactionAudience(bot.Realm))
                BroadcastFaction(bot.Name, bot.Realm, text);
            else if (scope == ThreadScope.Say && HasLocalAudience(bot))
                SayLine(bot, text);
            else if (scope == ThreadScope.Group && bot.Group != null && bot.Group == player?.Group)
                AutonomousBotGroupChatter.Send(bot, text, remember: false);   // recorded once, below
            else
                return 0;
            Note(channel, bot, text, invites, player?.Name);
            AutonomousChatWhy.Record(bot, ScopeName(scope), "player " + player?.Name, "said", rule, AutonomousChatReplyFit.Of(rule),
                "follows on from the conversation", text, to: player?.Name);
            return 0;
        }, ReplyDelayMs(bot, null, text, 0, Random.Shared));
    }

    private static string ScopeName(ThreadScope scope) => scope switch { ThreadScope.Faction => "faction", ThreadScope.Say => "say", _ => "group" };

    /// <summary>A corpus line as a rule name for /lswhy: kind, topic, and what it answers.</summary>
    internal static string RuleOf(VC.Line line, string fallback)
    {
        if (line == null)
            return fallback;
        string answers = line.Answers.Length > 0 ? " answers " + string.Join(',', line.Answers) : string.Empty;
        string intent = string.IsNullOrEmpty(line.Intent) ? string.Empty : " intent " + line.Intent;
        return $"{line.Kind}/{(string.IsNullOrEmpty(line.Topic) ? "-" : line.Topic)}{intent}{answers}";
    }

    /// <summary>A typo-prone writer's "*word" fix, a moment after the line with the typo.</summary>
    private static void SendCorrection(GameBot speaker, bool faction, string correction)
    {
        if (string.IsNullOrEmpty(correction))
            return;
        _ = new ECSGameTimer(speaker, _ =>
        {
            if (Eligible(speaker))
            {
                if (faction && HasFactionAudience(speaker.Realm))
                    BroadcastFaction(speaker.Name, speaker.Realm, correction);
                else if (!faction && HasLocalAudience(speaker))
                    SayLine(speaker, correction);
            }
            return 0;
        }, 1_200 + Random.Shared.Next(1_600));
    }

    /// <summary>
    /// How long a reply takes to arrive: a moment to read the line, then typing at roughly 40-60 ms a character
    /// (0.36-se.4; replies came at fixed 1.3-1.8 s steps whatever their length). <paramref name="after"/> is when
    /// the previous reply was due, so replies stay in order.
    /// </summary>
    public static int ReplyDelayMs(string reply, int after, Random random) =>
        // Beta 1.1.3: reading, a think pause and typing at an average 40 words a minute (AutonomousChatArbiter.DelayMs); the
        // bot's own speed where the caller knows the bot.
        AutonomousChatArbiter.DelayMs(null, reply, 40, after, random);

    /// <summary>How long this bot takes to answer <paramref name="incoming"/> with <paramref name="reply"/> at its own typing speed.</summary>
    public static int ReplyDelayMs(GameBot bot, string incoming, string reply, int after, Random random) =>
        AutonomousChatArbiter.DelayMs(incoming, reply, Wpm(bot), after, random);

    public static void OnPlayerFactionChat(GamePlayer player, string message)
    {
        if (player == null || string.IsNullOrWhiteSpace(message))
            return;
        BotChatTranscript.Player("faction", player, message);
        SendFaction(player.Name, player.Realm, message);
        // Asking anyone for gold or items marks the player for the beggars (AutonomousBeggars).
        AutonomousBeggars.OnPlayerAsked(player, message, "faction");
        // Naming one side of a running storyline episode takes that side (AutonomousBotStorylines); a scam takes no side.
        bool tookSide = !(AutonomousBotVoices.Enabled && AutonomousChatScreen.IsSpam(message)) && AutonomousBotStorylines.OnPlayerFactionChat(player, message);
        AutonomousBotDebates.PlayerSpoke(player.Realm, player);
        lock (Sync)
            LastPlayerFactionLine[player.Realm] = GameLoop.GameLoopTime;
        // Beta 1.1.3: nobody answers spam or a scam (blind round 2: "your account has been flagged" drew "ya?").
        if (Screened(player, message, "faction"))
            return;
        // A side taken after an episode is answered by the storyline's own tells; a generic reply from the one named read
        // as a non-sequitur (in-game check 2026-10-27: "Aineella that was a ninja loot" drew "Coruscating Mine has been good").
        if (tookSide && !AutonomousBotDebates.IsRunning(player.Realm))
            return;
        if (AutonomousBotGuildSocial.TryAnswerGuildRequest(player, message, true))
            return;
        if (TryThreadReply(player, message, AutonomousChatThreads.Faction(player.Realm), ThreadScope.Faction))
            return;
        // A player who joins a running debate ("nah, necros are fine") is answered in it, by the side they took or the other.
        if (AutonomousBotDebates.TryAnswerPlayer(player, message))
            return;
        lock (Sync)
        {
            if (LastFactionPlayerResponse.TryGetValue(player.Realm, out long last) && GameLoop.GameLoopTime - last < 12_000 && !NeedsAnswer(message))
                return;
            LastFactionPlayerResponse[player.Realm] = GameLoop.GameLoopTime;
        }
        SchedulePlayerReplies(player, message, true);
    }

    public static void OnPlayerLocalChat(GamePlayer player, string message)
    {
        if (player == null || string.IsNullOrWhiteSpace(message) || player.CurrentRegion == null)
            return;
        BotChatTranscript.Player("say", player, message);
        // The screen first: a scam draws nothing, not even from a beggar (round 3).
        if (Screened(player, message, "say"))
            return;
        // Asking for gold or items near a beggar in a capital: the beggar takes it personally (AutonomousBeggars).
        if (AutonomousBeggars.OnPlayerSay(player, message))
            return;
        if (AutonomousChatIntentModel.Predict(message) == eAutonomousChatIntent.IgnoreOutOfWorld)
            return;
        if (AutonomousBotGuildSocial.TryAnswerGuildRequest(player, message, false))
            return;
        if (TryThreadReply(player, message, AutonomousChatThreads.Say(player.CurrentRegionID), ThreadScope.Say))
            return;
        lock (Sync)
        {
            if (LastLocalPlayerResponse.TryGetValue(player.CurrentRegionID, out long last) && GameLoop.GameLoopTime - last < 10_000 && !NeedsAnswer(message))
                return;
            LastLocalPlayerResponse[player.CurrentRegionID] = GameLoop.GameLoopTime;
        }
        SchedulePlayerReplies(player, message, false);
    }

    /// <summary>
    /// Beta 1.1.3, second pass: spam and scams (links, dollars, "account flagged", a fake GM, account or plat sales, leet noise)
    /// draw no answer on any channel. True when the line is screened out (and recorded for /lswhy).
    /// </summary>
    internal static bool Screened(GamePlayer player, string message, string channel)
    {
        if (!AutonomousBotVoices.Enabled || !AutonomousChatScreen.IsSpam(message))
            return false;
        AutonomousChatWhy.RecordSilence(player?.Realm ?? eRealm.None, channel, "player " + player?.Name, "none spam", 0, "spam or a scam draws no answer",
            player?.Name);
        return true;
    }

    /// <summary>
    /// A line the reply cooldown must not drop: a question or a request. Chat audit 2026-10-21: "hi" then "anyone want to
    /// group?" five seconds later got no answer, because any reply in the last 12 s silenced the next line.
    /// </summary>
    public static bool NeedsAnswer(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        string shape = AutonomousChatShortTalk.QuestionShape(message);
        if (shape == "ack")
            return false;
        return message.Contains('?') || shape != null ||
               AutonomousChatIntentModel.Predict(message) is eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Rez or
                   eAutonomousChatIntent.Alarm or eAutonomousChatIntent.Help or eAutonomousChatIntent.Trading;
    }

    /// <summary>
    /// Beta 1.1.3, round 2: a player's /advice question is answered by one experienced gamebot of the realm, in /advice, from
    /// the knowledge lookup or the bot's own facts (trainers, travel, binding, spec points). Nothing weak: an /advice answer
    /// either helps or isn't given. Today no bot answered /advice at all.
    /// </summary>
    public static void OnPlayerAdvice(GamePlayer player, string message)
    {
        if (!AutonomousBotVoices.Enabled || player == null || string.IsNullOrWhiteSpace(message) || Screened(player, message, "advice"))
            return;
        GameBot advisor = AutonomousBotRegistry.Snapshot().Where(bot => bot.Realm == player.Realm && Eligible(bot) && bot.Level >= 20)
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (advisor == null)
            return;
        ReplyChoice choice = AutonomousChatKnowledge.TryAnswer(player, message, out string knowledge)
            ? new ReplyChoice(AutonomousBotVoices.StyleFor(advisor, knowledge), "knowledge")
            : VoicedChoice(advisor, message, () => null, player, addressed: false, channel: "advice");
        if (string.IsNullOrWhiteSpace(choice.Text) || AutonomousChatReplyFit.Of(choice.Route, message) < AutonomousChatReplyFit.Specific ||
            AutonomousChatReplyBudget.IsBrushOff(choice.Text))
        {
            AutonomousChatWhy.RecordSilence(player.Realm, "advice", "player " + player.Name, choice.Route ?? "none", AutonomousChatReplyFit.Of(choice.Route, message),
                "an /advice answer needs to know the answer", player.Name);
            return;
        }
        string text = choice.Text;
        _ = new ECSGameTimer(advisor, _ =>
        {
            if (!Eligible(advisor))
                return 0;
            string realm = player.Realm switch { eRealm.Albion => "ALB", eRealm.Midgard => "MID", eRealm.Hibernia => "HIB", _ => "NONE" };
            foreach (GamePlayer listener in ClientService.Instance.GetPlayersOfRealm(player.Realm))
                ChatUtil.SendAdviceMessage(listener, "Social.SendAdvice.Msg.Channel", realm, advisor.Name, text);
            BotChatTranscript.Bot("advice", advisor.Name, advisor.Realm, text);
            AutonomousChatWhy.Record(advisor, "advice", "player " + player.Name, "said", choice.Route, AutonomousChatReplyFit.Of(choice.Route, message),
                "an advisor who knows answers", text, choice.Line?.Id, player.Name);
            return 0;
        }, ReplyDelayMs(advisor, message, text, 0, Random.Shared) + 2_000);
    }

    /// <summary>
    /// Beta 1.1.3, round 2: a player's /broadcast or /chatgroup line (an LFG post, a WTB, a price check, a question) is answered
    /// the way a player nearby would: one gamebot that heard it sends a tell, if it has something that fits. Banter there draws
    /// nothing. <paramref name="channel"/> is "broadcast" (bots in the same region within earshot of a town) or "chatgroup"
    /// (bots of the realm).
    /// </summary>
    public static void OnPlayerOpenChannel(GamePlayer player, string message, string channel)
    {
        if (!AutonomousBotVoices.Enabled || player?.CurrentRegion == null || string.IsNullOrWhiteSpace(message) || Screened(player, message, channel))
            return;
        BotChatTranscript.Player(channel, player, message);
        if (!NeedsAnswer(message) || AutonomousChatScreen.OtherAddressee(message, null) != null)
            return;
        bool broadcast = channel == "broadcast";
        GameBot bot = AutonomousBotRegistry.Snapshot()
            .Where(candidate => candidate.Realm == player.Realm && Eligible(candidate) &&
                                (!broadcast || candidate.CurrentRegion == player.CurrentRegion && candidate.IsWithinRadius(player, 6_000)))
            .OrderByDescending(candidate => AsksToJoinGroup(message) || BroadcastsLfg(message) ? -Math.Abs(candidate.Level - player.Level) : 0)
            .ThenBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (bot == null)
            return;
        ReplyChoice choice = AsksToJoinGroup(message) || BroadcastsLfg(message)
            ? new ReplyChoice(AutonomousBotVoices.StyleFor(bot, JoinReply(bot, player, Random.Shared, message)), "join")
            : AutonomousChatKnowledge.TryAnswer(player, message, out string knowledge)
                ? new ReplyChoice(AutonomousBotVoices.StyleFor(bot, knowledge), "knowledge")
                : Budgeted(VoicedChoice(bot, message, () => null, player, addressed: false, channel: channel), bot, message, false, channel, player);
        if (string.IsNullOrWhiteSpace(choice.Text))
        {
            AutonomousChatWhy.RecordSilence(player.Realm, channel, "player " + player.Name, choice.Route ?? "none", 0, "no reply fits it", player.Name);
            return;
        }
        string text = choice.Text;
        _ = new ECSGameTimer(bot, _ =>
        {
            if (!AutonomousLivingServer.IsSpeaker(bot) || !bot.IsAlive || player.ObjectState != GameObject.eObjectState.Active)
                return 0;
            AutonomousLivingServer.Tell(bot, player, text);
            AutonomousChatWhy.Record(bot, "tell", channel + " from " + player.Name, "said", choice.Route, AutonomousChatReplyFit.Of(choice.Route, message),
                "answered a " + channel + " line by tell, as a player nearby would", text, choice.Line?.Id, player.Name);
            return 0;
        }, ReplyDelayMs(bot, message, text, 0, Random.Shared) + 1_500);
    }

    public const int TellMinGapMs = 3_000;
    public const int TellBusyRetryMs = 5_000;
    public const int TellMaxWaitMs = 30_000;

    /// <summary>
    /// A player's /send to an autonomous gamebot (in-game check 2026-10-17: bots said "pst me" and "send me a tell",
    /// but a tell to a bot answered "not in the game"). The bot answers with a /send after a typing pause, waiting
    /// up to 30 s if it is fighting or casting. Players lead groups and bots join them, so a request to join is
    /// answered with an invitation to invite the bot.
    /// </summary>
    public static void OnPlayerTell(GamePlayer player, GameBot bot, string message)
    {
        if (player == null || bot == null || string.IsNullOrWhiteSpace(message))
            return;
        BotChatTranscript.Player("tell", player, message, bot.Name);
        // The screen first: a scam by tell draws nothing, not even from a beggar (round 3).
        if (Screened(player, message, "tell"))
            return;
        if (AutonomousBeggars.OnPlayerTell(player, bot, message))
            return;
        lock (Sync)
        {
            string key = player.Name + "|" + bot.Name;
            if (LastTellResponse.TryGetValue(key, out long last) && GameLoop.GameLoopTime - last < TellMinGapMs)
                return;
            LastTellResponse[key] = GameLoop.GameLoopTime;
        }
        // "aelwyn u in a guild?" by tell: the bot's own name is no part of the question (the holdout check).
        if (AutonomousChatThreads.WithoutName(message, bot.Name) is { Length: > 0 } unnamed)
            message = unnamed;
        eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
        if (intent == eAutonomousChatIntent.Abuse)
            AutonomousLivingServer.RecordAbuse([bot], player);
        long asked = GameLoop.GameLoopTime;
        // The talking update: the reply is worked out when it is sent, not when the tell arrived up to 30 s before (a bot that
        // walked on still answered "where are you?" with where it had been).
        _ = new ECSGameTimer(bot, _ =>
        {
            if (!AutonomousLivingServer.IsSpeaker(bot) || !bot.IsAlive || player.ObjectState != GameObject.eObjectState.Active)
                return 0;
            if (bot.InCombat || bot.IsCasting)
                return GameLoop.GameLoopTime - asked < TellMaxWaitMs ? TellBusyRetryMs : 0;
            ReplyChoice reply = Budgeted(TellReply(bot, player, message, intent), bot, message, true, "tell", player);
            if (!string.IsNullOrWhiteSpace(reply.Text))
            {
                // Beta 1.1.3: the reply is worked out after reading the tell, then typed at the bot's own speed.
                int typing = AutonomousBotVoices.Enabled ? (int)(reply.Text.Length / (Wpm(bot) * 5 / 60_000.0)) : 0;
                _ = new ECSGameTimer(bot, _ =>
                {
                    if (!AutonomousLivingServer.IsSpeaker(bot) || !bot.IsAlive || player.ObjectState != GameObject.eObjectState.Active)
                        return 0;
                    AutonomousLivingServer.Tell(bot, player, reply.Text);
                    AutonomousChatWhy.Record(bot, "tell", "tell from " + player.Name, reply.Route.StartsWith("ack", StringComparison.Ordinal) ? "ack" : "said",
                        reply.Route, AutonomousChatReplyFit.Of(reply.Route, message), "a tell to it gets an answer", reply.Text, reply.Line?.Id, player.Name);
                    return 0;
                }, Math.Max(1, typing));
            }
            else
                AutonomousChatWhy.Record(bot, "tell", "tell from " + player.Name, "silent", reply.Route, AutonomousChatReplyFit.Of(reply.Route, message),
                    "no reply fits", to: player.Name);
            return 0;
        }, AutonomousBotVoices.Enabled ? 1_000 + Math.Min(3_000, message.Length * 20) + Random.Shared.Next(1_500) : 2_500 + Random.Shared.Next(3_000));
    }

    /// <summary>
    /// A bot's answer to a player's tell. With voices on it continues the exchange: "why?" or "where?" after the bot's last line
    /// (in the tell or anywhere in the last few minutes) is a follow-up, and an answer to the bot's question gets thanks. Then
    /// joining, "where are you?", knowledge, memory and the reply corpus, all in the bot's own writing style.
    /// </summary>
    private static ReplyChoice TellReply(GameBot bot, GamePlayer player, string message, eAutonomousChatIntent intent)
    {
        bool voices = AutonomousBotVoices.Enabled;
        long now = GameLoop.GameLoopTime;
        string channel = AutonomousChatThreads.Tell(player.Name, bot.Name);
        if (voices)
        {
            string word = AutonomousChatThreads.FollowUpWord(message);
            ChatThreadLog.Said last = AutonomousChatThreads.Log.LastBy(bot.DatabaseID, now, AutonomousChatThreads.TellMemoryMs);
            // A "...?" checks the bot's line only when it is about that line, said in this tell (review 2026-10-26: "u 50?" by tell
            // drew "yep, thats it" from a level 30 that had spoken in /faction).
            if (word == "confirm" && (last == null || last.Channel != channel || !AutonomousChatThreads.ConfirmFits(message, last.Text)))
                word = null;
            if (word != null && last != null && FollowUpReply(bot, player, word, last) is { } followUp)
                return new ReplyChoice(followUp, "thread followup_" + word);
            // Beta 1.1.3: a counter-offer to the bot's own price gets a price or a no.
            if (last != null && AutonomousChatContext.Price(last.Text) is { } asking && AutonomousChatContext.CounterOffer(message, asking, Random.Shared) is { } counter)
                return new ReplyChoice(AutonomousBotVoices.StyleFor(bot, counter), "context counter");
            ChatThreadLog.Said question = AutonomousChatThreads.Log.OpenQuestion(channel, now, AutonomousChatThreads.TellMemoryMs);
            // A duo or help offer the bot made: "sure" is answered with how to group (the player invites), "nah" with an ok.
            if (question != null && question.Invites == AutonomousLivingServer.AskGroupInvite && !message.Contains('?') &&
                AutonomousChatThreads.SaysYes(message) is bool yes)
            {
                AutonomousChatThreads.Log.Close(question);
                if (yes)
                    return new ReplyChoice(AutonomousBotVoices.StyleFor(bot, JoinReply(bot, player, Random.Shared)), "join");
                if (AutonomousBotVoices.TryLine(bot, "closing", out VC.Picked declined, answers: "lfg", relax: true, filter: NotAPromise))
                    return new ReplyChoice(declined.Text, "thread decline", declined.Line);
                return new ReplyChoice(AutonomousBotVoices.StyleFor(bot, "np, maybe later"), "thread decline");
            }
            if (question != null && AutonomousChatThreads.ReadsAsAnswer(message, intent))
            {
                AutonomousChatThreads.Log.Close(question);
                foreach (string answers in AutonomousChatThreads.ClosingAnswers(question.Invites))
                    if (AutonomousBotVoices.TryLine(bot, "closing", out VC.Picked closing, answers: answers, relax: true, filter: NotAPromise))
                        return new ReplyChoice(closing.Text, "thread answer", closing.Line);
            }
        }
        // With voices on, "ty"/"lol"/"hi" get a short reply rather than a knowledge lookup on their words; a question still gets one.
        bool shortTalk = voices && !message.Contains('?') && intent is eAutonomousChatIntent.Thanks or
            eAutonomousChatIntent.Laugh or eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Congrats or
            eAutonomousChatIntent.BotCheck or eAutonomousChatIntent.Away or eAutonomousChatIntent.Back;
        // An invite the bot turned down for a duty ("busy with a siege") says so; only one that never came is "try again".
        if (SaysInvited(message))
            return new ReplyChoice(AutonomousBotVoices.StyleFor(bot, bot.Group != null && bot.Group == player.Group
                ? Random.Shared.Next(2) == 0 ? "got it, omw" : "ty, on my way"
                : bot.PlayerGroupLeader != null && bot.PlayerGroupLeader != player || BotGroupInvite.DutyOf(bot) != BotGroupInvite.Duty.None
                    ? JoinReply(bot, player, Random.Shared)
                    : Random.Shared.Next(2) == 0 ? "didnt get anything, try again?" : "no invite came through, send it again?"), "tell invited");
        if (AsksToJoinGroup(message))
            return new ReplyChoice(AutonomousBotVoices.StyleFor(bot, JoinReply(bot, player, Random.Shared, message)), "join");
        if (AsksWhereYouAre(message))
            return new ReplyChoice(AutonomousBotVoices.StyleFor(bot, WhereText(Clean(bot.CurrentZone?.Description, "the frontier"),
                bot.CurrentZone != null && bot.CurrentZone == player.CurrentZone, bot.Level, bot.CharacterClass?.Name ?? "adventurer", Random.Shared)),
                "live where");
        if (voices && AutonomousChatLiveAnswers.AnswerQuestion(bot, message, true, Random.Shared) is { } fact)
            return new ReplyChoice(fact, "fact " + (AutonomousChatQuestions.SelfQuestion(message) ?? "world"));
        if (!shortTalk && AutonomousChatKnowledge.TryAnswer(player, message, out string knowledge))
            return new ReplyChoice(AutonomousBotVoices.StyleFor(bot, knowledge), "knowledge");
        return KnownChoice(bot, player, message, () => VoicedChoice(bot, message, () => GenerateDirectedReply(message), player, addressed: true, channel: "tell"));
    }

    /// <summary>"heading to vendo tonight", "anyone up for emain", "lets do df": a line that plans something. Pure.</summary>
    public static bool PlansSomething(string message) =>
        System.Text.RegularExpressions.Regex.IsMatch(AutonomousChatQuestions.Normalize(message),
            @"\b(going|heading|tonight|later|anyone|anybody|any1|lfg|lfm|want|wanna|lets|let s|run|doing|up for|plan|planning|forming|meet)\b");

    /// <summary>"lfg 24 druid", "lfm vendo": a broadcast for a group, not a question to the bot nearby.</summary>
    public static bool BroadcastsLfg(string message)
    {
        string text = AutonomousChatQuestions.Normalize(message);
        return System.Text.RegularExpressions.Regex.IsMatch(text, @"^(lfg|lfm|lf\dm|lf|looking for)\b") ||
               System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(lfg|lfm|lf\dm)\b");
    }

    /// <summary>Whether a bot fills the role a group asked for ("healer", "tank", "cc", "caster"; null fits all).</summary>
    public static bool FillsRole(GameBot bot, string role)
    {
        if (role == null || bot.CharacterClass == null)
            return true;
        var cls = (eCharacterClass)bot.CharacterClass.ID;
        return role switch
        {
            "healer" => BotPartyRoles.IsHealingClass(cls),
            "tank" => BotPartyRoles.For(cls) == BotPartyRole.Tank,
            "cc" => cls is eCharacterClass.Sorcerer or eCharacterClass.Minstrel or eCharacterClass.Bard or eCharacterClass.Healer or
                eCharacterClass.Mentalist or eCharacterClass.Skald or eCharacterClass.Enchanter or eCharacterClass.Eldritch,
            "caster" => AutonomousBotVoices.IsCasterClass(cls),
            "puller" => cls is eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger || BotPartyRoles.For(cls) == BotPartyRole.Tank,
            _ => true,
        };
    }

    /// <summary>"sent u an inv", "invited you", "check ur invite": the player already sent the invitation. Pure.</summary>
    public static bool SaysInvited(string message)
    {
        string text = " " + AutonomousChatQuestions.Normalize(message) + " ";
        // "i invited bob, hes coming" is about someone else (review 2026-10-26).
        return new[] { " sent u an inv", " sent you an inv", " sent u inv", " sent an inv", " invited u ", " invited you ", " check ur inv",
            " check your inv", " invited ya ", " inv d u ", " invd u ", " invd you ", " sent u a group inv", " sent you a group inv" }.Any(text.Contains) &&
               !text.Contains(" guild ");
    }

    /// <summary>"inv?", "can i join", "room for a cleric?" - not a guild request.</summary>
    public static bool AsksToJoinGroup(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        string text = " " + new string(message.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";
        if (text.Contains(" guild ") || text.Contains(" ginvite "))
            return false;
        // "ty for the group" and "good xp spot for 25?" are not requests (audit 2026-10-18); "ty, inv pls" is.
        if (new[] { " for the group ", " for the grp ", " for the invite ", " for the inv ", " for grouping ", " for the party " }.Any(text.Contains))
            return false;
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        text = text.Replace(" grp ", " group ").Replace(" grps ", " group ");
        if (new[] { " inv ", " invite ", " join ", " lfg ", " room for ", " duo ", " team up ", " lf1m ", " lfm ", " want in ",
                " u interested ", " you interested ", " need a tank ", " need a healer ", " need one ", " come join ", " join us ",
                " looking for a 7th ", " looking for an 8th ", " looking for one more ",
                // "you wanna come to emain with us tonight" (the holdout check)
                " come with us ", " with us tonight ", " run with us ", " roll with us ", " hunt with us ", " wanna come along ", " want to come along ",
                " u wanna come ", " you wanna come ", " want to come with ", " wanna come with ", " wanna tag along ", " want to tag along ", " tag along ", " can i come ", " can i join ", " mind if i join ", " mind if i tag " }.Any(text.Contains))
            return !new[] { " inv full ", " inventory " }.Any(text.Contains);
        // "u in?", "you in?" at the end: are you joining.
        if (text.EndsWith(" u in ") || text.EndsWith(" you in ") || text.EndsWith(" ya in "))
            return true;
        if (!text.Contains(" group ") && !text.Contains(" grp "))
            return false;
        // The audit (2026-10-26): "how was your group?" and "my old group quit" by tell drew "sure, send me an invite". A group
        // that is talked about is not a request; "want to group?", "group up?", "can i group with you" are.
        if (new[] { " my group ", " our group ", " your group ", " ur group ", " the group ", " old group ", " last group ", " that group ",
                " a group of ", " group was ", " group is ", " group xp ", " group heal ", " group quit ", " group left ", " group wiped " }.Any(text.Contains))
            return false;
        return message.TrimEnd().EndsWith('?') ||
               new[] { " want to group ", " wanna group ", " want a group ", " need a group ", " can i group ", " group up ", " group with ",
                   " you group ", " u group ", " lets group ", " let s group ", " looking to group ", " up for a group " }.Any(text.Contains);
    }

    /// <summary>
    /// "where are you?", "where r u", "wru", "where you at", "your loc?" - a question about the bot itself, not about
    /// a place or a trainer (in game 2026-10-18 a bot asked "where are you?" by /send answered "What did you need?").
    /// </summary>
    public static bool AsksWhereYouAre(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        string text = " " + new string(message.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        return new[] { " where are you ", " where r you ", " where are u ", " where r u ", " where you at ", " where u at ",
                " wru ", " your loc ", " ur loc ", " where are ya ", " where you hunting ", " where are you hunting " }
            .Any(text.Contains);
    }

    /// <summary>The bot's zone, level and class, the way a player would answer "where are you?".</summary>
    public static string WhereText(string zone, bool sameZone, int level, string className, Random random)
    {
        string cls = (className ?? "adventurer").ToLowerInvariant();
        if (sameZone)
            return random.Next(2) == 0 ? $"in {zone} too, other end of it. {level} {cls}" : $"also in {zone}, {level} {cls}, come find me";
        return random.Next(3) switch
        {
            0 => $"im in {zone}, {level} {cls}",
            1 => $"{zone} right now, {level} {cls}",
            _ => $"out in {zone}. you?",
        };
    }

    /// <summary>
    /// What a bot says to a group request across a big level gap, or null when the levels are close enough to share xp (within
    /// 8). Pure.
    /// </summary>
    public static string LevelGapReply(int botLevel, int playerLevel, Random random)
    {
        int gap = botLevel - playerLevel;
        if (Math.Abs(gap) <= 8)
            return null;
        if (gap > 0)
            return random.Next(3) switch
            {
                0 => $"im {botLevel}, you would get no xp with me. happy to help with something though",
                1 => $"im {botLevel} lol, way too high for you. need help with a quest or something?",
                _ => $"{botLevel} here, the xp would be awful for you. try someone closer to {playerLevel}",
            };
        return random.Next(2) == 0
            ? $"im only {botLevel}, i would just die out there with you"
            : $"im {botLevel}, too low for your camps sorry";
    }

    /// <summary>How long a bot named in a group ask while fighting or riding may take to answer.</summary>
    public const int NamedWhileFightingWaitMs = 90_000;

    /// <summary>A join reply said after the fight the bot was in when asked. Pure.</summary>
    public static string AfterAFight(string reply, Random random, bool riding = false) =>
        (riding ? random.Next(2) == 0 ? "was on a horse, " : "just got off the horse, "
            : random.Next(3) switch { 0 => "sorry, was fighting. ", 1 => "had a pull, ", _ => "just saw this, " }) + reply;

    /// <summary>How often a group call nobody near that level could answer still draws one honest reply.</summary>
    public const double NoneNearLevelReplyChance = 0.8;

    /// <summary>What a bot says to a group call when no bot is near that level (or that role). Pure.</summary>
    public static string NoneNearLevelReply(int level, string role, Random random)
    {
        if (role != null)
            return random.Next(2) == 0 ? $"no {role}s around your level that i know of, sorry" : $"havent seen a {role} your level on in a while";
        if (level < 10)
            return random.Next(4) switch
            {
                0 => "not many lowbies on right now, sorry",
                1 => "everyone's 40+ atm heh. solo a few levels, it goes quick",
                2 => "no lowbies around that i know of, gl",
                _ => "most of the realm is 50 right now, cant help sorry",
            };
        return random.Next(3) switch
        {
            0 => "nobody around your level that i know of, sorry",
            1 => "quiet at that level right now, gl",
            _ => "dont think anyone your level is on, sorry",
        };
    }

    public static string JoinReply(GameBot bot, GamePlayer player, Random random, string message = null)
    {
        // "45+ for the dragon, x me": a bot outside the band says so instead of joining (Beta 1.1.3, round 3).
        if (BandReply(bot.Level, message, random) is { } band)
            return band;
        if (bot.PlayerGroupLeader != null && bot.PlayerGroupLeader != player)
            return random.Next(2) == 0 ? "sorry, already grouped up with someone" : "cant right now, im in a group";
        // The talking update: a bot the invite would refuse (siege, relic, raid...) doesn't say "send me an invite".
        if (BotGroupInvite.DutyOf(bot) is not BotGroupInvite.Duty.None and var duty)
            return duty switch
            {
                BotGroupInvite.Duty.Siege => "cant right now, holding a keep",
                BotGroupInvite.Duty.Relic => "cant, got a relic to move",
                BotGroupInvite.Duty.RealmRaid => "cant, signed up for the raid",
                BotGroupInvite.Duty.RvrEvent => "cant right now, out with the realm",
                BotGroupInvite.Duty.StableRoute => "on a horse right now, catch me after",
                _ => "cant right now, sorry",
            };
        // The audit (2026-10-26): a 48 said "sure, send me an invite" to a 10. A player would mention the gap first.
        if (LevelGapReply(bot.Level, player.Level, random) is { } gap)
            return gap;
        if (!BotGroupInvite.CloseEnoughToInviteByName(player, bot))
        {
            string where = Clean(bot.CurrentZone?.Description, "the other side of the realm");
            if (bot.CurrentZone != null && bot.CurrentZone == player.CurrentZone)   // in-game check 2026-10-17: "im in Snowdonia" to a player in Snowdonia
                return random.Next(2) == 0 ? $"im a ways off, other side of {where}. come find me and inv" : "sure, but im not close. meet up and inv me";
            return random.Next(2) == 0 ? $"im out in {where}, invite me when you are closer" : $"sure, but im in {where}. meet up and inv me";
        }
        return random.Next(3) switch
        {
            0 => "sure, send me an invite",
            1 => "inv me and ill come to you",
            _ => "ok, invite me",
        };
    }

    /// <summary>The bot a player's /say line is aimed at without its name (AutonomousChatScreen.SayAddressee), or null.</summary>
    private static GameBot SayAddressee(GamePlayer player, GameBot[] near)
    {
        if (near.Length == 0)
            return null;
        long now = GameLoop.GameLoopTime;
        long lastSpeaker = AutonomousChatThreads.Log.Last(AutonomousChatThreads.Say(player.CurrentRegionID), now, AutonomousChatScreen.FollowsBotLineMs)?.BotId ?? 0;
        var bots = new AutonomousChatScreen.Near[near.Length];
        for (int i = 0; i < near.Length; i++)
        {
            GameBot bot = near[i];
            bots[i] = new AutonomousChatScreen.Near(bot.DatabaseID, player.GetDistanceTo(bot), player.IsObjectInFront(bot, 90),
                player.TargetObject == bot, lastSpeaker != 0 && bot.DatabaseID == lastSpeaker);
        }
        int others = 0;
        foreach (GamePlayer other in ClientService.Instance.GetPlayersOfRegion(player.CurrentRegion))
            if (other != player && other.IsWithinRadius(player, (int)AutonomousChatScreen.CloseRadius))
                others++;
        long? id = AutonomousChatScreen.SayAddressee(bots, others);
        return id is { } found ? near.FirstOrDefault(bot => bot.DatabaseID == found) : null;
    }

    /// <summary>
    /// A bot's answer to a group or raid with a level band it is outside ("45+" to a 30, "20-25" to a 40): "too low lol", "im
    /// only 30", "too high, im 40". Null when there is no band or the bot is inside it. Pure.
    /// </summary>
    public static string BandReply(int level, string message, Random random)
    {
        if (AutonomousChatShortTalk.LevelBand(message) is not { } band || level >= band.Min && level <= band.Max)
            return null;
        return level < band.Min
            ? random.Next(3) switch { 0 => "too low lol", 1 => $"im only {level}", _ => $"{level} here, too low sry" }
            : random.Next(2) == 0 ? $"too high for that, im {level}" : $"{level} lol, too high";
    }

    /// <summary>
    /// Whether a listing's name reads like an item a player would advertise. The level 50 starter kits are item
    /// templates named like "Offline level 50 Cleric 41/33" (provision_level50_roster.py); in-game check
    /// 2026-10-18: "Does anybody want to make me an offer on Offline level 50 Cleric 41/33?" in /faction.
    /// </summary>
    public static bool IsAdvertisableItemName(string name) =>
        !string.IsNullOrWhiteSpace(name) && !name.TrimStart().StartsWith("Offline level ", StringComparison.OrdinalIgnoreCase);

    public static bool TryAdvertiseExchangeItem(GameBot seller, string exactItemName)
    {
        // A running scene owns /faction (in-game check 2026-10-27: an exchange advert landed in a storyline's second line).
        if (!Eligible(seller) || !IsAdvertisableItemName(exactItemName) || !HasFactionAudience(seller.Realm) ||
            AutonomousBotDebates.IsRunning(seller.Realm))
            return false;
        lock (Sync)
        {
            if (LastFactionAmbient.TryGetValue(seller.Realm, out long last) && GameLoop.GameLoopTime - last < 180_000)
                return false;
            LastFactionAmbient[seller.Realm] = GameLoop.GameLoopTime;
        }

        string item = exactItemName.Trim();
        // With voices on, the seller says a corpus trade line about the listing (0.36-se.4: these lines were
        // written but never used, while every advert was one of two fixed sentences).
        VC.Picked picked = null;
        string opening = AutonomousBotVoices.TryLine(seller, "trade", out picked, flags: ["has_listing"], requireNeed: "has_listing",
                slots: new Dictionary<string, string>(StringComparer.Ordinal) { ["item"] = item })
            ? picked.Text
            : Random.Shared.Next(2) == 0
                ? $"Does anybody want to make me an offer on {item}?"
                : $"How much do u think {item} is worth?";
        BroadcastFaction(seller.Name, seller.Realm, opening);
        ScheduleBotReplies(seller, opening, true, false, picked?.Line);
        return true;
    }

    public static string GenerateFactionReply(string incoming, Random random = null)
    {
        random ??= Random.Shared;
        if (incoming.Contains("offer on", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("how much", StringComparison.OrdinalIgnoreCase) && incoming.Contains("worth", StringComparison.OrdinalIgnoreCase))
        {
            return random.Next(5) switch
            {
                0 => "Compare its level and bonuses before setting the price.",
                1 => "The Realm Exchange should show what similar gear is worth.",
                2 => "That may be useful to someone leveling nearby.",
                3 => "A merchant price can provide a reasonable minimum value.",
                _ => "I might offer a few silver if it suits my class.",
            };
        }
        if (incoming.Contains("mythic", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains(" broken", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains(" op", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("best class", StringComparison.OrdinalIgnoreCase))
        {
            return random.Next(8) switch
            {
                0 => "Every class has strengths that show in the right group.",
                1 => "Which specialization are you considering?",
                2 => "That ability may work better with the right positioning.",
                3 => "It is worth asking someone who plays that class often.",
                4 => "Equipment and specialization can change how the class feels.",
                5 => "I would compare how it performs solo and in a group.",
                6 => "A careful pull usually tells you more than the delve text.",
                _ => "Try the class and see which playstyle suits you.",
            };
        }
        if (incoming.Contains("lag", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains(" dc", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("disconnect", StringComparison.OrdinalIgnoreCase))
        {
            return random.Next(5) switch
            {
                0 => "Movement seems steady where I am.",
                1 => "I noticed a short delay too; it seems better now.",
                2 => "Give the area a moment to finish loading.",
                3 => "Hopefully it clears before your next pull.",
                _ => "Stay with your group until movement catches up.",
            };
        }
        return AutonomousChatIntentModel.Predict(incoming) switch
        {
            eAutonomousChatIntent.Greeting => random.Next(2) == 0 ? "Greetings. Good hunting out there." : "Hello there. The realm is awake today.",
            eAutonomousChatIntent.Banter => random.Next(2) == 0 ? "Good luck on your next pull." : "Save some of that energy for the frontier.",
            eAutonomousChatIntent.Abuse => random.Next(3) switch { 0 => "Let us keep faction chat helpful.", 1 => "I would rather focus on the realm.", _ => "Good hunting when you head back out." },
            eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Help => random.Next(2) == 0 ? "Share your level and location so nearby adventurers can help." : "I may be free once I finish my current task.",
            eAutonomousChatIntent.Travel => random.Next(2) == 0 ? "A stable master may shorten the trip." : "Check the road signs and travel with your group.",
            eAutonomousChatIntent.RvR => random.Next(2) == 0 ? "I have heard the frontier has some movement." : "A few defenders may head out before long.",
            eAutonomousChatIntent.Trading => random.Next(2) == 0 ? "The Realm Exchange may have something useful." : "I have seen a little trade around the capital.",
            eAutonomousChatIntent.Dungeon => "A careful group should clear one pull at a time on the way inside.",
            eAutonomousChatIntent.Grinding => "Share the camp and level if you are looking for company.",
            eAutonomousChatIntent.Farewell => "Safe travels. Until next time.",
            eAutonomousChatIntent.IgnoreOutOfWorld => "Let us keep faction chat focused on the realm.",
            _ => FactionReplies[random.Next(FactionReplies.Length)],
        };
    }

    public static string GenerateLocalReply(GameBot bot, string incoming, Random random = null)
    {
        random ??= Random.Shared;
        string activity = Clean(bot?.PersistentRecord?.Activity, "taking a short rest");
        string target = Clean(bot?.PersistentRecord?.TargetName, "the creatures nearby");
        string destination = Clean(bot?.PersistentRecord?.TravelDestination, bot?.CurrentZone?.Description ?? "this area");

        return AutonomousChatIntentModel.Predict(incoming) switch
        {
            eAutonomousChatIntent.Greeting => random.Next(2) == 0 ? $"Hello. I am keeping an eye on {target}." : $"Greetings. I am {LowerFirst(activity)}.",
            eAutonomousChatIntent.Banter => random.Next(2) == 0 ? "Good luck on the next pull." : "Keep your weapon ready and stay with the group.",
            eAutonomousChatIntent.Abuse => random.Next(3) switch { 0 => "Let us keep this helpful.", 1 => "I am focusing on my current task.", _ => "Safe travels when you move on." },
            eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Help => bot?.Group == null ? $"I am working around {target} for now, but I could group." : "I am already moving with a group right now.",
            eAutonomousChatIntent.Travel => $"I am headed toward {destination}.",
            eAutonomousChatIntent.Dungeon => $"We should clear one pull at a time if we head deeper. I am {LowerFirst(activity)}.",
            eAutonomousChatIntent.RvR => "If I head to the frontier, I will stay with the realm group.",
            eAutonomousChatIntent.Trading => "I will check my real inventory and the Realm Exchange when I return to town.",
            eAutonomousChatIntent.Grinding => $"I am focused on {target} at the moment.",
            eAutonomousChatIntent.Farewell => "Safe travels. I will keep working here.",
            eAutonomousChatIntent.IgnoreOutOfWorld => "Let us keep the conversation focused on the realm.",
            _ => random.Next(3) switch
            {
                0 => $"I am focused on {target} at the moment.",
                1 => $"Right now I am {LowerFirst(activity)}.",
                _ => $"I will be around {destination} for a while.",
            },
        };
    }

    public static int RollResponseCount(int eligibleCount, Random random = null)
    {
        if (eligibleCount <= 0)
            return 0;
        random ??= Random.Shared;
        return Math.Min(eligibleCount, random.NextDouble() < 0.32 ? 2 : 1);
    }

    public static bool ContainsMention(string message, string name)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(name))
            return false;
        int start = 0;
        while ((start = message.IndexOf(name, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = start + name.Length;
            bool left = start == 0 || !char.IsLetterOrDigit(message[start - 1]);
            bool right = end == message.Length || !char.IsLetterOrDigit(message[end]);
            if (left && right)
                return true;
            start = end;
        }
        return false;
    }

    public static void BroadcastFaction(string speakerName, eRealm realm, string message) =>
        BroadcastFactionAsWritten(speakerName, realm, AutonomousRoleplayers.VoiceByName(speakerName, realm, message));

    /// <summary>A bot's /faction line exactly as written (a roleplayer's own in-character line skips the speech pass).</summary>
    public static void BroadcastFactionAsWritten(string speakerName, eRealm realm, string message)
    {
        string safe = SendFaction(speakerName, realm, message);
        if (safe != null)
        {
            BotChatTranscript.Bot("faction", speakerName, realm, safe);
            AutonomousChatBudget.Meter.Count(AutonomousChatBudget.Channel.Faction, (long)realm, GameLoop.GameLoopTime);
        }
    }

    private static string SendFaction(string speakerName, eRealm realm, string message)
    {
        string safe = (message ?? string.Empty).Trim();
        if (safe.Length == 0)
            return null;
        if (safe.Length > 240)
            safe = safe[..240];
        string formatted = $"({speakerName}) <Faction> {safe}";
        foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRealm(realm))
            player.Out.SendMessage(formatted, eChatType.CT_Broadcast, eChatLoc.CL_ChatWindow);
        return safe;
    }

    /// <summary>
    /// How many bots answer a player's line. Greetings, dings and "wb" draw a few answers, as in a real realm;
    /// laughs and "brb" in /faction usually none (0.36-se.4; every line used to get 1-2 replies).
    /// </summary>
    public static int PlayerReplyCount(eAutonomousChatIntent intent, bool faction, bool directed, int available, Random random)
    {
        if (available <= 0)
            return 0;
        if (directed)
            return 1;
        double roll = random.NextDouble();
        int count = intent switch
        {
            eAutonomousChatIntent.Greeting => faction ? (roll < 0.35 ? 3 : roll < 0.8 ? 2 : 1) : (roll < 0.5 ? 2 : 1),
            eAutonomousChatIntent.Ding => faction ? (roll < 0.3 ? 3 : roll < 0.75 ? 2 : 1) : (roll < 0.45 ? 2 : 1),
            eAutonomousChatIntent.Back => roll < 0.4 ? 2 : 1,
            eAutonomousChatIntent.Farewell => faction ? (roll < 0.3 ? 2 : 1) : 1,
            // The talking update audit: a lone "lol" or "heh" in /faction drew "glad someone finds it funny" from a stranger, and
            // "brb" in /faction drew "k hurry back". A laugh at a bot's joke goes to that bot (the thread); the realm stays quiet.
            // Beta 1.1.3, second pass: short laughs and banter come back at a modest rate (blind round 2: most replies lost to
            // silence were these); the reply budget then answers with a short reaction or nothing.
            eAutonomousChatIntent.Laugh => faction ? roll < 0.15 ? 1 : 0 : roll < 0.6 ? 1 : 0,
            eAutonomousChatIntent.Away => faction ? (roll < 0.08 ? 1 : 0) : 1,
            eAutonomousChatIntent.Banter => faction ? (roll < 0.5 ? 1 : 0) : roll < 0.85 ? 1 : 0,
            eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Congrats => faction ? (roll < 0.55 ? 1 : 0) : 1,
            eAutonomousChatIntent.Alarm => faction ? (roll < 0.5 ? 2 : 1) : 1,
            eAutonomousChatIntent.IgnoreOutOfWorld or eAutonomousChatIntent.Abuse or eAutonomousChatIntent.BotCheck => 1,
            _ => roll < 0.32 ? 2 : 1,
        };
        return Math.Min(available, count);
    }

    private static void SchedulePlayerReplies(GamePlayer player, string message, bool faction)
    {
        eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
        bool voices = AutonomousBotVoices.Enabled;
        bool shortTalk = voices && !message.Contains('?') && intent is eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Congrats or
            eAutonomousChatIntent.Ding or eAutonomousChatIntent.Laugh or eAutonomousChatIntent.Away or eAutonomousChatIntent.Back or
            eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.BotCheck;
        string knowledge = null;
        bool hasKnowledge = !shortTalk && AutonomousChatKnowledge.TryAnswer(player, message, out knowledge);
        GameBot[] eligible = AutonomousBotRegistry.Snapshot()
            .Where(Eligible)
            .Where(bot => bot.Realm == player.Realm)
            .Where(bot => faction || bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE))
            .OrderByDescending(bot => PlayerRelevance(bot, player, message, intent, faction) - (faction && AnsweredRecently(player.Realm, bot) ? 6 : 0))
            .ThenBy(_ => Random.Shared.Next())
            .ToArray();
        GameBot[] mentioned = eligible.Where(bot => ContainsMention(message, bot.Name)).ToArray();
        bool directed = mentioned.Length > 0;
        // Beta 1.1.3, round 3: a /say line is for one bot also when the player targets it, answers its line, or stands right in
        // front of it with nobody else that close. That bot answers, once.
        if (voices && !faction && !directed && SayAddressee(player, eligible) is { } addressee)
        {
            mentioned = [addressee];
            directed = true;
        }
        // "torvald u still a thane?", "hail and well met siobhan": the name says who it is for, and is then no part of what is
        // said (the holdout check: names read as unknown words and hid the question or greeting).
        if (voices && directed && AutonomousChatThreads.WithoutName(message, mentioned[0].Name) is { Length: > 0 } unnamed && unnamed != message)
        {
            message = unnamed;
            intent = AutonomousChatIntentModel.Predict(message);
            shortTalk = !message.Contains('?') && intent is eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Congrats or
                eAutonomousChatIntent.Ding or eAutonomousChatIntent.Laugh or eAutonomousChatIntent.Away or eAutonomousChatIntent.Back or
                eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.BotCheck;
            knowledge = null;
            hasKnowledge = !shortTalk && AutonomousChatKnowledge.TryAnswer(player, message, out knowledge);
        }
        string shape = voices ? AutonomousChatShortTalk.QuestionShape(message) : null;
        // "ok", "nice", "me too" draw no answer; "anyone in camelot?" is answered by bots that are there, or by nobody
        // (chat audit 2026-10-21).
        if (shape == "ack")
        {
            AutonomousChatWhy.RecordSilence(player.Realm, faction ? "faction" : "say", "player " + player.Name, "none ack", 0,
                "an acknowledgement draws no answer", player.Name);
            return;
        }
        if (shape == "ask_inplace" && !hasKnowledge)
        {
            string place = AutonomousChatShortTalk.PlaceAsked(message);
            eligible = (directed ? mentioned : eligible).Where(bot => AutonomousChatLiveAnswers.IsIn(bot, place)).ToArray();
            mentioned = mentioned.Where(eligible.Contains).ToArray();
            directed = mentioned.Length > 0;
        }
        if (directed)
            eligible = mentioned;
        // The audit: "wb Sigrun" and "Aldric pull the next one" are for the bot named; said to nobody we know, nobody answers.
        if (voices && !directed && shape is "player_wb" or "player_order")
        {
            AutonomousChatWhy.RecordSilence(player.Realm, faction ? "faction" : "say", "player " + player.Name, "none undirected", 0,
                "said to nobody a bot knows", player.Name);
            return;
        }
        // "gz Aldric" to a bot that didn't just level: it wouldn't know what for, so it says nothing.
        if (voices && directed && intent == eAutonomousChatIntent.Congrats)
            eligible = eligible.Where(AutonomousBotVoices.JustLeveled).ToArray();
        // Play test 2026-10-27: "Yrilda wanna group?" in /faction drew nothing because Yrilda was busy (fighting or riding). A
        // player named mid-fight answers when the fight is over; the bot does the same, within a minute and a half.
        if (voices && faction && !directed && AsksToJoinGroup(message) &&
            AutonomousBotRegistry.Snapshot().FirstOrDefault(bot => bot.Realm == player.Realm && bot.IsAutonomousWorldBot &&
                !bot.IsTemporaryGroupHelper && bot.IsAlive && (bot.InCombat || bot.IsCasting || bot.IsOnStableMasterRoute || bot.IsOnHorse) &&
                ContainsMention(message, bot.Name)) is { } busy)
        {
            int waited = 0;
            bool riding = busy.IsOnStableMasterRoute || busy.IsOnHorse;
            _ = new ECSGameTimer(player, _ =>
            {
                if (!busy.IsAlive || busy.ObjectState != GameObject.eObjectState.Active)
                    return 0;
                if (!Eligible(busy))
                    return (waited += 5_000) < NamedWhileFightingWaitMs ? 5_000 : 0;
                string late = AutonomousBotVoices.StyleFor(busy, AfterAFight(JoinReply(busy, player, Random.Shared, message), Random.Shared, riding));
                if (!HasFactionAudience(busy.Realm))
                    return 0;
                BroadcastFaction(busy.Name, busy.Realm, late);
                Note(AutonomousChatThreads.Faction(busy.Realm), busy, late, "", player.Name);
                return 0;
            }, 5_000);
            return;
        }
        // Beta 1.1.3, second pass: a line said to someone else by name ("Caoimhe ty for the gems!!", "congrats to Tovah on 50!") is
        // theirs to answer (blind round 2: bots answered for them).
        if (voices && !directed && AutonomousChatScreen.OtherAddressee(message, null) is { } other)
        {
            AutonomousChatWhy.RecordSilence(player.Realm, faction ? "faction" : "say", "player " + player.Name, "none other", 0,
                $"said to {other}, not to a bot that heard it", player.Name);
            return;
        }
        // The talking update audit: "want to group?" in /say, "Aldric lfg?" in /faction: the bot asked says whether it will join,
        // with the real reasons (busy, far, a level gap), not a remark on grouping.
        if (voices && eligible.Length > 0 && AsksToJoinGroup(message) && (directed || !faction && !BroadcastsLfg(message)))
        {
            GameBot asked = directed ? mentioned[0] : eligible.OrderBy(bot => bot.GetDistanceTo(player)).First();
            string answer = AutonomousBotVoices.StyleFor(asked, JoinReply(asked, player, Random.Shared, message));
            _ = new ECSGameTimer(player, _ =>
            {
                if (!Eligible(asked))
                    return 0;
                if (faction && HasFactionAudience(asked.Realm))
                    BroadcastFaction(asked.Name, asked.Realm, answer);
                else if (!faction && HasLocalAudience(asked))
                    SayLine(asked, answer);
                else
                    return 0;
                Note(faction ? AutonomousChatThreads.Faction(asked.Realm) : AutonomousChatThreads.Say(asked.CurrentRegionID), asked, answer, "",
                    player.Name);
                return 0;
            }, ReplyDelayMs(answer, 0, Random.Shared));
            return;
        }
        string noneNearLevel = null;
        // A group looking for a role ("lfm vendo need healer pst") hears from bots of that role and about that level.
        if (voices && !directed && intent == eAutonomousChatIntent.Grouping)
        {
            string role = AutonomousChatShortTalk.NeededRole(message);
            int level = AutonomousChatLiveAnswers.LevelAsked(message, player.Level);
            GameBot[] fitting = eligible.Where(bot => Math.Abs(bot.Level - level) <= 6 && FillsRole(bot, role)).ToArray();
            // Free bots offer first ("friar here, invite me"): in game 2026-10-27 a friar busy in a five-bot party offered,
            // and answering the invite pulled it out of that party.
            GameBot[] free = fitting.Where(bot => bot.Group == null).ToArray();
            // Play test 2026-10-27: "lvl 2 viking lfg pst" and "any mid healers lfg near mularn?" drew no reply at all, because
            // no bot was within six levels. On a live server someone says so; one bot of the realm answers honestly.
            if (fitting.Length == 0 && eligible.Length > 0 && Random.Shared.NextDouble() < NoneNearLevelReplyChance)
            {
                noneNearLevel = NoneNearLevelReply(level, role, Random.Shared);
                eligible = eligible.OrderByDescending(bot => bot.CurrentRegion == player.CurrentRegion).Take(1).ToArray();
            }
            else
                eligible = free.Length > 0 ? free : fitting;
        }
        else if (voices && intent == eAutonomousChatIntent.Congrats)
            // "gz" is for whoever just levelled: only that bot says "ty" (a random bot thanking you would be odd).
            eligible = eligible.Where(AutonomousBotVoices.JustLeveled).ToArray();
        else if (voices && intent == eAutonomousChatIntent.Thanks && faction)
            // A realm-wide "ty" is answered by the groupmates it was for, not by strangers.
            eligible = eligible.Where(bot => bot.Group != null && bot.Group == player.Group).ToArray();
        // Living Server memory: bots that heard an insult remember it (chat only).
        if (intent == eAutonomousChatIntent.Abuse)
            AutonomousLivingServer.RecordAbuse(directed ? mentioned : eligible.Where(bot => !faction).Take(1), player);
        // With voices off, today's counts: one reply when named, for knowledge, real-world talk or abuse; else one or two.
        int count = hasKnowledge ? Math.Min(1, eligible.Length)
            : voices ? PlayerReplyCount(intent, faction, directed, eligible.Length, Random.Shared)
            : directed || intent is eAutonomousChatIntent.IgnoreOutOfWorld or eAutonomousChatIntent.Abuse ? Math.Min(1, eligible.Length)
            : RollResponseCount(eligible.Length);
        // A question the live world answers gets one answer: two bots both saying "big group of mids in Uppland" read as an echo
        // (live check 2026-10-21).
        if (shape is "ask_hunt" or "ask_enemy" or "ask_price" or "player_offer" or "player_wait")
            count = Math.Min(count, 1);
        if (noneNearLevel != null)
            count = Math.Min(1, eligible.Length);
        // A factual question gets one answer (or one honest "not sure"), not a chorus (the talking update audit).
        if (voices && AutonomousChatQuestions.IsFactual(message))
            count = Math.Min(count, 1);
        // The real level-up already drew grats (Living Server): "ding!" typed right after gets one more at most.
        if (voices && intent == eAutonomousChatIntent.Ding && !directed && AutonomousLivingServer.GratsSaidRecently(player.Name, 60_000))
            count = Math.Min(count, Random.Shared.NextDouble() < 0.5 ? 1 : 0);
        HashSet<string> scheduledResponses = new(StringComparer.OrdinalIgnoreCase);
        var spoken = new HashSet<GameBot>();
        int due = 0;
        int planned = 0;
        string lastRoute = null;
        string why = faction ? "faction" : "say";
        // Beta 1.1.3, arbitration: one primary answer; then, where a crowd would, a few pile-ons (more hellos for a hello, a
        // "lol" or "+1" for a joke or a remark), never a second answer to a question. Each reply arrives after its bot read the
        // line, thought and typed at its own speed.
        AutonomousChatArbiter.Plan plan = new(count > 0 ? 1 : 0, 0, 0);
        bool question = AutonomousChatQuestions.IsQuestion(message);
        int social = 0, reactions = 0;
        string reactionKind = null;
        for (int index = 0; index < eligible.Length && index < count + 3 && planned < plan.Total; index++)
        {
            GameBot responder = eligible[index];
            bool primary = planned == 0;
            ReplyChoice choice;
            if (!primary && reactions < plan.Reactions)
            {
                choice = new ReplyChoice(AutonomousBotVoices.StyleFor(responder, AutonomousChatArbiter.Reaction(reactionKind, Random.Shared)),
                    "pileon " + reactionKind);
                reactions++;
            }
            else
            {
                choice = noneNearLevel != null
                    ? new ReplyChoice(AutonomousBotVoices.StyleFor(responder, noneNearLevel), "live none_near_level")
                    : hasKnowledge
                    ? new ReplyChoice(AutonomousBotVoices.StyleFor(responder, knowledge), "knowledge")
                    : directed
                        ? KnownChoice(responder, player, message, () => VoicedChoice(responder, message, () => GenerateDirectedReply(message), player, intent,
                            addressed: true, channel: why))
                        // With voices on, a line no reply fits gets none: the old formal lines ("Things seem steady from where I am.")
                        // answered anything (chat audit 2026-10-21).
                        : VoicedChoice(responder, message, () => voices ? null : faction ? GenerateFactionReply(message) : GenerateLocalReply(responder, message),
                            // In /say a "u" is the bot nearby ("u selling that sword?"); in /faction it is nobody in particular.
                            player, intent, addressed: !faction, channel: why);
                // The reply budget: under the minimum fit, a bot named in the line acknowledges it and any other stays quiet.
                if (noneNearLevel == null && !hasKnowledge)
                    choice = Budgeted(choice, responder, message, directed, why, player);
                if (!primary)
                    social++;
            }
            lastRoute ??= choice.Route;
            if (!TryAddDistinctResponse(scheduledResponses, choice.Text, out string response))
            {
                if (!primary && choice.Route.StartsWith("pileon", StringComparison.Ordinal))
                    reactions--;
                continue;
            }
            if (primary)
            {
                plan = voices
                    ? AutonomousChatArbiter.Arrange(count, AutonomousChatReplyBudget.IsAlarm(message, intent) ? eAutonomousChatIntent.Alarm : intent, choice.Route,
                        directed, question, eligible.Length, AutonomousChatBudget.Current.PileOnCap, Random.Shared)
                    : new AutonomousChatArbiter.Plan(1, Math.Max(0, count - 1), 0);
                reactionKind = AutonomousChatArbiter.ReactionKind(choice.Route, intent);
                lastRoute = choice.Route;
            }
            else if (social > plan.Social && !choice.Route.StartsWith("pileon", StringComparison.Ordinal))
                continue;
            planned++;
            due = voices ? AutonomousChatArbiter.DelayMs(message, response, Wpm(responder), due, Random.Shared) : ReplyDelayMs(response, due, Random.Shared);
            if (faction)
                NoteFactionResponder(player.Realm, responder);
            // A bot named in the line answers itself; otherwise a busy bot's reply goes to the next bot that could say it.
            GameBot[] standIns = directed ? [] : eligible.Skip(count + 3).Take(PlayerReplyStandIns).ToArray();
            string text = response;
            string route = choice.Route;
            string lineId = choice.Line?.Id;
            int fit = AutonomousChatReplyFit.Of(route, message);
            string outcome = primary ? route.StartsWith("ack", StringComparison.Ordinal) ? "ack" : "said" : "pileon";
            _ = new ECSGameTimer(player, _ =>
            {
                GameBot speaker = PickSpeaker(responder, standIns,
                    bot => Eligible(bot) && !bot.InCombat && !bot.IsCasting && (faction || HasLocalAudience(bot)), spoken);
                if (speaker == null)
                    return 0;
                if (faction && HasFactionAudience(speaker.Realm))
                    BroadcastFaction(speaker.Name, speaker.Realm, text);
                else if (!faction && HasLocalAudience(speaker))
                    SayLine(speaker, text);
                else
                    return 0;
                Note(faction ? AutonomousChatThreads.Faction(speaker.Realm) : AutonomousChatThreads.Say(speaker.CurrentRegionID), speaker, text, "",
                    player.Name);
                AutonomousChatWhy.Record(speaker, why, "player " + player.Name, outcome, route, fit,
                    outcome == "pileon" ? "a bystander joins in after the first answer"
                    : speaker == responder ? (directed ? "named in the line" : "the best fit of " + eligible.Length + " who heard it")
                    : "stood in for " + responder.Name + ", who was busy", text, lineId, player.Name);
                return 0;
            }, due);
        }
        // The speaking lock: no unprompted line starts in this channel until the answers are out.
        if (planned > 0)
            AutonomousChatBudget.Meter.Delay(faction ? AutonomousChatBudget.Channel.Faction : AutonomousChatBudget.Channel.Say,
                faction ? (long)player.Realm : player.CurrentRegionID, GameLoop.GameLoopTime + due + 2_000);
        if (planned == 0)
            AutonomousChatWhy.RecordSilence(player.Realm, why, "player " + player.Name, lastRoute ?? "none", AutonomousChatReplyFit.Of(lastRoute, message),
                eligible.Length == 0 ? "no bot that heard it could answer" : count == 0 ? $"a line like this ({intent}) draws no answer here"
                    : "no reply fits it", player.Name);
        // One line per player line, so a question that drew no answer in game can be traced (play test 2026-10-27: "what lvl
        // r u all" and "lvl 2 viking lfg pst" went unanswered and the log could not say why).
        PlanLog.Debug($"CHAT_PLAYER_REPLY_PLAN player=\"{player.Name}\" channel={(faction ? "faction" : "say")} intent={intent} " +
                      $"shape={AutonomousChatShortTalk.QuestionShape(message) ?? "-"} knowledge={hasKnowledge} eligible={eligible.Length} " +
                      $"directed={directed} count={count} planned={planned}");
    }

    private static readonly DOL.Logging.Logger PlanLog = DOL.Logging.LoggerManager.Create(typeof(AutonomousBotChatCoordinator));

    /// <summary>Who answers a player first: a bot that can really help (a rezzer near a dead player, a bot in the
    /// frontier for "inc", a bot close in level for a group question), then bots whose task matches the words.</summary>
    private static int PlayerRelevance(GameBot bot, GamePlayer player, string message, eAutonomousChatIntent intent, bool faction = false)
    {
        int score = Relevance(bot, message);
        if (intent == eAutonomousChatIntent.Rez && WillComeRez(bot, player))
            score += 100;
        if (intent == eAutonomousChatIntent.Alarm && (bot.CurrentZone?.IsRvR == true || bot.CurrentRegion?.IsRvR == true))
            score += 20;
        if (intent == eAutonomousChatIntent.Grouping && Math.Abs(bot.Level - player.Level) <= 4)
            score += 5;
        // A bot that could actually join answers a group question first (playtest 2026-10-18: a bot in another
        // zone offered help to a level 3 near Cotswold).
        if (intent is eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Grinding or eAutonomousChatIntent.Rez)
            score += bot.CurrentZone != null && bot.CurrentZone == player.CurrentZone ? 8 : bot.CurrentRegion == player.CurrentRegion ? 4 : 0;
        // A groupmate answers first in /say, and for a rez or a "ty" anywhere; in /faction the realm answers (live test
        // 2026-10-18: the player's one groupmate answered nearly every /faction line, 20 lines in 20 minutes).
        if (bot.Group != null && bot.Group == player.Group && (!faction || intent is eAutonomousChatIntent.Rez or eAutonomousChatIntent.Thanks))
            score += 10;
        return score;
    }

    /// <summary>The last few bots that answered a player in /faction, by realm: they step back so others get a turn.</summary>
    private static readonly Dictionary<eRealm, Queue<long>> RecentFactionResponders = new();
    public const int RecentResponderMemory = 4;

    private static bool AnsweredRecently(eRealm realm, GameBot bot)
    {
        lock (Sync)
            return RecentFactionResponders.TryGetValue(realm, out var recent) && recent.Contains(bot.DatabaseID);
    }

    private static void NoteFactionResponder(eRealm realm, GameBot bot)
    {
        lock (Sync)
        {
            if (!RecentFactionResponders.TryGetValue(realm, out var recent))
                RecentFactionResponders[realm] = recent = new Queue<long>();
            recent.Enqueue(bot.DatabaseID);
            while (recent.Count > RecentResponderMemory)
                recent.Dequeue();
        }
    }

    /// <summary>
    /// True when this bot will really resurrect the player soon, so it may say "omw": the player is dead and
    /// near, the bot has a resurrection spell it can cast and is not fighting, and either it is in the player's
    /// group (group members rez each other) or Living Server deaths let free gamebots rez strangers.
    /// </summary>
    public static bool WillComeRez(GameBot bot, GamePlayer player) =>
        bot != null && player != null && !player.IsAlive && bot.IsAlive && !bot.InCombat && bot.ResurrectionSpell != null &&
        bot.ResurrectionSpell.Level <= bot.Level && bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, 1_500) &&
        (bot.Group != null && bot.Group == player.Group || AutonomousLivingServer.DeathOn && !bot.IsPlayerLedGroup);

    /// <summary>How likely a bot's opener of this kind gets an answer, and how many (0.36-se.4: a statement no longer
    /// always draws a reply; questions nearly always do).</summary>
    public static int BotReplyCount(string invites, int available, Random random)
    {
        if (available <= 0 || string.IsNullOrEmpty(invites) || invites == "none")
            return 0;
        double roll = random.NextDouble();
        int count = VC.InviteFamily(invites) switch
        {
            "ask" => roll < 0.3 ? 2 : roll < 0.92 ? 1 : 0,
            "boast" or "joke" => roll < 0.15 ? 2 : roll < 0.7 ? 1 : 0,
            "complaint" => roll < 0.12 ? 2 : roll < 0.65 ? 1 : 0,
            "lfg" or "trade" => roll < 0.55 ? 1 : 0,
            "news" => roll < 0.15 ? 2 : roll < 0.6 ? 1 : 0,
            _ => roll < 0.45 ? 1 : 0,   // chat
        };
        return Math.Min(available, count);
    }

    /// <summary>How likely the opener's speaker closes the exchange after an answer ("ty", "heh yeah").</summary>
    public static double ClosingChance(string invites) => VC.InviteFamily(invites) switch
    {
        "ask" => 0.5,
        "lfg" or "trade" => 0.35,
        "none" or "" => 0.0,
        _ => 0.2,
    };

    private static void ScheduleBotReplies(GameBot starter, string opening, bool faction, bool fromPlayer, VC.Line openerLine = null)
    {
        if (openerLine != null && !string.IsNullOrEmpty(openerLine.Invites) && AutonomousBotVoices.Enabled)
        {
            ScheduleVoicedBotReplies(starter, opening, faction, openerLine.Invites);
            return;
        }
        bool hasKnowledge = AutonomousChatKnowledge.TryAnswer(starter.Realm, opening, out string knowledge);
        GameBot[] eligible = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot != starter && Eligible(bot) && bot.Realm == starter.Realm)
            .Where(bot => faction || bot.CurrentRegion == starter.CurrentRegion && bot.IsWithinRadius(starter, WorldMgr.SAY_DISTANCE))
            .OrderByDescending(bot => Relevance(bot, opening))
            .ThenBy(_ => Random.Shared.Next())
            .ToArray();
        int count = hasKnowledge ? Math.Min(1, eligible.Length) : RollResponseCount(eligible.Length);
        HashSet<string> scheduledResponses = new(StringComparer.OrdinalIgnoreCase);
        int scheduledCount = 0;
        var said = new ReplyTally();
        for (int index = 0; index < count; index++)
        {
            GameBot responder = eligible[index];
            string response = hasKnowledge ? knowledge
                : VoicedReply(responder, opening, () => faction ? GenerateFactionReply(opening) : GenerateLocalReply(responder, opening));
            if (!TryAddDistinctResponse(scheduledResponses, response, out response))
                continue;
            Schedule(responder, 1_700 + scheduledCount++ * 2_100 + Random.Shared.Next(1_300), () =>
            {
                if (faction && HasFactionAudience(responder.Realm))
                {
                    BroadcastFaction(responder.Name, responder.Realm, response);
                    said.Add();
                }
                else if (!faction && HasLocalAudience(responder))
                {
                    SayLine(responder, response);
                    said.Add();
                }
            });
        }

        // A short optional closing line makes this read like a conversation,
        // while the channel cooldown prevents it from becoming a chat loop.
        if (!fromPlayer && !hasKnowledge && scheduledCount > 0 && Random.Shared.NextDouble() < 0.28)
        {
            string closing = faction ? "Fair enough. Safe travels." : "Understood. I will keep at it.";
            Schedule(starter, 5_500 + scheduledCount * 1_500, () =>
            {
                // In game 2026-10-18 a bot "answered itself": its closing went out although none of the replies did
                // (each responder was busy or gone when its turn came). The closing now follows a reply that was said.
                if (!ClosingFollowsAReply(said.Count))
                    return;
                if (faction && HasFactionAudience(starter.Realm))
                    BroadcastFaction(starter.Name, starter.Realm, closing);
                else if (!faction && HasLocalAudience(starter))
                    SayLine(starter, closing);
            });
        }
    }

    /// <summary>
    /// Replies to a bot's corpus opener by what it invites (0.36-se.4). The audit found 34 of 50 sampled replies
    /// didn't fit: replies were chosen by classifying the opener's words ("yellow cons are for warming up" read as
    /// an insult). Now a boast gets a boast reply, a question an answer (a real one for places and trainers), a
    /// plain remark often nothing, and "none" never anything. No old formal line is used. The closing is a written
    /// one ("ty", "heh yeah") that fits the opener, only after a reply was said, and never after a question.
    /// </summary>
    private static void ScheduleVoicedBotReplies(GameBot starter, string opening, bool faction, string invites)
    {
        string[] chain = VC.AnswerChain(invites);
        if (chain.Length == 0)
            return;
        // Chat audit 2026-10-21: "two handed or sword and board on a paladin?" drew "imo yes" (its set is ask_opinion), and
        // "ram on the door at Caer Benowyc!" (set "chat") drew "cant disagree". An either-or question takes the choice
        // answers first; an alarm takes the alarm replies.
        string openingShape = AutonomousChatShortTalk.QuestionShape(opening);
        if (openingShape == "ask_choice" && VC.InviteFamily(invites) == "ask" && !chain.Contains("ask_choice"))
            chain = ["ask_choice", .. chain];
        // "anyone else level their infil solo or am i doing it wrong?" (set ask_anyone) drew "count me out this time": only an
        // invitation ("anyone want to...", "who wants to...") takes the yes-or-no-to-coming answers; the rest asks an opinion.
        else if (invites == "ask_anyone" && openingShape is not ("ask_anyone" or "ask_on"))
            chain = ["ask_opinion", "ask"];
        eAutonomousChatIntent read = AutonomousChatIntentModel.Predict(opening);
        // "mids 8man in cathal, stay grouped" (set "chat") drew "same :)": frontier news takes the frontier replies.
        string alarmIntent = read == eAutonomousChatIntent.Alarm ? "alarm"
            : read == eAutonomousChatIntent.RvR && VC.InviteFamily(invites) is "chat" or "news" ? "rvr" : null;
        bool alarm = alarmIntent != null;
        bool asks = VC.InviteFamily(invites) == "ask";
        string knowledge = null;
        bool hasKnowledge = asks && AutonomousChatKnowledge.TryAnswer(starter.Realm, opening, out knowledge);
        GameBot[] eligible = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot != starter && Eligible(bot) && bot.Realm == starter.Realm)
            .Where(bot => faction || bot.CurrentRegion == starter.CurrentRegion && bot.IsWithinRadius(starter, WorldMgr.SAY_DISTANCE))
            .OrderByDescending(bot => Relevance(bot, opening) * 3 - (invites == "lfg" ? Math.Min(10, Math.Abs(bot.Level - starter.Level)) : 0))
            .ThenBy(_ => Random.Shared.Next())
            .ToArray();
        int count = hasKnowledge ? Math.Min(1, eligible.Length) : BotReplyCount(invites, eligible.Length, Random.Shared);
        if (count == 0)
            return;
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = starter.Name };
        HashSet<string> scheduled = new(StringComparer.OrdinalIgnoreCase);
        var said = new ReplyTally();
        string lastReply = null;
        int due = 0;
        // A few tries past the wanted count, not the whole realm, when no reply fits (review 2026-10-18).
        using AutonomousChatFiller.ReplyScope fillerScope = AutonomousChatFiller.Replying(opening, ChannelKey(faction ? "faction" : "say", starter),
            GameLoop.GameLoopTime);
        for (int index = 0; index < eligible.Length && index < count + 3 && scheduled.Count < count; index++)
        {
            GameBot responder = eligible[index];
            string response = null;
            VC.Line replyLine = null;
            if (hasKnowledge)
                response = AutonomousBotVoices.StyleFor(responder, knowledge);
            else if (alarm)
            {
                if (AutonomousBotVoices.TryLine(responder, "reply", out VC.Picked alarmReply, alarmIntent, slots: slots,
                        filter: line => AutonomousChatShortTalk.ReplyFits(opening, line.Text)))
                    (response, replyLine) = (alarmReply.Text, alarmReply.Line);
            }
            else
            {
                foreach (string answers in chain)
                {
                    if (AutonomousBotVoices.TryLine(responder, "reply", out VC.Picked picked, answers: answers, slots: slots,
                            filter: line => AutonomousBotVoices.ShapedFits(answers, opening, line.Text)))
                    {
                        (response, replyLine) = (picked.Text, picked.Line);
                        break;
                    }
                }
            }
            if (!TryAddDistinctResponse(scheduled, response, out response))
                continue;
            lastReply = response;
            due = ReplyDelayMs(response, due, Random.Shared);
            // A bot that is fighting when its reply is due hands it to another bot that could have said it
            // (re-run 2026-10-18: 26% of questions went unanswered because the one chosen bot was busy).
            GameBot[] standIns = eligible.Skip(index + 1).Take(3).ToArray();
            string text = response;
            _ = new ECSGameTimer(responder, _ =>
            {
                GameBot speaker = Eligible(responder) && !responder.InCombat ? responder
                    : standIns.FirstOrDefault(bot => Eligible(bot) && !bot.InCombat && (faction || HasLocalAudience(bot)));
                if (speaker == null)
                    return 0;
                // Bot-to-bot talk holds its tongue when a scene started after it was scheduled.
                if (faction && HasFactionAudience(speaker.Realm) && !AutonomousBotDebates.IsRunning(speaker.Realm))
                {
                    BroadcastFaction(speaker.Name, speaker.Realm, text);
                    said.Add();
                }
                else if (!faction && HasLocalAudience(speaker))
                {
                    SayLine(speaker, text);
                    said.Add();
                }
                else
                    return 0;
                Note(faction ? AutonomousChatThreads.Faction(speaker.Realm) : AutonomousChatThreads.Say(speaker.CurrentRegionID), speaker, text, "");
                AutonomousChatWhy.Record(speaker, faction ? "faction" : "say", "bot " + starter.Name + " (" + invites + ")", "said",
                    hasKnowledge ? "knowledge" : RuleOf(replyLine, "reply"), VC.Specificity(replyLine), "answers what the opener invites", text,
                    replyLine?.Id, starter.Name);
                return 0;
            }, due);
        }
        if (scheduled.Count == 0 || lastReply == null || lastReply.TrimEnd().EndsWith('?') || Random.Shared.NextDouble() >= ClosingChance(invites))
            return;
        // A trade answered with interest ("pst me the details") gets a follow-up, a turned-down one "ill keep it then".
        bool interested = VC.InviteFamily(invites) == "trade" && ShowsTradeInterest(lastReply);
        if (!AutonomousBotVoices.TryLine(starter, "closing", out VC.Picked closing, answers: VC.InviteFamily(invites),
                filter: line => interested == (line.Topic == "interested")))
            return;
        Schedule(starter, ReplyDelayMs(closing.Text, due, Random.Shared) + 800, () =>
        {
            if (!ClosingFollowsAReply(said.Count))
                return;
            if (faction && HasFactionAudience(starter.Realm) && !AutonomousBotDebates.IsRunning(starter.Realm))
                BroadcastFaction(starter.Name, starter.Realm, closing.Text);
            else if (!faction && HasLocalAudience(starter))
                SayLine(starter, closing.Text);
        });
    }

    /// <summary>A reply by name from a bot that remembers the player (Living Server memory), else the fallback.</summary>
    private static string KnownOr(GameBot responder, GamePlayer player, string incoming, Func<string> fallback) =>
        KnownChoice(responder, player, incoming, () => new ReplyChoice(fallback(), "fallback")).Text;

    /// <summary><see cref="KnownOr"/> with the route it took (C0).</summary>
    private static ReplyChoice KnownChoice(GameBot responder, GamePlayer player, string incoming, Func<ReplyChoice> fallback) =>
        // A question goes on to be answered, not replaced by a memory line (review 2026-10-26).
        AutonomousLivingServer.MemoryOn && AutonomousChatShortTalk.QuestionShape(incoming) == null && !AutonomousChatQuestions.IsQuestion(incoming) &&
        Random.Shared.NextDouble() < 0.6 &&
        AutonomousLivingServer.TryKnownReply(responder, player,
            AutonomousBotVoices.IntentName(AutonomousChatIntentModel.Predict(incoming)), out string known)
            ? new ReplyChoice(known, "memory " + AutonomousBotVoices.IntentName(AutonomousChatIntentModel.Predict(incoming)))
            : fallback();

    /// <summary>
    /// A corpus reply for the line's intent in the responder's voice (by name when it remembers the player), else
    /// the old reply. Answering a player, a short reply heard 40+ minutes ago may be used again rather than falling
    /// back to the old formal lines, and an unknown line gets a short "unknown" reply ("hm?", "heh") (0.36-se.4).
    /// </summary>
    private static string VoicedReply(GameBot responder, string incoming, Func<string> fallback, GamePlayer player = null,
        eAutonomousChatIntent? knownIntent = null, bool addressed = false) =>
        VoicedChoice(responder, incoming, fallback, player, knownIntent, addressed).Text;

    /// <summary>
    /// <see cref="VoicedReply"/> with the route it took ("fact level", "shape ask_on", "intent greeting", "not sure", "none
    /// unknown"...), the same route names <c>ChatAuditRoute</c> uses, and the corpus line, so /lswhy can say why (C0).
    /// </summary>
    internal static ReplyChoice VoicedChoice(GameBot responder, string incoming, Func<string> fallback, GamePlayer player = null,
        eAutonomousChatIntent? knownIntent = null, bool addressed = false, string channel = null)
    {
        using (AutonomousChatFiller.Replying(incoming, ChannelKey(channel ?? "faction", responder), GameLoop.GameLoopTime))
            return VoicedChoiceCore(responder, incoming, fallback, player, knownIntent, addressed, channel);
    }

    /// <summary>The key a channel moment's filler memory goes by: "faction:Albion", "say:1:Albion".</summary>
    internal static string ChannelKey(string channel, GameBot bot) =>
        channel == "say" ? $"say:{bot?.CurrentRegionID}:{bot?.Realm}" : $"{channel}:{bot?.Realm}";

    private static ReplyChoice VoicedChoiceCore(GameBot responder, string incoming, Func<string> fallback, GamePlayer player,
        eAutonomousChatIntent? knownIntent, bool addressed, string channel)
    {
        if (!AutonomousBotVoices.Enabled && !AutonomousLivingServer.MemoryOn)
            return new ReplyChoice(fallback(), "legacy");
        eAutonomousChatIntent value = knownIntent ?? AutonomousChatIntentModel.Predict(incoming);
        string intent = AutonomousBotVoices.IntentName(value);
        // A question the bot can answer truly is answered first, even by a bot that remembers the player (review 2026-10-26: the
        // memory reply below replaced it 60% of the time).
        if (AutonomousBotVoices.Enabled && AutonomousChatLiveAnswers.AnswerQuestion(responder, incoming, addressed, Random.Shared) is { } knownFact)
            return new ReplyChoice(knownFact, "fact " + (addressed ? AutonomousChatQuestions.SelfQuestion(incoming) ?? "world" : "world"));
        // Beta 1.1.3: the situation decides before the words' topic (gn, /stuck, inc, a call for help, a sale, a counter-offer).
        if (AutonomousBotVoices.Enabled && AutonomousChatContext.Reply(incoming, value, AutonomousChatShortTalk.QuestionShape(incoming),
                ContextFor(responder, player, channel), Random.Shared) is { } situation)
            return new ReplyChoice(AutonomousBotVoices.StyleFor(responder, situation.Text), situation.Route);
        // A holiday wish is answered in kind before anything else, even by a bot that remembers the player (in-game check
        // 2026-10-28: "happy halloween all!" drew "evening Claudetest, good to see you").
        if (AutonomousBotVoices.Enabled && value == eAutonomousChatIntent.Greeting && AutonomousChatIntentModel.IsHolidayWish(incoming) &&
            AutonomousBotVoices.TryGreetingReply(responder, incoming, out VC.Picked wish,
                player == null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player.Name }, relax: true) &&
            wish.Line?.Needs?.Any(need => need.StartsWith("holiday_", StringComparison.Ordinal)) == true)
            return new ReplyChoice(wish.Text, "intent greeting", wish.Line);
        // A bot that remembers this player answers by name, or coolly after abuse (Living Server memory). Not to a question
        // with answers of its own: "where to next?" drew "Wrenfield?" (live test 2026-10-18).
        if (player != null && AutonomousChatShortTalk.QuestionShape(incoming) == null && Random.Shared.NextDouble() < 0.6 &&
            AutonomousLivingServer.TryKnownReply(responder, player, intent, out string known))
            return new ReplyChoice(known, "memory " + intent);
        if (!AutonomousBotVoices.Enabled)
            return new ReplyChoice(fallback(), "legacy");
        var slots = player == null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player.Name };
        // Only a bot that will really come says "omw" to "rez pls" ("will_rez"); one that can't rez at all says so ("!can_rez").
        string[] with = value == eAutonomousChatIntent.Rez && WillComeRez(responder, player) ? ["will_rez"] : null;
        // An order the bot can't carry out gets an honest no ("heal me" to a thane), not "on it".
        if (AutonomousChatShortTalk.QuestionShape(incoming) == "player_order" &&
            AutonomousChatQuestions.CantDo(incoming, AutonomousChatLiveAnswers.FactsOf(responder)) is { } cant)
            return new ReplyChoice(AutonomousBotVoices.StyleFor(responder, cant), "fact cant");
        string shape = AutonomousChatShortTalk.QuestionShape(incoming);
        if (AutonomousBotVoices.TryShapedReply(responder, incoming, out VC.Picked shaped, slots, relax: player != null, asker: player))
            return new ReplyChoice(shaped.Text, (shaped.Line == null ? "live " : "shape ") + shape, shaped.Line);
        // An acknowledgement gets no answer; "anyone in camelot?" only from a bot that is there (chat audit 2026-10-21).
        if (shape is "ack" or "ask_inplace")
            return new ReplyChoice(null, "none " + (shape == "ack" ? "ack" : "not_there"));
        // "really?", "why?": a follow-up only the one it was said to can answer (the audit's "really?" drew "good question").
        if (AutonomousChatShortTalk.IsFollowUp(incoming))
            return new ReplyChoice(fallback(), "none followup");
        // A factual question no answer above knew ("can lurikeens be wardens", "when does the relic raid start") gets an honest
        // "not sure" now and then, never a remark on its topic: the audit's "is thid open for lvl 21" drew "if you see the zerg
        // coming, call it out".
        if (AutonomousChatQuestions.IsFactual(incoming) && value is not (eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Thanks or
                eAutonomousChatIntent.Rez or eAutonomousChatIntent.Alarm or eAutonomousChatIntent.Grouping or eAutonomousChatIntent.BotCheck or
                eAutonomousChatIntent.Abuse or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Ding or eAutonomousChatIntent.Congrats or
                // "back from vacation guys miss me?" is a hello, not a question to be unsure of (the holdout check).
                eAutonomousChatIntent.Back or eAutonomousChatIntent.Away or
                // "What be thy name, stranger?" is answered in kind (Beta 1.1.3).
                eAutonomousChatIntent.Roleplay))
        {
            if (Random.Shared.NextDouble() < 0.5 && AutonomousBotVoices.TryLine(responder, "reply", out VC.Picked honest, answers: "ask",
                    slots: slots, relax: player != null, filter: line => !line.Text.Contains("trainer") && !line.Text.Contains("guard")))
                return new ReplyChoice(honest.Text, "not sure", honest.Line);
            return new ReplyChoice(fallback(), "none factual");
        }
        if (value == eAutonomousChatIntent.Unknown)
        {
            // A question with no answer set gets an honest "not sure"; any other unread line no reply unless the bot was named
            // (the audit's "what time is it" drew "lol ok", "is anyone in camelot?" "maybe maybe not").
            if (incoming.Contains('?') && AutonomousBotVoices.TryLine(responder, "reply", out VC.Picked unsure, answers: "ask", slots: slots,
                    relax: player != null, filter: line => !line.Text.Contains("trainer") && !line.Text.Contains("guard")))
                return new ReplyChoice(unsure.Text, "not sure", unsure.Line);
            return new ReplyChoice(fallback(), "none unknown");
        }
        // Beta 1.1.3, round 2: an in-character line is answered in kind for what it does (a greeting returned, a challenge
        // answered, a toast with a toast), else with an emote; never a stock musing.
        if (value == eAutonomousChatIntent.Roleplay)
        {
            string act = AutonomousChatShortTalk.RoleplayAct(incoming);
            foreach (string answers in new[] { "player_rp_" + act, "player_rp_emote" })
                if (AutonomousBotVoices.TryLine(responder, "reply", out VC.Picked inKind, answers: answers, slots: slots, relax: true))
                    return new ReplyChoice(inKind.Text, "intent roleplay", inKind.Line);
            return new ReplyChoice("*nods*", "intent roleplay");
        }
        // Class talk is about the class asked about, or about no class in particular (re-run 2026-10-18: "is friar good?"
        // drew "necro pets do all the work").
        if (value == eAutonomousChatIntent.ClassTalk)
        {
            string[] asked = AutonomousChatShortTalk.ClassWordsIn(incoming);
            bool AboutAsked(VC.Line line) => AutonomousChatShortTalk.ClassWordsIn(line.Text).Any(w => asked.Any(a => AutonomousChatShortTalk.SameClassWord(w, a)));
            if (asked.Length > 0 && AutonomousBotVoices.TryLine(responder, "reply", out VC.Picked about, intent, slots: slots, relax: player != null,
                    filter: AboutAsked))
                return new ReplyChoice(about.Text, "intent classtalk asked", about.Line);
            if (AutonomousBotVoices.TryLine(responder, "reply", out VC.Picked general, intent, slots: slots, relax: player != null,
                    filter: line => !AutonomousChatShortTalk.NamesAClass(line.Text)))
                return new ReplyChoice(general.Text, "intent classtalk", general.Line);
        }
        else if (value == eAutonomousChatIntent.Greeting)
        {
            if (AutonomousBotVoices.TryGreetingReply(responder, incoming, out VC.Picked hello, slots, relax: player != null))
                return new ReplyChoice(hello.Text, "intent greeting", hello.Line);
        }
        // The audit: "vendo spiders give me the creeps" and "i miss the old df raids" drew dungeon tips. A remark about a dungeon or
        // the frontier that plans nothing is answered in kind ("lol same"), not with advice.
        string topicIntent = value is eAutonomousChatIntent.Dungeon or eAutonomousChatIntent.RvR && !AutonomousChatQuestions.IsQuestion(incoming) &&
                             !PlansSomething(incoming)
            ? AutonomousBotVoices.IntentName(eAutonomousChatIntent.Banter)
            : intent;
        if (value is not (eAutonomousChatIntent.ClassTalk or eAutonomousChatIntent.Greeting) &&
            AutonomousBotVoices.TryLine(responder, "reply", out VC.Picked voiced, topicIntent,
                     slots: slots, flags: with, relax: player != null,
                     filter: line => AutonomousChatShortTalk.ReplyFits(incoming, line.Text) &&
                                     AutonomousChatShortTalk.TradeReplyFits(incoming, line.Text) &&
                                     // "lurikeens are the cutest" drew "bet you cant solo it" and "lol sure u did" (replies to a boast).
                                     !(value is eAutonomousChatIntent.Banter or eAutonomousChatIntent.Dungeon or eAutonomousChatIntent.RvR &&
                                       System.Text.RegularExpressions.Regex.IsMatch(line.Text, @"sure (u|you) did|bet (u|you) cant|bet you can't|pics or|show off",
                                           System.Text.RegularExpressions.RegexOptions.IgnoreCase))))
            return new ReplyChoice(voiced.Text, "intent " + topicIntent, voiced.Line);
        // No reply of the line's kind fits. The corpus "unknown" set ("huh", "lol what", "maybe maybe not") read as nonsense to a
        // real line (chat audit 2026-10-21); a question gets an honest "not sure", anything else the caller's fallback.
        if (incoming.Contains('?') && value is not (eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or
                eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Back or eAutonomousChatIntent.Away or eAutonomousChatIntent.Laugh or
                eAutonomousChatIntent.Roleplay) &&
            AutonomousBotVoices.TryLine(responder, "reply", out VC.Picked notSure, answers: "ask", slots: slots,
                relax: player != null, filter: line => !line.Text.Contains("trainer") && !line.Text.Contains("guard")))
            return new ReplyChoice(notSure.Text, "intent " + intent + " -> not sure", notSure.Line);
        return new ReplyChoice(fallback(), "none " + intent);
    }

    /// <summary>What a bot knows of the moment for a reply chosen by the situation (AutonomousChatContext).</summary>
    internal static AutonomousChatContext.Facts ContextFor(GameBot bot, GamePlayer player, string channel)
    {
        bool near = player != null && (bot.Group != null && bot.Group == player.Group ||
                                       bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, 6_000));
        bool heals = bot.CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)bot.CharacterClass.ID);
        ChatThreadLog.Said last = AutonomousChatThreads.Log.LastBy(bot.DatabaseID, GameLoop.GameLoopTime, AutonomousChatThreads.FollowUpWindowMs);
        string lastLine = last == null || last.To != null && player != null && !string.Equals(last.To, player.Name, StringComparison.OrdinalIgnoreCase)
            ? null : last.Text;
        return new AutonomousChatContext.Facts(bot.Name, player?.Name, channel ?? "faction", near, heals,
            bot.CurrentZone?.IsRvR == true || bot.CurrentRegion?.IsRvR == true, bot.Level, lastLine, AutonomousBotVoices.CapitalOf(bot.Realm));
    }

    /// <summary>A bot's voiced reply to a player's line, or null (the /group answers use it; 0.36-se.4).</summary>
    internal static string ReplyToPlayer(GameBot responder, GamePlayer player, string message) =>
        ReplyChoiceToPlayer(responder, player, message).Text;

    /// <summary><see cref="ReplyToPlayer"/> with the route it took (C0).</summary>
    internal static ReplyChoice ReplyChoiceToPlayer(GameBot responder, GamePlayer player, string message, bool named = false, string channel = "group") =>
        Budgeted(VoicedChoice(responder, message, () => null, player, addressed: true, channel: channel), responder, message, named, channel, player);

    /// <summary>
    /// The reply budget (Beta 1.1.3) on a chosen reply: a reply under the minimum fit, or a stock brush-off, becomes a short
    /// acknowledgement when the line was a tell to the bot or named it, and silence otherwise (route "ack ..." or "silent ...").
    /// </summary>
    internal static ReplyChoice Budgeted(ReplyChoice choice, GameBot bot, string message, bool addressed, string channel = null, GamePlayer player = null)
    {
        if (!AutonomousBotVoices.Enabled || choice.Route == "legacy")
            return choice;
        // Beta 1.1.3, round 3: a scam gets silence on every route, whatever answered it ("oh ok", "sure ty" to a fake GM).
        if (AutonomousChatScreen.IsSpam(message))
            return new ReplyChoice(null, "silent (none spam)");
        // Never take thanks for heals or a rez the bot can't give ("np" from a thane to "ty for the heals").
        if (AutonomousChatReplyBudget.CreditFor(message) != null && MomentOf(bot, player, message, channel ?? "tell", addressed) is var credit &&
            AutonomousChatReplyBudget.ClaimsCredit(message, choice.Text, credit.CanHeal, credit.CanRez))
            choice = new ReplyChoice(null, "none credit");
        int fit = AutonomousChatReplyFit.Of(choice.Route, message);
        if (string.IsNullOrWhiteSpace(choice.Text) || AutonomousChatReplyBudget.IsBrushOff(choice.Text))
            fit = Math.Min(fit, AutonomousChatReplyFit.Weak);
        var outcome = AutonomousChatReplyBudget.Decide(fit, addressed, AutonomousChatBudget.MinReplyFit);
        if (outcome == AutonomousChatReplyBudget.Outcome.Answer)
            return choice;
        // "k", "ok", "nice": an acknowledgement draws nothing, even by tell.
        if (choice.Route is "none ack" or "none spam" or "none other")
            return new ReplyChoice(null, "silent (" + choice.Route + ")");
        // A raid or group outside the bot's level band: it says so rather than "sure" (round 3).
        if (addressed && bot != null && BandReply(bot.Level, message, Random.Shared) is { } band && AsksToJoinGroup(message))
            return new ReplyChoice(AutonomousBotVoices.StyleFor(bot, band), "short (band)");
        // Beta 1.1.3, second pass (blind round 2): a tell that says something gets a short fitting reply, not a bare "ya?"; a
        // weak line to nobody in particular a short reaction or a "dunno" at a rate by channel, not always silence.
        AutonomousChatReplyBudget.Moment moment = MomentOf(bot, player, message, channel ?? (addressed ? "tell" : "faction"), addressed);
        if (outcome == AutonomousChatReplyBudget.Outcome.Ack)
            return AutonomousChatReplyBudget.ToContent(moment, Random.Shared) is { } content
                ? new ReplyChoice(AutonomousBotVoices.StyleFor(bot, content), "short (" + choice.Route + ")")
                : new ReplyChoice(AutonomousChatReplyBudget.Ack(bot?.DatabaseID ?? 0, moment.Busy, Random.Shared), "ack (" + choice.Route + ")");
        return AutonomousChatReplyBudget.Rescue(moment, choice.Route, Random.Shared) is { } rescue
            ? new ReplyChoice(AutonomousBotVoices.StyleFor(bot, rescue), "rescue (" + choice.Route + ")")
            : new ReplyChoice(null, "silent (" + choice.Route + ")");
    }

    /// <summary>What the reply budget knows of a reply's moment: the bot's class, heals and rez, whether it is busy, its last line.</summary>
    internal static AutonomousChatReplyBudget.Moment MomentOf(GameBot bot, GamePlayer player, string message, string channel, bool addressed)
    {
        bool heals = bot?.CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)bot.CharacterClass.ID);
        bool rez = bot?.ResurrectionSpell != null && bot.ResurrectionSpell.Level <= bot.Level;
        string last = null;
        if (bot != null)
        {
            ChatThreadLog.Said said = AutonomousChatThreads.Log.LastBy(bot.DatabaseID, GameLoop.GameLoopTime, AutonomousChatThreads.FollowUpWindowMs);
            if (said != null && (said.To == null || player == null || string.Equals(said.To, player.Name, StringComparison.OrdinalIgnoreCase)))
                last = said.Text;
        }
        return new AutonomousChatReplyBudget.Moment(message, channel, addressed, AutonomousChatIntentModel.Predict(message),
            bot?.CharacterClass?.Name ?? bot?.ClassName, heals, rez, bot?.InCombat == true || bot?.IsCasting == true, last,
            addressed && bot != null ? AutonomousChatLiveAnswers.FactsOf(bot) : null);
    }

    /// <summary>A reply, the route that chose it (as <c>ChatAuditRoute</c> names routes) and the corpus line, if any (C0).</summary>
    internal readonly record struct ReplyChoice(string Text, string Route, VC.Line Line = null);

    /// <summary>How many of a conversation's scheduled replies were actually said (they run on timers).</summary>
    private sealed class ReplyTally
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Add() => Interlocked.Increment(ref _count);
    }

    /// <summary>A starter's closing line ("Fair enough. Safe travels.") only after somebody answered it.</summary>
    public static bool ClosingFollowsAReply(int repliesSaid) => repliesSaid > 0;

    /// <summary>
    /// Who says a reply when it comes due: the chosen bot, or, if it is fighting, casting or gone by then, the first stand-in
    /// that can talk and hasn't spoken in this exchange (played 2026-10-18: a level 50's lfg and "where is everyone?" drew
    /// nothing, because the chosen level 50s were in fights when their lines came due and nobody took over). Null: nobody.
    /// </summary>
    public static T PickSpeaker<T>(T chosen, IEnumerable<T> standIns, Func<T, bool> canSpeak, ISet<T> spoken) where T : class
    {
        lock (spoken)
        {
            T speaker = chosen != null && !spoken.Contains(chosen) && canSpeak(chosen)
                ? chosen
                : standIns.FirstOrDefault(bot => bot != null && !spoken.Contains(bot) && canSpeak(bot));
            if (speaker != null)
                spoken.Add(speaker);
            return speaker;
        }
    }

    /// <summary>How many other bots stand in for a busy one on a reply to a player.</summary>
    public const int PlayerReplyStandIns = 6;

    private static void Schedule(GameBot speaker, int delay, Action action)
    {
        _ = new ECSGameTimer(speaker, timer =>
        {
            if (Eligible(speaker) && !speaker.InCombat && !speaker.IsCasting)
                action();
            return 0;
        }, delay);
    }

    public static bool TryAddDistinctResponse(HashSet<string> scheduled, string response, out string normalized)
    {
        normalized = response?.Trim() ?? string.Empty;
        // Beta 1.1.3: near-copies count as the same ("Ha yes, this exactly." and "Heh yep, this exactly").
        string skeleton = AutonomousChatFiller.Skeleton(normalized);
        return scheduled != null && normalized.Length > 0 && scheduled.Add(skeleton.Length > 0 ? skeleton : normalized);
    }

    private static bool Eligible(GameBot bot) => bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
        bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && !bot.InCombat && !bot.IsCasting &&
        !bot.IsOnStableMasterRoute && !bot.IsOnHorse && !AutonomousBeggars.Is(bot);   // beggars talk only on their corner

    /// <summary>Closings said to a player never promise a tell the bot will not send ("sending a tell" is for bot-to-bot trades).</summary>
    private static bool NotAPromise(VC.Line line) => line.Topic != "interested";

    /// <summary>A reply to a trade post that wants it: "pst me the details", "how much", "ill take it". Pure.</summary>
    public static bool ShowsTradeInterest(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return false;
        string text = reply.ToLowerInvariant();
        return new[] { "pst", "send me", "how much", "i'll take", "ill take", "i will take", "interested", "quote", "/tell me", "tell me",
                "whats the price", "what's the price", "price?", "dibs", "i want it", "ill buy", "i'll buy", "i will buy" }
            .Any(text.Contains);
    }

    internal static bool HasFactionAudience(eRealm realm) => ClientService.Instance.GetPlayersOfRealm(realm).Count > 0;

    private static bool HasLocalAudience(GameBot bot) => bot?.CurrentRegion != null &&
        ClientService.Instance.GetPlayersOfRegion(bot.CurrentRegion)
            .Any(player => player.ObjectState == GameObject.eObjectState.Active && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE));

    private static string BuildAmbientOpening(GameBot starter, AutonomousBotChat.Context context, bool faction, out VC.Picked picked)
    {
        picked = null;
        // Bot voices first (corpus lines by personality, situation and style); the old lines are the fallback.
        if (AutonomousBotVoices.Enabled)
            return AutonomousHolidays.On && Random.Shared.NextDouble() < AutonomousHolidays.AmbientShare(AutonomousHolidays.Now) &&
                   AutonomousBotVoices.TryLine(starter, "ambient", out picked, topic: "holiday") ||
                   // Beta 1.1.3: lore musings only now and then as a /faction opener, no unasked Exchange plugs.
                   AutonomousBotVoices.TryLine(starter, AutonomousBotVoices.AmbientKind(starter, faction, Random.Shared), out picked,
                       filter: AmbientLineFits(faction)) ||
                   AutonomousBotVoices.TryLine(starter, "ambient", out picked, filter: AmbientLineFits(faction))
                ? picked.Text
                : null;

        if (Random.Shared.NextDouble() < 0.18)
        {
            string activity = Clean(starter.PersistentRecord?.Activity, string.Empty);
            string destination = Clean(starter.PersistentRecord?.TravelDestination, string.Empty);
            if (!string.IsNullOrWhiteSpace(destination) &&
                (activity.StartsWith("Walking to", StringComparison.OrdinalIgnoreCase) ||
                 activity.StartsWith("Approaching", StringComparison.OrdinalIgnoreCase)))
                return Random.Shared.Next(2) == 0
                    ? $"Anyone know where {destination} is?"
                    : $"Does anyone know where {destination} is at?";

            string target = Clean(starter.PersistentRecord?.TargetName, string.Empty);
            if (!string.IsNullOrWhiteSpace(target))
                return Random.Shared.Next(3) switch
                {
                    0 => $"Anyone know where {target} is?",
                    1 => $"Does anyone know where {target} is at?",
                    _ => $"Has anybody ever found {target}?",
                };

            if (!string.IsNullOrWhiteSpace(destination))
                return Random.Shared.Next(3) switch
                {
                    0 => $"Anyone know where {destination} is?",
                    1 => $"How do I get to {destination}?",
                    _ => $"Which stable master takes me to {destination}?",
                };
        }

        if (faction)
        {
            string situation = $"{starter.PersistentRecord?.Activity} {starter.PersistentRecord?.CurrentGoal} {starter.CurrentZone?.Description}";
            bool pvp = situation.Contains("rvr", StringComparison.OrdinalIgnoreCase) ||
                       situation.Contains("pvp", StringComparison.OrdinalIgnoreCase) ||
                       situation.Contains("frontier", StringComparison.OrdinalIgnoreCase) || starter.CurrentZone?.IsRvR == true;
            return BuildFactionTaskOpening(starter, context, pvp);
        }
        return AutonomousBotChat.Generate(context);
    }

    private static string BuildFactionTaskOpening(GameBot starter, AutonomousBotChat.Context context, bool pvp)
    {
        string className = Clean(context.ClassName, starter.ClassName);
        string zone = Clean(context.ZoneName, starter.CurrentZone?.Description ?? "my current area");
        string target = Clean(context.MonsterName, string.Empty);
        string destination = Clean(starter.PersistentRecord?.TravelDestination, string.Empty);
        int choice = Random.Shared.Next(10);

        if (pvp)
        {
            return (choice % 4) switch
            {
                0 => $"I am watching the frontier around {zone}. Where are defenders gathering?",
                1 => $"Any {className}s have advice for supporting a frontier group?",
                2 => "If anyone sees enemy movement, share the location so the realm can respond.",
                _ => "Good luck to everyone heading into the frontier. Stay with your group.",
            };
        }

        if (!string.IsNullOrWhiteSpace(target) && choice < 4)
        {
            return choice switch
            {
                0 => $"I am hunting {target} around {zone}. Has anyone found a steady camp nearby?",
                1 => $"These {target} are keeping me busy in {zone}. Anyone else working nearby?",
                2 => starter.Group?.MemberCount > 1
                    ? $"Our group is taking careful pulls of {target} in {zone}."
                    : $"I am taking careful pulls of {target} in {zone}.",
                _ => $"Has anyone learned a reliable way to handle {target} around {zone}?",
            };
        }

        if (!string.IsNullOrWhiteSpace(destination) && choice < 7)
        {
            return (choice % 3) switch
            {
                0 => $"I am heading toward {destination}. Any advice on the safest route?",
                1 => $"Does anyone know which stable master is best for reaching {destination}?",
                _ => $"Safe travels to anyone heading toward {destination}.",
            };
        }

        return choice switch
        {
            7 => $"Any other {className}s have advice for adventurers around level {starter.Level}?",
            8 => $"How are the hunting grounds around {zone} treating everyone?",
            9 when starter.Group?.MemberCount > 1 => $"Our group is working around {zone}. Are any other parties nearby?",
            _ => FactionOpeners[Random.Shared.Next(FactionOpeners.Length)],
        };
    }

    private static int Relevance(GameBot bot, string incoming)
    {
        string haystack = $"{bot.PersistentRecord?.Activity} {bot.PersistentRecord?.CurrentGoal} {bot.PersistentRecord?.TargetName} {bot.PersistentRecord?.TravelDestination}";
        return (incoming ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('?', '!', '.', ',', '\'', '"'))
            .Where(word => word.Length >= 4)
            .Count(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateDirectedReply(string incoming)
    {
        // With bot voices on, a bot named in chat answers like a person, not a clerk (0.36-se.4).
        if (AutonomousBotVoices.Enabled)
            return Random.Shared.Next(6) switch { 0 => "yeah?", 1 => "hm?", 2 => "whats up", 3 => "sup", 4 => "yes?", _ => "here" };
        if (incoming.Contains("stop", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("quiet", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("shut", StringComparison.OrdinalIgnoreCase))
        {
            return Random.Shared.Next(4) switch
            {
                0 => "Understood. I will focus on my task.",
                1 => "All right. Safe travels.",
                2 => "No problem. I will keep faction chat clear.",
                _ => "Okay. I will get back to work.",
            };
        }
        return AutonomousChatIntentModel.Predict(incoming) switch
        {
            eAutonomousChatIntent.Greeting => "Greetings. How can I help?",
            eAutonomousChatIntent.Abuse => "Let us keep the conversation helpful.",
            eAutonomousChatIntent.IgnoreOutOfWorld => "Let us keep the conversation focused on the realm.",
            _ => Random.Shared.Next(3) switch { 0 => "What did you need?", 1 => "Perhaps after this task.", _ => "Yes?" },
        };
    }

    private static string Clean(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().TrimEnd('.', '!', '?');
    private static string LowerFirst(string value) => string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
