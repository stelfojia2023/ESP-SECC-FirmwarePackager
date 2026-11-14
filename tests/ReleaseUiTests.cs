using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using SeccFirmwarePackager;

// Drives the delivered EXE's real Win32 controls and common dialogs on an interactive desktop.
internal static class ReleaseUiTests
{
    private const uint WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, WM_CLOSE = 0x0010, BM_CLICK = 0x00F5;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extra);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendText(IntPtr window, uint message, IntPtr length, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr GetTextMessage(IntPtr window, uint message, IntPtr length, StringBuilder text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendNumber(IntPtr window, uint message, IntPtr a, IntPtr b);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr a, IntPtr b);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PointNative point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    private sealed class Node
    {
        public IntPtr Handle;
        public string ClassName;
        public string Text;
        public int Id;
        public Rect Bounds;
    }

    private static Node Describe(IntPtr handle)
    {
        StringBuilder name = new StringBuilder(256);
        GetClassName(handle, name, name.Capacity);
        int length = (int)SendNumber(handle, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        if (length < 0 || length > 65535) length = 65535;
        StringBuilder text = new StringBuilder(Math.Max(2, length + 1));
        GetTextMessage(handle, WM_GETTEXT, (IntPtr)text.Capacity, text);
        Rect bounds;
        GetWindowRect(handle, out bounds);
        return new Node { Handle = handle, ClassName = name.ToString(), Text = text.ToString(), Id = GetDlgCtrlID(handle), Bounds = bounds };
    }

    private static List<Node> Children(IntPtr parent)
    {
        List<Node> result = new List<Node>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr ignored) { result.Add(Describe(h)); return true; }, IntPtr.Zero);
        return result;
    }

    private static bool IsClass(Node node, string name)
    {
        return node.ClassName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void WaitUntil(Func<bool> condition, string failure)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 12000)
        {
            if (condition()) return;
            Thread.Sleep(100);
        }
        throw new Exception(failure);
    }

    private static void Click(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !PostMessage(handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero))
            throw new Exception("Could not click a control.");
    }

    private static void SetText(IntPtr handle, string value)
    {
        if (SendText(handle, WM_SETTEXT, IntPtr.Zero, value) == IntPtr.Zero)
            throw new Exception("Could not set control text.");
    }

    private static Node Button(IntPtr root, string caption, int position)
    {
        List<Node> matches = new List<Node>();
        foreach (Node child in Children(root))
            if (IsClass(child, "BUTTON") && child.Text == caption) matches.Add(child);
        matches.Sort(delegate(Node a, Node b) { return a.Bounds.Top.CompareTo(b.Bounds.Top); });
        if (position >= matches.Count) throw new Exception("Button not found: " + caption + " #" + position);
        return matches[position];
    }

    private static IntPtr Dialog(uint processId, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr ignored)
        {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner != processId || !IsWindowVisible(h)) return true;
            StringBuilder text = new StringBuilder(1024);
            GetWindowText(h, text, text.Capacity);
            if (text.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void ChooseFile(uint processId, string title, string filePath)
    {
        IntPtr dialog = IntPtr.Zero;
        WaitUntil(delegate { dialog = Dialog(processId, title); return dialog != IntPtr.Zero; }, "File dialog did not appear: " + title);
        List<Node> edits = new List<Node>();
        foreach (Node child in Children(dialog))
            if (child.ClassName.Equals("Edit", StringComparison.OrdinalIgnoreCase) && IsWindowVisible(child.Handle)) edits.Add(child);
        edits.Sort(delegate(Node a, Node b) { return b.Bounds.Top.CompareTo(a.Bounds.Top); });
        if (edits.Count == 0) throw new Exception("No filename edit control in " + title);
        SetText(edits[0].Handle, filePath);
        IntPtr ok = GetDlgItem(dialog, 1);
        if (ok == IntPtr.Zero)
        {
            foreach (Node child in Children(dialog))
                if (child.Id == 1 && IsClass(child, "BUTTON")) { ok = child.Handle; break; }
        }
        Click(ok);
        WaitUntil(delegate { return Dialog(processId, title) == IntPtr.Zero; }, "File dialog did not close: " + title);
    }

    private static void DismissError(uint processId, string title, string expectedMessage)
    {
        IntPtr dialog = IntPtr.Zero;
        WaitUntil(delegate { dialog = Dialog(processId, title); return dialog != IntPtr.Zero; }, "Error dialog did not appear: " + title);
        if (!ContainsText(dialog, expectedMessage)) throw new Exception("Error dialog lacks expected reason: " + expectedMessage);
        if (!PostMessage(dialog, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)) throw new Exception("Could not close error dialog.");
        WaitUntil(delegate { return Dialog(processId, title) == IntPtr.Zero; }, "Error dialog did not close: " + title);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);

    private static Node EditInMetadataRow(IntPtr root, Node reference, int position)
    {
        List<Node> candidates = new List<Node>();
        IntPtr row = GetParent(reference.Handle);
        foreach (Node child in Children(root))
            if (IsClass(child, "EDIT") && GetParent(GetParent(child.Handle)) == row &&
                child.Bounds.Top > reference.Bounds.Bottom && IsWindowVisible(child.Handle))
                candidates.Add(child);
        candidates.Sort(delegate(Node a, Node b) { return a.Bounds.Left.CompareTo(b.Bounds.Left); });
        if (position >= candidates.Count) throw new Exception("Could not locate metadata edit #" + position);
        return candidates[position];
    }

    private static Node EditInRow(IntPtr root, Node reference, int position)
    {
        List<Node> candidates = new List<Node>();
        foreach (Node child in Children(root))
            if (IsClass(child, "EDIT") && GetParent(GetParent(child.Handle)) == GetParent(reference.Handle) && Math.Abs(child.Bounds.Top - reference.Bounds.Top) <= 14 && IsWindowVisible(child.Handle))
                candidates.Add(child);
        candidates.Sort(delegate(Node a, Node b) { return a.Bounds.Left.CompareTo(b.Bounds.Left); });
        if (position >= candidates.Count) throw new Exception("Could not locate row edit #" + position);
        return candidates[position];
    }

    private static bool ContainsText(IntPtr root, string expected)
    {
        foreach (Node child in Children(root))
            if (child.Text.IndexOf(expected, StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    private static void RealDrag(IntPtr target, string path)
    {
        Rect destination;
        if (!GetWindowRect(target, out destination) || destination.Right <= destination.Left || destination.Bottom <= destination.Top)
            throw new Exception("Cannot determine the drop target bounds.");
        int targetX = (destination.Left + destination.Right) / 2;
        int targetY = (destination.Top + destination.Bottom) / 2;
        PointNative oldCursor;
        GetCursorPos(out oldCursor);
        DragDropEffects effect = DragDropEffects.None;
        Exception failure = null;
        bool started = false;
        int attempts = 0;
        using (Form source = new Form())
        using (Label surface = new Label())
        using (System.Windows.Forms.Timer start = new System.Windows.Forms.Timer())
        using (System.Windows.Forms.Timer deadline = new System.Windows.Forms.Timer())
        {
            source.Text = "Drag test source";
            source.StartPosition = FormStartPosition.Manual;
            source.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            source.Location = new Point(12, 12);
            source.ClientSize = new Size(160, 65);
            source.TopMost = true;
            surface.Text = "Test file";
            surface.TextAlign = ContentAlignment.MiddleCenter;
            surface.Dock = DockStyle.Fill;
            source.Controls.Add(surface);
            surface.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (started || e.Button != MouseButtons.Left) return;
                started = true;
                start.Stop();
                Point initial = surface.PointToScreen(new Point(surface.Width / 2, surface.Height / 2));
                Console.WriteLine("Native drag mouse-down event received; source=" + initial.X + "," + initial.Y + " target=" + targetX + "," + targetY);
                Thread mover = new Thread(delegate()
                {
                    try
                    {
                        Thread.Sleep(250);
                        for (int i = 1; i <= 12; i++)
                        {
                            SetCursorPos(initial.X + (targetX - initial.X) * i / 12, initial.Y + (targetY - initial.Y) * i / 12);
                            Thread.Sleep(40);
                        }
                    }
                    finally
                    {
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                        Console.WriteLine("Native drag mouse-up sent");
                    }
                });
                mover.IsBackground = true;
                mover.Start();
                try
                {
                    DataObject data = new DataObject();
                    data.SetData(DataFormats.FileDrop, new[] { path });
                    effect = surface.DoDragDrop(data, DragDropEffects.Copy);
                    Console.WriteLine("Native drag result=" + effect);
                }
                catch (Exception ex) { failure = ex; }
                finally { if (!source.IsDisposed) source.BeginInvoke(new MethodInvoker(source.Close)); }
            };
            start.Interval = 500;
            start.Tick += delegate
            {
                if (started || ++attempts > 5) { start.Stop(); return; }
                source.Activate();
                bool foreground = SetForegroundWindow(source.Handle);
                Point initial = surface.PointToScreen(new Point(surface.Width / 2, surface.Height / 2));
                SetCursorPos(initial.X, initial.Y);
                PointNative actual;
                GetCursorPos(out actual);
                IntPtr under = WindowFromPoint(actual);
                uint underProcess;
                GetWindowThreadProcessId(under, out underProcess);
                Console.WriteLine("Native drag start=" + initial.X + "," + initial.Y + " cursor=" + actual.X + "," + actual.Y + " source-pid=" + Process.GetCurrentProcess().Id + " pointer-pid=" + underProcess + " foreground=" + foreground + " source-active=" + (GetForegroundWindow() == source.Handle));
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            };
            deadline.Interval = 12000;
            deadline.Tick += delegate { deadline.Stop(); failure = new Exception("Native drag timed out; started=" + started + ", attempts=" + attempts + ", target=" + destination.Left + "," + destination.Top + "."); mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero); source.Close(); };
            source.Shown += delegate { start.Start(); deadline.Start(); };
            try { Application.Run(source); }
            finally { mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero); SetCursorPos(oldCursor.X, oldCursor.Y); }
        }
        if (failure != null) throw new Exception("Native drag failed.", failure);
        if (!started || effect != DragDropEffects.Copy) throw new Exception("Native file drag was not accepted by the application.");
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length < 2 || args.Length > 3) { Console.Error.WriteLine("Usage: ReleaseUiTests.exe <release EXE> <fresh directory> [previous V1 sample]"); return 1; }
        Process process = null;
        try
        {
            SetProcessDPIAware();
            string exe = Path.GetFullPath(args[0]);
            string work = Path.GetFullPath(args[1]);
            if (Directory.Exists(work)) throw new Exception("Test directory already exists.");
            Directory.CreateDirectory(work);
            string input = Path.Combine(work, "M4 固件 文件.bin");
            string output = Path.Combine(work, "联合 包 文件.bin");
            File.WriteAllBytes(input, new byte[] { 0, 1, 2, 3, 255, 9 });
            process = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
            WaitUntil(delegate { process.Refresh(); return process.MainWindowHandle != IntPtr.Zero; }, "Release GUI window did not appear.");
            Thread.Sleep(1000);
            IntPtr root = process.MainWindowHandle;
            uint id = (uint)process.Id;
            Node inlineUpload = Button(root, "上传到 FTP", 0);
            if (!IsWindowVisible(inlineUpload.Handle)) throw new Exception("FTP upload must be visible inline on startup.");
            foreach (Node child in Children(root))
                if (IsClass(child, "BUTTON") && child.Text == "包校验")
                    throw new Exception("Standalone package verification button must be removed.");
            Click(Button(root, "ⓘ", 0).Handle);
            DismissError(id, "操作说明", "文件与版本");
            Node browseM4 = Button(root, "选择 M4 固件", 0);
            Node dropTarget = EditInRow(root, browseM4, 0);
            RealDrag(dropTarget.Handle, input);
            WaitUntil(delegate { return Describe(dropTarget.Handle).Text == input; }, "Native file drag did not populate the input path.");
            Node droppedName = EditInMetadataRow(root, browseM4, 0);
            if (Describe(droppedName.Handle).Text != Path.GetFileNameWithoutExtension(input)) throw new Exception("Native file drag did not populate the default name.");
            Click(browseM4.Handle);
            ChooseFile(id, "选择 M4 固件", input);
            Node m4Path = EditInRow(root, browseM4, 0);
            Node m4Name = EditInMetadataRow(root, browseM4, 0);
            Node m4Version = EditInMetadataRow(root, browseM4, 1);
            if (Describe(m4Path.Handle).Text != input || Describe(m4Name.Handle).Text != Path.GetFileNameWithoutExtension(input))
                throw new Exception("Browse dialog did not populate path and default firmware name.");
            Click(Button(root, "包生成", 0).Handle);
            DismissError(id, "打包失败", "版本");
            SetText(m4Version.Handle, "V000B01D15");
            Click(Button(root, "选择输出位置", 0).Handle);
            ChooseFile(id, "保存联合固件包", output);
            Node outputButton = Button(root, "包生成", 0);
            Node outputEdit = EditInRow(root, Button(root, "选择输出位置", 0), 0);
            if (Describe(outputEdit.Handle).Text != output) throw new Exception("Save dialog did not populate output path.");
            Click(outputButton.Handle);
            WaitUntil(delegate { return File.Exists(output) && ContainsText(root, "打包并校验成功"); }, "Build did not finish in the release GUI.");
            if (Packager.VerifyFile(output).Entries.Count != 1) throw new Exception("GUI-generated package is invalid.");
            Click(Button(root, "查看包头", 0).Handle);
            DismissError(id, "联合包包头 · 4096 字节", "SECCPKG1");
            if (args.Length == 3)
            {
                string previous = Path.GetFullPath(args[2]);
                if (!File.Exists(previous)) throw new Exception("Previous V1 sample not found.");
                if (Packager.VerifyFile(previous).Entries.Count != 2) throw new Exception("Previous V1 sample has unexpected contents.");
            }
            byte[] corrupt = File.ReadAllBytes(output);
            corrupt[4096] ^= 1;
            string corruptPath = Path.Combine(work, "损坏 联合 包.bin");
            File.WriteAllBytes(corruptPath, corrupt);
            bool rejected = false;
            try { Packager.VerifyFile(corruptPath); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Core validation did not reject corrupted package.");
            process.CloseMainWindow();
            if (!process.WaitForExit(10000)) throw new Exception("Release window did not close.");
            if (process.ExitCode != 0) throw new Exception("Release returned nonzero exit code.");
            Console.WriteLine("PASS delivered EXE: help, native file drag, browse, default name, missing-version error, save, generate, header dialog; core rejects corruption" + (args.Length == 3 ? " and validates prior sample" : ""));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL release UI " + ex); return 1; }
        finally { if (process != null) { process.Refresh(); if (!process.HasExited) process.Kill(); process.Dispose(); } }
    }
}

