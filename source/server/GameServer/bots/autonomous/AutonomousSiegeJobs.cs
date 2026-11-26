using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>Bounded per-keep jobs. Weak references and expiring leases cannot retain removed bots.</summary>
    public static class AutonomousSiegeJobs
    {
        private sealed record Lease(WeakReference<GameBot> Bot, long Until, BotSiegeKind Kind, bool Building = false);
        private static readonly object Gate = new();
        private static readonly Dictionary<(ushort Region, string Keep, eRealm Realm, int Slot), Lease> Jobs = new();
        // Run 2026-10-07 (Midgard on Caer Erasleigh): 17 attackers got two jobs, a carried catapult took one, and a
        // single ram wore the gate down 3% a minute. A small force still brings one ram; an army brings both rams and
        // the trebuchet.
        public static int Slots(int present) => present < 4 ? 0 : present < 8 ? 1 : present < 24 ? 3 : present < 64 ? 5 : 6;
        public static bool CanOperate(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Armsman or eCharacterClass.Mercenary or eCharacterClass.Paladin or eCharacterClass.Reaver or eCharacterClass.Scout or
            eCharacterClass.Warrior or eCharacterClass.Berserker or eCharacterClass.Savage or eCharacterClass.Thane or
            eCharacterClass.Hero or eCharacterClass.Champion or eCharacterClass.Blademaster or eCharacterClass.Ranger or eCharacterClass.Valewalker;
        public static bool Eligible(GameBot bot) => bot is { IsAutonomousWorldBot: true, IsAlive: true } &&
            !BotDutyLeases.IsSpokenForByPlayer(bot) && bot.CharacterClass != null && CanOperate((eCharacterClass)bot.CharacterClass.ID);

        /// <summary>Attackers: two rams, the trebuchet (triple damage against doors), a catapult, ballistas.
        /// Defenders: ballistas against siege, catapults on the attackers; never a trebuchet, whose 2,000 minimum
        /// range has no spot inside the keep (it logged placement_blocked with no candidate at all).</summary>
        public static BotSiegeKind SlotKind(int index, bool attacking) => attacking
            ? index < 2 ? BotSiegeKind.Ram : index == 2 ? BotSiegeKind.Trebuchet : index == 3 ? BotSiegeKind.Catapult : BotSiegeKind.Ballista
            : index < 2 ? BotSiegeKind.Ballista : index < 4 ? BotSiegeKind.Catapult : BotSiegeKind.Ballista;

        // ---- #612 / #666 (owner 2026-11-26): engines with an objective, and the 1.60 cap of 3 per door ----

        /// <summary>Patch 1.60, unchanged to 1.65: "Only 3 siege weapons can now attack a door at the same time." Owner
        /// 2026-11-26: at most 3 standing engines of one realm per door, in battlegrounds and the frontier; a player's
        /// engine counts toward the 3 but a player is never refused.</summary>
        public const int MaxEnginesPerDoor = 3;

        /// <summary>Battleground job places: ram, ram, trebuchet (the first three attacking kinds).</summary>
        public const int BattlegroundSlots = 3;

        /// <summary>Battlegrounds: rams and trebuchets only, the door engines (owner 2026-11-26).</summary>
        public static bool AllowedInBattleground(BotSiegeKind kind) => kind is BotSiegeKind.Ram or BotSiegeKind.Trebuchet;

        /// <summary>Whether a realm here may have siege jobs at all. Battlegrounds: only an attacker with a closed door
        /// (no defensive engines, nothing once the doors are down). Frontier: unchanged, attackers and defenders.</summary>
        public static bool HasObjective(bool battleground, bool attacking, bool closedDoor) => !battleground || attacking && closedDoor;

        /// <summary>How many job places a realm gets. Battlegrounds: ram, ram, trebuchet for an attacker at a closed door,
        /// none otherwise. Frontier: as before (6 attacking a door, 4 otherwise).</summary>
        public static int SlotCount(int present, bool attackingDoor, bool battleground) => battleground
            ? attackingDoor ? Math.Min(Slots(present), BattlegroundSlots) : 0
            : Math.Min(Slots(present), attackingDoor ? 6 : 4);

        /// <summary>The job kinds a realm can be given here, in slot order.</summary>
        public static BotSiegeKind[] KindsFor(int present, bool attackingDoor, bool battleground) =>
            Enumerable.Range(0, SlotCount(present, attackingDoor, battleground)).Select(i => SlotKind(i, attackingDoor)).ToArray();

        /// <summary>One engine as the door cap sees it. Standing: alive, above its decay health, and operated (by a bot or
        /// a player). An engine that was released (no operator) or destroyed frees its place.</summary>
        public readonly record struct EngineAtDoor(eRealm Realm, bool Alive, bool Working, bool Operated);

        public static bool Standing(EngineAtDoor engine, eRealm realm) =>
            engine.Realm == realm && engine.Alive && engine.Working && engine.Operated;

        public static int StandingAtDoor(IEnumerable<EngineAtDoor> engines, eRealm realm) => engines.Count(e => Standing(e, realm));

        /// <summary>True when one more bot engine would put the realm past <see cref="MaxEnginesPerDoor"/> at the door:
        /// standing engines (players' included) plus other bots of the realm mid-build there.</summary>
        public static bool DoorCapReached(int standing, int othersBuilding) => standing + othersBuilding >= MaxEnginesPerDoor;

        /// <summary>The overnight log's line for a refusal by the cap (an RVR_SIEGE action).</summary>
        public static string DoorCapDetail(int doorId, BotSiegeKind kind, int standing, int othersBuilding, bool battleground) =>
            $"door={doorId} kind={kind} standing={standing} building={othersBuilding} cap={MaxEnginesPerDoor} battleground={(battleground ? "true" : "false")}";

        /// <summary>Marks the bot's job as building (or not), so other bots of its realm count it against the cap.</summary>
        public static void MarkBuilding(GameBot bot, bool building)
        {
            if (bot == null) return;
            lock (Gate) foreach (var key in Jobs.Where(p => p.Value.Bot.TryGetTarget(out GameBot b) && b == bot).Select(p => p.Key).ToArray())
                Jobs[key] = Jobs[key] with { Building = building };
        }

        /// <summary>Bots of the realm, other than this one, building an engine for this keep in this region now.</summary>
        public static int OthersBuilding(GameBot bot, ushort region, string keep)
        {
            if (bot == null || keep == null) return 0;
            long now = GameLoop.GameLoopTime;
            lock (Gate) return Jobs.Count(p => p.Key.Region == region && p.Key.Keep == keep && p.Key.Realm == bot.Realm &&
                p.Value.Building && p.Value.Until >= now && p.Value.Bot.TryGetTarget(out GameBot b) && b != bot && b.IsAlive);
        }

        /// <summary>Tests only.</summary>
        public static void Reset() { lock (Gate) Jobs.Clear(); }

        /// <summary>Slot search order: slots matching a kit the bot already carries come first.</summary>
        public static int[] SlotOrder(int count, bool attacking, BotSiegeKind? carried) =>
            Enumerable.Range(0, count).OrderBy(index => carried.HasValue && SlotKind(index, attacking) == carried.Value ? 0 : 1)
                .ThenBy(index => index).ToArray();

        public static bool TryAcquire(GameBot bot, string keep, int present, bool attacking, bool enemyEngines,
            out BotSiegeKind kind, out int slot, ushort? destinationRegion = null, BotSiegeKind? carried = null, bool battleground = false)
        {
            kind = default; slot = -1;
            if (!Eligible(bot)) return false;
            // #666: in a battleground only an attacker at a closed door has siege work (attacking here means "at a door").
            if (!HasObjective(battleground, attacking, attacking)) return false;
            ushort region = destinationRegion ?? bot.CurrentRegionID;
            lock (Gate)
            {
                long now = GameLoop.GameLoopTime;
                foreach (var key in Jobs.Where(p => p.Value.Until < now || !p.Value.Bot.TryGetTarget(out GameBot b) || !b.IsAlive ||
                    b.ObjectState != GameObject.eObjectState.Active).Select(p => p.Key).ToArray()) Jobs.Remove(key);
                var existing = Jobs.FirstOrDefault(p => p.Key.Keep == keep && p.Key.Realm == bot.Realm &&
                    p.Key.Region == region && p.Value.Bot.TryGetTarget(out GameBot b) && b == bot);
                if (existing.Value != null && (!battleground || AllowedInBattleground(existing.Value.Kind)))
                {
                    Jobs[existing.Key] = existing.Value with { Until = now + 120_000 };
                    bot.TempProperties.SetProperty("SiegeJobUntil", now + 120_000);
                    kind = existing.Value.Kind; slot = existing.Key.Slot; return true;
                }
                if (existing.Value != null) Jobs.Remove(existing.Key);
                foreach (int index in SlotOrder(SlotCount(present, attacking, battleground), attacking, carried))
                {
                    var key = (region, keep, bot.Realm, index);
                    if (Jobs.ContainsKey(key)) continue;
                    kind = SlotKind(index, attacking);
                    if (kind == BotSiegeKind.Ballista && !enemyEngines) continue;
                    if (battleground && !AllowedInBattleground(kind)) continue;
                    Jobs[key] = new(new(bot), now + 120_000, kind);
                    bot.TempProperties.SetProperty("SiegeJobUntil", now + 120_000);
                    slot = index; return true;
                }
                return false;
            }
        }
        public static void Release(GameBot bot)
        {
            bot?.TempProperties.SetProperty("SiegeJobUntil", 0L);
            lock (Gate) foreach (var key in Jobs.Where(p => p.Value.Bot.TryGetTarget(out GameBot b) && b == bot).Select(p => p.Key).ToArray()) Jobs.Remove(key);
        }
        public static void Refresh(GameBot bot)
        {
            lock (Gate) foreach (var key in Jobs.Where(p => p.Value.Bot.TryGetTarget(out GameBot b) && b == bot).Select(p=>p.Key).ToArray())
                Jobs[key] = Jobs[key] with { Until = GameLoop.GameLoopTime + 120_000 };
            bot.TempProperties.SetProperty("SiegeJobUntil", GameLoop.GameLoopTime + 120_000);
        }
    }
}
