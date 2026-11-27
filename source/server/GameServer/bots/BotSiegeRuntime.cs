using System;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.CraftingEconomy;
using DOL.GS.Keeps;

namespace DOL.GS
{
    public enum BotSiegeKind { Ram, Catapult, Trebuchet, Ballista }

    /// <summary>Bot-only native siege adapter. Real equipment purchases; ammunition-free artillery by design.</summary>
    public static class BotSiegeRuntime
    {
        public const string RepairKit = "offline_siege_field_repair_kit";
        // Inventory packets encode generic stack counts as one unsigned byte.
        public const int RepairStackLimit = byte.MaxValue;
        public const int RepairRestockTarget = 5;
        public static bool IsSupply(string id) => id == RepairKit ||
            id is "deploy_siege_ram" or "deploy_siege_ram2" or "deploy_siege_ram3" or
                "deploy_siege_catapult" or "deploy_siege_catapult2" or "deploy_siege_catapult3" or
                "deploy_siege_trebuchet" or "deploy_siege_trebuchet2" or "deploy_siege_trebuchet3" or
                "deploy_siege_ballista" or "deploy_siege_ballista2" or "deploy_siege_ballista3";
        public static string Kit(eRealm realm, BotSiegeKind kind) => "deploy_siege_" + kind.ToString().ToLowerInvariant() +
            (realm == eRealm.Midgard ? "2" : realm == eRealm.Hibernia ? "3" : "");
        public static BotSiegeKind Kind(GameSiegeWeapon w) => w is GameSiegeRam ? BotSiegeKind.Ram :
            w is GameSiegeTrebuchet ? BotSiegeKind.Trebuchet : w is GameSiegeBallista ? BotSiegeKind.Ballista : BotSiegeKind.Catapult;

        [GameServerStartedEvent]
        public static void SupplyCatalog(DOLEvent e, object sender, EventArgs args)
        {
            // Explicitly requested bot repair alternative; no ammo or crafting-skill grants.
            DbItemTemplate repair = GameServer.Database.FindObjectByKey<DbItemTemplate>(RepairKit);
            if (repair == null)
            {
                repair = new DbItemTemplate { Id_nb=RepairKit,Name="field siege repair kit",Level=1,Model=520,
                    Price=50_000,PackSize=1,MaxCount=RepairStackLimit,Weight=10,IsPickable=true,IsDropable=true,IsTradable=true };
                GameServer.Database.AddObject(repair);
            }
            else if (repair.MaxCount != RepairStackLimit)
            { repair.MaxCount=RepairStackLimit; GameServer.Database.SaveObject(repair); }
            AddSupplyToMerchants(repair);
        }

        private static void AddSupplyToMerchants(DbItemTemplate template)
        {
            foreach (GameMerchant merchant in WorldMgr.GetAllRegions().Where(r => r != null).SelectMany(r => r.Merchants))
                if (merchant.TradeItems != null && new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
                    .Any(realm => AutonomousSiegePolicy.IsSiegeMerchant(realm, merchant.TradeItems.ItemsListID)) &&
                    !merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().Any(t => t.Id_nb == template.Id_nb))
                    merchant.TradeItems.AddTradeItem(0, eMerchantWindowSlot.FirstEmptyInPage, template);
        }

        public static DbInventoryItem Item(GameBot bot, string id) => id == null ? null : bot?.Inventory?.AllItems.FirstOrDefault(i =>
            i.Count > 0 && i.Id_nb == id && i.SlotPosition >= (int)eInventorySlot.FirstBackpack && i.SlotPosition <= (int)eInventorySlot.LastBackpack);

        public static int RepairRestockAmount(int held, long money, long unitPrice) => unitPrice<=0 ? 0 :
            (int)Math.Min(Math.Max(0,RepairRestockTarget-held),Math.Max(0,money)/unitPrice);

        // Opportunistic only: this never assigns a destination or interrupts a fight.
        public static int TryRestockRepairKits(GameBot bot, GameMerchant merchant)
        {
            if (bot?.IsAutonomousWorldBot!=true || bot.Inventory==null || bot.InCombat || bot.IsAttacking ||
                merchant?.TradeItems==null || merchant.CurrentRegion!=bot.CurrentRegion ||
                !merchant.IsWithinRadius(bot,GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE) ||
                merchant.Realm!=eRealm.None && merchant.Realm!=bot.Realm) return 0;
            DbItemTemplate template=merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().FirstOrDefault(t=>t.Id_nb==RepairKit && t.Price>0);
            if (template==null) return 0;
            lock (bot.Inventory)
            {
                var stacks=bot.Inventory.AllItems.Where(i=>i.Id_nb==RepairKit && i.Count>0 &&
                    i.SlotPosition >= (int)eInventorySlot.FirstBackpack && i.SlotPosition <= (int)eInventorySlot.LastBackpack).ToArray();
                int count=RepairRestockAmount(stacks.Sum(i=>i.Count),AutonomousBotEconomy.GetMoney(bot.DatabaseID),template.Price);
                if (count==0) return 0;
                DbInventoryItem stack=stacks.FirstOrDefault(i=>i.MaxCount-i.Count>=count);
                var slot=bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack,eInventorySlot.LastBackpack);
                if (stack==null && slot==eInventorySlot.Invalid) return 0;
                DbInventoryItem item=stack==null ? GameInventoryItem.Create(template) : null;
                if (stack==null && item==null) return 0;
                if (item!=null) item.Count=count;
                long cost=checked(template.Price*count);
                // Crafting 2.0 WP7: a siegecrafter gamebot of the realm may supply the kits below the merchant price.
                if (!TryPaySupply(bot,cost,EconomyFlowKind.RepairKit,out SupplyPayment payment)) return 0;
                bool added=stack!=null ? bot.Inventory.AddCountToStack(stack,count) : bot.Inventory.AddItem(slot,item);
                if (!added) { RefundSupply(bot,payment); return 0; }
                AutonomousBotEconomy.MarkInventoryChanged(bot); bot.MarkAutonomousStateDirty(); AutonomousBotStatusPersistence.Queue(bot,true);
                return count;
            }
        }

        #region Crafting 2.0 WP7: siege supply (bot_siege_supply)

        /// <summary>A siegecrafter gamebot supplies at this percent of the merchant price...</summary>
        public const int SupplyPricePercent = 80;
        /// <summary>...of which this percent of the merchant price is its materials (the realm's sink); the rest is its margin.</summary>
        public const int SupplyMaterialsPercent = 50;
        /// <summary>The siegecraft skill a supplier needs.</summary>
        public const int SupplyMinSkill = 100;

        /// <summary>How a siege purchase was paid: the buyer's coin, and the supplier's margin when a crafter gamebot sold it.</summary>
        public readonly record struct SupplyPayment(long Paid, long SupplierId, long Margin, EconomyFlowKind Kind);

        internal static readonly BotSinks.HourlySums Supplied = new();
        internal static readonly BotSinks.HourlySums Margins = new();
        private static readonly DOL.Logging.Logger SupplyLog = DOL.Logging.LoggerManager.Create(typeof(BotSiegeRuntime));

        /// <summary>The siegecrafter gamebot that supplies a buyer (tests replace it): WP4's roster, a live crafter of the
        /// realm with the skill, never the buyer itself.</summary>
        public static Func<GameBot, CrafterState> FindSupplier = bot =>
        {
            CrafterState crafter = BotCrafters.Roster?.FindServiceCrafter(bot.Realm, eCraftingSkill.SiegeCrafting, SupplyMinSkill, Random.Shared);
            return crafter == null || crafter.BotId == bot.DatabaseID ? null : crafter;
        };

        /// <summary>Pure: a siegecrafter's price for what the merchant sells at <paramref name="merchantCost"/> (at least 1).</summary>
        public static long SupplyPrice(long merchantCost) => merchantCost <= 0 ? 0 : Math.Max(1, merchantCost * SupplyPricePercent / 100);

        /// <summary>Pure: the supplier's margin (its price less its materials).</summary>
        public static long SupplyMargin(long merchantCost) =>
            Math.Max(0, SupplyPrice(merchantCost) - merchantCost * SupplyMaterialsPercent / 100);

        /// <summary>
        /// Pays for siege supplies the merchant sells at <paramref name="merchantCost"/>: with bot_siege_supply on (and
        /// bot_crafters), an autonomous gamebot buys from a siegecrafter gamebot of its realm at 80% of the merchant price
        /// and the supplier keeps the margin over its materials (half the merchant price); otherwise, or with no supplier,
        /// the merchant's price as today. Every move names <paramref name="kind"/> (Siege or RepairKit).
        /// </summary>
        public static bool TryPaySupply(GameBot bot, long merchantCost, EconomyFlowKind kind, out SupplyPayment payment)
        {
            payment = default;
            if (bot == null || merchantCost < 0)
                return false;
            CrafterState supplier = BotSinks.SiegeSupplyOn && BotSinks.IsGamebot(bot) && merchantCost > 0 ? FindSupplier(bot) : null;
            if (supplier == null)
            {
                if (!BotSinks.Spend(bot.DatabaseID, merchantCost, kind))
                    return false;
                payment = new SupplyPayment(merchantCost, 0, 0, kind);
                return true;
            }
            long price = SupplyPrice(merchantCost), margin = SupplyMargin(merchantCost);
            if (!BotSinks.Spend(bot.DatabaseID, price, kind))
                return false;
            if (margin > 0 && !BotSinks.Pay(supplier.BotId, margin, kind))
                margin = 0;
            payment = new SupplyPayment(price, supplier.BotId, margin, kind);
            Supplied.Add(bot.Realm, price);
            if (margin > 0)
                Margins.Add(bot.Realm, margin);
            if (EconomyDiagnostics.TradeLines && SupplyLog.IsInfoEnabled)
                SupplyLog.Info(EconomyLog.Line(EconomyLog.EconSiegeSupply, ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm),
                    ("kind", kind), ("merchantCopper", merchantCost), ("copper", price), ("supplier", supplier.Name),
                    ("supplierId", supplier.BotId), ("margin", margin)));
            return true;
        }

        /// <summary>A purchase that could not be put in the bag: the buyer gets its coin back and the supplier returns its
        /// margin when it still has it (both moves of the purchase's kind, so the ledger stays balanced either way).</summary>
        public static void RefundSupply(GameBot bot, SupplyPayment payment)
        {
            if (bot == null || payment.Paid <= 0)
                return;
            BotSinks.Pay(bot.DatabaseID, payment.Paid, payment.Kind);
            if (payment.SupplierId > 0 && payment.Margin > 0)
                BotSinks.Spend(payment.SupplierId, payment.Margin, payment.Kind);
        }

        #endregion

        public static bool Consume(GameBot bot, string id, int count)
        {
            if (bot?.Inventory == null || count <= 0) return false;
            lock (bot.Inventory)
            {
                DbInventoryItem item = Item(bot, id);
                if (item == null || item.Count < count) return false;
                if (!bot.Inventory.RemoveCountFromStack(item, count)) return false;
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
                return true;
            }
        }

        public static bool LegalEnemy(GameLiving owner, GameLiving target) => owner?.IsAlive == true && target?.IsAlive == true &&
            target.ObjectState == GameObject.eObjectState.Active && owner.CurrentRegion == target.CurrentRegion &&
            (target.Realm != eRealm.None || NeutralBattlegroundKeep(target)) && target.Realm != owner.Realm &&
            GameServer.ServerRules.IsAllowedToAttack(owner, target, true);

        // Battleground central keeps start unclaimed (realm none); their doors and guards are still fair siege targets.
        private static bool NeutralBattlegroundKeep(GameLiving target) =>
            BattlegroundBrackets.IsBattlegroundRegion(target.CurrentRegionID) && target is Keeps.GameKeepDoor or Keeps.GameKeepGuard;

        public static bool CanDamage(GameSiegeWeapon weapon, GameLiving target)
        {
            if (weapon?.Owner is not GameBot bot || !weapon.IsAlive || weapon.ObjectState != GameObject.eObjectState.Active ||
                weapon.IsMoving || !weapon.CanUse() || !LegalEnemy(bot, target) || BotPvpCrowdControl.Protected(bot, target)) return false;
            int range = weapon.GetDistanceTo(target);
            if (weapon is GameSiegeRam)
                return target is GameKeepDoor { State: eDoorState.Closed } && range <= weapon.attackComponent.AttackRange;
            if (range < weapon.MinAttackRange || range > weapon.MaxAttackRange) return false;
            // Catapults and trebuchets lob onto a ground target in an arc, as players used them: no line of sight.
            // A ballista fires directly: it needs a clear shot, or a target standing up on a wall.
            if (weapon is GameSiegeCatapult) return true;
            return target.Z - weapon.Z > 150 || Visible(weapon, target);
        }

        public static bool Visible(GameLiving source, GameLiving target)
        {
            var nav = PathfindingProvider.Instance;
            return source?.CurrentZone != null && target != null && nav.IsAvailable && nav.HasNavmesh(source.CurrentZone) &&
                nav.HasLineOfSight(source.CurrentZone, new(source.X, source.Y, source.Z + 48),
                    new(target.X, target.Y, target.Z + 48), nav.DefaultFilters);
        }

        public static bool CanRepair(GameBot bot, GameSiegeWeapon w) => bot?.IsAlive == true && w?.IsAlive == true &&
            w.ObjectState == GameObject.eObjectState.Active && bot.CurrentRegion == w.CurrentRegion && bot.Realm == w.Realm &&
            w.Owner == bot && !bot.InCombat && !w.InCombat && !w.IsMoving && !bot.IsMoving && !bot.IsCasting && !bot.IsIncapacitated && w.TimesRepaired <= 3 &&
            bot.IsWithinRadius(w, WorldMgr.INTERACT_DISTANCE) && Item(bot, RepairKit) != null;

        /// <summary>The bot holds a siege post: it operates an engine, or (#572 step 2) it stands at its ram crew seat. The
        /// army then defends it first (FrontierThreat), and it is busy for group invites and for rezzing strangers.</summary>
        public static bool HoldingPosition(GameBot bot) => AutonomousSiegeOwnership.All(bot).Any(w =>
            bot.IsWithinRadius(w, w.SIEGE_WEAPON_CONTROLE_DISTANCE)) || AutonomousRamCrews.AtSeat(bot);
        /// <summary>#572 step 2: an enemy at melee reach is striking this bot
        /// (AutonomousWorldBotController.IsSiegeOperatorThreat): the one thing that takes a ram crew member off its seat.
        /// Wall archers and casters above do not.</summary>
        public static bool StruckInMeleeReach(GameBot bot)
        {
            if (bot == null) return false;
            foreach (GameNPC npc in bot.GetNPCsInRadius(350))
                if (npc.IsAlive && npc.TargetObject == bot && npc.IsAttacking && npc.Realm != bot.Realm &&
                    AutonomousWorldBotController.IsSiegeOperatorThreat(System.Numerics.Vector2.Distance(new(bot.X, bot.Y), new(npc.X, npc.Y)), npc.Z - bot.Z))
                    return true;
            foreach (GamePlayer player in bot.GetPlayersInRadius(350))
                if (player.IsAlive && player.TargetObject == bot && player.IsAttacking && player.Realm != bot.Realm &&
                    AutonomousWorldBotController.IsSiegeOperatorThreat(System.Numerics.Vector2.Distance(new(bot.X, bot.Y), new(player.X, player.Y)), player.Z - bot.Z))
                    return true;
            return false;
        }
        public static bool Assigned(GameBot bot) => bot?.TempProperties.GetProperty<long>("SiegeJobUntil",0) > GameLoop.GameLoopTime;
    }
}
