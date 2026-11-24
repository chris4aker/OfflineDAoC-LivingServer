using System.Linq;

namespace DOL.GS.Commands
{
	/// <summary>
	/// /odd [note]: the player marks the last lines on screen as wrong, at the moment it happens (realism plan step 1, issue
	/// #475). Writes a CHAT_FLAG row to the session chat log (logs/session-chat.log) naming the rows it marks; the transcript
	/// tool (tools/bot-voices/session_transcript.py) counts flags as defects per hour, per channel, per build. Changes nothing
	/// in the game.
	/// </summary>
	[CmdAttribute(
		"&odd",
		ePrivLevel.Player,
		"Marks the last chat lines as odd, for the session chat log",
		"/odd - marks the last few lines you saw as wrong",
		"/odd <note> - the same, with a note saying what was wrong")]
	public class OddCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		/// <summary>How many of the newest rows the player saw one flag marks.</summary>
		public const int Marked = 3;

		public void OnCommand(GameClient client, string[] args)
		{
			GamePlayer player = client?.Player;
			if (player == null)
				return;
			string note = args.Length > 1 ? string.Join(" ", args.Skip(1)).Trim() : string.Empty;
			(long seq, long[] marked) = BotSayGate.Flag(player, note, Marked);
			DisplayMessage(client, marked.Length == 0
				? $"Noted (#{seq}) in the session chat log. No chat lines to mark yet."
				: $"Noted (#{seq}): the last {marked.Length} line{(marked.Length == 1 ? "" : "s")} you saw are marked in the session chat log.");
		}
	}
}
