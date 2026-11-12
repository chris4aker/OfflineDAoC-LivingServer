// The socket to the server: frames and sends client packets, reads server packets on a background thread. Ported from
// the new client's DaocClient.cs (Open / Send / ReadLoop): server -> client is [length u16 BE][code u8] data, unencrypted
// (the server has client encryption off unless the client sends a key).
using System.Net.Sockets;
using System.Threading.Channels;

namespace OfflineDaoc.Playtester.Protocol;

internal sealed class DaocConnection : IDisposable
{
    private readonly TcpClient _tcp = new() { NoDelay = true };
    private NetworkStream? _stream;
    private readonly object _sendLock = new();
    private ushort _sequence;
    private readonly Channel<(int Code, byte[] Data)> _inbox = Channel.CreateUnbounded<(int, byte[])>();

    public ushort SessionId { get; set; }
    public bool IsOpen => _tcp.Connected;
    public ChannelReader<(int Code, byte[] Data)> Inbox => _inbox.Reader;

    public async Task OpenAsync(string host, int port, CancellationToken cancel)
    {
        await _tcp.ConnectAsync(host, port, cancel);
        _stream = _tcp.GetStream();
        _ = Task.Run(ReadLoopAsync);
        var (code, body) = ClientPackets.CryptKeyRequest();
        Send(code, body);
    }

    public void Send(byte code, PacketWriter body)
    {
        if (_stream == null)
            throw new InvalidOperationException("not connected");
        lock (_sendLock)
        {
            byte[] frame = ClientPackets.Frame(code, body.ToArray(), _sequence++, SessionId);
            _stream.Write(frame, 0, frame.Length);
        }
    }

    public void Send((byte Code, PacketWriter Body) packet) => Send(packet.Code, packet.Body);

    private async Task ReadLoopAsync()
    {
        var head = new byte[3];
        string why = "the server closed the connection";
        try
        {
            while (true)
            {
                if (!await ReadExactlyAsync(head))
                    break;
                int len = (head[0] << 8) | head[1];
                var data = new byte[len];
                if (len > 0 && !await ReadExactlyAsync(data))
                    break;
                await _inbox.Writer.WriteAsync((head[2], data));
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            why = e.Message;
        }
        _inbox.Writer.TryWrite((-1, Text1252.Encoding.GetBytes(why)));
        _inbox.Writer.TryComplete();
    }

    private async Task<bool> ReadExactlyAsync(byte[] buf)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = await _stream!.ReadAsync(buf.AsMemory(got, buf.Length - got));
            if (n <= 0)
                return false;
            got += n;
        }
        return true;
    }

    public void Dispose()
    {
        try { _tcp.Close(); } catch (Exception) { }
    }
}
