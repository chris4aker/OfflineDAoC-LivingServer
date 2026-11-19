using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.API;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerTitles;
using DOL.GS.Quests;

namespace DOL.GS.DailyQuest.Albion
{
	public class CaleKeepCaptureAlb : Quests.DailyQuest
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		private const string questTitle = "Seize the Keep in Caledonia";
		private const int minimumLevel = 30;
		private const int maximumLevel = 35;

		// Capture Goal
		private const int MAX_CAPTURED = 1;
		
		private static GameNPC CaptainAlb = null; // Start NPC

		private int _isCaptured = 0;

		// Constructors
		public CaleKeepCaptureAlb() : base()
		{
		}

		public CaleKeepCaptureAlb(GamePlayer questingPlayer) : base(questingPlayer, 1)
		{
		}

		public CaleKeepCaptureAlb(GamePlayer questingPlayer, int step) : base(questingPlayer, step)
		{
		}

		public CaleKeepCaptureAlb(GamePlayer questingPlayer, DbQuest dbQuest) : base(questingPlayer, dbQuest)
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

			CaptainAlb = DOL.GS.Quests.QuestCaptains.Ensure(eRealm.Albion, 250); // Captain Aldwyn, the Caledonia portal keep

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
			CaptainAlb.AddQuestToGive(typeof (CaleKeepCaptureAlb));

			if (log.IsInfoEnabled)
				log.Info("Quest \"" + questTitle + "\" initialized");
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
			CaptainAlb.RemoveQuestToGive(typeof (CaleKeepCaptureAlb));
		}

		private static void TalkToCaptain(DOLEvent e, object sender, EventArgs args)
		{
			//We get the player from the event arguments and check if he qualifies		
			GamePlayer player = ((SourceEventArgs) args).Source as GamePlayer;
			if (player == null)
				return;

			if(CaptainAlb.CanGiveQuest(typeof (CaleKeepCaptureAlb), player)  <= 0)
				return;

			//We also check if the player is already doing the quest
			CaleKeepCaptureAlb quest = player.IsDoingQuest(typeof (CaleKeepCaptureAlb)) as CaleKeepCaptureAlb;

			if (e == GameObjectEvent.Interact)
			{
				if (quest != null)
				{
					switch (quest.Step)
					{
						case 1:
							CaptainAlb.SayTo(player, "Find a keep held by the enemy and capture it. Come back to me when it is done.");
							break;
						case 2:
							CaptainAlb.SayTo(player, "Welcome back, " + player.Name + ". Have you come to [report]?");
							break;
					}
				}
				else
				{
					CaptainAlb.SayTo(player, player.Name + ", the keep at the heart of Caledonia is the prize of this field, and while the enemy holds it, the field is theirs. " +
					                      "Take it for Albion and I will see you rewarded. Will you [take the keep]?");
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
						case "take the keep":
							player.Out.SendQuestSubscribeCommand(CaptainAlb, QuestMgr.GetIDForQuestType(typeof(CaleKeepCaptureAlb)), "Will you undertake " + questTitle + "?");
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
								player.Out.SendMessage("Well done! The keep is ours, and Albion holds the field. Come back on the morrow if you would serve again.", eChatType.CT_Chat, eChatLoc.CL_PopupWindow);
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
			if (player.IsDoingQuest(typeof (CaleKeepCaptureAlb)) != null)
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
			CaleKeepCaptureAlb quest = player.IsDoingQuest(typeof (CaleKeepCaptureAlb)) as CaleKeepCaptureAlb;

			if (quest == null)
				return;

			if (response == 0x00)
			{
				SendSystemMessage(player, "Good, now go out there and finish your work!");
			}
			else
			{
				SendSystemMessage(player, "Aborting Quest " + questTitle + ". You can start over again if you want.");
				quest.AbortQuest();
			}
		}

		protected static void SubscribeQuest(DOLEvent e, object sender, EventArgs args)
		{
			QuestEventArgs qargs = args as QuestEventArgs;
			if (qargs == null)
				return;

			if (qargs.QuestID != QuestMgr.GetIDForQuestType(typeof(CaleKeepCaptureAlb)))
				return;

			if (e == GamePlayerEvent.AcceptQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x01);
			else if (e == GamePlayerEvent.DeclineQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x00);
		}

		private static void CheckPlayerAcceptQuest(GamePlayer player, byte response)
		{
			if(CaptainAlb.CanGiveQuest(typeof (CaleKeepCaptureAlb), player)  <= 0)
				return;

			if (player.IsDoingQuest(typeof (CaleKeepCaptureAlb)) != null)
				return;

			if (response == 0x00)
			{
				player.Out.SendMessage("Another time, then.", eChatType.CT_Say, eChatLoc.CL_PopupWindow);
			}
			else
			{
				//Check if we can add the quest!
				if (!CaptainAlb.GiveQuest(typeof (CaleKeepCaptureAlb), player, 1))
					return;

				CaptainAlb.SayTo(player, "Thank you, " + player.Name + ". You are a true soldier of Albion!");

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
						return "Go to the battlefield and capture a keep. \nKeeps Captured: ("+ _isCaptured +" | 1)";
					case 2:
						return "Return to Captain Aldwyn in the Caledonia portal keep for your reward.";
				}
				return base.Description;
			}
		}

		public override void Notify(DOLEvent e, object sender, EventArgs args)
		{
			GamePlayer player = sender as GamePlayer;

			if (player?.IsDoingQuest(typeof(CaleKeepCaptureAlb)) == null)
				return;
			
			if (sender != m_questPlayer)
				return;

			if (Step != 1 || e != GamePlayerEvent.CapturedKeepsChanged) return;
			_isCaptured = 1;
			player.Out.SendMessage("Keep Captured: ("+_isCaptured+" | "+MAX_CAPTURED+")", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
			player.Out.SendQuestUpdate(this);
					
			if (_isCaptured >= MAX_CAPTURED)
			{
				// FinishQuest or go back to the captain
				Step = 2;
			}

		}
		
		public override string QuestPropertyKey
		{
			get => "CaleKeepCaptureAlb";
			set { ; }
		}
		
		public override void LoadQuestParameters()
		{
			
		}

		public override void SaveQuestParameters()
		{
			
		}

		public override void AbortQuest()
		{
			base.AbortQuest(); //Defined in Quest, changes the state, stores in DB etc ...
		}

		public override void FinishQuest()
		{
			if (m_questPlayer.Inventory.IsSlotsFree(1, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
			{
				m_questPlayer.GainQuestExperience((m_questPlayer.ExperienceForNextLevel - m_questPlayer.ExperienceForCurrentLevel) / 2, Name);
				m_questPlayer.AddMoney(Money.GetMoney(0, 0, m_questPlayer.Level * 2, 0, Util.Random(50)),
					"You receive {0} as a reward.");
				AtlasROGManager.GenerateBattlegroundToken(m_questPlayer, 1);
				_isCaptured = 0;
				base.FinishQuest(); //Defined in Quest, changes the state, stores in DB etc ...
			}
			else
			{
				m_questPlayer.Out.SendMessage("Clear one slot of your inventory for your reward", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
		}
	}
}
