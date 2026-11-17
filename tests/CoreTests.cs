using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using SeccFirmwarePackager;

internal static class CoreTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newName, string existingName, IntPtr securityAttributes);
    private static int assertions;
    private static string root;
    private static readonly int[] Sizes = { 1, 4097, 8193, 137, 4095 };
    private static readonly FirmwareType[] Types = { FirmwareType.PFC, FirmwareType.LLC, FirmwareType.M4, FirmwareType.PLC, FirmwareType.ESP32 };

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("ASSERT: " + message);
    }

    private static void Fails(Action action, string contains)
    {
        try { action(); }
        catch (Exception e)
        {
            Assert(e.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0, "expected '" + contains + "', got '" + e.Message + "'");
            return;
        }
        throw new Exception("Expected rejection containing: " + contains);
    }

    private static byte[] Pattern(int length, int type)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++) data[i] = (byte)(((i * 37) + type * 53 + (i >> 7)) & 255);
        return data;
    }

    private static byte[] Digest(byte[] bytes)
    {
        using (MD5 md5 = MD5.Create()) return md5.ComputeHash(bytes);
    }

    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static string SourcePath(int index)
    {
        return Path.Combine(root, "输入 固件", Types[index] + " 中文 firmware.bin");
    }

    private static FirmwareInput Input(int index)
    {
        return new FirmwareInput { Type = Types[index], Path = SourcePath(index), Name = Types[index] + " 固件 名称.bin", Version = "V000B01D15" };
    }

    private static List<FirmwareInput> Inputs(int mask)
    {
        List<FirmwareInput> result = new List<FirmwareInput>();
        for (int i = 4; i >= 0; i--) if ((mask & (1 << i)) != 0) result.Add(Input(i));
        return result;
    }

    private static void Rehash(byte[] bytes)
    {
        Array.Clear(bytes, 24, 16);
        Buffer.BlockCopy(Digest(bytes), 0, bytes, 24, 16);
    }

    private static byte[] Copy(byte[] source)
    {
        return (byte[])source.Clone();
    }

    private static void Put32(byte[] bytes, int offset, uint n)
    {
        for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(n >> (8 * i));
    }

    private static void CombinationTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "输入 固件"));
        Directory.CreateDirectory(Path.Combine(root, "组合 包"));
        for (int i = 0; i < 5; i++) File.WriteAllBytes(SourcePath(i), Pattern(Sizes[i], i + 1));
        for (int mask = 1; mask < 32; mask++)
        {
            string output = Path.Combine(root, "组合 包", "combo-" + mask.ToString("00") + ".bin");
            PackageReport report = Packager.Build(Inputs(mask), output);
            byte[] package = File.ReadAllBytes(output);
            Assert(package.Length == report.TotalSize && package.Length % 4096 == 0, "total length and alignment " + mask);
            byte[] storedWhole = new byte[16];
            Buffer.BlockCopy(package, 24, storedWhole, 0, 16);
            byte[] zeroed = Copy(package);
            Array.Clear(zeroed, 24, 16);
            Assert(Equal(storedWhole, Digest(zeroed)), "whole digest " + mask);
            Assert(Equal(storedWhole, report.PackageMd5), "reported digest " + mask);
            int index = 0;
            int expectedOffset = 4096;
            for (int i = 0; i < 5; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                FirmwareEntry entry = report.Entries[index++];
                Assert(entry.Type == Types[i], "order " + mask);
                Assert(entry.Offset == expectedOffset && entry.Size == Sizes[i], "offset and size " + mask);
                Assert(entry.Name == Types[i] + " 固件 名称.bin" && entry.Version == "V000B01D15", "metadata " + mask);
                byte[] expected = File.ReadAllBytes(SourcePath(i));
                byte[] extracted = new byte[entry.Size];
                Buffer.BlockCopy(package, (int)entry.Offset, extracted, 0, extracted.Length);
                Assert(Equal(extracted, expected), "input extraction " + mask);
                Assert(Equal(entry.Md5, Digest(expected)), "individual digest " + mask);
                int end = expectedOffset + expected.Length;
                int aligned = (end + 4095) / 4096 * 4096;
                for (int p = end; p < aligned; p++) Assert(package[p] == 255, "0xff padding " + mask);
                expectedOffset = aligned;
            }
            Assert(report.Entries.Count == index && report.TotalSize == expectedOffset, "entry count and final size " + mask);
            Assert(Packager.VerifyFile(output).Entries.Count == index, "verify file " + mask);
        }
        Console.WriteLine("PASS 31 nonempty combinations, alignment, extraction and hashes");
    }

    private static void BudgetTest()
    {
        string inputDir = Path.Combine(root, "预算上限 输入");
        Directory.CreateDirectory(inputDir);
        int[] budget = { 250 * 1024, 250 * 1024, 512 * 1024, 2 * 1024 * 1024, 5 * 1024 * 1024 / 2 };
        uint[] offsets = { 0x001000, 0x040000, 0x07F000, 0x0FF000, 0x2FF000 };
        List<FirmwareInput> inputs = new List<FirmwareInput>();
        for (int i = 0; i < 5; i++)
        {
            string path = Path.Combine(inputDir, Types[i] + ".bin");
            File.WriteAllBytes(path, Pattern(budget[i], i + 1));
            inputs.Add(new FirmwareInput { Type = Types[i], Path = path, Name = Types[i] + ".bin", Version = "V000B01D15" });
        }
        string output = Path.Combine(root, "预算上限.bundle.bin");
        PackageReport report = Packager.Build(inputs, output);
        Assert(report.TotalSize == 0x57F000, "budget total exactly 0x57F000");
        Assert(report.Warnings.Count == 0, "budget thresholds themselves are not warnings");
        for (int i = 0; i < 5; i++) Assert(report.Entries[i].Offset == offsets[i], "budget offset " + Types[i]);
        Console.WriteLine("PASS 5,628 KiB budget vector and all five expected offsets");
    }

    private static void CorruptionTests()
    {
        byte[] valid = File.ReadAllBytes(Path.Combine(root, "组合 包", "combo-31.bin"));
        byte[] b = Copy(valid); b[0] ^= 1; Fails(delegate { Packager.VerifyBytes(b); }, "包标识");
        b = Copy(valid); b[8] = 2; Fails(delegate { Packager.VerifyBytes(b); }, "不支持的包格式版本");
        b = Copy(valid); b[10] = 0; b[11] = 0; Fails(delegate { Packager.VerifyBytes(b); }, "包头长度");
        b = Copy(valid); b[12] ^= 1; Fails(delegate { Packager.VerifyBytes(b); }, "总长度");
        b = Copy(valid); b[16] = 0; Fails(delegate { Packager.VerifyBytes(b); }, "数量");
        b = Copy(valid); b[16] = 6; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "数量");
        b = Copy(valid); b[0x28 + 5 * 256] = 1; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "包头区域");
        b = Copy(valid); b[0x28 + 0x6E] = 1; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "预留区");
        b = Copy(valid); b[0x28 + 256] = 1; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "类型");
        b = Copy(valid); Put32(b, 0x28 + 4, 0); Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "偏移");
        b = Copy(valid); Put32(b, 0x28 + 8, 0x7FFFFFFF); Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "越界");
        b = Copy(valid); b[0x28 + 2] = 0; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "长度");
        b = Copy(valid); b[0x28 + 2] = 40; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "长度");
        b = Copy(valid); b[0x28 + 0x1C] = 0; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "长度");
        b = Copy(valid); b[0x28 + 0x1C] = 40; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "长度");
        b = Copy(valid); b[0x28 + 0x1E] = 0xFF; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "UTF-8");
        b = Copy(valid); b[0x28 + 0x1E + b[0x28 + 2]] = 1; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "未使用区域");
        b = Copy(valid); b[0x28 + 0x46 + b[0x28 + 0x1C]] = 1; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "未使用区域");
        b = Copy(valid); Put32(b, 0x14, 0x80); Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "目录");
        b = Copy(valid); b[4096] ^= 1; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "固件 MD5");
        b = Copy(valid); b[4097] = 0; Rehash(b); Fails(delegate { Packager.VerifyBytes(b); }, "填充");
        b = Copy(valid); b[24] ^= 1; Fails(delegate { Packager.VerifyBytes(b); }, "整包 MD5");
        b = new byte[valid.Length - 1]; Buffer.BlockCopy(valid, 0, b, 0, b.Length); Fails(delegate { Packager.VerifyBytes(b); }, "总长度");
        b = new byte[1024]; Fails(delegate { Packager.VerifyBytes(b); }, "截断");
        Console.WriteLine("PASS corruption, structural rejection, firmware/whole MD5, padding and truncation");
    }

    private static void ErrorTests()
    {
        Fails(delegate { Packager.Plan(new List<FirmwareInput>()); }, "未选择");
        FirmwareInput noVersion = Input(0); noVersion.Version = " ";
        Fails(delegate { Packager.Plan(new[] { noVersion }); }, "版本");
        FirmwareInput badPath = Input(0); badPath.Path = Path.Combine(root, "不存在的输入.bin");
        Fails(delegate { Packager.Plan(new[] { badPath }); }, "不可读取");
        using (FileStream locked = new FileStream(SourcePath(0), FileMode.Open, FileAccess.Read, FileShare.None))
            Fails(delegate { Packager.Plan(Inputs(1)); }, "不可读取");
        string emptyPath = Path.Combine(root, "空 文件.bin"); File.WriteAllBytes(emptyPath, new byte[0]);
        FirmwareInput empty = Input(0); empty.Path = emptyPath;
        Fails(delegate { Packager.Plan(new[] { empty }); }, "文件为空");
        FirmwareInput longName = Input(0); longName.Name = new string('固', 14);
        Fails(delegate { Packager.Plan(new[] { longName }); }, "最多允许 39");
        FirmwareInput validName = Input(0); validName.Name = new string('固', 13);
        Assert(Packager.Plan(new[] { validName }).Entries.Count == 1, "exact 39 UTF-8 bytes accepted");
        FirmwareInput emojiName = Input(0); emojiName.Name = new string('固', 12) + "😀";
        Fails(delegate { Packager.Plan(new[] { emojiName }); }, "最多允许 39");
        FirmwareInput longVersion = Input(0); longVersion.Version = new string('v', 40);
        Fails(delegate { Packager.Plan(new[] { longVersion }); }, "最多允许 39");
        FirmwareInput goodVersion = Input(0); goodVersion.Version = new string('v', 39);
        Assert(Packager.Plan(new[] { goodVersion }).Entries.Count == 1, "exact 39 bytes version accepted");
        FirmwareInput invalidUnicode = Input(0); invalidUnicode.Name = "\uD800";
        Fails(delegate { Packager.Plan(new[] { invalidUnicode }); }, "有效的 Unicode");
        FirmwareInput duplicate = Input(0);
        Fails(delegate { Packager.Plan(new[] { duplicate, Input(0) }); }, "重复");
        int[] budgets = { 250 * 1024, 250 * 1024, 512 * 1024, 2 * 1024 * 1024, 5 * 1024 * 1024 / 2 };
        for (int i = 0; i < budgets.Length; i++)
        {
            string boundary = Path.Combine(root, Types[i] + "-boundary.bin");
            FirmwareInput item = Input(i); item.Path = boundary;
            File.WriteAllBytes(boundary, new byte[0]);
            Fails(delegate { Packager.Plan(new[] { item }); }, "文件为空");
            File.WriteAllBytes(boundary, new byte[budgets[i]]);
            PackageReport exact = Packager.Plan(new[] { item });
            Assert(exact.Entries[0].Size == budgets[i] && exact.Warnings.Count == 0, Types[i] + " exact threshold accepted without warning");
            string generated = Path.Combine(root, "limit-" + Types[i] + ".bin");
            Packager.Build(new[] { item }, generated);
            Assert(Packager.VerifyFile(generated).Entries[0].Size == budgets[i], Types[i] + " boundary build and verify");
            File.WriteAllBytes(boundary, new byte[budgets[i] + 1]);
            if (i < 4)
            {
                PackageReport warning = Packager.Plan(new[] { item });
                Assert(warning.Warnings.Count == 1 && warning.Warnings[0].Contains("预算"), Types[i] + " over budget warns");
                string accepted = Path.Combine(root, "over-budget-" + Types[i] + ".bin");
                Assert(Packager.Build(new[] { item }, accepted).Warnings.Count == 1 &&
                       Packager.VerifyFile(accepted).Warnings.Count == 1, Types[i] + " over budget builds and validates");
            }
            else
            {
                Fails(delegate { Packager.Plan(new[] { item }); }, "上限");
                string rejected = Path.Combine(root, "rejected-" + Types[i] + ".bin");
                Fails(delegate { Packager.Build(new[] { item }, rejected); }, "上限");
                Assert(!File.Exists(rejected), Types[i] + " oversized build leaves no output");
            }
        }
        string packageLimit = Path.Combine(root, "PFC-package-limit.bin");
        FirmwareInput maxPackage = Input(0); maxPackage.Path = packageLimit;
        File.WriteAllBytes(packageLimit, new byte[Packager.MaxPackageSize - Packager.HeaderSize]);
        string maxOutput = Path.Combine(root, "exact-6MiB.bin");
        Assert(Packager.Build(new[] { maxPackage }, maxOutput).TotalSize == Packager.MaxPackageSize,
               "package exactly 6 MiB accepted");
        File.WriteAllBytes(packageLimit, new byte[Packager.MaxPackageSize - Packager.HeaderSize + 1]);
        Fails(delegate { Packager.Plan(new[] { maxPackage }); }, "6 MiB");
        Fails(delegate { Packager.VerifyBytes(new byte[Packager.MaxPackageSize + 1]); }, "6 MiB");
        Console.WriteLine("PASS four soft budgets, ESP32 hard limit and exact 6 MiB package boundary");
        Fails(delegate { Packager.Build(Inputs(1), Path.Combine(root, "missing", "no.bin")); }, "输出目录不存在");
        Fails(delegate { Packager.Build(Inputs(1), root); }, "目录");
        byte[] before = File.ReadAllBytes(SourcePath(0));
        Fails(delegate { Packager.Build(Inputs(1), SourcePath(0)); }, "同一个文件");
        Assert(Equal(before, File.ReadAllBytes(SourcePath(0))), "input remains unchanged");
        string hardLink = Path.Combine(root, "same-input-hard-link.bin");
        Assert(CreateHardLink(hardLink, SourcePath(0), IntPtr.Zero), "hard link created on Windows test volume");
        Fails(delegate { Packager.Build(Inputs(1), hardLink); }, "同一个文件");
        Assert(Equal(before, File.ReadAllBytes(SourcePath(0))), "hard link output does not modify input");
        string existing = Path.Combine(root, "existing-output.bin"); File.WriteAllBytes(existing, new byte[] { 1, 2, 3 });
        Fails(delegate { Packager.Build(new[] { noVersion }, existing); }, "版本");
        Assert(Equal(File.ReadAllBytes(existing), new byte[] { 1, 2, 3 }), "failed build preserves old output");
        using (FileStream locked = new FileStream(existing, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Fails(delegate { Packager.Build(Inputs(1), existing); }, "无法核对已有输出");
        Assert(Equal(File.ReadAllBytes(existing), new byte[] { 1, 2, 3 }), "locked output is preserved after write failure");
        Packager.Build(Inputs(1), existing);
        Assert(Packager.VerifyFile(existing).Entries.Count == 1, "valid build replaces existing output");
        Fails(delegate { Packager.VerifyFile(Path.Combine(root, "不存在.bin")); }, "不可读取");
        Console.WriteLine("PASS invalid input, budget and size limits, safe output and UTF-8 limits");
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new Exception("Pass a fresh writable test directory.");
            root = Path.GetFullPath(args[0]);
            if (Directory.Exists(root)) throw new Exception("Test directory already exists; refusing to overwrite.");
            Directory.CreateDirectory(root);
            CombinationTests();
            BudgetTest();
            CorruptionTests();
            ErrorTests();
            Console.WriteLine("PASS assertions=" + assertions);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL " + e); return 1; }
    }
}

