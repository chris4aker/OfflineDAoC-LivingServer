/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;
using System.Collections.Generic;

using DOL.Events;
using DOL.Database;
using DOL.GS.CraftingEconomy;

namespace DOL.GS.GameEvents
{
	/// <summary>
	/// Enable Crafting Level Upon Character Creation
	/// </summary>
	public static class CharacterCreationCraft
	{
		/// <summary>
		/// Register Character Creation Events
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		[ScriptLoadedEvent]
		public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			GameEventMgr.AddHandler(DatabaseEvent.CharacterCreated, new DOLEventHandler(OnCharacterCreation));

			// Crafting 2.0 (WP3): with era start every new character needs a working Master. Several capital masters (the
			// Camelot alchemist, the Aegir's Landing spellcrafter, the Domnann tailor) work only through their guild name,
			// which use_npcguildscripts (on by default and in the shipped world) turns into the master's class.
			if (CraftingEconomyProperties.CRAFTING_ERA_START && !ServerProperties.Properties.USE_NPCGUILDSCRIPTS && log.IsWarnEnabled)
				log.Warn("crafting_era_start is on but use_npcguildscripts is off: some capitals' crafting masters are decorations, so new characters may have to join an order in another town (docs/CRAFTING.md).");
		}

		private static readonly DOL.Logging.Logger log = DOL.Logging.LoggerManager.Create(typeof(CharacterCreationCraft));
		
		/// <summary>
		/// Unregister Character Creation Events
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		[ScriptUnloadedEvent]
		public static void OnScriptUnloaded(DOLEvent e, object sender, EventArgs args)
		{
			GameEventMgr.RemoveHandler(DatabaseEvent.CharacterCreated, new DOLEventHandler(OnCharacterCreation));
		}
		
		/// <summary>
		/// Triggered When New Character is Created.
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		public static void OnCharacterCreation(DOLEvent e, object sender, EventArgs args)
		{
			// Check Args
			var chArgs = args as CharacterEventArgs;
			
			if (chArgs == null)
				return;
			
			DbCoreCharacter ch = chArgs.Character;

			// Crafting 2.0 (WP3), crafting_era_start: a new character joins an order at a Master (1.65), so it starts with no
			// order and no skills. The game reads crafting from the account-realm row (AccountXCrafting), which GamePlayer
			// creates on the first login in a realm by the same rule; this character copy only mirrors it.
			if (CraftingEconomyProperties.CRAFTING_ERA_START)
			{
				ch.SerializedCraftingSkills = string.Empty;
				ch.CraftingPrimarySkill = (int)eCraftingSkill.NoCrafting;
				return;
			}
			
			// Add all Crafting skills at level 1
			var collectionAllCraftingSkills = new List<string>();
			foreach (int craftingSkillId in Enum.GetValues(typeof(eCraftingSkill)))
			{
				if (craftingSkillId > 0)
				{
					collectionAllCraftingSkills.Add(string.Format("{0}|1", craftingSkillId));
					if (craftingSkillId == (int)eCraftingSkill._Last)
						break;
				}
			}
			
			// Set Primary Skill to Basic.
			ch.SerializedCraftingSkills = string.Join(";", collectionAllCraftingSkills);
			ch.CraftingPrimarySkill = (int)eCraftingSkill.BasicCrafting;
		}

	}
}
