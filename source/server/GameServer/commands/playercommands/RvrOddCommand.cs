namespace DOL.GS.Commands
{
	/// <summary>
	/// /rvrodd [note]: the player flags a bad moment in the realm war (issue #505). Writes an RVR_ODD row to the server log
	/// with the nearest fight id and siege id, for source/tools/rvr_hour.py. Changes nothing in the game. When #475's /odd
	/// lands it calls <see cref="RvrOdd.Flag"/> too, and this command can go.
	/// </summary>
	[CmdAttribute(
		"&rvrodd",
		ePrivLevel.Player,
		"Marks this moment of the realm war as odd, for the server log",
		"/rvrodd - marks the fight or siege around you as wrong",
		"/rvrodd <note> - the same, with a note saying what was wrong")]
	public class RvrOddCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public void OnCommand(GameClient client, string[] args)
		{
			GamePlayer player = client?.Player;
			if (player == null)
				return;
			string note = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1).Trim() : string.Empty;
			(long seq, long fight, string siege) = RvrOdd.Flag(player, note);
			DisplayMessage(client, $"Noted (#{seq}) in the server log" +
				(fight > 0 ? $", fight {fight}" : string.Empty) + (siege != "none" ? $", near {siege}" : string.Empty) + ".");
		}
	}
}
