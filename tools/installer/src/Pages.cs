using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LivingServerSetup
{
    /// <summary>The page flow and what the player chose.</summary>
    internal sealed class Wizard
    {
        readonly SetupForm form;
        public List<GameInfo> Games = new List<GameInfo>();
        public bool Scanned;
        public GameInfo Chosen;           // null: a new install
        public InstallOptions Options;
        public InstallResult Last;

        public Wizard(SetupForm form) { this.form = form; }

        public void Welcome() => form.Show(new WelcomePage(form));
        public void Choose() => form.Show(new ChoosePage(form));
        public void Prepare() => form.Show(new OptionsPage(form));
        public void Install(InstallOptions o) { Options = o; form.Show(new InstallPage(form, o)); }
        public void Finished(InstallResult r) { Last = r; form.Show(new DonePage(form, r)); }

        public static string BuildLabel(string build)
        {
            if (string.IsNullOrEmpty(build)) return "an earlier build";
            if (build == Payload.BuildName) return "Living Server " + Payload.DisplayName;
            if (build.Contains("-se.") || build.Contains("chris")) return "the Social Expansion (" + build + ")";
            if (build.Contains("beta")) return "Living Server " + build;
            return "build " + build;
        }

        public static string DefaultFolder()
        {
            string parent = Directory.Exists("C:\\") ? Path.Combine("C:\\", "Games", "OfflineDAoC")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "OfflineDAoC");
            return FreeFolder(Path.Combine(parent, "playable-v" + Payload.BaseRelease + "b"));
        }

        /// <summary>The folder itself when it's free, else name-2, name-3...: a new install never proposes a game you
        /// already have (walkthrough 2026-10-28: it offered the existing playable-v0.37b).</summary>
        public static string FreeFolder(string path)
        {
            bool Taken(string p) { try { return Directory.Exists(p) && Directory.EnumerateFileSystemEntries(p).Any(); } catch { return true; } }
            if (!Taken(path)) return path;
            for (int n = 2; n < 100; n++)
                if (!Taken(path + "-" + n)) return path + "-" + n;
            return path;
        }
    }

    // ==================================================================================================== welcome

    internal sealed class WelcomePage : Page
    {
        public WelcomePage(SetupForm form) : base(form)
        {
            var begin = Put(new GoldButton("BEGIN", true), SetupForm.W - 48 - 176, 474, 176, 46);
            begin.Click += (s, e) => form.Wizard.Choose();
            var guide = Put(new LinkText("What is Living Server?") { FontSize = 9.5f }, SetupForm.W - 48 - 176, 528, 176, 22);
            guide.Click += (s, e) => Program.OpenUrl(Program.ProjectUrl);
        }

        protected override bool IsDragArea(Point p) => true;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.DrawCover(g, Theme.Img("hero.jpg"), ClientRectangle);
            var shade = R(0, 400, SetupForm.W, 200);
            using (var lb = Theme.VGrad(new Rectangle(shade.X, shade.Y - 1, shade.Width, shade.Height + 2), Color.FromArgb(0, 8, 6, 4), Color.FromArgb(235, 8, 6, 4)))
                g.FillRectangle(lb, shade);
            Theme.DrawShadowText(g, "Your single-player Camelot, alive like a 2003 server.", Theme.Serif(14f, FontStyle.Italic), R(48, 462, 600, 30), Theme.Parchment, TextFormatFlags.Left | TextFormatFlags.Top);
            Theme.DrawShadowText(g,
                "Setup installs everything you need: Offline DAoC by shadowofze, and the Living Server add-on on top. " +
                "Already playing Offline DAoC? It finds your game and adds Living Server, and your characters stay.",
                Theme.Serif(9.75f), R(49, 496, 600, 48), Theme.Text);
            Theme.DrawText(g, "Living Server " + Payload.DisplayName + " for Offline DAoC " + Payload.FitsLabel +
                "  ·  a free fan project, not affiliated with Electronic Arts or Broadsword", Theme.Serif(7.75f), R(49, 572, 760, 18), Theme.Faint,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // ==================================================================================================== choose

    internal sealed class ChoosePage : FramedPage
    {
        readonly Panel list = new DoubleBufferedPanel { AutoScroll = true, BackColor = Color.Transparent };
        readonly LinkText browse = new LinkText("Choose another game folder...");
        string scanning = "Looking for Offline DAoC on this PC...";
        ChoiceCard selected;

        public ChoosePage(SetupForm form) : base(form, "Choose your path", "Install the game with Living Server, or add Living Server to a game you already have.", 0)
        {
            Back.Click += (s, e) => form.Wizard.Welcome();
            Next.Click += (s, e) => Go();
            Next.Enabled = false;
            Put(list, 40, 184, 880, 344);
            browse.FitWidth();
            browse.Location = new Point(Theme.S(920) - browse.Width, Theme.S(155));
            browse.Height = Theme.S(22);
            Controls.Add(browse);
            browse.Click += (s, e) => BrowseForGame();
        }

        public override void Entered()
        {
            var wz = Form.Wizard;
            if (wz.Scanned) { Fill(); return; }
            var exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Manifests.TryRefresh();   // first: the new client's update fingerprint comes from it
                var games = new List<GameInfo>();
                try
                {
                    foreach (var p in GameScanner.FindGames(exeDir))
                    {
                        try { games.Add(GameScanner.Inspect(p)); }
                        catch (Exception ex) { Log.Write("Couldn't read " + p + ": " + ex.Message); }
                    }
                }
                catch (Exception ex) { Log.Write("Search: " + ex.Message); }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        wz.Games = games.OrderByDescending(Rank).ThenBy(g => g.Path).ToList();
                        wz.Scanned = true;
                        Fill();
                    }));
                }
                catch { }
            });
            Invalidate();
        }

        static int Rank(GameInfo g) =>
            g.IsBase && (g.Ls != LsState.ThisBuild || g.ClientBehind) ? 4 : g.IsBase ? 3 : g.IsUpdatable ? 2 : 0;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Form.Wizard.Scanned)
                Theme.DrawText(e.Graphics, scanning, Theme.Serif(10f, FontStyle.Italic), R(40, 200, 880, 24), Theme.Muted);
        }

        void Fill()
        {
            scanning = null;
            list.SuspendLayout();
            list.Controls.Clear();
            var wz = Form.Wizard;
            int y = 0, w = list.ClientSize.Width - Theme.S(wz.Games.Count > 4 ? 18 : 2);
            ChoiceCard pick = null;
            foreach (var g in wz.Games)
            {
                var card = GameCard(g);
                card.Width = w;
                card.Height = card.PreferredHeight(w);
                card.Location = new Point(0, y);
                list.Controls.Add(card);
                y += card.Height + Theme.S(10);
                if (pick == null && card.Enabled) pick = card;
            }
            var fresh = new ChoiceCard
            {
                Title = "New install",
                Body = "Download Offline DAoC " + Payload.BaseRelease + " from shadowofze's official release into a new folder, and add Living Server. " +
                       "About " + Theme.Gb(Manifests.BaseGame + Manifests.GamePatch) + " to download (" +
                       Theme.Gb(Manifests.BaseGame + Manifests.GamePatch + Manifests.NewClient + Manifests.ClientPatch) + " with the new client).",
                Stripe = Theme.Hibernia,
                Badge = wz.Games.Any(g => g.IsBase || g.IsUpdatable) ? "NEW GAME" : "RECOMMENDED",
                BadgeColor = wz.Games.Any(g => g.IsBase || g.IsUpdatable) ? Theme.Muted : Theme.Success,
            };
            if (wz.Games.Any(g => !g.IsBase && !g.IsUpdatable && g.CompareToBase() < 0))
                fresh.Lines.Add(("It can bring the characters of an older game along.", Theme.Muted));
            fresh.Width = w;
            fresh.Height = fresh.PreferredHeight(w);
            fresh.Location = new Point(0, y);
            fresh.Chosen += (s, e) => Select(fresh);
            list.Controls.Add(fresh);
            list.ResumeLayout();
            if (wz.Chosen != null)
                pick = list.Controls.OfType<ChoiceCard>().FirstOrDefault(c => (c.Tag2 as GameInfo)?.Path == wz.Chosen.Path) ?? pick;
            else if (wz.Options != null && wz.Options.NewInstall) pick = fresh;
            Select(pick ?? fresh);
            Invalidate();
        }

        ChoiceCard GameCard(GameInfo g)
        {
            var c = new ChoiceCard { Title = g.Title, Sub = g.Path, Tag2 = g, Stripe = Theme.Midgard };
            var dn = "Living Server " + Payload.DisplayName;
            if (g.Release == null)
            {
                c.Lines.Add(("Setup can't tell which version this is (it has no PACKAGE MANIFEST.sha256).", Theme.Danger));
                c.Enabled = false; c.Stripe = Theme.Iron;
            }
            else if (g.CompareToBase() > 0)
            {
                c.Lines.Add(("Newer than this Living Server build, which is made for " + Payload.FitsLabel + ". Look for a newer Living Server release.", Theme.Danger));
                c.Enabled = false; c.Stripe = Theme.Iron;
            }
            else if (g.IsBase)
            {
                switch (g.Ls)
                {
                    case LsState.None when g.ClientBehind:
                    case LsState.OtherBuild when g.ClientBehind:
                        c.Lines.Add(("Its new client missed the " + Payload.BaseRelease + " update: Setup applies the official client update, then adds " + dn + ".", Theme.GoldLight));
                        c.Badge = "UPDATE"; c.BadgeColor = Theme.Warning; break;
                    case LsState.None:
                        c.Lines.Add(("Adds " + dn + ". Your characters, bots and settings stay.", Theme.Success));
                        c.Badge = "ADD LIVING SERVER"; c.BadgeColor = Theme.Success; break;
                    case LsState.OtherBuild:
                        // Same name, another build (a test build, or a rebuilt release): "upgrades Beta 1.1 to Beta 1.1" read wrong.
                        c.Lines.Add((g.LsBuild == Payload.BuildName
                            ? "Has another " + dn + " build. Setup replaces it with this one (" + Payload.ShortCommit + "); everything you have stays."
                            : "Has " + Wizard.BuildLabel(g.LsBuild) + ". Setup upgrades it to " + dn + "; everything you have stays.", Theme.Success));
                        c.Badge = "UPGRADE"; c.BadgeColor = Theme.Success; break;
                    case LsState.ThisBuild when !g.ClientBehind:
                        c.Lines.Add((dn + " is installed. Choose this to check it, add the new client, or remove Living Server.", Theme.Muted));
                        c.Badge = "UP TO DATE"; c.BadgeColor = Theme.Gold; break;
                    case LsState.ThisBuild:
                        c.Lines.Add((dn + " is installed, but the new client missed its " + Payload.BaseRelease + " update. Setup applies the official client update, then checks Living Server.", Theme.GoldLight));
                        c.Badge = "UPDATE CLIENT"; c.BadgeColor = Theme.Warning; break;
                    default:
                        c.Lines.Add(("Its server files were changed by hand. Setup can replace them (they are backed up first).", Theme.Warning));
                        c.Badge = "CHANGED"; c.BadgeColor = Theme.Warning; break;
                }
            }
            else if (g.IsUpdatable)
            {
                c.Lines.Add(("Setup first updates it to " + Payload.BaseRelease + " with shadowofze's official update (characters, bots and settings stay), then adds Living Server.", Theme.GoldLight));
                if (g.Ls == LsState.OtherBuild) c.Lines.Add(("Its " + Wizard.BuildLabel(g.LsBuild) + " build comes off for the update; Living Server replaces it.", Theme.Muted));
                if (g.Ls == LsState.Modified || g.Ls == LsState.Unknown) c.Lines.Add(("Its server or launcher files were changed by hand: Setup asks before it replaces them.", Theme.Warning));
                c.Badge = "UPDATE"; c.BadgeColor = Theme.Warning;
            }
            else
            {
                c.Lines.Add(("Too old to update in place. Choose New install: Setup can bring these characters into the new game.", Theme.Muted));
                c.Enabled = false; c.Stripe = Theme.Iron;
            }
            if (g.Unfinished) c.Lines.Add(("The new client's install didn't finish in this folder.", Theme.Warning));
            if (c.Enabled) c.Chosen += (s, e) => Select(c);
            return c;
        }

        void Select(ChoiceCard c)
        {
            if (selected != null) selected.Selected = false;
            selected = c;
            if (c != null) { c.Selected = true; list.ScrollControlIntoView(c); }
            Next.Enabled = c != null;
        }

        void Go()
        {
            if (selected == null) return;
            Form.Wizard.Chosen = selected.Tag2 as GameInfo;
            Form.Wizard.Prepare();
        }

        void BrowseForGame()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose your Offline DAoC game folder (the one with START OFFLINE DAOC.cmd in it).", ShowNewFolderButton = false })
            {
                if (d.ShowDialog(Form) != DialogResult.OK) return;
                var path = d.SelectedPath;
                if (!GameScanner.IsGame(path))
                {
                    Dialog.Show(Form, "That isn't an Offline DAoC game folder", "There's no runtime\\server\\GameServer.dll in " + path + ".\n\nChoose the folder that has START OFFLINE DAOC.cmd in it.", "OK");
                    return;
                }
                var wz = Form.Wizard;
                var g = GameScanner.Inspect(path);
                wz.Games.RemoveAll(x => string.Equals(x.Path, g.Path, StringComparison.OrdinalIgnoreCase));
                wz.Games.Insert(0, g);
                wz.Chosen = g;
                Fill();
            }
        }
    }

    internal sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor, true);
        }
    }

    // ==================================================================================================== prepare

    internal sealed class OptionsPage : FramedPage
    {
        readonly GameInfo game;
        readonly PathBox folder = new PathBox();
        OptionToggle editionB, editionClassic, client, shortcuts, netfx, import, keep, remove, recommended;
        GameInfo importFrom;
        List<StepItem> plan = new List<StepItem>();
        string download = "", need = "", free = "";
        bool spaceOk = true;
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 400 };

        public OptionsPage(SetupForm form) : base(form, "", "", 1)
        {
            game = form.Wizard.Chosen;
            Back.Click += (s, e) => form.Wizard.Choose();
            Next.Click += (s, e) => Go();
            float y = 192;
            var prev = form.Wizard.Options;
            if (game == null)
            {
                Title = "Prepare your new game";
                Subtitle = "Everything comes from shadowofze's official release and is checked as it downloads.";
                folder.Text = prev?.NewInstall == true ? prev.Destination : Wizard.DefaultFolder();
                Put(folder, 40, y + 18, 386, 34);
                var browse = Put(new GoldButton("BROWSE"), 434, y + 18, 92, 34);
                browse.Click += (s, e) => PickFolder();
                folder.Box.TextChanged += (s, e) => Changed();
                y += 84;
                float y0 = y;
                editionB = Toggle(new OptionToggle(Payload.BaseRelease + "b + Sluaghbinder",
                    "Adds shadowofze's custom Hibernia class.", true, "ed"), ref y, 236);
                float y1 = y;
                y = y0;
                editionClassic = Toggle(new OptionToggle(Payload.BaseRelease + " original classes",
                    "Classic and Shrouded Isles only.", true, "ed"), ref y, 236);
                editionClassic.Left = Theme.S(40 + 250);
                y = Math.Max(y, y1);
                editionB.Checked = !(prev?.Classic ?? false);
                editionClassic.Checked = prev?.Classic ?? false;
                y += 10;
                client = Toggle(new OptionToggle("Add the new client (beta)",
                    "shadowofze's modern client for the same world: " + Theme.Gb(Manifests.NewClient + Manifests.ClientPatch) + " more to download."), ref y, 486);
                client.Checked = prev?.NewClient ?? false;
                importFrom = form.Wizard.Games.Where(g => g.Release != null && g.CompareToBase() < 0 && !g.IsUpdatable).OrderByDescending(g => new Version(g.Release)).FirstOrDefault();
                if (importFrom != null)
                {
                    import = Toggle(new OptionToggle("Bring my characters from " + importFrom.Title,
                        "Account, characters, items, houses and bots. That folder isn't changed."), ref y, 486);
                    import.Checked = true;
                }
            }
            else
            {
                Title = game.Ls == LsState.ThisBuild && game.Ready ? "Living Server is already here" : game.IsBase ? "Add Living Server to your game" : "Update your game and add Living Server";
                Subtitle = game.Title + "  ·  " + game.Path;
                if (game.Ls == LsState.ThisBuild && game.Ready)
                {
                    keep = Toggle(new OptionToggle("Keep Living Server and check it",
                        "Checks every Living Server file and repairs anything missing. Nothing changes if all is well.", true, "act"), ref y, 486);
                    remove = Toggle(new OptionToggle("Remove Living Server",
                        "Puts the released server and launcher back from Living Server's own backups. Your characters stay; its settings stay in the database, unused.", true, "act"), ref y, 486);
                    keep.Checked = true;
                    y += 10;
                }
                else if (!game.Ready)
                {
                    var note = (game.IsBase ? "The new client gets its " + Payload.BaseRelease + " update" : "Offline DAoC " + game.Edition + " is updated to " + Payload.BaseRelease) +
                               " with shadowofze's official updater first. It backs up everything it replaces, and your account, characters, bots and settings stay.";
                    var lbl = Put(new NoteLabel(note), 40, y, 486, 52);
                    y += 62;
                }
                if (!game.NewClient)
                {
                    client = Toggle(new OptionToggle("Add the new client (beta)",
                        "shadowofze's modern client for the same world, next to the original one. " + Theme.Gb(Manifests.NewClient + Manifests.ClientPatch) + " more to download."), ref y, 486);
                    client.Checked = prev != null && !prev.NewInstall && prev.Game?.Path == game.Path && prev.AddClient;
                }
            }
            // Owner 2026-10-28: a first install comes with everything on at our suggested settings, not off until found in a tab.
            // A game that already has Living Server keeps its own choices unless this is ticked.
            recommended = Toggle(new OptionToggle("Turn on every Living Server feature",
                "At our suggested settings: bot guilds, personalities, voices, all of server life and XP by level. Change any of them later in the launcher's Living Server tab."), ref y, 486);
            recommended.Checked = prev != null && (prev.NewInstall ? game == null : prev.Game?.Path == game?.Path)
                ? prev.RecommendedSettings : game == null || game.Ls == LsState.None;
            shortcuts = Toggle(new OptionToggle("Add desktop and Start menu shortcuts"), ref y, 486);
            shortcuts.Checked = prev?.Shortcuts ?? true;
            if (!NetFx35.IsOn())
            {
                netfx = Toggle(new OptionToggle("Turn on .NET Framework 3.5",
                    "The original game client needs it. Windows asks your permission."), ref y, 486);
                netfx.Checked = true;
            }
            refresh.Tick += (s, e) => { refresh.Stop(); Recalc(); };
            Recalc();
        }

        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }

        OptionToggle Toggle(OptionToggle t, ref float y, float w)
        {
            Put(t, 40, y, w);
            t.Height = t.PreferredHeight(t.Width);
            y += t.Height / Theme.Scale + 10;
            t.CheckedChanged += (s, e) => Changed();
            return t;
        }

        void Changed() { refresh.Stop(); refresh.Start(); }

        void PickFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose where the game goes. Setup makes a new folder there.", ShowNewFolderButton = true })
            {
                if (d.ShowDialog(Form) != DialogResult.OK) return;
                var p = d.SelectedPath;
                if (GameScanner.IsGame(p) || Directory.Exists(p) && Directory.EnumerateFileSystemEntries(p).Any())
                    p = Wizard.FreeFolder(Path.Combine(p, "playable-v" + Payload.BaseRelease + (editionClassic?.Checked == true ? "" : "b")));
                folder.Text = p;
            }
        }

        InstallOptions Build()
        {
            var o = new InstallOptions { Shortcuts = shortcuts.Checked, EnableNetFx = netfx?.Checked == true, RecommendedSettings = recommended?.Checked == true };
            if (game == null)
            {
                o.NewInstall = true;
                o.Destination = folder.Text.Trim().Trim('"');
                o.Classic = editionClassic.Checked;
                o.NewClient = client.Checked;
                o.ImportFrom = import?.Checked == true ? importFrom : null;
            }
            else
            {
                o.Game = game;
                o.AddClient = client?.Checked == true;
                o.Remove = remove?.Checked == true;
                if (o.Remove) { o.Shortcuts = false; o.EnableNetFx = false; o.AddClient = false; o.RecommendedSettings = false; }
                if (game.Ls == LsState.Modified) o.ForceLivingServer = false;
            }
            return o;
        }

        void Recalc()
        {
            if (shortcuts != null) shortcuts.Enabled = remove?.Checked != true;
            if (client != null && game != null) client.Enabled = remove?.Checked != true;
            if (netfx != null) netfx.Enabled = remove?.Checked != true;
            InstallOptions o;
            try
            {
                o = Build();
                if (o.NewInstall)
                {
                    if (string.IsNullOrWhiteSpace(o.Destination) || !Path.IsPathRooted(o.Destination)) throw new ArgumentException("Choose a full folder path, like C:\\Games\\OfflineDAoC.");
                    Path.GetFullPath(o.Destination);
                }
                var eng = new Engine(o);
                plan = eng.Items.Where(i => i.Title != "Check your PC").ToList();
                long dl = 0;
                if (o.NewInstall) { dl = Manifests.BaseGame + Manifests.GamePatch + (o.NewClient ? Manifests.NewClient + Manifests.ClientPatch : 0); }
                else if (!o.Remove)
                {
                    if (!game.IsBase) dl += Manifests.GamePatch;
                    if (game.NewClient && (!game.IsBase || game.ClientBehind)) dl += Manifests.ClientPatch;
                    if (o.AddClient && !game.NewClient) dl += Manifests.NewClient + Manifests.ClientPatch;
                }
                download = dl > 0 ? Theme.Gb(dl) : "nothing";
                var n = eng.NeededBytes();
                need = Theme.Gb(n);
                var root = Path.GetPathRoot(o.NewInstall ? Path.GetFullPath(o.Destination) : game.Path);
                try
                {
                    var f = new DriveInfo(root).AvailableFreeSpace;
                    free = Theme.Gb(f) + " free on " + root.TrimEnd('\\');
                    spaceOk = f >= n;
                }
                catch { free = "free space unknown"; spaceOk = true; }
                Next.Enabled = true;
            }
            catch (Exception e)
            {
                plan = new List<StepItem> { new StepItem { Title = e.Message, State = StepState.Failed } };
                download = need = free = "";
                Next.Enabled = false;
            }
            Next.Text = remove?.Checked == true ? "REMOVE" : keep?.Checked == true && client?.Checked != true ? "CHECK" : game != null && !game.Ready ? "UPDATE & INSTALL" : "INSTALL";
            Next.Width = Theme.S(Next.Text.Length > 8 ? 196 : 168);
            Next.Left = Theme.S(SetupForm.W - 40) - Next.Width;
            Back.Left = Next.Left - Theme.S(12) - Back.Width;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (game == null)
            {
                Theme.DrawText(g, "INSTALL TO", Theme.Serif(8f, FontStyle.Bold), R(41, 192, 300, 16), Theme.Gold);
                Theme.DrawText(g, "EDITION", Theme.Serif(8f, FontStyle.Bold), new Rectangle(Theme.S(41), editionB.Top - Theme.S(20), Theme.S(300), Theme.S(16)), Theme.Gold);
            }
            var panel = R(556, 186, 364, 338);
            Theme.DrawPanel(g, panel);
            Theme.DrawText(g, "WHAT SETUP WILL DO", Theme.Serif(8f, FontStyle.Bold), R(576, 202, 320, 16), Theme.Gold);
            int y = Theme.S(226);
            int n = 1;
            foreach (var step in plan)
            {
                var col = step.State == StepState.Failed ? Theme.Danger : Theme.Text;
                var label = step.State == StepState.Failed ? step.Title : n++ + ".  " + step.Title;
                var tf = Theme.Serif(9.25f, step.State == StepState.Failed ? FontStyle.Italic : FontStyle.Regular);
                var sz = Theme.Measure(label, tf, Theme.S(324));
                Theme.DrawText(g, label, tf, new Rectangle(Theme.S(576), y, Theme.S(324), sz.Height), col);
                y += sz.Height + Theme.S(5);
                if (y > Theme.S(430)) break;
            }
            if (!string.IsNullOrEmpty(need))
            {
                Theme.Rule(g, Theme.S(576), Theme.S(900), Theme.S(448), 120);
                var lf = Theme.Serif(8.75f); var vf = Theme.Serif(8.75f, FontStyle.Bold);
                Row(g, "Download", download, 458, lf, vf, Theme.Text);
                Row(g, "Disk space needed", "about " + need, 478, lf, vf, Theme.Text);
                Row(g, "On this drive", free, 498, lf, vf, spaceOk ? Theme.Success : Theme.Danger);
            }
        }

        void Row(Graphics g, string k, string v, float y, Font lf, Font vf, Color vc)
        {
            Theme.DrawText(g, k, lf, R(576, y, 160, 18), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.SingleLine);
            Theme.DrawText(g, v, vf, R(700, y, 200, 18), vc, TextFormatFlags.Right | TextFormatFlags.SingleLine);
        }

        void Go()
        {
            var o = Build();
            if (!spaceOk && Dialog.Show(Form, "Not enough free space", "This drive looks too full for the install (about " + need + " is needed, " + free + ").\n\nInstall anyway?", "Go back", "Install anyway") != 1)
                return;
            if (o.NewInstall && Directory.Exists(o.Destination) && Directory.EnumerateFileSystemEntries(o.Destination).Any())
            {
                // the engine refuses a folder with someone else's files; an unfinished install of ours continues
            }
            if (o.Remove && Dialog.Show(Form, "Remove Living Server?", "Setup puts the released Offline DAoC server and launcher back. Your characters and bots stay.\n\nYou can install Living Server again any time with this Setup.", "Cancel", "Remove") != 1)
                return;
            Form.Wizard.Install(o);
        }
    }

    internal sealed class NoteLabel : Control
    {
        public NoteLabel(string text)
        {
            Text = text;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var r = ClientRectangle;
            using (var b = new SolidBrush(Color.FromArgb(150, Theme.Warning))) e.Graphics.FillRectangle(b, 0, 0, Theme.S(3), r.Height);
            Theme.DrawText(e.Graphics, Text, Theme.Serif(9.25f), new Rectangle(Theme.S(12), 0, r.Width - Theme.S(12), r.Height), Theme.Text);
        }
    }

    // ==================================================================================================== install

    internal sealed class InstallPage : FramedPage
    {
        readonly Engine engine;
        readonly StepList steps = new StepList();
        readonly GoldProgress stepBar = new GoldProgress { Fill = Theme.Gold };
        readonly GoldProgress overall = new GoldProgress { Fill = Theme.Gold };
        readonly LinkText details = new LinkText("Show details");
        readonly TextBox log = new TextBox();
        readonly System.Windows.Forms.Timer ui = new System.Windows.Forms.Timer { Interval = 200 };
        readonly List<string> pending = new List<string>();
        Thread worker;
        bool done;

        public InstallPage(SetupForm form, InstallOptions o) : base(form, o.Remove ? "Removing Living Server" : "Forging your realm", "", 2)
        {
            engine = new Engine(o);
            Subtitle = o.NewInstall ? "Into " + o.Destination : o.Game.Title + "  ·  " + o.Game.Path;
            Back.Visible = false;
            Next.Text = "STOP";
            Next.Primary = false;
            Next.Width = Theme.S(132);
            Next.Left = Theme.S(SetupForm.W - 40 - 132);
            Next.Enabled = false;
            Next.Click += (s, e) => AskStop();
            steps.Items.AddRange(engine.Items);
            Put(steps, 40, 190, 440, Math.Min(330, steps.Items.Count * 44));
            Put(stepBar, 532, 352, 368, 10);
            Put(overall, 532, 420, 368, 24);
            Put(details, 532, 490, 160, 22).Click += (s, e) => ToggleLog();
            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.WordWrap = false;
            log.BackColor = Color.FromArgb(14, 12, 10); log.ForeColor = Theme.Muted; log.BorderStyle = BorderStyle.None;
            log.Font = Theme.Mono(8.25f);
            log.Visible = false;
            Put(log, 40, 186, 880, 290);
            Log.Written += OnLog;
            engine.Changed += () => { };
            ui.Tick += (s, e) => Pull();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Log.Written -= OnLog; ui.Dispose(); }
            base.Dispose(disposing);
        }

        void OnLog(string line) { lock (pending) pending.Add(line); }

        public override void Entered()
        {
            ui.Start();
            if (Program.DemoMode) { engine.DemoState(); return; }
            worker = new Thread(() =>
            {
                var r = engine.Run();
                try { BeginInvoke((Action)(() => Complete(r))); } catch { }
            }) { IsBackground = true, Name = "install" };
            worker.Start();
        }

        void Pull()
        {
            steps.Invalidate();
            if (engine.StepFraction.HasValue) { stepBar.Indeterminate = false; stepBar.Value = engine.StepFraction.Value; }
            else stepBar.Indeterminate = !done;
            overall.Value = engine.Overall;
            Next.Enabled = engine.CanCancel;
            List<string> lines;
            lock (pending) { lines = pending.ToList(); pending.Clear(); }
            if (lines.Count > 0 && log.Visible) { log.AppendText(string.Join(Environment.NewLine, lines) + Environment.NewLine); }
            Invalidate(R(512, 186, 408, 338));   // the whole status panel, its stop hint included
        }

        void ToggleLog()
        {
            log.Visible = !log.Visible;
            details.Text = log.Visible ? "Hide details" : "Show details";
            if (log.Visible)
            {
                try { log.Text = File.Exists(Log.FilePath) ? File.ReadAllText(Log.FilePath) : ""; } catch { }
                log.SelectionStart = log.TextLength; log.ScrollToCaret();
                log.BringToFront();
                details.Location = new Point(Theme.S(40), Theme.S(490));
            }
            else details.Location = new Point(Theme.S(532), Theme.S(490));
            details.BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (log.Visible) return;
            var g = e.Graphics;
            Theme.DrawPanel(g, R(512, 186, 408, 338));
            var status = string.IsNullOrEmpty(engine.Status) ? "Getting ready..." : engine.Status;
            Theme.DrawText(g, "NOW", Theme.Serif(8f, FontStyle.Bold), R(532, 204, 200, 16), Theme.Gold);
            Theme.DrawText(g, status, Theme.Serif(13f, FontStyle.Bold), R(532, 224, 368, 60), Theme.GoldBright);
            Theme.DrawText(g, engine.Detail ?? "", Theme.Serif(9.25f, FontStyle.Italic), R(532, 300, 368, 44), Theme.Muted);
            Theme.DrawText(g, "OVERALL", Theme.Serif(8f, FontStyle.Bold), R(532, 396, 200, 16), Theme.Gold);
            Theme.DrawText(g, ((int)Math.Floor(engine.Overall * 100)).ToString() + "%", Theme.Serif(10f, FontStyle.Bold), R(700, 393, 200, 20), Theme.GoldLight, TextFormatFlags.Right | TextFormatFlags.SingleLine);
            Theme.DrawText(g, engine.CanCancel ? "You can stop now: finished downloads are kept, and Setup continues where it stopped." :
                "Leave this window open until it's done.", Theme.Serif(8.5f), R(532, 452, 368, 34), Theme.Faint);
        }

        void AskStop()
        {
            if (!engine.CanCancel) return;
            if (Dialog.Show(Form, "Stop installing?", "Finished downloads are kept. Run Setup again and choose the same options to continue where it stopped.", "Keep going", "Stop") == 1)
                engine.Cancel();
        }

        public override bool CanClose()
        {
            if (done) return true;
            if (engine.CanCancel)
            {
                if (Dialog.Show(Form, "Stop installing?", "Finished downloads are kept. Run Setup again and choose the same options to continue where it stopped.", "Keep going", "Stop and close") == 1)
                {
                    engine.Cancel();
                    worker?.Join(15000);
                    return true;
                }
                return false;
            }
            Dialog.Show(Form, "Almost there", "Setup is in the middle of a step that mustn't be interrupted (unpacking or updating game files). Closing now could leave the game half-changed. It won't be long.", "OK");
            return false;
        }

        void Complete(InstallResult r)
        {
            done = true;
            ui.Stop();
            Pull();
            Form.Wizard.Finished(r);
        }
    }

    // ==================================================================================================== done

    internal sealed class DonePage : FramedPage
    {
        readonly InstallResult r;
        readonly string edition;
        OptionToggle clean;

        public DonePage(SetupForm form, InstallResult result) : base(form, "", "", 3)
        {
            r = result;
            var o = form.Wizard.Options;
            try { edition = Program.DemoMode ? Payload.BaseRelease + "b" : r.Outcome == Outcome.Success && !o.Remove ? GameScanner.Inspect(r.GamePath).Edition : ""; } catch { edition = Payload.BaseRelease; }
            Back.Visible = false;
            if (r.Outcome == Outcome.Success)
            {
                Title = o.Remove ? "Living Server is off" : "The realm awaits.";
                Subtitle = o.Remove ? "The released Offline DAoC server and launcher are back in " + r.GamePath + "."
                    : "Offline DAoC " + edition + " with Living Server " + Payload.DisplayName + " is ready in " + r.GamePath + ".";
                Next.Text = "CLOSE";
                Next.Primary = false;
                Next.Width = Theme.S(132); Next.Left = Theme.S(SetupForm.W - 40 - 132);
                Next.Click += (s, e) => { Cleanup(); form.Close(); };
                if (!o.Remove)
                {
                    var play = Put(new GoldButton("PLAY NOW", true), 560, 426, 176, 46);
                    play.Click += (s, e) => { Cleanup(); Play(); form.Close(); };
                    Put(new GoldButton("OPEN FOLDER"), 744, 426, 136, 46).Click += (s, e) => Program.Open(r.GamePath);
                    Put(new LinkText("Getting started guide"), 562, 480, 200, 22).Click += (s, e) => Program.OpenUrl(Program.ProjectUrl + "#getting-started");
                }
                if (r.CacheBytes > 64L << 20 && (Directory.Exists(r.CacheDir) || Program.DemoMode))
                {
                    clean = Put(new OptionToggle("Delete the downloaded setup files (" + Theme.Gb(r.CacheBytes) + ")",
                        "They were only needed to install. Keep them to reinstall without downloading."), 40, 476, 470);
                    clean.Height = clean.PreferredHeight(clean.Width);
                    clean.Checked = true;
                }
            }
            else
            {
                Title = r.Outcome == Outcome.Cancelled ? "Stopped" : "Setup didn't finish";
                Subtitle = r.Outcome == Outcome.Cancelled ? "Nothing is lost: run it again to continue." : "Nothing is lost. Here's what happened.";
                Next.Text = "TRY AGAIN";
                Next.Click += (s, e) => form.Wizard.Install(Retry(o, r));
                Back.Visible = true;
                Back.Text = "CLOSE";
                Back.Click += (s, e) => form.Close();
                float bx = 40, by = 190 + FailPanelHeight() / Theme.Scale + 18;
                if (r.CanForce)
                {
                    var b = Put(new GoldButton("REPLACE ANYWAY", true) { Accent = Theme.Albion }, bx, by, 200, 42);
                    b.Click += (s, e) => { var x = Retry(o, r); x.ForceLivingServer = true; form.Wizard.Install(x); };
                    bx += 212;
                }
                if (r.CanChangeFolder)
                {
                    var b = Put(new GoldButton("CHOOSE ANOTHER FOLDER", true), bx, by, 240, 42);
                    b.Click += (s, e) => form.Wizard.Prepare();
                    bx += 252;
                }
                if (r.CanFreshCopy)
                {
                    var b = Put(new GoldButton("FRESH COPY + MY CHARACTERS", true), bx, by, 280, 42);
                    b.Click += (s, e) => { var x = Retry(o, r); x.FreshCopyIfChanged = true; form.Wizard.Install(x); };
                    bx += 292;
                }
                Put(new GoldButton("SHOW LOG"), bx, by, 132, 42).Click += (s, e) => Program.Open(Log.FilePath);
            }
        }

        string HintText => r.Hint ?? (r.Outcome == Outcome.Failed ? "Click Try again. If it keeps happening, the log (Show log) has the full story." : "");

        int FailPanelHeight()
        {
            var mh = Theme.Measure(r.Message ?? "", Theme.Serif(11.5f, FontStyle.Bold), Theme.S(820)).Height;
            var hh = Theme.Measure(HintText, Theme.Serif(9.75f), Theme.S(820)).Height;
            return Math.Min(Theme.S(300), Theme.S(20) + mh + Theme.S(12) + hh + Theme.S(46));
        }

        /// <summary>The same choices again, aimed at where the game is now: a fresh copy made last time is the game from then on.</summary>
        static InstallOptions Retry(InstallOptions o, InstallResult r)
        {
            var x = (InstallOptions)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(o, null);
            if (!x.NewInstall)
            {
                var path = r.GamePath != null && GameScanner.IsGame(r.GamePath) ? r.GamePath : x.Game.Path;
                if (!string.Equals(path, x.Game.Path, StringComparison.OrdinalIgnoreCase)) x.FreshCopyIfChanged = false;
                x.Game = GameScanner.Inspect(path);
            }
            return x;
        }

        void Play()
        {
            var cmd = Path.Combine(r.GamePath, "START OFFLINE DAOC.cmd");
            if (r.NewClient && File.Exists(Path.Combine(r.GamePath, "START OFFLINE DAOC - NEW CLIENT.cmd")) &&
                Dialog.Show(Form, "Which client?", "Your game has both clients. They play the same account, characters and bots.", "Original client", "New client") == 1)
                cmd = Path.Combine(r.GamePath, "START OFFLINE DAOC - NEW CLIENT.cmd");
            try { Process.Start(new ProcessStartInfo(cmd) { WorkingDirectory = r.GamePath, UseShellExecute = true }); }
            catch (Exception e) { Log.Write("Play: " + e.Message); Program.Open(r.GamePath); }
        }

        void Cleanup()
        {
            if (clean?.Checked != true) return;
            try
            {
                var dir = r.CacheDir;
                // only the downloader's own cache, never anything else
                if (Path.GetFileName(dir) == ".downloads" && Path.GetFileName(Path.GetDirectoryName(dir)) == "Offline DAoC setup files")
                {
                    Directory.Delete(dir, true);
                    Log.Write("Deleted the downloaded setup files in " + dir);
                    // and the "Offline DAoC setup files" folder itself once nothing else is in it. Setup's own copy of the
                    // downloader goes too (each run writes it again); anything else keeps the folder (walkthrough 2026-10-28).
                    var parent = Path.GetDirectoryName(dir);
                    var downloader = Path.Combine(parent, "Get-OfflineDAoC.ps1");
                    if (File.Exists(downloader) && Directory.GetFileSystemEntries(parent).Length == 1)
                        File.Delete(downloader);
                    if (Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
                    {
                        Directory.Delete(parent);
                        Log.Write("Removed the empty folder " + parent);
                    }
                }
            }
            catch (Exception e) { Log.Write("Couldn't delete the setup files: " + e.Message); }
        }

        protected override void PaintBody(Graphics g)
        {
            if (r.Outcome != Outcome.Success) { base.PaintBody(g); return; }
            // the party on the road, from the welcome art, darkened under the panels
            var body = R(0, 110, SetupForm.W, 428);
            var clip = g.Clip;
            g.SetClip(body);
            var hero = Theme.Img("hero.jpg");
            // only the lower half of the art (the party), so the welcome page's lettering stays out
            Theme.DrawCover(g, hero, body, 0.25f, 0.5f, new RectangleF(0, hero.Height * 0.52f, hero.Width, hero.Height * 0.48f));
            using (var b = Theme.VGrad(body, Color.FromArgb(150, 10, 8, 6), Color.FromArgb(215, 10, 8, 6))) g.FillRectangle(b, body);
            g.Clip = clip;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var o = Form.Wizard.Options;
            if (r.Outcome == Outcome.Success)
            {
                if (!o.Remove)
                {
                    var panel = R(40, 186, 480, 278);
                    Theme.DrawPanel(g, panel, alpha: 215);
                    Theme.DrawText(g, "YOUR FIRST NIGHT IN CAMELOT", Theme.Serif(8f, FontStyle.Bold), R(60, 202, 440, 16), Theme.Gold);
                    var tips = new[]
                    {
                        ("Start the server", "PLAY NOW opens the launcher. Click START SERVER, wait for RUNNING, then ENTER REALM. Your account is made the first time."),
                        r.SettingsApplied
                            ? ("Living Server is on", "Every feature is on at our suggested settings. To change one, stop the server and open the launcher's Living Server tab.")
                            : ("Switch Living Server on", "With the server stopped, open the launcher's Living Server tab and turn on bot voices, personalities, guilds and server life."),
                        ("Fill the realms", "Use + Lv.1 and + Lv.50 on each realm's card. Raids need level 50s, and a realm needs 8 bots of level 5 or higher to form a guild."),
                    };
                    int y = Theme.S(226);
                    int i = 1;
                    foreach (var (h, t) in tips)
                    {
                        var num = R(60, 0, 24, 24); num.Y = y;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var b = Theme.VGrad(num, Theme.GoldBright, Theme.GoldDeep)) g.FillEllipse(b, num);
                        TextRenderer.DrawText(g, i++.ToString(), Theme.Serif(9.5f, FontStyle.Bold), num, Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        Theme.DrawText(g, h, Theme.Serif(10f, FontStyle.Bold), new Rectangle(Theme.S(96), y, Theme.S(404), Theme.S(18)), Theme.GoldLight, TextFormatFlags.Left | TextFormatFlags.SingleLine);
                        var tf = Theme.Serif(8.75f);
                        var th = Theme.Measure(t, tf, Theme.S(404)).Height;
                        Theme.DrawText(g, t, tf, new Rectangle(Theme.S(96), y + Theme.S(20), Theme.S(404), th), Theme.Text);
                        y += Theme.S(20) + th + Theme.S(12);
                    }
                    var warn = r.NetFxNeedsRestart ? "Restart Windows before you play: it finished turning on .NET Framework 3.5."
                        : r.NetFxSkipped ? ".NET Framework 3.5 is still off, and the original client needs it. READ ME FIRST.txt in the game folder shows how to turn it on."
                        : null;
                    var right = R(556, 186, 364, 226);
                    Theme.DrawPanel(g, right, alpha: 215);
                    Theme.DrawText(g, "GOOD TO KNOW", Theme.Serif(8f, FontStyle.Bold), R(576, 202, 320, 16), Theme.Gold);
                    var notes = new List<string>
                    {
                        "Undo any time: ROLLBACK.cmd in the " + Payload.RootFolder + " folder puts the files Living Server replaced back.",
                        "Updates to Offline DAoC come from shadowofze's downloader; run this Setup (or a newer one) afterwards to add Living Server again.",
                    };
                    if (r.Note != null) notes.Add(r.Note);
                    if (warn != null) notes.Insert(0, warn);
                    int ny = Theme.S(226);
                    for (int k = 0; k < notes.Count; k++)
                    {
                        var nf = Theme.Serif(8.75f, k == 0 && warn != null ? FontStyle.Bold : FontStyle.Regular);
                        var nh = Theme.Measure(notes[k], nf, Theme.S(310)).Height;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        Theme.Diamond(g, new Point(Theme.S(582), ny + Theme.S(7)), Theme.S(3), k == 0 && warn != null ? Theme.Warning : Theme.Gold);
                        Theme.DrawText(g, notes[k], nf, new Rectangle(Theme.S(592), ny, Theme.S(310), nh), k == 0 && warn != null ? Theme.Warning : Theme.Text);
                        ny += nh + Theme.S(10);
                    }
                }
            }
            else
            {
                var ph = FailPanelHeight();
                var panel = new Rectangle(Theme.S(40), Theme.S(190), Theme.S(880), ph);
                Theme.DrawPanel(g, panel);
                using (var b = new SolidBrush(r.Outcome == Outcome.Cancelled ? Theme.Warning : Theme.Danger)) g.FillRectangle(b, panel.X + Theme.S(4), panel.Y + Theme.S(4), Theme.S(4), panel.Height - Theme.S(8));
                var mf = Theme.Serif(11.5f, FontStyle.Bold);
                var mh = Theme.Measure(r.Message ?? "", mf, Theme.S(820)).Height;
                Theme.DrawText(g, r.Message ?? "", mf, new Rectangle(Theme.S(68), Theme.S(210), Theme.S(820), mh), r.Outcome == Outcome.Cancelled ? Theme.GoldLight : Color.FromArgb(240, 170, 160));
                var hh = Theme.Measure(HintText, Theme.Serif(9.75f), Theme.S(820)).Height;
                Theme.DrawText(g, HintText, Theme.Serif(9.75f), new Rectangle(Theme.S(68), Theme.S(222) + mh, Theme.S(820), hh), Theme.Text);
                Theme.DrawText(g, "Log: " + Log.FilePath, Theme.Serif(8f, FontStyle.Italic), new Rectangle(Theme.S(68), panel.Bottom - Theme.S(28), Theme.S(820), Theme.S(18)), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis);
            }
        }
    }

    // ==================================================================================================== dialog

    /// <summary>A small themed message box. Returns the index of the button pressed (or -1 when closed).</summary>
    internal sealed class Dialog : Form
    {
        int choice = -1;
        readonly string title, text;

        Dialog(string title, string text, string[] buttons)
        {
            this.title = title; this.text = text;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Theme.Panel;
            DoubleBuffered = true;
            var w = Theme.S(440);
            var th = Theme.Measure(text, Theme.Serif(9.75f), w - Theme.S(48)).Height;
            ClientSize = new Size(w, Theme.S(84) + th + Theme.S(64));
            int x = w - Theme.S(24);
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                int idx = i;
                var b = new GoldButton(buttons[i], i == buttons.Length - 1) { Height = Theme.S(36) };
                b.Width = Math.Max(Theme.S(104), TextRenderer.MeasureText(buttons[i], Theme.Serif(10.5f, FontStyle.Bold)).Width + Theme.S(36));
                x -= b.Width;
                b.Location = new Point(x, ClientSize.Height - Theme.S(54));
                b.Click += (s, e) => { choice = idx; Close(); };
                Controls.Add(b);
                if (i == buttons.Length - 1) AcceptButton = b;
                x -= Theme.S(10);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.FillStone(g, ClientRectangle);
            using (var pen = new Pen(Theme.Gold, Theme.S(1.5f))) g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            using (var pen = new Pen(Color.FromArgb(70, Theme.Gold))) g.DrawRectangle(pen, Theme.S(5), Theme.S(5), Width - Theme.S(11), Height - Theme.S(11));
            Theme.DrawText(g, title, Theme.Serif(13f, FontStyle.Bold), new Rectangle(Theme.S(24), Theme.S(22), Width - Theme.S(48), Theme.S(28)), Theme.GoldBright, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Theme.Rule(g, Theme.S(24), Width - Theme.S(24), Theme.S(58), 150);
            Theme.DrawText(g, text, Theme.Serif(9.75f), new Rectangle(Theme.S(24), Theme.S(72), Width - Theme.S(48), Height - Theme.S(140)), Theme.Text);
        }

        public static int Show(IWin32Window owner, string title, string text, params string[] buttons)
        {
            if (Program.Silent) { Log.Write("[dialog] " + title + ": " + text.Replace("\n", " ")); return buttons.Length - 1; }
            using (var d = new Dialog(title, text, buttons))
            {
                try { d.Icon = Theme.AppIcon(); } catch { }
                d.ShowDialog(owner);
                return d.choice;
            }
        }
    }
}
