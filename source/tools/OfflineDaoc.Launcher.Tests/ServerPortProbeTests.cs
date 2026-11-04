using System.Net;
using System.Net.Sockets;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

// Issue #32 (2026-10-30): the launcher stayed on STARTING because GetActiveTcpListeners() left out a listener that
// accepted connections. The probe falls back to a loopback connect.
[TestFixture]
public class ServerPortProbeTests
{
    // Reddit 2026-11-04: "stuck at Starting…, Enter Realm greyed out; yesterday it worked".
    [Test]
    public void AServerThatHasntAnsweredForMinutesGetsAHint()
    {
        Assert.That(ServerPortProbe.StuckServerHint(null), Is.Null, "no server process");
        Assert.That(ServerPortProbe.StuckServerHint(TimeSpan.FromSeconds(50)), Is.Null, "a normal start");
        Assert.That(ServerPortProbe.StuckServerHint(TimeSpan.FromMinutes(2.9)), Is.Null);
        string? hint = ServerPortProbe.StuckServerHint(TimeSpan.FromMinutes(14.6));
        Assert.That(hint, Does.Contain("14 minutes").And.Contain("STOP SERVER").And.Contain("server-console.log"));
    }

    [Test]
    public void TheTableAloneIsEnough()
    {
        bool probed = false;
        Assert.That(ServerPortProbe.Decide(true, () => { probed = true; return false; }), Is.True);
        Assert.That(probed, Is.False, "no connect when the table already lists the port");
    }

    [Test]
    public void AConnectDecidesWhenTheTableSaysNo()
    {
        Assert.That(ServerPortProbe.Decide(false, () => true), Is.True);
        Assert.That(ServerPortProbe.Decide(false, () => false), Is.False);
    }

    [Test]
    public void AListeningLoopbackPortIsFoundByTheConnect()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.That(ServerPortProbe.ConnectSucceeds(port, ServerPortProbe.ConnectTimeoutMilliseconds), Is.True);
            Assert.That(ServerPortProbe.IsListening(port), Is.True);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public void AClosedPortIsNotListening()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Assert.That(ServerPortProbe.ConnectSucceeds(port, ServerPortProbe.ConnectTimeoutMilliseconds), Is.False);
    }
}
