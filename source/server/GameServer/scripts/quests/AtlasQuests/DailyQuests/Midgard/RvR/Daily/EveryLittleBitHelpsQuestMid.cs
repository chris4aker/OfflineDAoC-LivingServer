using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Cache;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.API;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerTitles;
using DOL.GS.Quests;

namespace DOL.GS.DailyQuest.Midgard
{
	public class EveryLittleBitHelpsQuestMid : Quests.DailyQuest
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		private const string questTitle = "Every Little Bit Helps";
		private const int minimumLevel = 40;
		private const int maximumLevel = 50;

		private static GameNPC CaptainMid = null; // Start NPC

		private int _playersKilledAlb = 0;
		private int _playersKilledHib = 0;
		private const int MAX_KILLGOAL = 5;
		
		// prevent grey killing
		private const int MIN_PLAYER_CON = -3;

		// Constructors
		public EveryLittleBitHelpsQuestMid() : base()
		{
		}

		public EveryLittleBitHelpsQuestMid(GamePlayer questingPlayer) : base(questingPlayer)
		{
		}

		public EveryLittleBitHelpsQuestMid(GamePlayer questingPlayer, int step) : base(questingPlayer, step)
		{
		}

		public EveryLittleBitHelpsQuestMid(GamePlayer questingPlayer, DbQuest dbQuest) : base(questingPlayer, dbQuest)
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

			CaptainMid = DOL.GS.Quests.QuestCaptains.Ensure(eRealm.Midgard, 100); // Captain Hallvard, Svasud Faste

			#endregion

			#region defineItems
			#endregion

			#region defineObject
			#endregion

			GameEventMgr.AddHandler(GamePlayerEvent.AcceptQuest, new DOLEventHandler(SubscribeQuest));
			GameEventMgr.AddHandler(GamePlayerEvent.DeclineQuest, new DOLEventHandler(SubscribeQuest));

			GameEventMgr.AddHandler(CaptainMid, GameObjectEvent.Interact, new DOLEventHandler(TalkToCaptain));
			GameEventMgr.AddHandler(CaptainMid, GameLivingEvent.WhisperReceive, new DOLEventHandler(TalkToCaptain));

			/* Now we bring to the captain the possibility to give this quest to players */
			CaptainMid.AddQuestToGive(typeof (EveryLittleBitHelpsQuestMid));

			if (log.IsInfoEnabled)
				log.Info("Quest \"" + questTitle + "\" Mid initialized");
		}

		[ScriptUnloadedEvent]
		public static void ScriptUnloaded(DOLEvent e, object sender, EventArgs args)
		{
			//if not loaded, don't worry
			if (CaptainMid == null)
				return;
			// remove handlers
			GameEventMgr.RemoveHandler(GamePlayerEvent.AcceptQuest, new DOLEventHandler(SubscribeQuest));
			GameEventMgr.RemoveHandler(GamePlayerEvent.DeclineQuest, new DOLEventHandler(SubscribeQuest));

			GameEventMgr.RemoveHandler(CaptainMid, GameObjectEvent.Interact, new DOLEventHandler(TalkToCaptain));
			GameEventMgr.RemoveHandler(CaptainMid, GameLivingEvent.WhisperReceive, new DOLEventHandler(TalkToCaptain));

			/* Now we remove to the captain the possibility to give this quest to players */
			CaptainMid.RemoveQuestToGive(typeof (EveryLittleBitHelpsQuestMid));
		}

		private static void TalkToCaptain(DOLEvent e, object sender, EventArgs args)
		{
			//We get the player from the event arguments and check if he qualifies		
			GamePlayer player = ((SourceEventArgs) args).Source as GamePlayer;
			if (player == null)
				return;

			if(CaptainMid.CanGiveQuest(typeof (EveryLittleBitHelpsQuestMid), player)  <= 0)
				return;

			//We also check if the player is already doing the quest
			EveryLittleBitHelpsQuestMid quest = player.IsDoingQuest(typeof (EveryLittleBitHelpsQuestMid)) as EveryLittleBitHelpsQuestMid;

			if (e == GameObjectEvent.Interact)
			{
				if (quest != null)
				{
					switch (quest.Step)
					{
						case 1:
							CaptainMid.SayTo(player, "Bring down five fighters of Albion and five of Hibernia in the frontiers, then come back to me.");
							break;
						case 2:
							CaptainMid.SayTo(player, "Welcome back, " + player.Name + ". Have you come to [report]?");
							break;
					}
				}
				else
				{
					CaptainMid.SayTo(player, player.Name + ", our scouts need to know the strength of both enemy realms before they move on our keeps. " +
					                     "Bring down five fighters of Albion and five of Hibernia in the frontiers. Will you take on [both realms]?");
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
						case "both realms":
							player.Out.SendQuestSubscribeCommand(CaptainMid, QuestMgr.GetIDForQuestType(typeof(EveryLittleBitHelpsQuestMid)), "Will you undertake " + questTitle + "?");
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
								player.Out.SendMessage("Well fought. Midgard is in your debt. Come back on the morrow if you would serve again.", eChatType.CT_Chat, eChatLoc.CL_PopupWindow);
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
		
		public override bool CheckQuestQualification(GamePlayer player)
		{
			// if the player is already doing the quest his level is no longer of relevance
			if (player.IsDoingQuest(typeof (EveryLittleBitHelpsQuestMid)) != null)
				return true;

			// This checks below are only performed is player isn't doing quest already

			//if (player.HasFinishedQuest(typeof(Academy_47)) == 0) return false;

			//if (!CheckPartAccessible(player,typeof(CityOfCamelot)))
			//	return false;

			if (player.Level < minimumLevel || player.Level > maximumLevel)
				return false;

			return true;
		}

		private static void CheckPlayerAbortQuest(GamePlayer player, byte response)
		{
			EveryLittleBitHelpsQuestMid quest = player.IsDoingQuest(typeof (EveryLittleBitHelpsQuestMid)) as EveryLittleBitHelpsQuestMid;

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

			if (qargs.QuestID != QuestMgr.GetIDForQuestType(typeof(EveryLittleBitHelpsQuestMid)))
				return;

			if (e == GamePlayerEvent.AcceptQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x01);
			else if (e == GamePlayerEvent.DeclineQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x00);
		}

		private static void CheckPlayerAcceptQuest(GamePlayer player, byte response)
		{
			if(CaptainMid.CanGiveQuest(typeof (EveryLittleBitHelpsQuestMid), player)  <= 0)
				return;

			if (player.IsDoingQuest(typeof (EveryLittleBitHelpsQuestMid)) != null)
				return;

			if (response == 0x00)
			{
				player.Out.SendMessage("Another time, then.", eChatType.CT_Say, eChatLoc.CL_PopupWindow);
			}
			else
			{
				//Check if we can add the quest!
				if (!CaptainMid.GiveQuest(typeof (EveryLittleBitHelpsQuestMid), player, 1))
					return;

				CaptainMid.SayTo(player, "Go, then. You will find the enemy in the frontiers.");

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
						return "Bring down fighters of both enemy realms in the frontiers. \n" +
						       "Hibernians Slain: ("+ _playersKilledHib +" | "+ MAX_KILLGOAL +")\n" +
						       "Albionians Slain: ("+ _playersKilledAlb +" | "+ MAX_KILLGOAL +")";
					case 2:
						return "Return to Captain Hallvard in Svasud Faste for your reward.";
				}
				return base.Description;
			}
		}

		public override void Notify(DOLEvent e, object sender, EventArgs args)
		{
			GamePlayer player = sender as GamePlayer;

			if (player?.IsDoingQuest(typeof(EveryLittleBitHelpsQuestMid)) == null)
				return;

			if (sender != m_questPlayer)
				return;

			if (e != GameLivingEvent.EnemyKilled || Step != 1) return;
			EnemyKilledEventArgs gArgs = (EnemyKilledEventArgs) args;

			if (gArgs.Target.Realm == eRealm.Albion && gArgs.Target.Realm != player.Realm && DOL.GS.Quests.RvrQuestTargets.IsEnemyPlayer(gArgs.Target, player) && player.GetConLevel(gArgs.Target) > MIN_PLAYER_CON && _playersKilledAlb < MAX_KILLGOAL) 
			{
				_playersKilledAlb++;
				player.Out.SendMessage("Albionian Slain: (" + _playersKilledAlb + " | " + MAX_KILLGOAL + ")", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
				player.Out.SendQuestUpdate(this);
			}
			else if (gArgs.Target.Realm == eRealm.Hibernia && gArgs.Target.Realm != player.Realm && DOL.GS.Quests.RvrQuestTargets.IsEnemyPlayer(gArgs.Target, player) && player.GetConLevel(gArgs.Target) > MIN_PLAYER_CON && _playersKilledHib < MAX_KILLGOAL) 
			{
				_playersKilledHib++;
				player.Out.SendMessage("Hibernian Slain: (" + _playersKilledHib + " | " + MAX_KILLGOAL + ")", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
				player.Out.SendQuestUpdate(this);
			}
				
			if (_playersKilledAlb >= MAX_KILLGOAL && _playersKilledHib >= MAX_KILLGOAL)
			{
				// FinishQuest or go back to the captain
				Step = 2;
			}
		}
		
		public override string QuestPropertyKey
		{
			get => "EveryLittleBitHelpsQuestAlb";
			set { ; }
		}
		
		public override void LoadQuestParameters()
		{
			_playersKilledHib = GetCustomProperty("PlayersKilledHib") != null ? int.Parse(GetCustomProperty("PlayersKilledHib")) : 0;
			_playersKilledAlb = GetCustomProperty("PlayersKilledAlb") != null ? int.Parse(GetCustomProperty("PlayersKilledAlb")) : 0;
		}

		public override void SaveQuestParameters()
		{
			SetCustomProperty("PlayersKilledHib", _playersKilledHib.ToString());
			SetCustomProperty("PlayersKilledAlb", _playersKilledAlb.ToString());
		}
		public override void FinishQuest()
		{
			int reward = ServerProperties.Properties.DAILY_RVR_REWARD;
			
			m_questPlayer.GainQuestExperience((m_questPlayer.ExperienceForNextLevel - m_questPlayer.ExperienceForCurrentLevel)/5, Name);
			m_questPlayer.AddMoney(Money.GetMoney(0,0,m_questPlayer.Level,32,Util.Random(50)), "You receive {0} as a reward.");
			AtlasROGManager.GenerateReward(m_questPlayer, 250);
			AtlasROGManager.GenerateJewel(m_questPlayer, (byte)(m_questPlayer.Level + 1), m_questPlayer.Level + Util.Random(5, 15));
			_playersKilledHib = 0;
			_playersKilledAlb = 0;
			
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
