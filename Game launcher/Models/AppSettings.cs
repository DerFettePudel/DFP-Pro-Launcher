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
    public class AppSettings
    {
        // Profil & Farben
        public string UserName { get; set; } = "Gamer";
        public string BackgroundColor { get; set; } = "#0F111A";
        public string AccentColor { get; set; } = "#8B5CF6";

        // Darstellung
        public int CardWidth { get; set; } = 220;
        public int CardCorner { get; set; } = 14;
        public int UiScale { get; set; } = 100;
        public int HoverZoom { get; set; } = 105;
        public bool HoverAnimations { get; set; } = true;
        public bool ShowCardNames { get; set; } = true;
        public bool ShowCardSource { get; set; } = true;
        public int SortMode { get; set; }

        // Dashboard & Widgets
        public bool ShowClock { get; set; } = true;
        public bool Clock24h { get; set; } = true;
        public bool ClockSeconds { get; set; }
        public bool ShowWeather { get; set; } = true;
        public bool Fahrenheit { get; set; }
        public string WeatherCity { get; set; } = string.Empty;
        public bool ShowStats { get; set; } = true;
        public bool ShowRecent { get; set; } = true;
        public int RecentCount { get; set; } = 6;
        public bool ShowFavorites { get; set; } = true;

        // Spiele & Cover
        public bool FullResCovers { get; set; } = true;
        public string StartPage { get; set; } = "dashboard";

        // Systemüberwachung
        public int MonitorIntervalMs { get; set; } = 1000;
        public bool ShowSparklines { get; set; } = true;

        // Verhalten & Fenster
        public bool MinimizeOnLaunch { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool RememberWindow { get; set; } = true;
        public bool OnlineFeatures { get; set; } = true;
        public double WinLeft { get; set; }
        public double WinTop { get; set; }
        public double WinWidth { get; set; }
        public double WinHeight { get; set; }
        public bool WinMaximized { get; set; }

        // Daten
        public List<ManualGame> ManualGames { get; set; } = new();
        public List<ManualApp> ManualApps { get; set; } = new();
        public Dictionary<string, GameState> GameStates { get; set; } = new();
        public Dictionary<string, int> SteamIds { get; set; } = new();

        // Animationen
        public bool PageAnimations { get; set; } = true;
        public bool CardAnimations { get; set; } = true;
        public bool SmoothMetrics { get; set; } = true;
        public bool HoverGlow { get; set; } = true;
        public bool AnimatedBackground { get; set; }

        // Design
        public string BackgroundImagePath { get; set; } = string.Empty;
        public int BgDim { get; set; } = 55;
        public int BgBlur { get; set; } = 8;
        public bool GlassEffect { get; set; }
        public string CardLayout { get; set; } = "cards";   // cards | detail | list
        public bool SidebarCollapsed { get; set; }
        public List<string> HiddenSections { get; set; } = new();
        public bool ShowHero { get; set; } = true;

        // Funktionen
        public bool TrackPlaytime { get; set; } = true;
        public bool GamingProfile { get; set; }
        public bool ShowOverlay { get; set; } = true;
        public int OverlayCorner { get; set; } = 1;          // 0 oben links, 1 oben rechts, 2 unten links, 3 unten rechts
        public bool ControllerSupport { get; set; } = true;
        public bool MinimizeToTray { get; set; }
        public bool StartMinimized { get; set; }
        public string PreviousPowerPlan { get; set; } = string.Empty;
        public List<string> CollectionNames { get; set; } = new();
        public Dictionary<string, double> PlayLog { get; set; } = new();

        // Weitere Funktionen
        public bool ShowDeals { get; set; } = true;
        public List<string> SeenFreeGames { get; set; } = new();
        public bool StorageWarning { get; set; } = true;
        public int WeeklyGoalHours { get; set; }
        public int BreakReminderMinutes { get; set; }
        public bool AutoBackupSaves { get; set; } = true;
        public int BackupCount { get; set; }
        public List<string> DefaultCompanions { get; set; } = new();
        public Dictionary<string, string> Achievements { get; set; } = new();
        public bool NightOwl { get; set; }
        public bool InitialScanDone { get; set; }
        public List<ReleaseEntry> Releases { get; set; } = new();
        public List<string> ScreenshotFolders { get; set; } = new();

        // Sprache und Komfort
        public string Language { get; set; } = "auto";
        public bool CloseAppsEnabled { get; set; }
        public List<string> CloseApps { get; set; } = new();
        public bool AutoRescan { get; set; } = true;
        public bool GlobalHotkey { get; set; } = true;
        public bool MotionChecked { get; set; }

        // Anpassung, Leistung und Controller
        public List<string> NavOrder { get; set; } = new();
        public int CardSpacing { get; set; } = 12;
        public int CardAspect { get; set; } = 136;
        public int SidebarWidth { get; set; } = 250;
        public bool OledMode { get; set; }
        public bool OledPixelShift { get; set; } = true;
        public bool PerformanceMode { get; set; }
        public bool SoftwareRendering { get; set; }
        public bool PauseWhenMinimized { get; set; } = true;
        public bool AlwaysPlayGifs { get; set; }
        public string ControllerLayout { get; set; } = "auto";

        // Cover-Quelle, Spielzeit-Ausnahmen, Logo, Navigation, Schnellzugriff, Infobereich
        public string SteamGridDbKey { get; set; } = string.Empty;
        public List<string> TrackingIgnored { get; set; } = new();
        public int BrandLogoSize { get; set; } = 54;
        public int BrandNameSize { get; set; } = 20;
        public bool BrandShowName { get; set; } = true;
        public bool BrandShowSubtitle { get; set; } = true;
        public string BrandDesign { get; set; } = "original";
        public string BrandNameColor { get; set; } = "white";
        public string BrandName { get; set; } = "DFP PRO";
        public string BrandSubtitle { get; set; } = "LAUNCHER";
        public int NavFontSize { get; set; } = 14;
        public int NavItemPadding { get; set; } = 8;
        public List<QuickLink> QuickLinks { get; set; } = new();
        public bool QuickLinksSeeded { get; set; }
        public int QuickTileSize { get; set; } = 190;
        public bool ShowTrayIcon { get; set; } = true;
        public bool ShowToasts { get; set; } = true;
        public bool DoubleClickLaunch { get; set; }
        public bool ExitWarning { get; set; } = true;
        public bool SgdbAnimated { get; set; } = true;
        public bool SgdbAnimatedMigrated { get; set; }
        public List<string> AnimatedChecked { get; set; } = new();

        // Wunschliste, Spielinfos, Sensoren, Klänge, Updates, Einrichtung, Sicherung
        public List<WishItem> Wishlist { get; set; } = new();
        public Dictionary<string, GameInfoEntry> GameInfo { get; set; } = new();
        public DateTime LastWishCheck { get; set; }
        public bool SensorsEnabled { get; set; }
        public bool UiSounds { get; set; }
        public bool FetchGameInfo { get; set; } = true;
        public bool CheckUpdates { get; set; } = true;
        public string UpdateRepo { get; set; } = string.Empty;
        public DateTime LastUpdateCheck { get; set; }
        public string SkippedUpdate { get; set; } = string.Empty;
        public List<string> AppFavorites { get; set; } = new();
        public bool EasterEggs { get; set; } = true;
        public List<string> StreamerHiddenApps { get; set; } = new();
        public bool DownloadWidget { get; set; } = true;
        public bool StreamerLinksSeeded { get; set; }
        public List<QuickLink> StreamerLinks { get; set; } = new();
        public string StreamCensorStyle { get; set; } = "bar";
        public bool StreamHidePc { get; set; } = true;
        public string LibraryView { get; set; } = "cards";
        public bool PadCombo { get; set; } = true;
        public string PadComboMode { get; set; } = "backstart";
        public bool PadComboInGame { get; set; }
        public bool PadVibrate { get; set; }
        public int PadRepeat { get; set; } = 2;
        public bool PadAutoStart { get; set; }
        public List<string> PadSeenLayouts { get; set; } = new();
        public bool StreamerPageSeeded { get; set; }
        public bool StreamHideMusic { get; set; } = true;
        public bool QuickRealIcons { get; set; } = true;
        public List<QuickButton> StreamerApps { get; set; } = new();
        public string SidebarColor { get; set; } = string.Empty;
        public string NavTextColor { get; set; } = string.Empty;
        public string NavActiveColor { get; set; } = string.Empty;
        public bool RgbUnlocked { get; set; }
        public bool RgbAccent { get; set; }
        public int RgbSpeed { get; set; } = 2;
        public string FirstRunDate { get; set; } = string.Empty;
        public List<string> EggSeen { get; set; } = new();
        public bool MilestonesInit { get; set; }
        public List<int> MilestonesReached { get; set; } = new();
        public bool MusicPlayer { get; set; } = true;
        public string MusicService { get; set; } = "spotify";
        public List<string> MusicServicesOn { get; set; } = new();
        public bool QuickButtonsEnabled { get; set; }
        public List<QuickButton> QuickButtons { get; set; } = new();
        public bool WarnBattery { get; set; } = true;
        public string DeviceProfile { get; set; } = "auto";
        public bool DeviceAutoApplied { get; set; }
        public bool PowerButtons { get; set; }
        public bool PowerConfirm { get; set; } = true;
        public bool AutomationsEnabled { get; set; } = true;
        public List<AutomationRoutine> Routines { get; set; } = new();
        public bool SetupDone { get; set; }
        public string CloudBackupFolder { get; set; } = string.Empty;

        // Streamer-Modus und Komfort
        public bool StreamerMode { get; set; }
        public bool StreamerAuto { get; set; }
        public bool StreamHideName { get; set; } = true;
        public bool StreamHideLocation { get; set; } = true;
        public bool StreamHideStats { get; set; } = true;
        public bool StreamHideRecent { get; set; } = true;
        public bool StreamHidePaths { get; set; } = true;
        public bool StreamHideShots { get; set; } = true;
        public bool StreamMuteAlerts { get; set; } = true;
        public bool ReopenAfterGame { get; set; }
        public bool AskOnClose { get; set; }
        public bool TrayClickOpensLauncher { get; set; }
        public bool TypeToSearch { get; set; }
        public bool FavoritesFirst { get; set; }
        public bool ShowAlphaBar { get; set; } = true;
        public bool SessionReport { get; set; } = true;
        public bool MuteWhilePlaying { get; set; }
        public int DailyLimitHours { get; set; }
        public int OverlayOpacity { get; set; } = 90;
        public int OverlayScale { get; set; } = 100;
        public bool OverlayShowCpu { get; set; } = true;
        public bool OverlayShowGpu { get; set; } = true;
        public bool OverlayShowRam { get; set; } = true;
        public bool OverlayShowClock { get; set; }

        // Mod-Ordner, Discord, Widgets, Willkommen
        public bool ShowModButton { get; set; } = true;
        public bool WelcomeAnimation { get; set; } = true;
        public bool DiscordEnabled { get; set; }
        public string DiscordWebhook { get; set; } = string.Empty;
        public bool DiscordOnStart { get; set; } = true;
        public bool DiscordOnEnd { get; set; } = true;
        public bool DiscordSendName { get; set; } = true;
        public List<string> DashboardOrder { get; set; } = new();
        public List<string> DashboardHidden { get; set; } = new() { "goal", "backlog", "wishlist", "quick" };

        // OBS-Steuerung (OBS WebSocket v5)
        public bool ObsEnabled { get; set; }
        public string ObsHost { get; set; } = "localhost";
        public int ObsPort { get; set; } = 4455;
        public string ObsPasswordProtected { get; set; } = string.Empty;   // mit Windows DPAPI verschlüsselt, nie im Klartext
        public bool ObsAutoStreamer { get; set; } = true;
        public List<string> ObsStreamerScenes { get; set; } = new();

        // Einstellungs-Profile
        public List<SettingsProfile> Profiles { get; set; } = new();
        public bool ProfilesSeeded { get; set; }
        public string ActiveProfileId { get; set; } = string.Empty;
        public string ProfileOnGame { get; set; } = string.Empty;   // Profil-Id oder leer = aus
        public string ProfileOnObs { get; set; } = string.Empty;

        // Streamer-Extras
        public bool CaptureExclude { get; set; }
        public int GoLiveMinutes { get; set; } = 5;
        public string GoLiveRoutine { get; set; } = string.Empty;   // Name des Ablaufs, leer = keiner
        public bool GoLiveStartObs { get; set; }

        // Controller-Extras
        public Dictionary<string, Dictionary<string, string>> PadMappings { get; set; } = new();   // Typ (xbox/ps/nintendo) → Aktion → Taste
        public bool ConsoleStart { get; set; }

        // Bibliothek: smarte Listen, Spielgrößen, Jahresrückblick, Download-Meldung
        public List<SmartList> SmartLists { get; set; } = new();
        public Dictionary<string, GameSizeEntry> GameSizes { get; set; } = new();   // Schlüssel: Installationsordner
        public Dictionary<string, YearStat> YearStats { get; set; } = new();        // Schlüssel: Jahr, zum Beispiel "2026"
        public DateTime? YearStatsSince { get; set; }
        public bool DownloadDoneNotify { get; set; } = true;

        // Fehlerbericht und Beta-Kanal
        public bool ErrorReports { get; set; } = true;
        public bool BetaUpdates { get; set; }
    }

    /// <summary>Ein Profil speichert eine Auswahl von Einstellungen (nach Bereichen) und optional einen Energieplan.</summary>
    public class SettingsProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = "⭐";
        public List<string> Groups { get; set; } = new();
        public Dictionary<string, JsonElement> Values { get; set; } = new();
        public string PowerPlan { get; set; } = string.Empty;   // GUID des Energieplans, leer = nicht ändern
    }

    /// <summary>Datei beim Export der Einstellungen: Versionsangaben plus die eigentlichen Einstellungen.</summary>
    public class SettingsExportFile
    {
        public string Format { get; set; } = string.Empty;
        public int FormatVersion { get; set; }
        public string AppVersion { get; set; } = string.Empty;
        public DateTime Exported { get; set; }
        public AppSettings? Settings { get; set; }
    }

    /// <summary>Gespeicherter Filter aus kombinierbaren Regeln (alle aktiven Regeln müssen passen).</summary>
    public class SmartList
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public bool NeverPlayed { get; set; }
        public int MaxSizeGb { get; set; }          // 0 = egal
        public int NotPlayedMonths { get; set; }    // 0 = egal
        public string Source { get; set; } = string.Empty;   // leer = egal
    }

    public class GameSizeEntry
    {
        public long Bytes { get; set; }
        public DateTime Measured { get; set; }
    }

    public class YearStat
    {
        public Dictionary<string, double> Games { get; set; } = new();     // Spielname → Sekunden in diesem Jahr
        public Dictionary<string, string> Sources { get; set; } = new();   // Spielname → Quelle
        public double LongestSeconds { get; set; }
        public string LongestGame { get; set; } = string.Empty;
        public DateTime? LongestDate { get; set; }
    }
}
