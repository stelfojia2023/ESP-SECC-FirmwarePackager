using System;
using System.Drawing;
using System.Runtime.InteropServices;
internal static class IconCompare
{
    [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="PrivateExtractIconsW")]
    static extern uint Extract(string path, int index, int width, int height, IntPtr[] icons, uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    static Bitmap Read(string path, int size)
    {
        IntPtr[] handles = new IntPtr[1]; uint[] ids = new uint[1];
        if (Extract(path, 0, size, size, handles, ids, 1, 0) != 1 || handles[0] == IntPtr.Zero) throw new Exception("Cannot extract icon " + size);
        try { using (Icon icon = Icon.FromHandle(handles[0])) return icon.ToBitmap(); }
        finally { DestroyIcon(handles[0]); }
    }
    static int Main(string[] args)
    {
        try
        {
            foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
                using (Bitmap before = Read(args[0], size))
                using (Bitmap after = Read(args[1], size))
                {
                    if (before.Size != after.Size) throw new Exception("Icon dimensions changed");
                    for (int y = 0; y < before.Height; y++)
                        for (int x = 0; x < before.Width; x++)
                            if (before.GetPixel(x, y).ToArgb() != after.GetPixel(x, y).ToArgb()) throw new Exception("Icon pixels changed at " + size);
                }
            Console.WriteLine("PASS embedded EXE icon pixel comparison at seven sizes");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }
}
