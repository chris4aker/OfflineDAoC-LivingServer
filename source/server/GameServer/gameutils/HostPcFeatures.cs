namespace DOL.GS
{
    /// <summary>
    /// Play with friends (1.2, co-op audit rule 6): a few features act on the PC the server runs on, not on the player's
    /// own: /travel holds the run key in the game window there, and /bot pilot reads the quickbars the game saved there
    /// (%APPDATA%). They work only for a client connected from that PC (loopback) and fail closed, with a clear message,
    /// for a friend playing from another PC. Loopback gates only these host-PC side effects, never gameplay.
    /// </summary>
    public static class HostPcFeatures
    {
        public const string TravelHostOnly =
            "/travel works only on the PC that runs the server: it holds the run key in the game window there. " +
            "Playing from your own PC, run there with your own keys. No movement was started.";

        public const string PilotHostOnly =
            "/bot pilot works only on the PC that runs the server: it reads the quickbars the game saved there, and yours " +
            "are on your own PC.";

        /// <summary>This player's game connects from the server's own PC.</summary>
        public static bool IsOnHostPc(GamePlayer player) => player?.Client?.IsLoopbackConnection == true;
    }
}
