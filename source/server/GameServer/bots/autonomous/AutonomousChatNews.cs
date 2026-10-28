using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// One piece of world news as a realm sees it (the talking update): "keep_lost" for the realm that lost the keep, "keep_won"
/// for the one that took it, "keep_other" for the third. The slots are the names a line may use.
/// </summary>
public sealed record ChatNewsItem(string Topic, eRealm Realm, DateTime When)
{
    public long Id { get; init; }
    public string Keep { get; init; }
    public string Relic { get; init; }
    public string Raid { get; init; }
    public string Boss { get; init; }
    public string Zone { get; init; }
    public eRealm Enemy { get; init; }
    public eRealm Winner { get; init; }
    public eRealm Loser { get; init; }
    public int Size { get; init; }
    public ushort RegionId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    /// <summary>A key that marks the same event in other places (a keep's "rvr-keep-12"), so it is voiced once.</summary>
    public string EventKey { get; init; }

    public Dictionary<string, string> Slots()
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                slots[key] = value;
        }
        Add("keep", Keep);
        Add("relic", Relic);
        Add("raid", Raid);
        Add("boss", Boss);
        Add("zone", Zone);
        if (Enemy != eRealm.None) Add("enemy", GlobalConstants.RealmToName(Enemy));
        if (Winner != eRealm.None) Add("winner", GlobalConstants.RealmToName(Winner));
        if (Loser != eRealm.None) Add("loser", GlobalConstants.RealmToName(Loser));
        if (Size > 0) Add("size", Size.ToString());
        return slots;
    }
}

/// <summary>
/// The recent news of the world, by realm (pure, unit-tested). Corpus lines that speak of news need its flags
/// ("keep_lost_recent"), and their slots come from the matching item, so "lost {keep}" names the keep that was lost.
/// </summary>
public sealed class ChatNewsBoard
{
    public const int Capacity = 300;
    public static readonly TimeSpan KeepWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan RelicWindow = TimeSpan.FromHours(4);
    public static readonly TimeSpan RaidWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan DfWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan EnemyWindow = TimeSpan.FromMinutes(15);

    public const int SightingCapacity = 120;
    private readonly List<ChatNewsItem> _items = new();
    private readonly List<ChatNewsItem> _sightings = new();
    private long _nextId = 1;

    public ChatNewsItem Post(ChatNewsItem item)
    {
        lock (_items)
        {
            item = item with { Id = _nextId++ };
            // Sightings come often (one per realm and zone a minute); they are kept apart so they never push keep, relic and
            // raid news off the board.
            if (item.Topic == "enemy_seen")
            {
                _sightings.Add(item);
                if (_sightings.Count > SightingCapacity)
                    _sightings.RemoveRange(0, _sightings.Count - SightingCapacity);
                return item;
            }
            _items.Add(item);
            if (_items.Count > Capacity)
                _items.RemoveRange(0, _items.Count - Capacity);
            return item;
        }
    }

    public ChatNewsItem[] Snapshot()
    {
        lock (_items)
            return _items.ToArray();
    }

    /// <summary>The newest item of these topics for the realm within the window, or null.</summary>
    public ChatNewsItem Latest(eRealm realm, TimeSpan within, DateTime now, params string[] topics)
    {
        lock (_items)
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                ChatNewsItem item = _items[i];
                if (now - item.When > within)
                    continue;
                if (item.Realm == realm && topics.Contains(item.Topic))
                    return item;
            }
        return null;
    }

    /// <summary>The flags a speaker of this realm has from the news, each with the slots its lines use.</summary>
    public Dictionary<string, IReadOnlyDictionary<string, string>> FlagSlots(eRealm realm, DateTime now)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        void From(string flag, TimeSpan window, params string[] topics)
        {
            ChatNewsItem item = Latest(realm, window, now, topics);
            if (item != null)
                result[flag] = item.Slots();
        }
        From("keep_news", KeepWindow, "keep_won", "keep_lost", "keep_other");
        From("keep_lost_recent", KeepWindow, "keep_lost");
        From("keep_won_recent", KeepWindow, "keep_won");
        From("relic_lost_recent", RelicWindow, "relic_lost");
        From("relic_won_recent", RelicWindow, "relic_won");
        From("raid_won_recent", RaidWindow, "raid_won");
        From("raid_wiped_recent", RaidWindow, "raid_wiped");
        From("df_changed_recent", DfWindow, "df_won", "df_lost");
        return result;
    }

    /// <summary>Enemy groups reported in a realm's frontier recently (what the realm knows, for "where are the mids?").</summary>
    public ChatNewsItem[] EnemySightings(eRealm realm, DateTime now)
    {
        lock (_items)
            return _sightings.Where(item => item.Realm == realm && now - item.When <= EnemyWindow)
                .OrderByDescending(item => item.When).ToArray();
    }
}

/// <summary>
/// The talking update's world news (docs/bot-voices/TALKING-UPDATE.md). It watches keeps, relics, Darkness Falls, realm raids
/// and enemy sightings, keeps them on a <see cref="ChatNewsBoard"/>, gives speakers the flags and slots that make news lines
/// true, and has a bot tell /faction about news no realm notice already announced ("heard we lost Caer Benowyc").
/// </summary>
public static class AutonomousChatNews
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousChatNews));
    public static readonly ChatNewsBoard Board = new();
    private static readonly object Sync = new();
    private static readonly Dictionary<ushort, eRealm> KeepOwners = new();
    private static eRealm _dfOwner = eRealm.None;
    private static bool _handlers;

    /// <summary>News waiting to be told in /faction, oldest first, at most one per realm every <see cref="VoiceGapMs"/>.</summary>
    private static readonly List<(ChatNewsItem Item, long Due, long Expires)> ToVoice = new();
    private static readonly Dictionary<eRealm, long> LastVoiced = new();
    public const int VoiceGapMs = 45_000;
    public const int MaxQueued = 6;

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        lock (Sync)
        {
            try
            {
                foreach (AbstractGameKeep keep in GameServer.KeepManager.GetAllKeeps())
                    KeepOwners[keep.KeepID] = keep.Realm;
            }
            catch (Exception exception)
            {
                Log.Warn("Chat news: could not read the keeps", exception);
            }
            _dfOwner = DFEnterJumpPoint.DarknessFallOwner;
            if (!_handlers)
            {
                GameEventMgr.AddHandlerUnique(KeepEvent.KeepTaken, OnKeepTaken);
                GameEventMgr.AddHandlerUnique(RelicPadEvent.RelicStolen, OnRelic);
                GameEventMgr.AddHandlerUnique(RelicPadEvent.RelicMounted, OnRelic);
                _handlers = true;
            }
        }
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        lock (Sync)
        {
            if (!_handlers)
                return;
            GameEventMgr.RemoveHandler(KeepEvent.KeepTaken, OnKeepTaken);
            GameEventMgr.RemoveHandler(RelicPadEvent.RelicStolen, OnRelic);
            GameEventMgr.RemoveHandler(RelicPadEvent.RelicMounted, OnRelic);
            _handlers = false;
        }
    }

    private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

    /// <summary>The realm that is neither of these two.</summary>
    public static eRealm Third(eRealm a, eRealm b) => Realms.FirstOrDefault(r => r != a && r != b);

    /// <summary>
    /// The news items a change of keep owner makes, one per realm: "keep_won" for the new owner, "keep_lost" for the old,
    /// "keep_other" for the third (pure, unit-tested). Nothing for a keep that stays with its realm.
    /// </summary>
    public static ChatNewsItem[] KeepChange(string keep, string zone, eRealm previous, eRealm owner, DateTime now, string eventKey = null)
    {
        if (string.IsNullOrWhiteSpace(keep) || owner == previous || owner == eRealm.None)
            return [];
        var items = new List<ChatNewsItem>();
        foreach (eRealm realm in Realms)
        {
            string topic = realm == owner ? "keep_won" : realm == previous ? "keep_lost" : "keep_other";
            items.Add(new ChatNewsItem(topic, realm, now)
            {
                Keep = keep, Zone = zone, Winner = owner, Loser = previous,
                Enemy = realm == owner ? previous : realm == previous ? owner : eRealm.None, EventKey = eventKey,
            });
        }
        return items.ToArray();
    }

    private static void OnKeepTaken(DOLEvent e, object sender, EventArgs args)
    {
        if (args is not KeepEventArgs { Keep: { } keep })
            return;
        try
        {
            eRealm previous;
            lock (Sync)
            {
                previous = KeepOwners.GetValueOrDefault(keep.KeepID, eRealm.None);
                KeepOwners[keep.KeepID] = keep.Realm;
            }
            if (keep.IsPortalKeep || BattlegroundBrackets.IsBattlegroundRegion(keep.Region) || previous == eRealm.None)
                return;
            string zone = keep.CurrentRegion?.GetZone(keep.X, keep.Y)?.Description;
            foreach (ChatNewsItem item in KeepChange(keep.Name, zone, previous, keep.Realm, DateTime.UtcNow, $"rvr-keep-{keep.KeepID}"))
                Post(item with { RegionId = keep.Region, X = keep.X, Y = keep.Y }, voice: true);
        }
        catch (Exception exception)
        {
            Log.Warn("Chat news: keep taken", exception);
        }
    }

    /// <summary>The news items a relic moving makes (pure): stolen from its shrine, or set in a shrine (home again, or captured).</summary>
    public static ChatNewsItem[] RelicChange(string relic, bool stolen, eRealm shrineRealm, eRealm taker, eRealm original, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(relic))
            return [];
        if (stolen)
            return shrineRealm == eRealm.None ? [] : [new ChatNewsItem("relic_lost", shrineRealm, now) { Relic = relic, Enemy = taker }];
        if (shrineRealm == eRealm.None)
            return [];
        if (shrineRealm == original)
            return [new ChatNewsItem("relic_back", shrineRealm, now) { Relic = relic }];
        return [new ChatNewsItem("relic_won", shrineRealm, now) { Relic = relic, Loser = original, Enemy = original }];
    }

    private static void OnRelic(DOLEvent e, object sender, EventArgs args)
    {
        if (args is not RelicPadEventArgs { Relic: { } relic } || sender is not GameRelicPad pad)
            return;
        try
        {
            bool stolen = e == RelicPadEvent.RelicStolen;
            eRealm taker = relic.CurrentCarrier?.Realm ?? eRealm.None;
            foreach (ChatNewsItem item in RelicChange(relic.Name, stolen, pad.Realm, taker, relic.OriginalRealm, DateTime.UtcNow))
                Post(item with { RegionId = pad.CurrentRegionID, X = pad.X, Y = pad.Y }, voice: true);
        }
        catch (Exception exception)
        {
            Log.Warn("Chat news: relic", exception);
        }
    }

    /// <summary>A realm raid's end, from AutonomousRealmRaid ("Boss defeated", "Defeated"...); told by its realm notice.</summary>
    public static void RaidOutcome(eRealm realm, string raid, string boss, bool won)
    {
        if (realm == eRealm.None || string.IsNullOrWhiteSpace(raid))
            return;
        Post(new ChatNewsItem(won ? "raid_won" : "raid_wiped", realm, DateTime.UtcNow) { Raid = raid, Boss = boss }, voice: false);
    }

    /// <summary>An enemy group seen in the frontier (an alarm), for "where are the mids?" and enemy_seen lines.</summary>
    public static void EnemySeen(eRealm realm, eRealm enemy, string zone, int size, ushort region, int x, int y)
    {
        if (realm == eRealm.None || enemy == eRealm.None || string.IsNullOrWhiteSpace(zone))
            return;
        Post(new ChatNewsItem("enemy_seen", realm, DateTime.UtcNow) { Enemy = enemy, Zone = zone, Size = size, RegionId = region, X = x, Y = y },
            voice: false);
    }

    private static void Post(ChatNewsItem item, bool voice)
    {
        item = Board.Post(item);
        // Sightings come often and change no speaker flags: no cache reset, no log line.
        if (item.Topic == "enemy_seen")
            return;
        Invalidate(item.Realm);
        if (Log.IsInfoEnabled)
            Log.Info($"CHAT_NEWS topic={item.Topic} realm={item.Realm} keep=\"{item.Keep}\" relic=\"{item.Relic}\" raid=\"{item.Raid}\" " +
                     $"winner={item.Winner} loser={item.Loser}");
        if (!voice)
            return;
        long now = GameLoop.GameLoopTime;
        lock (Sync)
        {
            if (ToVoice.Count >= MaxQueued)
                ToVoice.RemoveAt(0);
            // A short wait lets a realm notice for the same event (a siege's outcome) go first; then this is dropped.
            ToVoice.Add((item, now + 50_000 + Random.Shared.Next(30_000), now + 5 * 60_000));
        }
    }

    /// <summary>Darkness Falls changing hands, checked each pulse (its owner is set by DFEnterJumpPoint).</summary>
    private static void CheckDarknessFalls()
    {
        eRealm owner = DFEnterJumpPoint.DarknessFallOwner;
        eRealm previous;
        lock (Sync)
        {
            if (owner == _dfOwner)
                return;
            previous = _dfOwner;
            _dfOwner = owner;
        }
        DateTime now = DateTime.UtcNow;
        if (owner != eRealm.None)
            Post(new ChatNewsItem("df_won", owner, now) { Loser = previous }, voice: true);
        if (previous != eRealm.None)
            Post(new ChatNewsItem("df_lost", previous, now) { Winner = owner }, voice: true);
    }

    /// <summary>Called with the realm notices (AutonomousBotGroupCoordinator's tick).</summary>
    public static void Pulse(long now)
    {
        try
        {
            CheckDarknessFalls();
            if (!AutonomousBotVoices.Enabled)
            {
                lock (Sync)
                    ToVoice.Clear();
                return;
            }
            var due = new List<ChatNewsItem>();
            lock (Sync)
            {
                ToVoice.RemoveAll(entry => entry.Expires < now);
                foreach (var entry in ToVoice.ToArray())
                {
                    if (entry.Due > now || LastVoiced.GetValueOrDefault(entry.Item.Realm) + VoiceGapMs > now)
                        continue;
                    ToVoice.Remove(entry);
                    // A realm notice already told this realm about the event (a siege outcome): no second line.
                    if (entry.Item.EventKey != null && RealmEventNotices.VoicedRecently(entry.Item.EventKey, entry.Item.Realm, now))
                        continue;
                    LastVoiced[entry.Item.Realm] = now;
                    due.Add(entry.Item);
                }
            }
            foreach (ChatNewsItem item in due)
                if (AutonomousBotChatCoordinator.TellNews(item))
                    RealmEventNotices.ToldByNews(item.EventKey, item.Realm, now);
        }
        catch (Exception exception)
        {
            Log.Warn("Chat news: pulse", exception);
        }
    }

    #region What a speaker knows

    private sealed record RealmState(long Built, HashSet<string> Flags, Dictionary<string, IReadOnlyDictionary<string, string>> NeedSlots);

    private static readonly Dictionary<eRealm, RealmState> States = new();
    public const int StateRefreshMs = 5_000;

    private static void Invalidate(eRealm realm)
    {
        lock (States)
            States.Remove(realm);
    }

    /// <summary>
    /// The news and siege flags of a realm right now, each with the slots its lines use: "defending" names the keep under
    /// siege and the attacker, "keep_lost_recent" the keep that was lost. Cached for a few seconds.
    /// </summary>
    public static (IReadOnlySet<string> Flags, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Slots) For(eRealm realm)
    {
        long now = GameLoop.GameLoopTime;
        lock (States)
            if (States.TryGetValue(realm, out RealmState cached) && now - cached.Built < StateRefreshMs)
                return (cached.Flags, cached.NeedSlots);
        var slots = Board.FlagSlots(realm, DateTime.UtcNow);
        try
        {
            foreach (var flag in SiegeFlags(realm, AutonomousRvrEventLayer.ActiveSieges(now), (region, x, y) =>
                         WorldMgr.GetRegion(region)?.GetZone(x, y)?.Description))
                slots[flag.Key] = flag.Value;
        }
        catch (Exception exception)
        {
            Log.Warn("Chat news: sieges", exception);
        }
        if (DFEnterJumpPoint.DarknessFallOwner == realm)
            slots["df_ours"] = new Dictionary<string, string>();
        var state = new RealmState(now, new HashSet<string>(slots.Keys, StringComparer.Ordinal), slots);
        lock (States)
            States[realm] = state;
        return (state.Flags, state.NeedSlots);
    }

    /// <summary>
    /// "defending" when one of the realm's keeps is under siege (the walls are being hit, or the enemy army marches on it),
    /// "attacking" when its army is on an enemy keep; the battle that has started wins over one still mustering (pure).
    /// </summary>
    public static Dictionary<string, IReadOnlyDictionary<string, string>> SiegeFlags(eRealm realm,
        IEnumerable<AutonomousRvrEventLayer.SiegeView> sieges, Func<ushort, int, int, string> zoneOf)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        AutonomousRvrEventLayer.SiegeView defend = null, attack = null;
        foreach (var siege in sieges.OrderByDescending(s => s.BattleStarted))
        {
            // The defenders know of a siege once the walls are hit or their defense is called (not while the enemy musters).
            if (siege.Defender == realm && defend == null && (siege.BattleStarted || siege.AttackObserved))
                defend = siege;
            if (siege.Attacker == realm && attack == null)
                attack = siege;
        }
        Dictionary<string, string> Slots(AutonomousRvrEventLayer.SiegeView siege, eRealm enemy)
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keep"] = AutonomousRvrEventLayer.Place(siege.KeepName),
                ["enemy"] = GlobalConstants.RealmToName(enemy),
            };
            string zone = zoneOf?.Invoke(siege.RegionId, siege.X, siege.Y);
            if (!string.IsNullOrWhiteSpace(zone))
                slots["frontier"] = zone;
            return slots;
        }
        if (defend != null)
            result["defending"] = Slots(defend, defend.Attacker);
        if (attack != null)
            result["attacking"] = Slots(attack, attack.Defender);
        return result;
    }

    #endregion
}
