using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OfflineDaoc.Launcher;

// Whether the game server is listening on its port. The system's listener table is the fast path. On some Windows
// builds GetActiveTcpListeners() leaves out a listener that accepts connections (issue #32, 2026-10-30: the server
// was up on 127.0.0.1:10300, Test-NetConnection succeeded, and the launcher stayed on STARTING), so when the table
// says no, a short loopback connect decides. A closed port answers at once, so the probe costs nothing while the
// server is down; the timeout only matters when nothing answers at all.
public static class ServerPortProbe
{
    public const int GameServerPort = 10300;
    public const int ConnectTimeoutMilliseconds = 150;

    public const int StuckServerMinutes = 3;

    /// <summary>
    /// The launcher footer's hint when this game's CoreServer has run for minutes without answering on the game port (a
    /// Reddit report 2026-11-04: "stuck at Starting…, Enter Realm greyed out; yesterday it worked"). A normal start answers
    /// within about a minute, so after <see cref="StuckServerMinutes"/> it is most likely a server left from an earlier
    /// session or one stuck while loading. STOP SERVER already offers to force-close a server this launcher didn't start.
    /// </summary>
    public static string? StuckServerHint(TimeSpan? processAge) =>
        processAge is { } age && age >= TimeSpan.FromMinutes(StuckServerMinutes)
            ? $"The server has been starting for {Math.Floor(age.TotalMinutes):0} minutes without answering (a server left from an earlier session, " +
              "or one stuck while loading). Click STOP SERVER to end it, then START SERVER. The end of runtime\\logs\\server-console.log says where it stopped."
            : null;

    public static bool IsListening(int port = GameServerPort) =>
        ListedByTable(port) || ConnectSucceeds(port, ConnectTimeoutMilliseconds);

    // The decision, kept apart from the two system calls so it can be tested.
    public static bool Decide(bool listedByTable, Func<bool> connectSucceeds) =>
        listedByTable || connectSucceeds();

    public static bool ListedByTable(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    public static bool ConnectSucceeds(int port, int timeoutMilliseconds)
    {
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            var connect = client.ConnectAsync(IPAddress.Loopback, port);
            return connect.Wait(timeoutMilliseconds) && client.Connected;
        }
        catch (Exception)
        {
            // A refused or timed-out connect, or an aggregate of one: not listening.
            return false;
        }
    }
}
