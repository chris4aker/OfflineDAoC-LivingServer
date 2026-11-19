using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DOL.GS.CraftingEconomy;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Every gamebot's personality type, stored the first time it matters (Crafting 2.0 WP4, plan §2.3 and Risk 3), so a
/// change of the type shares never re-types anyone. AutonomousBotPersonality.TypeFor reads the stored type first.
/// <list type="bullet">
/// <item>The first start with bot_crafters on stores every bot of the roster (retired ones too) with the type its id
/// had all along (the 12% table, cohort Legacy), and a marker row (BotId 0) that says the stored types began. Existing
/// bots, beggars and voices therefore keep exactly the type they had.</item>
/// <item>From then on, a gamebot seen with no stored type is new: it is typed by the share table at its creation
/// (bot_crafter_share_percent, 7% Crafters, cohort ShareAtCreation) and stored, so later changes of the share leave it
/// alone too.</item>
/// <item>With bot_crafters off nothing is stored or seeded, and a bot with no stored type keeps the 12% table: today's
/// behaviour. Stored types are honoured either way, so a type never changes once stored.</item>
/// </list>
/// Reads are a lock-free dictionary lookup (TypeFor runs in parallel brain turns); rows are written by the Living Server
/// flush, never on the game loop.
/// </summary>
public static class BotCrafterTypes
{
    private static readonly Logger Log = LoggerManager.Create(typeof(BotCrafterTypes));

    /// <summary>The marker row's id: no real bot has id 0.</summary>
    public const long MarkerId = 0;

    private static readonly ConcurrentDictionary<long, BotPersonalityType> Stored = new();
    internal static LivingServerTable<long, OfflineBotTypeRecord> Table = new("bot types", row => row.BotId);
    private static volatile bool _began;
    private static int _seeding;

    /// <summary>Whether the stored types began (the marker row exists): from then on an unknown bot is a new one.</summary>
    public static bool Began => _began;

    public static int Count => Stored.Count;

    public static bool TryGetStored(long botId, out BotPersonalityType type) => Stored.TryGetValue(botId, out type);

    /// <summary>The shares of a bot created now (the legacy table with the Crafter share set).</summary>
    public static int[] NewBotShares() =>
        BotCraftingRules.NewBotShares(AutonomousBotPersonality.TypeSharePercent, CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT);

    /// <summary>The type of a new bot: its id's stable roll on the share table at its creation. Pure.</summary>
    public static BotPersonalityType NewBotType(long botId, int crafterPercent) =>
        BotCraftingRules.TypeFromRoll((int)(AutonomousBotPersonality.Mix(botId, 1) % 100),
            BotCraftingRules.NewBotShares(AutonomousBotPersonality.TypeSharePercent, crafterPercent));

    /// <summary>
    /// The type AutonomousBotPersonality.TypeFor returns: the stored one; else, once the stored types began and while
    /// bot_crafters is on, a new bot's type from the share table (stored now); else <paramref name="legacy"/>.
    /// </summary>
    public static BotPersonalityType Resolve(long botId, BotPersonalityType legacy)
    {
        if (botId <= 0)
            return legacy;
        if (Stored.TryGetValue(botId, out BotPersonalityType stored))
            return stored;
        if (!_began || !CraftingEconomyProperties.BOT_CRAFTERS)
            return legacy;
        BotPersonalityType type = NewBotType(botId, CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT);
        return Store(botId, type, CrafterCohort.ShareAtCreation);
    }

    /// <summary>Stores a type once; a concurrent first store of the same bot keeps the first.</summary>
    private static BotPersonalityType Store(long botId, BotPersonalityType type, CrafterCohort cohort)
    {
        if (!Stored.TryAdd(botId, type))
            return Stored[botId];
        Table.Change(botId, () => new OfflineBotTypeRecord { BotId = botId }, row =>
        {
            row.PersonalityType = (int)type;
            row.Cohort = (int)cohort;
            row.StoredUtc = DateTime.UtcNow.ToString("O");
        });
        if (cohort == CrafterCohort.ShareAtCreation && type == BotPersonalityType.Crafter && Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.CraftEnroll, ("bot", botId), ("outcome", "typed"), ("type", type.ToString()),
                ("cohort", cohort.ToString()), ("sharePercent", CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT)));
        return type;
    }

    /// <summary>The stored rows (server start). The marker row says the stored types began.</summary>
    public static void Load(IEnumerable<OfflineBotTypeRecord> rows)
    {
        OfflineBotTypeRecord[] all = rows?.ToArray() ?? [];
        Table.Load(all);
        Stored.Clear();
        bool began = false;
        foreach (OfflineBotTypeRecord row in all)
        {
            if (row.BotId == MarkerId)
                began = true;
            else if (row.BotId > 0 && Enum.IsDefined(typeof(BotPersonalityType), row.PersonalityType))
                Stored[row.BotId] = (BotPersonalityType)row.PersonalityType;
        }
        _began = began;
    }

    /// <summary>
    /// Begins the stored types, once: every bot of <paramref name="roster"/> without a row keeps its legacy type (cohort
    /// Legacy), then the marker row. Returns how many were stored. A second call does nothing.
    /// </summary>
    public static int Begin(IEnumerable<long> roster)
    {
        if (_began)
            return 0;
        int stored = 0;
        foreach (long botId in roster ?? [])
        {
            if (botId <= 0 || Stored.ContainsKey(botId))
                continue;
            Store(botId, AutonomousBotPersonality.LegacyTypeFor(botId), CrafterCohort.Legacy);
            stored++;
        }
        Table.Change(MarkerId, () => new OfflineBotTypeRecord { BotId = MarkerId }, row =>
        {
            row.PersonalityType = -1;
            row.Cohort = (int)CrafterCohort.Legacy;
            row.StoredUtc = DateTime.UtcNow.ToString("O");
        });
        _began = true;
        if (Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.CraftEnroll, ("outcome", "types_began"), ("legacyBots", stored),
                ("sharePercent", CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT)));
        return stored;
    }

    /// <summary>With bot_crafters on and the stored types not begun: reads the roster off the game loop and begins them.</summary>
    public static void BeginInBackgroundIfDue()
    {
        if (_began || !CraftingEconomyProperties.BOT_CRAFTERS || GameServer.Database == null ||
            Interlocked.Exchange(ref _seeding, 1) != 0)
            return;
        Task.Run(() =>
        {
            try
            {
                Begin(GameServer.Database.SelectAllObjects<OfflineWorldBotRecord>().Select(record => record.BotId).ToArray());
            }
            catch (Exception exception)
            {
                Log.Error("Bot crafters: could not store the bots' types; tried again later", exception);
                Volatile.Write(ref _seeding, 0);
            }
        });
    }

    public static void Flush(bool wait) => Table.Flush(wait);

    /// <summary>Tests only: forget every stored type (and use a table that writes nowhere).</summary>
    public static void ResetForTests()
    {
        Table = new LivingServerTable<long, OfflineBotTypeRecord>("bot types", row => row.BotId, database: () => null);
        Stored.Clear();
        _began = false;
        Volatile.Write(ref _seeding, 0);
    }
}
