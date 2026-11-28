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
        public long LastPlayerReply;
    }

    /// <summary>A player's /group lines are answered at most this often per group.</summary>
    public const int PlayerReplyCooldownMs = 5_000;

    private static readonly ConditionalWeakTable<Group, Next> Groups = new();

    /// <summary>The next gap: a first line soon after the group forms, then every 4-8 minutes.</summary>
    public static long NextGapMs(bool first, Random random) =>
        first ? FirstLineMinMs + random.Next(FirstLineMaxMs - FirstLineMinMs + 1) : MinGapMs + random.Next(MaxGapMs - MinGapMs + 1);

    /// <summary>The budget meter's key for a group.</summary>
    internal static long KeyOf(Group group) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(group);

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

    /// <summary>"can u come to mularn?", "come here", "meet me at the bridge": the player asks a groupmate to come. Pure.</summary>
    public static bool AsksToCome(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        string text = " " + new string(message.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        return new[] { " can u come ", " can you come ", " could u come ", " could you come ", " come to me ", " come here ", " meet me ",
            " come help ", " come with me ", " join me ", " get over here ", " come find me ", " u coming ", " you coming ", " r u coming ", " are you coming " }.Any(text.Contains);
    }

    /// <summary>What a groupmate asked to come says. Same region: it is on its way (it follows the player there). Pure.</summary>
    public static string ComeReply(bool sameRegion, int distance, string zone, Random random)
    {
        string where = string.IsNullOrEmpty(zone) ? "the other side of the realm" : zone;
        if (sameRegion && distance <= 3_000)
            return random.Next(3) switch { 0 => "right behind you", 1 => "here", _ => "coming" };   // promise: true (a groupmate follows the player in its region)
        if (sameRegion)
            return random.Next(2) == 0 ? "omw" : $"omw, im over in {where}, give me a few";   // promise: true (a groupmate follows the player in its region)
        return random.Next(2) == 0 ? $"im over in {where}, cant get to you from here. come find me?" : $"stuck over in {where} for now, meet up later?";
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
        // Group talk is about the hunt the bot shares with the player ("nice pull", "pulling in 5"). Play test 2026-10-27: a
        // groupmate across the zone said "you have the patience of a saint with these pulls" while the player stood in town.
        if (!AutonomousBotCombatCalls.SawTheFight(bot.InCombat, members.Where(member => member is GamePlayer && member.CurrentRegion == bot.CurrentRegion)
                .Select(member => (int?)bot.GetDistanceTo(member)).Min()))
            return false;
        bool known = Groups.TryGetValue(group, out Next next);
        next ??= Groups.GetValue(group, _ => new Next());
        // Beta 1.1.2: the group channel's budget paces the banter (chat-budget.json "group"), after a first quiet minute or two.
        long now = GameLoop.GameLoopTime;
        long key = KeyOf(group);
        if (!known)
        {
            AutonomousChatBudget.Meter.Delay(AutonomousChatBudget.Channel.Group, key, now + NextGapMs(true, Random.Shared));
            return false;
        }
        var verdict = AutonomousChatBudget.Meter.TryStart(AutonomousChatBudget.Channel.Group, key, now,
            AutonomousChatBudget.LiveBand(AutonomousChatBudget.Channel.Group, 0), Random.Shared);
        if (verdict is AutonomousChatBudget.Verdict.HoldCeiling or AutonomousChatBudget.Verdict.HoldGap)
        {
            AutonomousChatWhy.CountHeld(AutonomousChatBudget.Channel.Group,
                verdict == AutonomousChatBudget.Verdict.HoldCeiling ? AutonomousChatWhy.Held.Ceiling : AutonomousChatWhy.Held.Gap);
            return false;
        }
        bool hunted = AutonomousBotCombatCalls.FoughtRecently(group, GameLoop.GameLoopTime);
        // 1.1.3: a line that fits the group's real size and the bot's state ("who is main assist?" not in a duo).
        int size = members.Length;
        bool away = AutonomousChatEngagement.WasAway(bot.DatabaseID, now, bot.IsReturningAfterRelease);
        // RC6 on the owner's PC: "oom, give me a minute" and "need a quick med" from a wizard at full power. A line about the bot's
        // own power or a rest fits only when it is low.
        bool low = bot.MaxMana > 0 && bot.ManaPercent < 40 || bot.EndurancePercent < 30 || bot.HealthPercent < 50;
        if (!AutonomousBotVoices.TryLine(bot, "group", out VC.Picked picked,
                filter: line => (hunted || !AutonomousBotCombatCalls.IsHuntTalk(line.Text)) && (low || !AutonomousBotCombatCalls.ClaimsLow(line.Text)) &&
                                AutonomousChatEngagement.FitsGroup(line.Text, size, away)))
            return false;
        Send(bot, picked.Text, picked.Line?.Invites ?? "");
        AutonomousChatWhy.Record(bot, "group", "ambient", "said", AutonomousBotChatCoordinator.RuleOf(picked.Line, "group"), VC.Specificity(picked.Line),
            "the group channel's turn, out of combat", picked.Text, picked.Line?.Id);
        if (!string.IsNullOrEmpty(picked.Correction))
            AutonomousLivingServer.Later(bot, 1_200 + Random.Shared.Next(1_400), () => bot.Group == group, () => Send(bot, picked.Correction, remember: false));
        Answer(bot, group, picked);
        return true;
    }

    /// <summary>
    /// The talking update audit: the player dies in a group of gamebots and nobody said a word. Now a groupmate does, a moment
    /// later: one that can rez says it will ("rez coming, dont release"), another "ouch, sorry". Not during a wipe call.
    /// </summary>
    public static void OnPlayerDying(GamePlayer player)
    {
        if (!AutonomousBotVoices.Enabled || player?.Group == null || Random.Shared.NextDouble() >= 0.75)
            return;
        Group group = player.Group;
        GameBot[] mates = group.GetMembersInTheGroup().OfType<GameBot>()
            .Where(mate => mate.IsAutonomousWorldBot && !mate.IsTemporaryGroupHelper && mate.IsAlive)
            // Play test 2026-10-27: Yrilda, in Aegir, said "that one came out of nowhere" when the player died in Midgard.
            .Where(mate => AutonomousBotCombatCalls.SawTheFight(mate.InCombat,
                mate.CurrentRegion == player.CurrentRegion ? (int?)mate.GetDistanceTo(player) : null))
            .OrderByDescending(mate => mate.ResurrectionSpell != null && mate.ResurrectionSpell.Level <= mate.Level)
            .ThenBy(_ => Random.Shared.Next()).ToArray();
        if (mates.Length == 0)
            return;
        GameBot speaker = mates[0];
        AutonomousLivingServer.Later(speaker, 1_500 + Random.Shared.Next(2_000), () => speaker.Group == group && speaker.IsAlive && !player.IsAlive,
            () =>
            {
                // Not in a wipe, which has its own call; and "ill rez you" only from a bot that will come and do it (review 2026-10-26).
                var members = group.GetMembersInTheGroup();
                if (members.Count(member => member.IsAlive) * 2 < members.Count)
                    return;
                bool willRez = AutonomousBotChatCoordinator.WillComeRez(speaker, player);
                if (AutonomousBotVoices.TryLine(speaker, "combat", out VC.Picked picked, topic: "player_down", relax: true,
                        withoutFlags: willRez ? null : ["can_rez"]))
                    Send(speaker, picked.Text, "none", player.Name);
            });
    }

    /// <summary>
    /// The talking update: a gamebot that joins the player's group says hello in /group a moment later ("hi all, ty for the
    /// inv"), most of the time. Lines of kind lfg, topic join.
    /// </summary>
    public static void OnJoinedPlayerGroup(GameBot bot)
    {
        if (!AutonomousBotVoices.Enabled || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || Random.Shared.NextDouble() >= 0.75)
            return;
        Group group = bot.Group;
        AutonomousLivingServer.Later(bot, 2_500 + Random.Shared.Next(3_500), () => bot.Group == group && bot.IsAlive && !bot.InCombat, () =>
        {
            // 1.1.3: "where are you all?" only to a group of three or more.
            int size = bot.Group?.MemberCount ?? 0;
            if (AutonomousBotVoices.TryLine(bot, "lfg", out VC.Picked picked, topic: "join", relax: true,
                    filter: line => AutonomousChatEngagement.FitsGroup(line.Text, size, false)))
                Send(bot, picked.Text, "none");
        });
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
            int size = group.MemberCount;
            bool away = AutonomousChatEngagement.WasAway(mate.DatabaseID, GameLoop.GameLoopTime, mate.IsReturningAfterRelease);
            if (!AutonomousBotVoices.TryLine(mate, "reply", out VC.Picked reply, answers: answers, slots: slots,
                    filter: line => AutonomousBotVoices.ShapedFits(answers, opener.Text, line.Text) && AutonomousChatEngagement.FitsGroup(line.Text, size, away)))
                continue;
            int delay = AutonomousBotChatCoordinator.ReplyDelayMs(reply.Text, 0, Random.Shared);
            AutonomousLivingServer.Later(mate, delay, () => mate.Group == group && mate.IsAlive && !mate.InCombat, () => Send(mate, reply.Text));
            return;
        }
    }

    /// <summary>
    /// The player said something in /group: a groupmate answers in /group, as /faction and /say do (live test 2026-10-18:
    /// "hi" and "where to next?" in /group got no answer). Bots that are fighting or casting stay quiet. Realism 2b (#479): the
    /// first answer takes the one party reply path (<see cref="AutonomousChatReplyPath.Party"/>, which the question bench runs too),
    /// the line is a turn of the group's conversation once, here, whatever answers it, and every answer is typed at the bot's own
    /// speed with the 12 s cap (<see cref="AutonomousChatArbiter.AnswerDelayMs"/>).
    /// </summary>
    public static void OnPlayerGroupChat(GamePlayer player, string message)
    {
        AutonomousBeggars.OnPlayerAsked(player, message, "group");
        if (player?.Group != null && !string.IsNullOrWhiteSpace(message))
            BotChatTranscript.Player("group", player, message);
        if (!AutonomousBotVoices.Enabled || player?.Group == null || string.IsNullOrWhiteSpace(message))
            return;
        // Beta 1.1.2: the screen runs before every route, the thread follow-ups too (round 3).
        if (AutonomousBotChatCoordinator.Screened(player, message, "group"))
            return;
        Group group = player.Group;
        GameBot[] mates = group.GetMembersInTheGroup().OfType<GameBot>()
            .Where(mate => mate.IsAutonomousWorldBot && !mate.IsTemporaryGroupHelper && mate.IsAlive && !mate.InCombat && !mate.IsCasting)
            .OrderBy(_ => Random.Shared.Next()).ToArray();
        if (mates.Length == 0)
            return;
        // Conversation memory (DESIGN-040, #479): the player's /group line is a turn of the group's conversation before any branch
        // answers it, so a place asked about and answered by the knowledge lookup is kept ("how do i get there" next).
        string key = AutonomousChatThreads.GroupChannel(group);
        NotePlayerLine(player.Name, key, message, GameLoop.GameLoopTime);
        // The talking update: "why?", "ty", a laugh or an answer to a groupmate's question follows on from what was said.
        if (AutonomousBotChatCoordinator.TryThreadReply(player, message, key, AutonomousBotChatCoordinator.ThreadScope.Group))
            return;
        GameBot[] named = mates.Where(mate => AutonomousBotChatCoordinator.ContainsMention(message, mate.Name)).ToArray();
        Next next = Groups.GetValue(group, _ => new Next());
        lock (next)
        {
            // #479: as in /faction and /say, a question or a line naming a groupmate is never lost to the cooldown.
            if (CooledDown(next.LastPlayerReply, GameLoop.GameLoopTime, AutonomousBotChatCoordinator.NeedsAnswer(message) || named.Length > 0))
            {
                AutonomousChatWhy.Dropped(player.Name, player.Realm, "group", AutonomousChatWhy.Codes.Cooldown, "a groupmate answered you a moment ago and this line asks nothing");
                return;
            }
            next.LastPlayerReply = GameLoop.GameLoopTime;
        }
        // Beta 1.1.2, second pass: a line to someone else in the group draws at most a laugh (light teasing).
        if (named.Length == 0 && AutonomousChatScreen.OtherAddressee(message, null) is { } other)
        {
            eAutonomousChatIntent toOther = AutonomousChatIntentModel.Predict(message);
            if (AutonomousChatScreen.MayReactToOthers("group", toOther) && Random.Shared.NextDouble() < 0.4)
            {
                GameBot teaser = mates[0];
                string laugh = AutonomousBotVoices.StyleFor(teaser, AutonomousChatReplyBudget.Reaction("laugh", Random.Shared));
                AutonomousLivingServer.Later(teaser, AutonomousChatArbiter.AnswerDelayMs(message, laugh, AutonomousBotChatCoordinator.Wpm(teaser), "reaction", false,
                        true, 0, Random.Shared),
                    () => teaser.Group == group && teaser.IsAlive, () => Send(teaser, laugh, "", player.Name));
            }
            else
                AutonomousChatWhy.RecordSilence(player.Realm, "group", "player " + player.Name, "none other", 0, $"said to {other}", player.Name);
            return;
        }
        GameBot[] pool = named.Length > 0 ? named : mates;
        // "eithne what spec r u": the name says who it is for (the holdout check).
        if (named.Length > 0 && AutonomousChatThreads.WithoutName(message, named[0].Name) is { Length: > 0 } unnamed)
            message = unnamed;
        // 1.2.3 (owner's game): "where is that" right after the leader's "meet up at Humberton Castle" is completed from the
        // group's conversation before the knowledge lookup sees it ("where is Humberton Castle"), and the bot that named the
        // place answers it. #479: the bot that has the floor takes the line first, whatever answers it.
        string resolved = ResolvedInGroup(player, group, message, out long floorBot);
        pool = AnswererFirst(pool, floorBot, bot => bot.DatabaseID);
        eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(resolved);
        // Chat audit CORPUS-C-012: a bare "gz" in /group was thanked ("ty, off to the trainer now") by a groupmate that had not
        // levelled. Only a bot that just levelled takes congratulations.
        pool = CongratulatedOnly(intent, pool, AutonomousBotVoices.JustLeveled);
        if (pool.Length == 0)
            return;
        // A group is small: one answer, two for a hello or a ding.
        int count = Math.Min(AutonomousBotChatCoordinator.PlayerReplyCount(intent, false, named.Length > 0, pool.Length, Random.Shared),
            intent is eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Ding ? 2 : 1);
        bool question = AutonomousChatQuestions.IsQuestion(resolved);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int due = 0;
        string lastRoute = null;
        for (int i = 0; i < pool.Length && (i == 0 || used.Count < count); i++)
        {
            GameBot mate = pool[i];
            AutonomousBotChatCoordinator.ReplyChoice choice = PartyChoice(mate, player, resolved, intent, named.Length > 0, "group", i == 0 ? count : count - used.Count);
            lastRoute = choice.Route;
            if (string.IsNullOrEmpty(choice.Text) || !AutonomousBotChatCoordinator.TryAddDistinctResponse(used, choice.Text, out string reply))
            {
                if (i == 0 && count == 0)
                    break;
                continue;
            }
            // A warning ("adds!", "inc") is taken at half the typing delay, and by a groupmate already fighting too: after "adds!" the
            // mate is usually in combat, and "on it" must not be dropped (as before #479).
            bool warning = i == 0 && AutonomousChatReplyBudget.IsAlarm(resolved, intent);
            due = WarningDueMs(AutonomousChatArbiter.AnswerDelayMs(resolved, reply, AutonomousBotChatCoordinator.Wpm(mate), choice.Route, i == 0 && question, true,
                due, Random.Shared), warning);
            string text = reply;
            AutonomousLivingServer.Later(mate, due, () => mate.Group == group && mate.IsAlive && (warning || !mate.InCombat), () =>
            {
                Send(mate, text, "", player.Name);
                AutonomousChatWhy.Record(mate, "group", "player " + player.Name, "said", choice.Route, AutonomousChatReplyFit.Of(choice.Route, resolved),
                    named.Length > 0 ? "named in the line" : "a groupmate answers", text, choice.Line?.Id, player.Name);
            });
            // A step before the voiced reply (knowledge, a warning, "come", the meet-up, a pause) is answered once.
            if (!IsVoicedRoute(choice.Route))
                break;
        }
        if (used.Count == 0)
            AutonomousChatWhy.RecordSilence(player.Realm, "group", "player " + player.Name, lastRoute ?? "none", AutonomousChatReplyFit.Of(lastRoute, resolved),
                count == 0 ? $"a line like this ({intent}) draws no answer" : "no reply fits it", player.Name);
    }

    /// <summary>Whether the 5 s cooldown drops a player's /group line: not a question or a line naming a groupmate (#479). Pure.</summary>
    public static bool CooledDown(long lastReply, long now, bool needsAnswer) =>
        !needsAnswer && lastReply > 0 && now - lastReply < PlayerReplyCooldownMs;

    /// <summary>When the first answer to a warning is due: at half the typing delay (the talking update audit; #479 keeps it). Pure.</summary>
    public static int WarningDueMs(int due, bool warning) => warning ? due / 2 : due;

    /// <summary>Whether a party answer came from the voiced reply (more than one may answer a hello), not a step before it. Pure.</summary>
    public static bool IsVoicedRoute(string route) =>
        route != null && !(route.StartsWith("knowledge", StringComparison.Ordinal) || route is "context alarm" or "context come" or "live meetup" or "live pause");

    /// <summary>The player's line as a turn of a party channel's conversation, once (a second call for the same line adds nothing).</summary>
    internal static void NotePlayerLine(string player, string key, string message, long now)
    {
        if (ChatConversations.Live.In(player, key, now)?.Turns.LastOrDefault() is not { ByPlayer: true } last || last.Text != message.Trim())
            ChatConversations.Live.NotePlayer(player, key, message, now);
    }

    /// <summary>
    /// A groupmate's or companion's answer to its player (#479): <see cref="AutonomousChatReplyPath.Party"/> with the live world in its
    /// ports. <paramref name="channel"/> is "group" or "say"; <paramref name="replies"/> is how many voiced answers the line may still
    /// draw (0: only a step before the voiced reply may answer it).
    /// </summary>
    internal static AutonomousBotChatCoordinator.ReplyChoice PartyChoice(GameBot mate, GamePlayer player, string message, eAutonomousChatIntent intent,
        bool addressed, string channel, int replies)
    {
        bool sameRegion = mate.CurrentRegion == player.CurrentRegion;
        int distance = sameRegion ? mate.GetDistanceTo(player) : int.MaxValue;
        return PartyWith(AutonomousBotChatCoordinator.PortsFor(mate, player), message, intent, addressed, channel, replies,
            knowledge: () => AutonomousChatKnowledge.TryAnswer(player, message, out string known) ? known : null,
            alarm: () => AlarmReply(mate, player, message, intent, channel),
            come: () => ComeReply(sameRegion, distance, mate.CurrentZone?.Description, Random.Shared),
            meetup: () => AutonomousChatMeetup.PartyReply(message, sameRegion && distance <= AutonomousChatMeetup.BesideRange, sameRegion,
                mate.CurrentZone?.Description, Random.Shared),
            pause: () => AutonomousChatMeetup.PauseReply(message, mate.HealthPercent, mate.MaxMana > 0 ? (int?)mate.ManaPercent : null, Random.Shared),
            voiced: () => AutonomousBotChatCoordinator.VoicedChoice(mate, message, () => null, player, addressed: true, channel: channel));
    }

    /// <summary>
    /// <see cref="PartyChoice"/> with its pieces as ports (#479; the question bench runs it on its stub world):
    /// <see cref="AutonomousChatReplyPath.Party"/>, with no voiced reply once the line may draw no more (<paramref name="replies"/> 0).
    /// </summary>
    public static AutonomousBotChatCoordinator.ReplyChoice PartyWith(AutonomousChatReplyPath.Ports ports, string message, eAutonomousChatIntent intent,
        bool addressed, string channel, int replies, Func<string> knowledge, Func<string> alarm, Func<string> come, Func<string> meetup, Func<string> pause,
        Func<AutonomousBotChatCoordinator.ReplyChoice> voiced) =>
        AutonomousChatReplyPath.Party(ports, message, intent, addressed, channel, knowledge, alarm, come, meetup, pause, replies <= 0 ? null : voiced);

    /// <summary>
    /// The talking update audit: "adds!!", "INC", "train coming RUN" in a PvE group drew "im nowhere near the frontier". A groupmate
    /// takes the warning ("got it", "on it"); Beta 1.1.2, round 2: an inc call gets a terse "where" or "on it", not "ty for the
    /// heads up" (too polite for a call). Unstyled, or null (then the voiced reply answers it as a warning).
    /// </summary>
    private static string AlarmReply(GameBot heard, GamePlayer player, string message, eAutonomousChatIntent intent, string channel) =>
        AutonomousChatContext.Reply(message, intent, AutonomousChatShortTalk.QuestionShape(message),
            AutonomousBotChatCoordinator.ContextFor(heard, player, channel), Random.Shared)?.Text;

    /// <summary>For a congratulation, only the groupmates that just levelled (anyone else would not know what for); any other
    /// line keeps the whole pool. Pure.</summary>
    public static T[] CongratulatedOnly<T>(eAutonomousChatIntent intent, T[] pool, Func<T, bool> justLeveled) =>
        intent == eAutonomousChatIntent.Congrats ? pool.Where(justLeveled).ToArray() : pool;

    /// <summary>
    /// The player's /group line completed from the group's conversation ("where is that" after the leader's "meet up at Humberton
    /// Castle" -> "where is Humberton Castle"), and the bot that has the floor there (0: none).
    /// </summary>
    private static string ResolvedInGroup(GamePlayer player, Group group, string message, out long floorBot)
    {
        floorBot = 0;
        if (player == null || group == null)
            return message;
        string key = AutonomousChatThreads.GroupChannel(group);
        long now = GameLoop.GameLoopTime;
        floorBot = ChatConversations.Live.FloorHolder(player.Name, key, now);
        return ChatConversations.Resolve(message, ChatConversations.Live.In(player.Name, key, now), player.Level);
    }

    /// <summary>The pool with the bot that has the floor (the leader that named the place) first, when it is in it. Pure.</summary>
    public static T[] AnswererFirst<T>(T[] pool, long floorBot, Func<T, long> idOf)
    {
        if (pool == null || pool.Length < 2 || floorBot <= 0)
            return pool;
        int at = Array.FindIndex(pool, bot => bot != null && idOf(bot) == floorBot);
        if (at <= 0)
            return pool;
        var ordered = new T[pool.Length];
        ordered[0] = pool[at];
        for (int i = 0, j = 1; i < pool.Length; i++)
            if (i != at)
                ordered[j++] = pool[i];
        return ordered;
    }

    /// <summary>A bot's line in /group. The talking update: remembered in the group's thread (with what it invites, and the
    /// player it answered), so "why?", "ty" and answers to its questions follow on as in /faction.</summary>
    internal static void Send(GameBot bot, string text, string invites = "", string to = null, bool remember = true)
    {
        // 1.1.3, after RC6: the time-of-day pass every bot line takes (AutonomousBotVoiceCorpus.ForNow).
        text = AutonomousBotVoiceCorpus.ForNow(text);
        // A formal companion (its Speech option) says only its own companion lines (CompanionChat165): one voice.
        if (bot.Group != null && AutonomousLivingServer.GroupHasPlayer(bot.Group) && !CompanionChat165.IsFormal(bot))
        {
            // Issue #475: out through the say gate (a pass-through).
            BotSayGate.Say(new BotSayGate.Utterance("group", bot.Name, bot.Realm, text) { Body = bot }, said =>
            {
                bot.Group.SendMessageToGroupMembers(bot, said, eChatType.CT_Group, eChatLoc.CL_ChatWindow);
                return said;
            });
            AutonomousChatEngagement.NoteGroupLine(bot.DatabaseID, text, GameLoop.GameLoopTime);
            AutonomousChatBudget.Meter.Count(AutonomousChatBudget.Channel.Group, KeyOf(bot.Group), GameLoop.GameLoopTime);
            if (remember)
            {
                string key = AutonomousChatThreads.GroupChannel(bot.Group);
                long now = GameLoop.GameLoopTime;
                AutonomousChatThreads.Log.Record(key, bot.DatabaseID, bot.Name, text, invites, now, to);
                // Realism 2b (#479; channels.md finding 9): a groupmate's line is a turn of the group's conversation for every player
                // in it, so the place it names is "there" and its question is open ("how do i get there" after "try spindel").
                foreach (GamePlayer reader in bot.Group.GetPlayersInTheGroup())
                    ChatConversations.Live.NoteBot(reader.Name, bot.DatabaseID, bot.Name, key, text, now);
            }
        }
    }
}
