using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Receives each real drop once, sharing it across the nearby expedition. With a player in the encounter (owner
    /// 2026-10-31) the player gets a quarter of the loot no matter what: one item in four (the first, fifth, ...) and a
    /// quarter of the coin; the rest goes at random to the gamebots that took part.
    /// </summary>
    public sealed class RealmRaidLootOwner : IGameStaticItemOwner
    {
        public sealed class Ledger
        {
            internal readonly Dictionary<long, int> ItemsReceived = new();
            internal int ItemsDropped;
            internal int ItemsToPlayers;
        }
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RealmRaidLootOwner));

        public const int PlayerSharePercent = 25;

        /// <summary>The real players in the encounter (set by the kill that drops the loot).</summary>
        internal GamePlayer[] Players { get; set; } = [];
        private readonly GameBot[] _members;
        private readonly Ledger _ledger;
        internal IReadOnlyList<GameBot> Members => _members;
        public string Name => "Realm expedition loot";
        public object GameStaticItemOwnerComparand => this;
        public RealmRaidLootOwner(GameBot[] members, Ledger ledger) { _members = members.Where(b => b != null).Distinct().ToArray(); _ledger = ledger; }

        public TryPickUpResult TryAutoPickUpItem(WorldInventoryItem item)
        {
            item.AssertLockAcquisition();
            lock (_ledger)
            {
                // the player's quarter first (high-end encounters only, counting their drops alone): one item in four,
                // given straight to the bag (no autoloot needed)
                if (Players.Length > 0 && _ledger.ItemsToPlayers * 100 < ++_ledger.ItemsDropped * PlayerSharePercent)
                    foreach (GamePlayer player in Players.Where(p => PlayerEligible(p, item)).OrderBy(_ => Random.Shared.Next()))
                        if (player.TryPickUpItem(player, item) == TryPickUpResult.Success)
                        {
                            _ledger.ItemsToPlayers++;
                            return TryPickUpResult.Success;
                        }
                // the other three in four: the gamebots that took part, at random among those with the fewest so far
                foreach (GameBot bot in _members.Where(b => Eligible(b, item))
                    .OrderBy(b => _ledger.ItemsReceived.GetValueOrDefault(b.DatabaseID)).ThenBy(_ => Random.Shared.Next()))
                {
                    TryPickUpResult result = bot.TryAutoPickUpItem(item);
                    if (result != TryPickUpResult.Success) continue;
                    _ledger.ItemsReceived[bot.DatabaseID] = _ledger.ItemsReceived.GetValueOrDefault(bot.DatabaseID) + 1;
                    // Raid loot is the reward the whole endgame turns on; record every piece (2026-10-19: the first two
                    // dragon kills left no trace of who received what).
                    if (Log.IsInfoEnabled)
                        Log.Info($"REALM_RAID_LOOT item=\"{item.Name}\" bot=\"{bot.Name}\" id={bot.DatabaseID} class=\"{bot.ClassName}\" " +
                                 $"region={item.CurrentRegionID} raiders={_members.Length}");
                    return result;
                }
            }
            if (Log.IsInfoEnabled)
                Log.Info($"REALM_RAID_LOOT_UNCLAIMED item=\"{item.Name}\" region={item.CurrentRegionID} raiders={_members.Length} " +
                         $"nearby={_members.Count(b => Eligible(b, item))} reason=\"no nearby raider could carry it\"");
            return TryPickUpResult.Blocked;
        }

        public TryPickUpResult TryAutoPickUpMoney(GameMoney money)
        {
            money.AssertLockAcquisition();
            // Use the same lock as normal bot currency transactions, but only live
            // records. Validate every share before mutation; no synchronous DB lookup.
            lock (typeof(AutonomousBotEconomy))
            {
                GameBot[] recipients = _members.Where(b => Eligible(b, money) && b.PersistentRecord != null).ToArray();
                GamePlayer[] players = Players.Where(p => PlayerEligible(p, money)).ToArray();
                long playerShare = players.Length == 0 ? 0 : recipients.Length == 0 ? money.Value : money.Value * PlayerSharePercent / 100;
                if (recipients.Length > 0 && !TryAllocateMoney(recipients.Select(b => b.PersistentRecord).ToArray(), money.Value - playerShare))
                    return TryPickUpResult.Blocked;
                if (recipients.Length == 0 && players.Length == 0)
                    return TryPickUpResult.Blocked;
                for (int i = 0; i < players.Length && playerShare > 0; i++)
                {
                    long part = playerShare / players.Length + (i < playerShare % players.Length ? 1 : 0);
                    if (part > 0)
                        players[i].AddMoney(part, "You get {0} as your share of the expedition's loot.");
                }
                if (Log.IsInfoEnabled)
                    Log.Info($"REALM_RAID_LOOT_MONEY copper={money.Value} recipients={recipients.Length} region={money.CurrentRegionID}");
                money.Value = 0; // Paid bags can never be paid again, including after a save-queue failure.
                money.RemoveFromWorld();
                foreach (GameBot bot in recipients)
                    AutonomousBotStatusPersistence.Queue(bot);
            }
            return TryPickUpResult.Success;
        }

        // Caller holds the normal economy lock. Validate the entire allocation
        // first so overflow cannot leave a partially paid raid or consume a bag.
        internal static bool TryAllocateMoney(OfflineWorldBotRecord[] records, long copper)
        {
            if (records.Length == 0 || copper < 0 || records.Any(r => r == null) ||
                records.Distinct().Count() != records.Length) return false;
            long quotient = copper / records.Length, remainder = copper % records.Length;
            for (int i = 0; i < records.Length; i++)
                if (records[i].MoneyCopper > long.MaxValue - quotient - (i < remainder ? 1 : 0)) return false;
            string now = DateTime.UtcNow.ToString("O");
            for (int i = 0; i < records.Length; i++)
            {
                records[i].MoneyCopper += quotient + (i < remainder ? 1 : 0);
                records[i].LastUpdateUtc = now;
                records[i].Dirty = true;
            }
            return true;
        }

        private static bool PlayerEligible(GamePlayer player, GameObject drop) => player != null && player.IsAlive &&
            player.ObjectState == GameObject.eObjectState.Active && player.CurrentRegionID == drop.CurrentRegionID &&
            player.IsWithinRadius(drop, WorldMgr.VISIBILITY_DISTANCE);

        // The Epic Special Forces receive nothing; with only them nearby a drop stays where it fell.
        private static bool Eligible(GameBot bot, GameObject drop) => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !bot.IsEpicSpecialForce &&
            bot.ObjectState == GameObject.eObjectState.Active && bot.CurrentRegionID == drop.CurrentRegionID &&
            bot.IsWithinRadius(drop, WorldMgr.VISIBILITY_DISTANCE) &&
            AutonomousBotRegistry.TryGet(bot.DatabaseID, out var current) && ReferenceEquals(current, bot);
        public TryPickUpResult TryPickUpMoney(GamePlayer source, GameMoney money) => TryPickUpResult.DoesNotWant;
        public TryPickUpResult TryPickUpItem(GamePlayer source, WorldInventoryItem item) => TryPickUpResult.DoesNotWant;
    }
}
