using System.Diagnostics;
using System.Net;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Client-only mode: start this game's client against a friend's server, from their invite code or address.
/// It never starts the local server or opens the local save. Ported from stefanrows/OfflineDAoC (Join Friend); hand port
/// from rambodolphin13/DividedRealms af1fcf8 (before the Character passport), Living Server 1.2. Original client only.
/// Two password boxes (owner, 2026-11-08): the host password, needed the first time only, and the friend's own
/// password, the only one their account keeps (JoinFriend.TryLoginPassword, HostPassword.Combine).
/// </summary>
internal sealed class JoinFriendForm : Form
{
    private readonly string _logsDirectory;
    private readonly JoinClient _client;
    private readonly TextBox _hostAddress = InputBox(64);
    private readonly TextBox _guestAccount = InputBox(20);
    private readonly TextBox _hostPassword = InputBox(20, password: true);
    private readonly TextBox _password = InputBox(20, password: true);
    private readonly CheckBox _rememberPassword = new()
    {
        Text = "Remember my password (encrypted for this Windows user)",
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 8f),
        BackColor = Color.Transparent,
    };
    private readonly Label _status = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.GoldLight,
        Font = new Font("Georgia", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Button _joinButton = new RuneButton
    {
        Text = "JOIN FRIEND",
        Width = 170,
        Height = 34,
        Accent = Color.FromArgb(104, 111, 77),
        Margin = new Padding(4),
    };
    private readonly Button _closeButton = new RuneButton
    {
        Text = "CLOSE",
        Width = 110,
        Height = 34,
        Accent = DaocTheme.Iron,
        Margin = new Padding(4),
        DialogResult = DialogResult.Cancel,
    };

    public JoinFriendForm(string runtime)
    {
        _client = JoinClient.For(runtime);
        _logsDirectory = Path.Combine(runtime, "logs");

        Text = "Join a Friend — Offline DAoC Living Server";
        ClientSize = new Size(640, 560);
        MinimumSize = new Size(600, 540);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = DaocTheme.Void;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 8.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var frame = new StoneSurface { Dock = DockStyle.Fill, Padding = new Padding(14) };
        Controls.Add(frame);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(4),
            BackColor = Color.Transparent,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        for (int index = 0; index < 4; index++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

        layout.Controls.Add(new Label
        {
            Text = "JOIN A FRIEND'S WORLD",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.GoldLight,
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Plays in your friend's world with an account on their server. " +
                   "Your own server and save are not touched.",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 9f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 1);
        layout.Controls.Add(Field("Invite code from your friend (or their address)", _hostAddress), 0, 2);
        layout.Controls.Add(Field("Your own account name on the host (made on your first login there; not \"offline\")", _guestAccount), 0, 3);
        layout.Controls.Add(Field("Host password from your friend (first time only; it makes your account)", _hostPassword), 0, 4);
        layout.Controls.Add(Field($"Your own password (1-{DOL.GS.PacketHandler.Client.v168.HostPassword.MaxOwnPasswordLength} characters; only you know it)", _password), 0, 5);
        layout.Controls.Add(_rememberPassword, 0, 6);

        var connectionNote = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(2, 4, 2, 2),
        };
        connectionNote.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connectionNote.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        connectionNote.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        connectionNote.Controls.Add(new Label
        {
            Text = $"The host presses START HOSTING in HOST FOR FRIENDS. You both need the same game and Living Server " +
                   $"version. This client logs in on port {_client.LoginPort}.",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.Muted,
            Font = new Font("Georgia", 8f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        connectionNote.Controls.Add(_status, 0, 1);
        layout.Controls.Add(connectionNote, 0, 7);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_joinButton);
        layout.Controls.Add(buttons, 0, 8);
        frame.Controls.Add(layout);

        _joinButton.Click += async (_, _) => await JoinAsync();
        _closeButton.Click += (_, _) => Close();
        AcceptButton = _joinButton;
        CancelButton = _closeButton;
        _status.Text = "Paste your friend's invite code and choose an account name.";
        LoadSavedProfile();
        if (_guestAccount.Text.Length == 0)
            _guestAccount.Text = JoinFriend.SuggestAccountName(Environment.UserName, Random.Shared);
    }

    /// <summary>For tests: the two password boxes.</summary>
    internal (TextBox Host, TextBox Own) PasswordBoxes => (_hostPassword, _password);

    private void LoadSavedProfile()
    {
        JoinFriendProfile saved = JoinFriendProfile.Load(JoinFriendProfile.DefaultPath);
        _hostAddress.Text = saved.HostAddress;
        _guestAccount.Text = saved.GuestAccount;
        _password.Text = saved.Password;
        _rememberPassword.Checked = saved.RememberPassword;
        if (saved.GuestAccount.Length > 0)
            _status.Text = saved.RememberPassword
                ? "Saved details loaded. Press JOIN FRIEND to connect."
                : "Saved details loaded. Enter your password to connect.";
    }

    private void SaveProfile(string target, string guestAccount, string password)
    {
        try
        {
            JoinFriendProfile.Save(JoinFriendProfile.DefaultPath, target, guestAccount, password, _rememberPassword.Checked);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Joining matters more than remembering; the next session asks again.
        }
    }

    private async Task JoinAsync()
    {
        if (!JoinFriend.TryParseTarget(_hostAddress.Text, _client.LoginPort, out IReadOnlyList<IPAddress> hosts, out string error))
        {
            SetStatus(error, DaocTheme.Danger);
            return;
        }
        string guestAccount = _guestAccount.Text.Trim();
        string accountProblem = JoinFriend.AccountProblem(guestAccount, Environment.UserName, Random.Shared);
        if (accountProblem.Length > 0)
        {
            SetStatus(accountProblem, DaocTheme.Danger);
            return;
        }
        string password = _password.Text;
        if (!JoinFriend.TryLoginPassword(_hostPassword.Text, password, out string sentPassword, out string passwordProblem))
        {
            SetStatus(passwordProblem, DaocTheme.Danger);
            return;
        }
        if (!_client.IsInstalled)
        {
            SetStatus("The game client is missing. Restore connect.exe and game.dll in the client folder.", DaocTheme.Danger);
            return;
        }

        _joinButton.Enabled = false;
        SetStatus("Looking for your friend's server…", DaocTheme.GoldLight);
        IPAddress? hostAddress = await JoinFriend.FindReachableAsync(hosts, _client.LoginPort, TimeSpan.FromSeconds(4));
        if (IsDisposed || Disposing)
            return;
        if (hostAddress == null)
        {
            SetStatus(
                "Cannot reach your friend's server. Check it has finished starting and they are hosting, and ask for a new code " +
                "if their internet address may have changed.",
                DaocTheme.Danger);
            _joinButton.Enabled = true;
            return;
        }

        try
        {
            ProcessStartInfo start = _client.CreateStartInfo(hostAddress, guestAccount, sentPassword);
            ClientDisplayPreferences.EnsureBorderlessFullscreen(_client.ClientDirectory);
            ClientSessionDiagnostics.Prepare(_logsDirectory);
            Process? client = Process.Start(start);
            if (client == null)
            {
                SetStatus("Windows did not start the game client. Check the client installation and try again.", DaocTheme.Danger);
                _joinButton.Enabled = true;
                return;
            }

            ClientSessionDiagnostics.Start(client, _client.ClientDirectory, _logsDirectory, hostAddress);
            SaveProfile(_hostAddress.Text.Trim(), guestAccount, password);
            // Only the own password is remembered; the host password is needed once.
            _password.Clear();
            _hostPassword.Clear();
            _hostPassword.Enabled = false;
            _hostAddress.Enabled = false;
            _guestAccount.Enabled = false;
            _rememberPassword.Enabled = false;
            _joinButton.Text = "CLIENT STARTED";
            SetStatus("Client started. Keep this window open while you play; close it when you finish.", DaocTheme.Success);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not start the game client: {exception.Message}", DaocTheme.Danger);
            _joinButton.Enabled = true;
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private static TextBox InputBox(int maxLength, bool password = false) => new()
    {
        Dock = DockStyle.Bottom,
        BackColor = DaocTheme.PanelAlt,
        ForeColor = DaocTheme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Georgia", 9f),
        MaxLength = maxLength,
        UseSystemPasswordChar = password,
        Margin = new Padding(0, 3, 0, 0),
    };

    private static Control Field(string caption, TextBox input)
    {
        var field = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(2, 2, 2, 3) };
        field.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 8f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        field.Controls.Add(input);
        return field;
    }
}
