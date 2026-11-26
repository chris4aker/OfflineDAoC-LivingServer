// The scenarios that need no gamebots: they run on the minimal world (CI) as well as on the real test world.
using System.Text.RegularExpressions;

using DAoCLivingServer.Playtester.Protocol;

namespace DAoCLivingServer.Playtester.Scenarios;

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

/// <summary>
/// Character creation follows the edition switch classes / enable_sluaghbinder (AGENTS.md, "Edition switch"): the 0.37b
/// world (on) makes the Sluaghbinder novice from the client's Hibernian Mauler slot, the 0.37 world (off) refuses it and
/// any direct request for the class, and both make the classic classes (CharacterCreateRequestHandler).
/// </summary>
internal sealed class ClassSwitch : Scenario
{
    public const string Account = "playtester3";
    public const int Hibernia = 3;
    public const int Celt = 9;
    public const int Naturalist = 53;   // a classic Hibernian starting class, in every edition
    public const int ClientSlot = 62;   // MaulerHib: the slot the 0.37b game.dll relabels "Sluaghbinder"
    public const int Sluaghbinder = 63;
    public const int Acolyte = 16;      // the Sluaghbinder novice (levels 1-4) is stored as Acolyte
    public static readonly int[] CustomClassIds = { Acolyte, ClientSlot, Sluaghbinder };

    public override string Id => "class-switch";
    public override string Title => "Character creation follows the Sluaghbinder switch";
    public override string Checks => "Reads classes / enable_sluaghbinder from the world (no row: on, the server's default). A player " +
        "(playtester3, not a GM: the refusals apply to players) creates a Hibernian Celt Naturalist, a classic class every edition offers. Then it " +
        "asks for the client's Sluaghbinder slot (class 62, the Hibernian Mauler slot). On: the server saves the Sluaghbinder novice " +
        "(Acolyte, 16). Off: it is refused, and so are direct requests for the Sluaghbinder (63) and the Acolyte (16); the world then " +
        "holds no Hibernian character of class 16, 62 or 63 on the account.";

    /// <summary>A Hibernian Celt of the class, with a fresh name (another one when the server's name list refuses it).</summary>
    private static async Task<CharacterSlot> Create(ScriptedPlayer player, ScenarioContext ctx, string prefix, int classId, Action<string>? named = null)
    {
        for (int attempt = 1; ; attempt++)
        {
            string name = Names.Fresh(prefix);
            named?.Invoke(name);
            try
            {
                return await player.CreateCharacterAsync(Hibernia, name, classId, Celt, false, ctx.Options.NetTimeout);
            }
            catch (PlaytestException e) when (attempt < 4 && e.Message.Contains("was refused (DupNameCheck"))
            {
                // a random name the server's invalid-names list rejects: try another
            }
        }
    }

    public override async Task RunAsync(ScenarioContext ctx)
    {
        string? value = ctx.World.Property("enable_sluaghbinder");
        bool on = ctx.World.CustomClassEnabled;
        ctx.Measure("enable_sluaghbinder", value ?? "(no row: on)");
        ctx.Measure("edition", on ? "class on (0.37b)" : "class off (0.37)");
        // An account of its own: an account that already has characters in another realm is held to that realm (unless the
        // server allows all realms), and the other scenarios make Albion characters on playtester and playtester2.
        var player = await ctx.LoginAsync("player", Account);
        await ctx.Step("the account is a player's (the class refusals don't apply to GMs)", () =>
        {
            int plvl = ctx.World.AccountPrivLevel(Account);
            ScenarioContext.Check(plvl == 1, $"{Account} has plvl {plvl}");
            return Task.FromResult("plvl 1");
        });
        await ctx.Step("a classic class is offered: a Celt Naturalist is created", async () =>
        {
            var made = await Create(player, ctx, "Ptnat", Naturalist);
            var saved = ctx.World.Character(made.Name) ?? throw new PlaytestException($"{made.Name} is listed but not in DOLCharacters");
            ScenarioContext.Check(saved.Class == Naturalist, $"{made.Name} was saved as class {saved.Class}");
            return $"{made.Name}, {made.ClassName} {made.RaceName}, saved as class {saved.Class}";
        });
        if (on)
        {
            await ctx.Step("on: the client's Sluaghbinder slot (62) makes the novice (Acolyte, 16)", async () =>
            {
                var made = await Create(player, ctx, "Ptslu", ClientSlot);
                var saved = ctx.World.Character(made.Name) ?? throw new PlaytestException($"{made.Name} is listed but not in DOLCharacters");
                ScenarioContext.Check(saved.Class == Acolyte, $"{made.Name} was saved as class {saved.Class}, not the Acolyte novice (16)");
                return $"{made.Name} saved as class {saved.Class}, listed as {made.ClassName}";
            });
            return;
        }
        foreach (int classId in CustomClassIds)
        {
            string what = classId switch { ClientSlot => "the client's Sluaghbinder slot (62)", Sluaghbinder => "the Sluaghbinder (63)", _ => "the Acolyte novice (16)" };
            await ctx.Step($"off: {what} is refused", async () =>
            {
                string name = "";
                try
                {
                    var made = await Create(player, ctx, "Ptoff", classId, n => name = n);
                    throw new PlaytestException($"the server created {made.Name} as {made.ClassName} (class {made.ClassId})");
                }
                catch (PlaytestException e) when (e.Message.Contains("was not in the character list"))
                {
                }
                ScenarioContext.Check(ctx.World.Character(name) == null, $"{name} is in DOLCharacters");
                return $"{name} not created";
            });
        }
        await ctx.Step("off: the world holds no Sluaghbinder character on the account", () =>
        {
            var hib = ctx.World.CharactersOf(Account, Hibernia);
            var custom = hib.Where(c => CustomClassIds.Contains(c.Class)).ToList();
            ScenarioContext.Check(custom.Count == 0, "saved: " + string.Join(", ", custom.Select(c => $"{c.Name} ({c.Class})")));
            return Task.FromResult($"{hib.Count} Hibernian character(s): " + string.Join(", ", hib.Select(c => $"{c.Name} ({c.Class})")));
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
