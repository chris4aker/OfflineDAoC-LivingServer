using DOL.GS;
using DOL.GS.ServerProperties;
using DOL.GS.Styles;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Open-field RvR tactics, work package 0 (docs/RVR-FIELD-1.2.md): the brain's hook points. Each hook is called by
    /// one line at its site in the brain and is a stub here: it asks <see cref="RvrTacticsActive"/> (the master switch
    /// rvr_bot_field_tactics, a battle region, and <see cref="RvrTacticsScope"/>) and its own switch, and then has no
    /// opinion, so the code at the site runs exactly as before. The later packages fill their own hook:
    /// <list type="bullet">
    /// <item>work package 2: <see cref="RvrTacticsTarget"/>, <see cref="RvrTacticsCrowdControl"/>, <see cref="TryRvrTacticsSupport"/></item>
    /// <item>work package 3: <see cref="RvrTacticsPeel"/>, <see cref="RvrTacticsProtectionTarget"/>, <see cref="TryRvrTacticsMove"/></item>
    /// <item>work package 4: the stance, through the plan (<see cref="RvrPlan"/>) the other hooks read</item>
    /// <item>work package 6: <see cref="RvrTacticsAllowsStealthOpener"/> (and <see cref="TryRvrTacticsMove"/> for the opener itself)</item>
    /// </list>
    /// The world controller's and the group coordinator's hooks are in AutonomousWorldBotController.RvrTactics.cs and
    /// AutonomousBotGroupCoordinator.RvrTactics.cs. A hook that does real work wraps it in
    /// AiProfile.Scope(AiSystem.RvrTactics); the stubs are not timed (two timestamps would cost more than they do).
    /// The fight board is built only when a hook asks for it (<see cref="RvrBoard"/>); no stub does.
    /// </summary>
    public partial class BotBrain
    {
        private long _rvrTacticsGateTick = long.MinValue;
        private bool _rvrTacticsGate;

        /// <summary>
        /// The gate every hook passes first: the master switch is on, and this bot is an autonomous gamebot in a battle
        /// region that <see cref="RvrTacticsScope"/> allows. The answer is kept for the rest of the game-loop tick, so
        /// the hooks of one think share one scope check.
        /// </summary>
        public bool RvrTacticsActive()
        {
            if (!Properties.RVR_BOT_FIELD_TACTICS)
                return false;
            long now = GameLoop.GameLoopTime;
            if (now == _rvrTacticsGateTick)
                return _rvrTacticsGate;
            _rvrTacticsGate = RvrTacticsApplyTo(BotBody);
            _rvrTacticsGateTick = now;
            return _rvrTacticsGate;
        }

        /// <summary>
        /// Whether the open-field tactics apply to this bot now, checked afresh: the master switch, an autonomous gamebot
        /// that no player has, in the frontier, a battleground or a neutral battle dungeon, and in
        /// <see cref="RvrTacticsScope"/> (no raid, battleground assault, keep fight, siege job or relic carrier).
        /// </summary>
        public static bool RvrTacticsApplyTo(GameBot bot) =>
            Properties.RVR_BOT_FIELD_TACTICS && bot?.IsAutonomousWorldBot == true && !bot.IsPlayerLedGroup &&
            !bot.IsTemporaryGroupHelper && AutonomousWorldBotController.IsBattleground(bot) && RvrTacticsScope.Allows(bot);

        /// <summary>
        /// This bot's group's fight board, at most a second old, or null when the tactics do not apply to the bot. Asking
        /// builds it when it is stale (one area search per group and second). Not for use under the coordinator's lock.
        /// </summary>
        public RvrFightBoard RvrBoard() => RvrTacticsActive() ? RvrFightBoards.Get(Body) : null;

        /// <summary>The group's fight plan (see <see cref="RvrBoard"/>), or null when the tactics do not apply to the bot.</summary>
        public RvrFightPlan RvrPlan() => RvrBoard()?.Plan;

        /// <summary>The group's plan if a board was built in the last two seconds, else null. Never builds.</summary>
        public RvrFightPlan PeekRvrPlan() => RvrTacticsActive() ? RvrFightBoards.PeekPlan(Body) : null;

        // ---- work package 2: assist, crowd control, interrupts ------------------------------------------------------

        /// <summary>
        /// Hook: AttackMostWanted, right after the target pick. The target the group's plan wants this bot on, or null
        /// for no opinion (the pick stands). <paramref name="picked"/> is what the brain chose;
        /// <paramref name="protectionTarget"/> and <paramref name="directAttacker"/> are the two it ranks first.
        /// Reserved for work package 2 (rvr_bot_assist).
        /// </summary>
        public GameLiving RvrTacticsTarget(GameLiving picked, GameLiving protectionTarget, GameLiving directAttacker)
        {
            if (!RvrTacticsActive() || !Properties.RVR_BOT_ASSIST)
                return null;
            return null;
        }

        /// <summary>
        /// Hook: the entry of TryPvpCrowdControl. Null for no opinion (today's crowd control runs); true when the plan
        /// cast its crowd control and the turn is used; false when the plan wants no crowd control from this bot now.
        /// Reserved for work package 2 (rvr_bot_cc_plan, rvr_bot_interrupts).
        /// </summary>
        public bool? RvrTacticsCrowdControl()
        {
            if (!RvrTacticsActive() || !(Properties.RVR_BOT_CC_PLAN || Properties.RVR_BOT_INTERRUPTS))
                return null;
            return null;
        }

        /// <summary>
        /// Hook: the support turn (PerformGroupSupport), after the bot stopped attacking and before heals and crowd
        /// control. True when the tactics used the turn; false for no opinion.
        /// Reserved for work packages 2 and 3 (an interrupt duty, a healer's kiting step).
        /// </summary>
        public bool TryRvrTacticsSupport()
        {
            if (!RvrTacticsActive())
                return false;
            return false;
        }

        // ---- work package 3: peel, protection, positioning -----------------------------------------------------------

        /// <summary>
        /// Hook: the entry of MaintainTankPeel. True when the plan handled Protect, Guard and Intercept this turn; false
        /// for no opinion (today's peel runs). Reserved for work package 3 (rvr_bot_peel).
        /// </summary>
        public bool RvrTacticsPeel()
        {
            if (!RvrTacticsActive() || !Properties.RVR_BOT_PEEL)
                return false;
            return false;
        }

        /// <summary>
        /// Hook: the entry of FindProtectionTarget. True when the plan decides whom this bot pulls off a group mate:
        /// <paramref name="target"/> is that enemy, or null for "nobody now". False for no opinion (today's search runs).
        /// Reserved for work package 3 (rvr_bot_peel).
        /// </summary>
        public bool RvrTacticsProtectionTarget(out GameLiving target)
        {
            target = null;
            if (!RvrTacticsActive() || !Properties.RVR_BOT_PEEL)
                return false;
            return false;
        }

        /// <summary>
        /// Hook: AttackMostWanted, with a target, before the ambush approach, the flank step and the circle. True when
        /// the tactics used the turn to move (backline, spread, kiting, a stealth opener); false for no opinion.
        /// Reserved for work packages 3 (rvr_bot_positioning) and 6 (rvr_bot_stealth_openers).
        /// </summary>
        public bool TryRvrTacticsMove(GameLiving target)
        {
            if (!RvrTacticsActive() || !(Properties.RVR_BOT_POSITIONING || Properties.RVR_BOT_STEALTH_OPENERS))
                return false;
            return false;
        }

        // ---- work package 6: stealth openers ---------------------------------------------------------------------------

        /// <summary>
        /// Hook: BotMeleeStylePolicy.Usable, for a style that needs stealth (which bots never pick today). True lets the
        /// bot use it as an opener; false for no opinion (the style stays out).
        /// Reserved for work package 6 (rvr_bot_stealth_openers).
        /// </summary>
        public static bool RvrTacticsAllowsStealthOpener(GameBot bot, Style style)
        {
            if (style?.StealthRequirement != true || !Properties.RVR_BOT_STEALTH_OPENERS || !RvrTacticsApplyTo(bot))
                return false;
            return false;
        }
    }
}
