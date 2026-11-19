using System;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using JNogueira.Discord.WebhookClient;

namespace DOL.GS.ServerRules
{
	/// <summary>
	/// Handles DF entrance jump point allowing only one realm to enter on Normal server type.
	/// </summary>
	public class DFEnterJumpPoint : IJumpPointHandler
	{
		/// <summary>
		/// Shared entrance authority for client players and server-controlled
		/// characters. Characters already inside Darkness Falls are not evicted
		/// when control changes; this method only answers whether a new entry is
		/// legal at this instant.
		/// </summary>
		public static bool CanRealmEnter(eRealm realm)
		{
			if (GameServer.Instance.Configuration.ServerType != EGameServerType.GST_Normal)
				return true;
			if (ServerProperties.Properties.ALLOW_ALL_REALMS_DF)
				return true;
			if (realm == PreviousOwner && LastRealmSwapTick + GracePeriod >= GameLoop.GameLoopTime)
				return true;
			return realm != eRealm.None && realm == DarknessFallOwner;
		}

		/// <summary>
		/// Decides whether player can jump to the target point.
		/// All messages with reasons must be sent here.
		/// Can change destination too.
		/// </summary>
		/// <param name="targetPoint">The jump destination</param>
		/// <param name="player">The jumping player</param>
		/// <returns>True if allowed</returns>
		public bool IsAllowedToJump(DbZonePoint targetPoint, GamePlayer player)
		{
            if (player.Client.Account.PrivLevel > 1)
            {
                return true;
            }
			return CanRealmEnter(player.Realm);
		}

		public static eRealm DarknessFallOwner = eRealm.None;
		public static eRealm PreviousOwner = eRealm.None;

		public static long GracePeriod = 900 * 1000; //15 mins
		public static long LastRealmSwapTick = 0;
		
		/// <summary>
		/// initialize the darkness fall entrance system
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		[ScriptLoadedEvent]
		public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			CheckDFOwner();
			GameEventMgr.AddHandler(KeepEvent.KeepTaken,new DOLEventHandler(OnKeepTaken));
		}

		/// <summary>
		/// check if a realm have more keep at start
		/// to know the DF owner
		/// </summary>
		
		private static void CheckDFOwner()
		{
			DarknessFallOwner = OwnerByKeepCount(DarknessFallOwner);
		}

		// The realm with the most keeps; a tie keeps a tied holder (AutonomousDarknessFallsPolicy.NextOwner, from True 1.65).
		private static eRealm OwnerByKeepCount(eRealm currentOwner) =>
			AutonomousDarknessFallsPolicy.NextOwner(currentOwner,
				GameServer.KeepManager.GetKeepCountByRealm(eRealm.Albion),
				GameServer.KeepManager.GetKeepCountByRealm(eRealm.Midgard),
				GameServer.KeepManager.GetKeepCountByRealm(eRealm.Hibernia));

		/// <summary>
		/// when  keep is taken it check if the realm which take gain the control of DF
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="arguments"></param>
		public static void OnKeepTaken(DOLEvent e, object sender, EventArgs arguments)
		{
			if (arguments is not KeepEventArgs)
				return;
			// Recount every realm, not only the one that took the keep (True 1.65, DF L3): a capture by a third realm can
			// take the lead from the holder, and a capture must never hand Darkness Falls to a realm that still has fewer
			// keeps than another (with no holder, any capture used to).
			eRealm oldOwner = DarknessFallOwner;
			eRealm newOwner = OwnerByKeepCount(oldOwner);
			if (newOwner == oldOwner)
				return;
			PreviousOwner = oldOwner;
			LastRealmSwapTick = GameLoop.GameLoopTime;
			DarknessFallOwner = newOwner;

			if (oldOwner != eRealm.None)
				BroadcastMessage(string.Format("{0} will lose access to Darkness Falls in 15 minutes!", GlobalConstants.RealmToName(oldOwner)), eRealm.None);
			if (newOwner != eRealm.None)
			{
				string messageDFGetControl = string.Format("The forces of {0} have gained access to Darkness Falls!", GlobalConstants.RealmToName(newOwner));
				BroadcastMessage(messageDFGetControl, eRealm.None);
				// Living Server 1.2: Darkness Falls changing hands is a screen-centre line too (at most every 15 minutes).
				LoreAtmosphere.DarknessFallsChanged(messageDFGetControl);
			}
		}
		
		/// <summary>
		/// Method to broadcast messages, if eRealm.None all can see,
		/// else only the right realm can see
		/// </summary>
		/// <param name="message">The message</param>
		/// <param name="realm">The realm</param>
		public static void BroadcastMessage(string message, eRealm realm)
		{
			foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRealm(realm))
				player.Out.SendMessage(message, eChatType.CT_Important, eChatLoc.CL_SystemWindow);

			if (DiscordClientManager.TryGetClient(WebhookType.RvR, out var discordClient))
			{
				// Create your DiscordMessage with all parameters of your message.
				var discordMessage = new DiscordMessage(
					"",
					username: "RvR",
					avatarUrl: "",
					tts: false,
					embeds: new[]
					{
						new DiscordMessageEmbed(
							author: new DiscordMessageEmbedAuthor("Darkness Falls"),
							color: 0,
							description: message
						)
					}
				);

				discordClient.SendToDiscordAsync(discordMessage);
			}
		}

		public static void SetDFOwner(GamePlayer p, eRealm NewDFOwner)
		{
			if (DarknessFallOwner != NewDFOwner)
			{
				foreach (GamePlayer otherPlayer in ClientService.Instance.GetPlayersOfRegion(WorldMgr.GetRegion(249)))
				{
					if (otherPlayer.Realm == DarknessFallOwner)
						otherPlayer.Out.SendSoundEffect(217, 0, 0, 0, 0, 0);
					else if (otherPlayer.Realm == NewDFOwner)
						otherPlayer.Out.SendSoundEffect(216, 0, 0, 0, 0, 0);
				}

				DarknessFallOwner = NewDFOwner;
				p.Out.SendMessage(string.Format("New DF Owner set to {0}", GlobalConstants.RealmToName(NewDFOwner)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
			else
				p.Out.SendMessage(string.Format("DF Owner is already set to {0}", GlobalConstants.RealmToName(NewDFOwner)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}
	}
}
