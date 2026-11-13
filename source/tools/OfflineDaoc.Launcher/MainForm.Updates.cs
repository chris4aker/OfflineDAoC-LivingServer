using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher;

// Living Server updates in the launcher's window (the work itself is in Updates/LauncherUpdater.cs):
// - beside the version, the Living Server build, a "Check for updates" link, the "Check automatically" setting and
//   a line for news (a newer build and the first line of its release notes, the download, the last update's result);
// - when a newer build is out, START SERVER reads GET UPDATE until the player decides: Install, Later or Skip this
//   version;
// - Install only with the server, the game and any other launcher closed; the launcher then hands over to the
//   package's INSTALL.cmd and closes, and the update step opens it again. Nothing installs without the player's click.
internal sealed partial class MainForm
{
    private static readonly Color StartAccent = Color.FromArgb(110, 128, 70);
    private LauncherUpdater? _updater;
    private HttpReleaseWeb? _releaseWeb;
    private ReleaseInfo? _updateOffer;
    private bool _updating;
    private readonly FlowLayoutPanel _updateStrip = new()
    {
        AutoSize = true,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
    };
    private readonly Label _updateBuild = new()
    {
        AutoSize = true,
        ForeColor = Color.FromArgb(196, 145, 70),
        Font = new Font("Georgia", 8.5f, FontStyle.Bold),
        Margin = new Padding(0, 2, 8, 0),
    };
    private readonly LinkLabel _checkUpdatesLink = new()
    {
        AutoSize = true,
        Text = "Check for updates",
        Font = new Font("Georgia", 8.5f),
        LinkColor = DaocTheme.GoldLight,
        ActiveLinkColor = DaocTheme.Parchment,
        VisitedLinkColor = DaocTheme.GoldLight,
        LinkBehavior = LinkBehavior.HoverUnderline,
        Margin = new Padding(0, 2, 8, 0),
    };
    private readonly CheckBox _autoUpdateCheck = new()
    {
        AutoSize = true,
        Text = "Check automatically",
        ForeColor = DaocTheme.Muted,
        Font = new Font("Georgia", 8f),
        Margin = new Padding(0, 1, 8, 0),
    };
    private readonly ProgressBar _updateProgress = new() { Width = 110, Height = 12, Visible = false, Margin = new Padding(0, 4, 8, 0) };
    private readonly Label _updateStatus = new()
    {
        AutoSize = false,
        AutoEllipsis = true,
        ForeColor = DaocTheme.Muted,
        Font = new Font("Georgia", 8.5f, FontStyle.Italic),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private LauncherUpdater Updater => _updater ??= new LauncherUpdater(LauncherUpdater.GameFolderFor(_root),
        _releaseWeb = new HttpReleaseWeb(LauncherUpdater.UserAgent));

    /// <summary>Puts the update line beside the version label (it follows the label when the window scales).</summary>
    private void AddUpdateStrip(Panel titlePanel, Label versionLabel)
    {
        if (LauncherBuild.Version == null)
            return; // a build without a Living Server name has nothing to compare
        _updateBuild.Text = "LIVING SERVER " + LauncherBuild.Version.DisplayName.ToUpperInvariant();
        _updateStrip.Controls.AddRange([_updateBuild, _checkUpdatesLink, _autoUpdateCheck, _updateProgress]);
        titlePanel.Controls.Add(_updateStrip);
        titlePanel.Controls.Add(_updateStatus);
        titlePanel.Layout += (_, _) =>
        {
            int gap = UiScale.Px(titlePanel, 14);
            _updateStrip.Location = new Point(versionLabel.Right + gap, versionLabel.Top + Math.Max(0, (versionLabel.Height - _updateStrip.Height) / 2));
            int left = _updateStrip.Right;
            _updateStatus.SetBounds(left, _updateStrip.Top, Math.Max(0, titlePanel.ClientSize.Width - left - UiScale.Px(titlePanel, 4)), _updateStrip.Height);
        };
        _checkUpdatesLink.LinkClicked += async (_, _) => await CheckForUpdatesAsync(asked: true);
        _autoUpdateCheck.Checked = Updater.Settings.CheckAutomatically;
        _autoUpdateCheck.CheckedChanged += (_, _) =>
        {
            Updater.Settings.CheckAutomatically = _autoUpdateCheck.Checked;
            Updater.SaveSettings();
            Updater.Log.Write("Automatic update checks " + (_autoUpdateCheck.Checked ? "on." : "off."));
        };
        SetToolTip(_autoUpdateCheck, "When the launcher starts, look for a new Living Server on GitHub (at most once a day). " +
                                     "Only the release's version is asked for; nothing about you or your game is sent.");
        ShowLastUpdateResult();
    }

    private void ShowLastUpdateResult()
    {
        var last = Updater.TakeLastResult();
        if (last == null || !last.TryGetValue("Build", out string? build))
            return;
        string name = BuildVersion.Parse(build)?.DisplayName ?? build;
        bool installed = last.TryGetValue("Result", out string? result) && result == "installed";
        Updater.Log.Write($"Opened after the update to {build}: {result} ({(last.TryGetValue("Detail", out string? d) ? d : "")})");
        SetUpdateStatus(installed ? $"Updated to Living Server {name}. Your characters and world are as you left them."
                                  : $"The update to {name} didn't install, and the game is as it was. See runtime\\logs\\launcher-updates.log.",
            installed ? DaocTheme.Success : DaocTheme.Danger);
    }

    private void SetUpdateStatus(string text, Color color, string? tip = null)
    {
        _updateStatus.Text = text;
        _updateStatus.ForeColor = color;
        _helpTip.SetToolTip(_updateStatus, tip ?? text);
    }

    /// <summary>Runs after the window opens (asked: false) and from the link (asked: true). Silent on failure unless asked.</summary>
    private async Task CheckForUpdatesAsync(bool asked)
    {
        BuildVersion? installed = LauncherBuild.Version;
        if (installed == null || _updating)
            return;
        if (!asked && !UpdatePolicy.ShouldCheck(Updater.Settings, DateTime.UtcNow, false))
            return;
        if (asked)
        {
            _checkUpdatesLink.Enabled = false;
            SetUpdateStatus("Asking GitHub for the newest Living Server…", DaocTheme.Muted);
        }
        try
        {
            var (offer, problem) = await Task.Run(() =>
            {
                ReleaseInfo? found = Updater.Check(installed, asked, DateTime.UtcNow, out string? why);
                return (found, why);
            });
            if (offer != null)
            {
                OfferUpdate(offer);
                if (asked)
                    ShowUpdateDialog();
            }
            else if (asked)
            {
                SetUpdateStatus(problem != null
                    ? "Couldn't reach GitHub (" + problem + "). Try again later."
                    : $"You have the newest Living Server ({installed.DisplayName}).", problem != null ? DaocTheme.Danger : DaocTheme.Muted);
            }
        }
        catch (Exception e)
        {
            Updater.Log.Write("Update check failed: " + e.Message);
            if (asked)
                SetUpdateStatus("Couldn't check for updates: " + e.Message, DaocTheme.Danger);
        }
        finally
        {
            _checkUpdatesLink.Enabled = true;
        }
    }

    private void OfferUpdate(ReleaseInfo release)
    {
        _updateOffer = release;
        var notes = release.NotesPreview(6);
        string headline = notes.Count > 0 ? notes[0] : release.Title ?? "";
        SetUpdateStatus($"{release.Version!.DisplayName} is out: {headline}", DaocTheme.GoldLight,
            $"Living Server {release.Version.DisplayName}{Environment.NewLine}{string.Join(Environment.NewLine, notes)}");
        _startButton.Text = "GET UPDATE";
        ((RuneButton)_startButton).Accent = DaocTheme.Gold;
        _helpTip.SetToolTip(_startButton, $"Living Server {release.Version.DisplayName} is out. Click to install it, or choose Later to start the server.");
    }

    private void ClearUpdateOffer()
    {
        _updateOffer = null;
        _startButton.Text = "START SERVER";
        ((RuneButton)_startButton).Accent = StartAccent;
        _helpTip.SetToolTip(_startButton, null);
    }

    /// <summary>START SERVER, or the update dialog while an update is offered.</summary>
    private void OnStartButton()
    {
        if (_updateOffer != null)
            ShowUpdateDialog();
        else
            StartServer();
    }

    private void ShowUpdateDialog()
    {
        ReleaseInfo? release = _updateOffer;
        if (release?.Version == null || _updating)
            return;
        string name = release.Version.DisplayName;
        var install = new TaskDialogButton("Install");
        var later = new TaskDialogButton("Later");
        var skip = new TaskDialogButton("Skip this version");
        var notes = release.NotesPreview(5);
        var page = new TaskDialogPage
        {
            Caption = "Living Server update",
            Heading = $"Update to Living Server {name}? Your characters and world are kept.",
            Text = (notes.Count > 0 ? string.Join(Environment.NewLine, notes) + Environment.NewLine + Environment.NewLine : "") +
                   $"The launcher downloads {release.Package?.Name} ({(release.Package?.Size ?? 0) / 1048576.0:0.0} MB) from GitHub and checks it, " +
                   "then closes while the package's INSTALL.cmd runs in its own window. It backs up every file it replaces " +
                   "(ROLLBACK.cmd undoes it), and the launcher opens again when it's done.",
            Footnote = release.PageUrl != null ? new TaskDialogFootnote(release.PageUrl) : null,
            Buttons = { install, later, skip },
            DefaultButton = install,
            Icon = TaskDialogIcon.Information,
            AllowCancel = true,
        };
        TaskDialogButton chosen = TaskDialog.ShowDialog(this, page);
        if (chosen == install)
        {
            _ = ApplyUpdateAsync(release);
        }
        else if (chosen == skip)
        {
            Updater.Skip(release.Version);
            ClearUpdateOffer();
            SetUpdateStatus($"Skipped {name}. Check for updates shows it again.", DaocTheme.Muted);
        }
        else
        {
            Updater.Log.Write("The player chose Later for " + release.Version.Name + ".");
            ClearUpdateOffer();
            SetUpdateStatus($"{name} is out: Check for updates to install it.", DaocTheme.GoldLight);
        }
    }

    private List<string> RunningGameParts() =>
        LauncherUpdater.RunningParts(Environment.ProcessId, IsServerRunning() || FindExactServerProcess() is not null);

    private async Task ApplyUpdateAsync(ReleaseInfo release)
    {
        string name = release.Version!.DisplayName;
        // Linux (under Wine): there is no Windows PowerShell for apply-update.ps1; the Linux script installs the update.
        if (WineHost.IsWine)
        {
            Updater.Log.Write("Under Wine: the update is installed by the Linux script, not the launcher.");
            MessageBox.Show(this, WineHost.UpdateNote(Updater.GameFolder), "Living Server update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        List<string> running = RunningGameParts();
        if (running.Count > 0)
        {
            Updater.Log.Write("Not updating while running: " + string.Join(", ", running));
            MessageBox.Show(this, $"Close {string.Join(", ", running)} first: Living Server is never updated while the game runs.",
                "Living Server update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _updating = true;
        _startButton.Enabled = _stopButton.Enabled = _playButton.Enabled = _checkUpdatesLink.Enabled = false;
        _updateProgress.Value = 0;
        _updateProgress.Visible = true;
        SetUpdateStatus($"Downloading {name}…", DaocTheme.GoldLight);
        try
        {
            PreparedUpdate prepared = await Task.Run(() => Updater.PrepareFromRelease(release, (done, total) => BeginInvoke(() =>
            {
                if (total > 0)
                    _updateProgress.Value = (int)Math.Min(100, done * 100 / total);
                _updateStatus.Text = total > 0
                    ? $"Downloading {name}: {done / 1048576.0:0.0} of {total / 1048576.0:0.0} MB"
                    : $"Downloading {name}: {done / 1048576.0:0.0} MB";
            }), CancellationToken.None));
            running = RunningGameParts();
            if (running.Count > 0)
                throw new PackageRefusedException($"{string.Join(", ", running)} started during the download. Close it and click GET UPDATE again; the download is kept.");
            SetUpdateStatus($"Installing {name}: the launcher closes now and opens again when it's done.", DaocTheme.GoldLight);
            Updater.StartHandoff(prepared, Environment.ProcessId, Environment.ProcessPath, noPause: false);
            Updater.Log.Write("Closing the launcher for the install.");
            Close();
        }
        catch (Exception e)
        {
            string why = e is PackageRefusedException ? e.Message : "The download didn't finish: " + ReleaseFeed.Innermost(e).Message;
            Updater.Log.Write("Update not applied: " + why);
            SetUpdateStatus($"{name} wasn't installed. Nothing in your game was changed.", DaocTheme.Danger, why);
            MessageBox.Show(this, why + Environment.NewLine + Environment.NewLine + "Nothing in your game was changed.",
                "Living Server update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _updateProgress.Visible = false;
            _updating = false;
            _checkUpdatesLink.Enabled = true;
            await RefreshDashboardAsync();
        }
    }
}
