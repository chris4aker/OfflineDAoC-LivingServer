using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The client Join Friend starts: this game's original client (connect.exe, login port 10300). The new client is not
/// supported yet. Hand port from rambodolphin13/DividedRealms 3c6cc9d (the fork's modern 1.130 client is left out).
/// </summary>
public sealed record JoinClient(string ClientDirectory, string Connector, int LoginPort)
{
    public const int ClassicLoginPort = 10300;

    public static JoinClient For(string runtime)
    {
        string official = Path.Combine(runtime, "client-opendaoc", "app");
        string directory = File.Exists(Path.Combine(official, "game.dll")) ? official : Path.Combine(runtime, "client");
        return new JoinClient(directory, Path.Combine(directory, "connect.exe"), ClassicLoginPort);
    }

    public bool IsInstalled => File.Exists(Connector) && File.Exists(Path.Combine(ClientDirectory, "game.dll"));

    public ProcessStartInfo CreateStartInfo(IPAddress host, string account, string password) =>
        new(Connector)
        {
            WorkingDirectory = ClientDirectory,
            UseShellExecute = false,
            ArgumentList = { "game.dll", host.ToString(), account, password },
        };
}

/// <summary>
/// Input checks and the reachability probe behind the Join Friend window. Hand port from rambodolphin13/DividedRealms
/// (3c6cc9d, 6f22a7a, af1fcf8), Living Server 1.2, without the Character passport.
/// </summary>
public static class JoinFriend
{
    /// <summary>The account every install makes for its own player (PortableCredentials). The host already has it.</summary>
    public const string HostAccountName = "offline";

    // A host on Tailscale, the home network or (internet mode) the internet; never this PC or a non-host address.
    public static bool IsAllowedHost(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;
        byte[] b = address.GetAddressBytes();
        return b[0] != 0 && b[0] < 224 && !(b[0] == 169 && b[1] == 254);
    }

    public static bool TryParseHost(string text, out IPAddress address, out string error)
    {
        text = text.Trim();
        if (!IPAddress.TryParse(text, out IPAddress? parsed) || parsed.AddressFamily != AddressFamily.InterNetwork ||
            !string.Equals(parsed.ToString(), text, StringComparison.Ordinal))
        {
            address = IPAddress.None;
            error = "Enter the host's address in dotted form, such as 100.90.12.34.";
            return false;
        }
        if (!IsAllowedHost(parsed))
        {
            address = IPAddress.None;
            error = "Use the host's Tailscale, home-network or internet address, not this PC's own.";
            return false;
        }
        address = parsed;
        error = string.Empty;
        return true;
    }

    /// <summary>What the guest typed: an invite code, or a host address. The hosts come in the order to try them.</summary>
    public static bool TryParseTarget(string text, int clientPort, out IReadOnlyList<IPAddress> hosts, out string error)
    {
        hosts = [];
        if (text.Contains('.'))
        {
            bool parsed = TryParseHost(text, out IPAddress address, out error);
            if (parsed)
                hosts = [address];
            return parsed;
        }
        if (!InviteCode.TryParse(text, out InviteCode.Invite invite))
        {
            error = $"That invite code isn't right. Copy the whole code from your friend, starting with {InviteCode.Prefix}.";
            return false;
        }
        if (invite.Port != clientPort)
        {
            error = $"This code is for a different game (login port {invite.Port}; this one uses {clientPort}). You both need the same game.";
            return false;
        }
        hosts = invite.Addresses.Where(IsAllowedHost).ToList();
        error = hosts.Count == 0 ? "That invite code has no address this PC can use." : string.Empty;
        return hosts.Count > 0;
    }

    /// <summary>The first host that answers on the login port.</summary>
    public static async Task<IPAddress?> FindReachableAsync(IReadOnlyList<IPAddress> hosts, int port, TimeSpan timeoutEach)
    {
        foreach (IPAddress host in hosts)
        {
            if (await CanReachAsync(host, port, timeoutEach))
                return host;
        }
        return null;
    }

    public static bool IsValidAccount(string account) =>
        account.Length is >= 1 and <= 20 && account.All(char.IsAsciiLetterOrDigit);

    /// <summary>
    /// "offline" is the host's own account (every install has one), so a friend who types it, or copies their own
    /// account.txt, would be refused with "wrong password". Join Friend asks for another name instead.
    /// </summary>
    public static bool IsHostAccountName(string account) =>
        string.Equals(account.Trim(), HostAccountName, StringComparison.OrdinalIgnoreCase);

    /// <summary>A name to offer instead: the Windows user name's letters and digits, or "friend" and four digits.</summary>
    public static string SuggestAccountName(string? userName, Random random)
    {
        string letters = new string((userName ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray());
        if (letters.Length > 20)
            letters = letters[..20];
        if (letters.Length >= 3 && !IsHostAccountName(letters))
            return letters;
        return "friend" + random.Next(1000, 10000).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The account name check with its message: empty when the name can be used.</summary>
    public static string AccountProblem(string account, string? userName, Random random)
    {
        if (!IsValidAccount(account))
            return "Your account name must be 1-20 letters or numbers.";
        if (IsHostAccountName(account))
            return $"\"{HostAccountName}\" is your friend's own account. Pick your own name, for example {SuggestAccountName(userName, random)}.";
        return string.Empty;
    }

    // The legacy client sends 1-20 printable ASCII characters.
    public static bool IsValidPassword(string password) =>
        password.Length is >= 1 and <= 20 && password.All(character => character is >= ' ' and <= '~');

    public static async Task<bool> CanReachAsync(IPAddress host, int port, TimeSpan timeout)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            await client.ConnectAsync(host, port, cancel.Token);
            return client.Connected;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }
}
