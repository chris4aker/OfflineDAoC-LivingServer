namespace OfflineDaoc.Launcher;

/// <summary>
/// Read-only "Battlegrounds" tab: one row per classic battleground with its level bracket, who holds the
/// central keep, and how many bots of each realm are inside or on their way. Fed by the server's
/// rvr-world.json (same refresh as the Active RvR tab); no announcements.
/// </summary>
internal sealed class BattlegroundsPanel : UserControl
{
    public sealed record Status(string Name, int MinLevel, int MaxLevel, string CentralKeep, string Owner,
        int AlbionInside, int MidgardInside, int HiberniaInside, int AlbionTravelling, int MidgardTravelling,
        int HiberniaTravelling, int Monsters);

    private sealed record Row(string Battleground, string Levels, string CentralKeep, string HeldBy,
        int Albion, int Midgard, int Hibernia, int Total, string OnTheirWay);

    private readonly Label _summary = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0),
        Font = new Font("Georgia", 9f, FontStyle.Bold), ForeColor = DaocTheme.GoldLight,
    };
    private readonly DataGridView _grid = new();
    private readonly BindingSource _source = new();

    public BattlegroundsPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(7) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_summary, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = DaocTheme.StoneDark;
        _grid.BorderStyle = BorderStyle.Fixed3D;
        _grid.GridColor = Color.FromArgb(78, 70, 56);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(61, 54, 43), ForeColor = DaocTheme.GoldLight,
            SelectionBackColor = Color.FromArgb(61, 54, 43), Font = new Font("Georgia", 8.25f, FontStyle.Bold), Padding = new Padding(2),
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(35, 31, 26), ForeColor = DaocTheme.Text,
            SelectionBackColor = Color.FromArgb(90, 72, 43), SelectionForeColor = DaocTheme.GoldLight,
            Padding = new Padding(2), Font = new Font("Georgia", 8.25f),
        };
        _grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(42, 37, 30) };
        _grid.ColumnHeadersHeight = 28;
        _grid.RowTemplate.Height = 25;
        _grid.DataSource = _source;
        _grid.Columns.Add(Column("Battleground", nameof(Row.Battleground), 120));
        _grid.Columns.Add(Column("Levels", nameof(Row.Levels), 70));
        _grid.Columns.Add(Column("Central keep", nameof(Row.CentralKeep), 140));
        _grid.Columns.Add(Column("Held by", nameof(Row.HeldBy), 90));
        _grid.Columns.Add(Column("Albion bots", nameof(Row.Albion), 85));
        _grid.Columns.Add(Column("Midgard bots", nameof(Row.Midgard), 90));
        _grid.Columns.Add(Column("Hibernia bots", nameof(Row.Hibernia), 90));
        _grid.Columns.Add(Column("Total inside", nameof(Row.Total), 85));
        _grid.Columns.Add(Column("On their way (Alb / Mid / Hib)", nameof(Row.OnTheirWay), 190));
        layout.Controls.Add(_grid, 0, 1);

        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(1100, 0), Margin = new Padding(6, 8, 6, 6), ForeColor = DaocTheme.Text,
            Text = "Bots with a battleground goal take a realm teleporter's [Battlegrounds] choice to the battleground of their level, " +
                   "then roam it and attack or defend its central keep. Set how many bots do this with Battlegrounds % on the Bot Goals Setting tab " +
                   "(15% of levels 20–49 by default). Players reach the same battlegrounds from any realm teleporter.",
        }, 0, 2);
        Controls.Add(layout);
        Show(null, false);
    }

    private static DataGridViewTextBoxColumn Column(string header, string property, int width) => new()
    {
        HeaderText = header, DataPropertyName = property, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    public void Show(IReadOnlyList<Status>? battlegrounds, bool serverRunning)
    {
        if (battlegrounds == null || battlegrounds.Count == 0)
        {
            _source.DataSource = new List<Row>();
            _summary.Text = serverRunning ? "Waiting for the first battleground report from the server…" : "Server stopped — no battleground activity.";
            return;
        }
        var rows = battlegrounds.Select(bg => new Row(bg.Name, $"{bg.MinLevel}–{bg.MaxLevel}",
            string.IsNullOrEmpty(bg.CentralKeep) ? "(none)" : bg.CentralKeep, string.IsNullOrEmpty(bg.Owner) ? "—" : bg.Owner,
            bg.AlbionInside, bg.MidgardInside, bg.HiberniaInside, bg.AlbionInside + bg.MidgardInside + bg.HiberniaInside,
            $"{bg.AlbionTravelling} / {bg.MidgardTravelling} / {bg.HiberniaTravelling}")).ToList();
        _source.DataSource = rows;
        int inside = rows.Sum(row => row.Total);
        int active = rows.Count(row => row.Total > 0);
        _summary.Text = serverRunning
            ? $"{inside} bots inside {active} of {rows.Count} battlegrounds"
            : "Server stopped — last known battleground state shown.";
    }
}
