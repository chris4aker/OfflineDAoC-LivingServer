using System;
using System.Collections;
using DOL.GS.Commands;
using DOL.GS.Keeps;
using DOL.Network;

namespace DOL.GS.PacketHandler
{
    [PacketLib(1127, GameClient.eClientVersion.Version1127)]
    public class PacketLib1127 : PacketLib1126
    {
        public PacketLib1127(GameClient client) : base(client) { }

        /// 1127 login granted packet unchanged, work around for server type
        public override void SendLoginGranted(byte color)
        {
            // work around for character screen bugs when server type sent as 00 but player doesnt have a realm
            // 0x07 allows for characters in all realms
            using (var pak = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init(GetPacketCode(eServerPackets.LoginGranted)))
            {
                pak.WritePascalString(m_gameClient.Account.Name);
                pak.WritePascalString(GameServer.Instance.Configuration.ServerNameShort); //server name
                pak.WriteByte(0x05); //Server ID, seems irrelevant
                var type = color == 0 ? 7 : color;
                pak.WriteByte((byte)type); // 00 normal type?, 01 mordred type, 03 gaheris type, 07 ywain type
                pak.WriteByte(0x00); // Trial switch 0x00 - subbed, 0x01 - trial acc
                SendTCP(pak);
            }
        }

        public override void SendMessage(string msg, eChatType type, eChatLoc loc)
        {
            SnoopManager.CheckAndBroadcast(m_gameClient.Player, msg, type, loc);
            if (m_gameClient.ClientState is not GameClient.eClientState.CharScreen)
                ScreenBursts.Note(m_gameClient.Player, msg, type);   // issue #475: counts system-window bursts, changes nothing
            SendRawMessage(msg, type, loc);
        }

        public override void SendRawMessage(string msg, eChatType type, eChatLoc loc)
        {
            if (m_gameClient.ClientState is GameClient.eClientState.CharScreen)
                return;

            var pak = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init(GetPacketCode(eServerPackets.Message));
            pak.WriteByte((byte) type);

            // The @@ prefix seems to be technically needed only for the send reply feature.
            // Otherwise the client able to print to the correct window based on eChatType.
            // We're keeping it here in case something else still needs it (more research needed).
            if (loc is eChatLoc.CL_ChatWindow)
                pak.WriteNonNullTerminatedString("@@");
            else if (loc is eChatLoc.CL_PopupWindow)
                pak.WriteNonNullTerminatedString("##");

            pak.WriteString(msg);
            SendTCP(pak);
        }

        /// <summary>
        /// The keep hookpoint store (guards, siege engines, oil). Port of OpenDAoC-Core 476059682 and d15534653: the 1.127
        /// client reads a different header (count first, little-endian ids) and the 1.125+ merchant row, so the 1.70 layout
        /// showed it a broken store.
        /// </summary>
        public override void SendHookPointStore(GameKeepHookPoint hookPoint)
        {
            using (var pak = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init(GetPacketCode(eServerPackets.KeepComponentHookpointStore)))
            {
                WriteHookPointStore(pak, hookPoint.Component.Keep.KeepID, hookPoint.Component.ID, hookPoint.ID,
                    HookPointInventory.GetInventory(hookPoint.ID).GetAllItems());
                SendTCP(pak);
            }
        }

        /// <summary>The client shows at most 30 (0x1e) hookpoint items.</summary>
        public const int MAX_HOOKPOINT_STORE_ITEMS = 30;

        /// <summary>
        /// The 1.127 hookpoint store body: count, a reference byte, three 1s, keep, component and hookpoint ids (little
        /// endian), then one merchant-style row per item: slot, the item's flag (high byte, low byte), four zero bytes
        /// (speed, hand, damage and object type, usable), weight 0, price in gold, icon and name.
        /// </summary>
        public static void WriteHookPointStore(PacketOut pak, int keepId, int componentId, int hookPointId, IList items)
        {
            int count = Math.Min(items.Count, MAX_HOOKPOINT_STORE_ITEMS);

            pak.WriteByte((byte) count);
            pak.WriteByte(0); // unknown / reference
            pak.WriteByte(1);
            pak.WriteByte(1);
            pak.WriteByte(1);
            pak.WriteShortLowEndian((ushort) keepId);
            pak.WriteShortLowEndian((ushort) componentId);
            pak.WriteShortLowEndian((ushort) hookPointId);

            for (int i = 0; i < count; i++)
            {
                HookPointItem item = (HookPointItem) items[i];
                pak.WriteByte((byte) i);
                pak.WriteByte((byte) (item.Flag >> 8)); // the merchant row's level field
                pak.WriteByte((byte) (item.Flag & 0xFF)); // the merchant row's value1 field
                pak.WriteByte(0); // speed / absorption
                pak.WriteByte(0); // hand
                pak.WriteByte(0); // damage type and object type
                pak.WriteByte(0); // 0 = can be bought
                pak.WriteShortLowEndian(0); // weight
                pak.WriteIntLowEndian((uint) item.Gold);
                pak.WriteShortLowEndian(item.Icon);
                pak.WritePascalStringIntLE(item.Name);
            }
        }
    }
}
