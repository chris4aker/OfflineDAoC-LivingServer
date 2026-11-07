using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// The client a command typed at the server console runs as (1.2 play test): no player, an admin account named
    /// "Console". Before, the console passed a client with no account and every command failed in ScriptMgr.HandleCommand
    /// (a NullReferenceException on client.Account), so "/epictest", "/serverproperties" and the rest could not be typed there.
    /// </summary>
    public static class ConsoleCommandClient
    {
        public const string AccountName = "Console";

        public static GameClient Create() => new(null)
        {
            Account = new DbAccount { Name = AccountName, PrivLevel = (uint)ePrivLevel.Admin, CustomParams = [] }
        };
    }
}
