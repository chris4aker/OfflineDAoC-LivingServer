// Character passport window (docs/CHARACTER-PASSPORT.md). The flows are rambodolphin13/DividedRealms' (da92848; credit:
// rambodolphin13), which put them in his Join Friend window; Living Server 1.2 gives them a window of their own, so the
// hosting work (Join Friend, Host for Friends) and the passport stay separate. The friend's address and account are typed
// here and never saved.
using System.Globalization;
using OfflineDaoc.Passport;

namespace OfflineDaoc.Launcher;

internal sealed class PassportForm : Form
{
    private const int DefaultLoginPort = 10300;
    private const long CopperPerGold = 10_000;

    private readonly string _runtime;
    private readonly string _localAccount;
    private readonly Func<bool> _serverRunning;
    private readonly ListBox _characters = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox _visitors = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _address = new() { Width = 220, PlaceholderText = "friend's address" };
    private readonly NumericUpDown _port = new() { Width = 80, Minimum = 1, Maximum = 65535 - PassportDeskClient.PortOffset, Value = DefaultLoginPort };
    private readonly TextBox _account = new() { Width = 160, PlaceholderText = "your account there" };
    private readonly TextBox _password = new() { Width = 160, UseSystemPasswordChar = true, PlaceholderText = "password there" };
    private readonly TextBox _visitingName = new() { Width = 160, PlaceholderText = "only if the name is taken" };
    private readonly CheckBox _wholeWallet = new() { Text = "Carry the whole realm wallet", Checked = true, AutoSize = true };
    private readonly NumericUpDown _purseGold = new() { Width = 100, Maximum = 1_000_000_000, Enabled = false };
    private readonly CheckBox _welcome = new() { Text = "Friends may bring their characters to my world when I host", AutoSize = true };
    private readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly List<Button> _actions = [];
    private IReadOnlyList<LocalCharacter> _local = [];
    private IReadOnlyList<VisitorInfo> _visiting = [];
    private bool _loadingWelcome;

    public PassportForm(string runtime, string localAccount, Func<bool> serverRunning)
    {
        _runtime = runtime;
        _localAccount = localAccount;
        _serverRunning = serverRunning;
        Text = "Character Passport — take your character to a friend's world and back";
        Size = new Size(980, 680);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.GoldLight;
        Font = new Font("Georgia", 9f);
        AutoScaleMode = AutoScaleMode.None;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

        var friend = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        friend.Controls.Add(Caption("Friend's world:"));
        friend.Controls.Add(_address);
        friend.Controls.Add(Caption("login port"));
        friend.Controls.Add(_port);
        friend.Controls.Add(Caption("account"));
        friend.Controls.Add(_account);
        friend.Controls.Add(_password);
        friend.Controls.Add(Caption("Join your friend's world once first, so your account exists there. Both servers' worlds are backed up before every change.", muted: true));
        layout.Controls.Add(friend, 0, 0);
        layout.SetColumnSpan(friend, 2);

        layout.Controls.Add(Group("Your characters (this world)", _characters), 0, 1);
        layout.Controls.Add(Group("Visiting your friend's world", _visitors), 1, 1);

        var homeActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        homeActions.Controls.Add(Action("TAKE TO FRIEND'S WORLD", DaocTheme.Midgard, BringAsync));
        homeActions.Controls.Add(Action("RECALL…", DaocTheme.Albion, RecallAsync));
        homeActions.Controls.Add(Caption("visiting name"));
        homeActions.Controls.Add(_visitingName);
        homeActions.Controls.Add(_wholeWallet);
        homeActions.Controls.Add(Caption("or gold"));
        homeActions.Controls.Add(_purseGold);
        _wholeWallet.CheckedChanged += (_, _) => _purseGold.Enabled = !_wholeWallet.Checked;
        layout.Controls.Add(homeActions, 0, 2);

        var friendActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        friendActions.Controls.Add(Action("LIST MY VISITORS", DaocTheme.Iron, ListVisitorsAsync));
        friendActions.Controls.Add(Action("COME HOME", DaocTheme.Hibernia, ComeHomeAsync));
        friendActions.Controls.Add(_welcome);
        _welcome.CheckedChanged += (_, _) => { if (!_loadingWelcome) SetWelcome(); };
        layout.Controls.Add(friendActions, 1, 2);

        layout.Controls.Add(_log, 0, 3);
        layout.SetColumnSpan(_log, 2);
        _log.BackColor = DaocTheme.StoneDark;
        _log.ForeColor = DaocTheme.Text;
        Controls.Add(layout);

        Shown += (_, _) => Start();
        UiScale.Apply(this);
    }

    private PassportWorld Home => _runtime;

    private static Label Caption(string text, bool muted = false) => new()
    {
        Text = text, AutoSize = true, Margin = new Padding(6, 7, 3, 0), ForeColor = muted ? DaocTheme.Muted : DaocTheme.GoldLight,
        MaximumSize = muted ? new Size(900, 0) : Size.Empty,
    };

    private static Control Group(string caption, Control content)
    {
        var group = new GroupBox { Text = caption, Dock = DockStyle.Fill, ForeColor = DaocTheme.GoldLight, Padding = new Padding(6) };
        content.BackColor = DaocTheme.Parchment;
        content.ForeColor = DaocTheme.Ink;
        group.Controls.Add(content);
        return group;
    }

    private Button Action(string text, Color accent, Func<Task> work)
    {
        var button = new RuneButton { Text = text, Accent = accent, Width = 210, Height = 34 };
        button.Click += async (_, _) => await RunAsync(work);
        _actions.Add(button);
        return button;
    }

    private void Start()
    {
        Say($"Your world: {_runtime}");
        if (_serverRunning())
            Say("Your server is running. Stop it to take a character away, bring one home or recall one.");
        else
        {
            try
            {
                foreach (string line in PassportTravel.ImportPending(Home, PassportTravel.DefaultFolder))
                    Say(line);
            }
            catch (PassportException e)
            {
                Say(e.Message);
            }
        }
        LoadWelcome();
        LoadCharacters();
    }

    private void LoadWelcome()
    {
        _loadingWelcome = true;
        try
        {
            _welcome.Checked = PassportEngine.VisitorsWelcome(Home);
        }
        catch (Exception e) when (e is PassportException or IOException or System.Data.SQLite.SQLiteException)
        {
            _welcome.Enabled = false;
        }
        finally
        {
            _loadingWelcome = false;
        }
    }

    private void SetWelcome()
    {
        try
        {
            if (_serverRunning())
                throw new PassportException("server_running", "Stop your server first, then change this.");
            PassportEngine.SetVisitorsWelcome(Home, _welcome.Checked);
            Say(_welcome.Checked ? "Friends may now bring their characters when you host." : "New visitors are turned off. Visitors already here can still go home.");
        }
        catch (Exception e) when (e is PassportException or IOException or System.Data.SQLite.SQLiteException)
        {
            Say(e.Message);
            LoadWelcome();
        }
    }

    private void LoadCharacters()
    {
        try
        {
            _local = PassportEngine.Characters(Home, _localAccount);
        }
        catch (Exception e) when (e is PassportException or IOException or System.Data.SQLite.SQLiteException)
        {
            _local = [];
            Say(e.Message);
        }
        _characters.Items.Clear();
        foreach (LocalCharacter character in _local)
            _characters.Items.Add(character.AwayAt == null
                ? $"{character.Name}  ·  level {character.Level}"
                : $"{character.Name}  ·  level {character.Level}  ·  away at {character.AwayAt}");
    }

    private LocalCharacter? SelectedCharacter() =>
        _characters.SelectedIndex >= 0 && _characters.SelectedIndex < _local.Count ? _local[_characters.SelectedIndex] : null;

    private PassportDeskClient Desk()
    {
        string address = _address.Text.Trim();
        if (address.Length == 0 || _account.Text.Trim().Length == 0 || _password.Text.Length == 0)
            throw new PassportException("details", "Type your friend's address, and your account and password in their world.");
        return new PassportDeskClient(address, (int)_port.Value + PassportDeskClient.PortOffset, _account.Text.Trim(), _password.Text);
    }

    private void RefuseWhileServerRuns()
    {
        if (_serverRunning())
            throw new PassportException("server_running", "Stop your server first. Nothing was changed.");
    }

    private async Task BringAsync()
    {
        LocalCharacter character = SelectedCharacter() ?? throw new PassportException("pick", "Pick one of your characters first.");
        RefuseWhileServerRuns();
        long? purse = _wholeWallet.Checked ? null : (long)_purseGold.Value * CopperPerGold;
        using PassportDeskClient desk = Desk();
        string name = await PassportTravel.BringAsync(Home, character.Name, _address.Text.Trim(), desk, PassportTravel.DefaultFolder,
            _visitingName.Text.Trim().Length == 0 ? null : _visitingName.Text.Trim(), purse);
        Say($"{character.Name} is in your friend's world as {name}. Join it and play. It stays locked here until it comes home.");
        LoadCharacters();
    }

    private async Task ListVisitorsAsync()
    {
        using PassportDeskClient desk = Desk();
        _visiting = await desk.VisitorsAsync();
        _visitors.Items.Clear();
        foreach (VisitorInfo visitor in _visiting)
            _visitors.Items.Add($"{visitor.Name}  ·  from home as {visitor.HomeName}  ·  {visitor.State}");
        Say(_visiting.Count == 0 ? "None of your characters is visiting that world." : $"{_visiting.Count} of your characters visiting.");
    }

    private async Task ComeHomeAsync()
    {
        if (_visitors.SelectedIndex < 0 || _visitors.SelectedIndex >= _visiting.Count)
            throw new PassportException("pick", "List your visitors, then pick the one to bring home. Log out of your friend's world first.");
        RefuseWhileServerRuns();
        VisitorInfo visitor = _visiting[_visitors.SelectedIndex];
        using PassportDeskClient desk = Desk();
        IReadOnlyList<string> notes = await PassportTravel.ComeHomeAsync(Home, visitor, desk, PassportTravel.DefaultFolder);
        Say($"{visitor.HomeName} came home with everything it earned.");
        foreach (string note in notes)
            Say(note);
        LoadCharacters();
        await ListVisitorsAsync();
    }

    private Task RecallAsync()
    {
        LocalCharacter character = SelectedCharacter() ?? throw new PassportException("pick", "Pick the character to recall.");
        if (character.AwayAt == null)
            throw new PassportException("not_away", $"{character.Name} is at home.");
        RefuseWhileServerRuns();
        DialogResult answer = MessageBox.Show(this,
            $"Recall {character.Name} without your friend's world?\n\n" +
            $"{character.Name} comes back exactly as it left, with the coin it carried. Everything it earned at {character.AwayAt} is lost, " +
            "and that trip can never come home.\n\nUse this only if your friend's world is gone. If you can still reach it, use COME HOME.",
            "Recall a character", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return Task.CompletedTask;
        PassportTravel.Recall(Home, character);
        Say($"{character.Name} is home again, as it left.");
        LoadCharacters();
        return Task.CompletedTask;
    }

    private async Task RunAsync(Func<Task> work)
    {
        foreach (Button button in _actions)
            button.Enabled = false;
        UseWaitCursor = true;
        try
        {
            await work();
        }
        catch (PassportException e)
        {
            Say(e.Message);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Say($"Your friend's world did not answer ({e.Message}). Check the address and that it is hosting, then try again.");
        }
        catch (Exception e) when (e is IOException or System.Data.SQLite.SQLiteException or InvalidDataException)
        {
            Say($"That did not work, and nothing was lost: {e.Message}");
        }
        finally
        {
            UseWaitCursor = false;
            foreach (Button button in _actions)
                button.Enabled = true;
        }
    }

    private void Say(string line) =>
        _log.AppendText($"{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}  {line}{Environment.NewLine}");
}
