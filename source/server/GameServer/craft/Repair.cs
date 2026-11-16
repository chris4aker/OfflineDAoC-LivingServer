using System;
using System.Reflection;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS
{
	/// <summary>
	/// The class holding all repair functions
	/// </summary>
	public class Repair
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		protected static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		#region Declaration

		/// <summary>
		/// The player currently crafting
		/// </summary>
		protected const string PLAYER_PARTNER = "PLAYER_PARTNER";

		#endregion

		#region First call function and callback

		/// <summary>
		/// Called when player try to use a secondary crafting skill
		/// </summary>
		/// <param name="item"></param>
		/// <param name="player"></param>
		/// <returns></returns>
		public static int BeginWork(GamePlayer player, DbInventoryItem item)
		{
			if (!IsAllowedToBeginWork(player, item, 50))
			{
				return 0;
			}

			GamePlayer tradePartner = null;
			if (player.TradeWindow != null) tradePartner = player.TradeWindow.Partner;
			
			if (player.IsMoving || player.IsStrafing)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.BeginWork.StopRepair1", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				if (tradePartner != null) tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.BeginWork.StopRepair2", player.Name, item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return 0;
			}

			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.BeginWork.BeginRepairing1", item.Name, CalculateSuccessChances(player, item).ToString()), eChatType.CT_System, eChatLoc.CL_SystemWindow);
			if (tradePartner != null) tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.BeginWork.BeginRepairing2", player.Name, item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);

			int workDuration = GetRepairTime(player, item);
			player.Out.SendTimerWindow(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.BeginWork.Repairing", item.Name), workDuration);
			player.CraftTimer = new ECSGameTimer(player);
			player.CraftTimer.Callback = new ECSGameTimer.ECSTimerCallback(Proceed);
			player.CraftTimer.Properties.SetProperty(AbstractCraftingSkill.PLAYER_CRAFTER, player);
			player.CraftTimer.Properties.SetProperty(PLAYER_PARTNER, tradePartner);
			player.CraftTimer.Properties.SetProperty(AbstractCraftingSkill.RECIPE_BEING_CRAFTED, item);
			player.CraftTimer.Start(workDuration * 1000);
			return 1;
		}

		/// <summary>
		/// Called when craft time is finished
		/// </summary>
		/// <param name="timer"></param>
		/// <returns></returns>
		protected static int Proceed(ECSGameTimer timer)
		{
			GamePlayer player = timer.Properties.GetProperty<GamePlayer>(AbstractCraftingSkill.PLAYER_CRAFTER);
			GamePlayer tradePartner = timer.Properties.GetProperty<GamePlayer>(PLAYER_PARTNER);
			DbInventoryItem item = timer.Properties.GetProperty<DbInventoryItem>(AbstractCraftingSkill.RECIPE_BEING_CRAFTED);

			if (player == null || item == null)
			{
				if (log.IsWarnEnabled)
					log.Warn("There was a problem getting back the item to the player in the secondary craft system.");
				return 0;
			}

			player.CraftTimer?.Stop();
			player.Out.SendCloseTimerWindow();

			if (Util.Chance(CalculateSuccessChances(player, item)))
			{
				if (ModifyConditionAndDurability(item))
				{
					player.Out.SendInventorySlotsUpdate([(eInventorySlot) item.SlotPosition]);
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.Proceed.FullyRepaired1", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					tradePartner?.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.Proceed.FullyRepaired2", player.Name, item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				}
			}
			else
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.Proceed.FailImprove1", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				tradePartner?.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.Proceed.FailImprove2", player.Name, item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}

			return 0;
		}

		#endregion

		#region Requirement check

		/// <summary>
		/// Check if the player own can enchant the item
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <param name="percentNeeded">min 50 max 100</param>
		/// <returns></returns>
		public static bool IsAllowedToBeginWork(GamePlayer player, DbInventoryItem item, int percentNeeded)
		{
			if (item.IsNotLosingDur)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.IsAllowedToBeginWork.CantRepair", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}
			
			if (item.SlotPosition < (int)eInventorySlot.FirstBackpack || item.SlotPosition > (int)eInventorySlot.LastBackpack)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.IsAllowedToBeginWork.BackpackItems"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}
			
			eCraftingSkill skill = CraftingMgr.GetSecondaryCraftingSkillToWorkOnItem(item);
			if (skill == eCraftingSkill.NoCrafting)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.IsAllowedToBeginWork.CantRepair", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}

			if (player.IsCrafting || player.IsSalvagingOrRepairing)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.IsAllowedToBeginWork.EndCurrentAction"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}

			if (item.Condition >= item.MaxCondition)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.IsAllowedToBeginWork.FullyRepaired", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}

			if (!HasSkillToRepair(player.GetCraftingSkillValue(skill), CraftingMgr.GetItemCraftLevel(item), percentNeeded))
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.IsAllowedToBeginWork.NotEnoughSkill", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}

			return true;
		}

		#endregion

		/// <summary>
		/// Whether a crafting skill is enough to repair an item: at least <paramref name="percentNeeded"/> percent of the
		/// item's craft level (50 for a repair, 100 for a repair in the trade window). 2026-10-30: this was written as
		/// (percentNeeded / 100) * craftLevel, integer division, so the 50 of BeginWork became 0 and anyone could start a
		/// repair, with the chance from CalculateSuccessChances at 0% below 44% skill.
		/// </summary>
		public static bool HasSkillToRepair(int skill, int craftLevel, int percentNeeded)
		{
			return (long)skill * 100 >= (long)craftLevel * percentNeeded;
		}

		#region Calcul functions

		/// <summary>
		/// Calculate crafting time
		/// </summary>
		protected static int GetRepairTime(GamePlayer player, DbInventoryItem item)
		{
			return Math.Max(1, item.Level / 2); // wrong but don't know the correct formula
		}

		/// <summary>
		/// Calculate the chance of sucess
		/// </summary>
		protected static int CalculateSuccessChances(GamePlayer player, DbInventoryItem item)
		{
			eCraftingSkill skill = CraftingMgr.GetSecondaryCraftingSkillToWorkOnItem(item);
			if (skill == eCraftingSkill.NoCrafting) return 0;

			return RepairChancePercent(player.GetCraftingSkillValue(skill), CraftingMgr.GetItemCraftLevel(item));
		}

		/// <summary>
		/// The chance to repair an item: 10% at half its craft level, 100% at its craft level. 2026-11-15: the formula
		/// divided by craftLevel * 0.5 in doubles, so a craft level of 0 or less (a level-0 weapon is -5) gave infinity or
		/// a negative slope, and the cast to int of infinity or NaN is not a percent. Such an item is trivial: 100%.
		/// </summary>
		public static int RepairChancePercent(int skill, int craftLevel)
		{
			if (craftLevel <= 0)
				return 100;

			if (skill <= 0)
				return 0;

			double chance = 90.0 / (craftLevel * 0.5) * skill - 80; // 50% = 10% chance, 100% = 100% chance
			return (int)Math.Clamp(chance, 0, 100);
		}

		public static bool ModifyConditionAndDurability(DbInventoryItem item)
		{
			int repairAmount = Math.Min(item.MaxCondition - item.Condition, item.Durability);
			item.Condition += repairAmount;

			if (!item.IsNotLosingDur)
				item.Durability -= repairAmount;

			return repairAmount > 0;
		}

		#endregion

		#region SiegeWeapon

		#region First call function and callback

		/// <summary>
		/// Called when player try to use a secondary crafting skill
		/// </summary>
		/// <param name="siegeWeapon"></param>
		/// <param name="player"></param>
		/// <returns></returns>
		public static int BeginWork(GamePlayer player, GameSiegeWeapon siegeWeapon)
		{
			if (!IsAllowedToBeginWork(player, siegeWeapon, 50))
			{
				return 0;
			}
			//chance with Woodworking
			if (player.IsMoving || player.IsStrafing)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.BeginWork.StopRepair1", siegeWeapon.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return 0;
			}

			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.BeginWork.BeginRepair", siegeWeapon.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);

			int workDuration = GetRepairTime(player, siegeWeapon);
			player.Out.SendTimerWindow(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.BeginWork.Repairing", siegeWeapon.Name), workDuration);
			player.CraftTimer = new ECSGameTimer(player);
			player.CraftTimer.Callback = new ECSGameTimer.ECSTimerCallback(ProceedSiegeWeapon);
			player.CraftTimer.Properties.SetProperty(AbstractCraftingSkill.PLAYER_CRAFTER, player);
			player.CraftTimer.Properties.SetProperty(AbstractCraftingSkill.RECIPE_BEING_CRAFTED, siegeWeapon);
			player.CraftTimer.Start(workDuration * 1000);
			return 1;
		}

		/// <summary>
		/// Called when craft time is finished
		/// </summary>
		/// <param name="timer"></param>
		/// <returns></returns>
		protected static int ProceedSiegeWeapon(ECSGameTimer timer)
		{
			GamePlayer player = timer.Properties.GetProperty<GamePlayer>(AbstractCraftingSkill.PLAYER_CRAFTER);
			GameSiegeWeapon siegeWeapon = timer.Properties.GetProperty<GameSiegeWeapon>(AbstractCraftingSkill.RECIPE_BEING_CRAFTED);

			if (player == null || siegeWeapon == null)
			{
				if (log.IsWarnEnabled)
					log.Warn("There was a problem getting back the item to the player in the secondary craft system.");
				return 0;
			}
			if (!Util.Chance(CalculateSuccessChances(player, siegeWeapon)))
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.ProceedSiegeWeapon.FailRepair", siegeWeapon.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return 0;
			}
			siegeWeapon.Health = siegeWeapon.MaxHealth;
			//player.CraftTimer.Stop();
			player.craftComponent.StopCraft();
			player.Out.SendCloseTimerWindow();
			ClientService.UpdateNpcForPlayer(player, siegeWeapon);
			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.Proceed.FullyRepaired1", siegeWeapon.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
			return 0;
		}
		#endregion

		#region Requirement check

		/// <summary>
		/// Check if the player own can enchant the item
		/// </summary>
		/// <param name="player"></param>
		/// <param name="siegeWeapon"></param>
		/// <param name="percentNeeded">min 50 max 100</param>
		/// <returns></returns>
		public static bool IsAllowedToBeginWork(GamePlayer player, GameSiegeWeapon siegeWeapon, int percentNeeded)
		{
			if (player.GetCraftingSkillValue(eCraftingSkill.WeaponCrafting) < 301)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.IsAllowedToBeginWork.WeaponCrafter"), eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return false;
			}

			if (player.IsCrafting || player.IsSalvagingOrRepairing)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.IsAllowedToBeginWork.EndCurrentAction"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}

			if (siegeWeapon.Health >= siegeWeapon.MaxHealth)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Repair.IsAllowedToBeginWork.FullyRepaired", siegeWeapon.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}
			return true;
		}

		#endregion

		#region Calcul functions

		/// <summary>
		/// Calculate crafting time
		/// </summary>
		protected static int GetRepairTime(GamePlayer player, GameSiegeWeapon siegeWeapon)
		{
			return 15; // wrong but don't know the correct formula
		}

		/// <summary>
		/// Calculate the chance of sucess
		/// </summary>
		protected static int CalculateSuccessChances(GamePlayer player, GameSiegeWeapon siegeWeapon)
		{
			return SiegeRepairChancePercent(player.GetCraftingSkillValue(eCraftingSkill.WoodWorking));
		}

		/// <summary>
		/// The chance to repair a siege weapon: 90 - 50 / woodworking, integer division as written (40% at 1, 65% at 2,
		/// 90% from 51). 2026-11-15: a woodworking of 0 threw DivideByZeroException in the repair timer; an untrained skill
		/// (-1) gave 140, clamped to 100. Both now count as 1, the lowest trained value.
		/// </summary>
		public static int SiegeRepairChancePercent(int woodworking)
		{
			int skill = Math.Max(1, woodworking);
			return Math.Clamp(90 - 50 / skill, 0, 100);
		}

		#endregion

		#endregion
	}
}
