using System.Net;
using System.Net.Http.Json;
using NUnit.Framework;

namespace OfflineDaoc.Passport.Tests;

/// <summary>The launcher's flows against a stand-in Desk, which runs the same engine on the host's save.</summary>
public partial class PassportRoundTripTests
{
    string Folder => Path.Combine(_folder, "passports");

    sealed class FakeDesk(string host) : HttpMessageHandler
    {
        /// <summary>Runs the call, then acts as if its answer never arrived.</summary>
        public bool LoseAnswer { get; set; }
        /// <summary>The first call, the login check, still gets through; nothing after it does.</summary>
        public bool Down { get; set; }
        int _calls;
        public int LoseAnswerOfConfirm { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down && ++_calls > 1)
                throw new HttpRequestException("unreachable");

            string path = request.RequestUri!.AbsolutePath;
            System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            HttpResponseMessage response;
            try
            {
                response = path switch
                {
                    "/passport/arrive" => await Arrive(request, query["name"]),
                    "/passport/visitors" => Json(PassportEngine.Visitors(host, "friend")),
                    "/passport/depart" => Depart(query["character"]!, query["purse"]),
                    "/passport/depart/confirm" => Confirm(query["character"]!, query["trip"]!),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                };
            }
            catch (PassportException refusal)
            {
                response = new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { code = refusal.Code, message = refusal.Message }) };
            }

            if (LoseAnswer && path == "/passport/arrive")
            {
                LoseAnswer = false;
                throw new TaskCanceledException();
            }
            return response;
        }

        async Task<HttpResponseMessage> Arrive(HttpRequestMessage request, string? name)
        {
            string file = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(file, await request.Content!.ReadAsByteArrayAsync());
                return Json(PassportEngine.Arrive(host, file, "friend", name));
            }
            finally
            {
                File.Delete(file);
            }
        }

        HttpResponseMessage Depart(string character, string? purse)
        {
            string folder = Path.Combine(Path.GetTempPath(), "depart-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = PassportEngine.Depart(host, character, purse == null ? null : long.Parse(purse), folder);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(path)) };
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        HttpResponseMessage Confirm(string character, string trip)
        {
            if (LoseAnswerOfConfirm > 0)
            {
                LoseAnswerOfConfirm--;
                throw new HttpRequestException("confirm lost");
            }
            PassportEngine.ConfirmDeparture(host, character, trip);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    PassportDeskClient Desk(FakeDesk fake) => new("127.0.0.1", 10310, "friend", "secret", fake);

    Task<string> Bring(FakeDesk fake, string? name = null) => PassportTravel.BringAsync(_home, "Traveller", "Friend's world", Desk(fake), Folder, name);

    bool LockedAtHome => (long)Value(_home, "SELECT count(*) FROM DOLCharactersXCustomParam WHERE KeyName='passport.away'")! > 0;
    long OnHost => (long)Value(_host, $"SELECT count(*) FROM DOLCharacters WHERE DOLCharacters_ID='{Id}'")!;

    [Test]
    public async Task Bring_DeliversTheCharacter()
    {
        Assert.That(await Bring(new FakeDesk(_host)), Is.EqualTo("Traveller"));

        Assert.That(LockedAtHome, Is.True);
        Assert.That(OnHost, Is.EqualTo(1));
        Assert.That(Directory.GetFiles(Path.Combine(Folder, "outgoing")), Is.Empty);
    }

    [Test]
    public void Bring_ARefusalUnlocksTheCharacterAndReturnsThePurse()
    {
        long wallet = Wallet(_home, "me", 2);
        Sql(_host, "INSERT INTO DOLCharacters (DOLCharacters_ID,AccountName,AccountSlot,Name,Realm) VALUES ('x','friend',300,'Traveller',3)");

        Assert.That(Assert.ThrowsAsync<PassportException>(() => Bring(new FakeDesk(_host)))!.Code, Is.EqualTo("name_taken"));

        Assert.That(LockedAtHome, Is.False);
        Assert.That(Wallet(_home, "me", 2), Is.EqualTo(wallet));
        Assert.That(Directory.GetFiles(Path.Combine(Folder, "outgoing")), Is.Empty);
    }

    [Test]
    public void Bring_NoAnswerKeepsTheCharacterLocked()
    {
        var desk = new FakeDesk(_host) { Down = true };

        Assert.That(Assert.ThrowsAsync<PassportException>(() => Bring(desk))!.Code, Is.EqualTo("no_answer"));

        Assert.That(LockedAtHome, Is.True, "The host might have it, so home must not unlock it.");
        Assert.That(Directory.GetFiles(Path.Combine(Folder, "outgoing")), Has.Length.EqualTo(1));
    }

    [Test]
    public void Bring_AWrongLoginLocksNothing()
    {
        var wrong = new RefusingDesk();

        Assert.That(Assert.ThrowsAsync<PassportException>(() => PassportTravel.BringAsync(_home, "Traveller", "x", new PassportDeskClient("127.0.0.1", 10310, "friend", "bad", wrong), Folder))!.Code, Is.EqualTo("login"));

        Assert.That(LockedAtHome, Is.False);
        Assert.That(Directory.Exists(Path.Combine(_home, "passport-backups")), Is.False, "A typo must not cost a save backup.");
    }

    sealed class RefusingDesk : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = JsonContent.Create(new { code = "login", message = "Wrong password." }) });
    }

    [Test]
    public void Backups_KeepOnlyTheNewestFew()
    {
        for (int i = 0; i < 9; i++)
            Directory.CreateDirectory(Path.Combine(_home, "passport-backups", $"20200101-00000{i}-Old-out"));

        Leave();

        Assert.That(Directory.GetDirectories(Path.Combine(_home, "passport-backups")), Has.Length.EqualTo(6));
        Assert.That(Directory.GetFiles(Path.Combine(_home, "passport-backups"), "opendaoc.sqlite3.db", SearchOption.AllDirectories), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task Bring_TryingAgainSendsTheSamePassport()
    {
        var desk = new FakeDesk(_host) { Down = true };
        Assert.ThrowsAsync<PassportException>(() => Bring(desk));
        string trip = PassportEngine.AwayTrip(_home, "Traveller")!;

        desk.Down = false;
        await Bring(desk);

        Assert.That(PassportEngine.AwayTrip(_home, "Traveller"), Is.EqualTo(trip));
        Assert.That(OnHost, Is.EqualTo(1));
        Assert.That(Directory.GetFiles(Path.Combine(Folder, "outgoing")), Is.Empty);
    }

    [Test]
    public async Task Bring_ALostAnswerIsNotARefusal()
    {
        Assert.That(await Bring(new FakeDesk(_host) { LoseAnswer = true }), Is.EqualTo("Traveller"));

        Assert.That(LockedAtHome, Is.True);
        Assert.That(OnHost, Is.EqualTo(1));
    }

    [Test]
    public async Task ComeHome_BringsItAllBack()
    {
        var desk = new FakeDesk(_host);
        long total = Wallet(_home, "me", 2);
        await Bring(desk);
        Sql(_host, $"UPDATE DOLCharacters SET Experience=777 WHERE DOLCharacters_ID='{Id}'");

        VisitorInfo visitor = (await Desk(desk).VisitorsAsync()).Single();
        IReadOnlyList<string> notes = await PassportTravel.ComeHomeAsync(_home, visitor, Desk(desk), Folder);

        Assert.That(notes, Is.Empty);
        Assert.That(LockedAtHome, Is.False);
        Assert.That(OnHost, Is.EqualTo(0));
        Assert.That(Value(_home, "SELECT Experience FROM DOLCharacters"), Is.EqualTo(777L));
        Assert.That(Wallet(_home, "me", 2), Is.EqualTo(total));
        Assert.That(Directory.GetFiles(Path.Combine(Folder, "pending")), Is.Empty);
        Assert.That(Directory.GetFiles(Path.Combine(Folder, "done")), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task ComeHome_ALostConfirmStillBringsItHomeAndClearsOnTheNextVisit()
    {
        var desk = new FakeDesk(_host) { LoseAnswerOfConfirm = 1 };
        await Bring(desk);
        VisitorInfo visitor = (await Desk(desk).VisitorsAsync()).Single();

        IReadOnlyList<string> notes = await PassportTravel.ComeHomeAsync(_home, visitor, Desk(desk), Folder);

        Assert.That(notes, Has.Count.EqualTo(1));
        Assert.That(LockedAtHome, Is.False);
        Assert.That(OnHost, Is.EqualTo(1), "The stale copy stays, locked.");
        Assert.That(PassportEngine.AwayTrip(_host, "Traveller"), Is.Not.Null);

        await Bring(desk);

        Assert.That(OnHost, Is.EqualTo(1));
        Assert.That(PassportEngine.Visitors(_host, "friend").Single().State, Is.EqualTo("visiting"));
        Assert.That(PassportEngine.AwayTrip(_host, "Traveller"), Is.Null, "The fresh visitor is playable.");
    }

    [Test]
    public async Task Pending_FinishesAReturnThatWasInterrupted()
    {
        var desk = new FakeDesk(_host);
        await Bring(desk);
        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        string pending = Directory.CreateDirectory(Path.Combine(Folder, "pending")).FullName;
        File.Copy(back, Path.Combine(pending, Path.GetFileName(back)));

        Assert.That(PassportTravel.ImportPending(_home, Folder), Is.EqualTo(new[] { "Traveller came home." }));

        Assert.That(LockedAtHome, Is.False);
        Assert.That(Directory.GetFiles(pending), Is.Empty);
    }

    [Test]
    public async Task Pending_ARecalledTripDoesNotComeBackOrBlock()
    {
        var desk = new FakeDesk(_host);
        await Bring(desk);
        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        PassportEngine.Recall(_home, "Traveller");
        string pending = Directory.CreateDirectory(Path.Combine(Folder, "pending")).FullName;
        File.Copy(back, Path.Combine(pending, Path.GetFileName(back)));

        Assert.That(PassportTravel.ImportPending(_home, Folder).Single(), Does.StartWith("Traveller: This trip was recalled"));
        Assert.That(Directory.GetFiles(pending), Is.Empty);
    }

    [Test]
    public void Recall_ClearsAPassportWaitingToBeSent()
    {
        var desk = new FakeDesk(_host) { Down = true };
        Assert.ThrowsAsync<PassportException>(() => Bring(desk));
        LocalCharacter character = PassportEngine.Characters(_home, "me").Single();
        Assert.That(character.AwayAt, Is.EqualTo("Friend's world"));

        PassportTravel.Recall(_home, character, Folder);

        Assert.That(LockedAtHome, Is.False);
        Assert.That(PassportTravel.PendingArrival(Id, Folder), Is.Null);
    }

    [Test]
    public async Task Visitors_ListOnlyThatAccountsTravellers()
    {
        await Bring(new FakeDesk(_host));

        Assert.That(PassportEngine.Visitors(_host, "friend").Single().HomeName, Is.EqualTo("Traveller"));
        Assert.That(PassportEngine.Visitors(_host, "stranger"), Is.Empty);
    }
}
