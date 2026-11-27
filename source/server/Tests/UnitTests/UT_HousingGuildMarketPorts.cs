using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.GS.Commands;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Issue #562, OpenDAoC port X1 (housing, guild and market), step 1. Only real players use houses, vaults,
/// consignment merchants and the social window; gamebots and companions never reach this code. Tests only.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_HousingGuildMarketPorts
{
    private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();
    private GameServer _previousServer;

    private sealed class InertServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    /// <summary>Finds nothing: every select is empty, so a consignment merchant's money row is missing.</summary>
    public class EmptyReadDatabase : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.ReturnType == typeof(bool))
                return true;
            if (method.ReturnType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(method.ReturnType))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(method.ReturnType.GetGenericArguments()[0]));
            return null;
        }
    }

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((InertServer)RuntimeHelpers.GetUninitializedObject(typeof(InertServer)));
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    // 7098d8ebe: MoveItem never returns null.

    [Test]
    public void EmptyFromSlotResult_ListsBothSlotsAsEmpty()
    {
        Dictionary<int, DbInventoryItem> result = GameInventoryObjectExtensions.EmptyFromSlotResult(
            eInventorySlot.HousingInventory_First, eInventorySlot.FirstBackpack);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Keys, Is.EquivalentTo(new[] { (int)eInventorySlot.HousingInventory_First, (int)eInventorySlot.FirstBackpack }));
        Assert.That(result.Values, Is.All.Null);
        // The consignment merchant's callers read Count without a null check; this must not throw.
        Assert.That(result.Count, Is.GreaterThan(0));
        Assert.That(GameInventoryObjectExtensions.IsEmptyFromSlotResult(result), Is.True);
    }

    [Test]
    public void IsEmptyFromSlotResult_IsFalseForEveryRealMoveShape()
    {
        DbInventoryItem moved = new();
        DbInventoryItem other = new();

        // Move to an empty slot, swap, stack, and a refused stack (empty, nothing moved but not an empty from-slot).
        Assert.That(GameInventoryObjectExtensions.IsEmptyFromSlotResult(new Dictionary<int, DbInventoryItem> { { 1, null }, { 2, moved } }), Is.False);
        Assert.That(GameInventoryObjectExtensions.IsEmptyFromSlotResult(new Dictionary<int, DbInventoryItem> { { 1, other }, { 2, moved } }), Is.False);
        Assert.That(GameInventoryObjectExtensions.IsEmptyFromSlotResult(new Dictionary<int, DbInventoryItem>()), Is.False);
        // The old null result still counts as an empty from-slot (the Realm Exchange refuses the move, as before).
        Assert.That(GameInventoryObjectExtensions.IsEmptyFromSlotResult(null), Is.True);
    }

    [Test]
    public void EmptyFromSlotResult_SameSlotTwice_IsStillAnEmptyFromSlot()
    {
        Dictionary<int, DbInventoryItem> result = GameInventoryObjectExtensions.EmptyFromSlotResult(
            eInventorySlot.HousingInventory_First, eInventorySlot.HousingInventory_First);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(GameInventoryObjectExtensions.IsEmptyFromSlotResult(result), Is.True);
    }

    // 42fd3c4f8: vault cache follows the owner.

    [TestCase("house-owner", "house-owner", true, true)]
    [TestCase("house-owner", "guild-id", true, false)] // house became a guild house
    [TestCase("alt-one", "alt-two", true, false)] // another character at the same personal vault
    [TestCase(null, "house-owner", true, false)]
    [TestCase("house-owner", "house-owner", false, false)] // expired or discarded after an error
    public void VaultItemCache_IsReusedOnlyForTheOwnerItWasReadFor(string cachedOwner, string currentOwner, bool loaded, bool expected)
    {
        Assert.That(GameVault.IsItemCacheValid(cachedOwner, currentOwner, loaded), Is.EqualTo(expected));
    }

    // e540603f9: a missing money row never throws or moves money.

    [Test]
    public void ConsignmentMerchant_WithoutMoneyRow_KeepsItsMoneyAndSaysSo()
    {
        GameConsignmentMerchant merchant = UninitializedMerchant(money: 5_000);

        Assert.That(merchant.HasMoneyRecord(), Is.False);
        Assert.DoesNotThrow(() => merchant.TotalMoney += 1_000);
        Assert.DoesNotThrow(() => merchant.TotalMoney = 0);
        Assert.That(merchant.TotalMoney, Is.EqualTo(5_000), "money that can't be saved isn't changed in memory either");
        Assert.That(GameConsignmentMerchant.MONEY_RECORD_MISSING_MESSAGE, Does.Contain("could not be loaded"));
    }

    [Test]
    public void ConsignmentMerchant_WithMoneyRow_SavesTheNewAmount()
    {
        DbHouseConsignmentMerchant row = new() { HouseNumber = 7, Money = 5_000 };
        RowMerchant merchant = UninitializedMerchant<RowMerchant>(money: 5_000);
        merchant.Row = row;

        Assert.That(merchant.HasMoneyRecord(), Is.True);
        merchant.TotalMoney += 1_000;

        Assert.That(merchant.TotalMoney, Is.EqualTo(6_000));
        Assert.That(row.Money, Is.EqualTo(6_000));
    }

    public sealed class RowMerchant : GameConsignmentMerchant
    {
        public DbHouseConsignmentMerchant Row;
        protected override DbHouseConsignmentMerchant LoadMoneyRecord() => Row;
    }

    // fed1f28ec / 409c19b3d: start-up guild-house emblem repair.

    [TestCase(true, "Wolves", "Wolves", 10, 42, true)]
    [TestCase(true, "Wolves", "Wolves", 42, 42, false)] // already right
    [TestCase(false, null, "Wolves", 0, 42, false)] // stale GuildHouseNumber pointing at a personal house
    [TestCase(true, "Bears", "Wolves", 10, 42, false)] // another guild's house
    public void GuildHouseEmblemRepair_TouchesOnlyThisGuildsHouse(bool isGuildHouse, string houseGuild, string guild, int houseEmblem, int guildEmblem, bool expected)
    {
        Assert.That(GuildMgr.ShouldRepairGuildHouseEmblem(isGuildHouse, houseGuild, guild, houseEmblem, guildEmblem), Is.EqualTo(expected));
    }

    // 582ff4db3: social window refresh.

    [TestCase(new[] { "&gc", "info", "1" }, true)]
    [TestCase(new[] { "&gc", "INFO", "1" }, true)]
    [TestCase(new[] { "&gc", "info" }, false)]
    [TestCase(new[] { "&gc", "who" }, false)]
    [TestCase(new[] { "&gc" }, false)]
    public void SocialWindowInfoRequest_SkipsTheSpamCheckOnlyForGcInfoWithArguments(string[] args, bool expected)
    {
        Assert.That(GuildCommandHandler.IsSocialWindowInfoRequest(args), Is.EqualTo(expected));
    }

    // 5d3ce2325 / 34e3357ca: market search gives the same items as a plain filter.

    [Test]
    public void MarketSearch_ReturnsExactlyTheMatchingItems_ForOneAndManyIndexSets()
    {
        using MarketSearchEngine engine = new();
        List<DbInventoryItem> all = new();
        ushort[] lots = { 100, 2000, 3000 }; // Albion, Midgard, Hibernia lots

        for (int i = 0; i < 60; i++)
        {
            DbInventoryItem item = Item($"owner-{i % 3}", lots[i % lots.Length], crafted: i % 2 == 0, i);
            all.Add(item);
            Assert.That(engine.AddItem(item), Is.True);
        }

        AssertSame(engine, all, new ItemQuery { Owner = "owner-1" }, item => item.OwnerID == "owner-1");
        AssertSame(engine, all, new ItemQuery { Owner = "owner-1", IsCrafted = true },
            item => item.OwnerID == "owner-1" && item.IsCrafted);
        AssertSame(engine, all, new ItemQuery { Realm = eRealm.Hibernia, Owner = "owner-2", IsCrafted = false },
            item => MarketSearchEngine.GetRealmOfLot(item.OwnerLot) == eRealm.Hibernia && item.OwnerID == "owner-2" && !item.IsCrafted);
        AssertSame(engine, all, new ItemQuery { Owner = "nobody" }, _ => false);
        Assert.That(engine.Search(new ItemQuery()).Count(), Is.EqualTo(all.Count));
    }

    [Test]
    public void MarketSearch_SingleSetResult_IsACopyOfTheIndex()
    {
        using MarketSearchEngine engine = new();
        DbInventoryItem first = Item("seller", 100, crafted: false, 1);
        DbInventoryItem second = Item("seller", 100, crafted: false, 2);
        engine.AddItem(first);

        IEnumerable<DbInventoryItem> result = engine.Search(new ItemQuery { Owner = "seller" });
        engine.AddItem(second);
        engine.RemoveItem(first);

        Assert.That(result, Is.EquivalentTo(new[] { first }), "a later listing or sale must not change a search already returned");
    }

    private static void AssertSame(MarketSearchEngine engine, List<DbInventoryItem> all, ItemQuery query, Func<DbInventoryItem, bool> match)
    {
        List<DbInventoryItem> found = engine.Search(query).ToList();
        Assert.That(found, Is.EquivalentTo(all.Where(match)));
        Assert.That(found.Distinct().Count(), Is.EqualTo(found.Count), "no duplicates");
    }

    private static DbInventoryItem Item(string owner, ushort lot, bool crafted, int n)
    {
        DbItemTemplate template = new()
        {
            Id_nb = $"x1-market-{n}-{Guid.NewGuid():N}",
            Name = $"listing {n}",
            Item_Type = (int)eInventorySlot.TorsoArmor,
            Object_Type = (int)eObjectType.Cloth,
            PackSize = 1,
            MaxCount = 1,
            IsTradable = true,
        };

        DbInventoryItem item = GameInventoryItem.Create(template);
        item.OwnerID = owner;
        item.OwnerLot = lot;
        item.IsCrafted = crafted;
        item.SlotPosition = (int)eInventorySlot.Consignment_First + n;
        return item;
    }

    private static GameConsignmentMerchant UninitializedMerchant(long money) => UninitializedMerchant<GameConsignmentMerchant>(money);

    private static T UninitializedMerchant<T>(long money) where T : GameConsignmentMerchant
    {
        T merchant = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(GameConsignmentMerchant).GetField("_moneyLock", instance)!.SetValue(merchant, new Lock());
        typeof(GameConsignmentMerchant).GetField("_money", instance)!.SetValue(merchant, money);
        return merchant;
    }
}
