using OfflineDaoc.Passport;

const string Usage = """
    Take a character to a friend's world and back. Each world's server must be stopped.

      inspect <passport>
      leave   <home runtime> <character> <out folder> [--purse copper] [--host label]
      arrive  <host runtime> <passport> <account> [--name visiting-name]
      depart  <host runtime> <character> <out folder> [--purse copper]
      confirm <host runtime> <character> <trip id>
      home    <home runtime> <passport>
      recall  <home runtime> <character>

    With a friend's server running, over its Passport Desk (login port + 10):

      visitors <address> <desk port> <account> <password>
      bring    <home runtime> <character> <address> <desk port> <account> <password> [--name visiting-name] [--folder path]
      return   <home runtime> <character id> <address> <desk port> <account> <password> [--folder path]
    """;

var positional = new List<string>();
var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (int i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
        options[args[i][2..]] = args[++i];
    else
        positional.Add(args[i]);
}

long? Purse() => options.TryGetValue("purse", out string? text) ? long.Parse(text) : null;

string Folder() => options.GetValueOrDefault("folder", PassportTravel.DefaultFolder);
PassportDeskClient Desk(int at) => new(positional[at], int.Parse(positional[at + 1]), positional[at + 2], positional[at + 3]);

try
{
    switch (positional.FirstOrDefault()?.ToLowerInvariant(), positional.Count)
    {
        case ("inspect", 2):
            Manifest manifest = PassportFile.Read(positional[1]).Manifest;
            Console.WriteLine($"{manifest.Name}, level {manifest.Level}, class {manifest.Class}, realm {manifest.Realm}");
            Console.WriteLine($"{manifest.Direction} passport from {manifest.FromWorld}, trip {manifest.TripId}, purse {manifest.PurseCopper} copper");
            break;
        case ("leave", 4):
            Console.WriteLine(PassportEngine.Leave(positional[1], positional[2], Purse(), options.GetValueOrDefault("host", "a friend's world"), positional[3]));
            break;
        case ("arrive", 4):
            ArrivalReport report = PassportEngine.Arrive(positional[1], positional[2], positional[3], options.GetValueOrDefault("name"));
            Console.WriteLine($"{report.Name} arrived in slot {report.AccountSlot}.");
            report.Notes.ToList().ForEach(Console.WriteLine);
            break;
        case ("depart", 4):
            Console.WriteLine(PassportEngine.Depart(positional[1], positional[2], Purse(), positional[3]));
            break;
        case ("confirm", 4):
            PassportEngine.ConfirmDeparture(positional[1], positional[2], positional[3]);
            break;
        case ("home", 3):
            PassportEngine.ComeHome(positional[1], positional[2]).ToList().ForEach(Console.WriteLine);
            break;
        case ("recall", 3):
            PassportEngine.Recall(positional[1], positional[2]);
            break;
        case ("visitors", 5):
            using (PassportDeskClient desk = Desk(1))
                foreach (VisitorInfo visitor in await desk.VisitorsAsync())
                    Console.WriteLine($"{visitor.CharacterId} {visitor.Name} ({visitor.HomeName}) {visitor.State} trip {visitor.TripId}");
            break;
        case ("bring", 7):
            using (PassportDeskClient desk = Desk(3))
                Console.WriteLine(await PassportTravel.BringAsync(positional[1], positional[2], positional[3], desk, Folder(), options.GetValueOrDefault("name"), Purse()));
            break;
        case ("return", 7):
            using (PassportDeskClient desk = Desk(3))
            {
                VisitorInfo visitor = (await desk.VisitorsAsync()).Single(v => v.CharacterId == positional[2]);
                (await PassportTravel.ComeHomeAsync(positional[1], visitor, desk, Folder(), Purse())).ToList().ForEach(Console.WriteLine);
            }
            break;
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
    return 0;
}
catch (PassportException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message}");
    return 1;
}
