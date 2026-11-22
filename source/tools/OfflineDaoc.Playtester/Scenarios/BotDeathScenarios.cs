// 1.2.1's bot death and rez fixes, at the packet level (they need no gamebots: a /spawn companion helper is a GameBot
// like any gamebot, and the GM's /lstest deathanim kills and raises it through the server's own code):
// - BotDeathAnimation: at a bot's death the original client (a non-zero handshake build) gets the removal of type 0 that
//   plays the fall, then nothing that creates, updates or removes the bot until it has health again; the new client
//   (build 0) gets what it got before. GameClient.IsOriginalGameClient, PacketLib168 / 171 / 1124, ClientService.
// - BotCorpseTargeting: /target <name> takes a dead bot, and a player's resurrection with nothing dead selected goes to
//   the bot named with /target, else a groupmate, else the nearest (target.cs, SpellHandler.CheckBeginCast).
// What these cannot see: what game.dll draws. They check the packets the server sends, not the fall on screen.
using System.Text.RegularExpressions;
using OfflineDaoc.Playtester.Protocol;

namespace OfflineDaoc.Playtester.Scenarios;

internal static class DeadBots
{
    public const string GmAccount = "playtestergm";

    /// <summary>TemporaryGroupCommands' "/spawn" answer: "Name, level 1 Armsman, joined fully equipped: ...".</summary>
    private static readonly Regex Joined = new(@"^(?<name>.+?), level (?<level>\d+) (?<class>\w+).*, joined fully equipped", RegexOptions.Compiled);

    /// <summary>The GM (the original client: /lstest is a GM command) in the world.</summary>
    public static async Task<ScriptedPlayer> GmAsync(ScenarioContext ctx, string prefix)
    {
        ScenarioContext.Check(ctx.World.AccountPrivLevel(GmAccount) >= 2, $"the test world's {GmAccount} account is not a GM");
        var gm = await ctx.LoginAsync("gm", GmAccount, ClientPackets.OriginalClientBuild);
        await ctx.PlayAsync(gm, prefix);
        return gm;
    }

    /// <summary>/spawn armsman: a companion helper (a GameBot) joins the owner's group. Returns it as the owner sees it.</summary>
    public static async Task<SeenLiving> SpawnHelperAsync(ScenarioContext ctx, ScriptedPlayer owner)
    {
        SeenLiving? helper = null;
        await ctx.Step($"{owner.Label}: /spawn armsman (a companion helper joins its group)", async () =>
        {
            int mark = owner.Mark();
            double asked = owner.Now;
            owner.Command("/spawn armsman");
            var line = await owner.WaitForChatAsync(l => l.Type == ChatTypes.System && Joined.IsMatch(l.Text), ctx.Options.NetTimeout, mark);
            if (line == null)
                throw new PlaytestException("no helper joined; the server said: " + SystemSince(owner, asked));
            string name = Joined.Match(line.Text).Groups["name"].Value;
            ScenarioContext.Check(name.Length > 0 && !name.Any(char.IsWhiteSpace), $"the helper's name \"{name}\" is not one word (/target takes one word)");
            helper = await owner.SeeAsync(name, ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException($"{name} joined but was never created for {owner.CharacterName}");
            // The server keeps each object's "what is near me" list for half a second (GameObject.GetObjectsInRadius): a
            // /target within that time of the spawn may not find the new helper yet (CI 2026-11-22, not a 1.2.1 matter).
            await Task.Delay(1500);
            return $"{name} (object {helper.ObjectId}): {line.Text[..Math.Min(80, line.Text.Length)]}";
        });
        return helper!;
    }

    /// <summary>The viewer has (or gets within the time) the living of this name; its object as the viewer knows it.</summary>
    public static async Task<SeenLiving> SeenByAsync(ScenarioContext ctx, ScriptedPlayer viewer, string name) =>
        await viewer.SeeAsync(name, ctx.Options.NetTimeout, 0) ?? throw new PlaytestException($"{viewer.CharacterName} never got {name} created");

    /// <summary>
    /// The GM selects the bot and types /lstest deathanim (it kills it: Health 0, Die). Waits for the GM's first removal of
    /// it and returns that event (its type is the check: 0 is the fall).
    /// </summary>
    public static async Task<ObjectRemoved> KillAsync(ScenarioContext ctx, ScriptedPlayer gm, SeenLiving bot)
    {
        ObjectRemoved? removed = null;
        await ctx.Step($"the GM kills {bot.Name} (/lstest deathanim): the original client gets the fall (RemoveObject type 0)", async () =>
        {
            gm.TargetObject(bot.ObjectId);
            await Task.Delay(400);
            int mark = gm.Mark();
            gm.Command("/lstest deathanim");
            var said = await gm.WaitForChatAsync(l => l.Text.StartsWith("Killing ", StringComparison.Ordinal) ||
                l.Text.StartsWith("Target a bot", StringComparison.Ordinal) || l.Text.Contains(" is dead: showing its fall again", StringComparison.Ordinal),
                ctx.Options.NetTimeout, mark) ?? throw new PlaytestException("no answer to /lstest deathanim");
            ScenarioContext.Check(said.Text.StartsWith("Killing " + bot.Name, StringComparison.Ordinal), "/lstest deathanim: " + said.Text);
            removed = await gm.WaitForOrNullAsync<ObjectRemoved>(r => r.ObjectId == bot.ObjectId, TimeSpan.FromSeconds(10), mark)
                ?? throw new PlaytestException($"no RemoveObject for {bot.Name} (object {bot.ObjectId}) within 10 s of its death");
            ScenarioContext.Check(removed.Type == 0, $"{bot.Name} was removed with type {removed.Type}, not 0 (the fall)");
            return $"RemoveObject {removed.ObjectId} type {removed.Type}";
        });
        return removed!;
    }

    /// <summary>The GM's /lstest deathanim rez: a resurrection cast as a player's goes, with whatever the GM has selected.</summary>
    public static async Task<LivingCreated> RezAsync(ScenarioContext ctx, ScriptedPlayer gm, SeenLiving expected, string why)
    {
        LivingCreated? back = null;
        await ctx.Step($"nothing selected, the GM casts a resurrection (/lstest deathanim rez): it goes to {expected.Name} ({why})", async () =>
        {
            gm.TargetObject(0);                           // the original client lets go of a dead bot's selection
            await Task.Delay(400);
            int mark = gm.Mark();
            gm.Command("/lstest deathanim rez");
            var chosen = await gm.WaitForChatAsync(l => l.Text.StartsWith("You have no dead target selected: your spell goes to ", StringComparison.Ordinal) ||
                l.Text.Contains("The cast was refused", StringComparison.Ordinal) || l.Text.StartsWith("You must select a target", StringComparison.Ordinal),
                ctx.Options.NetTimeout, mark) ?? throw new PlaytestException("the server did not say where the resurrection went");
            ScenarioContext.Check(chosen.Text.EndsWith(" goes to " + expected.Name + ".", StringComparison.Ordinal), chosen.Text);
            back = await gm.WaitForOrNullAsync<LivingCreated>(c => c.Name == expected.Name, TimeSpan.FromSeconds(10), mark)
                ?? throw new PlaytestException($"{expected.Name} was not created again for the GM within 10 s of the resurrection");
            var alive = await gm.WaitForOrNullAsync<ObjectMoved>(m => m.ObjectId == back.ObjectId && m.Health > 0, TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(alive != null, $"no update of {expected.Name} with health above 0 after the resurrection");
            double off = Math.Sqrt(Math.Pow(back.X - gm.X, 2) + Math.Pow(back.Y - gm.Y, 2));
            ctx.Measure($"{expected.Name} raised: units from the caster", Math.Round(off, 0));
            return $"{chosen.Text} Created again (object {back.ObjectId}, {off:0} units from the GM), health {alive!.Health}%";
        });
        return back!;
    }

    /// <summary>The server console lines (this start) with the text, waited for up to 10 s; none when no server was started.</summary>
    public static async Task<List<string>> ServerLinesAsync(ScenarioContext ctx, string text)
    {
        if (ctx.Server == null)
            return new List<string>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var lines = ctx.Server.ConsoleLinesWith(text);
            if (lines.Count > 0 || watch.Elapsed > TimeSpan.FromSeconds(10))
                return lines;
            await Task.Delay(500);
        }
    }

    /// <summary>A step that checks the server's log has a line with each text (skipped with --attach: no console to read).</summary>
    public static async Task LogStepAsync(ScenarioContext ctx, string name, params string[] texts)
    {
        if (ctx.Server == null)
        {
            ctx.Todo(name + " (attached to a running server: its console was not read)");
            return;
        }
        await ctx.Step(name, async () =>
        {
            var found = new List<string>();
            foreach (string text in texts)
            {
                var lines = await ServerLinesAsync(ctx, text);
                ScenarioContext.Check(lines.Count > 0, $"no server log line with: {text}");
                string line = lines[^1];
                int at = line.IndexOf(text.Split(' ')[0], StringComparison.Ordinal);
                found.Add(at >= 0 ? line[at..] : line);
            }
            return string.Join(" | ", found);
        });
    }

    public static string SystemSince(ScriptedPlayer player, double at)
    {
        var lines = player.ChatSnapshot().Where(l => l.At >= at && l.Type == ChatTypes.System).Select(l => l.Text).ToList();
        return lines.Count == 0 ? "(nothing)" : string.Join(" / ", lines.TakeLast(4));
    }

    public static string Packets(IEnumerable<ServerEvent> events) => string.Join(", ", events.Select(e => e switch
    {
        LivingCreated c => $"create {c.ObjectId}",
        ObjectMoved m => $"update {m.ObjectId} ({m.Health}%)",
        ObjectRemoved r => $"remove {r.ObjectId} type {r.Type}",
        _ => e.GetType().Name,
    }));
}

/// <summary>1.2.1 BotDeathAnimation: what an original client and the new client get when a bot dies in view, lies dead and is raised.</summary>
internal sealed class BotDeath : Scenario
{
    public override string Id => "bot-death";
    public override string Title => "A bot's death and raise, as the original client and the new client get it";
    public override string Checks => "The GM logs in as the original client (handshake build 0x2A07), a player as the new client (build 0). " +
        "The GM spawns a companion helper (/spawn armsman) and kills it (/lstest deathanim, setting bot_death_animation on by default). " +
        "The original client must get RemoveObject type 0 (the fall) and no other removal; then for 8 s, while the bot lies dead, no " +
        "create, update or removal of it, not even when it asks for the object (CreateObjectRequest), and the server's packet list " +
        "(/lstest deathanim) must say it held something back. The new client must get no type-0 removal and keep getting updates at 0 " +
        "health. /lstest deathanim raise: the original client gets the bot created again, standing (health above 0). The server log " +
        "must have CLIENT_HANDSHAKE client=original and client=new-or-unknown, and BOT_DEATH_ANIM fall / raised with original_clients=1. " +
        "Packets only: what game.dll draws is not seen.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        var gm = await DeadBots.GmAsync(ctx, "Ptdgm");
        var newClient = await ctx.LoginAsync("newclient", "playtester");
        await ctx.PlayAsync(newClient, "Ptdnew");
        var helper = await DeadBots.SpawnHelperAsync(ctx, gm);
        SeenLiving watched = null!;
        await ctx.Step($"the new client has {helper.Name} too", async () =>
        {
            watched = await DeadBots.SeenByAsync(ctx, newClient, helper.Name);
            ScenarioContext.Check(watched.ObjectId == helper.ObjectId, $"object {watched.ObjectId} for the new client, {helper.ObjectId} for the GM");
            return $"object {watched.ObjectId}";
        });
        await ctx.Step("/lstest deathanim knows the GM's client as the original and the setting is on", async () =>
        {
            gm.TargetObject(helper.ObjectId);
            await Task.Delay(400);
            int mark = gm.Mark();
            gm.Command("/lstest deathanim off");      // only the status line and "Packet list stopped."
            var line = await gm.WaitForChatAsync(l => l.Text.StartsWith("Death fall (bot_death_animation)", StringComparison.Ordinal), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("no /lstest deathanim status line");
            ScenarioContext.Check(line.Text.Contains("): on.", StringComparison.Ordinal), "the setting is not on: " + line.Text);
            ScenarioContext.Check(line.Text.Contains("the original game client", StringComparison.Ordinal), "the GM's client is not taken as the original: " + line.Text);
            return line.Text;
        });

        int gmBefore = gm.Mark(), newBefore = newClient.Mark();
        double killedAt = gm.Now;
        var fall = await DeadBots.KillAsync(ctx, gm, helper);
        int fallPlace = gm.PlaceOf(fall);
        await ctx.Step("the original client: nothing but the fall at the death (no type-1 removal before it)", () =>
        {
            var before = gm.EventsSince<ServerEvent>(gmBefore, fallPlace + 1).Where(e => About(e, helper.ObjectId)).ToList();
            ctx.Measure("original client, the death's packets", DeadBots.Packets(before));
            ScenarioContext.Check(before.OfType<ObjectRemoved>().Count() == 1, "removals: " + DeadBots.Packets(before));
            return Task.FromResult(DeadBots.Packets(before));
        });
        await ctx.Step("while it lies dead (8 s): the original client gets no create, update or removal of it, even when it asks", async () =>
        {
            await Task.Delay(3000);
            gm.RequestObject(helper.ObjectId);       // as game.dll asks for an object it was updated about but does not have
            await Task.Delay(5000);
            var after = gm.EventsSince<ServerEvent>(fallPlace + 1).Where(e => About(e, helper.ObjectId)).ToList();
            ScenarioContext.Check(after.Count == 0, $"{after.Count} packet(s) about the dead bot after its fall: " + DeadBots.Packets(after));
            return "none";
        });
        await ctx.Step("the server's packet list (/lstest deathanim) shows the fall sent and packets held back", () =>
        {
            var trace = gm.ChatSnapshot().Where(l => l.At >= killedAt && l.Text.StartsWith("deathanim: ", StringComparison.Ordinal)).Select(l => l.Text).ToList();
            ctx.Measure("deathanim lines", string.Join(" / ", trace));
            ScenarioContext.Check(trace.Any(t => t.StartsWith("deathanim: sent RemoveObject type 0 (the fall)", StringComparison.Ordinal)), "no \"sent RemoveObject type 0\" line");
            ScenarioContext.Check(trace.Any(t => t.StartsWith("deathanim: held ", StringComparison.Ordinal)), "no \"held\" line");
            return Task.FromResult(string.Join(" / ", trace));
        });
        await ctx.Step("the new client: no fall (no type-0 removal), it keeps getting the bot at 0 health", () =>
        {
            var seen = newClient.EventsSince<ServerEvent>(newBefore).Where(e => About(e, helper.ObjectId)).ToList();
            ctx.Measure("new client, packets about the dead bot", DeadBots.Packets(seen.Take(12)) + (seen.Count > 12 ? $" (+{seen.Count - 12})" : ""));
            ScenarioContext.Check(!seen.OfType<ObjectRemoved>().Any(), "the new client got: " + DeadBots.Packets(seen.OfType<ObjectRemoved>()));
            ScenarioContext.Check(seen.OfType<ObjectMoved>().Any(m => m.Health == 0), "no update at 0 health: " + DeadBots.Packets(seen));
            return Task.FromResult($"{seen.OfType<ObjectMoved>().Count(m => m.Health == 0)} update(s) at 0 health, no removal");
        });
        await ctx.Step("/lstest deathanim raise: the original client gets the bot created again, standing", async () =>
        {
            int mark = gm.Mark();
            gm.Command("/lstest deathanim raise");
            var said = await gm.WaitForChatAsync(l => l.Text.StartsWith("Raising ", StringComparison.Ordinal) || l.Text.Contains(" is alive.", StringComparison.Ordinal) ||
                l.Text.StartsWith("Target a bot", StringComparison.Ordinal), ctx.Options.NetTimeout, mark) ?? throw new PlaytestException("no answer to /lstest deathanim raise");
            ScenarioContext.Check(said.Text.StartsWith("Raising ", StringComparison.Ordinal), said.Text);
            var created = await gm.WaitForOrNullAsync<LivingCreated>(c => c.Name == helper.Name, TimeSpan.FromSeconds(10), mark)
                ?? throw new PlaytestException("not created again within 10 s of the raise");
            var standing = await gm.WaitForOrNullAsync<ObjectMoved>(m => m.ObjectId == created.ObjectId && m.Health > 0, TimeSpan.FromSeconds(10), mark);
            ScenarioContext.Check(standing != null, "no update with health above 0 after the raise");
            return $"created again (object {created.ObjectId}), health {standing!.Health}%";
        });
        await DeadBots.LogStepAsync(ctx, "the server log tells the two clients apart and records the fall and the raise",
            "client=original", "client=new-or-unknown",
            $"BOT_DEATH_ANIM fall bot=\"{helper.Name}\" id={helper.ObjectId} original_clients=1",
            $"BOT_DEATH_ANIM raised bot=\"{helper.Name}\"");
        await ctx.Step("the raise counted the one original client in range", async () =>
        {
            var lines = await DeadBots.ServerLinesAsync(ctx, $"BOT_DEATH_ANIM raised bot=\"{helper.Name}\"");
            if (ctx.Server == null)
                return "(attached: not read)";
            ScenarioContext.Check(lines.Any(l => l.Contains("original_clients=1", StringComparison.Ordinal)), string.Join(" / ", lines));
            return lines[^1][lines[^1].IndexOf("BOT_DEATH_ANIM", StringComparison.Ordinal)..];
        });
    }

    private static bool About(ServerEvent e, int id) => e switch
    {
        LivingCreated c => c.ObjectId == id,
        ObjectMoved m => m.ObjectId == id,
        ObjectRemoved r => r.ObjectId == id,
        _ => false,
    };
}

/// <summary>1.2.1 target.cs: /target &lt;name&gt; takes a dead bot (the original client cannot select one).</summary>
internal sealed class BotCorpseTarget : Scenario
{
    public override string Id => "bot-corpse-target";
    public override string Title => "/target names a dead bot";
    public override string Checks => "A player (original client) spawns a companion helper; the GM kills it (/lstest deathanim) and both " +
        "original clients get the fall. The player types /target <its name>: the server answers \"You target <name>.\" and sends " +
        "ChangeTarget with the bot's object; the GM does the same. The server log's BOT_DEATH_ANIM fall line counts both original " +
        "clients. Packets only: what game.dll shows as selected is not seen.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        var gm = await DeadBots.GmAsync(ctx, "Ptcgm");
        var player = await ctx.LoginAsync("player", "playtester", ClientPackets.OriginalClientBuild);
        await ctx.PlayAsync(player, "Ptcorp");
        var helper = await DeadBots.SpawnHelperAsync(ctx, player);
        SeenLiving forGm = null!;
        await ctx.Step($"the GM has {helper.Name} too", async () =>
        {
            forGm = await DeadBots.SeenByAsync(ctx, gm, helper.Name);
            return $"object {forGm.ObjectId}";
        });
        int playerBefore = player.Mark();
        await DeadBots.KillAsync(ctx, gm, forGm);
        await ctx.Step("the player's original client got the fall too", async () =>
        {
            var fell = await player.WaitForOrNullAsync<ObjectRemoved>(r => r.ObjectId == helper.ObjectId, TimeSpan.FromSeconds(10), playerBefore);
            ScenarioContext.Check(fell != null, "no RemoveObject for the player");
            ScenarioContext.Check(fell!.Type == 0, $"removed with type {fell.Type}");
            return $"RemoveObject type {fell.Type}";
        });
        foreach (var who in new[] { player, gm })
        {
            await ctx.Step($"{who.Label}: /target {helper.Name} selects the dead bot", async () =>
            {
                int mark = who.Mark();
                who.Command("/target " + helper.Name);
                var said = await who.WaitForChatAsync(l => l.Text.Contains("You target ", StringComparison.Ordinal) ||
                    l.Text.StartsWith("You don't see ", StringComparison.Ordinal), ctx.Options.NetTimeout, mark)
                    ?? throw new PlaytestException("no answer to /target");
                if (!said.Text.Contains("You target " + helper.Name, StringComparison.Ordinal))
                {
                    // Once more, 2 s on, and what the server logged about the bot, for the report.
                    await Task.Delay(2000);
                    int again = who.Mark();
                    who.Command("/target " + helper.Name);
                    var second = await who.WaitForChatAsync(l => l.Text.Contains("You target ", StringComparison.Ordinal) ||
                        l.Text.StartsWith("You don't see ", StringComparison.Ordinal), ctx.Options.NetTimeout, again);
                    var logged = ctx.Server?.ConsoleLinesWith(helper.Name).TakeLast(8).Select(l => l.Length > 220 ? l[..220] : l) ?? Enumerable.Empty<string>();
                    throw new PlaytestException($"{said.Text} (2 s later: {second?.Text ?? "no answer"}); server lines: {string.Join(" || ", logged)}");
                }
                var changed = await who.WaitForOrNullAsync<TargetChanged>(t => t.ObjectId == helper.ObjectId, TimeSpan.FromSeconds(5), mark);
                ScenarioContext.Check(changed != null, $"no ChangeTarget to object {helper.ObjectId}");
                return $"{said.Text} ChangeTarget {changed!.ObjectId}";
            });
        }
        await DeadBots.LogStepAsync(ctx, "the server log's fall line counts both original clients",
            $"BOT_DEATH_ANIM fall bot=\"{helper.Name}\" id={helper.ObjectId} original_clients=2");
    }
}

/// <summary>1.2.1 BotCorpseTargeting: a resurrection with nothing dead selected picks the corpse.</summary>
internal sealed class BotRez : Scenario
{
    public override string Id => "bot-rez";
    public override string Title => "A resurrection with nothing selected picks the dead bot";
    public override string Checks => "The GM (original client) and a player each spawn a companion helper; the player walks 900 units off. " +
        "The GM kills its own helper, walks next to the player's and kills that one, so the stranger's corpse is the nearer. With nothing " +
        "selected the GM casts a resurrection (/lstest deathanim rez: a corpse spell cast through the player casting path, " +
        "CastingComponent and SpellHandler.CheckBeginCast): the server must say it goes to the groupmate and create it again, standing, " +
        "at the GM's side. The GM kills its helper again (now the nearest corpse, and a groupmate), names the stranger's with " +
        "/target and casts again with nothing selected: it must go to the one named. The cast is the test command's spell, not a " +
        "class's own resurrection (the minimal world has no spell lines): it is a GM cast through the same code. Packets only.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        var gm = await DeadBots.GmAsync(ctx, "Ptrgm");
        var player = await ctx.LoginAsync("player", "playtester");
        await ctx.PlayAsync(player, "Ptrez");
        await ctx.Step("the player walks 900 units off", async () =>
        {
            await player.MoveToAsync(player.X + 900, player.Y, player.Z);
            return $"at {player.X:0},{player.Y:0}";
        });
        var mine = await DeadBots.SpawnHelperAsync(ctx, gm);
        var theirs = await DeadBots.SpawnHelperAsync(ctx, player);
        await Task.Delay(2000);
        SeenLiving stranger = null!;
        await ctx.Step($"the GM has the player's helper {theirs.Name}", async () =>
        {
            stranger = await DeadBots.SeenByAsync(ctx, gm, theirs.Name);
            return $"object {stranger.ObjectId}";
        });
        var mineAtDeath = gm.SeenById(mine.ObjectId) ?? mine;
        await DeadBots.KillAsync(ctx, gm, mineAtDeath);
        double toMine = 0, toStranger = 0;
        await ctx.Step($"the GM walks next to {theirs.Name}, so its corpse will be the nearer", async () =>
        {
            var there = gm.SeenById(stranger.ObjectId) ?? stranger;
            double dx = gm.X - there.X, dy = gm.Y - there.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            await gm.MoveToAsync((float)(there.X + dx / d * 80), (float)(there.Y + dy / d * 80), gm.Z);
            await Task.Delay(1000);
            var now = gm.SeenById(stranger.ObjectId) ?? there;
            toMine = Math.Sqrt(Math.Pow(gm.X - mineAtDeath.X, 2) + Math.Pow(gm.Y - mineAtDeath.Y, 2));
            toStranger = Math.Sqrt(Math.Pow(gm.X - now.X, 2) + Math.Pow(gm.Y - now.Y, 2));
            ctx.Measure("GM to its own helper's corpse", Math.Round(toMine));
            ctx.Measure("GM to the player's helper", Math.Round(toStranger));
            ScenarioContext.Check(toMine > toStranger + 150, $"its own helper's corpse is {toMine:0} units off, the player's helper {toStranger:0}: not a test of the order");
            ScenarioContext.Check(toMine < 1400, $"its own helper's corpse is {toMine:0} units off: out of the spell's 1500");
            return $"{toMine:0} units from {mine.Name}'s corpse (groupmate), {toStranger:0} from {theirs.Name} (stranger)";
        });
        stranger = gm.SeenById(stranger.ObjectId) ?? stranger;
        await DeadBots.KillAsync(ctx, gm, stranger);
        int beforeRez = gm.Mark();
        var raised = await DeadBots.RezAsync(ctx, gm, mine, $"the groupmate, {toMine:0} units off, before the stranger {toStranger:0} units off");
        await ctx.Step($"{theirs.Name} (still dead) was not created for the GM", () =>
        {
            var created = gm.EventsSince<LivingCreated>(beforeRez).Where(c => c.Name == theirs.Name).ToList();
            ScenarioContext.Check(created.Count == 0, $"{theirs.Name} was created {created.Count} time(s)");
            return Task.FromResult("not created");
        });
        var mineAgain = gm.SeenById(raised.ObjectId) ?? new SeenLiving(raised.ObjectId, raised.Name, false, raised.Level, raised.X, raised.Y, raised.Z);
        await DeadBots.KillAsync(ctx, gm, mineAgain);
        await ctx.Step($"/target {theirs.Name}: the GM names the stranger's corpse", async () =>
        {
            int mark = gm.Mark();
            gm.Command("/target " + theirs.Name);
            var said = await gm.WaitForChatAsync(l => l.Text.Contains("You target ", StringComparison.Ordinal) ||
                l.Text.StartsWith("You don't see ", StringComparison.Ordinal), ctx.Options.NetTimeout, mark) ?? throw new PlaytestException("no answer to /target");
            ScenarioContext.Check(said.Text.Contains("You target " + theirs.Name, StringComparison.Ordinal), said.Text);
            return said.Text;
        });
        await DeadBots.RezAsync(ctx, gm, theirs, "the one named with /target, before the groupmate lying at the GM's feet");
        await DeadBots.LogStepAsync(ctx, "the server log has both raises",
            $"BOT_DEATH_ANIM raised bot=\"{mine.Name}\"", $"BOT_DEATH_ANIM raised bot=\"{theirs.Name}\"");
    }
}

/// <summary>#438 (1.2.1): GM "/jump &lt;bot&gt; to me" moves a bot (a companion helper has no client, so the player lookup
/// never found it and the GM was told it was not in the game).</summary>
internal sealed class BotJump : Scenario
{
    public override string Id => "bot-jump";
    public override string Title => "GM /jump <bot> to me moves a companion helper";
    public override string Checks => "A player spawns a companion helper (/spawn armsman). The GM, 900 units off, types /jump <helper> to me: " +
        "the server must answer \"<helper> jumps to you\", the GM must get the helper within 150 units of itself (a create or an " +
        "update), and the server log must have a GM_JUMP_BOT line for it. Packets and log only.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        var gm = await DeadBots.GmAsync(ctx, "Ptjgm");
        var player = await ctx.LoginAsync("player", "playtester");
        await ctx.PlayAsync(player, "Ptjump");
        var helper = await DeadBots.SpawnHelperAsync(ctx, player);
        await ctx.Step("the GM walks 900 units off", async () =>
        {
            await gm.MoveToAsync(gm.X + 900, gm.Y, gm.Z);
            await Task.Delay(1000);
            return $"at {gm.X:0},{gm.Y:0}";
        });
        await ctx.Step($"/jump {helper.Name} to me: the helper is put beside the GM", async () =>
        {
            int mark = gm.Mark();
            gm.Command($"/jump {helper.Name} to me");
            var said = await gm.WaitForChatAsync(l => l.Text.Contains(" jumps to you", StringComparison.Ordinal) ||
                l.Text.Contains("is not in the game", StringComparison.OrdinalIgnoreCase) ||
                l.Text.Contains("could not be moved", StringComparison.Ordinal), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("no answer to /jump; the server said: " + DeadBots.SystemSince(gm, gm.Now - 5));
            ScenarioContext.Check(said.Text.StartsWith(helper.Name + " jumps to you", StringComparison.Ordinal), said.Text);
            ServerEvent? near = null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (near == null && watch.Elapsed < TimeSpan.FromSeconds(10))
            {
                near = gm.EventsSince<ServerEvent>(mark).FirstOrDefault(e => e switch
                {
                    LivingCreated c => c.Name == helper.Name && Near(gm, c.X, c.Y),
                    ObjectMoved m => m.ObjectId == helper.ObjectId && Near(gm, m.X, m.Y),
                    _ => false,
                });
                if (near == null) await Task.Delay(250);
            }
            ScenarioContext.Check(near != null, $"the GM got no create or update of {helper.Name} within 150 units of itself");
            var (x, y) = near switch { LivingCreated c => (c.X, c.Y), ObjectMoved m => (m.X, m.Y), _ => (0, 0) };
            double off = Math.Sqrt(Math.Pow(x - gm.X, 2) + Math.Pow(y - gm.Y, 2));
            ctx.Measure("helper after /jump: units from the GM", Math.Round(off, 0));
            return $"{said.Text} ({near.GetType().Name}, {off:0} units from the GM)";
        });
        await DeadBots.LogStepAsync(ctx, "the server log records the GM's move of the helper",
            $"GM_JUMP_BOT gm=\"{gm.CharacterName}\" bot=\"{helper.Name}\"");
    }

    private static bool Near(ScriptedPlayer gm, int x, int y) => Math.Sqrt(Math.Pow(x - gm.X, 2) + Math.Pow(y - gm.Y, 2)) <= 150;
}
