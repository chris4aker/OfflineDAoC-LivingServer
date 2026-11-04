// The scenarios that need no gamebots: they run on the minimal world (CI) as well as on the real test world.
using System.Text.RegularExpressions;

using OfflineDaoc.Playtester.Protocol;

namespace OfflineDaoc.Playtester.Scenarios;

/// <summary>(a) A character survives a save and a server restart.</summary>
internal sealed class LoginSaveRestart : Scenario
{
    public override string Id => "login-save-restart";
    public override string Title => "Log in, save, restart the server, log in again";
    public override string Checks => "A new character walks 300 units, quits (the server saves it), the server stops and starts again, " +
        "and the character is listed with the same level and class and comes back where it quit.";
    public override bool RestartsServer => true;

    private static readonly Regex Gloc = new(@"You are at X:(-?\d+) Y:(-?\d+) Z:(-?\d+) Heading:(\d+) Region:(\d+)", RegexOptions.Compiled);

    public override async Task RunAsync(ScenarioContext ctx)
    {
        if (ctx.Server == null)
            throw new SkipScenario("needs --server-dir: this scenario stops and starts the server itself");
        var first = await ctx.LoginAsync("player", "playtester");
        var character = await ctx.PlayAsync(first, "Ptsave");
        int x = 0, y = 0, z = 0, region = 0;
        await ctx.Step("walk 300 units and ask the server where we are (/gloc)", async () =>
        {
            float startX = first.X;
            await first.MoveToAsync(first.X + 300, first.Y, first.Z);
            await Task.Delay(500);
            int mark = first.Mark();
            first.Command("/gloc");
            var line = await first.WaitForChatAsync(l => Gloc.IsMatch(l.Text), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("no /gloc answer");
            var m = Gloc.Match(line.Text);
            (x, y, z, region) = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[5].Value));
            ScenarioContext.Check(Math.Abs(x - (startX + 300)) < 60, $"the server has us at X {x}, not near {startX + 300:0}");
            return line.Text;
        });
        await ctx.Step("/quit: the server saves the character and sends us to the character screen", async () =>
        {
            await first.QuitAsync(TimeSpan.FromSeconds(75));
            return "back at the character screen";
        });
        await ctx.Step("the save is in the world database", () =>
        {
            var saved = ctx.World.Character(character.Name) ?? throw new PlaytestException($"{character.Name} is not in DOLCharacters");
            ctx.Measure("saved position", $"{saved.X},{saved.Y},{saved.Z} region {saved.Region}");
            ScenarioContext.Check(saved.Region == region && Math.Abs(saved.X - x) < 60 && Math.Abs(saved.Y - y) < 60,
                $"saved at {saved.X},{saved.Y} region {saved.Region}, the server said {x},{y} region {region}");
            return Task.FromResult($"level {saved.Level}, class {saved.Class}, at {saved.X},{saved.Y},{saved.Z} region {saved.Region}, last played {saved.LastPlayed}");
        });
        await first.DisposeAsync();
        await ctx.Step("stop the server (console exit: every player and bot saved)", async () =>
        {
            int? code = await ctx.Server.StopAsync(ctx.Options.ServerStopTimeout);
            return $"exit code {code}";
        });
        await ctx.Step("start the server again", async () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await ctx.Server.StartAsync(ctx.Options.ServerStartTimeout);
            ctx.Measure("restart seconds", Math.Round(watch.Elapsed.TotalSeconds, 1));
            return $"started in {watch.Elapsed.TotalSeconds:0.0} s";
        });
        var again = await ctx.LoginAsync("player-again", "playtester");
        CharacterSlot? listed = null;
        await ctx.Step("the character is listed with the same level and class", async () =>
        {
            var list = await again.ListCharactersAsync(character.Realm, ctx.Options.NetTimeout);
            listed = list.FirstOrDefault(c => c.Name == character.Name) ?? throw new PlaytestException($"{character.Name} is not in the list after the restart");
            ScenarioContext.Check(listed.Level == character.Level && listed.ClassId == character.ClassId,
                $"listed as level {listed.Level} class {listed.ClassId}, created as level {character.Level} class {character.ClassId}");
            return $"{listed.Name}, level {listed.Level} {listed.ClassName}, {listed.Location}";
        });
        await ctx.Step("enter the world: back where it quit", async () =>
        {
            await again.EnterWorldAsync(listed!, ctx.Options.NetTimeout);
            double off = Math.Sqrt(Math.Pow(again.X - x, 2) + Math.Pow(again.Y - y, 2));
            ctx.Measure("distance from where it quit", Math.Round(off, 1));
            ScenarioContext.Check(again.Region == region && off < 60, $"came back at {again.X:0},{again.Y:0} region {again.Region}, quit at {x},{y} region {region}");
            return $"at {again.X:0},{again.Y:0},{again.Z:0} region {again.Region} ({off:0.0} units from where it quit)";
        });
    }
}

/// <summary>Say and tells between two scripted players (needs no gamebots).</summary>
internal sealed class ChatBetweenPlayers : Scenario
{
    public override string Id => "chat-players";
    public override string Title => "Say and tells between two players";
    public override string Checks => "Two scripted players at the same start: a /say from one is heard by the other, and a tell each way arrives.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        var one = await ctx.LoginAsync("one", "playtester");
        var two = await ctx.LoginAsync("two", "playtester2");
        var a = await ctx.PlayAsync(one, "Ptone");
        var b = await ctx.PlayAsync(two, "Pttwo");
        string token = Names.Fresh("Check").ToLowerInvariant();
        await ctx.Step($"{a.Name} and {b.Name} see each other", async () =>
        {
            // Until the server has placed both in each other's view, a /say is not heard (first run here, 2026-11-04).
            var seen = await one.WaitForOrNullAsync<LivingCreated>(c => c.IsPlayer && c.Name == b.Name, TimeSpan.FromSeconds(15), 0);
            var seenBack = await two.WaitForOrNullAsync<LivingCreated>(c => c.IsPlayer && c.Name == a.Name, TimeSpan.FromSeconds(15), 0);
            ScenarioContext.Check(seen != null && seenBack != null, $"{a.Name} sees {b.Name}: {seen != null}; {b.Name} sees {a.Name}: {seenBack != null}");
            return $"objects {seen!.ObjectId} and {seenBack!.ObjectId}";
        });
        await ctx.Step($"{a.Name} says a line; {b.Name} hears it", async () =>
        {
            int mark = two.Mark();
            one.Say("hello " + token);
            var heard = await two.WaitForChatAsync(l => l.Type == ChatTypes.Say && l.Text.Contains(token), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("the /say was not heard");
            return heard.Text;
        });
        await ctx.Step($"{a.Name} sends {b.Name} a tell", async () =>
        {
            int mark = two.Mark();
            double sent = one.Now;
            one.Tell(b.Name, "tell " + token);
            var got = await two.WaitForChatAsync(l => ChatTypes.IsTellFrom(l, a.Name) && l.Text.Contains(token), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("the tell did not arrive");
            ctx.Measure("tell seconds", Math.Round(got.At - sent, 2));
            return got.Text;
        });
        await ctx.Step($"{b.Name} answers with a tell", async () =>
        {
            int mark = one.Mark();
            two.Tell(a.Name, "back " + token);
            var got = await one.WaitForChatAsync(l => ChatTypes.IsTellFrom(l, b.Name) && l.Text.Contains(token), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("the answer did not arrive");
            return got.Text;
        });
    }
}

/// <summary>A group made by invitation, and group chat (needs no gamebots).</summary>
internal sealed class GroupInvite : Scenario
{
    public override string Id => "group-players";
    public override string Title => "Invite, accept, group chat, leave";
    public override string Checks => "One player invites the other by name, the other accepts the dialog, both get the group window with two " +
        "members, a /g line is heard, and /disband ends it.";

    public override async Task RunAsync(ScenarioContext ctx)
    {
        var lead = await ctx.LoginAsync("leader", "playtester");
        var member = await ctx.LoginAsync("member", "playtester2");
        var a = await ctx.PlayAsync(lead, "Ptlead");
        var b = await ctx.PlayAsync(member, "Ptmemb");
        await ctx.Step($"{a.Name} invites {b.Name}; {b.Name} accepts", async () =>
        {
            int leadMark = lead.Mark(), memberMark = member.Mark();
            lead.Invite(b.Name);
            var members = await member.AcceptGroupInviteAsync(ctx.Options.NetTimeout, memberMark);
            await lead.WaitForGroupAsync(g => g.Count == 2, ctx.Options.NetTimeout, leadMark);
            return "group: " + string.Join(", ", members.Select(m => $"{m.Name} ({m.ClassName} {m.Level})"));
        });
        await ctx.Step("group chat (/g) is heard", async () =>
        {
            int mark = member.Mark();
            string token = Names.Fresh("grp").ToLowerInvariant();
            lead.Command("/g ready " + token);
            var heard = await member.WaitForChatAsync(l => l.Type == ChatTypes.Group && l.Text.Contains(token), ctx.Options.NetTimeout, mark)
                ?? throw new PlaytestException("the group line was not heard");
            return heard.Text;
        });
        await ctx.Step("/disband", async () =>
        {
            int mark = lead.Mark();
            lead.Command("/disband");
            var line = await lead.WaitForChatAsync(l => l.Type == ChatTypes.System, TimeSpan.FromSeconds(5), mark);
            return line?.Text ?? "(no system line)";
        });
    }
}
