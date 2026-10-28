using System.Data.SQLite;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class SocialExpansionSettingsTests
{
    private string _folder = null!, _database = null!;
    private bool _stopped;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "social-expansion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _database = Path.Combine(_folder, "opendaoc.sqlite3.db");
        _stopped = true;
        // The world database's ServerProperty table: Key is the primary key and ServerProperty_ID is unique.
        Exec("""
            CREATE TABLE ServerProperty (Category TEXT NOT NULL, `Key` TEXT NOT NULL PRIMARY KEY, Description TEXT NOT NULL,
                DefaultValue TEXT NOT NULL, Value TEXT NOT NULL, LastTimeRowUpdated TEXT, ServerProperty_ID TEXT NOT NULL UNIQUE);
            INSERT INTO ServerProperty VALUES ('rates', 'xp_rate', 'flat', '1', '1', '', '');
            """);
    }

    [TearDown] public void Cleanup()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private void Exec(string sql)
    {
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private Dictionary<string, string> Rows()
    {
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = "SELECT `Key`, Value FROM ServerProperty";
        using var reader = command.ExecuteReader();
        var rows = new Dictionary<string, string>();
        while (reader.Read()) rows[reader.GetString(0)] = reader.GetString(1);
        return rows;
    }

    private static readonly SocialExpansionSettings AllOn = new()
    {
        BotGuilds = true, GuildSharePercent = 60, GuildMinLevel = 10, Personalities = true, Voices = true,
        GuildAlliances = false, PlayerXpByLevel = SocialExpansionSettings.SuggestedPlayerXp,
        BotXpByLevel = SocialExpansionSettings.SuggestedBotXp,
        LivingMemory = true, LivingGrats = true, LivingFirsts = true, LivingDeath = true, LivingAlarms = true,
        LivingNemesis = true, ScheduleMode = "clock", LivingBreaks = true, LivingBodyLanguage = true, LivingBeggars = true,
        LivingDeeds = true, LivingStorylines = true, LivingCharacters = true, LivingRumours = true,
        LivingHolidays = true, LivingLeeroy = true,
    };

    [Test] public void NoRowsMeansTheServerDefaults()
    {
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(SocialExpansionSettings.Defaults));
        Assert.That(problems, Is.Empty);
        // Since Beta 1.1 (the talking update) the chat-only parts are on: voices, memory, grats, and the beggars. The rest stays off.
        Assert.That(SocialExpansionSettings.Defaults.Voices && SocialExpansionSettings.Defaults.LivingMemory &&
                    SocialExpansionSettings.Defaults.LivingGrats && SocialExpansionSettings.Defaults.LivingBeggars, Is.True);
        Assert.That(SocialExpansionSettings.Defaults.BotGuilds || SocialExpansionSettings.Defaults.Personalities ||
                    SocialExpansionSettings.Defaults.LivingDeath || SocialExpansionSettings.Defaults.LivingBreaks, Is.False);
        Assert.That(SocialExpansionSettings.Defaults.PlayerXpByLevel + SocialExpansionSettings.Defaults.BotXpByLevel, Is.Empty);
    }

    [Test] public void RecommendedTurnsEveryFeatureOnAndSavesAsIs()
    {
        // Setup applies these on a first install (owner 2026-10-28): every switch on, the suggested XP ranges.
        var r = SocialExpansionSettings.Recommended;
        var switches = typeof(SocialExpansionSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.CanWrite).ToArray();
        Assert.That(switches.Length, Is.GreaterThanOrEqualTo(24));
        Assert.That(switches.Where(p => !(bool)p.GetValue(r)!).Select(p => p.Name), Is.Empty);
        Assert.That(r.PlayerXpByLevel, Is.EqualTo(SocialExpansionSettings.SuggestedPlayerXp));
        Assert.That(r.BotXpByLevel, Is.EqualTo(SocialExpansionSettings.SuggestedBotXp));
        Assert.That(r.ScheduleMode, Is.EqualTo("clock"));
        Assert.That(r.Validate(), Is.Null);
        r.Save(_database, null, () => true);
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(r));
        Assert.That(problems, Is.Empty);
    }

    [Test] public void SaveWritesWhatTheServerReadsAndLoadsItBack()
    {
        AllOn.Save(_database, null, () => _stopped);
        var rows = Rows();
        Assert.That(rows["bot_guilds_enabled"], Is.EqualTo("True"));
        Assert.That(rows["bot_guild_share"], Is.EqualTo("0.6"));
        Assert.That(rows["bot_guild_min_level"], Is.EqualTo("10"));
        Assert.That(rows["bot_guild_alliances"], Is.EqualTo("False"));
        Assert.That(rows["bot_personalities_enabled"], Is.EqualTo("True"));
        Assert.That(rows["xp_rate_by_level"], Is.EqualTo("1-19:3;20-34:5;35-50:12"));
        Assert.That(rows["xp_rate"], Is.EqualTo("1"), "the flat rate is not touched");
        Assert.That(rows["bot_living_memory"], Is.EqualTo("True"));
        Assert.That(rows["bot_schedule_mode"], Is.EqualTo("clock"));
        Assert.That(rows["bot_living_breaks"], Is.EqualTo("True"));
        Assert.That(rows["bot_living_body_language"], Is.EqualTo("True"));
        Assert.That(rows["bot_living_beggars"], Is.EqualTo("True"));
        Assert.That(rows["bot_living_deeds"] + rows["bot_living_storylines"] + rows["bot_living_characters"] + rows["bot_living_rumours"],
            Is.EqualTo("TrueTrueTrueTrue"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(AllOn));
        Assert.That(problems, Is.Empty);
    }

    [Test] public void SaveOnlyWritesWhatChanged()
    {
        Exec("INSERT INTO ServerProperty VALUES ('guild', 'bot_guild_share', 'd', '', '0.655', '', 'bot_guild_share')");
        var loaded = SocialExpansionSettings.Load(_database, new List<string>());
        Assert.That(loaded.GuildSharePercent, Is.EqualTo(66));
        (loaded with { Personalities = true }).Save(_database, loaded, () => _stopped);
        var rows = Rows();
        Assert.That(rows["bot_guild_share"], Is.EqualTo("0.655"), "an untouched setting keeps its exact value");
        Assert.That(rows["bot_personalities_enabled"], Is.EqualTo("True"));
        Assert.That(rows.ContainsKey("bot_guilds_enabled"), Is.False);
    }

    [Test] public void UnreadableValuesAreReportedAndShownAsDefaults()
    {
        Exec("""
            INSERT INTO ServerProperty VALUES ('guild', 'bot_guilds_enabled', 'd', '', 'maybe', '', 'bot_guilds_enabled');
            INSERT INTO ServerProperty VALUES ('guild', 'bot_guild_share', 'd', '', '1.5', '', 'bot_guild_share');
            INSERT INTO ServerProperty VALUES ('rates', 'bot_xp_rate_by_level', 'd', '', '50-1:2', '', 'bot_xp_rate_by_level');
            """);
        var problems = new List<string>();
        var keys = new HashSet<string>();
        var loaded = SocialExpansionSettings.Load(_database, problems, keys);
        Assert.That(loaded.BotGuilds, Is.False, "the server reads an unreadable bool as False");
        Assert.That(loaded.GuildSharePercent, Is.EqualTo(100), "a share above 1 shows as 100%");
        Assert.That(problems, Has.Count.EqualTo(2));
        Assert.That(keys, Is.EquivalentTo(new[] { "bot_guilds_enabled", "bot_guild_share" }));
        Assert.That(loaded.BotXpByLevel, Is.EqualTo("50-1:2"), "an invalid range setting is kept so its error can be shown");
        Assert.That(loaded.Validate(), Does.Contain("ends before it starts"));
    }

    [Test] public void LivingServerScheduleIsReadLikeTheServerAndAllOffSwitchesEveryPartOff()
    {
        Exec("INSERT INTO ServerProperty VALUES ('bots', 'bot_schedule_mode', 'd', '', 'sometimes', '', 'bot_schedule_mode')");
        var problems = new List<string>();
        var keys = new HashSet<string>();
        var loaded = SocialExpansionSettings.Load(_database, problems, keys);
        Assert.That(loaded.ScheduleMode, Is.EqualTo("off"), "the server reads an unknown mode as off");
        Assert.That(keys, Is.EquivalentTo(new[] { "bot_schedule_mode" }));
        Assert.That(AllOn with { ScheduleMode = "weekly" }, Has.Property("IsAllOff").False);
        Assert.That((AllOn with { ScheduleMode = "weekly" }).Validate(), Does.Contain("off, session or clock"));
        var off = AllOn.AllOff();
        Assert.That(off.AnyLiving, Is.False);
        Assert.That(off.IsAllOff, Is.True);
        Assert.That((SocialExpansionSettings.Defaults with { ScheduleMode = "session" }).IsAllOff, Is.False);
        Assert.That((SocialExpansionSettings.Defaults with { LivingBodyLanguage = true }).IsAllOff, Is.False);
        Assert.That(off.LivingBodyLanguage, Is.False);
        Assert.That(off.LivingBeggars || off.LivingDeeds || off.LivingStorylines || off.LivingCharacters || off.LivingRumours, Is.False);
        Assert.That(off.LivingHolidays || off.LivingLeeroy, Is.False);
        Assert.That((SocialExpansionSettings.Defaults.AllOff() with { LivingBeggars = true }).IsAllOff, Is.False);
    }

    [Test] public void NothingIsWrittenWhileTheServerRunsOrWithInvalidRanges()
    {
        _stopped = false;
        Assert.Throws<InvalidOperationException>(() => AllOn.Save(_database, null, () => _stopped));
        _stopped = true;
        Assert.Throws<InvalidOperationException>(() => (AllOn with { PlayerXpByLevel = "1-19:3;10-20:2" }).Save(_database, null, () => _stopped));
        Assert.Throws<InvalidOperationException>(() => (AllOn with { BotXpByLevel = "1-50:0" }).Save(_database, null, () => _stopped));
        Assert.That(Rows().Keys, Is.EquivalentTo(new[] { "xp_rate" }));
    }

    [Test] public void RepairRewritesOnlyTheBadRowsAndAllOffKeepsTheChoices()
    {
        Exec("""
            INSERT INTO ServerProperty VALUES ('guild', 'bot_guilds_enabled', 'd', '', 'maybe', '', 'bot_guilds_enabled');
            INSERT INTO ServerProperty VALUES ('guild', 'bot_guild_share', 'd', '', '0.655', '', 'bot_guild_share');
            """);
        var keys = new HashSet<string>();
        var loaded = SocialExpansionSettings.Load(_database, new List<string>(), keys);
        loaded.Save(_database, loaded, () => _stopped, keys);
        var rows = Rows();
        Assert.That(rows["bot_guilds_enabled"], Is.EqualTo("False"), "the unreadable row is repaired");
        Assert.That(rows["bot_guild_share"], Is.EqualTo("0.655"), "a readable row is not rewritten by a repair");
        Assert.That(rows.ContainsKey("bot_personalities_enabled"), Is.False);

        var custom = AllOn with { GuildSharePercent = 40, GuildMinLevel = 20, GuildClaims = false };
        var off = custom.AllOff();
        Assert.That(off.IsAllOff, Is.True);
        Assert.That((off.GuildSharePercent, off.GuildMinLevel, off.GuildClaims, off.Debates), Is.EqualTo((40, 20, false, true)),
            "ALL OFF switches the features off but keeps the player's share, level and parts");
    }

    [Test] public void AFailedLoadNeverSavesOverTheRealSettings()
    {
        Exec("DROP TABLE ServerProperty");
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.SocialExpansionSettingsControl")!;
        using var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
        var save = (Button)type.GetField("_save", Hidden)!.GetValue(panel)!;
        ((CheckBox)type.GetField("_guilds", Hidden)!.GetValue(panel)!).Checked = true;
        Assert.That(save.Enabled, Is.False, "nothing can be saved over settings that couldn't be read");
        Assert.That(type.GetProperty("HasUnsavedChanges")!.GetValue(panel), Is.False, "the start guard isn't blocked by a failed read");
        Exec("""
            CREATE TABLE ServerProperty (Category TEXT NOT NULL, `Key` TEXT NOT NULL PRIMARY KEY, Description TEXT NOT NULL,
                DefaultValue TEXT NOT NULL, Value TEXT NOT NULL, LastTimeRowUpdated TEXT, ServerProperty_ID TEXT NOT NULL UNIQUE);
            INSERT INTO ServerProperty VALUES ('guild', 'bot_personalities_enabled', 'd', '', 'True', '', 'bot_personalities_enabled');
            """);
        type.GetMethod("SaveSettings", Hidden)!.Invoke(panel, null);
        Assert.That(Rows().Keys, Is.EquivalentTo(new[] { "bot_personalities_enabled" }), "SaveSettings refuses after a failed read");
        ((Button)type.GetField("_undo", Hidden)!.GetValue(panel)!).PerformClick();
        Assert.That(((CheckBox)type.GetField("_personalities", Hidden)!.GetValue(panel)!).Checked, Is.True, "UNDO EDITS reads them again");
    }

    /// <summary>The keys and defaults here must be the server's (ServerProperties.cs), or the tab would show the wrong state.</summary>
    [Test] public void KeysAndDefaultsMatchTheServer()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source/server/GameServer/serverproperty/ServerProperties.cs")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null) Assert.Ignore("Server source not found beside the tests.");
        string source = File.ReadAllText(Path.Combine(dir!, "source/server/GameServer/serverproperty/ServerProperties.cs"));
        var defaults = SocialExpansionSettings.Defaults.ToStoredValues();
        foreach (var (key, category, _) in typeof(SocialExpansionSettings)
                     .GetField("Settings", BindingFlags.Static | BindingFlags.NonPublic)!
                     .GetValue(null) as (string Key, string Category, string Description)[] ?? [])
        {
            var match = Regex.Match(source, $"\\[ServerProperty\\(\"(\\w+)\", \"{key}\", \"(?:[^\"\\\\]|\\\\.)*\", ([^)]*)\\)\\]");
            Assert.That(match.Success, Is.True, key + " is not a server setting");
            Assert.That(match.Groups[1].Value, Is.EqualTo(category), key + " category");
            string serverDefault = match.Groups[2].Value.Trim().Trim('"');
            string expected = serverDefault switch
            {
                "true" => "True",
                "false" => "False",
                _ when double.TryParse(serverDefault, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)
                    => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                _ => serverDefault,
            };
            Assert.That(defaults[key], Is.EqualTo(expected), key + " default");
        }
    }

    [Test] public void TheTabFollowsTheServerStateAndDependencies()
    {
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.SocialExpansionSettingsControl")!;
        using var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
        T Field<T>(string name) => (T)type.GetField(name, Hidden)!.GetValue(panel)!;
        void Call(string name) => type.GetMethod(name, Hidden | BindingFlags.Public)!.Invoke(panel, null);

        var guilds = Field<CheckBox>("_guilds");
        var claims = Field<CheckBox>("_guildClaims");
        var voices = Field<CheckBox>("_voices");
        var debates = Field<CheckBox>("_debates");
        var save = Field<Button>("_save");
        Color muted = Color.FromArgb(166, 154, 128), parchment = Color.FromArgb(215, 199, 157);
        Assert.That(claims.ForeColor, Is.EqualTo(muted), "guild parts are dimmed until bot guilds are on");
        // Since Beta 1.1 voices are on by default, so debates start bright and dim when voices are switched off.
        Assert.That(voices.Checked, Is.True, "bot voices are on by default");
        Assert.That(debates.ForeColor, Is.EqualTo(parchment));
        Assert.That(save.Enabled, Is.False, "nothing to save yet");
        voices.Checked = false;
        Assert.That(debates.ForeColor, Is.EqualTo(muted), "debates are dimmed until voices are on");

        guilds.Checked = true;
        voices.Checked = true;
        Assert.That(claims.ForeColor, Is.EqualTo(parchment));
        Assert.That(debates.ForeColor, Is.EqualTo(parchment));
        Assert.That(save.Enabled, Is.True);
        Assert.That(new[] { guilds, claims, voices, debates }.All(box => box.Enabled), Is.True, "switches are never disabled (unreadable on the dark theme)");

        var playerXp = type.GetField("_playerXp", Hidden)!.GetValue(panel)!;
        var mode = (ComboBox)playerXp.GetType().GetField("_mode", Hidden)!.GetValue(playerXp)!;
        var text = (TextBox)playerXp.GetType().GetField("_text", Hidden)!.GetValue(playerXp)!;
        mode.SelectedIndex = 2;
        text.Text = "1-19:3;15-30:2";
        Assert.That(save.Enabled, Is.False, "overlapping ranges can't be saved");
        mode.SelectedIndex = 1;
        Assert.That(text.Text, Is.EqualTo(SocialExpansionSettings.SuggestedPlayerXp));

        _stopped = false;
        Call("UpdateState");
        Assert.That(save.Enabled, Is.False, "nothing is saved while the server runs");
        Assert.That(Field<Label>("_status").Text, Does.StartWith("SERVER RUNNING"));
        Call("SaveSettings");
        Assert.That(Rows().ContainsKey("bot_guilds_enabled"), Is.False);

        _stopped = true;
        Call("UpdateState");
        Call("SaveSettings");
        var rows = Rows();
        Assert.That(rows["bot_guilds_enabled"], Is.EqualTo("True"));
        // Voices were switched off and on again: still the default (on since Beta 1.1), so nothing is written for them.
        Assert.That(rows.ContainsKey("bot_voices_enabled"), Is.False);
        Assert.That(rows["xp_rate_by_level"], Is.EqualTo(SocialExpansionSettings.SuggestedPlayerXp));
        Assert.That(type.GetProperty("HasUnsavedChanges")!.GetValue(panel), Is.False);

        Field<Button>("_allOff").PerformClick();
        Call("SaveSettings");
        rows = Rows();
        Assert.That(rows["bot_guilds_enabled"], Is.EqualTo("False"));
        Assert.That(rows["bot_voices_enabled"], Is.EqualTo("False"), "ALL OFF switches the new default off too");
        Assert.That(rows["xp_rate_by_level"], Is.EqualTo(""));
    }

    /// <summary>
    /// Renders the real launcher (Options, Bot Goals Setting and Living Server tabs) and the tab in several
    /// states to PNG files in TestResults' work folder (ui-preview\), so the look can be checked against the
    /// other settings screens. CI uploads them as an artifact.
    /// </summary>
    [Test] public void RendersPreviewsBesideTheOtherSettingsTabs()
    {
        string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-preview");
        Directory.CreateDirectory(folder);
        var launcher = Assembly.Load("OfflineDAoC");

        using (var form = (Form)Activator.CreateInstance(launcher.GetType("OfflineDaoc.Launcher.MainForm")!)!)
        {
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20_000, -20_000);
            form.Show();
            Application.DoEvents();
            TabControl tabs = All(form).OfType<TabControl>().First(t => t.TabPages.Cast<TabPage>().Any(p => p.Text == "Living Server"));
            foreach (var (tab, file) in new[] { ("Options", "1-options.png"), ("Bot Goals Setting", "2-bot-goals.png"), ("Living Server", "3-living-server-in-launcher.png") })
            {
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(p => p.Text == tab);
                Application.DoEvents();
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(Path.Combine(folder, file));
            }
        }

        var type = launcher.GetType("OfflineDaoc.Launcher.SocialExpansionSettingsControl")!;
        void Render(string file, Action<Control> setup, bool bottom = false, int scroll = 0)
        {
            using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20_000, -20_000),
                ClientSize = new Size(1180, 1060), BackColor = Color.FromArgb(35, 31, 26) };
            var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
            host.Controls.Add(panel);
            host.Show();
            setup(panel);
            type.GetMethod("UpdateState")!.Invoke(panel, null);
            Application.DoEvents();
            if (scroll > 0)
            {
                var scroller = panel.Controls.OfType<Panel>().First(c => c.AutoScroll);
                scroller.AutoScrollPosition = new Point(0, scroll);
                Application.DoEvents();
            }
            if (bottom)
            {
                // The CI desktop is small: show the XP card by scrolling the cards to the end.
                var scroller = panel.Controls.OfType<Panel>().First(c => c.AutoScroll);
                scroller.AutoScrollPosition = new Point(0, 10_000);
                Application.DoEvents();
            }
            using var image = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
            panel.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(folder, file));
        }
        T F<T>(Control panel, string name) => (T)type.GetField(name, Hidden)!.GetValue(panel)!;
        void Xp(Control panel, string row, int mode, string? text = null)
        {
            var xp = type.GetField(row, Hidden)!.GetValue(panel)!;
            ((ComboBox)xp.GetType().GetField("_mode", Hidden)!.GetValue(xp)!).SelectedIndex = mode;
            if (text != null) ((TextBox)xp.GetType().GetField("_text", Hidden)!.GetValue(xp)!).Text = text;
        }

        Render("4-tab-all-off.png", _ => { });
        Render("5-tab-editing.png", panel =>
        {
            F<CheckBox>(panel, "_guilds").Checked = true;
            F<CheckBox>(panel, "_voices").Checked = true;
            F<CheckBox>(panel, "_personalities").Checked = true;
            Xp(panel, "_playerXp", 1);
            Xp(panel, "_botXp", 2, "1-19:1.5;15-34:2.5");
        });
        Render("5b-tab-editing-xp.png", panel =>
        {
            Xp(panel, "_playerXp", 1);
            Xp(panel, "_botXp", 2, "1-19:1.5;15-34:2.5");
        }, bottom: true);
        Render("5c-tab-editing-living.png", panel =>
        {
            F<CheckBox>(panel, "_memory").Checked = true;
            F<CheckBox>(panel, "_death").Checked = true;
            F<ComboBox>(panel, "_schedule").SelectedIndex = 2;
        }, scroll: 600);
        AllOn.Save(_database, null, () => true);
        Render("6-tab-saved-on.png", _ => { });
        Render("6c-tab-saved-on-living.png", _ => { }, scroll: 600);
        Render("6b-tab-saved-on-xp.png", _ => { }, bottom: true);
        _stopped = false;
        Render("7-tab-server-running.png", _ => { });
        TestContext.Progress.WriteLine("UI previews: " + folder);
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in All(child)) yield return nested;
        }
    }
}
