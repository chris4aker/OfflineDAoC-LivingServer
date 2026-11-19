using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.API;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerTitles;
using DOL.GS.Quests;

namespace DOL.GS.DailyQuest.Hibernia
{
	public class PlayerKillQuestHib : Quests.DailyQuest
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		private const string questTitle = "Enemies of the Realm";
		private const int minimumLevel = 40;
		private const int maximumLevel = 50;

		private static GameNPC CaptainHib = null; // Start NPC

		private int PlayersKilled = 0;
		private const int MAX_KILLED = 10;
		
		// prevent grey killing
		private const int MIN_PLAYER_CON = -3;

		// Constructors
		public PlayerKillQuestHib() : base()
		{
		}

		public PlayerKillQuestHib(GamePlayer questingPlayer) : base(questingPlayer)
		{
		}

		public PlayerKillQuestHib(GamePlayer questingPlayer, int step) : base(questingPlayer, step)
		{
		}

		public PlayerKillQuestHib(GamePlayer questingPlayer, DbQuest dbQuest) : base(questingPlayer, dbQuest)
		{
		}

		public override int Level
		{
			get
			{
				// Quest Level
				return minimumLevel;
			}
		}
		
		[ScriptLoadedEvent]
		public static void ScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			if (!ServerProperties.Properties.LOAD_QUESTS)
				return;

			#region defineNPCs

			CaptainHib = DOL.GS.Quests.QuestCaptains.Ensure(eRealm.Hibernia, 200); // Captain Eithne, Druim Ligen

			#endregion

			#region defineItems
			#endregion

			#region defineObject
			#endregion

			GameEventMgr.AddHandler(GamePlayerEvent.AcceptQuest, new DOLEventHandler(SubscribeQuest));
			GameEventMgr.AddHandler(GamePlayerEvent.DeclineQuest, new DOLEventHandler(SubscribeQuest));

			GameEventMgr.AddHandler(CaptainHib, GameObjectEvent.Interact, new DOLEventHandler(TalkToCaptain));
			GameEventMgr.AddHandler(CaptainHib, GameLivingEvent.WhisperReceive, new DOLEventHandler(TalkToCaptain));

			/* Now we bring to the captain the possibility to give this quest to players */
			CaptainHib.AddQuestToGive(typeof (PlayerKillQuestHib));

			if (log.IsInfoEnabled)
				log.Info("Quest \"" + questTitle + "\" initialized");
		}

		[ScriptUnloadedEvent]
		public static void ScriptUnloaded(DOLEvent e, object sender, EventArgs args)
		{
			//if not loaded, don't worry
			if (CaptainHib == null)
				return;
			// remove handlers
			GameEventMgr.RemoveHandler(GamePlayerEvent.AcceptQuest, new DOLEventHandler(SubscribeQuest));
			GameEventMgr.RemoveHandler(GamePlayerEvent.DeclineQuest, new DOLEventHandler(SubscribeQuest));

			GameEventMgr.RemoveHandler(CaptainHib, GameObjectEvent.Interact, new DOLEventHandler(TalkToCaptain));
			GameEventMgr.RemoveHandler(CaptainHib, GameLivingEvent.WhisperReceive, new DOLEventHandler(TalkToCaptain));

			/* Now we remove to the captain the possibility to give this quest to players */
			CaptainHib.RemoveQuestToGive(typeof (PlayerKillQuestHib));
		}

		private static void TalkToCaptain(DOLEvent e, object sender, EventArgs args)
		{
			//We get the player from the event arguments and check if he qualifies		
			GamePlayer player = ((SourceEventArgs) args).Source as GamePlayer;
			if (player == null)
				return;

			if(CaptainHib.CanGiveQuest(typeof (PlayerKillQuestHib), player)  <= 0)
				return;

			//We also check if the player is already doing the quest
			PlayerKillQuestHib quest = player.IsDoingQuest(typeof (PlayerKillQuestHib)) as PlayerKillQuestHib;

			if (e == GameObjectEvent.Interact)
			{
				if (quest != null)
				{
					switch (quest.Step)
					{
						case 1:
							CaptainHib.SayTo(player, "The enemy is out in the frontiers and the battlegrounds. Bring down ten of them and come back to me.");
							break;
						case 2:
							CaptainHib.SayTo(player, "Welcome back, " + player.Name + ". Have you come to [report]?");
							break;
					}
				}
				else
				{
					CaptainHib.SayTo(player, "Well met, " + player.Name + ". I am Captain Eithne of the Druim Ligen garrison. Our scouts tell me Albion and Midgard are thick in the frontier today. " +
					                     "Will you [ride out] and bring ten of them down?");
				}
			}
				// The player whispered to the NPC
			else if (e == GameLivingEvent.WhisperReceive)
			{
				WhisperReceiveEventArgs wArgs = (WhisperReceiveEventArgs) args;
				if (quest == null)
				{
					switch (wArgs.Text)
					{
						case "ride out":
							player.Out.SendQuestSubscribeCommand(CaptainHib, QuestMgr.GetIDForQuestType(typeof(PlayerKillQuestHib)), "Will you undertake " + questTitle + "?");
							break;
					}
				}
				else
				{
					switch (wArgs.Text)
					{
						case "report":
							if (quest.Step == 2)
							{
								player.Out.SendMessage("Well fought. Hibernia is in your debt. Come back on the morrow if you would serve again.", eChatType.CT_Chat, eChatLoc.CL_PopupWindow);
								quest.FinishQuest();
							}
							break;
						case "abort":
							player.Out.SendCustomDialog("Do you really want to abort this quest, \nall items gained during quest will be lost?", new CustomDialogResponse(CheckPlayerAbortQuest));
							break;
					}
				}
			}
		}

		public override string QuestPropertyKey
		{
			get => "PlayerKillQuestHib";
			set { ; }
		}

		public override bool CheckQuestQualification(GamePlayer player)
		{
			// if the player is already doing the quest his level is no longer of relevance
			if (player.IsDoingQuest(typeof (PlayerKillQuestHib)) != null)
				return true;

			// This checks below are only performed is player isn't doing quest already

			//if (player.HasFinishedQuest(typeof(Academy_47)) == 0) return false;

			//if (!CheckPartAccessible(player,typeof(CityOfCamelot)))
			//	return false;

			if (player.Level < minimumLevel || player.Level > maximumLevel)
				return false;

			return true;
		}

		public override void LoadQuestParameters()
		{
			PlayersKilled = GetCustomProperty(QuestPropertyKey) != null ? int.Parse(GetCustomProperty(QuestPropertyKey)) : 0;
		}

		public override void SaveQuestParameters()
		{
			SetCustomProperty(QuestPropertyKey, PlayersKilled.ToString());
		}

		private static void CheckPlayerAbortQuest(GamePlayer player, byte response)
		{
			PlayerKillQuestHib quest = player.IsDoingQuest(typeof (PlayerKillQuestHib)) as PlayerKillQuestHib;

			if (quest == null)
				return;

			if (response == 0x00)
			{
				SendSystemMessage(player, "Good, now go out there and shed some blood!");
			}
			else
			{
				SendSystemMessage(player, "Aborting Quest " + questTitle + ". You can start over again if you want.");
				quest.AbortQuest();
			}
		}

		private static void SubscribeQuest(DOLEvent e, object sender, EventArgs args)
		{
			QuestEventArgs qargs = args as QuestEventArgs;
			if (qargs == null)
				return;

			if (qargs.QuestID != QuestMgr.GetIDForQuestType(typeof(PlayerKillQuestHib)))
				return;

			if (e == GamePlayerEvent.AcceptQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x01);
			else if (e == GamePlayerEvent.DeclineQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x00);
		}

		private static void CheckPlayerAcceptQuest(GamePlayer player, byte response)
		{
			if(CaptainHib.CanGiveQuest(typeof (PlayerKillQuestHib), player)  <= 0)
				return;

			if (player.IsDoingQuest(typeof (PlayerKillQuestHib)) != null)
				return;

			if (response == 0x00)
			{
				player.Out.SendMessage("Another time, then.", eChatType.CT_Say, eChatLoc.CL_PopupWindow);
			}
			else
			{
				//Check if we can add the quest!
				if (!CaptainHib.GiveQuest(typeof (PlayerKillQuestHib), player, 1))
					return;

				CaptainHib.SayTo(player, "Go, then. You will find the enemy in the frontiers and the battlegrounds.");

			}
		}

		//Set quest name
		public override string Name
		{
			get { return questTitle; }
		}

		// Define Steps
		public override string Description
		{
			get
			{
				switch (Step)
				{
					case 1:
						return "Bring down enemy fighters in the frontiers or the battlegrounds. \nEnemies Slain: ("+ PlayersKilled +" | "+ MAX_KILLED +")";
					case 2:
						return "Return to Captain Eithne in Druim Ligen for your reward.";
				}
				return base.Description;
			}
		}

		public override void Notify(DOLEvent e, object sender, EventArgs args)
		{
			GamePlayer player = sender as GamePlayer;

			if (player?.IsDoingQuest(typeof(PlayerKillQuestHib)) == null)
				return;

			if (sender != m_questPlayer)
				return;

			if (e != GameLivingEvent.EnemyKilled || Step != 1) return;
			EnemyKilledEventArgs gArgs = (EnemyKilledEventArgs) args;

			if (gArgs.Target.Realm == 0 || gArgs.Target.Realm == player.Realm || !DOL.GS.Quests.RvrQuestTargets.IsEnemyPlayer(gArgs.Target, player) ||
			    !(player.GetConLevel(gArgs.Target) > MIN_PLAYER_CON)) return;
			PlayersKilled++;
			player.Out.SendMessage("Enemy Slain: (" + PlayersKilled + " | " + MAX_KILLED + ")", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
			player.Out.SendQuestUpdate(this);
					
			if (PlayersKilled >= MAX_KILLED)
			{
				// FinishQuest or go back to the captain
				Step = 2;
			}
		}

		public override void FinishQuest()
		{
			int reward = ServerProperties.Properties.DAILY_RVR_REWARD;
			
			m_questPlayer.GainQuestExperience((m_questPlayer.ExperienceForNextLevel - m_questPlayer.ExperienceForCurrentLevel)/5, Name);
			m_questPlayer.AddMoney(Money.GetMoney(0,0,m_questPlayer.Level,32,Util.Random(50)), "You receive {0} as a reward.");
			AtlasROGManager.GenerateReward(m_questPlayer, 250);
			AtlasROGManager.GenerateJewel(m_questPlayer, (byte)(m_questPlayer.Level + 1), m_questPlayer.Level + Util.Random(5, 15));
			PlayersKilled = 0;
			
			if (reward > 0)
			{
				m_questPlayer.Out.SendMessage($"You have been rewarded {reward} Realmpoints for your service to the realm.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				m_questPlayer.GainRealmPoints(reward, false);
				m_questPlayer.Out.SendUpdatePlayer();
			}
			base.FinishQuest(); //Defined in Quest, changes the state, stores in DB etc ...
			
		}
	}
}
