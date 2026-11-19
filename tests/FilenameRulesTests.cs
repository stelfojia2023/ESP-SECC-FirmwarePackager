using System;
using SeccFirmwarePackager;

internal static class FilenameRulesTests
{
    private static int assertions;
    private static void Assert(bool value, string message)
    { assertions++; if (!value) throw new Exception(message); }
    private static void Parsed(string path, FirmwareType expectedType, string expectedName, string expectedVersion)
    {
        FirmwareType type; string name, version;
        Assert(FirmwareFileName.TryParse(path, out type, out name, out version), "parse " + path);
        Assert(type == expectedType, "type " + path);
        Assert(name == expectedName, "name " + path);
        Assert(version == expectedVersion, "version " + path);
    }
    private static void Rejected(string path)
    {
        FirmwareType type; string name, version;
        Assert(!FirmwareFileName.TryParse(path, out type, out name, out version), "reject " + path);
        Assert(type == (FirmwareType)0 && name == "" && version == "", "failed parse resets outputs");
    }
    public static int Main()
    {
        try
        {
            Parsed("PLC_maya-2.6.7.10.simage", FirmwareType.PLC, "PLC_maya-2.6.7.10", "maya-2.6.7.10");
            Parsed("PLC_maya-2.6.7.10_debug.hex", FirmwareType.PLC, "PLC_maya-2.6.7.10_debug", "maya-2.6.7.10");
            Parsed("pLc_MaYa-2.6.7.10.BIN", FirmwareType.PLC, "pLc_MaYa-2.6.7.10", "MaYa-2.6.7.10");
            Parsed("PLC_board_maya-2.6.7.10.custom", FirmwareType.PLC, "PLC_board_maya-2.6.7.10", "maya-2.6.7.10");
            Parsed("PLC_maya-2.6.7.10", FirmwareType.PLC, "PLC_maya-2.6.7.10", "maya-2.6.7.10");
            Parsed("PLC_maya-2.6.7.10_debug", FirmwareType.PLC, "PLC_maya-2.6.7.10_debug", "maya-2.6.7.10");
            Parsed("PLC_maya-2.6.7.10.123", FirmwareType.PLC, "PLC_maya-2.6.7.10.123", "maya-2.6.7.10.123");
            Parsed("PLC_maya-2.6.7.10_debug.123", FirmwareType.PLC, "PLC_maya-2.6.7.10_debug", "maya-2.6.7.10");
            foreach (string other in new[] { "PFC", "LLC", "M4", "ESP32" }) Rejected(other + "_maya-2.6.7.10.simage");
            Parsed("PFC_release_V000B01D15.bin", FirmwareType.PFC, "PFC_release_V000B01D15", "V000B01D15");
            Parsed(@"C:\firmware\llc_hw_v12_debug.BIN", FirmwareType.LLC, "llc_hw_v12_debug", "v12");
            Parsed("/tmp/build/M4_V1.2.3.bin", FirmwareType.M4, "M4_V1.2.3", "V1");
            Parsed("PLC_release_vABC_test.extra.bIn", FirmwareType.PLC, "PLC_release_vABC_test.extra", "vABC");
            Parsed("esp32__board__V2026_extra.bin", FirmwareType.ESP32, "esp32__board__V2026_extra", "V2026");
            Parsed("PFC.bin", FirmwareType.PFC, "PFC", "");
            Parsed("M4_board.bin", FirmwareType.M4, "M4_board", "");
            Parsed("LLC_V.bin", FirmwareType.LLC, "LLC_V", "V");
            Parsed("PFC_V1_V2.bin", FirmwareType.PFC, "PFC_V1_V2", "V1");
            Parsed("PFCboard_v5.bin", FirmwareType.PFC, "PFCboard_v5", "v5");
            Parsed("M4v9.bin", FirmwareType.M4, "M4v9", "");
            Parsed("PLC_BOARD.V8.bin", FirmwareType.PLC, "PLC_BOARD.V8", "");
            Parsed("ESP32_中文_v测试.bin", FirmwareType.ESP32, "ESP32_中文_v测试", "v测试");
            Parsed("PFC_" + new string('x', 100) + "_V1.bin", FirmwareType.PFC, "PFC_" + new string('x', 100) + "_V1", "V1");
            foreach (string path in new[] { null, "", ".bin", "other_V1.bin", "xPFC_V1.bin", "PFC_V1.hex", "PFC_V1.bin.bak", "PFC_V1.bin/", " PFC_V1.bin" }) Rejected(path);
            Console.WriteLine("PASS filename parsing assertions=" + assertions);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
