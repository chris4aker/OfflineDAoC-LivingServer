namespace DOL.GS;

public static class BotGroupInvite
{
    /// <summary>/invite by name reaches an autonomous bot only nearby: it walks to you, it can't cross regions.</summary>
    public const int InviteByNameRange = 6_000;

    public static bool CloseEnoughToInviteByName(GamePlayer player, GameBot bot) =>
        player?.CurrentRegion != null && bot?.CurrentRegion == player.CurrentRegion && player.IsWithinRadius(bot, InviteByNameRange);

    /// <summary>The duty that keeps an autonomous gamebot from answering an invitation (None when it is free).</summary>
    public enum Duty { None, Dead, Siege, Relic, RvrEvent, RealmRaid, StableRoute, Helper, Begging }

    /// <summary>
    /// The first duty that rules out an invitation, in a fixed order. Pure, so the rules are tested without a world.
    /// Audit 2026-10-17: /invite by name reaches autonomous gamebots within 6,000 units, and taking one from its
    /// autonomous group mid-siege, mid-raid or carrying a relic broke the job it was on.
    /// </summary>
    public static Duty DutyOf(bool alive, bool siege, bool relic, bool rvrEvent, bool realmRaid, bool stableRoute, bool helper) =>
        !alive ? Duty.Dead :
        siege ? Duty.Siege :
        relic ? Duty.Relic :
        rvrEvent ? Duty.RvrEvent :
        realmRaid ? Duty.RealmRaid :
        stableRoute ? Duty.StableRoute :
        helper ? Duty.Helper :
        Duty.None;

    /// <summary>What the player is told when an autonomous gamebot is on duty (null when it is free).</summary>
    public static string BusyMessage(string name, Duty duty) => duty switch
    {
        Duty.Dead => $"{name} is dead and can't join you right now.",
        Duty.Siege => $"{name} is busy with a siege right now.",
        Duty.Relic => $"{name} is carrying a relic right now.",
        Duty.RvrEvent => $"{name} is marching with a realm force right now.",
        Duty.RealmRaid => $"{name} is committed to a realm raid right now.",
        Duty.StableRoute => $"{name} is riding a stable master route right now.",
        Duty.Helper => $"{name} is already helping another player.",
        Duty.Begging => $"{name} won't leave their corner.",
        _ => null,
    };

    /// <summary>The duty of an autonomous gamebot. Companion bots are never on duty here.</summary>
    public static Duty DutyOf(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true)
            return Duty.None;
        // The force tag is never cleared when an event ends, so it counts only while its event is still running.
        string force = bot.TempProperties.GetProperty<string>("RvrEventForce");
        Duty duty = DutyOf(bot.IsAlive,
            BotSiegeRuntime.Assigned(bot) || BotSiegeRuntime.HoldingPosition(bot),
            bot.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK) != null,
            !string.IsNullOrEmpty(force) && AutonomousRvrEventLayer.IsEventForce(force, GameLoop.GameLoopTime),
            AutonomousRealmRaid.IsReserved(bot),
            bot.IsOnStableMasterRoute,
            bot.IsTemporaryGroupHelper);
        // A beggar (bot_living_beggars) never groups.
        return duty == Duty.None && AutonomousBeggars.Is(bot) ? Duty.Begging : duty;
    }

    public static bool TryInvite(GamePlayer player, GameBot bot)
    {
        if (player == null || bot == null || player.Realm != bot.Realm)
        {
            if (player != null)
                ChatUtil.SendSystemMessage(player, "Only a bot of your realm will accept the invitation.");
            return false;
        }

        if (player.Group != null && player.Group.Leader != player)
        {
            ChatUtil.SendSystemMessage(player, "You are not the leader of your group.");
            return false;
        }

        if (player.Group != null && player.Group.MemberCount >= ServerProperties.Properties.GROUP_MAX_MEMBER)
        {
            ChatUtil.SendSystemMessage(player, "The group is full.");
            return false;
        }

        string busy = BusyMessage(bot.Name, DutyOf(bot));
        if (busy != null)
        {
            ChatUtil.SendSystemMessage(player, busy);
            return false;
        }

        if (bot.Group != null)
        {
            bool autonomousGroup = bot.PlayerGroupLeader == null;
            if (!autonomousGroup)
            {
                ChatUtil.SendSystemMessage(player, $"{bot.Name} is already following another player-led group.");
                return false;
            }

            bot.Group.RemoveMember(bot);
            ChatUtil.SendSystemMessage(player, $"{bot.Name} leaves the autonomous group to answer your invitation.");
        }

        if (player.Group == null)
        {
            var group = new Group(player);
            GroupMgr.AddGroup(group);
            group.AddMember(player);
        }

        if (!player.Group.AddMember(bot) || !bot.EnterPlayerLedGroup(player))
            return false;

        bot.PathTo(player, bot.MaxSpeed);
        ChatUtil.SendSystemMessage(player, $"{bot.Name} joins your group and starts moving to you.");
        AutonomousBotGroupChatter.OnJoinedPlayerGroup(bot);
        return true;
    }
}
