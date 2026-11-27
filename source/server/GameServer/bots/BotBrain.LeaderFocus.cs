using System;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// #571 step 1, leader focus (port of rambodolphin13/DividedRealms <c>ad8e087c</c>, bots/BotBrain.LeaderFocus.cs,
    /// GPL-3.0; rules in <see cref="CompanionLeaderFocus"/>). Before it, the target choice in AttackMostWanted was
    /// "protection, else whoever hit me in the last 5 s, else my pet's fight, else my top threat": the player's attack put
    /// its target on every companion's aggro list (AssistPlayerAttack), but whatever hit a companion took it straight back,
    /// so a group's damage spread over a pack or an enemy group instead of landing on the player's target.
    /// </summary>
    public partial class BotBrain
    {
        private GameLiving _lastLeaderFocusLogged;

        /// <summary>
        /// The player's target, when leader focus applies to this bot and takes it; otherwise null (the choice stays as
        /// before). Puts the target on the aggro list, as an assist does, so the attack turn keeps it.
        /// </summary>
        internal GameLiving LeaderFocusTarget()
        {
            GameBot bot = BotBody;
            if (bot == null || !CompanionLeaderFocus.AppliesTo(CompanionLeaderFocus.On, bot.IsPlayerLedGroup,
                    BotPartyRoles.IsTank(bot), BotPartyRoles.IsSupport(bot)))
                return null;
            // PvE and RvR alike (CompanionLeaderFocus.InPve, owner 2026-11-27, #571).
            if (!CompanionLeaderFocus.InPlace(AutonomousWorldBotController.IsBattleground(bot)))
                return null;
            if (bot.PlayerGroupLeader is not GamePlayer leader || !PlayerLedPullCoordinator.Available(bot, leader))
                return null;

            GameLiving focus = PlayerLedPullCoordinator.FindLeaderTarget(leader);
            bool live = focus != null && focus != Body && focus.IsAlive &&
                focus.ObjectState == GameObject.eObjectState.Active && focus.CurrentRegion == Body.CurrentRegion;
            if (!CompanionLeaderFocus.Takes(live, live && CanAggroTarget(focus), live && IsBreakablyControlled(focus),
                    live && Body.IsWithinRadius(focus, MAX_AGGRO_LIST_DISTANCE),
                    live && EpicEncounterTactics.MustNotHit(bot, focus)))
                return null;

            if (!AggroList.ContainsKey(focus))
                AddToAggroList(focus, Math.Max(100, focus.EffectiveLevel * 10));
            return focus;
        }

        /// <summary>
        /// The leader-focus log row, change-only per bot. <paramref name="directAttacker"/> and <paramref name="petTarget"/>
        /// are what the target choice saw; <paramref name="threat"/> its top threat when it worked one out.
        /// </summary>
        private void NoteLeaderFocus(GameLiving focus, GameLiving directAttacker, GameLiving petTarget, GameLiving threat)
        {
            if (focus == null)
            {
                _lastLeaderFocusLogged = null;
                return;
            }
            GameLiving without = directAttacker ?? petTarget ?? threat;
            if (!CompanionLeaderFocus.Logs(focus, without, _lastLeaderFocusLogged))
                return;
            _lastLeaderFocusLogged = focus;
            GameBot bot = BotBody;
            // A bot in the player's group turned onto the player's target over its own attacker, its pet's fight or its threat (#571).
            log.Info($"COMPANION_LEADER_FOCUS bot=\"{bot?.Name}\" class=\"{bot?.ClassName}\" kind={LeaderFocusBotKind(bot)} " +
                     $"leader=\"{bot?.PlayerGroupLeader?.Name}\" target=\"{focus.Name}\" over=\"{without?.Name ?? "-"}\" " +
                     $"why={CompanionLeaderFocus.Why(without, directAttacker, petTarget)} " +
                     $"fromBot={(int)Body.GetDistanceTo(focus)} region={Body.CurrentRegionID}");
        }

        /// <summary>Which kind of bot in the player's group, so a PC log tells them apart (AGENTS.md: tell bot types apart).</summary>
        private static string LeaderFocusBotKind(GameBot bot) =>
            bot == null ? "-" :
            bot.IsHiredCompanion ? "hired" :
            bot.IsTemporaryGroupHelper ? "helper" :
            bot.IsAutonomousWorldBot ? "gamebot" : "companion";
    }
}
