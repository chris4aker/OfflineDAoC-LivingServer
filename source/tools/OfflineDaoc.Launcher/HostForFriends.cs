using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DOL.GS.PacketHandler.Client.v168;
using DOL.Network;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Host for Friends: lets friends on the home network, Tailscale or (internet mode, opt-in) the internet join this game's
/// server. Hand port from rambodolphin13/DividedRealms (3c6cc9d, 6f22a7a, af1fcf8, b0744a2), Living Server 1.2; the
/// Character passport (da78651f) is not part of it.
///
/// The server normally listens on 127.0.0.1 only. Hosting makes it listen on every network (IP and UdpIP 0.0.0.0), so
/// this PC's own client keeps using 127.0.0.1 and friends reach it on the home network or Tailscale address. RegionIP
/// stays 127.0.0.1: the server then sends each client the address that client connected to (RegionAddress.ForClient).
/// Windows Firewall rules (made with administrator approval) admit the local subnet and Tailscale, for this install's
/// CoreServer.exe and the game's two ports only.
///
/// The host password is the gate (the owner's decision, 2026-11-08): hosting always needs one, and the server refuses a
/// new account from another PC without it (HostPassword). This PC needs none. There is no friend list.
///
/// Internet mode adds router port forwarding: the server asks the router over UPnP at start (EnableUPnP) and removes the
/// mapping when it stops. DetectRegionIP stays off; the server keeps the router's internet address as PublicIP and gives
/// it only to clients connecting from the internet. The firewall rules then admit any address.
/// </summary>
public static class HostForFriends
{
    public const string Loopback = "127.0.0.1";
    public const string AllInterfaces = "0.0.0.0";
    public const string FriendRemoteAddresses = "LocalSubnet,100.64.0.0/10";
    public const string BackupFileName = "serverconfig.before-host-for-friends.xml";
    public const string SettingsFileName = "host-for-friends.json";
    public const string FirewallScriptName = "host-for-friends-firewall.cmd";
    public const string FirewallRemoveScriptName = "host-for-friends-firewall-remove.cmd";
    public const int MinHostPasswordLength = 6;
    public const int MaxHostPasswordLength = 20;
    private const string HostPasswordHashSetting = "HostPasswordHash";

    public sealed record State(bool Hosting, int LoginPort, int UdpPort, bool AutoAccountCreation, bool Internet = false, bool PasswordSet = false);

    /// <summary>What the firewall rules were last made for, and their names (STOP HOSTING removes exactly those).</summary>
    public sealed class Settings
    {
        public bool FirewallOpenToInternet { get; set; }
        public List<string> FirewallRules { get; set; } = [];
    }

    public static string ConfigPath(string runtime) => Path.Combine(runtime, "server", "config", "serverconfig.xml");

    public static State Read(string runtime)
    {
        XElement server = LoadServer(ConfigPath(runtime), out _);
        bool hosting = Value(server, "IP") == AllInterfaces;
        return new State(
            Hosting: hosting,
            LoginPort: int.TryParse(Value(server, "Port"), out int port) ? port : 10300,
            UdpPort: int.TryParse(Value(server, "UdpPort"), out int udp) ? udp : 10400,
            AutoAccountCreation: !string.Equals(Value(server, "AutoAccountCreation"), "False", StringComparison.OrdinalIgnoreCase),
            Internet: hosting && string.Equals(Value(server, "EnableUPnP"), "True", StringComparison.OrdinalIgnoreCase),
            PasswordSet: !string.IsNullOrEmpty(Value(server, HostPasswordHashSetting)));
    }

    /// <summary>
    /// Turns hosting on with the host password, in one write: listen on every network, router closed (UPnP off unless
    /// <paramref name="internet"/>), the region address left on loopback, automatic account creation on. The first time,
    /// the player's serverconfig.xml is copied to <see cref="BackupFileName"/>.
    /// </summary>
    public static void Enable(string runtime, string password, bool internet = false)
    {
        if (!TryCheckHostPassword(password, out string error))
            throw new ArgumentException(error, nameof(password));

        string path = ConfigPath(runtime);
        XElement server = LoadServer(path, out XDocument document);
        string backup = Path.Combine(Path.GetDirectoryName(path)!, BackupFileName);
        if (Value(server, "IP") != AllInterfaces)
            File.Copy(path, backup, overwrite: true);
        Set(server, "IP", AllInterfaces);
        Set(server, "UdpIP", AllInterfaces);
        Set(server, "EnableUPnP", internet ? "True" : "False");
        Set(server, "DetectRegionIP", "False");
        // Friends get their account on their first login, with the host password.
        Set(server, "AutoAccountCreation", "True");
        Set(server, HostPasswordHashSetting, HashHostPassword(password));
        if (internet && server.Element("PublicIP") == null)
            Set(server, "PublicIP", "auto");
        Save(document, path);
    }

    /// <summary>Back to this PC only: loopback, UPnP off, no host password. Other settings are kept.</summary>
    public static void Disable(string runtime)
    {
        string path = ConfigPath(runtime);
        XElement server = LoadServer(path, out XDocument document);
        Set(server, "IP", Loopback);
        Set(server, "UdpIP", Loopback);
        Set(server, "EnableUPnP", "False");
        Set(server, HostPasswordHashSetting, string.Empty);
        Save(document, path);
    }

    /// <summary>The same hash as the server's HostPassword.Hash (the launcher compiles that file).</summary>
    public static string HashHostPassword(string password) => HostPassword.Hash(password);

    /// <summary>A host password is always needed: 6-20 printable ASCII characters (what the legacy client can send).</summary>
    public static bool TryCheckHostPassword(string password, out string error)
    {
        error = string.Empty;
        if (password.Length is >= MinHostPasswordLength and <= MaxHostPasswordLength && JoinFriend.IsValidPassword(password))
            return true;
        error = password.Length == 0
            ? "Choose a password for your friends. Without one, nobody else can make an account in your world."
            : $"The password must be {MinHostPasswordLength}-{MaxHostPasswordLength} printable ASCII characters (letters, digits, symbols; no accents).";
        return false;
    }

    /// <summary>A PublicIP typed into serverconfig.xml, for a router that was forwarded by hand.</summary>
    public static IPAddress? ManualPublicAddress(string runtime)
    {
        string? value = Value(LoadServer(ConfigPath(runtime), out _), "PublicIP");
        return IPAddress.TryParse(value, out IPAddress? address) && IsInternet(address) ? address : null;
    }

    /// <summary>
    /// The addresses an invite code carries, in the order a friend tries them. Tailscale and internet addresses go
    /// before the home network one, which could be some other device on the friend's own network.
    /// </summary>
    public static IReadOnlyList<IPAddress> InviteAddresses(IPAddress? internet) => InviteAddresses(internet, LocalAddresses());

    public static IReadOnlyList<IPAddress> InviteAddresses(IPAddress? internet, IReadOnlyList<LocalAddress> local)
    {
        var usable = local.Where(entry => JoinFriend.IsAllowedHost(entry.Address)).ToList();
        var addresses = new List<IPAddress>(usable.Select(entry => entry.Address).Where(IsTailscale).Take(1));
        if (internet != null)
            addresses.Add(internet);
        // Adapters with a gateway first, so not WSL or Hyper-V; one without beats no address at all.
        addresses.AddRange(usable
            .Where(entry => !IsInternet(entry.Address) && !IsTailscale(entry.Address))
            .OrderByDescending(entry => entry.HasGateway)
            .Select(entry => entry.Address)
            .Take(1));
        return addresses.Distinct().ToList();
    }

    /// <summary>
    /// An internet address held by this PC itself, as when the modem is in bridge mode and no router sits in front.
    /// Friends reach it directly, with no UPnP or port forwarding.
    /// </summary>
    public static IPAddress? DirectInternetAddress() => DirectInternetAddress(LocalAddresses());

    public static IPAddress? DirectInternetAddress(IReadOnlyList<LocalAddress> local) =>
        local.FirstOrDefault(entry => entry.HasGateway && IsInternet(entry.Address))?.Address;

    public static string SettingsPath(string runtime) => Path.Combine(runtime, SettingsFileName);

    public static Settings LoadSettings(string runtime)
    {
        try
        {
            string path = SettingsPath(runtime);
            return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings() : new Settings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    public static void SaveSettings(string runtime, Settings settings) =>
        File.WriteAllText(SettingsPath(runtime), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>True for a public internet address; false for loopback, private, link-local and shared (CGNAT, Tailscale) ones.</summary>
    public static bool IsInternet(IPAddress address) => NetworkTrust.IsInternet(address);

    public static bool IsTailscale(IPAddress address) => NetworkTrust.IsTailscale(address);

    public sealed record RouterCheck(bool Usable, IPAddress? InternetAddress, string Message);

    /// <summary>Asks the router over UPnP for its internet address, as the server does at start. Blocks a few seconds.</summary>
    public static RouterCheck CheckRouter(State state)
    {
        try
        {
            var nat = new UpnpNat();
            if (!nat.Discover())
                return new RouterCheck(false, null, NoUpnp(state));
            IPAddress external = nat.GetExternalIP();
            if (!IsInternet(external))
                return new RouterCheck(false, external,
                    $"Your router's internet address is {external}: your provider shares addresses between customers (CGNAT), " +
                    "or another router sits in front of yours. Friends can't reach you this way; use Tailscale.");
            return new RouterCheck(true, external,
                $"Router OK (UPnP). Friends join at  {external}. The server opens TCP {state.LoginPort} and UDP {state.UdpPort} " +
                $"on the router while it runs, to {nat.LocalAddress}, and closes them when it stops.");
        }
        catch (Exception exception) when (exception is SocketException or WebException or IOException or HttpRequestException or
                                          System.Xml.XmlException or InvalidOperationException or FormatException or NullReferenceException)
        {
            return new RouterCheck(false, null, NoUpnp(state));
        }
    }

    private static string NoUpnp(State state) =>
        "Your router did not answer UPnP (it may be off in the router's settings). Turn UPnP on, or forward " +
        $"TCP {state.LoginPort} and UDP {state.UdpPort} to this PC by hand and set PublicIP in server\\config\\serverconfig.xml " +
        "to your internet address.";

    /// <summary>An IPv4 address of this PC; HasGateway is false on virtual adapters (WSL, Hyper-V).</summary>
    public sealed record LocalAddress(IPAddress Address, bool HasGateway);

    /// <summary>This PC's IPv4 addresses on interfaces that are up, loopback left out.</summary>
    public static IReadOnlyList<LocalAddress> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                                  network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(network =>
                {
                    IPInterfaceProperties properties = network.GetIPProperties();
                    bool gateway = properties.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                    return properties.UnicastAddresses
                        .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address))
                        .Select(unicast => new LocalAddress(unicast.Address, gateway));
                })
                .DistinctBy(entry => entry.Address)
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    public static string FirewallRuleName(string protocol, int port) => $"Offline DAoC friends {protocol} {port}";

    /// <summary>
    /// The game's two ports, and the Passport Desk's (login port + <see cref="OfflineDaoc.Passport.PassportDeskClient.PortOffset"/>),
    /// which listens only while hosting. Before the audit of 2026-11-13 the Desk's port was left closed, so a friend's
    /// passport could not reach it through Windows Firewall.
    /// </summary>
    private static (string Protocol, int Port)[] FirewallPorts(State state) =>
        [("TCP", state.LoginPort), ("UDP", state.UdpPort), ("TCP", state.LoginPort + OfflineDaoc.Passport.PassportDeskClient.PortOffset)];

    public static IReadOnlyList<string> FirewallRuleNames(State state) =>
        FirewallPorts(state).Select(port => FirewallRuleName(port.Protocol, port.Port)).ToList();

    /// <summary>
    /// The netsh lines that admit friends (or, with <paramref name="add"/> false, remove those rules). Rules are tied to
    /// this install's CoreServer.exe and the game's two ports, so Windows does not show its own (unrestricted) "allow
    /// access" prompt when the server first listens on the network. Home network and Tailscale only, unless internet mode.
    /// </summary>
    public static string FirewallScript(string runtime, State state, bool add, bool openToInternet = false)
    {
        string remote = openToInternet ? "any" : FriendRemoteAddresses;
        string program = Path.Combine(runtime, "server", "CoreServer.exe");
        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine(add
            ? "rem Offline DAoC Living Server: let friends reach this game's server."
            : "rem Offline DAoC Living Server: remove the Host for Friends firewall rules.");
        foreach (var (protocol, port) in FirewallPorts(state))
        {
            string name = FirewallRuleName(protocol, port);
            script.AppendLine($"netsh advfirewall firewall delete rule name=\"{name}\" >nul 2>&1");
            if (add)
                script.AppendLine($"netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={protocol} " +
                                  $"localport={port} remoteip={remote} program=\"{program}\" profile=any");
        }
        return script.ToString();
    }

    /// <summary>
    /// The rules STOP HOSTING removes: the names recorded when START HOSTING made them, or, for rules made before the
    /// names were recorded, the names this launcher gives them. Only names this launcher creates are ever listed.
    /// </summary>
    public static IReadOnlyList<string> RulesToRemove(State state, Settings settings)
    {
        var ours = new HashSet<string>(FirewallRuleNames(state), StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> recorded = settings.FirewallRules.Where(name => name.StartsWith("Offline DAoC friends ", StringComparison.Ordinal));
        List<string> names = recorded.Concat(ours).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names;
    }

    /// <summary>The netsh lines that delete exactly these rules, by name.</summary>
    public static string FirewallRemoveScript(IEnumerable<string> names)
    {
        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("rem Offline DAoC Living Server: remove the Host for Friends firewall rules (by name, nothing else).");
        script.AppendLine("set failed=0");
        foreach (string name in names)
        {
            if (name.Contains('"') || !name.StartsWith("Offline DAoC friends ", StringComparison.Ordinal))
                continue;
            script.AppendLine($"netsh advfirewall firewall show rule name=\"{name}\" >nul 2>&1 && (netsh advfirewall firewall delete rule name=\"{name}\" >nul || set failed=1)");
        }
        script.AppendLine("exit /b %failed%");
        return script.ToString();
    }

    private static XElement LoadServer(string path, out XDocument document)
    {
        document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        return document.Root?.Element("Server") ?? throw new InvalidDataException("serverconfig.xml has no <Server> section.");
    }

    private static string? Value(XElement server, string name) => server.Element(name)?.Value.Trim();

    private static void Set(XElement server, string name, string value)
    {
        XElement? element = server.Element(name);
        if (element != null)
        {
            element.Value = value;
            return;
        }
        // A new setting goes on its own line after the last one, with the same indentation.
        XElement? last = server.Elements().LastOrDefault();
        if (last?.PreviousNode is XText { Value: var indent } && string.IsNullOrWhiteSpace(indent))
            last.AddAfterSelf(new XText(indent), new XElement(name, value));
        else
            server.Add(new XElement(name, value));
    }

    private static void Save(XDocument document, string path)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            document.Save(temporary, SaveOptions.DisableFormatting);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
