using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SeccFirmwarePackager;

internal static class HeaderViewerTests
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CharRange { public int Min, Max; }
    [StructLayout(LayoutKind.Sequential)] private struct FormatRange
    { public IntPtr Hdc, TargetHdc; public NativeRect Area, Page; public CharRange Range; }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr FormatMessage(IntPtr window, uint message, IntPtr render, ref FormatRange range);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr ClearFormat(IntPtr window, uint message, IntPtr w, IntPtr l);
    private static void SaveEvidence(Form form, RichTextBox hex, string work)
    {
        File.WriteAllText(Path.Combine(work, "header-text.txt"), hex.Text, Encoding.UTF8);
        File.WriteAllText(Path.Combine(work, "header-selection.txt"),
            "SelectionStart=" + hex.SelectionStart + "\r\nSelectionLength=" + hex.SelectionLength +
            "\r\nSelectedText=" + hex.SelectedText + "\r\nSelectionBackColor=" + hex.SelectionBackColor +
            "\r\nFont=" + hex.Font + "\r\nReadOnly=" + hex.ReadOnly, Encoding.UTF8);
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            using (Bitmap picture = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(picture, new Rectangle(Point.Empty, form.Size)); picture.Save(Path.Combine(work, "header-viewer-mono.png")); }
            Console.WriteLine("Header evidence: Mono DrawToBitmap, not Windows desktop capture."); return;
        }
        // CopyFromScreen is genuine desktop evidence, but a hosted runner may have no active desktop.
        try
        {
            form.Activate(); form.Refresh(); Application.DoEvents();
            using (Bitmap desktop = new Bitmap(form.Width, form.Height))
            using (Graphics graphics = Graphics.FromImage(desktop))
            { graphics.CopyFromScreen(form.Location, Point.Empty, form.Size); desktop.Save(Path.Combine(work, "header-viewer-desktop-candidate.png")); }
            Console.WriteLine("Desktop capture saved as candidate; inspect for active desktop/occlusion before treating it as visual proof.");
        }
        catch (Exception ex) { Console.WriteLine("Desktop capture unavailable: " + ex.GetType().Name + ": " + ex.Message); }
        using (Bitmap picture = new Bitmap(form.Width, form.Height))
        using (Bitmap native = new Bitmap(hex.ClientSize.Width, hex.ClientSize.Height))
        {
            form.DrawToBitmap(picture, new Rectangle(Point.Empty, form.Size));
            using (Graphics graphics = Graphics.FromImage(native))
            {
                graphics.Clear(hex.BackColor);
                int width = (int)Math.Round(native.Width * 1440.0 / graphics.DpiX);
                int height = (int)Math.Round(native.Height * 1440.0 / graphics.DpiY);
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    FormatRange range = new FormatRange { Hdc = hdc, TargetHdc = hdc,
                        Area = new NativeRect { Right = width, Bottom = height },
                        Page = new NativeRect { Right = width, Bottom = height },
                        Range = new CharRange { Min = 0, Max = hex.TextLength } };
                    int end = FormatMessage(hex.Handle, 0x0439, new IntPtr(1), ref range).ToInt32();
                    Assert(end > 0, "native RichEdit rendered characters");
                }
                finally { ClearFormat(hex.Handle, 0x0439, IntPtr.Zero, IntPtr.Zero); graphics.ReleaseHdc(hdc); }
            }
            Point screenOrigin = hex.PointToScreen(Point.Empty);
            Point origin = new Point(screenOrigin.X - form.Left, screenOrigin.Y - form.Top);
            using (Graphics graphics = Graphics.FromImage(picture)) graphics.DrawImageUnscaled(native, origin);
            picture.Save(Path.Combine(work, "header-viewer-native-richtext.png"));
        }
        File.WriteAllText(Path.Combine(work, "render-method.txt"),
            "header-viewer-native-richtext.png uses the live RichEdit control EM_FORMATRANGE rendering over the form DrawToBitmap. It is NOT a desktop screenshot. Native print rendering may omit selection highlight; highlight is checked by actual selection assertions and recorded in header-selection.txt. Desktop candidate requires visual inspection.");
        Console.WriteLine("Saved native RichEdit EM_FORMATRANGE evidence (not a desktop screenshot).");
    }
    private static int count;
    private static void Assert(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
    private static void Fails(Action action, Type expected)
    { try { action(); } catch (Exception ex) { Assert(expected.IsInstanceOfType(ex), "expected " + expected.Name + " got " + ex); return; } throw new Exception("Expected failure"); }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string work = Path.GetFullPath(args[0]); Directory.CreateDirectory(work);
            byte[] original = new byte[8192]; for (int i = 0; i < original.Length; i++) original[i] = (byte)(i % 256);
            string path = Path.Combine(work, "header-fixture.bin"); File.WriteAllBytes(path, original);
            byte[] header = HeaderViewer.ReadHeader(path);
            Assert(header.Length == 4096, "only header returned");
            for (int i = 0; i < header.Length; i++) Assert(header[i] == original[i], "header byte " + i);
            Assert(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(original), "read leaves whole file unchanged");
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Write)) { file.Position = 4096; file.WriteByte(42); }
            Assert(Convert.ToBase64String(HeaderViewer.ReadHeader(path)) == Convert.ToBase64String(header), "payload changes have no effect on header snapshot");
            string shortPath = Path.Combine(work, "short.bin"); File.WriteAllBytes(shortPath, new byte[4095]);
            Fails(delegate { HeaderViewer.ReadHeader(shortPath); }, typeof(InvalidDataException));
            string exact = Path.Combine(work, "exact.bin"); File.WriteAllBytes(exact, header);
            Assert(HeaderViewer.ReadHeader(exact).Length == 4096, "exact header boundary accepted");
            Fails(delegate { HeaderViewer.ReadHeader(Path.Combine(work, "missing.bin")); }, typeof(FileNotFoundException));
            Fails(delegate { HeaderViewer.FormatHex(null); }, typeof(ArgumentException));
            Fails(delegate { HeaderViewer.FormatHex(new byte[4095]); }, typeof(ArgumentException));
            Fails(delegate { HeaderViewer.FormatHex(new byte[4097]); }, typeof(ArgumentException));
            string[] lines = HeaderViewer.FormatHex(header).Split('\n');
            Assert(lines.Length == 256, "256 sixteen-byte rows");
            Assert(lines[0] == "0000  00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F  |................|", "8+8 bytes and nonprintable ASCII");
            Assert(lines[2].EndsWith("| !\"#$%&'()*+,-./|"), "printable ASCII preserved");
            Assert(lines[255].StartsWith("0FF0  F0 F1 F2 F3 F4 F5 F6 F7  F8 F9 FA FB FC FD FE FF"), "last row ends exactly at FFF");
            int offset;
            foreach (string text in new[] { "0", "0000", "0x0000", "fff", "0X0FFF", " 0xfff " })
                Assert(HeaderViewer.TryParseOffset(text, out offset) && (offset == 0 || offset == 4095), "valid offset " + text);
            foreach (string text in new[] { null, "", "0x", "1000", "0x1000", "-1", "+1", "0xFFFF", "10000", "GG", "1.0" })
                Assert(!HeaderViewer.TryParseOffset(text, out offset), "invalid offset " + text);
            Application.EnableVisualStyles();
            using (HeaderViewer form = new HeaderViewer(header))
            {
                form.Show(); Application.DoEvents();
                TextBox address = (TextBox)form.Controls.Find("HeaderOffset", true)[0];
                RichTextBox hex = (RichTextBox)form.Controls.Find("HeaderHex", true)[0];
                Assert(hex.ReadOnly && !hex.WordWrap, "read-only non-wrapping hex");
                int lastChar = hex.Lines[0].Length - 1;
                int glyph = TextRenderer.MeasureText("|", hex.Font, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
                int textRight = hex.GetPositionFromCharIndex(lastChar).X + glyph;
                Assert(textRight <= hex.ClientSize.Width, "full hex and ASCII row fits without horizontal clipping");
                Assert(hex.ClientSize.Width - textRight <= Math.Max(32, hex.Font.Height * 2), "header right margin remains compact");
                Assert(form.MinimumSize.Width == form.Width, "minimum width retains the full header line");
                FlowLayoutPanel navigation = (FlowLayoutPanel)address.Parent;
                foreach (Control item in navigation.Controls)
                    Assert(item.Bounds.Right <= navigation.ClientSize.Width, "navigation remains fully visible");
                Size originalSize = form.Size;
                form.Size = form.MinimumSize; Application.DoEvents();
                Assert(hex.GetPositionFromCharIndex(lastChar).X + glyph <= hex.ClientSize.Width, "header row fits at minimum window size");
                form.Size = originalSize; Application.DoEvents();
                MethodInfo jump = typeof(HeaderViewer).GetMethod("JumpToAddress", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (int target in new[] { 0, 7, 8, 15, 16, 4095 })
                {
                    address.Text = "0x" + target.ToString("X4"); jump.Invoke(form, null); Application.DoEvents();
                    int col = target % 16;
                    int expected = hex.GetFirstCharIndexFromLine(target / 16) + 6 + 3 * col + (col >= 8 ? 1 : 0);
                    Assert(hex.SelectionStart == expected && hex.SelectionLength == 2, "exact byte highlight at " + target);
                    Assert(hex.SelectedText == header[target].ToString("X2"), "highlight matches byte at " + target);
                }
                int prior = hex.SelectionStart; address.Text = "0x1000"; jump.Invoke(form, null);
                Assert(hex.SelectionStart == prior, "invalid offset does not move existing highlight outside header");
                Assert(hex.Text == HeaderViewer.FormatHex(header), "navigation leaves header content unchanged");
                address.Text = "0x0008"; jump.Invoke(form, null); Application.DoEvents();
                SaveEvidence(form, hex, work);
                form.Close();
            }
            Console.WriteLine("PASS header viewer assertions=" + count + "; bounded-read implementation reviewed separately"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
