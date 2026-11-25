using System.Reflection;
using DOL.Logging;

namespace DOL.GS.PacketHandler.Client.v168
{
    [PacketHandler(PacketHandlerType.TCP, eClientPackets.CreatePlayerRequest, "Handles requests for players(0x7C) in game", eClientStatus.PlayerInGame)]
    public class PlayerCreationRequestHandler : PacketHandler
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        protected override void HandlePacketInternal(GameClient client, GSPacketIn packet)
        {
            ushort id = client.Version >= GameClient.eClientVersion.Version1126 ? packet.ReadShortLowEndian() : packet.ReadShort();
            GameClient target = ClientService.Instance.GetClientBySessionId(id);

            if (target == null)
            {
                if (Log.IsWarnEnabled)
                    Log.Warn($"Client {client.SessionID}:{client.TcpEndpointAddress} account {(client.Account == null ? "null" : client.Account.Name)} requested invalid client {id}");

                // Uncomment if this is spammed, but try not to disconnect if id == 0.
                // client.Disconnect();
                return;
            }

            if (!target.IsPlaying || target.Player == null || target.Player.ObjectState != GameObject.eObjectState.Active)
                return;

            // Port of OpenDAoC-Core 446a99f1f: a client could ask for any player by session ID and so see a stealthed or
            // far-away player (Play with friends).
            GamePlayer playerTarget = target.Player;
            GamePlayer player = client.Player;

            if (player == null || !player.CanDetect(playerTarget) || !player.IsWithinRadius(playerTarget, WorldMgr.VISIBILITY_DISTANCE))
                return;

            client.Out.SendPlayerCreate(playerTarget);
            client.Out.SendLivingEquipmentUpdate(playerTarget);
        }
    }
}
