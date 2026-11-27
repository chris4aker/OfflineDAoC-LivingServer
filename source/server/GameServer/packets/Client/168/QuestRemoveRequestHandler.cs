namespace DOL.GS.PacketHandler.Client.v168
{
    [PacketHandlerAttribute(PacketHandlerType.TCP, eClientPackets.RemoveQuestRequest, "Quest Remove request Handler.", eClientStatus.PlayerInGame)]
    public class QuestRemoveRequestHandler : PacketHandler
    {
        protected override void HandlePacketInternal(GameClient client, GSPacketIn packet)
        {
            _ = packet.ReadShort();
            ushort questIndex = packet.ReadShort();
            _ = packet.ReadShort();
            _ = packet.ReadShort();

            // Port of OpenDAoC-Core 22197f1f6: looked up under the quest list lock, aborted outside it.
            client.Player.GetQuestByIndex(questIndex)?.AbortQuest();
        }
    }
}
