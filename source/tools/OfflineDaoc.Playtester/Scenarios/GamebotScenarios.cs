// The scenarios that need autonomous gamebots (the real test world: make_test_world.py from-release adds a roster).
// On the minimal world, which has none, they are skipped, never passed.
namespace OfflineDaoc.Playtester.Scenarios;

internal static class Gamebots
{
    /// <summary>Waits until the world database shows gamebots of the realm online (the server writes IsOnline).</summary>
    public static async Task<IReadOnlyList<WorldBot>> OnlineAsync(ScenarioContext ctx, int realm, int atLeast = 1)
    {
        if (ctx.World.Bots(realm).Count == 0)
            throw new SkipScenario("no gamebots of this realm in the test world (the minimal world has none; make the real test world " +
                "with make_test_world.py from-release)");
        IReadOnlyList<WorldBot> online = Array.Empty<WorldBot>();
        await ctx.Step($"wait for {atLeast} gamebot(s) of realm {realm} to be online", async () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed < ctx.Options.BotsOnlineTimeout)
            {
                online = ctx.World.Bots(realm).Where(b => b.Online).ToList();
                if (online.Count >= atLeast)
                    break;
                await Task.Delay(3000);
            }
            ctx.Measure("gamebots online", online.Count);
            ctx.Measure("seconds until gamebots online", Math.Round(watch.Elapsed.TotalSeconds, 1));
            ScenarioContext.Check(online.Count >= atLeast, $"{online.Count} online after {ctx.Options.BotsOnlineTimeout.TotalSeconds:0} s");
            return string.Join(", ", online.Select(b => $"{b.Name} ({b.ClassName} {b.Level})"));
        });
        return online;
    }
}

/// <summary>(b) A tell to a gamebot gets an answer.</summary>
internal sealed class TellGamebot : Scenario
{
    public override string Id => "tell-gamebot";
    public override string Title => "A tell to a gamebot gets a reply";
    public override string Checks => "A player sends an online gamebot of its realm a tell and gets a tell back within the reply time " +
        "(--reply-seconds, default 45; the bot reads, waits while fighting, then types).";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        _ = ctx.World.Bots(1).Count > 0 ? 0 : throw new SkipScenario("no gamebots in the test world (the minimal world has none)");
        var player = await ctx.LoginAsync("player", "playtester");
        await ctx.PlayAsync(player, "Pttell");
        var online = await Gamebots.OnlineAsync(ctx, 1);
        var replies = new List<double>();
        foreach (var bot in online.Take(3))
        {
            await ctx.Step($"tell {bot.Name}: \"hey, how's it going?\"", async () =>
            {
                int mark = player.Mark();
                double sent = player.Now;
                player.Tell(bot.Name, "hey, how's it going?");
                var reply = await player.WaitForChatAsync(l => ChatTypes.IsTellFrom(l, bot.Name) ||
                    l.Type == ChatTypes.System && l.Text.Contains(bot.Name) && l.Text.Contains("not in the game", StringComparison.OrdinalIgnoreCase),
                    ctx.Options.TellReplyTimeout, mark);
                ScenarioContext.Check(reply != null, $"no reply within {ctx.Options.TellReplyTimeout.TotalSeconds:0} s");
                ScenarioContext.Check(reply!.Type == ChatTypes.Send, $"the server answered: {reply.Text}");
                replies.Add(Math.Round(reply.At - sent, 2));
                return $"{reply.At - sent:0.0} s: {reply.Text}";
            });
        }
        ctx.Measure("reply seconds", string.Join(", ", replies));
    }
}

/// <summary>(b) /faction carries bot lines at a measured rate.</summary>
internal sealed class FactionRate : Scenario
{
    public override string Id => "faction-rate";
    public override string Title => "/faction shows bot lines at a measured rate";
    public override string Checks => "A player listens to /faction for --listen-seconds (default 180) after one line of its own and counts " +
        "the gamebots' lines: lines a minute, speakers, answers in the first minute. Fails below --min-faction-per-minute (default 0.5).";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        _ = ctx.World.Bots(1).Count > 0 ? 0 : throw new SkipScenario("no gamebots in the test world (the minimal world has none)");
        var player = await ctx.LoginAsync("player", "playtester");
        var me = await ctx.PlayAsync(player, "Ptfact");
        await Gamebots.OnlineAsync(ctx, 1);
        await ctx.Step($"say one line in /faction, then listen {ctx.Options.FactionListen.TotalSeconds:0} s", async () =>
        {
            int mark = player.Mark();
            double start = player.Now;
            player.Faction("anyone up for a group in camelot hills?");
            await Task.Delay(ctx.Options.FactionListen);
            var lines = player.ChatSnapshot().Where(l => l.At >= start && ChatTypes.IsFaction(l, out _)).ToList();
            var others = lines.Where(l => ChatTypes.IsFaction(l, out string who) && !who.Equals(me.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var speakers = others.Select(l => { ChatTypes.IsFaction(l, out string who); return who; }).Distinct().ToList();
            bool echoed = lines.Count > others.Count;
            double minutes = ctx.Options.FactionListen.TotalMinutes;
            double rate = Math.Round(others.Count / minutes, 2);
            int firstMinute = others.Count(l => l.At - start <= 60);
            ctx.Measure("faction lines from others", others.Count);
            ctx.Measure("faction lines per minute", rate);
            ctx.Measure("faction speakers", speakers.Count);
            ctx.Measure("faction lines in the first minute after ours", firstMinute);
            ctx.Measure("our own line shown back to us", echoed);
            ScenarioContext.Check(others.Count > 0 && rate >= ctx.Options.MinFactionPerMinute,
                $"{others.Count} line(s) in {minutes:0.#} min = {rate}/min, below {ctx.Options.MinFactionPerMinute}/min");
            _ = mark;
            return $"{others.Count} lines from {speakers.Count} speakers in {minutes:0.#} min ({rate}/min); {firstMinute} in the first minute";
        });
    }
}

/// <summary>(c) Hook: a kill quest done in a group with a gamebot. The group part runs; the quest steps are marked TODO.</summary>
internal sealed class KillQuestInBotGroup : Scenario
{
    public override string Id => "kill-quest-bot-group";
    public override string Title => "A kill quest in a group with a gamebot (hook)";
    public override string Checks => "The GM jumps to an online gamebot, invites it by name and checks it joins; then finds a kill quest " +
        "for the group's level in the world data, jumps to its giver and asks for the offer. Killing and the turn-in are still TODO.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        _ = ctx.World.Bots(1).Count > 0 ? 0 : throw new SkipScenario("no gamebots in the test world (the minimal world has none)");
        ScenarioContext.Check(ctx.World.AccountPrivLevel("playtestergm") >= 2, "the test world's playtestergm account is not a GM");
        var gm = await ctx.LoginAsync("gm", "playtestergm");
        await ctx.PlayAsync(gm, "Ptgm");
        var online = await Gamebots.OnlineAsync(ctx, 1);
        await BotGroup.JoinAsync(ctx, gm, online);
        await Quests.TakeQuestAsync(ctx, gm, new[] { 0, 1 }, "kill");
        ctx.Todo("kill the quest target with the gamebot assisting: /jump to the target's camp (Mob rows named in TargetName), /target it, " +
            "attack, and wait for the QuestEntry goal to show done; check the bot gets its kill credit (UT_QuestCredit covers the rule)");
        ctx.Todo("turn the quest in at the giver: interact, wait for the reward window (Dialog 0x21), answer it (QuestRewardChosen 0x40) " +
            "and check the journal entry is gone and the reward XP / money arrived (PointsUpdate)");
    }
}

/// <summary>(c) A delivery quest from the world data, taken, carried and handed in.</summary>
internal sealed class DeliveryQuest : Scenario
{
    public override string Id => "delivery-quest";
    public override string Title => "A delivery quest";
    public override string Checks => "The GM finds a delivery quest (first step Deliver) in the world data, jumps to its giver and takes it, " +
        "then does each step: /jump to the step's target, give it the step's item from the backpack (or interact), and checks the " +
        "journal moves on; at the last step the quest is finished and its reward XP arrives.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        if (ctx.World.IsMinimal)
            throw new SkipScenario("the minimal world has no quests or NPCs");
        ScenarioContext.Check(ctx.World.AccountPrivLevel("playtestergm") >= 2, "the test world's playtestergm account is not a GM");
        var gm = await ctx.LoginAsync("gm", "playtestergm");
        await ctx.PlayAsync(gm, "Ptdeliv");
        var quest = await Quests.TakeQuestAsync(ctx, gm, new[] { 2, 3 }, "delivery");
        await Quests.RunStepsAsync(ctx, gm, quest);
    }
}

internal static class BotGroup
{
    /// <summary>The GM jumps to a gamebot (its last saved position), sees it, invites it by name and waits for the group window.</summary>
    public static async Task JoinAsync(ScenarioContext ctx, ScriptedPlayer gm, IReadOnlyList<WorldBot> online)
    {
        WorldBot? joined = null;
        foreach (var bot in online.Take(3))
        {
            bool inView = false;
            await ctx.Step($"/jump to {bot.Name} and see it", async () =>
            {
                // /jump to <name> finds NPCs by name (JumpCommandHandler: WorldMgr.GetNPCsByName), gamebots included; the
                // bot's saved position is the fallback (it is minutes old: the first run here found the bot gone from it).
                int mark = gm.Mark();
                gm.Command($"/jump to {bot.Name}");
                var seen = await gm.SeeAsync(bot.Name, TimeSpan.FromSeconds(15), mark);
                string how = "by name";
                if (seen == null)
                {
                    var fresh = ctx.World.Bots(bot.Realm).FirstOrDefault(b => b.Name == bot.Name) ?? bot;
                    mark = gm.Mark();
                    gm.Command($"/jump to {fresh.X} {fresh.Y} {fresh.Z} {fresh.Region}");
                    seen = await gm.SeeAsync(bot.Name, TimeSpan.FromSeconds(15), mark);
                    how = "to its saved position";
                }
                inView = seen != null;
                return inView ? $"jumped {how}: {bot.Name} in view (object {seen!.ObjectId})" : $"{bot.Name} not in view after both jumps";
            });
            if (!inView)
                continue;
            await ctx.Step($"/invite {bot.Name}: it joins the group", async () =>
            {
                int mark = gm.Mark();
                gm.Invite(bot.Name);
                var group = await gm.WaitForOrNullAsync<Protocol.GroupWindow>(g => g.Members.Any(m => m.Name.Equals(bot.Name, StringComparison.OrdinalIgnoreCase)),
                    TimeSpan.FromSeconds(60), mark);
                if (group == null)
                {
                    var why = gm.ChatSnapshot().LastOrDefault(l => l.Type == ChatTypes.System && l.Text.Contains(bot.Name));
                    throw new PlaytestException($"{bot.Name} did not join within 60 s{(why != null ? ": " + why.Text : "")}");
                }
                return "group: " + string.Join(", ", group.Members.Select(m => $"{m.Name} ({m.ClassName} {m.Level})"));
            });
            joined = bot;
            break;
        }
        ScenarioContext.Check(joined != null, "no gamebot could be reached to invite");
        ctx.Measure("gamebot in the group", joined!.Name);
    }
}

internal static class Quests
{
    /// <summary>
    /// Does a data quest's deliver and interact steps in order (DataQuest.eStepType 2-5): /jump to the step's target by
    /// name, give it the step's item from the backpack (Deliver) or interact with it (Interact), and wait for the quest to
    /// move on. A kill, whisper, search or collect step is left as a TODO.
    /// </summary>
    public static async Task RunStepsAsync(ScenarioContext ctx, ScriptedPlayer gm, QuestCandidate quest)
    {
        var steps = ctx.World.QuestSteps(quest.Id);
        long xpBefore = gm.Experience;
        foreach (var step in steps)
        {
            if (step.Type is not (2 or 3 or 4 or 5))
            {
                ctx.Todo($"step {step.Index + 1} of {quest.Name} is step type {step.Type} ({step.Text}): the scripted player does deliver and interact steps only");
                return;
            }
            SeenLiving? target = null;
            await ctx.Step($"step {step.Index + 1}: /jump to {step.Target}", async () =>
            {
                // /jump to <name> takes one word (a second one is read as a realm), so jump to the NPC's Mob row.
                var at = ctx.World.MobPosition(step.Target, gm.Region) ?? throw new PlaytestException($"no Mob row named {step.Target}");
                int mark = gm.Mark();
                gm.Command($"/jump to {at.X} {at.Y} {at.Z} {at.Region}");
                target = await gm.SeeAsync(step.Target, TimeSpan.FromSeconds(15), mark);
                ScenarioContext.Check(target != null, $"{step.Target} not in view at {at.X},{at.Y} region {at.Region}");
                return $"{step.Target} is object {target!.ObjectId}";
            });
            bool give = step.Type is 2 or 3 && step.Item.Length > 0;
            await ctx.Step($"step {step.Index + 1}: {(give ? $"give {step.Target} the {step.Item}" : $"interact with {step.Target}")}", async () =>
            {
                int mark = gm.Mark();
                gm.Target(step.Target);
                if (give)
                {
                    string itemName = ctx.World.ItemName(step.Item) ?? step.Item;
                    int? slot = gm.BackpackSlotOf(itemName);
                    ScenarioContext.Check(slot != null, $"no {itemName} ({step.Item}) in the backpack the server showed us");
                    gm.GiveItem(slot!.Value, target!.ObjectId);
                }
                else
                    gm.Interact(target!.ObjectId);
                bool last = step.Index == steps.Count - 1;
                // The quest moves on: a journal update, a quest line from the server, or (at the end) reward experience.
                var moved = await gm.WaitForOrNullAsync<object>(e => e is Protocol.QuestEntry ||
                    e is ChatLine l && (l.Text.Contains("quest", StringComparison.OrdinalIgnoreCase) || l.Text.Contains("experience", StringComparison.OrdinalIgnoreCase)),
                    TimeSpan.FromSeconds(10), mark);
                ScenarioContext.Check(moved != null, "nothing happened: no journal update and no quest line");
                await Task.Delay(1000);
                var lines = gm.ChatSnapshot().Where(l => l.At >= gm.Now - 12 && l.Type != ChatTypes.Broadcast && l.Type != ChatTypes.Lfg && l.Type != ChatTypes.Trade)
                    .Select(l => l.Text).TakeLast(4);
                return (last ? "last step; " : "") + string.Join(" / ", lines);
            });
        }
        await Task.Delay(1000);
        if (xpBefore >= 0 && gm.Experience >= 0)
            ctx.Measure("experience gained over the quest", gm.Experience - xpBefore);
        var done = gm.ChatSnapshot().LastOrDefault(l => l.Text.StartsWith($"You have completed the {quest.Name} quest", StringComparison.OrdinalIgnoreCase));
        ctx.Measure("completed line", done?.Text ?? "(none seen)");
        ScenarioContext.Check(done != null, $"after the last step no \"You have completed the {quest.Name} quest\" line");
    }

    private static readonly System.Text.RegularExpressions.Regex Keyword = new(@"\[([^\]]+)\]");

    /// <summary>The server gave the quest: its line ("You have been given the X quest.") or a journal entry with a name.</summary>
    private static bool IsGiven(object e) =>
        e is ChatLine l && l.Text.StartsWith("You have been given the ", StringComparison.Ordinal) ||
        e is Protocol.QuestEntry q && q.Name.Length > 0 && !q.Name.StartsWith("You have no current", StringComparison.Ordinal);

    /// <summary>Picks a quest whose first step is one of the step types, sets the GM to its level, jumps to the giver,
    /// interacts and waits for the offer window (Dialog 0x22); accepts it (QuestSubscribe 0x64) and waits for the journal.</summary>
    public static async Task<QuestCandidate> TakeQuestAsync(ScenarioContext ctx, ScriptedPlayer gm, int[] stepTypes, string kind)
    {
        var candidates = ctx.World.QuestCandidates(gm.Region, stepTypes, maxMinLevel: 5);
        ctx.Measure($"{kind} quests in region {gm.Region} for levels up to 5", candidates.Count);
        if (candidates.Count == 0)
        {
            ctx.Todo($"no {kind} data quest with a giver in region {gm.Region} up to level 5: pick another region or level");
            throw new SkipScenario($"no {kind} quest found in region {gm.Region}");
        }
        var quest = candidates[0];
        ctx.Measure("quest", $"{quest.Name} (DataQuest {quest.Id}) from {quest.Giver}, first target {quest.Target}");
        await ctx.Step("/player level 5 (the GM's own character, for the quest's level)", async () =>
        {
            int mark = gm.Mark();
            gm.Command("/player level 5");
            var line = await gm.WaitForChatAsync(l => l.Type == ChatTypes.System, TimeSpan.FromSeconds(5), mark);
            return line?.Text ?? "(no answer)";
        });
        SeenLiving? giver = null;
        await ctx.Step($"/jump to {quest.Giver} and see them", async () =>
        {
            int mark = gm.Mark();
            gm.Command($"/jump to {quest.GiverX} {quest.GiverY} {quest.GiverZ} {quest.Region}");
            giver = await gm.SeeAsync(quest.Giver, TimeSpan.FromSeconds(20), mark);
            ScenarioContext.Check(giver != null, $"{quest.Giver} not in view at {quest.GiverX},{quest.GiverY}");
            return $"{quest.Giver} is object {giver!.ObjectId}";
        });
        await ctx.Step($"interact with {quest.Giver} (and whisper the [keyword]): the quest is taken", async () =>
        {
            int mark = gm.Mark();
            gm.Target(quest.Giver);
            gm.Interact(giver!.ObjectId);
            var answer = await gm.WaitForOrNullAsync<object>(e => e is Protocol.DialogBox { Code: 0x22 } || IsGiven(e), TimeSpan.FromSeconds(4), mark);
            string how = "on interact";
            if (answer == null)
            {
                // A standard DataQuest giver answers the interact with its text, and whispering its AcceptText gives the quest
                // ("I need someone to ... [Lieutenant] Kuebler" / AcceptText "Lieutenant": "You have been given the Simple Request
                // quest."). The AcceptText is not always the [keyword] shown: Ripper says "[throatripper]" and accepts "kill it"
                // (first runs on the release test world, 2026-11-04). Without an AcceptText, the [keyword] is whispered.
                var said = gm.ChatSnapshot().LastOrDefault(l => l.At >= gm.Now - 6 && Keyword.IsMatch(l.Text));
                string keyword = quest.AcceptText.Length > 0 ? quest.AcceptText : said != null ? Keyword.Match(said.Text).Groups[1].Value : "";
                ScenarioContext.Check(keyword.Length > 0, "no quest offer, no AcceptText and no [keyword] from the giver");
                int whisperMark = gm.Mark();
                gm.Command("/whisper " + keyword);
                answer = await gm.WaitForOrNullAsync<object>(e => e is Protocol.DialogBox { Code: 0x22 } || IsGiven(e), TimeSpan.FromSeconds(8), whisperMark);
                how = $"after whispering \"{keyword}\"" + (said != null ? $" (the giver said \"{said.Text}\")" : "");
            }
            ScenarioContext.Check(answer != null, "the quest was neither offered nor given");
            if (answer is Protocol.DialogBox offer)
            {
                // An offer window (a RewardQuest or a DataQuest with AcceptText): accept it. The accept packet (QuestSubscribe
                // 0x64) is not yet confirmed against game.dll: no giver in these runs opened a window.
                int acceptMark = gm.Mark();
                gm.AnswerQuestOffer(offer, accept: true);
                answer = await gm.WaitForOrNullAsync<object>(IsGiven, TimeSpan.FromSeconds(10), acceptMark);
                ScenarioContext.Check(answer != null, $"accepted the offer of {offer.QuestTitle}, but the quest was not given");
                how += $", offer window {offer.QuestTitle} accepted";
            }
            var given = gm.ChatSnapshot().LastOrDefault(l => l.Text.StartsWith("You have been given the ", StringComparison.Ordinal));
            ctx.Measure("quest taken", given?.Text ?? "(journal entry)");
            return $"{how}: {given?.Text ?? "journal entry added"}";
        });
        return quest;
    }
}
