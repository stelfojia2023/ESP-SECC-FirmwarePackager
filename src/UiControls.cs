using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("0.0.4.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.0.4.0")]

namespace SeccFirmwarePackager
{
    internal enum ButtonGlyph { None, Folder, Clear, FolderDownload }

    // Authoritative button paint metrics. Distances are device pixels except
    // GlyphSize, which is scaled from BaseDpi; glyph coordinates are fractions.
    internal static class UiMetrics
    {
        public const int ButtonMinSize = 8, ButtonLeft = 1, ButtonTop = 1;
        public const int ButtonPressOffset = 2, ButtonWidthTrim = 4, ButtonHeightTrim = 6;
        public const int ButtonCornerDiameter = 12, ButtonRimInset = 1, ButtonRimMinSize = 2, ButtonFocusInset = 4;
        public const float ButtonStroke = 1F;
        public const float DisabledBlend = .65F, HoverBlend = .10F;
        public const float TopBlend = .48F, PressedTopBlend = .08F, BottomBlend = .08F, PressedBottomBlend = .12F;
        public const float EdgeBlend = .15F, PressedEdgeBlend = .24F;
        public const int ShadowLayers = 3, PressedShadowLayers = 1, ShadowStep = 1;
        public const int ShadowOuterAlpha = 6, ShadowAlphaStep = 5, PressedShadowAlpha = 12;
        public const int RimTopAlpha = 220, PressedRimTopAlpha = 100, RimBottomAlpha = 18;
        public const float BaseDpi = 96F, GlyphSize = 22F, GlyphPadding = 8F, GlyphMinSize = 4F, GlyphTextGap = 12F;
        public const float GlyphMinStroke = 1.2F, GlyphStrokeDivisor = 14F;
        public const float CrossNear = .22F, CrossFar = .78F;
        public const float FolderLeft = .08F, FolderBottom = .82F, FolderTop = .17F;
        public const float FolderTabEnd = .36F, FolderShoulder = .47F, FolderBodyTop = .31F, FolderRight = .91F;
        public const float ArrowX = .65F, ArrowTop = .46F, ArrowBottom = .88F;
        public const float ArrowLeft = .50F, ArrowRight = .80F, ArrowShoulder = .73F;
        public const int ArcLeft = 180, ArcTop = 270, ArcRight = 0, ArcBottom = 90, ArcSweep = 90;
    }

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
            if (Width < UiMetrics.ButtonMinSize || Height < UiMetrics.ButtonMinSize) return;
            e.Graphics.Clear(OpaqueBackground());
            if (Parent != null)
            {
                GraphicsState backgroundState = e.Graphics.Save();
                try
                {
                    e.Graphics.TranslateTransform(-Left, -Top);
                    InvokePaintBackground(Parent, new PaintEventArgs(e.Graphics, new Rectangle(Left, Top, Width, Height)));
                }
                finally { e.Graphics.Restore(backgroundState); }
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool down = pressed && Enabled;
            Rectangle r = new Rectangle(UiMetrics.ButtonLeft, UiMetrics.ButtonTop + (down ? UiMetrics.ButtonPressOffset : 0), Width - UiMetrics.ButtonWidthTrim, Height - UiMetrics.ButtonHeightTrim);
            Color face = Enabled ? BackColor : Blend(BackColor, UiColors.ButtonDisabledTint, UiMetrics.DisabledBlend);
            if (hovered && Enabled) face = Blend(face, UiColors.ButtonHighlight, UiMetrics.HoverBlend);
            Color top = Blend(face, UiColors.ButtonHighlight, down ? UiMetrics.PressedTopBlend : UiMetrics.TopBlend);
            Color bottom = Blend(face, UiColors.ButtonShadeTint, down ? UiMetrics.PressedBottomBlend : UiMetrics.BottomBlend);
            using (GraphicsPath path = Round(r))
            {
                // Three restrained translucent layers approximate a soft contact
                // shadow without a bitmap, and collapse when the button is pressed.
                if (Enabled)
                {
                    for (int layer = down ? UiMetrics.PressedShadowLayers : UiMetrics.ShadowLayers; layer >= 1; layer--)
                    {
                        using (GraphicsPath shadow = (GraphicsPath)path.Clone())
                        using (Matrix move = new Matrix())
                        {
                            move.Translate(0, layer * UiMetrics.ShadowStep); shadow.Transform(move);
                            using (Brush brush = new SolidBrush(Color.FromArgb(down ? UiMetrics.PressedShadowAlpha : UiMetrics.ShadowOuterAlpha + (UiMetrics.ShadowLayers - layer) * UiMetrics.ShadowAlphaStep, UiColors.ButtonShadow)))
                                e.Graphics.FillPath(brush, shadow);
                        }
                    }
                }
                using (LinearGradientBrush brush = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical))
                    e.Graphics.FillPath(brush, path);
                using (Pen edge = new Pen(Blend(UiColors.Border, UiColors.Ink, down ? UiMetrics.PressedEdgeBlend : UiMetrics.EdgeBlend), UiMetrics.ButtonStroke))
                    e.Graphics.DrawPath(edge, path);
                // A fine inner light edge gives the silver surface a small amount
                // of material definition, not a thick bevel or glossy highlight.
                Rectangle inner = r; inner.Inflate(-UiMetrics.ButtonRimInset, -UiMetrics.ButtonRimInset);
                if (inner.Width > UiMetrics.ButtonRimMinSize && inner.Height > UiMetrics.ButtonRimMinSize)
                {
                    using (GraphicsPath rim = Round(inner))
                    using (LinearGradientBrush light = new LinearGradientBrush(inner,
                        Color.FromArgb(down ? UiMetrics.PressedRimTopAlpha : UiMetrics.RimTopAlpha, UiColors.ButtonHighlight), Color.FromArgb(UiMetrics.RimBottomAlpha, UiColors.ButtonHighlight), LinearGradientMode.Vertical))
                    using (Pen pen = new Pen(light, UiMetrics.ButtonStroke)) e.Graphics.DrawPath(pen, rim);
                }
            }
            Rectangle textRect = r;
            if (glyph != ButtonGlyph.None)
            {
                float size = Math.Min(UiMetrics.GlyphSize * e.Graphics.DpiX / UiMetrics.BaseDpi, Math.Min(r.Width - UiMetrics.GlyphPadding, r.Height - UiMetrics.GlyphPadding));
                if (size >= UiMetrics.GlyphMinSize)
                {
                    float x = iconOnly ? r.Left + (r.Width - size) / 2F : r.Left + UiMetrics.GlyphPadding;
                    float y = r.Top + (r.Height - size) / 2F;
                    DrawGlyph(e.Graphics, new RectangleF(x, y, size, size), Enabled ? ForeColor : SystemColors.GrayText);
                    if (!iconOnly) { int offset = (int)Math.Ceiling(size + UiMetrics.GlyphTextGap); textRect.X += offset; textRect.Width -= offset; }
                }
            }
            if (!iconOnly || glyph == ButtonGlyph.None)
                TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Enabled ? ForeColor : SystemColors.GrayText,
                    (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) { r.Inflate(-UiMetrics.ButtonFocusInset, -UiMetrics.ButtonFocusInset); ControlPaint.DrawFocusRectangle(e.Graphics, r, ForeColor, face); }
        }
        private Color OpaqueBackground()
        {
            for (Control ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor.BackColor.A == 255) return ancestor.BackColor;
            return UiColors.Canvas;
        }
        private static Color Blend(Color source, Color target, float amount)
        {
            return Color.FromArgb(
                (int)Math.Round(source.R + (target.R - source.R) * amount),
                (int)Math.Round(source.G + (target.G - source.G) * amount),
                (int)Math.Round(source.B + (target.B - source.B) * amount));
        }
        private void DrawGlyph(Graphics graphics, RectangleF r, Color color)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            using (Pen pen = new Pen(color, Math.Max(UiMetrics.GlyphMinStroke, w / UiMetrics.GlyphStrokeDivisor)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                if (glyph == ButtonGlyph.Clear)
                {
                    graphics.DrawLine(pen, x + w * UiMetrics.CrossNear, y + h * UiMetrics.CrossNear, x + w * UiMetrics.CrossFar, y + h * UiMetrics.CrossFar);
                    graphics.DrawLine(pen, x + w * UiMetrics.CrossFar, y + h * UiMetrics.CrossNear, x + w * UiMetrics.CrossNear, y + h * UiMetrics.CrossFar);
                    return;
                }
                PointF[] outline = {
                    new PointF(x + w * UiMetrics.FolderLeft, y + h * UiMetrics.FolderBottom), new PointF(x + w * UiMetrics.FolderLeft, y + h * UiMetrics.FolderTop),
                    new PointF(x + w * UiMetrics.FolderTabEnd, y + h * UiMetrics.FolderTop), new PointF(x + w * UiMetrics.FolderShoulder, y + h * UiMetrics.FolderBodyTop),
                    new PointF(x + w * UiMetrics.FolderRight, y + h * UiMetrics.FolderBodyTop), new PointF(x + w * UiMetrics.FolderRight, y + h * UiMetrics.FolderBottom),
                    new PointF(x + w * UiMetrics.FolderLeft, y + h * UiMetrics.FolderBottom)
                };
                graphics.DrawLines(pen, outline);
                if (glyph == ButtonGlyph.FolderDownload)
                {
                    graphics.DrawLine(pen, x + w * UiMetrics.ArrowX, y + h * UiMetrics.ArrowTop, x + w * UiMetrics.ArrowX, y + h * UiMetrics.ArrowBottom);
                    graphics.DrawLine(pen, x + w * UiMetrics.ArrowLeft, y + h * UiMetrics.ArrowShoulder, x + w * UiMetrics.ArrowX, y + h * UiMetrics.ArrowBottom);
                    graphics.DrawLine(pen, x + w * UiMetrics.ArrowRight, y + h * UiMetrics.ArrowShoulder, x + w * UiMetrics.ArrowX, y + h * UiMetrics.ArrowBottom);
                }
            }
        }

        private static GraphicsPath Round(Rectangle r)
        {
            int d = Math.Min(UiMetrics.ButtonCornerDiameter, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, UiMetrics.ArcLeft, UiMetrics.ArcSweep); p.AddArc(r.Right - d, r.Top, d, d, UiMetrics.ArcTop, UiMetrics.ArcSweep);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, UiMetrics.ArcRight, UiMetrics.ArcSweep); p.AddArc(r.Left, r.Bottom - d, d, d, UiMetrics.ArcBottom, UiMetrics.ArcSweep);
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

