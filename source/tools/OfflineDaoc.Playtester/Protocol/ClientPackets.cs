// What the scripted player sends: the client -> server packets of game.dll 1.127. Each layout is ported from the new
// client's network client (source/new-client/godot/game/net/DaocClient.cs), which follows the server's own handlers
// (packets/Client/168); the comments name the handler.
namespace OfflineDaoc.Playtester.Protocol;

internal static class ClientPackets
{
    public const byte ClientType = 0x06;                  // Labyrinth of the Minotaur client (every expansion on)
    public static readonly byte[] Version = { 1, 1, 27 };

    /// <summary>A packet as it goes on the wire: [length u16][sequence u16][session u16][parameter u16][code u16] data [checksum].</summary>
    public static byte[] Frame(byte code, byte[] body, ushort sequence, ushort session)
    {
        var buf = new byte[10 + body.Length + 2];
        buf[0] = (byte)(body.Length >> 8);
        buf[1] = (byte)body.Length;
        buf[2] = (byte)(sequence >> 8);
        buf[3] = (byte)sequence;
        buf[4] = (byte)(session >> 8);
        buf[5] = (byte)session;
        buf[9] = code;
        Buffer.BlockCopy(body, 0, buf, 10, body.Length);
        ushort sum = Checksum(buf, 0, buf.Length - 2);
        buf[^2] = (byte)(sum >> 8);
        buf[^1] = (byte)sum;
        return buf;
    }

    /// <summary>PacketProcessor.CalculateChecksum.</summary>
    public static ushort Checksum(byte[] packet, int offset, int size)
    {
        byte val1 = 0x7E, val2 = 0x7E;
        for (int i = offset; i < offset + size; i++)
        {
            val1 += packet[i];
            val2 += val1;
        }
        return (ushort)(val2 - ((val1 + val2) << 8));
    }

    /// <summary>CryptKeyRequestHandler: client type, version 1.1.27, revision letter, build.</summary>
    public static (byte, PacketWriter) CryptKeyRequest()
    {
        var w = new PacketWriter();
        w.Byte(ClientType);
        w.Bytes(Version);
        w.Byte((byte)'a');
        w.Short(0);
        return (0xF4, w);
    }

    /// <summary>LoginRequestHandler (1.126+): account and password, each a little-endian length and its bytes.</summary>
    public static (byte, PacketWriter) Login(string account, string password)
    {
        var w = new PacketWriter();
        w.IntPascal(account);
        w.IntPascal(password);
        return (0xA7, w);
    }

    /// <summary>CharacterOverviewRequestHandler: 0 asks which realm the account may use, 1-3 that realm's characters.</summary>
    public static (byte, PacketWriter) CharacterOverview(int realm)
    {
        var w = new PacketWriter();
        w.Byte((byte)realm);
        return (0xFC, w);
    }

    /// <summary>DuplicateNameCheckRequestHandler: the name in 24 bytes.</summary>
    public static (byte, PacketWriter) CheckName(string name)
    {
        var w = new PacketWriter();
        w.Fixed(name, 24);
        return (0xCB, w);
    }

    /// <summary>CharacterCreateRequestHandler.CreationCharacterData (1.126+), operation 1. The slot counts across realms.</summary>
    public static (byte, PacketWriter) CreateCharacter(int realm, int slot, string name, int classId, int race, int gender, int model, int[] stats)
    {
        var w = new PacketWriter();
        w.Byte((byte)(slot + (realm - 1) * 10));
        w.IntPascal(name);
        w.Bytes(new byte[] { 0x18, 0, 0, 0 });
        w.Byte(1);                                        // CustomMode: the face bytes below are the character's
        for (int i = 0; i < 6; i++) w.Byte(0);            // eye size, lip size, eye colour, hair colour, face, hair style
        w.Bytes(new byte[3]);
        w.Byte(0);                                        // mood
        w.Bytes(new byte[9]);
        w.Byte(1);                                        // operation: create
        w.Byte(0);
        w.Bytes(new byte[2]);
        w.Bytes(new byte[15]);
        w.Byte((byte)classId);
        w.Byte((byte)realm);
        w.Byte((byte)((race & 0x1F) | (gender << 7)));
        w.ShortLE((ushort)model);
        for (int i = 0; i < 8; i++) w.Byte((byte)stats[i]);   // STR DEX CON QUI INT PIE EMP CHA
        w.Bytes(new byte[43]);
        w.Byte((byte)stats[2]);                           // constitution again
        w.Byte(0);
        return (0xFF, w);
    }

    /// <summary>CharacterSelectRequest, RegionListRequest, WorldInitRequest: the three packets that enter the world.</summary>
    public static IEnumerable<(byte, PacketWriter)> EnterWorld(int realm, int slot, string name)
    {
        int index = slot + (realm - 1) * 10;
        var select = new PacketWriter();
        select.Byte((byte)index);
        select.Fixed(name, 28);
        yield return (0x10, select);
        var regions = new PacketWriter();
        regions.Byte((byte)index);
        yield return (0x9D, regions);
        var init = new PacketWriter();
        init.Byte((byte)index);
        yield return (0xD4, init);
    }

    /// <summary>WorldInitRequest after a region change.</summary>
    public static (byte, PacketWriter) WorldInitAgain()
    {
        var w = new PacketWriter();
        w.Byte(0);
        return (0xD4, w);
    }

    public static (byte, PacketWriter) PlayerInit() => (0xE8, new PacketWriter());

    /// <summary>GameOpenRequest: flag 0 = no UDP.</summary>
    public static (byte, PacketWriter) GameOpen()
    {
        var w = new PacketWriter();
        w.Byte(0);
        return (0xBF, w);
    }

    /// <summary>PlayerPositionUpdateHandler.HandlePacketSince1124 (+ the 1.127 object id).</summary>
    /// <param name="targetInView">ActionFlags.TARGET_IN_VIEW (0x30): the server's melee and spells check it on every update
    /// (ProcessActionFlags; without it an attack only says "X is not in view!", first fight here, 2026-11-04)</param>
    public static (byte, PacketWriter) Position(float x, float y, float z, float speed, int session, int objectId, int zone, int heading, int health,
        bool targetInView = false)
    {
        var w = new PacketWriter();
        w.FloatLE(x); w.FloatLE(y); w.FloatLE(z); w.FloatLE(speed); w.FloatLE(0);
        w.Short((ushort)session);
        w.Short((ushort)objectId);
        w.Short((ushort)zone);
        w.Byte(0);                                        // state flags
        w.Byte(0);
        w.Short(0x8000);                                  // on the ground
        w.Short((ushort)(heading & 0xFFF));
        w.Byte((byte)(targetInView ? 0x30 : 0));          // action flags
        w.Bytes(new byte[2]);
        w.Byte((byte)health);
        w.Bytes(new byte[4]);
        return (0xA9, w);
    }

    /// <summary>PlayerCommandHandler (1.127): "&amp;command args" for a slash command, "&amp;say text" for chat.</summary>
    public static (byte, PacketWriter) Command(string text)
    {
        var w = new PacketWriter();
        w.Byte(0);
        w.CString(text);
        return (0xAF, w);
    }

    /// <summary>PlayerTargetHandler: the target's object id (0 = none); 0x6000 = in view.</summary>
    public static (byte, PacketWriter) Target(int objectId, int flags = 0x6000)
    {
        var w = new PacketWriter();
        w.Short((ushort)objectId);
        w.Short((ushort)flags);
        return (0xB0, w);
    }

    /// <summary>ObjectInteractRequestHandler: right-click on an object.</summary>
    public static (byte, PacketWriter) Interact(int objectId, int x, int y, int session)
    {
        var w = new PacketWriter();
        w.Int((uint)x); w.Int((uint)y);
        w.Short((ushort)session);
        w.Short((ushort)objectId);
        w.Short(0); w.Short(0);
        return (0x7A, w);
    }

    /// <summary>PlayerAttackRequestHandler: attack mode on / off.</summary>
    public static (byte, PacketWriter) Attack(bool start)
    {
        var w = new PacketWriter();
        w.Byte((byte)(start ? 1 : 0));
        w.Byte(0);
        w.Short(0);
        return (0x74, w);
    }

    /// <summary>DialogResponseHandler: the data words and code of the dialog back, 1 = yes / OK.</summary>
    public static (byte, PacketWriter) DialogResponse(int data1, int data2, int data3, int code, int response)
    {
        var w = new PacketWriter();
        w.Short((ushort)data1); w.Short((ushort)data2); w.Short((ushort)data3);
        w.Byte((byte)code);
        w.Byte((byte)response);
        w.Short(0);
        return (0x82, w);
    }

    /// <summary>CreateObjectRequestHandler: ask for a living the server updated but never created for us.</summary>
    public static (byte, PacketWriter) RequestObject(int id)
    {
        var w = new PacketWriter();
        w.ShortLE((ushort)id);
        w.Short(0);
        return (0xBE, w);
    }

    /// <summary>PlayerMoveItemRequestHandler: move an item; a slot above 1000 is "give it to object (slot - 1000)".</summary>
    public static (byte, PacketWriter) MoveItem(int session, int fromSlot, int toSlot, int count)
    {
        var w = new PacketWriter();
        w.Short((ushort)session);
        w.Short((ushort)toSlot);
        w.Short((ushort)fromSlot);
        w.Short((ushort)count);
        return (0xDD, w);
    }

    /// <summary>PlayerTrainHandler (0x53): a specialization, by its place in the trainer window's list, to a level.</summary>
    public static (byte, PacketWriter) TrainSpec(int index, int level)
    {
        var w = new PacketWriter();
        w.Int((uint)index);
        for (int i = 1; i < 8; i++) w.Int(0xFFFFFFFF);
        w.Int((uint)level);
        for (int i = 36; i < 64; i++) w.Byte(0);
        for (int i = 0; i < 50; i++) w.Int(0);
        return (0x53, w);
    }

    /// <summary>PlayerBuyRequestHandler: from the targeted merchant, its item at page * 30 + slot.</summary>
    public static (byte, PacketWriter) Buy(int x, int y, int session, int itemSlot, int count, int menu = 0)
    {
        var w = new PacketWriter();
        w.Int((uint)x); w.Int((uint)y);
        w.Short((ushort)session);
        w.Short((ushort)itemSlot);
        w.Byte((byte)count);
        w.Byte((byte)menu);
        return (0x78, w);
    }

    /// <summary>PlayerCreationRequestHandler (CreatePlayerRequest 0xD5, 1.126+ little-endian): ask for a player whose position came but no create.</summary>
    public static (byte, PacketWriter) RequestPlayer(int sessionId)
    {
        var w = new PacketWriter();
        w.ShortLE((ushort)sessionId);
        w.Short(0);
        return (0xD5, w);
    }

    /// <summary>CheckLOSResponse: the client answers the server's line-of-sight question (always "can see" here).</summary>
    public static (byte, PacketWriter) LosReply(int source, int target, bool canSee)
    {
        var w = new PacketWriter();
        w.Short((ushort)source);
        w.Short((ushort)target);
        w.Short((ushort)(canSee ? 0x100 : 0));
        w.Short(0);
        return (0xD0, w);
    }

    /// <summary>PingRequestHandler: 4 skipped bytes, a timestamp, 4 bytes.</summary>
    public static (byte, PacketWriter) Ping(uint timestamp)
    {
        var w = new PacketWriter();
        w.Int(0);
        w.Int(timestamp);
        w.Int(0);
        return (0xA3, w);
    }
}
