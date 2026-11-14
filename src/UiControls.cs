using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("0.0.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.0.2.0")]

namespace SeccFirmwarePackager
{
    internal enum ButtonGlyph { None, Folder, Clear, FolderDownload }

    // Retains native Button keyboard activation, accessibility and Click semantics.
    internal sealed class RaisedButton : Button
    {
        private bool pressed;
        private bool hovered;
        private ButtonGlyph glyph;
        private bool iconOnly;
        public ButtonGlyph Glyph
        {
            get { return glyph; }
            set
            {
                glyph = value;
                if (String.IsNullOrEmpty(AccessibleName) && value != ButtonGlyph.None)
                    AccessibleName = value == ButtonGlyph.Clear ? "清除" : value == ButtonGlyph.FolderDownload ? "选择输出位置" : "选择文件";
                Invalidate();
            }
        }
        // Keep Text as a meaningful caption for accessibility even when not painted.
        public bool IconOnly { get { return iconOnly; } set { iconOnly = value; Invalidate(); } }
        public RaisedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 8 || Height < 8) return;
            e.Graphics.Clear(Parent == null ? UiColors.Canvas : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(1, pressed ? 3 : 1, Width - 4, Height - 6);
            Color face = Enabled ? BackColor : Color.FromArgb(237, 237, 232);
            if (hovered && Enabled) face = ControlPaint.Light(face, .12F);
            if (pressed && Enabled) face = ControlPaint.Dark(face, .04F);
            using (GraphicsPath path = Round(r))
            {
                if (!pressed && Enabled)
                {
                    using (GraphicsPath shadow = (GraphicsPath)path.Clone())
                    using (Matrix move = new Matrix())
                    {
                        move.Translate(0, 2); shadow.Transform(move);
                        using (Brush brush = new SolidBrush(Color.FromArgb(28, 72, 69, 60))) e.Graphics.FillPath(brush, shadow);
                    }
                }
                using (Brush brush = new SolidBrush(face)) e.Graphics.FillPath(brush, path);
                using (Pen pen = new Pen(Color.FromArgb(216, 218, 207))) e.Graphics.DrawPath(pen, path);
            }
            Rectangle textRect = r;
            if (glyph != ButtonGlyph.None)
            {
                float size = Math.Min(22F * e.Graphics.DpiX / 96F, Math.Min(r.Width - 8F, r.Height - 8F));
                if (size >= 4)
                {
                    float x = iconOnly ? r.Left + (r.Width - size) / 2F : r.Left + 8F;
                    float y = r.Top + (r.Height - size) / 2F;
                    DrawGlyph(e.Graphics, new RectangleF(x, y, size, size), Enabled ? ForeColor : SystemColors.GrayText);
                    if (!iconOnly) { int offset = (int)Math.Ceiling(size + 12); textRect.X += offset; textRect.Width -= offset; }
                }
            }
            if (!iconOnly || glyph == ButtonGlyph.None)
                TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Enabled ? ForeColor : SystemColors.GrayText,
                    (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) { r.Inflate(-4, -4); ControlPaint.DrawFocusRectangle(e.Graphics, r, ForeColor, face); }
        }
        private void DrawGlyph(Graphics graphics, RectangleF r, Color color)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            using (Pen pen = new Pen(color, Math.Max(1.2F, w / 14F)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                if (glyph == ButtonGlyph.Clear)
                {
                    graphics.DrawLine(pen, x + w * .22F, y + h * .22F, x + w * .78F, y + h * .78F);
                    graphics.DrawLine(pen, x + w * .78F, y + h * .22F, x + w * .22F, y + h * .78F);
                    return;
                }
                PointF[] outline = {
                    new PointF(x + w * .08F, y + h * .82F), new PointF(x + w * .08F, y + h * .17F),
                    new PointF(x + w * .36F, y + h * .17F), new PointF(x + w * .47F, y + h * .31F),
                    new PointF(x + w * .91F, y + h * .31F), new PointF(x + w * .91F, y + h * .82F),
                    new PointF(x + w * .08F, y + h * .82F)
                };
                graphics.DrawLines(pen, outline);
                if (glyph == ButtonGlyph.FolderDownload)
                {
                    graphics.DrawLine(pen, x + w * .65F, y + h * .46F, x + w * .65F, y + h * .88F);
                    graphics.DrawLine(pen, x + w * .50F, y + h * .73F, x + w * .65F, y + h * .88F);
                    graphics.DrawLine(pen, x + w * .80F, y + h * .73F, x + w * .65F, y + h * .88F);
                }
            }
        }

        private static GraphicsPath Round(Rectangle r)
        {
            int d = Math.Min(12, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }

    // Placeholder never enters Text: validation, copying and package metadata see
    // only user input. Native Windows cue banners remain compatible with .NET 4.
    internal sealed class PlaceholderTextBox : TextBox
    {
        private string placeholder = "";
        private static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);
        public string PlaceholderText
        {
            get { return placeholder; }
            set { placeholder = value ?? ""; ApplyPlaceholder(); Invalidate(); }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); ApplyPlaceholder();
        }
        private void ApplyPlaceholder()
        {
            if (IsHandleCreated && IsWindows)
                SendMessage(Handle, 0x1501 /* EM_SETCUEBANNER */, IntPtr.Zero, placeholder);
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            // Mono has no EM_SETCUEBANNER support. Paint a read-only visual cue
            // after its normal single-line edit painting, without changing Text.
            if (!IsWindows && message.Msg == 0x000F && !Focused && TextLength == 0 &&
                !Multiline && !String.IsNullOrEmpty(placeholder))
            {
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                {
                    Rectangle bounds = ClientRectangle; bounds.Inflate(-1, 0);
                    TextRenderer.DrawText(graphics, placeholder, Font, bounds, UiColors.Muted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
        }
    }

    // Cosmetic feedback only: unavailable audio must never affect packaging or FTP.
    internal sealed class UiSounds : IDisposable
    {
        private readonly SoundPlayer[] players = new SoundPlayer[3];
        private readonly Stream[] streams = new Stream[3];
        private bool enabled = true;
        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; if (!value) foreach (SoundPlayer player in players) if (player != null) try { player.Stop(); } catch (Exception) { } }
        }
        public UiSounds()
        {
            string[] names = { "click", "success", "failure" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    streams[i] = typeof(UiSounds).Assembly.GetManifestResourceStream("Sounds." + names[i] + ".wav");
                    if (streams[i] != null) { players[i] = new SoundPlayer(streams[i]); players[i].Load(); }
                }
                catch (Exception) { if (players[i] != null) players[i].Dispose(); players[i] = null; }
            }
        }
        public void Play(string name)
        {
            int index = name == "success" ? 1 : name == "failure" ? 2 : 0;
            if (enabled && players[index] != null) try { players[index].Play(); } catch (Exception) { }
        }
        public void Dispose()
        {
            Enabled = false;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null) players[i].Dispose();
                if (streams[i] != null) streams[i].Dispose();
            }
        }
    }
}
