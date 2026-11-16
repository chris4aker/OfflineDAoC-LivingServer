using System.Data.SQLite;

namespace OfflineDaoc.Launcher;

/// <summary>
/// What the launcher deletes when the player deletes a bot, or every bot, while the server is stopped (the bot list's
/// DELETE buttons). The bot's character, its items, its Exchange and auction rows and its queued commands, as before 1.2,
/// and from 1.2 its rows in the Crafting 2.0 tables (EconomyTables.All, WP9). A running server is sent a Delete command
/// instead (AutonomousPopulationController.DeleteBot).
///
/// Every table of EconomyTables.All has one decision here (<see cref="EconomyDecisions"/>; BotPurgeTests checks the list
/// against EconomyTables.cs):
/// <list type="bullet">
/// <item>offline_bot_types: the bot's stored personality type. The marker row (BotId 0, "the stored types began") stays,
/// so bots made later get the new-bot share instead of being stored as legacy bots.</item>
/// <item>offline_bot_crafters: the bot's crafter row.</item>
/// <item>offline_bot_craft_orders: the orders the bot placed as a buyer (their escrowed coin was its own; it goes with the
/// bot, as its purse does). An order the bot was to make stays: when it expires the server refunds its buyer
/// (AutonomousBotCraftOrders.ExpireDue), so another bot's or a player's coin is never lost. With every bot deleted, every
/// gamebot's order goes; a player's or the quartermaster's (BuyerBotId 0) stays and expires the same way.</item>
/// <item>offline_crafter_customers: what the bot remembered of its customers, and what crafters remembered of it.</item>
/// <item>offline_exchange_index and offline_economy_ledger: the world's prices and hourly money supply, not any bot's;
/// kept. The release build empties them (progress-policy.json).</item>
/// </list>
/// A table that does not exist yet (a world the 1.2 server has not started on) is skipped.
/// </summary>
public static class BotPurge
{
    public enum Decision { DeleteBotRows, KeepWorldRows }

    /// <summary>EconomyTables.All, each with its decision and the SQL that deletes one bot's rows (@id, @owner) and every bot's.</summary>
    public static readonly (string Table, Decision Decision, string? OneBot, string? AllBots)[] EconomyDecisions =
    [
        ("offline_bot_types", Decision.DeleteBotRows,
            "DELETE FROM offline_bot_types WHERE BotId=@id AND BotId<>0",
            "DELETE FROM offline_bot_types WHERE BotId<>0"),
        ("offline_bot_crafters", Decision.DeleteBotRows,
            "DELETE FROM offline_bot_crafters WHERE BotId=@id",
            "DELETE FROM offline_bot_crafters"),
        ("offline_bot_craft_orders", Decision.DeleteBotRows,
            "DELETE FROM offline_bot_craft_orders WHERE BuyerBotId=@id",
            "DELETE FROM offline_bot_craft_orders WHERE BuyerBotId<>0"),
        ("offline_exchange_index", Decision.KeepWorldRows, null, null),
        ("offline_economy_ledger", Decision.KeepWorldRows, null, null),
        ("offline_crafter_customers", Decision.DeleteBotRows,
            "DELETE FROM offline_crafter_customers WHERE CrafterBotId=@id OR CustomerId=@owner",
            "DELETE FROM offline_crafter_customers"),
    ];

    private static readonly string[] OneBotCore =
    [
        "DELETE FROM Inventory WHERE OwnerID=@owner",
        "DELETE FROM offline_auction_escrow WHERE BidderBotId=@id",
        "DELETE FROM offline_auction_ledger WHERE SellerBotId=@id OR BuyerBotId=@id",
        "DELETE FROM offline_auction_listings WHERE SellerBotId=@id OR HighBidderBotId=@id",
        "DELETE FROM offline_bot_commands WHERE BotId=@id",
    ];

    private static readonly string[] AllBotsCore =
    [
        "DELETE FROM Inventory WHERE OwnerID LIKE 'offlinebot:%'",
        "DELETE FROM offline_auction_escrow WHERE BidderBotId IN (SELECT BotId FROM offline_world_bots)",
        "DELETE FROM offline_auction_ledger WHERE SellerBotId IN (SELECT BotId FROM offline_world_bots) OR BuyerBotId IN (SELECT BotId FROM offline_world_bots)",
        "DELETE FROM offline_auction_listings WHERE SellerBotId IN (SELECT BotId FROM offline_world_bots) OR HighBidderBotId IN (SELECT BotId FROM offline_world_bots)",
        "DELETE FROM offline_bot_commands WHERE BotId IN (SELECT BotId FROM offline_world_bots)",
    ];

    /// <summary>The statements for one bot, in order (the character record last), for the tables this world has.</summary>
    public static IReadOnlyList<string> OneBotStatements(ISet<string> tables) =>
        [.. OneBotCore, .. Economy(tables, allBots: false), "DELETE FROM offline_world_bots WHERE BotId=@id"];

    /// <summary>The statements for every bot, in order (the character records last), for the tables this world has.</summary>
    public static IReadOnlyList<string> AllBotsStatements(ISet<string> tables) =>
        [.. AllBotsCore, .. Economy(tables, allBots: true), "DELETE FROM offline_world_bots"];

    private static IEnumerable<string> Economy(ISet<string> tables, bool allBots) =>
        EconomyDecisions.Where(d => d.Decision == Decision.DeleteBotRows && tables.Contains(d.Table))
            .Select(d => (allBots ? d.AllBots : d.OneBot)!);

    /// <summary>Deletes one bot's data in the caller's transaction.</summary>
    public static void DeleteBot(SQLiteConnection connection, SQLiteTransaction transaction, long botId)
    {
        foreach (string sql in OneBotStatements(Tables(connection, transaction)))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@id", botId);
            command.Parameters.AddWithValue("@owner", $"offlinebot:{botId}");
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Deletes every bot's data in the caller's transaction.</summary>
    public static void DeleteAllBots(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        foreach (string sql in AllBotsStatements(Tables(connection, transaction)))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    public static HashSet<string> Tables(SQLiteConnection connection, SQLiteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = command.ExecuteReader();
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) tables.Add(reader.GetString(0));
        return tables;
    }
}
