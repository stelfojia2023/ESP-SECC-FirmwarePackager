using System;

namespace SeccFirmwarePackager
{
    // Filename recognition only. Package validation and batch-selection policy belong to callers.
    internal static class FirmwareFileName
    {
        internal static string VersionFromName(string stem)
        {
            string version = String.Empty;
            // First underscore-prefixed V/v token; preserve its original spelling.
            for (int i = 0; i + 1 < stem.Length; i++)
            {
                if (stem[i] != '_' || (stem[i + 1] != 'V' && stem[i + 1] != 'v')) continue;
                int end = i + 2;
                while (end < stem.Length && stem[end] != '_' && stem[end] != '.') end++;
                version = stem.Substring(i + 1, end - i - 1);
                break;
            }
            return version;
        }

        internal static bool TryParse(string path, out FirmwareType type, out string name, out string version)
        {
            type = (FirmwareType)0;
            name = String.Empty;
            version = String.Empty;
            if (String.IsNullOrEmpty(path)) return false;
            // Recognize both separators without depending on the host operating system.
            int separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            string file = path.Substring(separator + 1);
            if (!file.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) return false;
            string stem = file.Substring(0, file.Length - 4);
            foreach (FirmwareType candidate in new[] { FirmwareType.PFC, FirmwareType.LLC, FirmwareType.M4, FirmwareType.PLC, FirmwareType.ESP32 })
            {
                if (!stem.StartsWith(candidate.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                type = candidate;
                name = stem;
                version = VersionFromName(stem);
                return true;
            }
            return false;
        }
    }
}
