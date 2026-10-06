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
    public partial class MainWindow
    {
        // ───────────────────────────── Systemüberwachung ─────────────────────────────

        private static PerformanceCounter? TryCreateCounter(string category, string counter, string instance = "")
        {
            try
            {
                return string.IsNullOrEmpty(instance)
                    ? new PerformanceCounter(category, counter)
                    : new PerformanceCounter(category, counter, instance);
            }
            catch
            {
                return null;
            }
        }

        private void InitSystemMonitoring()
        {
            cpuCounter = TryCreateCounter("Processor", "% Processor Time", "_Total");
            cpuFreqCounter = TryCreateCounter("Processor Information", "% Processor Performance", "_Total");
            ramCounter = TryCreateCounter("Memory", "Available MBytes");

            try { cpuCounter?.NextValue(); } catch { }
            try { cpuFreqCounter?.NextValue(); } catch { }

            try { totalRamMB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0); }
            catch { totalRamMB = 0; }

            try
            {
                baseCpuMhz = Convert.ToDouble(Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", 0));
            }
            catch { baseCpuMhz = 0; }

            try
            {
                string raw = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                    "ProcessorNameString", null) as string ?? string.Empty;
                cpuDisplayName = CleanName(raw);
            }
            catch { cpuDisplayName = string.Empty; }

            systemTimer.Interval = TimeSpan.FromSeconds(1.0);
            systemTimer.Tick += (s, e) => UpdateSystemMetrics();
            systemTimer.Start();
        }

        private void RefreshGpuCounters()
        {
            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Engine")) return;

                var category = new PerformanceCounterCategory("GPU Engine");
                var names = category.GetInstanceNames()
                    .Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var gone in gpuCounters.Keys.Where(k => !names.Contains(k)).ToList())
                {
                    gpuCounters[gone].Dispose();
                    gpuCounters.Remove(gone);
                }

                foreach (var name in names)
                {
                    if (gpuCounters.ContainsKey(name)) continue;
                    gpuCounters[name] = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true);
                }
            }
            catch { }
        }

        private static void AddHistory(List<double> history, double value)
        {
            history.Add(value);
            while (history.Count > SparkPoints) history.RemoveAt(0);
        }

        private static System.Windows.Media.Brush LoadBrush(double percent, System.Windows.Media.Brush normal)
            => percent >= 90 ? BrushRed : percent >= 75 ? BrushAmber : normal;

        /// <summary>Zeichnet den Kreisbogen (0–100 %) für das Ring-Diagramm.</summary>
        private static void SetArc(System.Windows.Shapes.Path path, double percent)
        {
            const double radius = 69;
            const double center = 75;

            percent = Math.Clamp(percent, 0, 100);
            if (percent < 0.5)
            {
                path.Data = null;
                return;
            }
            if (percent > 99.9) percent = 99.9;

            double angle = percent / 100.0 * 2 * Math.PI;
            var start = new System.Windows.Point(center, center - radius);
            var end = new System.Windows.Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));

            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new System.Windows.Size(radius, radius), 0,
                angle > Math.PI, SweepDirection.Clockwise, true));

            path.Data = new PathGeometry(new[] { figure });
        }

        private static void UpdateSparkline(Canvas canvas, System.Windows.Shapes.Polygon polygon, List<double> history)
        {
            if (history.Count < 2) return;

            double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 240;
            double height = canvas.Height;
            double step = width / (SparkPoints - 1);
            double startX = width - (history.Count - 1) * step;

            var points = new PointCollection { new System.Windows.Point(startX, height) };
            for (int i = 0; i < history.Count; i++)
            {
                double y = height - Math.Clamp(history[i], 0, 100) / 100.0 * height;
                points.Add(new System.Windows.Point(startX + i * step, y));
            }
            points.Add(new System.Windows.Point(width, height));

            polygon.Points = points;
        }

        private void DisposeMonitoring()
        {
            systemTimer.Stop();
            clockTimer.Stop();
            weatherTimer.Stop();
            cpuCounter?.Dispose();
            cpuFreqCounter?.Dispose();
            ramCounter?.Dispose();
            foreach (var counter in gpuCounters.Values) counter.Dispose();
            gpuCounters.Clear();
        }

        // ───────────────────────────── Hardware auslesen ─────────────────────────────

        private static List<GpuAdapter> ReadGpuAdapters()
        {
            var list = new List<GpuAdapter>();

            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (classKey == null) return list;

                foreach (string sub in classKey.GetSubKeyNames())
                {
                    if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;

                    using var key = classKey.OpenSubKey(sub);
                    if (key == null) continue;
                    if (key.GetValue("DriverDesc") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                    if (Regex.IsMatch(name, "Basic|Remote|Virtual", RegexOptions.IgnoreCase)) continue;

                    var adapter = new GpuAdapter
                    {
                        Name = CleanName(name),
                        Driver = key.GetValue("DriverVersion") as string ?? string.Empty
                    };

                    // Der Hersteller der Karte (z.B. Gigabyte, ASUS, MSI) steckt in der Subsystem-ID
                    string matching = key.GetValue("MatchingDeviceId") as string ?? string.Empty;
                    var subsys = Regex.Match(matching, "subsys_[0-9a-f]{4}([0-9a-f]{4})", RegexOptions.IgnoreCase);
                    if (subsys.Success && BoardVendors.TryGetValue(subsys.Groups[1].Value, out var vendor))
                        adapter.Vendor = vendor;

                    object? memory = key.GetValue("HardwareInformation.qwMemorySize");
                    if (memory is long qword)
                        adapter.VramBytes = qword;
                    else if (key.GetValue("HardwareInformation.MemorySize") is byte[] bytes && bytes.Length >= 4)
                        adapter.VramBytes = BitConverter.ToUInt32(bytes, 0);

                    list.Add(adapter);
                }
            }
            catch { }

            return list;
        }

        private static GpuAdapter? PickPrimaryGpu(List<GpuAdapter> adapters)
        {
            return adapters.FirstOrDefault(a => Regex.IsMatch(a.Name, "NVIDIA|GeForce|RTX|GTX|Radeon RX|Radeon Pro|Arc"))
                   ?? adapters.FirstOrDefault();
        }

        private static string DescribeGpu(GpuAdapter gpu)
        {
            string text = gpu.Name;

            if (gpu.Vendor.Length > 0 && !gpu.Name.Contains(gpu.Vendor, StringComparison.OrdinalIgnoreCase))
                text += $"  ·  {gpu.Vendor}";
            if (gpu.VramBytes > 0)
                text += $"  ·  {Math.Round(gpu.VramBytes / GigaByte):F0} GB";

            return text;
        }

        private static string MemoryType(int smbios) => smbios switch
        {
            20 => "DDR",
            21 => "DDR2",
            24 => "DDR3",
            26 => "DDR4",
            30 => "LPDDR4",
            34 => "DDR5",
            35 => "LPDDR5",
            _ => string.Empty
        };

        private static string FormatKb(long kb) => kb >= 1024 ? $"{kb / 1024.0:F0} MB" : $"{kb} KB";

        private void ApplyTileNames(List<GpuAdapter> adapters)
        {
            if (cpuDisplayName.Length > 0)
            {
                TxtCpuName.Text = cpuDisplayName;
                TxtCpuName.ToolTip = cpuDisplayName;
            }

            string ram = ramSummary.Length > 0
                ? ramSummary
                : totalRamMB > 0 ? $"{totalRamMB / 1024.0:F0} GB Arbeitsspeicher" : string.Empty;
            TxtRamName.Text = ram;
            TxtRamName.ToolTip = ram;

            var gpu = PickPrimaryGpu(adapters);
            string gpuText = gpu != null ? DescribeGpu(gpu) : "Keine Grafikkarte erkannt";
            TxtGpuName.Text = gpuText;
            TxtGpuName.ToolTip = gpuText;
        }

        private async Task RefreshHardwareAsync()
        {
            if (hardwareBusy) return;
            hardwareBusy = true;
            TxtHardwareStatus.Text = "Hardware wird ausgelesen ...";
            BtnRefreshHardware.IsEnabled = false;

            try
            {
                var adapters = await Task.Run(() => ReadGpuAdapters());
                var result = await Task.Run(() => RunPowerShell(HardwareScript, 90000));

                JsonDocument? doc = null;
                try
                {
                    int start = result.Output.IndexOf('{');
                    int end = result.Output.LastIndexOf('}');
                    if (start >= 0 && end > start)
                        doc = JsonDocument.Parse(result.Output.Substring(start, end - start + 1));
                }
                catch
                {
                    doc = null;
                }

                try
                {
                    HardwareList.ItemsSource = BuildHardwareGroups(doc?.RootElement, adapters);
                }
                finally
                {
                    doc?.Dispose();
                }

                ApplyTileNames(adapters);
                TxtHardwareStatus.Text = $"Zuletzt ausgelesen um {DateTime.Now:HH:mm} Uhr";
            }
            catch (Exception ex)
            {
                TxtHardwareStatus.Text = "Auslesen fehlgeschlagen: " + ex.Message;
            }
            finally
            {
                hardwareBusy = false;
                BtnRefreshHardware.IsEnabled = true;
            }
        }

        private async void BtnRefreshHardware_Click(object sender, RoutedEventArgs e) => await RefreshHardwareAsync();

        private List<HardwareGroup> BuildHardwareGroups(JsonElement? root, List<GpuAdapter> adapters)
        {
            var groups = new List<HardwareGroup>();

            HardwareGroup NewGroup(string title)
            {
                var group = new HardwareGroup { Title = title };
                groups.Add(group);
                return group;
            }

            void Row(HardwareGroup group, string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    group.Rows.Add(new InfoRow { Label = label, Value = value.Trim() });
            }

            IEnumerable<JsonElement> Items(string name) =>
                root.HasValue ? JsonItems(root.Value, name) : Enumerable.Empty<JsonElement>();

            // ── System ──
            var system = NewGroup("System");
            var os = Items("os").FirstOrDefault();
            bool hasOs = os.ValueKind == JsonValueKind.Object;
            int build = Environment.OSVersion.Version.Build;

            Row(system, "Computername", Environment.MachineName);
            Row(system, "Betriebssystem", hasOs ? GetJsonString(os, "Caption") : (build >= 22000 ? "Windows 11" : "Windows 10"));
            Row(system, "Build", hasOs ? GetJsonString(os, "BuildNumber") : build.ToString());
            Row(system, "Architektur", hasOs ? GetJsonString(os, "OSArchitecture") : (Environment.Is64BitOperatingSystem ? "64-Bit" : "32-Bit"));

            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            Row(system, "Laufzeit seit Start", uptime.TotalDays >= 1
                ? $"{(int)uptime.TotalDays} Tg. {uptime.Hours} Std. {uptime.Minutes} Min."
                : $"{uptime.Hours} Std. {uptime.Minutes} Min.");

            // ── Prozessor ──
            var cpuGroup = NewGroup("Prozessor");
            var cpu = Items("cpu").FirstOrDefault();
            bool hasCpu = cpu.ValueKind == JsonValueKind.Object;

            Row(cpuGroup, "Modell", cpuDisplayName.Length > 0 ? cpuDisplayName : (hasCpu ? CleanName(GetJsonString(cpu, "Name")) : string.Empty));
            if (hasCpu)
            {
                Row(cpuGroup, "Hersteller", GetJsonString(cpu, "Manufacturer"));
                Row(cpuGroup, "Kerne / Threads", $"{JsonLong(cpu, "NumberOfCores")} Kerne / {JsonLong(cpu, "NumberOfLogicalProcessors")} Threads");

                long maxMhz = JsonLong(cpu, "MaxClockSpeed");
                if (maxMhz > 0) Row(cpuGroup, "Takt", $"{maxMhz / 1000.0:F2} GHz");

                long l2 = JsonLong(cpu, "L2CacheSize");
                long l3 = JsonLong(cpu, "L3CacheSize");
                if (l2 > 0 || l3 > 0)
                    Row(cpuGroup, "Cache", $"L2 {FormatKb(l2)}  ·  L3 {FormatKb(l3)}");
            }
            else
            {
                Row(cpuGroup, "Threads", Environment.ProcessorCount.ToString());
            }

            // ── Arbeitsspeicher ──
            var ramGroup = NewGroup("Arbeitsspeicher");
            var modules = Items("ram").ToList();
            long total = modules.Sum(m => JsonLong(m, "Capacity"));
            if (total == 0) total = (long)(totalRamMB * 1024 * 1024);

            Row(ramGroup, "Gesamt", total > 0 ? $"{total / GigaByte:F0} GB" : null);

            int moduleIndex = 1;
            foreach (var module in modules)
            {
                long capacity = JsonLong(module, "Capacity");
                long speed = JsonLong(module, "ConfiguredClockSpeed");
                if (speed == 0) speed = JsonLong(module, "Speed");

                string type = MemoryType((int)JsonLong(module, "SMBIOSMemoryType"));
                string slot = GetJsonString(module, "DeviceLocator");
                if (slot.Length == 0) slot = $"Modul {moduleIndex}";

                string detail = $"{capacity / GigaByte:F0} GB";
                if (type.Length > 0) detail += $" {type}";
                if (speed > 0) detail += type.Length > 0 ? $"-{speed}" : $" {speed} MT/s";

                string part = GetJsonString(module, "PartNumber").Trim();
                Row(ramGroup, slot, part.Length > 0 ? $"{detail}  ·  {part}" : detail);
                moduleIndex++;
            }

            if (modules.Count > 0)
            {
                var first = modules[0];
                long firstSpeed = JsonLong(first, "ConfiguredClockSpeed");
                if (firstSpeed == 0) firstSpeed = JsonLong(first, "Speed");
                string firstType = MemoryType((int)JsonLong(first, "SMBIOSMemoryType"));

                string kind = firstType.Length > 0 ? (firstSpeed > 0 ? $"{firstType}-{firstSpeed}" : firstType) : string.Empty;
                ramSummary = $"{total / GigaByte:F0} GB {kind}".Trim() +
                             (modules.Count > 1 ? $"  ({modules.Count} × {JsonLong(first, "Capacity") / GigaByte:F0} GB)" : string.Empty);
            }

            // ── Grafikkarte(n) ──
            int gpuIndex = 1;
            foreach (var adapter in adapters)
            {
                var gpuGroup = NewGroup(adapters.Count > 1 ? $"Grafikkarte {gpuIndex}" : "Grafikkarte");
                Row(gpuGroup, "Modell", adapter.Name);
                Row(gpuGroup, "Kartenhersteller", adapter.Vendor);
                if (adapter.VramBytes > 0) Row(gpuGroup, "Videospeicher", $"{Math.Round(adapter.VramBytes / GigaByte):F0} GB");
                Row(gpuGroup, "Treiberversion", adapter.Driver);
                gpuIndex++;
            }

            // ── Anzeige ──
            var display = NewGroup("Anzeige");
            int monitorIndex = 1;
            foreach (var monitor in Items("monitors"))
            {
                string name = GetJsonString(monitor, "Name");
                if (name.Length == 0) name = GetJsonString(monitor, "Maker");
                Row(display, $"Monitor {monitorIndex}", name);
                monitorIndex++;
            }

            var gpuRows = Items("gpu").ToList();
            long wmiRefresh = gpuRows.Select(row => JsonLong(row, "CurrentRefreshRate")).FirstOrDefault(v => v > 0);

            // Auflösung und Bildrate jedes angeschlossenen Bildschirms direkt von Windows (echte Pixel, unabhängig von der DPI-Skalierung)
            var displays = ReadDisplays();

            if (displays.Count == 0)
            {
                foreach (var gpuRow in gpuRows)
                {
                    long wmiWidth = JsonLong(gpuRow, "CurrentHorizontalResolution");
                    long wmiHeight = JsonLong(gpuRow, "CurrentVerticalResolution");
                    if (wmiWidth > 0 && wmiHeight > 0)
                        displays.Add(new DisplayInfo((int)wmiWidth, (int)wmiHeight, NormalizeRefresh((int)wmiRefresh), displays.Count == 0, false, 0, 0));
                }
            }

            if (displays.Count == 0)
            {
                foreach (var screen in Forms.Screen.AllScreens)
                    displays.Add(new DisplayInfo(screen.Bounds.Width, screen.Bounds.Height, NormalizeRefresh((int)wmiRefresh),
                        screen.Primary, false, screen.Bounds.X, screen.Bounds.Y));
            }

            int screenIndex = 1;
            foreach (var info in displays)
            {
                string resolution = $"{info.Width} × {info.Height}";
                if (info.Hertz > 0) resolution += $" @ {info.Hertz} Hz";
                if (info.Portrait) resolution += "  ·  " + Loc.T("Hochformat");
                if (info.Primary) resolution += "  (Hauptanzeige)";
                Row(display, $"Auflösung {screenIndex}", resolution);
                screenIndex++;
            }

            // ── Mainboard & BIOS ──
            var boardGroup = NewGroup("Mainboard und BIOS");
            var board = Items("board").FirstOrDefault();
            if (board.ValueKind == JsonValueKind.Object)
            {
                Row(boardGroup, "Hersteller", GetJsonString(board, "Manufacturer"));
                Row(boardGroup, "Modell", GetJsonString(board, "Product"));
            }
            var bios = Items("bios").FirstOrDefault();
            if (bios.ValueKind == JsonValueKind.Object)
            {
                Row(boardGroup, "BIOS-Hersteller", GetJsonString(bios, "Manufacturer"));
                Row(boardGroup, "BIOS-Version", GetJsonString(bios, "SMBIOSBIOSVersion"));
                Row(boardGroup, "BIOS-Datum", GetJsonString(bios, "ReleaseDate"));
            }

            // ── Laufwerke ──
            var diskGroup = NewGroup("Laufwerke");
            foreach (var disk in Items("disks"))
            {
                var parts = new List<string> { FormatDiskSize(JsonLong(disk, "Size")) };
                string media = GetJsonString(disk, "MediaType");
                string bus = GetJsonString(disk, "BusType");
                if (media.Length > 0 && !media.Equals("Unspecified", StringComparison.OrdinalIgnoreCase)) parts.Add(media);
                if (bus.Length > 0 && !bus.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) parts.Add(bus);
                Row(diskGroup, GetJsonString(disk, "FriendlyName"), string.Join("  ·  ", parts));
            }

            // ── Audio & Netzwerk ──
            var other = NewGroup("Audio und Netzwerk");
            int soundIndex = 1;
            foreach (var sound in Items("sound"))
                Row(other, $"Audio {soundIndex++}", GetJsonString(sound, "Name"));
            int netIndex = 1;
            foreach (var adapter in Items("net"))
                Row(other, $"Netzwerk {netIndex++}", GetJsonString(adapter, "Name"));

            return groups.Where(g => g.Rows.Count > 0).ToList();
        }

        // ───────────────────────────── Speicher ─────────────────────────────

        private void RefreshDrives()
        {
            var models = new List<DriveInfoModel>();

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;

                    double total = drive.TotalSize / GigaByte;
                    double free = drive.AvailableFreeSpace / GigaByte;
                    double used = total - free;
                    double percent = total > 0 ? used / total * 100.0 : 0;
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Lokaler Datenträger" : drive.VolumeLabel;

                    models.Add(new DriveInfoModel
                    {
                        DriveName = $"{drive.Name} ({label})",
                        InfoText = $"{used:F1} GB belegt von {total:F1} GB  ·  {free:F1} GB frei",
                        PercentText = $"{percent:F0}% belegt",
                        UsedPercent = percent,
                        BarBrush = LoadBrush(percent, BrushRam)
                    });
                }
                catch { }
            }

            DrivesControl.ItemsSource = models;
        }

        private static long GetDirectorySize(string path)
        {
            long total = 0;
            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
                    total += file.Length;
            }
            catch { }
            return total;
        }

        private async void BtnCalcGameSizes_Click(object sender, RoutedEventArgs e)
        {
            BtnCalcGameSizes.IsEnabled = false;
            TxtGameSizesInfo.Text = "Berechne ... das kann bei vielen Spielen einen Moment dauern.";

            var targets = allGames
                .Where(g => !string.IsNullOrEmpty(g.InstallDir) && Directory.Exists(g.InstallDir))
                .Select(g => (g.Name, g.InstallDir))
                .ToList();

            var results = await Task.Run(() => targets
                .Select(t => (t.Name, Size: GetDirectorySize(t.InstallDir)))
                .OrderByDescending(r => r.Size)
                .ToList());

            long max = results.Count > 0 ? results.Max(r => r.Size) : 0;

            GameSizesControl.ItemsSource = results.Select(r => new GameSizeModel
            {
                Name = r.Name,
                SizeText = FormatBytes(r.Size),
                Percent = max > 0 ? r.Size * 100.0 / max : 0
            }).ToList();

            TxtGameSizesInfo.Text = results.Count == 0
                ? "Keine installierten Spiele gefunden."
                : $"{results.Count} Spiele  ·  zusammen {FormatBytes(results.Sum(r => r.Size))}";

            BtnCalcGameSizes.IsEnabled = true;
        }

        // ───────────────────────────── Optimierung ─────────────────────────────

        private async Task RefreshPowerPlanAsync()
        {
            var result = await Task.Run(() => RunHidden("powercfg", "/getactivescheme"));
            var match = Regex.Match(result.Output, @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");

            string name;
            if (!match.Success)
            {
                name = "unbekannt";
            }
            else if (PowerPlanNames.TryGetValue(match.Value, out var known))
            {
                name = known;
            }
            else
            {
                var label = Regex.Match(result.Output, @"\(([^)]+)\)");
                string shown = label.Success ? label.Groups[1].Value.Trim() : string.Empty;
                name = Regex.IsMatch(shown, "Ultimat", RegexOptions.IgnoreCase) ? "Ultimative Leistung"
                     : shown.Length > 0 ? shown
                     : "Benutzerdefiniert";
            }

            currentPowerPlanName = name;
            TxtPowerPlan.Text = $"Aktiver Plan: {name}";
            UpdatePlanTiles(name);
            RefreshGamingCheck();
        }

        // ───────────────────────────── Gaming-Check: Windows-Einstellungen für Spiele ─────────────────────────────

        private static bool ReadGameModeEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
                object? value = key?.GetValue("AutoGameModeEnabled");
                return value == null || Convert.ToInt32(value) != 0;
            }
            catch
            {
                return true;
            }
        }

        private static void SetGameModeEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
                key?.SetValue("AutoGameModeEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static bool ReadGameDvrEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
                object? value = key?.GetValue("GameDVR_Enabled");
                return value == null || Convert.ToInt32(value) != 0;
            }
            catch
            {
                return true;
            }
        }

        private static void SetGameDvrEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
                key?.SetValue("GameDVR_Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
                key?.SetValue("AppCaptureEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static bool ReadMouseAccelEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                return (key?.GetValue("MouseSpeed") as string) == "1";
            }
            catch
            {
                return false;
            }
        }

        private static void SetMouseAccelEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse");
                key?.SetValue("MouseSpeed", enabled ? "1" : "0");
                key?.SetValue("MouseThreshold1", enabled ? "6" : "0");
                key?.SetValue("MouseThreshold2", enabled ? "10" : "0");
            }
            catch { }

            // Sofort anwenden, ohne dass sich der Nutzer ab- und wieder anmelden muss
            try
            {
                NativeExtras.SystemParametersInfoMouse(0x0004 /* SPI_SETMOUSE */, 0,
                    new[] { 0, 0, enabled ? 1 : 0 }, 0x02 /* SPIF_SENDCHANGE */);
            }
            catch { }
        }

        private void PopulateOptimizationToggles()
        {
            if (ChkGameMode == null) return;

            loadingOptimizationToggles = true;
            try
            {
                ChkGameMode.IsChecked = ReadGameModeEnabled();
                ChkGameDvr.IsChecked = ReadGameDvrEnabled();
                ChkMouseAccel.IsChecked = ReadMouseAccelEnabled();
            }
            finally
            {
                loadingOptimizationToggles = false;
            }

            RefreshGamingCheck();
        }

        private void ChkGameMode_Changed(object sender, RoutedEventArgs e)
        {
            if (loadingOptimizationToggles) return;
            SetGameModeEnabled(ChkGameMode.IsChecked == true);
            RefreshGamingCheck();
        }

        private void ChkGameDvr_Changed(object sender, RoutedEventArgs e)
        {
            if (loadingOptimizationToggles) return;
            SetGameDvrEnabled(ChkGameDvr.IsChecked == true);
            RefreshGamingCheck();
        }

        private void ChkMouseAccel_Changed(object sender, RoutedEventArgs e)
        {
            if (loadingOptimizationToggles) return;
            SetMouseAccelEnabled(ChkMouseAccel.IsChecked == true);
            RefreshGamingCheck();
        }

        private void RefreshGamingCheck()
        {
            if (ArcGamingCheck == null) return;

            bool powerOk = currentPowerPlanName is "Höchstleistung" or "Ultimative Leistung";
            bool gameModeOk = ReadGameModeEnabled();
            bool mouseOk = !ReadMouseAccelEnabled();
            bool dvrOk = !ReadGameDvrEnabled();

            int score = (powerOk ? 1 : 0) + (gameModeOk ? 1 : 0) + (mouseOk ? 1 : 0) + (dvrOk ? 1 : 0);

            SetArc(ArcGamingCheck, score / 4.0 * 100);
            TxtGamingCheckScore.Text = $"{score}/4";
            TxtGamingCheckHint.Text = score == 4
                ? Loc.T("Alles bereit. Dein PC ist für Spiele optimal eingestellt.")
                : Loc.T($"Noch {4 - score} von 4 Punkten offen. Du kannst sie unten mit einem Klick verbessern.");

            void SetRow(TextBlock icon, bool ok)
            {
                icon.Text = ok ? "✓" : "⚠";
                icon.Foreground = ok ? MakeBrush("#34D399") : MakeBrush("#F59E0B");
            }

            SetRow(IconCheckPower, powerOk);
            SetRow(IconCheckGameMode, gameModeOk);
            SetRow(IconCheckMouse, mouseOk);
            SetRow(IconCheckDvr, dvrOk);
        }

        private void BtnOpenSetting_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button { Tag: string target })
                OpenShell(target);
        }

        // ───────────────────────────── Tools & Links ─────────────────────────────

        private Border CreateTile(string emoji, ImageSource? image, string title, string subtitle, Action onClick,
            System.Windows.Controls.ContextMenu? menu = null, double width = 200)
        {
            var tile = new Border
            {
                Width = width,
                Height = 118,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(16),
                Background = BrushCardBg,
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Cursor = System.Windows.Input.Cursors.Hand,
                ContextMenu = menu
            };

            var stack = new StackPanel();

            if (image != null)
            {
                stack.Children.Add(new System.Windows.Controls.Image
                {
                    Source = image,
                    Width = 32,
                    Height = 32,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = emoji,
                    FontSize = 26,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = BrushSubtle,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            tile.Child = stack;

            tile.MouseEnter += (s, e) => tile.Background = BrushTileHover;
            tile.MouseLeave += (s, e) => tile.Background = BrushCardBg;
            tile.MouseLeftButtonUp += (s, e) => onClick();

            return tile;
        }

        private Border CreateToolTile(string icon, string colorHex, string title, string subtitle, Action onClick, double width = 190, double height = 138)
        {
            var badge = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(11),
                Background = MakeBrush(colorHex),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = icon,
                    FontSize = 19,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            };

            var stack = new StackPanel();
            stack.Children.Add(badge);
            stack.Children.Add(new TextBlock
            {
                Text = Loc.T(title),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = Loc.T(subtitle),
                Foreground = MakeBrush("#B4BCCB"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

            var tile = new Border
            {
                Width = width,
                Height = height,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(16),
                Background = BrushCardBg,
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = stack
            };
            tile.MouseEnter += (s, e) => tile.Background = BrushTileHover;
            tile.MouseLeave += (s, e) => tile.Background = BrushCardBg;
            tile.MouseLeftButtonUp += (s, e) => onClick();
            return tile;
        }

        private TextBlock ToolSectionHeader(string text) => new()
        {
            Text = Loc.T(text),
            Foreground = MakeBrush("#E5E7EB"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 18, 0, 10),
            Effect = (System.Windows.Media.Effects.Effect)FindResource("TextShadow")
        };

        private void BuildToolTiles()
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string screenshots = System.IO.Path.Combine(pictures, "Screenshots");
            string screenshotTarget = Directory.Exists(screenshots) ? screenshots : pictures;

            const string blue = "#3B82F6";
            const string purple = "#8B5CF6";
            const string green = "#10B981";

            var tools = new (string Icon, string Color, string Title, string Text, string Target, string Category)[]
            {
                ("🖥", blue, "Anzeige", "Auflösung und Bildrate", "ms-settings:display", "Anzeige und Sound"),
                ("🔊", blue, "Sound", "Audioeinstellungen", "ms-settings:sound", "Anzeige und Sound"),
                ("🖼", blue, "Screenshots", "Ordner öffnen", screenshotTarget, "Anzeige und Sound"),
                ("✂", blue, "Snipping Tool", "Bildschirmausschnitt", "ms-screenclip:", "Anzeige und Sound"),

                ("📋", purple, "Task-Manager", "Windows Task-Manager", "taskmgr.exe", "System und Diagnose"),
                ("📈", purple, "Ressourcenmonitor", "Detaillierte Auslastung", "resmon.exe", "System und Diagnose"),
                ("ℹ", purple, "Systeminfo", "Hardware und System", "msinfo32.exe", "System und Diagnose"),
                ("🩺", purple, "DxDiag", "DirectX-Diagnose", "dxdiag.exe", "System und Diagnose"),
                ("🔧", purple, "Geräte-Manager", "Treiber verwalten", "devmgmt.msc", "System und Diagnose"),
                ("🌐", purple, "Netzwerk", "Netzwerkverbindungen", "ncpa.cpl", "System und Diagnose"),

                ("🕹", green, "Spielmodus", "Windows Game Mode", "ms-settings:gaming-gamemode", "Spiele und Leistung"),
                ("🚀", green, "Autostart-Apps", "Programme beim Start", "ms-settings:startupapps", "Spiele und Leistung"),
                ("🔄", green, "Windows Update", "Updates suchen", "ms-settings:windowsupdate", "Spiele und Leistung")
            };

            if (settings.PowerButtons)
            {
                const string red = "#EF4444";
                tools = tools.Concat(new (string Icon, string Color, string Title, string Text, string Target, string Category)[]
                {
                    ("🔌", red, "Herunterfahren", "PC ausschalten", "power:shutdown", "Energie"),
                    ("🔄", red, "Neu starten", "PC neu starten", "power:restart", "Energie"),
                    ("🌙", red, "Energie sparen", "Standby", "power:sleep", "Energie"),
                    ("👤", red, "Abmelden", "Benutzer abmelden", "power:logoff", "Energie"),
                    ("🔒", red, "PC sperren", "Bildschirm sperren", "power:lock", "Energie")
                }).ToArray();
            }

            toolEntries = tools.Select(t => (t.Icon, t.Title, t.Text, t.Target)).ToArray();

            bool firstGroup = true;
            foreach (var group in tools.GroupBy(t => t.Category))
            {
                var header = ToolSectionHeader(group.Key);
                if (firstGroup) { header.Margin = new Thickness(0, 0, 0, 10); firstGroup = false; }
                ToolsContainer.Children.Add(header);

                var row = new WrapPanel();
                foreach (var tool in group)
                {
                    string target = tool.Target;
                    row.Children.Add(CreateToolTile(tool.Icon, tool.Color, tool.Title, tool.Text, () => HandleToolTarget(target)));
                }
                ToolsContainer.Children.Add(row);
            }

            BuildSystemCommandTiles();
        }

        // ───────────────────────────── Anwendungen ─────────────────────────────

        private List<AppEntry> LoadFallbackApps()
        {
            var apps = new List<AppEntry>();

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            void AddApp(string name, string? arguments, params string[] candidates)
            {
                string? exe = candidates.FirstOrDefault(File.Exists);
                if (exe == null) return;

                apps.Add(new AppEntry
                {
                    Name = name,
                    ExePath = exe,
                    Arguments = arguments ?? string.Empty,
                    Icon = ExtractIcon(exe)
                });
            }

            // Spiele-Launcher (Steam, Epic, Battle.net, ...) tauchen bereits über die Spiele-Bibliothek auf
            // und werden hier bewusst nicht mit aufgenommen, damit Spiele und Programme getrennt bleiben.
            AddApp("Discord", "--processStart Discord.exe", System.IO.Path.Combine(local, "Discord", "Update.exe"));
            AddApp("Spotify", null,
                System.IO.Path.Combine(roaming, "Spotify", "Spotify.exe"),
                System.IO.Path.Combine(local, "Microsoft", "WindowsApps", "Spotify.exe"));
            AddApp("OBS Studio", null, System.IO.Path.Combine(programFiles, "obs-studio", "bin", "64bit", "obs64.exe"));
            AddApp("Google Chrome", null,
                System.IO.Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                System.IO.Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"));
            AddApp("Microsoft Edge", null,
                System.IO.Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                System.IO.Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"));
            AddApp("Firefox", null, System.IO.Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"));

            return apps;
        }

        private void LaunchApp(AppEntry app)
        {
            try
            {
                if (app.AppId.Length > 0)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + app.AppId) { UseShellExecute = true });
                    return;
                }

                var info = new ProcessStartInfo(app.ExePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(app.ExePath) ?? string.Empty
                };
                if (!string.IsNullOrEmpty(app.Arguments)) info.Arguments = app.Arguments;
                Process.Start(info);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // Administrator-Abfrage abgebrochen
            }
            catch (Exception ex)
            {
                Msg($"App konnte nicht gestartet werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Programme (*.exe)|*.exe",
                Title = "App auswählen"
            };
            if (dialog.ShowDialog() != true) return;

            if (settings.ManualApps.Any(a => string.Equals(a.ExePath, dialog.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                Msg("Diese App ist bereits in der Liste.");
                return;
            }

            settings.ManualApps.Add(new ManualApp
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
                ExePath = dialog.FileName
            });
            SaveSettings();
            RefreshApps();
        }

        private void RemoveManualApp(AppEntry app)
        {
            settings.ManualApps.RemoveAll(a => string.Equals(a.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase));
            SaveSettings();
            RefreshApps();
        }

        // ───────────────────────────── Downloads ─────────────────────────────

        private void RefreshDownloads()
        {
            var list = new List<FileEntry>();

            try
            {
                if (Directory.Exists(DownloadsPath))
                {
                    var files = new DirectoryInfo(DownloadsPath)
                        .EnumerateFiles()
                        .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                        .OrderByDescending(f => f.LastWriteTime)
                        .Take(40);

                    foreach (var file in files)
                    {
                        list.Add(new FileEntry
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            SizeText = FormatBytes(file.Length),
                            DateText = file.LastWriteTime.ToString("dd.MM.yyyy HH:mm")
                        });
                    }
                }
            }
            catch { }

            GridDownloads.ItemsSource = list;
            TxtDownloadsInfo.Text = $"{list.Count} neueste Dateien aus {DownloadsPath}";
        }

        private void OpenSelectedDownload()
        {
            if (GridDownloads.SelectedItem is FileEntry entry) OpenShell(entry.FullPath);
        }

        private void GridDownloads_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenSelectedDownload();
        private void BtnOpenDownload_Click(object sender, RoutedEventArgs e) => OpenSelectedDownload();
        private void BtnRefreshDownloads_Click(object sender, RoutedEventArgs e) => RefreshDownloads();

        private void BtnShowDownload_Click(object sender, RoutedEventArgs e)
        {
            if (GridDownloads.SelectedItem is FileEntry entry)
                OpenShell("explorer.exe", $"/select,\"{entry.FullPath}\"");
        }

        private void BtnOpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(DownloadsPath)) OpenShell(DownloadsPath);
        }

        // ───────────────────────────── Systemüberwachung (weich animiert) ─────────────────────────────

        private MetricsSample SampleMetrics()
        {
            // Zähler nur selten abfragen, damit sich Dashboard, Ansicht und Overlay nicht gegenseitig stören
            if (lastSample != null && (DateTime.Now - lastSampleTime).TotalMilliseconds < 400)
                return lastSample;

            var sample = new MetricsSample();

            try
            {
                sample.Cpu = Math.Min(100f, cpuCounter?.NextValue() ?? 0f);
                float cpuPerf = cpuFreqCounter?.NextValue() ?? 100f;
                sample.Mhz = baseCpuMhz > 0 ? baseCpuMhz * cpuPerf / 100.0 : 0;

                if (ramCounter != null && totalRamMB > 0)
                {
                    sample.UsedMb = Math.Max(0, totalRamMB - ramCounter.NextValue());
                    sample.Ram = Math.Min(100, sample.UsedMb / totalRamMB * 100.0);
                }

                if (gpuRefreshCountdown-- <= 0)
                {
                    RefreshGpuCounters();
                    gpuRefreshCountdown = 5;
                }

                var perEngine = new Dictionary<string, double>();
                foreach (var pair in gpuCounters)
                {
                    try
                    {
                        int idx = pair.Key.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
                        string engine = idx >= 0 ? pair.Key.Substring(idx) : pair.Key;
                        perEngine.TryGetValue(engine, out double current);
                        perEngine[engine] = current + pair.Value.NextValue();
                    }
                    catch { }
                }

                sample.HasGpu = gpuCounters.Count > 0;
                sample.Gpu = perEngine.Count > 0 ? Math.Min(100, perEngine.Values.Max()) : 0;
            }
            catch { }

            lastSample = sample;
            lastSampleTime = DateTime.Now;
            return sample;
        }

        private void UpdateSystemMetrics()
        {
            bool onSystem = ViewSystem.Visibility == Visibility.Visible;
            bool onDashboard = ViewDashboard.Visibility == Visibility.Visible;
            bool overlayActive = overlayWindow != null && overlayWindow.IsVisible;
            if (!overlayActive && (!IsVisible || WindowState == WindowState.Minimized)) return;

            // Nur messen, wenn man es auch sieht – spart Ressourcen während des Spielens.
            if (!onSystem && !onDashboard && !overlayActive) return;

            var m = SampleMetrics();

            if (onDashboard)
            {
                TxtMiniCpu.Text = $"CPU {m.Cpu:F0}%";
                TxtMiniRam.Text = $"RAM {m.Ram:F0}%";
                TxtMiniGpu.Text = m.HasGpu ? $"GPU {m.Gpu:F0}%" : "GPU –";
            }

            if (overlayActive) UpdateOverlayContent(m);

            if (!onSystem) return;

            gpuAvailable = m.HasGpu;
            ringTarget[0] = m.Cpu;
            ringTarget[1] = m.Ram;
            ringTarget[2] = m.Gpu;

            if (!settings.SmoothMetrics)
            {
                for (int i = 0; i < 3; i++) ringShown[i] = ringTarget[i];
                ApplyRings();
            }

            TxtCpuSub.Text = m.Mhz > 0 ? $"{m.Mhz:F0} MHz" : "Auslastung";
            TxtRamSub.Text = totalRamMB > 0 ? $"{m.UsedMb / 1024.0:F1} / {totalRamMB / 1024.0:F0} GB" : "Auslastung";
            TxtGpuSub.Text = m.HasGpu ? "3D-Last" : "nicht verfügbar";
            UpdateMetricDetails();
            UpdateDriveAndNetwork();

            AddHistory(cpuHistory, m.Cpu);
            AddHistory(ramHistory, m.Ram);
            AddHistory(gpuHistory, m.Gpu);

            if (settings.ShowSparklines)
            {
                UpdateSparkline(CanvasCpu, PolyCpu, cpuHistory);
                UpdateSparkline(CanvasRam, PolyRam, ramHistory);
                UpdateSparkline(CanvasGpu, PolyGpu, gpuHistory);
            }
        }

        private void RingTick()
        {
            if (ViewSystem.Visibility != Visibility.Visible) return;

            bool changed = false;
            for (int i = 0; i < 3; i++)
            {
                double diff = ringTarget[i] - ringShown[i];
                if (Math.Abs(diff) < 0.05)
                {
                    if (ringShown[i] != ringTarget[i])
                    {
                        ringShown[i] = ringTarget[i];
                        changed = true;
                    }
                    continue;
                }

                ringShown[i] += diff * (settings.SmoothMetrics ? 0.12 : 1.0);
                changed = true;
            }

            if (changed) ApplyRings();
        }

        private void ApplyRings()
        {
            TxtCpuPercent.Text = $"{ringShown[0]:F0}%";
            SetArc(ArcCpu, ringShown[0]);
            ArcCpu.Stroke = LoadBrush(ringShown[0], BrushCpu);

            TxtRamPercent.Text = $"{ringShown[1]:F0}%";
            SetArc(ArcRam, ringShown[1]);
            ArcRam.Stroke = LoadBrush(ringShown[1], BrushRam);

            TxtGpuPercent.Text = gpuAvailable ? $"{ringShown[2]:F0}%" : "–";
            SetArc(ArcGpu, gpuAvailable ? ringShown[2] : 0);
            ArcGpu.Stroke = LoadBrush(ringShown[2], BrushGpu);
        }

        // ───────────────────────────── Mini-Overlay ─────────────────────────────

        private void EnsureOverlay()
        {
            if (overlayWindow != null) return;

            overlayGameText = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 320
            };
            overlayStatsText = new TextBlock
            {
                Foreground = MakeBrush("#D1D5DB"),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var content = new StackPanel { MinWidth = 220 };
            content.Children.Add(overlayGameText);
            content.Children.Add(overlayStatsText);

            var card = new Border
            {
                Background = MakeBrush("#E60F111A"),
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Child = content
            };

            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = card
            };
            window.SourceInitialized += (s, e) =>
            {
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                int style = NativeFeatures.GetWindowLong(handle, -20);
                // klick-durchlässig, nicht aktivierbar, nicht in Alt+Tab
                NativeFeatures.SetWindowLong(handle, -20, style | 0x20 | 0x80 | 0x08000000);
            };

            overlayWindow = window;
        }

        private void UpdateOverlayVisibility()
        {
            bool shouldShow = settings.ShowOverlay && activeSessions.Count > 0;
            bool testing = DateTime.Now < overlayTestUntil;

            if (shouldShow || testing)
            {
                EnsureOverlay();
                if (overlayWindow != null && !overlayWindow.IsVisible) overlayWindow.Show();
                UpdateOverlayContent(SampleMetrics());
            }
            else if (overlayWindow != null && overlayWindow.IsVisible)
            {
                overlayWindow.Hide();
            }
        }

        private void UpdateOverlayContent(MetricsSample m)
        {
            if (overlayWindow == null || overlayGameText == null || overlayStatsText == null) return;

            var current = activeSessions.Keys.FirstOrDefault();
            if (current != null && sessionStart.TryGetValue(current, out var started))
            {
                var elapsed = DateTime.Now - started;
                overlayGameText.Text = $"🎮 {current.Name}  ·  {(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            }
            else
            {
                overlayGameText.Text = "🎮 DFP Pro Launcher  ·  Overlay-Test";
            }

            var overlayParts = new List<string>();
            if (settings.OverlayShowCpu) overlayParts.Add($"CPU {m.Cpu,3:F0}%");
            if (settings.OverlayShowGpu) overlayParts.Add($"GPU {(m.HasGpu ? m.Gpu.ToString("F0").PadLeft(3) : "  –")}%");
            if (settings.OverlayShowRam) overlayParts.Add($"RAM {m.Ram,3:F0}%");
            if (settings.OverlayShowClock) overlayParts.Add(DateTime.Now.ToString("HH:mm"));
            overlayStatsText.Text = string.Join("   ", overlayParts);
            ApplyOverlayLook();
            if (DateTime.Now < overlayNoticeUntil) overlayStatsText.Text += "\n" + overlayNotice;

            PositionOverlay();
        }

        private void PositionOverlay()
        {
            if (overlayWindow == null) return;

            overlayWindow.UpdateLayout();
            var area = SystemParameters.WorkArea;
            double width = overlayWindow.ActualWidth;
            double height = overlayWindow.ActualHeight;
            const double margin = 16;

            bool left = settings.OverlayCorner == 0 || settings.OverlayCorner == 2;
            bool top = settings.OverlayCorner == 0 || settings.OverlayCorner == 1;
            overlayWindow.Left = left ? area.Left + margin : area.Right - width - margin;
            overlayWindow.Top = top ? area.Top + margin : area.Bottom - height - margin;
        }

        // ───────────────────────────── Netzwerk: Ping-Test ─────────────────────────────

        private static string? GetGatewayAddress()
        {
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    foreach (var gateway in nic.GetIPProperties().GatewayAddresses)
                    {
                        if (gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                            !gateway.Address.Equals(System.Net.IPAddress.Any))
                            return gateway.Address.ToString();
                    }
                }
            }
            catch { }

            return null;
        }

        private static async Task<(double? Average, int Lost, int Sent)> PingHostAsync(string host, int count)
        {
            int lost = 0;
            var times = new List<long>();

            using var ping = new System.Net.NetworkInformation.Ping();
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(host, 1500);
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success) times.Add(reply.RoundtripTime);
                    else lost++;
                }
                catch
                {
                    lost++;
                }
            }

            return (times.Count > 0 ? (double?)times.Average() : null, lost, count);
        }

        private static async Task<(double? Average, int Lost, int Sent)> HttpLatencyAsync(string url, int count)
        {
            int lost = 0;
            var times = new List<double>();

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var watch = Stopwatch.StartNew();
                    using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                    using var response = await Http.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    watch.Stop();
                    times.Add(watch.Elapsed.TotalMilliseconds);
                }
                catch
                {
                    lost++;
                }
            }

            // Bester Wert, weil die erste Verbindung den Verbindungsaufbau mitzählt
            return (times.Count > 0 ? (double?)times.Min() : null, lost, count);
        }

        private PingResult ToPingResult(string name, (double? Average, int Lost, int Sent) result)
        {
            if (result.Average == null)
            {
                return new PingResult { Name = name, Text = "keine Antwort", Percent = 0, BarBrush = BrushRed };
            }

            double ms = result.Average.Value;
            string text = $"{ms:F0} ms" + (result.Lost > 0 ? $"  ·  {result.Lost} von {result.Sent} verloren" : string.Empty);
            var brush = ms < 40 ? BrushGpu : ms < 100 ? BrushAmber : BrushRed;
            return new PingResult { Name = name, Text = text, Percent = Math.Min(100, ms / 2.0), BarBrush = brush };
        }

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            BtnPing.IsEnabled = false;
            TxtPingInfo.Text = "Teste die Verbindung ...";

            var results = new List<PingResult>();
            try
            {
                string? gateway = GetGatewayAddress();
                if (gateway != null)
                    results.Add(ToPingResult("Router", await PingHostAsync(gateway, 4)));

                results.Add(ToPingResult("Cloudflare (1.1.1.1)", await PingHostAsync("1.1.1.1", 4)));
                results.Add(ToPingResult("Google DNS (8.8.8.8)", await PingHostAsync("8.8.8.8", 4)));

                if (settings.OnlineFeatures)
                {
                    results.Add(ToPingResult("Steam", await HttpLatencyAsync("https://store.steampowered.com/", 3)));
                    results.Add(ToPingResult("Epic Games", await HttpLatencyAsync("https://store.epicgames.com/", 3)));
                    results.Add(ToPingResult("Xbox", await HttpLatencyAsync("https://www.xbox.com/", 3)));
                }

                PingList.ItemsSource = results;
                TxtPingInfo.Text = $"Getestet um {DateTime.Now:HH:mm} Uhr. Unter 40 ms ist sehr gut, über 100 ms spürbar langsam.";
            }
            catch (Exception ex)
            {
                TxtPingInfo.Text = "Test fehlgeschlagen: " + ex.Message;
            }
            finally
            {
                BtnPing.IsEnabled = true;
            }
        }

        // ───────────────────────────── Programme beim Spielstart schließen ─────────────────────────────

        private void BtnPickCloseApps_Click(object sender, RoutedEventArgs e)
        {
            var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle.Length > 0 &&
                        process.Id != Environment.ProcessId && !NeverClose.Contains(process.ProcessName))
                        names.Add(process.ProcessName);
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
            foreach (string saved in settings.CloseApps) names.Add(saved);

            var chosen = PickFromList("Programme beim Spielstart schließen",
                "Diese Programme werden beim Spielstart sauber geschlossen (wie mit dem Schließen-Knopf) und nach dem Spiel wieder geöffnet. Nicht gespeicherte Arbeit kann dabei nachgefragt werden.",
                names.ToList(), settings.CloseApps.ToHashSet(StringComparer.OrdinalIgnoreCase));
            if (chosen == null) return;

            settings.CloseApps = chosen;
            SaveSettings();
        }

        private Task CloseConfiguredAppsAsync(bool force = false)
        {
            if ((!force && !settings.CloseAppsEnabled) || settings.CloseApps.Count == 0) return Task.CompletedTask;

            closedApps.Clear();
            foreach (string name in settings.CloseApps)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (process.MainWindowHandle == IntPtr.Zero) continue;

                        string? path = NativeFeatures.GetProcessPath(process.Id);
                        if (process.CloseMainWindow() && !string.IsNullOrEmpty(path) && !closedApps.Contains(path))
                            closedApps.Add(path);
                    }
                    catch { }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            if (closedApps.Count > 0)
                ShowToast("🧹", "Programme geschlossen", string.Join(", ", closedApps.Select(p => System.IO.Path.GetFileNameWithoutExtension(p)).Take(4)), 5);

            return Task.CompletedTask;
        }

        private void RestoreClosedApps()
        {
            if (closedApps.Count == 0) return;

            foreach (string path in closedApps.ToList())
            {
                try
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    var running = Process.GetProcessesByName(name);
                    bool alreadyRunning = running.Length > 0;
                    foreach (var process in running) process.Dispose();
                    if (alreadyRunning) continue;

                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch { }
            }

            closedApps.Clear();
            ShowToast("🧹", "Programme wieder geöffnet", string.Empty, 4);
        }

        // ───────────────────────────── Grafiktreiber ─────────────────────────────

        private void UpdateDriverInfo()
        {
            var gpu = PickPrimaryGpu(ReadGpuAdapters());
            TxtDriverInfo.Text = gpu == null
                ? "Keine Grafikkarte erkannt"
                : gpu.Name + (gpu.Driver.Length > 0 ? "  ·  Aktueller Treiber: " + gpu.Driver : string.Empty);
        }

        private void BtnOpenDriverPage_Click(object sender, RoutedEventArgs e)
        {
            var gpu = PickPrimaryGpu(ReadGpuAdapters());
            string name = gpu?.Name ?? string.Empty;

            if (Regex.IsMatch(name, "NVIDIA|GeForce|RTX|GTX", RegexOptions.IgnoreCase))
                OpenShell("https://www.nvidia.com/Download/index.aspx");
            else if (Regex.IsMatch(name, "AMD|Radeon", RegexOptions.IgnoreCase))
                OpenShell("https://www.amd.com/en/support");
            else if (Regex.IsMatch(name, "Intel|Arc", RegexOptions.IgnoreCase))
                OpenShell("https://www.intel.com/content/www/us/en/download-center/home.html");
            else
                OpenShell("ms-settings:windowsupdate");
        }

        // ───────────────────────────── Autostart-Programme verwalten ─────────────────────────────

        private const string AutostartRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutostartApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        private void AddAutostartEntries(RegistryKey hive, bool editable)
        {
            try
            {
                using var run = hive.OpenSubKey(AutostartRunKey);
                using var approved = hive.OpenSubKey(AutostartApprovedKey);
                if (run == null) return;

                foreach (string name in run.GetValueNames())
                {
                    string entryName = name;
                    string command = run.GetValue(name) as string ?? string.Empty;

                    bool enabled = true;
                    if (approved?.GetValue(name) is byte[] flags && flags.Length > 0) enabled = (flags[0] & 1) == 0;

                    var texts = new StackPanel();
                    var box = new System.Windows.Controls.CheckBox
                    {
                        Content = entryName,
                        Foreground = System.Windows.Media.Brushes.White,
                        IsChecked = enabled,
                        IsEnabled = editable,
                        ToolTip = editable ? command : Loc.T("Gilt für alle Benutzer. Ändern erfordert Administratorrechte."),
                    };
                    box.Checked += (s, e) => SetAutostartEnabled(entryName, true);
                    box.Unchecked += (s, e) => SetAutostartEnabled(entryName, false);
                    texts.Children.Add(box);
                    texts.Children.Add(new TextBlock
                    {
                        Text = command,
                        Foreground = BrushSubtle,
                        FontSize = 11,
                        Margin = new Thickness(22, 0, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    texts.Margin = new Thickness(0, 0, 0, 10);
                    AutostartPanel.Children.Add(texts);
                }
            }
            catch { }
        }

        private void SetAutostartEnabled(string name, bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AutostartApprovedKey);
                var data = new byte[12];
                data[0] = (byte)(enabled ? 0x02 : 0x03);
                key.SetValue(name, data, RegistryValueKind.Binary);
            }
            catch (Exception ex)
            {
                Msg($"Autostart konnte nicht geändert werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshAutostart()
        {
            if (AutostartPanel == null) return;

            AutostartPanel.Children.Clear();
            AddAutostartEntries(Registry.CurrentUser, true);
            AddAutostartEntries(Registry.LocalMachine, false);

            if (AutostartPanel.Children.Count == 0)
                AutostartPanel.Children.Add(new TextBlock { Text = Loc.T("Keine Autostart-Programme gefunden."), Foreground = BrushSubtle });
        }

        // ───────────────────────────── Temperaturen und Lüfter (LibreHardwareMonitor) ─────────────────────────────

        private LibreHardwareMonitor.Hardware.Computer? hardwareMonitor;
        private readonly DispatcherTimer sensorTimer = new();
        private bool sensorsBusy;

        private sealed class SensorRow
        {
            public string Group { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private void InitSensors()
        {
            sensorTimer.Interval = TimeSpan.FromSeconds(2);
            sensorTimer.Tick += async (s, e) =>
            {
                if (!settings.SensorsEnabled || sensorsBusy) return;
                if (ViewSystem.Visibility != Visibility.Visible || !IsVisible || WindowState == WindowState.Minimized) return;
                await RefreshSensorsAsync();
            };
            sensorTimer.Start();
        }

        private static string SensorGroupName(LibreHardwareMonitor.Hardware.HardwareType type)
        {
            switch (type)
            {
                case LibreHardwareMonitor.Hardware.HardwareType.Cpu:
                    return "Prozessor";
                case LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia:
                case LibreHardwareMonitor.Hardware.HardwareType.GpuAmd:
                case LibreHardwareMonitor.Hardware.HardwareType.GpuIntel:
                    return "Grafikkarte";
                default:
                    return "Mainboard";
            }
        }

        private List<SensorRow> ReadSensors()
        {
            if (hardwareMonitor == null)
            {
                var computer = new LibreHardwareMonitor.Hardware.Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMotherboardEnabled = true
                };
                computer.Open();
                hardwareMonitor = computer;
            }

            var rows = new List<SensorRow>();
            var snap = new SensorSnapshot();

            void Collect(LibreHardwareMonitor.Hardware.IHardware hardware, string group)
            {
                hardware.Update();
                CaptureSnapshot(snap, hardware);
                int taken = 0;

                foreach (var sensor in hardware.Sensors)
                {
                    if (!sensor.Value.HasValue) continue;

                    bool temperature = sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature;
                    bool fan = sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan;
                    if (!temperature && !fan) continue;
                    if (fan && sensor.Value.Value < 1) continue;
                    if (taken >= 8) break;

                    rows.Add(new SensorRow
                    {
                        Group = group,
                        Name = sensor.Name,
                        Value = temperature ? $"{sensor.Value.Value:F0} °C" : $"{sensor.Value.Value:F0} U/min"
                    });
                    taken++;
                }

                foreach (var sub in hardware.SubHardware) Collect(sub, group);
            }

            foreach (var hardware in hardwareMonitor.Hardware)
                Collect(hardware, SensorGroupName(hardware.HardwareType));

            sensorSnap = snap;
            return rows;
        }

        private async Task RefreshSensorsAsync()
        {
            sensorsBusy = true;
            try
            {
                var rows = await Task.Run(ReadSensors);
                RenderSensors(rows);
                UpdateMetricDetails();
            }
            catch (Exception ex)
            {
                LogError("Sensoren", ex);
                TxtSensorStatus.Text = Loc.T("Die Sensoren konnten nicht gelesen werden. Details stehen im Fehlerprotokoll.");
                SensorPanel.Children.Clear();
                settings.SensorsEnabled = false;
                isLoadingSettings = true;
                try { ChkSensors.IsChecked = false; }
                finally { isLoadingSettings = false; }
                SaveSettings();
            }
            finally
            {
                sensorsBusy = false;
            }
        }

        private void RenderSensors(List<SensorRow> rows)
        {
            SensorPanel.Children.Clear();

            if (rows.Count == 0)
            {
                TxtSensorStatus.Text = Loc.T("Keine Sensoren gefunden. Starte den Launcher als Administrator, damit mehr Werte verfügbar sind.");
                return;
            }

            TxtSensorStatus.Text = Loc.T("Aktualisiert alle 2 Sekunden.");

            foreach (var group in rows.GroupBy(r => r.Group))
            {
                SensorPanel.Children.Add(new TextBlock
                {
                    Text = Loc.T(group.Key),
                    Foreground = BrushSubtle,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 10, 0, 6)
                });

                foreach (var row in group)
                {
                    var line = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    line.Children.Add(new TextBlock
                    {
                        Text = row.Name,
                        Foreground = System.Windows.Media.Brushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    var value = new TextBlock { Text = row.Value, FontWeight = FontWeights.SemiBold, Margin = new Thickness(16, 0, 0, 0) };
                    value.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                    Grid.SetColumn(value, 1);
                    line.Children.Add(value);

                    SensorPanel.Children.Add(line);
                }
            }
        }

        private void ApplySensorSettings()
        {
            if (SensorCard == null) return;

            SensorCard.Visibility = settings.SensorsEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (settings.SensorsEnabled && ViewSystem.Visibility == Visibility.Visible && !sensorsBusy)
                _ = RefreshSensorsAsync();
        }

        private void CloseSensors()
        {
            try
            {
                hardwareMonitor?.Close();
                hardwareMonitor = null;
            }
            catch { }
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void BtnRestartAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (IsRunningAsAdmin())
            {
                Msg("Der Launcher läuft bereits mit Administratorrechten.", "Administrator");
                return;
            }

            try
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;

                Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = true, Verb = "runas" });
                ExitApplication();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Die Windows-Abfrage wurde abgebrochen: nichts weiter tun
            }
            catch (Exception ex)
            {
                Msg($"Konnte nicht geöffnet werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────────────────────────── Overlay-Aussehen ─────────────────────────────

        private void ApplyOverlayLook()
        {
            if (overlayWindow == null) return;

            overlayWindow.Opacity = Math.Clamp(settings.OverlayOpacity, 30, 100) / 100.0;
            double scale = Math.Clamp(settings.OverlayScale, 70, 200) / 100.0;
            if (overlayWindow.Content is Border card) card.LayoutTransform = new ScaleTransform(scale, scale);
        }

        // ───────────────────────────── Arbeitsspeicher ─────────────────────────────

        private void UpdateMemoryText()
        {
            if (TxtMemory == null) return;

            try
            {
                using var self = Process.GetCurrentProcess();
                TxtMemory.Text = Loc.T("Aktuell belegt:") + $" {self.WorkingSet64 / 1048576} MB";
            }
            catch { }
        }

        private void BtnFreeMemory_Click(object sender, RoutedEventArgs e)
        {
            imageCache.Clear();
            gifCache.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            UpdateMemoryText();
        }

        // ───────────────────────────── Bildschirme (jeder angeschlossene Monitor) ─────────────────────────────

        private sealed record DisplayInfo(int Width, int Height, int Hertz, bool Primary, bool Portrait, int X, int Y)
        {
            public int RawWidth { get; init; }
            public int RawHeight { get; init; }
        }

        /// <summary>Windows meldet bei Bildwiederholraten wie 239,76 Hz nur die abgeschnittene Zahl (239).</summary>
        private static int NormalizeRefresh(int hertz)
            => Array.IndexOf(new[] { 60, 75, 100, 120, 144, 165, 180, 240, 360, 480 }, hertz + 1) >= 0 ? hertz + 1 : hertz;

        private static List<DisplayInfo> ReadDisplays()
        {
            var list = new List<DisplayInfo>();

            try
            {
                for (uint index = 0; index < 32; index++)
                {
                    var device = new NativeExtras.DisplayDevice { cb = Interop.Marshal.SizeOf<NativeExtras.DisplayDevice>() };
                    if (!NativeExtras.EnumDisplayDevices(null, index, ref device, 0)) break;

                    bool attached = (device.StateFlags & 0x1) != 0;     // am Desktop angeschlossen
                    bool mirror = (device.StateFlags & 0x8) != 0;       // Spiegel-Treiber
                    if (!attached || mirror) continue;

                    var mode = new NativeExtras.DisplayMode { dmSize = (short)Interop.Marshal.SizeOf<NativeExtras.DisplayMode>() };
                    if (!NativeExtras.EnumDisplaySettings(device.DeviceName, -1, ref mode)) continue;
                    if (mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0) continue;

                    // Gedrehte Bildschirme (90° oder 270°) melden Breite und Höhe vertauscht
                    bool portrait = mode.dmDisplayOrientation is 1 or 3;
                    int width = portrait ? mode.dmPelsHeight : mode.dmPelsWidth;
                    int height = portrait ? mode.dmPelsWidth : mode.dmPelsHeight;
                    bool primary = (device.StateFlags & 0x4) != 0;

                    list.Add(new DisplayInfo(width, height, NormalizeRefresh(mode.dmDisplayFrequency), primary,
                        portrait, mode.dmPositionX, mode.dmPositionY)
                    {
                        RawWidth = mode.dmPelsWidth,
                        RawHeight = mode.dmPelsHeight
                    });
                }
            }
            catch { }

            return list.OrderByDescending(d => d.Primary).ThenBy(d => d.X).ThenBy(d => d.Y).ToList();
        }

        // ───────────────────────────── Energieplan als Kacheln ─────────────────────────────

        private void UpdatePlanTiles(string activeName)
        {
            void Mark(Border? tile, Border? badge, string planName)
            {
                if (tile == null || badge == null) return;

                bool active = activeName == planName;
                badge.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

                if (active)
                {
                    tile.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                    tile.Background = MakeBrush("#1AFFFFFF");
                }
                else
                {
                    tile.BorderBrush = MakeBrush("#26FFFFFF");
                    tile.Background = MakeBrush("#0FFFFFFF");
                }
            }

            Mark(PlanSaver, BadgePlanSaver, "Energiesparen");
            Mark(PlanBalanced, BadgePlanBalanced, "Ausbalanciert");
            Mark(PlanHigh, BadgePlanHigh, "Höchstleistung");
            Mark(PlanUltimate, BadgePlanUltimate, "Ultimative Leistung");
        }

        private async Task<string?> FindPlanGuidAsync(string namePattern)
        {
            var list = await Task.Run(() => RunHidden("powercfg", "/list"));
            foreach (Match match in Regex.Matches(list.Output, @"([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s+\(([^)]+)\)"))
            {
                if (Regex.IsMatch(match.Groups[2].Value, namePattern, RegexOptions.IgnoreCase))
                    return match.Groups[1].Value;
            }
            return null;
        }

        private async Task ActivatePowerPlanAsync(string guid, bool quiet = false)
        {
            const string ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
            string target = guid;

            // "Ultimative Leistung" ist auf vielen PCs versteckt und muss erst angelegt werden
            if (guid.Equals(ultimate, StringComparison.OrdinalIgnoreCase))
            {
                string? existing = await FindPlanGuidAsync("Ultimat");
                if (existing != null)
                {
                    target = existing;
                }
                else
                {
                    var copy = await Task.Run(() => RunHidden("powercfg", $"-duplicatescheme {ultimate}"));
                    var created = Regex.Match(copy.Output, "[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
                    if (copy.ExitCode == 0 && created.Success) target = created.Value;
                }
            }

            var result = await Task.Run(() => RunHidden("powercfg", $"/setactive {target}"));
            if (result.ExitCode != 0 && !quiet)
                Msg("Dieser Energiesparplan ist auf deinem PC nicht verfügbar.", "Energiesparplan",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

            await RefreshPowerPlanAsync();
        }

        private async void PlanTile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string guid }) await ActivatePowerPlanAsync(guid);
        }

        // ───────────────────────────── Anwendungen: alle Programme mit großen Symbolen ─────────────────────────────

        private const string StartAppsScript = """
            $ErrorActionPreference = 'SilentlyContinue'
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            $items = @(Get-StartApps | Select-Object Name, AppID)
            ConvertTo-Json -InputObject $items -Compress
            """;

        private readonly List<AppEntry> discoveredApps = new();
        private readonly Dictionary<string, ImageSource?> manualAppIcons = new(StringComparer.OrdinalIgnoreCase);
        private bool appsDiscovered;
        private bool appsLoading;
        private string appFilter = "all";
        private string appSearch = string.Empty;

        private static readonly string[] AppCategories = { "Gaming", "Chat", "Browser", "Medien", "Office", "Tools", "Sonstige" };

        private static readonly Regex AppExcluded = new(
            @"uninstall|deinstall|readme|release ?notes|documentation|dokumentation|\bhelp\b|\bhilfe\b|website|webseite|\blicen[cs]e|lizenz|changelog|\bmanual\b|handbuch|\beula\b|feedback|\bsupport\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly (string Category, Regex Pattern)[] AppCategoryRules =
        {
            ("Gaming", new Regex(@"steam|epic games|gog galaxy|battle\.net|ubisoft|\bea app\b|\borigin\b|rockstar|xbox|geforce|nvidia|logitech g|g hub|razer|corsair|icue|afterburner|\bmsi\b|gigabyte|steelseries|adrenalin|amd software|overwolf|playnite|wargaming|riot|valorant|league of legends|minecraft|curseforge|\bgames?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("Chat", new Regex(@"discord|teamspeak|skype|telegram|whatsapp|\bsignal\b|slack|\bteams\b|zoom|\belement\b|viber|messenger|mumble|guilded", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("Browser", new Regex(@"chrome|\bedge\b|firefox|\bopera\b|brave|vivaldi|tor browser|browser", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("Medien", new Regex(@"spotify|\bvlc\b|\bobs\b|streamlabs|audacity|davinci|premiere|after effects|photoshop|lightroom|illustrator|itunes|media player|handbrake|wallpaper|stream deck|blender|gimp|\bpaint\b|krita|foobar|voicemeeter|reaper|fl studio|ableton|capcut|clipchamp|elgato|kamera|camera|movies|filme|fotos|photos|musik|music|groove|tidal|deezer|youtube|netflix|twitch", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("Office", new Regex(@"\bword\b|excel|powerpoint|outlook|onenote|\baccess\b|publisher|libreoffice|acrobat|\breader\b|notion|obsidian|\boffice\b|openoffice|onlyoffice|\bwps\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("Tools", new Regex(@"notepad|visual studio|\bcode\b|terminal|powershell|7-zip|winrar|everything|powertoys|\bgit\b|python|node\.js|putty|filezilla|onedrive|explorer|task.?manager|\bcmd\b|regedit|systemsteuerung|control panel|rechner|calculator|snipping|ausschneiden|defender|sicherheit|winscp|docker|postman|unity|unreal|android studio|intellij|rider|pycharm|sublime|tailscale|teamviewer|anydesk|cpu-z|gpu-z|hwinfo|crystaldisk|ccleaner|bitwarden|keepass|1password|veracrypt|rufus|etcher|wireshark|vmware|virtualbox|hyper-v|dxdiag|editor|einstellungen|settings", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        };

        private static string GuessAppCategory(string name)
        {
            foreach (var (category, pattern) in AppCategoryRules)
            {
                if (pattern.IsMatch(name)) return category;
            }
            return "Sonstige";
        }

        /// <summary>Löst Pfade aus Get-StartApps auf (zum Beispiel {GUID}\Programm\app.exe) und gibt sie zurück, wenn die Datei existiert.</summary>
        private static string ResolveStartAppPath(string appId)
        {
            try
            {
                if (System.IO.Path.IsPathRooted(appId) && appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return File.Exists(appId) ? appId : string.Empty;

                var match = Regex.Match(appId, @"^\{([0-9A-Fa-f\-]{36})\}\\(.+)$");
                if (!match.Success) return string.Empty;

                string? root = match.Groups[1].Value.ToUpperInvariant() switch
                {
                    "6D809377-6AF0-444B-8957-A3773F02200E" or "905E63B6-C1BF-494E-B29C-65B732D3D21A"
                        => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E" => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "1AC14E77-02E7-4E5D-B744-2EB1AE5198B7" => Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27" => Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
                    "F38BF404-1D43-42F2-9305-67DE0B28FC23" => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "F1B32785-6FBA-4FCF-9D55-7B8E7F157091" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "3EB685DB-65F9-4CF6-A03A-E3EF65729F3D" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "62AB5D82-FDC1-4DC3-A9DD-070D1D495D97" => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    _ => null
                };
                if (root == null) return string.Empty;

                string full = System.IO.Path.Combine(root, match.Groups[2].Value);
                return full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Liest alle Programme aus dem Startmenü (Win32 und Store-Apps).</summary>
        private List<AppEntry> DiscoverApps()
        {
            var apps = new List<AppEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var result = RunPowerShell(StartAppsScript, 30000);
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                try
                {
                    using var doc = JsonDocument.Parse(result.Output);
                    var root = doc.RootElement;
                    var items = root.ValueKind == JsonValueKind.Array
                        ? root.EnumerateArray().ToList()
                        : new List<JsonElement> { root };

                    foreach (var item in items)
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;

                        string name = GetJsonString(item, "Name").Trim();
                        string appId = GetJsonString(item, "AppID").Trim();
                        if (name.Length == 0 || appId.Length == 0) continue;
                        if (AppExcluded.IsMatch(name)) continue;
                        if (appId.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) continue;   // Spiele-Verknüpfungen
                        if (name.Equals("DFP Pro Launcher", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!seen.Add(name)) continue;

                        apps.Add(new AppEntry
                        {
                            Name = name,
                            AppId = appId,
                            ExePath = ResolveStartAppPath(appId),
                            Category = GuessAppCategory(name)
                        });
                    }
                }
                catch { }
            }

            if (apps.Count == 0)
            {
                foreach (var fallback in LoadFallbackApps())
                {
                    fallback.Category = GuessAppCategory(fallback.Name);
                    apps.Add(fallback);
                }
            }

            return apps;
        }

        /// <summary>Holt das große Symbol einer App über die Windows-Shell (bis 256 Pixel).</summary>
        private static ImageSource? ShellImage(string parsingName, int size)
        {
            if (string.IsNullOrWhiteSpace(parsingName)) return null;

            IntPtr bitmap = IntPtr.Zero;
            ShellNative.IShellItemImageFactory? factory = null;

            try
            {
                var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                ShellNative.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory);

                int hr = factory.GetImage(new ShellNative.NativeSize { cx = size, cy = size }, 0x4 /* nur Symbol */, out bitmap);
                if (hr != 0 || bitmap == IntPtr.Zero) return null;

                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmap != IntPtr.Zero) ShellNative.DeleteObject(bitmap);
                if (factory != null)
                {
                    try { Interop.Marshal.ReleaseComObject(factory); }
                    catch { }
                }
            }
        }

        private static void LoadAppIcons(List<AppEntry> apps, int size)
        {
            // Die Shell-Symbole brauchen einen Thread mit STA-Modus
            var thread = new System.Threading.Thread(() =>
            {
                foreach (var app in apps)
                {
                    if (app.Icon != null) continue;

                    string parsing = app.AppId.Length > 0 ? "shell:AppsFolder\\" + app.AppId : app.ExePath;
                    app.Icon = ShellImage(parsing, size)
                               ?? (app.ExePath.Length > 0 ? ExtractIcon(app.ExePath) : null);
                }
            })
            { IsBackground = true };

            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join(120000);
        }

        private bool IsGameShortcut(AppEntry app)
        {
            foreach (var game in allGames)
            {
                if (string.Equals(game.Name, app.Name, StringComparison.OrdinalIgnoreCase)) return true;
                if (app.ExePath.Length > 0 && game.InstallDir.Length > 0
                    && app.ExePath.StartsWith(game.InstallDir, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private List<AppEntry> BuildAppList()
        {
            var list = new List<AppEntry>();

            foreach (var app in discoveredApps)
            {
                if (IsGameShortcut(app)) continue;
                app.IsFavorite = settings.AppFavorites.Contains(app.Name, StringComparer.OrdinalIgnoreCase);
                list.Add(app);
            }

            foreach (var manual in settings.ManualApps)
            {
                if (!File.Exists(manual.ExePath)) continue;

                if (!manualAppIcons.TryGetValue(manual.ExePath, out var icon))
                {
                    icon = ShellImage(manual.ExePath, 96) ?? ExtractIcon(manual.ExePath);
                    manualAppIcons[manual.ExePath] = icon;
                }

                list.RemoveAll(a => string.Equals(a.ExePath, manual.ExePath, StringComparison.OrdinalIgnoreCase));
                list.Add(new AppEntry
                {
                    Name = manual.Name,
                    ExePath = manual.ExePath,
                    IsManual = true,
                    Icon = icon,
                    Category = GuessAppCategory(manual.Name),
                    IsFavorite = settings.AppFavorites.Contains(manual.Name, StringComparer.OrdinalIgnoreCase)
                });
            }

            return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private void RefreshApps()
        {
            if (AppsContainer == null) return;

            RenderApps();
            if (!appsDiscovered && !appsLoading) _ = DiscoverAppsAsync(false);
        }

        private async Task DiscoverAppsAsync(bool force)
        {
            if (appsLoading || (appsDiscovered && !force)) return;

            appsLoading = true;
            RenderApps();

            try
            {
                var found = await Task.Run(() =>
                {
                    var apps = DiscoverApps();
                    LoadAppIcons(apps, 96);
                    return apps;
                });

                discoveredApps.Clear();
                discoveredApps.AddRange(found);
                appsDiscovered = true;
            }
            catch (Exception ex)
            {
                LogError("Anwendungen", ex);
            }
            finally
            {
                appsLoading = false;
            }

            RenderApps();
            MigrateQuickButtons();
            RenderSidebarExtras();
        }

        private void BtnRefreshApps_Click(object sender, RoutedEventArgs e)
        {
            manualAppIcons.Clear();
            _ = DiscoverAppsAsync(true);
        }

        private void TxtAppSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (AppsContainer == null) return;

            appSearch = TxtAppSearch.Text.Trim();
            AppSearchHint.Visibility = TxtAppSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderAppTiles(cachedApps);
        }

        private void RenderApps()
        {
            if (AppsContainer == null) return;

            var all = BuildAppList();
            cachedApps = all;

            if (appFilter != "all" && appFilter != "fav" && !all.Any(a => a.Category == appFilter)) appFilter = "all";

            BuildAppFilterChips(all);
            RenderAppTiles(all);
        }

        private void BuildAppFilterChips(List<AppEntry> all)
        {
            AppsFilterPanel.Children.Clear();
            var chipStyle = TryFindResource("ChipStyle") as Style;

            void AddChip(string key, string label, int count)
            {
                var chip = new System.Windows.Controls.RadioButton
                {
                    Content = $"{label}  {count}",
                    GroupName = "AppsCategory",
                    Tag = key,
                    IsChecked = appFilter == key
                };
                if (chipStyle != null) chip.Style = chipStyle;
                chip.Checked += (s, e) =>
                {
                    appFilter = key;
                    RenderAppTiles(cachedApps);
                };
                AppsFilterPanel.Children.Add(chip);
            }

            AddChip("all", Loc.T("Alle"), all.Count);
            AddChip("fav", Loc.T("Favoriten"), all.Count(a => a.IsFavorite));
            foreach (string category in AppCategories)
            {
                int count = all.Count(a => a.Category == category);
                if (count > 0) AddChip(category, Loc.T(category), count);
            }
        }

        private void RenderAppTiles(List<AppEntry> all)
        {
            AppsContainer.Children.Clear();

            IEnumerable<AppEntry> query = all;
            if (appFilter == "fav") query = query.Where(a => a.IsFavorite);
            else if (appFilter != "all") query = query.Where(a => a.Category == appFilter);
            if (appSearch.Length > 0) query = query.Where(a => a.Name.Contains(appSearch, StringComparison.CurrentCultureIgnoreCase));

            var shown = query.ToList();
            foreach (var app in shown) AppsContainer.Children.Add(CreateAppTile(app));

            if (appsLoading && all.Count == 0)
            {
                TxtAppsEmpty.Text = Loc.T("Anwendungen werden gesucht ...");
                TxtAppsEmpty.Visibility = Visibility.Visible;
            }
            else if (shown.Count == 0)
            {
                TxtAppsEmpty.Text = Loc.T("Keine Apps erkannt. Füge deine Programme mit „App hinzufügen“ selbst hinzu.");
                TxtAppsEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                TxtAppsEmpty.Visibility = Visibility.Collapsed;
            }

            TxtAppsSubtitle.Text = Loc.T($"{all.Count} Apps gefunden · Klick zum Starten, Rechtsklick für mehr");
        }

        private Border CreateAppTile(AppEntry app)
        {
            var content = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            if (app.Icon != null)
            {
                var image = new System.Windows.Controls.Image
                {
                    Source = app.Icon,
                    Width = 56,
                    Height = 56,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                content.Children.Add(image);
            }
            else
            {
                content.Children.Add(new TextBlock
                {
                    Text = "🧩",
                    FontSize = 40,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = app.Name,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            });
            content.Children.Add(new TextBlock
            {
                Text = Loc.T(app.IsManual ? "Eigene App" : app.Category),
                Foreground = BrushSubtle,
                FontSize = 11,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });

            var layout = new Grid();
            layout.Children.Add(content);
            if (app.IsFavorite)
            {
                layout.Children.Add(new TextBlock
                {
                    Text = "★",
                    Foreground = BrushGold,
                    FontSize = 14,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top
                });
            }

            var menu = CreateMenu();
            AddMenuItem(menu, "▶  Starten", () => LaunchApp(app));
            AddMenuItem(menu, app.IsFavorite ? "☆  Aus Favoriten entfernen" : "★  Zu Favoriten hinzufügen", () => ToggleAppFavorite(app));
            if (app.ExePath.Length > 0 && File.Exists(app.ExePath))
            {
                string? folder = System.IO.Path.GetDirectoryName(app.ExePath);
                if (!string.IsNullOrEmpty(folder)) AddMenuItem(menu, "📂  Dateispeicherort öffnen", () => OpenShell(folder));
            }
            if (app.IsManual) AddMenuItem(menu, "🗑  Aus Liste entfernen", () => RemoveManualApp(app));

            var tile = new Border
            {
                Width = 150,
                Height = 150,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(12),
                Background = BrushCardBg,
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Cursor = System.Windows.Input.Cursors.Hand,
                ContextMenu = menu,
                Child = layout
            };
            tile.MouseEnter += (s, e) => tile.Background = BrushTileHover;
            tile.MouseLeave += (s, e) => tile.Background = BrushCardBg;
            tile.MouseLeftButtonUp += (s, e) => LaunchApp(app);
            return tile;
        }

        private void ToggleAppFavorite(AppEntry app)
        {
            if (app.IsFavorite) settings.AppFavorites.RemoveAll(n => string.Equals(n, app.Name, StringComparison.OrdinalIgnoreCase));
            else settings.AppFavorites.Add(app.Name);

            SaveSettings();
            RenderApps();
        }

        // ═════════════════════════════ Aufräumen ═════════════════════════════

        private sealed class CleanupItem
        {
            public string Icon = string.Empty;
            public string Title = string.Empty;
            public string Description = string.Empty;
            public bool ShowCount;
            public bool NoSize;
            public Func<(long Bytes, int Count)> Measure = () => (0L, 0);
            public Func<Task<long>> Clear = () => Task.FromResult(0L);
            public long Bytes;
            public int Count;
            public TextBlock? SizeText;
            public System.Windows.Controls.Button? Button;
        }

        private readonly List<CleanupItem> cleanupItems = new();
        private bool cleanupBusy;
        private long systemCrashBytes;

        private static readonly EnumerationOptions CleanupEnumeration = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        private static string FormatCleanupSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";

            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F0} KB";

            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F0} MB";

            return $"{mb / 1024.0:F1} GB";
        }

        private static (long Bytes, int Count) MeasureFolder(string path, TimeSpan? olderThan = null)
        {
            long bytes = 0;
            int count = 0;

            try
            {
                if (!Directory.Exists(path)) return (0L, 0);

                foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", CleanupEnumeration))
                {
                    try
                    {
                        if (olderThan.HasValue && DateTime.UtcNow - file.LastWriteTimeUtc < olderThan.Value) continue;
                        bytes += file.Length;
                        count++;
                    }
                    catch { }
                }
            }
            catch { }

            return (bytes, count);
        }

        private static long ClearFolder(string path, TimeSpan? olderThan = null)
        {
            long freed = 0;

            try
            {
                if (!Directory.Exists(path)) return 0;
                var root = new DirectoryInfo(path);

                foreach (var file in root.EnumerateFiles("*", CleanupEnumeration))
                {
                    try
                    {
                        if (olderThan.HasValue && DateTime.UtcNow - file.LastWriteTimeUtc < olderThan.Value) continue;
                        long length = file.Length;
                        file.Delete();
                        freed += length;
                    }
                    catch { }   // Datei wird gerade benutzt: bleibt einfach liegen
                }

                foreach (var dir in root.EnumerateDirectories("*", CleanupEnumeration).OrderByDescending(d => d.FullName.Length).ToList())
                {
                    try
                    {
                        if (!dir.EnumerateFileSystemInfos().Any()) dir.Delete();
                    }
                    catch { }
                }
            }
            catch { }

            return freed;
        }

        private static (long Bytes, int Count) SumMeasures(IEnumerable<(long Bytes, int Count)> parts)
        {
            long bytes = 0;
            int count = 0;
            foreach (var part in parts)
            {
                bytes += part.Bytes;
                count += part.Count;
            }
            return (bytes, count);
        }

        private static string[] ShaderFolders()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            return new[]
            {
                System.IO.Path.Combine(local, "D3DSCache"),
                System.IO.Path.Combine(local, "NVIDIA", "DXCache"),
                System.IO.Path.Combine(local, "NVIDIA", "GLCache"),
                System.IO.Path.Combine(data, "NVIDIA Corporation", "NV_Cache"),
                System.IO.Path.Combine(local, "AMD", "DxCache"),
                System.IO.Path.Combine(local, "AMD", "DxcCache"),
                System.IO.Path.Combine(local, "AMD", "GLCache"),
                System.IO.Path.Combine(local, "AMD", "VkCache"),
                System.IO.Path.Combine(local, "Intel", "ShaderCache")
            };
        }

        private static string[] UserCrashFolders()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return new[]
            {
                System.IO.Path.Combine(local, "CrashDumps"),
                System.IO.Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"),
                System.IO.Path.Combine(local, "Microsoft", "Windows", "WER", "ReportQueue"),
                System.IO.Path.Combine(local, "Microsoft", "Windows", "WER", "Temp")
            };
        }

        private static string[] SystemCrashFolders()
        {
            string data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            return new[]
            {
                System.IO.Path.Combine(data, "Microsoft", "Windows", "WER", "ReportArchive"),
                System.IO.Path.Combine(data, "Microsoft", "Windows", "WER", "ReportQueue"),
                System.IO.Path.Combine(data, "Microsoft", "Windows", "WER", "Temp"),
                System.IO.Path.Combine(windows, "Minidump")
            };
        }

        private static long MemoryDumpSize()
        {
            try
            {
                var file = new FileInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "MEMORY.DMP"));
                return file.Exists ? file.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static (long Bytes, int Count) QueryRecycleBin()
        {
            try
            {
                var info = new NativeMethods.ShQueryRbInfo { cbSize = Interop.Marshal.SizeOf<NativeMethods.ShQueryRbInfo>() };
                int result = NativeMethods.SHQueryRecycleBin(null, ref info);
                return result == 0 ? (info.i64Size, (int)info.i64NumItems) : (0L, 0);
            }
            catch
            {
                return (0L, 0);
            }
        }

        private void BuildCleanupItems()
        {
            if (cleanupItems.Count > 0) return;

            string temp = System.IO.Path.GetTempPath();
            string windowsTemp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            var day = TimeSpan.FromHours(24);

            cleanupItems.Add(new CleanupItem
            {
                Icon = "🗂",
                Title = "Temporäre Dateien",
                Description = "Dateien in den Temp-Ordnern, die älter als 24 Stunden sind",
                Measure = () => SumMeasures(new[] { MeasureFolder(temp, day) }
                    .Concat(IsRunningAsAdmin() ? new[] { MeasureFolder(windowsTemp, day) } : Array.Empty<(long Bytes, int Count)>())),
                Clear = () => Task.Run(() => ClearFolder(temp, day) + (IsRunningAsAdmin() ? ClearFolder(windowsTemp, day) : 0))
            });

            cleanupItems.Add(new CleanupItem
            {
                Icon = "🎮",
                Title = "Shader-Cache",
                Description = "DirectX, NVIDIA, AMD, Intel – sinnvoll nach Treiber-Updates (der erste Spielstart danach dauert kurz länger)",
                Measure = () => SumMeasures(ShaderFolders().Select(f => MeasureFolder(f))),
                Clear = () => Task.Run(() => ShaderFolders().Sum(f => ClearFolder(f)))
            });

            cleanupItems.Add(new CleanupItem
            {
                Icon = "🗑",
                Title = "Papierkorb",
                Description = "Gelöschte Dateien endgültig entfernen",
                ShowCount = true,
                Measure = QueryRecycleBin,
                Clear = () => Task.Run(() =>
                {
                    long before = QueryRecycleBin().Bytes;
                    NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, 7);   // ohne Rückfrage, Fortschritt und Ton
                    return before;
                })
            });

            cleanupItems.Add(new CleanupItem
            {
                Icon = "⚠",
                Title = "Absturzberichte",
                Description = "Speicherabbilder und Fehlerberichte abgestürzter Programme",
                Measure = () =>
                {
                    var user = SumMeasures(UserCrashFolders().Select(f => MeasureFolder(f)));
                    long system = SystemCrashFolders().Sum(f => MeasureFolder(f).Bytes) + MemoryDumpSize();
                    systemCrashBytes = system;
                    return IsRunningAsAdmin() ? (user.Bytes + system, user.Count) : user;
                },
                Clear = () => Task.Run(() =>
                {
                    long freed = UserCrashFolders().Sum(f => ClearFolder(f));
                    if (IsRunningAsAdmin())
                    {
                        freed += SystemCrashFolders().Sum(f => ClearFolder(f));
                        try
                        {
                            string dump = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "MEMORY.DMP");
                            long length = MemoryDumpSize();
                            File.Delete(dump);
                            freed += length;
                        }
                        catch { }
                    }
                    return freed;
                })
            });

            cleanupItems.Add(new CleanupItem
            {
                Icon = "🖼",
                Title = "Cover-Cache",
                Description = "Heruntergeladene Cover – werden danach automatisch neu geladen",
                Measure = () => MeasureFolder(CoversDir),
                Clear = async () =>
                {
                    long freed = await Task.Run(() =>
                    {
                        long before = MeasureFolder(CoversDir).Bytes;
                        try { if (Directory.Exists(CoversDir)) Directory.Delete(CoversDir, true); }
                        catch { }
                        return Math.Max(0, before - MeasureFolder(CoversDir).Bytes);
                    });

                    lock (settings.SteamIds)
                    {
                        foreach (var key in settings.SteamIds.Where(p => p.Value <= 0).Select(p => p.Key).ToList())
                            settings.SteamIds.Remove(key);
                    }

                    imageCache.Clear();
                    foreach (var game in allGames) game.CoverPath = string.Empty;
                    ApplyFilter();
                    _ = DownloadCoversAsync(allGames);
                    return freed;
                }
            });

            cleanupItems.Add(new CleanupItem
            {
                Icon = "📄",
                Title = "Launcher-Protokolle",
                Description = "Fehlerprotokolle dieses Launchers",
                Measure = () => MeasureFolder(LogDir),
                Clear = () => Task.Run(() => ClearFolder(LogDir))
            });

            cleanupItems.Add(new CleanupItem
            {
                Icon = "🌐",
                Title = "DNS-Cache",
                Description = "Hilft bei Verbindungsproblemen zu Spiele-Servern und Launchern",
                NoSize = true,
                Measure = () => (0L, 0),
                Clear = () => Task.Run(() =>
                {
                    RunHidden("ipconfig", "/flushdns");
                    return 0L;
                })
            });
        }

        private void RenderCleanup()
        {
            if (CleanupPanel == null) return;

            BuildCleanupItems();
            CleanupPanel.Children.Clear();

            bool first = true;
            foreach (var entry in cleanupItems)
            {
                var item = entry;

                var badge = new Border
                {
                    Width = 38,
                    Height = 38,
                    CornerRadius = new CornerRadius(10),
                    Background = MakeBrush("#1AFFFFFF"),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = item.Icon,
                        FontSize = 17,
                        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                        Foreground = System.Windows.Media.Brushes.White,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    }
                };

                var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) };
                texts.Children.Add(new TextBlock
                {
                    Text = Loc.T(item.Title),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold
                });
                texts.Children.Add(new TextBlock
                {
                    Text = Loc.T(item.Description),
                    Foreground = BrushSubtle,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });

                item.SizeText = new TextBlock
                {
                    Text = item.NoSize ? string.Empty : "…",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    MinWidth = 96,
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };

                item.Button = new System.Windows.Controls.Button
                {
                    Content = Loc.T("Leeren"),
                    MinWidth = 78,
                    Padding = new Thickness(14, 7, 14, 7),
                    Margin = new Thickness(16, 0, 0, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    IsEnabled = item.NoSize
                };
                item.Button.Click += async (s, e) => await ClearCleanupItemAsync(item);

                var layout = new Grid();
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                layout.Children.Add(badge);
                Grid.SetColumn(texts, 1);
                layout.Children.Add(texts);
                Grid.SetColumn(item.SizeText, 2);
                layout.Children.Add(item.SizeText);
                Grid.SetColumn(item.Button, 3);
                layout.Children.Add(item.Button);

                CleanupPanel.Children.Add(new Border
                {
                    BorderBrush = MakeBrush("#14FFFFFF"),
                    BorderThickness = first ? new Thickness(0) : new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(0, 10, 0, 10),
                    Child = layout
                });
                first = false;
            }
        }

        private void UpdateCleanupRow(CleanupItem item)
        {
            if (item.SizeText != null)
            {
                item.SizeText.Text = item.NoSize ? string.Empty
                    : item.ShowCount && item.Count > 0 ? $"{FormatCleanupSize(item.Bytes)} ({item.Count})"
                    : FormatCleanupSize(item.Bytes);
            }

            if (item.Button != null)
                item.Button.IsEnabled = !cleanupBusy && (item.NoSize || item.Bytes > 0 || item.Count > 0);
        }

        private void SetCleanupBusy(bool busy)
        {
            cleanupBusy = busy;
            foreach (var item in cleanupItems) UpdateCleanupRow(item);
            if (BtnCleanupAll != null) BtnCleanupAll.IsEnabled = !busy;
            if (BtnCleanupRefresh != null) BtnCleanupRefresh.IsEnabled = !busy;
        }

        private async Task MeasureCleanupAsync()
        {
            if (CleanupPanel == null) return;
            BuildCleanupItems();

            var tasks = cleanupItems
                .Select(item => Task.Run<(long Bytes, int Count)>(() =>
                {
                    try { return item.Measure(); }
                    catch { return (0L, 0); }
                }))
                .ToList();
            var results = await Task.WhenAll(tasks);

            long total = 0;
            for (int i = 0; i < cleanupItems.Count; i++)
            {
                cleanupItems[i].Bytes = results[i].Bytes;
                cleanupItems[i].Count = results[i].Count;
                total += results[i].Bytes;
                UpdateCleanupRow(cleanupItems[i]);
            }

            TxtCleanupTotal.Text = total > 0
                ? Loc.T($"{FormatCleanupSize(total)} lassen sich freigeben")
                : Loc.T("Alles sauber");

            bool needAdmin = !IsRunningAsAdmin() && systemCrashBytes > 0;
            TxtCleanupNote.Visibility = needAdmin ? Visibility.Visible : Visibility.Collapsed;
            if (needAdmin)
                TxtCleanupNote.Text = Loc.T($"Systemberichte: {FormatCleanupSize(systemCrashBytes)} lassen sich nur mit Administratorrechten löschen.");
        }

        private async Task ClearCleanupItemAsync(CleanupItem item)
        {
            if (cleanupBusy) return;
            SetCleanupBusy(true);

            try
            {
                long freed = await item.Clear();
                ShowToast("🧹", "Aufgeräumt",
                    item.NoSize ? Loc.T("Der DNS-Cache wurde geleert.") : Loc.T($"{FormatCleanupSize(freed)} freigegeben"), 5);
            }
            catch (Exception ex)
            {
                LogError("Aufräumen", ex);
            }
            finally
            {
                SetCleanupBusy(false);
            }

            await MeasureCleanupAsync();
        }

        private async void BtnCleanupAll_Click(object sender, RoutedEventArgs e)
        {
            if (cleanupBusy) return;

            var answer = Msg("Alle Bereiche in der Liste leeren? Der Papierkorb wird dabei endgültig geleert.", "Alles leeren",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            SetCleanupBusy(true);
            long freed = 0;

            try
            {
                foreach (var item in cleanupItems.Where(i => i.NoSize || i.Bytes > 0 || i.Count > 0).ToList())
                    freed += await item.Clear();

                ShowToast("🧹", "Aufgeräumt", Loc.T($"{FormatCleanupSize(freed)} freigegeben"), 6);
            }
            catch (Exception ex)
            {
                LogError("Aufräumen", ex);
            }
            finally
            {
                SetCleanupBusy(false);
            }

            await MeasureCleanupAsync();
        }

        private async void BtnCleanupRefresh_Click(object sender, RoutedEventArgs e) => await MeasureCleanupAsync();

        // ───────────────────────────── Ladekabel-Warnung ─────────────────────────────

        private bool ConfirmBeforeLaunch(GameItem game)
        {
            if (!settings.WarnBattery) return true;

            try
            {
                var power = Forms.SystemInformation.PowerStatus;
                bool hasBattery = (power.BatteryChargeStatus & Forms.BatteryChargeStatus.NoSystemBattery) == 0
                                  && power.BatteryChargeStatus != Forms.BatteryChargeStatus.Unknown;
                if (!hasBattery || power.PowerLineStatus != Forms.PowerLineStatus.Offline) return true;

                int percent = (int)Math.Round(power.BatteryLifePercent * 100);
                var answer = Msg($"Das Ladekabel ist nicht angeschlossen (Akku: {percent} %). Am Akku laufen Spiele langsamer und der Akku ist schnell leer. Trotzdem starten?",
                    "Ladekabel fehlt", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return answer == MessageBoxResult.Yes;
            }
            catch
            {
                return true;
            }
        }

        // ───────────────────────────── Energie-Funktionen ─────────────────────────────

        private void HandleToolTarget(string target)
        {
            if (target.StartsWith("power:", StringComparison.Ordinal)) PowerAction(target.Substring(6));
            else OpenShell(target);
        }

        private void PowerAction(string kind)
        {
            (string Title, string Text, string File, string Args) action = kind switch
            {
                "shutdown" => ("PC herunterfahren", "Soll der PC jetzt heruntergefahren werden? Nicht gespeicherte Arbeit in anderen Programmen geht verloren.", "shutdown.exe", "/s /t 3"),
                "restart" => ("PC neu starten", "Soll der PC jetzt neu gestartet werden? Nicht gespeicherte Arbeit in anderen Programmen geht verloren.", "shutdown.exe", "/r /t 3"),
                "sleep" => ("Energie sparen", "Soll der PC jetzt in den Energiesparmodus wechseln?", "rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"),
                "logoff" => ("Abmelden", "Soll der Benutzer jetzt abgemeldet werden? Nicht gespeicherte Arbeit geht verloren.", "shutdown.exe", "/l"),
                _ => ("PC sperren", string.Empty, "rundll32.exe", "user32.dll,LockWorkStation")
            };

            if (kind != "lock" && settings.PowerConfirm)
            {
                string text = Loc.T(action.Text);
                if (activeSessions.Count > 0 && (kind == "shutdown" || kind == "restart"))
                    text += " " + Loc.T("Es läuft noch ein Spiel.");

                var answer = Msg(text, action.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            try
            {
                SaveSettings();
                Process.Start(new ProcessStartInfo(action.File, action.Args) { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception ex)
            {
                LogError("Energie", ex);
                Msg($"Das hat nicht geklappt:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ───────────────────────────── Abläufe (Automationen) ─────────────────────────────

        private static readonly (string Key, string Label)[] RoutineTriggers =
        {
            ("manual", "Nur von Hand"),
            ("start", "Wenn der Launcher startet"),
            ("gamestart", "Wenn ein Spiel startet"),
            ("gameend", "Wenn ein Spiel beendet wird")
        };

        private static readonly (string Key, string Label)[] StepTypes =
        {
            ("launch", "▶  Programm starten"),
            ("window", "🪟  Fenster platzieren"),
            ("wait", "⏱  Warten"),
            ("url", "🌐  Webseite öffnen"),
            ("game", "🎮  Spiel starten"),
            ("power", "⚡  Energieplan setzen"),
            ("streamer", "📡  Streamer-Modus"),
            ("toast", "💬  Meldung zeigen"),
            ("kill", "⛔  Programm beenden")
        };

        private static readonly (string Guid, string Label)[] PowerPlans =
        {
            ("a1841308-3541-4fab-bc81-f71556f20b4a", "Energiesparen"),
            ("381b4222-f694-41f0-9685-ff5bb260df2e", "Ausbalanciert"),
            ("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "Höchstleistung"),
            ("e9a42b02-d5df-448d-aa00-03f14749eb61", "Ultimative Leistung")
        };

        private static readonly string[] ProtectedProcesses =
            { "explorer", "winlogon", "csrss", "wininit", "services", "lsass", "svchost", "dwm", "system", "smss", "DFPProLauncher" };

        private readonly HashSet<string> runningRoutines = new();

        private static AutomationStep CloneStep(AutomationStep s) => new()
        {
            Type = s.Type,
            Target = s.Target,
            ProcessName = s.ProcessName,
            Number = s.Number,
            Mode = s.Mode
        };

        private string TriggerLabel(string key)
            => Loc.T(RoutineTriggers.FirstOrDefault(t => t.Key == key).Label ?? "Nur von Hand");

        private string ModeLabel(string mode) => mode switch
        {
            "maximize" => Loc.T("Maximiert"),
            "minimize" => Loc.T("Minimiert"),
            _ => Loc.T("Verschoben")
        };

        private string DescribeStep(AutomationStep s) => s.Type switch
        {
            "launch" => Loc.T("Programm starten") + ": " + (s.Target.StartsWith("app:", StringComparison.Ordinal) ? s.Target.Substring(4) : System.IO.Path.GetFileName(s.Target)),
            "wait" => Loc.T($"Warten: {s.Number} Sekunden"),
            "window" => Loc.T("Fenster platzieren") + $": {s.ProcessName} → " + Loc.T($"Bildschirm {s.Number}") + ", " + ModeLabel(s.Mode),
            "url" => Loc.T("Webseite öffnen") + ": " + s.Target,
            "power" => Loc.T("Energieplan") + ": " + Loc.T(PowerPlans.FirstOrDefault(p => p.Guid == s.Target).Label ?? s.Target),
            "toast" => Loc.T("Meldung zeigen") + ": " + s.Target,
            "kill" => Loc.T("Programm beenden") + ": " + s.ProcessName,
            "game" => Loc.T("Spiel starten") + ": " + s.Target,
            "streamer" => s.Mode == "on" ? Loc.T("Streamer-Modus einschalten") : Loc.T("Streamer-Modus ausschalten"),
            _ => s.Type
        };

        private AutomationStep? PromptStep(string type, AutomationStep? existing, List<AutomationStep> before)
        {
            var step = existing != null ? CloneStep(existing) : new AutomationStep { Type = type };

            switch (type)
            {
                case "launch":
                    {
                        int source = ChooseOne(Loc.T("Programm starten"), Loc.T("Woher soll das Programm kommen?"),
                            new List<string> { Loc.T("Aus meinen Anwendungen wählen"), Loc.T("Datei auswählen ...") });
                        if (source < 0) return null;

                        if (source == 0)
                        {
                            var names = cachedApps.Select(a => a.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
                            if (names.Count == 0)
                            {
                                Msg("Es wurden noch keine Anwendungen gefunden. Wähle stattdessen eine Datei aus.");
                                return null;
                            }

                            int chosen = ChooseOne(Loc.T("Programm wählen"), string.Empty, names);
                            if (chosen < 0) return null;
                            step.Target = "app:" + names[chosen];
                        }
                        else
                        {
                            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programme und Verknüpfungen|*.exe;*.lnk;*.bat;*.cmd;*.url|Alle Dateien|*.*" };
                            if (dialog.ShowDialog() != true) return null;
                            step.Target = dialog.FileName;
                        }
                        return step;
                    }

                case "wait":
                    {
                        string? text = PromptText(Loc.T("Warten"), Loc.T("Wie viele Sekunden soll gewartet werden?"), (existing?.Number ?? 5).ToString());
                        if (text == null || !int.TryParse(text, out int seconds)) return null;
                        step.Number = Math.Clamp(seconds, 1, 600);
                        return step;
                    }

                case "window":
                    {
                        var previous = before.LastOrDefault(s => s.Type == "launch");
                        string guess = previous == null ? string.Empty
                            : previous.Target.StartsWith("app:", StringComparison.Ordinal) ? previous.Target.Substring(4).Replace(" ", string.Empty)
                            : System.IO.Path.GetFileNameWithoutExtension(previous.Target);

                        string? name = PromptText(Loc.T("Fenster platzieren"),
                            Loc.T("Name des Programms (Prozessname, zum Beispiel Discord)"), existing?.ProcessName ?? guess);
                        if (name == null) return null;

                        int count = Math.Max(1, ReadDisplays().Count);
                        var monitors = Enumerable.Range(1, count)
                            .Select(n => n == 1 ? Loc.T("Bildschirm 1 (Hauptbildschirm)") : Loc.T($"Bildschirm {n}")).ToList();
                        int monitor = ChooseOne(Loc.T("Welcher Bildschirm?"), string.Empty, monitors);
                        if (monitor < 0) return null;

                        int mode = ChooseOne(Loc.T("Wie soll das Fenster aussehen?"), string.Empty,
                            new List<string> { Loc.T("Maximiert (füllt den Bildschirm)"), Loc.T("Nur auf den Bildschirm verschieben"), Loc.T("Minimiert") });
                        if (mode < 0) return null;

                        step.ProcessName = name;
                        step.Number = monitor + 1;
                        step.Mode = new[] { "maximize", "move", "minimize" }[mode];
                        return step;
                    }

                case "url":
                    {
                        string? url = PromptText(Loc.T("Webseite öffnen"), Loc.T("Adresse der Webseite"), existing?.Target ?? "https://");
                        if (url == null) return null;
                        step.Target = url.Contains("://") ? url : "https://" + url;
                        return step;
                    }

                case "power":
                    {
                        int plan = ChooseOne(Loc.T("Energieplan setzen"), string.Empty, PowerPlans.Select(p => Loc.T(p.Label)).ToList());
                        if (plan < 0) return null;
                        step.Target = PowerPlans[plan].Guid;
                        return step;
                    }

                case "toast":
                    {
                        string? text = PromptText(Loc.T("Meldung zeigen"), Loc.T("Text der Meldung"), existing?.Target ?? string.Empty);
                        if (text == null) return null;
                        step.Target = text;
                        return step;
                    }

                case "kill":
                    {
                        string? name = PromptText(Loc.T("Programm beenden"), Loc.T("Name des Programms (Prozessname, zum Beispiel Discord)"), existing?.ProcessName ?? string.Empty);
                        if (name == null) return null;
                        step.ProcessName = name;
                        return step;
                    }

                case "game":
                    {
                        var names = allGames.Select(g => g.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
                        if (names.Count == 0) return null;

                        int chosen = ChooseOne(Loc.T("Spiel starten"), string.Empty, names);
                        if (chosen < 0) return null;
                        step.Target = names[chosen];
                        return step;
                    }

                case "streamer":
                    {
                        int choice = ChooseOne(Loc.T("Streamer-Modus"), string.Empty,
                            new List<string> { Loc.T("Streamer-Modus einschalten"), Loc.T("Streamer-Modus ausschalten") });
                        if (choice < 0) return null;
                        step.Mode = choice == 0 ? "on" : "off";
                        return step;
                    }
            }

            return null;
        }

        private bool EditRoutine(AutomationRoutine routine)
        {
            var dialog = CreateDialog(Loc.T("Ablauf bearbeiten"), 640, out var panel);

            panel.Children.Add(MutedLabel("Name des Ablaufs"));
            var name = new System.Windows.Controls.TextBox { Text = routine.Name, Height = 38, Margin = new Thickness(0, 6, 0, 14) };
            panel.Children.Add(name);

            string trigger = routine.Trigger;
            var filterPanel = new StackPanel { Visibility = trigger.StartsWith("game", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed };

            panel.Children.Add(MutedLabel("Wann soll der Ablauf starten?"));
            panel.Children.Add(ChipRow(RoutineTriggers, trigger, key =>
            {
                trigger = key;
                filterPanel.Visibility = key.StartsWith("game", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
            }));

            filterPanel.Children.Add(MutedLabel("Nur für dieses Spiel (leer = für alle Spiele)"));
            var filter = new System.Windows.Controls.TextBox { Text = routine.GameFilter, Height = 38, Margin = new Thickness(0, 6, 0, 14) };
            filterPanel.Children.Add(filter);
            panel.Children.Add(filterPanel);

            panel.Children.Add(MutedLabel("Schritte (werden der Reihe nach ausgeführt)"));
            var steps = routine.Steps.Select(CloneStep).ToList();
            var stepsPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            panel.Children.Add(stepsPanel);

            void RenderSteps()
            {
                stepsPanel.Children.Clear();

                if (steps.Count == 0)
                {
                    stepsPanel.Children.Add(new TextBlock
                    {
                        Text = Loc.T("Noch keine Schritte. Füge unten den ersten hinzu."),
                        Foreground = BrushSubtle,
                        Margin = new Thickness(0, 4, 0, 8)
                    });
                    return;
                }

                for (int i = 0; i < steps.Count; i++)
                {
                    int index = i;

                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    row.Children.Add(new TextBlock
                    {
                        Text = $"{index + 1}.  {DescribeStep(steps[index])}",
                        Foreground = System.Windows.Media.Brushes.White,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0)
                    });

                    var tools = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                    tools.Children.Add(MiniButton("↑", "Nach oben", () =>
                    {
                        if (index == 0) return;
                        (steps[index - 1], steps[index]) = (steps[index], steps[index - 1]);
                        RenderSteps();
                    }));
                    tools.Children.Add(MiniButton("↓", "Nach unten", () =>
                    {
                        if (index >= steps.Count - 1) return;
                        (steps[index + 1], steps[index]) = (steps[index], steps[index + 1]);
                        RenderSteps();
                    }));
                    tools.Children.Add(MiniButton("✏", "Bearbeiten", () =>
                    {
                        var edited = PromptStep(steps[index].Type, steps[index], steps.Take(index).ToList());
                        if (edited == null) return;
                        steps[index] = edited;
                        RenderSteps();
                    }));
                    tools.Children.Add(MiniButton("✕", "Entfernen", () =>
                    {
                        steps.RemoveAt(index);
                        RenderSteps();
                    }));
                    Grid.SetColumn(tools, 1);
                    row.Children.Add(tools);

                    stepsPanel.Children.Add(new Border
                    {
                        Padding = new Thickness(12, 8, 8, 8),
                        Margin = new Thickness(0, 0, 0, 6),
                        CornerRadius = new CornerRadius(8),
                        Background = MakeBrush("#0FFFFFFF"),
                        Child = row
                    });
                }
            }

            RenderSteps();

            var addButton = new System.Windows.Controls.Button
            {
                Content = Loc.T("＋ Schritt hinzufügen"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 20)
            };
            addButton.Click += (s, e) =>
            {
                var menu = CreateMenu();
                foreach (var (typeKey, label) in StepTypes)
                {
                    string type = typeKey;
                    AddMenuItem(menu, label, () =>
                    {
                        var added = PromptStep(type, null, steps);
                        if (added == null) return;
                        steps.Add(added);
                        RenderSteps();
                    });
                }
                menu.PlacementTarget = addButton;
                menu.IsOpen = true;
            };
            panel.Children.Add(addButton);

            bool saved = false;
            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancel = new System.Windows.Controls.Button { Content = "Abbrechen", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var save = new System.Windows.Controls.Button { Content = "Speichern", Padding = new Thickness(28, 8, 28, 8), IsDefault = true };
            save.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            save.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(name.Text))
                {
                    Msg("Bitte gib dem Ablauf einen Namen.");
                    return;
                }

                routine.Name = name.Text.Trim();
                routine.Trigger = trigger;
                routine.GameFilter = filter.Text.Trim();
                routine.Steps = steps;
                saved = true;
                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            dialog.ShowDialog();
            return saved;
        }

        private void RenderRoutines()
        {
            if (RoutinesPanel == null) return;
            RoutinesPanel.Children.Clear();

            if (settings.Routines.Count == 0)
            {
                RoutinesPanel.Children.Add(new TextBlock
                {
                    Text = Loc.T("Noch keine Abläufe. Lege einen neuen an oder füge das Beispiel ein."),
                    Foreground = BrushSubtle,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 14)
                });
                return;
            }

            foreach (var entry in settings.Routines.ToList())
            {
                var routine = entry;

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                texts.Children.Add(new TextBlock { Text = routine.Name, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });

                string trigger = TriggerLabel(routine.Trigger);
                if (routine.Trigger.StartsWith("game", StringComparison.Ordinal) && routine.GameFilter.Length > 0)
                    trigger += $" ({routine.GameFilter})";
                texts.Children.Add(new TextBlock
                {
                    Text = trigger + "  ·  " + Loc.T($"{routine.Steps.Count} Schritte"),
                    Foreground = BrushSubtle,
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                row.Children.Add(texts);

                var tools = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                tools.Children.Add(MiniButton("▶", "Jetzt ausführen", () => _ = RunRoutineAsync(routine, true)));
                tools.Children.Add(MiniButton("✏", "Bearbeiten", () =>
                {
                    if (!EditRoutine(routine)) return;
                    SaveSettings();
                    RenderRoutines();
                }));
                tools.Children.Add(MiniButton("🗑", "Löschen", () =>
                {
                    settings.Routines.Remove(routine);
                    SaveSettings();
                    RenderRoutines();
                }));
                Grid.SetColumn(tools, 1);
                row.Children.Add(tools);

                var enabled = new System.Windows.Controls.CheckBox { IsChecked = routine.Enabled, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                enabled.Checked += (s, e) => { routine.Enabled = true; SaveSettings(); };
                enabled.Unchecked += (s, e) => { routine.Enabled = false; SaveSettings(); };
                Grid.SetColumn(enabled, 2);
                row.Children.Add(enabled);

                RoutinesPanel.Children.Add(new Border
                {
                    Padding = new Thickness(14, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    CornerRadius = new CornerRadius(10),
                    Background = MakeBrush("#0FFFFFFF"),
                    Child = row
                });
            }
        }

        private void BtnRoutineNew_Click(object sender, RoutedEventArgs e)
        {
            var routine = new AutomationRoutine { Name = Loc.T("Neuer Ablauf"), Trigger = "manual" };
            if (!EditRoutine(routine)) return;

            settings.Routines.Add(routine);
            SaveSettings();
            RenderRoutines();
        }

        private void BtnRoutineSample_Click(object sender, RoutedEventArgs e)
        {
            settings.Routines.Add(new AutomationRoutine
            {
                Name = Loc.T("Beispiel: Gaming-Abend"),
                Trigger = "manual",
                Steps = new List<AutomationStep>
                {
                    new() { Type = "launch", Target = "app:Discord" },
                    new() { Type = "window", ProcessName = "Discord", Number = 2, Mode = "maximize" },
                    new() { Type = "wait", Number = 3 },
                    new() { Type = "launch", Target = "app:Spotify" },
                    new() { Type = "toast", Target = "Viel Spaß!" }
                }
            });
            SaveSettings();
            RenderRoutines();
            ShowToast("⚙", "Beispiel hinzugefügt", "Passe die Schritte mit dem Stift an deine Programme an.", 7, null, true);
        }

        private void RunRoutinesFor(string trigger, GameItem? game)
        {
            if (!settings.AutomationsEnabled) return;

            foreach (var routine in settings.Routines.Where(r => r.Enabled && r.Trigger == trigger).ToList())
            {
                if (game != null && routine.GameFilter.Length > 0
                    && !game.Name.Contains(routine.GameFilter, StringComparison.CurrentCultureIgnoreCase)) continue;

                _ = RunRoutineAsync(routine, false);
            }
        }

        private async Task RunRoutineAsync(AutomationRoutine routine, bool manual)
        {
            if (!settings.AutomationsEnabled && !manual) return;
            if (!runningRoutines.Add(routine.Name)) return;

            try
            {
                if (manual) ShowToast("⚙", "Ablauf gestartet", routine.Name, 4, null, true);

                foreach (var step in routine.Steps.ToList())
                {
                    try
                    {
                        await RunStepAsync(step);
                    }
                    catch (Exception ex)
                    {
                        LogError("Ablauf: " + routine.Name, ex);
                    }
                }
            }
            finally
            {
                runningRoutines.Remove(routine.Name);
            }
        }

        private async Task RunStepAsync(AutomationStep step)
        {
            switch (step.Type)
            {
                case "launch":
                    if (step.Target.StartsWith("app:", StringComparison.Ordinal))
                    {
                        string appName = step.Target.Substring(4);
                        var app = cachedApps.FirstOrDefault(a => a.Name == appName);
                        if (app == null) throw new InvalidOperationException("App nicht gefunden: " + appName);
                        LaunchApp(app);
                    }
                    else if (File.Exists(step.Target) || Directory.Exists(step.Target))
                    {
                        Process.Start(new ProcessStartInfo(step.Target)
                        {
                            UseShellExecute = true,
                            WorkingDirectory = System.IO.Path.GetDirectoryName(step.Target) ?? string.Empty
                        });
                    }
                    else
                    {
                        throw new FileNotFoundException("Datei nicht gefunden", step.Target);
                    }
                    await Task.Delay(800);
                    break;

                case "wait":
                    await Task.Delay(Math.Clamp(step.Number, 1, 600) * 1000);
                    break;

                case "window":
                    await PlaceWindowAsync(step.ProcessName, step.Number, step.Mode);
                    break;

                case "url":
                    OpenShell(step.Target);
                    break;

                case "power":
                    await ActivatePowerPlanAsync(step.Target);
                    break;

                case "toast":
                    ShowToast("⚙", "Ablauf", step.Target, 6, null, true);
                    break;

                case "kill":
                    await CloseProcessesAsync(step.ProcessName);
                    break;

                case "game":
                    {
                        var game = allGames.FirstOrDefault(g => string.Equals(g.Name, step.Target, StringComparison.OrdinalIgnoreCase));
                        if (game == null) throw new InvalidOperationException("Spiel nicht gefunden: " + step.Target);
                        LaunchGame(game);
                        break;
                    }

                case "streamer":
                    settings.StreamerMode = step.Mode == "on";
                    SaveSettings();
                    ApplyStreamerMode();
                    break;
            }
        }

        private static IntPtr FindWindowHandle(string name)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        try
                        {
                            process.Refresh();
                            if (process.MainWindowHandle != IntPtr.Zero && NativeExtras.IsWindowVisible(process.MainWindowHandle))
                                return process.MainWindowHandle;
                        }
                        catch { }
                    }
                }

                foreach (var process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try
                        {
                            if (process.MainWindowHandle != IntPtr.Zero
                                && process.MainWindowTitle.Contains(name, StringComparison.OrdinalIgnoreCase)
                                && NativeExtras.IsWindowVisible(process.MainWindowHandle))
                                return process.MainWindowHandle;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return IntPtr.Zero;
        }

        private async Task PlaceWindowAsync(string processName, int monitor, string mode)
        {
            string name = Regex.Replace(processName.Trim(), @"\.exe$", string.Empty, RegexOptions.IgnoreCase);
            if (name.Length == 0) return;

            IntPtr handle = IntPtr.Zero;
            for (int attempt = 0; attempt < 40 && handle == IntPtr.Zero; attempt++)
            {
                handle = await Task.Run(() => FindWindowHandle(name));
                if (handle == IntPtr.Zero) await Task.Delay(500);
            }

            if (handle == IntPtr.Zero)
            {
                LogError("Ablauf", new InvalidOperationException("Kein Fenster gefunden für: " + processName));
                return;
            }

            await Task.Delay(700);   // das Fenster in Ruhe fertig aufbauen lassen

            NativeExtras.ShowWindow(handle, 9);   // zuerst normal darstellen
            if (mode == "minimize")
            {
                NativeExtras.ShowWindow(handle, 6);
                return;
            }

            var displays = ReadDisplays();
            var target = displays.Count > 0 ? displays[Math.Clamp(monitor - 1, 0, displays.Count - 1)] : null;

            if (target != null)
            {
                int rawWidth = target.RawWidth > 0 ? target.RawWidth : target.Width;
                int rawHeight = target.RawHeight > 0 ? target.RawHeight : target.Height;

                NativeExtras.GetWindowRect(handle, out var rect);
                int width = Math.Min(Math.Max(rect.Right - rect.Left, 640), Math.Max(640, rawWidth - 160));
                int height = Math.Min(Math.Max(rect.Bottom - rect.Top, 420), Math.Max(420, rawHeight - 160));

                // 0x0004 = Reihenfolge nicht ändern, 0x0010 = nicht aktivieren
                NativeExtras.SetWindowPos(handle, IntPtr.Zero, target.X + 80, target.Y + 80, width, height, 0x0004 | 0x0010);
            }

            if (mode == "maximize") NativeExtras.ShowWindow(handle, 3);
        }

        private async Task CloseProcessesAsync(string processName)
        {
            string name = Regex.Replace(processName.Trim(), @"\.exe$", string.Empty, RegexOptions.IgnoreCase);
            if (name.Length == 0 || ProtectedProcesses.Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Dieses Programm darf nicht beendet werden: " + processName);

            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes)
            {
                try { process.CloseMainWindow(); }
                catch { }
            }

            await Task.Delay(2500);

            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }

        // ───────────────────────────── Systembefehle (PowerShell) unter Tools ─────────────────────────────

        private sealed record SysCmd(string Icon, string Color, string Title, string Text, string Script, bool Admin, string Confirm);

        private static readonly (string Group, SysCmd[] Items)[] SystemCommandGroups =
        {
            ("Reinigung", new[]
            {
                new SysCmd("🗑", "#F59E0B", "Temp-Dateien leeren", "Löscht temporäre Dateien deines Benutzers",
                    "Write-Host 'Temporäre Dateien werden gelöscht ...'; Get-ChildItem -Path $env:TEMP -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; Write-Host 'Fertig.' -ForegroundColor Green",
                    false, "Alle Dateien im Temp-Ordner löschen? Dateien, die gerade benutzt werden, bleiben liegen."),
                new SysCmd("🧹", "#F59E0B", "Papierkorb leeren", "Leert den Papierkorb endgültig",
                    "Clear-RecycleBin -Force -ErrorAction SilentlyContinue; Write-Host 'Papierkorb geleert.' -ForegroundColor Green",
                    false, "Den Papierkorb endgültig leeren?"),
                new SysCmd("🌐", "#F59E0B", "DNS-Cache leeren", "Hilft bei Verbindungsproblemen", "ipconfig /flushdns", false, string.Empty),
                new SysCmd("📦", "#F59E0B", "Windows-Update-Cache leeren", "🛡 Räumt heruntergeladene Update-Reste auf",
                    "Write-Host 'Dienste werden angehalten ...'; Stop-Service wuauserv,bits -Force -ErrorAction SilentlyContinue; Remove-Item 'C:\\Windows\\SoftwareDistribution\\Download\\*' -Recurse -Force -ErrorAction SilentlyContinue; Start-Service wuauserv,bits; Write-Host 'Fertig.' -ForegroundColor Green",
                    true, "Den Windows-Update-Cache leeren? Windows lädt Updates danach bei Bedarf neu.")
            }),
            ("Reparatur", new[]
            {
                new SysCmd("🛡", "#8B5CF6", "Systemdateien prüfen (SFC)", "🛡 Sucht und repariert beschädigte Windows-Dateien", "sfc /scannow", true, string.Empty),
                new SysCmd("🩹", "#8B5CF6", "Windows-Abbild prüfen (DISM)", "🛡 Prüft den Zustand der Windows-Installation",
                    "DISM /Online /Cleanup-Image /CheckHealth; DISM /Online /Cleanup-Image /ScanHealth", true, string.Empty),
                new SysCmd("💽", "#8B5CF6", "Laufwerk C: prüfen", "🛡 Nur lesen, es wird nichts geändert", "chkdsk C:", true, string.Empty),
                new SysCmd("📶", "#8B5CF6", "Netzwerk zurücksetzen", "🛡 Setzt Winsock und IP-Einstellungen zurück",
                    "netsh winsock reset; netsh int ip reset; ipconfig /release; ipconfig /renew; ipconfig /flushdns; Write-Host 'Bitte starte den PC neu.' -ForegroundColor Yellow",
                    true, "Das Netzwerk wird kurz unterbrochen und danach ist ein Neustart nötig. Fortfahren?")
            }),
            ("Auslesen", new[]
            {
                new SysCmd("ℹ", "#3B82F6", "Systeminformationen", "Hardware, Windows und Updates im Überblick", "systeminfo", false, string.Empty),
                new SysCmd("🌍", "#3B82F6", "Netzwerk-Informationen", "IP-Adressen, DNS und Adapter", "ipconfig /all", false, string.Empty),
                new SysCmd("📡", "#3B82F6", "Verbindungstest", "Ping an Cloudflare und Google", "ping 1.1.1.1 -n 6; ping 8.8.8.8 -n 6", false, string.Empty),
                new SysCmd("💾", "#3B82F6", "Laufwerke und Gesundheit", "Zustand und freier Platz aller Laufwerke",
                    "Get-PhysicalDisk | Format-Table FriendlyName,MediaType,HealthStatus,@{n='GB';e={[math]::Round($_.Size/1GB)}} -AutoSize; Get-Volume | Where-Object DriveLetter | Format-Table DriveLetter,FileSystemLabel,@{n='Frei GB';e={[math]::Round($_.SizeRemaining/1GB,1)}},@{n='Gesamt GB';e={[math]::Round($_.Size/1GB,1)}} -AutoSize",
                    false, string.Empty),
                new SysCmd("🧠", "#3B82F6", "RAM-Fresser", "Die 15 Programme mit dem größten Speicherbedarf",
                    "Get-Process | Sort-Object WorkingSet -Descending | Select-Object -First 15 Name,@{n='RAM MB';e={[math]::Round($_.WorkingSet/1MB)}} | Format-Table -AutoSize", false, string.Empty),
                new SysCmd("🔋", "#3B82F6", "Akku-Bericht", "Erstellt einen Bericht auf dem Desktop",
                    "powercfg /batteryreport /output \"$env:USERPROFILE\\Desktop\\Akku-Bericht.html\"; Start-Process \"$env:USERPROFILE\\Desktop\\Akku-Bericht.html\"", false, string.Empty),
                new SysCmd("🎮", "#3B82F6", "Grafiktreiber-Info", "Name, Version und Datum des Treibers",
                    "Get-CimInstance Win32_VideoController | Format-List Name,DriverVersion,DriverDate,VideoModeDescription", false, string.Empty),
                new SysCmd("💥", "#3B82F6", "Letzte Programmabstürze", "Die letzten 10 Fehler aus der Ereignisanzeige",
                    "Get-WinEvent -FilterHashtable @{LogName='Application';Id=1000} -MaxEvents 10 -ErrorAction SilentlyContinue | Format-List TimeCreated,Message", false, string.Empty)
            })
        };

        private void RunSystemCommand(SysCmd command)
        {
            if (command.Confirm.Length > 0)
            {
                var answer = Msg(Loc.T(command.Confirm), Loc.T(command.Title), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }

            string title = command.Title.Replace("'", string.Empty);
            string script = $"$Host.UI.RawUI.WindowTitle = 'DFP Pro Launcher - {title}'; {command.Script}; Write-Host ''; Write-Host 'Fertig. Du kannst dieses Fenster schließen.' -ForegroundColor Green";
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -NoExit -EncodedCommand {encoded}")
                {
                    UseShellExecute = true,
                    Verb = command.Admin ? "runas" : "open"
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Die Rückfrage von Windows (Administratorrechte) wurde abgebrochen
            }
            catch (Exception ex)
            {
                LogError("Systembefehl", ex);
                Msg($"Der Befehl konnte nicht gestartet werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BuildSystemCommandTiles()
        {
            ToolsContainer.Children.Add(new TextBlock
            {
                Text = Loc.T("Systembefehle"),
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 34, 0, 4),
                Effect = (System.Windows.Media.Effects.Effect)FindResource("TextShadow")
            });
            ToolsContainer.Children.Add(new TextBlock
            {
                Text = Loc.T("Ein Klick öffnet ein PowerShell-Fenster mit dem Befehl. Mit 🛡 gekennzeichnete Befehle brauchen Administratorrechte, Windows fragt dann nach. Rechtsklick kopiert den Befehl."),
                Foreground = MakeBrush("#E5E7EB"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                Effect = (System.Windows.Media.Effects.Effect)FindResource("TextShadow")
            });

            foreach (var (group, items) in SystemCommandGroups)
            {
                ToolsContainer.Children.Add(ToolSectionHeader(group));
                var row = new WrapPanel();

                foreach (var command in items)
                {
                    var item = command;
                    var tile = CreateToolTile(item.Icon, item.Color, item.Title, item.Text, () => RunSystemCommand(item), 250, 152);

                    var menu = CreateMenu();
                    AddMenuItem(menu, "▶  Ausführen", () => RunSystemCommand(item));
                    AddMenuItem(menu, "📋  Befehl kopieren", () =>
                    {
                        try
                        {
                            System.Windows.Clipboard.SetText(item.Script);
                            ShowToast("📋", "Kopiert", "Der Befehl liegt in der Zwischenablage.", 3);
                        }
                        catch { }
                    });
                    tile.ContextMenu = menu;
                    row.Children.Add(tile);
                }

                ToolsContainer.Children.Add(row);
            }
        }

        // ───────────────────────────── Systemüberwachung: mehr Werte ─────────────────────────────

        private sealed class SensorSnapshot
        {
            public double? CpuTemp, CpuPower, GpuTemp, GpuPower, GpuClock, VramUsedMb, VramTotalMb;
        }

        private SensorSnapshot sensorSnap = new();
        private double gpuTotalVramMb = -1;
        private List<System.Diagnostics.PerformanceCounter>? vramCounters;

        private static void CaptureSnapshot(SensorSnapshot snap, LibreHardwareMonitor.Hardware.IHardware hardware)
        {
            var type = hardware.HardwareType;
            bool cpu = type == LibreHardwareMonitor.Hardware.HardwareType.Cpu;
            bool gpu = type == LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia
                    || type == LibreHardwareMonitor.Hardware.HardwareType.GpuAmd
                    || type == LibreHardwareMonitor.Hardware.HardwareType.GpuIntel;
            if (!cpu && !gpu) return;

            double? cpuPreferred = null, cpuMax = null;

            foreach (var sensor in hardware.Sensors)
            {
                if (!sensor.Value.HasValue) continue;
                double value = sensor.Value.Value;
                string name = sensor.Name;

                switch (sensor.SensorType)
                {
                    case LibreHardwareMonitor.Hardware.SensorType.Temperature:
                        if (cpu)
                        {
                            if (Regex.IsMatch(name, "Package|Tctl|Tdie")) cpuPreferred = value;
                            cpuMax = Math.Max(cpuMax ?? 0, value);
                        }
                        else if (name.Contains("Core") && !name.Contains("Hot")) snap.GpuTemp = value;
                        else snap.GpuTemp ??= value;
                        break;
                    case LibreHardwareMonitor.Hardware.SensorType.Power:
                        if (cpu && name.Contains("Package")) snap.CpuPower = value;
                        else if (gpu && (name.Contains("Package") || name.Contains("Power"))) snap.GpuPower = value;
                        break;
                    case LibreHardwareMonitor.Hardware.SensorType.Clock:
                        if (gpu && name.Contains("Core")) snap.GpuClock = value;
                        break;
                    case LibreHardwareMonitor.Hardware.SensorType.SmallData:
                        if (gpu && name.Contains("Memory Used")) snap.VramUsedMb = value;
                        else if (gpu && name.Contains("Memory Total")) snap.VramTotalMb = value;
                        break;
                }
            }

            if (cpu) snap.CpuTemp = cpuPreferred ?? cpuMax;
        }

        private double? ReadVramUsedMb()
        {
            try
            {
                if (vramCounters == null)
                {
                    vramCounters = new List<System.Diagnostics.PerformanceCounter>();
                    var category = new System.Diagnostics.PerformanceCounterCategory("GPU Adapter Memory");
                    foreach (string instance in category.GetInstanceNames())
                        vramCounters.Add(new System.Diagnostics.PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instance, true));
                }

                if (vramCounters.Count == 0) return null;
                return vramCounters.Max(c => c.NextValue()) / (1024.0 * 1024.0);
            }
            catch
            {
                vramCounters = new List<System.Diagnostics.PerformanceCounter>();
                return null;
            }
        }

        private double ReadVramTotalMb()
        {
            if (gpuTotalVramMb >= 0) return gpuTotalVramMb;

            try
            {
                long bytes = ReadGpuAdapters().OrderByDescending(a => a.VramBytes).FirstOrDefault()?.VramBytes ?? 0;
                gpuTotalVramMb = bytes / (1024.0 * 1024.0);
            }
            catch
            {
                gpuTotalVramMb = 0;
            }
            return gpuTotalVramMb;
        }

        private void UpdateMetricDetails()
        {
            if (TxtCpuDetail == null || lastSample == null) return;

            var m = lastSample;
            var s = sensorSnap;

            // Prozessor
            var cpu = new List<string> { Loc.T($"Auslastung: {m.Cpu:F0} %") };
            if (m.Mhz > 0) cpu.Add(Loc.T($"Takt: {m.Mhz / 1000.0:F2} GHz"));
            cpu.Add(Loc.T($"Threads: {Environment.ProcessorCount}"));
            if (s.CpuTemp.HasValue) cpu.Add(Loc.T($"Temperatur: {s.CpuTemp.Value:F0} °C"));
            if (s.CpuPower.HasValue) cpu.Add(Loc.T($"Leistung: {s.CpuPower.Value:F0} W"));
            TxtCpuDetail.Text = string.Join("\n", cpu);

            // Arbeitsspeicher
            if (totalRamMB > 0)
            {
                double usedGb = m.UsedMb / 1024.0, totalGb = totalRamMB / 1024.0;
                TxtRamDetail.Text = Loc.T($"{usedGb:F1} GB von {totalGb:F0} GB belegt") + "\n" + Loc.T($"Frei: {Math.Max(0, totalGb - usedGb):F1} GB");
            }
            else
            {
                TxtRamDetail.Text = Loc.T($"Auslastung: {m.Ram:F0} %");
            }

            // Grafikkarte
            if (!m.HasGpu)
            {
                TxtGpuDetail.Text = Loc.T("Keine Messwerte für die Grafikkarte verfügbar.");
                return;
            }

            var gpu = new List<string> { Loc.T($"Auslastung: {m.Gpu:F0} %") };

            double vramTotal = s.VramTotalMb ?? ReadVramTotalMb();
            double? vramUsed = s.VramUsedMb ?? ReadVramUsedMb();
            if (vramTotal > 0 && vramUsed.HasValue)
                gpu.Add(Loc.T($"VRAM: {vramUsed.Value / 1024.0:F1} GB von {vramTotal / 1024.0:F0} GB belegt"));
            else if (vramTotal > 0)
                gpu.Add(Loc.T($"VRAM: {vramTotal / 1024.0:F0} GB"));

            if (s.GpuClock.HasValue) gpu.Add(Loc.T($"Takt: {s.GpuClock.Value:F0} MHz"));
            if (s.GpuTemp.HasValue) gpu.Add(Loc.T($"Temperatur: {s.GpuTemp.Value:F0} °C"));
            if (s.GpuPower.HasValue) gpu.Add(Loc.T($"Leistung: {s.GpuPower.Value:F0} W"));
            TxtGpuDetail.Text = string.Join("\n", gpu);
        }

        private DateTime lastDriveUpdate = DateTime.MinValue;
        private DateTime lastNetTime = DateTime.MinValue;
        private long lastNetIn, lastNetOut;

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024) return $"{bytesPerSecond:F0} B/s";
            if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024.0:F0} KB/s";
            return $"{bytesPerSecond / (1024.0 * 1024.0):F1} MB/s";
        }

        private void UpdateDriveAndNetwork()
        {
            if (DriveRows == null) return;

            // Netzwerk (jede Sekunde)
            try
            {
                long totalIn = 0, totalOut = 0;
                string adapter = string.Empty;
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                        || nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;

                    var stats = nic.GetIPv4Statistics();
                    totalIn += stats.BytesReceived;
                    totalOut += stats.BytesSent;
                    if (adapter.Length == 0) adapter = nic.Name;
                }

                var now = DateTime.Now;
                if (lastNetTime != DateTime.MinValue)
                {
                    double seconds = Math.Max(0.2, (now - lastNetTime).TotalSeconds);
                    TxtNetDown.Text = "↓ " + FormatSpeed(Math.Max(0, totalIn - lastNetIn) / seconds);
                    TxtNetUp.Text = "↑ " + FormatSpeed(Math.Max(0, totalOut - lastNetOut) / seconds);
                }
                lastNetIn = totalIn;
                lastNetOut = totalOut;
                lastNetTime = now;
                TxtNetName.Text = adapter.Length > 0 ? adapter : Loc.T("Kein Netzwerk");
            }
            catch { }

            // Laufwerke (alle 5 Sekunden)
            if ((DateTime.Now - lastDriveUpdate).TotalSeconds < 5) return;
            lastDriveUpdate = DateTime.Now;

            DriveRows.Children.Clear();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

                    double totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                    double usedGb = totalGb - drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    double percent = totalGb > 0 ? usedGb / totalGb * 100 : 0;

                    var line = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                    string label = drive.VolumeLabel.Length > 0 ? $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}" : drive.Name.TrimEnd('\\');
                    line.Children.Add(new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });

                    var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = MakeBrush("#1F2937"), Margin = new Thickness(0, 6, 0, 4) };
                    var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Background = MakeBrush(percent > 90 ? "#EF4444" : percent > 75 ? "#F59E0B" : "#34D399") };
                    var host = new Grid();
                    host.Children.Add(track);
                    host.Children.Add(fill);
                    host.SizeChanged += (s, e) => fill.Width = Math.Max(0, host.ActualWidth * percent / 100.0);
                    line.Children.Add(host);

                    line.Children.Add(new TextBlock
                    {
                        Text = Loc.T($"{usedGb:F0} GB von {totalGb:F0} GB belegt ({percent:F0} %)"),
                        Foreground = BrushSubtle,
                        FontSize = 12
                    });
                    DriveRows.Children.Add(line);
                }
                catch { }
            }
        }
    }
}
