// Byte helpers for the DAoC 1.127 protocol. Ported from the new client's network client
// (source/new-client/godot/game/net/DaocClient.cs, Writer / Reader, Offline DAoC Rebuilt), without Godot.
using System.Text;

namespace OfflineDaoc.Playtester.Protocol;

internal static class Text1252
{
    public static readonly Encoding Encoding;

    static Text1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding = Encoding.GetEncoding(1252);
    }
}

/// <summary>Builds a packet body: big-endian by default, as the server's PacketIn reads it.</summary>
internal sealed class PacketWriter
{
    private readonly List<byte> _b = new();
    public int Length => _b.Count;
    public void Byte(byte v) => _b.Add(v);
    public void Bytes(byte[] v) => _b.AddRange(v);
    public void Short(ushort v) { _b.Add((byte)(v >> 8)); _b.Add((byte)v); }
    public void ShortLE(ushort v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); }
    public void Int(uint v) { _b.Add((byte)(v >> 24)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 8)); _b.Add((byte)v); }
    public void IntLE(uint v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 24)); }
    public void IntPascal(string s) { var b = Text1252.Encoding.GetBytes(s ?? ""); IntLE((uint)b.Length); _b.AddRange(b); }
    public void Pascal(string s)
    {
        var b = Text1252.Encoding.GetBytes(s ?? "");
        int n = Math.Min(255, b.Length);
        Byte((byte)n);
        _b.AddRange(b[..n]);
    }
    public void Fixed(string s, int len)
    {
        var b = Text1252.Encoding.GetBytes(s ?? "");
        for (int i = 0; i < len; i++) _b.Add(i < b.Length ? b[i] : (byte)0);
    }
    public void CString(string s) { _b.AddRange(Text1252.Encoding.GetBytes(s ?? "")); _b.Add(0); }
    public void FloatLE(float v)
    {
        var b = BitConverter.GetBytes(v);
        if (!BitConverter.IsLittleEndian) Array.Reverse(b);
        _b.AddRange(b);
    }
    public byte[] ToArray() => _b.ToArray();
}

/// <summary>Reads a packet body; reading past the end gives zeros, as the new client's reader does.</summary>
internal sealed class PacketReader
{
    private readonly byte[] _d;
    private int _p;
    public PacketReader(byte[] d) { _d = d; }
    public int Left => _d.Length - _p;
    public int At(int offset) => _p + offset < _d.Length ? _d[_p + offset] : 0;
    public byte Byte() => _p < _d.Length ? _d[_p++] : (byte)0;
    public void Skip(int n) => _p = Math.Min(_d.Length, _p + n);
    public int Short() => (Byte() << 8) | Byte();
    public int ShortLE() => Byte() | (Byte() << 8);
    public uint Int() => ((uint)Byte() << 24) | ((uint)Byte() << 16) | ((uint)Byte() << 8) | Byte();
    public uint IntLE() => Byte() | ((uint)Byte() << 8) | ((uint)Byte() << 16) | ((uint)Byte() << 24);
    public float FloatLE() => BitConverter.Int32BitsToSingle((int)IntLE());
    public ulong LongLE() { ulong lo = IntLE(); ulong hi = IntLE(); return lo | (hi << 32); }
    public string Pascal() => Str(Byte());
    /// <summary>A little-endian length and its bytes; the overview's strings carry their NUL in the length.</summary>
    public string IntPascal() => Str((int)IntLE()).TrimEnd('\0');
    public string FixedRaw(int len) => Str(len);
    public string Fixed(int len) { var s = Str(len); int z = s.IndexOf('\0'); return z >= 0 ? s[..z] : s; }
    public string CString()
    {
        int s = _p;
        while (_p < _d.Length && _d[_p] != 0) _p++;
        var t = Text1252.Encoding.GetString(_d, s, _p - s);
        if (_p < _d.Length) _p++;
        return t;
    }
    private string Str(int n)
    {
        n = Math.Max(0, Math.Min(n, Left));
        var s = Text1252.Encoding.GetString(_d, _p, n);
        _p += n;
        return s;
    }
}
