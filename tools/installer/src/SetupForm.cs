using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LivingServerSetup
{
    /// <summary>The borderless setup window: hosts one page at a time and its minimize/close buttons.</summary>
    internal sealed class SetupForm : Form
    {
        public const int W = 960, H = 600;
        readonly WindowButton min = new WindowButton(false), close = new WindowButton(true);
        public Page Current { get; private set; }
        public readonly Wizard Wizard;

        public SetupForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(W), Theme.S(H));
            BackColor = Theme.Void;
            Text = "Offline DAoC: Living Server Setup";
            try { Icon = Theme.AppIcon(); } catch { }
            DoubleBuffered = true;
            KeyPreview = true;
            min.Click += (s, e) => WindowState = FormWindowState.Minimized;
            close.Click += (s, e) => Close();
            Wizard = new Wizard(this);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000;          // WS_MINIMIZEBOX: taskbar minimize/restore for a borderless window
                cp.ClassStyle |= 0x00020000;     // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!Program.IsWindows) return;
            try
            {
                int round = 2; // DWMWCP_ROUND: rounded corners on Windows 11
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
                int dark = 1;  // dark title-bar hints for the taskbar preview
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public void BeginDrag()
        {
            if (!Program.IsWindows) return;
            try { ReleaseCapture(); SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero); } catch { }
        }

        public void Show(Page page)
        {
            SuspendLayout();
            var old = Current;
            Current = page;
            page.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            Controls.Add(page);
            page.Controls.Add(min);
            page.Controls.Add(close);
            min.Location = new Point(ClientSize.Width - Theme.S(8 + 34 + 6 + 34), Theme.S(8));
            close.Location = new Point(ClientSize.Width - Theme.S(8 + 34), Theme.S(8));
            min.BringToFront(); close.BringToFront();
            page.BringToFront();
            if (old != null) { Controls.Remove(old); old.Dispose(); }
            ResumeLayout();
            page.Entered();
            page.Focus();
            page.SelectNextControl(page, true, true, true, true);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Current != null && !Current.CanClose()) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.GoldDeep)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>A full-window page. Drawn in OnPaintBackground so the themed controls show the art behind them.</summary>
    internal abstract class Page : Control
    {
        protected readonly SetupForm Form;

        protected Page(SetupForm form)
        {
            Form = form;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Void;
            TabStop = false;
        }

        public virtual void Entered() { }
        public virtual bool CanClose() => true;
        protected virtual bool IsDragArea(Point p) => p.Y < Theme.S(110);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && IsDragArea(e.Location)) Form.BeginDrag();
        }

        /// <summary>Places a control at logical coordinates.</summary>
        protected T Put<T>(T c, float x, float y, float w = -1, float h = -1) where T : Control
        {
            c.Location = new Point(Theme.S(x), Theme.S(y));
            if (w >= 0) c.Width = Theme.S(w);
            if (h >= 0) c.Height = Theme.S(h);
            Controls.Add(c);
            return c;
        }

        protected Rectangle R(float x, float y, float w, float h) => Theme.S(x, y, w, h);
    }

    /// <summary>The pages with the banner, a title, and the footer with the step crumbs and buttons.</summary>
    internal abstract class FramedPage : Page
    {
        protected string Title, Subtitle;
        protected int Crumb;  // 0 Choose, 1 Prepare, 2 Install, 3 Play
        protected readonly GoldButton Next = new GoldButton("NEXT", true);
        protected readonly GoldButton Back = new GoldButton("BACK");

        protected FramedPage(SetupForm form, string title, string subtitle, int crumb) : base(form)
        {
            Title = title; Subtitle = subtitle; Crumb = crumb;
            Put(Next, SetupForm.W - 40 - 168, 550);
            Put(Back, SetupForm.W - 40 - 168 - 12 - 132, 550);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            var all = ClientRectangle;
            Theme.FillStone(g, all);
            PaintBody(g);
            var header = Theme.Img("header.jpg");
            Theme.DrawCover(g, header, R(0, 0, SetupForm.W, 110), 0f, 0.5f);
            var ver = "Living Server " + Payload.DisplayName + "  ·  for Offline DAoC " + Payload.FitsLabel;
            Theme.DrawText(g, ver, Theme.Serif(8.25f, FontStyle.Italic), R(560, 76, 380, 20), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.SingleLine);

            if (!string.IsNullOrEmpty(Title))
            {
                Theme.DrawShadowText(g, Title, Theme.Serif(17f, FontStyle.Bold), R(40, 122, 880, 34), Theme.GoldLight, TextFormatFlags.Left | TextFormatFlags.Top);
                if (!string.IsNullOrEmpty(Subtitle))
                    Theme.DrawText(g, Subtitle, Theme.Serif(9.5f), R(41, 156, 880, 22), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            PaintFooter(g);
        }

        /// <summary>The area between the banner and the footer, under the title: a soft light from the banner by default.</summary>
        protected virtual void PaintBody(Graphics g)
        {
            using (var lb = Theme.VGrad(R(0, 100, SetupForm.W, 200), Color.FromArgb(40, 194, 157, 77), Color.FromArgb(0, 194, 157, 77)))
                g.FillRectangle(lb, R(0, 110, SetupForm.W, 190));
        }

        void PaintFooter(Graphics g)
        {
            // footer
            var foot = R(0, 538, SetupForm.W, 62);
            using (var b = new SolidBrush(Color.FromArgb(235, 14, 12, 10))) g.FillRectangle(b, foot);
            Theme.Rule(g, 0, Width, foot.Top, 170);
            var names = new[] { "CHOOSE", "PREPARE", "INSTALL", "PLAY" };
            int x = Theme.S(40);
            var f = Theme.Serif(8.5f, FontStyle.Bold);
            for (int i = 0; i < names.Length; i++)
            {
                var col = i == Crumb ? Theme.GoldBright : i < Crumb ? Theme.Gold : Theme.Faint;
                var sz = TextRenderer.MeasureText(names[i], f);
                var r = new Rectangle(x, foot.Top + Theme.S(21), sz.Width, sz.Height);
                TextRenderer.DrawText(g, names[i], f, r, col, TextFormatFlags.NoPrefix);
                if (i == Crumb)
                    using (var pen = new Pen(Theme.Gold, Theme.S(2))) g.DrawLine(pen, r.Left, r.Bottom + Theme.S(3), r.Right, r.Bottom + Theme.S(3));
                x = r.Right + Theme.S(10);
                if (i < names.Length - 1)
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    Theme.Diamond(g, new Point(x + Theme.S(6), r.Top + r.Height / 2), Theme.S(3), i < Crumb ? Theme.Gold : Theme.Iron);
                    x += Theme.S(22);
                }
            }
        }
    }
}
