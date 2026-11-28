using System.Reflection;
using NUnit.Framework;

/// <summary>
/// Living Server 1.2 (owner's PC, build b1b6846, 2026-11-18 ~05:57): with the launcher maximized, clicks on STOP SERVER and
/// ENTER REALM did nothing, and after restoring the window STOP SERVER worked first try. These tests lay the real launcher
/// out at its normal size and at maximized sizes up to 4K and check, with Windows' own child-window hit test, that the
/// header's buttons are the top-most control at their centres and lie wholly inside every control that holds them. So
/// nothing in the launcher (an overlay, a panel stretched over them on resize, a fixed-size image) covers them at any size.
/// The cause on the PC was outside the launcher (see docs/KNOWN-ISSUES.md, "A maximized launcher ignores clicks").
/// </summary>
[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class LauncherWindowSizeTests
{
    private static readonly Assembly Launcher = Assembly.Load("DAoCLivingServer");
    private static readonly BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] HeaderButtons = ["_startButton", "_stopButton", "_playButton", "_joinFriendButton", "_hostFriendsButton"];

    // The normal window (MainForm's Size), its minimum, and maximized on 1080p, 1440p and 4K screens (work area less the taskbar).
    [TestCase(1100, 700)]
    [TestCase(960, 620)]
    [TestCase(1920, 1040)]
    [TestCase(2560, 1400)]
    [TestCase(3840, 2100)]
    public void HeaderButtonsAreTheTopMostControlAtTheirCentresAtEverySize(int width, int height)
    {
        WithLauncher(width, height, (root, form, type) =>
        {
            foreach (string field in HeaderButtons)
            {
                var button = (Button)type.GetField(field, HiddenInstance)!.GetValue(form)!;
                Assert.That(button.Visible, Is.True, $"{button.Text} is shown at {width}x{height}");
                AssertInsideEveryParent(root, button, $"{width}x{height}");
                Point centre = root.PointToClient(button.PointToScreen(new Point(button.Width / 2, button.Height / 2)));
                Control hit = TopMostAt(root, centre);
                Assert.That(hit, Is.SameAs(button),
                    $"At {width}x{height} a click on the centre of {button.Text} lands on {Describe(hit)}, not on the button");
            }
        });
    }

    /// <summary>
    /// Issue #795: the update status line had only the width left of the update controls (about 65 px), so its messages
    /// showed only as a hover tooltip. At every size it is now wide and tall enough for its longest usual message, and
    /// no control that holds it cuts it off.
    /// </summary>
    [TestCase(1100, 700)]
    [TestCase(960, 620)]
    [TestCase(1920, 1040)]
    public void TheUpdateStatusLineHasRoomForItsMessages(int width, int height)
    {
        if (DAoCLivingServer.Updates.LauncherBuild.Version == null)
            Assert.Ignore("This build has no Living Server name, so the launcher has no update strip.");
        WithLauncher(width, height, (root, form, type) =>
        {
            var status = (Label)type.GetField("_updateStatus", HiddenInstance)!.GetValue(form)!;
            const string message = "The update to 1.2.3 didn't install, and the game is as it was.";
            Size needed = TextRenderer.MeasureText(message, status.Font);
            Assert.That(status.Width, Is.GreaterThanOrEqualTo(needed.Width), $"status line width at {width}x{height}");
            Assert.That(status.Height, Is.GreaterThanOrEqualTo(needed.Height), $"status line height at {width}x{height}");
            AssertInsideEveryParent(root, status, $"{width}x{height}");
        });
    }

    /// <summary>The header-button check is not empty: a transparent panel laid over the buttons (the kind of overlay looked for) is found.</summary>
    [Test]
    public void TheHitTestFindsAnOverlayOverTheButtons()
    {
        WithLauncher(1920, 1040, (root, form, type) =>
        {
            var stop = (Button)type.GetField("_stopButton", HiddenInstance)!.GetValue(form)!;
            // Laid over the whole launcher (a layout panel in between would place it in a cell of its own).
            var overlay = new Panel { BackColor = Color.Transparent, Bounds = root.ClientRectangle };
            root.Controls.Add(overlay);
            overlay.BringToFront();
            overlay.CreateControl();
            Point centre = root.PointToClient(stop.PointToScreen(new Point(stop.Width / 2, stop.Height / 2)));
            Assert.That(TopMostAt(root, centre), Is.SameAs(overlay));
        });
    }

    private static void WithLauncher(int width, int height, Action<Control, Form, Type> check)
    {
        Type type = Launcher.GetType("DAoCLivingServer.Launcher.MainForm")!;
        // Windows caps any Form, even a child one, at this machine's largest window size (about the screen size; on CI
        // 1044x788). So the launcher's controls are moved, in their z-order, onto a plain panel of the size under test:
        // their docking and anchoring then lay them out exactly as the form's client area of that size would. The
        // message loop is never pumped, so the launcher's Shown handler (database reads, timers) never runs.
        using var host = new Form
        {
            ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20_000, -20_000),
            ClientSize = new Size(200, 200), FormBorderStyle = FormBorderStyle.None,
        };
        using var form = (Form)Activator.CreateInstance(type)!;
        var root = new Panel { Location = Point.Empty, Size = new Size(width, height), Padding = form.Padding, BackColor = form.BackColor };
        host.Controls.Add(root);
        Control[] controls = form.Controls.Cast<Control>().ToArray();
        root.SuspendLayout();
        root.Controls.AddRange(controls);
        root.ResumeLayout(true);
        host.Show();
        LayoutAll(root);
        Assert.That(root.ClientSize, Is.EqualTo(new Size(width, height)), "the launcher's client area has the size under test");
        check(root, form, type);
    }

    /// <summary>The deepest visible child window under a point, by Windows' own child-window hit test, level by level.</summary>
    private static Control TopMostAt(Control root, Point clientPoint)
    {
        Control current = root;
        Point point = clientPoint;
        while (true)
        {
            Control child = current.GetChildAtPoint(point, GetChildAtPointSkip.Invisible);
            if (child == null)
                return current;
            point = child.PointToClient(current.PointToScreen(point));
            current = child;
        }
    }

    private static void AssertInsideEveryParent(Control root, Control control, string size)
    {
        Rectangle screen = control.RectangleToScreen(control.ClientRectangle);
        for (Control parent = control.Parent; parent != null && parent != root.Parent; parent = parent.Parent)
        {
            Rectangle inParent = parent.RectangleToClient(screen);
            Assert.That(parent.ClientRectangle.Contains(inParent), Is.True,
                $"At {size} {control.Text} ({inParent}) is cut off by {Describe(parent)} ({parent.ClientRectangle})");
        }
    }

    private static string Describe(Control control) =>
        $"{control.GetType().Name}{(string.IsNullOrEmpty(control.Text) ? "" : $" '{control.Text}'")} at {control.Bounds}";

    private static void LayoutAll(Control root)
    {
        root.PerformLayout();
        foreach (Control child in root.Controls)
            LayoutAll(child);
    }
}
