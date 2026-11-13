namespace DOL.GS
{
    /// <summary>
    /// Which clients skip the connection time-outs (Living Server 1.2, play with friends; co-op audit 2026-11-03 §1
    /// "Idle timeouts").
    ///
    /// Before 1.2 only the loopback client (this PC) skipped the ping time-out, the position time-out (soft link death)
    /// and the idle kick, because the legacy client can stop pinging and sending positions while it stands in /grind.
    /// A friend on the home network or Tailscale hits the same client behaviour, so a trusted address
    /// (DOL.Network.NetworkTrust.IsTrusted: this PC, 10/8, 172.16/12, 192.168/16, 100.64/10) now skips them too.
    /// A public address keeps every upstream rule, in /grind or not.
    /// </summary>
    public static class ClientTimeoutPolicy
    {
        /// <summary>The ping time-out (ClientService) and the position time-out (PlayerMovementComponent).</summary>
        public static bool SkipsConnectionTimeouts(bool loopback, bool trustedNetwork) => loopback || trustedNetwork;

        /// <summary>The idle kick to the character screen (server / player idle kick, off by default).</summary>
        public static bool SkipsIdleKick(bool loopback, bool trustedNetwork) => loopback || trustedNetwork;

        public static bool SkipsConnectionTimeouts(GameClient client) =>
            client != null && SkipsConnectionTimeouts(client.IsLoopbackConnection, client.IsTrustedNetworkConnection);

        public static bool SkipsIdleKick(GameClient client) =>
            client != null && SkipsIdleKick(client.IsLoopbackConnection, client.IsTrustedNetworkConnection);
    }
}
