using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace OfflineDaoc.Passport;

/// <summary>The launcher's side of a host's Passport Desk.</summary>
public sealed class PassportDeskClient : IDisposable
{
    /// <summary>The Desk listens this far above the login port, so an invite code needs no second number.</summary>
    public const int PortOffset = 10;

    readonly HttpClient _http;

    public PassportDeskClient(string address, int deskPort, string account, string password, HttpMessageHandler? handler = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri($"http://{address}:{deskPort}/");
        _http.Timeout = TimeSpan.FromSeconds(120);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{account}:{password}")));
    }

    public void Dispose() => _http.Dispose();

    public async Task<ArrivalReport> ArriveAsync(string passportPath, string? visitingName)
    {
        string query = string.IsNullOrWhiteSpace(visitingName) ? "" : $"?name={WebUtility.UrlEncode(visitingName.Trim())}";
        using var content = new ByteArrayContent(await File.ReadAllBytesAsync(passportPath));
        using HttpResponseMessage response = await _http.PostAsync($"passport/arrive{query}", content);
        await ThrowOnRefusalAsync(response);
        return (await response.Content.ReadFromJsonAsync<ArrivalReport>())!;
    }

    public async Task<IReadOnlyList<VisitorInfo>> VisitorsAsync()
    {
        using HttpResponseMessage response = await _http.GetAsync("passport/visitors");
        await ThrowOnRefusalAsync(response);
        return (await response.Content.ReadFromJsonAsync<List<VisitorInfo>>())!;
    }

    public async Task<byte[]> DepartAsync(string characterId, long? purseCopper)
    {
        string purse = purseCopper == null ? "" : $"&purse={purseCopper}";
        using HttpResponseMessage response = await _http.PostAsync($"passport/depart?character={characterId}{purse}", null);
        await ThrowOnRefusalAsync(response);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task ConfirmAsync(string characterId, string tripId)
    {
        using HttpResponseMessage response = await _http.PostAsync($"passport/depart/confirm?character={characterId}&trip={tripId}", null);
        await ThrowOnRefusalAsync(response);
    }

    static async Task ThrowOnRefusalAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        DeskRefusal? refusal = null;
        try
        {
            refusal = await response.Content.ReadFromJsonAsync<DeskRefusal>();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException)
        {
            // Not the Desk's own answer: treated as no answer below.
        }
        if (refusal?.Code == null)
            throw new HttpRequestException($"The Passport Desk answered {(int)response.StatusCode}.");
        throw new PassportException(refusal.Code, refusal.Message ?? refusal.Code);
    }

    sealed record DeskRefusal(string? Code, string? Message);
}

/// <summary>
/// A trip as the launcher runs it. A character may be in one world only, so an unanswered request never
/// counts as a refusal: the host may have taken the character, and only the host can say.
/// </summary>
public static class PassportTravel
{
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfflineDAoC", "passports");

    static string Outgoing(string folder) => Directory.CreateDirectory(Path.Combine(folder, "outgoing")).FullName;
    static string Pending(string folder) => Directory.CreateDirectory(Path.Combine(folder, "pending")).FullName;
    static string Done(string folder) => Directory.CreateDirectory(Path.Combine(folder, "done")).FullName;

    /// <summary>
    /// Locks the character at home and hands it to the host. A definite refusal unlocks it again. With no
    /// answer it stays locked, and calling this again sends the same passport.
    /// </summary>
    public static async Task<string> BringAsync(PassportWorld home, string character, string hostLabel, PassportDeskClient desk, string folder, string? visitingName = null, long? purseCopper = null)
    {
        string path;
        string? trip = PassportEngine.AwayTrip(home, character);
        if (trip != null)
        {
            path = Directory.GetFiles(Outgoing(folder), $"*{trip}-{Manifest.Outbound}.odpassport").SingleOrDefault()
                ?? throw new PassportException("already_away", $"{character} is already away. Use Come Home, or Recall if that world is gone.");
        }
        else
        {
            // A wrong password or a closed Desk stops here, before anything is backed up or locked.
            await desk.VisitorsAsync();
            path = PassportEngine.Leave(home, character, purseCopper, hostLabel, Outgoing(folder));
            trip = PassportFile.Read(path).Manifest.TripId;
        }

        string characterId = PassportFile.Read(path).Manifest.CharacterId;
        try
        {
            ArrivalReport report = await desk.ArriveAsync(path, visitingName);
            File.Delete(path);
            return report.Name;
        }
        catch (PassportException refusal) when (refusal.Code is "already_here" or "replayed")
        {
            // An earlier try got through and its answer was lost.
            VisitorInfo? visitor = await FindAsync(desk, characterId, trip);
            if (visitor == null)
                throw Unlock(home, character, path, refusal);
            File.Delete(path);
            return visitor.Name;
        }
        catch (PassportException refusal)
        {
            throw Unlock(home, character, path, refusal);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            VisitorInfo? visitor = null;
            try
            {
                visitor = await FindAsync(desk, characterId, trip);
            }
            catch (Exception second) when (second is HttpRequestException or TaskCanceledException or PassportException)
            {
                // Still no answer: the host may or may not have the character.
            }
            if (visitor == null)
                throw new PassportException("no_answer", $"Your friend's world did not answer, so {character} stays locked here. Try again, or use Recall if that world is gone.");
            File.Delete(path);
            return visitor.Name;
        }
    }

    /// <summary>The passport of a trip whose first try got no answer, for this character. Null when there is none.</summary>
    public static string? PendingArrival(string characterId, string? folder = null)
    {
        foreach (string path in Directory.GetFiles(Outgoing(folder ?? DefaultFolder), "*.odpassport"))
            if (PassportFile.Read(path).Manifest.CharacterId == characterId)
                return path;
        return null;
    }

    /// <summary>Takes the character back from the host. Once the passport is safe on this PC, the host's confirm is only tidying.</summary>
    public static async Task<IReadOnlyList<string>> ComeHomeAsync(PassportWorld home, VisitorInfo visitor, PassportDeskClient desk, string folder, long? purseCopper = null)
    {
        byte[] passport = await desk.DepartAsync(visitor.CharacterId, purseCopper);
        string path = Path.Combine(Pending(folder), PassportFile.FileName(visitor.HomeName, visitor.TripId, Manifest.Return));
        string partial = path + ".partial";
        await File.WriteAllBytesAsync(partial, passport);
        File.Move(partial, path, overwrite: true);

        var notes = new List<string>();
        try
        {
            await desk.ConfirmAsync(visitor.CharacterId, visitor.TripId);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or PassportException)
        {
            notes.Add("Your friend's world still lists this character, locked. It clears on the next visit.");
        }

        notes.AddRange(ImportPassport(home, path, folder));
        return notes;
    }

    /// <summary>Gives up on a trip: the character is unlocked as it left, and a passport still waiting to be sent goes.</summary>
    public static void Recall(PassportWorld home, LocalCharacter character, string? folder = null)
    {
        PassportEngine.Recall(home, character.Name);
        if (PendingArrival(character.Id, folder) is { } waiting)
            File.Delete(waiting);
    }

    /// <summary>Finishes returns an earlier run could not. Returns one line per character it dealt with.</summary>
    public static IReadOnlyList<string> ImportPending(PassportWorld home, string folder)
    {
        var done = new List<string>();
        foreach (string path in Directory.GetFiles(Pending(folder), "*.odpassport"))
        {
            string name = PassportFile.Read(path).Manifest.Name;
            try
            {
                ImportPassport(home, path, folder);
                done.Add($"{name} came home.");
            }
            catch (PassportException e) when (e.Code is "wrong_trip" or "not_home")
            {
                // Recalled, or meant for another save on this PC; retrying would only fail again.
                File.Move(path, Path.Combine(Done(folder), Path.GetFileName(path)), overwrite: true);
                done.Add($"{name}: {e.Message}");
            }
        }
        return done;
    }

    static IReadOnlyList<string> ImportPassport(PassportWorld home, string path, string folder)
    {
        IReadOnlyList<string> notes = PassportEngine.ComeHome(home, path);
        File.Move(path, Path.Combine(Done(folder), Path.GetFileName(path)), overwrite: true);
        return notes;
    }

    static async Task<VisitorInfo?> FindAsync(PassportDeskClient desk, string characterId, string trip) =>
        (await desk.VisitorsAsync()).FirstOrDefault(v => v.CharacterId == characterId && v.TripId == trip);

    static PassportException Unlock(PassportWorld home, string character, string path, PassportException refusal)
    {
        PassportEngine.Recall(home, character);
        File.Delete(path);
        return refusal;
    }
}
