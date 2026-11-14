using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("0.0.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.0.1.0")]

namespace SeccFirmwarePackager
{
    // Retains native Button keyboard activation, accessibility and Click semantics.
    internal sealed class RaisedButton : Button
    {
        private bool pressed;
        private bool hovered;
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
            TextRenderer.DrawText(e.Graphics, Text, Font, r, Enabled ? ForeColor : SystemColors.GrayText,
                (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) { r.Inflate(-4, -4); ControlPaint.DrawFocusRectangle(e.Graphics, r, ForeColor, face); }
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
