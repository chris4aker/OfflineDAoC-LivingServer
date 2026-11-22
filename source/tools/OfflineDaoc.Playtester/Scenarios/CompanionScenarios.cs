// 1.2.1's companion and reward fixes, at the packet level on a minimal world (no gamebots, no game files):
// - #424 PlayerTargetHandler / PLAYER_CLICK_TARGET: a click (the PlayerTarget packet the client sends) on a living bot,
//   standing and following, is taken by the server.
// - #397 HireCommand165 / CompanionWindows165 / HiredCompanions165: /hire train reads line names; a hired companion joins
//   at its player's level, trained, and gets plain merchant gear above level 5 (HIRED_COMPANION_TRAINED); gear that can't
//   be found is tried again at the next summon, where the training weapon counts as empty and is swapped
//   (HIRED_COMPANION_GEAR_RETRIED).
// - #396 AutonomousBotRealmPointRewards: an enemy record-backed bot (IsEligibleVictim: a GameBot built from a world bot
//   record, not a /spawn helper) pays its killer a player kill's experience and bounty points, and nothing again within
//   the repeat-kill window.
// The minimal world has no gamebots, so the bot of #396 is a hired companion: the same GameBot type, flag
// (IsAutonomousWorldBot) and death path (GameNPC.Die, AbstractServerRules.OnNpcKilled, AutonomousBotRealmPointRewards.Award)
// as a gamebot in Thidranki. What these cannot see: what game.dll draws and sends (the click packet is the one the
// playtester's own client sends, with and without the examine bit), and a gamebot of the real roster.
using System.Text.RegularExpressions;
using OfflineDaoc.Playtester.Protocol;

namespace OfflineDaoc.Playtester.Scenarios;

internal static class Companions
{
    private static readonly Regex MobCreated = new(@"^Mob created: OID=(\d+)", RegexOptions.Compiled);
    private static readonly Regex MerchantCreated = new(@"^Merchant created: OID=(\d+)", RegexOptions.Compiled);
    private static readonly Regex NowLevel = new(@"^(?<line>.+) is now (?<level>\d+)\.$", RegexOptions.Compiled);

    /// <summary>The GM puts up a healer of no realm where it stands (every character starts at the one startup spot of
    /// the minimal world, so the players are beside it): /hire opens there.</summary>
    public static async Task HealerAsync(ScenarioContext ctx, ScriptedPlayer gm)
    {
        await ctx.Step("the GM puts up a healer here (/mob create DOL.GS.GameHealer 0): /hire works beside one", async () =>
        {
            int mark = gm.Mark();
            gm.Command("/mob create DOL.GS.GameHealer 0");
            var line = await gm.WaitForChatAsync(l => MobCreated.IsMatch(l.Text) || l.Text.StartsWith("There was an error creating", StringComparison.Ordinal),
                ctx.Options.NetTimeout, mark) ?? throw new PlaytestException("no answer to /mob create");
            ScenarioContext.Check(MobCreated.IsMatch(line.Text), line.Text);
            return line.Text;
        });
    }

    /// <summary>The GM puts up an Albion merchant here with the test world's list of plain gear (make_test_world.py
    /// minimal --companions): /merchant create, then /merchant sell add on it. Returns its object id.</summary>
    public static async Task<int> MerchantAsync(ScenarioContext ctx, ScriptedPlayer gm, string itemList)
    {
        int id = 0;
        await ctx.Step($"the GM puts up an Albion merchant selling {itemList} (/merchant create, /merchant sell add)", async () =>
        {
            int mark = gm.Mark();
            gm.Command("/merchant create");
            var made = await gm.WaitForChatAsync(l => MerchantCreated.IsMatch(l.Text), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("no answer to /merchant create: " + DeadBots.SystemSince(gm, 0));
            id = int.Parse(MerchantCreated.Match(made.Text).Groups[1].Value);
            await gm.SeeAsync("New Merchant", TimeSpan.FromSeconds(5), mark);
            gm.TargetObject(id);
            await Task.Delay(400);
            mark = gm.Mark();
            gm.Command("/merchant sell add " + itemList);
            var loaded = await gm.WaitForChatAsync(l => l.Text.StartsWith("Merchant articles list loaded", StringComparison.Ordinal) ||
                l.Text.Contains("merchant", StringComparison.OrdinalIgnoreCase), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("no answer to /merchant sell add");
            ScenarioContext.Check(loaded.Text.StartsWith("Merchant articles list loaded", StringComparison.Ordinal), loaded.Text);
            gm.TargetObject(0);
            return $"{made.Text}; {loaded.Text}";
        });
        return id;
    }

    /// <summary>The GM makes the player a class (/player class) and a level (/player level).</summary>
    public static async Task ClassAndLevelAsync(ScenarioContext ctx, ScriptedPlayer gm, ScriptedPlayer player, int classId, string className, int level)
    {
        if (classId > 0)
            await ctx.Step($"the GM makes {player.CharacterName} an {className} (/player class {classId})", async () =>
            {
                await Party.GmTargetsAsync(gm, player);
                int mark = player.Mark();
                gm.Command($"/player class {classId}");
                var line = await player.WaitForChatAsync(l => l.Text.Contains(className, StringComparison.OrdinalIgnoreCase) ||
                    l.Text.Contains("class", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(10), mark);
                return line?.Text ?? "(no line)";
            });
        await Party.SetLevelAsync(ctx, gm, player, level);
        gm.TargetObject(0);
    }

    /// <summary>
    /// The player hires a companion beside the healer as the chat window shows it: /hire, /hire class+ until the class
    /// (null: the first the design offers), /hire name, /hire confirm. Returns the companion as the player's client got
    /// it created, and its name.
    /// </summary>
    public static async Task<SeenLiving> HireAsync(ScenarioContext ctx, ScriptedPlayer player, string? className)
    {
        await ctx.Step($"{player.Label}: /hire beside the healer opens the hire window", async () =>
        {
            int mark = player.Mark();
            player.Command("/hire");
            var line = await player.WaitForChatAsync(l => l.Text.StartsWith("Click the links in the healer's window", StringComparison.Ordinal) ||
                l.Text.StartsWith("Stand beside a healer", StringComparison.Ordinal) || l.Text.StartsWith("Hired companions are switched off", StringComparison.Ordinal) ||
                l.Text.StartsWith("Not while you are fighting", StringComparison.Ordinal), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("no answer to /hire: " + DeadBots.SystemSince(player, 0));
            ScenarioContext.Check(line.Text.StartsWith("Click the links", StringComparison.Ordinal), line.Text);
            return line.Text;
        });
        if (className != null)
            await ctx.Step($"/hire class+ until the design is an {className}", async () =>
            {
                var seen = new List<string>();
                for (int i = 0; i < 20; i++)
                {
                    int mark = player.Mark();
                    player.Command("/hire class+");
                    var line = await player.WaitForChatAsync(l => l.Text.StartsWith("Companion to hire: ", StringComparison.Ordinal), ctx.Options.NetTimeout, mark)
                        ?? throw new PlaytestException("no design line after /hire class+");
                    if (line.Text.Contains($" {className}, ", StringComparison.Ordinal))
                        return line.Text;
                    seen.Add(line.Text);
                }
                throw new PlaytestException($"no {className} in 20 /hire class+: " + string.Join(" / ", seen.Take(4)));
            });
        string name = Names.Fresh("Ptc");
        await ctx.Step($"/hire name {name}", async () =>
        {
            int mark = player.Mark();
            player.Command("/hire name " + name);
            var line = await player.WaitForChatAsync(l => l.Text.StartsWith("Companion to hire: ", StringComparison.Ordinal), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("no design line after /hire name");
            ScenarioContext.Check(line.Text.StartsWith($"Companion to hire: {name},", StringComparison.Ordinal), line.Text + " " + DeadBots.SystemSince(player, 0));
            return line.Text;
        });
        SeenLiving companion = null!;
        await ctx.Step($"/hire confirm: {name} is hired and joins (the roster says \"with you\")", async () =>
        {
            int mark = player.Mark();
            player.Command("/hire confirm");
            var roster = await player.WaitForChatAsync(l => l.Text.Contains($". {name}, ", StringComparison.Ordinal), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("no roster line naming the companion: " + DeadBots.SystemSince(player, player.Now - 5));
            ScenarioContext.Check(roster.Text.EndsWith(": with you", StringComparison.Ordinal), roster.Text);
            companion = await player.SeeAsync(name, ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException($"{name} was hired but never created for {player.CharacterName}");
            return $"{roster.Text} (object {companion.ObjectId}, level {companion.Level} in its create packet)";
        });
        await Task.Delay(1000);
        return companion;
    }

    /// <summary>The newest server log line with the tag that names the companion (waits up to 10 s); the part from the tag.</summary>
    public static async Task<string> LogLineAsync(ScenarioContext ctx, string tag, string name)
    {
        var lines = await DeadBots.ServerLinesAsync(ctx, $"{tag} owner=");
        var mine = lines.Where(l => l.Contains($"\"{name}\"", StringComparison.Ordinal)).ToList();
        if (mine.Count == 0)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (mine.Count == 0 && watch.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(500);
                mine = (ctx.Server?.ConsoleLinesWith($"{tag} owner=") ?? new List<string>())
                    .Where(l => l.Contains($"\"{name}\"", StringComparison.Ordinal)).ToList();
            }
        }
        ScenarioContext.Check(mine.Count > 0, $"no {tag} line for {name} in the server log");
        string line = mine[^1];
        int at = line.IndexOf(tag, StringComparison.Ordinal);
        return at >= 0 ? line[at..] : line;
    }

    /// <summary>A key="value" (or key=value) field of a log line.</summary>
    public static string Field(string line, string key)
    {
        var quoted = Regex.Match(line, $@"\b{Regex.Escape(key)}=""(?<v>[^""]*)""");
        if (quoted.Success)
            return quoted.Groups["v"].Value;
        var plain = Regex.Match(line, $@"\b{Regex.Escape(key)}=(?<v>\S+)");
        return plain.Success ? plain.Groups["v"].Value : "";
    }

    /// <summary>"/hire train 1 words": the answer it gave (the "X is now N." line, the problem, or the list of lines).</summary>
    public static async Task<ChatLine> TrainAsync(ScenarioContext ctx, ScriptedPlayer player, string words, string companion)
    {
        int mark = player.Mark();
        player.Command("/hire train 1 " + words);
        return await player.WaitForChatAsync(l => NowLevel.IsMatch(l.Text) || l.Text.StartsWith(companion + "'s spec lines: ", StringComparison.Ordinal) ||
            l.Text.Contains(" needs ", StringComparison.Ordinal) || l.Text.Contains(" can't go above ", StringComparison.Ordinal) ||
            l.Text.Contains(" is already ", StringComparison.Ordinal) || l.Text.StartsWith(companion + " has no spec lines", StringComparison.Ordinal) ||
            l.Text.StartsWith("Summon that companion first", StringComparison.Ordinal) || l.Text.StartsWith("Choose a spec line", StringComparison.Ordinal),
            ctx.Options.NetTimeout, mark) ?? throw new PlaytestException($"no answer to /hire train 1 {words}");
    }

    /// <summary>The line and level of an "X is now N." answer, or null.</summary>
    public static (string Line, int Level)? NowAt(ChatLine line)
    {
        var m = NowLevel.Match(line.Text);
        return m.Success ? (m.Groups["line"].Value, int.Parse(m.Groups["level"].Value)) : null;
    }

    /// <summary>A click on a living (#424) and the server's answer to the examine bit: "You target [Name]."</summary>
    public static async Task<ChatLine?> ClickAsync(ScriptedPlayer player, SeenLiving bot, bool examine, TimeSpan timeout)
    {
        int mark = player.Mark();
        player.Click(bot.ObjectId, examine);
        return examine
            ? await player.WaitForChatAsync(l => l.Text.StartsWith($"You target [{bot.Name}]", StringComparison.Ordinal), timeout, mark)
            : null;
    }

    /// <summary>The server's PLAYER_CLICK_TARGET line for this click (player, object, kind and examine bit), waited for.</summary>
    public static async Task<string> ClickLineAsync(ScenarioContext ctx, ScriptedPlayer player, SeenLiving bot, string kind, bool examine, int atLeast = 1)
    {
        if (ctx.Server == null)
            return "(attached: the server log was not read)";
        string head = $"PLAYER_CLICK_TARGET player=\"{player.CharacterName}\" id={bot.ObjectId} kind={kind} name=\"{bot.Name}\"";
        string tail = $"examine={examine}";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        List<string> lines;
        while (true)
        {
            lines = ctx.Server.ConsoleLinesWith(head).Where(l => l.Contains(tail, StringComparison.Ordinal)).ToList();
            if (lines.Count >= atLeast || watch.Elapsed > TimeSpan.FromSeconds(10))
                break;
            await Task.Delay(500);
        }
        if (lines.Count < atLeast)
        {
            var any = ctx.Server.ConsoleLinesWith("PLAYER_CLICK_TARGET").TakeLast(4).Select(l => l.Length > 240 ? l[..240] : l);
            throw new PlaytestException($"{lines.Count} server line(s) \"{head} ... {tail}\" (wanted {atLeast}); the last click lines: " +
                string.Join(" || ", any));
        }
        string line = lines[^1];
        ScenarioContext.Check(line.Contains(" result=set alive=True ", StringComparison.Ordinal), "the server did not take the click: " + line);
        return line[line.IndexOf("PLAYER_CLICK_TARGET", StringComparison.Ordinal)..];
    }
}

/// <summary>#424: a click on a living bot, standing and following, in the original client's protocol.</summary>
internal sealed class BotClickTarget : Scenario
{
    public override string Id => "bot-click-target";
    public override string Title => "A click selects a living bot (standing and moving)";
    public override string Checks => "A player logs in as the original client (handshake build 0x2A07) and spawns a companion helper (/spawn armsman). " +
        "It clicks the standing helper (PlayerTarget with the in-view bits and the examine bit): the server must answer \"You target " +
        "[Name].\" and log PLAYER_CLICK_TARGET ... kind=companion result=set alive=True examine=True. A click without the examine bit " +
        "must be logged result=set too. Then the player walks off, the helper follows, and a click on it while it moves must be taken " +
        "the same way. The click packet is the playtester's own (the new client's PlayerTarget), not game.dll's: what game.dll sends " +
        "for a mouse click is the owner's check.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        var player = await ctx.LoginAsync("player", "playtester", ClientPackets.OriginalClientBuild);
        await ctx.PlayAsync(player, "Ptclick");
        var helper = await DeadBots.SpawnHelperAsync(ctx, player);
        await ctx.Step($"a click on {helper.Name} standing (examine bit): \"You target [{helper.Name}].\"", async () =>
        {
            var said = await Companions.ClickAsync(player, helper, true, ctx.Options.NetTimeout)
                ?? throw new PlaytestException($"no \"You target [{helper.Name}].\" after the click: " + DeadBots.SystemSince(player, player.Now - 5));
            return said.Text;
        });
        await ctx.Step("the server log: the click was taken (result=set, alive, examine)", async () =>
            await Companions.ClickLineAsync(ctx, player, helper, "companion", examine: true));
        await ctx.Step("a click without the examine bit is taken too", async () =>
        {
            await Companions.ClickAsync(player, helper, false, TimeSpan.Zero);
            return await Companions.ClickLineAsync(ctx, player, helper, "companion", examine: false);
        });
        await ctx.Step($"a click on {helper.Name} while it follows the player", async () =>
        {
            player.TargetObject(0);
            var before = player.SeenById(helper.ObjectId) ?? helper;
            var walk = player.MoveToAsync(player.X + 700, player.Y + 200, player.Z);
            await Task.Delay(2500);
            var atClick = player.SeenById(helper.ObjectId) ?? before;
            var said = await Companions.ClickAsync(player, atClick, true, ctx.Options.NetTimeout);
            await walk;
            await Task.Delay(1500);
            var after = player.SeenById(helper.ObjectId) ?? atClick;
            double moved = Math.Sqrt(Math.Pow(after.X - before.X, 2) + Math.Pow(after.Y - before.Y, 2));
            double movedBeforeClick = Math.Sqrt(Math.Pow(atClick.X - before.X, 2) + Math.Pow(atClick.Y - before.Y, 2));
            ctx.Measure("helper moved during the walk (units)", Math.Round(moved));
            ctx.Measure("helper moved before the click (units)", Math.Round(movedBeforeClick));
            ScenarioContext.Check(said != null, $"no \"You target [{helper.Name}].\" for the click while it followed");
            string line = await Companions.ClickLineAsync(ctx, player, helper, "companion", examine: true, atLeast: 2);
            if (moved < 100)
                ctx.Flag($"{helper.Name} moved only {moved:0} units during the walk: the click was on a bot that hardly moved");
            return $"{said!.Text} The helper moved {movedBeforeClick:0} units before the click and {moved:0} in all. {line}";
        });
    }
}

/// <summary>#397 and the hired companions' level and gear: hire at level 12, train by line name, gear retried and the
/// training weapon swapped once a merchant is up.</summary>
internal sealed class HireTrain : Scenario
{
    public override string Id => "hire-train";
    public override string Title => "A hired companion joins at its player's level, trains by line name and gets its gear at the next summon";
    public override string Checks => "Needs a world from make_test_world.py minimal --companions (an Armsman's lines and abilities, a list of plain " +
        "level-10 gear, no merchant in the world). The GM puts up a healer; a player (original client) is made a level 12 Armsman and " +
        "hires an Armsman (/hire, class+, name, confirm). The companion must be created at level 12 and HIRED_COMPANION_TRAINED must " +
        "say level=12->12 with points spent and, with no Albion merchant up, gear=\"none (no merchant of its realm found)\" " +
        "gear_retry=next-summon. A click on it must be logged kind=gamebot result=set (#424). #397: after /hire respec 1, " +
        "/hire train 1 pole, /hire train 1 two handed 5 and /hire train 1 3 (Parry, by number) must answer \"Polearm is now 2.\", " +
        "\"Two Handed is now 5.\" and \"Parry is now 2.\", /hire train 1 slash raise Slash, and /hire train 1 smite and /hire train 1 cr " +
        "(two lines start so) answer with the companion's lines and the words to type. Then the GM puts up a merchant with the list; " +
        "/hire dismiss 1 and /hire summon 1 must log HIRED_COMPANION_GEAR_RETRIED with merchant gear and the training weapon replaced " +
        "(training_weapon_replaced not none) and gear_retry=no. Packets and log lines only.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        string? itemList = ctx.World.Marker.GetValueOrDefault("companions");
        if (string.IsNullOrEmpty(itemList))
            throw new SkipScenario("needs a world from make_test_world.py minimal --companions (spec lines and a merchant list)");
        var gm = await DeadBots.GmAsync(ctx, "Pthgm");
        var player = await ctx.LoginAsync("player", "playtester", ClientPackets.OriginalClientBuild);
        await ctx.PlayAsync(player, "Pthire");
        await Companions.HealerAsync(ctx, gm);
        await Companions.ClassAndLevelAsync(ctx, gm, player, 2, "Armsman", 12);
        var companion = await Companions.HireAsync(ctx, player, "Armsman");
        string name = companion.Name;

        string trained = "";
        await ctx.Step($"{name} joined at level 12, trained (HIRED_COMPANION_TRAINED)", async () =>
        {
            trained = await Companions.LogLineAsync(ctx, "HIRED_COMPANION_TRAINED", name);
            ScenarioContext.Check(Companions.Field(trained, "level") == "12->12", "not trained at 12: " + trained);
            ScenarioContext.Check(Companions.Field(trained, "player_level") == "12", trained);
            ScenarioContext.Check(int.TryParse(Companions.Field(trained, "spent"), out int spent) && spent > 0, "no points spent: " + trained);
            ScenarioContext.Check(companion.Level == 12, $"created at level {companion.Level}");
            ctx.Measure("first arrival", trained);
            return trained;
        });
        string firstGear = Companions.Field(trained, "gear");
        bool gearAtFirst = firstGear.Length > 0 && !firstGear.StartsWith("none", StringComparison.Ordinal);
        if (!gearAtFirst)
            await ctx.Step("no Albion merchant is up yet: no gear, tried again at the next summon", () =>
            {
                ScenarioContext.Check(Companions.Field(trained, "gear") == "none (no merchant of its realm found)", trained);
                ScenarioContext.Check(Companions.Field(trained, "gear_retry") == "next-summon", trained);
                return Task.FromResult($"gear=\"{Companions.Field(trained, "gear")}\" gear_retry={Companions.Field(trained, "gear_retry")}");
            });
        else
            ctx.Todo($"a merchant of the realm was already up (gear=\"{firstGear}\"): the retry at the next summon is not tested on this world");

        await ctx.Step($"a click on {name} (a record-backed bot: kind=gamebot) is taken (#424)", async () =>
        {
            var said = await Companions.ClickAsync(player, companion, true, ctx.Options.NetTimeout)
                ?? throw new PlaytestException($"no \"You target [{name}].\"");
            return said.Text + " " + await Companions.ClickLineAsync(ctx, player, companion, "gamebot", examine: true);
        });

        // #397: /hire train by line name, by its beginning, by number, with a level, and an unclear line.
        await ctx.Step("/hire respec 1 (free): every line back to 1, the points to spend", async () =>
        {
            player.Command("/hire respec 1");
            await Task.Delay(1500);
            return "respec sent";
        });
        async Task Expect(string words, string line, int level)
        {
            await ctx.Step($"/hire train 1 {words}: \"{line} is now {level}.\"", async () =>
            {
                var said = await Companions.TrainAsync(ctx, player, words, name);
                ScenarioContext.Check(said.Text == $"{line} is now {level}.", said.Text);
                return said.Text;
            });
        }
        await Expect("pole", "Polearm", 2);
        await Expect("two handed 5", "Two Handed", 5);
        await Expect("3", "Parry", 2);
        await ctx.Step("/hire train 1 slash (an autotrain line: it starts at a quarter of the level): Slash goes up", async () =>
        {
            var said = await Companions.TrainAsync(ctx, player, "slash", name);
            var now = Companions.NowAt(said);
            ScenarioContext.Check(now is { Line: "Slash" } && now.Value.Level >= 2, said.Text);
            return said.Text;
        });
        foreach (string unclear in new[] { "smite", "cr" })
            await ctx.Step($"/hire train 1 {unclear}: not one line, so the answer lists the lines and the words to type", async () =>
            {
                var said = await Companions.TrainAsync(ctx, player, unclear, name);
                ScenarioContext.Check(said.Text.StartsWith($"{name}'s spec lines: 1 ", StringComparison.Ordinal), said.Text);
                ScenarioContext.Check(said.Text.Contains("Two Handed", StringComparison.Ordinal) && said.Text.Contains("Type /hire train 1 <line name or number> [level]", StringComparison.Ordinal), said.Text);
                return said.Text;
            });

        if (gearAtFirst)
            return;
        await Companions.MerchantAsync(ctx, gm, itemList);
        await ctx.Step($"/hire dismiss 1: {name} goes to wait", async () =>
        {
            int mark = player.Mark();
            player.Command("/hire dismiss 1");
            var line = await player.WaitForChatAsync(l => l.Text.Contains($". {name}, ", StringComparison.Ordinal) && l.Text.Contains(": waiting", StringComparison.Ordinal),
                ctx.Options.NetTimeout, mark) ?? throw new PlaytestException("no roster line with the companion waiting: " + DeadBots.SystemSince(player, player.Now - 5));
            await Task.Delay(5000);                   // its save (inventory with the training weapon) before the summon
            return line.Text;
        });
        await ctx.Step($"/hire summon 1 beside the healer: {name} comes back", async () =>
        {
            int mark = player.Mark();
            player.Command("/hire summon 1");
            var line = await player.WaitForChatAsync(l => l.Text.Contains($". {name}, ", StringComparison.Ordinal) && l.Text.EndsWith(": with you", StringComparison.Ordinal),
                ctx.Options.NetTimeout, mark) ?? throw new PlaytestException("no roster line with the companion back: " + DeadBots.SystemSince(player, player.Now - 5));
            return line.Text;
        });
        await ctx.Step("HIRED_COMPANION_GEAR_RETRIED: merchant gear now, and the training weapon swapped for a merchant weapon", async () =>
        {
            string retried = await Companions.LogLineAsync(ctx, "HIRED_COMPANION_GEAR_RETRIED", name);
            ctx.Measure("second arrival", retried);
            string gear = Companions.Field(retried, "gear"), swapped = Companions.Field(retried, "training_weapon_replaced");
            ScenarioContext.Check(gear.Length > 0 && !gear.StartsWith("none", StringComparison.Ordinal), "no gear: " + retried);
            ScenarioContext.Check(gear.Contains("Playtester ", StringComparison.Ordinal), "not the merchant's gear: " + retried);
            ScenarioContext.Check(swapped.Length > 0 && swapped != "none" && !swapped.Contains("deleted", StringComparison.Ordinal),
                "the training weapon was not swapped: " + retried);
            ScenarioContext.Check(Companions.Field(retried, "gear_retry") == "no", retried);
            ScenarioContext.Check(Companions.Field(retried, "level") == "12->12", retried);
            return retried;
        });
    }
}

/// <summary>#396: a kill of an enemy record-backed bot pays experience and bounty points, once per repeat-kill window.</summary>
internal sealed class GamebotKillXp : Scenario
{
    public override string Id => "gamebot-kill-xp";
    public override string Title => "An enemy bot's kill pays experience and bounty points (and nothing again at once)";
    public override string Checks => "The minimal world has no gamebots, so the victim is a hired companion: a GameBot built from a world bot record, " +
        "which AutonomousBotRealmPointRewards.IsEligibleVictim takes as a gamebot (a /spawn helper is not). A player made level 5 hires " +
        "one beside a healer the GM puts up. The GM turns Midgard (/player realm 2) and kills it (/harm 100000: damage through " +
        "GameLiving.TakeDamage, then GameNPC.Die, AbstractServerRules.OnNpcKilled and AutonomousBotRealmPointRewards.Award). The GM " +
        "must get \"You get N experience points.\" with N above 0 and \"You get N bounty points!\" (Camelot Hills is no battleground), " +
        "and the server log RVR_BOT_KILLED with the GM as the Midgard killer. Revived at once (/hire revive 1), killed again: " +
        "\"has been killed recently and is worth no experience!\" and no experience. The killer is a GM's /harm, not a player's " +
        "weapon, and the victim a hired companion, not a gamebot of the roster: the reward path from the death on is the real one.";

    private static readonly Regex Bounty = new(@"^You get ([\d,]+) bounty points!", RegexOptions.Compiled);
    private static readonly Regex Realm = new(@"^You get ([\d,]+) realm points!", RegexOptions.Compiled);

    public override async Task RunAsync(ScenarioContext ctx)
    {
        var gm = await DeadBots.GmAsync(ctx, "Ptxgm");
        var owner = await ctx.LoginAsync("owner", "playtester");
        await ctx.PlayAsync(owner, "Ptxown");
        await Companions.HealerAsync(ctx, gm);
        await Companions.ClassAndLevelAsync(ctx, gm, owner, 0, "", 5);
        var hired = await Companions.HireAsync(ctx, owner, null);
        SeenLiving victim = null!;
        await ctx.Step($"the GM has {hired.Name} too", async () =>
        {
            victim = await DeadBots.SeenByAsync(ctx, gm, hired.Name);
            return $"object {victim.ObjectId}, level {victim.Level}";
        });
        await ctx.Step("the GM turns Midgard (/player realm 2, with the companion selected: the GM's own realm)", async () =>
        {
            gm.TargetObject(victim.ObjectId);
            await Task.Delay(400);
            int mark = gm.Mark();
            gm.Command("/player realm 2");
            var line = await gm.WaitForChatAsync(l => l.Text.StartsWith("You successfully changed ", StringComparison.Ordinal), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("no answer to /player realm 2: " + DeadBots.SystemSince(gm, gm.Now - 5));
            ScenarioContext.Check(line.Text.Contains(gm.CharacterName, StringComparison.Ordinal) && line.Text.Contains("Midgard", StringComparison.Ordinal), line.Text);
            return line.Text;
        });
        await Kill(ctx, gm, victim, worth: true);
        await DeadBots.LogStepAsync(ctx, "the server log: RVR_BOT_KILLED with the GM as the Midgard killer",
            $"RVR_BOT_KILLED victim={hired.Name} ");
        await ctx.Step("the RVR_BOT_KILLED line names the GM, of Midgard", async () =>
        {
            var lines = await DeadBots.ServerLinesAsync(ctx, $"RVR_BOT_KILLED victim={hired.Name} ");
            if (ctx.Server == null)
                return "(attached: not read)";
            string line = lines[^1][lines[^1].IndexOf("RVR_BOT_KILLED", StringComparison.Ordinal)..];
            ScenarioContext.Check(line.Contains($"killer=\"{gm.CharacterName}\" killerRealm=Midgard", StringComparison.Ordinal), line);
            return line;
        });
        SeenLiving back = null!;
        await ctx.Step($"{owner.CharacterName} revives {hired.Name} at once (/hire revive 1 beside the healer)", async () =>
        {
            int gmMark = gm.Mark();
            owner.Command("/hire revive 1");
            var created = await gm.WaitForOrNullAsync<LivingCreated>(c => c.Name == hired.Name, TimeSpan.FromSeconds(15), gmMark);
            var alive = created == null ? null
                : await gm.WaitForOrNullAsync<ObjectMoved>(m => m.ObjectId == created.ObjectId && m.Health > 0, TimeSpan.FromSeconds(10), gmMark);
            if (alive == null)
            {
                var seen = gm.SeenById(victim.ObjectId);
                if (seen is { Health: > 0 })
                    back = seen;
                else
                    throw new PlaytestException($"{hired.Name} was not seen standing again by the GM: " + DeadBots.SystemSince(owner, owner.Now - 15));
            }
            else
                back = gm.SeenById(created!.ObjectId) ?? new SeenLiving(created.ObjectId, created.Name, false, created.Level, created.X, created.Y, created.Z);
            return $"object {back.ObjectId}" + (alive != null ? $", health {alive.Health}%" : "");
        });
        await Kill(ctx, gm, back, worth: false);
    }

    private static async Task Kill(ScenarioContext ctx, ScriptedPlayer gm, SeenLiving bot, bool worth)
    {
        string title = worth
            ? $"the GM kills {bot.Name} (/harm 100000): experience and bounty points, as a player kill pays"
            : $"killed again within the repeat-kill window: \"worth no experience\", no experience";
        await ctx.Step(title, async () =>
        {
            gm.TargetObject(bot.ObjectId);
            await Task.Delay(300);
            double at = gm.Now;
            int mark = gm.Mark();
            gm.Command("/harm 100000");
            if (worth)
            {
                var xp = await gm.WaitForChatAsync(l => l.Text.StartsWith("You get ", StringComparison.Ordinal) && l.Text.Contains(" experience points", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(15), mark) ?? throw new PlaytestException("no experience for the kill: " + DeadBots.SystemSince(gm, at));
                await Task.Delay(1500);
                long experience = gm.ExperienceAnnouncedSince(at);
                var lines = gm.ChatSnapshot().Where(l => l.At >= at).Select(l => l.Text).ToList();
                string? bounty = lines.FirstOrDefault(l => Bounty.IsMatch(l));
                string? realm = lines.FirstOrDefault(l => Realm.IsMatch(l));
                ctx.Measure("experience for the kill", experience);
                ctx.Measure("bounty points line", bounty ?? "(none)");
                ctx.Measure("realm points line", realm ?? "(none)");
                ScenarioContext.Check(experience > 0, $"experience {experience}: {xp.Text}");
                ScenarioContext.Check(bounty != null, "no bounty points line: " + string.Join(" / ", lines.Take(8)));
                return $"{xp.Text} {bounty} {realm}";
            }
            var none = await gm.WaitForChatAsync(l => l.Text.Contains("has been killed recently and is worth no experience!", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15), mark) ?? throw new PlaytestException("no \"worth no experience\" line: " + DeadBots.SystemSince(gm, at));
            await Task.Delay(1500);
            long again = gm.ExperienceAnnouncedSince(at);
            ScenarioContext.Check(again == 0, $"{again} experience for a kill within the window");
            return none.Text;
        });
    }
}
