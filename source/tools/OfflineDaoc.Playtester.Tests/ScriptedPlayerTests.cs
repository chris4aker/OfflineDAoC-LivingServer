using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using OfflineDaoc.Playtester;
using OfflineDaoc.Playtester.Protocol;

namespace OfflineDaoc.Playtester.Tests;

/// <summary>The scripted player against a fake server on a loopback port: what it sends and how it reacts.</summary>
public class ScriptedPlayerTests
{
    /// <summary>A server that reads framed client packets and sends [length][code] packets back.</summary>
    private sealed class FakeServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private TcpClient? _client;
        private NetworkStream? _stream;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public FakeServer() => _listener.Start();

        public async Task AcceptAsync() { _client = await _listener.AcceptTcpClientAsync(); _stream = _client.GetStream(); }

        public async Task<(int Code, byte[] Body)> ReadAsync()
        {
            var head = new byte[10];
            await _stream!.ReadExactlyAsync(head);
            int len = (head[0] << 8) | head[1];
            var rest = new byte[len + 2];
            await _stream.ReadExactlyAsync(rest);
            return (head[9], rest[..len]);
        }

        /// <summary>Reads until a packet with this code arrives (the player also sends pings and positions).</summary>
        public async Task<byte[]> ExpectAsync(int code)
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var (got, body) = await ReadAsync().WaitAsync(cancel.Token);
                if (got == code)
                    return body;
            }
        }

        public void Send(byte code, PacketWriter body)
        {
            byte[] b = body.ToArray();
            var frame = new byte[3 + b.Length];
            frame[0] = (byte)(b.Length >> 8); frame[1] = (byte)b.Length; frame[2] = code;
            b.CopyTo(frame, 3);
            _stream!.Write(frame);
        }

        public void Dispose() { _client?.Close(); _listener.Stop(); }
    }

    private static ScriptedPlayer NewPlayer() => new("test", Stopwatch.StartNew(), (_, _, _) => 0);

    [Test]
    public async Task HandshakeThenLoginIsGranted()
    {
        using var server = new FakeServer();
        await using var player = NewPlayer();
        var accept = server.AcceptAsync();
        await player.ConnectAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(5));
        await accept;
        byte[] crypt = await server.ExpectAsync(0xF4);
        Assert.That(crypt[..4], Is.EqualTo(new byte[] { 0x06, 1, 1, 27 }), "client type and version 1.127");
        var login = player.LoginAsync("playtester", "playtester", TimeSpan.FromSeconds(5));
        byte[] body = await server.ExpectAsync(0xA7);
        var r = new PacketReader(body);
        Assert.That((r.IntPascal(), r.IntPascal()), Is.EqualTo(("playtester", "playtester")));
        var granted = new PacketWriter();
        granted.Pascal("playtester"); granted.Pascal("CoreServer");
        server.Send(0x2A, granted);
        await login;
        Assert.That(player.Account, Is.EqualTo("playtester"));
    }

    [Test]
    public async Task ARefusedLoginSaysWhy()
    {
        using var server = new FakeServer();
        await using var player = NewPlayer();
        var accept = server.AcceptAsync();
        await player.ConnectAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(5));
        await accept;
        var login = player.LoginAsync("playtester", "x", TimeSpan.FromSeconds(5));
        await server.ExpectAsync(0xA7);
        var denied = new PacketWriter();
        denied.Byte(0x0b);
        server.Send(0x2C, denied);
        var e = Assert.ThrowsAsync<PlaytestException>(async () => await login);
        Assert.That(e!.Message, Does.Contain("eLoginError 11"));
    }

    [Test]
    public async Task AnUnknownPlayersPositionMakesItAskForThatPlayer()
    {
        using var server = new FakeServer();
        await using var player = NewPlayer();
        var accept = server.AcceptAsync();
        await player.ConnectAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(5));
        await accept;
        var pos = new PacketWriter();
        pos.FloatLE(1); pos.FloatLE(2); pos.FloatLE(3); pos.FloatLE(0); pos.FloatLE(0);
        pos.Short(9); pos.Short(44); pos.Short(0);
        server.Send(0xA9, pos);
        byte[] ask = await server.ExpectAsync(0xD5);
        Assert.That(ask[0] | (ask[1] << 8), Is.EqualTo(9));
    }

    [Test]
    public async Task ItAnswersTheServersLineOfSightQuestion()
    {
        using var server = new FakeServer();
        await using var player = NewPlayer();
        var accept = server.AcceptAsync();
        await player.ConnectAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(5));
        await accept;
        var los = new PacketWriter();
        los.Short(12); los.Short(34);
        server.Send(0xD0, los);
        byte[] reply = await server.ExpectAsync(0xD0);
        Assert.That(reply[..6], Is.EqualTo(new byte[] { 0, 12, 0, 34, 0x01, 0x00 }));
    }

    [Test]
    public async Task ChatWaitsSeeLinesFromTheMarkOn()
    {
        using var server = new FakeServer();
        await using var player = NewPlayer();
        var accept = server.AcceptAsync();
        await player.ConnectAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(5));
        await accept;
        int mark = player.Mark();
        var msg = new PacketWriter();
        msg.Byte(ChatTypes.Broadcast);
        msg.CString("@@(Aldric) <Faction> lf cleric 1-3");
        server.Send(0xAF, msg);
        var line = await player.WaitForChatAsync(l => ChatTypes.IsFaction(l, out _), TimeSpan.FromSeconds(5), mark);
        Assert.That(line?.Text, Is.EqualTo("(Aldric) <Faction> lf cleric 1-3"));
        Assert.That(await player.WaitForChatAsync(l => l.Text.Contains("never"), TimeSpan.FromMilliseconds(200)), Is.Null);
        Assert.That(player.ChatSnapshot(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AGroupInviteIsAcceptedWithItsOwnDataWords()
    {
        using var server = new FakeServer();
        await using var player = NewPlayer();
        var accept = server.AcceptAsync();
        await player.ConnectAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(5));
        await accept;
        var joined = player.AcceptGroupInviteAsync(TimeSpan.FromSeconds(5), player.Mark());
        var dialog = new PacketWriter();
        dialog.Byte(0); dialog.Byte(0x05); dialog.Short(3); dialog.Short(0); dialog.Short(0); dialog.Short(0);
        dialog.Byte(1); dialog.Byte(1); dialog.CString("Ptlead has invited you to join a group.");
        server.Send(0x81, dialog);
        byte[] answer = await server.ExpectAsync(0x82);
        Assert.That(answer[..8], Is.EqualTo(new byte[] { 0, 3, 0, 0, 0, 0, 0x05, 0x01 }));
        var window = new PacketWriter();
        window.Byte(6); window.Byte(2);
        window.Pascal("Ptlead"); window.Pascal("Fighter"); window.Short(3); window.Byte(1);
        window.Pascal("Ptmemb"); window.Pascal("Fighter"); window.Short(4); window.Byte(1);
        server.Send(0x16, window);
        var members = await joined;
        Assert.That(members.Select(m => m.Name), Is.EqualTo(new[] { "Ptlead", "Ptmemb" }));
    }

    private static void SendNpc(FakeServer server, int oid, string name)
    {
        var npc = new PacketWriter();
        npc.Short((ushort)oid); npc.Short(0); npc.Short(0); npc.Short(100); npc.Int(1000); npc.Int(1000);
        npc.Short(0); npc.Short(0); npc.Byte(50); npc.Byte(8); npc.Byte(0); npc.Byte(0); npc.Byte(0); npc.Byte(0); npc.Short(0);
        npc.Pascal(name); npc.Pascal("");
        server.Send(0xDA, npc);
    }

    [Test]
    public async Task AFightEndsWhenTheTargetDiesAndSaysSoWhenWeDie()
    {
        using var server = new FakeServer();
        await using var player = NewPlayer();
        var accept = server.AcceptAsync();
        await player.ConnectAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(5));
        await accept;
        SendNpc(server, 77, "Hugrath Wormly");
        Assert.That(await player.SeeAsync("Hugrath Wormly", TimeSpan.FromSeconds(5), 0), Is.Not.Null);
        var fight = player.FightAsync(77, TimeSpan.FromSeconds(10));
        await server.ExpectAsync(0xB0);                    // it targets the mob
        var killed = new PacketWriter();
        killed.Byte(0x17);
        killed.CString("@@Hugrath Wormly dies!");
        server.Send(0xAF, killed);
        Assert.That(await fight, Does.Contain("dies"));

        SendNpc(server, 78, "Throatripper");
        await player.SeeAsync("Throatripper", TimeSpan.FromSeconds(5), 0);
        var lost = player.FightAsync(78, TimeSpan.FromSeconds(10));
        var died = new PacketWriter();
        died.Byte(0x15);
        died.CString("@@You have died.  Type /release to return to your last bindpoint.");
        server.Send(0xAF, died);
        var e = Assert.ThrowsAsync<PlaytestException>(async () => await lost);
        Assert.That(e!.Message, Does.Contain("died fighting Throatripper"));
    }
}
