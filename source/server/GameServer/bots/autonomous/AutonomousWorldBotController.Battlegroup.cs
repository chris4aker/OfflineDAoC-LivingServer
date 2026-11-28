using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// #599 step 1: a battlegroup's follower group moves with its lead group. Its leader trails the lead's leader (path when
    /// more than <see cref="BattlegroupTrailStart"/> away, stop inside <see cref="BattlegroupTrailStop"/>; the goal held until the
    /// lead has moved <see cref="BattlegroupTrailRetargetDistance"/>, and a stuck trail leaves the battlegroup) and proposes the
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

        /// <summary>
        /// The trail's goal moves to the lead's spot only once the lead is this far from it (or the trailer has reached it),
        /// so a running lead does not hand the route a new goal every few seconds: the route's progress clock and its stall
        /// recovery run on one goal, and the trailer asks for a new path far less often.
        /// </summary>
        public const float BattlegroupTrailRetargetDistance = 1_500f;
        /// <summary>A trailing leader that walks this long without getting anywhere reports the group stuck (it leaves).</summary>
        public const long BattlegroupTrailStuckMilliseconds = 50_000L;
        /// <summary>Trail turns further apart than this start the trail over (a new link, or back from a fight).</summary>
        public const long BattlegroupTrailTurnGapMilliseconds = 5_000L;

        /// <summary>Whether the trail takes the lead's spot as its new goal: none held, the lead has gone
        /// <see cref="BattlegroupTrailRetargetDistance"/> beyond it, or the trailer is within the stop of it. Pure.</summary>
        public static bool BattlegroupTrailRetarget(Vector2? held, Vector2 lead, Vector2 trailer) =>
            held is not { } goal || Vector2.Distance(goal, lead) > BattlegroupTrailRetargetDistance ||
            Vector2.Distance(trailer, goal) <= BattlegroupTrailStop;

        /// <summary>Whether a walking trailer whose last progress (96 or more moved) was at <paramref name="progressTick"/>
        /// is stuck. Pure.</summary>
        public static bool BattlegroupTrailStuck(long progressTick, long now) =>
            progressTick > 0 && now - progressTick >= BattlegroupTrailStuckMilliseconds;

        /// <summary>After a refused trail path the trailer waits this long before it asks again (one look interval), so a
        /// trailer with no path does not ask for one every turn until the stuck report (#766, region 200).</summary>
        public const long BattlegroupTrailPathBackoffMilliseconds = 5_000L;

        /// <summary>Whether the trailer may ask for a path: none refused yet (0), or the back-off since the last refusal at
        /// <paramref name="refusedTick"/> is over. Pure.</summary>
        public static bool BattlegroupTrailMayAskPath(long refusedTick, long now) =>
            refusedTick == 0 || now - refusedTick >= BattlegroupTrailPathBackoffMilliseconds;

        private Vector3? _battlegroupTrailGoal;
        private Vector3 _battlegroupTrailMark;
        private long _battlegroupTrailProgressTick, _battlegroupTrailTurnTick, _battlegroupTrailRefusedTick;

        private bool TrailBattlegroupLead(GameBot bot, string forceId, string leadForce, GameBot lead)
        {
            AutonomousRvrEventLayer.ForceIntent leadIntent = AutonomousRvrEventLayer.IntentOf(leadForce);
            ProposeRvrIntent(bot, new(AutonomousRvrEventLayer.IntentSource.Battlegroup,
                leadIntent?.Intent ?? AutonomousRvrEventLayer.Intent.Roam, leadIntent?.ObjectiveId, false, null,
                "battlegroup.follow", $"trailing {leadForce}"), mirrorIntent: true, mirrorKeep: false);
            _rvrRuleHint = "battlegroup.follow";
            long now = GameLoop.GameLoopTime;
            if (now - _battlegroupTrailTurnTick > BattlegroupTrailTurnGapMilliseconds)
            {
                _battlegroupTrailGoal = null;
                _battlegroupTrailProgressTick = 0;
                _battlegroupTrailRefusedTick = 0;
            }
            _battlegroupTrailTurnTick = now;
            float distance = Vector2.Distance(new(bot.X, bot.Y), new(lead.X, lead.Y));
            if (BattlegroupTrailWalks(distance, bot.IsMoving))
            {
                Vector3 here = new(bot.X, bot.Y, bot.Z);
                if (BattlegroupTrailRetarget(_battlegroupTrailGoal is { } held ? new Vector2(held.X, held.Y) : null,
                        new(lead.X, lead.Y), new(bot.X, bot.Y)))
                    _battlegroupTrailGoal = new Vector3(lead.X, lead.Y, lead.Z);
                if (_battlegroupTrailProgressTick == 0 || Vector3.DistanceSquared(here, _battlegroupTrailMark) >= 96 * 96)
                {
                    _battlegroupTrailMark = here;
                    _battlegroupTrailProgressTick = now;
                }
                // A refused path counts against the stuck clock; the goal is picked again, and the next path is asked for
                // only after the back-off.
                if (BattlegroupTrailMayAskPath(_battlegroupTrailRefusedTick, now))
                {
                    if (!IssueVariedRvrPath(bot, _battlegroupTrailGoal.Value))
                    {
                        _battlegroupTrailGoal = null;
                        _battlegroupTrailRefusedTick = now;
                    }
                    else
                        _battlegroupTrailRefusedTick = 0;
                }
                if (BattlegroupTrailStuck(_battlegroupTrailProgressTick, now))
                {
                    AutonomousRvrEventLayer.ReportBattlegroupStuck(forceId);
                    _battlegroupTrailProgressTick = 0;
                }
                SetRvrStatus(bot, "Following the battlegroup lead", "Roam with the other groups of the battlegroup",
                    $"{(int)distance} behind the lead group", lead.Name);
                return true;
            }
            _battlegroupTrailGoal = null;
            _battlegroupTrailProgressTick = 0;
            _battlegroupTrailRefusedTick = 0;
            bot.StopMovingOnPath();
            bot.StopMoving();
            SetRvrStatus(bot, "With the battlegroup", "Roam with the other groups of the battlegroup",
                "Close behind the lead group", lead.Name);
            return true;
        }
    }
}
