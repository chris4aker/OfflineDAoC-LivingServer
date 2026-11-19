using System;
using System.Collections;
using System.Collections.Generic;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS
{
    /// <summary>
    /// Spellcrafting. Crafting 2.0 WP1 (2026-11-15): every number and the combine check come from
    /// <see cref="SpellcraftRules"/> (a bot spellcrafter imbues by the same rules), with upstream's fifth-bonus and
    /// focus-on-staves fixes; with crafting_classic_losses on an explosion destroys the item too.
    /// </summary>
    public class SpellCrafting : AdvancedCraftingSkill
	{
        protected override String Profession
        {
            get
            {
                return "CraftersProfession.Spellcrafter";
            }
        }

        private static readonly int[,] itemMaxBonusLevel =  // taken from mythic Spellcraft calculator
		{
			{0,1,1,1,1,1,1},
			{1,1,1,1,1,2,2},
			{1,1,1,2,2,2,2},
			{1,1,2,2,2,3,3},
			{1,2,2,2,3,3,4},
			{1,2,2,3,3,4,4},
			{2,2,3,3,4,4,5},
			{2,3,3,4,4,5,5},
			{2,3,3,4,5,5,6},
			{2,3,4,4,5,6,7},
			{2,3,4,5,6,6,7},
			{3,4,4,5,6,7,8},
			{3,4,5,6,6,7,9},
			{3,4,5,6,7,8,9},
			{3,4,5,6,7,8,10},
			{3,5,6,7,8,9,10},
			{4,5,6,7,8,10,11},
			{4,5,6,8,9,10,12},
			{4,6,7,8,9,11,12},
			{4,6,7,8,10,11,13},
			{4,6,7,9,10,12,13},
			{5,6,8,9,11,12,14},
			{5,7,8,10,11,13,15},
			{5,7,9,10,12,13,15},
			{5,7,9,10,12,14,16},
			{5,8,9,11,12,14,16},
			{6,8,10,11,13,15,17},
			{6,8,10,12,13,15,18},
			{6,8,10,12,14,16,18},
			{6,9,11,12,14,16,19},
			{6,9,11,13,15,17,20},
			{7,9,11,13,15,17,20},
			{7,10,12,14,16,18,21},
			{7,10,12,14,16,19,21},
			{7,10,12,14,17,19,22},
			{7,10,13,15,17,20,23},
			{8,11,13,15,17,20,23},
			{8,11,13,16,18,21,24},
			{8,11,14,16,18,21,24},
			{8,11,14,16,19,22,25},
			{8,12,14,17,19,22,26},
			{9,12,15,17,20,23,26},
			{9,12,15,18,20,23,27},
			{9,13,15,18,21,24,27},
			{9,13,16,18,21,24,28},
			{9,13,16,19,22,25,29},
			{10,13,16,19,22,25,29},
			{10,14,17,20,23,26,30},
			{10,14,17,20,23,27,31},
			{10,14,17,20,23,27,31},
			{10,15,18,21,24,28,32},
		};

		public SpellCrafting()
		{
			Icon = 0x0D;
			Name = LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, 
				"Crafting.Name.Evocation");
			eSkill = eCraftingSkill.SpellCrafting;
		}

		#region Classic craft functions
		public override void GainCraftingSkillPoints(GamePlayer player, Recipe recipe)
		{
			if (Util.Chance(CalculateChanceToGainPoint(player, recipe.Level)))
			{
				GainRatedPoints(player, eCraftingSkill.SpellCrafting);

				if (player.GetCraftingSkillValue(eCraftingSkill.GemCutting) < subSkillCap)
	                GainRatedPoints(player, eCraftingSkill.GemCutting, subSkillCap);

				player.Out.SendUpdateCraftingSkills();
			}
		}

		#endregion

		#region Requirement check

		/// <summary>
		/// This function is called when player accept the combine
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		public override bool IsAllowedToCombine(GamePlayer player, DbInventoryItem item)
		{
			if (!base.IsAllowedToCombine(player, item)) return false;

			if (((DbInventoryItem)player.TradeWindow.TradeItems[0]).Model == 525) // first ingredient to combine is Dust => echantement
			{
				if (item.Level < 15)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, 
						"SpellCrafting.IsAllowedToCombine.NoEnchanted"), eChatType.CT_System, 
						eChatLoc.CL_SystemWindow);

					return false;
				}

				lock (player.TradeWindow.Lock)
				{
					foreach (DbInventoryItem materialToCombine in player.TradeWindow.TradeItems)
					{
						if (materialToCombine.Model != 525)
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, 
								"SpellCrafting.IsAllowedToCombine.FalseMaterial", 
								materialToCombine.Name), 
								eChatType.CT_System, eChatLoc.CL_SystemWindow);

							return false;
						}
					}
				}
			}
			else
			{
				List<ImbueGem> gems;
				lock (player.TradeWindow.Lock)
				{
					gems = TradeWindowGems(player, withFocus: true);
				}

				ImbueRefusal refusal = SpellcraftRules.CheckImbue(ItemBonuses(item), item.Level, item.Quality,
					item.Object_Type == (int)eObjectType.Staff, gems, player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting));

				if (refusal != ImbueRefusal.None)
				{
					string key = SpellcraftRules.RefusalKey(refusal);
					string text = refusal switch
					{
						ImbueRefusal.AlreadyImbued or ImbueRefusal.TooManyBonuses or ImbueRefusal.OverchargeTooFar =>
							LanguageMgr.GetTranslation(player.Client.Account.Language, key, item.Name),
						// upstream aa8b5232b's message; Crafting.txt (WP3's file) does not have the key yet
						ImbueRefusal.FocusNotOnStaff => Text(player, key, "Only staves can be imbued with focus properties!"),
						_ => LanguageMgr.GetTranslation(player.Client.Account.Language, key),
					};
					player.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}
			}

			return true;
		}

		#endregion

		#region Apply magical effect

		/// <summary>
		/// Apply all needed magical bonus to the item
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		protected override void ApplyMagicalEffect(GamePlayer player, DbInventoryItem item)
		{
			if (item == null || player.TradeWindow.TradeItems[0] == null) return; // be sure at least one item in each side

			if (((DbInventoryItem)player.TradeWindow.TradeItems[0]).Model == 525) // Echant item
			{
				ApplyMagicalDusts(player, item);
			}
			else if (((DbInventoryItem)player.TradeWindow.TradeItems[0]).Object_Type == (int)eObjectType.SpellcraftGem) // Spellcraft item
			{
				if (ApplySpellcraftGems(player, item))
					return; // destroyed by a classic explosion: nothing left to save
			}

			if (item.Template is DbItemUnique)
			{
				GameInventoryObjectExtensions.SaveItem(item);
				GameServer.Database.SaveObject(item.Template as DbItemUnique);
			}
			else
			{
				ChatUtil.SendErrorMessage(player, "Spellcrafting error: Item was not an ItemUnique, crafting changes not saved to DB!");
				log.ErrorFormat("Spellcrafting error: Item {0} was not an ItemUnique for player {1}, crafting changes not saved to DB!", item.Id_nb, player.Name);
			}
		}

		/// <summary>
		/// Apply all magical dust
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		private void ApplyMagicalDusts(GamePlayer player, DbInventoryItem item)
		{
			int spellCrafterLevel = player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting);

			int bonusCap;
			if (spellCrafterLevel < 300 || item.Level < 15) bonusCap = 0;
			else if (spellCrafterLevel < 400 || item.Level < 20) bonusCap = 5;
			else if (spellCrafterLevel < 500 || item.Level < 25) bonusCap = 10;
			else if (spellCrafterLevel < 600 || item.Level < 30) bonusCap = 15;
			else if (spellCrafterLevel < 700 || item.Level < 35) bonusCap = 20;
			else if (spellCrafterLevel < 800 || item.Level < 40) bonusCap = 25;
			else if (spellCrafterLevel < 900 || item.Level < 45) bonusCap = 30;
			else bonusCap = 35;

			player.Inventory.BeginChanges();

			int bonusMod = item.Bonus;
			lock (player.TradeWindow.Lock)
			{
				foreach (DbInventoryItem gemme in player.TradeWindow.TradeItems)
				{
					switch (gemme.Name)
					{
						case "coppery imbued dust": bonusMod += 2; break;
						case "coppery enchanted dust": bonusMod += 4; break;
						case "coppery glowing dust": bonusMod += 6; break;
						case "coppery ensorcelled dust": bonusMod += 8; break;

						case "silvery imbued dust": bonusMod += 10; break;
						case "silvery enchanted dust": bonusMod += 12; break;
						case "silvery glowing dust": bonusMod += 14; break;
						case "silvery ensorcelled dust": bonusMod += 16; break;

						case "golden imbued dust": bonusMod += 18; break;
						case "golden enchanted dust": bonusMod += 20; break;
						case "golden glowing dust": bonusMod += 22; break;
						case "golden ensorcelled dust": bonusMod += 24; break;

						case "mithril imbued dust": bonusMod += 26; break;
						case "mithril enchanted dust": bonusMod += 28; break;
						case "mithril glowing dust": bonusMod += 30; break;
						case "mithril ensorcelled dust": bonusMod += 32; break;

						case "platinum imbued dust": bonusMod += 34; break;
						case "platinum enchanted dust": bonusMod += 36; break;
						case "platinum glowing dust": bonusMod += 38; break;
						case "platinum ensorcelled dust": bonusMod += 40; break;

					}
					player.Inventory.RemoveCountFromStack(gemme, 1);
					InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, gemme.Template);
				}
			}

			item.Bonus = Math.Min(bonusMod, bonusCap);

			player.Inventory.CommitChanges();
		}

		/// <summary>
		/// Apply all spellcraft gems bonus
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns>true when a classic explosion destroyed the item</returns>
		private bool ApplySpellcraftGems(GamePlayer player, DbInventoryItem item)
		{
			bool destroyed = false;
			int maxBonusLevel = GetItemMaxImbuePoints(item);
			int bonusLevel = GetTotalImbuePoints(player, item);

			int sucessChances = 100;
			if (bonusLevel > maxBonusLevel) sucessChances = CalculateChanceToOverchargeItem(player, item, maxBonusLevel, bonusLevel);
			int destroyChance = 100 - CalculateChanceToPreserveItem(player, item, maxBonusLevel, bonusLevel);


			GamePlayer tradePartner = player.TradeWindow.Partner;

			if (Util.Chance(sucessChances))
			{
				lock (player.TradeWindow.Lock)
				{
					player.Inventory.BeginChanges();
					foreach (DbInventoryItem gem in (ArrayList)player.TradeWindow.TradeItems.Clone())
					{
						if (item.Bonus1Type == 0)
						{
							item.Bonus1Type = gem.Bonus1Type;
							item.Bonus1 = gem.Bonus1;
						}
						else if (item.Bonus2Type == 0)
						{
							item.Bonus2Type = gem.Bonus1Type;
							item.Bonus2 = gem.Bonus1;
						}
						else if (item.Bonus3Type == 0)
						{
							item.Bonus3Type = gem.Bonus1Type;
							item.Bonus3 = gem.Bonus1;
						}
						else if (item.Bonus4Type == 0)
						{
							item.Bonus4Type = gem.Bonus1Type;
							item.Bonus4 = gem.Bonus1;
						}
						player.Inventory.RemoveCountFromStack(gem, 1);
						InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, gem.Template);
					}
					player.Inventory.CommitChanges();
				}

				if (tradePartner != null)
				{
					tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.ImbuedItem", player.Name), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				}
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.ImbuedItem", player.Name), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}
			else if (Util.Chance(destroyChance))
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.PowerExplodes"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);

				lock (player.TradeWindow.Lock)
				{
					player.Inventory.BeginChanges();
                    // Luhz Crafting Update:
                    // The base item is no longer lost when spellcrafting explodes - only gems are destroyed.
                    foreach (DbInventoryItem gem in (ArrayList)player.TradeWindow.TradeItems.Clone())
                    {
                        if (gem.Object_Type == (int)eObjectType.SpellcraftGem)
						{
                            player.Inventory.RemoveCountFromStack(gem, 1);
							InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, gem.Template);
						}
                    }
                    player.Inventory.CommitChanges();
				}

				// crafting_classic_losses (off by default; WP1): the era's explosion takes the item with the gems.
				if (CraftRules.ExplosionDestroysItem(CraftingEconomyProperties.CRAFTING_CLASSIC_LOSSES))
					destroyed = DestroyExplodedItem(player, item);

				player.Emote(eEmote.SpellGoBoom);
				player.Health = 0;
				player.Die(player); // On official you take damages
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);

				if (tradePartner != null)
				{
					if (Util.Chance(40))
					{
						tradePartner.Emote(eEmote.SpellGoBoom);
						tradePartner.Health = 0;
						tradePartner.Die(player);
					}
					tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				}
			}
			else
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				if (tradePartner != null)
				{
					tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				}
			}
			return destroyed;
		}

		/// <summary>
		/// Removes an item a classic explosion destroyed from its owner's backpack (the trade partner's when imbuing
		/// another player's item). Returns false if it was not found there.
		/// </summary>
		private static bool DestroyExplodedItem(GamePlayer player, DbInventoryItem item)
		{
			GamePlayer owner = player.TradeWindow?.Partner ?? player;
			if (owner.Inventory == null || !owner.Inventory.RemoveItem(item))
				return false;

			InventoryLogging.LogInventoryAction(owner, "(craft)", eInventoryActionType.Craft, item.Template, item.Count);
			string text = Text(player, "SpellCrafting.ApplySpellcraftGems.ItemDestroyed", "The {0} is destroyed!", item.Name);
			player.Out.SendMessage(text, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			if (owner != player)
				owner.Out.SendMessage(Text(owner, "SpellCrafting.ApplySpellcraftGems.ItemDestroyed", "The {0} is destroyed!", item.Name), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			return true;
		}

		#endregion

		# region Show informations to player

		public virtual void ShowSpellCraftingInfo(GamePlayer player, DbInventoryItem item)
		{
			// 525 == dust.
			if ((player.TradeWindow.TradeItems[0] as DbInventoryItem).Model == 525)
				return;

			ArrayList spellCraftingInfo = new(10);
			int totalItemCharges = GetItemMaxImbuePoints(item);
			int totalGemCharges = GetTotalImbuePoints(player, item);
			spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.TotalImbue", item.Name, totalItemCharges));
			spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.CurrentBonus", item.Name));

			if (item.Bonus1Type != 0)
			{
				spellCraftingInfo.Add($"* {SkillBase.GetPropertyName((eProperty) item.Bonus1Type)}: {item.Bonus1} {(item.Bonus1Type is >= (int) eProperty.Resist_First and <= (int) eProperty.Resist_Last ? "%" : " pts")}");

				if (item.Bonus2Type != 0)
				{
					spellCraftingInfo.Add($"* {SkillBase.GetPropertyName((eProperty) item.Bonus2Type)}: {item.Bonus2} {(item.Bonus2Type is >= (int) eProperty.Resist_First and <= (int) eProperty.Resist_Last ? "%" : "pts")}");

					if (item.Bonus3Type != 0)
					{
						spellCraftingInfo.Add($"* {SkillBase.GetPropertyName((eProperty) item.Bonus3Type)}: {item.Bonus3} {(item.Bonus3Type is >= (int) eProperty.Resist_First and <= (int) eProperty.Resist_Last ? "%" : "pts")}");

						if (item.Bonus4Type != 0)
							spellCraftingInfo.Add($"* {SkillBase.GetPropertyName((eProperty) item.Bonus4Type)}: {item.Bonus4} {(item.Bonus4Type is >= (int) eProperty.Resist_First and <= (int) eProperty.Resist_Last ? "%" : "pts")}");
					}
				}
			}

			spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.GemBonuses"));

			lock (player.TradeWindow.Lock)
			{
				for (int i = 0; i < player.TradeWindow.ItemsCount; i++)
				{
					DbInventoryItem currentGem = player.TradeWindow.TradeItems[i] as DbInventoryItem;
					spellCraftingInfo.Add($"* {currentGem.Name} - {SkillBase.GetPropertyName((eProperty) currentGem.Bonus1Type)}: ({GetGemImbuePoints(currentGem.Bonus1Type, currentGem.Bonus1)}) {currentGem.Bonus1} {(currentGem.Bonus1Type >= (int) eProperty.Resist_First && currentGem.Bonus1Type <= (int) eProperty.Resist_Last ? "%" : "pts")}");
				}
			}

			spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.ImbueCapacity", totalGemCharges, totalItemCharges));

			if (totalGemCharges > totalItemCharges)
			{
				int maxBonusLevel = GetItemMaxImbuePoints(item);
				int bonusLevel = GetTotalImbuePoints(player, item);
				spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.Penalty", GetOverchargePenality(totalItemCharges, totalGemCharges)));
				spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.Success", CalculateChanceToOverchargeItem(player, item, maxBonusLevel, bonusLevel)));
			}

			GamePlayer partner = player.TradeWindow.Partner;

			foreach (string line in spellCraftingInfo)
			{
				player.Out.SendMessage(line, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				partner?.Out.SendMessage(line, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}

			if (totalGemCharges > totalItemCharges)
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfos.Modified"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}

		# endregion

		#region Calcul functions

		/// <summary>The item's Bonus1-4 for SpellcraftRules.</summary>
		protected static ImbueBonus[] ItemBonuses(DbInventoryItem item) =>
		[
			new(item.Bonus1Type, item.Bonus1), new(item.Bonus2Type, item.Bonus2),
			new(item.Bonus3Type, item.Bonus3), new(item.Bonus4Type, item.Bonus4),
		];

		/// <summary>Everything in the crafter's side of the window as gems for SpellcraftRules (the caller holds the lock).
		/// <paramref name="withFocus"/> looks up whether each gem's bonus is a focus (only the combine check needs it).</summary>
		protected static List<ImbueGem> TradeWindowGems(GamePlayer player, bool withFocus = false)
		{
			var gems = new List<ImbueGem>(player.TradeWindow.ItemsCount);
			for (int i = 0; i < player.TradeWindow.TradeItems.Count; i++)
			{
				DbInventoryItem gem = (DbInventoryItem)player.TradeWindow.TradeItems[i];
				gems.Add(new ImbueGem(gem.Object_Type, gem.Bonus1Type, gem.Bonus1, gem.Quality,
					withFocus && SkillBase.CheckPropertyType((eProperty)gem.Bonus1Type, ePropertyType.Focus)));
			}
			return gems;
		}

		/// <summary>
        /// Get the sucess chance to overcharge the item (SpellcraftRules.ChanceToOvercharge, Kort's 1.87 calculator)
		/// </summary>
		protected int CalculateChanceToOverchargeItem(GamePlayer player, DbInventoryItem item, int maxBonusLevel, int bonusLevel)
		{
			return SpellcraftRules.ChanceToOvercharge(item.Quality, player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting), maxBonusLevel, bonusLevel);
		}

		/// <summary>
		/// Get the maximum bonus level the item can hold (SpellcraftRules.MaxImbuePoints)
		/// </summary>
		protected int GetItemMaxImbuePoints(DbInventoryItem item)
		{
			return SpellcraftRules.MaxImbuePoints(item.Level, item.Quality);
		}

		/// <summary>
		/// Get gems bonus level (SpellcraftRules.TotalImbuePoints: the item's Bonus1-3 and every gem in the window)
		/// </summary>
		/// <param name="player">player spellcrafting</param>
		/// <param name="item">item to calculate imbues for</param>
		protected int GetTotalImbuePoints(GamePlayer player, DbInventoryItem item)
		{
			var existing = new List<(int Type, int Value)>(3)
			{
				(item.Bonus1Type, item.Bonus1), (item.Bonus2Type, item.Bonus2), (item.Bonus3Type, item.Bonus3),
			};
			List<ImbueGem> gems;
			lock (player.TradeWindow.Lock)
			{
				gems = TradeWindowGems(player);
			}
			return SpellcraftRules.TotalImbuePoints(existing, SpellcraftRules.GemBonuses(gems));
		}

		/// <summary>
		/// Get how much the gem use like bonus point (SpellcraftRules.GemImbuePoints)
		/// </summary>
		protected int GetGemImbuePoints(int bonusType, int bonusValue)
		{
			return SpellcraftRules.GemImbuePoints(bonusType, bonusValue);
		}

		/// <summary>
		/// Get the % overcharge penality (SpellcraftRules.OverchargePenalty)
		/// </summary>
		protected int GetOverchargePenality(int maxBonusLevel, int bonusLevel)
		{
			return SpellcraftRules.OverchargePenalty(maxBonusLevel, bonusLevel);
		}

		/// <summary>
        /// Get the chance to preserve item while overcharging (SpellcraftRules.ChanceToPreserve)
		/// </summary>
		protected int CalculateChanceToPreserveItem(GamePlayer player, DbInventoryItem item, int maxBonusLevel, int bonusLevel)
		{
			var gemQualities = new List<int>(player.TradeWindow.ItemsCount);
			lock (player.TradeWindow.Lock)
			{
				foreach (DbInventoryItem gem in player.TradeWindow.TradeItems)
					gemQualities.Add(gem.Quality);
			}
			return SpellcraftRules.ChanceToPreserve(item.Quality, gemQualities, player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting));
		}

		#endregion

	}
}
