// A scripted player: one game client the playtester drives over the real 1.127 protocol (no game client, no window).
// It logs in, lists, creates and selects characters, enters the world, says, sends tells, uses /faction and any other
// slash command (GM commands too when the account is a GM), reads every chat line it is sent, invites and accepts
// groups, targets and walks straight lines. Everything it receives is kept, so a scenario can wait for "a line like
// this within N seconds" and the report can list what was seen.
// The protocol code is the new client's (source/new-client/godot/game/net/DaocClient.cs). lometur's HearthDAoC headless
// client (playerbots/HeadlessClient.cs, their #137) was the starting idea: it logs in through the real handlers but runs
// inside the server with no socket, so this tool talks over TCP instead and none of its code is ported.
using System.Diagnostics;
using OfflineDaoc.Playtester.Protocol;

namespace OfflineDaoc.Playtester;

internal sealed record ChatLine(double At, int Type, string Text, string Player)
{
    public string Channel => ChatTypes.Name(Type);
}

internal sealed record SeenLiving(int ObjectId, string Name, bool IsPlayer, int Level, int X, int Y, int Z, int Health = 100, int Target = 0,
    double UpdatedAt = 0);

internal sealed class ScriptedPlayer : IAsyncDisposable
{
    // eRace -> male / female model (eLivingModel; the new client's RACES table) and the starting stats
    // (GlobalConstants.STARTING_STATS_DICT) in the packet's order STR DEX CON QUI INT PIE EMP CHA.
    private static readonly Dictionary<int, (int Male, int Female, int[] Stats)> Races = new()
    {
        [1] = (32, 35, new[] { 60, 60, 60, 60, 60, 60, 60, 60 }),      // Briton
        [3] = (39, 43, new[] { 70, 50, 70, 50, 60, 60, 60, 60 }),      // Highlander
        [5] = (503, 507, new[] { 70, 50, 70, 50, 60, 60, 60, 60 }),    // Norseman
        [9] = (302, 310, new[] { 60, 60, 60, 60, 60, 60, 60, 60 }),    // Celt
    };

    private readonly Stopwatch _clock;
    private readonly Func<int, int, int, int?> _zoneOf;
    private readonly List<object> _log = new();
    private readonly object _sync = new();
    private readonly HashSet<int> _asked = new();
    private bool _targetInView;
    private readonly HashSet<(int Who, int Whom)> _targeted = new();   // which living had which target (ObjectUpdate)

    /// <summary>Whether a living was seen with this target (its object updates carry its target).</summary>
    public bool HasTargeted(int who, int whom)
    {
        lock (_sync)
            return _targeted.Contains((who, whom));
    }                          // we have a target and tell the server it is in view
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DaocConnection? _net;
    private Task? _pump;
    private CancellationTokenSource _stop = new();
    private bool _inWorld;
    private int _realmAsked;

    public string Label { get; }
    public string Account { get; private set; } = "";
    public string CharacterName { get; private set; } = "";
    public int Realm { get; private set; }
    public int ObjectId { get; private set; }
    public int Region { get; private set; }
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Z { get; private set; }
    public int Heading { get; private set; }
    public int Zone { get; private set; }
    public bool Connected => _net?.IsOpen == true;
    public bool InWorld => _inWorld;
    public string? DisconnectReason { get; private set; }
    public Dictionary<int, SeenLiving> Seen { get; } = new();
    public long Experience { get; private set; } = -1;
    public long Money { get; private set; } = -1;
    /// <summary>The inventory by slot (40-79 the backpack), as the server's inventory updates describe it.</summary>
    public Dictionary<int, InventoryItem> Inventory { get; } = new();
    public IReadOnlyList<GroupMember> Group { get; private set; } = Array.Empty<GroupMember>();
    private readonly List<ChatLine> _chat = new();

    /// <summary>Every chat line received so far (a copy: the reader thread keeps adding).</summary>
    public IReadOnlyList<ChatLine> ChatSnapshot()
    {
        lock (_sync)
            return _chat.ToList();
    }

    /// <param name="zoneOf">the zone id at a region position (the test world's Zones table), for position updates</param>
    /// <param name="zoneOrigin">a zone's region offset (its OffsetX / OffsetY times 8192), to place objects' updates</param>
    public ScriptedPlayer(string label, Stopwatch clock, Func<int, int, int, int?> zoneOf, Func<int, (int X, int Y)?>? zoneOrigin = null)
    {
        Label = label;
        _clock = clock;
        _zoneOf = zoneOf;
        _zoneOrigin = zoneOrigin ?? (_ => null);
    }

    private readonly Func<int, (int X, int Y)?> _zoneOrigin;

    public double Now => _clock.Elapsed.TotalSeconds;

    // ------------------------------------------------------------------ connecting and characters

    public async Task ConnectAsync(string host, int port, TimeSpan timeout)
    {
        _net = new DaocConnection();
        using var cancel = new CancellationTokenSource(timeout);
        await _net.OpenAsync(host, port, cancel.Token);
        _pump = Task.Run(PumpAsync);
        _ = Task.Run(KeepAliveAsync);
    }

    /// <summary>Logs in (the server creates a missing account when automatic account creation is on).</summary>
    public async Task LoginAsync(string account, string password, TimeSpan timeout)
    {
        int mark = Mark();
        Send(ClientPackets.Login(account, password));
        var reply = await WaitForAsync<ServerEvent>(e => e is LoginGranted or LoginDenied or Disconnected, timeout, mark);
        if (reply is LoginDenied denied)
            throw new PlaytestException($"login refused for {account} (eLoginError {denied.Code})");
        if (reply is Disconnected gone)
            throw new PlaytestException($"disconnected while logging in: {gone.Why}");
        Account = account;
    }

    /// <summary>The characters of one realm (1 Albion, 2 Midgard, 3 Hibernia).</summary>
    public async Task<IReadOnlyList<CharacterSlot>> ListCharactersAsync(int realm, TimeSpan timeout)
    {
        int mark = Mark();
        _realmAsked = realm;
        Send(ClientPackets.CharacterOverview(realm));
        var list = await WaitForAsync<CharacterList>(_ => true, timeout, mark);
        return list.Characters;
    }

    /// <summary>Creates a character with the starting stats plus 10 on three stats, and returns it from the refreshed list.</summary>
    public async Task<CharacterSlot> CreateCharacterAsync(int realm, string name, int classId, int race, bool female, TimeSpan timeout)
    {
        if (!Races.TryGetValue(race, out var look))
            throw new ArgumentException($"race {race} is not in the playtester's table", nameof(race));
        var existing = await ListCharactersAsync(realm, timeout);
        int slot = Enumerable.Range(0, 10).FirstOrDefault(s => existing.All(c => c.Slot != s), -1);
        if (slot < 0)
            throw new PlaytestException($"no free character slot in realm {realm}");
        int mark = Mark();
        Send(ClientPackets.CheckName(name));
        var check = await WaitForAsync<NameChecked>(_ => true, timeout, mark);
        if (check.Result != 0)
            throw new PlaytestException($"the name {name} was refused (DupNameCheck {check.Result})");
        int[] stats = (int[])look.Stats.Clone();
        stats[0] += 10; stats[1] += 10; stats[2] += 10;   // STR DEX CON: 30 creation points, as the validator wants
        mark = Mark();
        Send(ClientPackets.CreateCharacter(realm, slot, name, classId, race, female ? 1 : 0, female ? look.Female : look.Male, stats));
        // The server answers a creation with LoginGranted again (the character screen refreshes).
        await WaitForAsync<ServerEvent>(e => e is LoginGranted or Disconnected, timeout, mark);
        var after = await ListCharactersAsync(realm, timeout);
        return after.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new PlaytestException($"{name} was not in the character list after creation (refused by the server?)");
    }

    /// <summary>Enters the world as game.dll did and waits until the server has placed the character.</summary>
    public async Task EnterWorldAsync(CharacterSlot character, TimeSpan timeout)
    {
        await ListCharactersAsync(character.Realm, timeout);   // the server enters from the realm last asked
        int mark = Mark();
        foreach (var packet in ClientPackets.EnterWorld(character.Realm, character.Slot, character.Name))
            Send(packet);
        var you = await WaitForAsync<PositionAndObjectId>(_ => true, timeout, mark);
        CharacterName = character.Name;
        Realm = character.Realm;
        Send(ClientPackets.PlayerInit());
        Send(ClientPackets.GameOpen());
        _inWorld = true;
        // A client's first position update puts the character in its zone ("You have entered ..."): until then other
        // players nearby do not hear its /say (first in-container run, 2026-11-04). Wait for that line, at most 5 s.
        int entered = Mark();
        SendPosition(you.X, you.Y, you.Z, 0);
        await WaitForOrNullAsync<ChatLine>(l => l.Text.StartsWith("You have entered ", StringComparison.Ordinal), TimeSpan.FromSeconds(5), entered);
    }

    // ------------------------------------------------------------------ talking

    /// <summary>Any slash command: "/faction hi", "/invite Name", "/jump to 1 2 3 1" ...</summary>
    public void Command(string slashCommand)
    {
        string text = slashCommand.TrimStart();
        if (text.StartsWith('/'))
            text = "&" + text[1..];
        else if (!text.StartsWith('&'))
            text = "&say " + text;
        Send(ClientPackets.Command(text));
    }

    public void Say(string text) => Command("/say " + text);
    public void Tell(string name, string text) => Command($"/send {name} {text}");
    public void Faction(string text) => Command("/faction " + text);
    public void Invite(string name) => Command("/invite " + name);

    /// <summary>Waits for a chat line (from now, or from a mark) that matches.</summary>
    public Task<ChatLine?> WaitForChatAsync(Func<ChatLine, bool> match, TimeSpan timeout, int? since = null) =>
        WaitForOrNullAsync<ChatLine>(match, timeout, since ?? Mark());

    // ------------------------------------------------------------------ groups, targets, moving

    /// <summary>Accepts the next group invitation (Dialog code 0x05) and waits for the group window.</summary>
    public async Task<IReadOnlyList<GroupMember>> AcceptGroupInviteAsync(TimeSpan timeout, int? since = null)
    {
        int from = since ?? Mark();
        var invite = await WaitForAsync<DialogBox>(d => d.Code == 0x05, timeout, from);
        int mark = Mark();
        Send(ClientPackets.DialogResponse(invite.Data1, invite.Data2, invite.Data3, invite.Code, 1));
        var window = await WaitForAsync<GroupWindow>(g => g.Members.Count >= 2, timeout, mark);
        return window.Members;
    }

    public async Task<IReadOnlyList<GroupMember>> WaitForGroupAsync(Func<IReadOnlyList<GroupMember>, bool> match, TimeSpan timeout, int since)
    {
        var window = await WaitForAsync<GroupWindow>(g => match(g.Members), timeout, since);
        return window.Members;
    }

    /// <summary>A living of this name in view now, or the first one created for us from the mark on (within the timeout).</summary>
    public async Task<SeenLiving?> SeeAsync(string name, TimeSpan timeout, int since)
    {
        var created = await WaitForOrNullAsync<LivingCreated>(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase), TimeSpan.FromMilliseconds(300), since);
        if (created == null)
            lock (_sync)
            {
                var already = Seen.Values.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (already != null)
                    return already;
            }
        created ??= await WaitForOrNullAsync<LivingCreated>(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase), timeout, since);
        return created == null ? null : new SeenLiving(created.ObjectId, created.Name, created.IsPlayer, created.Level, created.X, created.Y, created.Z);
    }

    private static readonly System.Text.RegularExpressions.Regex XpLine = new(@"^You get ([\d,]+) experience points", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The experience the server announced since a time ("You get 2,300 experience points." lines), summed.</summary>
    public long ExperienceAnnouncedSince(double at) =>
        ChatSnapshot().Where(l => l.At >= at).Select(l => XpLine.Match(l.Text)).Where(m => m.Success)
            .Sum(m => long.Parse(m.Groups[1].Value.Replace(",", "")));

    /// <summary>Buys from the targeted merchant (its window's page * 30 + slot).</summary>
    public void Buy(int page, int slot, int count = 1) => Send(ClientPackets.Buy((int)X, (int)Y, _net?.SessionId ?? 0, page * 30 + slot, count));

    /// <summary>Moves an item between slots (10 = the right hand, 40-79 the backpack).</summary>
    public void MoveItem(int fromSlot, int toSlot, int count = 1) => Send(ClientPackets.MoveItem(_net?.SessionId ?? 0, fromSlot, toSlot, count));

    /// <summary>Trains a specialization to a level at the trainer whose window is open (its place in the window's list).</summary>
    public void TrainSpec(int index, int level) => Send(ClientPackets.TrainSpec(index, level));

    /// <summary>Answers the next dialog of this code with yes (or no) and returns it (null when none came).</summary>
    public async Task<DialogBox?> AnswerDialogAsync(int code, bool yes, TimeSpan timeout, int since)
    {
        var dialog = await WaitForOrNullAsync<DialogBox>(d => d.Code == code, timeout, since);
        if (dialog != null)
            Send(ClientPackets.DialogResponse(dialog.Data1, dialog.Data2, dialog.Data3, dialog.Code, yes ? 1 : 0));
        return dialog;
    }

    /// <summary>The living with this object id as last seen (its place follows its updates).</summary>
    public SeenLiving? SeenById(int objectId)
    {
        lock (_sync)
            return Seen.GetValueOrDefault(objectId);
    }

    /// <summary>Attack mode on / off (the client's attack key) on the current target.</summary>
    public void Attack(bool on) => Send(ClientPackets.Attack(on));

    /// <summary>
    /// Melee as a player at the keyboard does: target it, close in to 80 units facing it, attack, and keep facing and
    /// following it until it dies (health 0, a death line naming it, or it leaves view), or the time is up or we die.
    /// Returns how it ended.
    /// </summary>
    public async Task<string> FightAsync(int objectId, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        var first = SeenById(objectId) ?? throw new PlaytestException($"object {objectId} is not in view");
        int mark = Mark();
        Send(ClientPackets.Target(objectId));
        _targetInView = true;
        bool attacking = false;
        int lastHealth = first.Health;
        while (watch.Elapsed < timeout)
        {
            var now = SeenById(objectId);
            if (now == null)
            {
                StopFighting();
                // gone from view: a kill only if it was hurt (a mob that walks or is moved off is not one)
                if (lastHealth >= 100)
                    throw new PlaytestException($"{first.Name} left view unhurt");
                return $"it left view at {lastHealth}% health (dead and removed)";
            }
            lastHealth = now.Health;
            if (now.Health == 0)
            {
                StopFighting();
                return "its health reached 0";
            }
            var mine = await WaitForOrNullAsync<ChatLine>(l => l.Text.StartsWith("You have died", StringComparison.Ordinal) ||
                l.Text.StartsWith(CharacterName + " was just killed", StringComparison.Ordinal), TimeSpan.FromMilliseconds(1), mark);
            if (mine != null)
            {
                StopFighting();
                throw new PlaytestException($"{CharacterName} died fighting {first.Name} ({mine.Text})");
            }
            var died = await WaitForOrNullAsync<ChatLine>(l => (CharacterName.Length == 0 || !l.Text.StartsWith(CharacterName, StringComparison.Ordinal)) &&
                l.Text.Contains(first.Name, StringComparison.OrdinalIgnoreCase) &&
                (l.Text.Contains(" dies", StringComparison.Ordinal) || l.Text.Contains("killed", StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromMilliseconds(50), mark);
            if (died != null)
            {
                StopFighting();
                return died.Text;
            }
            double dx = now.X - X, dy = now.Y - Y, dist = Math.Sqrt(dx * dx + dy * dy);
            Heading = (int)((Math.Atan2(-dx, dy) * 2048 / Math.PI + 4096) % 4096);
            if (dist > 120 && dist < 3000)
            {
                double step = Math.Min(dist - 80, 191 * 0.4);
                SendPosition((float)(X + dx / dist * step), (float)(Y + dy / dist * step), now.Z, 191);
            }
            else
                SendPosition(X, Y, Z, 0);
            if (!attacking && dist < 300)
            {
                Send(ClientPackets.Target(objectId));
                Attack(true);
                attacking = true;
            }
            await Task.Delay(400);
        }
        StopFighting();
        throw new PlaytestException($"{first.Name} still alive after {timeout.TotalSeconds:0} s");
    }

    private void StopFighting()
    {
        Attack(false);
        _targetInView = false;
    }

    /// <summary>Targets a living seen nearby by its name (the server created it for us).</summary>
    public SeenLiving Target(string name)
    {
        SeenLiving? living;
        lock (_sync)
            living = Seen.Values.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (living == null)
            throw new PlaytestException($"{name} is not in view");
        Send(ClientPackets.Target(living.ObjectId));
        return living;
    }

    /// <summary>Right-click on a living or object (a quest giver, a merchant, a door).</summary>
    public void Interact(int objectId) => Send(ClientPackets.Interact(objectId, (int)X, (int)Y, _net?.SessionId ?? 0));

    /// <summary>The backpack slot (40-79) of an item with this name, or null.</summary>
    public int? BackpackSlotOf(string itemName)
    {
        lock (_sync)
            return Inventory.Values.Where(i => i.Slot is >= 40 and <= 79 && i.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                .Select(i => (int?)i.Slot).FirstOrDefault();
    }

    /// <summary>Gives an item from a backpack slot to a living (drag and drop on it: PlayerMoveItemRequestHandler, slot 1000 + id).</summary>
    public void GiveItem(int fromSlot, int objectId, int count = 1) =>
        Send(ClientPackets.MoveItem(_net?.SessionId ?? 0, fromSlot, 1000 + objectId, count));

    /// <summary>
    /// Answers a quest offer window (Dialog 0x22: data1 the quest, data2 the giver) through DialogResponseHandler's
    /// QuestSubscribe case (0x64, data3 0 = subscribe). Unverified against game.dll: the hook scenarios report it.
    /// </summary>
    public void AnswerQuestOffer(DialogBox offer, bool accept) =>
        Send(ClientPackets.DialogResponse(offer.Data1, offer.Data2, 0, 0x64, accept ? 1 : 0));

    /// <summary>Walks a straight line at running speed, sending position updates as a client does (5 a second).</summary>
    public async Task MoveToAsync(float x, float y, float z, int speed = 191)
    {
        const double step = 0.2;
        double dx = x - X, dy = y - Y, dz = z - Z;
        double length = Math.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)Math.Ceiling(length / (speed * step)));
        Heading = (int)((Math.Atan2(-dx, dy) * 2048 / Math.PI + 4096) % 4096);
        float sx = X, sy = Y, sz = Z;
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            SendPosition((float)(sx + dx * t), (float)(sy + dy * t), (float)(sz + dz * t), i < steps ? speed : 0);
            await Task.Delay(TimeSpan.FromSeconds(step));
        }
    }

    /// <summary>/quit and wait for the server to send us back to the character screen (it saves the character first).</summary>
    public async Task QuitAsync(TimeSpan timeout)
    {
        int mark = Mark();
        Command("/quit");
        await WaitForAsync<ServerEvent>(e => e is QuitToScreen or Disconnected, timeout, mark);
        _inWorld = false;
    }

    // ------------------------------------------------------------------ the event log

    /// <summary>A place in the event log: waits started from it also see what arrived after it but before the wait.</summary>
    public int Mark()
    {
        lock (_sync)
            return _log.Count;
    }

    public async Task<T> WaitForAsync<T>(Func<T, bool> match, TimeSpan timeout, int since) where T : class
    {
        var found = await WaitForOrNullAsync(match, timeout, since);
        return found ?? throw new PlaytestException($"{Label}: no {typeof(T).Name} within {timeout.TotalSeconds:0} s" +
            (DisconnectReason != null ? $" (disconnected: {DisconnectReason})" : ""));
    }

    public async Task<T?> WaitForOrNullAsync<T>(Func<T, bool> match, TimeSpan timeout, int since) where T : class
    {
        var deadline = Stopwatch.StartNew();
        int next = since;
        while (true)
        {
            Task changed;
            lock (_sync)
            {
                for (; next < _log.Count; next++)
                    if (_log[next] is T item && match(item))
                        return item;
                changed = _changed.Task;
            }
            var left = timeout - deadline.Elapsed;
            if (left <= TimeSpan.Zero)
                return null;
            await Task.WhenAny(changed, Task.Delay(left));
        }
    }

    private void Record(object item)
    {
        TaskCompletionSource wake;
        lock (_sync)
        {
            _log.Add(item);
            wake = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        wake.TrySetResult();
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var (code, data) in _net!.Inbox.ReadAllAsync(_stop.Token))
            {
                if (code < 0)
                {
                    DisconnectReason = Text1252.Encoding.GetString(data);
                    _inWorld = false;
                    Record(new Disconnected(DisconnectReason));
                    continue;
                }
                if (Trace)
                    Console.Error.WriteLine($"[{Label} {Now:0.00}] <- 0x{code:X2} ({data.Length} bytes)");
                ServerEvent e;
                try { e = ServerPackets.Decode(code, data, _realmAsked); }
                catch (Exception ex) { e = new OtherPacket(code, data.Length); Console.Error.WriteLine($"[{Label}] packet 0x{code:X2}: {ex.Message}"); }
                if (Trace && e is not OtherPacket)
                    Console.Error.WriteLine($"[{Label} {Now:0.00}]    {Describe(e)}");
                Apply(e);
                Record(e);
                if (e is ChatMessage m)
                    Record(AddChat(m));
            }
        }
        catch (OperationCanceledException) { }
    }

    private static string Describe(ServerEvent e) => e is CharacterList list
        ? $"CharacterList [{string.Join("; ", list.Characters)}]"
        : e.ToString();

    /// <summary>Dead since "You have died", alive again at "You have been resurrected" or a release.</summary>
    public bool IsDead { get; private set; }

    private ChatLine AddChat(ChatMessage m)
    {
        var line = new ChatLine(Math.Round(Now, 2), m.Type, m.Text, Label);
        if (m.Text.StartsWith("You have died", StringComparison.Ordinal))
            IsDead = true;
        else if (m.Text.StartsWith("You have been resurrected", StringComparison.Ordinal) || m.Text.StartsWith("You release", StringComparison.Ordinal))
            IsDead = false;
        lock (_sync)
            _chat.Add(line);
        return line;
    }

    private void Apply(ServerEvent e)
    {
        switch (e)
        {
            case SessionAssigned s:
                _net!.SessionId = (ushort)s.SessionId;
                break;
            case PositionAndObjectId you:
                ObjectId = you.ObjectId;
                Region = you.Region;
                X = you.X; Y = you.Y; Z = you.Z;
                Heading = you.Heading;
                Zone = _zoneOf(Region, (int)X, (int)Y) ?? Zone;
                if (!_inWorld && Region != 0 && CharacterName != "")
                {                                         // after a region change: tell the server the zone is loaded
                    Send(ClientPackets.PlayerInit());
                    Send(ClientPackets.GameOpen());
                    _inWorld = true;
                }
                break;
            case Jumped j when j.X != 0 || j.Y != 0:
                X = j.X; Y = j.Y; Z = j.Z;
                Zone = _zoneOf(Region, j.X, j.Y) ?? Zone;
                break;
            case RegionChanged:
                _inWorld = false;
                Send(ClientPackets.WorldInitAgain());
                break;
            case LivingCreated c:
                lock (_sync)
                {
                    Seen[c.ObjectId] = new SeenLiving(c.ObjectId, c.Name, c.IsPlayer, c.Level, c.X, c.Y, c.Z, UpdatedAt: Now);
                    _asked.Remove(c.ObjectId);
                }
                break;
            case ObjectMoved moved when moved.ObjectId != ObjectId:
                // An update for a living never created for us: ask for it, as game.dll did (CreateObjectRequest 0xBE;
                // the new client does the same: its DaocClient.RequestObject). A known one gets its new place and health.
                bool ask;
                lock (_sync)
                {
                    ask = !Seen.ContainsKey(moved.ObjectId) && _asked.Add(moved.ObjectId);
                    if (Seen.TryGetValue(moved.ObjectId, out var known))
                    {
                        var origin = _zoneOrigin(moved.Zone);
                        Seen[moved.ObjectId] = origin is { } o
                            ? known with { X = o.X + moved.X, Y = o.Y + moved.Y, Z = moved.Z, Health = moved.Health, Target = moved.Target, UpdatedAt = Now }
                            : known with { Health = moved.Health, Target = moved.Target, UpdatedAt = Now };
                        if (moved.Target != 0)
                            _targeted.Add((moved.ObjectId, moved.Target));
                    }
                }
                if (ask)
                    Send(ClientPackets.RequestObject(moved.ObjectId));
                break;
            case PlayerMoved pm when pm.ObjectId != ObjectId:
                // Players are not created by the world update: game.dll asks for one when its position arrives
                // (ClientService.UpdateWorld: "Players aren't updated here on purpose"; PlayerCreationRequestHandler).
                bool askPlayer;
                lock (_sync)
                    askPlayer = !Seen.ContainsKey(pm.ObjectId) && _asked.Add(pm.ObjectId);
                if (askPlayer)
                    Send(ClientPackets.RequestPlayer(pm.SessionId));
                break;
            case ObjectRemoved gone:
                lock (_sync)
                    Seen.Remove(gone.ObjectId);
                break;
            case GroupWindow g:
                Group = g.Members;
                break;
            case LosRequest los:
                Send(ClientPackets.LosReply(los.Source, los.Target, true));
                break;
            case QuitToScreen:
                _inWorld = false;
                break;
            case InventoryUpdate inv:
                lock (_sync)
                    foreach (var item in inv.Items)
                        if (item.Name.Length == 0) Inventory.Remove(item.Slot); else Inventory[item.Slot] = item;
                break;
            case MoneyUpdate money:
                Money = money.Copper;
                break;
            case PointsUpdate points:
                Experience = points.Experience;
                break;
        }
    }

    private async Task KeepAliveAsync()
    {
        var sincePing = Stopwatch.StartNew();
        while (!_stop.IsCancellationRequested && Connected)
        {
            try
            {
                await Task.Delay(2000, _stop.Token);
                if (sincePing.Elapsed.TotalSeconds >= 10)
                {
                    Send(ClientPackets.Ping((uint)_clock.ElapsedMilliseconds));
                    sincePing.Restart();
                }
                if (_inWorld)
                    SendPosition(X, Y, Z, 0);         // standing still: the heartbeat a client sends
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { return; }
        }
    }

    private void SendPosition(float x, float y, float z, int speed)
    {
        X = x; Y = y; Z = z;
        Zone = _zoneOf(Region, (int)x, (int)y) ?? Zone;
        Send(ClientPackets.Position(x, y, z, speed, _net!.SessionId, ObjectId, Zone, Heading, 100, _targetInView));
    }

    /// <summary>PLAYTESTER_TRACE=1 prints every packet code sent and received (for working out a protocol question).</summary>
    private static readonly bool Trace = Environment.GetEnvironmentVariable("PLAYTESTER_TRACE") == "1";

    private void Send((byte Code, PacketWriter Body) packet)
    {
        if (_net == null || !_net.IsOpen)
            return;
        if (Trace)
            Console.Error.WriteLine($"[{Label} {Now:0.00}] -> 0x{packet.Code:X2} ({packet.Body.Length} bytes)");
        try { _net.Send(packet); }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _net?.Dispose();
        if (_pump != null)
            try { await _pump.WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception) { }
    }
}

internal sealed class PlaytestException(string message) : Exception(message);

/// <summary>eChatType names (packets/Server/IPacketLib.cs), for the report.</summary>
internal static class ChatTypes
{
    public const int System = 0x00, Say = 0x01, Send = 0x02, Group = 0x03, Guild = 0x04, Broadcast = 0x05, Emote = 0x06,
        Help = 0x07, Chat = 0x08, Lfg = 0x23, Trade = 0x24;

    public static string Name(int type) => type switch
    {
        System => "system", Say => "say", Send => "tell", Group => "group", Guild => "guild", Broadcast => "broadcast",
        Emote => "emote", Help => "help", Chat => "chat", 0x09 => "advice", 0x0a => "officer", 0x0b => "alliance",
        0x0c or 0x0d => "battlegroup", 0x0f => "staff", Lfg => "lfg", Trade => "trade",
        0x10 or 0x13 or 0x19 or 0x1b or 0x1f => "spell", 0x11 or 0x12 or 0x17 or 0x18 or 0x1d or 0x1e => "combat",
        0x14 => "merchant", 0x15 or 0x16 or 0x20 or 0x21 or 0x22 => "death", 0x1a => "loot", 0x1c => "important",
        0x64 => "social", >= 0xC8 and <= 0xCB => "screen",
        _ => $"type{type}",
    };

    /// <summary>A /faction line as AutonomousBotChatCoordinator.SendFaction writes it: "(Name) &lt;Faction&gt; text".</summary>
    public static bool IsFaction(ChatLine line, out string speaker)
    {
        speaker = "";
        if (line.Type != Broadcast || !line.Text.StartsWith('('))
            return false;
        int close = line.Text.IndexOf(") <Faction> ", StringComparison.Ordinal);
        if (close <= 1)
            return false;
        speaker = line.Text[1..close];
        return true;
    }

    /// <summary>A tell as ChatUtil.SendSendMessage writes it: "Name sends, \"text\"".</summary>
    public static bool IsTellFrom(ChatLine line, string name) =>
        line.Type == Send && line.Text.StartsWith(name + " sends, ", StringComparison.OrdinalIgnoreCase);
}
