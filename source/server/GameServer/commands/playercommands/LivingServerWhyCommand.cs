using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
	/// <summary>
	/// /lswhy: why a gamebot said what it said, or why nobody answered (the chat framework's C0,
	/// docs/bot-voices/CHAT-BUDGETS.md). Reads the decisions AutonomousChatWhy keeps; changes nothing.
	/// </summary>
	[CmdAttribute(
		"&lswhy",
		ePrivLevel.Player,
		"Shows why gamebots said what they said in chat",
		"/lswhy - your target, or the last gamebot that spoke near you or to you",
		"/lswhy <name> - that gamebot's last chat decisions",
		"/lswhy last [count] - the last chat decisions in your realm, the quiet ones too")]
	public class LivingServerWhyCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public const int Shown = 6;

		public void OnCommand(GameClient client, string[] args)
		{
			GamePlayer player = client.Player;
			long now = GameLoop.GameLoopTime;
			string sub = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
			if (sub == "last")
			{
				int count = args.Length > 2 && int.TryParse(args[2], out int asked) ? Math.Clamp(asked, 1, 20) : 8;
				Show(client, $"The last {count} chat decisions in your realm:",
					AutonomousChatWhy.Recent(count, decision => decision.Realm == player.Realm || client.Account.PrivLevel > 1), now);
				return;
			}
			GameBot bot = null;
			string name = null;
			if (sub.Length > 0)
				name = string.Join(" ", args.Skip(1));
			else if (player.TargetObject is GameBot target && target.IsAutonomousWorldBot)
				bot = target;
			if (bot == null && name == null)
			{
				long last = AutonomousChatWhy.LastSpeaker(player.Realm, player.Name);
				bot = last > 0 ? AutonomousBotRegistry.Snapshot().FirstOrDefault(candidate => candidate.DatabaseID == last) : null;
				if (bot == null)
				{
					DisplayMessage(client, "No gamebot has said anything yet. Target one, or use /lswhy last.");
					return;
				}
			}
			if (bot != null && bot.Realm != player.Realm && client.Account.PrivLevel <= 1)
			{
				DisplayMessage(client, "You can only read the chat decisions of your own realm's gamebots.");
				return;
			}
			List<AutonomousChatWhy.Decision> decisions = AutonomousChatWhy.ForBot(bot?.DatabaseID ?? 0, bot?.Name ?? name, Shown);
			if (client.Account.PrivLevel <= 1)
				decisions = decisions.Where(decision => decision.Realm == player.Realm).ToList();
			Show(client, $"Why {bot?.Name ?? name} said what it said (newest first):", decisions, now);
		}

		private static void Show(GameClient client, string title, List<AutonomousChatWhy.Decision> decisions, long now)
		{
			if (decisions.Count == 0)
			{
				client.Out.SendMessage(title + " nothing recorded yet.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}
			client.Out.SendMessage(title, eChatType.CT_System, eChatLoc.CL_SystemWindow);
			foreach (AutonomousChatWhy.Decision decision in decisions)
				client.Out.SendMessage("  " + AutonomousChatWhy.Describe(decision, now), eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}
	}
}
