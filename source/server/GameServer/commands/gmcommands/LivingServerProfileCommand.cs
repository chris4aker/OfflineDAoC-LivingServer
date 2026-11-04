using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    // /lsprofile, Living Server 1.2: the last minute of the AI profiler (AiProfile), in game. It only reads the numbers the
    // profiler already keeps; it changes nothing.
    [CmdAttribute(
        "&lsprofile",
        ePrivLevel.GM,
        "Show the AI profiler's last minute: tick percentiles, serial and brain time, lock waits and the costliest AI systems",
        "/lsprofile - the last minute's tick numbers and the ten costliest systems",
        "/lsprofile all - every system that ran in the last minute",
        "/lsprofile line - the AI_PROFILE log line as written")]
    public sealed class LivingServerProfileCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            string sub = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            if (!AiProfile.Enabled)
            {
                Say(client, "The AI profiler is off. Turn the server property ai_profile (autonomous_population) on; " +
                    "the first minute is ready a minute later.");
                return;
            }

            AiProfileMinute minute = AiProfile.LastMinute;
            if (minute == null)
            {
                Say(client, "The AI profiler is on, but no minute has finished yet. Try again in a minute.");
                return;
            }

            if (sub == "line")
            {
                Say(client, minute.SummaryLine());
                return;
            }

            int count = sub == "all" ? minute.Systems.Length : AiProfileMinute.TopCount;
            foreach (string line in minute.ChatLines(count))
                Say(client, line);
            Say(client, $"Every minute is also in the server log (AI_PROFILE) and in logs/{AiProfileCsv.FileName}.");
        }

        private static void Say(GameClient client, string text) =>
            client.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }
}
