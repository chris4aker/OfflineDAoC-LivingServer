using System.Text.RegularExpressions;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The launcher's About (issue #641): the product's name and build, the "not affiliated" notice, the GPL v3 license and
/// every project and person whose work is in it, read from CREDITS.md (embedded at build time, so it is the same text).
/// </summary>
internal sealed class AboutForm : Form
{
    internal const string ResourceName = "OfflineDaoc.Launcher.CREDITS.md";

    /// <summary>CREDITS.md as embedded in this launcher, or a short fallback naming the license and the upstream work.</summary>
    internal static string CreditsMarkdown()
    {
        using Stream? stream = typeof(AboutForm).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            return "DAoC - Living Server is free software under the GNU General Public License, version 3. It is built on " +
                   "Dawn of Light, OpenDAoC and the original Offline DAoC by shadowofze. Not affiliated with Electronic Arts or Broadsword.";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Markdown reduced to plain text for a text box: headings and emphasis go, a link keeps its text and address.</summary>
    internal static string PlainText(string markdown)
    {
        var lines = new List<string>();
        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw;
            line = Regex.Replace(line, @"^#{1,6}\s+", "");
            line = Regex.Replace(line, @"^>\s?", "");
            line = Regex.Replace(line, @"\[([^\]]+)\]\((https?://[^)]+)\)", "$1 ($2)");
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]*\)", "$1");
            line = line.Replace("**", "").Replace("`", "");
            lines.Add(line);
        }
        return string.Join(Environment.NewLine, lines).Trim();
    }

    internal static string Title => "About " + MainForm.ProductName;

    internal static string Heading() => MainForm.ProductName + "  ·  " + MainForm.DisplayVersion;

    public AboutForm()
    {
        Text = Title;
        ClientSize = new Size(720, 560);
        MinimumSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = DaocTheme.Void;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            AutoSize = true, Text = Heading(), Font = new Font("Georgia", 12f, FontStyle.Bold), ForeColor = DaocTheme.GoldLight,
            Margin = new Padding(0, 0, 0, 8),
        }, 0, 0);
        layout.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
            BackColor = Color.FromArgb(24, 20, 16), ForeColor = DaocTheme.Text, BorderStyle = BorderStyle.FixedSingle,
            Text = PlainText(CreditsMarkdown()),
        }, 0, 1);
        var close = new Button { Text = "CLOSE", AutoSize = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
        layout.Controls.Add(close, 0, 2);
        AcceptButton = close;
        CancelButton = close;
    }
}
