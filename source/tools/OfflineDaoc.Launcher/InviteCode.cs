using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace OfflineDaoc.Launcher;

/// <summary>
/// An invite code: the host's addresses and login port as text a friend can paste into Join Friend. It needs no
/// server or lookup; the code is the address. It holds no password, and a new one is needed if the address changes.
/// Layout: version, port (2 bytes), 1-3 IPv4 addresses, then a 2-byte check so a damaged code is refused.
/// Hand port from rambodolphin13/DividedRealms af1fcf8; Living Server's codes start with LS (the fork's with DR).
/// </summary>
public static class InviteCode
{
    public const int MaxAddresses = 3;
    public const string Prefix = "LS";
    private const byte Version = 1;
    private const int CheckLength = 2;
    private const int HeaderLength = 3;

    // Crockford base 32: no I, L, O or U, so a code survives being read out or retyped.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public sealed record Invite(IReadOnlyList<IPAddress> Addresses, int Port);

    public static string Create(IEnumerable<IPAddress> addresses, int port)
    {
        List<IPAddress> usable = addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork).Distinct().Take(MaxAddresses).ToList();
        if (usable.Count == 0)
            throw new ArgumentException("An invite needs at least one IPv4 address.", nameof(addresses));
        if (port is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(port));

        var body = new List<byte> { Version, (byte) (port >> 8), (byte) port };
        foreach (IPAddress address in usable)
            body.AddRange(address.GetAddressBytes());
        body.AddRange(Check(body.ToArray()));

        string text = Encode(body.ToArray());
        var groups = Enumerable.Range(0, (text.Length + 3) / 4).Select(index => text.Substring(index * 4, Math.Min(4, text.Length - index * 4)));
        return Prefix + "-" + string.Join("-", groups);
    }

    public static bool TryParse(string text, out Invite invite)
    {
        invite = new Invite([], 0);
        string compact = new string(text.Where(character => !char.IsWhiteSpace(character) && character != '-').ToArray()).ToUpperInvariant();
        if (!compact.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        if (!TryDecode(compact[Prefix.Length..], out byte[] bytes))
            return false;

        int addressBytes = bytes.Length - HeaderLength - CheckLength;
        if (addressBytes <= 0 || addressBytes % 4 != 0 || addressBytes / 4 > MaxAddresses || bytes[0] != Version)
            return false;
        if (!Check(bytes[..^CheckLength]).SequenceEqual(bytes[^CheckLength..]))
            return false;

        var addresses = new List<IPAddress>();
        for (int offset = HeaderLength; offset < HeaderLength + addressBytes; offset += 4)
            addresses.Add(new IPAddress(bytes[offset..(offset + 4)]));
        invite = new Invite(addresses, bytes[1] << 8 | bytes[2]);
        return true;
    }

    private static byte[] Check(byte[] body) => SHA256.HashData(body)[..CheckLength];

    private static string Encode(byte[] bytes)
    {
        var text = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte value in bytes)
        {
            buffer = (buffer << 8 | value) & 0xFFFF;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                text.Append(Alphabet[buffer >> bits & 31]);
            }
        }
        if (bits > 0)
            text.Append(Alphabet[buffer << (5 - bits) & 31]);
        return text.ToString();
    }

    private static bool TryDecode(string text, out byte[] bytes)
    {
        var decoded = new List<byte>();
        bytes = [];
        int buffer = 0, bits = 0;
        foreach (char character in text)
        {
            int value = Alphabet.IndexOf(Normalize(character));
            if (value < 0)
                return false;
            buffer = (buffer << 5 | value) & 0xFFFF;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                decoded.Add((byte) (buffer >> bits));
            }
        }
        // Leftover bits are padding: anything else is a retyped last letter the check would not cover.
        if (bits >= 5 || (buffer & ((1 << bits) - 1)) != 0)
            return false;
        bytes = decoded.ToArray();
        return true;
    }

    // The letters people mistype for digits.
    private static char Normalize(char character) => character switch
    {
        'O' => '0',
        'I' or 'L' => '1',
        _ => character,
    };
}
