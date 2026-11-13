using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.ServerProperties;
using Engagement = DOL.GS.AutonomousChatEngagement;
using Recall = DOL.GS.AutonomousChatRecall;

namespace DOL.GS;

/// <summary>
/// 1.1.3, bots that take notice of you, an evening that remembers itself, and the regulars' running bits (the live part of
/// AutonomousChatEngagement, AutonomousChatRecall and AutonomousChatRegulars). A friend tells you hello when you log in and
/// grats when you ding; a bot by your body says "ouch, u ok?", and a healer that has really set out to rez you says so; a
/// friend in the zone you just reached waves by tell; a bot nearby notices the good item you just put on; a solo bot of your
/// level now and then sends a real group invitation; a bot that turns your invite down says why, like a player; a friend
/// answers your small talk from what it is really doing. Every unprompted line aimed at you goes through one pacer (a few an
/// hour solo, more grouped or among friends), only a bot that isn't cool towards you speaks, and every part is behind its
/// existing switch (grats, death, memory, tells, voices).
/// </summary>
public static partial class AutonomousLivingServer
{
    private sealed class Watch
    {
        public int ZoneId = -1;
        public long ZoneSince;
        public bool ZoneTold = true;
        public Dictionary<int, string> Gear;
    }

    private static readonly Dictionary<string, Watch> Watched = new(StringComparer.OrdinalIgnoreCase);
    private static long _nextEngageScan;
    private static long _nextRealmScan;
    private static readonly Dictionary<long, (long NextAt, int Turn)> NextBit = new();
    private static readonly Dictionary<eRealm, long> NextBitInRealm = new();

    /// <summary>A realm hears a regular's running bit at most once in 8-14 minutes.</summary>
    public const long BitRealmRestMinMs = 8 * 60_000;

    /// <summary>A player who just arrived in a zone stays this long before a friend there notices.</summary>
    public const long ZoneSettleMs = 45_000;

    /// <summary>Friends of the player (a good group or a rez, never cool) who are online, free to talk and of the player's realm.</summary>
    private static IEnumerable<(GameBot Bot, OfflineBotMemoryRecord Row)> FriendsOnline(GamePlayer player)
    {
        if (!MemoryOn || player == null)
            yield break;
        foreach (OfflineBotMemoryRecord row in Memories.Where(r => string.Equals(r.PlayerName, player.Name, StringComparison.OrdinalIgnoreCase) &&
                                                                   MoodOf(r) == "friendly" && r.Grouped + r.Rezzed >= 1))
            if (AutonomousBotRegistry.TryGet(row.BotId, out GameBot bot) && bot.Realm == player.Realm && IsSpeaker(bot))
                yield return (bot, row);
    }

    private static int FriendCount(GamePlayer player) => FriendsOnline(player).Count();

    /// <summary>Takes one of the player's turns for an unprompted line (AutonomousChatEngagement.Pacer).</summary>
    private static bool EngageTurn(GamePlayer player, long now, bool witnessed = false) =>
        Engagement.Live.TryTake(player.Name, now, player.Group != null, FriendCount(player), witnessed);

    private static bool CoolTowards(GameBot bot, GamePlayer player) => TryRecall(bot, player, out OfflineBotMemoryRecord row) && MoodOf(row) == "cool";

    private static bool Friendly(GameBot bot, GamePlayer player) =>
        TryRecall(bot, player, out OfflineBotMemoryRecord row) && MoodOf(row) == "friendly" && row.Grouped + row.Rezzed >= 1;

    private static bool NearPlayer(GameBot bot, GamePlayer player, int range) =>
        bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, range);

    /// <summary>A tell from a bot to a player, styled in the bot's voice, after a typing pause (it must still be free then).</summary>
    private static void TellLater(GameBot bot, GamePlayer player, string text, int delayMs, string invites = null, Action after = null, string role = "engage") =>
        Later(bot, delayMs, () => CanTalk(bot) && player.ObjectState == GameObject.eObjectState.Active && player.Client != null, () =>
        {
            using (AutonomousChatExchange.Tag("player", AutonomousChatExchange.NewId(), 0, role, bot))
                Tell(bot, player, AutonomousBotVoices.StyleFor(bot, text), invites);
            after?.Invoke();
        });

    #region Login, level, death

    /// <summary>The player entered the game: a friend (not a guildmate: the guild greets in /guild) says hello by tell.</summary>
    private static void OnPlayerEnteredEngage(DOLEvent e, object sender, EventArgs args)
    {
        if (sender is not GamePlayer player || !MemoryOn || !TellsOn)
            return;
        lock (Watched)
            Watched.Remove(player.Name);
        // The friends are looked at once the world has settled around the player.
        Later(player, 25_000 + Random.Shared.Next(40_000), () => player.Client != null, () =>
        {
            long now = GameLoop.GameLoopTime;
            var friend = FriendsOnline(player).Where(f => CanTalk(f.Bot) && (player.Guild == null || f.Bot.Guild != player.Guild) &&
                                                          !NearPlayer(f.Bot, player, WorldMgr.SAY_DISTANCE * 2))
                .OrderByDescending(f => f.Row.Grouped + f.Row.Rezzed * 2 + Random.Shared.NextDouble() * 2).FirstOrDefault();
            if (friend.Bot == null || !EngageTurn(player, now))
                return;
            // 1.1.3, after RC6: the duo offer only from a friend free to group now and not setting off somewhere (a bot on its way
            // to a stable master offered "im solo atm if u want company" and answered the invite with "on a horse").
            bool duo = FreeToOffer(friend.Bot, player) && AutonomousBotVoices.TravelingTo(friend.Bot) == null;
            TellLater(friend.Bot, player, Engagement.LoginTell(player.Name, duo, Random.Shared, DateTime.Now.Hour), 0, duo ? AskGroupInvite : null, role: "login");
        });
    }

    /// <summary>The player dinged: one friend who isn't there (the bots nearby say grats in /say) tells them grats.</summary>
    internal static void EngageLevelUp(GamePlayer player, int level)
    {
        if (!GratsOn || !TellsOn || player?.Client == null)
            return;
        long now = GameLoop.GameLoopTime;
        // A guildmate in a bot guild says grats in /guild already (AutonomousBotGuildSocial), a groupmate in /group.
        var friend = FriendsOnline(player).Where(f => CanTalk(f.Bot) && (f.Bot.Group == null || f.Bot.Group != player.Group) &&
                                                      (player.Guild == null || f.Bot.Guild != player.Guild) &&
                                                      !NearPlayer(f.Bot, player, WorldMgr.SAY_DISTANCE))
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (friend.Bot == null || Random.Shared.NextDouble() >= (level >= 50 ? 0.9 : 0.6) || !EngageTurn(player, now))
            return;
        TellLater(friend.Bot, player, Engagement.LevelUpTell(player.Name, level, Random.Shared), 6_000 + Random.Shared.Next(14_000), role: "grats");
    }

    /// <summary>
    /// A solo player died: a bot standing by (a friend first), not fighting and not cool towards them, says so in /say. The
    /// rez itself is offered by the healer that sets out to give it (<see cref="OnRezClaimed"/>).
    /// </summary>
    internal static void EngagePlayerDeath(GamePlayer player)
    {
        if (!DeathOn || player?.CurrentRegion == null || player.Group != null)
            return;
        long now = GameLoop.GameLoopTime;
        GameBot witness = player.GetNPCsInRadius(WorldMgr.SAY_DISTANCE).OfType<GameBot>()
            .Where(bot => bot.Realm == player.Realm && CanTalk(bot) && !bot.IsTemporaryGroupHelper && !CoolTowards(bot, player))
            .OrderByDescending(bot => Friendly(bot, player)).ThenBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (witness == null || Random.Shared.NextDouble() >= 0.6 || !EngageTurn(player, now, witnessed: true))
            return;
        bool friend = Friendly(witness, player);
        Later(witness, 2_500 + Random.Shared.Next(3_000), () => CanTalk(witness) && !player.IsAlive && witness.IsWithinRadius(player, WorldMgr.SAY_DISTANCE), () =>
        {
            using (AutonomousChatExchange.Tag("player", AutonomousChatExchange.NewId(), 0, "death", witness))
                Speak(witness, Channel.Say, AutonomousBotVoices.StyleFor(witness, Engagement.DeathSympathy(player.Name, friend, Random.Shared)));
        });
    }

    private static readonly Dictionary<string, long> RezOfferedFor = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// BotBrain: a healer has set out to rez a player who isn't in its group (it claimed the body). It says so once per death,
    /// in /say when close, else by tell, unless it is already standing over the body (its "rezzing" line comes with the cast).
    /// </summary>
    public static void OnRezClaimed(GameBot bot, GamePlayer corpse)
    {
        if (!DeathOn || !IsSpeaker(bot) || corpse?.Client == null || corpse.IsAlive || CoolTowards(bot, corpse))
            return;
        long now = GameLoop.GameLoopTime;
        lock (RezOfferedFor)
        {
            if (RezOfferedFor.TryGetValue(corpse.Name, out long at) && now - at < 90_000)
                return;
            RezOfferedFor[corpse.Name] = now;
        }
        if (bot.IsWithinRadius(corpse, 250))
            return;
        string line = AutonomousBotVoices.StyleFor(bot, Engagement.RezOffer(corpse.Name, Friendly(bot, corpse), Random.Shared));
        if (bot.IsWithinRadius(corpse, WorldMgr.SAY_DISTANCE))
        {
            using (AutonomousChatExchange.Tag("player", AutonomousChatExchange.NewId(), 0, "rez_offer", bot))
                Speak(bot, Channel.Say, line);
        }
        else if (TellsOn)
            using (AutonomousChatExchange.Tag("player", AutonomousChatExchange.NewId(), 0, "rez_offer", bot))
                Tell(bot, corpse, line);
    }

    #endregion

    #region Invites

    /// <summary>
    /// A real invitation from a bot: the player gets a yes/no box, and "yes" groups them (the player leads, as when they
    /// invite: BotGroupInvite.TryInvite). If the bot got busy or wandered off meanwhile, it says so instead.
    /// </summary>
    internal static void SendRealInvite(GameBot bot, GamePlayer player)
    {
        if (player?.Client == null || bot == null || player.Group != null)
            return;
        player.Out.SendCustomDialog(Engagement.InviteDialog(bot.Name, bot.Level, ClassOf(bot)), (answering, response) =>
        {
            if (answering == null || answering != player)
                return;
            if (response != 0x01)
            {
                Log.Info($"BOT_INVITE_RESULT bot=\"{bot.Name}\" player=\"{player.Name}\" from=bot result=declined_by_player");
                return;
            }
            if (!IsSpeaker(bot) || bot.Group != null || BotGroupInvite.DutyOf(bot) != BotGroupInvite.Duty.None ||
                !BotGroupInvite.CloseEnoughToInviteByName(player, bot))
            {
                Log.Info($"BOT_INVITE_RESULT bot=\"{bot.Name}\" player=\"{player.Name}\" from=bot result=bot_no_longer_free");
                if (IsSpeaker(bot))
                    TellLater(bot, player, Random.Shared.Next(2) == 0 ? "ah sry, got pulled into something, next time?" : "nm, cant now sry, later?", 1_500,
                        role: "decline");
                return;
            }
            bool joined = BotGroupInvite.TryInvite(player, bot);
            Log.Info($"BOT_INVITE_RESULT bot=\"{bot.Name}\" player=\"{player.Name}\" from=bot result={(joined ? "grouped" : "failed")}");
        });
    }

    /// <summary>
    /// The player's invite was turned down (a duty, or the bot follows another player): besides the system line, the bot
    /// answers by tell as a player would ("sry, on the raid in Krondon atm"; a friend adds "catch u after?"). RC5 on the
    /// owner's PC: an invite to a friend on a raid got no word from him at all. Once a minute per bot and player.
    /// </summary>
    public static void OnInviteDeclined(GamePlayer player, GameBot bot, BotGroupInvite.Duty duty, bool inAnotherPlayersGroup)
    {
        if (player?.Client == null || !IsSpeaker(bot))
            return;
        Log.Info($"BOT_INVITE_RESULT bot=\"{bot.Name}\" player=\"{player.Name}\" from=player result=declined duty={duty} other_group={inAnotherPlayersGroup} " +
                 $"zone=\"{PlaceOf(bot)}\"");
        if (!TellsOn || CoolTowards(bot, player))
            return;
        long now = GameLoop.GameLoopTime;
        var key = (bot.DatabaseID, player.Name + "|decline");
        lock (LastTellPair)
        {
            if (LastTellPair.TryGetValue(key, out long at) && now - at < 60_000)
                return;
            LastTellPair[key] = now;
        }
        string line = Engagement.DeclineReply(duty, inAnotherPlayersGroup, PlaceOf(bot), Friendly(bot, player), Random.Shared);
        if (line == null)
            return;
        // A dead bot can type; a busy one answers between pulls.
        Later(bot, 2_000 + Random.Shared.Next(3_000), () => IsSpeaker(bot) && player.Client != null, () =>
        {
            using (AutonomousChatExchange.Tag("player", AutonomousChatExchange.NewId(), 0, "decline", bot))
                Tell(bot, player, AutonomousBotVoices.StyleFor(bot, line));
        });
    }

    /// <summary>What became of the player's invite of an autonomous gamebot (grouped, or failed and why), for the PC's log.</summary>
    public static void LogInviteResult(GamePlayer player, GameBot bot, string result) =>
        Log.Info($"BOT_INVITE_RESULT bot=\"{bot?.Name}\" player=\"{player?.Name}\" from=player result={result}");

    #endregion

    #region Small talk with a friend

    /// <summary>
    /// A friend's answer to the player's small talk by tell ("hey what are you up to tonight?"), from what it is really doing
    /// and where they grouped; <paramref name="offers"/> when it asks the player along. Null when the bot isn't a friend or the
    /// tell isn't small talk (the caller answers as before).
    /// </summary>
    internal static string TryFriendChat(GameBot bot, GamePlayer player, string message, out bool offers)
    {
        offers = false;
        if (!MemoryOn || bot == null || player == null || !Engagement.IsSmallTalk(message) ||
            !TryRecall(bot, player, out OfflineBotMemoryRecord row) || MoodOf(row) != "friendly" || row.Grouped + row.Rezzed < 1)
            return null;
        AutonomousChatFieldTalk.Facts facts = AutonomousChatFieldTalk.Of(bot, Random.Shared);
        string zone = string.IsNullOrWhiteSpace(facts.Zone) ? null : facts.Zone;
        var doing = new Engagement.Doing(zone, facts.InGroup || facts.InTown || facts.InRvR ? null : facts.Monster, facts.Destination, facts.InGroup,
            facts.InTown, facts.InRvR, facts.InDungeon);
        offers = FreeToOffer(bot, player);
        return Engagement.FriendChat(player.Name, doing, row.LastPlace, offers, Random.Shared);
    }

    /// <summary>
    /// Whether a bot may offer to group with the player (1.1.3, after RC6): both out of a group, the bot on no duty and not riding a
    /// stable route, and within three levels. Every offer to group by tell asks this (a friend's small talk, its login hello). Pure.
    /// </summary>
    public static bool FreeToOffer(bool playerGrouped, bool botGrouped, BotGroupInvite.Duty duty, bool onStableRoute, int botLevel, int playerLevel) =>
        !playerGrouped && !botGrouped && duty == BotGroupInvite.Duty.None && !onStableRoute && Math.Abs(botLevel - playerLevel) <= 3;

    internal static bool FreeToOffer(GameBot bot, GamePlayer player) =>
        bot != null && player != null && FreeToOffer(player.Group != null, bot.Group != null, BotGroupInvite.DutyOf(bot), bot.IsOnStableMasterRoute,
            bot.Level, player.Level);

    #endregion

    #region The pulse: zones, gear, follow-ups, running bits

    /// <summary>Every five seconds on the game loop (AutonomousLivingServer.Pulse).</summary>
    internal static void EngagePulse(long now)
    {
        if (now < _nextEngageScan)
            return;
        _nextEngageScan = now + 5_000;
        if (MemoryOn)
            foreach (GamePlayer player in ClientService.Instance.GetPlayers())
            {
                if (player?.Client == null || player.ObjectState != GameObject.eObjectState.Active || player.CurrentRegion == null)
                    continue;
                Watch watch;
                lock (Watched)
                    if (!Watched.TryGetValue(player.Name, out watch))
                        Watched[player.Name] = watch = new Watch();
                WatchZone(player, watch, now);
                WatchGear(player, watch, now);
            }
        if (!AutonomousBotVoices.Enabled || now < _nextRealmScan)
            return;
        _nextRealmScan = now + 15_000;
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            if (!RealmHasPlayer(realm) || AutonomousBotDebates.IsRunning(realm))
                continue;
            try
            {
                if (!TryFollowUp(realm, now))
                    TryBit(realm, now);
            }
            catch (Exception exception)
            {
                Log.Warn("Living Server: follow-up or running bit failed", exception);
            }
        }
    }

    private static void WatchZone(GamePlayer player, Watch watch, long now)
    {
        Zone zone = player.CurrentZone;
        if (zone == null)
            return;
        if (zone.ID != watch.ZoneId)
        {
            // The first zone seen (log-in) is no arrival.
            watch.ZoneTold = watch.ZoneId < 0;
            watch.ZoneId = zone.ID;
            watch.ZoneSince = now;
            return;
        }
        if (watch.ZoneTold || now - watch.ZoneSince < ZoneSettleMs || !TellsOn || !player.IsAlive || player.InCombat)
            return;
        watch.ZoneTold = true;
        // A capital or a dungeon is no place to wave from across the zone.
        if (zone.IsDungeon || player.CurrentRegion.IsCapitalCity)
            return;
        var friend = FriendsOnline(player).Where(f => CanTalk(f.Bot) && f.Bot.CurrentZone == zone && (f.Bot.Group == null || f.Bot.Group != player.Group) &&
                                                      !NearPlayer(f.Bot, player, WorldMgr.SAY_DISTANCE * 2))
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (friend.Bot == null || !EngageTurn(player, now))
            return;
        TellLater(friend.Bot, player, Engagement.ZoneTell(player.Name, zone.Description, Random.Shared), 2_000 + Random.Shared.Next(6_000), role: "zone");
    }

    private static void WatchGear(GamePlayer player, Watch watch, long now)
    {
        var gear = new Dictionary<int, string>();
        DbInventoryItem fresh = null;
        try
        {
            foreach (DbInventoryItem item in (IEnumerable<DbInventoryItem>)player.Inventory?.EquippedItems ?? Array.Empty<DbInventoryItem>())
            {
                if (item == null)
                    continue;
                gear[item.SlotPosition] = item.Id_nb;
                if (watch.Gear != null && (!watch.Gear.TryGetValue(item.SlotPosition, out string before) || before != item.Id_nb) &&
                    Engagement.NotableGear(item.Quality, item.Level, player.Level, true) && !string.IsNullOrWhiteSpace(item.Name))
                    fresh = item;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        bool first = watch.Gear == null;
        watch.Gear = gear;
        if (first || fresh == null || !player.IsAlive || player.InCombat)
            return;
        GameBot noticer = player.GetNPCsInRadius(WorldMgr.SAY_DISTANCE).OfType<GameBot>()
            .Where(bot => bot.Realm == player.Realm && CanTalk(bot) && !bot.IsTemporaryGroupHelper && !CoolTowards(bot, player))
            .OrderByDescending(bot => Friendly(bot, player)).ThenBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (noticer == null || Random.Shared.NextDouble() >= 0.5 || !EngageTurn(player, now))
            return;
        bool friend = Friendly(noticer, player);
        string itemName = fresh.Name;
        bool grouped = noticer.Group != null && noticer.Group == player.Group;
        Later(noticer, 3_000 + Random.Shared.Next(5_000), () => CanTalk(noticer) && noticer.IsWithinRadius(player, WorldMgr.SAY_DISTANCE), () =>
        {
            string line = AutonomousBotVoices.StyleFor(noticer, Engagement.GearLine(player.Name, itemName, friend, Random.Shared));
            using (AutonomousChatExchange.Tag("player", AutonomousChatExchange.NewId(), 0, "gear", noticer))
                Speak(noticer, grouped ? Channel.Group : Channel.Say, line);
        });
    }

    private static bool FactionFree(GameBot bot) => CanTalk(bot) && !bot.IsOnStableMasterRoute && !bot.IsTemporaryGroupHelper && !AutonomousBeggars.Is(bot);

    /// <summary>Under the realm's /faction target (not just its ceiling): a follow-up or a bit never crowds the channel.</summary>
    private static bool UnderTarget(eRealm realm)
    {
        var band = AutonomousChatBudget.LiveBand(AutonomousChatBudget.Channel.Faction, AutonomousBotChatCoordinator.RealmBots(realm));
        return AutonomousChatBudget.Meter.PerMinute(AutonomousChatBudget.Channel.Faction, (long)realm, GameLoop.GameLoopTime) < band.Target &&
               AutonomousBotChatCoordinator.FactionRoom(realm) && AutonomousChatExchange.RoomFor(AutonomousChatExchange.FactionKey(realm), GameLoop.GameLoopTime);
    }

    /// <summary>A follow-up to something the realm's /faction carried tonight, asked by a regular (or the other debater).</summary>
    private static bool TryFollowUp(eRealm realm, long now)
    {
        Recall.Store store = Recall.Of(realm);
        if (store.Count == 0 || !UnderTarget(realm))
            return false;
        Recall.Note[] due = store.Due(now);
        if (due.Length == 0 || !store.TryTurn(now, Random.Shared))
            return false;
        Recall.Note note = Recall.Choose(due, id => AutonomousBotRegistry.TryGet(id, out GameBot b) && b.Realm == realm && FactionFree(b), Random.Shared);
        if (note == null || !AutonomousBotRegistry.TryGet(note.BotId, out GameBot subject))
            return false;
        GameBot asker = null;
        if (note.Kind == Recall.Kind.Debated && AutonomousBotRegistry.TryGet(note.OtherId, out GameBot other) && FactionFree(other))
            asker = other;
        int realmBots = AutonomousBotChatCoordinator.RealmBots(realm);
        asker ??= AutonomousBotRegistry.Snapshot().Where(bot => bot != subject && bot.Realm == realm && FactionFree(bot) &&
                                                                 AutonomousChatRegulars.IsRegular(bot.DatabaseID, AutonomousBotPersonality.For(bot).Type, now, realmBots))
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (asker == null)
            return false;
        AutonomousChatFieldTalk.Facts facts = AutonomousChatFieldTalk.Of(subject, Random.Shared);
        var state = new Recall.Now(facts.InGroup, facts.AtCamp, facts.Zone);
        if (Recall.FollowUp(note, state, store.Deaths(note.BotId, now), Random.Shared) is not { } lines)
            return false;
        store.MarkUsed(note);
        string lane = AutonomousChatExchange.FactionKey(realm);
        var plan = new List<AutonomousChatExchange.Line>
        {
            new(0, AutonomousChatIdiolect.Apply(lines.Ask, asker.DatabaseID, false, Random.Shared, terse: true), 0, "asker"),
            new(1, AutonomousChatIdiolect.Apply(lines.Answer, subject.DatabaseID, false, Random.Shared, terse: true), 5_000 + Random.Shared.Next(6_000), "answerer"),
        };
        AutonomousChatExchange.Play("followup", AutonomousChatExchange.NewId(), lane, plan, [asker, subject],
            bot => FactionFree(bot) && RealmHasPlayer(realm) && !AutonomousBotDebates.IsRunning(realm) && AutonomousBotChatCoordinator.FactionRoom(realm),
            (bot, line) =>
            {
                if (!AutonomousBotChatCoordinator.BroadcastFaction(bot.Name, realm, line.Text))
                    return false;
                AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), bot, line.Text, null);
                return true;
            }, AutonomousChatExchange.Rules.Faction, firstIndex: 0);
        return true;
    }

    /// <summary>A regular's running bit, now and then, under the realm's target.</summary>
    private static void TryBit(eRealm realm, long now)
    {
        lock (NextBitInRealm)
        {
            if (NextBitInRealm.TryGetValue(realm, out long next) && now < next)
                return;
        }
        if (!UnderTarget(realm))
            return;
        int realmBots = AutonomousBotChatCoordinator.RealmBots(realm);
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot().Where(b => b.Realm == realm && FactionFree(b)).OrderBy(_ => Random.Shared.Next()))
        {
            BotPersonalityType type = AutonomousBotPersonality.For(bot).Type;
            if (!AutonomousChatRegulars.IsRegular(bot.DatabaseID, type, now, realmBots))
                continue;
            int turn;
            lock (NextBit)
            {
                if (NextBit.TryGetValue(bot.DatabaseID, out var state) && now < state.NextAt)
                    continue;
                turn = state.Turn;
            }
            bool officer = bot.Guild != null && AutonomousBotRealmGuilds.IsBotGuild(bot.Guild);
            string bit = AutonomousChatRegulars.BitOf(bot.DatabaseID, AutonomousChatRegulars.ArchetypeOf(bot.DatabaseID, type, officer), bot.Guild?.Name, turn);
            if (bit == null)
                continue;
            string text = AutonomousBotVoices.StyleFor(bot, bit);
            bool said;
            using (AutonomousChatExchange.Tag("bit", AutonomousChatExchange.NewId(), 0, "opener", bot))
                said = AutonomousBotChatCoordinator.BroadcastFaction(bot.Name, realm, text);
            if (!said)
                return;
            AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), bot, text, null);
            lock (NextBit)
            {
                NextBit[bot.DatabaseID] = (now + AutonomousChatRegulars.BitRestMs(bot.DatabaseID), turn + 1);
                if (NextBit.Count > 4_096)
                    NextBit.Clear();
            }
            lock (NextBitInRealm)
                NextBitInRealm[realm] = now + BitRealmRestMinMs + Random.Shared.Next(6 * 60_000);
            return;
        }
    }

    #endregion
}
