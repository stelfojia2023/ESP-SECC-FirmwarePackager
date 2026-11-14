using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SeccFirmwarePackager
{
    // Displays a snapshot of the fixed header only. No package payload is read.
    internal sealed class HeaderViewer : Form
    {
        public const int HeaderSize = 4096;
        private readonly RichTextBox hex = new RichTextBox();
        private readonly TextBox address = new TextBox();
        private readonly Label feedback = new Label();
        private int highlighted = -1;

        public HeaderViewer(string path) : this(ReadHeader(path)) { }

        public HeaderViewer(byte[] header)
        {
            string formatted = FormatHex(header);
            Text = "联合包包头 · 4096 字节";
            Name = "HeaderViewer";
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UiColors.Canvas;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(880, 650);
            Padding = new Padding(16);
            ShowInTaskbar = false;

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            FlowLayoutPanel navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            navigation.Controls.Add(new Label { Text = "十六进制地址", AutoSize = true, Margin = new Padding(0, 9, 8, 0) });
            address.Name = "HeaderOffset"; address.Text = "0x0000"; address.Width = 120;
            address.AccessibleName = "包头十六进制地址，范围 0x0000 到 0x0FFF";
            address.Margin = new Padding(0, 5, 8, 0);
            navigation.Controls.Add(address);
            Button jump = new RaisedButton { Text = "定位", Width = 88, Height = 32, BackColor = UiColors.SoftBlue, ForeColor = UiColors.Ink, Margin = new Padding(0, 2, 8, 0) };
            jump.Click += delegate { JumpToAddress(); };
            navigation.Controls.Add(jump);
            navigation.Controls.Add(new Label { Text = "0x0000 – 0x0FFF · 只读", AutoSize = true, ForeColor = UiColors.Muted, Margin = new Padding(0, 9, 0, 0) });
            address.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; JumpToAddress(); } };
            layout.Controls.Add(navigation, 0, 0);
            feedback.Dock = DockStyle.Fill; feedback.Text = "每行 16 字节，按 8 + 8 分组；右侧为 ASCII。"; feedback.ForeColor = UiColors.Muted;
            feedback.AccessibleName = "地址定位结果";
            layout.Controls.Add(feedback, 0, 1);
            hex.Name = "HeaderHex"; hex.Dock = DockStyle.Fill; hex.ReadOnly = true; hex.WordWrap = false;
            hex.DetectUrls = false; hex.HideSelection = false; hex.BackColor = Color.FromArgb(253, 252, 249);
            hex.ForeColor = UiColors.Ink; hex.Font = new Font("Consolas", 10F);
            hex.ScrollBars = RichTextBoxScrollBars.Both; hex.BorderStyle = BorderStyle.FixedSingle;
            hex.AccessibleName = "只读包头十六进制和 ASCII 内容";
            hex.Text = formatted; layout.Controls.Add(hex, 0, 2);

            // Size to the actual RichEdit line, accounting for font metrics and window chrome.
            PerformLayout(); layout.PerformLayout();
            int lastIndex = hex.Lines[0].Length - 1;
            Point last = hex.GetPositionFromCharIndex(lastIndex);
            int glyphWidth = TextRenderer.MeasureText("|", hex.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            int textWidth = last.X > 0 ? last.X + glyphWidth : TextRenderer.MeasureText(hex.Lines[0], hex.Font,
                Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            int navigationWidth = navigation.Controls[navigation.Controls.Count - 1].Right;
            int compactWidth = Size.Width - hex.ClientSize.Width + Math.Max(textWidth, navigationWidth) + 12;
            MinimumSize = new Size(compactWidth, 450);
            Size = new Size(compactWidth, 650);
        }

        public static byte[] ReadHeader(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择联合包文件。", "path");
            byte[] header = new byte[HeaderSize];
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int count = 0;
                while (count < header.Length)
                {
                    int read = input.Read(header, count, header.Length - count);
                    if (read == 0) throw new InvalidDataException("文件不足 4096 字节，无法显示完整包头。");
                    count += read;
                }
            }
            return header;
        }

        public static string FormatHex(byte[] header)
        {
            if (header == null || header.Length != HeaderSize) throw new ArgumentException("包头必须恰好为 4096 字节。", "header");
            StringBuilder result = new StringBuilder();
            for (int row = 0; row < HeaderSize; row += 16)
            {
                result.Append(row.ToString("X4", CultureInfo.InvariantCulture)).Append("  ");
                for (int col = 0; col < 16; col++)
                {
                    if (col == 8) result.Append(' ');
                    result.Append(header[row + col].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                }
                result.Append(" |");
                for (int col = 0; col < 16; col++)
                {
                    byte value = header[row + col];
                    result.Append(value >= 32 && value <= 126 ? (char)value : '.');
                }
                result.Append('|');
                if (row + 16 < HeaderSize) result.Append('\n');
            }
            return result.ToString();
        }

        public static bool TryParseOffset(string value, out int offset)
        {
            offset = 0;
            if (value == null) return false;
            string text = value.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            return text.Length > 0 && text.Length <= 4 && Int32.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out offset) && offset >= 0 && offset < HeaderSize;
        }

        private void JumpToAddress()
        {
            int offset;
            if (!TryParseOffset(address.Text, out offset))
            {
                feedback.ForeColor = Color.Firebrick;
                feedback.Text = "地址无效：请输入 0x0000 到 0x0FFF 的十六进制地址。";
                address.Focus(); address.SelectAll(); return;
            }
            if (highlighted >= 0) { hex.Select(highlighted, 2); hex.SelectionBackColor = hex.BackColor; }
            int column = offset % 16;
            highlighted = hex.GetFirstCharIndexFromLine(offset / 16) + 6 + column * 3 + (column >= 8 ? 1 : 0);
            hex.Select(highlighted, 2); hex.SelectionBackColor = Color.FromArgb(225, 213, 156);
            hex.ScrollToCaret(); hex.Focus();
            feedback.ForeColor = UiColors.Ink;
            feedback.Text = "已定位 0x" + offset.ToString("X4", CultureInfo.InvariantCulture) + "，高亮为该地址的字节。";
        }
    }
}
