using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.CraftingEconomy;

namespace DOL.GS;

/// <summary>
/// The crafter gamebots (Crafting 2.0 WP4; the IBotCrafterRoster contract of WP0) and their book-keeping rows in
/// offline_bot_crafters. A crafter is an autonomous gamebot of the Crafter personality, not a beggar, while bot_crafters
/// is on; companions and players never are. Its skills stay in offline_world_bots.SerializedCraftingSkills; its row holds
/// the order it joined, counters and its last title. Also the store (IEconomyStore) of the rows and of the stored types.
/// </summary>
public sealed class BotCrafterRoster : IBotCrafterRoster, IEconomyStore
{
    public static readonly BotCrafterRoster Instance = new();

    internal LivingServerTable<long, OfflineBotCrafterRecord> Rows = new("bot crafters", row => row.BotId);

    public string Name => "bot crafters";

    // ---- IBotCrafterRoster ----

    public bool IsCrafter(long botId)
    {
        if (!AutonomousBotCrafting.Enabled || botId <= 0)
            return false;
        if (AutonomousBotRegistry.TryGet(botId, out GameBot bot) && bot != null)
            return AutonomousBotCrafting.IsCrafter(bot);
        return BotCraftingRules.IsCrafterType(AutonomousBotPersonality.TypeFor(botId), AutonomousBeggars.On && AutonomousBeggars.IsBeggarId(botId));
    }

    public bool TryGet(long botId, out CrafterState state)
    {
        state = null;
        if (!IsCrafter(botId) || !Rows.TryGet(botId, out OfflineBotCrafterRecord row))
            return false;
        state = StateOf(row);
        return true;
    }

    public IEnumerable<CrafterState> InRealm(eRealm realm)
    {
        if (!AutonomousBotCrafting.Enabled)
            return [];
        return Rows.Where(row => row.Realm == (int)realm).Where(row => IsCrafter(row.BotId)).Select(StateOf).ToArray();
    }

    public CrafterState FindServiceCrafter(eRealm realm, eCraftingSkill skill, int minSkill, Random random)
    {
        CrafterState[] able = InRealm(realm)
            .Where(state => state.Skills.TryGetValue(skill, out int value) && value >= minSkill)
            .Where(state => AutonomousBotRegistry.TryGet(state.BotId, out _))
            .ToArray();
        return able.Length == 0 ? null : able[(random ?? Random.Shared).Next(able.Length)];
    }

    private static CrafterState StateOf(OfflineBotCrafterRecord row)
    {
        GameBot live = AutonomousBotRegistry.TryGet(row.BotId, out GameBot bot) ? bot : null;
        var skills = BotCraftingSkills.Parse(live?.PersistentRecord?.SerializedCraftingSkills)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        return new CrafterState(row.BotId, live?.Name ?? string.Empty, (eRealm)row.Realm, live?.Level ?? 0,
            (eCraftingSkill)row.PrimaryCraft, skills, AutonomousBotEconomy.GetMoney(row.BotId), row.CoinEarned - row.CoinSpent,
            CrafterSpendPolicy.Default);
    }

    // ---- Book-keeping ----

    /// <summary>The crafters of a realm per order, for the realm weights (BotCraftingRules.ChooseOrder).</summary>
    public IReadOnlyDictionary<eCraftingSkill, int> OrderCounts(eRealm realm) =>
        Rows.Where(row => row.Realm == (int)realm && row.BotId > 0)
            .GroupBy(row => (eCraftingSkill)row.PrimaryCraft)
            .ToDictionary(group => group.Key, group => group.Count());

    public void Enroll(GameBot bot, eCraftingSkill order)
    {
        string now = DateTime.UtcNow.ToString("O");
        Rows.Change(bot.DatabaseID, () => new OfflineBotCrafterRecord { BotId = bot.DatabaseID, CreatedUtc = now }, row =>
        {
            row.Realm = (int)bot.Realm;
            row.PrimaryCraft = (int)order;
            row.LastTitle = BotCraftingRules.Title(order, 1);
        });
    }

    public void NoteMade(long botId, bool masterpiece) => Touch(botId, row =>
    {
        row.CraftsMade++;
        if (masterpiece)
            row.Masterpieces++;
    });

    public void NoteSpent(long botId, long copper) => Touch(botId, row => row.CoinSpent += Math.Max(0, copper));

    /// <summary>Coin a crafter earned from its work (WP5's sales and orders; the enchanting fees here).</summary>
    public void NoteEarned(long botId, long copper) => Touch(botId, row => row.CoinEarned += Math.Max(0, copper));

    public void NoteTitle(long botId, string title) => Touch(botId, row => row.LastTitle = title ?? string.Empty);

    public void NoteSession(long botId) => Touch(botId, row => row.LastSessionUtc = DateTime.UtcNow.ToString("O"));

    private void Touch(long botId, Action<OfflineBotCrafterRecord> change)
    {
        if (Rows.TryGet(botId, out _))
            Rows.Change(botId, () => new OfflineBotCrafterRecord { BotId = botId }, change);
    }

    public bool TryGetRow(long botId, out OfflineBotCrafterRecord row) => Rows.TryGet(botId, out row);

    // ---- IEconomyStore ----

    public void Load()
    {
        Rows.Load(GameServer.Database.SelectAllObjects<OfflineBotCrafterRecord>());
        BotCrafterTypes.Load(GameServer.Database.SelectAllObjects<OfflineBotTypeRecord>());
        BotCrafterTypes.BeginInBackgroundIfDue();
    }

    private long _nextTypeCheck;

    public void Pulse(long now)
    {
        // The switch may be turned on while the server runs: the stored types begin then (off the game loop).
        if (now < _nextTypeCheck)
            return;
        _nextTypeCheck = now + 10_000;
        BotCrafterTypes.BeginInBackgroundIfDue();
    }

    public void Flush(bool wait)
    {
        Rows.Flush(wait);
        BotCrafterTypes.Flush(wait);
    }

    /// <summary>Tests only: an empty roster whose rows are written nowhere.</summary>
    public void ResetForTests()
    {
        Rows = new LivingServerTable<long, OfflineBotCrafterRecord>("bot crafters", row => row.BotId, database: () => null);
        _nextTypeCheck = 0;
    }
}
