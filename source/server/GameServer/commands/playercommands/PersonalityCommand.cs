using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
	/// <summary>Shows a targeted gamebot's personality (docs/BOT-PERSONALITIES.md).</summary>
	[CmdAttribute(
		"&personality",
		ePrivLevel.Player,
		"Shows the personality of the gamebot you have targeted",
		"/personality (with a gamebot targeted)")]
	public class PersonalityCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public void OnCommand(GameClient client, string[] args)
		{
			if (client.Player.TargetObject is not GameBot bot || !bot.IsAutonomousWorldBot || bot.DatabaseID <= 0)
			{
				DisplayMessage(client, "Target a gamebot first.");
				return;
			}
			if (bot.Realm != client.Player.Realm && client.Account.PrivLevel <= 1)
			{
				DisplayMessage(client, "You can only read the personality of your own realm's gamebots.");
				return;
			}
			string state = AutonomousBotPersonality.Enabled ? string.Empty : " (personalities are switched off, so it doesn't act on it)";
			string voice = AutonomousBotVoices.Corpus?.VoiceFor(bot.DatabaseID).Name;
			string voiceText = voice == null ? string.Empty : $"; writes {voice}{(AutonomousBotVoices.Enabled ? string.Empty : " (voices off)")}";
			string line = bot.IsHiredCompanion
				? HiredCompanionText(bot.Name, AutonomousBotPersonality.Describe(AutonomousBotPersonality.For(bot)), voice,
					CompanionChat165.IsFormal(bot), AutonomousBotVoices.Enabled)
				: $"{bot.Name}: {AutonomousBotPersonality.Describe(AutonomousBotPersonality.For(bot))}{state}{voiceText}";
			client.Out.SendMessage(line, eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}

		/// <summary>
		/// What /personality says of a hired companion (chat audit TEXT-005; owner 2026-11-18 09:14: a companion has the same chat
		/// as any gamebot). Its personality colours what it says; it follows its player, so it steers no town breaks or camps.
		/// Pure.
		/// </summary>
		public static string HiredCompanionText(string name, string personality, string voice, bool formal, bool voicesOn) =>
			$"{name} (hired companion): {personality}, in what they say; they follow you, so it steers no town breaks or camps; " +
			(formal ? "talks formally (their Speech option)" : voice == null ? "writes plain" : $"writes {voice}{(voicesOn ? string.Empty : " (voices off)")}");
	}
}
