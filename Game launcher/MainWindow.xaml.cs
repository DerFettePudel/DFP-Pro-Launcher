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

    // ───────────────────────────── Hauptfenster ─────────────────────────────

    public partial class MainWindow : Window
    {
        private const string AutostartName = "DFP Pro Launcher";
        private const double GigaByte = 1024.0 * 1024.0 * 1024.0;
        private const int SparkPoints = 60;

        // PowerShell-Skripte (werden per -EncodedCommand ausgeführt, daher keine Quoting-Probleme)
        private const string HardwareScript = """
            $ErrorActionPreference = 'SilentlyContinue'
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

            $cpu   = @(Get-CimInstance Win32_Processor | Select-Object Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize)
            $ram   = @(Get-CimInstance Win32_PhysicalMemory | Select-Object Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, DeviceLocator, SMBIOSMemoryType)
            $gpu   = @(Get-CimInstance Win32_VideoController | Select-Object Name, CurrentRefreshRate, CurrentHorizontalResolution, CurrentVerticalResolution)
            $board = @(Get-CimInstance Win32_BaseBoard | Select-Object Manufacturer, Product)
            $bios  = @(Get-CimInstance Win32_BIOS | Select-Object Manufacturer, SMBIOSBIOSVersion, @{n='ReleaseDate';e={ if ($_.ReleaseDate) { $_.ReleaseDate.ToString('dd.MM.yyyy') } else { '' } }})
            $disks = @(Get-PhysicalDisk | Select-Object FriendlyName, Size, @{n='MediaType';e={[string]$_.MediaType}}, @{n='BusType';e={[string]$_.BusType}})
            $os    = @(Get-CimInstance Win32_OperatingSystem | Select-Object Caption, BuildNumber, OSArchitecture)
            $sound = @(Get-CimInstance Win32_SoundDevice | Select-Object Name)
            $net   = @(Get-CimInstance Win32_NetworkAdapter -Filter 'PhysicalAdapter=True AND NetEnabled=True' | Select-Object Name)
            $mon   = @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID | ForEach-Object {
                [PSCustomObject]@{
                    Name  = (-join ([char[]]($_.UserFriendlyName | Where-Object { $_ -ne 0 })))
                    Maker = (-join ([char[]]($_.ManufacturerName | Where-Object { $_ -ne 0 })))
                }
            })

            [PSCustomObject]@{ cpu = $cpu; ram = $ram; gpu = $gpu; board = $board; bios = $bios; disks = $disks; os = $os; sound = $sound; net = $net; monitors = $mon } | ConvertTo-Json -Depth 4 -Compress
            """;

        private const string XboxScript = """
            $ErrorActionPreference = 'SilentlyContinue'
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            $items = @(Get-AppxPackage | Where-Object { $_.InstallLocation -and (Test-Path (Join-Path $_.InstallLocation 'MicrosoftGame.config')) } | ForEach-Object {
                [PSCustomObject]@{ Name = $_.Name; Family = $_.PackageFamilyName; Full = $_.PackageFullName; Location = $_.InstallLocation }
            })
            ConvertTo-Json -InputObject $items -Compress
            """;

        private static readonly string[] SortLabels = { "Name (A–Z)", "Zuletzt gespielt", "Meist gestartet", "Zuletzt hinzugefügt", "Beste Bewertung", "Meiste Spielzeit" };
        private static CultureInfo GermanCulture => Loc.Culture;

        private static readonly string SettingsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Game_launcher");
        private static readonly string SettingsFilePath = System.IO.Path.Combine(SettingsDir, "settings.json");
        private static readonly string CoversDir = System.IO.Path.Combine(SettingsDir, "covers");
        private static readonly string LogDir = System.IO.Path.Combine(SettingsDir, "logs");
        private static readonly string SettingsBackupDir = System.IO.Path.Combine(SettingsDir, "settings_backups");
        private static readonly string PendingReportPath = System.IO.Path.Combine(LogDir, "fehlerbericht_offen.txt");
        private static readonly string LegacySettingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static readonly string DownloadsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly System.Net.Http.HttpClient Http = CreateHttpClient();

        private static readonly Dictionary<string, string> PowerPlanNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["a1841308-3541-4fab-bc81-f71556f20b4a"] = "Energiesparen",
            ["381b4222-f694-41f0-9685-ff5bb260df2e"] = "Ausbalanciert",
            ["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"] = "Höchstleistung",
            ["e9a42b02-d5df-448d-aa00-03f14749eb61"] = "Ultimative Leistung"
        };

        // PCI-Hersteller-IDs der Grafikkarten-Hersteller (Subsystem-Vendor)
        private static readonly Dictionary<string, string> BoardVendors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["1458"] = "Gigabyte",
            ["1043"] = "ASUS",
            ["1462"] = "MSI",
            ["3842"] = "EVGA",
            ["19DA"] = "Zotac",
            ["196E"] = "PNY",
            ["1569"] = "Palit",
            ["10B0"] = "Gainward",
            ["1682"] = "XFX",
            ["1DA2"] = "Sapphire",
            ["148C"] = "PowerColor",
            ["1849"] = "ASRock",
            ["1B4C"] = "KFA2 / Galax",
            ["7377"] = "Colorful",
            ["10DE"] = "NVIDIA",
            ["1002"] = "AMD",
            ["8086"] = "Intel",
            ["1028"] = "Dell",
            ["103C"] = "HP",
            ["17AA"] = "Lenovo"
        };

        // Farben (einmal erzeugt und eingefroren)
        private static SolidColorBrush BrushCardBg = MakeBrush("#131722");
        private static SolidColorBrush BrushCardBorder = MakeBrush("#1F2937");
        private static SolidColorBrush BrushFooter = MakeBrush("#181D2A");
        private static SolidColorBrush BrushTileHover = MakeBrush("#1B2132");
        private static readonly SolidColorBrush BrushPlaceholder = MakeBrush("#374151");
        private static readonly SolidColorBrush BrushSubtle = MakeBrush("#6B7280");
        private static readonly SolidColorBrush BrushOverlay = MakeBrush("#99000000");
        private static readonly SolidColorBrush BrushGold = MakeBrush("#FBBF24");
        private static readonly SolidColorBrush BrushAmber = MakeBrush("#F59E0B");
        private static readonly SolidColorBrush BrushRed = MakeBrush("#EF4444");
        private static readonly SolidColorBrush BrushCpu = MakeBrush("#60A5FA");
        private static readonly SolidColorBrush BrushRam = MakeBrush("#A78BFA");
        private static readonly SolidColorBrush BrushGpu = MakeBrush("#34D399");

        private List<GameItem> allGames = new();
        private AppSettings settings = new();
        private string currentFilter = "all";
        private bool isScanning;
        private bool isLoadingSettings = true;
        private bool hardwareBusy;
        private string cpuDisplayName = string.Empty;
        private string ramSummary = string.Empty;

        private (double Lat, double Lon, string Name)? weatherLocation;
        private string weatherLocationKey = string.Empty;

        private readonly Dictionary<string, BitmapImage> imageCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer applyTimer = new();
        private readonly DispatcherTimer clockTimer = new();
        private readonly DispatcherTimer weatherTimer = new();

        // Systemüberwachung
        private readonly DispatcherTimer systemTimer = new();
        private PerformanceCounter? cpuCounter;
        private PerformanceCounter? cpuFreqCounter;
        private PerformanceCounter? ramCounter;
        private readonly Dictionary<string, PerformanceCounter> gpuCounters = new();
        private readonly List<double> cpuHistory = new();
        private readonly List<double> ramHistory = new();
        private readonly List<double> gpuHistory = new();
        private int gpuRefreshCountdown;
        private double totalRamMB;
        private double baseCpuMhz;

        public MainWindow()
        {
            // Nur eine Instanz: ein zweiter Start holt das bereits laufende Fenster nach vorn
            if (!AcquireSingleInstance()) Environment.Exit(0);

            ApplyRenderModeEarly();
            InitializeComponent();
            LoadSettings();
            InitLanguage();
            RestoreWindowBounds();
            InitSystemMonitoring();
            ApplyViewSettings();

            BuildToolTiles();
            BuildLinkTiles();
            RefreshApps();
            ApplyTileNames(ReadGpuAdapters());

            // Änderungen an Reglern werden gesammelt angewendet
            applyTimer.Interval = TimeSpan.FromMilliseconds(350);
            applyTimer.Tick += (s, e) =>
            {
                applyTimer.Stop();
                SaveSettings();
                ApplyViewSettings();
                ApplyFilter();
                RefreshDashboard();
            };

            clockTimer.Interval = TimeSpan.FromSeconds(1);
            clockTimer.Tick += (s, e) => UpdateClock();
            clockTimer.Start();

            weatherTimer.Interval = TimeSpan.FromMinutes(20);
            weatherTimer.Tick += async (s, e) => await RefreshWeatherAsync();
            weatherTimer.Start();

            InitFeatures();
            InitExtras();
            InitComfort();
            InitExtras3();
            InitExtras4();
            InitExtras5();
            InitExtras6();
            InitExtras7();
            InitExtras8();
            InitExtras9();
            InitExtras10();
            InitExtras11();
            InitExtras12();
            InitExtras13();
            InitExtras14();
            InitExtras15();
            InitExtras16();
            InitExtras17();
            InitExtras18();
            InitExtras19();
            InitExtras20();
            InitExtras21();
            InitExtras22();
            InitExtras23();

            isLoadingSettings = false;
            RefreshDashboard();
            UpdateClock();
            ShowStartPage();

            Loaded += async (s, e) =>
            {
                _ = RefreshWeatherAsync();
                _ = RefreshHardwareAsync();
                await LoadInstalledGamesAsync();
            };
            Closing += (s, e) => SaveWindowBounds();
            Closed += (s, e) => DisposeMonitoring();
        }

        // ───────────────────────────── Hilfsmethoden ─────────────────────────────

        private static System.Net.Http.HttpClient CreateHttpClient()
        {
            var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DFP-Pro-Launcher/1.0");
            return client;
        }

        private static MessageBoxResult Msg(string text, string title = "DFP Pro Launcher",
            MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
            => ShowDarkMessage(Loc.T(text), Loc.T(title), buttons, icon);

        private static SolidColorBrush MakeBrush(string? hex, string fallback = "#000000")
        {
            SolidColorBrush brush;
            try
            {
                brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex ?? fallback));
            }
            catch
            {
                brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallback));
            }
            brush.Freeze();
            return brush;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return unit == 0 ? $"{size:F0} {units[unit]}" : $"{size:F1} {units[unit]}";
        }

        /// <summary>Datenträger werden vom Hersteller dezimal angegeben (1 TB = 1000 GB).</summary>
        private static string FormatDiskSize(long bytes)
        {
            double gb = bytes / 1e9;
            return gb >= 1000 ? $"{gb / 1000.0:F1} TB" : $"{gb:F0} GB";
        }

        private static string FormatRelative(DateTime time)
        {
            var span = DateTime.Now - time;
            if (span.TotalMinutes < 1) return "gerade eben";
            if (span.TotalMinutes < 60) return $"vor {(int)span.TotalMinutes} Min.";
            if (span.TotalHours < 24) return $"vor {(int)span.TotalHours} Std.";
            if (span.TotalDays < 2) return "gestern";
            if (span.TotalDays < 30) return $"vor {(int)span.TotalDays} Tagen";
            return time.ToString("dd.MM.yyyy");
        }

        private void OpenShell(string target, string? arguments = null)
        {
            try
            {
                var info = new ProcessStartInfo(target) { UseShellExecute = true };
                if (arguments != null) info.Arguments = arguments;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Msg($"Konnte nicht geöffnet werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static (int ExitCode, string Output) RunHidden(string file, string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                });
                if (process == null) return (-1, string.Empty);

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                return (process.ExitCode, output);
            }
            catch
            {
                return (-1, string.Empty);
            }
        }

        private static (int ExitCode, string Output) RunPowerShell(string script, int timeoutMs)
        {
            try
            {
                string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
                using var process = Process.Start(new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                });
                if (process == null) return (-1, string.Empty);

                var outputTask = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return (-1, string.Empty);
                }
                return (process.ExitCode, outputTask.GetAwaiter().GetResult());
            }
            catch
            {
                return (-1, string.Empty);
            }
        }

        private static string GetJsonString(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static long JsonLong(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out long parsed)) return parsed;
            return 0;
        }

        private static IEnumerable<JsonElement> JsonItems(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var element)) yield break;

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) yield return item;
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                yield return element;
            }
        }

        private static string CleanName(string raw)
        {
            string text = Regex.Replace(raw, @"\((R|TM|r|tm)\)|®|™", string.Empty);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        // ───────────────────────────── Hilfsfunktionen ─────────────────────────────

        private static System.Windows.Media.Color ParseColor(string? hex, string fallback)
        {
            try
            {
                return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex ?? fallback);
            }
            catch
            {
                return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallback);
            }
        }

        private static SolidColorBrush MakeAlphaBrush(string? hex, byte alpha)
        {
            var color = ParseColor(hex, "#0F111A");
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private System.Windows.Media.Color GetAccentColor()
            => Resources["AccentBrush"] is SolidColorBrush brush ? brush.Color : ParseColor(settings.AccentColor, "#8B5CF6");

        private static string FormatPlaytime(long seconds)
        {
            if (seconds < 60) return seconds <= 0 ? "0 Min." : "< 1 Min.";

            double minutes = seconds / 60.0;
            if (minutes < 60) return $"{minutes:F0} Min.";

            double hours = minutes / 60.0;
            return hours < 100 ? $"{hours:F1} Std." : $"{hours:F0} Std.";
        }

        private static string FormatPlaytimeLong(long seconds)
        {
            long hours = seconds / 3600;
            long minutes = seconds % 3600 / 60;
            return hours > 0 ? $"{hours} Std. {minutes} Min." : $"{minutes} Min.";
        }

        private static bool HasCover(GameItem game)
            => !string.IsNullOrEmpty(EffectiveCover(game)) && File.Exists(EffectiveCover(game));

        /// <summary>Einfache Animation von einem Wert zum anderen (mit sanftem Auslaufen).</summary>
        private void Tween(double from, double to, int durationMs, Action<double> apply, Action? done = null)
        {
            var start = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (s, e) =>
            {
                double t = Math.Min(1.0, (DateTime.UtcNow - start).TotalMilliseconds / durationMs);
                double eased = 1 - Math.Pow(1 - t, 3);
                apply(from + (to - from) * eased);

                if (t >= 1.0)
                {
                    timer.Stop();
                    done?.Invoke();
                }
            };
            timer.Start();
        }

        private void CountUp(TextBlock block, int target)
        {
            if (!settings.PageAnimations || target <= 0)
            {
                block.Text = target.ToString();
                return;
            }

            Tween(0, target, 650, value => block.Text = ((int)Math.Round(value)).ToString(), () => block.Text = target.ToString());
        }

        private BitmapSource CreateLogoBitmap(int size)
        {
            var logo = (FrameworkElement)FindResource("DfpLogo");
            logo.Width = size;
            logo.Height = size;
            logo.Measure(new System.Windows.Size(size, size));
            logo.Arrange(new Rect(0, 0, size, size));
            logo.UpdateLayout();

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(logo);
            bitmap.Freeze();
            return bitmap;
        }

        private static System.Drawing.Icon BitmapSourceToIcon(BitmapSource source)
        {
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);
            stream.Position = 0;

            using var bitmap = new System.Drawing.Bitmap(stream);
            IntPtr handle = bitmap.GetHicon();
            using var temporary = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temporary.Clone();
        }

        // ═════════════════════════════ Weitere Funktionen ═════════════════════════════

        private static readonly (string Key, string Title)[] StatusOptions =
        {
            ("backlog", "📋  Will ich spielen"),
            ("playing", "▶  Spiele ich gerade"),
            ("done", "✅  Durchgespielt")
        };

        private readonly DispatcherTimer maintenanceTimer = new();
        private readonly Dictionary<string, Border> banners = new();
        private readonly Dictionary<GameItem, int> sessionBreakCount = new();
        private readonly Random random = new();

        private string overlayNotice = string.Empty;
        private DateTime overlayNoticeUntil = DateTime.MinValue;
        private DateTime lastDealsRefresh = DateTime.MinValue;

        private List<DealItem> epicFree = new();
        private List<DealItem> epicUpcoming = new();
        private List<DealItem> steamDeals = new();

        private void InitExtras()
        {
            AllowDrop = true;
            DragOver += OnWindowDragOver;
            Drop += OnWindowDrop;

            maintenanceTimer.Interval = TimeSpan.FromMinutes(30);
            maintenanceTimer.Tick += async (s, e) => await RunMaintenanceAsync();
            maintenanceTimer.Start();
        }

        /// <summary>Wird nach jedem Spiele-Scan aufgerufen.</summary>
        private async Task AfterScanAsync()
        {
            settings.InitialScanDone = true;
            lastScan = DateTime.Now;
            SaveSettings();
            RefreshBoard();
            CheckMissingManualGames();
            _ = LoadGameInfoAsync();
            _ = ScanModFoldersAsync();
            RefreshApps();
            _ = UpgradeToAnimatedCoversAsync();
            CheckMilestones();
            await RunMaintenanceAsync();
        }

        private async Task RunMaintenanceAsync()
        {
            try
            {
                CheckStorage();
                CheckReleases();
                CheckAchievements(settings.Achievements.Count == 0);

                if (settings.ShowDeals && settings.OnlineFeatures && (DateTime.Now - lastDealsRefresh).TotalHours >= 6)
                    await RefreshDealsAsync(true);

                await CheckWishlistIfDueAsync();
                await CheckForUpdateIfDueAsync();
            }
            catch { }
        }

        private static bool IsNewGame(GameItem game)
            => game.FirstSeen.HasValue
               && (DateTime.Now - game.FirstSeen.Value).TotalDays <= 7
               && game.PlaySeconds < 60
               && game.LaunchCount == 0;

        private static string StatusIcon(string status) => status switch
        {
            "backlog" => "📋",
            "playing" => "▶",
            "done" => "✅",
            _ => string.Empty
        };

        private Border CreateBadge(string text, bool accent)
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 0, 4, 0),
                Background = accent ? null : BrushOverlay,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                }
            };
            if (accent) badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            return badge;
        }

        // ═════════════════════════════ Sprachen & Komfortfunktionen ═════════════════════════════

        private static System.Threading.Mutex? singleInstanceMutex;
        private static System.Threading.EventWaitHandle? showEventHandle;

        private sealed class TextState
        {
            public string Original = string.Empty;
            public string Shown = string.Empty;
            public int Version = -1;
        }

        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, TextState> textStates = new();
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, TextState> tipStates = new();
        private readonly DispatcherTimer translateTimer = new();
        private bool germanRestorePending;

        private readonly List<string> closedApps = new();
        private readonly List<Forms.ToolStripItem> trayRecentItems = new();
        private DateTime lastScan = DateTime.Now;
        private bool silentRescan;
        private const int HotkeyId = 0x4450;
        private System.Windows.Interop.HwndSource? hotkeySource;

        private static readonly HashSet<string> NeverClose = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "dwm", "winlogon", "csrss", "svchost", "taskmgr", "ApplicationFrameHost", "TextInputHost",
            "SystemSettings", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "LockApp", "sihost", "fontdrvhost"
        };

        // ═════════════════════════════ Anpassung, Leistung und Controller ═════════════════════════════

        private bool buildingNavSettings;
        private readonly Dictionary<string, GifAnimator> gifCache = new();
        private readonly DispatcherTimer shiftTimer = new();

        private bool xinputMissing;
        private string padDescription = string.Empty;
        private string padLayoutDetected = "xbox";

        [Flags]
        private enum PadButton
        {
            None = 0, Up = 1, Down = 2, Left = 4, Right = 8, Confirm = 16, Back = 32,
            Details = 64, Favorite = 128, PrevTab = 256, NextTab = 512, Start = 1024,
            TriggerLeft = 2048, TriggerRight = 4096, Select = 8192
        }

        private IEnumerable<GameItem> VisibleGames => allGames.Where(g => !g.Hidden);

        private void InitExtras3()
        {
            BuildNavMenus();
            BuildNavSettings();

            shiftTimer.Interval = TimeSpan.FromMinutes(3);
            shiftTimer.Tick += (s, e) => AnimateShift(random.Next(-4, 5), random.Next(-4, 5));

            StateChanged += (s, e) => UpdateTimersForVisibility();
            IsVisibleChanged += (s, e) => UpdateTimersForVisibility();

            SidebarFooterText.Cursor = System.Windows.Input.Cursors.Hand;
            SidebarFooterText.MouseLeftButtonUp += (s, e) => ShowAbout();
        }

        private static void ApplyRenderModeEarly()
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return;

                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFilePath));
                if (doc.RootElement.TryGetProperty("SoftwareRendering", out var value) && value.ValueKind == JsonValueKind.True)
                    RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            }
            catch { }
        }

        // ───────────────────────────── Dunkle Menüs und Meldungen ─────────────────────────────

        private System.Windows.Controls.ContextMenu CreateMenu()
        {
            var menu = new System.Windows.Controls.ContextMenu();
            if (TryFindResource("DarkContextMenu") is Style style) menu.Style = style;
            return menu;
        }

        private Forms.ContextMenuStrip CreateTrayMenu()
        {
            var accent = GetAccentColor();
            return new Forms.ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(System.Drawing.Color.FromArgb(accent.R, accent.G, accent.B)),
                BackColor = System.Drawing.Color.FromArgb(19, 23, 34),
                ForeColor = System.Drawing.Color.FromArgb(229, 231, 235),
                ShowImageMargin = false
            };
        }

        private static MessageBoxResult ShowDarkMessage(string text, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            var app = System.Windows.Application.Current;
            if (app == null) return System.Windows.MessageBox.Show(text, title, buttons, icon);

            // Aus Hintergrund-Threads zuerst auf den UI-Thread wechseln
            if (!app.Dispatcher.CheckAccess())
                return app.Dispatcher.Invoke(() => ShowDarkMessage(text, title, buttons, icon));

            if (app.MainWindow is not MainWindow main || !main.IsLoaded)
                return System.Windows.MessageBox.Show(text, title, buttons, icon);

            return main.ShowMessageDialog(text, title, buttons, icon);
        }

        private MessageBoxResult ShowMessageDialog(string text, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            var dialog = CreateDialog(title, 480, out var panel);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string symbol = icon switch
            {
                MessageBoxImage.Error => "⛔",
                MessageBoxImage.Warning => "⚠",
                MessageBoxImage.Question => "❓",
                _ => "ℹ"
            };
            row.Children.Add(new TextBlock
            {
                Text = symbol,
                FontSize = 30,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 16, 0)
            });

            var message = new TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            Grid.SetColumn(message, 1);
            row.Children.Add(message);
            panel.Children.Add(row);

            var result = MessageBoxResult.None;
            var buttonRow = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            void AddButton(string caption, MessageBoxResult value, bool isDefault, bool isCancel)
            {
                var button = new System.Windows.Controls.Button
                {
                    Content = Loc.T(caption),
                    Padding = new Thickness(24, 8, 24, 8),
                    Margin = new Thickness(10, 0, 0, 0),
                    IsDefault = isDefault,
                    IsCancel = isCancel
                };
                if (isDefault) button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                button.Click += (s, e) =>
                {
                    result = value;
                    dialog.DialogResult = true;
                };
                buttonRow.Children.Add(button);
            }

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    AddButton("OK", MessageBoxResult.OK, true, true);
                    break;
                case MessageBoxButton.OKCancel:
                    AddButton("Abbrechen", MessageBoxResult.Cancel, false, true);
                    AddButton("OK", MessageBoxResult.OK, true, false);
                    break;
                case MessageBoxButton.YesNo:
                    AddButton("Nein", MessageBoxResult.No, false, true);
                    AddButton("Ja", MessageBoxResult.Yes, true, false);
                    break;
                default:
                    AddButton("Abbrechen", MessageBoxResult.Cancel, false, true);
                    AddButton("Nein", MessageBoxResult.No, false, false);
                    AddButton("Ja", MessageBoxResult.Yes, true, false);
                    break;
            }

            panel.Children.Add(buttonRow);
            dialog.ShowDialog();

            if (result == MessageBoxResult.None)
            {
                result = buttons switch
                {
                    MessageBoxButton.OK => MessageBoxResult.OK,
                    MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                    MessageBoxButton.YesNo => MessageBoxResult.No,
                    _ => MessageBoxResult.Cancel
                };
            }
            return result;
        }

        // ═════════════════════════════ Schnellzugriff, Logo, SteamGridDB, Infobereich-Panel ═════════════════════════════

        private readonly Dictionary<string, ImageSource?> storeLogos = new();
        private Window? trayFlyout;
        private readonly DispatcherTimer flyoutTimer = new();

        private static readonly (string Name, string Icon, string Url)[] DefaultQuickLinks =
        {
            ("Steam", "🛒", "https://store.steampowered.com"),
            ("Epic Games Store", "🎮", "https://store.epicgames.com"),
            ("GOG", "🕹", "https://www.gog.com"),
            ("Xbox und Game Pass", "🟢", "https://www.xbox.com"),
            ("Discord", "💬", "https://discord.com/download"),
            ("NVIDIA Treiber", "🟩", "https://www.nvidia.com/Download/index.aspx"),
            ("AMD Treiber", "🟥", "https://www.amd.com/en/support"),
            ("Intel Treiber", "🟦", "https://www.intel.com/content/www/us/en/download-center/home.html")
        };

        private static readonly Dictionary<string, (string Ring, string[] Chain)> BrandDesigns = new()
        {
            ["original"] = ("#8B5CF6", new[] { "#FCD34D", "#F59E0B", "#B45309" }),
            ["gold"] = ("#F59E0B", new[] { "#FDE68A", "#FBBF24", "#B45309" }),
            ["neon"] = ("#22D3EE", new[] { "#F0ABFC", "#D946EF", "#86198F" }),
            ["ice"] = ("#38BDF8", new[] { "#F1F5F9", "#CBD5E1", "#64748B" }),
            ["mono"] = ("#9CA3AF", new[] { "#F3F4F6", "#9CA3AF", "#4B5563" }),
            ["accent"] = (string.Empty, new[] { "#FCD34D", "#F59E0B", "#B45309" })
        };

        private static readonly string[] BuiltInIgnored =
            { "wallpaper engine", "wallpaper_engine", "lively wallpaper", "rainmeter", "steam link", "steamvr" };

        private void InitExtras4()
        {
            flyoutTimer.Interval = TimeSpan.FromMilliseconds(260);
            flyoutTimer.Tick += (s, e) =>
            {
                flyoutTimer.Stop();
                ShowTrayFlyout();
            };

            ApplySettingsCategory("general");
        }

        // ═════════════════════════════ Animierte Cover, Fehlerprotokoll, Sicherungen ═════════════════════════════

        private static readonly object LogLock = new();
        private static bool settingsRecovered;
        private DateTime lastErrorToast = DateTime.MinValue;

        private void InitExtras5()
        {
            HookErrorLogging();
            BackupSettingsIfDue();
            UpdateLastBackupText();

            if (settingsRecovered)
            {
                Dispatcher.BeginInvoke(new Action(() => ShowToast("♻", "Einstellungen wiederhergestellt",
                    "Die Einstellungsdatei war beschädigt. Die letzte Sicherung wurde geladen.", 8)),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        // ───────────────────────────── Ansicht als Bild speichern ─────────────────────────────

        private bool SaveVisualAsPng(FrameworkElement element, string title)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = Loc.T(title),
                    Filter = "PNG (*.png)|*.png",
                    FileName = "DFP_" + DateTime.Now.ToString("yyyy-MM-dd") + ".png"
                };
                if (dialog.ShowDialog() != true) return false;

                element.UpdateLayout();
                int width = (int)Math.Ceiling(element.ActualWidth);
                int height = (int)Math.Ceiling(element.ActualHeight);
                if (width < 10 || height < 10) return false;

                // Hintergrund mitzeichnen, damit das Bild nicht durchsichtig wird
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawRectangle(MakeBrush("#0F111A"), null, new Rect(0, 0, width, height));
                    context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
                }

                var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(visual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);

                ShowToast("📸", "Bild gespeichert", System.IO.Path.GetFileName(dialog.FileName), 4);
                return true;
            }
            catch (Exception ex)
            {
                Msg($"Speichern fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ═════════════════════════════ Profile, Klänge, Design, Updates, Sicherung, Einrichtung ═════════════════════════════


        private readonly Dictionary<string, System.Media.SoundPlayer> uiSounds = new();

        private void InitExtras6()
        {
            InitSensors();
            ApplySensorSettings();

            // Bestehende Installationen brauchen den Einrichtungs-Assistenten nicht
            if (settings.InitialScanDone && !settings.SetupDone)
            {
                settings.SetupDone = true;
                SaveSettings();
            }

            Loaded += (s, e) =>
            {
                if (!settings.SetupDone)
                    Dispatcher.BeginInvoke(new Action(RunSetupWizard), DispatcherPriority.ApplicationIdle);
            };

            MirrorSettingsBackup();
        }

        // ═════════════════════════════ Mod-Ordner, Discord, Widgets, Schemas, Willkommen ═════════════════════════════

        private readonly Dictionary<string, string> modFolders = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<GameItem, DateTime> discordStarts = new();
        private bool modScanRunning;
        private SchemeSnapshot? schemeUndo;
        private DispatcherTimer? welcomeTimer;

        private StackPanel? goalWidget;
        private StackPanel? backlogWidget;
        private StackPanel? wishWidget;
        private StackPanel? quickWidget;

        private static readonly (string Key, string Icon, string Title)[] DashWidgetInfo =
        {
            ("hero", "🎬", "Weiterspielen-Banner"),
            ("clock", "🕐", "Uhr und Wetter"),
            ("stats", "📈", "Statistik-Kacheln"),
            ("recent", "🕘", "Zuletzt gespielt"),
            ("favorites", "★", "Favoriten"),
            ("goal", "🎯", "Wochenziel"),
            ("backlog", "📋", "Als Nächstes spielen"),
            ("wishlist", "💝", "Wunschliste"),
            ("quick", "🔗", "Schnellzugriff"),
            ("actions", "⚡", "Schnellaktionen")
        };

        private static readonly string[] DefaultDashHidden = { "goal", "backlog", "wishlist", "quick" };

        private void InitExtras8()
        {
            BuildDashWidgetSettings();
            BuildSchemeTiles();

            Loaded += (s, e) =>
            {
                StartWelcomeAnimation();
                _ = ScanModFoldersAsync();
            };
        }

        // ═════════════════════════════ Bildschirme, Energieplan, Anwendungen ═════════════════════════════

        // ═════════════════════════════ Musik, Schnellstart, Akku, Geräteprofil, Abläufe, Energie ═════════════════════════════

        // ───────────────────────────── Kleine Hilfen für Dialoge ─────────────────────────────

        private int ChooseOne(string title, string hint, List<string> options)
        {
            var dialog = CreateDialog(title, 480, out var panel);

            if (!string.IsNullOrEmpty(hint))
                panel.Children.Add(new TextBlock { Text = hint, Foreground = BrushSubtle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

            int result = -1;
            var list = new StackPanel();
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var button = new System.Windows.Controls.Button
                {
                    Content = options[i],
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                    Padding = new Thickness(14, 9, 14, 9),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                button.Click += (s, e) =>
                {
                    result = index;
                    dialog.DialogResult = true;
                };
                list.Children.Add(button);
            }

            panel.Children.Add(new ScrollViewer { MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
            panel.Children.Add(new System.Windows.Controls.Button
            {
                Content = "Abbrechen",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
                IsCancel = true
            });

            dialog.ShowDialog();
            return result;
        }

        private TextBlock MutedLabel(string text) => new()
        {
            Text = Loc.T(text),
            Foreground = BrushSubtle,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        private WrapPanel ChipRow(IEnumerable<(string Key, string Label)> options, string selected, Action<string> changed)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 14) };
            var style = TryFindResource("ChipStyle") as Style;
            string group = Guid.NewGuid().ToString("N");

            foreach (var (key, label) in options)
            {
                string value = key;
                var chip = new System.Windows.Controls.RadioButton
                {
                    Content = Loc.T(label),
                    GroupName = group,
                    IsChecked = key == selected
                };
                if (style != null) chip.Style = style;
                chip.Checked += (s, e) => changed(value);
                row.Children.Add(chip);
            }
            return row;
        }

        private Border MiniButton(string text, string tip, Action click, double width = 32)
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            var border = new Border
            {
                Width = width,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                Background = MakeBrush("#1AFFFFFF"),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = Loc.T(tip),
                Child = label
            };
            border.MouseEnter += (s, e) => border.Background = MakeBrush("#33FFFFFF");
            border.MouseLeave += (s, e) => border.Background = MakeBrush("#1AFFFFFF");
            border.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                click();
            };
            return border;
        }

        // ═════════════════════════════ Fensterleiste, Farben, RGB, Controller-Akku, Entwickler ═════════════════════════════

        // ═════════════════════════════ Runde 21, Teil 1 ═════════════════════════════

        private void InitExtras13()
        {
            // Musik-Fenster: groß, mittig und mit abgedunkeltem Hintergrund statt halb im Bild
            MusicPanel.IsVisibleChanged += (s, e) =>
            {
                MusicBackdrop.Visibility = MusicPanel.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                if (MusicPanel.IsVisible) SizeMusicPanel();
            };
            SizeChanged += (s, e) =>
            {
                if (MusicPanel.IsVisible) SizeMusicPanel();
            };

            // Die Streamer-Seite ist beim ersten Start ausgeblendet und lässt sich unter „Bereiche anzeigen“ einschalten
            if (!settings.StreamerPageSeeded)
            {
                settings.StreamerPageSeeded = true;
                if (!settings.HiddenSections.Contains("streamer")) settings.HiddenSections.Add("streamer");
                SaveSettings();
                ApplyNavVisibility();
            }

            UpdateStreamerToggles();
        }

        private void SizeMusicPanel()
        {
            double areaWidth = Math.Max(600, RootGrid.ActualWidth - SidebarBorder.ActualWidth);
            double areaHeight = Math.Max(480, RootGrid.ActualHeight - 60);

            MusicPanel.Width = Math.Clamp(areaWidth * 0.78, 560, 1200);
            MusicPanel.Height = Math.Clamp(areaHeight * 0.88, 420, 860);
        }

        private void MusicBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => MusicPanel.Visibility = Visibility.Collapsed;

        // ═════════════════════════════ Runde 21, Teil 2: Cover-Flow und Controller-Modus ═════════════════════════════

        private sealed class PadItem
        {
            public string Name = string.Empty;
            public string Meta = string.Empty;
            public GameItem? Game;
            public AppEntry? App;
        }

        private PadItem PadItemFromGame(GameItem game) => new() { Name = game.Name, Meta = BuildMetaText(game), Game = game };

        private PadItem PadItemFromApp(AppEntry app) => new() { Name = app.Name, Meta = Loc.T(app.IsManual ? "Eigene App" : app.Category), App = app };



        private static void FlowFocus(FrameworkElement element, double amount)
        {
            if (element is Border { Tag: Border frame }) frame.Opacity = amount;
        }

        // ═════════════════════════════ Runde 22 ═════════════════════════════

        // ═════════════════════════════ Runde 23 ═════════════════════════════
    }
}
