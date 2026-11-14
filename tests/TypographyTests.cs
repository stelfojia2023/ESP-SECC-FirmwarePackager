using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Diagnostic only: no OS DPI, smoothing or application settings are changed.
internal static class TypographyTests
{
    [DllImport("shcore.dll")] private static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessDPIAware();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfo(uint action, uint parameter, out uint value, uint flags);
    private static readonly StringBuilder Log = new StringBuilder();
    private static readonly string[] Families = { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimHei", "Segoe UI" };
    private static readonly float[] Sizes = { 10f, 10.5f, 11f };
    private const string Sample = "固件联合打包　选择文件　包生成　包校验　上传成功　文件大小超出限制";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: TypographyTests.exe app.exe output-directory"); return 2; }
        string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        try
        {
            Log.AppendLine("Typography diagnostic; NOT a real OS 125% / 150% DPI switching test.");
            Log.AppendLine("Timestamp UTC: " + DateTime.UtcNow.ToString("o"));
            Log.AppendLine("Font inventory and awareness refer to THIS TEST PROCESS, not the application executable manifest.");
            Log.AppendLine("No DPI-awareness API setter is called; rendering inherits the test process environment.");
            Log.AppendLine("TextRenderer is used for the font comparison, with identical text and regular style. Segoe UI Chinese glyphs can use Windows glyph fallback; installed family does not prove native CJK glyph coverage.");
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            ReportSystem();
            HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (InstalledFontCollection collection = new InstalledFontCollection())
                foreach (FontFamily family in collection.Families) installed.Add(family.Name);
            Log.AppendLine("MICROSOFT YAHEI UI INSTALLED IN THIS CI/TEST ENVIRONMENT: " + installed.Contains("Microsoft YaHei UI"));
            if (!installed.Contains("Microsoft YaHei UI")) Log.AppendLine("WARNING: Microsoft YaHei UI is absent. Requesting that font can silently fall back; screenshots must not be described as actual YaHei UI rendering.");
            foreach (string family in Families)
            {
                bool available = installed.Contains(family);
                Log.AppendLine("Installed [" + family + "]: " + available);
                if (available) RenderFamily(family, output);
                else Log.AppendLine("SKIP comparison: " + family + " is absent; no substitute font image generated.");
            }
            RenderApplication(Path.GetFullPath(args[0]), output);
            VerifyExecutableAwareness(Path.GetFullPath(args[0]));
            File.WriteAllText(Path.Combine(output, "typography-diagnostics.txt"), Log.ToString(), Encoding.UTF8);
            Console.WriteLine("Typography diagnostic completed. " + output); return 0;
        }
        catch (Exception ex)
        {
            Log.AppendLine("FAILED: " + ex.GetType().FullName + ": " + ex.Message);
            File.WriteAllText(Path.Combine(output, "typography-diagnostics.txt"), Log.ToString(), Encoding.UTF8);
            Console.Error.WriteLine("Typography diagnostic failed; see report."); return 1;
        }
    }

    private static void VerifyExecutableAwareness(string path)
    {
        using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = false }))
        {
            try
            {
                process.WaitForInputIdle(10000);
                int awareness; int hr = GetProcessDpiAwareness(process.Handle, out awareness);
                Log.AppendLine("Production EXE process DPI awareness: HRESULT=" + hr + "; value=" + awareness);
                if (hr != 0 || awareness != 1) throw new InvalidOperationException("Production executable must be system DPI aware.");
            }
            finally
            {
                if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(5000)) { process.Kill(); process.WaitForExit(); } }
            }
        }
    }

    private static void ReportSystem()
    {
        try { int awareness; int hr = GetProcessDpiAwareness(IntPtr.Zero, out awareness); Log.AppendLine("GetProcessDpiAwareness HRESULT=" + hr + "; value=" + awareness + " (0=unaware, 1=system, 2=per-monitor; does not distinguish V1/V2)"); }
        catch (Exception ex) { Log.AppendLine("GetProcessDpiAwareness unavailable: " + ex.GetType().Name); }
        try { Log.AppendLine("IsProcessDPIAware fallback: " + IsProcessDPIAware()); }
        catch (Exception ex) { Log.AppendLine("IsProcessDPIAware unavailable: " + ex.GetType().Name); }
        try
        {
            uint enabled, kind;
            bool ok = SystemParametersInfo(0x004A, 0, out enabled, 0);
            Log.AppendLine("SPI_GETFONTSMOOTHING: " + (ok ? enabled.ToString() : "unavailable"));
            ok = SystemParametersInfo(0x200A, 0, out kind, 0);
            Log.AppendLine("SPI_GETFONTSMOOTHINGTYPE: " + (ok ? kind.ToString() + " (1=standard, 2=ClearType)" : "unavailable"));
        }
        catch (Exception ex) { Log.AppendLine("Text smoothing query unavailable: " + ex.GetType().Name); }
        using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
            Log.AppendLine("Screen Graphics DPI X/Y: " + graphics.DpiX + "/" + graphics.DpiY);
    }

    private static void RenderFamily(string name, string output)
    {
        float dpi;
        using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) dpi = screen.DpiX;
        // Physical comparison at observed DPI, not a fabricated DPI setting.
        int width = Math.Max(1100, (int)Math.Ceiling(1100 * dpi / 96f));
        int row = Math.Max(80, (int)Math.Ceiling(80 * dpi / 96f));
        using (Bitmap image = new Bitmap(width, row * Sizes.Length + row))
        {
            image.SetResolution(dpi, dpi);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.White);
                using (Font label = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Regular, GraphicsUnit.Point))
                    TextRenderer.DrawText(graphics, name + " | actual graphics DPI " + dpi.ToString(CultureInfo.InvariantCulture), label, new Point(20, 15), Color.Black);
                for (int i = 0; i < Sizes.Length; i++)
                using (Font font = new Font(name, Sizes[i], FontStyle.Regular, GraphicsUnit.Point))
                {
                    if (!String.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.AppendLine("SKIP unexpected family substitution: " + name + " -> " + font.Name);
                        return;
                    }
                    Log.AppendLine("Comparison: " + font.Name + " " + font.SizeInPoints + "pt; unit=" + font.Unit + "; graphics DPI=" + graphics.DpiX);
                    TextRenderer.DrawText(graphics, Sizes[i].ToString(CultureInfo.InvariantCulture) + " pt   " + Sample, font,
                        new Rectangle(20, row * (i + 1), width - 40, row), Color.FromArgb(48, 54, 60), Color.White,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }
            image.Save(Path.Combine(output, "font-" + name.Replace(' ', '-') + ".png"), ImageFormat.Png);
        }
    }

    private static void Sanitize(Control control)
    {
        TextBox text = control as TextBox;
        if (text != null)
        {
            string n = (text.Name ?? "").ToLowerInvariant();
            if (text.UseSystemPasswordChar || text.PasswordChar != '\0' || n.Contains("password")) text.Text = "";
            else if (n.Contains("ftp"))
                text.Text = n.Contains("user") ? "synthetic" : n.Contains("port") ? "21" : n.Contains("file") || n.Contains("path") ? "" : "example.invalid";
        }
        foreach (Control child in control.Controls) Sanitize(child);
    }

    private static void Describe(Control control, string parent)
    {
        string id = parent + "/" + control.GetType().Name + "[" + control.Name + "]";
        // Intentionally never log Text, credentials, tooltips or field values.
        Log.AppendLine(id + "; font=" + control.Font.Name + "; size=" + control.Font.Size.ToString(CultureInfo.InvariantCulture) + "; unit=" + control.Font.Unit + "; points=" + control.Font.SizeInPoints.ToString(CultureInfo.InvariantCulture) + "; bounds=" + control.Bounds + "; visible=" + control.Visible);
        foreach (Control child in control.Controls) Describe(child, id);
    }

    private static void RenderApplication(string path, string output)
    {
        Assembly assembly = Assembly.LoadFrom(path);
        Type main = null;
        foreach (Type type in assembly.GetTypes()) if (type.Name == "MainForm" && typeof(Form).IsAssignableFrom(type)) { main = type; break; }
        if (main == null) throw new InvalidOperationException("MainForm not found.");
        using (Form form = (Form)Activator.CreateInstance(main, true))
        {
            Sanitize(form);
            form.Show(); Application.DoEvents(); Sanitize(form); Application.DoEvents();
            using (Graphics graphics = form.CreateGraphics()) Log.AppendLine("MainForm Graphics DPI X/Y: " + graphics.DpiX + "/" + graphics.DpiY);
            Describe(form, "");
            using (Bitmap image = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                image.Save(Path.Combine(output, "main-form-draw-to-bitmap.png"), ImageFormat.Png);
            }
            Log.AppendLine("Main form evidence uses WinForms DrawToBitmap, NOT desktop capture; native controls such as RichTextBox may not paint fully.");
            form.Close();
        }
    }
}
