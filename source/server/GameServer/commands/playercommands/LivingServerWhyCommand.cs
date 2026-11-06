using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
	/// <summary>
	/// /lswhy: why a gamebot said what it said, or why nobody answered (the chat framework's C0,
	/// docs/bot-voices/CHAT-BUDGETS.md), and what has the gamebot's time (its duty lease and the last claims on it,
	/// docs/DUTY-LEASE.md). Reads the decisions AutonomousChatWhy and BotDutyLeases keep; changes nothing.
	/// </summary>
	[CmdAttribute(
		"&lswhy",
		ePrivLevel.Player,
		"Shows why gamebots said what they said in chat",
		"/lswhy - your target, or the last gamebot that spoke near you or to you",
		"/lswhy <name> - that gamebot's last chat decisions",
		"/lswhy last [count] - the last chat decisions in your realm, the quiet ones too",
		"/lswhy me - why bots answered your own lines, or didn't",
		"/lswhy held [count] - the last replies the reply budget held back in your realm",
		"/lswhy budget - the chat budget around you: each channel's floor, target, ceiling and lines a minute now",
		"/lswhy duty [name] - what has that gamebot's time (your target without a name): its duty lease and the last claims on it")]
	public class LivingServerWhyCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public const int Shown = 6;
		/// <summary>Claim decisions shown under a bot's chat decisions (all of /lswhy duty shows <see cref="Shown"/>).</summary>
		public const int DutyShown = 3;

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
			if (sub == "budget")
			{
				Budget(client, player, now);
				return;
			}
			if (sub == "duty")
			{
				Duty(client, player, args.Length > 2 ? string.Join(" ", args.Skip(2)) : null, now);
				return;
			}
			// 1.2: your own lines, with each answer's or silence's reason code.
			if (sub == "me")
			{
				Show(client, "Why bots answered your lines, or didn't (newest first):", AutonomousChatWhy.ForPlayer(player.Name, 8), now);
				return;
			}
			if (sub == "held")
			{
				int count = args.Length > 2 && int.TryParse(args[2], out int asked) ? Math.Clamp(asked, 1, 20) : 8;
				Show(client, $"The last {count} replies held back by the reply budget in your realm:",
					AutonomousChatWhy.Recent(count, decision => decision.Outcome == "held" &&
						(decision.Realm == player.Realm || client.Account.PrivLevel > 1)), now);
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
			if (bot == null && name != null)
				AutonomousBotRegistry.TryGetByName(name, out bot);
			if (bot == null || bot.Realm == player.Realm || client.Account.PrivLevel > 1)
				ShowDuty(client, player, bot, bot?.Name ?? name, now, DutyShown);
		}

		private void Duty(GameClient client, GamePlayer player, string name, long now)
		{
			GameBot bot = null;
			if (name == null)
				bot = player.TargetObject as GameBot;
			else
				AutonomousBotRegistry.TryGetByName(name, out bot);
			if (bot is { IsAutonomousWorldBot: false })
			{
				DisplayMessage(client, $"{bot.Name} is a companion: its time is its owner's, it holds no duty lease.");
				return;
			}
			if (bot == null && name == null)
			{
				DisplayMessage(client, "Target a gamebot, or use /lswhy duty <name>.");
				return;
			}
			if (bot != null && bot.Realm != player.Realm && client.Account.PrivLevel <= 1)
			{
				DisplayMessage(client, "You can only read the duties of your own realm's gamebots.");
				return;
			}
			ShowDuty(client, player, bot, bot?.Name ?? name, now, Shown);
		}

		private static void ShowDuty(GameClient client, GamePlayer player, GameBot bot, string name, long now, int count)
		{
			List<string> lines = BotDutyLeases.WhyLines(bot, name, now, count, client.Account.PrivLevel > 1 ? eRealm.None : player.Realm);
			client.Out.SendMessage($"What has {name}'s time: {lines[0]}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			foreach (string line in lines.Skip(1))
				client.Out.SendMessage(line, eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}

		private static void Budget(GameClient client, GamePlayer player, long now)
		{
			int bots = AutonomousBotChatCoordinator.RealmBots(player.Realm);
			client.Out.SendMessage($"Chat budget for {GlobalConstants.RealmToName(player.Realm)}, {bots} gamebots online, hour {DateTime.Now.Hour} (lines a minute):",
				eChatType.CT_System, eChatLoc.CL_SystemWindow);
			void Line(string label, AutonomousChatBudget.Channel channel, long key, int count)
			{
				var band = AutonomousChatBudget.LiveBand(channel, count);
				var replies = AutonomousChatBudget.LiveReplyBand(channel, count);
				// 1.2: the ambient lines and the replies are counted apart where a channel budgets its replies.
				double measured = replies.Ceiling > 0
					? AutonomousChatBudget.Meter.AmbientPerMinute(channel, key, now, band.Target)
					: AutonomousChatBudget.Meter.PerMinute(channel, key, now, AutonomousChatBudget.LineKind.Ambient, AutonomousChatBudget.WindowMs(band.Target));
				string text = $"  {label}: {measured:0.00} now; floor {band.Floor:0.00}, target {band.Target:0.00}, ceiling {band.Ceiling:0.00}" +
					$" (held back: {AutonomousChatWhy.HeldCount(channel, AutonomousChatWhy.Held.Gap)} gap, {AutonomousChatWhy.HeldCount(channel, AutonomousChatWhy.Held.Ceiling)} ceiling)";
				if (replies.Ceiling > 0)
					text += $"; replies {AutonomousChatBudget.Meter.RepliesPerMinute(channel, key, now, replies.Target):0.00} now, target {replies.Target:0.00}, " +
						$"ceiling {replies.Ceiling:0.00} (held back: {AutonomousChatWhy.HeldCount(channel, AutonomousChatWhy.Held.ReplyTarget)} target, " +
						$"{AutonomousChatWhy.HeldCount(channel, AutonomousChatWhy.Held.ReplyCeiling)} ceiling; let through {AutonomousChatWhy.RepliesAllowed(channel)})";
				client.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
			Line("/faction", AutonomousChatBudget.Channel.Faction, (long)player.Realm, bots);
			Line("/lfg", AutonomousChatBudget.Channel.Lfg, (long)player.Realm, bots);
			Line("/trade", AutonomousChatBudget.Channel.Trade, (long)player.Realm, bots);
			Line("/say here", AutonomousChatBudget.Channel.Say, player.CurrentRegionID, bots);
			bool capital = AutonomousBotVoices.IsCapitalRegion(player.CurrentRegionID);
			Line(capital ? "/broadcast in this capital" : "/broadcast in this town", capital ? AutonomousChatBudget.Channel.Capital : AutonomousChatBudget.Channel.Town,
				((long)player.CurrentRegionID << 16) | (player.CurrentZone?.ID ?? 0), bots);
			if (player.Guild != null)
				Line("your guild", AutonomousChatBudget.Channel.Guild, AutonomousBotGuildSocial.KeyOf(player.Guild), player.Guild.GetOnlineBotMembers().Length + 1);
			if (player.Group != null)
				Line("your group", AutonomousChatBudget.Channel.Group, AutonomousBotGroupChatter.KeyOf(player.Group), 0);
			client.Out.SendMessage($"  A reply to you needs a fit of {AutonomousChatBudget.MinReplyFit} (4 known, 3 specific, 2 fits, 1 weak). " +
				"A tell, or a line that names a bot, is always answered.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
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
