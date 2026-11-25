using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>What the travel stealth does with a gamebot this turn.</summary>
public enum StealthTravelMove : byte
{
    /// <summary>Nothing: another owner decides (the fight, the ambush approach, the opener), or the bot is not a stealther.</summary>
    Leave,
    /// <summary>Go into stealth (the too-close rule may still refuse, GameBot.Stealth).</summary>
    Hide,
    /// <summary>Come out of stealth: to ride a stablemaster route, to use a porter, or because the duo's leader did.</summary>
    Unhide,
}

/// <summary>
/// The one owner of a gamebot stealther's stealth while it moves (#522; stealth.md findings 1 and 7). Before, only the roam
/// turn's "check" step hid a bot, on its stand point, and its "move on" step unhid it, so every stealther walked to its
/// camp in plain view, vanished on the spot, and reappeared to walk off; and a stealth duo's second stood visible beside a
/// hidden leader. Now a camping stealther (the melee stealthers and the archers, with Stealth spec) in a small party
/// (soloer, duo, stealther) in the old frontiers travels hidden once it is out of the portal keep's and the bind's safe
/// area, at the player's stealth speed (StealthRules.SpeedFactor), and comes out only to fight (the brain), to ride a
/// stablemaster route or to use a porter. A duo's follower that can stealth copies its leader (the CompanionStealth.Maintain
/// pattern). Out of the open-field scope (RvrTacticsScope), in a warband or an eight, and for companions, nothing changes.
/// Needs rvr_roam_checks (the stealther camps). The movement-owner piece (#532) may later absorb this; it owns only the
/// stealth state, not the path. The rule is pure (<see cref="Decide"/>); <see cref="Maintain"/> reads the game.
/// </summary>
public static class BotStealthTravel
{
    /// <summary>How often a bot that should hide tries again (the too-close rule may have refused it).</summary>
    public const long LookMilliseconds = 1_000;

    private sealed class State
    {
        public long NextLook;
    }

    private static readonly ConditionalWeakTable<GameBot, State> States = new();

    /// <summary>
    /// The decision (pure). <paramref name="inScope"/>: an autonomous gamebot in the open-field scope in an old frontier;
    /// <paramref name="fighting"/>: in combat, attacking, casting or with an enemy target (the brain owns stealth then);
    /// <paramref name="mustShow"/>: on a stablemaster route, waiting for a porter, or carrying a relic;
    /// <paramref name="follower"/>: in a group it does not lead.
    /// </summary>
    public static StealthTravelMove Decide(bool inScope, bool canStealth, bool campingStealther, bool smallParty, bool follower,
        bool leaderStealthed, bool stealthed, bool fighting, bool mustShow, bool mounted, bool safeArea)
    {
        if (!inScope || !canStealth || fighting)
            return StealthTravelMove.Leave;
        if (mustShow)
            return stealthed ? StealthTravelMove.Unhide : StealthTravelMove.Leave;
        if (!smallParty)
            return StealthTravelMove.Leave;
        if (!Wants(campingStealther, follower, leaderStealthed))
            return follower && stealthed ? StealthTravelMove.Unhide : StealthTravelMove.Leave;
        if (stealthed || mounted || safeArea)
            return StealthTravelMove.Leave;
        return StealthTravelMove.Hide;
    }

    /// <summary>Whether the bot should be hidden while it moves: a camping stealther on its own or leading, or a follower of a hidden leader. Pure.</summary>
    public static bool Wants(bool campingStealther, bool follower, bool leaderStealthed) =>
        follower ? leaderStealthed : campingStealther;

    /// <summary>A gamebot stealther kept hidden for its travel: the idle unhide (BotBrain.TryEnterStealthWhileIdle) leaves it be.</summary>
    public static bool HoldsStealth(GameBot bot) =>
        bot is { IsStealthed: true } && Applies(bot) && CanStealth(bot) && !MustShow(bot) && SmallParty(bot) &&
        Wants(CampingStealther(bot), IsFollower(bot, out GameBot leader), leader?.IsStealthed == true);

    /// <summary>The brain's turn (BotBrain.Think, beside CompanionStealth.Maintain).</summary>
    public static void Maintain(GameBot bot)
    {
        if (!Properties.RVR_ROAM_CHECKS || bot is not { IsAutonomousWorldBot: true } || bot.CurrentRegionID is not (1 or 100 or 200) ||
            !CanStealth(bot))
            return;
        State state = States.GetOrCreateValue(bot);
        long now = GameLoop.GameLoopTime;
        if (now < state.NextLook)
            return;
        state.NextLook = now + LookMilliseconds;
        if (!Applies(bot))
            return;
        bool follower = IsFollower(bot, out GameBot leader);
        bool stealthed = bot.IsStealthed;
        bool fighting = Fighting(bot);
        bool mustShow = MustShow(bot);
        bool smallParty = SmallParty(bot);
        bool camping = CampingStealther(bot);
        // The area lookup is the costly read: only for a bot about to hide.
        bool safeArea = !stealthed && !fighting && !mustShow && smallParty && Wants(camping, follower, leader?.IsStealthed == true) &&
            InSafeArea(bot);
        switch (Decide(true, true, camping, smallParty, follower, leader?.IsStealthed == true, stealthed, fighting, mustShow,
                    bot.IsOnHorse, safeArea))
        {
            case StealthTravelMove.Hide:
                bot.Stealth(true);
                if (bot.IsStealthed)
                    RvrStealthRecord.Travel(bot, true, follower ? "follow" : "travel");
                break;
            case StealthTravelMove.Unhide:
                bot.Stealth(false);
                RvrStealthRecord.Travel(bot, false, mustShow ? MustShowReason(bot) : "leader");
                break;
        }
    }

    // ---- reading the game ----------------------------------------------------------------------------------------

    private static bool Applies(GameBot bot) =>
        Properties.RVR_ROAM_CHECKS && bot is { IsAutonomousWorldBot: true, IsAlive: true } &&
        bot.ObjectState == GameObject.eObjectState.Active && bot.CurrentRegionID is 1 or 100 or 200 &&
        RvrTacticsScope.Allows(bot);

    public static bool CanStealth(GameBot bot) => (bot?.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0;

    private static bool CampingStealther(GameBot bot) => bot.CharacterClass != null &&
        RvrStealthOpener.IsCampingStealther((eCharacterClass)bot.CharacterClass.ID) && CanStealth(bot);

    private static bool SmallParty(GameBot bot) =>
        AutonomousBotGroupCoordinator.RvrPartyKindOf(bot) == RvrPartyKind.Small;

    private static bool IsFollower(GameBot bot, out GameBot leader)
    {
        leader = bot.Group?.LivingLeader as GameBot;
        return leader != null && leader != bot && leader.CurrentRegionID == bot.CurrentRegionID;
    }

    private static bool Fighting(GameBot bot) =>
        bot.InCombat || bot.IsAttacking || bot.IsCasting || bot.Brain is BotBrain { HasAggro: true } ||
        bot.TargetObject is GameLiving { IsAlive: true } target && target.Realm != bot.Realm && target.Realm != eRealm.None;

    private static bool MustShow(GameBot bot) =>
        bot.IsOnStableMasterRoute || GameRelic.IsPlayerCarryingRelic(bot) ||
        bot.TempProperties.GetProperty<object>(AutonomousFrontierTransport.RequestKey) != null;

    private static string MustShowReason(GameBot bot) =>
        bot.IsOnStableMasterRoute ? "ride" : GameRelic.IsPlayerCarryingRelic(bot) ? "relic" : "porter";

    private static bool InSafeArea(GameBot bot)
    {
        var areas = bot.CurrentZone?.GetAreasOfSpot(bot);
        if (areas == null)
            return false;
        for (int i = 0; i < areas.Count; i++)
            if (areas[i] is AbstractArea area && (area.IsSafeArea || area is Area.BindArea))
                return true;
        return false;
    }
}
