// Hand port from shadowofze/OfflineDAoC-True165 d2187a498 (BotBrain.Flank.cs, by shadowofze, GPL-3.0), first added
// there in 7ac71824. Ported for Living Server 1.2.1 ("positional styles, step 2"): only the gamebot rule. Dropped: the
// True 1.65 Mode gate (Classic165Mode) and the companion "Positioning" option (CompanionOptions165); companions never
// flank here. Added: raids, epic encounters, player-led groups and tanks are left alone, the style filter is ours
// (BotMeleeStylePolicy), and the bot only walks to a flank spot the navigation mesh says it can reach on foot.
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS;
using DOL.GS.Styles;

namespace DOL.AI.Brain
{
    /// <summary>
    /// A damage-dealing autonomous gamebot steps to the back (or side) of a monster that is fighting someone else, so its
    /// back and side styles and the chains after them fire (True165's class audit, 2026-11-05: Prima has Berserkers,
    /// Blademasters, Mercenaries and Infiltrators attack from behind while the tank holds the monster). It tries for at
    /// most four seconds, only when it knows such a style for the weapon in hand, and tries again when the monster has
    /// turned more than 45 degrees or moved more than 100 units, at most every three seconds.
    /// Never: tanks (they keep facing their monster), companions and any bot in a player-led group, raid parties, raid
    /// dungeons and epic encounters (EpicEncounterTactics and its stations and blast dodges place raiders there).
    /// </summary>
    public partial class BotBrain
    {
        public const long FlankMilliseconds = 4_000;
        public const int FlankStandOff = 80;
        public const long FlankRetryMilliseconds = 3_000;
        public const int FlankRetryTurn = 512;      // 45 degrees in heading units (4096 a circle)
        public const int FlankRetryMove = 100;
        /// <summary>Only a monster this close is flanked; farther away the bot first closes in as usual.</summary>
        public const int FlankReach = 400;
        /// <summary>A surface walk that ends this close (2D) to the flank spot reaches it.</summary>
        public const float FlankSpotTolerance = 24f;
        /// <summary>A flank spot this far above or below the bot is another floor: not taken.</summary>
        public const float FlankSpotMaxRise = 160f;

        private GameLiving _flankTarget;
        private GameLiving _flankDoneFor;
        private long _flankSince;
        private long _flankDoneAt;
        private ushort _flankDoneHeading;
        private Vector2 _flankDonePosition;

        /// <summary>
        /// Pure rule: whether a bot steps to the flank of a monster that fights someone else. Only an autonomous gamebot of
        /// its own (not a companion or a player's temporary helper), not in a player-led group, not a tank class, and not in
        /// a raid or an epic encounter.
        /// </summary>
        public static bool FlankAllowed(bool gamebot, bool playerLedGroup, bool tankClass, bool raidOrEpic) =>
            gamebot && !playerLedGroup && !tankClass && !raidOrEpic;

        /// <summary>Pure rule: a finished flank is tried again once the monster turned or moved away from it.</summary>
        public static bool FlankRetry(long sinceDone, ushort headingThen, ushort headingNow, float moved)
        {
            if (sinceDone < FlankRetryMilliseconds)
                return false;
            int turn = System.Math.Abs(headingNow % 4096 - headingThen % 4096);
            return System.Math.Min(turn, 4096 - turn) > FlankRetryTurn || moved > FlankRetryMove;
        }

        /// <summary>Pure: whether a bot at <paramref name="angle"/> degrees from where the monster faces may use a style for
        /// <paramref name="position"/>; the same windows as StyleProcessor.CanUseStyle.</summary>
        public static bool InFlankPosition(float angle, Style.eOpeningPosition position) =>
            position == Style.eOpeningPosition.Back
                ? angle is > 150 and < 210
                : angle is >= 45 and <= 150 or >= 210 and <= 315;

        /// <summary>Pure: the heading from the monster to the flank spot. Straight behind for a back style; for a side
        /// style the side the bot is already nearer to.</summary>
        public static ushort FlankHeading(ushort monsterHeading, float angleToBot, Style.eOpeningPosition position) =>
            position == Style.eOpeningPosition.Back
                ? (ushort)((monsterHeading + 2048) % 4096)
                : (ushort)((monsterHeading + (angleToBot is > 180 and < 360 ? 3072 : 1024)) % 4096);

        /// <summary>Pure: whether a surface walk toward <paramref name="spot"/> that ended at <paramref name="walked"/>
        /// reached it on the bot's own floor. No walk (no mesh) never reaches it.</summary>
        public static bool FlankSpotReached(Vector3 spot, Vector3? walked, float fromZ) =>
            walked is Vector3 w &&
            Vector2.DistanceSquared(new(w.X, w.Y), new(spot.X, spot.Y)) <= FlankSpotTolerance * FlankSpotTolerance &&
            System.Math.Abs(w.Z - fromZ) <= FlankSpotMaxRise;

        /// <summary>A back or side style the bot may use: ours, not a stealth opener, not a taunt.</summary>
        public static bool IsFlankStyle(Style style) => BotMeleeStylePolicy.IsEligible(style) &&
            BotMeleeStylePolicy.IsBackOrSide(style) && !BotMeleeStylePolicy.IsTauntStyle(style);

        /// <summary>The back style (preferred) or side style this bot could use on its target with the weapon in hand.</summary>
        private Style FlankStyle()
        {
            GameBot bot = BotBody;
            if (bot?.Styles == null)
                return null;
            DbInventoryItem left = bot.Inventory?.GetItem(eInventorySlot.LeftHandWeapon);
            return bot.Styles
                .Where(style => IsFlankStyle(style) && style.Level <= bot.Level &&
                                BotMeleeStylePolicy.MatchesWeapon(style, bot.ActiveWeapon, left, bot.ActiveWeaponSlot))
                .OrderBy(style => (Style.eOpeningPosition)style.OpeningRequirementValue == Style.eOpeningPosition.Back ? 0 : 1)
                .ThenByDescending(style => style.GrowthRate)
                .FirstOrDefault();
        }

        /// <summary>Raid parties, raid dungeons and epic encounters: their own tactics place the raiders.</summary>
        private static bool InRaidOrEpicFight(GameBot bot, GameNPC target) =>
            EpicEncounterTactics.For(target) != null || EpicEncounterTactics.IsTacticPart(target) ||
            EpicEncounterTactics.IsHazard(target) || AutonomousRealmRaid.IsRaidDungeon(bot.CurrentRegionID) ||
            bot.Group != null && AutonomousRealmRaid.GetView(bot.Group) != null;

        /// <summary>Where the bot would stand on its own floor, or null when the mesh has no walk there.</summary>
        private static Vector3? ReachableFlankSpot(GameBot bot, Vector3 spot)
        {
            Zone zone = bot.CurrentZone;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            if (zone == null || nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return null;
            Vector3? walked = AutonomousNavigationSurface.MoveAlongGround(nav, zone, new(bot.X, bot.Y, bot.Z), spot);
            return FlankSpotReached(spot, walked, bot.Z) ? walked : null;
        }

        /// <summary>Walks the bot to the flank of its target; true while that walk owns the turn.</summary>
        private bool TryFlank(GameLiving target)
        {
            GameBot bot = BotBody;
            if (target != null && target == _flankDoneFor && FlankRetry(
                    GameLoop.GameLoopTime - _flankDoneAt, _flankDoneHeading, target.Heading,
                    Vector2.Distance(_flankDonePosition, new(target.X, target.Y))))
                _flankDoneFor = null;
            if (bot == null || target is not GameNPC monster || target is GameBot || !target.IsAlive ||
                target == _flankDoneFor || PrefersCurrentSpellRange() ||
                bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance || bot.IsStealthed ||
                target.TargetObject is not GameLiving victim || victim == bot || !victim.IsAlive ||
                !bot.IsWithinRadius(target, FlankReach) ||
                !FlankAllowed(bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper, bot.IsPlayerLedGroup,
                    BotPartyRoles.IsTank(bot), InRaidOrEpicFight(bot, monster)))
            {
                _flankTarget = null;
                return false;
            }
            Style style = FlankStyle();
            if (style == null)
                return Done();
            var position = (Style.eOpeningPosition)style.OpeningRequirementValue;
            float angle = target.GetAngle(bot);
            if (InFlankPosition(angle, position))
                return Done();

            long now = GameLoop.GameLoopTime;
            if (_flankTarget != target)
            {
                _flankTarget = target;
                _flankSince = now;
            }
            if (now - _flankSince > FlankMilliseconds)
                return Done();

            Point2D spot2 = target.GetPointFromHeading(FlankHeading(target.Heading, angle, position), FlankStandOff);
            if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(spot2.X, spot2.Y)) <= 30 * 30)
                return Done();
            // No navmesh change and no guess: a spot the bot cannot walk to on its own floor is not taken.
            if (ReachableFlankSpot(bot, new Vector3(spot2.X, spot2.Y, target.Z)) is not Vector3 spot)
                return Done();
            if (bot.attackComponent.AttackState)
                bot.StopAttack();
            bot.PathTo(spot, bot.MaxSpeed);
            return true;

            bool Done()
            {
                _flankDoneFor = target;
                _flankDoneAt = GameLoop.GameLoopTime;
                _flankDoneHeading = target.Heading;
                _flankDonePosition = new(target.X, target.Y);
                _flankTarget = null;
                if (bot.attackComponent.AttackState == false && bot.TargetObject == target)
                    bot.TurnTo(target);
                return false;
            }
        }
    }
}
