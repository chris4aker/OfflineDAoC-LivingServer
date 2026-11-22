using System;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Scripts;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private OFTeleporter _frontierPorter;
    private GameMerchant _medallionMerchant;
    private long _nextPorterSearch;
    private Vector3? _frontierWaitingPoint;
    private long _nextWaitingPointSearch;
    private long _medallionNoRoomUntil;
    private string _frontierDecline = string.Empty;
    private long _frontierWaitSince;
    private long _frontierWaitLastNote;
    private long _frontierWaitLastReport;

    private bool TryReturnFromForeignFrontier(GameBot bot)
    {
        // PvE/services cannot walk through another realm's border gates to get
        // home. Use the same real porter/ticket boarding used by outbound RvR.
        ushort home = bot.Realm switch { eRealm.Albion => 1, eRealm.Midgard => 100, eRealm.Hibernia => 200, _ => 0 };
        if (home == 0 || bot.CurrentRegionID == home || bot.CurrentRegionID is not (1 or 100 or 200) ||
            AutonomousObjectiveAssignments.Parse(bot.PersistentRecord?.ObjectiveKind) == eAutonomousObjectiveKind.RvR ||
            GameRelic.IsPlayerCarryingRelic(bot)) return false;
        var passage = AutonomousFrontierTransport.Destination(bot.Realm, home);
        return passage != null && TryFrontierTransport(bot, new("return-home", passage.Location.Name, "frontier",
            home, passage.Location.X, passage.Location.Y, passage.Location.Z, bot.Level, false, true));
    }

    /// <summary>
    /// The first leg to another realm's frontier for a bot outside the three frontier regions (its capital after a release):
    /// its own frontier's border keep, where the porter and its medallion merchant stand. Null when the bot is already in a
    /// frontier region, the destination is no porter frontier, or its own frontier can't be reached from here either.
    /// </summary>
    private static CampDestination HomeFrontierLeg(GameBot bot, CampDestination destination)
    {
        ushort home = AutonomousFrontierTransport.HomeFrontier(bot.Realm);
        if (!AutonomousFrontierTransport.NeedsHomeFrontierFirst(bot.CurrentRegionID, destination.RegionId, home)) return null;
        var passage = AutonomousFrontierTransport.Destination(bot.Realm, home);
        if (passage == null || FindNextCrossing(bot, home, passage.Location.X, passage.Location.Y) == null) return null;
        return new("rvr-home-frontier", passage.Location.Name, "frontier",
            home, passage.Location.X, passage.Location.Y, passage.Location.Z, bot.Level, false, true);
    }

    /// <summary>Beyond this distance from the medallion merchant a bot enters the frontier on foot through its own border keep.</summary>
    private const int FrontierMedallionReach = 20_000;

    private bool TryFrontierTransport(GameBot bot, CampDestination destination)
    {
        _frontierDecline = "outside-the-frontiers-or-relic";
        if (bot.CurrentRegionID == destination.RegionId || bot.CurrentRegionID is not (1 or 100 or 200) ||
            destination.RegionId is not (1 or 100 or 200) || GameRelic.IsPlayerCarryingRelic(bot)) return false;
        var passage = AutonomousFrontierTransport.Destination(bot.Realm,destination.RegionId);
        _frontierDecline = "no-passage";
        if (passage == null) return false;
        bool returningToPve = passage.Medallion == "home_necklace" &&
            AutonomousObjectiveAssignments.Parse(bot.PersistentRecord?.ObjectiveKind) != eAutonomousObjectiveKind.RvR;
        var party=AutonomousFrontierTransport.BoardingParty(bot, passage);
        if(party.Any(GameRelic.IsPlayerCarryingRelic))
        {
            foreach(var member in party) member.TempProperties.RemoveProperty(AutonomousFrontierTransport.RequestKey);
            _frontierDecline = "relic-in-party";
            return false;
        }
        // A full backpack must not cancel every member's approach before the
        // owner can reach the real merchant and sell ordinary vendor trash.
        if (_frontierPorter?.ObjectState != GameObject.eObjectState.Active || _frontierPorter.CurrentRegion != bot.CurrentRegion)
        {
            _frontierDecline = "porter-search-cooling";
            if (GameLoop.GameLoopTime < _nextPorterSearch) return false;
            _nextPorterSearch = GameLoop.GameLoopTime + 30_000;
            // The nearest porter with its medallion merchant beside it, not simply the nearest (#418).
            _frontierPorter = AutonomousFrontierTransport.PorterFor(bot, passage);
            _medallionMerchant = null;
            _frontierWaitingPoint = null;
            _nextWaitingPointSearch = 0;
        }
        _frontierDecline = "no-porter-in-region";
        if (_frontierPorter == null) return false;
        // Declare the transport intent before approaching its merchant. A
        // returning PvE bot may need its own portal-keep door to BUY the ticket.
        // Boarding still requires the real purchased medallion and all safety checks.
        bot.TempProperties.SetProperty(AutonomousFrontierTransport.RequestKey,new AutonomousFrontierTransport.Request(
            _frontierPorter,passage,bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}"));
        if (AutonomousFrontierTransport.Ticket(bot,passage) == null)
        {
            // A full bag with nothing to sell: stop walking back to the merchant for a while (it looped between the
            // merchant and its crossing), and let the bot's other plans and the watchdog take over.
            _frontierDecline = "full-bag-backoff";
            if (GameLoop.GameLoopTime < _medallionNoRoomUntil) return false;
            if (_medallionMerchant?.ObjectState != GameObject.eObjectState.Active ||
                !_medallionMerchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().Any(item=>item.Id_nb==passage.Medallion))
                _medallionMerchant = AutonomousFrontierTransport.MedallionMerchantNear(_frontierPorter, bot.Realm, passage.Medallion);
            _frontierDecline = $"no-medallion-merchant-at:{_frontierPorter.Name}";
            if (_medallionMerchant == null) return false;
            // Each realm sells frontier medallions only at its first border keep (Druim Ligen, Castle Sauvage, Svasud
            // Faste). A bot staged at the other border keep walks into the frontier through that keep's gate, as players
            // do, instead of crossing the realm for a medallion (run 24: Hibernians at Druim Cain stood still for 15
            // minutes "collecting a frontier medallion" 110,000 units from Araisa).
            // Another realm's frontier has no walking route since the dungeon hub stopped being a hallway
            // (AutonomousDungeonPolicy.MayRouteThrough), so a distant warband rides or walks to its porter.
            _frontierDecline = "walks-in";
            if (bot.GetDistanceTo(_medallionMerchant) > FrontierMedallionReach &&
                FindNextCrossing(bot, destination.RegionId, destination.X, destination.Y) != null) return false;
            // A ride (town teleporter or horse route) when clearly faster than walking to the merchant.
            if (TryBeginFasterStableRoute(bot, new(_medallionMerchant.X, _medallionMerchant.Y, _medallionMerchant.Z), _medallionMerchant.Name))
                return true;
            if (!ApproachSupplyMerchant(bot, _medallionMerchant))
            {
                SetRvrStatus(bot,"Collecting frontier medallion",destination.MonsterName,$"Walking to {_medallionMerchant.Name} for {passage.Medallion}");
                return true;
            }
            var template = _medallionMerchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().First(item=>item.Id_nb==passage.Medallion);
            var slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack,eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid)
            {
                // A full backpack after RvR must not force a walk through enemy
                // gates. Sell one ordinary disposable item at this real merchant;
                // never delete an item or grant a free ticket/teleport.
                var trash = AutonomousBotEconomy.FindVendorTrashCandidate(bot);
                bool sold = trash != null && AutonomousBotEconomy.TrySellToVendor(bot, _medallionMerchant, trash, out _);
                if (AutonomousFrontierTransport.KeepWaitingForMedallionRoom(true, sold))
                {
                    SetRvrStatus(bot, "Making room for frontier medallion", destination.MonsterName,
                        "At the real merchant; preserving equipped items and protected upgrades");
                    NoteFrontierWait(bot, "making-room", destination, passage);
                    return true;
                }
                _medallionNoRoomUntil = GameLoop.GameLoopTime + AutonomousFrontierTransport.MedallionNoRoomBackoffMilliseconds;
            }
            _frontierDecline = slot == eInventorySlot.Invalid ? "bag-full" : "cannot-pay";
            if (slot==eInventorySlot.Invalid || template.Price<0 || template.Price>0 && !AutonomousBotEconomy.TrySpend(bot.DatabaseID,template.Price))
                return false; // Existing legal dungeon itinerary remains available.
            var ticket = GameInventoryItem.Create(template);
            if (ticket == null || !bot.Inventory.AddItem(slot,ticket))
            {
                if(template.Price>0) AutonomousBotEconomy.AddMoney(bot.DatabaseID,template.Price);
                _frontierDecline = "ticket-not-added";
                return false;
            }
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            AutonomousBotStatusPersistence.Queue(bot,true);
        }
        // Ordinary warbands retain their shared departure. Committed siege
        // responders board independently; defenders receive a bounded priority slice.
        if (!_frontierWaitingPoint.HasValue && GameLoop.GameLoopTime >= _nextWaitingPointSearch)
        {
            _nextWaitingPointSearch = GameLoop.GameLoopTime + 10_000;
            if (AutonomousFrontierTransport.TryWaitingPoint(bot, _frontierPorter, out var waiting))
                _frontierWaitingPoint = waiting;
        }
        if (!_frontierWaitingPoint.HasValue)
        {
            SetRvrStatus(bot,"Finding teleporter waiting space",destination.MonsterName,"Waiting for a reachable spot clear of the teleporter");
            NoteFrontierWait(bot, "finding-space", destination, passage);
            return true;
        }
        if (Vector3.Distance(new(bot.X,bot.Y,bot.Z),_frontierWaitingPoint.Value) > 40)
            IssuePath(bot,_frontierWaitingPoint.Value);
        else { bot.StopMovingOnPath(); bot.StopMoving(); }
        AutonomousFrontierTransport.WakeBoardingPorter(bot,
            bot.TempProperties.GetProperty<AutonomousFrontierTransport.Request>(AutonomousFrontierTransport.RequestKey));
        SetRvrStatus(bot,"Boarding frontier teleporter",destination.MonsterName,
            AutonomousFrontierTransport.HasDefenderPriority(bot, passage)
                ? $"At {_frontierPorter.Name}; priority defense departure to {passage.Location.Name}"
                : AutonomousFrontierTransport.HasCommittedSiegePassage(bot, passage)
                    ? $"At {_frontierPorter.Name}; boarding independently for the siege at the next departure to {passage.Location.Name}"
                    : $"At {_frontierPorter.Name}; waiting for the warband and the native departure to {passage.Location.Name}");
        NoteFrontierWait(bot, "boarding", destination, passage);
        return true;
    }

    /// <summary>
    /// Called on every turn a bot spends at a frontier porter without leaving (the walk to its medallion merchant is not a
    /// wait: it moves, and the watchdog sees that). A wait past
    /// <see cref="AutonomousFrontierTransport.LongWaitMilliseconds"/> logs RVR_FRONTIER_WAIT_LONG with what holds it: the
    /// porter and its distance, the ticket, the waiting spot, the first boarding check the bot fails (notReady=), its
    /// warband members that are not ready and why, how long the warband has held it, and how long ago the porter last called
    /// its passengers (ceremony=). Issue #418: some gamebots waited at frontier teleporters until the watchdog moved them,
    /// and no line said why.
    /// </summary>
    private void NoteFrontierWait(GameBot bot, string phase, CampDestination destination, AutonomousFrontierTransport.Passage passage)
    {
        long now = GameLoop.GameLoopTime;
        if (!AutonomousFrontierTransport.WaitContinues(_frontierWaitLastNote, now))
        {
            _frontierWaitSince = now;
            _frontierWaitLastReport = 0;
        }
        _frontierWaitLastNote = now;
        if (!AutonomousFrontierTransport.LongWaitReportDue(_frontierWaitSince, _frontierWaitLastReport, now) || !Log.IsWarnEnabled)
            return;
        _frontierWaitLastReport = now;
        try
        {
            var request = bot.TempProperties.GetProperty<AutonomousFrontierTransport.Request>(AutonomousFrontierTransport.RequestKey);
            passage ??= request?.Passage ?? (destination == null ? null : AutonomousFrontierTransport.Destination(bot.Realm, destination.RegionId));
            OFTeleporter porter = request?.Porter ?? _frontierPorter;
            string porterText = porter == null ? "none" : $"\"{porter.Name}\" porterDist={(int)bot.GetDistanceTo(porter)}";
            string notReady = phase == "waiting" ? $"declined:{_frontierDecline}"
                : AutonomousFrontierTransport.NotReadyReason(bot, porter, request) ?? "ready";
            string ticket = passage == null ? "n/a" : AutonomousFrontierTransport.Ticket(bot, passage) != null ? "yes" : "no";
            string spot = _frontierWaitingPoint is Vector3 point
                ? ((int)Vector3.Distance(new(bot.X, bot.Y, bot.Z), point)).ToString() : "none";
            string blocking = "none";
            int party = 1;
            if (passage != null && porter != null)
            {
                GameBot[] members = AutonomousFrontierTransport.BoardingParty(bot, passage);
                party = members.Length;
                var held = members.Where(member => member != bot && member.CurrentRegionID != passage.Region)
                    .Select(member =>
                    {
                        var memberRequest = member.TempProperties.GetProperty<AutonomousFrontierTransport.Request>(
                            AutonomousFrontierTransport.RequestKey);
                        string reason = memberRequest == null ? "no-request"
                            : memberRequest.Passage.Region != passage.Region ? $"other-passage-{memberRequest.Passage.Region}"
                            : AutonomousFrontierTransport.NotReadyReason(member, porter, memberRequest);
                        return reason == null ? null : $"{member.Name}:{reason}";
                    })
                    .Where(text => text != null).ToList();
                if (held.Count > 0) blocking = $"{held.Count}[{string.Join(",", held.Take(4))}]";
            }
            Log.Warn($"RVR_FRONTIER_WAIT_LONG bot={bot.Name} id={bot.DatabaseID} level={bot.Level} " +
                     $"realm={GlobalConstants.RealmToName(bot.Realm)} phase={phase} waited={(now - _frontierWaitSince) / 1000}s " +
                     $"at={bot.CurrentRegionID}:{bot.X},{bot.Y},{bot.Z} to={destination?.RegionId}:\"{destination?.MonsterName}\" " +
                     $"passage=\"{passage?.Location.Name ?? "none"}\" porter={porterText} ticket={ticket} spotDist={spot} " +
                     $"notReady={notReady} party={party} notReadyMembers={blocking} " +
                     $"heldForWarband={AutonomousFrontierTransport.SecondsHeldForWarband(bot)}s " +
                     $"ceremony={AutonomousFrontierTransport.SecondsSinceCeremony(porter)}s " +
                     $"objective={AutonomousObjectiveAssignments.KindFor(bot)} grouped={(bot.Group != null ? "yes" : "no")}");
        }
        catch (Exception error)
        {
            Log.Warn($"RVR_FRONTIER_WAIT_LONG bot={bot.Name} id={bot.DatabaseID} phase={phase} (details failed: {error.Message})");
        }
    }
}
