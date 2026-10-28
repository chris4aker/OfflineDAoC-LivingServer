namespace OfflineDaoc.Launcher;

/// <summary>
/// The launcher's Living Server tab: switches for bot guilds, personalities, voices and debates, and the
/// XP-by-level-range rates, saved like the Options tab's settings (server stopped; used at the next start).
/// It replaces the set_bot_guilds.py, set_bot_social.py and set_xp_by_level.py helpers for players.
///
/// It is drawn like the Options tab: inset cards with a gold title, a three-column grid of a bold parchment
/// label or switch (210 px), a control (135 px) and a description, styled inputs, an italic status line, and
/// rune buttons. Sizes are 96-DPI pixels; MainForm's UiScale pass scales the whole tab with the window.
/// </summary>
internal sealed class SocialExpansionSettingsControl : UserControl
{
    private const int LabelColumn = 210, ControlColumn = 135, TitleRow = 48, Row = 40, CardPadding = 20;
    private static readonly Font LabelFont = new("Georgia", 9f, FontStyle.Bold);
    private static readonly Font DescriptionFont = new("Georgia", 9.25f, FontStyle.Bold);
    private static readonly Font TitleFont = new("Georgia", 13f, FontStyle.Bold);
    private static readonly Font InputFont = new("Georgia", 8.25f);
    private static readonly Font StatusFont = new("Georgia", 8.5f, FontStyle.Italic);

    private readonly string _database;
    private readonly Func<bool> _serverStopped;
    private readonly CheckBox _guilds = Switch("Bot guilds");
    private readonly NumericUpDown _share = Number(0, 100, "Share of bots in guilds");
    private readonly NumericUpDown _minLevel = Number(0, 50, "Lowest guild level");
    private readonly CheckBox _guildSocial = Switch("Guild chat", part: true);
    private readonly CheckBox _guildRecruit = Switch("Invites to you", part: true);
    private readonly CheckBox _guildClaims = Switch("Keep claims", part: true);
    private readonly CheckBox _guildEconomy = Switch("Dues and merit", part: true);
    private readonly CheckBox _guildAlliances = Switch("Alliances", part: true);
    private readonly CheckBox _personalities = Switch("Bot personalities");
    private readonly CheckBox _voices = Switch("Bot voices");
    private readonly CheckBox _debates = Switch("/faction debates", part: true);
    private readonly CheckBox _memory = Switch("Bots remember you");
    private readonly CheckBox _grats = Switch("Grats");
    private readonly CheckBox _firsts = Switch("Server firsts");
    private readonly CheckBox _death = Switch("Rezzes and corpse runs");
    private readonly CheckBox _alarms = Switch("Frontier alarms");
    private readonly CheckBox _nemesis = Switch("Nemeses");
    private readonly ComboBox _schedule = Choice("Bot schedule", "Off  (all online)", "Session", "Real clock");
    private readonly CheckBox _breaks = Switch("Breaks and linkdeads");
    private readonly CheckBox _body = Switch("Body language");
    private readonly CheckBox _beggars = Switch("Beggars");
    private readonly CheckBox _deeds = Switch("Deeds and reputation");
    private readonly CheckBox _storylines = Switch("Storylines");
    private readonly CheckBox _characters = Switch("Spammers and roleplayers");
    private readonly CheckBox _rumours = Switch("True rumours");
    private readonly CheckBox _holidays = Switch("Holiday chatter");
    private readonly CheckBox _leeroy = Switch("Leeroy in raids");
    private readonly XpRow _playerXp = new("YOUR CHARACTERS", SocialExpansionSettings.SuggestedPlayerXp, "3× · 5× · 12×");
    private readonly XpRow _botXp = new("AUTONOMOUS BOTS", SocialExpansionSettings.SuggestedBotXp, "1.5× · 2.5× · 6×");
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        Font = StatusFont, ForeColor = DaocTheme.Muted,
    };
    private readonly RuneButton _save = Action("SAVE SETTINGS", DaocTheme.Gold);
    private readonly RuneButton _undo = Action("UNDO EDITS", DaocTheme.Iron);
    private readonly RuneButton _allOff = Action("ALL OFF", DaocTheme.Albion);
    private readonly RuneButton _herald = Action("CAMELOT HERALD", DaocTheme.Hibernia);
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private SocialExpansionSettings _saved = SocialExpansionSettings.Defaults;
    private bool _loading;
    // The saved settings couldn't be read at all: nothing may be saved over them (that would overwrite the
    // player's real values with what the tab happens to show) until a reload succeeds.
    private bool _loadFailed;
    // Rows the server can't read as stored; a save rewrites exactly these, plus whatever the player changed.
    private readonly HashSet<string> _repairKeys = new(StringComparer.Ordinal);
    private bool _lastStopped = true;   // the first UpdateState reads the state; a later stop triggers a reload
    private string _notice = "";
    private bool _noticeIsError;
    // Rows that only work while their feature is on; shown dimmed (not disabled) while it is off.
    private readonly List<(Control Switch, Label Description, Func<SocialExpansionSettings, bool> Active)> _dependent = [];

    /// <summary>The player changed something and hasn't saved it (what MainForm's start guard asks about).</summary>
    public bool HasUnsavedChanges => !_loadFailed && ReadValues() != _saved;

    private bool NeedsSave => !_loadFailed && (ReadValues() != _saved || _repairKeys.Count > 0);

    public SocialExpansionSettingsControl(string database, Func<bool> serverStopped)
    {
        _database = database;
        _serverStopped = serverStopped;
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;

        var guilds = Card("BOT GUILDS", DaocTheme.Gold, 8);
        Add(guilds, _guilds, null, "Bots form realm guilds, wear guild names, group with guildmates, recruit you, and accept your /ginvite.");
        Func<SocialExpansionSettings, bool> guildsOn = v => v.BotGuilds;
        Dependent(Caption("BOTS IN GUILDS"), Add(guilds, null, WithUnit(_share, "%"), "Share of eligible autonomous bots placed in a guild."), guildsOn);
        Dependent(Caption("LOWEST LEVEL"), Add(guilds, null, _minLevel, "Bots below this level stay unguilded."), guildsOn);
        Dependent(_guildSocial, Add(guilds, _guildSocial, null, "Greetings and congratulations in guild chat, answers to your /gu, recruiting in /faction."), guildsOn);
        Dependent(_guildRecruit, Add(guilds, _guildRecruit, null, "Bot officers invite you with a yes/no box when you ask for a guild or are nearby. Needs guild chat."),
            v => v.BotGuilds && v.GuildSocial);
        Dependent(_guildClaims, Add(guilds, _guildClaims, null, "Bot guilds claim the keeps their realm takes and defend them first. Never while you are inside."), guildsOn);
        Dependent(_guildEconomy, Add(guilds, _guildEconomy, null, "Dues from real loot, merit points from level-ups, and 24-hour guild buffs."), guildsOn);
        Dependent(_guildAlliances, Add(guilds, _guildAlliances, null, "Each realm's RvR and raiding bot guilds form one alliance. Your own guild's alliances are never touched."), guildsOn);

        var voices = Card("PERSONALITIES AND VOICES", DaocTheme.Gold, 3);
        Add(voices, _personalities, null, "Leveler, Socialite, Daredevil, Explorer, Crafter or No-lifer, tilting how each bot spends its time. Target a bot and type /personality.");
        Add(voices, _voices, null, "Period chat lines from 2002–2003, chosen by realm, class, level and moment, each bot in its own writing style.");
        Dependent(_debates, Add(voices, _debates, null, "Spec arguments, relic alerts and long threads in /faction, every 30–45 minutes while you play. Needs bot voices."),
            v => v.Voices);

        var living = Card("SERVER LIFE", DaocTheme.Gold, 16);
        Add(living, _memory, null, "Bots of your realm remember grouping with you, your rezzes and rude words: they greet and thank you by name.");
        Add(living, _grats, null, "Dings get grats from the group and bots nearby; a new level 50 gets cheers in /faction.");
        Add(living, _firsts, null, "The first to level 50, to 50 in each class and to each realm rank is announced and goes in the Herald.");
        Add(living, _death, null, "Dead bots ask you for a rez and thank you; a passing bot healer may rez you (you still accept).");
        Add(living, _alarms, null, "A frontier bot that sees 3+ enemies calls size, classes and place in /faction (once per 2 minutes).");
        Add(living, _nemesis, null, "Bots remember who killed them, name the killer in /faction and gloat when they get revenge.");
        Captioned(Caption("BOT SCHEDULE", part: false), Add(living, null, _schedule,
            "Off: all bots online. Session: bots play 1–4 hour sessions. Real clock: busy evenings, quiet nights."));
        Add(living, _breaks, null, "Idle bots go brb for 5–25 minutes; a bot in your group rarely goes linkdead for a minute.");
        Add(living, _body, null, "Idle bots look around, emote, shift their feet in town, wave when you walk up and cheer a ding. No jumping or sitting.");
        Add(living, _beggars, null, "A few Crafter bots beg in their capital and hoard coin. Ask anyone for gold and they follow you, insulting you, until you leave.");
        Add(living, _deeds, null, "Bots that see you solo a named, go on a streak or die to a rat tell /faction. You earn a reputation and a nickname.");
        Add(living, _storylines, null, "Pairs of bots live out dramas over days. Take a side in /faction and the ending follows you.");
        Add(living, _characters, null, "Gold spammers flood /faction until they're banned; a few bots roleplay in thee and thou and get mocked.");
        Add(living, _rumours, null, "Bots pass on true rumours: a rare named that just spawned, an enemy keep with its guards down.");
        Add(living, _holidays, null, "Around Halloween, Christmas, New Year and other holidays on your PC's calendar, bots talk about them and wish you well.");
        Add(living, _leeroy, null, "One end-game raid in twenty has a bot who charges the final boss early, yelling his name. The raid follows him in.");

        var xp = Card("XP BY LEVEL RANGE", DaocTheme.Gold, 5);
        xp = xp with { Height = TitleRow + 2 * (XpRow.Height + XpRow.ErrorHeight) + Row + CardPadding };
        _playerXp.AddTo(xp.Grid);
        _botXp.AddTo(xp.Grid);
        var note = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = StatusFont, ForeColor = DaocTheme.Muted,
            Text = "Suggested rates are for levels 1–19 · 20–34 · 35–50: they keep levels 1–34 worth playing and shorten the 35–50 grind. " +
                   "Inside a range its rate replaces the flat XP on the Options tab; other levels keep it. RvR zones still use the RvR XP rate.",
        };
        xp.Grid.Controls.Add(note, 0, xp.Grid.RowCount - 1);
        xp.Grid.SetColumnSpan(note, 3);

        // The footer (status and buttons) stays at the bottom of the tab while the cards scroll above it.
        var footer = new InsetPanel { Dock = DockStyle.Bottom, Height = 64, Accent = DaocTheme.Gold, Padding = new Padding(CardPadding, 8, CardPadding, 8) };
        var footerGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, BackColor = Color.Transparent, Margin = Padding.Empty };
        buttons.Controls.AddRange([_herald, _undo, _allOff, _save]);
        footerGrid.Controls.Add(_status, 0, 0);
        footerGrid.Controls.Add(buttons, 1, 0);
        footer.Controls.Add(footerGrid);

        // The intro line and the three cards, top to bottom, at fixed 96-DPI heights like the Options card.
        var page = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, BackColor = Color.Transparent };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        (Control Control, int Height)[] parts =
        [
            (Intro(), 40),
            (guilds.Panel, guilds.Height),
            (voices.Panel, voices.Height),
            (living.Panel, living.Height),
            (xp.Panel, xp.Height),
        ];
        page.RowCount = parts.Length;
        int total = 0;
        foreach (var (control, height) in parts)
        {
            int rowHeight = height + control.Margin.Vertical;
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
            page.Controls.Add(control);
            total += rowHeight;
        }
        page.Height = total;
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = DaocTheme.Panel };
        scroller.Controls.Add(page);
        Controls.Add(footer);
        Controls.Add(scroller);
        scroller.BringToFront();   // docked last, so it fills what the footer leaves

        foreach (var box in new[] { _guilds, _guildSocial, _guildRecruit, _guildClaims, _guildEconomy, _guildAlliances, _personalities, _voices, _debates,
                     _memory, _grats, _firsts, _death, _alarms, _nemesis, _breaks, _body, _beggars, _deeds, _storylines, _characters, _rumours, _holidays, _leeroy })
            box.CheckedChanged += (_, _) => Edited();
        _schedule.SelectedIndexChanged += (_, _) => Edited();
        _share.ValueChanged += (_, _) => Edited();
        _minLevel.ValueChanged += (_, _) => Edited();
        _playerXp.Changed += Edited;
        _botXp.Changed += Edited;
        _save.Click += (_, _) => SaveSettings();
        _undo.Click += (_, _) => LoadSettings();
        _herald.Click += async (_, _) => await OpenHeraldAsync();
        _allOff.Click += (_, _) =>
        {
            SetValues(ReadValues().AllOff());
            SetNotice("ALL FEATURES SWITCHED OFF HERE. Click SAVE SETTINGS to apply it.", error: false);
            UpdateState();
        };
        LoadSettings();
        _poll.Tick += (_, _) => UpdateState();
        _poll.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _poll.Dispose();
        base.Dispose(disposing);
    }

    private void Edited()
    {
        if (_loading) return;
        SetNotice("", error: false);
        UpdateState();
    }

    private void SetNotice(string text, bool error)
    {
        _notice = text;
        _noticeIsError = error;
    }

    private SocialExpansionSettings ReadValues() => new()
    {
        BotGuilds = _guilds.Checked,
        GuildSharePercent = (int)_share.Value,
        GuildMinLevel = (int)_minLevel.Value,
        GuildSocial = _guildSocial.Checked,
        GuildRecruitPlayer = _guildRecruit.Checked,
        GuildClaims = _guildClaims.Checked,
        GuildEconomy = _guildEconomy.Checked,
        GuildAlliances = _guildAlliances.Checked,
        Personalities = _personalities.Checked,
        Voices = _voices.Checked,
        Debates = _debates.Checked,
        LivingMemory = _memory.Checked,
        LivingGrats = _grats.Checked,
        LivingFirsts = _firsts.Checked,
        LivingDeath = _death.Checked,
        LivingAlarms = _alarms.Checked,
        LivingNemesis = _nemesis.Checked,
        ScheduleMode = SocialExpansionSettings.ScheduleModes[Math.Max(0, _schedule.SelectedIndex)],
        LivingBreaks = _breaks.Checked,
        LivingBodyLanguage = _body.Checked,
        LivingBeggars = _beggars.Checked,
        LivingDeeds = _deeds.Checked,
        LivingStorylines = _storylines.Checked,
        LivingCharacters = _characters.Checked,
        LivingRumours = _rumours.Checked,
        LivingHolidays = _holidays.Checked,
        LivingLeeroy = _leeroy.Checked,
        PlayerXpByLevel = _playerXp.Value,
        BotXpByLevel = _botXp.Value,
    };

    private void SetValues(SocialExpansionSettings settings)
    {
        _loading = true;
        _guilds.Checked = settings.BotGuilds;
        _share.Value = Math.Clamp(settings.GuildSharePercent, 0, 100);
        _minLevel.Value = Math.Clamp(settings.GuildMinLevel, 0, 50);
        _guildSocial.Checked = settings.GuildSocial;
        _guildRecruit.Checked = settings.GuildRecruitPlayer;
        _guildClaims.Checked = settings.GuildClaims;
        _guildEconomy.Checked = settings.GuildEconomy;
        _guildAlliances.Checked = settings.GuildAlliances;
        _personalities.Checked = settings.Personalities;
        _voices.Checked = settings.Voices;
        _debates.Checked = settings.Debates;
        _memory.Checked = settings.LivingMemory;
        _grats.Checked = settings.LivingGrats;
        _firsts.Checked = settings.LivingFirsts;
        _death.Checked = settings.LivingDeath;
        _alarms.Checked = settings.LivingAlarms;
        _nemesis.Checked = settings.LivingNemesis;
        _schedule.SelectedIndex = Math.Max(0, Array.IndexOf(SocialExpansionSettings.ScheduleModes, settings.ScheduleMode));
        _breaks.Checked = settings.LivingBreaks;
        _body.Checked = settings.LivingBodyLanguage;
        _beggars.Checked = settings.LivingBeggars;
        _deeds.Checked = settings.LivingDeeds;
        _storylines.Checked = settings.LivingStorylines;
        _characters.Checked = settings.LivingCharacters;
        _rumours.Checked = settings.LivingRumours;
        _holidays.Checked = settings.LivingHolidays;
        _leeroy.Checked = settings.LivingLeeroy;
        _playerXp.Value = settings.PlayerXpByLevel;
        _botXp.Value = settings.BotXpByLevel;
        _loading = false;
    }

    private void LoadSettings()
    {
        var problems = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            _saved = SocialExpansionSettings.Load(_database, problems, keys);
            _loadFailed = false;
            _repairKeys.Clear();
            _repairKeys.UnionWith(keys);
            SetNotice(problems.Count == 0 ? "" : string.Join(" ", problems) + " SAVE SETTINGS repairs these rows.", error: problems.Count > 0);
            SetValues(_saved);
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            _repairKeys.Clear();
            _saved = SocialExpansionSettings.Defaults;
            SetValues(_saved);
            SetNotice("CANNOT READ THE SAVED SETTINGS, so nothing can be saved here: " + ex.Message +
                " Close other programs using the game's database, then click UNDO EDITS to read them again.", error: true);
        }
        UpdateState();
    }

    /// <summary>Writes the Camelot Herald from the game database (read-only; fine while the server runs) and opens it.</summary>
    private async Task OpenHeraldAsync()
    {
        _herald.Enabled = false;
        try
        {
            string runtime = Path.GetDirectoryName(Path.GetDirectoryName(_database)!)!;
            string records = Path.Combine(runtime, "server", "realm-event-records.sqlite3");
            string path = await Task.Run(() => HeraldPage.Write(_database, records, Path.Combine(runtime, "herald"), DateTime.Now));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            SetNotice("HERALD WRITTEN: " + path, error: false);
        }
        catch (Exception ex) { SetNotice("THE HERALD COULD NOT BE WRITTEN: " + ex.Message, error: true); }
        finally { _herald.Enabled = true; UpdateState(); }
    }

    private void SaveSettings()
    {
        try
        {
            var values = ReadValues();
            if (_loadFailed) throw new InvalidOperationException("the saved settings couldn't be read, so saving could overwrite them.");
            values.Save(_database, _saved, _serverStopped, _repairKeys);
            _saved = values;
            _repairKeys.Clear();
            SetNotice("SAVED. These settings will be used on the next server startup.", error: false);
        }
        catch (Exception ex) { SetNotice("NOT SAVED: " + ex.Message, error: true); }
        UpdateState();
    }

    public void UpdateState()
    {
        bool stopped = _serverStopped();
        // When the server stops, settings may have been changed elsewhere (a helper script, the other 0.36c
        // launcher): read them again, unless the player has edits here.
        if (stopped && !_lastStopped && !_loading && !HasUnsavedChanges && !_loadFailed)
        {
            _lastStopped = true;
            LoadSettings();
            return;
        }
        _lastStopped = stopped;
        var values = ReadValues();
        string? invalid = values.Validate();
        // Nothing is disabled (disabled WinForms text is drawn embossed and unreadable on this dark theme):
        // inactive parts are dimmed, and saving waits for a stopped server.
        foreach (var (control, description, active) in _dependent)
        {
            Color color = active(values) ? DaocTheme.Parchment : DaocTheme.Muted;
            control.ForeColor = color;
            description.ForeColor = color;
        }
        _playerXp.ShowError(SocialExpansionSettings.XpError("NOT VALID", values.PlayerXpByLevel));
        _botXp.ShowError(SocialExpansionSettings.XpError("NOT VALID", values.BotXpByLevel));
        bool changed = NeedsSave;
        _save.Enabled = stopped && invalid is null && changed;
        _undo.Enabled = changed || _loadFailed;
        _allOff.Enabled = !_loadFailed && !values.IsAllOff;
        if (!stopped)
            Status("SERVER RUNNING. You can make edits, but stop the server to save them." + (_notice == "" ? "" : "  " + _notice), DaocTheme.Muted);
        else if (invalid is not null)
            Status(invalid, DaocTheme.Danger);
        else if (_notice != "")
            Status(_notice, _noticeIsError ? DaocTheme.Danger : DaocTheme.Success);
        else if (changed)
            Status("UNSAVED EDITS. Click SAVE SETTINGS; they will be used on the next server startup.", DaocTheme.GoldLight);
        else
            Status(Summary(_saved), DaocTheme.Muted);
    }

    private void Status(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private static string Summary(SocialExpansionSettings s)
    {
        var on = new List<string>();
        if (s.BotGuilds) on.Add("bot guilds");
        if (s.Personalities) on.Add("personalities");
        if (s.Voices) on.Add(s.Debates ? "voices and debates" : "voices");
        int living = new[] { s.LivingMemory, s.LivingGrats, s.LivingFirsts, s.LivingDeath, s.LivingAlarms, s.LivingNemesis,
            s.ScheduleMode != "off", s.LivingBreaks, s.LivingBodyLanguage, s.LivingBeggars,
            s.LivingDeeds, s.LivingStorylines, s.LivingCharacters, s.LivingRumours, s.LivingHolidays, s.LivingLeeroy }.Count(x => x);
        if (living > 0) on.Add($"Server life ({living} of 16 parts)");
        if (s.PlayerXpByLevel != "" || s.BotXpByLevel != "") on.Add("XP by level");
        return on.Count == 0
            ? "Settings are loaded from the saved server settings. Everything is off: the game plays like the normal release."
            : "Saved and on at the next server startup: " + string.Join(", ", on) + ".";
    }

    // ---- Layout, matching the Options tab -------------------------------------------------------------

    private sealed record CardParts(InsetPanel Panel, TableLayoutPanel Grid, int Height);

    /// <summary>An inset card with a gold title row and <paramref name="rows"/> 40-px rows, like the Options card.</summary>
    private static CardParts Card(string title, Color accent, int rows)
    {
        var panel = new InsetPanel
        {
            Dock = DockStyle.Fill, Accent = accent, Margin = new Padding(18, 0, 18, 14),
            Padding = new Padding(CardPadding, CardPadding / 2, CardPadding, CardPadding / 2),
        };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = rows + 1, BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumn));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ControlColumn));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleRow));
        for (int i = 0; i < rows; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Row));
        var heading = new Label
        {
            AutoSize = true, Text = title, Font = TitleFont, ForeColor = DaocTheme.GoldLight,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0),
        };
        grid.Controls.Add(heading, 0, 0);
        grid.SetColumnSpan(heading, 3);
        panel.Controls.Add(grid);
        return new CardParts(panel, grid, TitleRow + rows * Row + CardPadding);
    }

    /// <summary>
    /// One row: a switch spanning the label and control columns, or (first = null) a control whose caption the
    /// caller adds with <see cref="Dependent"/> or <see cref="Captioned"/>, then a description. Returns the description.
    /// </summary>
    private static Label Add(CardParts card, Control? first, Control? control, string description)
    {
        int row = card.Grid.GetRow(card.Grid.Controls[^1]) + 1;
        if (first is not null)
        {
            card.Grid.Controls.Add(first, 0, row);
            if (control is null) card.Grid.SetColumnSpan(first, 2);
        }
        if (control is not null) card.Grid.Controls.Add(control, 1, row);
        var label = Description(description);
        card.Grid.Controls.Add(label, 2, row);
        return label;
    }

    /// <summary>Registers a row that only works while its feature is on. A caption not yet placed goes in its row's first column.</summary>
    private void Dependent(Control switchOrCaption, Label description, Func<SocialExpansionSettings, bool> active)
    {
        if (switchOrCaption.Parent is null && description.Parent is TableLayoutPanel grid)
            grid.Controls.Add(switchOrCaption, 0, grid.GetRow(description));
        _dependent.Add((switchOrCaption, description, active));
    }

    private static Control Intro() => new Label
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = StatusFont, ForeColor = DaocTheme.Muted,
        Margin = new Padding(20, 6, 18, 0),
        Text = "Bots that feel like 2003 players. Everything here starts off; with it all off the game plays exactly like the normal " +
               "release. Your /spawn companions never change. Saved settings are used on the next server startup.",
    };

    private static CheckBox Switch(string text, bool part = false) => new()
    {
        Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Font = LabelFont, ForeColor = DaocTheme.Parchment,
        // The guild and voice parts sit under their feature, indented like a sub-option.
        Margin = new Padding(part ? 22 : 0, 0, 0, 0),
    };

    /// <summary>Places a caption in its row's first column (for a control row that is always active).</summary>
    private static void Captioned(Control caption, Label description)
    {
        if (description.Parent is TableLayoutPanel grid)
            grid.Controls.Add(caption, 0, grid.GetRow(description));
    }

    private static ComboBox Choice(string name, params string[] items)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 128, AccessibleName = name };
        box.Items.AddRange(items);
        box.SelectedIndex = 0;
        StyleInput(box);
        return box;
    }

    private static Label Caption(string text, bool part = true) => new()
    {
        Dock = DockStyle.Fill, Text = text, Font = LabelFont, ForeColor = DaocTheme.Parchment, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(part ? 22 : 0, 0, 0, 0),
    };

    private static Label Description(string text) => new()
    {
        Dock = DockStyle.Fill, Text = text, Font = DescriptionFont, ForeColor = DaocTheme.Parchment, TextAlign = ContentAlignment.MiddleLeft,
    };

    private static NumericUpDown Number(int minimum, int maximum, string name)
    {
        var number = new NumericUpDown { Minimum = minimum, Maximum = maximum, Width = 62, AccessibleName = name, TextAlign = HorizontalAlignment.Right };
        StyleInput(number);
        return number;
    }

    private static Control WithUnit(Control input, string unit)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, BackColor = Color.Transparent, Margin = Padding.Empty };
        flow.Controls.Add(input);
        flow.Controls.Add(new Label { AutoSize = true, Text = unit, Font = LabelFont, ForeColor = DaocTheme.Parchment, Margin = new Padding(0, 6, 0, 0) });
        return flow;
    }

    // As MainForm.StyleInput: the launcher's dark parchment inputs.
    private static void StyleInput(Control input)
    {
        input.BackColor = Color.FromArgb(59, 53, 43);
        input.ForeColor = DaocTheme.Text;
        input.Font = InputFont;
        input.Anchor = AnchorStyles.Left;
        input.Margin = new Padding(0, 0, 8, 0);
        if (input is TextBox textBox) textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private static RuneButton Action(string text, Color accent) => new()
    {
        Text = text, Accent = accent, AutoSize = true, MinimumSize = new Size(150, 32), Margin = new Padding(8, 0, 0, 0),
    };

    /// <summary>One XP-by-level row: Off, the suggested ranges, or custom ranges typed in, plus an error line under it.</summary>
    private sealed class XpRow
    {
        private const int Off = 0, Suggested = 1, Custom = 2;
        public const int Height = 44, ErrorHeight = 24;
        private readonly string _label;
        private readonly string _suggested;
        private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 128 };
        private readonly TextBox _text = new() { Width = 220 };
        private readonly Label _hint = new() { AutoSize = true, Font = DescriptionFont, ForeColor = DaocTheme.Parchment, Margin = new Padding(4, 7, 0, 0) };
        private readonly Label _error = new() { Dock = DockStyle.Fill, Font = StatusFont, ForeColor = DaocTheme.Danger, TextAlign = ContentAlignment.TopLeft };
        private readonly string _suggestedHint;
        private bool _setting;
        public event Action? Changed;

        public XpRow(string label, string suggested, string suggestedHint)
        {
            _label = label;
            _suggested = suggested;
            _suggestedHint = suggestedHint;
            StyleInput(_mode);
            StyleInput(_text);
            _mode.Items.AddRange(["Off  (flat rate)", "Suggested", "Custom"]);
            _mode.AccessibleName = label + " XP by level";
            _text.AccessibleName = label + " XP ranges";
            _mode.SelectedIndexChanged += (_, _) =>
            {
                if (_setting) return;
                if (_mode.SelectedIndex == Off) _text.Text = "";
                else if (_mode.SelectedIndex == Suggested) _text.Text = _suggested;
                else if (_text.Text.Trim() == "") _text.Text = _suggested;
                Refresh();
                Changed?.Invoke();
            };
            _text.TextChanged += (_, _) => { if (!_setting) Changed?.Invoke(); };
            Value = "";
        }

        /// <summary>Two grid rows: label, mode and ranges with a hint, then the error line.</summary>
        public void AddTo(TableLayoutPanel grid)
        {
            int row = grid.GetRow(grid.Controls[^1]) + 1;
            grid.RowStyles[row] = new RowStyle(SizeType.Absolute, Height);
            grid.RowStyles[row + 1] = new RowStyle(SizeType.Absolute, ErrorHeight);
            grid.Controls.Add(new Label { Dock = DockStyle.Fill, Text = _label, Font = LabelFont, ForeColor = DaocTheme.Parchment, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            grid.Controls.Add(_mode, 1, row);
            var ranges = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, BackColor = Color.Transparent, Margin = Padding.Empty };
            ranges.Controls.Add(_text);
            ranges.Controls.Add(_hint);
            grid.Controls.Add(ranges, 2, row);
            grid.Controls.Add(_error, 1, row + 1);
            grid.SetColumnSpan(_error, 2);
        }

        public string Value
        {
            get => _mode.SelectedIndex == Off ? "" : _text.Text.Trim();
            set
            {
                _setting = true;
                string text = value.Trim();
                _mode.SelectedIndex = text == "" ? Off : Normalize(text) == Normalize(_suggested) ? Suggested : Custom;
                _text.Text = text;
                Refresh();
                _setting = false;
            }
        }

        private void Refresh()
        {
            _text.ReadOnly = _mode.SelectedIndex != Custom;
            _text.Visible = _mode.SelectedIndex != Off;
            _hint.Text = _mode.SelectedIndex switch
            {
                Off => "The flat rate from the Options tab",
                Suggested => _suggestedHint,
                _ => "first-last:rate;  …",
            };
        }


        public void ShowError(string? error) => _error.Text = error ?? "";

        private static string Normalize(string spec) => string.Concat(spec.Where(c => !char.IsWhiteSpace(c))).TrimEnd(';');
    }
}
