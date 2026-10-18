using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS.PacketHandler;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Talk inside the real player's group (bot voices, 0.36-se.4). Gamebots in a player's group never said
/// anything unprompted (BotBrain skipped them), so a group of bots felt like a group of pets. Now, out of
/// combat, one of them says a corpus "group" line in /group every 4-8 minutes (the first one or two minutes
/// after the group forms), and a groupmate may answer it. Never during a fight, and only with bot voices on.
/// </summary>
public static class AutonomousBotGroupChatter
{
    public const int FirstLineMinMs = 60_000;
    public const int FirstLineMaxMs = 120_000;
    public const int MinGapMs = 4 * 60_000;
    public const int MaxGapMs = 8 * 60_000;

    private sealed class Next
    {
        public long At;
        public long LastPlayerReply;
    }

    /// <summary>A player's /group lines are answered at most this often per group.</summary>
    public const int PlayerReplyCooldownMs = 5_000;

    private static readonly ConditionalWeakTable<Group, Next> Groups = new();

    /// <summary>The next gap: a first line soon after the group forms, then every 4-8 minutes.</summary>
    public static long NextGapMs(bool first, Random random) =>
        first ? FirstLineMinMs + random.Next(FirstLineMaxMs - FirstLineMinMs + 1) : MinGapMs + random.Next(MaxGapMs - MinGapMs + 1);

    /// <summary>True when it is this group's turn to hear a line now (and books the next turn).</summary>
    public static bool TakeTurn(object sync, Func<long> get, Action<long> set, long now, Random random, bool known)
    {
        lock (sync)
        {
            if (!known)
            {
                set(now + NextGapMs(true, random));
                return false;
            }
            if (now < get())
                return false;
            set(now + NextGapMs(false, random));
            return true;
        }
    }

    /// <summary>From the bot's ambient chat check: maybe say a group line. True when the bot spoke.</summary>
    public static bool TryGroupLine(GameBot bot)
    {
        if (!AutonomousBotVoices.Enabled || bot?.Group == null || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper ||
            !bot.IsAlive || bot.InCombat || bot.IsCasting || !AutonomousLivingServer.GroupHasPlayer(bot.Group))
            return false;
        Group group = bot.Group;
        GameLiving[] members = group.GetMembersInTheGroup().ToArray();
        if (members.Any(member => member.IsAlive && member.InCombat))
            return false;
        bool known = Groups.TryGetValue(group, out Next next);
        next ??= Groups.GetValue(group, _ => new Next());
        if (!TakeTurn(next, () => next.At, value => next.At = value, GameLoop.GameLoopTime, Random.Shared, known))
            return false;
        if (!AutonomousBotVoices.TryLine(bot, "group", out VC.Picked picked))
            return false;
        Send(bot, picked.Text);
        if (!string.IsNullOrEmpty(picked.Correction))
            AutonomousLivingServer.Later(bot, 1_200 + Random.Shared.Next(1_400), () => bot.Group == group, () => Send(bot, picked.Correction));
        Answer(bot, group, picked);
        return true;
    }

    /// <summary>A groupmate may pick the line up, as in /faction (a question nearly always, a remark sometimes).</summary>
    private static void Answer(GameBot speaker, Group group, VC.Picked opener)
    {
        string[] chain = VC.AnswerChain(opener.Line.Invites);
        if (chain.Length == 0)
            return;
        GameBot[] mates = group.GetMembersInTheGroup().OfType<GameBot>()
            .Where(mate => mate != speaker && mate.IsAutonomousWorldBot && !mate.IsTemporaryGroupHelper && mate.IsAlive)
            .OrderBy(_ => Random.Shared.Next()).ToArray();
        if (AutonomousBotChatCoordinator.BotReplyCount(opener.Line.Invites, Math.Min(1, mates.Length), Random.Shared) == 0)
            return;
        GameBot mate = mates[0];
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = speaker.Name };
        foreach (string answers in chain)
        {
            if (!AutonomousBotVoices.TryLine(mate, "reply", out VC.Picked reply, answers: answers, slots: slots,
                    filter: line => AutonomousChatShortTalk.ReplyFits(opener.Text, line.Text)))
                continue;
            int delay = AutonomousBotChatCoordinator.ReplyDelayMs(reply.Text, 0, Random.Shared);
            AutonomousLivingServer.Later(mate, delay, () => mate.Group == group && mate.IsAlive && !mate.InCombat, () => Send(mate, reply.Text));
            return;
        }
    }

    /// <summary>
    /// The player said something in /group: a groupmate answers in /group, as /faction and /say do (live test 2026-10-18:
    /// "hi" and "where to next?" in /group got no answer). Bots that are fighting or casting stay quiet.
    /// </summary>
    public static void OnPlayerGroupChat(GamePlayer player, string message)
    {
        if (!AutonomousBotVoices.Enabled || player?.Group == null || string.IsNullOrWhiteSpace(message))
            return;
        Group group = player.Group;
        GameBot[] mates = group.GetMembersInTheGroup().OfType<GameBot>()
            .Where(mate => mate.IsAutonomousWorldBot && !mate.IsTemporaryGroupHelper && mate.IsAlive && !mate.InCombat && !mate.IsCasting)
            .OrderBy(_ => Random.Shared.Next()).ToArray();
        if (mates.Length == 0)
            return;
        Next next = Groups.GetValue(group, _ => new Next());
        lock (next)
        {
            if (next.LastPlayerReply > 0 && GameLoop.GameLoopTime - next.LastPlayerReply < PlayerReplyCooldownMs)
                return;
            next.LastPlayerReply = GameLoop.GameLoopTime;
        }
        GameBot[] named = mates.Where(mate => AutonomousBotChatCoordinator.ContainsMention(message, mate.Name)).ToArray();
        GameBot[] pool = named.Length > 0 ? named : mates;
        if (AutonomousChatKnowledge.TryAnswer(player, message, out string knowledge))
        {
            GameBot answerer = pool[0];
            AutonomousLivingServer.Later(answerer, AutonomousBotChatCoordinator.ReplyDelayMs(knowledge, 0, Random.Shared),
                () => answerer.Group == group && answerer.IsAlive, () => Send(answerer, knowledge));
            return;
        }
        eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
        // A group is small: one answer, two for a hello or a ding.
        int count = Math.Min(AutonomousBotChatCoordinator.PlayerReplyCount(intent, false, named.Length > 0, pool.Length, Random.Shared),
            intent is eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Ding ? 2 : 1);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int due = 0;
        for (int i = 0; i < pool.Length && used.Count < count; i++)
        {
            GameBot mate = pool[i];
            string reply = AutonomousBotChatCoordinator.ReplyToPlayer(mate, player, message);
            if (string.IsNullOrEmpty(reply) || !AutonomousBotChatCoordinator.TryAddDistinctResponse(used, reply, out reply))
                continue;
            due = AutonomousBotChatCoordinator.ReplyDelayMs(reply, due, Random.Shared);
            string text = reply;
            AutonomousLivingServer.Later(mate, due, () => mate.Group == group && mate.IsAlive && !mate.InCombat, () => Send(mate, text));
        }
    }

    private static void Send(GameBot bot, string text)
    {
        if (bot.Group != null && AutonomousLivingServer.GroupHasPlayer(bot.Group))
            bot.Group.SendMessageToGroupMembers(bot, text, eChatType.CT_Group, eChatLoc.CL_ChatWindow);
    }
}
