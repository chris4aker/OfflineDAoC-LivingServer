using DAoCLivingServer.Updates;

namespace DAoCLivingServer.Launcher;

// Living Server updates in the launcher's window (the work itself is in Updates/LauncherUpdater.cs):
// - beside the version label (which names the Living Server build), a "Check for updates" link, the "Check
//   automatically" setting and a line for news (a newer build and the first line of its release notes, the download, the last update's result);
// - when a newer build is out, START SERVER reads GET UPDATE until the player decides: Install, Later or Skip this
//   version;
// - Install only with the server, the game and any other launcher closed; the launcher then hands over to the
//   package's INSTALL.cmd and closes, and the update step opens it again. Nothing installs without the player's click.
// - "Test builds" (off by default, issue #637) also offers release candidates published as GitHub pre-releases, shown
//   as test builds; one can be installed here or into a new test folder beside this install (TestFolderInstall).
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
        // Issue #795: AutoSize alone grows but never shrinks, so the strip kept the panel's default 100 px height. The
        // status line, laid out from it, ended up below the title panel's visible area (only its tooltip showed).
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
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
    private readonly CheckBox _testBuildsCheck = new()
    {
        AutoSize = true,
        Text = "Offer test builds (pre-releases)",
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

    /// <summary>Puts the update controls beside the version label and the update status on its own row under them (both
    /// follow the label when the window scales).</summary>
    private void AddUpdateStrip(Panel titlePanel, Label versionLabel)
    {
        if (LauncherBuild.Version == null)
            return; // a build without a Living Server name has nothing to compare
        // The version label itself names the build now ("LIVING SERVER 1.2.2", MainForm.VersionLabel), so the strip holds
        // only the update controls.
        _updateStrip.Controls.AddRange([_checkUpdatesLink, _autoUpdateCheck, _testBuildsCheck, _updateProgress]);
        titlePanel.Controls.Add(_updateStrip);
        titlePanel.Controls.Add(_updateStatus);
        titlePanel.Layout += (_, _) =>
        {
            int gap = UiScale.Px(titlePanel, 14);
            _updateStrip.Location = new Point(versionLabel.Right + gap, versionLabel.Top + Math.Max(0, (versionLabel.Height - _updateStrip.Height) / 2));
            // Issue #795: the status used to take the strip's height (100 px, see _updateStrip) with its text centred
            // below the title panel's visible area, and only the width left of the strip, so its messages showed only as
            // a hover tooltip. It now has a row of its own under the version label, with the header's width from the
            // label on (the header row in the constructor is 152 px for it). It never ends below the title panel: with
            // some fonts the row under the strip is a pixel or two short, and the status then moves up over the strip's
            // empty bottom edge rather than being cut off (the strip stays in front, so its controls still take clicks).
            int left = versionLabel.Left;
            int below = Math.Max(versionLabel.Bottom, _updateStrip.Bottom);
            int top = Math.Max(versionLabel.Bottom, Math.Min(below, titlePanel.ClientSize.Height - _updateStatus.PreferredHeight));
            _updateStatus.SetBounds(left, top, Math.Max(0, titlePanel.ClientSize.Width - left - UiScale.Px(titlePanel, 4)),
                _updateStatus.PreferredHeight);
        };
        _checkUpdatesLink.LinkClicked += async (_, _) => await CheckForUpdatesAsync(asked: true);
        _autoUpdateCheck.Checked = Updater.Settings.CheckAutomatically;
        _autoUpdateCheck.CheckedChanged += (_, _) =>
        {
            Updater.Settings.CheckAutomatically = _autoUpdateCheck.Checked;
            Updater.SaveSettings();
            Updater.Log.Write("Automatic update checks " + (_autoUpdateCheck.Checked ? "on." : "off."));
        };
        _testBuildsCheck.Checked = Updater.Settings.OfferTestBuilds;
        _testBuildsCheck.CheckedChanged += (_, _) =>
        {
            Updater.Settings.OfferTestBuilds = _testBuildsCheck.Checked;
            Updater.SaveSettings();
            Updater.Log.Write("Test builds (pre-releases) " + (_testBuildsCheck.Checked ? "offered." : "not offered."));
        };
        SetToolTip(_testBuildsCheck, "For testers: also offer test builds, the release candidates published as pre-releases on " +
                                     "GitHub. A test build can go into a new test folder beside this one, so your world here is never touched.");
        SetToolTip(_autoUpdateCheck, "When the launcher starts, look for a new Living Server on GitHub (at most once a day). " +
                                     "Only the release's version is asked for; nothing about you or your game is sent.");
        ShowLastUpdateResult();
    }

    private void ShowLastUpdateResult()
    {
        var last = Updater.TakeLastResult();
        if (last == null || !last.TryGetValue("Build", out string? build))
            return;
        string shown = last.TryGetValue("Release", out string? release) && release.Length > 0 ? release : build;
        string name = BuildVersion.Parse(shown)?.DisplayName ?? shown;
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
        SetTip(_updateStatus, tip ?? text);
    }

    /// <summary>Runs after the window opens (asked: false) and from the link (asked: true). Silent on failure unless asked.</summary>
    private async Task CheckForUpdatesAsync(bool asked)
    {
        BuildVersion? installed = LauncherBuild.Version;
        if (installed == null || _updating)
            return;
        // A test build's binaries name the final's build; the marker says which candidate it is (TestBuildMarker).
        installed = await Task.Run(() => Updater.InstalledBuild(installed)) ?? installed;
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
                ShowNothingOffered(UpdateCheckNotice.NothingOffered(installed, problem, Updater.Settings.OfferTestBuilds));
            }
        }
        catch (Exception e)
        {
            Updater.Log.Write("Update check failed: " + e.Message);
            if (asked)
                ShowNothingOffered(UpdateCheckNotice.CheckFailed(installed, e.Message));
        }
        finally
        {
            _checkUpdatesLink.Enabled = true;
        }
    }

    /// <summary>
    /// An asked check found nothing to offer (issue #795): the status line, plus a message box, so the click visibly
    /// answers. A plain MessageBox, as it renders the same on Windows and under Wine.
    /// </summary>
    private void ShowNothingOffered(UpdateCheckNotice notice)
    {
        SetUpdateStatus(notice.StatusLine, notice.IsProblem ? DaocTheme.Danger : DaocTheme.Muted);
        Updater.Log.Write("Told the player: " + notice.Text.Replace(Environment.NewLine, " "));
        if (IsDisposed || !IsHandleCreated)
            return;
        MessageBox.Show(this, notice.Text, notice.Caption, MessageBoxButtons.OK,
            notice.IsProblem ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private void OfferUpdate(ReleaseInfo release)
    {
        _updateOffer = release;
        var notes = release.NotesPreview(6);
        string headline = notes.Count > 0 ? notes[0] : release.Title ?? "";
        string kind = release.IsTestBuild ? "TEST BUILD (pre-release) " : "";
        SetUpdateStatus($"{kind}{release.Version!.DisplayName} is out: {headline}", release.IsTestBuild ? DaocTheme.Danger : DaocTheme.GoldLight,
            $"Living Server {kind}{release.Version.DisplayName}{Environment.NewLine}{string.Join(Environment.NewLine, notes)}");
        _startButton.Text = release.IsTestBuild ? "GET TEST BUILD" : "GET UPDATE";
        ((RuneButton)_startButton).Accent = DaocTheme.Gold;
        SetTip(_startButton, $"Living Server {release.Version.DisplayName} is out. Click to install it, or choose Later to start the server.");
    }

    // A fixed tip rather than none: removing the tip from the focused button mid-timer is one way to trip the WinForms
    // keyboard tooltip bug (ToolTipFaultFilter).
    private const string StartButtonTip = "Start the game server.";

    private void ClearUpdateOffer()
    {
        _updateOffer = null;
        _startButton.Text = "START SERVER";
        ((RuneButton)_startButton).Accent = StartAccent;
        SetTip(_startButton, StartButtonTip);
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
        bool test = release.IsTestBuild;
        var install = new TaskDialogButton(test ? "Install here" : "Install");
        var testFolder = new TaskDialogButton("Install into a new test folder");
        var later = new TaskDialogButton("Later");
        var skip = new TaskDialogButton("Skip this version");
        var notes = release.NotesPreview(5);
        var page = new TaskDialogPage
        {
            Caption = test ? "Living Server test build" : "Living Server update",
            Heading = test
                ? $"Living Server {name} is a TEST BUILD (a pre-release). Install it into a new test folder, or here?"
                : $"Update to Living Server {name}? Your characters and world are kept.",
            Text = (test ? "A new test folder is a fresh install beside this one, with a new world: this install, its world and " +
                           "characters are not touched. Install here updates this install (ROLLBACK.cmd undoes it)." +
                           Environment.NewLine + Environment.NewLine : "") +
                   (notes.Count > 0 ? string.Join(Environment.NewLine, notes) + Environment.NewLine + Environment.NewLine : "") +
                   $"The launcher downloads {release.Package?.Name} ({(release.Package?.Size ?? 0) / 1048576.0:0.0} MB) from GitHub and checks it, " +
                   "then closes while the package's INSTALL.cmd runs in its own window. It backs up every file it replaces " +
                   "(ROLLBACK.cmd undoes it), and the launcher opens again when it's done.",
            Footnote = release.PageUrl != null ? new TaskDialogFootnote(release.PageUrl) : null,
            DefaultButton = test ? testFolder : install,
            Icon = test ? TaskDialogIcon.Warning : TaskDialogIcon.Information,
            AllowCancel = true,
        };
        if (test)
            page.Buttons.Add(testFolder);
        page.Buttons.Add(install);
        page.Buttons.Add(later);
        page.Buttons.Add(skip);
        TaskDialogButton chosen = TaskDialog.ShowDialog(this, page);
        if (chosen == testFolder)
        {
            _ = InstallIntoTestFolderAsync(release);
        }
        else if (chosen == install)
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

    /// <summary>
    /// "Install into a new test folder" (issue #637): asks for the folder name (beside this install), makes a fresh
    /// install there with a clean world (TestFolderInstall), then hands over to that folder's INSTALL.cmd and, if the
    /// player wants, opens that folder's launcher. This install's world and saves are only read.
    /// </summary>
    private async Task InstallIntoTestFolderAsync(ReleaseInfo release)
    {
        string name = release.Version!.DisplayName;
        string game = Updater.GameFolder;
        string? refused = TestFolderInstall.RunningRefusal(() => ServerPortProbe.IsListening(), RunningGameParts());
        if (refused != null)
        {
            Updater.Log.Write("Test folder not made: " + refused);
            MessageBox.Show(this, refused, "Living Server test build", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? target = AskTestFolder(game);
        if (target == null)
            return;
        _updating = true;
        _startButton.Enabled = _stopButton.Enabled = _playButton.Enabled = _checkUpdatesLink.Enabled = false;
        _updateProgress.Value = 0;
        _updateProgress.Visible = true;
        SetUpdateStatus($"Making the test folder {Path.GetFileName(target)}…", DaocTheme.GoldLight);
        Updater.Log.Write($"Making a test folder for {release.TagName}: {target}");
        try
        {
            var (testUpdater, prepared) = await Task.Run(() => TestFolderInstall.Create(game, target, release, _releaseWeb!,
                () => ServerPortProbe.IsListening(),
                text => BeginInvoke(() => _updateStatus.Text = text),
                (done, total) => BeginInvoke(() =>
                {
                    if (total > 0)
                        _updateProgress.Value = (int)Math.Min(100, done * 100 / total);
                }), CancellationToken.None));
            refused = TestFolderInstall.RunningRefusal(() => ServerPortProbe.IsListening(), RunningGameParts());
            if (refused != null)
                throw new PackageRefusedException(refused + " The test folder is ready; install the test build from its own launcher.");
            // DAoCLivingServer.exe from 1.2.3 (#641); OfflineDAoC.exe in a test folder of an older build.
            string testLauncher = Path.Combine(target, "runtime", "DAoCLivingServer.exe");
            if (!File.Exists(testLauncher)) testLauncher = Path.Combine(target, "runtime", "OfflineDAoC.exe");
            bool open = MessageBox.Show(this,
                $"The test folder {target} is ready with a new world. This launcher now closes while {name} installs there." +
                Environment.NewLine + Environment.NewLine + "Open the test folder's launcher when it's done? (No opens this one again.)",
                "Living Server test build", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            Updater.Log.Write($"Test folder {target} ready; handing over to its INSTALL.cmd, then opening {(open ? testLauncher : "this launcher")}.");
            testUpdater.StartHandoff(prepared, Environment.ProcessId, open && File.Exists(testLauncher) ? testLauncher : Environment.ProcessPath, noPause: false);
            Close();
        }
        catch (Exception e)
        {
            string why = e is PackageRefusedException ? e.Message : "The test folder wasn't made: " + ReleaseFeed.Innermost(e).Message;
            Updater.Log.Write("Test folder not made: " + why);
            SetUpdateStatus($"{name} wasn't installed into a test folder. This install wasn't changed.", DaocTheme.Danger, why);
            MessageBox.Show(this, why + Environment.NewLine + Environment.NewLine + "This install wasn't changed.",
                "Living Server test build", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _updateProgress.Visible = false;
            _updating = false;
            _checkUpdatesLink.Enabled = true;
            await RefreshDashboardAsync();
        }
    }

    /// <summary>The test folder's full path, asked with "&lt;this folder&gt;-test" filled in; null when cancelled.</summary>
    private string? AskTestFolder(string game)
    {
        string parent = Path.GetDirectoryName(game) ?? game;
        using var form = new Form
        {
            Text = "Install into a new test folder",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        var prompt = new Label { AutoSize = true, MaximumSize = new Size(UiScale.Px(this, 460), 0),
            Text = $"A fresh install with a new world, in a new folder in {parent}. This install is not changed. Folder name:" };
        var box = new TextBox { Text = TestFolderInstall.DefaultFolderName(game), Width = UiScale.Px(this, 460) };
        var problem = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(UiScale.Px(this, 460), 0) };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var ok = new Button { Text = "Make the test folder", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([cancel, ok]);
        layout.Controls.AddRange([prompt, box, problem, buttons]);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        string? chosen = null;
        ok.Click += (_, _) =>
        {
            string? why = TestFolderInstall.CheckTarget(game, box.Text, out string full);
            if (why != null)
            {
                problem.Text = why;
                return;
            }
            chosen = full;
            form.DialogResult = DialogResult.OK;
        };
        return form.ShowDialog(this) == DialogResult.OK ? chosen : null;
    }
}
