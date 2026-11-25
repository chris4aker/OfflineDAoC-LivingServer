using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Source guards for upstream ports whose behaviour needs live clients or a running world: the trade lock order
    /// (OpenDAoC-Core 662f315fd), the realm-ability static's caster handler (84f1a46a6), the static item's delete cleanup
    /// (e04fb3af8), the save lock (8e3cfb833), the quit timer during a region change (4783841c1), the creation request's
    /// stealth check (446a99f1f), the attack component's references (c707d057f) and the drop lock (b28922b5e).
    /// </summary>
    [TestFixture]
    public class UT_UpstreamCorePorts
    {
        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        private static string Method(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
            int open = source.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
            }
            throw new AssertionException("unbalanced " + signature);
        }

        [Test]
        public void AcceptTrade_TakesBothInventoriesBeforeTheWindowAndClosesAfterIt()
        {
            string source = Source("gameutils", "PlayerTradeWindow.cs");
            string accept = Method(source, "public bool AcceptTrade()");
            Assert.That(accept, Does.Contain("lock (first)"));
            Assert.That(accept, Does.Contain("lock (second)"));
            Assert.That(accept, Does.Contain("string.CompareOrdinal(m_owner.InternalID, partner.InternalID)"), "a stable order for both players");
            Assert.That(accept.IndexOf("AcceptTradeLocked()", StringComparison.Ordinal),
                Is.LessThan(accept.IndexOf("CloseTrade()", StringComparison.Ordinal)), "the trade closes after the window lock is released");
            Assert.That(Method(source, "private bool AcceptTradeLocked()"), Does.Not.Contain("CloseTrade()"),
                "CloseTrade locks the players, which ReceiveTradeItem holds before the window lock");
        }

        [Test]
        public void RealmAbilityStatic_RemovesItsCasterHandlerWhenItLeaves()
        {
            string source = Source("realmabilities", "Statics", "GenericBase.cs");
            string add = Method(source, "public override bool AddToWorld()");
            Assert.That(add.IndexOf("base.AddToWorld()", StringComparison.Ordinal),
                Is.LessThan(add.IndexOf("GameEventMgr.AddHandler", StringComparison.Ordinal)), "nothing is registered if adding failed");
            string remove = Method(source, "public override bool RemoveFromWorld(int respawnSeconds)");
            Assert.That(remove, Does.Contain("GameEventMgr.RemoveHandler(m_caster, GamePlayerEvent.RemoveFromWorld"));
            Assert.That(remove, Does.Contain("m_pulseTimer?.Stop()"));
        }

        [Test]
        public void StaticItemDelete_RemovesItsHandlers()
        {
            string delete = Method(Source("gameobjects", "GameStaticItem.cs"), "public override void Delete()");
            Assert.That(delete, Does.Contain("GameEventMgr.RemoveAllHandlersForObject(this)"));
            Assert.That(delete, Does.Contain("ClearObjectsInRadiusCache()"));
        }

        [Test]
        public void PeriodicSave_IsSerialised()
        {
            string save = Method(Source("GameServer.cs"), "protected void SaveTimerProc(object sender)");
            Assert.That(save, Does.Contain("lock (_saveTimerLock)"));
        }

        [Test]
        public void QuitTimer_StopsWhileTheClientIsNotPlaying()
        {
            string source = Source("gameobjects", "GamePlayer.cs");
            int timer = source.IndexOf("public class QuitTimer", StringComparison.Ordinal);
            string tick = Method(source.Substring(timer), "protected override int OnTick(ECSGameTimer timer)");
            Assert.That(tick, Does.Contain("ClientState is not GameClient.eClientState.Playing"));
        }

        [Test]
        public void CreatePlayerRequest_RespectsStealthAndRange()
        {
            string source = Source("packets", "Client", "168", "PlayerCreationRequestHandler.cs");
            Assert.That(source, Does.Contain("!player.CanDetect(playerTarget)"));
            Assert.That(source, Does.Contain("WorldMgr.VISIBILITY_DISTANCE"));
        }

        [Test]
        public void AttackComponent_DropsItsTargetsWhenItStops()
        {
            string source = Source("ECS-Components", "AttackComponent.cs");
            string stop = Method(source, "private void Stop()");
            Assert.That(stop, Does.Contain("weaponAction = null"));
            Assert.That(stop, Does.Contain("_startAttackTarget = null"));
            Assert.That(Method(source, "private void BroadcastObserverMessage(AttackData ad)"), Does.Contain("Array.Clear(_broadcastExcludes)"));
        }

        [Test]
        public void DropItem_CreatesTheGroundItemOutsideTheInventoryLock()
        {
            string drop = Method(Source("gameobjects", "GamePlayer.cs"), "public virtual bool DropItem(eInventorySlot slot_pos, out WorldInventoryItem droppedItem)");
            string locked = Method(drop, "lock (Inventory.Lock)");
            Assert.That(locked, Does.Not.Contain("CreateItemOnTheGround"));
            Assert.That(locked, Does.Not.Contain("Notify("));
            Assert.That(drop, Does.Contain("CreateItemOnTheGround(item)"));
        }

        [Test]
        public void WeaponSwap_KeepsTheActiveSlotWhenItIsStillOccupied()
        {
            string source = Source("gameutils", "GamePlayerInventory.cs");
            Assert.That(Regex.Matches(source, @"weaponSlotStillOccupied \?").Count, Is.EqualTo(3));
        }
    }
}
