using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace DAoCLivingServer.Launcher;

/// <summary>
/// Scales the launcher for high-DPI screens (Windows display scaling, e.g. 150% on a 4K monitor)
/// and for Windows "Make text bigger" (Accessibility > Text size). The layout is written in
/// 96-DPI pixels; fonts are in points, so Windows already enlarges them for display scaling and
/// they only need the text-size factor. At 100% scaling and normal text size nothing changes.
/// </summary>
internal static class UiScale
{
    // Fonts this class produced, so a later pass never enlarges them twice.
    private static readonly ConditionalWeakTable<Font, object> ScaledFonts = new();

    /// <summary>Text-size factor: the manual launcher scale if set, else Windows "Make text bigger" (1.0 to 2.25).</summary>
    internal static float TextFactor { get; private set; } = ReadTextFactor();

    /// <summary>Layout factor for a control: display scaling times text size.</summary>
    internal static float Factor(Control control) => control.DeviceDpi / 96f * TextFactor;

    /// <summary>A 96-DPI pixel length at this control's scale.</summary>
    internal static int Px(Control control, int pixels) => (int)Math.Round(pixels * Factor(control));

    private static float ReadTextFactor()
    {
        // Manual extra size on top of Windows display scaling, for a 4K screen left at 100%:
        // a number such as 150 (or 1.5) in launcher-ui-scale.txt beside the launcher, or in
        // the OFFLINE_DAOC_UI_SCALE environment variable. Allowed 100 to 300 percent.
        string? manual = Environment.GetEnvironmentVariable("OFFLINE_DAOC_UI_SCALE");
        try
        {
            string file = Path.Combine(AppContext.BaseDirectory, "launcher-ui-scale.txt");
            if (string.IsNullOrWhiteSpace(manual) && File.Exists(file))
                manual = File.ReadAllText(file);
        }
        catch (Exception)
        {
            // Unreadable file: ignore it.
        }
        if (float.TryParse(manual?.Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            float factor = value > 5 ? value / 100f : value;
            if (factor is >= 1f and <= 3f)
                return factor;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
            if (key?.GetValue("TextScaleFactor") is int percent && percent is >= 100 and <= 225)
                return percent / 100f;
        }
        catch (Exception)
        {
            // No access to the setting: keep normal text size.
        }
        return 1f;
    }

    /// <summary>Scales a fully built form once, before it is shown.</summary>
    internal static void Apply(Form form)
    {
        // Never make the window's minimum size larger than the screen: cap the extra text size
        // (Windows display scaling itself is left alone).
        Rectangle screen = Screen.FromControl(form).WorkingArea;
        float dpi = form.DeviceDpi / 96f;
        float fits = Math.Min(screen.Width / (float)Math.Max(1, form.MinimumSize.Width),
            screen.Height / (float)Math.Max(1, form.MinimumSize.Height));
        TextFactor = Math.Max(1f, Math.Min(TextFactor, fits / dpi));
        float factor = Factor(form);
        if (Math.Abs(factor - 1f) < 0.01f)
            return;
        ScaleTree(form, factor);

        // Keep the larger window on screen.
        Rectangle area = Screen.FromControl(form).WorkingArea;
        form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width), Math.Min(form.MinimumSize.Height, area.Height));
        form.Size = new Size(Math.Min(form.Width, area.Width), Math.Min(form.Height, area.Height));
    }

    /// <summary>
    /// Scales a control built after the form was shown (refreshed group cards). Call it after
    /// the control is added to its parent, so inherited fonts already come from the scaled form.
    /// </summary>
    internal static void ApplyToNewControl(Control control)
    {
        float factor = Factor(control);
        if (Math.Abs(factor - 1f) < 0.01f)
            return;
        ScaleTree(control, factor);
    }

    private static void ScaleTree(Control root, float factor)
    {
        var scaledFonts = new Dictionary<Font, Font>();
        Font ScaleFont(Font font)
        {
            if (TextFactor == 1f || ScaledFonts.TryGetValue(font, out _))
                return font;
            if (!scaledFonts.TryGetValue(font, out Font? scaled))
            {
                scaled = new Font(font.FontFamily, font.Size * TextFactor, font.Style, font.Unit);
                scaledFonts[font] = scaled;
                ScaledFonts.AddOrUpdate(scaled, scaled);
            }
            return scaled;
        }

        // Parents first: a child that inherits its font then already reports the new one,
        // and only explicitly set fonts are replaced.
        foreach (Control control in PreOrder(root))
        {
            Font font = ScaleFont(control.Font);
            if (!ReferenceEquals(font, control.Font))
                control.Font = font;

            switch (control)
            {
                case TabControl { SizeMode: TabSizeMode.Fixed } tabs:
                    tabs.ItemSize = new Size((int)Math.Round(tabs.ItemSize.Width * factor), (int)Math.Round(tabs.ItemSize.Height * factor));
                    break;
                case DataGridView grid:
                    ScaleGrid(grid, factor, ScaleFont);
                    break;
            }
        }

        root.Scale(new SizeF(factor, factor));
    }

    private static void ScaleGrid(DataGridView grid, float factor, Func<Font, Font> scaleFont)
    {
        int Scaled(int value) => (int)Math.Round(value * factor);
        grid.RowTemplate.Height = Scaled(grid.RowTemplate.Height);
        if (grid.ColumnHeadersHeightSizeMode != DataGridViewColumnHeadersHeightSizeMode.AutoSize)
            grid.ColumnHeadersHeight = Scaled(grid.ColumnHeadersHeight);
        foreach (DataGridViewCellStyle? style in new[] { grid.DefaultCellStyle, grid.ColumnHeadersDefaultCellStyle,
                     grid.RowsDefaultCellStyle, grid.AlternatingRowsDefaultCellStyle })
            if (style?.Font != null)
                style.Font = scaleFont(style.Font);
        foreach (DataGridViewColumn column in grid.Columns)
            ScaleColumn(column, factor);
        // Columns added later (data binding) start from the same 96-DPI widths.
        grid.ColumnAdded += (_, e) => ScaleColumn(e.Column, factor);
    }

    private static void ScaleColumn(DataGridViewColumn column, float factor)
    {
        column.MinimumWidth = Math.Max(2, (int)Math.Round(column.MinimumWidth * factor));
        column.Width = Math.Max(column.MinimumWidth, (int)Math.Round(column.Width * factor));
    }

    private static IEnumerable<Control> PreOrder(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (Control nested in PreOrder(child))
                yield return nested;
    }
}
