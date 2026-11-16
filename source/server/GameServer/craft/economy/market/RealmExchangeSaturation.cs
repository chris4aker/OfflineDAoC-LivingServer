using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.GS.CraftingEconomy;

namespace DOL.GS;

/// <summary>
/// Stops gamebots flooding the Realm Exchange with one kind of item (Crafting 2.0 WP5). Before a bot posts, it counts the
/// bot listings already in its realm of the same object type and slot within the same five-level band; at the cap it
/// sells the piece to a vendor instead. Player listings are never counted or limited.
/// <para>Ported from rambodolphin13's DividedRealms (RealmExchangeSaturation, 702ef108), GPL-3.0. DR's Discord report of
/// 2026-11-09: 25k listings, nearly all staves; the test world held 13,136 bot listings, 9,056 of them level-2 crafting
/// practice pieces (2,180 woolen regal cloaks) and 1,268 looted focus staves. Changes in the port: it applies through
/// WP0's IBotItemPolicy (BlocksListing, OnListed) and only while bot_crafters is on (off by default, so loot listings are
/// as before); its counts are an immutable snapshot plus lock-free counters for the posts since, because brain turns run
/// in parallel and a seam may not lock; a stale snapshot is rebuilt by one caller while the others read the old one.</para>
/// </summary>
public static class RealmExchangeSaturation
{
    /// <summary>Bot listings of one kind (realm, object type, slot, level band) before bots stop posting more (DR).</summary>
    public const int MaxBotListingsPerKind = 25;
    public const int LevelBand = MarketBand.LevelsPerBand;
    private const long RefreshMilliseconds = 30_000;

    public readonly record struct Kind(ushort Lot, int ObjectType, int ItemType, int Band);

    private sealed class Snapshot
    {
        public readonly IReadOnlyDictionary<Kind, int> Counts;
        public readonly ConcurrentDictionary<Kind, int> Since = new();
        public readonly long BuiltAt;

        public Snapshot(IReadOnlyDictionary<Kind, int> counts, long builtAt)
        {
            Counts = counts;
            BuiltAt = builtAt;
        }

        public int CountOf(Kind kind) => Counts.GetValueOrDefault(kind) + Since.GetValueOrDefault(kind);
    }

    private static volatile Snapshot _snapshot;
    private static int _building;

    /// <summary>The market the counts come from (tests may replace it).</summary>
    public static Func<IEnumerable<DbInventoryItem>> Market = () => MarketCache.SearchItems(new ItemQuery());

    /// <summary>bot_crafters: saturation is part of the crafters' market (off by default).</summary>
    public static bool On => CraftingEconomyProperties.BOT_CRAFTERS;

    public static Kind KindOf(ushort lot, DbInventoryItem item) =>
        new(lot, item.Object_Type, item.Item_Type, Math.Max(0, item.Level) / LevelBand);

    /// <summary>Counts live (unexpired) bot listings by kind (DR). Pure, so tests can feed it rows.</summary>
    public static Dictionary<Kind, int> Count(IEnumerable<DbInventoryItem> listings, DateTime utcNow)
    {
        var counts = new Dictionary<Kind, int>();
        foreach (DbInventoryItem item in listings ?? [])
        {
            if (item == null || item.SellPrice <= 0 || !RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) ||
                !AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _) || RealmExchangeExpiry.IsExpired(item, utcNow))
                continue;
            Kind kind = KindOf(item.OwnerLot, item);
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }
        return counts;
    }

    public static bool IsSaturated(IReadOnlyDictionary<Kind, int> counts, ushort lot, DbInventoryItem item) =>
        item != null && counts.GetValueOrDefault(KindOf(lot, item)) >= MaxBotListingsPerKind;

    /// <summary>Whether the bot's realm already has enough bot listings like this item (DR).</summary>
    public static bool IsSaturated(eRealm realm, DbInventoryItem item)
    {
        if (item == null)
            return false;
        ushort lot = RealmExchangeBroker.GetOwnerLot(realm);
        if (!RealmExchangeBroker.IsExchangeOwnerLot(lot))
            return false;
        Snapshot snapshot = Current();
        return snapshot != null && snapshot.CountOf(KindOf(lot, item)) >= MaxBotListingsPerKind;
    }

    /// <summary>Counts a post at once, so bots between refreshes don't all post into the same gap (DR).</summary>
    public static void NoteListed(DbInventoryItem item)
    {
        if (item == null || !RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot))
            return;
        Snapshot snapshot = Current();
        snapshot?.Since.AddOrUpdate(KindOf(item.OwnerLot, item), 1, (_, count) => count + 1);
    }

    /// <summary>The counts, rebuilt once every 30 s by one caller (DR: one market pass, not one per bot appraisal).</summary>
    private static Snapshot Current()
    {
        Snapshot snapshot = _snapshot;
        long now = Environment.TickCount64;
        if (snapshot != null && now - snapshot.BuiltAt < RefreshMilliseconds)
            return snapshot;
        if (Interlocked.CompareExchange(ref _building, 1, 0) != 0)
            return snapshot; // another turn is counting: use the old counts (none the first time)
        try
        {
            Snapshot built = new(Count(Market().ToList(), DateTime.UtcNow), now);
            _snapshot = built;
            return built;
        }
        catch
        {
            return snapshot;
        }
        finally
        {
            Volatile.Write(ref _building, 0);
        }
    }

    /// <summary>For tests: forget the cached counts.</summary>
    public static void Reset()
    {
        _snapshot = null;
        Volatile.Write(ref _building, 0);
        Market = () => MarketCache.SearchItems(new ItemQuery());
    }
}
