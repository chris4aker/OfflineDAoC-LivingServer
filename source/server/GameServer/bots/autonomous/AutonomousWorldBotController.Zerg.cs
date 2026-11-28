using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// #599 step 3 (#530 step 5d): the realm's zerg in the controller. Its lead group's leader walks to the zerg's gate (a
    /// milegate or the road by one, AutonomousRvrEventLayer.ZergAnchors), mills about there, and reports its arrival so the
    /// event layer rolls it on to the next gate after the hold: slow (no faster stable route) and near the gates, as period
    /// zergs were ("Zergs move slow and rarely venture too far from their spawn point", reference.md §2.1). A recruit whose
    /// group stands in another region travels to the zerg's gate first; in the lead's region it trails the lead (the step 1
    /// trail). Enemies seen or called are fought first, earlier in the turn, as by every warband; a committed event plan, a
    /// rally, a relic carry and a shared event come before this. The lead's own warband review still runs on its timer
    /// (<see cref="ZergLeadReview"/>): when it opens or joins a keep or relic event, the whole zerg goes (its followers trail
    /// the lead in), as period zergs went "at a milegate or keep" (reference.md §2.3, §5.5). Autonomous gamebots only.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        /// <summary>The lead counts as at its gate inside this (the RvR turn's own arrival distance).</summary>
        public const float ZergAnchorArrival = 650f;

        /// <summary>
        /// Who takes a zerg turn: the lead's leader when the zerg has a gate; a recruit's leader when its group stands in
        /// another region than the zerg's (it travels there). Anything else (a recruit in the zerg's region whose lead's
        /// leader is not about, a zerg with no gate) goes on with the warband's own turn. Pure.
        /// </summary>
        public static bool ZergTurnApplies(bool isLead, bool hasAnchor, ushort botRegion, ushort zergRegion) =>
            hasAnchor && (isLead || botRegion != zergRegion);

        /// <summary>Whether the zerg's lead reviews its objective now: its warband's review timer is due and it works no siege engine. Pure.</summary>
        public static bool ZergLeadReviewDue(bool isLead, long now, long nextReview, bool siegeAssigned) =>
            isLead && now >= nextReview && !siegeAssigned;

        /// <summary>
        /// The zerg lead's warband review (ChooseRvrDestination, the only path that opens or joins an attack). When it takes a
        /// keep or relic event (<see cref="AutonomousRvrEventLayer.ZergReviewJoins"/>), the destination is the event's, the
        /// event layer is told (the zerg is not ended by its lead being in it; the next pulse puts the followers on the
        /// event's roster under its cap, AutonomousRvrEventLayer.EnrolZergFollowers), and the warband's own turn goes on with
        /// it: true. Another shared event the review joined (a relic carrier's escort, another frontier's siege) is the lead's
        /// own: the destination is set and the turn goes on with it (true), and the next pulse ends the zerg (why=event).
        /// Anything else (a roam, a hunt) leaves the zerg at its gates: false.
        /// </summary>
        private bool ZergLeadReview(GameBot bot, string forceId, ushort zergRegion, long now)
        {
            _nextRvrPlanReview = now + 45_000 + bot.ObjectID % 15_000;
            CampDestination reviewed = ChooseRvrDestination(bot);
            if (reviewed == null || !_rvrSharedEvent)
                return false;
            _rvrDestination = reviewed;
            _rvrApproachDestination = null;
            _patrolDestination = null;
            _campStartedTick = now;
            if (AutonomousRvrEventLayer.ZergReviewJoins(_rvrIntent, _rvrSharedEvent, reviewed.RegionId, zergRegion) &&
                AutonomousRvrEventLayer.IsSiegeEvent(reviewed.Id, now))
                AutonomousRvrEventLayer.ReportZergAttack(forceId, reviewed.Id, now);
            return true;
        }

        private bool TakeZergTurn(GameBot bot, string forceId, AutonomousRvrEventLayer.ZergOrder zerg)
        {
            if (!ZergTurnApplies(zerg.IsLead, zerg.Anchor != null, bot.CurrentRegionID, zerg.Region))
                return false;
            // The lead's review: a keep or relic event takes the whole zerg (the warband's own turn goes on with it).
            if (ZergLeadReviewDue(zerg.IsLead, GameLoop.GameLoopTime, _nextRvrPlanReview, BotSiegeRuntime.Assigned(bot)) &&
                ZergLeadReview(bot, forceId, zerg.Region, GameLoop.GameLoopTime))
                return false;
            AutonomousRvrEventLayer.ZergAnchor anchor = zerg.Anchor;
            string rule = zerg.IsLead ? "zerg.anchor" : "zerg.join";
            ProposeRvrIntent(bot, new(AutonomousRvrEventLayer.IntentSource.Zerg, AutonomousRvrEventLayer.Intent.Roam, zerg.ZergId,
                false, null, rule, zerg.IsLead ? $"holding {anchor.SpotId}" : $"joining {zerg.ZergId}"), mirrorIntent: true, mirrorKeep: false);
            _rvrRuleHint = rule;
            long now = GameLoop.GameLoopTime;
            string id = $"zerg-anchor-{anchor.SpotId}";
            if (_rvrDestination?.Id != id)
            {
                _rvrDestination = new(id, anchor.Label, anchor.Label, anchor.Region, (int)anchor.X, (int)anchor.Y, (int)anchor.Z,
                    1, false, true);
                _rvrApproachDestination = null;
                _patrolDestination = null;
                _campStartedTick = now;
            }
            // A recruit on its way does not review (the zerg is its objective); the lead's review runs above, on its timer.
            if (!zerg.IsLead)
                _nextRvrPlanReview = now + 45_000;
            if (bot.CurrentRegionID != anchor.Region)
            {
                SetRvrStatus(bot, "Joining the realm's zerg", "Travel to the zerg at the gates", $"The zerg holds {anchor.Label}",
                    anchor.Label);
                return TravelRvrObjective(bot, _rvrDestination);
            }
            float distance = Vector2.Distance(new(bot.X, bot.Y), new(anchor.X, anchor.Y));
            if (distance > ZergAnchorArrival)
            {
                // The zerg walks: no faster stable route (period zergs had "little speed", reference.md §6).
                IssueVariedRvrPath(bot, new Vector3(anchor.X, anchor.Y, anchor.Z));
                SetRvrStatus(bot, $"Leading the realm's zerg to {anchor.Label}", "Hold the gates with the realm's zerg",
                    $"{(int)distance} from {anchor.Label}", anchor.Label);
                return true;
            }
            AutonomousRvrEventLayer.ReportZergAtAnchor(forceId, anchor.SpotId, now);
            PatrolRvr(bot);
            SetRvrStatus(bot, $"Holding {anchor.Label} with the zerg", "Hold the gates with the realm's zerg",
                "Waiting at the gate for the enemy", anchor.Label);
            return true;
        }
    }
}
