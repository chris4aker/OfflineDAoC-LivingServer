// The scenarios that need autonomous gamebots (the real test world: make_test_world.py from-release adds a roster).
// On the minimal world, which has none, they are skipped, never passed.
using System.Text.RegularExpressions;

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

    /// <summary>
    /// Waits until a gamebot is online and has trained up to its level (its record's "trained-level|N" = its level: a
    /// helper bot from make_test_world.py trains at its trainer after its first login). Untrained after the time limit,
    /// the scenario goes on with it and says so.
    /// </summary>
    public static async Task<WorldBot> WaitTrainedAsync(ScenarioContext ctx, string name, TimeSpan timeout)
    {
        WorldBot? bot = null;
        await ctx.Step($"wait for {name} to be online and trained", async () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            (int Level, int Trained)? training = null;
            while (watch.Elapsed < timeout)
            {
                bot = ctx.World.Bots(1).FirstOrDefault(b => b.Name == name && b.Online);
                training = ctx.World.BotTraining(name);
                if (bot != null && training is { } t && t.Trained >= t.Level)
                    break;
                await Task.Delay(3000);
            }
            ScenarioContext.Check(bot != null, $"{name} did not come online within {timeout.TotalSeconds:0} s");
            bool trained = training is { } done && done.Trained >= done.Level;
            ctx.Measure("helper trained", trained ? $"yes, to level {training!.Value.Trained}" : $"no (trained to {training?.Trained})");
            ctx.Measure("seconds until the helper was ready", Math.Round(watch.Elapsed.TotalSeconds, 1));
            return $"{name}, level {bot!.Level} {bot.ClassName}, " + (trained ? "trained" : $"NOT trained (trained-level {training?.Trained})");
        });
        return bot!;
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

/// <summary>(b) /faction carries bot lines at the rate the server's chat budget predicts.</summary>
internal sealed class FactionRate : Scenario
{
    public override string Id => "faction-rate";
    public override string Title => "/faction shows bot lines at the predicted rate";
    public override string Checks => "With most of the realm's gamebots online (80% of the roster), a player says one /faction line and " +
        "listens: at least --listen-seconds (default 180) and until 10 lines are expected, at most --max-listen-seconds (default 1800). " +
        "It counts the gamebots' lines (lines a minute, speakers, the first minute) and predicts the rate as the server does: the " +
        "faction target plus its reply target in chat-budget.json for the bots online, the hour factor and bot_voices_chat_rate. It fails " +
        "below half of that (or below --min-faction-per-minute when given), and flags a rate above the ambient ceiling plus the reply " +
        "ceiling (fails it with --fail-over-ceiling).";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        int roster = ctx.World.Bots(1).Count;
        _ = roster > 0 ? 0 : throw new SkipScenario("no gamebots in the test world (the minimal world has none)");
        var player = await ctx.LoginAsync("player", "playtester");
        var me = await ctx.PlayAsync(player, "Ptfact");
        await Gamebots.OnlineAsync(ctx, 1, Math.Max(1, (int)Math.Ceiling(roster * ctx.Options.FactionRosterOnline)));
        var budget = ChatBudget.Load(ctx.Server?.ServerDir);
        var (chatRate, clock) = FactionPrediction.SettingsOf(ctx.World);
        FactionPrediction Predict() => FactionPrediction.Of(budget, ctx.World.Bots(1).Count(b => b.Online), DateTime.Now.Hour, clock,
            chatRate, ctx.Options.FactionPassShare);
        var first = Predict();
        var listen = first.Expected > 0.001 ? TimeSpan.FromMinutes(ctx.Options.FactionExpectedLines / first.Expected) : ctx.Options.FactionListenMax;
        if (listen < ctx.Options.FactionListen) listen = ctx.Options.FactionListen;
        if (listen > ctx.Options.FactionListenMax) listen = ctx.Options.FactionListenMax;
        await ctx.Step($"say one line in /faction, then listen {listen.TotalSeconds:0} s ({first.Expected:0.###}/min predicted)", async () =>
        {
            double start = player.Now;
            player.Faction("anyone up for a group in camelot hills?");
            // The prediction is followed through the listen (bots online, the hour), as the server's live band is.
            var samples = new List<FactionPrediction> { first };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed < listen)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, Math.Max(0.1, (listen - watch.Elapsed).TotalSeconds))));
                samples.Add(Predict());
            }
            var lines = player.ChatSnapshot().Where(l => l.At >= start && ChatTypes.IsFaction(l, out _)).ToList();
            var others = lines.Where(l => ChatTypes.IsFaction(l, out string who) && !who.Equals(me.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var speakers = others.Select(l => { ChatTypes.IsFaction(l, out string who); return who; }).Distinct().ToList();
            bool echoed = lines.Count > others.Count;
            double minutes = listen.TotalMinutes;
            double rate = Math.Round(others.Count / minutes, 3);
            int firstMinute = others.Count(l => l.At - start <= 60);
            double expected = Math.Round(samples.Average(p => p.Expected), 3);
            double mark = Math.Round(ctx.Options.MinFactionPerMinute ?? samples.Average(p => p.PassMark), 3);
            double high = Math.Round(samples.Average(p => p.HighMark), 3);
            var (passes, tooHigh) = FactionPrediction.Judge(others.Count, rate, mark, high);
            ctx.Measure("faction lines from others", others.Count);
            ctx.Measure("faction lines per minute", rate);
            ctx.Measure("expected faction lines per minute (chat budget target + reply target)", expected);
            ctx.Measure("chat budget target and reply target per minute",
                $"{samples.Average(p => p.Band.Target):0.###} + {samples.Average(p => p.Replies.Target):0.###}");
            ctx.Measure("faction pass mark per minute", mark + (ctx.Options.MinFactionPerMinute == null
                ? $" ({ctx.Options.FactionPassShare:0.##} of expected)" : " (--min-faction-per-minute)"));
            ctx.Measure("chat budget floor and ceiling per minute",
                $"{samples.Average(p => p.Band.Floor):0.###} / {samples.Average(p => p.Band.Ceiling):0.###}");
            ctx.Measure("reply ceiling per minute", Math.Round(samples.Average(p => p.Replies.Ceiling), 3));
            ctx.Measure("faction high mark per minute (ceiling + reply ceiling)", high);
            ctx.Measure("faction over the high mark", tooHigh);
            ctx.Measure("expected faction lines in the listen", Math.Round(expected * minutes, 1));
            ctx.Measure("listen minutes", Math.Round(minutes, 1));
            ctx.Measure("gamebots online in the realm (mean, min-max)",
                $"{samples.Average(p => p.BotsOnline):0.#} ({samples.Min(p => p.BotsOnline)}-{samples.Max(p => p.BotsOnline)} of {roster})");
            ctx.Measure("population factor", Math.Round(samples.Average(p => p.PopulationFactor), 4));
            ctx.Measure("hour factor", $"{samples.Average(p => p.HourFactor):0.###} (local hour {first.LocalHour}" +
                (clock ? ", bot_schedule_mode clock)" : ")"));
            ctx.Measure("bot_voices_chat_rate", chatRate);
            ctx.Measure("chat budget", budget.Source);
            ctx.Measure("faction speakers", speakers.Count);
            ctx.Measure("faction lines in the first minute after ours", firstMinute);
            ctx.Measure("our own line shown back to us", echoed);
            ScenarioContext.Check(passes,
                $"{others.Count} line(s) in {minutes:0.#} min = {rate}/min, below the pass mark {mark}/min (expected {expected}/min)");
            string over = $"/faction ran at {rate}/min, above the server's ceiling {high}/min (ambient ceiling + reply ceiling; expected {expected}/min)";
            ScenarioContext.Check(!(tooHigh && ctx.Options.FailOverCeiling), over);
            if (tooHigh)
                ctx.Flag(over);
            return $"{rate}/min measured, {expected}/min expected (pass at {mark}/min, flag above {high}/min{(tooHigh ? ": FLAGGED" : "")}): " +
                $"{others.Count} lines from {speakers.Count} speakers in {minutes:0.#} min; {firstMinute} in the first minute";
        });
    }
}

/// <summary>
/// (c) A kill quest done in a group with a gamebot, as a player meets one: the player is made a trained, armed Armsman a
/// few levels above the target, takes the quest, meets a gamebot above its level (the test world's helper), invites
/// it, fights the target with it, gets the kill credit and hands the quest in.
/// </summary>
internal sealed class KillQuestInBotGroup : Scenario
{
    public override string Id => "kill-quest-bot-group";
    public override string Title => "A kill quest in a group with a gamebot";
    public override string Checks => "The region's kill quest with a hand-in and the weakest target: the GM makes a player an Armsman 4 " +
        "levels above the target (/player class, /player level); it trains at its trainer (/train), buys and wields a weapon of its " +
        "level, takes the quest, meets the test world's helper gamebot (above it), invites it; the GM brings the group to " +
        "the target (/player jump group), they fight it, the kill must count (step item, journal or completed), the hand-in steps " +
        "run and the quest must complete. Reports the experience of the kill and of the hand-in.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        _ = ctx.World.Bots(1).Count > 0 ? 0 : throw new SkipScenario("no gamebots in the test world (the minimal world has none)");
        ScenarioContext.Check(ctx.World.AccountPrivLevel("playtestergm") >= 2, "the test world's playtestergm account is not a GM");
        // the helper: the highest level bot of the realm (make_test_world.py --helper-level, 10 by default)
        var helper = ctx.World.Bots(1).OrderByDescending(b => b.Level).First();
        if (helper.Level < 3)
            throw new SkipScenario("the test world has no helper bot above level 2 (make_test_world.py from-release --helper-level)");
        var gm = await ctx.LoginAsync("gm", "playtestergm");
        var player = await ctx.LoginAsync("player", "playtester");
        await ctx.PlayAsync(gm, "Ptgm");
        await ctx.PlayAsync(player, "Ptkill");   // a Fighter (the server starts every Albion fighter as one); an Armsman below
        var choices = ctx.World.KillQuests(player.Region, (int)player.X, (int)player.Y, maxPlayerLevel: helper.Level - 1);
        ctx.Measure("kill quests for the group", choices.Count);
        if (choices.Count == 0)
            throw new SkipScenario($"no kill quest in region {player.Region} for a level below the helper's {helper.Level}");
        // one with a hand-in, the weakest target first (a player picks a quest it can do with the group it has)
        var kill = choices.Where(c => c.Steps.Count > 1).OrderBy(c => c.TargetLevel).ThenBy(c => c.Distance).FirstOrDefault() ?? choices[0];
        var quest = kill.Quest;
        ctx.Measure("quest", $"{quest.Name} (DataQuest {quest.Id}): kill {kill.Steps[0].Target} (level {kill.TargetLevel}, " +
            $"{kill.Distance:0} units away), {kill.Steps.Count} step(s)");

        await Party.MakeArmsmanAsync(ctx, gm, player, kill.PlayerLevel);
        await Party.BuyWeaponAsync(ctx, gm, player, kill.PlayerLevel);
        await Quests.TakeQuestAsync(ctx, player, gm, quest);
        var bot = await Gamebots.WaitTrainedAsync(ctx, helper.Name, ctx.Options.BotsOnlineTimeout);
        var botSeen = await Party.MeetAndGroupAsync(ctx, gm, player, bot);
        double killStart = player.Now;
        int killMark = player.Mark();
        var target = (await Party.GoToAsync(ctx, gm, player, botSeen, kill.Steps[0].Target, kill.TargetX, kill.TargetY, kill.TargetZ))!;
        await Party.FightAsync(ctx, player, botSeen, target, gm);
        double creditAt = player.Now;
        await ctx.Step("the kill counts for the quest and gives experience", async () =>
        {
            await Task.Delay(2000);
            var lines = player.ChatSnapshot().Where(l => l.At >= killStart).Select(l => l.Text).ToList();
            long gained = player.ExperienceAnnouncedSince(killStart);
            ctx.Measure("experience from the kill", gained);
            ScenarioContext.Check(gained > 0, "no experience for the kill");
            // KillFinish ends the quest; a Kill step moves it on: the target's step item ("You receive the Slith's Tail from
            // Slith!", the next step's delivery), or the journal entry sent again
            bool finished = lines.Any(l => l.StartsWith($"You have completed the {quest.Name} quest", StringComparison.OrdinalIgnoreCase));
            string? item = lines.FirstOrDefault(l => l.StartsWith("You receive the ", StringComparison.Ordinal) &&
                l.Contains($"from {kill.Steps[0].Target}", StringComparison.OrdinalIgnoreCase));
            var entry = await player.WaitForOrNullAsync<Protocol.QuestEntry>(q => q.Name == quest.Name, TimeSpan.FromSeconds(2), killMark);
            bool advanced = finished || item != null || entry != null;
            ScenarioContext.Check(advanced, "the quest did not move on after the kill");
            ctx.Measure("kill credit", finished ? "the quest completed" : item ?? $"journal: {entry!.Text}");
            return $"{gained} experience; " + (finished ? "quest completed" : item ?? $"journal now: {entry!.Text}");
        });
        if (kill.Steps.Count > 1)
            await Quests.RunStepsAsync(ctx, player, gm, quest, fromStep: 1);
        ctx.Measure("experience from the hand-in", player.ExperienceAnnouncedSince(creditAt + 2.5));
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
        var candidates = ctx.World.QuestCandidates(gm.Region, new[] { 2, 3 }, maxMinLevel: 5);
        ctx.Measure($"delivery quests in region {gm.Region} for levels up to 5", candidates.Count);
        if (candidates.Count == 0)
            throw new SkipScenario($"no delivery quest found in region {gm.Region}");
        var quest = candidates[0];
        ctx.Measure("quest", $"{quest.Name} (DataQuest {quest.Id}) from {quest.Giver}, first target {quest.Target}");
        await ctx.Step("/player level 5 (the GM's own character, for the quest's level)", async () =>
        {
            int mark = gm.Mark();
            gm.Command("/player level 5");
            var line = await gm.WaitForChatAsync(l => l.Type == ChatTypes.System, TimeSpan.FromSeconds(5), mark);
            return line?.Text ?? "(no answer)";
        });
        await Quests.TakeQuestAsync(ctx, gm, gm, quest);
        await Quests.RunStepsAsync(ctx, gm, gm, quest);
    }
}

/// <summary>
/// Guild dues and merit with a gamebot in the guild (the check the 1.1.2 playtest could not make): a player's own guild
/// with dues on, a gamebot invited into it and into the group, a few monsters killed together for their coin, and the
/// guild bank and merit read back with /gc info.
/// </summary>
internal sealed class GuildDues : Scenario
{
    public override string Id => "guild-dues";
    public override string Title => "Guild dues and merit with a gamebot in the guild";
    public override string Checks => "The GM forms a guild with the player as leader (/gc create); the player sets dues (/gc dues 50), levels " +
        "up twice (/player levelup: merit for each level), invites the test world's helper gamebot into the guild (/gc invite) and the group, " +
        "and kills up to four monsters with it for their coin. /gc info before and after gives the dues paid into the bank and the merit.";

    private static readonly Regex MeritLine = new(@"Merit Points: (\d+)", RegexOptions.Compiled);
    private static readonly Regex DuesLine = new(@"Dues: (\d+)%? Guild Bank: (.*)", RegexOptions.Compiled);
    private static readonly Regex Coin = new(@"(\d+) (mithril|platinum|gold|silver|copper)", RegexOptions.Compiled);

    /// <summary>"1 gold, 2 silver, 3 copper" (Money.GetString) in copper.</summary>
    internal static long ParseMoney(string text)
    {
        long total = 0;
        foreach (Match m in Coin.Matches(text))
            total += long.Parse(m.Groups[1].Value) * m.Groups[2].Value switch
            {
                "mithril" => 100_000_000_000L, "platinum" => 100_000_000L, "gold" => 10_000L, "silver" => 100L, _ => 1L,
            };
        return total;
    }

    /// <summary>/gc info: (merit, dues percent, bank in copper).</summary>
    internal static async Task<(long Merit, long Dues, long Bank)> InfoAsync(ScriptedPlayer player, TimeSpan timeout)
    {
        await Task.Delay(1000);       // /gc's spam guard
        int mark = player.Mark();
        player.Command("/gc info");
        var merit = await player.WaitForChatAsync(l => MeritLine.IsMatch(l.Text), timeout, mark);
        var dues = await player.WaitForChatAsync(l => DuesLine.IsMatch(l.Text), timeout, mark);
        if (merit == null || dues == null)
            throw new PlaytestException("no /gc info answer");
        var d = DuesLine.Match(dues.Text);
        return (long.Parse(MeritLine.Match(merit.Text).Groups[1].Value), long.Parse(d.Groups[1].Value), ParseMoney(d.Groups[2].Value));
    }

    public override async Task RunAsync(ScenarioContext ctx)
    {
        _ = ctx.World.Bots(1).Count > 0 ? 0 : throw new SkipScenario("no gamebots in the test world (the minimal world has none)");
        ScenarioContext.Check(ctx.World.AccountPrivLevel("playtestergm") >= 2, "the test world's playtestergm account is not a GM");
        var helper = ctx.World.Bots(1).OrderByDescending(b => b.Level).First();
        if (helper.Level < 3)
            throw new SkipScenario("the test world has no helper bot above level 2 (make_test_world.py from-release --helper-level)");
        var gm = await ctx.LoginAsync("gm", "playtestergm");
        var player = await ctx.LoginAsync("player", "playtester");
        await ctx.PlayAsync(gm, "Ptgm");
        await ctx.PlayAsync(player, "Ptguild");
        int level = Math.Max(2, helper.Level - 3);
        await Party.MakeArmsmanAsync(ctx, gm, player, level);
        await Party.BuyWeaponAsync(ctx, gm, player, level);
        string guild = "Playtest " + char.ToUpperInvariant(Names.Fresh("x")[1]) + Names.Fresh("x")[2..];

        await ctx.Step("leave the starting guild (/gc quit, confirm)", async () =>
        {
            int mark = player.Mark();
            player.Command("/gc quit");
            var dialog = await player.AnswerDialogAsync(0x08, yes: true, TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(dialog != null, "no leave-guild dialog");
            var line = await player.WaitForChatAsync(l => l.Text.Contains("left", StringComparison.OrdinalIgnoreCase) || l.Text.Contains("leave", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5), mark);
            return line?.Text ?? dialog!.Text;
        });
        await ctx.Step($"the GM forms \"{guild}\" with {player.CharacterName} as leader (/gc create)", async () =>
        {
            await Party.GmTargetsAsync(gm, player);
            int mark = gm.Mark();
            gm.Command($"/gc create {guild}");
            var line = await gm.WaitForChatAsync(l => l.Text.Contains(guild), TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(line != null, "the guild was not formed");
            return line!.Text;
        });
        await ctx.Step("the leader sets dues (/gc dues 50)", async () =>
        {
            await Task.Delay(1000);   // /gc's spam guard (500 ms) ignores a command after the last one
            int mark = player.Mark();
            player.Command("/gc dues 50");
            var line = await player.WaitForChatAsync(l => l.Type is ChatTypes.System or 0x1a, TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(line != null, "no answer to /gc dues");
            ScenarioContext.Check(line!.Text.Contains("dues", StringComparison.OrdinalIgnoreCase), line.Text);
            return line.Text;
        });
        var before = (Merit: 0L, Dues: 0L, Bank: 0L);
        await ctx.Step("/gc info before", async () =>
        {
            before = await InfoAsync(player, ctx.Options.NetTimeout);
            ScenarioContext.Check(before.Dues > 0, $"dues are {before.Dues}%");
            return $"merit {before.Merit}, dues {before.Dues}%, bank {before.Bank} copper";
        });
        await ctx.Step("two levels (/player levelup) earn the guild merit", async () =>
        {
            var lines = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                await Party.GmTargetsAsync(gm, player);
                int mark = player.Mark();
                gm.Command("/player levelup");
                var line = await player.WaitForChatAsync(l => l.Text.Contains("merit points for your guild"), TimeSpan.FromSeconds(10), mark);
                lines.Add(line?.Text ?? "(no merit line)");
            }
            var now = await InfoAsync(player, ctx.Options.NetTimeout);
            ctx.Measure("merit from two levels", now.Merit - before.Merit);
            ScenarioContext.Check(now.Merit > before.Merit, $"merit stayed {now.Merit}");
            before = now;
            return string.Join(" / ", lines) + $"; merit now {now.Merit}";
        });

        var bot = await Gamebots.WaitTrainedAsync(ctx, helper.Name, ctx.Options.BotsOnlineTimeout);
        var botSeen = await Party.MeetAndGroupAsync(ctx, gm, player, bot);
        await ctx.Step($"/gc invite {bot.Name}: it joins the guild", async () =>
        {
            await Task.Delay(1000);
            int mark = player.Mark();
            player.Command($"/gc invite {bot.Name}");
            var line = await player.WaitForChatAsync(l => l.Text.Contains(bot.Name), TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(line != null, $"no line about {bot.Name} joining");
            ScenarioContext.Check(!line!.Text.Contains("could not", StringComparison.OrdinalIgnoreCase), line.Text);
            return line.Text;
        });

        long purse = player.Money;
        double huntStart = player.Now;
        int now = level + 2;   // after the two levels
        // green to blue for the player: not grey (no experience, no coin: "You gain no experience from this kill!"), and
        // nothing a lone player cannot solo when the gamebot does not join in (level-9 camps came in threes and killed a
        // level-11 Armsman in 9 s; in this container the bot seldom fights, without navmeshes)
        // ... and away from the start village: a kill a guard helps with is worth nothing either (AbstractServerRules:
        // "if a guard is involved, don't give any XP, drop any loot"). The cutpurses and poachers by Cotswold gave no
        // experience and no coin in four runs, most likely for this reason (not confirmed in the server log).
        double sx = player.X, sy = player.Y;
        var monsters = ctx.World.MonstersNear(player.Region, (int)sx, (int)sy, 30000, Math.Max(1, now - 5), now - 3)
            .Where(m => Math.Sqrt(Math.Pow(m.X - sx, 2) + Math.Pow(m.Y - sy, 2)) > 6000)
            .GroupBy(m => m.Name).Select(g => g.First()).Take(8).ToList();
        ctx.Measure("monsters to hunt", string.Join(", ", monsters.Select(m => $"{m.Name} ({m.Level})")));
        ScenarioContext.Check(monsters.Count > 0, $"no monster of level {now - 5}-{now - 3} near the start");
        int kills = 0;
        foreach (var monster in monsters)
        {
            // one that is not up (dead, or roamed off) or that the scripted player cannot reach (a flier): the next
            var target = await Party.GoToAsync(ctx, gm, player, botSeen, monster.Name, monster.X, monster.Y, monster.Z, optional: true);
            if (target == null || !await Party.FightAsync(ctx, player, botSeen, target, gm, optional: true, timeout: TimeSpan.FromSeconds(60)))
                continue;
            kills++;
            await Task.Delay(3000);           // the coin is picked up (autoloot) and split
            var info = await InfoAsync(player, ctx.Options.NetTimeout);
            if (info.Bank > before.Bank)
                break;
        }
        ctx.Measure("monsters killed", kills);
        if (player.IsDead)
        {
            await Party.GmTargetsAsync(gm, player);
            gm.Command("/player rez");
            await Task.Delay(1000);
        }
        await ctx.Step("/gc info after: the dues are in the bank", async () =>
        {
            var after = await InfoAsync(player, ctx.Options.NetTimeout);
            long earned = player.Money - purse;
            long picked = player.ChatSnapshot().Where(l => l.At >= huntStart && l.Text.StartsWith("You pick up ", StringComparison.Ordinal))
                .Sum(l => ParseMoney(l.Text));
            ctx.Measure("coin picked up", picked);
            ctx.Measure("coin the player kept", earned);
            ctx.Measure("dues paid into the guild bank", after.Bank - before.Bank);
            ctx.Measure("merit after the hunt", after.Merit - before.Merit);
            var lines = player.ChatSnapshot().Where(l => l.Text.Contains("merit points for your guild") && l.Text.Contains(bot.Name)).Select(l => l.Text).ToList();
            ctx.Measure("merit lines naming the bot", lines.Count > 0 ? string.Join(" / ", lines) : "none (it gained no level)");
            ScenarioContext.Check(after.Bank > before.Bank, $"the bank stayed at {after.Bank} copper after {kills} kill(s) (coin kept: {earned})");
            return $"bank {before.Bank} -> {after.Bank} copper ({before.Dues}% of {picked} picked up is {picked * before.Dues / 100}), " +
                $"merit {before.Merit} -> {after.Merit}, the player kept {earned} copper";
        });
    }
}

/// <summary>A player and a gamebot as a group, with the GM's help for levels and travel.</summary>
internal static class Party
{
    /// <summary>
    /// The GM targets the player for a GM command, going to it first when it is far (a GM command works on the target,
    /// and an out-of-range target falls back to the GM itself: "You gave &lt;GM&gt; gold", first runs here).
    /// </summary>
    public static async Task GmTargetsAsync(ScriptedPlayer gm, ScriptedPlayer player)
    {
        double off = Math.Sqrt(Math.Pow(gm.X - player.X, 2) + Math.Pow(gm.Y - player.Y, 2));
        if (gm.Region != player.Region || off > 1000)
        {
            int mark = gm.Mark();
            gm.Command($"/jump to {player.CharacterName}");
            await gm.WaitForOrNullAsync<Protocol.ServerEvent>(e => e is Protocol.Jumped or Protocol.PositionAndObjectId, TimeSpan.FromSeconds(10), mark);
            await Task.Delay(500);
        }
        var seen = await gm.SeeAsync(player.CharacterName, TimeSpan.FromSeconds(10), 0);
        if (seen == null)
            throw new PlaytestException($"the GM does not see {player.CharacterName}");
        gm.Target(player.CharacterName);
        await Task.Delay(200);
    }

    /// <summary>The GM targets the player and sets its level (/player level).</summary>
    public static async Task SetLevelAsync(ScenarioContext ctx, ScriptedPlayer gm, ScriptedPlayer player, int level)
    {
        await ctx.Step($"the GM sets {player.CharacterName} to level {level} (/player level)", async () =>
        {
            await GmTargetsAsync(gm, player);
            int mark = player.Mark();
            gm.Command($"/player level {level}");
            var line = await player.WaitForChatAsync(l => l.Text.Contains($"level to {level}"), TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(line != null, "the level change did not arrive");
            return line!.Text;
        });
    }

    /// <summary>
    /// The player as a player of that level is: the GM makes it an Armsman (/player class: the Fighter would otherwise
    /// stop at level 5 until it joins its class) and sets its level; then the GM brings it to an Armsman trainer, where
    /// it opens the trainer window (/train) and spends its points: the main weapon first, then shield and parry, in the
    /// shares a level-10 tank has (Slash 9, Shields 7, Parry 5), as make_test_world.py trains its helper.
    /// </summary>
    public static async Task MakeArmsmanAsync(ScenarioContext ctx, ScriptedPlayer gm, ScriptedPlayer player, int level)
    {
        await ctx.Step($"the GM makes {player.CharacterName} an Armsman (/player class 2)", async () =>
        {
            await GmTargetsAsync(gm, player);
            int mark = player.Mark();
            gm.Command("/player class 2");
            var line = await player.WaitForChatAsync(l => l.Text.Contains("Armsman", StringComparison.OrdinalIgnoreCase) ||
                l.Text.Contains("class", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(10), mark);
            return line?.Text ?? "(no line; checked by the trainer below)";
        });
        await SetLevelAsync(ctx, gm, player, level);
        var trainer = ctx.World.Trainers(player.Region, "ArmsmanTrainer").FirstOrDefault();
        ScenarioContext.Check(trainer.Name != null, $"no Armsman trainer in region {player.Region}");
        await ctx.Step($"train at {trainer.Name} (/train, then each line in the trainer window)", async () =>
        {
            int mark = player.Mark();
            gm.Command(Quests.JumpCommand(gm, player, trainer.X, trainer.Y, trainer.Z, player.Region));
            var seen = await player.SeeAsync(trainer.Name, TimeSpan.FromSeconds(15), mark);
            ScenarioContext.Check(seen != null, $"{trainer.Name} not in view");
            player.Target(trainer.Name);
            await Task.Delay(500);
            // /train opens the trainer window (TrainerWindow code 0), or, with the server's custom_train on (as in the
            // release world), answers with its syntax: then each line is trained by typing /train <line> <level>.
            mark = player.Mark();
            player.Command("/train");
            var answer = await player.WaitForOrNullAsync<object>(e => e is Protocol.TrainerSpecs ||
                e is ChatLine l && l.Text.StartsWith("/train <line>", StringComparison.Ordinal), TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(answer != null, "no trainer window and no /train answer");
            var window = answer as Protocol.TrainerSpecs;
            await Task.Delay(1700);   // /train's spam guard: a second /train within 1.5 s is ignored
            var trained = new List<string>();
            int left = window?.Points ?? int.MaxValue;
            foreach (var (line, share) in new[] { ("Slash", 0.9), ("Shields", 0.7), ("Parry", 0.5) })
            {
                var spec = window?.Specs.FirstOrDefault(sp => sp.Name.Equals(line, StringComparison.OrdinalIgnoreCase)) ?? (-1, 1, line);
                if (spec.Name == null)
                    continue;
                int want = Math.Max(spec.Level, (int)Math.Round(level * share));
                while (window != null && want > spec.Level && Enumerable.Range(spec.Level + 1, want - spec.Level).Sum() > left)
                    want--;
                if (want <= spec.Level)
                    continue;
                if (window != null)
                {
                    left -= Enumerable.Range(spec.Level + 1, want - spec.Level).Sum();
                    player.TrainSpec(spec.Index, want);
                }
                else
                    player.Command($"/train {line} {want}");
                trained.Add($"{line} {want}");
                await Task.Delay(1700);   // the command's spam guard
            }
            int points = window?.Points ?? -1;
            var spent = player.ChatSnapshot().Where(l => l.At >= player.Now - 10 && l.Text.StartsWith("You spend", StringComparison.Ordinal)).Select(l => l.Text).ToList();
            ctx.Measure("player's training", string.Join(", ", trained) + (points >= 0 ? $" ({points} points)" : " (typed /train)"));
            ScenarioContext.Check(spent.Count > 0, "the trainer took no points: " +
                string.Join(" / ", player.ChatSnapshot().Where(l => l.At >= player.Now - 10 && l.Type == ChatTypes.System).Select(l => l.Text).TakeLast(3)));
            return string.Join(" / ", spent);
        });
    }

    /// <summary>
    /// The player buys a weapon of its level as a player would: the GM gives it the coin (/player money), brings it to a
    /// merchant of the region who sells a one-handed slashing weapon, it buys it and wields it in its right hand.
    /// </summary>
    public static async Task BuyWeaponAsync(ScenarioContext ctx, ScriptedPlayer gm, ScriptedPlayer player, int level)
    {
        var sale = ctx.World.WeaponOnSale(player.Region, 3, Math.Max(1, level - 4), level);
        if (sale == null)
        {
            ctx.Measure("weapon", "none on sale for the level: the practice sword");
            return;
        }
        var w = sale.Value;
        await ctx.Step($"buy a {w.Item} (level {w.Level}) from {w.Merchant} and wield it", async () =>
        {
            await GmTargetsAsync(gm, player);
            int mark = player.Mark();
            gm.Command($"/player money gold {Math.Max(1, w.Price / 10000 + 1)}");
            var paid = await player.WaitForChatAsync(l => l.Text.Contains("has given you some gold"), TimeSpan.FromSeconds(5), mark);
            ScenarioContext.Check(paid != null, "the coin did not reach the player");
            mark = player.Mark();
            gm.Command(Quests.JumpCommand(gm, player, w.X, w.Y, w.Z, player.Region));
            var merchant = await player.SeeAsync(w.Merchant, TimeSpan.FromSeconds(15), mark);
            ScenarioContext.Check(merchant != null, $"{w.Merchant} not in view");
            player.Target(w.Merchant);
            player.Interact(merchant!.ObjectId);
            await Task.Delay(800);
            mark = player.Mark();
            player.Buy(w.Page, w.Slot);
            var got = await player.WaitForOrNullAsync<Protocol.InventoryUpdate>(u => u.Items.Any(i => i.Name.Equals(w.Item, StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(got != null, $"no {w.Item} in the backpack after buying: " +
                string.Join(" / ", player.ChatSnapshot().Where(l => l.At >= player.Now - 10).Select(l => l.Text).TakeLast(2)));
            int slot = got!.Items.First(i => i.Name.Equals(w.Item, StringComparison.OrdinalIgnoreCase)).Slot;
            mark = player.Mark();
            player.MoveItem(slot, 10);
            var wield = await player.WaitForChatAsync(l => l.Text.StartsWith("You wield", StringComparison.Ordinal) && l.Text.Contains(w.Item, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(wield != null, $"the {w.Item} was not wielded");
            ctx.Measure("weapon", $"{w.Item} (level {w.Level}, {w.Price} copper) from {w.Merchant}");
            return wield!.Text;
        });
    }

    /// <summary>The GM jumps to the gamebot and brings the player (/jump name to me); the player invites it into the group.</summary>
    public static async Task<SeenLiving> MeetAndGroupAsync(ScenarioContext ctx, ScriptedPlayer gm, ScriptedPlayer player, WorldBot bot)
    {
        SeenLiving? botSeen = null;
        await ctx.Step($"meet {bot.Name} (level {bot.Level}): the GM jumps to it, then brings {player.CharacterName}", async () =>
        {
            int mark = gm.Mark();
            gm.Command($"/jump to {bot.Name}");
            var gmSees = await gm.SeeAsync(bot.Name, TimeSpan.FromSeconds(15), mark);
            ScenarioContext.Check(gmSees != null, $"{bot.Name} not in view after /jump to {bot.Name}");
            mark = player.Mark();
            gm.Command($"/jump {player.CharacterName} to me");
            botSeen = await player.SeeAsync(bot.Name, TimeSpan.FromSeconds(15), mark);
            ScenarioContext.Check(botSeen != null, $"{player.CharacterName} does not see {bot.Name}");
            return $"{bot.Name} is object {botSeen!.ObjectId}, at {botSeen.X},{botSeen.Y}";
        });
        await ctx.Step($"{player.CharacterName} invites {bot.Name}: it joins", async () =>
        {
            int mark = player.Mark();
            player.Invite(bot.Name);
            var group = await player.WaitForOrNullAsync<Protocol.GroupWindow>(g => g.Members.Any(m => m.Name == bot.Name), TimeSpan.FromSeconds(60), mark);
            ScenarioContext.Check(group != null, $"{bot.Name} did not join within 60 s");
            return string.Join(", ", group!.Members.Select(m => $"{m.Name} ({m.ClassName} {m.Level})"));
        });
        return botSeen!;
    }

    /// <summary>
    /// The GM jumps 500 units short of a place, then moves the whole group to itself (/player jump group: every member,
    /// the gamebot too, Player.cs). A long walk would test the bot's pathing instead, and in a world without navmeshes
    /// (this container) a gamebot does not move at all (first run: it stayed 26,000 units behind). Returns the target.
    /// </summary>
    public static async Task<SeenLiving?> GoToAsync(ScenarioContext ctx, ScriptedPlayer gm, ScriptedPlayer player, SeenLiving bot,
        string targetName, int x, int y, int z, bool optional = false)
    {
        SeenLiving? target = null;
        double distance = Math.Sqrt(Math.Pow(x - player.X, 2) + Math.Pow(y - player.Y, 2));
        Func<string, Func<Task<string>>, Task> step = optional ? (n, b) => ctx.TryStep(n, b) : ctx.Step;
        await step($"the group goes to {targetName} ({distance:0} units off): the GM jumps there, then /player jump group", async () =>
        {
            double angle = Math.Atan2(y - player.Y, x - player.X);
            int gx = (int)(x - Math.Cos(angle) * 500), gy = (int)(y - Math.Sin(angle) * 500);
            gm.Command($"/jump to {gx} {gy} {z} {player.Region}");
            await Task.Delay(1500);
            int mark = player.Mark();
            gm.Command($"/player jump group {player.CharacterName}");
            var moved = await player.WaitForOrNullAsync<Protocol.ServerEvent>(e => e is Protocol.Jumped or Protocol.PositionAndObjectId, TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(moved != null, "the player was not moved");
            await Task.Delay(2000);
            target = await player.SeeAsync(targetName, TimeSpan.FromSeconds(20), mark);
            ScenarioContext.Check(target != null, $"{targetName} not in view at its camp");
            // the bot came along when an update or a create places it here after the jump (an old entry is not proof)
            double jumped = player.Now - 4;
            SeenLiving? b = null;
            for (int i = 0; i < 20 && (b == null || b.UpdatedAt < jumped); i++)
            {
                b = player.SeenById(bot.ObjectId);
                if (b == null || b.UpdatedAt < jumped)
                    await Task.Delay(500);
            }
            bool here = b != null && b.UpdatedAt >= jumped;
            double off = here ? Math.Sqrt(Math.Pow(b!.X - player.X, 2) + Math.Pow(b.Y - player.Y, 2)) : -1;
            ctx.Measure($"{bot.Name} at {targetName}", here ? $"{off:0} units from the player" : "no fresh update from it after the jump");
            return $"{targetName} in view (object {target!.ObjectId}); {bot.Name} " + (here ? $"{off:0} units away" : "not confirmed here");
        });
        return target;
    }

    /// <summary>The player fights the target with the gamebot in its group; counts the bot's hits among the combat lines.</summary>
    public static async Task<bool> FightAsync(ScenarioContext ctx, ScriptedPlayer player, SeenLiving bot, SeenLiving target, ScriptedPlayer? gm = null,
        bool optional = false, TimeSpan? timeout = null)
    {
        bool won = false;
        Func<string, Func<Task<string>>, Task> step = optional ? (n, b) => ctx.TryStep(n, b) : ctx.Step;
        await step($"fight {target.Name} with {bot.Name}", async () =>
        {
            // Full health first, as a player rests before a pull (the GM's /heal on the player).
            if (gm != null)
            {
                await GmTargetsAsync(gm, player);
                if (player.IsDead)
                {
                    gm.Command("/player rez");   // after a lost fight: back on its feet where the GM stands
                    await Task.Delay(1000);
                }
                gm.Command("/heal");
                await Task.Delay(500);
            }
            double start = player.Now;
            try
            {
                string end = await player.FightAsync(target.ObjectId, timeout ?? TimeSpan.FromMinutes(3));
                won = true;
                return end;
            }
            finally
            {
                await Task.Delay(1500);
                var lines = player.ChatSnapshot().Where(l => l.At >= start && (l.Type is 0x11 or 0x12 or 0x17 or 0x1d || l.Text.Contains(target.Name)))
                    .Select(l => l.Text).ToList();
                int botLines = lines.Count(l => l.StartsWith(bot.Name + " ", StringComparison.Ordinal));
                bool botEngaged = player.HasTargeted(bot.ObjectId, target.ObjectId);
                ctx.Measure($"fight with {target.Name}", $"{player.Now - start:0} s, {lines.Count} combat lines, {botLines} of them {bot.Name}'s; " +
                    $"{bot.Name} {(botEngaged ? "targeted it" : "was never seen targeting it")}");
            }
        });
        return won;
    }
}

internal static class Quests
{
    /// <summary>
    /// Does a data quest's deliver and interact steps in order (DataQuest.eStepType 2-5): /jump to the step's target by
    /// name, give it the step's item from the backpack (Deliver) or interact with it (Interact), and wait for the quest to
    /// move on. A kill, whisper, search or collect step is left as a TODO.
    /// </summary>
    public static async Task RunStepsAsync(ScenarioContext ctx, ScriptedPlayer gm, ScriptedPlayer jumper, QuestCandidate quest, int fromStep = 0)
    {
        var steps = ctx.World.QuestSteps(quest.Id);
        long xpBefore = gm.Experience;
        foreach (var step in steps.Skip(fromStep))
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
                jumper.Command(JumpCommand(jumper, gm, at.X, at.Y, at.Z, at.Region));
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

    /// <summary>The GM's jump: itself to a place, or (another player) "/jump name to x y z region".</summary>
    public static string JumpCommand(ScriptedPlayer jumper, ScriptedPlayer who, int x, int y, int z, int region) =>
        ReferenceEquals(jumper, who) ? $"/jump to {x} {y} {z} {region}" : $"/jump {who.CharacterName} to {x} {y} {z} {region}";

    /// <summary>The GM (jumper) puts the player at the quest's giver; the player interacts and, when the giver only talks,
    /// whispers the AcceptText. An offer window (Dialog 0x22) is accepted (QuestSubscribe 0x64).</summary>
    public static async Task TakeQuestAsync(ScenarioContext ctx, ScriptedPlayer gm, ScriptedPlayer jumper, QuestCandidate quest)
    {
        SeenLiving? giver = null;
        await ctx.Step($"{gm.CharacterName}: to {quest.Giver} (/jump), who is in view", async () =>
        {
            int mark = gm.Mark();
            jumper.Command(JumpCommand(jumper, gm, quest.GiverX, quest.GiverY, quest.GiverZ, quest.Region));
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
    }
}
