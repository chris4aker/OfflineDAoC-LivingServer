using DOL.GS;
using DOL.GS.LoreChannels;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Living Server 1.2: the lore the launcher ships (the same data/lore files the server embeds, built into this exe), and
/// what the Herald and the Realm Records page make of it.
/// </summary>
public static class LauncherLore
{
    private static readonly Lazy<LoreLibrary> Loaded = new(Load);
    public static ILoreSource Source => Loaded.Value;

    private static LoreLibrary Load()
    {
        var assembly = typeof(LauncherLore).Assembly;
        var files = new List<(string, string)>();
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("OfflineDaoc/lore/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files.Add((name, reader.ReadToEnd()));
        }
        return LoreLibrary.Parse(files);
    }

    /// <summary>A record's details with its chronicle sentence first, when its lore has one.</summary>
    public static string Describe(RealmEventRecord record, ILoreSource? source = null) =>
        LoreChronicle.For(source ?? Source, record) is { } chronicle ? chronicle + Environment.NewLine + Environment.NewLine + record.Details : record.Details;

    /// <summary>A realm's bestiary from the event ledger: first kills and raid records.</summary>
    public static List<LoreBestiaryEntry> Bestiary(ILoreSource source, string ledger, int realm) =>
        LoreBestiary.For(source, realm, File.Exists(ledger) ? LoreFirstKillStore.Read(ledger) : [],
            File.Exists(ledger) ? LoreFirstKillStore.RaidRecords(ledger) : []);
}

/// <summary>
/// The Bestiary page of the Realm Records tab: every boss of the chosen realm's own and the neutral places, with its
/// place and legend; its story, the realm's first kill and its raid record open once the realm has killed it.
/// </summary>
public sealed class BestiaryControl : UserControl
{
    private readonly Func<string> _locate;
    private readonly ILoreSource _source;
    private readonly ComboBox _realm = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly Button _refresh = new() { Text = "REFRESH", AutoSize = true };
    public readonly ListBox List = new() { Dock = DockStyle.Fill, IntegralHeight = false, BackColor = DaocTheme.StoneDark, ForeColor = DaocTheme.Text };
    public readonly TextBox Page = new() { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = DaocTheme.StoneDark, ForeColor = DaocTheme.Text };
    public readonly Label Status = new() { AutoSize = true, ForeColor = DaocTheme.GoldLight };
    private List<LoreBestiaryEntry> _entries = [];
    private int _request;

    public BestiaryControl(Func<string> locate, ILoreSource? source = null)
    {
        _locate = locate;
        _source = source ?? LauncherLore.Source;
        Dock = DockStyle.Fill; BackColor = DaocTheme.Panel; ForeColor = DaocTheme.Text; Font = new Font("Georgia", 9);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _realm.Items.AddRange(["Albion", "Midgard", "Hibernia"]);
        _realm.SelectedIndex = 0;
        toolbar.Controls.AddRange([_realm, _refresh]);
        layout.Controls.Add(toolbar, 0, 0); layout.SetColumnSpan(toolbar, 2);
        layout.Controls.Add(List, 0, 1); layout.Controls.Add(Page, 1, 1);
        layout.Controls.Add(Status, 0, 2); layout.SetColumnSpan(Status, 2);
        Controls.Add(layout);
        Status.Text = "The bestiary of the realm's places. A boss's story opens when the realm first kills it.";
        _realm.SelectedIndexChanged += async (_, _) => await RefreshAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        List.SelectedIndexChanged += (_, _) => ShowSelected();
    }

    public int Realm => _realm.SelectedIndex + 1;
    public void SelectRealm(int realm) => _realm.SelectedIndex = Math.Clamp(realm, 1, 3) - 1;

    public static string Row(LoreBestiaryEntry e) =>
        $"{e.Boss.Name} - {e.Location.Name}" + (e.Unlocked ? $" (slain {LoreBestiary.When(e.FirstKillUtc)})" : " (not yet slain)");

    public async Task RefreshAsync()
    {
        int request = ++_request, realm = Realm;
        _refresh.Enabled = false;
        try
        {
            var entries = await Task.Run(() => LauncherLore.Bestiary(_source, _locate(), realm));
            if (IsDisposed || request != _request) return;
            _entries = entries;
            List.BeginUpdate();
            List.Items.Clear();
            foreach (var e in entries) List.Items.Add(Row(e));
            List.EndUpdate();
            if (List.Items.Count > 0) List.SelectedIndex = 0; else Page.Text = "No lore written for this realm's places yet.";
            Status.Text = entries.Count == 0 ? "No bosses in the lore yet."
                : $"{entries.Count(e => e.Unlocked)} of {entries.Count} slain by {LoreRealm.Name(realm)} · from this game's own records";
        }
        catch (Exception ex) when (ex is System.Data.SQLite.SQLiteException or IOException or UnauthorizedAccessException)
        {
            if (!IsDisposed && request == _request) Status.Text = "Could not read the records: " + ex.Message;
        }
        finally { if (!IsDisposed && request == _request) _refresh.Enabled = true; }
    }

    private void ShowSelected()
    {
        int i = List.SelectedIndex;
        Page.Text = i >= 0 && i < _entries.Count ? string.Join(Environment.NewLine, LoreBestiary.Page(_entries[i], Realm)) : "";
    }

    protected override void Dispose(bool disposing) { if (disposing) _request++; base.Dispose(disposing); }
}
