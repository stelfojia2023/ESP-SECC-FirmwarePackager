using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SeccFirmwarePackager
{
    internal sealed class UiPalette
    {
        public readonly string Key, Label;
        public readonly Color Canvas, Card, CardTop, CardBottom, Field, Blue, SoftBlue, Border, ButtonPrimary, ButtonSecondary, ButtonIcon, Swatch;

        private UiPalette(string key, string label, params string[] values)
        {
            Key = key; Label = label;
            Canvas = ColorTranslator.FromHtml(values[0]); Card = ColorTranslator.FromHtml(values[1]);
            CardTop = ColorTranslator.FromHtml(values[2]); CardBottom = ColorTranslator.FromHtml(values[3]);
            Field = ColorTranslator.FromHtml(values[4]); Blue = ColorTranslator.FromHtml(values[5]);
            SoftBlue = ColorTranslator.FromHtml(values[6]); Border = ColorTranslator.FromHtml(values[7]);
            ButtonPrimary = ColorTranslator.FromHtml(values[8]); ButtonSecondary = ColorTranslator.FromHtml(values[9]);
            ButtonIcon = ColorTranslator.FromHtml(values[10]);
            Swatch = ColorTranslator.FromHtml(values[11]);
        }

        public static readonly UiPalette[] All = {
            new UiPalette("silver", "银色", "#EEEFF1", "#F7F8FA", "#FFFFFF", "#ECEEF1", "#F0F1F3", "#506575", "#E3E9E8", "#B9BEC5", "#DEE6E2", "#E7E9ED", "#F5F6F8", "#C9CED2"),
            new UiPalette("blue", "蓝色", "#E6EDF7", "#F4F8FD", "#FBFDFF", "#E3EBF6", "#ECF2FA", "#426080", "#D7E5F5", "#AABED4", "#C8DAEE", "#DCE8F5", "#F1F6FC", "#B4CEE9"),
            new UiPalette("purple", "紫色", "#EDE9F6", "#F8F6FC", "#FDFBFF", "#EEE9F8", "#F2EEF9", "#6B5A80", "#E4DBF2", "#C0B5D2", "#DDD2EE", "#E9E2F3", "#F6F3FB", "#C5B9E4"),
            new UiPalette("pink", "粉色", "#F7E9ED", "#FCF7F8", "#FFFCFD", "#F6E8EC", "#F9F0F2", "#875B68", "#F1DCE3", "#D2B5BE", "#EECED9", "#F4E1E7", "#FBF4F6", "#EAC0CF"),
            new UiPalette("orange", "橙色", "#F8EBE2", "#FEF8F4", "#FFFCFA", "#F7E9E0", "#FAF1EA", "#886048", "#F3E1D3", "#D4B9A6", "#EECFB9", "#F4E2D6", "#FCF5F0", "#F2C6AA"),
            new UiPalette("yellow", "黄色", "#F7F1DF", "#FCFAF1", "#FFFDF7", "#F4ECD4", "#FAF6E9", "#796743", "#F1E8CC", "#D1C6A7", "#E9DDB3", "#F3EBD4", "#FBF9EF", "#ECDC9B"),
            new UiPalette("green", "绿色", "#E7F1E9", "#F5FAF6", "#FCFFFC", "#E4EFE6", "#EFF6F0", "#496B54", "#D8EADD", "#AEC6B4", "#C9E2CF", "#DEEEE1", "#F3F9F4", "#B6D8BF")
        };

        public static UiPalette Find(string key)
        {
            foreach (UiPalette palette in All)
                if (String.Equals(palette.Key, key, StringComparison.Ordinal)) return palette;
            return All[0];
        }

        public Color Translate(Color color, UiPalette target)
        {
            if (color.IsEmpty) return color;
            int value = color.ToArgb();
            if (value == Canvas.ToArgb()) return target.Canvas;
            if (value == Card.ToArgb()) return target.Card;
            if (value == Field.ToArgb()) return target.Field;
            if (value == Blue.ToArgb()) return target.Blue;
            if (value == SoftBlue.ToArgb()) return target.SoftBlue;
            if (value == Border.ToArgb()) return target.Border;
            if (value == ButtonPrimary.ToArgb()) return target.ButtonPrimary;
            if (value == ButtonSecondary.ToArgb()) return target.ButtonSecondary;
            if (value == ButtonIcon.ToArgb()) return target.ButtonIcon;
            return color;
        }
    }

    internal static class AppearanceSettings
    {
        private static string DirectoryPath()
        {
            string isolated = Environment.GetEnvironmentVariable("SECC_PACKAGER_SETTINGS_DIR");
            if (!String.IsNullOrWhiteSpace(isolated)) return isolated;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LINCHR", "FirmwarePackager");
        }

        public static UiPalette Load()
        {
            try
            {
                string path = Path.Combine(DirectoryPath(), "appearance.txt");
                FileInfo file = new FileInfo(path);
                if (!file.Exists || file.Length > 128) return UiPalette.All[0];
                return UiPalette.Find(File.ReadAllText(path, Encoding.UTF8).Trim());
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException))
                    throw;
                return UiPalette.All[0];
            }
        }

        public static bool Save(UiPalette palette)
        {
            string temp = null;
            try
            {
                string directory = DirectoryPath(); Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "appearance.txt");
                temp = Path.Combine(directory, ".appearance-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(temp, palette.Key, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException))
                    throw;
                return false;
            }
            finally
            {
                try { if (temp != null && File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
            }
        }
    }

    internal static class UiColors
    {
        // Current appearance tokens; the seven palettes are specified in docs/七色配色规范.md.
        public static Color Canvas = ColorTranslator.FromHtml("#EEEFF1");
        public static Color Card = ColorTranslator.FromHtml("#F7F8FA");
        public static Color CardTop = ColorTranslator.FromHtml("#FFFFFF");
        public static Color CardBottom = ColorTranslator.FromHtml("#ECEEF1");
        public static Color Field = ColorTranslator.FromHtml("#F0F1F3");
        public static readonly Color Ink = ColorTranslator.FromHtml("#30363C");
        public static readonly Color Muted = ColorTranslator.FromHtml("#626A73");
        public static Color Blue = ColorTranslator.FromHtml("#506575");
        public static Color SoftBlue = ColorTranslator.FromHtml("#E3E9E8");
        public static Color Border = ColorTranslator.FromHtml("#B9BEC5");
        public static Color ButtonPrimary = ColorTranslator.FromHtml("#DEE6E2");
        public static Color ButtonSecondary = ColorTranslator.FromHtml("#E7E9ED");
        public static Color ButtonIcon = ColorTranslator.FromHtml("#F5F6F8");
        public static readonly Color ButtonDisabledTint = Color.FromArgb(235, 236, 238);
        public static readonly Color ButtonShadeTint = Color.FromArgb(100, 107, 114);
        public static readonly Color ButtonShadow = Color.FromArgb(61, 67, 74);
        public static readonly Color ButtonHighlight = Color.White;
        public static readonly Color SurfaceShadow = Color.FromArgb(22, 45, 52, 64);
        public static readonly Color SurfaceHighlight = Color.FromArgb(215, 255, 255, 255);
        public static readonly Color FieldInset = Color.FromArgb(25, 45, 52, 64);
        public static readonly Color Error = ColorTranslator.FromHtml("#B53239");
        public static readonly Color HeaderHighlight = ColorTranslator.FromHtml("#E1D59C");
        public static readonly Color Success = ColorTranslator.FromHtml("#1D7858");

        public static UiPalette Current = UiPalette.All[0];
        public static void Apply(UiPalette palette)
        {
            Current = palette;
            Canvas = palette.Canvas; Card = palette.Card; CardTop = palette.CardTop; CardBottom = palette.CardBottom;
            Field = palette.Field; Blue = palette.Blue; SoftBlue = palette.SoftBlue; Border = palette.Border;
            ButtonPrimary = palette.ButtonPrimary; ButtonSecondary = palette.ButtonSecondary; ButtonIcon = palette.ButtonIcon;
        }
    }

    internal sealed class AppearanceSwatch : Button
    {
        public readonly UiPalette Palette;

        public AppearanceSwatch(UiPalette palette)
        {
            Palette = palette; Name = "Appearance_" + palette.Key; Text = palette.Label;
            AccessibleName = palette.Label + "配色"; AccessibleRole = AccessibleRole.RadioButton;
            Size = new Size(30, 32); Margin = new Padding(1, 0, 1, 0);
            BackColor = UiColors.Canvas; UseVisualStyleBackColor = false;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new AppearanceSwatchAccessibleObject(this);
        }

        private sealed class AppearanceSwatchAccessibleObject : ControlAccessibleObject
        {
            private readonly AppearanceSwatch swatch;
            public AppearanceSwatchAccessibleObject(AppearanceSwatch owner) : base(owner) { swatch = owner; }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates state = base.State;
                    return Object.ReferenceEquals(UiColors.Current, swatch.Palette) ? state | AccessibleStates.Checked : state;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float x = (ClientSize.Width - 18F) / 2F, y = (ClientSize.Height - 18F) / 2F;
            using (Brush fill = new SolidBrush(Palette.Swatch)) e.Graphics.FillEllipse(fill, x, y, 18, 18);
            using (Pen outline = new Pen(Color.FromArgb(110, 91, 100, 109), 1F)) e.Graphics.DrawEllipse(outline, x, y, 18, 18);
            if (Object.ReferenceEquals(UiColors.Current, Palette))
                using (Pen selected = new Pen(UiColors.Ink, 1.6F)) e.Graphics.DrawEllipse(selected, x - 3, y - 3, 24, 24);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(0, 1, Width - 1, Height - 2), UiColors.Ink, BackColor);
        }
    }

    // Drawing units are device pixels; layout dimensions remain WinForms logical units.
    internal static class UiSurfaceMetrics
    {
        public const float CardArcDiameter = 22F;
        public const float CardShadowX = 1F, CardShadowY = 3F;
        public const float FieldArcDiameter = 9F;
        public const float FieldBorder = 1F, FieldFocusBorder = 1.5F;
    }

    internal sealed class SectionCard : Panel
    {
        public SectionCard()
        {
            BackColor = UiColors.Canvas;
            Padding = new Padding(18, 14, 18, 18);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (Width < 4 || Height < 4) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(1.5F, 1.5F, Width - 6F, Height - 7F);
            float d = Math.Min(UiSurfaceMetrics.CardArcDiameter, Math.Min(r.Width, r.Height));
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (System.Drawing.Drawing2D.Matrix shift = new System.Drawing.Drawing2D.Matrix())
                using (GraphicsPath shadow = (GraphicsPath)path.Clone())
                {
                    shift.Translate(UiSurfaceMetrics.CardShadowX, UiSurfaceMetrics.CardShadowY); shadow.Transform(shift);
                    using (Brush shade = new SolidBrush(UiColors.SurfaceShadow)) e.Graphics.FillPath(shade, shadow);
                }
                using (Brush fill = new LinearGradientBrush(r, UiColors.CardTop, UiColors.CardBottom, LinearGradientMode.Vertical)) e.Graphics.FillPath(fill, path);
                using (Pen highlight = new Pen(UiColors.SurfaceHighlight))
                    e.Graphics.DrawLine(highlight, r.Left + 12, r.Top + 1, r.Right - 12, r.Top + 1);
                using (Pen border = new Pen(UiColors.Border)) e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class SoftField : Panel
    {
        private readonly TextBoxBase input;

        public SoftField(TextBoxBase box, bool multiline)
        {
            input = box;
            BackColor = UiColors.Field;
            Padding = multiline ? new Padding(9, 7, 9, 7) : new Padding(8, 5, 8, 3);
            Margin = new Padding(3, 3, 3, 3);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            input.BorderStyle = BorderStyle.None;
            input.BackColor = UiColors.Field;
            input.Dock = DockStyle.Fill;
            input.Enter += delegate { Invalidate(); };
            input.Leave += delegate { Invalidate(); };
            Controls.Add(input);
            Click += delegate { if (input.CanFocus) input.Focus(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 4 || Height < 4) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5F, 0.5F, Width - 2F, Height - 2F);
            float d = Math.Min(UiSurfaceMetrics.FieldArcDiameter, Math.Min(r.Width, r.Height));
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (Pen inset = new Pen(UiColors.FieldInset))
                    e.Graphics.DrawLine(inset, r.Left + 5, r.Top + 1, r.Right - 5, r.Top + 1);
                using (Pen border = new Pen(input.Focused ? UiColors.Blue : UiColors.Border, input.Focused ? UiSurfaceMetrics.FieldFocusBorder : UiSurfaceMetrics.FieldBorder))
                    e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class FtpAddressBox : TextBox
    {
        public Action<string> PasteAddress;

        protected override void WndProc(ref Message message)
        {
            const int WM_PASTE = 0x0302;
            if (message.Msg == WM_PASTE && PasteAddress != null)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string value = Clipboard.GetText().Trim();
                        if (value.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                        {
                            PasteAddress(value);
                            return;
                        }
                    }
                }
                catch (ExternalException) { }
            }
            base.WndProc(ref message);
        }
    }

    internal sealed class FtpDirectoryViewer : Form
    {
        public FtpDirectoryViewer(FtpDirectoryResult result, string localName)
        {
            Text = "FTP 目录"; Name = "FtpDirectoryViewer";
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UiColors.Canvas; AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            Size = new Size(650, Math.Min(460, Math.Max(260, 215 + result.Entries.Length * 22)));
            MinimumSize = new Size(480, 260); Padding = new Padding(14);

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(layout);
            TextBox directory = new TextBox { Name = "FtpDirectoryAddress", ReadOnly = true, Text = result.DirectoryUrl, AccessibleName = "FTP 目录地址" };
            directory.TabStop = false;
            layout.Controls.Add(new SoftField(directory, false) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
            string summary = result.Entries.Length == 0 ? "目录为空" : "目录共 " + result.Entries.Length + " 个条目";
            if (!String.IsNullOrEmpty(localName))
                summary += ContainsName(result.Entries, localName) ? " · 已找到同名条目（仅比较名称）" : " · 未找到同名条目（仅比较名称）";
            Label info = new Label { Name = "FtpDirectorySummary", Text = summary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiColors.Muted };
            layout.Controls.Add(info, 0, 1);
            TextBox listing = new TextBox { Name = "FtpDirectoryEntries", Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, BackColor = UiColors.Card, ForeColor = UiColors.Ink,
                AccessibleName = "服务器返回的目录条目", Text = String.Join("\r\n", result.Entries) };
            layout.Controls.Add(listing, 0, 2);
            Shown += delegate { listing.Focus(); listing.Select(0, 0); };
            Button close = new RaisedButton { Name = "FtpDirectoryClose", Text = "关闭", Width = 100, Height = 32,
                BackColor = UiColors.SoftBlue, ForeColor = UiColors.Ink, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            close.Click += delegate { Close(); };
            layout.Controls.Add(close, 0, 3); CancelButton = close;
        }

        private static bool ContainsName(string[] entries, string name)
        {
            foreach (string entry in entries)
            {
                int slash = entry.LastIndexOf('/');
                if (String.Equals(slash < 0 ? entry : entry.Substring(slash + 1), name, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    public sealed class MainForm : Form
    {
        private sealed class Row
        {
            public FirmwareType Type;
            public TextBox PathBox;
            public TextBox NameBox;
            public TextBox VersionBox;
            public Label SizeLabel;
            public string AutoName;
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox output = new TextBox();
        private readonly RichTextBox details = new RichTextBox();
        private readonly TextBox status = new TextBox();
        private readonly Button build = new RaisedButton();
        private readonly FtpAddressBox ftpAddress = new FtpAddressBox();
        private readonly TextBox ftpUser = new TextBox();
        private readonly TextBox ftpPassword = new TextBox();
        private readonly Button ftpUpload = new RaisedButton();
        private readonly Button ftpDirectory = new RaisedButton();
        private readonly Label ftpStatus = new Label();
        private readonly ToolTip tooltips = new ToolTip();
        private readonly UiSounds sounds = new UiSounds();
        private readonly Panel packageScroll = new Panel();
        private readonly Panel upgradeScroll = new Panel();
        private readonly Button packageTab = new RaisedButton();
        private readonly Button upgradeTab = new RaisedButton();
        private readonly UpgradePage upgradePage;
        private readonly FlowLayoutPanel appearanceSwatches = new FlowLayoutPanel();
        private readonly Font pageTabFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
        private readonly Font pageTabSelectedFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        private Button ftpBrowseFile;
        private TableLayoutPanel workspace;
        private SectionCard previewCard;
        private readonly TextBox ftpFile = new TextBox();
        private readonly TextBox ftpDownloadAddress = new TextBox();
        private readonly Button ftpCopyDownloadAddress = new RaisedButton();
        private TableLayoutPanel ftpDownloadRow;
        private RowStyle ftpDownloadRowStyle;
        private readonly Label ftpSourceKind = new Label();
        private string lastGeneratedPackage;
        private byte[] lastGeneratedHeader;
        private readonly Button headerView = new RaisedButton();
        private bool ftpPackageSource = true;
        private string ftpAuthority;
        private bool ftpBusy;
        private bool ftpListing;
        private bool changing;
        private bool loadingFolder;
        private string lastPreviewMessage;
        private bool pageInitialized;
        private bool showingUpgradePage;
        private Size packagePageSize;
        private Size upgradePageSize;

        public MainForm()
        {
            UiColors.Apply(AppearanceSettings.Load());
            upgradePage = new UpgradePage();
            upgradePage.ResultReported += delegate(string message, bool error)
            {
                AppendResult(message, error ? UiColors.Error : UiColors.Ink);
            };
            Text = "打包工具 · V0.0.13"; Name = "MainForm";
            MinimumSize = new Size(1100, 720);
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Max(1100, Math.Min(1320, work.Width - 24)), Math.Max(720, Math.Min(792, work.Height - 24)));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9.5F); AutoScaleMode = AutoScaleMode.Dpi; BackColor = UiColors.Canvas;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Button help = new RaisedButton();
            PreparePageTab(help, "HelpButton", "! 操作说明");
            help.Size = new Size(180, 40); help.AccessibleName = "操作说明";
            help.BackColor = UiColors.ButtonSecondary;
            help.Click += delegate { ShowHelp(); }; tooltips.SetToolTip(help, "操作说明");
            TableLayoutPanel root = new TableLayoutPanel { Name = "MainPages", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = UiColors.Canvas };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);
            FlowLayoutPanel navigation = new FlowLayoutPanel { Name = "PageNavigation", Dock = DockStyle.Fill, WrapContents = false,
                Margin = Padding.Empty, Padding = new Padding(18, 0, 0, 0), BackColor = UiColors.Canvas };
            PreparePageTab(packageTab, "PackPageTab", "固件打包");
            PreparePageTab(upgradeTab, "UpgradePageTab", "设备升级");
            packageTab.Click += delegate { ShowUpgradePage(false); };
            upgradeTab.Click += delegate { ShowUpgradePage(true); };
            navigation.Controls.Add(packageTab); navigation.Controls.Add(upgradeTab); navigation.Controls.Add(help);
            TableLayoutPanel top = new TableLayoutPanel { Name = "HeaderBar", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = Padding.Empty, BackColor = UiColors.Canvas };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 242));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            appearanceSwatches.Name = "AppearanceSwatches"; appearanceSwatches.Dock = DockStyle.Fill;
            appearanceSwatches.WrapContents = false; appearanceSwatches.Margin = Padding.Empty;
            appearanceSwatches.Padding = new Padding(0, 6, 18, 0); appearanceSwatches.BackColor = UiColors.Canvas;
            SetupAppearanceSwatches();
            top.Controls.Add(navigation, 0, 0); top.Controls.Add(appearanceSwatches, 1, 0);
            root.Controls.Add(top, 0, 0);
            Panel pageHost = new Panel { Name = "PageHost", Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = UiColors.Canvas };
            packageScroll.Name = "ContentScroll"; packageScroll.Dock = DockStyle.Fill; packageScroll.AutoScroll = true;
            upgradeScroll.Name = "UpgradeScroll"; upgradeScroll.Dock = DockStyle.Fill; upgradeScroll.AutoScroll = true;
            upgradeScroll.Controls.Add(upgradePage); pageHost.Controls.Add(upgradeScroll); pageHost.Controls.Add(packageScroll);
            root.Controls.Add(pageHost, 0, 1);
            ShowUpgradePage(false);
            workspace = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3,
                Padding = new Padding(18, 4, 18, 14), Margin = Padding.Empty };
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 440));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 270)); packageScroll.Controls.Add(workspace);
            TableLayoutPanel inputs = CardLayout(5); inputs.Name = "InputsCard"; inputs.BackColor = UiColors.Canvas;
            for (int i = 0; i < 5; i++) inputs.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            workspace.Controls.Add(inputs, 0, 0);
            FirmwareType[] types = { FirmwareType.PFC, FirmwareType.LLC, FirmwareType.M4, FirmwareType.PLC, FirmwareType.ESP32 };
            for (int i = 0; i < types.Length; i++)
            {
                TableLayoutPanel staging = new TableLayoutPanel { ColumnCount = 7, RowCount = 2 };
                AddRow(staging, types[i], 1);
                Control[] c = new Control[7]; for (int n = 0; n < 7; n++) c[n] = staging.GetControlFromPosition(n, 1);
                SectionCard card = new SectionCard { Name = "FirmwareCard_" + types[i], Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 6), Padding = new Padding(12, 8, 12, 10) };
                TableLayoutPanel item = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = Padding.Empty, BackColor = Color.Transparent };
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
                item.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); item.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                c[0].Font = new Font(Font.FontFamily, 12F, FontStyle.Bold); c[0].BackColor = Color.Transparent; c[0].ForeColor = UiColors.Blue;
                item.Controls.Add(c[0], 0, 0); item.SetRowSpan(c[0], 2);
                item.Controls.Add(c[1], 1, 0); item.SetColumnSpan(c[1], 2);
                item.Controls.Add(c[2], 3, 0); item.Controls.Add(c[3], 3, 1);
                item.Controls.Add(c[4], 1, 1); item.Controls.Add(c[5], 2, 1);
                ConfigureIcon((Button)c[2], "folder", "选择 " + types[i] + " 固件");
                ConfigureIcon((Button)c[3], "clear", "清除 " + types[i] + " 选择（不删除文件）");
                rows[i].PathBox.AccessibleName = types[i] + " 固件路径";
                rows[i].NameBox.AccessibleName = types[i] + " 固件名称"; rows[i].VersionBox.AccessibleName = types[i] + " 固件版本";
                // Size is shown in the dynamic list rather than repeated in each card.
                c[6].Parent = null; c[6].Visible = false;
                card.Controls.Add(item); inputs.Controls.Add(card, 0, i); staging.Dispose();
            }
            TableLayoutPanel right = CardLayout(3); right.BackColor = UiColors.Canvas;
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 108)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); workspace.Controls.Add(right, 1, 0);
            SectionCard loadCard = new SectionCard { Name = "AutoLoadCard", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 8), Padding = new Padding(12, 8, 12, 10) };
            TableLayoutPanel load = CardLayout(2); load.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); load.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TextBox folder = new PlaceholderTextBox { Name = "FolderPath", PlaceholderText = "选择固件文件夹", AccessibleName = "批量加载固件文件夹" };
            Button folderButton = new RaisedButton { Name = "BrowseFolder", Dock = DockStyle.Fill };
            ConfigureIcon(folderButton, "folder", "选择固件文件夹");
            TableLayoutPanel folderLine = (TableLayoutPanel)PathLine(folder, folderButton);
            load.Controls.Add(folderLine, 0, 0);
            Button autoLoad = MainButton("AutoLoadButton", "一键加载", true);
            Button clearAll = MainButton("ClearAllButton", "一键清理", false);
            clearAll.Click += delegate { ClearAllInputs(); };
            folderButton.Click += delegate { ChooseFolder(); };
            autoLoad.Click += delegate { LoadFolder(folder.Text); };
            tooltips.SetToolTip(clearAll, "清空固件选择，不删除源文件");
            load.Controls.Add(RightActions(clearAll, autoLoad), 0, 1);
            loadCard.Controls.Add(load); right.Controls.Add(loadCard, 0, 0);
            SectionCard packageCard = new SectionCard { Name = "PackageCard", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 8), Padding = new Padding(12, 8, 12, 10) };
            TableLayoutPanel package = CardLayout(2); package.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); package.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            output.Name = "OutputPath"; output.AccessibleName = "输出 BIN 文件路径";
            output.TextChanged += delegate { tooltips.SetToolTip(output, output.Text); };
            Button browse = new RaisedButton { Name = "BrowseOutput", Dock = DockStyle.Fill }; ConfigureIcon(browse, "folder", "选择输出位置"); browse.Click += BrowseOutput;
            package.Controls.Add(PathLine(output, browse), 0, 0);
            build.Name = "BuildButton"; build.Text = "包生成"; SetupMainButton(build, true); build.Click += BuildClicked;
            headerView.Name = "HeaderViewButton"; headerView.Text = "查看包头"; headerView.Enabled = false;
            SetupMainButton(headerView, false); headerView.Click += delegate { ShowGeneratedHeader(); };
            package.Controls.Add(RightActions(headerView, build), 0, 1); packageCard.Controls.Add(package); right.Controls.Add(packageCard, 0, 1);
            SectionCard ftpCard = new SectionCard { Name = "FtpInlinePanel", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 6), Padding = new Padding(12, 8, 12, 10) };
            TableLayoutPanel ftp = CardLayout(5);
            for (int i = 0; i < 4; i++) ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); ftp.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ftpFile.Name = "FtpFilePath"; ftpFile.ReadOnly = true; ftpFile.AllowDrop = true; ftpFile.AccessibleName = "FTP 上传文件";
            ftpFile.DragEnter += InputDragEnter;
            ftpFile.DragDrop += delegate(object sender, DragEventArgs e) { string[] files = DraggedFiles(e); if (!ftpBusy && files != null && files.Length == 1 && File.Exists(files[0])) SetFtpFile(files[0], false); };
            ftpFile.TextChanged += delegate { tooltips.SetToolTip(ftpFile, ftpFile.Text); };
            ftpBrowseFile = new RaisedButton { Name = "FtpBrowseFile", Dock = DockStyle.Fill }; ConfigureIcon(ftpBrowseFile, "folder", "选择 FTP 上传文件");
            ftpBrowseFile.Click += delegate { using (OpenFileDialog d = new OpenFileDialog { Title = "选择 FTP 上传文件", Filter = "所有文件 (*.*)|*.*", CheckFileExists = true }) if (d.ShowDialog(this) == DialogResult.OK) SetFtpFile(d.FileName, false); };
            ftp.Controls.Add(LabeledLine("文件", PathLine(ftpFile, ftpBrowseFile)), 0, 0);
            ftpAddress.Name = "FtpAddress"; ftpAddress.AccessibleName = "FTP URL";
            ftpAddress.PasteAddress = delegate(string value) { ApplyFtpAddress(value, true); };
            ftpAddress.Leave += delegate { if (!String.IsNullOrWhiteSpace(ftpAddress.Text)) ApplyFtpAddress(ftpAddress.Text, false); };
            ftpAddress.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyFtpAddress(ftpAddress.Text, true); } };
            ftpUser.Name = "FtpUser"; ftpUser.AccessibleName = "FTP 用户名";
            ftpPassword.Name = "FtpPassword"; ftpPassword.UseSystemPasswordChar = true; ftpPassword.AccessibleName = "FTP 密码";
            ftpAddress.Text = "";
            ftpUser.Text = "";
            ftpPassword.Text = "";
            ftpAuthority = null; // First address accepts entered credentials; later server changes clear them.
            ftp.Controls.Add(LabeledLine("URL", new SoftField(ftpAddress, false) { Dock = DockStyle.Fill }), 0, 1);
            ftp.Controls.Add(LabeledLine("用户名", new SoftField(ftpUser, false) { Dock = DockStyle.Fill }), 0, 2);
            ftp.Controls.Add(LabeledLine("密码", new SoftField(ftpPassword, false) { Dock = DockStyle.Fill }), 0, 3);
            ftpUpload.Name = "FtpUploadButton"; ftpUpload.Text = "上传到 FTP"; SetupMainButton(ftpUpload, true); ftpUpload.Click += FtpUploadClicked;
            ftpDirectory.Name = "FtpDirectoryButton"; ftpDirectory.Text = "打开 FTP 目录"; SetupMainButton(ftpDirectory, false); ftpDirectory.Click += FtpDirectoryClicked;
            ftp.Controls.Add(RightActions(ftpDirectory, ftpUpload), 0, 4); ftpCard.Controls.Add(ftp); right.Controls.Add(ftpCard, 0, 2);
            FormClosing += FtpFormClosing;
            grid.Name = "PreviewGrid"; grid.Dock = DockStyle.Fill; grid.ReadOnly = true;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false; grid.ColumnHeadersVisible = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BorderStyle = BorderStyle.None; grid.BackgroundColor = UiColors.Card; grid.GridColor = UiColors.Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.RowTemplate.Height = 27; grid.ScrollBars = ScrollBars.Horizontal;
            grid.DefaultCellStyle.SelectionBackColor = UiColors.SoftBlue; grid.DefaultCellStyle.SelectionForeColor = UiColors.Ink;
            grid.DefaultCellStyle.BackColor = UiColors.Card;
            grid.Columns.Add("Type", "类型"); grid.Columns.Add("Name", "文件路径"); grid.Columns.Add("Size", "大小"); grid.Columns.Add("Offset", "偏移");
            grid.Columns[0].FillWeight = 40; grid.Columns[1].FillWeight = 240; grid.Columns[2].FillWeight = 100; grid.Columns[3].FillWeight = 70;
            grid.Columns[0].DefaultCellStyle.BackColor = UiColors.SoftBlue;
            previewCard = new SectionCard { Name = "PreviewCard", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4), Padding = new Padding(14, 10, 14, 12), Visible = false };
            previewCard.Controls.Add(grid); workspace.Controls.Add(previewCard, 0, 1); workspace.SetColumnSpan(previewCard, 2);
            SectionCard result = new SectionCard { Name = "OperationResultCard", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(14, 8, 14, 10) };
            TableLayoutPanel resultBody = CardLayout(3); resultBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            ftpDownloadRowStyle = new RowStyle(SizeType.Absolute, 0); resultBody.RowStyles.Add(ftpDownloadRowStyle);
            resultBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            resultBody.Controls.Add(TitleLabel("操作结果"), 0, 0);
            ftpDownloadRow = new TableLayoutPanel { Name = "FtpDownloadRow", Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Visible = false };
            ftpDownloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            ftpDownloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ftpDownloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            ftpDownloadRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ftpDownloadAddress.Name = "FtpDownloadAddress"; ftpDownloadAddress.AccessibleName = "FTP 下载地址（含密码）"; ftpDownloadAddress.ReadOnly = true;
            ftpCopyDownloadAddress.Name = "FtpCopyDownloadAddress"; ftpCopyDownloadAddress.Text = "复制";
            ftpCopyDownloadAddress.AccessibleName = "复制 FTP 下载地址"; ftpCopyDownloadAddress.Dock = DockStyle.Fill;
            StyleButton(ftpCopyDownloadAddress, false, false); ftpCopyDownloadAddress.Click += CopyFtpDownloadAddress;
            tooltips.SetToolTip(ftpCopyDownloadAddress, "复制的下载地址包含 FTP 密码");
            ftpDownloadRow.Controls.Add(FieldLabel("下载地址"), 0, 0);
            ftpDownloadRow.Controls.Add(new SoftField(ftpDownloadAddress, false) { Dock = DockStyle.Fill }, 1, 0);
            ftpDownloadRow.Controls.Add(ftpCopyDownloadAddress, 2, 0); resultBody.Controls.Add(ftpDownloadRow, 0, 1);
            details.Name = "ResultDetails"; details.Multiline = true; details.ReadOnly = true; details.ScrollBars = RichTextBoxScrollBars.Both;
            details.WordWrap = false; details.DetectUrls = false; details.MaxLength = Int32.MaxValue; details.ForeColor = UiColors.Ink;
            resultBody.Controls.Add(new SoftField(details, true) { Dock = DockStyle.Fill, Padding = new Padding(9, 5, 9, 5) }, 0, 2); result.Controls.Add(resultBody);
            workspace.Controls.Add(result, 0, 2); workspace.SetColumnSpan(result, 2);
            RefreshPreview();
        }

        private void SetupAppearanceSwatches()
        {
            foreach (UiPalette palette in UiPalette.All)
            {
                AppearanceSwatch swatch = new AppearanceSwatch(palette);
                swatch.Click += delegate(object sender, EventArgs e)
                {
                    SelectAppearance(((AppearanceSwatch)sender).Palette);
                };
                tooltips.SetToolTip(swatch, palette.Label + "配色");
                appearanceSwatches.Controls.Add(swatch);
            }
        }

        private static void RecolorControl(Control control, UiPalette oldPalette, UiPalette newPalette)
        {
            // Descendants are read first, before an inherited parent color changes.
            foreach (Control child in control.Controls) RecolorControl(child, oldPalette, newPalette);
            DataGridView view = control as DataGridView;
            if (view != null)
            {
                view.BackgroundColor = oldPalette.Translate(view.BackgroundColor, newPalette);
                view.GridColor = oldPalette.Translate(view.GridColor, newPalette);
                view.DefaultCellStyle.BackColor = oldPalette.Translate(view.DefaultCellStyle.BackColor, newPalette);
                view.DefaultCellStyle.SelectionBackColor = oldPalette.Translate(view.DefaultCellStyle.SelectionBackColor, newPalette);
                foreach (DataGridViewColumn column in view.Columns)
                {
                    column.DefaultCellStyle.BackColor = oldPalette.Translate(column.DefaultCellStyle.BackColor, newPalette);
                    column.DefaultCellStyle.SelectionBackColor = oldPalette.Translate(column.DefaultCellStyle.SelectionBackColor, newPalette);
                }
            }
            Color background = oldPalette.Translate(control.BackColor, newPalette);
            if (background.ToArgb() != control.BackColor.ToArgb()) control.BackColor = background;
            // RichTextBox retains the existing per-entry colors and selection.
            if (!(control is RichTextBox))
            {
                Color foreground = oldPalette.Translate(control.ForeColor, newPalette);
                if (foreground.ToArgb() != control.ForeColor.ToArgb()) control.ForeColor = foreground;
            }
            control.Invalidate();
        }

        private void SelectAppearance(UiPalette palette)
        {
            UiPalette previous = UiColors.Current;
            if (Object.ReferenceEquals(previous, palette)) return;
            UiColors.Apply(palette);
            RecolorControl(this, previous, palette);
            ftpStatus.ForeColor = previous.Translate(ftpStatus.ForeColor, palette);
            if (!AppearanceSettings.Save(palette))
            {
                string warning = "当前配色已应用，但无法保存；下次打开时可能无法恢复。";
                ShowInputResult(warning, true);
                MessageBox.Show(this, warning, "配色未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void PreparePageTab(Button button, string name, string text)
        {
            button.Name = name; button.Text = text; button.AccessibleName = text;
            StyleButton(button, false, false);
            Font styledFont = button.Font; button.Font = pageTabFont; styledFont.Dispose();
            button.Size = new Size(180, 40); button.Margin = new Padding(0, 2, 8, 2);
        }

        private void ShowUpgradePage(bool show)
        {
            if (pageInitialized && showingUpgradePage != show)
            {
                Size current = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
                if (show) packagePageSize = current; else upgradePageSize = current;
                MinimumSize = show ? new Size(920, 460) : new Size(1100, 720);
                if (WindowState == FormWindowState.Normal)
                {
                    if (show)
                        Size = upgradePageSize.IsEmpty ? new Size(Math.Min(current.Width, 1000), Math.Min(current.Height, 560)) : upgradePageSize;
                    else if (!packagePageSize.IsEmpty) Size = packagePageSize;
                }
            }
            pageInitialized = true; showingUpgradePage = show;
            packageScroll.Visible = !show; upgradeScroll.Visible = show;
            packageTab.BackColor = show ? UiColors.ButtonSecondary : UiColors.ButtonPrimary;
            upgradeTab.BackColor = show ? UiColors.ButtonPrimary : UiColors.ButtonSecondary;
            packageTab.Font = show ? pageTabFont : pageTabSelectedFont;
            upgradeTab.Font = show ? pageTabSelectedFont : pageTabFont;
        }

        private Button MainButton(string name, string text, bool primary)
        { Button b = new RaisedButton { Name = name, Text = text }; SetupMainButton(b, primary); return b; }
        private void SetupMainButton(Button b, bool primary)
        { StyleButton(b, primary, false); b.Size = new Size(180, 40); b.Anchor = AnchorStyles.Right | AnchorStyles.Top; }
        private static Control RightActions(params Button[] buttons)
        {
            // Keep action buttons compact and aligned across the three cards.
            TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                buttons[i].Margin = new Padding(3, 5, 3, 3);
                p.Controls.Add(buttons[i], buttons.Length == 1 ? 2 : i + 1, 0);
            }
            return p;
        }
        private static Control PathLine(TextBox field, Button browse)
        {
            TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.Controls.Add(new SoftField(field, false) { Dock = DockStyle.Fill }, 0, 0); p.Controls.Add(browse, 1, 0); return p;
        }
        private static Control LabeledLine(string title, Control content)
        {
            TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.Controls.Add(FieldLabel(title), 0, 0); p.Controls.Add(content, 1, 0); return p;
        }
        private void ConfigureIcon(Button b, string icon, string accessible)
        {
            StyleButton(b, false, true); b.Text = accessible; b.AccessibleName = accessible;
            RaisedButton raised = b as RaisedButton;
            if (raised != null) { raised.IconOnly = true; raised.Glyph = icon == "clear" ? ButtonGlyph.Clear : b.Name == "BrowseOutput" ? ButtonGlyph.FolderDownload : ButtonGlyph.Folder; }
            tooltips.SetToolTip(b, accessible);
        }

        private static Label TitleLabel(string title)
        {
            Label label = FieldLabel(title); label.ForeColor = UiColors.Ink;
            label.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold); return label;
        }

        private void SetFtpFile(string path, bool package)
        {
            ftpFile.Text = Path.GetFullPath(path);
            ftpPackageSource = package;
            ftpSourceKind.Text = package ? "联合包" : "本地文件";
        }

        private static Label FieldLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = UiColors.Muted;
            label.Margin = Padding.Empty;
            return label;
        }

        private static TableLayoutPanel CardLayout(int rows)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Margin = Padding.Empty;
            layout.BackColor = Color.Transparent;
            layout.ColumnCount = 1;
            layout.RowCount = rows;
            return layout;
        }

        private static void StyleTextBox(TextBox box, bool readOnly)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = UiColors.Field;
            box.ForeColor = UiColors.Ink;
        }

        private void StyleButton(Button button, bool primary, bool subtle)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(3, 3, 3, 3);
            button.Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? UiColors.ButtonPrimary : (subtle ? UiColors.ButtonIcon : UiColors.ButtonSecondary);
            button.ForeColor = subtle ? UiColors.Muted : UiColors.Ink;
            button.Click -= PlayClickSound;
            button.Click += PlayClickSound;
        }

        private void PlayClickSound(object sender, EventArgs e) { sounds.Play("click"); }

        private void AddRow(TableLayoutPanel table, FirmwareType type, int index)
        {
            Row row = new Row();
            row.Type = type;
            rows.Add(row);
            Label label = new Label();
            label.Text = type.ToString();
            label.Dock = DockStyle.Fill;
            label.Margin = new Padding(4, 4, 8, 4);
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            label.ForeColor = UiColors.Blue;
            label.BackColor = UiColors.SoftBlue;
            table.Controls.Add(label, 0, index);
            row.PathBox = new TextBox();
            row.PathBox.Name = "Path_" + type;
            row.PathBox.ReadOnly = true;
            row.PathBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (!e.Control || e.KeyCode != Keys.V) return;
                e.SuppressKeyPress = true;
                try { SetInputFile(row, ClipboardFirmwarePath(row.Type)); }
                catch (Exception ex) { ShowInputResult(ex.Message, true); }
            };
            row.PathBox.AllowDrop = true;
            StyleTextBox(row.PathBox, false);
            row.PathBox.TextChanged += delegate { InputPathChanged(row); };
            row.PathBox.TextChanged += delegate { tooltips.SetToolTip(row.PathBox, row.PathBox.Text); };
            row.PathBox.DragEnter += InputDragEnter;
            row.PathBox.DragDrop += delegate(object sender, DragEventArgs e) { InputDragDrop(row, e); };
            SoftField pathField = new SoftField(row.PathBox, false);
            pathField.Dock = DockStyle.Fill;
            pathField.AllowDrop = true;
            pathField.DragEnter += InputDragEnter;
            pathField.DragDrop += delegate(object sender, DragEventArgs e) { InputDragDrop(row, e); };
            Label dragHint = new Label { Name = "DragHint_" + type, Text = "拖入固件或粘贴路径", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiColors.Muted, BackColor = UiColors.Field, AutoEllipsis = true, AllowDrop = true };
            dragHint.Click += delegate { row.PathBox.Focus(); };
            dragHint.DragEnter += InputDragEnter;
            dragHint.DragDrop += delegate(object sender, DragEventArgs e) { InputDragDrop(row, e); };
            pathField.Controls.Add(dragHint);
            dragHint.BringToFront();
            EventHandler updateDragHint = delegate { dragHint.Visible = String.IsNullOrWhiteSpace(row.PathBox.Text); };
            row.PathBox.TextChanged += updateDragHint;
            table.Controls.Add(pathField, 1, index);
            Button select = new RaisedButton();
            select.Name = "Select_" + type;
            select.Text = "浏览";
            select.Dock = DockStyle.Fill;
            StyleButton(select, false, false);
            select.Click += delegate { SelectInput(row); };
            table.Controls.Add(select, 2, index);
            Button clear = new RaisedButton();
            clear.Name = "Clear_" + type;
            clear.Text = "清除";
            clear.Dock = DockStyle.Fill;
            StyleButton(clear, false, true);
            clear.Click += delegate { ClearInput(row); };
            table.Controls.Add(clear, 3, index);
            row.NameBox = new PlaceholderTextBox { PlaceholderText = "固件名称" };
            row.NameBox.Name = "Name_" + type;
            StyleTextBox(row.NameBox, false);
            row.NameBox.TextChanged += InputChanged;
            row.NameBox.TextChanged += delegate { tooltips.SetToolTip(row.NameBox, row.NameBox.Text); };
            SoftField nameField = new SoftField(row.NameBox, false);
            nameField.Dock = DockStyle.Fill;
            table.Controls.Add(nameField, 4, index);
            row.VersionBox = new PlaceholderTextBox { PlaceholderText = "固件版本" };
            row.VersionBox.Name = "Version_" + type;
            StyleTextBox(row.VersionBox, false);
            row.VersionBox.TextChanged += InputChanged;
            row.VersionBox.TextChanged += delegate { tooltips.SetToolTip(row.VersionBox, row.VersionBox.Text); };
            SoftField versionField = new SoftField(row.VersionBox, false);
            versionField.Dock = DockStyle.Fill;
            table.Controls.Add(versionField, 5, index);
            row.SizeLabel = new Label();
            row.SizeLabel.Name = "Size_" + type;
            row.SizeLabel.Text = "未选择";
            row.SizeLabel.Dock = DockStyle.Fill;
            row.SizeLabel.TextAlign = ContentAlignment.MiddleLeft;
            row.SizeLabel.ForeColor = UiColors.Muted;
            row.SizeLabel.Font = new Font("Microsoft YaHei UI", 8.3F, FontStyle.Regular);
            table.Controls.Add(row.SizeLabel, 6, index);
        }

        private void ShowHelp()
        {
            using (Form help = new Form())
            {
                help.Text = "操作说明"; help.StartPosition = FormStartPosition.CenterParent;
                help.Size = new Size(650, 520); help.MinimumSize = new Size(480, 360); help.BackColor = UiColors.Canvas;
                TextBox text = new TextBox { Name = "HelpText", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = UiColors.Canvas, Font = Font };
                text.Text = "文件与版本\r\n名称为文件名去掉扩展名；PFC/LLC/M4/ESP32 需 .bin，PLC 不限后缀。V/v 版本取至下一个下划线或点；PLC 的 maya 版本保留点号，取至下一个下划线。名称/版本可修改，各最多 39 个 UTF-8 字节。手动选入缺版本文件后须补填才能生成。\r\n\r\n一键加载\r\n仅扫描当前文件夹；按 PFC、LLC、M4、PLC、ESP32 前缀识别，大小写不敏感；PLC 不限后缀，其他类型识别 .bin。同类唯一且有效才更新；多文件、缺版本或无效项保留原选择并提示。一键清理不删除磁盘文件，不清 FTP 配置和输出位置。\r\n\r\n大小限制\r\n文件不可为空。PFC/LLC 预算：256,000 字节（250 KiB）；M4 预算：524,288 字节（512 KiB）；PLC 预算：2,097,152 字节（2 MiB）。超过预算提示但可继续。ESP32 上限：2,621,440 字节（2.5 MiB）；整包最多 6,291,456 字节（6 MiB），含包头和对齐。加载和生成都会检查。\r\n\r\nFTP\r\n生成后默认上传该联合包，也可独立选任意本地文件。联合包上传前校验；普通文件不做包格式校验。打开 FTP 目录可查看服务器返回条目，同名仅比较名称。普通 FTP 明文传输，服务器同名文件可能覆盖。\r\n\r\n查看包头\r\n生成成功后可查看本次包最前 4096 字节；只读，不改变文件，也不读取后续固件内容。地址范围 0x0000～0x0FFF。\r\n\r\n设备升级\r\n生成的联合包会自动关联至升级页面，也可拖入或选择其他 BIN。本地升级先校验包并确认设备地址；接收校验不等于安装完成，等待超时不会取消设备安装，可在本次会话继续等待。真实设备尚未联调；远程运营平台接口尚未接入。";
                text.Text += "\r\n\r\n界面配色\r\n点击顶部右侧色圈即可切换界面配色，选中项带外环；首次为银色，之后恢复上次选择。";
                help.Padding = new Padding(20); help.Controls.Add(text);
                help.Shown += delegate { text.Select(0, 0); };
                help.ShowDialog(this);
            }
        }

        private byte[] ReadGeneratedHeader()
        {
            if (lastGeneratedHeader == null || String.IsNullOrEmpty(lastGeneratedPackage))
                throw new InvalidDataException("请先成功生成联合包。");
            byte[] current = HeaderViewer.ReadHeader(lastGeneratedPackage);
            for (int i = 0; i < current.Length; i++)
                if (current[i] != lastGeneratedHeader[i]) throw new InvalidDataException("文件包头已发生变化，请重新生成后查看。");
            return current;
        }

        private void ShowGeneratedHeader()
        {
            try
            {
                using (HeaderViewer viewer = new HeaderViewer(ReadGeneratedHeader())) viewer.ShowDialog(this);
                ShowInputResult("已查看包头：" + Path.GetFileName(lastGeneratedPackage), false);
            }
            catch (Exception ex)
            {
                ShowInputResult("查看包头失败：" + ex.Message, true);
                MessageBox.Show(this, ex.Message, "无法查看包头", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowInputResult(string message, bool error)
        {
            AppendResult(message, error ? UiColors.Error : UiColors.Ink);
        }

        private void AppendResult(string message, Color color)
        {
            if (String.IsNullOrWhiteSpace(message)) return;
            details.Select(details.TextLength, 0);
            details.SelectionColor = color;
            details.AppendText("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] " +
                message.TrimEnd('\r', '\n') + Environment.NewLine);
            details.Select(details.TextLength, 0);
            details.SelectionColor = UiColors.Ink;
            details.ScrollToCaret();
        }

        private void ReportFtp(string message, Color color)
        {
            ftpStatus.ForeColor = color;
            ftpStatus.Text = message;
            AppendResult(message, color);
        }

        private void ChooseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择固件所在文件夹（仅扫描当前层）";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    Controls.Find("FolderPath", true)[0].Text = dialog.SelectedPath;
            }
        }

        private void LoadFolder(string folder)
        {
            string[] paths;
            try
            {
                if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    throw new InvalidDataException("请选择可读取的固件文件夹。");
                paths = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly);
                Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)) throw;
                ShowInputResult("加载失败：" + ex.Message, true); return;
            }
            Dictionary<FirmwareType, List<string>> groups = new Dictionary<FirmwareType, List<string>>();
            List<string> messages = new List<string>();
            bool error = false;
            foreach (string path in paths)
            {
                FirmwareType type; string name, version;
                if (!FirmwareFileName.TryParse(path, out type, out name, out version)) continue;
                if (!groups.ContainsKey(type)) groups[type] = new List<string>();
                groups[type].Add(path);
            }
            int loaded = 0;
            loadingFolder = true;
            try
            {
                foreach (Row row in rows)
                {
                    List<string> candidates;
                    if (!groups.TryGetValue(row.Type, out candidates))
                    { messages.Add(row.Type + "：未找到匹配文件，保留原选择。"); continue; }
                    if (candidates.Count != 1)
                    { error = true; messages.Add(row.Type + "：发现 " + candidates.Count + " 个同类文件，未自动选择，保留原选择。"); continue; }
                    FirmwareType type; string name, version;
                    FirmwareFileName.TryParse(candidates[0], out type, out name, out version);
                    if (String.IsNullOrEmpty(version))
                    { error = true; messages.Add(row.Type + "：文件名缺少版本，未加载，保留原选择。"); continue; }
                    try
                    {
                        PackageReport inputReport = SetInputFile(row, candidates[0]);
                        loaded++; messages.Add(row.Type + "：已加载 " + name);
                        foreach (string warning in inputReport.Warnings) messages.Add("警告：" + warning);
                    }
                    catch (Exception ex)
                    {
                        if (!(ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)) throw;
                        error = true; messages.Add(row.Type + "：未加载，保留原选择。" + ex.Message);
                    }
                }
            }
            finally { loadingFolder = false; }
            ShowInputResult("加载完成：更新 " + loaded + " 项；未匹配或无效项保持不变。" + Environment.NewLine + String.Join(Environment.NewLine, messages.ToArray()), error);
        }

        private void ClearAllInputs()
        {
            changing = true;
            try
            {
                foreach (Row row in rows)
                { row.PathBox.Clear(); row.NameBox.Clear(); row.VersionBox.Clear(); row.AutoName = null; }
            }
            finally { changing = false; }
            RefreshPreview();
            ShowInputResult("已清空五类固件选择；磁盘文件、FTP 配置和输出位置均未改变。", false);
        }

        private void SelectInput(Row row)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 " + row.Type + " 固件";
                dialog.Filter = row.Type == FirmwareType.PLC ? "所有文件 (*.*)|*.*" : "固件文件 (*.bin)|*.bin|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { SetInputFile(row, dialog.FileName); }
                catch (Exception ex) { ShowInputResult(ex.Message, true); MessageBox.Show(this, ex.Message, "无法添加固件", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private PackageReport SetInputFile(Row row, string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException(row.Type + " 固件文件不存在或不可读取。");
            if (row.Type != FirmwareType.PLC && !String.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择 .bin 固件文件。");
            FirmwareType detected; string name, version;
            if (FirmwareFileName.TryIdentifyType(path, out detected) && detected != row.Type)
                throw new InvalidDataException("文件名类型为 " + detected + "，不能加载到 " + row.Type + " 栏。");
            if (!FirmwareFileName.TryParse(path, out detected, out name, out version))
            {
                name = row.Type == FirmwareType.PLC ? FirmwareFileName.PlcFileStem(path) : Path.GetFileNameWithoutExtension(path);
                version = row.Type == FirmwareType.PLC ? FirmwareFileName.PlcVersionFromName(name) : FirmwareFileName.VersionFromName(name);
            }
            // Reuse the package's UTF-8, empty-file and size protections before changing UI state.
            // Missing manual versions remain genuinely empty and must be filled before generating.
            PackageReport inputReport = Packager.Plan(new FirmwareInput[] { new FirmwareInput { Type = row.Type, Path = path, Name = name, Version = String.IsNullOrEmpty(version) ? "待填写" : version } });
            changing = true;
            try
            {
                row.PathBox.Text = path;
                row.NameBox.Text = name;
                row.VersionBox.Text = version;
                row.AutoName = row.NameBox.Text;
            }
            finally { changing = false; }
            if (!loadingFolder) ShowInputResult("已添加 " + row.Type + " 固件：" + Path.GetFileName(path), false);
            RefreshPreview();
            if (!loadingFolder)
                foreach (string warning in inputReport.Warnings)
                    if (status.Text.IndexOf(warning, StringComparison.Ordinal) < 0)
                        ShowInputResult("警告：" + warning, false);
            return inputReport;
        }

        private static string ClipboardFirmwarePath(FirmwareType type)
        {
            try { return ReadClipboardFirmwarePath(Clipboard.GetDataObject); }
            catch (InvalidDataException ex)
            {
                if (type == FirmwareType.PLC)
                    throw new InvalidDataException(ex.Message.Replace("BIN 固件文件", "PLC 固件文件"), ex);
                throw;
            }
        }

        internal static string ReadClipboardFirmwarePath(Func<IDataObject> readClipboard)
        {
            ExternalException lastError = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    IDataObject data = readClipboard();
                    if (data != null && data.GetDataPresent(DataFormats.FileDrop))
                    {
                        string[] files = data.GetData(DataFormats.FileDrop) as string[];
                        if (files != null && files.Length > 1)
                            throw new InvalidDataException("请复制一个 BIN 固件文件。");
                        if (files != null && files.Length == 1) return files[0];
                    }
                    else if (data != null && data.GetDataPresent(DataFormats.UnicodeText))
                    {
                        string text = data.GetData(DataFormats.UnicodeText) as string;
                        if (!String.IsNullOrWhiteSpace(text)) return text.Trim().Trim('"');
                    }
                }
                catch (ExternalException ex) { lastError = ex; }
                if (attempt < 4) Thread.Sleep(25);
            }
            throw new InvalidDataException("请复制一个 BIN 固件文件或文件路径后重试。", lastError);
        }

        private static string[] DraggedFiles(DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            return e.Data.GetData(DataFormats.FileDrop) as string[];
        }

        private static void InputDragEnter(object sender, DragEventArgs e)
        {
            string[] files = DraggedFiles(e);
            e.Effect = files != null && files.Length == 1 && File.Exists(files[0])
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void InputDragDrop(Row row, DragEventArgs e)
        {
            string[] files = DraggedFiles(e);
            if (files == null || files.Length != 1 || !File.Exists(files[0]))
            {
                MessageBox.Show(this, "请向每个固件输入框拖入一个可读取的文件。", "无法添加固件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { SetInputFile(row, files[0]); }
            catch (Exception ex) { ShowInputResult(ex.Message, true); MessageBox.Show(this, ex.Message, "无法添加固件", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void InputPathChanged(Row row)
        {
            if (changing) return;
            if (File.Exists(row.PathBox.Text) && (String.IsNullOrEmpty(row.NameBox.Text) || row.NameBox.Text == row.AutoName))
            {
                changing = true;
                try
                {
                    row.NameBox.Text = Path.GetFileName(row.PathBox.Text);
                    row.AutoName = row.NameBox.Text;
                }
                finally { changing = false; }
            }
            RefreshPreview();
        }

        private void ClearInput(Row row)
        {
            bool hadSelection = !String.IsNullOrWhiteSpace(row.PathBox.Text) || !String.IsNullOrWhiteSpace(row.NameBox.Text) ||
                !String.IsNullOrWhiteSpace(row.VersionBox.Text);
            changing = true;
            row.PathBox.Clear();
            row.NameBox.Clear();
            row.VersionBox.Clear();
            row.AutoName = null;
            changing = false;
            RefreshPreview();
            if (hadSelection) ShowInputResult("已清除 " + row.Type + " 固件选择。", false);
        }

        private void InputChanged(object sender, EventArgs e)
        {
            if (!changing) RefreshPreview();
        }

        private List<FirmwareInput> Inputs()
        {
            List<FirmwareInput> inputs = new List<FirmwareInput>();
            foreach (Row row in rows)
            {
                if (String.IsNullOrWhiteSpace(row.PathBox.Text)) continue;
                inputs.Add(new FirmwareInput { Type = row.Type, Path = row.PathBox.Text, Name = row.NameBox.Text, Version = row.VersionBox.Text });
            }
            return inputs;
        }

        private void RefreshPreview()
        {
            if (workspace == null || previewCard == null) return;
            List<FirmwareInput> selected = Inputs();
            grid.Rows.Clear();
            PackageReport plan = null;
            string problem = null;
            try { if (selected.Count != 0) plan = Packager.Plan(selected); }
            catch (Exception ex) { problem = ex.Message; }
            foreach (Row row in rows)
            {
                if (String.IsNullOrWhiteSpace(row.PathBox.Text)) { row.SizeLabel.Text = "未选择"; continue; }
                string size;
                try { long bytes = new FileInfo(row.PathBox.Text).Length; size = bytes.ToString("N0") + " B"; }
                catch { size = "不可读取"; }
                row.SizeLabel.Text = size;
                string offset = "待校验";
                if (plan != null) foreach (FirmwareEntry entry in plan.Entries) if (entry.Type == row.Type) { offset = "0x" + entry.Offset.ToString("X8"); break; }
                grid.Rows.Add(row.Type, row.PathBox.Text, "大小：" + size, "偏移：" + offset);
            }
            previewCard.Visible = grid.Rows.Count > 0;
            int rowPixels = 0; foreach (DataGridViewRow visibleRow in grid.Rows) rowPixels += visibleRow.Height;
            workspace.RowStyles[1].Height = grid.Rows.Count == 0 ? 0 : previewCard.Margin.Vertical + previewCard.Padding.Vertical + rowPixels + SystemInformation.HorizontalScrollBarHeight + 6;
            status.Text = problem == null ? "" : "未就绪：" + problem;
            status.ForeColor = problem == null ? UiColors.Muted : UiColors.Error;
            if (plan != null && plan.Warnings.Count > 0) status.Text = String.Join("\r\n", plan.Warnings.ToArray());
            if (!loadingFolder && !String.IsNullOrEmpty(status.Text) && status.Text != lastPreviewMessage)
                AppendResult(status.Text, status.ForeColor);
            lastPreviewMessage = status.Text;
            workspace.PerformLayout();
        }

        private void BrowseOutput(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "保存联合固件包";
                dialog.Filter = "联合固件包 (*.bin)|*.bin|所有文件 (*.*)|*.*";
                dialog.DefaultExt = "bin";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = false;
                if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.FileName;
            }
        }

        private bool ApplyFtpAddress(string raw, bool showErrors)
        {
            try
            {
                FtpLocation location = FtpUploader.ParseAddress(raw);
                string authority = location.DirectoryUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
                if (location.HasCredentials)
                {
                    ftpUser.Text = location.Username;
                    ftpPassword.Text = location.Password;
                }
                else if (ftpAuthority != null && !String.Equals(ftpAuthority, authority, StringComparison.OrdinalIgnoreCase))
                {
                    ftpUser.Clear();
                    ftpPassword.Clear();
                }
                ftpAuthority = authority;
                ftpAddress.Text = location.DirectoryUri.AbsoluteUri;
                tooltips.SetToolTip(ftpAddress, ftpAddress.Text);
                return true;
            }
            catch (InvalidDataException ex)
            {
                if (raw != null && raw.IndexOf('@') >= 0) ftpAddress.Clear();
                ReportFtp(ex.Message, UiColors.Error);
                if (showErrors) MessageBox.Show(this, ex.Message, "FTP 地址无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void FtpUploadClicked(object sender, EventArgs e)
        {
            if (ftpBusy) return;
            if (!ApplyFtpAddress(ftpAddress.Text, true)) { SetFtpDownloadAddress(null); return; }
            try
            {
                if (String.IsNullOrWhiteSpace(ftpFile.Text) || !File.Exists(ftpFile.Text))
                    throw new InvalidDataException("请先生成联合包，或选择要上传的本地文件。");
                if (String.IsNullOrWhiteSpace(ftpUser.Text)) throw new InvalidDataException("请输入 FTP 用户名。");
                FtpLocation location = FtpUploader.ParseAddress(ftpAddress.Text);
                Uri destination = FtpUploader.FileUri(location, Path.GetFileName(ftpFile.Text));
                DialogResult answer = MessageBox.Show(this,
                    "本地文件：\n" + ftpFile.Text + "\n\n" + (ftpPackageSource ? "联合包：上传前校验。" : "普通文件：直接上传，不进行联合包校验。") + "\n\n即将上传至：\n" + destination.AbsoluteUri +
                    "\n\n普通 FTP 明文传输账号、密码和文件；服务器同名文件可能被覆盖。确定上传？",
                    "确认 FTP 上传", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) { ReportFtp("已取消 FTP 上传。", UiColors.Muted); return; }
                StartFtpTransfer(ftpFile.Text, ftpAddress.Text, ftpUser.Text, ftpPassword.Text, ftpPackageSource);
            }
            catch (InvalidDataException ex)
            {
                SetFtpDownloadAddress(null);
                sounds.Play("failure");
                ReportFtp("上传失败：" + ex.Message, UiColors.Error);
                MessageBox.Show(this, ex.Message, "上传失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void FtpDirectoryClicked(object sender, EventArgs e)
        {
            if (ftpBusy || !ApplyFtpAddress(ftpAddress.Text, true)) return;
            if (String.IsNullOrWhiteSpace(ftpUser.Text))
            {
                ReportFtp("打开 FTP 目录失败：请输入 FTP 用户名。", UiColors.Error);
                MessageBox.Show(this, "请输入 FTP 用户名。", "打开 FTP 目录失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string address = ftpAddress.Text, username = ftpUser.Text, password = ftpPassword.Text;
            string localName = String.IsNullOrEmpty(ftpFile.Text) ? null : Path.GetFileName(ftpFile.Text);
            ftpListing = true;
            SetFtpBusy(true);
            ReportFtp("正在读取 FTP 目录…", UiColors.Blue);
            ThreadPool.QueueUserWorkItem(delegate
            {
                FtpDirectoryResult result = null;
                string error = null;
                try { result = FtpUploader.ListDirectory(address, username, password); }
                catch (InvalidDataException ex) { error = ex.Message; }
                catch (Exception) { error = "读取 FTP 目录时发生错误，请检查网络和服务器状态。"; }
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (IsDisposed) return;
                        ftpListing = false;
                        SetFtpBusy(false);
                        if (result != null)
                        {
                            ReportFtp("目录读取成功：共 " + result.Entries.Length + " 个条目。", UiColors.Success);
                            using (FtpDirectoryViewer viewer = new FtpDirectoryViewer(result, localName)) viewer.ShowDialog(this);
                        }
                        else
                        {
                            sounds.Play("failure");
                            ReportFtp("打开 FTP 目录失败：" + error, UiColors.Error);
                            MessageBox.Show(this, error, "打开 FTP 目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private void StartFtpUpload(string path, string address, string username, string password)
        {
            StartFtpTransfer(path, address, username, password, true);
        }

        private void StartFtpTransfer(string path, string address, string username, string password, bool package)
        {
            SetFtpDownloadAddress(null);
            ftpListing = false;
            SetFtpBusy(true);
            ReportFtp((package ? "正在校验联合包并上传：" : "正在上传本地文件：") + Path.GetFileName(path), UiColors.Blue);
            ThreadPool.QueueUserWorkItem(delegate
            {
                FtpUploadResult result = null;
                string error = null;
                try { result = package ? FtpUploader.Upload(path, address, username, password) : FtpUploader.UploadFile(path, address, username, password); }
                catch (InvalidDataException ex) { error = ex.Message; }
                catch (Exception) { error = "读取文件或连接 FTP 时发生错误，请检查文件、网络和服务器状态。"; }
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (IsDisposed) return;
                        SetFtpBusy(false);
                        if (result != null)
                        {
                            sounds.Play("success");
                            ReportFtp("上传成功：" + Path.GetFileName(path) + "，FTP 服务器已确认接收（" + result.StatusCode + "），共 " + result.Bytes.ToString("N0") + " 字节。", UiColors.Success);
                            tooltips.SetToolTip(ftpStatus, result.RemoteUrl);
                            if (result.OcppDownloadUrl != null) SetFtpDownloadAddress(result.OcppDownloadUrl);
                            else ReportFtp("无法生成设备下载地址：" + result.OcppDownloadProblem, UiColors.Error);
                        }
                        else
                        {
                            sounds.Play("failure");
                            ReportFtp("上传失败：" + Path.GetFileName(path) + "，" + error + " 服务器上可能留有不完整文件。", UiColors.Error);
                            tooltips.SetToolTip(ftpStatus, ftpStatus.Text);
                        }
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private void SetFtpDownloadAddress(string address)
        {
            bool available = !String.IsNullOrEmpty(address);
            ftpDownloadAddress.Text = available ? address : "";
            ftpDownloadRowStyle.Height = available ? 32 : 0;
            ftpDownloadRow.Visible = available;
            ftpDownloadRow.Parent.PerformLayout();
        }

        private void CopyFtpDownloadAddress(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(ftpDownloadAddress.Text)) return;
            try
            {
                Clipboard.SetText(ftpDownloadAddress.Text);
                ReportFtp("已复制 FTP 下载地址。", UiColors.Ink);
            }
            catch (ExternalException)
            {
                ReportFtp("无法访问剪贴板，请稍后重试或手动复制下载地址。", UiColors.Error);
            }
        }

        private void SetFtpBusy(bool busy)
        {
            ftpBusy = busy;
            ftpUpload.Enabled = !busy;
            ftpDirectory.Enabled = !busy;
            ftpBrowseFile.Enabled = !busy;
            ftpFile.Enabled = !busy;

            ftpAddress.Enabled = !busy;
            ftpUser.Enabled = !busy;
            ftpPassword.Enabled = !busy;
            build.Enabled = !busy;
        }

        private void FtpFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.Cancel) return;
            if (upgradePage.TransferActive && MessageBox.Show(this,
                "本地升级传输仍在进行。关闭可能中断设备接收；已接收的安装可能继续。确定关闭？",
                "本地升级进行中", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                e.Cancel = true;
                AppendResult("已取消关闭，继续本地升级。", UiColors.Ink);
                return;
            }
            if (ftpBusy && !ftpListing && MessageBox.Show(this, "上传仍在进行。关闭可能中断传输，并在服务器上留下不完整文件。确定关闭？",
                "上传进行中", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                e.Cancel = true;
                if (upgradePage.TransferActive) AppendResult("已取消关闭，继续本地升级。", UiColors.Ink);
            }
            if (!e.Cancel) upgradePage.ShutdownClient();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tooltips.Dispose(); sounds.Dispose(); }
            base.Dispose(disposing);
            if (disposing) { pageTabFont.Dispose(); pageTabSelectedFont.Dispose(); }
        }

        private void BuildClicked(object sender, EventArgs e)
        {
            try
            {
                Packager.Plan(Inputs());
                RefreshPreview();
                if (String.IsNullOrWhiteSpace(output.Text)) throw new InvalidDataException("请选择联合包输出位置。");
                if (File.Exists(output.Text))
                {
                    DialogResult answer = MessageBox.Show(this, "输出文件已存在。确认用新联合包替换？\n" + output.Text,
                        "确认替换", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes) { ShowInputResult("已取消替换联合包。", false); return; }
                }
                PackageReport report = Packager.Build(Inputs(), output.Text);
                string generatedPath = Path.GetFullPath(output.Text);
                byte[] generatedHeader = HeaderViewer.ReadHeader(generatedPath);
                lastGeneratedPackage = generatedPath;
                lastGeneratedHeader = generatedHeader;
                headerView.Enabled = true;
                SetFtpFile(lastGeneratedPackage, true);
                upgradePage.UseGeneratedPackage(lastGeneratedPackage, report);
                ShowReport("打包并校验成功", lastGeneratedPackage, report);
            }
            catch (Exception ex)
            {
                sounds.Play("failure");
                string message = "打包失败：" + ex.Message;
                ShowInputResult(message, true);
                MessageBox.Show(this, message, "打包失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowReport(string result, string path, PackageReport report)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(result);
            text.AppendLine("文件：" + path);
            text.AppendFormat("联合包大小：{0:N0} 字节；固件数量：{1}\r\n", report.TotalSize, report.Entries.Count);
            foreach (FirmwareEntry entry in report.Entries)
            {
                text.AppendFormat("{0}  {1}  {2}  大小：{3:N0} 字节\r\n", entry.Type, entry.Name, entry.Version, entry.Size);
            }
            foreach (string warning in report.Warnings) text.AppendLine("\r\n警告：" + warning);
            sounds.Play("success");
            AppendResult(text.ToString(), UiColors.Ink);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}


