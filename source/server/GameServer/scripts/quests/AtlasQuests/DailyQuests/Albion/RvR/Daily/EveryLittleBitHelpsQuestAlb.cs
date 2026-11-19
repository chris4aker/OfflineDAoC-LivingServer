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

namespace DOL.GS.DailyQuest.Albion
{
	public class EveryLittleBitHelpsQuestAlb : Quests.DailyQuest
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		private const string questTitle = "Every Little Bit Helps";
		private const int minimumLevel = 40;
		private const int maximumLevel = 50;

		private static GameNPC CaptainAlb = null; // Start NPC

		private int _playersKilledMid = 0;
		private int _playersKilledHib = 0;
		private const int MAX_KILLGOAL = 5;
		
		// prevent grey killing
		protected const int MIN_PLAYER_CON = -3;

		// Constructors
		public EveryLittleBitHelpsQuestAlb() : base()
		{
		}

		public EveryLittleBitHelpsQuestAlb(GamePlayer questingPlayer) : base(questingPlayer)
		{
		}

		public EveryLittleBitHelpsQuestAlb(GamePlayer questingPlayer, int step) : base(questingPlayer, step)
		{
		}

		public EveryLittleBitHelpsQuestAlb(GamePlayer questingPlayer, DbQuest dbQuest) : base(questingPlayer, dbQuest)
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

			CaptainAlb = DOL.GS.Quests.QuestCaptains.Ensure(eRealm.Albion, 1); // Captain Edric, Castle Sauvage

			#endregion

			#region defineItems
			#endregion

			#region defineObject
			#endregion

			GameEventMgr.AddHandler(GamePlayerEvent.AcceptQuest, new DOLEventHandler(SubscribeQuest));
			GameEventMgr.AddHandler(GamePlayerEvent.DeclineQuest, new DOLEventHandler(SubscribeQuest));

			GameEventMgr.AddHandler(CaptainAlb, GameObjectEvent.Interact, new DOLEventHandler(TalkToCaptain));
			GameEventMgr.AddHandler(CaptainAlb, GameLivingEvent.WhisperReceive, new DOLEventHandler(TalkToCaptain));

			/* Now we bring to the captain the possibility to give this quest to players */
			CaptainAlb.AddQuestToGive(typeof (EveryLittleBitHelpsQuestAlb));

			if (log.IsInfoEnabled)
				log.Info("Quest \"" + questTitle + "\" Alb initialized");
		}

		[ScriptUnloadedEvent]
		public static void ScriptUnloaded(DOLEvent e, object sender, EventArgs args)
		{
			//if not loaded, don't worry
			if (CaptainAlb == null)
				return;
			// remove handlers
			GameEventMgr.RemoveHandler(GamePlayerEvent.AcceptQuest, new DOLEventHandler(SubscribeQuest));
			GameEventMgr.RemoveHandler(GamePlayerEvent.DeclineQuest, new DOLEventHandler(SubscribeQuest));

			GameEventMgr.RemoveHandler(CaptainAlb, GameObjectEvent.Interact, new DOLEventHandler(TalkToCaptain));
			GameEventMgr.RemoveHandler(CaptainAlb, GameLivingEvent.WhisperReceive, new DOLEventHandler(TalkToCaptain));

			/* Now we remove to the captain the possibility to give this quest to players */
			CaptainAlb.RemoveQuestToGive(typeof (EveryLittleBitHelpsQuestAlb));
		}

		private static void TalkToCaptain(DOLEvent e, object sender, EventArgs args)
		{
			//We get the player from the event arguments and check if he qualifies		
			GamePlayer player = ((SourceEventArgs) args).Source as GamePlayer;
			if (player == null)
				return;

			if(CaptainAlb.CanGiveQuest(typeof (EveryLittleBitHelpsQuestAlb), player)  <= 0)
				return;

			//We also check if the player is already doing the quest
			EveryLittleBitHelpsQuestAlb quest = player.IsDoingQuest(typeof (EveryLittleBitHelpsQuestAlb)) as EveryLittleBitHelpsQuestAlb;

			if (e == GameObjectEvent.Interact)
			{
				if (quest != null)
				{
					switch (quest.Step)
					{
						case 1:
							CaptainAlb.SayTo(player, "Bring down five fighters of Midgard and five of Hibernia in the frontiers, then come back to me.");
							break;
						case 2:
							CaptainAlb.SayTo(player, "Welcome back, " + player.Name + ". Have you come to [report]?");
							break;
					}
				}
				else
				{
					CaptainAlb.SayTo(player, player.Name + ", our scouts need to know the strength of both enemy realms before they move on our keeps. " +
					                     "Bring down five fighters of Midgard and five of Hibernia in the frontiers. Will you take on [both realms]?");
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
							player.Out.SendQuestSubscribeCommand(CaptainAlb, QuestMgr.GetIDForQuestType(typeof(EveryLittleBitHelpsQuestAlb)), "Will you undertake " + questTitle + "?");
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
								player.Out.SendMessage("Well fought. Albion is in your debt. Come back on the morrow if you would serve again.", eChatType.CT_Chat, eChatLoc.CL_PopupWindow);
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
			if (player.IsDoingQuest(typeof (EveryLittleBitHelpsQuestAlb)) != null)
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
			EveryLittleBitHelpsQuestAlb quest = player.IsDoingQuest(typeof (EveryLittleBitHelpsQuestAlb)) as EveryLittleBitHelpsQuestAlb;

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

			if (qargs.QuestID != QuestMgr.GetIDForQuestType(typeof(EveryLittleBitHelpsQuestAlb)))
				return;

			if (e == GamePlayerEvent.AcceptQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x01);
			else if (e == GamePlayerEvent.DeclineQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x00);
		}

		private static void CheckPlayerAcceptQuest(GamePlayer player, byte response)
		{
			if(CaptainAlb.CanGiveQuest(typeof (EveryLittleBitHelpsQuestAlb), player)  <= 0)
				return;

			if (player.IsDoingQuest(typeof (EveryLittleBitHelpsQuestAlb)) != null)
				return;

			if (response == 0x00)
			{
				player.Out.SendMessage("Another time, then.", eChatType.CT_Say, eChatLoc.CL_PopupWindow);
			}
			else
			{
				//Check if we can add the quest!
				if (!CaptainAlb.GiveQuest(typeof (EveryLittleBitHelpsQuestAlb), player, 1))
					return;

				CaptainAlb.SayTo(player, "Go, then. You will find the enemy in the frontiers.");

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
						       "Midgardians Slain: ("+ _playersKilledMid +" | "+ MAX_KILLGOAL +")";
					case 2:
						return "Return to Captain Edric in Castle Sauvage for your reward.";
				}
				return base.Description;
			}
		}

		public override void Notify(DOLEvent e, object sender, EventArgs args)
		{
			GamePlayer player = sender as GamePlayer;

			if (player == null || player.IsDoingQuest(typeof(EveryLittleBitHelpsQuestAlb)) == null)
				return;

			if (sender != m_questPlayer)
				return;

			if (e != GameLivingEvent.EnemyKilled || Step != 1) return;
			EnemyKilledEventArgs gArgs = (EnemyKilledEventArgs) args;

			if (gArgs.Target.Realm == eRealm.Midgard && gArgs.Target.Realm != player.Realm && DOL.GS.Quests.RvrQuestTargets.IsEnemyPlayer(gArgs.Target, player) && player.GetConLevel(gArgs.Target) > MIN_PLAYER_CON && _playersKilledMid < MAX_KILLGOAL) 
			{
				_playersKilledMid++;
				player.Out.SendMessage("Midgardian Slain: (" + _playersKilledMid + " | " + MAX_KILLGOAL + ")", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
				player.Out.SendQuestUpdate(this);
			}
			else if (gArgs.Target.Realm == eRealm.Hibernia && gArgs.Target.Realm != player.Realm && DOL.GS.Quests.RvrQuestTargets.IsEnemyPlayer(gArgs.Target, player) && player.GetConLevel(gArgs.Target) > MIN_PLAYER_CON && _playersKilledHib < MAX_KILLGOAL) 
			{
				_playersKilledHib++;
				player.Out.SendMessage("Hibernian Slain: (" + _playersKilledHib + " | " + MAX_KILLGOAL + ")", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
				player.Out.SendQuestUpdate(this);
			}
				
			if (_playersKilledMid >= MAX_KILLGOAL && _playersKilledHib >= MAX_KILLGOAL)
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
			_playersKilledMid = GetCustomProperty("PlayersKilledMid") != null ? int.Parse(GetCustomProperty("PlayersKilledMid")) : 0;
		}

		public override void SaveQuestParameters()
		{
			SetCustomProperty("PlayersKilledHib", _playersKilledHib.ToString());
			SetCustomProperty("PlayersKilledMid", _playersKilledMid.ToString());
		}
		
		public override void FinishQuest()
		{
			int reward = ServerProperties.Properties.DAILY_RVR_REWARD;
			
			m_questPlayer.GainQuestExperience((m_questPlayer.ExperienceForNextLevel - m_questPlayer.ExperienceForCurrentLevel)/5, Name);
			m_questPlayer.AddMoney(Money.GetMoney(0,0,m_questPlayer.Level,32,Util.Random(50)), "You receive {0} as a reward.");
			AtlasROGManager.GenerateReward(m_questPlayer, 250);
			AtlasROGManager.GenerateJewel(m_questPlayer, (byte)(m_questPlayer.Level + 1), m_questPlayer.Level + Util.Random(5, 15));
			_playersKilledHib = 0;
			_playersKilledMid = 0;
			
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
