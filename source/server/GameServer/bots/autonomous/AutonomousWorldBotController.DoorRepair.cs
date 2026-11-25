using System;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Commands;
using DOL.GS.CraftingEconomy;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>#525: a defender gamebot's door repair after a defense (rules in <see cref="AutonomousDoorRepair"/>).</summary>
    public sealed partial class AutonomousWorldBotController
    {
        private string _doorRepairKeep;
        private ushort _doorRepairRegion;
        private long _doorRepairUntil, _doorRepairJobEnd, _doorRepairTickStart, _doorRepairNext, _doorRepairApproachSince;
        private GameKeepDoor _doorRepairDoor;
        private Vector3? _doorRepairSpot;
        private bool _doorRepairBuying;

        /// <summary>Called when a defense this bot was in ends (its destination is released): if the keep is still the
        /// bot's realm's, the bot may take the repair job for a while.</summary>
        private void NoteDefenseEnded(GameBot bot, CampDestination destination, long now)
        {
            if (destination?.Id?.StartsWith("rvr-keep-", StringComparison.Ordinal) != true || !bot.IsAutonomousWorldBot) return;
            _doorRepairKeep = destination.Id; _doorRepairRegion = destination.RegionId;
            _doorRepairUntil = now + AutonomousDoorRepair.WindowMilliseconds; _doorRepairJobEnd = 0;
            _doorRepairDoor = null; _doorRepairTickStart = 0; _doorRepairNext = 0; _doorRepairSpot = null;
        }

        private void EndDoorRepair(GameBot bot, string reason)
        {
            if (_doorRepairKeep != null && _doorRepairJobEnd != 0 && Log.IsInfoEnabled)
                Log.Info($"RVR_DOOR_REPAIR action=end bot={bot.Name} keep={_doorRepairKeep} door={_doorRepairDoor?.DoorId ?? 0} " +
                    $"doorHp={_doorRepairDoor?.HealthPercent ?? 0} reason={reason}");
            AutonomousDoorRepair.Release(bot);
            _doorRepairBuying = false;
            _doorRepairKeep = null; _doorRepairDoor = null; _doorRepairTickStart = 0; _doorRepairSpot = null; _doorRepairJobEnd = 0;
        }

        private void DoorRepairLog(GameBot bot, string action, string detail)
        {
            if (Log.IsInfoEnabled)
                Log.Info($"RVR_DOOR_REPAIR action={action} bot={bot.Name} keep={_doorRepairKeep} door={_doorRepairDoor?.DoorId ?? 0} " +
                    $"doorHp={_doorRepairDoor?.HealthPercent ?? 0} {detail}");
        }

        /// <summary>One turn of the repair job. True while the bot works on it (walking, buying, repairing); false to let
        /// the bot do anything else this turn (no job, a fight first, or the job ended).</summary>
        private bool TryRunDoorRepair(GameBot bot)
        {
            if (_doorRepairKeep == null) return false;
            long now = GameLoop.GameLoopTime;
            if (_doorRepairJobEnd == 0 && now > _doorRepairUntil) { EndDoorRepair(bot, "window"); return false; }
            if (_doorRepairJobEnd != 0 && now > _doorRepairJobEnd) { EndDoorRepair(bot, "time_limit"); return false; }
            if (now < _doorRepairNext) return false;
            if (!bot.IsAlive || bot.CurrentRegionID != _doorRepairRegion || BotSiegeRuntime.Assigned(bot) || BotSiegeRuntime.HoldingPosition(bot))
                return false;
            var keep = GameServer.KeepManager.GetKeepsOfRegion(_doorRepairRegion).FirstOrDefault(k => _doorRepairKeep == $"rvr-keep-{k.KeepID}");
            if (keep == null || keep.Realm != bot.Realm) { EndDoorRepair(bot, "keep_lost"); return false; }
            // Defenders still present: a bot that has gone off elsewhere does not walk back to repair.
            if (!_doorRepairBuying && !bot.IsWithinRadius(new Point3D(keep.X, keep.Y, keep.Z), 4000)) { EndDoorRepair(bot, "away"); return false; }

            GameKeepDoor door = _doorRepairDoor;
            if (door == null || door.ObjectState != GameObject.eObjectState.Active || door.HealthPercent >= 100 ||
                !AutonomousDoorRepair.TryClaim(_doorRepairRegion, door.DoorId, bot, now))
            {
                if (door != null && door.HealthPercent >= 100) DoorRepairLog(bot, "door_full", "");
                AutonomousDoorRepair.Release(bot);
                _doorRepairDoor = door = keep.Doors.Values
                    .Where(d => d.IsAttackableDoor && d.ObjectState == GameObject.eObjectState.Active && d.HealthPercent < 100)
                    .OrderBy(d => d.HealthPercent).ThenBy(bot.GetDistanceTo)
                    .FirstOrDefault(d => AutonomousDoorRepair.TryClaim(_doorRepairRegion, d.DoorId, bot, now));
                _doorRepairTickStart = 0; _doorRepairSpot = null; _doorRepairApproachSince = 0;
                if (door == null) { EndDoorRepair(bot, "no_damaged_door"); return false; }
                if (_doorRepairJobEnd == 0)
                {
                    _doorRepairJobEnd = now + AutonomousDoorRepair.JobLimitMilliseconds;
                    DoorRepairLog(bot, "start", $"level={door.Level} woodPerRepair={RepairCommandHandler.GetNeededWoodForOneTick(door.Level)} " +
                        $"skill={AutonomousDoorRepair.RepairSkill(AutonomousBotCrafting.SkillValue(bot, eCraftingSkill.WoodWorking))}");
                }
            }

            int needed = RepairCommandHandler.GetNeededWoodForOneTick(door.Level);
            int wood = BotWoodUnits(bot);
            bool enemies = door.EnemiesNearForRepair || keep.InCombat;
            string blocked = AutonomousDoorRepair.Blocked(bot.IsAlive, bot.InCombat, bot.IsMoving, door.InCombat, enemies,
                door.HealthPercent, bot.IsWithinRadius(door, AutonomousDoorRepair.Reach - 24), wood, needed);
            if (blocked is "in_combat" or "door_under_attack" or "enemies_near")
            {
                // No repair while hit (player rule); fighting comes first. The job waits.
                if (_doorRepairTickStart != 0) DoorRepairLog(bot, "interrupted", $"reason={blocked}");
                _doorRepairTickStart = 0; _doorRepairNext = now + 10_000; return false;
            }
            if (blocked == "no_wood") return BuyRepairWood(bot, door, needed, wood, now);
            if (blocked is "too_far" or "moving")
            {
                if (!bot.IsWithinRadius(door, AutonomousDoorRepair.Reach - 24))
                {
                    if (_doorRepairApproachSince == 0) _doorRepairApproachSince = now;
                    if (now - _doorRepairApproachSince > 90_000)
                    { DoorRepairLog(bot, "unreachable", ""); _doorRepairDoor = null; AutonomousDoorRepair.Release(bot); _doorRepairNext = now + 5_000; return false; }
                    _doorRepairSpot ??= DoorRepairSpot(bot, keep, door);
                    if (_doorRepairSpot == null)
                    { DoorRepairLog(bot, "unreachable", "no floor beside the door"); _doorRepairDoor = null; AutonomousDoorRepair.Release(bot); _doorRepairNext = now + 5_000; return false; }
                    _doorRepairTickStart = 0;
                    IssuePath(bot, _doorRepairSpot.Value, preciseArrival: true);
                    SetRvrStatus(bot, "Walking to repair the keep door", door.Name, "Door repair with wood after the defense");
                    return true;
                }
                bot.StopMovingOnPath(); bot.StopMoving();
                return true;
            }
            _doorRepairApproachSince = 0;
            bot.TargetObject = door;
            if (_doorRepairTickStart == 0)
            {
                _doorRepairTickStart = now;
                bot.Emote(eEmote.PlayerPrepare);
                Message.SystemToArea(bot, $"{bot.Name} begins repairing the {door.Name.ToLowerInvariant()}.", eChatType.CT_Emote, bot);
            }
            if (now - _doorRepairTickStart < AutonomousDoorRepair.RepairMilliseconds)
            {
                AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.SiegeParticipation);
                SetRvrStatus(bot, $"Repairing the keep door ({(AutonomousDoorRepair.RepairMilliseconds - (now - _doorRepairTickStart) + 999) / 1000} s)",
                    door.Name, $"Door repair with wood after the defense; door at {door.HealthPercent}%");
                return true;
            }
            _doorRepairTickStart = 0;
            double chance = RepairCommandHandler.RepairChance(
                AutonomousDoorRepair.RepairSkill(AutonomousBotCrafting.SkillValue(bot, eCraftingSkill.WoodWorking)), door.Level);
            if (Util.Chance(chance))
            {
                int before = door.HealthPercent;
                door.Repair((int)(door.MaxHealth * RepairCommandHandler.DoorRepairShare));
                RemoveBotWood(bot, needed);
                DoorRepairLog(bot, "repaired", $"from={before} wood={needed} woodLeft={BotWoodUnits(bot)}");
            }
            else DoorRepairLog(bot, "failed", $"chance={chance:0.00}");
            return true;
        }

        /// <summary>A floor spot within reach of the door on the keep's side (defenders repair from inside).</summary>
        private static Vector3? DoorRepairSpot(GameBot bot, AbstractGameKeep keep, GameKeepDoor door)
        {
            var nav = PathfindingProvider.Instance;
            if (!nav.IsAvailable || !nav.HasNavmesh(bot.CurrentZone)) return null;
            Vector3 at = new(door.X, door.Y, door.Z);
            Vector2 toKeep = new(keep.X - door.X, keep.Y - door.Y);
            toKeep = toKeep.LengthSquared() > 1 ? Vector2.Normalize(toKeep) : Vector2.UnitX;
            foreach (float d in new[] { 110f, 150f, -110f, -150f, 60f })
            {
                Vector3 raw = at + new Vector3(toKeep * d, 0);
                Vector3? floor = nav.GetClosestPoint(bot.CurrentZone, raw, 48, 48, 256, nav.DefaultFilters);
                if (!floor.HasValue || Vector3.Distance(floor.Value, at) > AutonomousDoorRepair.Reach - 40) continue;
                if (AutonomousZoneItinerary.HasCompleteCorridor(nav, bot.CurrentZone, new(bot.X, bot.Y, bot.Z), floor.Value)) return floor;
            }
            return null;
        }

        private static int BotWoodUnits(GameBot bot)
        {
            if (bot.Inventory == null) return 0;
            lock (bot.Inventory)
                return AutonomousDoorRepair.WoodUnits(bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                    .Where(i => i != null).Select(i => (i.Name, i.Count)).ToArray());
        }

        /// <summary>Takes wood units from the backpack as the player's /repair does (whole stacks, then part of one).</summary>
        private static void RemoveBotWood(GameBot bot, int units)
        {
            lock (bot.Inventory)
            {
                foreach (var item in bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).ToArray())
                {
                    if (units <= 0) break;
                    if (item == null || !RepairCommandHandler.IsWood(item.Name)) continue;
                    int value = RepairCommandHandler.GetWoodValue(item.Name.ToLowerInvariant());
                    if (value <= 0) continue;
                    if (value * item.Count <= units) { units -= value * item.Count; bot.Inventory.RemoveItem(item); }
                    else { bot.Inventory.RemoveCountFromStack(item, (int)Math.Ceiling(units / (double)value)); units = 0; }
                }
            }
            AutonomousBotEconomy.MarkInventoryChanged(bot); bot.MarkAutonomousStateDirty(); AutonomousBotStatusPersistence.Queue(bot, true);
        }

        /// <summary>Buys boards for a few repairs from the nearest wood seller of the realm in this region (real coin).</summary>
        private bool BuyRepairWood(GameBot bot, GameKeepDoor door, int needed, int have, long now)
        {
            var offers = bot.CurrentRegion?.Merchants
                .Where(m => m != null && m.ObjectState == GameObject.eObjectState.Active && m.TradeItems != null &&
                    (m.Realm == eRealm.None || m.Realm == bot.Realm) && AutonomousBotEconomy.SellsForCoin(m))
                .SelectMany(m => m.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>()
                    .Where(t => t.Price > 0 && RepairCommandHandler.IsWood(t.Name) && RepairCommandHandler.GetWoodValue(t.Name.ToLowerInvariant()) > 0)
                    .Select(t => (Merchant: m, Template: t)))
                .OrderBy(o => o.Template.Price / (double)(Math.Max(1, o.Template.PackSize) * RepairCommandHandler.GetWoodValue(o.Template.Name.ToLowerInvariant())))
                .ThenBy(o => bot.GetDistanceTo(o.Merchant))
                .ToArray() ?? [];
            if (offers.Length == 0) { DoorRepairLog(bot, "no_wood_seller", $"need={needed} have={have}"); EndDoorRepair(bot, "no_wood_seller"); return false; }
            var (merchant, template) = offers[0];
            _doorRepairBuying = true;
            if (!ApproachSupplyMerchant(bot, merchant))
            { SetRvrStatus(bot, "Buying wood to repair the keep door", merchant.Name, "Door repair with wood after the defense"); return true; }
            _doorRepairBuying = false;
            int value = RepairCommandHandler.GetWoodValue(template.Name.ToLowerInvariant());
            var plan = AutonomousDoorRepair.PlanPurchase(needed * AutonomousDoorRepair.RepairsPerPurchase - have, needed - have, value,
                template.PackSize, template.Price, template.MaxCount, AutonomousBotEconomy.GetMoney(bot.DatabaseID));
            if (plan.Packs == 0) { DoorRepairLog(bot, "no_coin", $"need={needed - have} price={template.Price}"); EndDoorRepair(bot, "no_coin"); return false; }
            lock (bot.Inventory)
            {
                if (!BotSinks.Spend(bot.DatabaseID, plan.Cost, DOL.GS.CraftingEconomy.EconomyFlowKind.Siege))
                { EndDoorRepair(bot, "no_coin"); return false; }
                if (!bot.Inventory.AddTemplate(GameInventoryItem.Create(template), plan.Boards, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                { BotSinks.Pay(bot.DatabaseID, plan.Cost, DOL.GS.CraftingEconomy.EconomyFlowKind.Siege); EndDoorRepair(bot, "no_room"); return false; }
            }
            AutonomousBotEconomy.MarkInventoryChanged(bot); bot.MarkAutonomousStateDirty(); AutonomousBotStatusPersistence.Queue(bot, true);
            DoorRepairLog(bot, "wood_bought", $"item={template.Id_nb} boards={plan.Boards} copper={plan.Cost} merchant={merchant.Name}");
            return true;
        }
    }
}
