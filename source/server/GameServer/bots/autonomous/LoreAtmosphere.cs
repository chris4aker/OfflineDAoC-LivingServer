using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// What the atmosphere lines remember across restarts (Living Server 1.2): a character's first-entry line for a place today
/// ("entry:{location}:{character}" = day). One row per character and place. An optional progress table (progress-policy.json).
/// A realm's first kills are LoreFirstKills' (the realm event ledger).
/// </summary>
[DataTable(TableName = "offline_lore_memory")]
public sealed class OfflineLoreMemoryRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineLoreMemoryRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Unique = true, Varchar = 160)] public string MemoryKey { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 128)] public string Value { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string UpdatedUtc { get; set; } = string.Empty;
}

/// <summary>
/// The screen-centre atmosphere lines (Living Server 1.2, docs/LORE.md; the owner's favourite channel): one service every
/// lore trigger goes through, on the existing centre-text path (CT_ScreenCenter / CT_ScreenCenterSmaller; the new client
/// draws them in game_ui.gd). Real players only (bots get no packets). Each player has an <see cref="AtmosphereThrottle"/>.
/// Local lines need the launcher's "Atmosphere lines" switch (<c>living_atmosphere_lines</c>, on by default); Darkness Falls
/// changing hands follows the RvR announcements switch instead, as it is realm news.
/// </summary>
public static class LoreAtmosphere
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool On => Properties.LIVING_ATMOSPHERE_LINES;

    public const long PulseMs = 1_000;
    public const long PollMs = 3_000;
    public const long EntryTtlMs = 30_000;
    public const long AreaTtlMs = 20_000;
    public const long PhaseTtlMs = 8_000;
    public const long RealmTtlMs = 60_000;
    public const int LairRadius = 1_500;

    internal static readonly LivingServerTable<string, OfflineLoreMemoryRecord> Memory =
        new("lore memory", row => row.MemoryKey, StringComparer.OrdinalIgnoreCase);

    private sealed class PlayerState
    {
        public readonly AtmosphereThrottle Throttle = new();
        public readonly HashSet<string> Session = new(StringComparer.OrdinalIgnoreCase);
        public string LocationId;
        public bool Polled;
        public long NextPoll;
        public long NewsSeen;
    }

    private static readonly Dictionary<GamePlayer, PlayerState> Players = new();
    private static readonly ConcurrentQueue<(GamePlayer Player, AtmosphereThrottle.Line Line)> Offers = new();
    private static readonly ConcurrentQueue<(eRealm Realm, AtmosphereThrottle.Line Line, Func<GamePlayer, bool> Who)> RealmOffers = new();
    private static readonly long[] NewsAt = new long[4];   // index 0: every realm; 1-3 one realm
    private static long _nextPulse;
    private static int _pulsing;
    private static Timer _flushTimer;

    #region Lifecycle

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            Memory.Load(GameServer.Database.SelectAllObjects<OfflineLoreMemoryRecord>());
        }
        catch (Exception exception)
        {
            Log.Warn("Lore: could not read offline_lore_memory; first entries and first kills start fresh", exception);
        }
        _flushTimer = new Timer(_ => Memory.Flush(), null, 10_000, 10_000);
        // The lore channels (loremasters, books, codex, Herald, first kills) read the engine's bible from here on.
        LoreChannels.LoreChannelHub.UseSource(new LoreBibleSource(LoreBible.Current));
        LoreBossWatch.Start();
        if (Log.IsInfoEnabled)
            Log.Info($"Lore: atmosphere lines {(On ? "on" : "off")}, {Memory.Count} remembered first entries");
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        LoreBossWatch.Stop();
        _flushTimer?.Dispose();
        _flushTimer = null;
        Memory.Flush(wait: true);
    }

    #endregion

    #region Memory

    /// <summary>The day this character last saw the place's first-entry line, or null.</summary>
    public static string EntryDay(string characterId, string locationId) =>
        Memory.TryGet($"{LoreEntryRules.EntryKey(locationId)}:{characterId}", out OfflineLoreMemoryRecord row) ? row.Value : null;

    private static void RememberEntry(string characterId, string locationId, string day) =>
        Memory.Change($"{LoreEntryRules.EntryKey(locationId)}:{characterId}",
            () => new OfflineLoreMemoryRecord { MemoryKey = $"{LoreEntryRules.EntryKey(locationId)}:{characterId}" },
            row =>
            {
                row.Value = day;
                row.UpdatedUtc = DateTime.UtcNow.ToString("O");
            });

    #endregion

    #region Offers (any thread)

    private static bool Hears(GamePlayer player) =>
        player?.Client != null && player.Client.ClientState == GameClient.eClientState.Playing && player.ObjectState == GameObject.eObjectState.Active;

    /// <summary>An atmosphere line for one player (the smaller centre text), throttled. Needs the switch.</summary>
    public static void Local(GamePlayer player, string text, long ttlMs, string onceKey = null, bool perDay = false)
    {
        if (!On || player == null || string.IsNullOrWhiteSpace(text))
            return;
        Offers.Enqueue((player, new AtmosphereThrottle.Line(text, false, false, null, GameLoop.GameLoopTime + ttlMs) { OnceKey = onceKey, OncePerDay = perDay }));
    }

    /// <summary>An atmosphere line for every player in a zone (a boss phase), each throttled on their own.</summary>
    public static void Zone(Zone zone, string text, long ttlMs)
    {
        if (!On || zone == null || string.IsNullOrWhiteSpace(text))
            return;
        foreach (GamePlayer player in ClientService.Instance.GetPlayersOfZone(zone))
            Local(player, text, ttlMs);
    }

    /// <summary>
    /// A realm-scale lore line (a realm's first kill, a dragon waking, a raid forming): news priority, never held by the 30 s
    /// gap. <paramref name="realm"/> None is every realm. <paramref name="lore"/> false (Darkness Falls) skips the
    /// atmosphere switch.
    /// </summary>
    public static void Realm(eRealm realm, string text, bool large, string chatCopy, Func<GamePlayer, bool> who = null, bool lore = true)
    {
        if (lore && !On || string.IsNullOrWhiteSpace(text))
            return;
        RealmOffers.Enqueue((realm, new AtmosphereThrottle.Line(text, true, large, chatCopy, GameLoop.GameLoopTime + RealmTtlMs), who));
    }

    /// <summary>Realm news went out on the centre of the screen (keeps, relics, raid ends, sieges): atmosphere lines wait.</summary>
    public static void NoteNews(eRealm realm = eRealm.None)
    {
        long now = GameLoop.GameLoopTime;
        int index = realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia ? (int)realm : 0;
        Interlocked.Exchange(ref NewsAt[index], now);
    }

    #endregion

    #region Pulse (game loop)

    /// <summary>From the notice pulse: polls where each player is, then sends what each throttle lets through.</summary>
    public static void Pulse(long now)
    {
        if (now < Interlocked.Read(ref _nextPulse) || Interlocked.Exchange(ref _pulsing, 1) == 1)
            return;
        try
        {
            Interlocked.Exchange(ref _nextPulse, now + PulseMs);
            LoreBossWatch.Pulse(now);
            List<GamePlayer> online = ClientService.Instance.GetPlayers();
            foreach (GamePlayer gone in Players.Keys.Where(p => !online.Contains(p)).ToList())
                Players.Remove(gone);
            lock (Forget)
            {
                foreach (GamePlayer player in Forget)
                    if (Players.TryGetValue(player, out PlayerState forgotten))
                    {
                        forgotten.Session.Clear();
                        forgotten.Polled = false;
                        forgotten.NextPoll = 0;
                    }
                Forget.Clear();
            }
            while (Offers.TryDequeue(out var offer))
                if (offer.Line != null && Hears(offer.Player) && !Shown(State(offer.Player), offer.Player, offer.Line))
                    State(offer.Player).Throttle.Offer(offer.Line, now);
            while (RealmOffers.TryDequeue(out var offer))
                foreach (GamePlayer player in online)
                    if (Hears(player) && (offer.Realm == eRealm.None || player.Realm == offer.Realm) && (offer.Who == null || offer.Who(player)))
                        State(player).Throttle.Offer(offer.Line, now);
            foreach (GamePlayer player in online)
            {
                if (!Hears(player))
                    continue;
                PlayerState state = State(player);
                long news = Math.Max(Interlocked.Read(ref NewsAt[0]),
                    player.Realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia ? Interlocked.Read(ref NewsAt[(int)player.Realm]) : 0);
                if (news > state.NewsSeen)
                {
                    state.NewsSeen = news;
                    state.Throttle.NoteNews(news);
                }
                if (On && now >= state.NextPoll)
                {
                    state.NextPoll = now + PollMs;
                    Poll(player, state, now);
                }
                foreach (AtmosphereThrottle.Line line in state.Throttle.Due(now))
                    Send(player, state, line);
            }
        }
        catch (Exception exception)
        {
            Log.Warn("Lore: atmosphere pulse failed", exception);
        }
        finally
        {
            Volatile.Write(ref _pulsing, 0);
        }
    }

    private static PlayerState State(GamePlayer player)
    {
        if (!Players.TryGetValue(player, out PlayerState state))
            Players[player] = state = new PlayerState();
        return state;
    }

    private static bool Shown(PlayerState state, GamePlayer player, AtmosphereThrottle.Line line)
    {
        if (line.OnceKey == null)
            return false;
        if (!line.OncePerDay)
            return state.Session.Contains(line.OnceKey);
        string location = line.OnceKey[(LoreEntryRules.EntryKey(string.Empty).Length)..];
        return EntryDay(player.ObjectId, location) == LoreEntryRules.Day(DateTime.Now);
    }

    private static void Send(GamePlayer player, PlayerState state, AtmosphereThrottle.Line line)
    {
        if (line.OnceKey != null && Shown(state, player, line))
            return;
        player.Out.SendMessage(line.Text, line.Large ? eChatType.CT_ScreenCenter : eChatType.CT_ScreenCenterSmaller, eChatLoc.CL_SystemWindow);
        if (!string.IsNullOrWhiteSpace(line.ChatCopy))
            player.Out.SendMessage(line.ChatCopy, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
        if (line.OnceKey == null)
            return;
        if (line.OncePerDay)
            RememberEntry(player.ObjectId, line.OnceKey[(LoreEntryRules.EntryKey(string.Empty).Length)..], LoreEntryRules.Day(DateTime.Now));
        else
            state.Session.Add(line.OnceKey);
    }

    /// <summary>Where the player is now: a first entry into a location, a named area, a lair.</summary>
    private static void Poll(GamePlayer player, PlayerState state, long now)
    {
        LoreBible bible = LoreBible.Current;
        if (bible.Locations.Length == 0 || player.CurrentRegion == null || player.CurrentZone == null)
            return;
        ushort region = player.CurrentRegionID;
        LoreBible.Location here = LoreEntryRules.LocationAt(bible, region, player.CurrentZone.ID);
        string previous = state.Polled ? state.LocationId : null;
        state.Polled = true;
        state.LocationId = here?.Id;
        Random random = Random.Shared;
        if (here != null && here.FirstEntry.Length > 0 &&
            LoreEntryRules.EntryDue(previous, here.Id, EntryDay(player.ObjectId, here.Id), LoreEntryRules.Day(DateTime.Now)))
            Local(player, here.FirstEntry[random.Next(here.FirstEntry.Length)], EntryTtlMs, LoreEntryRules.EntryKey(here.Id), perDay: true);
        // Every location's areas in this region, not only this place's: an umbrella's lair areas (hibernia-wilds) inside a
        // dragon's zone, and areas of a location with no regions of its own (shrouded-isles).
        foreach ((LoreBible.Location owner, LoreBible.Area area) in bible.AreasInRegion(region))
        {
            string key = LoreEntryRules.AreaKey(owner.Id, area.Name);
            if (area.Lines.Length > 0 && !state.Session.Contains(key) && LoreEntryRules.InArea(area, region, player.X, player.Y, player.Z))
                Local(player, area.Lines[random.Next(area.Lines.Length)], AreaTtlMs, key);
        }
        foreach (GameNPC npc in player.GetNPCsInRadius(LairRadius))
        {
            if (npc is GameBot || !npc.IsAlive)
                continue;
            LoreBible.Boss boss = LoreBossWatch.BossOf(npc);
            if (boss == null || boss.LairEntry.Length == 0)
                continue;
            string key = LoreEntryRules.LairKey(boss.Id);
            if (!state.Session.Contains(key))
                Local(player, boss.LairEntry[random.Next(boss.LairEntry.Length)], AreaTtlMs, key);
        }
    }

    /// <summary>
    /// /lstest lore: where the lore puts the GM, and their first-entry (today), area and lair marks forgotten, so the lines
    /// show again on the next look (about 3 s) as they would for a player walking in.
    /// </summary>
    public static string TestHere(GamePlayer player)
    {
        if (!On)
            return "Atmosphere lines are off (living_atmosphere_lines).";
        LoreBible bible = LoreBible.Current;
        LoreBible.Location here = player.CurrentZone == null ? null : LoreEntryRules.LocationAt(bible, player.CurrentRegionID, player.CurrentZone.ID);
        if (here != null)
            Memory.Remove($"{LoreEntryRules.EntryKey(here.Id)}:{player.ObjectId}");
        lock (Forget)
            Forget.Add(player);
        return here == null
            ? $"No lore place here (region {player.CurrentRegionID}, zone {player.CurrentZone?.ID}). {bible.Locations.Length} places and {bible.Bosses.Length} bosses are loaded."
            : $"You are in {here.Name} ({here.Id}): {here.FirstEntry.Length} first-entry line(s), {here.Areas.Length} area(s), " +
              $"{bible.BossesAt(here.Id).Length} boss file(s). Its lines may show again now.";
    }

    private static readonly HashSet<GamePlayer> Forget = new();

    #endregion

    #region Realm-scale triggers

    private static readonly Dictionary<string, long> LastRealmLine = new(StringComparer.Ordinal);

    private static bool Due(string key, long gap)
    {
        long now = GameLoop.GameLoopTime;
        lock (LastRealmLine)
        {
            if (!LoreOnceRules.GapOver(LastRealmLine.TryGetValue(key, out long last) ? last : null, now, gap))
                return false;
            LastRealmLine[key] = now;
            return true;
        }
    }

    /// <summary>
    /// A dragon takes to the sky (its brain set IsRestless; HibCuuldurach, AlbGolestandt, MidGjalpinulva): the gold line for
    /// the players in its zone, the smaller one for its realm-mates elsewhere. At most once an hour per dragon. The
    /// script's own bellow in chat stays.
    /// </summary>
    public static void DragonWakes(GameNPC dragon)
    {
        if (!On || dragon == null)
            return;
        LoreBible.Boss boss = LoreBible.Current.BossForClass(dragon.GetType().Name);
        string wakePhase = boss?.AtmospherePhases.FirstOrDefault(p => LoreFightRules.TryParseTrigger(p.Trigger, out var t) && t.Kind == LoreFightRules.Kind.Wake)?.Text;
        string text = !string.IsNullOrWhiteSpace(boss?.Wake) ? boss.Wake : !string.IsNullOrWhiteSpace(wakePhase) ? wakePhase : $"{dragon.Name} takes to the sky!";
        if (!Due("wake:" + dragon.GetType().Name, LoreOnceRules.DragonWakeGapMs))
            return;
        LoreBossWatch.EventYell(dragon, boss, LoreFightRules.Kind.Wake);
        Zone zone = dragon.CurrentZone;
        eRealm home = dragon.Realm != eRealm.None ? dragon.Realm : RealmOf(boss);
        Realm(eRealm.None, text, true, null, player => player.CurrentZone == zone);
        if (home != eRealm.None)
            RealmSmaller(home, text, player => player.CurrentZone != zone);
    }

    private static void RealmSmaller(eRealm realm, string text, Func<GamePlayer, bool> who)
    {
        if (!On || string.IsNullOrWhiteSpace(text))
            return;
        RealmOffers.Enqueue((realm, new AtmosphereThrottle.Line(text, true, false, null, GameLoop.GameLoopTime + RealmTtlMs), who));
    }

    public static eRealm RealmOf(LoreBible.Boss boss) => boss?.Realm switch
    {
        "Albion" => eRealm.Albion,
        "Midgard" => eRealm.Midgard,
        "Hibernia" => eRealm.Hibernia,
        _ => eRealm.None,
    };

    /// <summary>
    /// Darkness Falls changed hands (DFEnterJumpPoint.OnKeepTaken): the gold line for everyone, at most once every 15
    /// minutes. The CT_Important lines the script sends stay as the chat copy. Follows the RvR announcements switch.
    /// </summary>
    public static void DarknessFallsChanged(string message)
    {
        if (!GameWideAnnouncements.Enabled(AnnouncementKind.RvrBattleground) || !Due("darkness-falls", LoreOnceRules.DarknessFallsGapMs))
            return;
        Realm(eRealm.None, message, true, null, lore: false);
    }

    /// <summary>A raid forms (AutonomousRealmRaid): the location's raidForming line for that realm's players, after the forming notice.</summary>
    public static void RaidForming(eRealm realm, ushort region, string raidName)
    {
        LoreBible bible = LoreBible.Current;
        LoreBible.Location location = bible.LocationsInRegion(region).FirstOrDefault(l => l.RaidForming.Length > 0) ??
                                      bible.Locations.FirstOrDefault(l => l.RaidForming.Length > 0 && string.Equals(l.Name, raidName, StringComparison.OrdinalIgnoreCase));
        if (location == null)
            return;
        RealmSmaller(realm, location.RaidForming[Random.Shared.Next(location.RaidForming.Length)], null);
    }

    /// <summary>A realm killed a boss for the first time ever: the gold line to the whole world with a chat copy naming the realm.</summary>
    public static void FirstKill(eRealm realm, LoreBible.Boss boss)
    {
        string realmName = GlobalConstants.RealmToName(realm);
        string text = !string.IsNullOrWhiteSpace(boss.RealmFirstKill) ? boss.RealmFirstKill : $"{boss.Name} has fallen.";
        Realm(eRealm.None, text, true, $"{realmName} has slain {boss.Name} for the first time. {text}");
    }

    #endregion
}
