using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Crafting;

namespace DOL.GS
{
    /// <summary>
    /// The craft trip (Crafting 2.0 WP4): the executor of the 'C' task-break activity (BotCrafterSeams.TripActivity),
    /// through WP0's partial method <see cref="RunBreakActivities"/>, run between training and the inventory stop. One
    /// trip: join an order if the bot has none, buy the materials, walk to a station and work there; what it made is posted
    /// at the inventory stop that follows. Ported from rambodolphin13's DividedRealms (AutonomousWorldBotController.Crafting,
    /// 93c25b69 → ceac061a), GPL-3.0. Changes: the order comes from the realm weights and skips an order with no master
    /// (logged); stations include the alchemy table; plans and attempts wait for the per-tick budget; the session's
    /// best piece and seconds, the enchant and the advert at the end (AutonomousBotCrafting.FinishSession); order delivery
    /// is WP5's task-break hook.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        private enum eCraftTripPhase
        {
            Start,
            Master,
            Plan,
            Shop,
            Station,
            Work,
            Done,
        }

        /// <summary>Longest walk to one stop of a craft trip before the trip ends (DR).</summary>
        private const long CraftLegMilliseconds = 8 * 60_000;

        private string _craftAssignment = string.Empty;
        private eCraftTripPhase _craftPhase;
        private CraftNPC _craftMaster;
        private GameMerchant _craftMerchant;
        private GameStaticItem _craftStation;
        private BotCraftingRules.Session _craftSession;
        private BotCraftingRules.Purchase _craftPurchase;
        private long _craftLegStartedTick;
        private long _craftTripStartedTick;
        private long _craftWorkDoneTick;
        private int _craftAttempts;
        private int _craftSessionsDone;
        private bool _craftSessionLogged;
        private readonly List<DbInventoryItem> _craftMade = new();

        partial void RunBreakActivities(GameBot bot, ref bool handled)
        {
            if (!handled && AutonomousObjectiveAssignments.HasBetweenTaskFlag(bot?.PersistentRecord, BotCrafterSeams.TripFlag) &&
                HandleCraftTrip(bot))
                handled = true;
        }

        /// <summary>One turn of the trip: true while the trip uses this turn; false once it is over (DR's HandleCraftTrip).</summary>
        private bool HandleCraftTrip(GameBot bot)
        {
            string assignment = bot.PersistentRecord?.ObjectiveAssignmentId ?? string.Empty;
            if (!string.Equals(_craftAssignment, assignment, StringComparison.Ordinal))
            {
                // A trip the break's clock cut short still sorts what it made (its best, its seconds).
                if (_craftPhase != eCraftTripPhase.Done)
                    FinishCraftSession(bot, "The task break ended");
                _craftAssignment = assignment;
                _craftPhase = eCraftTripPhase.Start;
                _craftSession = null;
                _craftAttempts = 0;
                _craftSessionsDone = 0;
                _craftSessionLogged = false;
                _craftMade.Clear();
                _craftTripStartedTick = GameLoop.GameLoopTime;
                AutonomousBotCrafting.ForgetTrip(bot);
            }
            if (_craftPhase != eCraftTripPhase.Done && !AutonomousBotCrafting.IsCrafter(bot))
                return EndCraftTrip(bot, "Crafting is off");
            switch (_craftPhase)
            {
                case eCraftTripPhase.Start:
                    if (AutonomousBotCrafting.IsTrained(bot))
                    {
                        _craftPhase = eCraftTripPhase.Plan;
                        return BeginCraftShopping(bot);
                    }
                    eCraftingSkill order = AutonomousBotCrafting.ChooseOrder(bot,
                        candidate => NearestByTravel(bot, AutonomousBotCrafting.MastersOf(candidate, bot.Realm)) != null);
                    if (order == eCraftingSkill.NoCrafting)
                        return EndCraftTrip(bot, "No craft master of any order is in reach");
                    _craftMaster = NearestByTravel(bot, AutonomousBotCrafting.MastersOf(order, bot.Realm));
                    if (_craftMaster == null)
                        return EndCraftTrip(bot, "No craft master of this order is in reach");
                    SetCraftLeg(eCraftTripPhase.Master);
                    return true;
                case eCraftTripPhase.Master:
                    if (WalkToCraftStop(bot, _craftMaster, Math.Max(32, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE) - 16, "Join a crafting order"))
                        return true;
                    if (!bot.IsWithinRadius(_craftMaster, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE))
                        return EndCraftTrip(bot, "Could not reach the craft master");
                    AutonomousBotCrafting.Train(bot, _craftMaster);
                    _craftPhase = eCraftTripPhase.Plan;
                    return BeginCraftShopping(bot);
                case eCraftTripPhase.Plan:
                    return BeginCraftShopping(bot);
                case eCraftTripPhase.Shop:
                    if (WalkToCraftStop(bot, _craftMerchant, Math.Max(32, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE) - 16, "Buy crafting materials"))
                        return true;
                    if (!AutonomousBotCrafting.TryBuy(bot, _craftMerchant, _craftSession, _craftPurchase))
                        return EndCraftTrip(bot, "Could not buy the materials");
                    return NextCraftPurchase(bot);
                case eCraftTripPhase.Station:
                    if (_craftStation != null && WalkToCraftStop(bot, _craftStation, AbstractCraftingSkill.CRAFT_DISTANCE - 64, "Work at a crafting station"))
                        return true;
                    if (_craftStation != null && !bot.IsWithinRadius(_craftStation, AbstractCraftingSkill.CRAFT_DISTANCE))
                        return EndCraftTrip(bot, "Could not reach the crafting station");
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    if (_craftStation != null)
                        BotTurning.TurnGradually(bot, _craftStation);
                    if (!_craftSessionLogged)
                    {
                        _craftSessionLogged = true;
                        AutonomousBotCrafting.LogSessionStart(bot, _craftSession, _craftStation?.Model ?? 0);
                    }
                    _craftWorkDoneTick = GameLoop.GameLoopTime + AutonomousBotCrafting.WorkSeconds(bot, _craftSession) * 1000L;
                    _craftPhase = eCraftTripPhase.Work;
                    SetStatus(bot, $"Crafting {_craftSession.Recipe.ProductName}",
                        _craftSession.OrderId != null ? "Make a piece another bot paid for" : "Craft real items at a real station",
                        $"Skill {AutonomousBotCrafting.SkillValue(bot, _craftSession.Skill)} {_craftSession.Skill}", string.Empty);
                    return true;
                case eCraftTripPhase.Work:
                    if (bot.IsMoving)
                    {
                        // A moved crafter loses the attempt, as a player does (DR).
                        _craftPhase = eCraftTripPhase.Station;
                        return true;
                    }
                    if (GameLoop.GameLoopTime < _craftWorkDoneTick || !AutonomousBotCrafting.TryTakeAttempt())
                        return true; // the clock, or the server's attempts for this tick are used up
                    DbInventoryItem made = AutonomousBotCrafting.Attempt(bot, _craftSession, out bool stop);
                    _craftAttempts++;
                    if (made != null)
                        _craftMade.Add(made);
                    if (stop || _craftMade.Count >= _craftSession.Copies)
                    {
                        int madeCount = _craftMade.Count;
                        FinishCraftSession(bot, $"Finished at the station; made {madeCount}");
                        // The next session of the same trip, while coin, room and the trip's time allow (ours; DR made one).
                        if (!stop && BotCraftingRules.AnotherSession(_craftSessionsDone, GameLoop.GameLoopTime - _craftTripStartedTick))
                        {
                            _craftPhase = eCraftTripPhase.Plan;
                            return BeginCraftShopping(bot);
                        }
                        return EndCraftTrip(bot, $"Finished at the station; {_craftSessionsDone} session(s), made {madeCount} in the last");
                    }
                    _craftPhase = eCraftTripPhase.Station;
                    return true;
                default:
                    return false;
            }
        }

        private void SetCraftLeg(eCraftTripPhase phase)
        {
            _craftPhase = phase;
            _craftLegStartedTick = GameLoop.GameLoopTime;
        }

        private bool EndCraftTrip(GameBot bot, string reason)
        {
            FinishCraftSession(bot, reason);
            _craftPhase = eCraftTripPhase.Done;
            AutonomousBotCrafting.MarkTripDone(bot);
            bot.StopMovingOnPath();
            bot.StopMoving();
            SetStatus(bot, "Done crafting", "Use or sell what was made", reason);
            return false;
        }

        /// <summary>The end of one session (one recipe, its copies): its best and seconds, its line; nothing when it made no attempt.</summary>
        private void FinishCraftSession(GameBot bot, string reason)
        {
            if (_craftSession != null && _craftAttempts > 0)
            {
                AutonomousBotCrafting.FinishSession(bot, _craftSession, _craftMade.ToArray(), _craftAttempts, reason);
                _craftSessionsDone++;
            }
            _craftSession = null;
            _craftAttempts = 0;
            _craftSessionLogged = false;
            _craftMade.Clear();
        }

        private bool BeginCraftShopping(GameBot bot)
        {
            long msLeft = BotCraftingRules.TripWorkMilliseconds - (GameLoop.GameLoopTime - _craftTripStartedTick);
            _craftSession = AutonomousBotCrafting.PlanSession(bot, msLeft, out bool deferred);
            if (deferred)
                return true; // the server's plans for this tick are used up: next turn
            if (_craftSession == null)
                return EndCraftTrip(bot, _craftSessionsDone > 0
                    ? $"Done after {_craftSessionsDone} session(s): nothing more fits the purse, the bag or the time"
                    : "Nothing to craft at this skill and purse");
            return NextCraftPurchase(bot);
        }

        private bool NextCraftPurchase(GameBot bot)
        {
            BotCraftingRules.Purchase purchase = AutonomousBotCrafting.ShoppingFor(bot, _craftSession).FirstOrDefault();
            if (purchase != null)
            {
                _craftPurchase = purchase;
                _craftMerchant = NearestByTravel(bot, AutonomousBotCrafting.Sellers(purchase.Material, bot.Realm));
                if (_craftMerchant == null)
                    return EndCraftTrip(bot, $"No merchant in reach sells {purchase.Material.Name}");
                SetCraftLeg(eCraftTripPhase.Shop);
                return true;
            }
            CraftStation station = BotCraftingRules.StationFor(_craftSession.Skill, _craftSession.Recipe.ObjectType,
                _craftSession.Recipe.Materials.Select(material => material.Model));
            _craftStation = station == CraftStation.None ? null : NearestByTravel(bot, AutonomousBotCrafting.Stations(station));
            if (station != CraftStation.None && _craftStation == null)
                return EndCraftTrip(bot, $"No {station} in reach");
            SetCraftLeg(eCraftTripPhase.Station);
            return true;
        }

        private T NearestByTravel<T>(GameBot bot, IEnumerable<T> stops) where T : GameObject
        {
            HashSet<ushort> reachable = ReachableRegions(bot.Realm, bot.CurrentRegionID);
            return stops
                .Where(stop => stop.ObjectState == GameObject.eObjectState.Active && reachable.Contains(stop.CurrentRegionID) &&
                               stop.CurrentZone != null && IsZoneAccessible(bot.Realm, stop.CurrentZone) && stop.CurrentZone.IsDungeon == false &&
                               !(stop is GameNPC npc && AutonomousServiceReachability.IsSkipped(AutonomousServiceReachability.Key(npc), bot.DatabaseID, GameLoop.GameLoopTime)))
                .OrderBy(stop => EstimateTravelMinutes(bot, stop.CurrentRegionID, stop.X, stop.Y))
                .FirstOrDefault();
        }

        /// <summary>One turn of walking to a stop; false once the bot stands within <paramref name="radius"/> of it, or gives up (DR).</summary>
        private bool WalkToCraftStop(GameBot bot, GameObject stop, int radius, string goal)
        {
            if (stop?.ObjectState is not GameObject.eObjectState.Active || GameLoop.GameLoopTime - _craftLegStartedTick > CraftLegMilliseconds)
            {
                if (stop is GameNPC npc)
                    AutonomousServiceReachability.ReportFailure(AutonomousServiceReachability.Key(npc), bot.DatabaseID, GameLoop.GameLoopTime);
                return false;
            }
            var destination = new CampDestination($"craft-{stop.ObjectID}", stop.Name, stop.CurrentZone?.Description ?? string.Empty,
                stop.CurrentRegionID, stop.X, stop.Y, stop.Z, 1, false, false);
            if (bot.CurrentRegionID != stop.CurrentRegionID)
            {
                CampDestination previous = _camp;
                _camp = destination;
                bool moving = TravelAcrossRegions(bot);
                _camp = previous;
                return moving;
            }
            if (bot.IsWithinRadius(stop, radius))
                return false;
            Vector3 raw = new(stop.X, stop.Y, stop.Z);
            if (!TryResolveConnectedApproach(bot, raw, Math.Max(16, radius - 16), out Vector3 point))
                point = raw;
            if (TryBeginFasterStableRoute(bot, point, stop.Name))
                return true;
            IssuePath(bot, point, preciseArrival: true);
            SetStatus(bot, $"Traveling to {stop.Name}", goal, "A craft trip between tasks", stop.Name, destination.ZoneName);
            return true;
        }
    }
}
