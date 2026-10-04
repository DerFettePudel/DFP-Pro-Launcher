using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Game_launcher
{
    public class GameScannerServices
    {
        public bool IsBlacklisted(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            string lower = text.ToLower();
            return lower.Contains("unins") || lower.Contains("setup") || lower.Contains("crash") || lower.Contains("redist");
        }

        public string GetExecutablePath(string installLocation, string displayIcon)
        {
            if (!string.IsNullOrEmpty(displayIcon))
            {
                string cleanPath = displayIcon.Split(',')[0].Trim('"');
                if (File.Exists(cleanPath) && cleanPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return cleanPath;
                }
            }

            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
            {
                var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.AllDirectories)
                    .Where(f => !IsBlacklisted(f))
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .ToList();

                return exeFiles.FirstOrDefault() ?? string.Empty;
            }

            return string.Empty;
        }
    }
}