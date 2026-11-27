using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// #599 step 1: a battlegroup's follower group moves with its lead group. Its leader trails the lead's leader (path when
    /// more than <see cref="BattlegroupTrailStart"/> away, stop inside <see cref="BattlegroupTrailStop"/>) and proposes the
    /// lead's intent and objective to the event layer (<c>IntentSource.Battlegroup</c>, rule <c>battlegroup.follow</c>), so
    /// the force record shows it. Enemies it sees or is called on are fought first, earlier in the RvR turn, as by every
    /// warband. A committed event plan, a rally and a relic carry come before this (the turn returns earlier), and the
    /// battlegroup layer drops a group that joins an event. Autonomous gamebots only.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        /// <summary>A trailing group's leader starts walking when the lead's leader is this far away...</summary>
        public const float BattlegroupTrailStart = 900f;
        /// <summary>...and stops once inside this (a loose column, not a block of sixteen).</summary>
        public const float BattlegroupTrailStop = 500f;

        /// <summary>Whether a trailing leader walks this turn: beyond the start, or still moving beyond the stop. Pure.</summary>
        public static bool BattlegroupTrailWalks(float distance, bool moving) =>
            distance > BattlegroupTrailStart || moving && distance > BattlegroupTrailStop;

        private bool TrailBattlegroupLead(GameBot bot, string leadForce, GameBot lead)
        {
            AutonomousRvrEventLayer.ForceIntent leadIntent = AutonomousRvrEventLayer.IntentOf(leadForce);
            ProposeRvrIntent(bot, new(AutonomousRvrEventLayer.IntentSource.Battlegroup,
                leadIntent?.Intent ?? AutonomousRvrEventLayer.Intent.Roam, leadIntent?.ObjectiveId, false, null,
                "battlegroup.follow", $"trailing {leadForce}"), mirrorIntent: true, mirrorKeep: false);
            _rvrRuleHint = "battlegroup.follow";
            float distance = Vector2.Distance(new(bot.X, bot.Y), new(lead.X, lead.Y));
            if (BattlegroupTrailWalks(distance, bot.IsMoving))
            {
                IssueVariedRvrPath(bot, new Vector3(lead.X, lead.Y, lead.Z));
                SetRvrStatus(bot, "Following the battlegroup lead", "Roam with the other groups of the battlegroup",
                    $"{(int)distance} behind the lead group", lead.Name);
                return true;
            }
            bot.StopMovingOnPath();
            bot.StopMoving();
            SetRvrStatus(bot, "With the battlegroup", "Roam with the other groups of the battlegroup",
                "Close behind the lead group", lead.Name);
            return true;
        }
    }
}
