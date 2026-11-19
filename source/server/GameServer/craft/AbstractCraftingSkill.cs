using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Language;

namespace DOL.GS
{
    /// <summary>
    /// AbstractCraftingSkill is the base class for all crafting skill.
    /// Crafting 2.0 WP1 (2026-11-15): every number (con, chance to make, chance to gain, time, quality, the secondary
    /// skills, the pace) comes from <see cref="CraftRules"/>, so a gamebot crafter rolls exactly as a player does. This
    /// class keeps the player's side: messages, inventory, the trade window. DividedRealms' skill-value members
    /// (ChanceToMakeAt, BaseChanceToGainAt, QualityAt, CraftingTimeAt; rambodolphin13, 93c25b69) stay as one-line
    /// delegations, so DR's bot crafting ports unchanged.
    /// </summary>
    public abstract class AbstractCraftingSkill
	{
		protected static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		private bool finishedCraft = false; 
		#region Declaration
		/// <summary>
		/// the maximum possible range within a player has to be to a forge , lathe ect to craft an item
		/// </summary>
		public const int CRAFT_DISTANCE = 512;

		/// <summary>
		/// The icone number used by this craft.
		/// </summary>
		private byte m_icon;

		/// <summary>
		/// The name show for this craft.
		/// </summary>
		private string m_name;

		/// <summary>
		/// The crafting skill id of this craft
		/// </summary>
		private eCraftingSkill m_eskill;

		/// <summary>
		/// The player currently crafting
		/// </summary>
		public const string PLAYER_CRAFTER = "PLAYER_CRAFTER";

		/// <summary>
		/// The recipe being crafted
		/// </summary>
		public const string RECIPE_BEING_CRAFTED = "RECIPE_BEING_CRAFTED";

		/// <summary>
		/// The list of raw materials for the recipe beign crafted
		/// </summary>
		public const string RECIPE_RAW_MATERIAL_LIST = "RECIPE_RAW_MATERIAL_LIST";

		public const int subSkillCap = 1300;

		public virtual string CRAFTER_TITLE_PREFIX
		{
			get
			{
				return string.Empty;
			}
		}
		public eCraftingSkill eSkill
		{
			get
			{
				return m_eskill;
			}
			set
			{
				m_eskill = value;
			}
		}
		public byte Icon
		{
			get
			{
				return m_icon;
			}
			set
			{
				m_icon = value;
			}
		}
		public string Name
		{
			get
			{
				return m_name;
			}
			set
			{
				m_name = value;
			}
		}
		#endregion

		#region First call function and callback
		public virtual void CraftItem(GamePlayer player, Recipe recipe)
		{
			if (!CanPlayerStartToCraftItem(player, recipe))
			{
				return;
			}

			if (player.IsCrafting)
			{
				StopCraftingCurrentItem(player, recipe.Product);
				return;
			}

			int craftingTime = GetCraftingTime(player, recipe);

			var chanceToMakeItem = CalculateChanceToMakeItem(player, recipe.Level);
			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.CraftItem.BeginWork", recipe.Product.Name, chanceToMakeItem.ToString()), eChatType.CT_Missed, eChatLoc.CL_SystemWindow);
			player.Out.SendTimerWindow(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.CraftItem.CurrentlyMaking", recipe.Product.Name), craftingTime);

			player.Stealth(false);

			//StartCraftingTimerAndSetCallBackMethod(player, recipe, craftingTime);
			player.craftComponent.StartCraft(recipe, this, craftingTime);
		}

		protected virtual void StartCraftingTimerAndSetCallBackMethod(GamePlayer player, Recipe recipe, int craftingTime)
        {
			//player.CraftTimer = new ECSGameTimer(player);
			//player.CraftTimer.Callback = new ECSGameTimer.ECSTimerCallback(MakeItem);
			//player.CraftTimer.Properties.setProperty(PLAYER_CRAFTER, player);
			//player.CraftTimer.Properties.setProperty(RECIPE_BEING_CRAFTED, recipe);
			//player.CraftTimer.Start(craftingTime * 1000);
		}

		protected virtual void StopCraftingCurrentItem(GamePlayer player, DbItemTemplate itemToCraft)
		{
			//player.CraftTimer.Stop();
			player.craftComponent.StopCraft();
			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.CraftItem.StopWork", itemToCraft.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}

		/// <summary>What a player is told when the server rules refuse a recipe's product (another realm's recipe).
		/// From rambodolphin13's DividedRealms (abe7b327), GPL-3.0.</summary>
		public static string CraftRealmRefusal(DbItemTemplate product)
		{
			string realm = (eRealm)product.Realm switch
			{
				eRealm.Albion => "Albion",
				eRealm.Midgard => "Midgard",
				eRealm.Hibernia => "Hibernia",
				_ => "no realm",
			};
			return $"You can't make the {product.Name}: that recipe is for {realm}. "
				+ "Use /craft alb, /craft mid or /craft hib to show your own realm's recipes.";
		}

		/// <summary>
		/// A translated message, or <paramref name="fallback"/> (English, formatted with the same arguments) when the
		/// language files have no such key. WP1's new messages use it until WP3, which owns Crafting.txt, adds the keys.
		/// </summary>
		internal static string Text(GamePlayer player, string key, string fallback, params object[] args)
		{
			args ??= Array.Empty<object>();
			string language = player?.Client?.Account?.Language;
			try
			{
				if (language != null && LanguageMgr.TryGetTranslation(out string translated, language, key, new ReadOnlySpan<object>(args)))
					return translated;
			}
			catch (Exception)
			{
				// no language manager (a test) or a bad format: the English text below
			}
			return args.Length == 0 ? fallback : string.Format(fallback, args);
		}

		protected virtual bool CanPlayerStartToCraftItem(GamePlayer player, Recipe recipe)
		{
			
			player.TempProperties.SetProperty(CraftAction.RECIPE_TO_CRAFT_PROPERTY, recipe);
			
			if (!GameServer.ServerRules.IsAllowedToCraft(player, recipe.Product))
			{
				// This refusal used to say nothing, so the craft button looked dead (DividedRealms abe7b327, player report 2026-11-08).
				player.Out.SendMessage(CraftRealmRefusal(recipe.Product), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}

			if (!CheckForTools(player, recipe))
			{
				return false;
			}

			if (!CheckSecondCraftingSkillRequirement(player, recipe))
			{
				return false;
			}

			if (!CheckRawMaterials(player, recipe))
			{
				return false;
			}

			if (player.IsMoving || player.IsStrafing)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.CraftItem.MoveAndInterrupt"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}

			if (player.InCombat)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.CraftItem.CantCraftInCombat"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}
			
			if (player.IsCasting)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.CraftItem.CantCraftCasting"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}
			

			return true;
		}

		#endregion

		#region Requirement check

		protected virtual bool CheckForTools(GamePlayer player, Recipe recipe)
		{
			return true;
		}

		/// <summary>The secondary skill a material of this model is worked with, or NoCrafting (CraftRules; DR's name).</summary>
		public static eCraftingSkill SecondarySkillForMaterial(int model) => CraftRules.SecondarySkillForMaterial(model);

		public virtual bool CheckSecondCraftingSkillRequirement(GamePlayer player, Recipe recipe)
        {
			int minimumLevel = GetSecondaryCraftingSkillMinimumLevel(recipe);

			if (minimumLevel <= 0)
				return true; // no requirement needed

			foreach (var ingredient in recipe.Ingredients)
			{
				DbItemTemplate material = ingredient.Material;
				eCraftingSkill secondary = CraftRules.SecondarySkillForMaterial(material.Model);

				if (secondary == eCraftingSkill.NoCrafting || player.GetCraftingSkillValue(secondary) >= minimumLevel)
					continue;

				string key = secondary switch
				{
					eCraftingSkill.ClothWorking => "AbstractCraftingSkill.CheckSecondCraftingSkillRequirement.NoClothworkingSkill",
					eCraftingSkill.LeatherCrafting => "AbstractCraftingSkill.CheckSecondCraftingSkillRequirement.NoLeathercraftingSkill",
					eCraftingSkill.MetalWorking => "AbstractCraftingSkill.CheckSecondCraftingSkillRequirement.NoMetalworkingSkill",
					_ => "AbstractCraftingSkill.CheckSecondCraftingSkillRequirement.NoWoodworkingSkill",
				};
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, key, minimumLevel, material.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}
			return true;
		}

		public virtual bool CheckRawMaterials(GamePlayer player, Recipe recipe)
        {
			ArrayList missingMaterials = null;

			long totalPrice = 0;
			lock (player.Inventory.Lock)
			{
				foreach (var ingredient in recipe.Ingredients)
				{
					DbItemTemplate material = ingredient.Material;

					totalPrice += material.Price * ingredient.Count;

					bool result = false;
					int count = ingredient.Count;

					foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
					{
						if (item != null && item.Name == material.Name)
						{
							if (item.Count >= count)
							{
								result = true;
								break;
							}
							else
							{
								count -= item.Count;
							}
						}
					}

					if (result == false)
					{
						if (missingMaterials == null)
						{
							missingMaterials = new ArrayList(5);
						}

						missingMaterials.Add("(" + count + ") " + material.Name);
					}
				}

				if (missingMaterials != null)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.CheckRawMaterial.NoIngredients", recipe.Product.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.CheckRawMaterial.YouAreMissing", recipe.Product.Name), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
					foreach (string materialName in missingMaterials)
					{
						player.Out.SendMessage(materialName, eChatType.CT_Important, eChatLoc.CL_ChatWindow);
					}

					if (player.Client.Account.PrivLevel == (uint)ePrivLevel.Player) return false;
				}

				return true;
			}
		}

		#endregion

		#region Gain points
		/// <summary>
		/// One won skill gain of a craft: the player pace (crafting_skill_rate) in points, 1 = the original single point,
		/// never past <paramref name="cap"/> (subSkillCap for the secondary skills). Callers keep their own gain
		/// conditions (the chance, the grey rule, the cap checks). From shadowofze's True165 (00f9fbf), GPL-3.0.
		/// A gain of several points no longer steps over a crafting title (True165 did this in GamePlayer.GainCraftingSkill;
		/// here, because WP3 owns that region): GamePlayer announces the title the new value lands on, and the titles the
		/// gain stepped over are announced here, lowest first. At the default pace (1 point) nothing is added.
		/// </summary>
		protected static void GainRatedPoints(GamePlayer player, eCraftingSkill skill, int cap = CraftingRate165.NoCap)
		{
			int before = player.GetCraftingSkillValue(skill);
			int points = CraftRules.PointsPerGain(before, CraftingRate165.PlayerRate, cap);
			if (points <= 0)
				return;

			player.GainCraftingSkill(skill, points);

			if (points > 1)
				foreach (int title in CraftingRate165.TitlesSteppedOver(before, player.GetCraftingSkillValue(skill)))
					GameEventMgr.Notify(GamePlayerEvent.NextCraftingTierReached, player, new NextCraftingTierReachedEventArgs(skill, title));
		}

		public virtual void GainCraftingSkillPoints(GamePlayer player, Recipe recipe)
		{
			foreach (var ingredient in recipe.Ingredients)
			{
				eCraftingSkill secondary = CraftRules.SecondarySkillForMaterial(ingredient.Material.Model);
				if (secondary != eCraftingSkill.NoCrafting && player.GetCraftingSkillValue(secondary) < subSkillCap)
					GainRatedPoints(player, secondary, subSkillCap);
			}

			player.Out.SendUpdateCraftingSkills();
		}
		#endregion

		#region Use materials and created crafted item
		public virtual bool RemoveUsedMaterials(GamePlayer player, Recipe recipe)
		{
			Dictionary<int, int?> dataSlots = new Dictionary<int, int?>(10);

			lock (player.Inventory.Lock)
			{
				foreach (var ingredient in recipe.Ingredients)
				{
					DbItemTemplate template = ingredient.Material;

					bool result = false;
					int count = ingredient.Count;

					foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
					{
						if (item != null && item.Name == template.Name)
						{
							if (item.Count >= count)
							{
								if (item.Count == count)
								{
									dataSlots.Add(item.SlotPosition, null);
								}
								else
								{
									dataSlots.Add(item.SlotPosition, count);
								}
								result = true;
								break;
							}
							else
							{
								dataSlots.Add(item.SlotPosition, null);
								count -= item.Count;
							}
						}
					}
					if (result == false)
					{
						return false;
					}
				}
			}

			player.Inventory.BeginChanges();
			Dictionary<int, int?>.Enumerator enumerator = dataSlots.GetEnumerator();
			while (enumerator.MoveNext())
			{
				KeyValuePair<int, int?> de = enumerator.Current;
				DbInventoryItem item = player.Inventory.GetItem((eInventorySlot)de.Key);
				if (item != null)
				{
					if (!de.Value.HasValue)
					{
						player.Inventory.RemoveItem(item);
					}
					else
					{
						player.Inventory.RemoveCountFromStack(item, de.Value.Value);
					}
					InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, item.Template, de.Value.HasValue ? de.Value.Value : item.Count);
				}
			}
			player.Inventory.CommitChanges();

			return true;//all raw material removed and item created
		}

		public virtual void BuildCraftedItem(GamePlayer player, Recipe recipe)
		{
			var product = recipe.Product;

			Dictionary<int, int> changedSlots = new Dictionary<int, int>(5); // key : > 0 inventory ; < 0 ground || value: < 0 = new item count; > 0 = add to old

			lock (player.Inventory.Lock)
			{
				int count = product.PackSize < 1 ? 1 : product.PackSize;
				foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
				{
					if (item == null)
						continue;

					if (item.Id_nb.Equals(product.Id_nb) == false)
						continue;

					if (item.Count >= product.MaxCount)
						continue;

					int countFree = item.MaxCount - item.Count;
					if (count > countFree)
					{
						changedSlots.Add(item.SlotPosition, countFree); // existing item should be changed
						count -= countFree;
					}
					else
					{
						changedSlots.Add(item.SlotPosition, count); // existing item should be changed
						count = 0;
						break;
					}
				}

				if (count > 0) // Add new object
				{
					eInventorySlot firstEmptySlot = player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
					if (firstEmptySlot == eInventorySlot.Invalid)
					{
						changedSlots.Add(-1, -count); // Create the item on the ground
					}
					else
					{
						changedSlots.Add((int)firstEmptySlot, -count); // Create the item in the free slot
					}
					count = 0;
				}
			}
			
			DbInventoryItem newItem = null;

			player.Inventory.BeginChanges();

			Dictionary<int, int>.Enumerator enumerator = changedSlots.GetEnumerator();
			while (enumerator.MoveNext())
			{
				KeyValuePair<int, int> slot = enumerator.Current;
				int countToAdd = slot.Value;
				if (countToAdd > 0)	// Add to exiting item
				{
					newItem = player.Inventory.GetItem((eInventorySlot)slot.Key);
					if (newItem != null && player.Inventory.AddCountToStack(newItem, countToAdd))
					{
						InventoryLogging.LogInventoryAction("(craft)", player, eInventoryActionType.Other, newItem.Template, countToAdd);
						// count incremented, continue with next change
						continue;
					}
				}

				if (recipe.IsForUniqueProduct == false)
				{
					newItem = GameInventoryItem.Create(product);
				}
				else
				{
					DbItemUnique unique = new DbItemUnique(product);
					newItem = GameInventoryItem.Create(unique);
					newItem.Quality = GetQuality(player, recipe.Level);
				}

				newItem.IsCrafted = true;
				newItem.Creator = player.Name;
				newItem.Count = -countToAdd;

				if (slot.Key > 0)	// Create new item in the backpack
				{
					player.Inventory.AddItem((eInventorySlot)slot.Key, newItem);
					InventoryLogging.LogInventoryAction("(craft)", player, eInventoryActionType.Craft, newItem.Template, newItem.Count);
				}
				else					// Create new item on the ground
				{
					player.CreateItemOnTheGround(newItem);
					player.Out.SendDialogBox(eDialogCode.SimpleWarning, 0, 0, 0, 0, eDialogType.Ok, true, LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.BuildCraftedItem.BackpackFull", product.Name));
				}
			}

			player.Inventory.CommitChanges();

			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.BuildCraftedItem.Successfully", product.Name, newItem.Quality), eChatType.CT_Missed, eChatLoc.CL_SystemWindow);

			if (recipe.IsForUniqueProduct && newItem.Quality == 100)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AbstractCraftingSkill.BuildCraftedItem.Masterpiece"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				player.Out.SendPlaySound(eSoundType.Craft, 0x04);
			}
			else
			{
				player.Out.SendPlaySound(eSoundType.Craft, 0x03);
			}
			
		}

		#endregion

		#region Calcul functions
		public virtual int CalculateChanceToMakeItem(GamePlayer player, int craftingLevel)
        {
			return ChanceToMakeAt(player.GetCraftingSkillValue(m_eskill), craftingLevel);
		}

		/// <summary>Chance to make an item at this skill value (CraftRules.ChanceToMake); a gamebot crafts by the same table.</summary>
		public int ChanceToMakeAt(int skillValue, int craftingLevel) => CraftRules.ChanceToMake(skillValue, craftingLevel);

		/// <summary>
		/// Calculate chance to gain point (CraftRules.ChanceToGainPoint: the con's chance, plus the capital bonus in a
		/// capital city, patch 1.86)
		/// </summary>
		public virtual int CalculateChanceToGainPoint(GamePlayer player, int recipeLevel)
		{
			bool inCapital = player.CurrentRegion?.IsCapitalCity == true;
			var context = new CraftContext(player.GetCraftingSkillValue(m_eskill), recipeLevel, inCapital,
				inCapital ? player.CraftingSkillBonus : 0);
			return CraftRules.ChanceToGainPoint(context);
		}

		/// <summary>Chance to gain a point at this skill value, before a capital city's bonus (CraftRules).</summary>
		public int BaseChanceToGainAt(int skillValue, int recipeLevel) => CraftRules.BaseChanceToGainPoint(skillValue, recipeLevel);

		/// <summary>The clock a player crafts to: everything CraftRules.CraftingTimeSeconds needs, read from the player.</summary>
		public CraftContext CraftContextFor(GamePlayer player, Recipe recipe)
		{
			eRealm realm = player.Realm;
			return new CraftContext(
				Skill: player.GetCraftingSkillValue(m_eskill),
				RecipeLevel: recipe.Level,
				SpeedMultiplier: player.CraftingSpeed,
				RealmRelics: RelicMgr.GetRelicCount(realm),
				KeepTimerDivisor: CraftRules.KeepTimerDivisor(
					Keeps.KeepBonusMgr.RealmHasBonus(DOL.GS.Keeps.eKeepBonusType.Craft_Timers_5, realm),
					Keeps.KeepBonusMgr.RealmHasBonus(DOL.GS.Keeps.eKeepBonusType.Craft_Timers_3, realm)),
				MaxCraftTimeSeconds: Properties.MAX_CRAFT_TIME,
				InstantGm: (ePrivLevel)player.Client.Account.PrivLevel >= ePrivLevel.GM);
		}

		public virtual int GetCraftingTime(GamePlayer player, Recipe recipe)
		{
			if ((ePrivLevel) player.Client.Account.PrivLevel >= ePrivLevel.GM)
				return 0;

			int craftingTime = CraftRules.CraftingTimeSeconds(CraftContextFor(player, recipe), CraftRules.IngredientCounts(recipe));

			if (player.Client.Account.PrivLevel > 1)
			{
				player.Out.SendMessage($"[DEBUG] Crafting time: {craftingTime}s", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
			return craftingTime;
		}

		/// <summary>Crafting time in seconds at this skill value, at crafting speed 1 (DR's CraftingTimeAt). A gamebot works
		/// to the same clock: its realm's relics and keep bonuses and max_craft_time apply.</summary>
		public int CraftingTimeAt(int skillValue, Recipe recipe, eRealm realm)
		{
			var context = new CraftContext(skillValue, recipe.Level,
				RealmRelics: RelicMgr.GetRelicCount(realm),
				KeepTimerDivisor: CraftRules.KeepTimerDivisor(
					Keeps.KeepBonusMgr.RealmHasBonus(DOL.GS.Keeps.eKeepBonusType.Craft_Timers_5, realm),
					Keeps.KeepBonusMgr.RealmHasBonus(DOL.GS.Keeps.eKeepBonusType.Craft_Timers_3, realm)),
				MaxCraftTimeSeconds: Properties.MAX_CRAFT_TIME);
			return CraftRules.CraftingTimeSeconds(context, CraftRules.IngredientCounts(recipe));
		}

		public virtual int GetSecondaryCraftingSkillMinimumLevel(Recipe recipe)
		{
			return 0;
		}

		/// <summary>
		/// Calculate crafted item quality (CraftRules: the 1.87 rules, or the classic 94-99% with crafting_classic_quality)
		/// </summary>
		private int GetQuality(GamePlayer player, int recipeLevel)
		{
			return QualityAt(player.GetCraftingSkillValue(m_eskill), recipeLevel);
		}

		/// <summary>Crafted item quality at this skill value (DR's QualityAt), by the server's quality rule.</summary>
		public int QualityAt(int skillValue, int recipeLevel) =>
			CraftRules.RollQuality(skillValue, recipeLevel, null, CraftingEconomyProperties.CRAFTING_CLASSIC_QUALITY);

		/// <summary>
		/// get item con color compared to crafters skill (CraftRules.Con)
		/// </summary>
		/// <param name="crafterSkill"></param>
		/// <param name="itemCraftingLevel"></param>
		/// <returns>-3 grey, -2 green, -1 blue, 0 yellow, 1 orange, 2 red, 3 purple</returns>
		public int GetItemCon(int crafterSkill, int itemCraftingLevel) => (int)CraftRules.Con(crafterSkill, itemCraftingLevel);
		#endregion
	}
}
