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
using DOL.GS.ServerProperties;
using DOL.Language;
using System;
using System.Collections;

namespace DOL.GS.Keeps
{
	/// <summary>
	/// Represents a keep hastener
	/// </summary>
	public class FrontierHastener : GameKeepGuard
	{
		public override eFlags Flags
		{
			get { return eFlags.PEACE; }
		}

		protected override void SetModel()
		{
			if (!ServerProperties.Properties.AUTOMODEL_GUARDS_LOADED_FROM_DB && !LoadedFromScript)
			{
				return;
			}
			// Port of OpenDAoC-Core 43924def4: by the model realm, so a hastener of a keep with no realm wears the model
			// SetRealm picked for it (as the other keep guards do), not always Albion's.
			switch (ModelRealm)
			{
				case eRealm.None:
				case eRealm.Albion:
					{
						Model = (ushort)eLivingModel.AlbionHastener;
						Size = 45;
						break;
					}
				case eRealm.Midgard:
					{
						Model = (ushort)eLivingModel.MidgardHastener;
						Size = 50;
						Flags ^= eFlags.GHOST;
						break;
					}
				case eRealm.Hibernia:
					{
						Model = (ushort)eLivingModel.HiberniaHastener;
						Size = 45;
						break;
					}
			}
			return;
		}

		#region Examine/Interact Message

		/// <summary>
		/// Adds messages to ArrayList which are sent when object is targeted
		/// </summary>
		/// <param name="player">GamePlayer that is examining this object</param>
		/// <returns>list with string messages</returns>
		public override IList GetExamineMessages(GamePlayer player)
		{
			IList list = new ArrayList();
			list.Add("You examine " + GetName(0, false) + ".  " + GetPronoun(0, true) + " is " + GetAggroLevelString(player, false) + " and is a hastener.");
			return list;
		}

		public override bool Interact(GamePlayer player)
		{
			if (!base.Interact(player))
				return false;

			if (!Serves(GameServer.Instance.Configuration.ServerType, player.Realm == Realm,
				() => GameServer.ServerRules.IsSameRealm(player, this, true)))
				return false;

			TurnTo(player, 5000);
			this.CastSpellOnOwnerAndPets(player, SkillBase.GetSpellByID(GameHastener.SPEEDOFTHEREALMID), SkillBase.GetSpellLine(GlobalSpellsLines.Realm_Spells), false);
			player.Out.SendSpellEffectAnimation(this, player, SkillBase.GetSpellByID(935).ClientEffect, 0, false, 1);
			return true;
		}
		#endregion Examine/Interact Message

		/// <summary>
		/// Whether a hastener speeds up this player. Port of OpenDAoC-Core 43924def4, adapted: on PvE and PvP servers the
		/// server rules decide, so every player there is served (upstream's fix). On our normal RvR server it stays the
		/// player's own realm: a hastener is a PEACE NPC, which NormalServerRules.IsSameRealm counts as everyone's realm,
		/// so upstream's change as written would have let an enemy use another realm's keep hastener.
		/// </summary>
		public static bool Serves(EGameServerType serverType, bool sameRealm, Func<bool> sameRealmByServerRules) =>
			serverType is EGameServerType.GST_PvE or EGameServerType.GST_PvP ? sameRealmByServerRules() : sameRealm;

		protected override void SetRespawnTime()
		{
				RespawnInterval = 5000;
		}

		protected override void SetName()
		{
			Name = LanguageMgr.GetTranslation(Properties.SERV_LANGUAGE, "SetGuardName.Hastener");
			return;
		}
	}
}