using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>Reads the money supply for a snapshot. The source calls <c>closeWindow</c> once, just before it reads
    /// the purses that can change while it runs (live bots, online players), so the flows and the supply line up.</summary>
    public interface IEconomySupplySource
    {
        void Read(SupplyBuilder builder, Action closeWindow);
        void WriteRows(IReadOnlyList<OfflineEconomyLedgerRecord> rows);
    }

    /// <summary>
    /// The economy ledger (Crafting 2.0 WP2; docs/ECONOMY-LEDGER.md), instrumentation only: it changes no amount. Coin
    /// flows are summed by <see cref="EconomyFlowLedger"/> (True165's BotMoneyLedger165, a8c793e / df86f9f, via True165,
    /// GPL-3.0) from WP0's AddMoney/TrySpend sink and the Exchange's SaleCommitted seam. At start-up a baseline snapshot
    /// of the money supply is taken; at every UTC hour another, and one ECON_SUPPLY and one ECON_FLOWS line per realm
    /// (plus the All total) is logged and one offline_economy_ledger row per realm written. At shutdown a partial hour is
    /// logged (no row). Snapshots read the database on a background task, never on the game loop; the game loop only
    /// notices the hour and copies the online players. Setting: economy_ledger_enabled (on).
    /// </summary>
    public sealed class EconomyLedger : IEconomyStore
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        public static readonly EconomyLedger Instance = new();

        public string Name => "economy ledger";
        public EconomyFlowLedger Flows { get; } = new();

        /// <summary>Replaceable by tests.</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
        public Action<string> Write { get; set; } = line => { if (Log.IsInfoEnabled) Log.Info(line); };
        public Func<bool> Enabled { get; set; } = () => CraftingEconomyProperties.ECONOMY_LEDGER_ENABLED;
        public IEconomySupplySource Source { get; set; } = new ServerSupplySource();

        private readonly object _chainGate = new();
        private Task _chain = Task.CompletedTask;
        // Snapshot state: only the chain (one task at a time) touches these.
        private SupplySnapshot _previous;
        // Game-loop state.
        private bool _started;
        private DateTime _nextHourUtc;
        private long _nextPlayersRefresh;
        private volatile IReadOnlyDictionary<long, (int Realm, int Level)> _botInfo = new Dictionary<long, (int, int)>();

        private EconomyLedger()
        {
            Flows.Enabled = () => _started && Enabled();
            Flows.BotInfo = InfoOf;
        }

        /// <summary>For tests: a ledger of their own (the server uses <see cref="Instance"/>).</summary>
        public static EconomyLedger CreateForTests() => new();

        [ScriptLoadedEvent]
        public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args) => Install(Instance);

        /// <summary>Registers the ledger with WP0's seams: the store lifecycle, the flow sink and the sale event.</summary>
        public static void Install(EconomyLedger ledger)
        {
            EconomyLifecycle.Register(ledger);
            EconomyFlows.Sink = ledger.Flows;
            EconomySeams.SaleCommitted -= ledger.Flows.OnSaleCommitted;
            EconomySeams.SaleCommitted += ledger.Flows.OnSaleCommitted;
        }

        private (int Realm, int Level) InfoOf(long botId)
        {
            if (AutonomousBotRegistry.TryGet(botId, out GameBot live) && live != null)
                return ((int)live.Realm, live.Level);
            return _botInfo.TryGetValue(botId, out var info) ? info : (0, 0);
        }

        // ---- lifecycle ----

        public void Load()
        {
            if (!Enabled())
                return;
            Start();
        }

        private void Start()
        {
            _started = true;
            _nextHourUtc = NextHour(Clock());
            Enqueue(() => Snapshot(Clock(), partial: false, rows: false));
        }

        public void Pulse(long now)
        {
            bool enabled = Enabled();
            if (!enabled)
            {
                if (_started)
                {
                    // Turned off: stop counting and forget the baseline; turning it on again starts a new one.
                    _started = false;
                    Enqueue(() => _previous = null);
                }
                return;
            }
            using (AiProfile.Scope(AiSystem.EconomyLedger))
            {
                if (!_started)
                {
                    Start();
                    return;
                }
                if (now >= _nextPlayersRefresh)
                {
                    _nextPlayersRefresh = now + 60_000;
                    if (Source is ServerSupplySource server)
                        server.RefreshPlayers();
                }
                DateTime utc = Clock();
                if (utc < _nextHourUtc)
                    return;
                DateTime hour = _nextHourUtc;
                _nextHourUtc = NextHour(utc);
                Enqueue(() => Snapshot(hour, partial: false, rows: true));
            }
        }

        public void Flush(bool wait)
        {
            // Only at shutdown: the hour so far, logged (a partial hour writes no row).
            if (!wait || !_started)
                return;
            Task pending;
            lock (_chainGate)
                pending = _chain;
            try { pending.Wait(TimeSpan.FromSeconds(30)); }
            catch { } // RunSafely already logged it
            RunSafely(() => Snapshot(Clock(), partial: true, rows: false));
            _started = false;
        }

        /// <summary>The next UTC hour after <paramref name="utc"/>.</summary>
        public static DateTime NextHour(DateTime utc)
        {
            utc = utc.ToUniversalTime();
            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
        }

        private void Enqueue(Action action)
        {
            lock (_chainGate)
                _chain = _chain.ContinueWith(_ => RunSafely(action), CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
        }

        private static void RunSafely(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                // Never fault the chain: a faulted task would throw from every later Wait (and the logger itself can
                // throw before it is configured, as in tests).
                try
                {
                    if (Log.IsWarnEnabled)
                        Log.Warn("Economy ledger: snapshot failed (the hour's flows carry into the next)", exception);
                }
                catch { }
            }
        }

        /// <summary>Tests: waits for queued snapshots.</summary>
        public void WaitForSnapshots()
        {
            Task pending;
            lock (_chainGate)
                pending = _chain;
            pending.Wait(TimeSpan.FromSeconds(30));
        }

        // ---- the snapshot ----

        /// <summary>
        /// Reads the supply, closes the flow window, logs the lines and (hourly) writes the rows. When the read fails
        /// before the window is closed, nothing is lost: the window stays open and the next hour covers both.
        /// </summary>
        public IReadOnlyList<EconomyLedgerSnapshot.RealmHour> Snapshot(DateTime endUtc, bool partial, bool rows)
        {
            var builder = new SupplyBuilder();
            EconomyFlowLedger.Window window = null;
            Source.Read(builder, () => window ??= Flows.TakeWindow());
            window ??= Flows.TakeWindow();
            SupplySnapshot now = builder.Build(Clock());
            IReadOnlyList<EconomyLedgerSnapshot.RealmHour> hours = EconomyLedgerSnapshot.Compute(_previous, now, window, endUtc, partial);
            foreach (EconomyLedgerSnapshot.RealmHour hour in hours)
            {
                Write(EconomyLedgerSnapshot.SupplyLine(hour));
                string flows = EconomyLedgerSnapshot.FlowsLine(hour, window);
                if (flows != null)
                    Write(flows);
                if (hour.Realm == EconomyLedgerSnapshot.AllRealms && EconomyLedgerSnapshot.True165Line(hour, window) is { } alias)
                    Write(alias);
            }
            if (rows && _previous != null)
            {
                IReadOnlyList<OfflineEconomyLedgerRecord> records = EconomyLedgerSnapshot.Rows(hours);
                if (records.Count > 0)
                    Source.WriteRows(records);
            }
            _previous = now;
            _botInfo = now.Purses.ToDictionary(pair => pair.Key, pair => (pair.Value.Realm, pair.Value.Level));
            return hours;
        }

        /// <summary>Tests: back to a fresh ledger (no baseline, no sums).</summary>
        public void ResetForTests()
        {
            WaitForSnapshots();
            _previous = null;
            _started = false;
            _nextHourUtc = default;
            _nextPlayersRefresh = 0;
            _botInfo = new Dictionary<long, (int, int)>();
            Flows.Reset();
        }

        /// <summary>Tests: start counting as Load would, without a baseline task.</summary>
        public void StartForTests(DateTime utc)
        {
            _started = true;
            _nextHourUtc = NextHour(utc);
        }

        public DateTime NextHourForTests => _nextHourUtc;
    }

    /// <summary>
    /// The server's money supply: offline_world_bots (a live bot's record instead of its row), AccountXMoney (an online
    /// player's purse instead of the saved row), unclaimed Exchange proceeds, guild banks and Exchange listings.
    /// Runs on the ledger's background task; the online players are copied on the game loop.
    /// </summary>
    public sealed class ServerSupplySource : IEconomySupplySource
    {
        private const string ProceedsKeyPrefix = "RealmExchange.Proceeds.";
        private volatile GamePlayer[] _players = [];

        /// <summary>Game loop: a copy of the online players (ClientService's list is per tick).</summary>
        public void RefreshPlayers()
        {
            try { _players = ClientService.Instance.GetPlayers().ToArray(); }
            catch { }
        }

        public void Read(SupplyBuilder builder, Action closeWindow)
        {
            IObjectDatabase database = GameServer.Database;
            // 1. Saved rows (slow; before the window closes).
            IList<OfflineWorldBotRecord> bots = database.SelectAllObjects<OfflineWorldBotRecord>();
            var wallets = new Dictionary<(string Account, int Realm), long>();
            foreach (DbAccountXMoney wallet in database.SelectAllObjects<DbAccountXMoney>())
                if (wallet?.AccountId != null)
                    wallets[(wallet.AccountId, wallet.Realm)] =
                        Money.GetMoney(wallet.Mithril, wallet.Platinum, wallet.Gold, wallet.Silver, wallet.Copper);
            IList<DbCoreCharacterXCustomParam> proceeds = DOLDB<DbCoreCharacterXCustomParam>.SelectObjects(
                DB.Column("KeyName").IsLike(ProceedsKeyPrefix + "%"));
            foreach (DbCoreCharacterXCustomParam row in proceeds)
                if (row?.KeyName != null && row.KeyName.StartsWith(ProceedsKeyPrefix, StringComparison.Ordinal) &&
                    int.TryParse(row.KeyName.AsSpan(ProceedsKeyPrefix.Length), out int realm))
                    builder.Proceeds(realm, PendingCopper(row.Value));

            // 2. The window closes; then what moves while the server runs is read from memory, quickly.
            closeWindow();
            var seen = new HashSet<long>();
            foreach (OfflineWorldBotRecord row in bots)
            {
                if (row == null)
                    continue;
                seen.Add(row.BotId);
                if (AutonomousBotRegistry.TryGet(row.BotId, out GameBot live) && live?.PersistentRecord != null)
                    builder.Bot(row.BotId, live.PersistentRecord.Realm, live.Level, live.PersistentRecord.MoneyCopper,
                        live.ObjectState == GameObject.eObjectState.Active);
                else
                    builder.Bot(row.BotId, row.Realm, row.Level, row.MoneyCopper);
            }
            foreach (GameBot live in AutonomousBotRegistry.Snapshot())
                if (live?.PersistentRecord != null && live.DatabaseID > 0 && seen.Add(live.DatabaseID))
                    builder.Bot(live.DatabaseID, live.PersistentRecord.Realm, live.Level, live.PersistentRecord.MoneyCopper, true);
            foreach (GamePlayer player in _players)
            {
                string account = player?.Client?.Account?.ObjectId;
                if (account != null && player.ObjectState == GameObject.eObjectState.Active)
                    wallets[(account, (int)player.Realm)] = player.GetCurrentMoney();
            }
            foreach (((_, int realm), long copper) in wallets)
                builder.Wallet(realm, copper);
            foreach (Guild guild in Guilds())
                builder.Guild((int)guild.Realm, (long)guild.GetGuildBank());
            DateTime utcNow = DateTime.UtcNow;
            foreach (DbInventoryItem item in MarketCache.SearchItems(new ItemQuery()))
            {
                int realm = item?.OwnerLot switch
                {
                    RealmExchangeBroker.AlbionOwnerLot => 1,
                    RealmExchangeBroker.MidgardOwnerLot => 2,
                    RealmExchangeBroker.HiberniaOwnerLot => 3,
                    _ => 0,
                };
                if (realm == 0 || item.SellPrice <= 0 || RealmExchangeExpiry.IsExpired(item, utcNow))
                    continue;
                builder.Listing(realm, item.SellPrice, AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _));
            }
            for (int realm = 1; realm <= 3; realm++)
                builder.Crafters(realm, BotCrafters.Roster.InRealm((eRealm)realm).Count());
        }

        public void WriteRows(IReadOnlyList<OfflineEconomyLedgerRecord> rows)
        {
            using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
                if (!GameServer.Database.AddObject(rows.Cast<DataObject>()))
                    throw new InvalidOperationException($"could not write {rows.Count} {EconomyTables.EconomyLedger} row(s)");
        }

        /// <summary>The guilds, retried when a guild is created or removed while the list is copied.</summary>
        private static List<Guild> Guilds()
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return GuildMgr.GetGuilds(); }
                catch (InvalidOperationException) when (attempt < 3) { Thread.Sleep(5); }
            }
        }

        /// <summary>The pending copper of a proceeds row ("pending|claiming|starting", RealmExchangeBroker's format).</summary>
        public static long PendingCopper(string value)
        {
            string[] parts = value?.Split('|');
            return parts?.Length == 3 && long.TryParse(parts[0], out long pending) && pending > 0 ? pending : 0;
        }
    }
}
