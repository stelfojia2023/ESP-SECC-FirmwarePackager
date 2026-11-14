using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SeccFirmwarePackager;

// Drives the delivered EXE's real Win32 controls and common dialogs on an interactive desktop.
internal static class ReleaseUiTests
{
    private const uint WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, WM_CLOSE = 0x0010, BM_CLICK = 0x00F5;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
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

    private static Node EditInRow(IntPtr root, Node reference, int position)
    {
        List<Node> candidates = new List<Node>();
        foreach (Node child in Children(root))
            if (IsClass(child, "EDIT") && Math.Abs(child.Bounds.Top - reference.Bounds.Top) <= 8 && IsWindowVisible(child.Handle))
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

    public static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: ReleaseUiTests.exe <release EXE> <fresh directory>"); return 1; }
        Process process = null;
        try
        {
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
            Node browseM4 = Button(root, "浏览", 2);
            Click(browseM4.Handle);
            ChooseFile(id, "选择 M4 固件", input);
            Node m4Path = EditInRow(root, browseM4, 0);
            Node m4Name = EditInRow(root, browseM4, 1);
            Node m4Version = EditInRow(root, browseM4, 2);
            if (Describe(m4Path.Handle).Text != input || Describe(m4Name.Handle).Text != Path.GetFileName(input))
                throw new Exception("Browse dialog did not populate path and default firmware name.");
            Click(Button(root, "生成联合包", 0).Handle);
            DismissError(id, "打包失败", "版本");
            SetText(m4Version.Handle, "V000B01D15");
            Click(Button(root, "选择位置", 0).Handle);
            ChooseFile(id, "保存联合固件包", output);
            Node outputButton = Button(root, "生成联合包", 0);
            Node outputEdit = EditInRow(root, outputButton, 0);
            if (Describe(outputEdit.Handle).Text != output) throw new Exception("Save dialog did not populate output path.");
            Click(outputButton.Handle);
            WaitUntil(delegate { return File.Exists(output) && ContainsText(root, "打包并校验成功"); }, "Build did not finish in the release GUI.");
            if (Packager.VerifyFile(output).Entries.Count != 1) throw new Exception("GUI-generated package is invalid.");
            Click(Button(root, "打开并校验联合包", 0).Handle);
            ChooseFile(id, "打开并校验 V1 联合固件包", output);
            WaitUntil(delegate { return ContainsText(root, "校验通过"); }, "Open and verify did not display success.");
            byte[] corrupt = File.ReadAllBytes(output);
            corrupt[4096] ^= 1;
            string corruptPath = Path.Combine(work, "损坏 联合 包.bin");
            File.WriteAllBytes(corruptPath, corrupt);
            Click(Button(root, "打开并校验联合包", 0).Handle);
            ChooseFile(id, "打开并校验 V1 联合固件包", corruptPath);
            DismissError(id, "校验失败", "固件 MD5");
            if (!ContainsText(root, "校验失败")) throw new Exception("Verification failure was not displayed in the result area.");
            process.CloseMainWindow();
            if (!process.WaitForExit(10000)) throw new Exception("Release window did not close.");
            if (process.ExitCode != 0) throw new Exception("Release returned nonzero exit code.");
            Console.WriteLine("PASS delivered EXE: browse, default name, missing-version error, save, generate, open, verify and corrupted-file error");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL release UI " + ex); return 1; }
        finally { if (process != null) { process.Refresh(); if (!process.HasExited) process.Kill(); process.Dispose(); } }
    }
}
