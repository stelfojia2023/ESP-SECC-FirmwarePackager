using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SeccFirmwarePackager;

// Byte-level contract derived from the supplied ESP-SECC format, not the writer's constants.
internal static class NewFormatContractTests
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Bytes(byte[] actual, int offset, byte[] expected, string label)
    {
        for (int i = 0; i < expected.Length; i++)
            Require(actual[offset + i] == expected[i], label + " at 0x" + (offset + i).ToString("X"));
    }

    private static void Zero(byte[] actual, int start, int end, string label)
    {
        for (int i = start; i < end; i++) Require(actual[i] == 0, label + " at 0x" + i.ToString("X"));
    }

    private static byte[] Hex(string value)
    {
        byte[] bytes = new byte[value.Length / 2];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(value.Substring(i * 2, 2), 16);
        return bytes;
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new Exception("Pass a fresh writable directory and legacy package");
            string root = Path.GetFullPath(args[0]);
            if (Directory.Exists(root)) throw new Exception("Test directory already exists");
            Directory.CreateDirectory(root);
            string pfc = Path.Combine(root, "pfc.bin");
            string esp = Path.Combine(root, "esp.bin");
            File.WriteAllBytes(pfc, new byte[] { 0x11, 0x22, 0x33 });
            File.WriteAllBytes(esp, new byte[] { 0x91, 0xA2 });
            string output = Path.Combine(root, "combined.bin");
            FirmwareInput[] inputs = {
                new FirmwareInput { Type = FirmwareType.ESP32, Path = esp, Name = "设备", Version = "V2.3" },
                new FirmwareInput { Type = FirmwareType.PFC, Path = pfc, Name = "PFC", Version = "V1" }
            };
            PackageReport report = Packager.Build(inputs, output);
            byte[] data = File.ReadAllBytes(output);
            Require(data.Length == 0x3000, "total length with padding");
            Bytes(data, 0, Encoding.ASCII.GetBytes("SECCPKG1"), "magic");
            Bytes(data, 8, Hex("01000010003000000200000128000000"), "public fields");
            int a = 0x28, b = 0x128;
            Bytes(data, a, Hex("010003000010000003000000"), "first entry fields");
            Bytes(data, a + 0x0C, Hex("bafae3a174ab91fc70db7a6aa50f4f52"), "PFC MD5");
            Bytes(data, a + 0x1C, Hex("0200"), "PFC version length");
            Bytes(data, a + 0x1E, Encoding.UTF8.GetBytes("PFC"), "PFC name");
            Zero(data, a + 0x1E + 3, a + 0x46, "PFC name NUL and unused bytes");
            Bytes(data, a + 0x46, Encoding.UTF8.GetBytes("V1"), "PFC version");
            Zero(data, a + 0x46 + 2, a + 0x6E, "PFC version NUL and unused bytes");
            Zero(data, a + 0x6E, a + 0x100, "PFC extension");
            Bytes(data, b, Hex("050006000020000002000000"), "second entry fields");
            Bytes(data, b + 0x0C, Hex("4d4abf1526d2c9b0f800fe40054d5ebe"), "ESP32 MD5");
            Bytes(data, b + 0x1C, Hex("0400"), "ESP32 version length");
            Bytes(data, b + 0x1E, Encoding.UTF8.GetBytes("设备"), "ESP32 UTF-8 name");
            Zero(data, b + 0x1E + 6, b + 0x46, "ESP32 name NUL and unused bytes");
            Bytes(data, b + 0x46, Encoding.UTF8.GetBytes("V2.3"), "ESP32 version");
            Zero(data, b + 0x46 + 4, b + 0x100, "ESP32 version and extension padding");
            Zero(data, 0x228, 0x1000, "unused header");
            Bytes(data, 0x1000, new byte[] { 0x11, 0x22, 0x33 }, "PFC bytes");
            Bytes(data, 0x2000, new byte[] { 0x91, 0xA2 }, "ESP32 bytes");
            for (int p = 0x1003; p < 0x2000; p++) Require(data[p] == 0xFF, "PFC padding");
            for (int p = 0x2002; p < 0x3000; p++) Require(data[p] == 0xFF, "ESP32 padding");
            byte[] zeroed = (byte[])data.Clone();
            Array.Clear(zeroed, 0x18, 16);
            byte[] digest;
            using (MD5 md5 = MD5.Create()) digest = md5.ComputeHash(zeroed);
            Bytes(data, 0x18, digest, "whole package MD5");
            Require(report.Entries.Count == 2 && Packager.VerifyFile(output).Entries.Count == 2, "public verification");
            Console.WriteLine("PASS independent two-entry byte layout, NUL slots, offsets, payload and hashes");

            File.WriteAllBytes(pfc, new byte[250 * 1024 + 1]);
            FirmwareInput oversized = new FirmwareInput { Type = FirmwareType.PFC, Path = pfc, Name = "PFC", Version = "V1" };
            PackageReport plan = Packager.Plan(new[] { oversized });
            Require(plan.Warnings.Count == 1 && plan.Warnings[0].Contains("预算"), "PFC above budget shows warning in preview");
            string budgetOutput = Path.Combine(root, "over-budget.bin");
            PackageReport built = Packager.Build(new[] { oversized }, budgetOutput);
            Require(built.Warnings.Count == 1 && Packager.VerifyFile(budgetOutput).Warnings.Count == 1,
                    "over-budget PFC can build and validate with warning");
            Console.WriteLine("PASS PFC budget warning does not reject an otherwise valid package");

            File.WriteAllBytes(pfc, new byte[] { 0x11 });
            FirmwareInput boundary = new FirmwareInput { Type = FirmwareType.PFC, Path = pfc,
                Name = new string('固', 13), Version = new string('v', 39) };
            string boundaryOutput = Path.Combine(root, "max-text.bin");
            Packager.Build(new[] { boundary }, boundaryOutput);
            byte[] maxText = File.ReadAllBytes(boundaryOutput);
            Bytes(maxText, 0x28 + 2, Hex("2700"), "39-byte name length");
            Bytes(maxText, 0x28 + 0x1C, Hex("2700"), "39-byte version length");
            Require(maxText[0x28 + 0x1E + 39] == 0 && maxText[0x28 + 0x46 + 39] == 0,
                    "39-byte metadata retains terminating NUL");
            PackageReport verified = Packager.VerifyFile(boundaryOutput);
            Require(verified.Entries[0].Name == boundary.Name && verified.Entries[0].Version == boundary.Version,
                    "39-byte metadata round trip");
            Console.WriteLine("PASS exact 39-byte UTF-8 metadata and NUL terminators");

            bool oldRejected = false;
            try { Packager.VerifyFile(args[1]); }
            catch (InvalidDataException error) { oldRejected = error.Message.Contains("目录"); }
            Require(oldRejected, "legacy 0x80-directory package must not pass new format verification");
            Console.WriteLine("PASS actual legacy 0x80-directory package is rejected");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + error); return 1; }
    }
}
