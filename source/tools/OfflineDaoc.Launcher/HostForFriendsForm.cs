using System.ComponentModel;
using System.Diagnostics;
using System.Net;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The host's side of playing together: one button that opens this server to friends, a password, and the invite
/// code to send them. Starting asks Windows once for the firewall rules and starts the server if it is stopped.
/// Hand port from rambodolphin13/DividedRealms af1fcf8 and b0744a2, Living Server 1.2: the password is always needed
/// (the owner's decision, 2026-11-08), internet mode is off unless the host ticks it, and there is no friend list.
/// </summary>
internal sealed class HostForFriendsForm : Form
{
    private readonly string _runtime;
    private readonly Func<bool> _serverRunning;
    private readonly Action _startServer;
    private readonly Label _state = Line(13f, FontStyle.Bold, DaocTheme.GoldLight);
    private readonly TextBox _password = new()
    {
        Dock = DockStyle.Bottom,
        BackColor = DaocTheme.PanelAlt,
        ForeColor = DaocTheme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Georgia", 9f),
        MaxLength = HostForFriends.MaxHostPasswordLength,
        UseSystemPasswordChar = true,
    };
    private readonly CheckBox _internet = new()
    {
        Text = "Also let friends join over the internet (opens the router while the server runs; off by default)",
        Checked = false,
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 8.5f),
        BackColor = Color.Transparent,
    };
    private readonly Label _router = Line(8.5f, FontStyle.Regular, DaocTheme.Parchment);
    private readonly TextBox _code = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = DaocTheme.PanelAlt,
        ForeColor = DaocTheme.GoldLight,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Consolas", 12f, FontStyle.Bold),
        TextAlign = HorizontalAlignment.Center,
        Margin = new Padding(0, 3, 0, 0),
    };
    private readonly Label _status = Line(8.5f, FontStyle.Regular, DaocTheme.GoldLight);
    private readonly Button _toggle = Action("START HOSTING", Color.FromArgb(110, 128, 70));
    private readonly Button _copy = Action("COPY CODE", Color.FromArgb(92, 84, 128));
    private readonly Button _close = Action("CLOSE", DaocTheme.Iron);
    private HostForFriends.State _current = new(false, 10300, 10400, true);
    private HostForFriends.Settings _settings = new();
    private bool _busy;

    public HostForFriendsForm(string runtime, Func<bool> serverRunning, Action startServer)
    {
        _runtime = runtime;
        _serverRunning = serverRunning;
        _startServer = startServer;

        Text = "Host for Friends — Offline DAoC Living Server";
        ClientSize = new Size(660, 490);
        MinimumSize = new Size(600, 450);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = DaocTheme.Void;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 8.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var frame = new StoneSurface { Dock = DockStyle.Fill, Padding = new Padding(14) };
        Controls.Add(frame);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, BackColor = Color.Transparent, Padding = new Padding(4) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 38f, 28f, 44f, 56f, 26f, 44f, 22f, 36f })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

        layout.Controls.Add(new Label
        {
            Text = "HOST FOR FRIENDS",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.GoldLight,
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        layout.Controls.Add(_state, 0, 1);
        layout.Controls.Add(Note(
            "Press START HOSTING, then send friends the invite code and the password. They paste the code into JOIN FRIEND, " +
            "pick their own account name and use the password. Their characters are saved in YOUR world. They play as players: " +
            "Make Me GM stays with your own account."), 0, 2);

        var passwordField = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(2, 2, 2, 3) };
        passwordField.Controls.Add(new Label
        {
            Text = $"Password for friends ({HostForFriends.MinHostPasswordLength}-{HostForFriends.MaxHostPasswordLength} characters; needed for every new account from another PC)",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 8f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        passwordField.Controls.Add(_password);
        layout.Controls.Add(passwordField, 0, 3);
        layout.Controls.Add(_internet, 0, 4);
        layout.Controls.Add(_router, 0, 5);
        layout.Controls.Add(Note("Invite code:", bold: true), 0, 6);
        layout.Controls.Add(_code, 0, 7);
        layout.Controls.Add(_status, 0, 8);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_copy);
        buttons.Controls.Add(_toggle);
        layout.Controls.Add(buttons, 0, 9);
        frame.Controls.Add(layout);

        _toggle.Click += async (_, _) => await ToggleAsync();
        _copy.Click += (_, _) => CopyCode();
        _close.Click += (_, _) => Close();
        CancelButton = _close;
        Shown += async (_, _) =>
        {
            _settings = HostForFriends.LoadSettings(_runtime);
            await RefreshStateAsync();
        };
    }

    private async Task RefreshStateAsync()
    {
        try
        {
            _current = HostForFriends.Read(_runtime);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not read serverconfig.xml: {exception.Message}", DaocTheme.Danger);
            _toggle.Enabled = false;
            return;
        }
        _state.Text = _current.Hosting ? "You are hosting: friends can join while your server runs." : "Not hosting: only this PC can join your server.";
        _state.ForeColor = _current.Hosting ? DaocTheme.Success : DaocTheme.GoldLight;
        _toggle.Text = _current.Hosting ? "STOP HOSTING" : "START HOSTING";
        _password.Enabled = _internet.Enabled = !_current.Hosting;
        if (_current.Hosting)
            _internet.Checked = _current.Internet;
        _code.Text = string.Empty;
        _copy.Enabled = false;
        _router.ForeColor = DaocTheme.Parchment;
        _router.Text = _current.Hosting
            ? (_current.Internet ? string.Empty : "Home network and Tailscale only. Your router stays closed.")
            : "Only send the code and the password to people you want in your world.";
        if (!_current.Hosting)
            return;

        IPAddress? internet = null;
        if (_current.Internet)
        {
            _router.Text = "Asking your router…";
            HostForFriends.State asked = _current;
            HostForFriends.RouterCheck check = await Task.Run(() => HostForFriends.CheckRouter(asked));
            if (IsDisposed)
                return;
            internet = check.Usable ? check.InternetAddress : HostForFriends.ManualPublicAddress(_runtime);
            _router.Text = internet != null && !check.Usable ? $"Using your own PublicIP setting: {internet}." : check.Message;
            if (internet == null && HostForFriends.DirectInternetAddress() is IPAddress direct)
            {
                internet = direct;
                _router.Text = $"This PC is on the internet directly at {direct} (no router in front), so friends join there.";
            }
            _router.ForeColor = internet != null ? DaocTheme.Success : DaocTheme.Danger;
        }

        IReadOnlyList<IPAddress> addresses = HostForFriends.InviteAddresses(internet);
        if (addresses.Count == 0)
        {
            SetStatus("This PC has no network address friends can use.", DaocTheme.Danger);
            return;
        }
        _code.Text = InviteCode.Create(addresses, _current.LoginPort);
        _copy.Enabled = true;
    }

    private async Task ToggleAsync()
    {
        if (_busy)
            return;
        _busy = true;
        _toggle.Enabled = false;
        try
        {
            if (_current.Hosting)
                StopHosting();
            else
                await StartHostingAsync();
            await RefreshStateAsync();
        }
        finally
        {
            _busy = false;
            _toggle.Enabled = true;
        }
    }

    private void StopHosting()
    {
        try
        {
            HostForFriends.Disable(_runtime);
            string remove = Path.Combine(_runtime, HostForFriends.FirewallRemoveScriptName);
            File.WriteAllText(remove, HostForFriends.FirewallScript(_runtime, _current, add: false));
            SetStatus("Hosting stopped: the server listens on this PC only and the password is gone. The firewall rules are " +
                      $"harmless while it does; to remove them, run {HostForFriends.FirewallRemoveScriptName} as administrator." + RestartNote(),
                DaocTheme.Success);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not change serverconfig.xml: {exception.Message}", DaocTheme.Danger);
        }
    }

    private async Task StartHostingAsync()
    {
        bool internet = _internet.Checked;
        string password = _password.Text;
        if (!HostForFriends.TryCheckHostPassword(password, out string error))
        {
            SetStatus(error, DaocTheme.Danger);
            return;
        }

        if (!await EnsureFirewallAsync(internet))
            return;
        try
        {
            HostForFriends.Enable(_runtime, password, internet);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not change serverconfig.xml: {exception.Message}", DaocTheme.Danger);
            return;
        }
        _password.Clear();

        if (_serverRunning())
        {
            SetStatus("Stop and start the server for hosting to take effect.", DaocTheme.GoldLight);
            return;
        }
        _startServer();
        SetStatus("Hosting is on and your server is starting. Friends can join once it has finished loading.", DaocTheme.Success);
    }

    private string RestartNote() => _serverRunning() ? " Stop and start the server for it to take effect." : string.Empty;

    private async Task<bool> EnsureFirewallAsync(bool internet)
    {
        if (await RulesPresentAsync() && _settings.FirewallOpenToInternet == internet)
            return true;

        string script = Path.Combine(_runtime, HostForFriends.FirewallScriptName);
        SetStatus("Windows will ask for permission to let friends through the firewall…", DaocTheme.GoldLight);
        try
        {
            File.WriteAllText(script, HostForFriends.FirewallScript(_runtime, _current, add: true, internet));
            using Process? process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", script },
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process != null)
                await process.WaitForExitAsync();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            SetStatus("Windows permission was declined, so hosting did not start.", DaocTheme.Danger);
            return false;
        }
        catch (Exception exception)
        {
            SetStatus($"Could not change the firewall: {exception.Message}", DaocTheme.Danger);
            return false;
        }

        if (!await RulesPresentAsync())
        {
            SetStatus($"The firewall did not change. You can run {script} as administrator yourself.", DaocTheme.Danger);
            return false;
        }
        _settings.FirewallOpenToInternet = internet;
        try
        {
            HostForFriends.SaveSettings(_runtime, _settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Only costs one more permission prompt next time.
            SetStatus($"The firewall changed, but the setting could not be saved: {exception.Message}", DaocTheme.GoldLight);
        }
        return true;
    }

    private async Task<bool> RulesPresentAsync()
    {
        try
        {
            foreach (string rule in HostForFriends.FirewallRuleNames(_current))
            {
                using Process? process = Process.Start(new ProcessStartInfo("netsh")
                {
                    ArgumentList = { "advfirewall", "firewall", "show", "rule", $"name={rule}" },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                });
                if (process == null)
                    return false;
                await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private void CopyCode()
    {
        if (_code.Text.Length == 0)
            return;
        Clipboard.SetText(_code.Text);
        SetStatus("Copied. Send the code and the password to your friend for JOIN FRIEND.", DaocTheme.Success);
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private static Label Line(float size, FontStyle style, Color color) => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = color,
        Font = new Font("Georgia", size, style),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static Label Note(string text, bool bold = false) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 8.5f, bold ? FontStyle.Bold : FontStyle.Regular),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static Button Action(string text, Color accent) => new RuneButton
    {
        Text = text,
        Width = 170,
        Height = 34,
        Accent = accent,
        Margin = new Padding(4),
    };
}
