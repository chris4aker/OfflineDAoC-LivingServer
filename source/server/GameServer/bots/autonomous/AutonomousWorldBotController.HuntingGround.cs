using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// #570 step 3: the controller side of <see cref="AutonomousHuntingGround"/> (solo autonomous gamebots only). Each
    /// method is called from one place in AutonomousWorldBotController.cs and does nothing with the switch off, for a
    /// bot in a group or a dynamic party, in a dungeon or in Darkness Falls.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        private long _nextAreaHuntLogTick;
        /// <summary>The next solo plan prefers a camp of blue or harder typical con (the bot outleveled its last one).</summary>
        private bool _moveUpNextPlan;

        private bool IsSoloHunter(GameBot bot) =>
            bot.Group == null && _groupDirective?.IsDynamic != true && bot.IsAutonomousWorldBot;

        /// <summary>
        /// In the camp loop, before the target search: most of the last kills here conned green, so the bot moves up a
        /// spot as a player did. Out of combat only. True when it gave the camp up.
        /// </summary>
        private bool TryMoveUpSpot(BotBrain brain, GameBot bot)
        {
            if (!AutonomousHuntingGround.Enabled || _camp == null || !IsSoloHunter(bot) || _camp.IsDungeon ||
                _camp.RegionId == AutonomousDarknessFallsPolicy.RegionId || brain.HasAggro || bot.InCombat ||
                !AutonomousHuntingGround.TakeMoveUp(bot.DatabaseID))
                return false;
            Log.Info($"AUTONOMOUS_CAMP_MOVE_UP bot={bot.Name} id={bot.DatabaseID} level={bot.Level} " +
                     $"class=\"{bot.ClassName}\" camp=\"{_camp.Id}\" target=\"{_camp.MonsterName}\" " +
                     $"green={AutonomousHuntingGround.GreenKillsToMoveUp}/{AutonomousHuntingGround.KillWindow}");
            // not re-picked for a while, as an outleveled camp (#535)
            _recentFailedSoloCamps[_camp.Id] = GameLoop.GameLoopTime + AutonomousDeathRecoveryPolicy.FailureMemoryMilliseconds;
            _moveUpNextPlan = true;
            AbandonCamp(bot, $"Outleveled most {_camp.MonsterName}; moving up a spot");
            return true;
        }

        /// <summary>The solo planner's legal camps after a move-up: blue or harder typical con when any is (once).</summary>
        private IEnumerable<AutonomousBotDecisionEngine.Camp> ApplyMoveUp(IEnumerable<AutonomousBotDecisionEngine.Camp> legal)
        {
            if (!_moveUpNextPlan)
                return legal;
            _moveUpNextPlan = false;
            if (!AutonomousHuntingGround.Enabled)
                return legal;
            return AutonomousHuntingGround.MoveUpCamps(legal.ToArray(), camp => camp.TypicalCon);
        }

        /// <summary>
        /// After the spread weights: a solo bot's outdoor camps weigh by its hunting zone (same zone x8, neighbour x3,
        /// rest of the region x1, another region x0.25). Dungeon camps keep their weight (the dungeon-or-outdoor roll came
        /// first and the two never share a draw), so Darkness Falls and the dungeons stay as open to solo bots as before.
        /// </summary>
        private AutonomousBotDecisionEngine.Camp[] WeighHuntingGround(GameBot bot, AutonomousBotDecisionEngine.Camp[] cells,
            Dictionary<string, CampDestination> destinations)
        {
            if (!AutonomousHuntingGround.Enabled || !IsSoloHunter(bot) || cells.Length < 2)
                return cells;
            var home = AutonomousHuntingGround.Home(bot.DatabaseID);
            Zone homeZone = home.Region != 0 ? WorldMgr.GetZone(home.Zone) : null;
            if (homeZone == null)
                return cells;
            return cells.Select(camp =>
            {
                if (camp.IsDungeon || !destinations.TryGetValue(camp.Id, out CampDestination d))
                    return camp;
                Zone campZone = WorldMgr.GetRegion(d.RegionId)?.GetZone(d.X, d.Y);
                double factor = AutonomousHuntingGround.ZoneFactor(home.Region, home.Zone, d.RegionId, campZone?.ID ?? 0,
                    AutonomousHuntingGround.AreNeighbours(homeZone, campZone));
                int weight = camp.SpreadWeight > 0 ? camp.SpreadWeight : AutonomousBotDecisionEngine.OutdoorSpawnWeight(camp);
                return camp with { SpreadWeight = AutonomousHuntingGround.Weigh(weight, factor) };
            }).ToArray();
        }

        /// <summary>An outdoor solo camp makes its zone the bot's hunting ground.</summary>
        private void NoteHuntingGround(GameBot bot, CampDestination selected, bool sharedGroup)
        {
            if (!AutonomousHuntingGround.Enabled || sharedGroup || !IsSoloHunter(bot) || selected.IsDungeon ||
                selected.RegionId == AutonomousDarknessFallsPolicy.RegionId)
                return;
            if (WorldMgr.GetRegion(selected.RegionId)?.GetZone(selected.X, selected.Y) is { } zone)
                AutonomousHuntingGround.NoteSoloCamp(bot.DatabaseID, selected.Id, selected.RegionId, zone.ID);
        }

        /// <summary>Area hunting applies at this camp for this bot (solo, outdoors, not Darkness Falls).</summary>
        private bool AreaHuntingApplies(GameBot bot) =>
            _camp != null && AutonomousHuntingGround.AreaHuntingApplies(AutonomousHuntingGround.Enabled, IsSoloHunter(bot),
                bot.CurrentZone?.IsDungeon == true || _camp.IsDungeon, _camp.RegionId == AutonomousDarknessFallsPolicy.RegionId);

        /// <summary>The camp target filter when the camp's own monster is gone: a named monster within the camp area that
        /// cons green up to the bot's solo ceiling and that it did not fail against lately.</summary>
        private bool IsAreaHuntTarget(GameBot bot, GameNPC npc, long nowTick)
        {
            if (npc.EffectiveLevel < 0 || string.IsNullOrWhiteSpace(npc.Name) ||
                !AutonomousHuntingGround.WithinCampArea(_camp.X, _camp.Y, npc.X, npc.Y) ||
                _recentFailedSoloTargets.TryGetValue(npc.Name, out long failedUntil) && failedUntil > nowTick)
                return false;
            ConColor ceiling = (ConColor)Math.Max((int)MaximumTargetCon(1), (int)(_camp.FallbackMaximumCon ?? ConColor.GREEN));
            return AutonomousHuntingGround.AreaConAllowed(ConLevels.GetConColor(bot.GetConLevel(npc)), ceiling);
        }

        private void LogAreaHunt(GameBot bot, GameNPC found)
        {
            if (found == null || GameLoop.GameLoopTime < _nextAreaHuntLogTick)
                return;
            _nextAreaHuntLogTick = GameLoop.GameLoopTime + 60_000;
            Log.Debug($"AUTONOMOUS_AREA_HUNT bot={bot.Name} id={bot.DatabaseID} level={bot.Level} camp={_camp.Id} " +
                      $"assigned=\"{_camp.MonsterName}\" target=\"{found.Name}\" level_target={found.EffectiveLevel}");
        }
    }
}
