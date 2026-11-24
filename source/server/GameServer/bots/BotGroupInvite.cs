using System;
using System.Linq;

namespace DOL.GS;

public static class BotGroupInvite
{
    /// <summary>/invite by name reaches an autonomous bot only nearby: it walks to you, it can't cross regions.</summary>
    public const int InviteByNameRange = 6_000;

    public static bool CloseEnoughToInviteByName(GamePlayer player, GameBot bot) =>
        player?.CurrentRegion != null && bot?.CurrentRegion == player.CurrentRegion && player.IsWithinRadius(bot, InviteByNameRange);

    /// <summary>The duty that keeps an autonomous gamebot from answering an invitation (None when it is free).</summary>
    public enum Duty { None, Dead, Siege, Relic, RvrEvent, RealmRaid, StableRoute, Helper, Begging, Buffing }

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

    /// <summary>What the player is told when an autonomous gamebot is on duty (null when it is free), with bot_show_as_bots as set.</summary>
    public static string BusyMessage(string name, Duty duty) => BusyMessage(name, duty, ServerProperties.Properties.BOT_SHOW_AS_BOTS);

    /// <summary>
    /// What the player is told when an autonomous gamebot is on duty (null when it is free). <paramref name="showBots"/>
    /// (bot_show_as_bots, #485): only then does a line call it a buffbot; otherwise it reads as a player's answer would.
    /// </summary>
    public static string BusyMessage(string name, Duty duty, bool showBots) => duty switch
    {
        Duty.Dead => $"{name} is dead and can't join you right now.",
        Duty.Siege => $"{name} is busy with a siege right now.",
        Duty.Relic => $"{name} is carrying a relic right now.",
        Duty.RvrEvent => $"{name} is marching with a realm force right now.",
        Duty.RealmRaid => $"{name} is committed to a realm raid right now.",
        Duty.StableRoute => $"{name} is riding a stable master route right now.",
        Duty.Helper => $"{name} is already helping another player.",
        Duty.Begging => $"{name} won't leave their corner.",
        Duty.Buffing => showBots ? $"{name} is a buffbot and never joins a group." : DeclinedMessage(name),
        _ => null,
    };

    /// <summary>The game's own line when someone turns an invitation down.</summary>
    public static string DeclinedMessage(string name) => $"{name} declines your invitation.";

    /// <summary>An invite to a character of another realm. Only with bot_show_as_bots does it say "gamebot" (#485).</summary>
    public static string OtherRealmMessage(bool showBots) =>
        showBots ? "Only a gamebot of your realm will accept the invitation." : "You can only invite characters of your own realm.";

    /// <summary>The /who tag of a gamebot: " [Playerbot]" only with bot_show_as_bots (owner decision, 2026-11-24, #485).</summary>
    public static string WhoTag(bool showBots) => showBots ? " [Playerbot]" : string.Empty;

    /// <summary>The duty of an autonomous gamebot. Companion bots are never on duty here.</summary>
    public static Duty DutyOf(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true)
            return Duty.None;
        // An RvR event's lease (a defense response, a siege party) counts while its force is in a running event. A solo
        // RvR bot's own force joins events through the planner without a lease yet, so its force tag is read too; the
        // tag is never cleared when an event ends, so it counts only while its event is still running.
        long now = GameLoop.GameLoopTime;
        string force = bot.TempProperties.GetProperty<string>("RvrEventForce");
        string leased = BotDutyLeases.Current(bot) is { Kind: eDutyLeaseKind.RvrEvent } lease ? lease.Owner : null;
        Duty duty = DutyOf(bot.IsAlive,
            BotSiegeRuntime.Assigned(bot) || BotSiegeRuntime.HoldingPosition(bot),
            bot.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK) != null,
            leased != null && AutonomousRvrEventLayer.IsEventForce(leased, now) ||
            !string.IsNullOrEmpty(force) && force != leased && AutonomousRvrEventLayer.IsEventForce(force, now),
            AutonomousRealmRaid.IsReserved(bot),
            bot.IsOnStableMasterRoute,
            bot.IsTemporaryGroupHelper);
        // A No Lifer's buffbot never groups, whatever else it is doing (NoLiferBuffBots).
        if (bot.IsNoLiferBuffBot)
            return Duty.Buffing;
        // A beggar (bot_living_beggars) never groups.
        return duty == Duty.None && AutonomousBeggars.Is(bot) ? Duty.Begging : duty;
    }

    /// <summary>
    /// The player invites a bot (/invite, the invite button). A hired companion or a /spawn helper joins at once, as before;
    /// an autonomous gamebot answers as a person would (#485, <see cref="BotInviteDecision"/>): after a pause, after its fight,
    /// or not at all, with its reason by tell. True when the invitation went out (or the bot joined).
    /// </summary>
    public static bool TryInvite(GamePlayer player, GameBot bot) => TryInvite(player, bot, immediate: false);

    /// <summary>
    /// <paramref name="immediate"/>: the bot already said yes (it invited the player, who accepted its box), so it joins at once.
    /// </summary>
    public static bool TryInvite(GamePlayer player, GameBot bot, bool immediate)
    {
        if (player == null || bot == null || player.Realm != bot.Realm)
        {
            if (player != null)
                ChatUtil.SendSystemMessage(player, OtherRealmMessage(ServerProperties.Properties.BOT_SHOW_AS_BOTS));
            return false;
        }

        // Play with friends (1.2): another player's companion follows only its owner (BotHumanBinding).
        if (bot.Owner != null && bot.Owner != player)
        {
            ChatUtil.SendSystemMessage(player, $"{bot.Name} is {bot.Owner.Name}'s helper and follows only them.");
            return false;
        }

        // Hired companions (from True 1.65): one follows only the player character who hired it.
        if (bot.IsHiredCompanion && !HiredCompanions165.IsHiredBy(bot, player))
        {
            ChatUtil.SendSystemMessage(player, $"{bot.Name} is another player's hired companion and follows only them.");
            return false;
        }

        string why = PlayerSideBlocks(player);
        if (why != null)
        {
            ChatUtil.SendSystemMessage(player, why);
            return false;
        }

        Duty duty = DutyOf(bot);
        string busy = BusyMessage(bot.Name, duty);
        if (busy != null)
        {
            ChatUtil.SendSystemMessage(player, busy);
            // 1.1.3: the bot answers too, as a player would (RC5 on the owner's PC: a friend on a raid gave no word).
            if (bot.IsAutonomousWorldBot)
                AutonomousLivingServer.OnInviteDeclined(player, bot, duty, false);
            return false;
        }

        if (bot.Group != null && bot.PlayerGroupLeader != null)
        {
            ChatUtil.SendSystemMessage(player, $"{bot.Name} is already in another player's group.");
            if (bot.IsAutonomousWorldBot && bot.PlayerGroupLeader != player)
                AutonomousLivingServer.OnInviteDeclined(player, bot, Duty.None, true);
            return false;
        }

        if (!immediate && TakesAThought(bot))
            return Consider(player, bot);
        return Join(player, bot, narrate: true);
    }

    /// <summary>Why the player can't invite anyone now (not the leader, a full group), or null.</summary>
    private static string PlayerSideBlocks(GamePlayer player)
    {
        if (player.Group != null && player.Group.Leader != player)
            return BotLedGroups.BotLeadsMessageFor(player) ?? "You are not the leader of your group.";
        if (player.Group != null && player.Group.MemberCount >= ServerProperties.Properties.GROUP_MAX_MEMBER)
            return "The group is full.";
        return null;
    }

    /// <summary>An autonomous gamebot of its own (not a hired companion, a /spawn helper or anyone's helper) answers an invite as a person.</summary>
    public static bool TakesAThought(GameBot bot) =>
        bot != null && bot.IsAutonomousWorldBot && !bot.IsHiredCompanion && !bot.IsTemporaryGroupHelper && bot.Owner == null;

    /// <summary>Joins now: leaves its bot group if it is in one, enters the player's group and walks to the player.</summary>
    private static bool Join(GamePlayer player, GameBot bot, bool narrate)
    {
        if (bot.Group != null)
        {
            bot.Group.RemoveMember(bot);
            if (narrate)
                ChatUtil.SendSystemMessage(player, $"{bot.Name} leaves their group to answer your invitation.");
        }

        if (player.Group == null)
        {
            var group = new Group(player);
            GroupMgr.AddGroup(group);
            group.AddMember(player);
        }

        if (!player.Group.AddMember(bot) || !bot.EnterPlayerLedGroup(player))
        {
            // 1.1.3: this failure was silent; the player hears of it and the log says so (BOT_INVITE_RESULT).
            ChatUtil.SendSystemMessage(player, $"{bot.Name} could not join your group.");
            if (bot.IsAutonomousWorldBot)
                AutonomousLivingServer.LogInviteResult(player, bot, "failed reason=add_member");
            return false;
        }

        bot.PathTo(player, bot.MaxSpeed);
        // #485: a gamebot that thought about it gets only the game's own "X has joined the group." (Group.AddMember).
        if (narrate)
            ChatUtil.SendSystemMessage(player, $"{bot.Name} joins your group and starts moving to you.");
        if (bot.IsAutonomousWorldBot)
            AutonomousLivingServer.LogInviteResult(player, bot, "grouped");
        AutonomousBotGroupChatter.OnJoinedPlayerGroup(bot);
        return true;
    }

    #region A person behind the invite (#485)

    // The invitation each gamebot is answering: the player's name and when it came. One at a time per bot.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, (string Player, long At)> Considering = new();

    /// <summary>The bot is still answering this player's invitation (it has not joined or said no yet).</summary>
    public static bool IsConsidering(GameBot bot, GamePlayer player) =>
        bot != null && player != null && Considering.TryGetValue(bot.DatabaseID, out var held) && held.Player == player.Name &&
        GameLoop.GameLoopTime - held.At < BotInviteDecision.AfterFightCapMs + 30_000;

    /// <summary>What the bot is doing now, for the invite decision.</summary>
    public static BotInviteDecision.Activity ActivityOf(GameBot bot)
    {
        bool inBotGroup = bot.Group != null && bot.PlayerGroupLeader == null;
        bool groupFighting = inBotGroup && bot.Group.GetMembersInTheGroup().Any(member => member.IsAlive && member.InCombat);
        return BotInviteDecision.ActivityOf(inBotGroup, groupFighting, inBotGroup ? AutonomousBotGroupCoordinator.PhaseOf(bot.Group) : null,
            bot.InCombat || bot.IsAttacking, bot.IsRecoveryResting || bot.IsSeatedByHabit, bot.IsMoving,
            AutonomousBotGroupCoordinator.IsInTownArea(bot));
    }

    /// <summary>
    /// How this bot would answer this player's invite now: the act (<see cref="Consider"/>) and the words (the tell's
    /// JoinReply) both ask this, and a grouped bot's answer is held for ten minutes, so "sure, ill leave my grp" by tell
    /// is what the invite then does.
    /// </summary>
    public static BotInviteDecision.Decision Consult(GameBot bot, GamePlayer player, out BotInviteDecision.Activity activity, out bool friend)
    {
        activity = ActivityOf(bot);
        friend = AutonomousLivingServer.IsFriend(bot, player);
        double leave = BotInviteDecision.Remember(bot.DatabaseID, player.Name, GameLoop.GameLoopTime, Random.Shared.NextDouble);
        return BotInviteDecision.Decide(bot.Level, player.Level, activity, friend, leave, Random.Shared.NextDouble());
    }

    private static bool Consider(GamePlayer player, GameBot bot)
    {
        long now = GameLoop.GameLoopTime;
        if (Considering.TryGetValue(bot.DatabaseID, out var held) && now - held.At < BotInviteDecision.AfterFightCapMs + 30_000)
        {
            ChatUtil.SendSystemMessage(player, held.Player == player.Name
                ? $"You have already invited {bot.Name}."
                : $"{bot.Name} is considering another invitation.");
            return held.Player == player.Name;
        }
        BotInviteDecision.Decision decision = Consult(bot, player, out BotInviteDecision.Activity activity, out bool friend);
        ChatUtil.SendSystemMessage(player, $"You have invited {bot.Name} to join your group.");
        AutonomousLivingServer.LogInviteResult(player, bot,
            $"considering verdict={decision.Verdict} activity={activity} delay_ms={decision.DelayMs} gap={bot.Level - player.Level}");
        switch (decision.Verdict)
        {
            case BotInviteDecision.Verdict.DeclineLevelGap:
            case BotInviteDecision.Verdict.DeclineBotGroup:
            {
                string line = decision.Verdict == BotInviteDecision.Verdict.DeclineLevelGap
                    ? AutonomousBotChatCoordinator.LevelGapReply(bot.Level, player.Level, Random.Shared)
                    : BotInviteDecision.BotGroupDeclineLine(activity, friend, Random.Shared);
                int pause = 2_500 + Random.Shared.Next(4_000);
                AutonomousLivingServer.Later(bot, pause, () => player.Client != null, () =>
                {
                    ChatUtil.SendSystemMessage(player, DeclinedMessage(bot.Name));
                    AutonomousLivingServer.LogInviteResult(player, bot, $"declined verdict={decision.Verdict}");
                });
                AutonomousLivingServer.InviteAnswer(bot, player, line, pause + 1_000 + Random.Shared.Next(2_500));
                return true;
            }
            case BotInviteDecision.Verdict.AcceptAfterFight:
                Considering[bot.DatabaseID] = (player.Name, now);
                AutonomousLivingServer.InviteAnswer(bot, player, BotInviteDecision.FightingLine(Random.Shared), 3_000 + Random.Shared.Next(3_000),
                    whileFighting: true);
                AwaitFightEnd(player, bot, now);
                return true;
            default:
                Considering[bot.DatabaseID] = (player.Name, now);
                string first = decision.Verdict == BotInviteDecision.Verdict.LeaveGroupAndAccept
                    ? BotInviteDecision.LeavingLine(friend, Random.Shared)
                    : friend && bot.Level - player.Level > BotInviteDecision.MaxLevelGap ? BotInviteDecision.PowerLevelLine(Random.Shared) : null;
                if (first != null)
                    AutonomousLivingServer.InviteAnswer(bot, player, first, Math.Max(1_500, decision.DelayMs / 2));
                AutonomousLivingServer.Later(bot, decision.DelayMs, null, () => Answer(player, bot));
                return true;
        }
    }

    // A bot fighting alone looks every two seconds; when the fight is over it joins 1.5-4 s later, and after a minute it gives up.
    private static void AwaitFightEnd(GamePlayer player, GameBot bot, long since)
    {
        _ = new ECSGameTimer(bot, timer =>
        {
            try
            {
                if (!Considering.TryGetValue(bot.DatabaseID, out var held) || held.Player != player.Name)
                    return 0;
                if (bot.ObjectState != GameObject.eObjectState.Active || player.Client == null)
                {
                    Considering.TryRemove(bot.DatabaseID, out _);
                    return 0;
                }
                if (!bot.InCombat && !bot.IsAttacking)
                {
                    AutonomousLivingServer.Later(bot, 1_500 + Random.Shared.Next(2_500), null, () => Answer(player, bot));
                    return 0;
                }
                if (GameLoop.GameLoopTime - since < BotInviteDecision.AfterFightCapMs)
                    return BotInviteDecision.AfterFightPollMs;
                Considering.TryRemove(bot.DatabaseID, out _);
                ChatUtil.SendSystemMessage(player, DeclinedMessage(bot.Name));
                AutonomousLivingServer.LogInviteResult(player, bot, "declined verdict=FightTooLong");
                AutonomousLivingServer.InviteAnswer(bot, player, BotInviteDecision.FightTooLongLine(Random.Shared), 500, whileFighting: true);
            }
            catch (Exception exception)
            {
                Considering.TryRemove(bot.DatabaseID, out _);
                log.Warn($"BOT_INVITE_RESULT wait for {bot.Name}'s fight failed", exception);
            }
            return 0;
        }, BotInviteDecision.AfterFightPollMs);
    }

    /// <summary>The bot clicks yes: everything is checked again (the player may have left, grouped or filled up meanwhile).</summary>
    private static void Answer(GamePlayer player, GameBot bot)
    {
        if (!Considering.TryRemove(bot.DatabaseID, out var held) || held.Player != player.Name)
            return;
        if (player.Client == null || player.ObjectState != GameObject.eObjectState.Active)
            return;
        string why = player.Realm != bot.Realm || !bot.IsAlive ? "gone" : PlayerSideBlocks(player) != null ? "player_group"
            : DutyOf(bot) != Duty.None ? "duty" : bot.Group != null && bot.PlayerGroupLeader != null ? "other_player" : null;
        if (why != null)
        {
            AutonomousLivingServer.LogInviteResult(player, bot, $"failed reason={why}");
            if (why == "player_group")
                ChatUtil.SendSystemMessage(player, $"{bot.Name} could not join your group.");
            else
                ChatUtil.SendSystemMessage(player, DeclinedMessage(bot.Name));
            return;
        }
        Join(player, bot, narrate: false);
    }

    /// <summary>Forgets every invitation being answered (tests).</summary>
    public static void ResetConsidering() => Considering.Clear();

    private static readonly DOL.Logging.Logger log = DOL.Logging.LoggerManager.Create(typeof(BotGroupInvite));

    #endregion
}
