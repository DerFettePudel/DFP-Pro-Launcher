using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Interop = System.Runtime.InteropServices;

namespace Game_launcher
{
    public class QuickButton
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
    }

    public class AutomationStep
    {
        public string Type { get; set; } = "wait";
        public string Target { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public int Number { get; set; }
        public string Mode { get; set; } = string.Empty;
    }

    public class AutomationRoutine
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Trigger { get; set; } = "manual";
        public string GameFilter { get; set; } = string.Empty;
        public List<AutomationStep> Steps { get; set; } = new();
    }

    public class GameItem
    {
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string InstallDir { get; set; } = string.Empty;
        public string? LaunchUri { get; set; }
        public string Source { get; set; } = "Manuell";
        public string AppId { get; set; } = string.Empty;            // Steam-AppID
        public string PackageFullName { get; set; } = string.Empty;  // Xbox-Paketname (zum Deinstallieren)
        public List<string> CoverUrls { get; set; } = new();         // mögliche Cover-Quellen, beste zuerst
        public string CoverPath { get; set; } = string.Empty;        // lokale Cover-Datei
        public string FallbackCoverPath { get; set; } = string.Empty;
        public ImageSource? LocalIcon { get; set; }
        public bool IsFavorite { get; set; }
        public DateTime? LastPlayed { get; set; }
        public int LaunchCount { get; set; }
        public long PlaySeconds { get; set; }
        public int Rating { get; set; }
        public string Notes { get; set; } = string.Empty;
        public List<string> Collections { get; set; } = new();
        public DateTime? FirstSeen { get; set; }
        public string Status { get; set; } = string.Empty;          // backlog | playing | done
        public string SavePath { get; set; } = string.Empty;
        public string LaunchArgs { get; set; } = string.Empty;
        public bool RunAsAdmin { get; set; }
        public List<string> CompanionApps { get; set; } = new();
        public bool Hidden { get; set; }
        public string CustomCover { get; set; } = string.Empty;
        public double Price { get; set; }
        public string ProfileMode { get; set; } = "global";
        public string CloseMode { get; set; } = "global";
        public string ModPath { get; set; } = string.Empty;
    }

    public class GameState
    {
        public bool IsFavorite { get; set; }
        public DateTime? LastPlayed { get; set; }
        public int LaunchCount { get; set; }
        public long PlaySeconds { get; set; }
        public int Rating { get; set; }
        public string Notes { get; set; } = string.Empty;
        public List<string> Collections { get; set; } = new();
        public DateTime? FirstSeen { get; set; }
        public string Status { get; set; } = string.Empty;          // backlog | playing | done
        public string SavePath { get; set; } = string.Empty;
        public string LaunchArgs { get; set; } = string.Empty;
        public bool RunAsAdmin { get; set; }
        public List<string> CompanionApps { get; set; } = new();
        public bool Hidden { get; set; }
        public string CustomCover { get; set; } = string.Empty;
        public double Price { get; set; }
        public string ProfileMode { get; set; } = "global";
        public string CloseMode { get; set; } = "global";
        public string ModPath { get; set; } = string.Empty;
    }

    public class ManualGame
    {
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
    }

    public class ManualApp
    {
        public string Name { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
    }

    public class AppEntry
    {
        public string Name { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool IsManual { get; set; }
        public ImageSource? Icon { get; set; }
        public string AppId { get; set; } = string.Empty;
        public string Category { get; set; } = "Sonstige";
        public bool IsFavorite { get; set; }
    }

    public class InfoRow
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class HardwareGroup
    {
        public string Title { get; set; } = string.Empty;
        public List<InfoRow> Rows { get; set; } = new();
    }

    public class GpuAdapter
    {
        public string Name { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public string Driver { get; set; } = string.Empty;
        public long VramBytes { get; set; }
    }

    public class FileEntry
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string SizeText { get; set; } = string.Empty;
        public string DateText { get; set; } = string.Empty;
    }

    public class DriveInfoModel
    {
        public string DriveName { get; set; } = string.Empty;
        public string InfoText { get; set; } = string.Empty;
        public string PercentText { get; set; } = string.Empty;
        public double UsedPercent { get; set; }
        public System.Windows.Media.Brush BarBrush { get; set; } = System.Windows.Media.Brushes.MediumPurple;
    }

    public class GameSizeModel
    {
        public string Name { get; set; } = string.Empty;
        public string SizeText { get; set; } = string.Empty;
        public double Percent { get; set; }
    }

    public class StatRow
    {
        public string Rank { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TimeText { get; set; } = string.Empty;
        public double Percent { get; set; }
    }

    public class MetricsSample
    {
        public double Cpu { get; set; }
        public double Ram { get; set; }
        public double Gpu { get; set; }
        public double Mhz { get; set; }
        public double UsedMb { get; set; }
        public bool HasGpu { get; set; }
    }

    public class SpotlightEntry
    {
        public string Icon { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public int Score { get; set; }
        public Action Action { get; set; } = () => { };
    }

    public class ReleaseEntry
    {
        public string Name { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }

    public class DealItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Badge { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Store { get; set; } = string.Empty;   // epic | steam
    }

    public class PingResult
    {
        public string Name { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public double Percent { get; set; }
        public System.Windows.Media.Brush BarBrush { get; set; } = System.Windows.Media.Brushes.MediumPurple;
    }

    public class AchievementRow
    {
        public string Icon { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ProgressText { get; set; } = string.Empty;
        public double Percent { get; set; }
        public double Opacity { get; set; } = 1.0;
        public bool Unlocked { get; set; }
    }

    public class AchievementDef
    {
        public string Id { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Current { get; set; }
        public double Target { get; set; }
        public bool Secret { get; set; }
    }

    public class ShotInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    public class WishItem
    {
        public string Name { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public double TargetPrice { get; set; }
        public double FinalPrice { get; set; }
        public double InitialPrice { get; set; }
        public int Discount { get; set; }
        public bool Free { get; set; }
        public string PriceText { get; set; } = string.Empty;
        public DateTime Checked { get; set; }
        public double NotifiedPrice { get; set; }
    }

    public class GameInfoEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Genres { get; set; } = new();
        public string Release { get; set; } = string.Empty;
        public string Developer { get; set; } = string.Empty;
        public string Lang { get; set; } = string.Empty;
        public DateTime Updated { get; set; }
    }

    public class ThemePack
    {
        public string BackgroundColor { get; set; } = "#0F111A";
        public string AccentColor { get; set; } = "#8B5CF6";
        public int CardWidth { get; set; } = 220;
        public int CardCorner { get; set; } = 14;
        public int CardSpacing { get; set; } = 12;
        public int CardAspect { get; set; } = 136;
        public int UiScale { get; set; } = 100;
        public int HoverZoom { get; set; } = 105;
        public bool GlassEffect { get; set; }
        public bool OledMode { get; set; }
        public int SidebarWidth { get; set; } = 250;
        public int NavFontSize { get; set; } = 14;
        public int NavItemPadding { get; set; } = 8;
        public string BrandDesign { get; set; } = "original";
        public string BrandNameColor { get; set; } = "white";
        public int BrandLogoSize { get; set; } = 54;
        public int BrandNameSize { get; set; } = 20;
        public string BrandName { get; set; } = "DFP PRO";
        public string BrandSubtitle { get; set; } = "LAUNCHER";
    }

    public class QuickLink
    {
        public string Name { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;   // Emoji, Bilddatei oder leer (= Symbol der Datei)
    }
}
