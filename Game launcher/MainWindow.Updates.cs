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
        // ───────────────────────────── Nur eine Instanz ─────────────────────────────

        private static bool AcquireSingleInstance()
        {
            try
            {
                singleInstanceMutex = new System.Threading.Mutex(true, @"Local\DFPProLauncher_SingleInstance", out bool created);
                if (created) return true;

                // Neustart (zum Beispiel als Administrator): auf das Ende der alten Instanz warten
                if (Environment.GetCommandLineArgs().Contains("--restart"))
                {
                    try
                    {
                        if (singleInstanceMutex.WaitOne(10000)) return true;
                    }
                    catch (System.Threading.AbandonedMutexException)
                    {
                        return true;
                    }
                }

                try
                {
                    using var existing = System.Threading.EventWaitHandle.OpenExisting(@"Local\DFPProLauncher_Show");
                    existing.Set();
                }
                catch { }
                return false;
            }
            catch
            {
                return true;
            }
        }

        private void StartSingleInstanceListener()
        {
            try
            {
                showEventHandle = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Local\DFPProLauncher_Show");
                System.Threading.ThreadPool.RegisterWaitForSingleObject(showEventHandle,
                    (state, timedOut) => Dispatcher.BeginInvoke(new Action(ShowFromTray)), null, -1, false);
            }
            catch { }
        }

        // ───────────────────────────── Automatisch aktualisieren ─────────────────────────────

        private async void OnWindowActivated(object? sender, EventArgs e)
        {
            if (!settings.AutoRescan || isScanning || controllerMode || silentRescan) return;
            if ((DateTime.Now - lastScan).TotalMinutes < 10) return;

            lastScan = DateTime.Now;
            var before = allGames.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            silentRescan = true;
            try
            {
                await LoadInstalledGamesAsync();
            }
            finally
            {
                silentRescan = false;
            }

            var added = allGames.Where(g => !before.Contains(g.Name)).Select(g => g.Name).ToList();
            if (added.Count > 0)
                ShowToast("🎮", "Neue Spiele gefunden", string.Join(", ", added.Take(3)), 6);
        }

        // ───────────────────────────── Globaler Hotkey (Strg+Alt+D) ─────────────────────────────

        private void RegisterGlobalHotkey()
        {
            UnregisterGlobalHotkey();
            if (!settings.GlobalHotkey) return;

            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            hotkeySource = System.Windows.Interop.HwndSource.FromHwnd(handle);
            hotkeySource?.AddHook(HotkeyHook);

            // 0x0001 = Alt, 0x0002 = Strg, 0x4000 = nicht wiederholen; 0x44 = Taste D
            NativeExtras.RegisterHotKey(handle, HotkeyId, 0x0001 | 0x0002 | 0x4000, 0x44);
            // Strg+Alt+S schaltet den Streamer-Modus um
            NativeExtras.RegisterHotKey(handle, HotkeyId + 1, 0x0001 | 0x0002 | 0x4000, 0x53);
        }

        private void UnregisterGlobalHotkey()
        {
            try
            {
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    NativeExtras.UnregisterHotKey(handle, HotkeyId);
                    NativeExtras.UnregisterHotKey(handle, HotkeyId + 1);
                }
                hotkeySource?.RemoveHook(HotkeyHook);
                hotkeySource = null;
            }
            catch { }
        }

        private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312 && wParam.ToInt32() == HotkeyId)
            {
                ToggleLauncherVisibility();
                handled = true;
            }
            else if (msg == 0x0312 && wParam.ToInt32() == HotkeyId + 1)
            {
                Dispatcher.BeginInvoke(new Action(ToggleStreamerMode));
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ToggleLauncherVisibility()
        {
            if (IsVisible && WindowState != WindowState.Minimized && IsActive)
            {
                if (settings.MinimizeToTray && trayIcon != null) HideToTray();
                else WindowState = WindowState.Minimized;
            }
            else
            {
                ShowFromTray();
            }
        }

        // ───────────────────────────── Fehlerprotokoll ─────────────────────────────

        private static void LogError(string source, Exception? error)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(LogDir);
                    string file = System.IO.Path.Combine(LogDir, "error.log");

                    if (File.Exists(file) && new FileInfo(file).Length > 512 * 1024)
                    {
                        File.Copy(file, System.IO.Path.Combine(LogDir, "error.old.log"), true);
                        File.Delete(file);
                    }

                    File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\r\n{error}\r\n\r\n");
                }
            }
            catch { }
        }

        private void HookErrorLogging()
        {
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                app.DispatcherUnhandledException += (s, e) =>
                {
                    LogError("Oberfläche", e.Exception);
                    e.Handled = true;

                    if ((DateTime.Now - lastErrorToast).TotalSeconds > 30)
                    {
                        lastErrorToast = DateTime.Now;
                        ShowToast("⚠", "Unerwarteter Fehler", "Details stehen im Fehlerprotokoll (Einstellungen, Daten und Info).", 8);
                    }
                };
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError("Programm", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogError("Hintergrundaufgabe", e.Exception);
                e.SetObserved();
            };
        }

        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                string file = System.IO.Path.Combine(LogDir, "error.log");
                Process.Start(new ProcessStartInfo(File.Exists(file) ? file : LogDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Msg($"Konnte nicht geöffnet werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────────────────────────── Einstellungen sichern und wiederherstellen ─────────────────────────────

        private const int SettingsBackupKeep = 5;
        private static readonly TimeSpan SettingsBackupInterval = TimeSpan.FromDays(7);
        private static bool settingsSaveBlocked;

        /// <summary>Alle Sicherungen, die neueste zuerst. Der Dateiname beginnt mit dem Datum (settings_JJJJMMTT…).</summary>
        private static List<string> SettingsBackupFiles()
        {
            try
            {
                if (!Directory.Exists(SettingsBackupDir)) return new List<string>();
                return Directory.GetFiles(SettingsBackupDir, "settings_*.json")
                    .OrderByDescending(f => System.IO.Path.GetFileName(f), StringComparer.Ordinal)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>Datum einer Sicherung aus dem Namen: settings_20261006.json (alt) oder settings_20261006_142301_start.json.</summary>
        private static DateTime? SettingsBackupDate(string file)
        {
            var match = Regex.Match(System.IO.Path.GetFileName(file), @"^settings_(\d{8})(?:_(\d{6}))?");
            if (!match.Success) return null;

            string stamp = match.Groups[1].Value + (match.Groups[2].Success ? match.Groups[2].Value : "000000");
            return DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date : null;
        }

        /// <summary>Legt eine Sicherung der gespeicherten Einstellungsdatei an und behält nur die letzten 5.</summary>
        private static string? CreateSettingsBackup(string reason)
        {
            try
            {
                if (!File.Exists(SettingsFilePath) || new FileInfo(SettingsFilePath).Length < 20) return null;

                // Nur eine gültige Datei sichern, damit keine defekte Fassung die gute ersetzt
                if (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath)) == null) return null;

                Directory.CreateDirectory(SettingsBackupDir);
                string target = System.IO.Path.Combine(SettingsBackupDir, $"settings_{DateTime.Now:yyyyMMdd_HHmmss}_{reason}.json");
                File.Copy(SettingsFilePath, target, true);

                foreach (string old in SettingsBackupFiles().Skip(SettingsBackupKeep))
                {
                    try { File.Delete(old); } catch { }
                }
                return target;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Beim Start: sichern, wenn die letzte Sicherung älter als 7 Tage ist (oder es noch keine gibt).</summary>
        private static void BackupSettingsIfDue()
        {
            DateTime? latest = SettingsBackupFiles().Select(SettingsBackupDate).FirstOrDefault(d => d != null);
            if (latest == null || DateTime.Now - latest.Value >= SettingsBackupInterval) CreateSettingsBackup("start");
        }

        private void UpdateLastBackupText()
        {
            if (TxtLastBackup == null) return;
            DateTime? latest = SettingsBackupFiles().Select(SettingsBackupDate).FirstOrDefault(d => d != null);
            TxtLastBackup.Text = latest == null
                ? Loc.T("Noch keine Sicherung vorhanden.")
                : Loc.T($"Letzte Sicherung: {latest.Value:dd.MM.yyyy HH:mm}");
        }

        private void BtnBackupSettingsNow_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            if (CreateSettingsBackup("manuell") != null)
                ShowToast("💾", "Gesichert", "Deine Einstellungen wurden gesichert.", 4);
            else
                Msg("Die Sicherung konnte nicht angelegt werden.", "Sicherung", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateLastBackupText();
        }

        private static AppSettings? TryRestoreSettingsBackup()
        {
            try
            {
                // Die beschädigte Datei aufheben, damit sie nicht überschrieben wird
                if (File.Exists(SettingsFilePath))
                {
                    string broken = System.IO.Path.Combine(SettingsDir, $"settings.defekt_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                    File.Copy(SettingsFilePath, broken, true);
                }

                if (!Directory.Exists(SettingsBackupDir)) return null;

                foreach (string file in SettingsBackupFiles())
                {
                    try
                    {
                        var restored = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file));
                        if (restored == null) continue;

                        settingsRecovered = true;
                        return restored;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private void BtnOpenBackups_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(SettingsBackupDir);
                Process.Start(new ProcessStartInfo(SettingsBackupDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Msg($"Konnte nicht geöffnet werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────────────────────────── Updates direkt im Launcher ─────────────────────────────

        private const string InstallerAssetName = "DFP_Pro_Launcher_Setup.exe";
        private string updatePromptedVersion = string.Empty;
        private bool updateDialogOpen;

        private sealed class UpdateInfo
        {
            public Version Version = new(0, 0);
            public int Beta;                       // 0 = normale Version, 1 = -beta, 2 = -beta2 ...
            public string Tag = string.Empty;
            public string Notes = string.Empty;
            public string InstallerUrl = string.Empty;
            public string HashUrl = string.Empty;
            public string Repo = string.Empty;
            public long Size;
        }

        private static Version? CurrentVersion()
            => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;

        private static string VersionText()
        {
            var (number, beta) = CurrentAppVersion();
            return $"{number.Major}.{number.Minor}.{Math.Max(0, number.Build)}" + BetaSuffix(beta);
        }

        private static string BetaSuffix(int beta) => beta <= 0 ? string.Empty : beta == 1 ? "-beta" : $"-beta{beta}";

        /// <summary>Liest „v1.2.3“, „1.2.3-beta“ oder „1.2.3-beta2+abc“ (Zusatz nach + wird ignoriert).</summary>
        private static bool TryParseAppVersion(string? text, out (Version Number, int Beta) result)
        {
            result = (new Version(0, 0, 0), 0);
            var match = Regex.Match((text ?? string.Empty).Trim().TrimStart('v', 'V'),
                @"^(\d+)\.(\d+)(?:\.(\d+))?(?:\.\d+)?(?:-beta(\d*))?(?:\+.*)?$", RegexOptions.IgnoreCase);
            if (!match.Success) return false;

            int Part(int group) => match.Groups[group].Success && match.Groups[group].Value.Length > 0 ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
            int beta = match.Value.Contains("-beta", StringComparison.OrdinalIgnoreCase) ? Math.Max(1, Part(4)) : 0;
            result = (new Version(Part(1), Part(2), Part(3)), beta);
            return true;
        }

        /// <summary>Vergleicht zwei Versionen: höhere Nummer gewinnt, bei gleicher Nummer ist die normale Version neuer als jede Vorabversion.</summary>
        private static int CompareAppVersions((Version Number, int Beta) a, (Version Number, int Beta) b)
        {
            int compare = a.Number.CompareTo(b.Number);
            if (compare != 0) return compare;
            if (a.Beta == b.Beta) return 0;
            if (a.Beta == 0) return 1;
            if (b.Beta == 0) return -1;
            return a.Beta.CompareTo(b.Beta);
        }

        /// <summary>Installierte Version samt Beta-Zusatz (aus der beim Bau gesetzten Versionsangabe).</summary>
        private static (Version Number, int Beta) CurrentAppVersion()
        {
            try
            {
                string? informational = System.Reflection.Assembly.GetEntryAssembly()?
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?.InformationalVersion;
                if (TryParseAppVersion(informational, out var parsed)) return parsed;
            }
            catch { }

            var version = CurrentVersion();
            return (version == null ? new Version(1, 0, 0) : new Version(version.Major, version.Minor, Math.Max(0, version.Build)), 0);
        }

        /// <summary>Versionsanzeige: Testversionen aus Visual Studio tragen den Zusatz „Testversion“.</summary>
        private static string VersionLabel()
        {
#if DEBUG
            return VersionText() + " (" + Loc.T("Testversion") + ")";
#else
            return VersionText();
#endif
        }

        /// <summary>Das GitHub-Repository wird beim automatischen Bau in das Programm geschrieben.</summary>
        private static string BuiltInUpdateRepo()
        {
            try
            {
                return System.Reflection.Assembly.GetEntryAssembly()?
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                    .OfType<System.Reflection.AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Reserve, falls beim Bau keine Update-Quelle eingetragen wurde.</summary>
        private const string FallbackUpdateRepository = "DerFettePudel/DFP-Pro-Launcher";

        private string UpdateRepository()
        {
            if (!string.IsNullOrWhiteSpace(settings.UpdateRepo)) return settings.UpdateRepo.Trim();

            string built = BuiltInUpdateRepo();
            return built.Length > 0 ? built : FallbackUpdateRepository;
        }

        private static bool IsValidRepository(string repo) => Regex.IsMatch(repo ?? string.Empty, @"^[\w.\-]+/[\w.\-]+$");

        /// <summary>Downloads sind nur von der Release-Seite des eigenen Repositorys erlaubt.</summary>
        private static bool IsTrustedDownload(string url, string repo)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Scheme == Uri.UriSchemeHttps
                       && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                       && uri.AbsolutePath.StartsWith($"/{repo}/releases/download/", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private async Task<UpdateInfo?> FetchLatestReleaseAsync(string repo)
        {
            // Beta-Kanal: alle Releases durchsehen (auch „Pre-release“), sonst nur die neueste normale Version
            bool beta = settings.BetaUpdates;
            string url = beta ? $"https://api.github.com/repos/{repo}/releases?per_page=30" : $"https://api.github.com/repos/{repo}/releases/latest";

            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("DFPProLauncher");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array) return ParseRelease(root, repo);

            UpdateInfo? best = null;
            foreach (var release in root.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
                var info = ParseRelease(release, repo);
                if (info != null && (best == null || CompareAppVersions((info.Version, info.Beta), (best.Version, best.Beta)) > 0)) best = info;
            }
            return best;
        }

        private static UpdateInfo? ParseRelease(JsonElement root, string repo)
        {
            string tag = GetJsonString(root, "tag_name");
            if (tag.Length == 0 || !TryParseAppVersion(tag, out var parsed)) return null;

            var info = new UpdateInfo { Version = parsed.Number, Beta = parsed.Beta, Tag = tag, Notes = GetJsonString(root, "body"), Repo = repo };

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = GetJsonString(asset, "name");
                    string url = GetJsonString(asset, "browser_download_url");

                    if (name.Equals(InstallerAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        info.InstallerUrl = url;
                        info.Size = JsonLong(asset, "size");
                    }
                    else if (name.Equals(InstallerAssetName + ".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        info.HashUrl = url;
                    }
                }
            }

            // Erst anzeigen, wenn der Installer wirklich am Release hängt
            return info.InstallerUrl.Length > 0 ? info : null;
        }

        /// <summary>Gibt true zurück, wenn eine neuere Version gefunden wurde.</summary>
        private async Task<bool> CheckForUpdateAsync(bool manual)
        {
            string repo = UpdateRepository();
            if (!IsValidRepository(repo))
            {
                if (manual) TxtUpdateStatus.Text = Loc.T("Für diese Version ist keine Update-Quelle hinterlegt.");
                return false;
            }

            try
            {
                var latest = await FetchLatestReleaseAsync(repo);
                settings.LastUpdateCheck = DateTime.Now;
                SaveSettings();

                if (latest == null)
                {
                    if (manual) TxtUpdateStatus.Text = Loc.T("Es wurde keine Version gefunden.");
                    return false;
                }

                if (CompareAppVersions((latest.Version, latest.Beta), CurrentAppVersion()) > 0)
                {
                    string available = latest.Beta > 0 ? Loc.T($"Vorabversion {latest.Tag} ist verfügbar.") : $"Version {latest.Tag} ist verfügbar.";
                    ShowBanner("update", "⬆", available, "Jetzt aktualisieren", () => ShowUpdateDialog(latest));
                    if (manual) TxtUpdateStatus.Text = available;

                    bool alreadySkipped = settings.SkippedUpdate == latest.Tag;
                    bool alreadyAsked = updatePromptedVersion == latest.Tag;
                    if (manual || (!alreadySkipped && !alreadyAsked && activeSessions.Count == 0))
                    {
                        updatePromptedVersion = latest.Tag;
                        ShowUpdateDialog(latest);
                    }
                    return true;
                }

                RemoveBanner("update");
                if (manual) TxtUpdateStatus.Text = Loc.T("Du hast die neueste Version.");
            }
            catch (Exception ex)
            {
                LogError("Update-Suche", ex);
                if (manual) TxtUpdateStatus.Text = Loc.T("Die Suche ist fehlgeschlagen. Prüfe deine Internetverbindung.");
            }
            return false;
        }

        private bool updateChecking;
        private bool updateCheckedThisSession;
        private readonly DispatcherTimer updateTimer = new();

        private async Task CheckForUpdateIfDueAsync()
        {
#if DEBUG
            await Task.CompletedTask;   // Testversionen aus Visual Studio fragen nicht automatisch nach Updates
#else
            if (updateChecking || !settings.CheckUpdates || !settings.OnlineFeatures) return;

            // Beim ersten Mal nach dem Start wird immer geprüft, danach höchstens alle 15 Minuten
            if (updateCheckedThisSession && (DateTime.Now - settings.LastUpdateCheck).TotalMinutes < 15) return;

            updateChecking = true;
            try
            {
                updateCheckedThisSession = true;
                await CheckForUpdateAsync(false);
            }
            finally
            {
                updateChecking = false;
            }
#endif
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateRepoPanel.Visibility == Visibility.Visible)
            {
                settings.UpdateRepo = TxtUpdateRepo.Text.Trim();
                SaveSettings();
            }

            TxtUpdateStatus.Text = Loc.T("Suche läuft ...");
            await CheckForUpdateAsync(true);
        }

        private void ApplyUpdateUi()
        {
            if (TxtVersion == null) return;

            TxtVersion.Text = Loc.T($"Installierte Version: {VersionLabel()}");
            UpdateRepoPanel.Visibility = Visibility.Collapsed;
            if (TxtAboutVersion != null) TxtAboutVersion.Text = "Version " + VersionLabel();
        }

        private static string CleanReleaseNotes(string markdown)
        {
            string text = (markdown ?? string.Empty).Replace("\r", string.Empty);
            text = Regex.Replace(text, @"^.*Full Changelog.*$\n?", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
            text = Regex.Replace(text, @"https?://\S+", string.Empty);
            text = text.Replace("**", string.Empty).Replace("`", string.Empty);
            text = Regex.Replace(text, @"^#+\s*", string.Empty, RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*[\*\-]\s+", "• ", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
            return text.Length > 1600 ? text.Substring(0, 1600) + " ..." : text;
        }

        private async Task<string> DownloadUpdateAsync(UpdateInfo info, IProgress<(long Done, long Total)> progress, System.Threading.CancellationToken token)
        {
            if (!IsTrustedDownload(info.InstallerUrl, info.Repo))
                throw new InvalidOperationException("Der Download-Link gehört nicht zum Update-Repository.");

            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DFPProLauncher_Update");
            Directory.CreateDirectory(folder);
            string target = System.IO.Path.Combine(folder, $"DFP_Pro_Launcher_Setup_{info.Tag.TrimStart('v', 'V')}.exe");

            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DFPProLauncher");

            using (var response = await client.GetAsync(info.InstallerUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? info.Size;

                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(target);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    progress.Report((done, total));
                }
            }

            // Prüfsumme: ohne passende Prüfsumme wird nichts installiert
            if (info.HashUrl.Length == 0 || !IsTrustedDownload(info.HashUrl, info.Repo))
            {
                TryDelete(target);
                throw new InvalidDataException(Loc.T("Für dieses Update fehlt die Prüfsumme. Aus Sicherheitsgründen wird es nicht installiert."));
            }

            string expected = (await client.GetStringAsync(info.HashUrl, token)).Trim()
                .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;

            string actual;
            await using (var stream = File.OpenRead(target))
                actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, token)).ToLowerInvariant();

            if (expected != actual)
            {
                TryDelete(target);
                throw new InvalidDataException(Loc.T("Die Prüfsumme stimmt nicht. Der Download wurde verworfen."));
            }

            return target;
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch { }
        }

        private void ShowUpdateDialog(UpdateInfo info)
        {
            if (updateDialogOpen) return;
            updateDialogOpen = true;

            try
            {
                // Ist der Launcher minimiert, holen wir ihn nach vorn, damit das Fenster nicht verschwindet
                if (IsVisible && WindowState == WindowState.Minimized) ShowFromTray();

                var dialog = CreateDialog("Update verfügbar", 580, out var panel);
                dialog.Topmost = true;
                dialog.ShowInTaskbar = true;
                var cts = new System.Threading.CancellationTokenSource();
                bool busy = false;

                panel.Children.Add(new TextBlock
                {
                    Text = Loc.T("Update verfügbar"),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 24,
                    FontWeight = FontWeights.Bold
                });

                var versions = new TextBlock
                {
                    Text = $"{VersionText()}   →   {info.Version.Major}.{info.Version.Minor}.{Math.Max(0, info.Version.Build)}{BetaSuffix(info.Beta)}",
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 4)
                };
                versions.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                panel.Children.Add(versions);

                panel.Children.Add(new TextBlock
                {
                    Text = Loc.T("Eine neue Version des DFP Pro Launcher ist bereit. Sie wird direkt hier heruntergeladen und installiert."),
                    Foreground = BrushSubtle,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                });

                panel.Children.Add(new TextBlock
                {
                    Text = Loc.T("Neuigkeiten in dieser Version"),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                string notes = CleanReleaseNotes(info.Notes);
                var notesBox = new Border
                {
                    Background = BrushFooter,
                    BorderBrush = BrushCardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 10, 8, 10),
                    Margin = new Thickness(0, 0, 0, 18),
                    Child = new ScrollViewer
                    {
                        MaxHeight = 190,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            Text = notes.Length == 0 ? Loc.T("Keine Beschreibung vorhanden.") : notes,
                            Foreground = MakeBrush("#D1D5DB"),
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 8, 0)
                        }
                    }
                };
                panel.Children.Add(notesBox);

                // Fortschritt
                var status = new TextBlock { Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
                var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = MakeBrush("#1F2937") };
                var fill = new Border { Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Width = 0 };
                fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                var bar = new Grid { Margin = new Thickness(0, 0, 0, 18), Visibility = Visibility.Collapsed };
                bar.Children.Add(track);
                bar.Children.Add(fill);
                panel.Children.Add(status);
                panel.Children.Add(bar);

                var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var skip = new System.Windows.Controls.Button { Content = Loc.T("Diese Version überspringen"), Margin = new Thickness(0, 0, 10, 0) };
                var later = new System.Windows.Controls.Button { Content = Loc.T("Später"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
                var install = new System.Windows.Controls.Button { Content = Loc.T("Jetzt aktualisieren"), Padding = new Thickness(26, 8, 26, 8), IsDefault = true };
                install.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                buttons.Children.Add(skip);
                buttons.Children.Add(later);
                buttons.Children.Add(install);
                panel.Children.Add(buttons);

                skip.Click += (s, e) =>
                {
                    settings.SkippedUpdate = info.Tag;
                    SaveSettings();
                    RemoveBanner("update");
                    dialog.DialogResult = true;
                };

                later.Click += (s, e) =>
                {
                    if (busy) cts.Cancel();
                    else dialog.DialogResult = false;
                };

                dialog.Closing += (s, e) =>
                {
                    if (busy) e.Cancel = true;   // während des Downloads nicht schließen
                };

                install.Click += async (s, e) =>
                {
                    if (activeSessions.Count > 0)
                    {
                        Msg("Es läuft noch ein Spiel. Installiere das Update nach dem Spiel.", "Update verfügbar", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    busy = true;
                    install.IsEnabled = false;
                    skip.IsEnabled = false;
                    later.Content = Loc.T("Abbrechen");
                    status.Visibility = Visibility.Visible;
                    bar.Visibility = Visibility.Visible;
                    status.Text = Loc.T("Lade herunter ... 0 %");

                    var progress = new Progress<(long Done, long Total)>(p =>
                    {
                        double fraction = p.Total > 0 ? Math.Clamp((double)p.Done / p.Total, 0, 1) : 0;
                        fill.Width = bar.ActualWidth * fraction;
                        status.Text = Loc.T($"Lade herunter ... {(int)(fraction * 100)} %");
                    });

                    try
                    {
                        string installer = await DownloadUpdateAsync(info, progress, cts.Token);

                        status.Text = Loc.T("Starte die Installation. Der Launcher schließt sich kurz und öffnet sich danach von selbst wieder.");
                        fill.Width = bar.ActualWidth;
                        SaveSettings();
                        await Task.Run(() => CreateSettingsBackup("update"));   // vor jedem Update sichern
                        await Task.Delay(1200);

                        Process.Start(new ProcessStartInfo(installer, "/SILENT /NORESTART /CLOSEAPPLICATIONS /SUPPRESSMSGBOXES") { UseShellExecute = true });

                        busy = false;
                        dialog.DialogResult = true;
                        Dispatcher.BeginInvoke(new Action(ExitApplication), DispatcherPriority.Background);
                    }
                    catch (OperationCanceledException)
                    {
                        busy = false;
                        dialog.DialogResult = false;
                    }
                    catch (Exception ex)
                    {
                        LogError("Update", ex);
                        busy = false;
                        install.IsEnabled = true;
                        skip.IsEnabled = true;
                        later.Content = Loc.T("Später");
                        bar.Visibility = Visibility.Collapsed;
                        status.Text = Loc.T("Das Update konnte nicht geladen werden:") + " " + ex.Message;
                    }
                };

                dialog.ShowDialog();
            }
            finally
            {
                updateDialogOpen = false;
            }
        }

        private void InitExtras9()
        {
            ApplyUpdateUi();

            updateTimer.Interval = TimeSpan.FromMinutes(15);
            updateTimer.Tick += async (s, e) => await CheckForUpdateIfDueAsync();
            updateTimer.Start();

            Loaded += async (s, e) =>
            {
                await Task.Delay(6000);
                await CheckForUpdateIfDueAsync();
            };
        }

        // ───────────────────────────── Sicherungsordner (zum Beispiel OneDrive) ─────────────────────────────

        private string? CloudRoot()
        {
            string folder = settings.CloudBackupFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
            return System.IO.Path.Combine(folder, "DFP Pro Launcher");
        }

        private void MirrorToCloud(string file, string subfolder)
        {
            try
            {
                string? root = CloudRoot();
                if (root == null || !File.Exists(file)) return;

                string target = System.IO.Path.Combine(root, subfolder);
                Directory.CreateDirectory(target);
                File.Copy(file, System.IO.Path.Combine(target, System.IO.Path.GetFileName(file)), true);
            }
            catch { }
        }

        private void MirrorSettingsBackup()
        {
            try
            {
                if (!Directory.Exists(SettingsBackupDir)) return;
                string? latest = SettingsBackupFiles().FirstOrDefault();
                if (latest != null) MirrorToCloud(latest, "Einstellungen");
            }
            catch { }
        }

        private void UpdateCloudFolderText()
        {
            if (TxtCloudFolder == null) return;
            TxtCloudFolder.Text = string.IsNullOrWhiteSpace(settings.CloudBackupFolder) ? Loc.T("Kein Ordner festgelegt") : settings.CloudBackupFolder;
        }

        private void BtnPickCloudFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = Loc.T("Ordner für Sicherungen auswählen (zum Beispiel in OneDrive oder Dropbox)"),
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;

            settings.CloudBackupFolder = dialog.SelectedPath;
            SaveSettings();
            UpdateCloudFolderText();
            BtnBackupAllNow_Click(sender, e);
        }

        private void BtnClearCloudFolder_Click(object sender, RoutedEventArgs e)
        {
            settings.CloudBackupFolder = string.Empty;
            SaveSettings();
            UpdateCloudFolderText();
        }

        private async void BtnBackupAllNow_Click(object sender, RoutedEventArgs e)
        {
            string? root = CloudRoot();
            if (root == null)
            {
                Msg("Wähle zuerst einen Sicherungsordner.", "Sicherung");
                return;
            }

            try
            {
                SaveSettings();
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(System.IO.Path.Combine(root, "Einstellungen"));
                    if (File.Exists(SettingsFilePath))
                        File.Copy(SettingsFilePath, System.IO.Path.Combine(root, "Einstellungen", "settings.json"), true);

                    string saves = System.IO.Path.Combine(SettingsDir, "backups");
                    if (!Directory.Exists(saves)) return;

                    foreach (string file in Directory.GetFiles(saves, "*.zip", SearchOption.AllDirectories))
                    {
                        string relative = System.IO.Path.GetRelativePath(saves, file);
                        string target = System.IO.Path.Combine(root, "Spielstaende", relative);
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                        if (!File.Exists(target)) File.Copy(file, target);
                    }
                });
                ShowToast("☁", "Sicherung abgeschlossen", settings.CloudBackupFolder, 5);
            }
            catch (Exception ex)
            {
                Msg($"Sichern fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────────────────────────── Discord-Benachrichtigung (eigener Webhook) ─────────────────────────────

        private static bool IsDiscordWebhook(string? url)
            => Regex.IsMatch(url ?? string.Empty, @"^https://(?:(?:ptb|canary)\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w\-]+$");

        private int AccentColorInt()
        {
            var color = GetAccentColor();
            return (color.R << 16) | (color.G << 8) | color.B;
        }

        private async Task<string?> PostDiscordAsync(string title, string description, string? thumbnail)
        {
            var embed = new Dictionary<string, object>
            {
                ["title"] = title,
                ["description"] = description,
                ["color"] = AccentColorInt(),
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["footer"] = new Dictionary<string, object> { ["text"] = "DFP Pro Launcher" }
            };
            if (!string.IsNullOrEmpty(thumbnail))
                embed["thumbnail"] = new Dictionary<string, object> { ["url"] = thumbnail };

            var payload = new Dictionary<string, object>
            {
                ["username"] = "DFP Pro Launcher",
                ["embeds"] = new[] { embed }
            };

            using var content = new System.Net.Http.StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(settings.DiscordWebhook.Trim(), content);
            return response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}";
        }

        private string DiscordUser() => settings.DiscordSendName ? settings.UserName : Loc.T("Jemand");

        private async Task SendDiscordAsync(string title, string description, GameItem? game)
        {
            if (!settings.DiscordEnabled || !IsDiscordWebhook(settings.DiscordWebhook)) return;

            try
            {
                string? thumbnail = game != null && game.Source == "Steam" && game.AppId.Length > 0
                    ? $"https://cdn.akamai.steamstatic.com/steam/apps/{game.AppId}/header.jpg"
                    : null;

                string? error = await PostDiscordAsync(title, description, thumbnail);
                if (error != null) LogError("Discord", new InvalidOperationException(error));
            }
            catch (Exception ex)
            {
                LogError("Discord", ex);
            }
        }

        private void DiscordSessionStarted(GameItem game)
        {
            if (!settings.DiscordEnabled) return;

            discordStarts[game] = DateTime.Now;
            if (!settings.DiscordOnStart) return;

            _ = SendDiscordAsync("🎮 " + Loc.T($"{DiscordUser()} spielt jetzt {game.Name}"),
                Loc.T($"Quelle: {game.Source}"), game);
        }

        private void DiscordSessionEnded(GameItem game)
        {
            if (!discordStarts.TryGetValue(game, out var started)) return;
            discordStarts.Remove(game);

            if (!settings.DiscordEnabled || !settings.DiscordOnEnd) return;

            string duration = FormatPlaytime((long)(DateTime.Now - started).TotalSeconds);
            _ = SendDiscordAsync("🏁 " + Loc.T($"{DiscordUser()} hat {game.Name} beendet"),
                Loc.T($"Spielzeit dieser Sitzung: {duration}"), game);
        }

        private void BtnDiscordSave_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtDiscordWebhook.Text.Trim();
            if (url.Length > 0 && !IsDiscordWebhook(url))
            {
                TxtDiscordStatus.Text = Loc.T("Das ist keine gültige Discord-Webhook-Adresse.");
                return;
            }

            settings.DiscordWebhook = url;
            SaveSettings();
            TxtDiscordStatus.Text = url.Length == 0 ? Loc.T("Webhook entfernt.") : Loc.T("Gespeichert.");
        }

        private async void BtnDiscordTest_Click(object sender, RoutedEventArgs e)
        {
            BtnDiscordSave_Click(sender, e);
            if (!IsDiscordWebhook(settings.DiscordWebhook)) return;

            TxtDiscordStatus.Text = Loc.T("Sende Test-Nachricht ...");
            try
            {
                string? error = await PostDiscordAsync("✅ " + Loc.T("Test erfolgreich"),
                    Loc.T("Dein DFP Pro Launcher ist mit diesem Discord-Kanal verbunden."), null);
                TxtDiscordStatus.Text = error == null
                    ? Loc.T("Test-Nachricht gesendet. Schau in deinen Discord-Kanal.")
                    : Loc.T("Discord hat die Nachricht abgelehnt:") + " " + error;
            }
            catch
            {
                TxtDiscordStatus.Text = Loc.T("Senden fehlgeschlagen. Prüfe deine Internetverbindung.");
            }
        }

        // ═════════════════════════════ Fehlerbericht und Beta-Kanal ═════════════════════════════

        private DateTime lastReportPrompt = DateTime.MinValue;
        private bool reportDialogOpen;

        private void InitExtras21()
        {
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                // Zusätzlich zum vorhandenen Protokoll: nach dem Fehler (nicht mittendrin) einen Bericht anbieten
                app.DispatcherUnhandledException += (s, e) =>
                {
                    var error = e.Exception;
                    Dispatcher.BeginInvoke(new Action(() => OfferErrorReport("Oberfläche", error)), DispatcherPriority.ApplicationIdle);
                };
            }

            // Absturz: Der Launcher kann nicht mehr fragen, darum den Bericht für den nächsten Start merken
            AppDomain.CurrentDomain.UnhandledException += (s, e) => SavePendingReport(BuildErrorReport("Programm (Absturz)", e.ExceptionObject as Exception));

            Loaded += async (s, e) =>
            {
                await Task.Delay(8000);
                OfferPendingReport();
            };
        }

        /// <summary>Entfernt private Angaben: Benutzer- und PC-Namen, Pfade im Benutzerordner, Links, E-Mail- und IP-Adressen.</summary>
        private string SanitizeReport(string text)
        {
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (profile.Length > 3) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
                text = Regex.Replace(text, @"[A-Za-z]:\\Users\\[^\\\s""']+", @"C:\Users\<Benutzer>", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"https?://\S+", "<Link>");
                text = Regex.Replace(text, @"[\w.+\-]+@[\w\-]+\.[\w.\-]+", "<E-Mail>");
                text = Regex.Replace(text, @"(?<!Version=)(?<![\d.])\b(?:\d{1,3}\.){3}\d{1,3}\b", "<IP>");

                var tokens = new List<string> { Environment.UserName, Environment.MachineName, Environment.UserDomainName, settings.UserName };
                foreach (string token in tokens.Where(t => !string.IsNullOrWhiteSpace(t) && t.Trim().Length >= 3 && !t.Equals("Gamer", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
                    text = Regex.Replace(text, Regex.Escape(token.Trim()), "<privat>", RegexOptions.IgnoreCase);
            }
            catch { }
            return text;
        }

        private string BuildErrorReport(string source, Exception? error)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("DFP Pro Launcher – Fehlerbericht");
            text.AppendLine($"Version: {VersionText()} | Windows {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor} (Build {Environment.OSVersion.Version.Build}) | Sprache: {Loc.Language}");
            text.AppendLine($"Zeit: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Bereich: {source}");
            text.AppendLine();

            int depth = 0;
            for (var current = error; current != null && depth < 3; current = current.InnerException, depth++)
            {
                if (depth > 0) text.AppendLine("--- Ursache ---");
                text.AppendLine($"{current.GetType().FullName}: {current.Message}");
                if (!string.IsNullOrEmpty(current.StackTrace)) text.AppendLine(current.StackTrace);
            }
            if (error == null) text.AppendLine("(keine Angaben zum Fehler)");

            return SanitizeReport(text.ToString().Trim());
        }

        private static void SavePendingReport(string report)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                File.WriteAllText(PendingReportPath, report);
            }
            catch { }
        }

        private bool ErrorReportsPossible => settings.ErrorReports && settings.OnlineFeatures && IsDiscordWebhook(settings.DiscordWebhook);

        private void OfferErrorReport(string source, Exception error)
        {
            if (!ErrorReportsPossible || reportDialogOpen) return;
            if ((DateTime.Now - lastReportPrompt).TotalMinutes < 5) return;   // nicht bei jedem Folgefehler fragen

            string report = BuildErrorReport(source, error);

            // Im Controller-Modus oder während eines Spiels nicht stören: beim nächsten Start fragen
            if (controllerMode || activeSessions.Count > 0)
            {
                SavePendingReport(report);
                return;
            }

            lastReportPrompt = DateTime.Now;
            ShowErrorReportDialog(report, false);
        }

        private void OfferPendingReport()
        {
            try
            {
                if (!File.Exists(PendingReportPath)) return;
                string report = File.ReadAllText(PendingReportPath);
                File.Delete(PendingReportPath);   // nur einmal fragen
                if (!ErrorReportsPossible || report.Trim().Length == 0) return;

                lastReportPrompt = DateTime.Now;
                ShowErrorReportDialog(report, true);
            }
            catch { }
        }

        /// <summary>Vorschau mit genau dem Text, der gesendet würde. Gesendet wird nur nach Klick auf „Senden“.</summary>
        private void ShowErrorReportDialog(string report, bool fromLastRun)
        {
            reportDialogOpen = true;
            try
            {
                var dialog = CreateDialog("Fehlerbericht senden?", 660, out var panel);

                panel.Children.Add(new TextBlock
                {
                    Text = Loc.T(fromLastRun
                        ? "Beim letzten Mal wurde der Launcher wegen eines Fehlers beendet. Möchtest du den Bericht an deinen Discord-Webhook senden? Private Angaben sind entfernt. Das wird gesendet:"
                        : "Im Launcher ist ein unerwarteter Fehler aufgetreten. Möchtest du den Bericht an deinen Discord-Webhook senden? Private Angaben sind entfernt. Das wird gesendet:"),
                    Foreground = MakeBrush("#D1D5DB"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var preview = new System.Windows.Controls.TextBox
                {
                    Text = report,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 260,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Top,
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                panel.Children.Add(preview);

                var stop = new System.Windows.Controls.CheckBox { Content = Loc.T("Nicht mehr nachfragen"), Margin = new Thickness(0, 0, 0, 16) };
                panel.Children.Add(stop);

                bool send = false;
                var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var cancel = new System.Windows.Controls.Button { Content = Loc.T("Nicht senden"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true, IsDefault = true };
                var ok = new System.Windows.Controls.Button { Content = Loc.T("Senden"), Padding = new Thickness(26, 8, 26, 8) };
                ok.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                ok.Click += (s, e) =>
                {
                    send = true;
                    dialog.DialogResult = true;
                };
                buttons.Children.Add(cancel);
                buttons.Children.Add(ok);
                panel.Children.Add(buttons);

                dialog.ShowDialog();

                if (stop.IsChecked == true)
                {
                    settings.ErrorReports = false;
                    SaveSettings();
                    if (ChkErrorReports != null)
                    {
                        bool before = isLoadingSettings;
                        isLoadingSettings = true;
                        try { ChkErrorReports.IsChecked = false; }
                        finally { isLoadingSettings = before; }
                    }
                }
                if (send) _ = SendErrorReportAsync(report);
            }
            finally
            {
                reportDialogOpen = false;
            }
        }

        private async Task SendErrorReportAsync(string report)
        {
            string url = settings.DiscordWebhook.Trim();
            if (!IsDiscordWebhook(url)) return;

            // Nachricht auf dem Oberflächen-Thread zusammenbauen, senden im Hintergrund
            string body = report.Length > 3800 ? report.Substring(0, 3800) + "\n…" : report;
            var payload = new Dictionary<string, object>
            {
                ["username"] = "DFP Pro Launcher",
                ["embeds"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["title"] = "⚠ Fehlerbericht",
                        ["description"] = "```\n" + body.Replace("```", "'''") + "\n```",
                        ["color"] = 0xEF4444,
                        ["timestamp"] = DateTime.UtcNow.ToString("o")
                    }
                }
            };
            string json = JsonSerializer.Serialize(payload);

            bool ok = await Task.Run(async () =>
            {
                try
                {
                    using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    using var response = await Http.PostAsync(url, content);
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            });

            ShowToast(ok ? "✅" : "⚠", "Fehlerbericht", ok ? "Der Bericht wurde gesendet. Danke!" : "Der Bericht konnte nicht gesendet werden.", 5, null, true);
        }

        private void ChkErrorReports_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            settings.ErrorReports = ChkErrorReports.IsChecked == true;
            SaveSettings();
        }

        private void ChkBetaUpdates_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            settings.BetaUpdates = ChkBetaUpdates.IsChecked == true;
            SaveSettings();

            if (settings.BetaUpdates && settings.OnlineFeatures)
            {
                TxtUpdateStatus.Text = Loc.T("Suche läuft ...");
                _ = CheckForUpdateAsync(true);
            }
            else
            {
                TxtUpdateStatus.Text = settings.BetaUpdates ? string.Empty : Loc.T("Du bekommst wieder nur normale Versionen.");
            }
        }

        private void PopulateReportAndBeta()
        {
            if (ChkErrorReports == null) return;
            ChkErrorReports.IsChecked = settings.ErrorReports;
            ChkBetaUpdates.IsChecked = settings.BetaUpdates;
        }
    }
}
