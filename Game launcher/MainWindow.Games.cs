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
        // ───────────────────────────── Spiele-Scan ─────────────────────────────

        private async Task LoadInstalledGamesAsync()
        {
            if (isScanning) return;
            isScanning = true;
            TxtGameCount.Text = "Spiele werden gesucht ...";

            List<GameItem> games;
            try
            {
                var manualGames = settings.ManualGames.ToList();

                games = await Task.Run(() =>
                {
                    var found = new Dictionary<string, GameItem>(StringComparer.OrdinalIgnoreCase);

                    ScanSteamGames(found);
                    ScanEpicGames(found, LoadEpicCoverMap());
                    ScanRockstarGames(found);
                    ScanXboxGames(found);
                    ScanGogGames(found);
                    ScanEaGames(found);
                    ScanUbisoftGames(found);
                    ScanBattleNetGames(found);

                    foreach (var manual in manualGames)
                    {
                        if (string.IsNullOrWhiteSpace(manual.Name) || found.ContainsKey(manual.Name)) continue;

                        found[manual.Name] = new GameItem
                        {
                            Name = manual.Name,
                            ExecutablePath = manual.ExecutablePath,
                            Source = "Manuell",
                            LocalIcon = ExtractIcon(manual.ExecutablePath)
                        };
                    }

                    foreach (var game in found.Values) AssignCachedCover(game);
                    return found.Values.ToList();
                });
            }
            finally
            {
                isScanning = false;
            }

            ApplyGameStates(games);
            allGames = games;

            playCardEntrance = !silentRescan;
            ApplyFilter();
            RefreshDashboard();

            _ = DownloadCoversAsync(games);
            _ = AfterScanAsync();
        }

        private static bool IsLikelyGameExe(string file)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            string[] unwanted =
            {
                "unins", "setup", "crash", "redist", "dxsetup", "installer",
                "helper", "report", "easyanticheat", "battleye"
            };
            return !unwanted.Any(u => name.Contains(u));
        }

        /// <summary>Sucht die wahrscheinlichste Haupt-.exe (erst im Hauptordner, dann bis Tiefe 3).</summary>
        private static string FindMainExe(string directory)
        {
            try
            {
                var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
                var candidates = Directory.EnumerateFiles(directory, "*.exe", options).Where(IsLikelyGameExe).ToList();

                if (candidates.Count == 0)
                {
                    options.RecurseSubdirectories = true;
                    options.MaxRecursionDepth = 3;
                    candidates = Directory.EnumerateFiles(directory, "*.exe", options).Where(IsLikelyGameExe).ToList();
                }

                return candidates.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static ImageSource? ExtractIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;

                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze(); // nötig, weil ggf. im Hintergrund-Thread erzeugt
                return source;
            }
            catch
            {
                return null;
            }
        }

        private static List<string> SteamCoverUrls(string appId) => new()
        {
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg",
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg"
        };

        private static void ScanSteamGames(Dictionary<string, GameItem> found)
        {
            try
            {
                string? steamPath =
                    Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                    ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;

                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return;

                var libraries = new List<string> { System.IO.Path.GetFullPath(System.IO.Path.Combine(steamPath, "steamapps")) };
                string vdfFile = System.IO.Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

                if (File.Exists(vdfFile))
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdfFile), @"""path""\s+""([^""]+)"""))
                    {
                        string libraryRoot = match.Groups[1].Value.Replace(@"\\", @"\");
                        string steamApps = System.IO.Path.GetFullPath(System.IO.Path.Combine(libraryRoot, "steamapps"));

                        if (Directory.Exists(steamApps) && !libraries.Contains(steamApps, StringComparer.OrdinalIgnoreCase))
                            libraries.Add(steamApps);
                    }
                }

                foreach (string library in libraries)
                {
                    if (!Directory.Exists(library)) continue;

                    foreach (string acf in Directory.EnumerateFiles(library, "appmanifest_*.acf"))
                    {
                        try
                        {
                            string text = File.ReadAllText(acf);
                            var nameMatch = Regex.Match(text, @"""name""\s+""([^""]+)""");
                            var dirMatch = Regex.Match(text, @"""installdir""\s+""([^""]+)""");
                            var idMatch = Regex.Match(text, @"""appid""\s+""([^""]+)""");

                            if (!nameMatch.Success || !dirMatch.Success || !idMatch.Success) continue;

                            string gameName = nameMatch.Groups[1].Value;
                            if (gameName.StartsWith("Steamworks") || gameName.StartsWith("Proton") ||
                                gameName.StartsWith("Steam Linux Runtime") || gameName.Contains("Redistributable"))
                                continue;

                            string installDir = System.IO.Path.Combine(library, "common", dirMatch.Groups[1].Value);
                            if (!Directory.Exists(installDir) || found.ContainsKey(gameName)) continue;

                            string appId = idMatch.Groups[1].Value;
                            string exePath = FindMainExe(installDir);

                            found[gameName] = new GameItem
                            {
                                Name = gameName,
                                ExecutablePath = exePath,
                                InstallDir = installDir,
                                AppId = appId,
                                LaunchUri = $"steam://rungameid/{appId}",
                                CoverUrls = SteamCoverUrls(appId),
                                Source = "Steam",
                                LocalIcon = ExtractIcon(exePath)
                            };
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>Liest die Cover-Adressen aus dem Katalog-Cache des Epic Games Launchers.</summary>
        private static Dictionary<string, string> LoadEpicCoverMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string file = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Epic", "EpicGamesLauncher", "Data", "Catalog", "catcache.bin");
                if (!File.Exists(file)) return map;

                string base64;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    base64 = reader.ReadToEnd().Trim();
                }

                using var doc = JsonDocument.Parse(Convert.FromBase64String(base64));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;

                string[] preferredTypes = { "DieselGameBoxTall", "OfferImageTall", "Thumbnail" };

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    string id = GetJsonString(item, "id");
                    if (id.Length == 0 || !item.TryGetProperty("keyImages", out var images) || images.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (string wanted in preferredTypes)
                    {
                        string url = string.Empty;
                        foreach (var image in images.EnumerateArray())
                        {
                            if (GetJsonString(image, "type") == wanted)
                            {
                                url = GetJsonString(image, "url");
                                break;
                            }
                        }

                        if (url.Length > 0)
                        {
                            map[id] = url;
                            break;
                        }
                    }
                }
            }
            catch { }

            return map;
        }

        private static void ScanEpicGames(Dictionary<string, GameItem> found, Dictionary<string, string> coverMap)
        {
            try
            {
                string manifestDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Epic", "EpicGamesLauncher", "Data", "Manifests");

                if (!Directory.Exists(manifestDir)) return;

                foreach (string itemFile in Directory.EnumerateFiles(manifestDir, "*.item"))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(itemFile));
                        var root = document.RootElement;

                        string gameName = GetJsonString(root, "DisplayName");
                        string launchExe = GetJsonString(root, "LaunchExecutable");
                        string installDir = GetJsonString(root, "InstallLocation");
                        string appName = GetJsonString(root, "AppName");
                        string catalogId = GetJsonString(root, "CatalogItemId");

                        if (gameName == "" || launchExe == "" || installDir == "" || found.ContainsKey(gameName)) continue;

                        string fullExePath = System.IO.Path.Combine(installDir, launchExe);
                        if (!File.Exists(fullExePath)) continue;

                        var game = new GameItem
                        {
                            Name = gameName,
                            ExecutablePath = fullExePath,
                            InstallDir = installDir,
                            LaunchUri = appName == ""
                                ? null
                                : $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true",
                            Source = "Epic Games",
                            LocalIcon = ExtractIcon(fullExePath)
                        };

                        if (catalogId.Length > 0 && coverMap.TryGetValue(catalogId, out var coverUrl))
                            game.CoverUrls.Add(coverUrl);

                        found[gameName] = game;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void ScanRockstarGames(Dictionary<string, GameItem> found)
        {
            try
            {
                using var rootKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Rockstar Games");
                if (rootKey == null) return;

                foreach (string subkeyName in rootKey.GetSubKeyNames())
                {
                    if (subkeyName.Contains("Launcher", StringComparison.OrdinalIgnoreCase) ||
                        subkeyName.Contains("Social Club", StringComparison.OrdinalIgnoreCase) ||
                        found.ContainsKey(subkeyName))
                        continue;

                    using var subKey = rootKey.OpenSubKey(subkeyName);
                    if (subKey == null) continue;

                    string? folder = subKey.GetValue("InstallFolder") as string ?? subKey.GetValue("FolderHandled") as string;
                    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;

                    // GTA V muss über PlayGTAV.exe gestartet werden
                    string exePath = System.IO.Path.Combine(folder, "PlayGTAV.exe");
                    if (!File.Exists(exePath))
                    {
                        exePath = Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                            .Where(f => IsLikelyGameExe(f)
                                     && !System.IO.Path.GetFileNameWithoutExtension(f).Contains("launcher", StringComparison.OrdinalIgnoreCase)
                                     && !System.IO.Path.GetFileNameWithoutExtension(f).Contains("social", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => new FileInfo(f).Length)
                            .FirstOrDefault() ?? string.Empty;
                    }

                    if (!File.Exists(exePath)) continue;

                    found[subkeyName] = new GameItem
                    {
                        Name = subkeyName,
                        ExecutablePath = exePath,
                        InstallDir = folder,
                        Source = "Rockstar",
                        LocalIcon = ExtractIcon(exePath)
                    };
                }
            }
            catch { }
        }

        /// <summary>Findet Spiele der Xbox-App / des Microsoft Stores (Game Pass).</summary>
        private static void ScanXboxGames(Dictionary<string, GameItem> found)
        {
            try
            {
                var result = RunPowerShell(XboxScript, 60000);
                int start = result.Output.IndexOf('[');
                int end = result.Output.LastIndexOf(']');
                if (start < 0 || end <= start) return;

                using var doc = JsonDocument.Parse(result.Output.Substring(start, end - start + 1));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

                foreach (var package in doc.RootElement.EnumerateArray())
                {
                    try
                    {
                        string location = GetJsonString(package, "Location");
                        string family = GetJsonString(package, "Family");
                        string full = GetJsonString(package, "Full");
                        string configPath = System.IO.Path.Combine(location, "MicrosoftGame.config");

                        if (location.Length == 0 || family.Length == 0 || !File.Exists(configPath)) continue;

                        var config = System.Xml.Linq.XDocument.Load(configPath).Root;
                        var visuals = config?.Element("ShellVisuals");
                        var executable = config?.Element("ExecutableList")?.Elements("Executable").FirstOrDefault();

                        string name = visuals?.Attribute("DefaultDisplayName")?.Value ?? string.Empty;
                        if (name.Length == 0 || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                        {
                            // Anzeigename steht dann im Ordnernamen (z.B. D:\XboxGames\Forza Horizon 5\Content)
                            name = string.Equals(System.IO.Path.GetFileName(location), "Content", StringComparison.OrdinalIgnoreCase)
                                ? new DirectoryInfo(location).Parent?.Name ?? GetJsonString(package, "Name")
                                : GetJsonString(package, "Name");
                        }
                        if (name.Length == 0 || found.ContainsKey(name)) continue;

                        string exeName = executable?.Attribute("Name")?.Value ?? string.Empty;
                        string appId = executable?.Attribute("Id")?.Value ?? "Game";
                        string exePath = exeName.Length > 0 ? System.IO.Path.Combine(location, exeName) : string.Empty;

                        string fallbackCover = string.Empty;
                        foreach (string attribute in new[] { "Square480x480Logo", "Square150x150Logo", "StoreLogo" })
                        {
                            string? logo = visuals?.Attribute(attribute)?.Value;
                            if (string.IsNullOrEmpty(logo)) continue;

                            string candidate = System.IO.Path.Combine(location, logo);
                            if (File.Exists(candidate))
                            {
                                fallbackCover = candidate;
                                break;
                            }
                        }

                        found[name] = new GameItem
                        {
                            Name = name,
                            ExecutablePath = exePath,
                            InstallDir = location,
                            LaunchUri = $"shell:AppsFolder\\{family}!{appId}",
                            PackageFullName = full,
                            FallbackCoverPath = fallbackCover,
                            Source = "Xbox",
                            LocalIcon = File.Exists(exePath) ? ExtractIcon(exePath) : null
                        };
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ───────────────────────────── Cover (immer in bester Auflösung) ─────────────────────────────

        private static string CoverFilePath(GameItem game)
        {
            string safe = Regex.Replace($"{game.Source}_{game.Name}", @"[^\w\-]+", "_");
            if (safe.Length > 100) safe = safe.Substring(0, 100);
            return System.IO.Path.Combine(CoversDir, safe + ".jpg");
        }

        private static void AssignCachedCover(GameItem game)
        {
            try
            {
                string? cached = FindCachedCover(game);
                if (cached != null) game.CoverPath = cached;
            }
            catch { }
        }

        private static string NormalizeTitle(string title)
            => Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]", string.Empty);

        /// <summary>Sucht ein Spiel im Steam-Shop und gibt die AppID zurück (leer = nicht gefunden).</summary>
        private async Task<string> FindSteamAppIdAsync(string name)
        {
            lock (settings.SteamIds)
            {
                if (settings.SteamIds.TryGetValue(name, out int cached))
                    return cached > 0 ? cached.ToString() : string.Empty;
            }

            string id = string.Empty;
            try
            {
                string url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(name)}&l=german&cc=DE";
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));

                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    string wanted = NormalizeTitle(name);
                    foreach (var item in items.EnumerateArray())
                    {
                        if (NormalizeTitle(GetJsonString(item, "name")) != wanted) continue;

                        if (item.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out int number))
                        {
                            id = number.ToString();
                            break;
                        }
                    }
                }

                // Auch "nicht gefunden" merken, damit nicht bei jedem Start erneut gesucht wird
                lock (settings.SteamIds)
                {
                    settings.SteamIds[name] = id.Length > 0 ? int.Parse(id) : -1;
                }
            }
            catch
            {
                // Netzwerkfehler: nicht merken, beim nächsten Mal erneut versuchen
            }

            return id;
        }

        private static async Task<bool> TryDownloadAsync(IEnumerable<string> urls, string targetFile)
        {
            foreach (string url in urls)
            {
                try
                {
                    using var response = await Http.GetAsync(url);
                    if (!response.IsSuccessStatusCode) continue;

                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    if (bytes.Length < 1500) continue;

                    await File.WriteAllBytesAsync(targetFile, bytes);
                    return true;
                }
                catch { }
            }
            return false;
        }

        private async Task FetchCoverAsync(GameItem game)
        {
            string file = CoverFilePath(game);

            IEnumerable<string> Allowed(IEnumerable<string> urls) =>
                settings.FullResCovers ? urls : urls.Where(u => !u.Contains("_2x", StringComparison.Ordinal));

            if (settings.OnlineFeatures)
            {
                var sgdb = await FindSgdbCoverAsync(game);
                if (sgdb != null)
                {
                    string gridExt = GridExtension(sgdb.Value.Url);
                    string targetExt = sgdb.Value.Animated ? (gridExt == ".png" ? ".apng" : gridExt) : ".jpg";
                    string target = System.IO.Path.ChangeExtension(file, targetExt);
                    if (await TryDownloadAsync(new[] { sgdb.Value.Url }, target))
                    {
                        RemoveOtherCoverVariants(game, target);
                        game.CoverPath = target;
                        return;
                    }
                }

                if (game.CoverUrls.Count > 0 && await TryDownloadAsync(Allowed(game.CoverUrls), file))
                {
                    game.CoverPath = file;
                    return;
                }

                // Nicht-Steam-Spiele: passendes Cover über den Steam-Shop finden
                if (game.Source != "Steam")
                {
                    string appId = await FindSteamAppIdAsync(game.Name);
                    if (appId.Length > 0 && await TryDownloadAsync(Allowed(SteamCoverUrls(appId)), file))
                    {
                        game.CoverPath = file;
                        return;
                    }
                }
            }

            if (game.FallbackCoverPath.Length > 0 && File.Exists(game.FallbackCoverPath))
                game.CoverPath = game.FallbackCoverPath;
        }

        private async Task DownloadCoversAsync(List<GameItem> games)
        {
            var pending = games.Where(g => string.IsNullOrEmpty(g.CoverPath)).ToList();
            if (pending.Count == 0) return;

            try { Directory.CreateDirectory(CoversDir); } catch { }

            int completed = 0;
            coversLoading = true;
            using var gate = new System.Threading.SemaphoreSlim(4);

            var tasks = pending.Select(async game =>
            {
                await gate.WaitAsync();
                try
                {
                    await FetchCoverAsync(game);
                }
                catch { }
                finally
                {
                    gate.Release();

                    int done = System.Threading.Interlocked.Increment(ref completed);
                    coversLoading = done < pending.Count;
                    if (done % 8 == 0 || done == pending.Count)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ApplyFilter();
                            RefreshDashboard();
                        });
                    }
                }
            }).ToList();

            await Task.WhenAll(tasks);
            SaveSettings();
        }

        private BitmapImage? GetCachedImage(string path)
        {
            string key = $"{path}|{settings.FullResCovers}|{settings.PerformanceMode}";
            if (imageCache.TryGetValue(key, out var cached)) return cached;

            try
            {
                if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    var still = DecodeStillWithImageSharp(path, settings.PerformanceMode ? 320 : settings.FullResCovers ? 0 : 600);
                    if (still != null) imageCache[key] = still;
                    return still;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (settings.PerformanceMode) image.DecodePixelWidth = 320;
                else if (!settings.FullResCovers) image.DecodePixelWidth = 600;
                image.EndInit();
                image.Freeze();

                imageCache[key] = image;
                return image;
            }
            catch
            {
                return null;
            }
        }

        // ───────────────────────────── Spiele anzeigen, starten, deinstallieren ─────────────────────────────

        private void LaunchGame(GameItem game)
        {
            if (!ConfirmBeforeLaunch(game)) return;

            PlayUiSound("launch");
            try
            {
                if (!string.IsNullOrEmpty(game.LaunchUri))
                {
                    Process.Start(new ProcessStartInfo(game.LaunchUri) { UseShellExecute = true });
                }
                else if (File.Exists(game.ExecutablePath))
                {
                    var startInfo = new ProcessStartInfo(game.ExecutablePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = System.IO.Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty
                    };
                    if (!string.IsNullOrWhiteSpace(game.LaunchArgs)) startInfo.Arguments = game.LaunchArgs;
                    if (game.RunAsAdmin) startInfo.Verb = "runas";
                    Process.Start(startInfo);
                }
                else
                {
                    Msg($"Die Datei wurde nicht gefunden:\n{game.ExecutablePath}", "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return; // Administrator-Abfrage abgebrochen
            }
            catch (Exception ex)
            {
                Msg($"Spiel konnte nicht gestartet werden:\n{ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RegisterLaunch(game);
        }

        private void RegisterLaunch(GameItem game)
        {
            StartCompanions(game);

            game.LastPlayed = DateTime.Now;
            game.LaunchCount++;
            SaveGameState(game);
            CheckAchievements(false);

            if (settings.MinimizeOnLaunch) WindowState = WindowState.Minimized;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyFilter();
                RefreshDashboard();
            }));
        }

        private void ToggleFavorite(GameItem game)
        {
            game.IsFavorite = !game.IsFavorite;
            SaveGameState(game);
            ApplyFilter();
            RefreshDashboard();
        }

        private void OpenGameLocation(GameItem game)
        {
            if (File.Exists(game.ExecutablePath))
                OpenShell("explorer.exe", $"/select,\"{game.ExecutablePath}\"");
            else if (Directory.Exists(game.InstallDir))
                OpenShell(game.InstallDir);
        }

        private static (string File, string Args) SplitCommand(string command)
        {
            command = command.Trim();

            if (command.StartsWith('"'))
            {
                int closing = command.IndexOf('"', 1);
                if (closing > 0)
                    return (command.Substring(1, closing - 1), command.Substring(closing + 1).Trim());
            }

            int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0)
                return (command.Substring(0, exe + 4), command.Substring(exe + 4).Trim());

            return (command, string.Empty);
        }

        /// <summary>Sucht in der Windows-Programmliste nach dem Deinstallationsprogramm eines Spiels.</summary>
        private static string? FindUninstallCommand(GameItem game)
        {
            string[] subPaths =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            string installDir = game.InstallDir.TrimEnd('\\', '/');

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (string subPath in subPaths)
                {
                    using var root = hive.OpenSubKey(subPath);
                    if (root == null) continue;

                    foreach (string name in root.GetSubKeyNames())
                    {
                        using var key = root.OpenSubKey(name);
                        if (key == null) continue;

                        string uninstall = key.GetValue("UninstallString") as string ?? string.Empty;
                        if (uninstall.Length == 0) continue;

                        string display = key.GetValue("DisplayName") as string ?? string.Empty;
                        string location = (key.GetValue("InstallLocation") as string ?? string.Empty).TrimEnd('\\', '/');

                        bool nameMatch = display.Length > 0 && string.Equals(display, game.Name, StringComparison.OrdinalIgnoreCase);
                        bool pathMatch = installDir.Length > 0 && location.Length > 0
                                         && string.Equals(location, installDir, StringComparison.OrdinalIgnoreCase);

                        if (nameMatch || pathMatch) return uninstall;
                    }
                }
            }

            return null;
        }

        private async void UninstallGame(GameItem game)
        {
            var answer = Msg($"„{game.Name}“ wirklich deinstallieren?\n\nDas Spiel wird von deinem PC entfernt.",
                "Deinstallieren", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            // Vorsichtshalber den Spielstand sichern, bevor das Spiel entfernt wird
            if (!string.IsNullOrEmpty(game.SavePath)) await BackupSavesAsync(game, true);

            try
            {
                // Steam: Deinstallations-Dialog von Steam öffnen
                if (game.Source == "Steam" && game.AppId.Length > 0)
                {
                    OpenShell($"steam://uninstall/{game.AppId}");
                    Msg("Steam öffnet jetzt die Deinstallation.\nKlicke danach auf „Neu scannen“, um die Liste zu aktualisieren.");
                    return;
                }

                // Xbox / Microsoft Store: Paket entfernen
                if (game.Source == "Xbox" && game.PackageFullName.Length > 0)
                {
                    string script = $"$ErrorActionPreference = 'Stop'; Remove-AppxPackage -Package '{game.PackageFullName}'";
                    var result = await Task.Run(() => RunPowerShell(script, 180000));

                    if (result.ExitCode == 0)
                    {
                        allGames.Remove(game);
                        ApplyFilter();
                        RefreshDashboard();
                        Msg($"„{game.Name}“ wurde deinstalliert.");
                    }
                    else
                    {
                        Msg("Die automatische Deinstallation hat nicht funktioniert.\nWindows öffnet jetzt die App-Verwaltung.",
                            "Deinstallieren", MessageBoxButton.OK, MessageBoxImage.Warning);
                        OpenShell("ms-settings:appsfeatures");
                    }
                    return;
                }

                // Alle anderen: Deinstallationsprogramm aus der Windows-Programmliste
                string? command = FindUninstallCommand(game);
                if (command != null)
                {
                    var (file, args) = SplitCommand(command);
                    Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
                    Msg("Das Deinstallationsprogramm wurde gestartet.\nKlicke danach auf „Neu scannen“, um die Liste zu aktualisieren.");
                    return;
                }

                if (game.Source == "Epic Games")
                {
                    OpenShell("com.epicgames.launcher://");
                    Msg("Epic Games erlaubt keine Deinstallation von außen.\nDer Epic Games Launcher wurde geöffnet: Bibliothek → Spiel → ⋯ → Deinstallieren.");
                    return;
                }

                Msg("Für dieses Spiel wurde kein Deinstallationsprogramm gefunden.\nWindows öffnet jetzt die App-Verwaltung.",
                    "Deinstallieren", MessageBoxButton.OK, MessageBoxImage.Warning);
                OpenShell("ms-settings:appsfeatures");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // Administrator-Abfrage abgebrochen
            }
            catch (Exception ex)
            {
                Msg($"Deinstallation fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void AddMenuItem(System.Windows.Controls.ContextMenu menu, string header, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = Loc.T(header) };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }

        private IEnumerable<GameItem> SortGames(IEnumerable<GameItem> games) => settings.SortMode switch
        {
            1 => games.OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                      .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => games.OrderByDescending(g => g.LaunchCount)
                      .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            3 => games.OrderByDescending(g => g.FirstSeen ?? DateTime.MinValue)
                      .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            4 => games.OrderByDescending(g => g.Rating)
                      .ThenByDescending(g => g.PlaySeconds)
                      .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            5 => games.OrderByDescending(g => g.PlaySeconds)
                      .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        private void ApplyFilter()
        {
            if (GamesContainer == null || TxtSearch == null || TxtGameCount == null || BtnSort == null) return;

            bool showHidden = currentFilter == "hidden";
            IEnumerable<GameItem> query = allGames.Where(g => g.Hidden == showHidden);
            int baseCount = allGames.Count(g => g.Hidden == showHidden);

            if (currentFilter == "fav")
                query = query.Where(g => g.IsFavorite);
            else if (currentFilter == "new")
                query = query.Where(IsNewGame);
            else if (currentFilter == "unplayed")
                query = query.Where(g => g.PlaySeconds < 60 && g.LaunchCount == 0);
            else if (currentFilter.StartsWith("col:"))
                query = query.Where(g => g.Collections.Contains(currentFilter.Substring(4)));
            else if (currentFilter.StartsWith("smart:"))
            {
                var smart = settings.SmartLists.FirstOrDefault(l => l.Id == currentFilter.Substring(6));
                if (smart != null) query = query.Where(g => MatchesSmartList(smart, g));
            }
            else if (currentFilter != "all" && currentFilter != "hidden")
                query = query.Where(g => g.Source == currentFilter);

            if (genreFilter.Length > 0)
                query = query.Where(g => GameHasGenre(g, genreFilter));

            string text = TxtSearch.Text == SearchPlaceholder ? string.Empty : TxtSearch.Text.Trim();
            if (text.Length > 0)
                query = query.Where(g => g.Name.Contains(text, StringComparison.OrdinalIgnoreCase));

            var sorted = SortGames(query);
            if (settings.FavoritesFirst) sorted = sorted.OrderByDescending(g => g.IsFavorite);
            var visible = sorted.ToList();

            BtnSort.Content = $"Sortierung: {SortLabels[Math.Clamp(settings.SortMode, 0, SortLabels.Length - 1)]}";
            TxtGameCount.Text = visible.Count == baseCount
                ? $"{baseCount} Spiele"
                : $"{visible.Count} von {baseCount} Spielen";

            DisplayGames(visible);
        }

        private void RemoveManualGame(GameItem game)
        {
            settings.ManualGames.RemoveAll(m =>
                string.Equals(m.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            SaveSettings();

            allGames.Remove(game);
            ApplyFilter();
            RefreshDashboard();
        }

        private void BtnAddGame_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Ausführbare Dateien (*.exe)|*.exe",
                Title = "Spiel-Anwendung auswählen"
            };

            if (dialog.ShowDialog() != true) return;

            string exePath = dialog.FileName;

            if (allGames.Any(g => string.Equals(g.ExecutablePath, exePath, StringComparison.OrdinalIgnoreCase)))
            {
                Msg("Dieses Spiel ist bereits in der Liste.");
                return;
            }

            string name = System.IO.Path.GetFileNameWithoutExtension(exePath);

            settings.ManualGames.Add(new ManualGame { Name = name, ExecutablePath = exePath });
            SaveSettings();

            var game = new GameItem
            {
                Name = name,
                ExecutablePath = exePath,
                Source = "Manuell",
                LocalIcon = ExtractIcon(exePath)
            };
            AssignCachedCover(game);
            ApplyGameStates(new[] { game });
            allGames.Add(game);

            ApplyFilter();
            RefreshDashboard();

            _ = DownloadCoversAsync(new List<GameItem> { game });
        }

        private async void BtnRescanGames_Click(object sender, RoutedEventArgs e)
        {
            if (isScanning) return;

            imageCache.Clear();

            await LoadInstalledGamesAsync();
            Msg($"Spiele-Scan abgeschlossen!\n{allGames.Count} Spiele gefunden.");
        }

        private void BtnSort_Click(object sender, RoutedEventArgs e)
        {
            settings.SortMode = (settings.SortMode + 1) % SortLabels.Length;
            SaveSettings();
            playCardEntrance = true;
            ApplyFilter();
        }

        private void Filter_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton { Tag: string tag })
            {
                currentFilter = tag;
                playCardEntrance = true;
                ApplyFilter();
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtSearch.Text == SearchPlaceholder) TxtSearch.Text = string.Empty;
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSearch.Text)) TxtSearch.Text = SearchPlaceholder;
        }

        private const string GuidPattern = @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}";
        private const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        private static readonly (string Name, string Bg, string Accent)[] ThemePresets =
        {
            ("Dunkel", "#0F111A", "#8B5CF6"),
            ("Pechschwarz", "#000000", "#A78BFA"),
            ("Cyberpunk", "#0D0221", "#FF2E97"),
            ("Wald", "#0A1411", "#34D399"),
            ("Ozean", "#07121F", "#38BDF8"),
            ("Sonnenuntergang", "#17100F", "#FB923C"),
            ("Rosé", "#150D14", "#F472B6"),
            ("Blutrot", "#120709", "#EF4444")
        };

        private static readonly (string Icon, string Title, string Key)[] PageEntries =
        {
            ("🏠", "Dashboard", "dashboard"),
            ("🎮", "Spiele", "games"),
            ("📈", "Statistik", "stats"),
            ("🎁", "Angebote", "deals"),
            ("📋", "Backlog und Planer", "backlog"),
            ("🖼", "Galerie", "gallery"),
            ("🧩", "Anwendungen", "apps"),
            ("🔗", "Schnellzugriff", "links"),
            ("💝", "Wunschliste", "wishlist"),
            ("⬇", "Downloads", "downloads"),
            ("📊", "Systemüberwachung", "system"),
            ("💾", "Speicher", "storage"),
            ("⚡", "Optimierung", "optimization"),
            ("🛠", "Tools", "tools"),
            ("📡", "Streamer", "streamer"),
            ("⚙", "Einstellungen", "settings")
        };

        private bool playCardEntrance;
        private bool coversLoading;
        private bool exitRequested;
        private bool trayHintShown;
        private string loadedBackgroundPath = string.Empty;

        private (string Icon, string Title, string Text, string Target)[] toolEntries =
            Array.Empty<(string Icon, string Title, string Text, string Target)>();
        private List<AppEntry> cachedApps = new();
        private List<GameItem> visibleGames = new();

        // Weiche Ring-Animation
        private readonly DispatcherTimer ringTimer = new();
        private readonly double[] ringTarget = new double[3];
        private readonly double[] ringShown = new double[3];
        private bool gpuAvailable;
        private MetricsSample? lastSample;
        private DateTime lastSampleTime = DateTime.MinValue;

        // Spielzeit
        private readonly DispatcherTimer playTimer = new();
        private readonly Dictionary<GameItem, int> activeSessions = new();
        private readonly Dictionary<GameItem, DateTime> sessionStart = new();
        private int playSaveCounter;
        private bool profileActive;
        private string currentPowerPlanName = string.Empty;
        private bool loadingOptimizationToggles;

        // Mini-Overlay
        private Window? overlayWindow;
        private TextBlock? overlayGameText;
        private TextBlock? overlayStatsText;
        private DateTime overlayTestUntil = DateTime.MinValue;

        // Controller-Modus
        private readonly DispatcherTimer controllerTimer = new();
        private bool controllerMode;
        private int controllerIndex;
        private WindowState savedWindowState = WindowState.Normal;
        private WindowStyle savedWindowStyle = WindowStyle.SingleBorderWindow;

        // Spotlight-Suche
        private readonly List<SpotlightEntry> spotlightEntries = new();
        private int spotlightIndex;

        // Infobereich
        private Forms.NotifyIcon? trayIcon;

        private void InitFeatures()
        {
            try { Icon = BitmapFrame.Create(CreateLogoBitmap(64)); } catch { }

            BuildThemePresets();
            BuildCollectionChips();

            ringTimer.Interval = TimeSpan.FromMilliseconds(30);
            ringTimer.Tick += (s, e) => RingTick();
            ringTimer.Start();

            playTimer.Interval = TimeSpan.FromSeconds(10);
            playTimer.Tick += async (s, e) => await PlaytimeTickAsync();
            playTimer.Start();

            controllerTimer.Interval = TimeSpan.FromMilliseconds(60);
            controllerTimer.Tick += (s, e) => ControllerTick();
            controllerTimer.Start();

            SourceInitialized += (s, e) => ApplyWindowEffects();
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += OnClosingFeatures;
            Closed += (s, e) =>
            {
                trayIcon?.Dispose();
                overlayWindow?.Close();
            };
            Loaded += (s, e) =>
            {
                SetupTray();
                ApplyAppearance();

                if (settings.StartMinimized)
                {
                    if (settings.MinimizeToTray && trayIcon != null) HideToTray();
                    else WindowState = WindowState.Minimized;
                }
            };
        }

        // ───────────────────────────── Spielstatus speichern ─────────────────────────────

        private void ApplyGameStates(IEnumerable<GameItem> games)
        {
            foreach (var game in games)
            {
                if (!settings.GameStates.TryGetValue(game.Name, out var state))
                {
                    // Neu installierte Spiele merken (beim allerersten Scan nicht, sonst wäre alles "neu")
                    if (settings.InitialScanDone)
                    {
                        game.FirstSeen = DateTime.Now;
                        CopyStateToSettings(game);
                    }
                    continue;
                }

                game.IsFavorite = state.IsFavorite;
                game.LastPlayed = state.LastPlayed;
                game.LaunchCount = state.LaunchCount;
                game.PlaySeconds = state.PlaySeconds;
                game.Rating = state.Rating;
                game.Notes = state.Notes ?? string.Empty;
                game.Collections = state.Collections ?? new List<string>();
                game.FirstSeen = state.FirstSeen;
                game.Status = state.Status ?? string.Empty;
                game.SavePath = state.SavePath ?? string.Empty;
                game.LaunchArgs = state.LaunchArgs ?? string.Empty;
                game.RunAsAdmin = state.RunAsAdmin;
                game.CompanionApps = state.CompanionApps ?? new List<string>();
                game.Hidden = state.Hidden;
                game.CustomCover = state.CustomCover ?? string.Empty;
                game.Price = state.Price;
                game.ProfileMode = state.ProfileMode ?? "global";
                game.CloseMode = state.CloseMode ?? "global";
                game.ModPath = state.ModPath ?? string.Empty;
            }
        }

        private void CopyStateToSettings(GameItem game)
        {
            if (!settings.GameStates.TryGetValue(game.Name, out var state))
            {
                state = new GameState();
                settings.GameStates[game.Name] = state;
            }

            state.IsFavorite = game.IsFavorite;
            state.LastPlayed = game.LastPlayed;
            state.LaunchCount = game.LaunchCount;
            state.PlaySeconds = game.PlaySeconds;
            state.Rating = game.Rating;
            state.Notes = game.Notes;
            state.Collections = game.Collections.ToList();
            state.FirstSeen = game.FirstSeen;
            state.Status = game.Status;
            state.SavePath = game.SavePath;
            state.LaunchArgs = game.LaunchArgs;
            state.RunAsAdmin = game.RunAsAdmin;
            state.CompanionApps = game.CompanionApps.ToList();
            state.Hidden = game.Hidden;
            state.CustomCover = game.CustomCover;
            state.Price = game.Price;
            state.ProfileMode = game.ProfileMode;
            state.CloseMode = game.CloseMode;
            state.ModPath = game.ModPath;
        }

        private void SaveGameState(GameItem game)
        {
            CopyStateToSettings(game);
            SaveSettings();
        }

        // ───────────────────────────── Spielkarten, Detailkarten & Liste ─────────────────────────────

        private void PlayEntrance(UIElement element, ScaleTransform? scale, int index)
        {
            if (index < 0 || !settings.CardAnimations) return;

            var delay = TimeSpan.FromMilliseconds(Math.Min(index, 20) * 35);
            element.Opacity = 0;
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { BeginTime = delay });

            if (scale == null) return;

            scale.ScaleX = 0.92;
            scale.ScaleY = 0.92;
            var pop = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(320))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        private UIElement CreateShimmer()
        {
            var move = new TranslateTransform(-1, 0);
            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 0),
                RelativeTransform = move
            };
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(42, 255, 255, 255), 0.5));
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 1.0));

            move.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-1, 1, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever });

            return new Border { Background = brush, IsHitTestVisible = false };
        }

        private string BuildMetaText(GameItem game)
        {
            if (game.PlaySeconds >= 60 && !SHide(settings.StreamHideStats)) return $"{game.Source}  ·  {FormatPlaytime(game.PlaySeconds)}";
            if (game.LastPlayed.HasValue) return $"{game.Source}  ·  {FormatRelative(game.LastPlayed.Value)}";
            return game.Source;
        }

        private System.Windows.Controls.ContextMenu BuildGameMenu(GameItem game)
        {
            var menu = CreateMenu();

            AddMenuItem(menu, "▶  Spielen", () => LaunchGame(game));
            AddMenuItem(menu, game.IsFavorite ? "★  Aus Favoriten entfernen" : "☆  Zu Favoriten hinzufügen", () => ToggleFavorite(game));
            AddMenuItem(menu, "📝  Details, Bewertung und Notizen ...", () => ShowGameDetails(game));

            var collections = new System.Windows.Controls.MenuItem { Header = Loc.T("📁  Sammlungen") };
            foreach (string name in settings.CollectionNames)
            {
                string collection = name;
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = collection,
                    IsCheckable = true,
                    IsChecked = game.Collections.Contains(collection)
                };
                item.Click += (s, e) => ToggleCollection(game, collection);
                collections.Items.Add(item);
            }
            if (settings.CollectionNames.Count > 0) collections.Items.Add(new System.Windows.Controls.Separator());

            var create = new System.Windows.Controls.MenuItem { Header = Loc.T("＋  Neue Sammlung ...") };
            create.Click += (s, e) => CreateCollectionFor(game);
            collections.Items.Add(create);
            menu.Items.Add(collections);

            var statusMenu = new System.Windows.Controls.MenuItem { Header = Loc.T("📋  Status (Backlog)") };
            foreach (var (statusKey, statusTitle) in StatusOptions)
            {
                string key = statusKey;
                var statusItem = new System.Windows.Controls.MenuItem { Header = Loc.T(statusTitle), IsCheckable = true, IsChecked = game.Status == key };
                statusItem.Click += (s, e) => SetStatus(game, game.Status == key ? string.Empty : key);
                statusMenu.Items.Add(statusItem);
            }
            menu.Items.Add(statusMenu);

            var saves = new System.Windows.Controls.MenuItem { Header = Loc.T("💾  Spielstände") };
            AddSubItem(saves, "Jetzt sichern", () => _ = BackupNowAsync(game));
            AddSubItem(saves, "Wiederherstellen ...", () => ShowRestoreDialog(game));
            AddSubItem(saves, "Spielstand-Ordner festlegen ...", () => ChooseSavePath(game));
            menu.Items.Add(saves);

            AddMenuItem(menu, "⚙  Startoptionen und Begleitprogramme ...", () => ShowLaunchOptions(game));

            if (!string.IsNullOrEmpty(game.InstallDir) || File.Exists(game.ExecutablePath))
                AddMenuItem(menu, "📂  Speicherort öffnen", () => OpenGameLocation(game));
            AddMenuItem(menu, "🖼  Eigenes Cover festlegen ...", () => ChooseCustomCover(game));
            if (!string.IsNullOrEmpty(game.CustomCover))
                AddMenuItem(menu, "↺  Cover zurücksetzen", () => ResetCustomCover(game));
            AddMenuItem(menu, game.Hidden ? "👁  Wieder anzeigen" : "🙈  Verstecken", () => ToggleHidden(game));
            AddMenuItem(menu, IsTrackingIgnored(game) && !BuiltInIgnored.Any(p => game.Name.Contains(p, StringComparison.OrdinalIgnoreCase)) && game.AppId != "431960"
                ? "⏱  Spielzeit wieder erfassen" : "⏱  Spielzeit nicht erfassen", () => ToggleTrackingIgnored(game));
            AddMenuItem(menu, "⏱  Spielzeit bearbeiten ...", () => EditPlaytime(game));
            AddMenuItem(menu, "💶  Kaufpreis eintragen ...", () => EditPrice(game));
            AddMenuItem(menu, "⚙  Spielprofil ...", () => ShowGameProfile(game));
            if (modFolders.TryGetValue(game.Name, out var modFolderPath))
                AddMenuItem(menu, "🧩  Mod-Ordner öffnen", () => OpenShell(modFolderPath));
            AddMenuItem(menu, "🧩  Mod-Ordner festlegen ...", () => PickModFolder(game));
            if (!string.IsNullOrEmpty(game.ModPath))
                AddMenuItem(menu, "↺  Mod-Ordner zurücksetzen", () => ResetModFolder(game));
            AddMenuItem(menu, "🗑  Deinstallieren ...", () => UninstallGame(game));
            if (game.Source == "Manuell")
                AddMenuItem(menu, "✖  Aus Liste entfernen", () => RemoveManualGame(game));

            return menu;
        }

        private void DisplayGames(List<GameItem> games)
        {
            if (GamesListContainer == null) return;

            bool animate = playCardEntrance && settings.CardAnimations;
            playCardEntrance = false;

            string layout = controllerMode ? "cards" : settings.CardLayout;
            visibleGames = games;
            UpdateAlphaBar();

            if (settings.LibraryView == "flow" && !controllerMode)
            {
                GamesContainer.Children.Clear();
                GamesListContainer.Children.Clear();
                RenderFlow();
                return;
            }

            GamesContainer.Children.Clear();
            GamesListContainer.Children.Clear();
            GamesContainer.Margin = new Thickness(-settings.CardSpacing, 0, 0, 0);

            if (games.Count == 0)
            {
                GamesContainer.Visibility = Visibility.Visible;
                GamesListContainer.Visibility = Visibility.Collapsed;
                GamesContainer.Children.Add(new TextBlock
                {
                    Text = allGames.Count == 0 && !isScanning
                        ? "Noch keine Spiele gefunden. Füge Spiele mit „Spiel hinzufügen“ hinzu oder starte einen neuen Scan."
                        : "Keine Treffer.",
                    Foreground = BrushSubtle,
                    Margin = new Thickness(12, 8, 0, 0)
                });
                return;
            }

            GamesContainer.Visibility = layout == "list" ? Visibility.Collapsed : Visibility.Visible;
            GamesListContainer.Visibility = layout == "list" ? Visibility.Visible : Visibility.Collapsed;

            double width = controllerMode ? Math.Max(settings.CardWidth, 240) : settings.CardWidth;
            int index = 0;

            foreach (var game in games)
            {
                int anim = animate ? index : -1;

                if (layout == "list")
                    GamesListContainer.Children.Add(CreateGameRow(game, true, anim));
                else if (layout == "detail")
                    GamesContainer.Children.Add(CreateGameRow(game, false, anim));
                else
                    GamesContainer.Children.Add(CreateGameCard(game, width, anim));

                index++;
            }

            if (controllerMode) HighlightControllerSelection();
        }

        private static readonly SolidColorBrush CardMetaBrush = MakeBrush("#E5E7EB");

        private static readonly LinearGradientBrush CardFooterScrim = CreateCardFooterScrim();

        private static LinearGradientBrush CreateCardFooterScrim()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(0, 1)
            };
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x08, 0x0A, 0x10), 0.0));
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xB3, 0x08, 0x0A, 0x10), 0.55));
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xEB, 0x08, 0x0A, 0x10), 1.0));
            brush.Freeze();
            return brush;
        }

        private Border CreateGameCard(GameItem game, double width, int animIndex = -1)
        {
            double height = Math.Round(width * settings.CardAspect / 100.0);
            double radius = settings.CardCorner;
            double zoom = settings.HoverZoom / 100.0;
            var scale = new ScaleTransform(1.0, 1.0);

            // Äußerer Rahmen: trägt Zoom, Leuchtrand und Rechtsklick-Menü
            var outer = new Border
            {
                Width = width,
                Height = height,
                Margin = new Thickness(settings.CardSpacing),
                CornerRadius = new CornerRadius(radius),
                Cursor = System.Windows.Input.Cursors.Hand,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = scale,
                ContextMenu = BuildGameMenu(game),
                ToolTip = !string.IsNullOrEmpty(game.InstallDir) ? game.InstallDir
                        : !string.IsNullOrEmpty(game.ExecutablePath) ? game.ExecutablePath
                        : game.Name
            };

            // Innere Karte: abgerundet zugeschnitten
            var card = new Border
            {
                Background = BrushCardBg,
                CornerRadius = new CornerRadius(radius),
                Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius)
            };
            outer.Child = card;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Bildbereich ──
            var imageArea = new Grid();

            imageArea.Children.Add(new TextBlock
            {
                Text = game.Name.Length > 0 ? game.Name.Substring(0, 1).ToUpperInvariant() : "?",
                FontSize = width * 0.3,
                FontWeight = FontWeights.Bold,
                Foreground = BrushPlaceholder,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });

            BitmapImage? cover = HasCover(game) ? GetCachedImage(EffectiveCover(game)) : null;

            if (cover != null)
            {
                bool isLogo = EffectiveCover(game) == game.FallbackCoverPath;   // kleine Xbox-Logos nicht abschneiden
                var coverImage = new System.Windows.Controls.Image
                {
                    Source = cover,
                    Stretch = isLogo ? Stretch.Uniform : Stretch.UniformToFill,
                    Margin = isLogo ? new Thickness(width * 0.12) : new Thickness(0)
                };
                RenderOptions.SetBitmapScalingMode(coverImage, BitmapScalingMode.HighQuality);
                imageArea.Children.Add(coverImage);
                if (IsAnimatedCover(EffectiveCover(game))) AttachGif(coverImage, outer, EffectiveCover(game));
            }
            else
            {
                if (game.LocalIcon != null)
                {
                    imageArea.Children.Add(new System.Windows.Controls.Image
                    {
                        Source = game.LocalIcon,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(width * 0.16)
                    });
                }

                if (coversLoading && settings.OnlineFeatures && settings.CardAnimations)
                    imageArea.Children.Add(CreateShimmer());
            }

            // Favoriten-Stern (oben rechts)
            var star = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = BrushOverlay,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(8),
                Opacity = game.IsFavorite ? 1 : 0,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = game.IsFavorite ? "★" : "☆",
                    FontSize = 17,
                    Foreground = game.IsFavorite ? BrushGold : System.Windows.Media.Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                }
            };
            star.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true; // nicht gleichzeitig das Spiel starten
                Dispatcher.BeginInvoke(new Action(() => ToggleFavorite(game)));
            };
            imageArea.Children.Add(star);

            // Abzeichen oben links: "NEU" und Backlog-Status
            var badges = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(8)
            };
            if (IsNewGame(game)) badges.Children.Add(CreateBadge("NEU", true));
            if (game.Status.Length > 0) badges.Children.Add(CreateBadge(StatusIcon(game.Status), false));
            if (badges.Children.Count > 0) imageArea.Children.Add(badges);

            // "Spielen"-Button (erscheint beim Hover)
            var playButton = new System.Windows.Controls.Button
            {
                Content = "▶ SPIELEN",
                Width = Math.Round(width * 0.6),
                Height = 38,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Opacity = 0,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            playButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            imageArea.Children.Add(playButton);

            Grid.SetRow(imageArea, 0);
            Grid.SetRowSpan(imageArea, 2);   // das Cover behält die volle Kartenhöhe
            grid.Children.Add(imageArea);

            // ── Fußzeile: Name + Quelle ──
            var footerPanel = new StackPanel();
            if (settings.ShowCardNames)
            {
                footerPanel.Children.Add(new TextBlock
                {
                    Text = game.Name,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            if (settings.ShowCardSource)
            {
                footerPanel.Children.Add(new TextBlock
                {
                    Text = BuildMetaText(game),
                    Foreground = CardMetaBrush,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, settings.ShowCardNames ? 2 : 0, 0, 0)
                });
            }

            // Die Fußzeile liegt als weicher Verlauf auf dem unteren Rand des Covers
            var footer = new Border
            {
                Background = CardFooterScrim,
                Padding = new Thickness(10, 30, 10, 9),
                Child = footerPanel,
                Visibility = footerPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed
            };
            Grid.SetRow(footer, 1);
            grid.Children.Add(footer);

            card.Child = grid;

            // ── Einblend-Animation ──
            PlayEntrance(outer, scale, animIndex);

            // ── Hover ──
            outer.MouseEnter += (s, e) =>
            {
                if (settings.HoverAnimations)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(zoom, TimeSpan.FromMilliseconds(150)));
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(zoom, TimeSpan.FromMilliseconds(150)));
                }
                if (settings.HoverGlow)
                {
                    outer.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = GetAccentColor(),
                        BlurRadius = 30,
                        ShadowDepth = 0,
                        Opacity = 0.75
                    };
                }
                playButton.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
                if (!game.IsFavorite)
                    star.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
            };

            outer.MouseLeave += (s, e) =>
            {
                if (settings.HoverAnimations)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
                }
                if (!controllerMode) outer.Effect = null;
                playButton.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150)));
                if (!game.IsFavorite)
                    star.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150)));
            };

            // ── Starten ──
            outer.MouseLeftButtonDown += (s, e) =>
            {
                if (!settings.DoubleClickLaunch || e.ClickCount >= 2) LaunchGame(game);
            };
            HookMultiSelect(outer, game);
            HookCoverDrop(outer, game);
            AddModButton(imageArea, outer, game, true);
            playButton.Click += (s, e) =>
            {
                e.Handled = true;
                LaunchGame(game);
            };

            return outer;
        }

        /// <summary>Breite Detailkarte (compact = false) oder schlanke Listenzeile (compact = true).</summary>
        private Border CreateGameRow(GameItem game, bool compact, int animIndex)
        {
            double radius = Math.Min(settings.CardCorner, compact ? 12 : 18);
            double height = compact ? 70 : 150;
            double coverWidth = compact ? 42 : 100;
            double coverHeight = height - 16;

            var row = new Border
            {
                Height = height,
                Width = compact ? double.NaN : 430,
                Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(12, 0, 0, 12),
                Padding = new Thickness(8),
                Background = BrushCardBg,
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(radius),
                Cursor = System.Windows.Input.Cursors.Hand,
                ContextMenu = BuildGameMenu(game)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Cover
            var coverBox = new Border
            {
                Width = coverWidth,
                Height = coverHeight,
                CornerRadius = new CornerRadius(8),
                Background = BrushFooter,
                Margin = new Thickness(0, 0, 14, 0),
                Clip = new RectangleGeometry(new Rect(0, 0, coverWidth, coverHeight), 8, 8)
            };
            BitmapImage? cover = HasCover(game) ? GetCachedImage(EffectiveCover(game)) : null;
            if (cover != null)
            {
                bool isLogo = EffectiveCover(game) == game.FallbackCoverPath;
                var image = new System.Windows.Controls.Image
                {
                    Source = cover,
                    Stretch = isLogo ? Stretch.Uniform : Stretch.UniformToFill
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                coverBox.Child = image;
                if (IsAnimatedCover(EffectiveCover(game))) AttachGif(image, row, EffectiveCover(game));
            }
            else
            {
                coverBox.Child = new TextBlock
                {
                    Text = game.Name.Length > 0 ? game.Name.Substring(0, 1).ToUpperInvariant() : "?",
                    FontSize = compact ? 20 : 34,
                    FontWeight = FontWeights.Bold,
                    Foreground = BrushPlaceholder,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
            }
            grid.Children.Add(coverBox);

            // Infos
            var info = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(info, 1);
            info.Children.Add(new TextBlock
            {
                Text = game.Name,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = compact ? 15 : 17,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = BuildMetaText(game),
                Foreground = BrushSubtle,
                FontSize = 12,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (!compact)
            {
                if (game.Rating > 0)
                {
                    info.Children.Add(new TextBlock
                    {
                        Text = new string('★', game.Rating) + new string('☆', 5 - game.Rating),
                        Foreground = BrushGold,
                        FontSize = 14,
                        Margin = new Thickness(0, 6, 0, 0)
                    });
                }
                if (game.LastPlayed.HasValue)
                {
                    info.Children.Add(new TextBlock
                    {
                        Text = "Zuletzt gespielt " + FormatRelative(game.LastPlayed.Value),
                        Foreground = BrushSubtle,
                        FontSize = 12,
                        Margin = new Thickness(0, 6, 0, 0)
                    });
                }
                if (game.Collections.Count > 0)
                {
                    info.Children.Add(new TextBlock
                    {
                        Text = "📁 " + string.Join(", ", game.Collections),
                        Foreground = BrushSubtle,
                        FontSize = 12,
                        Margin = new Thickness(0, 4, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
            }
            grid.Children.Add(info);

            // Aktionen
            var actions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 6, 0)
            };
            Grid.SetColumn(actions, 2);

            var starText = new TextBlock
            {
                Text = game.IsFavorite ? "★" : "☆",
                FontSize = 22,
                Foreground = game.IsFavorite ? BrushGold : BrushSubtle,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };
            starText.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                Dispatcher.BeginInvoke(new Action(() => ToggleFavorite(game)));
            };
            actions.Children.Add(starText);

            var play = new System.Windows.Controls.Button { Content = "▶  Spielen", Padding = new Thickness(16, 8, 16, 8) };
            play.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            play.Click += (s, e) =>
            {
                e.Handled = true;
                LaunchGame(game);
            };
            actions.Children.Add(play);
            grid.Children.Add(actions);

            row.Child = grid;

            PlayEntrance(row, null, animIndex);

            row.MouseEnter += (s, e) => row.Background = BrushTileHover;
            row.MouseLeave += (s, e) => row.Background = BrushCardBg;
            row.MouseLeftButtonDown += (s, e) =>
            {
                if (!settings.DoubleClickLaunch || e.ClickCount >= 2) LaunchGame(game);
            };
            HookMultiSelect(row, game);
            HookCoverDrop(row, game);

            return row;
        }

        // ───────────────────────────── Sammlungen, Bewertung & Notizen ─────────────────────────────

        private void BuildCollectionChips()
        {
            foreach (var chip in FilterChips.Children.OfType<System.Windows.Controls.RadioButton>()
                         .Where(c => c.Tag is string tag && tag.StartsWith("col:")).ToList())
            {
                FilterChips.Children.Remove(chip);
            }

            foreach (string name in settings.CollectionNames)
            {
                string collection = name;
                var chip = new System.Windows.Controls.RadioButton
                {
                    Content = "📁 " + collection,
                    Tag = "col:" + collection,
                    GroupName = "Filter",
                    Style = (Style)FindResource("ChipStyle")
                };
                chip.Checked += Filter_Checked;

                var menu = CreateMenu();
                AddMenuItem(menu, "🗑  Sammlung löschen", () => DeleteCollection(collection));
                chip.ContextMenu = menu;

                FilterChips.Children.Add(chip);
            }
        }

        private void ToggleCollection(GameItem game, string collection)
        {
            if (!game.Collections.Remove(collection)) game.Collections.Add(collection);
            SaveGameState(game);
            ApplyFilter();
        }

        private void CreateCollectionFor(GameItem game)
        {
            string? name = PromptText("Neue Sammlung", "Name der Sammlung (zum Beispiel „Backlog“ oder „Koop“):", string.Empty);
            if (name == null) return;

            if (!settings.CollectionNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                settings.CollectionNames.Add(name);
                BuildCollectionChips();
            }

            string existing = settings.CollectionNames.First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (!game.Collections.Contains(existing)) game.Collections.Add(existing);
            SaveGameState(game);
            ApplyFilter();
        }

        private void DeleteCollection(string name)
        {
            var answer = Msg($"Die Sammlung „{name}“ löschen?\nDie Spiele selbst bleiben erhalten.", "Sammlung löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            settings.CollectionNames.Remove(name);
            foreach (var game in allGames)
            {
                if (game.Collections.Remove(name)) CopyStateToSettings(game);
            }
            SaveSettings();

            if (currentFilter == "col:" + name)
            {
                currentFilter = "all";
                var all = FilterChips.Children.OfType<System.Windows.Controls.RadioButton>().FirstOrDefault(c => c.Tag as string == "all");
                if (all != null) all.IsChecked = true;
            }

            BuildCollectionChips();
            ApplyFilter();
        }

        private Window CreateDialog(string title, double width, out StackPanel panel)
        {
            panel = new StackPanel { Margin = new Thickness(24) };

            var dialog = new Window
            {
                Title = title,
                Width = width,
                SizeToContent = SizeToContent.Height,
                Owner = IsVisible ? this : null,
                WindowStartupLocation = IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Content = panel
            };
            dialog.Resources.MergedDictionaries.Add(Resources);
            dialog.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AppBackgroundBrush");
            dialog.SourceInitialized += (s, e) =>
            {
                TranslateTree(dialog);
                dialog.Title = Loc.T(dialog.Title);

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(dialog).Handle;
                int dark = 1;
                NativeFeatures.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            };
            // Streamer-Modus: private Angaben schon vor dem ersten Bild verdecken
            dialog.Loaded += (s, e) =>
            {
                if (StreamerOn) ScrubSensitiveText();
            };

            return dialog;
        }

        private string? PromptText(string title, string label, string initial)
        {
            var dialog = CreateDialog(title, 460, out var panel);

            panel.Children.Add(new TextBlock { Text = label, Foreground = BrushSubtle, TextWrapping = TextWrapping.Wrap });

            var input = new System.Windows.Controls.TextBox { Text = initial, Height = 38, Margin = new Thickness(0, 10, 0, 18) };
            panel.Children.Add(input);

            string? result = null;
            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancel = new System.Windows.Controls.Button { Content = "Abbrechen", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var ok = new System.Windows.Controls.Button { Content = "OK", Padding = new Thickness(28, 8, 28, 8), IsDefault = true };
            ok.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            ok.Click += (s, e) =>
            {
                result = input.Text.Trim();
                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            dialog.Loaded += (s, e) =>
            {
                input.Focus();
                input.SelectAll();
            };
            dialog.ShowDialog();

            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        private void ShowGameDetails(GameItem game)
        {
            var dialog = CreateDialog(game.Name, 600, out var panel);

            // Kopfbereich mit Cover und Infos
            var header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var poster = new Border
            {
                Width = 112,
                Height = 168,
                CornerRadius = new CornerRadius(12),
                Background = BrushCardBg,
                Margin = new Thickness(0, 0, 20, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Top
            };
            if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage image)
                poster.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            header.Children.Add(poster);

            var info = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(info, 1);
            info.Children.Add(new TextBlock
            {
                Text = game.Name,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            void Line(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                info.Children.Add(new TextBlock
                {
                    Text = text,
                    Foreground = MakeBrush("#9CA3AF"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3)
                });
            }

            Line($"Quelle: {game.Source}");
            if (!SHide(settings.StreamHideStats)) Line($"Spielzeit: {FormatPlaytimeLong(game.PlaySeconds)}");
            Line(game.LastPlayed.HasValue ? $"Zuletzt gespielt: {game.LastPlayed.Value:dd.MM.yyyy HH:mm} ({FormatRelative(game.LastPlayed.Value)})" : "Noch nicht gespielt");
            Line($"Über den Launcher gestartet: {game.LaunchCount}×");
            if (!SHide(settings.StreamHidePaths)) Line(game.InstallDir);
            header.Children.Add(info);
            panel.Children.Add(header);
            AddGameInfoSection(panel, game);

            // Bewertung
            panel.Children.Add(new TextBlock { Text = "Bewertung", Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 4) });

            int rating = game.Rating;
            var starBlocks = new List<TextBlock>();
            var starRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };

            void RenderStars()
            {
                for (int i = 0; i < starBlocks.Count; i++)
                {
                    starBlocks[i].Text = i < rating ? "★" : "☆";
                    starBlocks[i].Foreground = i < rating ? BrushGold : BrushSubtle;
                }
            }

            for (int i = 1; i <= 5; i++)
            {
                int value = i;
                var block = new TextBlock { FontSize = 30, Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
                block.MouseLeftButtonUp += (s, e) =>
                {
                    rating = rating == value ? 0 : value;
                    RenderStars();
                };
                starBlocks.Add(block);
                starRow.Children.Add(block);
            }
            RenderStars();
            panel.Children.Add(starRow);

            // Sammlungen
            panel.Children.Add(new TextBlock { Text = "Sammlungen", Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 6) });
            var boxes = new Dictionary<string, System.Windows.Controls.CheckBox>();
            if (settings.CollectionNames.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Noch keine Sammlungen. Lege sie per Rechtsklick auf ein Spiel an.",
                    Foreground = BrushSubtle,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 16)
                });
            }
            else
            {
                var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
                foreach (string name in settings.CollectionNames)
                {
                    var box = new System.Windows.Controls.CheckBox
                    {
                        Content = name,
                        Foreground = System.Windows.Media.Brushes.White,
                        IsChecked = game.Collections.Contains(name),
                        Margin = new Thickness(0, 0, 18, 6)
                    };
                    boxes[name] = box;
                    wrap.Children.Add(box);
                }
                panel.Children.Add(wrap);
            }

            // Notizen
            panel.Children.Add(new TextBlock { Text = "Notizen", Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 6) });
            var notes = new System.Windows.Controls.TextBox
            {
                Text = game.Notes,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 110,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 20)
            };
            panel.Children.Add(notes);

            // Buttons
            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancel = new System.Windows.Controls.Button { Content = "Abbrechen", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var save = new System.Windows.Controls.Button { Content = "Speichern", Padding = new Thickness(26, 8, 26, 8) };
            save.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            save.Click += (s, e) =>
            {
                game.Rating = rating;
                game.Notes = notes.Text;
                game.Collections = boxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
                SaveGameState(game);
                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            if (dialog.ShowDialog() == true)
            {
                ApplyFilter();
                RefreshDashboard();
            }
        }

        // ───────────────────────────── Spielzeit & Statistik ─────────────────────────────

        private static List<string> GetRunningProcessPaths()
        {
            var result = new List<string>();

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    string? path = NativeFeatures.GetProcessPath(process.Id);
                    if (!string.IsNullOrEmpty(path)) result.Add(path);
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }

            return result;
        }

        private static bool IsGameRunning(GameItem game, List<string> runningPaths)
        {
            if (!string.IsNullOrEmpty(game.InstallDir))
            {
                string prefix = game.InstallDir.TrimEnd('\\', '/') + "\\";
                return runningPaths.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }

            return !string.IsNullOrEmpty(game.ExecutablePath)
                   && runningPaths.Any(p => string.Equals(p, game.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        }

        private void AddPlayLog(double seconds)
        {
            string key = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            settings.PlayLog.TryGetValue(key, out double current);
            settings.PlayLog[key] = current + seconds;
        }

        private async Task PlaytimeTickAsync()
        {
            if (!settings.TrackPlaytime || allGames.Count == 0) return;

            List<string> running;
            try
            {
                running = await Task.Run(() => GetRunningProcessPaths());
            }
            catch
            {
                return;
            }

            var now = DateTime.Now;
            bool anyRunning = false;
            bool sessionEnded = false;

            foreach (var game in allGames.ToList())
            {
                if (!IsTrackingIgnored(game) && IsGameRunning(game, running))
                {
                    anyRunning = true;
                    bool isNew = !activeSessions.ContainsKey(game);
                    activeSessions[game] = 0;

                    if (isNew)
                    {
                        sessionStart[game] = now;
                        DiscordSessionStarted(game);
                        RunRoutinesFor("gamestart", game);
                        if (game.Status == "backlog") game.Status = "playing";
                        if (ProfileWanted(game) && activeSessions.Count == 1) _ = ActivateGamingProfileAsync();
                        if (activeSessions.Count == 1) OnProfileGameStarted();
                        if (activeSessions.Count == 1 && CloseAppsWanted(game)) _ = CloseConfiguredAppsAsync(true);
                    }

                    game.PlaySeconds += 10;
                    game.LastPlayed = now;
                    AddPlayLog(10);
                    AddYearPlay(game, 10);
                }
                else if (activeSessions.TryGetValue(game, out int missed))
                {
                    if (missed + 1 >= 2)
                    {
                        activeSessions.Remove(game);
                        if (sessionStart.TryGetValue(game, out var startedAt)) RecordSession(game, now - startedAt);
                        sessionStart.Remove(game);
                        CopyStateToSettings(game);
                        sessionEnded = true;
                        sessionBreakCount.Remove(game);
                        ShowSessionReport(game);
                        CheckMilestones();
                        ResumePadAfterGame();
                        DiscordSessionEnded(game);
                        RunRoutinesFor("gameend", game);
                        if (settings.AutoBackupSaves && !string.IsNullOrEmpty(game.SavePath)) _ = BackupSavesAsync(game, true);

                        if (activeSessions.Count == 0)
                        {
                            _ = DeactivateGamingProfileAsync();
                            RestoreClosedApps();
                            OnProfileGameEnded();
                        }
                    }
                    else
                    {
                        activeSessions[game] = missed + 1;
                    }
                }
            }

            CheckBreakReminders(now);
            SampleSessions();
            CheckDailyLimit(now);
            CheckNightEgg(now);

            // Zwischendurch speichern (etwa jede Minute)
            playSaveCounter++;
            if (anyRunning && playSaveCounter % 6 == 0)
            {
                foreach (var game in activeSessions.Keys) CopyStateToSettings(game);
                SaveSettings();
            }
            if (sessionEnded) SaveSettings();

            // Energiesparplan nach einem Absturz wiederherstellen
            if (!anyRunning && activeSessions.Count == 0 && !profileActive && settings.PreviousPowerPlan.Length > 0)
                _ = DeactivateGamingProfileAsync();

            UpdateOverlayVisibility();

            if (sessionEnded && activeSessions.Count == 0 && settings.ReopenAfterGame) ReopenLauncherAfterGame();

            if (sessionEnded)
            {
                ApplyFilter();
                RefreshDashboard();
            }
            if (ViewStats.Visibility == Visibility.Visible && (sessionEnded || (anyRunning && playSaveCounter % 6 == 0)))
                RefreshStats(false);
        }

        private async Task ActivateGamingProfileAsync()
        {
            profileActive = true;

            var current = await Task.Run(() => RunHidden("powercfg", "/getactivescheme"));
            var match = Regex.Match(current.Output, GuidPattern);
            if (!match.Success) return;

            string active = match.Value.ToLowerInvariant();
            if (active == HighPerformancePlan) return;

            settings.PreviousPowerPlan = active;
            SaveSettings();
            await Task.Run(() => RunHidden("powercfg", $"/setactive {HighPerformancePlan}"));
        }

        private async Task DeactivateGamingProfileAsync()
        {
            profileActive = false;
            if (settings.PreviousPowerPlan.Length == 0) return;

            string plan = settings.PreviousPowerPlan;
            settings.PreviousPowerPlan = string.Empty;
            SaveSettings();
            await Task.Run(() => RunHidden("powercfg", $"/setactive {plan}"));
        }

        private void RefreshStats(bool animate)
        {
            if (ViewStats == null) return;

            long total = allGames.Where(g => !IsTrackingIgnored(g)).Sum(g => g.PlaySeconds);
            TxtStatsTotal.Text = FormatPlaytimeLong(total);

            double weekSeconds = 0;
            double maxDay = 0;
            var days = new List<(DateTime Day, double Seconds)>();
            for (int i = 6; i >= 0; i--)
            {
                var day = DateTime.Today.AddDays(-i);
                settings.PlayLog.TryGetValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out double seconds);
                days.Add((day, seconds));
                weekSeconds += seconds;
                maxDay = Math.Max(maxDay, seconds);
            }
            TxtStatsWeek.Text = FormatPlaytimeLong((long)weekSeconds);

            var top = allGames.OrderByDescending(g => g.PlaySeconds).FirstOrDefault(g => g.PlaySeconds >= 60);
            TxtStatsTop.Text = top?.Name ?? "–";

            // Wochendiagramm
            WeekChart.Children.Clear();
            for (int i = 0; i < days.Count; i++)
            {
                var (day, seconds) = days[i];
                bool today = i == days.Count - 1;
                double barHeight = maxDay > 0 ? Math.Max(5, seconds / maxDay * 130) : 5;

                var cell = new StackPanel
                {
                    VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                    Margin = new Thickness(6, 0, 6, 0)
                };
                cell.Children.Add(new TextBlock
                {
                    Text = seconds >= 60 ? FormatPlaytime((long)seconds) : string.Empty,
                    Foreground = BrushSubtle,
                    FontSize = 11,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                var bar = new Border
                {
                    Width = 36,
                    Height = barHeight,
                    CornerRadius = new CornerRadius(8),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Opacity = today ? 1.0 : 0.65
                };
                bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                if (animate && settings.PageAnimations)
                {
                    bar.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(0, barHeight, TimeSpan.FromMilliseconds(520))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(i * 55),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                cell.Children.Add(bar);

                cell.Children.Add(new TextBlock
                {
                    Text = day.ToString("ddd", GermanCulture),
                    Foreground = today ? System.Windows.Media.Brushes.White : BrushSubtle,
                    FontSize = 12,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 0)
                });

                WeekChart.Children.Add(cell);
            }

            // Rangliste
            var ranked = allGames.Where(g => g.PlaySeconds >= 60 && !IsTrackingIgnored(g)).OrderByDescending(g => g.PlaySeconds).Take(10).ToList();
            long best = ranked.Count > 0 ? ranked[0].PlaySeconds : 0;
            TopGamesList.ItemsSource = ranked.Select((g, index) => new StatRow
            {
                Rank = (index + 1).ToString(),
                Name = g.Name,
                TimeText = FormatPlaytime(g.PlaySeconds),
                Percent = best > 0 ? g.PlaySeconds * 100.0 / best : 0
            }).ToList();
            TxtStatsEmpty.Visibility = ranked.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            RefreshStatsExtras();
            BuildHeatmap();
        }

        // ───────────────────────────── Angebote: Gratis-Spiele & Steam-Rabatte ─────────────────────────────

        private static bool TryPath(JsonElement start, out JsonElement result, params string[] path)
        {
            result = start;
            foreach (string name in path)
            {
                if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(name, out var next))
                    return false;
                result = next;
            }
            return true;
        }

        private static DateTime? ParseOfferDate(JsonElement offer, string property)
        {
            string text = GetJsonString(offer, property);
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed.LocalDateTime;
            return null;
        }

        private static async Task<(List<DealItem> Free, List<DealItem> Upcoming)> FetchEpicFreeAsync()
        {
            var free = new List<DealItem>();
            var upcoming = new List<DealItem>();

            string json = await Http.GetStringAsync(
                "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=de&country=DE&allowCountries=DE");
            using var doc = JsonDocument.Parse(json);

            if (!TryPath(doc.RootElement, out var elements, "data", "Catalog", "searchStore", "elements") ||
                elements.ValueKind != JsonValueKind.Array)
                return (free, upcoming);

            foreach (var element in elements.EnumerateArray())
            {
                try
                {
                    string title = GetJsonString(element, "title");
                    if (title.Length == 0) continue;

                    string slug = GetJsonString(element, "productSlug");
                    if (slug.Length == 0) slug = GetJsonString(element, "urlSlug");
                    if (slug.Length == 0 && TryPath(element, out var mappings, "offerMappings") &&
                        mappings.ValueKind == JsonValueKind.Array && mappings.GetArrayLength() > 0)
                        slug = GetJsonString(mappings[0], "pageSlug");
                    slug = slug.Replace("/home", string.Empty);

                    string url = slug.Length > 0
                        ? $"https://store.epicgames.com/de/p/{slug}"
                        : "https://store.epicgames.com/de/free-games";

                    string image = string.Empty;
                    if (element.TryGetProperty("keyImages", out var images) && images.ValueKind == JsonValueKind.Array)
                    {
                        foreach (string wanted in new[] { "OfferImageWide", "DieselStoreFrontWide", "Thumbnail", "OfferImageTall" })
                        {
                            foreach (var candidate in images.EnumerateArray())
                            {
                                if (GetJsonString(candidate, "type") == wanted)
                                {
                                    image = GetJsonString(candidate, "url");
                                    break;
                                }
                            }
                            if (image.Length > 0) break;
                        }
                    }

                    if (!element.TryGetProperty("promotions", out var promotions) || promotions.ValueKind != JsonValueKind.Object)
                        continue;

                    // Aktuell gratis
                    if (promotions.TryGetProperty("promotionalOffers", out var current) && current.ValueKind == JsonValueKind.Array &&
                        current.GetArrayLength() > 0 &&
                        TryPath(current[0], out var offers, "promotionalOffers") && offers.ValueKind == JsonValueKind.Array &&
                        offers.GetArrayLength() > 0)
                    {
                        var offer = offers[0];
                        long percent = TryPath(offer, out var discount, "discountSetting") ? JsonLong(discount, "discountPercentage") : 0;
                        if (percent == 0)
                        {
                            var end = ParseOfferDate(offer, "endDate");
                            free.Add(new DealItem
                            {
                                Id = GetJsonString(element, "id") + title,
                                Title = title,
                                Subtitle = end.HasValue ? $"Gratis bis {end.Value:dd.MM. HH:mm} Uhr" : "Gratis",
                                Badge = "GRATIS",
                                Store = "epic",
                                ImageUrl = image,
                                Url = url
                            });
                            continue;
                        }
                    }

                    // Demnächst gratis
                    if (promotions.TryGetProperty("upcomingPromotionalOffers", out var next) && next.ValueKind == JsonValueKind.Array &&
                        next.GetArrayLength() > 0 &&
                        TryPath(next[0], out var nextOffers, "promotionalOffers") && nextOffers.ValueKind == JsonValueKind.Array &&
                        nextOffers.GetArrayLength() > 0)
                    {
                        var start = ParseOfferDate(nextOffers[0], "startDate");
                        upcoming.Add(new DealItem
                        {
                            Id = GetJsonString(element, "id") + title,
                            Title = title,
                            Subtitle = start.HasValue ? $"Ab {start.Value:dd.MM. HH:mm} Uhr gratis" : "Bald gratis",
                            Badge = "BALD",
                            Store = "epic",
                            ImageUrl = image,
                            Url = url
                        });
                    }
                }
                catch { }
            }

            return (free, upcoming);
        }

        private static async Task<List<DealItem>> FetchSteamDealsAsync()
        {
            var list = new List<DealItem>();

            string json = await Http.GetStringAsync("https://store.steampowered.com/api/featuredcategories?cc=de&l=german");
            using var doc = JsonDocument.Parse(json);

            if (!TryPath(doc.RootElement, out var items, "specials", "items") || items.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in items.EnumerateArray())
            {
                string name = GetJsonString(item, "name");
                long id = JsonLong(item, "id");
                long percent = JsonLong(item, "discount_percent");
                long original = JsonLong(item, "original_price");
                long finalPrice = JsonLong(item, "final_price");
                if (name.Length == 0 || id == 0 || percent <= 0) continue;

                string image = GetJsonString(item, "large_capsule_image");
                if (image.Length == 0) image = GetJsonString(item, "header_image");

                list.Add(new DealItem
                {
                    Id = id.ToString(),
                    Title = name,
                    Subtitle = original > 0
                        ? $"{finalPrice / 100.0:F2} € statt {original / 100.0:F2} €"
                        : $"{finalPrice / 100.0:F2} €",
                    Badge = $"-{percent} %",
                    Store = "steam",
                    ImageUrl = image,
                    Url = $"https://store.steampowered.com/app/{id}"
                });

                if (list.Count >= 12) break;
            }

            return list;
        }

        private async Task RefreshDealsAsync(bool silent)
        {
            if (!settings.OnlineFeatures)
            {
                TxtDealsStatus.Text = "Online-Funktionen sind in den Einstellungen deaktiviert.";
                return;
            }

            TxtDealsStatus.Text = "Angebote werden geladen ...";
            BtnRefreshDeals.IsEnabled = false;

            try
            {
                var epic = await FetchEpicFreeAsync();
                epicFree = epic.Free;
                epicUpcoming = epic.Upcoming;
            }
            catch { }

            try
            {
                steamDeals = await FetchSteamDealsAsync();
            }
            catch { }

            lastDealsRefresh = DateTime.Now;
            RenderDeals();
            BtnRefreshDeals.IsEnabled = true;
            TxtDealsStatus.Text = epicFree.Count + epicUpcoming.Count + steamDeals.Count == 0
                ? "Keine Angebote geladen. Prüfe deine Internetverbindung."
                : $"Stand: {DateTime.Now:HH:mm} Uhr";

            CheckFreeGameBanner(silent);
        }

        private async void BtnRefreshDeals_Click(object sender, RoutedEventArgs e) => await RefreshDealsAsync(false);

        private void CheckFreeGameBanner(bool silent)
        {
            var unseen = epicFree.Where(d => !settings.SeenFreeGames.Contains(d.Id)).ToList();
            if (!settings.ShowDeals || unseen.Count == 0)
            {
                RemoveBanner("deals");
                return;
            }

            string names = string.Join(", ", unseen.Select(d => d.Title).Take(3));
            ShowBanner("deals", "🎁", $"Gratis bei Epic Games: {names}", "Ansehen", () => NavigateTo("deals"), MarkFreeGamesSeen);

            if (silent)
                ShowToast("🎁", "Neue Gratis-Spiele", names, 8, () => NavigateTo("deals"));
        }

        private void MarkFreeGamesSeen()
        {
            bool changed = false;
            foreach (var deal in epicFree)
            {
                if (settings.SeenFreeGames.Contains(deal.Id)) continue;
                settings.SeenFreeGames.Add(deal.Id);
                changed = true;
            }

            while (settings.SeenFreeGames.Count > 60) settings.SeenFreeGames.RemoveAt(0);
            RemoveBanner("deals");
            if (changed) SaveSettings();
        }

        private static BitmapImage? LoadWebImage(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(url);
                image.DecodePixelWidth = 500;
                image.EndInit();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private void RenderDeals()
        {
            FillDeals(EpicFreeContainer, epicFree, "Zurzeit gibt es keine Gratis-Spiele.");
            FillDeals(EpicUpcomingContainer, epicUpcoming, "Noch keine Vorschau verfügbar.");
            FillDeals(SteamDealsContainer, steamDeals, "Keine Angebote gefunden.");
        }

        private void FillDeals(WrapPanel panel, List<DealItem> deals, string emptyText)
        {
            panel.Children.Clear();

            if (deals.Count == 0)
            {
                panel.Children.Add(new TextBlock { Text = emptyText, Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 12) });
                return;
            }

            foreach (var deal in deals)
                panel.Children.Add(CreateDealCard(deal));
        }

        private Border CreateDealCard(DealItem deal)
        {
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(124) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var imageArea = new Grid { Background = BrushFooter };
            var picture = LoadWebImage(deal.ImageUrl);
            if (picture != null)
                imageArea.Children.Add(new System.Windows.Controls.Image { Source = picture, Stretch = Stretch.UniformToFill });

            var badge = CreateBadge(deal.Badge, true);
            badge.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            badge.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            badge.Margin = new Thickness(10);
            imageArea.Children.Add(badge);
            layout.Children.Add(imageArea);

            var texts = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
            Grid.SetRow(texts, 1);
            texts.Children.Add(new TextBlock
            {
                Text = deal.Title,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            texts.Children.Add(new TextBlock
            {
                Text = deal.Subtitle,
                Foreground = BrushSubtle,
                FontSize = 12,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var storeName = deal.Store == "steam" ? "Steam" : deal.Store == "epic" ? "Epic Games Store" : string.Empty;
            if (storeName.Length > 0)
            {
                var storeRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                var logo = GetStoreLogo(deal.Store);
                if (logo != null)
                    storeRow.Children.Add(new System.Windows.Controls.Image { Source = logo, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
                storeRow.Children.Add(new TextBlock { Text = storeName, Foreground = BrushSubtle, FontSize = 11, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                texts.Children.Add(storeRow);
            }
            layout.Children.Add(texts);

            var card = new Border
            {
                Width = 250,
                Margin = new Thickness(0, 0, 14, 14),
                CornerRadius = new CornerRadius(14),
                Background = BrushCardBg,
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ClipToBounds = true,
                Child = layout
            };
            card.MouseEnter += (s, e) => card.Background = BrushTileHover;
            card.MouseLeave += (s, e) => card.Background = BrushCardBg;
            card.MouseLeftButtonUp += (s, e) => OpenShell(deal.Url);
            return card;
        }

        // ───────────────────────────── Backlog-Board & Release-Planer ─────────────────────────────

        private void SetStatus(GameItem game, string status)
        {
            game.Status = status;
            SaveGameState(game);
            RefreshBoard();
            ApplyFilter();
            CheckAchievements(false);
        }

        private void RefreshBoard()
        {
            if (BoardBacklog == null || BoardPlaying == null || BoardDone == null) return;

            FillColumn(BoardBacklog, "backlog");
            FillColumn(BoardPlaying, "playing");
            FillColumn(BoardDone, "done");
            RefreshReleases();
        }

        private void FillColumn(StackPanel column, string status)
        {
            column.Children.Clear();

            foreach (var game in VisibleGames.Where(g => g.Status == status).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
                column.Children.Add(CreateBoardItem(game));

            if (column.Children.Count == 0)
            {
                column.Children.Add(new TextBlock
                {
                    Text = "Leer. Ziehe Spiele hierher oder nutze ＋.",
                    Foreground = BrushSubtle,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 4, 0, 0)
                });
            }
        }

        private Border CreateBoardItem(GameItem game)
        {
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var thumb = new Border
            {
                Width = 34,
                Height = 48,
                CornerRadius = new CornerRadius(6),
                Background = BrushCardBg,
                Margin = new Thickness(0, 0, 10, 0)
            };
            if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage image)
                thumb.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            layout.Children.Add(thumb);

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(texts, 1);
            texts.Children.Add(new TextBlock
            {
                Text = game.Name,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            texts.Children.Add(new TextBlock
            {
                Text = BuildMetaText(game),
                Foreground = BrushSubtle,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            layout.Children.Add(texts);

            var item = new Border
            {
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(10),
                Background = BrushFooter,
                Cursor = System.Windows.Input.Cursors.Hand,
                ContextMenu = BuildGameMenu(game),
                ToolTip = "Ziehen zum Verschieben, Doppelklick zum Starten",
                Child = layout
            };

            var dragStart = new System.Windows.Point();
            item.MouseLeftButtonDown += (s, e) =>
            {
                dragStart = e.GetPosition(null);
                if (e.ClickCount == 2) LaunchGame(game);
            };
            item.MouseMove += (s, e) =>
            {
                if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

                var position = e.GetPosition(null);
                if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;

                System.Windows.DragDrop.DoDragDrop(item, game.Name, System.Windows.DragDropEffects.Move);
            };
            item.MouseEnter += (s, e) => item.Background = BrushTileHover;
            item.MouseLeave += (s, e) => item.Background = BrushFooter;

            return item;
        }

        private void Board_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat)
                ? System.Windows.DragDropEffects.Move
                : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void Board_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (sender is Border { Tag: string status } &&
                e.Data.GetData(System.Windows.DataFormats.StringFormat) is string name)
            {
                var game = allGames.FirstOrDefault(g => g.Name == name);
                if (game != null) SetStatus(game, status);
            }
            e.Handled = true;
        }

        private void BtnBoardAdd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { Tag: string status }) return;

            var candidates = allGames
                .Where(g => g.Status != status)
                .Select(g => g.Name)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var chosen = PickFromList("Spiele hinzufügen", "Wähle die Spiele aus, die in diese Spalte sollen.", candidates);
            if (chosen == null || chosen.Count == 0) return;

            foreach (string name in chosen)
            {
                var game = allGames.FirstOrDefault(g => g.Name == name);
                if (game == null) continue;
                game.Status = status;
                CopyStateToSettings(game);
            }

            SaveSettings();
            RefreshBoard();
            ApplyFilter();
            CheckAchievements(false);
        }

        private void RefreshReleases()
        {
            ReleasesList.Children.Clear();

            var items = settings.Releases.OrderBy(r => r.Date).ToList();
            if (items.Count == 0)
            {
                ReleasesList.Children.Add(new TextBlock
                {
                    Text = "Noch keine Einträge. Trage kommende Spiele ein, der Launcher zählt die Tage herunter.",
                    Foreground = BrushSubtle,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var release in items)
            {
                var entry = release;
                int days = (entry.Date.Date - DateTime.Today).Days;
                string countdown = days > 1 ? $"in {days} Tagen" : days == 1 ? "morgen" : days == 0 ? "heute!" : "erschienen";

                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

                row.Children.Add(new TextBlock
                {
                    Text = "🗓  " + entry.Name,
                    Foreground = System.Windows.Media.Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var date = new TextBlock { Text = entry.Date.ToString("dd.MM.yyyy"), Foreground = BrushSubtle, Margin = new Thickness(12, 0, 12, 0) };
                Grid.SetColumn(date, 1);
                row.Children.Add(date);

                var left = new TextBlock
                {
                    Text = countdown,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = days <= 0 ? BrushGold : System.Windows.Media.Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };
                Grid.SetColumn(left, 2);
                row.Children.Add(left);

                var menu = CreateMenu();
                AddMenuItem(menu, "🗑  Eintrag löschen", () =>
                {
                    settings.Releases.Remove(entry);
                    SaveSettings();
                    RefreshReleases();
                });
                row.ContextMenu = menu;

                ReleasesList.Children.Add(row);
            }
        }

        private void BtnAddRelease_Click(object sender, RoutedEventArgs e)
        {
            string? name = PromptText("Neuer Termin", "Name des kommenden Spiels:", string.Empty);
            if (name == null) return;

            string? dateText = PromptText("Erscheinungsdatum", "Datum im Format TT.MM.JJJJ:", DateTime.Today.AddDays(30).ToString("dd.MM.yyyy"));
            if (dateText == null) return;

            if (!DateTime.TryParse(dateText, CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out var date))
            {
                Msg("Das Datum konnte nicht gelesen werden. Bitte im Format TT.MM.JJJJ eingeben.", "Ungültiges Datum",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            settings.Releases.Add(new ReleaseEntry { Name = name, Date = date });
            SaveSettings();
            RefreshReleases();
        }

        // ───────────────────────────── Screenshot-Galerie ─────────────────────────────

        private static List<ShotInfo> FindScreenshots(List<GameItem> games, List<string> extraFolders)
        {
            var folders = new List<(string Path, string Label)>();

            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            folders.Add((System.IO.Path.Combine(pictures, "Screenshots"), "Windows"));
            folders.Add((System.IO.Path.Combine(videos, "Captures"), "Xbox Game Bar"));

            try
            {
                string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    string userdata = System.IO.Path.Combine(steamPath, "userdata");
                    if (Directory.Exists(userdata))
                    {
                        foreach (string user in Directory.GetDirectories(userdata))
                        {
                            string remote = System.IO.Path.Combine(user, "760", "remote");
                            if (!Directory.Exists(remote)) continue;

                            foreach (string appDir in Directory.GetDirectories(remote))
                            {
                                string shots = System.IO.Path.Combine(appDir, "screenshots");
                                if (!Directory.Exists(shots)) continue;

                                string appId = System.IO.Path.GetFileName(appDir);
                                string label = games.FirstOrDefault(g => g.AppId == appId)?.Name ?? "Steam";
                                folders.Add((shots, label));
                            }
                        }
                    }
                }
            }
            catch { }

            foreach (string folder in extraFolders)
                folders.Add((folder, System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'))));

            var results = new List<ShotInfo>();
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };

            foreach (var (folderPath, label) in folders)
            {
                try
                {
                    if (!Directory.Exists(folderPath)) continue;

                    foreach (string file in Directory.EnumerateFiles(folderPath))
                    {
                        if (!extensions.Contains(System.IO.Path.GetExtension(file))) continue;
                        results.Add(new ShotInfo { FilePath = file, Time = File.GetLastWriteTime(file), Label = label });
                    }
                }
                catch { }
            }

            return results.OrderByDescending(r => r.Time).ToList();
        }

        private static BitmapImage? LoadThumbnail(string path)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.DecodePixelWidth = 380;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private Border CreateShotTile(ShotInfo shot, out Border imageHost)
        {
            imageHost = new Border { Background = BrushFooter };

            var caption = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 8) };
            caption.Children.Add(new TextBlock
            {
                Text = shot.Label,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            caption.Children.Add(new TextBlock
            {
                Text = shot.Time.ToString("dd.MM.yyyy HH:mm"),
                Foreground = MakeBrush("#D1D5DB"),
                FontSize = 11
            });

            var shade = new Border
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Background = MakeBrush("#B0000000"),
                Padding = new Thickness(0, 6, 0, 0),
                Child = caption
            };

            var grid = new Grid();
            grid.Children.Add(imageHost);
            grid.Children.Add(shade);

            var menu = CreateMenu();
            AddMenuItem(menu, "🖼  Öffnen", () => OpenShell(shot.FilePath));
            AddMenuItem(menu, "📂  Im Ordner zeigen", () => OpenShell("explorer.exe", $"/select,\"{shot.FilePath}\""));
            AddMenuItem(menu, "📋  In die Zwischenablage kopieren", () =>
            {
                try
                {
                    var full = new BitmapImage(new Uri(shot.FilePath));
                    System.Windows.Clipboard.SetImage(full);
                    ShowToast("📋", "Kopiert", "Der Screenshot liegt in der Zwischenablage.", 3);
                }
                catch (Exception ex)
                {
                    Msg($"Kopieren fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            AddMenuItem(menu, "🎨  Als Hintergrundbild verwenden", () => SetBackgroundFromFile(shot.FilePath));

            var tile = new Border
            {
                Width = 250,
                Height = 156,
                Margin = new Thickness(0, 0, 14, 14),
                CornerRadius = new CornerRadius(12),
                Clip = new RectangleGeometry(new Rect(0, 0, 250, 156), 12, 12),
                Cursor = System.Windows.Input.Cursors.Hand,
                ContextMenu = menu,
                Child = grid
            };
            grid.Children.Add(CreateShotActions(tile, shot.FilePath));
            AddMenuItem(menu, "🗑  Löschen ...", () => DeleteShot(tile, shot.FilePath));
            tile.MouseLeftButtonUp += (s, e) => OpenShell(shot.FilePath);
            return tile;
        }

        private async Task LoadThumbnailIntoAsync(string path, Border host, System.Threading.SemaphoreSlim gate)
        {
            await gate.WaitAsync();
            try
            {
                var image = await Task.Run(() => LoadThumbnail(path));
                if (image != null)
                    host.Child = new System.Windows.Controls.Image { Source = image, Stretch = Stretch.UniformToFill };
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task RefreshGalleryAsync()
        {
            TxtGalleryStatus.Text = "Screenshots werden gesucht ...";

            var gamesSnapshot = allGames.ToList();
            var folders = settings.ScreenshotFolders.ToList();
            var shots = await Task.Run(() => FindScreenshots(gamesSnapshot, folders));

            GalleryContainer.Children.Clear();
            TxtGalleryStatus.Text = shots.Count == 0
                ? "Keine Screenshots gefunden. Füge über „Ordner hinzufügen“ deinen Screenshot-Ordner hinzu."
                : $"{shots.Count} Screenshots gefunden, die neuesten {Math.Min(60, shots.Count)} werden angezeigt.";

            using var gate = new System.Threading.SemaphoreSlim(3);
            var loaders = new List<Task>();
            foreach (var shot in shots.Take(60))
            {
                var tile = CreateShotTile(shot, out var host);
                GalleryContainer.Children.Add(tile);
                loaders.Add(LoadThumbnailIntoAsync(shot.FilePath, host, gate));
            }

            await Task.WhenAll(loaders);
        }

        private async void BtnRefreshGallery_Click(object sender, RoutedEventArgs e) => await RefreshGalleryAsync();

        private async void BtnAddShotFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Ordner mit Screenshots auswählen",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;

            if (!settings.ScreenshotFolders.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
            {
                settings.ScreenshotFolders.Add(dialog.SelectedPath);
                SaveSettings();
            }

            if (ViewGallery.Visibility == Visibility.Visible)
                await RefreshGalleryAsync();
            else
                ShowToast("🖼", "Ordner hinzugefügt", dialog.SelectedPath, 4);
        }

        // ───────────────────────────── Spielstände sichern & wiederherstellen ─────────────────────────────

        private static string SafeFileName(string name)
        {
            string safe = Regex.Replace(name, @"[^\w\-]+", "_");
            return safe.Length > 80 ? safe.Substring(0, 80) : safe;
        }

        private static string BackupDirFor(GameItem game)
            => System.IO.Path.Combine(SettingsDir, "backups", SafeFileName(game.Name));

        private static string? GuessSavePath(GameItem game)
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string clean = Regex.Replace(game.Name, @"[™®©:]", string.Empty).Trim();
            var names = new[] { game.Name, clean }.Distinct().ToList();
            var roots = new[]
            {
                System.IO.Path.Combine(documents, "My Games"),
                documents,
                System.IO.Path.Combine(documents, "Rockstar Games"),
                System.IO.Path.Combine(profile, "Saved Games"),
                local,
                roaming
            };

            foreach (string root in roots)
            {
                foreach (string name in names)
                {
                    string candidate = System.IO.Path.Combine(root, name);
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        private bool ChooseSavePath(GameItem game)
        {
            string? guess = GuessSavePath(game);
            if (guess != null)
            {
                var answer = Msg($"Mögliche Spielstände gefunden:\n{guess}\n\nDiesen Ordner verwenden?", "Spielstand-Ordner",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes)
                {
                    game.SavePath = guess;
                    SaveGameState(game);
                    return true;
                }
                if (answer == MessageBoxResult.Cancel) return false;
            }

            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = $"Ordner mit den Spielständen von „{game.Name}“ auswählen",
                UseDescriptionForTitle = true,
                SelectedPath = !string.IsNullOrEmpty(game.SavePath) ? game.SavePath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return false;

            game.SavePath = dialog.SelectedPath;
            SaveGameState(game);
            return true;
        }

        private static void PruneBackups(string directory, int keep)
        {
            try
            {
                foreach (var old in new DirectoryInfo(directory).GetFiles("*.zip").OrderByDescending(f => f.CreationTime).Skip(keep))
                    old.Delete();
            }
            catch { }
        }

        private async Task<bool> BackupSavesAsync(GameItem game, bool automatic)
        {
            if (string.IsNullOrEmpty(game.SavePath) || !Directory.Exists(game.SavePath)) return false;

            string directory = BackupDirFor(game);
            string file = System.IO.Path.Combine(directory, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + (automatic ? "_auto" : "") + ".zip");
            string source = game.SavePath;

            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(directory);
                    System.IO.Compression.ZipFile.CreateFromDirectory(source, file, System.IO.Compression.CompressionLevel.Optimal, false);
                    PruneBackups(directory, 10);
                    MirrorToCloud(file, System.IO.Path.Combine("Spielstaende", SafeFileName(game.Name)));
                });

                settings.BackupCount++;
                SaveSettings();
                CheckAchievements(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task BackupNowAsync(GameItem game)
        {
            if (string.IsNullOrEmpty(game.SavePath) && !ChooseSavePath(game)) return;

            bool ok = await BackupSavesAsync(game, false);
            if (ok)
                ShowToast("💾", "Spielstand gesichert", game.Name, 4);
            else
                Msg("Die Sicherung ist fehlgeschlagen. Läuft das Spiel noch oder ist der Ordner leer?", "Spielstand sichern",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ShowRestoreDialog(GameItem game)
        {
            string directory = BackupDirFor(game);
            var backups = Directory.Exists(directory)
                ? new DirectoryInfo(directory).GetFiles("*.zip").OrderByDescending(f => f.CreationTime).ToList()
                : new List<FileInfo>();

            if (string.IsNullOrEmpty(game.SavePath) || backups.Count == 0)
            {
                Msg(string.IsNullOrEmpty(game.SavePath)
                        ? "Lege zuerst den Spielstand-Ordner fest (Rechtsklick, dann „Spielstände“)."
                        : "Es gibt noch keine Sicherungen für dieses Spiel.",
                    "Spielstand wiederherstellen");
                return;
            }

            var dialog = CreateDialog("Spielstand wiederherstellen", 520, out var panel);
            panel.Children.Add(new TextBlock
            {
                Text = $"Wähle eine Sicherung von „{game.Name}“. Der aktuelle Stand wird vorher automatisch gesichert.",
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var list = new StackPanel();
            var options = new List<(System.Windows.Controls.RadioButton Button, FileInfo File)>();
            foreach (var backup in backups.Take(15))
            {
                var radio = new System.Windows.Controls.RadioButton
                {
                    Content = $"{backup.CreationTime:dd.MM.yyyy HH:mm}   ·   {FormatBytes(backup.Length)}" + (backup.Name.Contains("_auto") ? "   ·   automatisch" : string.Empty),
                    GroupName = "Backups",
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 8),
                    IsChecked = options.Count == 0
                };
                options.Add((radio, backup));
                list.Children.Add(radio);
            }
            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 16) });

            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancel = new System.Windows.Controls.Button { Content = "Abbrechen", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var restore = new System.Windows.Controls.Button { Content = "Wiederherstellen", Padding = new Thickness(22, 8, 22, 8) };
            restore.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            buttons.Children.Add(cancel);
            buttons.Children.Add(restore);
            panel.Children.Add(buttons);

            FileInfo? selected = null;
            restore.Click += (s, e) =>
            {
                selected = options.FirstOrDefault(o => o.Button.IsChecked == true).File;
                dialog.DialogResult = true;
            };

            if (dialog.ShowDialog() != true || selected == null) return;
            _ = RestoreSavesAsync(game, selected);
        }

        private async Task RestoreSavesAsync(GameItem game, FileInfo backup)
        {
            try
            {
                await BackupSavesAsync(game, true);
                string target = game.SavePath;
                await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(backup.FullName, target, true));
                ShowToast("↩", "Spielstand wiederhergestellt", game.Name, 5);
            }
            catch (Exception ex)
            {
                Msg($"Wiederherstellen fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────────────────────────── Startoptionen & Begleitprogramme ─────────────────────────────

        private static bool IsAppRunning(AppEntry app)
        {
            try
            {
                string processName = System.IO.Path.GetFileNameWithoutExtension(app.ExePath);
                var match = Regex.Match(app.Arguments ?? string.Empty, @"--processStart\s+(\S+)", RegexOptions.IgnoreCase);
                if (match.Success) processName = System.IO.Path.GetFileNameWithoutExtension(match.Groups[1].Value);

                var processes = Process.GetProcessesByName(processName);
                bool running = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                return running;
            }
            catch
            {
                return false;
            }
        }

        private void StartCompanions(GameItem game)
        {
            var names = settings.DefaultCompanions
                .Concat(game.CompanionApps)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0) return;

            foreach (string name in names)
            {
                var app = cachedApps.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
                if (app == null || IsAppRunning(app)) continue;
                LaunchApp(app);
            }
        }

        private List<string>? PickFromList(string title, string hint, List<string> items, ISet<string>? preselected = null)
        {
            var dialog = CreateDialog(title, 520, out var panel);
            panel.Children.Add(new TextBlock
            {
                Text = hint,
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var list = new StackPanel();
            var boxes = new List<System.Windows.Controls.CheckBox>();
            foreach (string item in items)
            {
                var box = new System.Windows.Controls.CheckBox
                {
                    Content = item,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 8),
                    IsChecked = preselected != null && preselected.Contains(item)
                };
                boxes.Add(box);
                list.Children.Add(box);
            }
            if (items.Count == 0)
                list.Children.Add(new TextBlock { Text = "Keine Einträge vorhanden.", Foreground = BrushSubtle });

            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 16) });

            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancel = new System.Windows.Controls.Button { Content = "Abbrechen", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var ok = new System.Windows.Controls.Button { Content = "OK", Padding = new Thickness(28, 8, 28, 8), IsDefault = true };
            ok.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            List<string>? result = null;
            ok.Click += (s, e) =>
            {
                result = boxes.Where(b => b.IsChecked == true).Select(b => b.Content?.ToString() ?? string.Empty).ToList();
                dialog.DialogResult = true;
            };

            return dialog.ShowDialog() == true ? result : null;
        }

        private void BtnPickCompanions_Click(object sender, RoutedEventArgs e)
        {
            var apps = cachedApps.Select(a => a.Name).ToList();
            var chosen = PickFromList("Standard-Begleitprogramme",
                "Diese Programme starten bei jedem Spielstart automatisch mit, falls sie noch nicht laufen.",
                apps, settings.DefaultCompanions.ToHashSet(StringComparer.OrdinalIgnoreCase));
            if (chosen == null) return;

            settings.DefaultCompanions = chosen;
            SaveSettings();
        }

        private void ShowLaunchOptions(GameItem game)
        {
            var dialog = CreateDialog("Startoptionen: " + game.Name, 560, out var panel);
            bool viaLauncher = !string.IsNullOrEmpty(game.LaunchUri);

            panel.Children.Add(new TextBlock
            {
                Text = viaLauncher
                    ? "Dieses Spiel startet über Steam, Epic oder die Xbox-App. Startparameter und Administrator-Start stellst du bitte dort ein."
                    : "Startparameter und Administrator-Start gelten für den direkten Start über die .exe-Datei.",
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            panel.Children.Add(new TextBlock { Text = "Startparameter", Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 6) });
            var args = new System.Windows.Controls.TextBox { Text = game.LaunchArgs, Height = 38, IsEnabled = !viaLauncher, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(args);

            var admin = new System.Windows.Controls.CheckBox
            {
                Content = "Als Administrator starten",
                Foreground = System.Windows.Media.Brushes.White,
                IsChecked = game.RunAsAdmin,
                IsEnabled = !viaLauncher,
                Margin = new Thickness(0, 0, 0, 18)
            };
            panel.Children.Add(admin);

            panel.Children.Add(new TextBlock { Text = "Begleitprogramme für dieses Spiel", Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 8) });
            var boxes = new Dictionary<string, System.Windows.Controls.CheckBox>();
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            foreach (var app in cachedApps)
            {
                var box = new System.Windows.Controls.CheckBox
                {
                    Content = app.Name,
                    Foreground = System.Windows.Media.Brushes.White,
                    IsChecked = game.CompanionApps.Contains(app.Name, StringComparer.OrdinalIgnoreCase),
                    Margin = new Thickness(0, 0, 18, 8)
                };
                boxes[app.Name] = box;
                wrap.Children.Add(box);
            }
            if (cachedApps.Count == 0)
                wrap.Children.Add(new TextBlock { Text = "Keine Anwendungen erkannt.", Foreground = BrushSubtle });
            panel.Children.Add(wrap);

            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancel = new System.Windows.Controls.Button { Content = "Abbrechen", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var save = new System.Windows.Controls.Button { Content = "Speichern", Padding = new Thickness(26, 8, 26, 8) };
            save.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            save.Click += (s, e) =>
            {
                game.LaunchArgs = args.Text.Trim();
                game.RunAsAdmin = admin.IsChecked == true;
                game.CompanionApps = boxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
                SaveGameState(game);
                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            dialog.ShowDialog();
        }

        private static void AddSubItem(System.Windows.Controls.MenuItem parent, string header, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = Loc.T(header) };
            item.Click += (s, e) => action();
            parent.Items.Add(item);
        }

        // ───────────────────────────── Spiele per Drag & Drop hinzufügen ─────────────────────────────

        private static string? ResolveShortcut(string lnkPath)
        {
            try
            {
                Type? type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;

                dynamic shell = Activator.CreateInstance(type)!;
                dynamic link = shell.CreateShortcut(lnkPath);
                string? target = link.TargetPath as string;
                return string.IsNullOrEmpty(target) ? null : target;
            }
            catch
            {
                return null;
            }
        }

        private bool AddManualGameFromPath(string exePath)
        {
            if (!File.Exists(exePath)) return false;
            if (allGames.Any(g => string.Equals(g.ExecutablePath, exePath, StringComparison.OrdinalIgnoreCase))) return false;

            string name = System.IO.Path.GetFileNameWithoutExtension(exePath);
            settings.ManualGames.Add(new ManualGame { Name = name, ExecutablePath = exePath });

            var game = new GameItem
            {
                Name = name,
                ExecutablePath = exePath,
                Source = "Manuell",
                LocalIcon = ExtractIcon(exePath)
            };
            AssignCachedCover(game);
            ApplyGameStates(new[] { game });
            allGames.Add(game);
            return true;
        }

        private void OnWindowDragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
                ? System.Windows.DragDropEffects.Copy
                : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void OnWindowDrop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files) return;

            var added = new List<GameItem>();
            foreach (string file in files)
            {
                string path = file;
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) path = ResolveShortcut(path) ?? path;
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                if (AddManualGameFromPath(path)) added.Add(allGames[allGames.Count - 1]);
            }

            if (added.Count == 0)
            {
                ShowToast("ℹ", "Nichts hinzugefügt", "Ziehe .exe-Dateien oder Verknüpfungen (.lnk) auf das Fenster.", 5);
                return;
            }

            SaveSettings();
            ApplyFilter();
            RefreshDashboard();
            ShowToast("✅", added.Count == 1 ? "Spiel hinzugefügt" : $"{added.Count} Spiele hinzugefügt", string.Join(", ", added.Select(g => g.Name).Take(3)), 4);
            _ = DownloadCoversAsync(added);
            e.Handled = true;
        }

        // ───────────────────────────── Ziele, Erfolge & Pausen ─────────────────────────────

        private double GetWeekSeconds()
        {
            double total = 0;
            for (int i = 0; i < 7; i++)
            {
                string key = DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (settings.PlayLog.TryGetValue(key, out double seconds)) total += seconds;
            }
            return total;
        }

        private bool HasPlayedOn(DateTime day)
            => settings.PlayLog.TryGetValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out double seconds) && seconds >= 300;

        private int GetPlayStreak()
        {
            int streak = 0;
            var day = DateTime.Today;
            if (!HasPlayedOn(day)) day = day.AddDays(-1);

            while (HasPlayedOn(day))
            {
                streak++;
                day = day.AddDays(-1);
            }
            return streak;
        }

        private List<AchievementDef> GetAchievementDefs()
        {
            var list = GetBaseAchievementDefs();
            if (EggsOn) list.AddRange(SecretAchievementDefs());
            return list;
        }

        private List<AchievementDef> GetBaseAchievementDefs()
        {
            int games = VisibleGames.Count();
            int launches = VisibleGames.Sum(g => g.LaunchCount);
            double hours = VisibleGames.Where(g => !IsTrackingIgnored(g)).Sum(g => g.PlaySeconds) / 3600.0;
            int streak = GetPlayStreak();
            int rated = VisibleGames.Count(g => g.Rating > 0);
            int favorites = VisibleGames.Count(g => g.IsFavorite);
            int notes = VisibleGames.Count(g => !string.IsNullOrWhiteSpace(g.Notes));
            int sources = VisibleGames.Select(g => g.Source).Distinct().Count();
            int done = VisibleGames.Count(g => g.Status == "done");
            bool goalReached = settings.WeeklyGoalHours > 0 && GetWeekSeconds() >= settings.WeeklyGoalHours * 3600.0;

            AchievementDef Def(string id, string icon, string title, string description, double current, double target)
                => new() { Id = id, Icon = icon, Title = title, Description = description, Current = current, Target = target };

            return new List<AchievementDef>
            {
                Def("first_launch", "🚀", "Erster Start", "Starte ein Spiel über den Launcher.", Math.Min(launches, 1), 1),
                Def("launch_50", "🎯", "Starthelfer", "Starte 50-mal ein Spiel über den Launcher.", launches, 50),
                Def("lib_10", "📚", "Sammler", "Habe 10 Spiele in deiner Bibliothek.", games, 10),
                Def("lib_25", "🗄", "Großsammler", "Habe 25 Spiele in deiner Bibliothek.", games, 25),
                Def("lib_50", "🏛", "Bibliothekar", "Habe 50 Spiele in deiner Bibliothek.", games, 50),
                Def("time_10", "⏱", "Aufgewärmt", "Spiele insgesamt 10 Stunden.", hours, 10),
                Def("time_100", "🏃", "Marathon", "Spiele insgesamt 100 Stunden.", hours, 100),
                Def("time_500", "👑", "Legende", "Spiele insgesamt 500 Stunden.", hours, 500),
                Def("streak_3", "🔥", "Dranbleiber", "Spiele 3 Tage in Folge.", streak, 3),
                Def("streak_7", "⚡", "Stammgast", "Spiele 7 Tage in Folge.", streak, 7),
                Def("streak_30", "🌟", "Unermüdlich", "Spiele 30 Tage in Folge.", streak, 30),
                Def("critic", "📝", "Kritiker", "Bewerte 10 Spiele.", rated, 10),
                Def("favorites", "⭐", "Lieblinge", "Markiere 5 Favoriten.", favorites, 5),
                Def("organizer", "📁", "Ordnungsliebe", "Lege 3 Sammlungen an.", settings.CollectionNames.Count, 3),
                Def("notes", "📓", "Notizbuch", "Schreibe zu 5 Spielen Notizen.", notes, 5),
                Def("multi", "🧩", "Alleskönner", "Habe Spiele aus 3 verschiedenen Quellen.", sources, 3),
                Def("night", "🌙", "Nachtschwärmer", "Spiele nach Mitternacht.", settings.NightOwl ? 1 : 0, 1),
                Def("goal", "🏆", "Ziel erreicht", "Erreiche dein Wochenziel.", goalReached ? 1 : 0, 1),
                Def("backup", "💾", "Vorsichtig", "Sichere einen Spielstand.", Math.Min(settings.BackupCount, 1), 1),
                Def("done", "✅", "Durchgespielt", "Verschiebe ein Spiel nach „Durchgespielt“.", Math.Min(done, 1), 1)
            };
        }

        private void CheckAchievements(bool silent)
        {
            if (allGames.Count == 0 && settings.Achievements.Count == 0 && !silent) return;

            var unlocked = new List<AchievementDef>();
            foreach (var def in GetAchievementDefs())
            {
                if (settings.Achievements.ContainsKey(def.Id)) continue;
                if (def.Current < def.Target) continue;

                settings.Achievements[def.Id] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                unlocked.Add(def);
            }

            if (unlocked.Count == 0) return;
            SaveSettings();

            if (!silent)
            {
                foreach (var def in unlocked.Take(3))
                    ShowToast("🏆", "Erfolg freigeschaltet: " + def.Title, def.Description, 7, () => NavigateTo("stats"));
                Confetti();
            }

            if (ViewStats.Visibility == Visibility.Visible) RefreshStatsExtras();
        }

        private void RefreshStatsExtras()
        {
            if (GoalCard == null || AchievementsList == null) return;

            // Wochenziel
            double week = GetWeekSeconds();
            if (settings.WeeklyGoalHours > 0)
            {
                double goal = settings.WeeklyGoalHours * 3600.0;
                GoalCard.Visibility = Visibility.Visible;
                GoalProgress.Value = Math.Min(100, week / goal * 100);
                TxtGoalText.Text = week >= goal
                    ? $"Geschafft! {FormatPlaytime((long)week)} von {settings.WeeklyGoalHours} Std. in den letzten 7 Tagen."
                    : $"{FormatPlaytime((long)week)} von {settings.WeeklyGoalHours} Std. in den letzten 7 Tagen.";
            }
            else
            {
                GoalCard.Visibility = Visibility.Collapsed;
            }

            // Erfolge
            var defs = GetAchievementDefs();
            var rows = defs.Select(d =>
            {
                bool done = settings.Achievements.ContainsKey(d.Id);
                bool hidden = d.Secret && !done;
                double percent = Math.Min(100, d.Current / d.Target * 100);
                return new AchievementRow
                {
                    Icon = hidden ? "❓" : d.Icon,
                    Title = hidden ? "???" : d.Title,
                    Description = hidden ? "Geheim: Finde das passende Easter Egg." : d.Description,
                    Unlocked = done,
                    Percent = done ? 100 : percent,
                    Opacity = done ? 1.0 : 0.5,
                    ProgressText = done ? "Freigeschaltet" : d.Secret ? "Geheim" : $"{Math.Min(d.Current, d.Target):F0} / {d.Target:F0}"
                };
            })
            .OrderByDescending(r => r.Unlocked)
            .ThenByDescending(r => r.Percent)
            .ToList();

            AchievementsList.ItemsSource = rows;
            TxtAchievementCount.Text = $"{rows.Count(r => r.Unlocked)} von {rows.Count} freigeschaltet";
        }

        private void CheckBreakReminders(DateTime now)
        {
            if (activeSessions.Count > 0 && now.Hour < 5 && !settings.NightOwl)
            {
                settings.NightOwl = true;
                CheckAchievements(false);
            }

            if (settings.BreakReminderMinutes <= 0) return;

            foreach (var pair in sessionStart.ToList())
            {
                var elapsed = now - pair.Value;
                int blocks = (int)(elapsed.TotalMinutes / settings.BreakReminderMinutes);
                sessionBreakCount.TryGetValue(pair.Key, out int notified);

                if (blocks > notified)
                {
                    sessionBreakCount[pair.Key] = blocks;
                    string text = $"Du spielst „{pair.Key.Name}“ seit {(int)elapsed.TotalHours} Std. {elapsed.Minutes} Min. Zeit für eine kurze Pause!";
                    overlayNotice = Loc.T("⏰ Zeit für eine Pause!");
                    overlayNoticeUntil = now.AddSeconds(30);
                    ShowToast("⏰", "Pause?", text, 12, null, true);
                }
            }
        }

        private void BtnYearReview_Click(object sender, RoutedEventArgs e) => ShowYearReview(DateTime.Now.Year);

        // ───────────────────────────── Spiel-Roulette ─────────────────────────────

        private void ShowRoulette()
        {
            var pool = VisibleGames.Where(g => g.Status == "backlog").ToList();
            if (pool.Count < 2) pool = VisibleGames.Where(g => g.PlaySeconds < 60 && g.LaunchCount == 0).ToList();
            if (pool.Count < 2) pool = VisibleGames.ToList();

            if (pool.Count == 0)
            {
                Msg("Es wurden noch keine Spiele gefunden.");
                return;
            }

            var dialog = CreateDialog("Spiel-Roulette", 520, out var panel);

            var caption = new TextBlock
            {
                Text = "Der Zufall wählt ...",
                Foreground = BrushSubtle,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            };
            panel.Children.Add(caption);

            var poster = new Border
            {
                Width = 120,
                Height = 180,
                CornerRadius = new CornerRadius(12),
                Background = BrushCardBg,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            };
            panel.Children.Add(poster);

            var nameText = new TextBlock
            {
                Text = "🎲",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 76,
                Margin = new Thickness(0, 0, 0, 18)
            };
            panel.Children.Add(nameText);

            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            var again = new System.Windows.Controls.Button { Content = "Nochmal drehen", Margin = new Thickness(0, 0, 10, 0), IsEnabled = false };
            var start = new System.Windows.Controls.Button { Content = "Starten", Padding = new Thickness(26, 8, 26, 8), Margin = new Thickness(0, 0, 10, 0), IsEnabled = false };
            start.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            var close = new System.Windows.Controls.Button { Content = "Schließen", IsCancel = true };
            buttons.Children.Add(again);
            buttons.Children.Add(start);
            buttons.Children.Add(close);
            panel.Children.Add(buttons);

            GameItem? winner = null;
            DispatcherTimer? timer = null;

            void Spin()
            {
                again.IsEnabled = false;
                start.IsEnabled = false;
                caption.Text = Loc.T("Der Zufall wählt ...");
                poster.Background = BrushCardBg;

                int step = 0;
                timer?.Stop();
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                timer.Tick += (s, e) =>
                {
                    step++;
                    winner = pool[random.Next(pool.Count)];
                    nameText.Text = winner.Name;

                    if (step >= 28)
                    {
                        timer!.Stop();
                        caption.Text = Loc.T("Heute spielst du:");
                        if (HasCover(winner) && GetCachedImage(winner.CoverPath) is BitmapImage image)
                            poster.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                        again.IsEnabled = true;
                        start.IsEnabled = true;
                        return;
                    }

                    timer!.Interval = TimeSpan.FromMilliseconds(40 + step * step * 1.5);
                };
                timer.Start();
            }

            again.Click += (s, e) => Spin();
            start.Click += (s, e) =>
            {
                dialog.DialogResult = true;
                if (winner != null) LaunchGame(winner);
            };
            dialog.Closed += (s, e) => timer?.Stop();
            dialog.Loaded += (s, e) => Spin();

            dialog.ShowDialog();
        }

        // ───────────────────────────── Bibliothek exportieren ─────────────────────────────

        private static string CsvEscape(string value)
            => "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";

        private List<string[]> BuildLibraryRows()
        {
            var rows = new List<string[]>
            {
                new[]
                {
                    Loc.T("Name"), Loc.T("Quelle"), Loc.T("Spielzeit (Std.)"), Loc.T("Gestartet"),
                    Loc.T("Zuletzt gespielt"), Loc.T("Bewertung"), Loc.T("Status"), Loc.T("Sammlungen"), Loc.T("Notizen")
                }
            };

            foreach (var game in allGames.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                string status = game.Status.Length == 0 ? string.Empty
                    : Loc.T(Regex.Replace(StatusOptions.First(o => o.Key == game.Status).Title, @"^[^\p{L}]+", string.Empty));

                rows.Add(new[]
                {
                    game.Name,
                    Loc.T(game.Source),
                    (game.PlaySeconds / 3600.0).ToString("F1", Loc.Culture),
                    game.LaunchCount.ToString(),
                    game.LastPlayed.HasValue ? game.LastPlayed.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty,
                    game.Rating > 0 ? game.Rating.ToString() : string.Empty,
                    status,
                    string.Join(", ", game.Collections),
                    game.Notes
                });
            }

            return rows;
        }

        private void BtnExportLibrary_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "DFP_Pro_Launcher_Library",
                Filter = "CSV (*.csv)|*.csv|HTML (*.html)|*.html",
                DefaultExt = ".csv"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var rows = BuildLibraryRows();
                bool html = dialog.FilterIndex == 2 || dialog.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
                var encoding = new System.Text.UTF8Encoding(true);

                if (html)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>DFP Pro Launcher</title><style>")
                      .Append("body{background:#0F111A;color:#E5E7EB;font-family:Segoe UI,Arial,sans-serif;padding:32px}")
                      .Append("h1{font-size:24px}table{border-collapse:collapse;width:100%}")
                      .Append("th,td{padding:8px 12px;border-bottom:1px solid #1F2937;text-align:left;font-size:14px}")
                      .Append("th{color:#9CA3AF;font-weight:600}tr:hover td{background:#161B28}</style></head><body>")
                      .Append("<h1>DFP Pro Launcher</h1><table>");

                    for (int i = 0; i < rows.Count; i++)
                    {
                        sb.Append("<tr>");
                        foreach (string cell in rows[i])
                            sb.Append(i == 0 ? "<th>" : "<td>").Append(System.Net.WebUtility.HtmlEncode(cell)).Append(i == 0 ? "</th>" : "</td>");
                        sb.Append("</tr>");
                    }

                    sb.Append("</table></body></html>");
                    File.WriteAllText(dialog.FileName, sb.ToString(), encoding);
                }
                else
                {
                    string separator = Loc.Culture.TextInfo.ListSeparator;
                    File.WriteAllLines(dialog.FileName, rows.Select(r => string.Join(separator, r.Select(CsvEscape))), encoding);
                }

                Msg("Die Bibliothek wurde exportiert.");
            }
            catch (Exception ex)
            {
                Msg($"Export fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────────────────────────── Steam-Spielzeit importieren ─────────────────────────────

        private static void ParseSteamApps(string text, Dictionary<string, long> minutes, Dictionary<string, long> lastPlayed)
        {
            foreach (Match start in Regex.Matches(text, "\"apps\"\\s*\\{", RegexOptions.IgnoreCase))
            {
                int i = start.Index + start.Length;
                int depth = 1;
                int blockStart = -1;
                string? currentId = null;

                while (i < text.Length && depth > 0)
                {
                    char c = text[i];

                    if (c == '"')
                    {
                        int end = text.IndexOf('"', i + 1);
                        if (end < 0) break;

                        string token = text.Substring(i + 1, end - i - 1);
                        if (depth == 1 && token.Length > 0 && token.All(char.IsDigit))
                        {
                            int j = end + 1;
                            while (j < text.Length && char.IsWhiteSpace(text[j])) j++;

                            if (j < text.Length && text[j] == '{')
                            {
                                currentId = token;
                                blockStart = j;
                                depth++;
                                i = j + 1;
                                continue;
                            }
                        }

                        i = end + 1;
                        continue;
                    }

                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 1 && currentId != null)
                        {
                            string block = text.Substring(blockStart, i - blockStart);

                            var playtime = Regex.Match(block, "\"Playtime\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase);
                            if (playtime.Success && long.TryParse(playtime.Groups[1].Value, out long value) && value > 0)
                                minutes[currentId] = Math.Max(value, minutes.TryGetValue(currentId, out long old) ? old : 0);

                            var played = Regex.Match(block, "\"LastPlayed\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase);
                            if (played.Success && long.TryParse(played.Groups[1].Value, out long stamp) && stamp > 0)
                                lastPlayed[currentId] = Math.Max(stamp, lastPlayed.TryGetValue(currentId, out long oldStamp) ? oldStamp : 0);

                            currentId = null;
                        }
                    }

                    i++;
                }
            }
        }

        private int ImportSteamPlaytime()
        {
            string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrEmpty(steamPath)) return 0;

            string userdata = System.IO.Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdata)) return 0;

            var minutes = new Dictionary<string, long>();
            var lastPlayed = new Dictionary<string, long>();

            foreach (string user in Directory.GetDirectories(userdata))
            {
                string file = System.IO.Path.Combine(user, "config", "localconfig.vdf");
                if (!File.Exists(file)) continue;

                try
                {
                    ParseSteamApps(File.ReadAllText(file), minutes, lastPlayed);
                }
                catch { }
            }

            int updated = 0;
            foreach (var game in allGames)
            {
                if (IsTrackingIgnored(game) || game.AppId.Length == 0 || !minutes.TryGetValue(game.AppId, out long value)) continue;

                bool changed = false;
                if (value * 60 > game.PlaySeconds)
                {
                    game.PlaySeconds = value * 60;
                    changed = true;
                }

                if (lastPlayed.TryGetValue(game.AppId, out long stamp))
                {
                    var time = DateTimeOffset.FromUnixTimeSeconds(stamp).LocalDateTime;
                    if (!game.LastPlayed.HasValue || time > game.LastPlayed.Value)
                    {
                        game.LastPlayed = time;
                        changed = true;
                    }
                }

                if (changed)
                {
                    CopyStateToSettings(game);
                    updated++;
                }
            }

            if (updated > 0) SaveSettings();
            return updated;
        }

        private void BtnImportSteamTime_Click(object sender, RoutedEventArgs e)
        {
            int updated;
            try
            {
                updated = ImportSteamPlaytime();
            }
            catch
            {
                updated = 0;
            }

            if (updated == 0)
            {
                Msg("Keine Steam-Spielzeit gefunden. Steam speichert sie nicht immer lokal.", "Steam-Spielzeit importieren");
                return;
            }

            ApplyFilter();
            RefreshDashboard();
            RefreshStats(false);
            CheckAchievements(false);
            ShowToast("⏱", $"{updated} Spiele aktualisiert.", string.Empty, 5);
        }

        // ───────────────────────────── Spiele verstecken ─────────────────────────────

        private void ToggleHidden(GameItem game)
        {
            game.Hidden = !game.Hidden;
            SaveGameState(game);
            ApplyFilter();
            RefreshDashboard();
            RefreshBoard();

            if (game.Hidden)
                ShowToast("🙈", "Spiel versteckt", game.Name + "\n" + Loc.T("Du findest es unter dem Filter „Versteckt“."), 5,
                    () => ToggleHidden(game));
        }

        private void BtnManageHidden_Click(object sender, RoutedEventArgs e)
        {
            var hidden = allGames.Where(g => g.Hidden).Select(g => g.Name)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

            if (hidden.Count == 0)
            {
                Msg("Es sind keine Spiele versteckt. Verstecke Spiele per Rechtsklick auf die Karte.", "Versteckte Spiele");
                return;
            }

            var keep = PickFromList("Versteckte Spiele",
                "Häkchen = bleibt versteckt. Entferne das Häkchen, um ein Spiel wieder anzuzeigen.",
                hidden, hidden.ToHashSet(StringComparer.OrdinalIgnoreCase));
            if (keep == null) return;

            foreach (var game in allGames.Where(g => g.Hidden))
            {
                if (keep.Contains(game.Name)) continue;
                game.Hidden = false;
                CopyStateToSettings(game);
            }

            SaveSettings();
            ApplyFilter();
            RefreshDashboard();
            RefreshBoard();
        }

        // ───────────────────────────── Eigene Cover und animierte GIFs ─────────────────────────────

        private static string CustomCoverDir => System.IO.Path.Combine(SettingsDir, "custom");

        private static string EffectiveCover(GameItem game)
            => !string.IsNullOrEmpty(game.CustomCover) && File.Exists(game.CustomCover) ? game.CustomCover : game.CoverPath;


        private static bool IsImageFile(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".apng";
        }

        private void SetCustomCover(GameItem game, string sourceFile)
        {
            try
            {
                Directory.CreateDirectory(CustomCoverDir);
                string baseName = SafeFileName(game.Name);
                string target = System.IO.Path.Combine(CustomCoverDir, baseName + System.IO.Path.GetExtension(sourceFile).ToLowerInvariant());

                foreach (string old in Directory.EnumerateFiles(CustomCoverDir, baseName + ".*"))
                {
                    if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase)) File.Delete(old);
                }
                if (!string.Equals(sourceFile, target, StringComparison.OrdinalIgnoreCase)) File.Copy(sourceFile, target, true);

                game.CustomCover = target;
                imageCache.Clear();
                gifCache.Clear();
                SaveGameState(game);
                ApplyFilter();
                RefreshDashboard();
                RefreshBoard();
                ShowToast("🖼", "Eigenes Cover gespeichert", game.Name, 4);
            }
            catch (Exception ex)
            {
                Msg($"Das Bild konnte nicht geladen werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChooseCustomCover(GameItem game) => ShowCoverPicker(game);

        private void ChooseCoverFromFile(GameItem game)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Cover auswählen"),
                Filter = Loc.T("Bilder, GIF und WebP") + " (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng"
            };
            if (dialog.ShowDialog() == true) SetCustomCover(game, dialog.FileName);
        }

        private void ResetCustomCover(GameItem game)
        {
            try
            {
                if (!string.IsNullOrEmpty(game.CustomCover) && File.Exists(game.CustomCover)) File.Delete(game.CustomCover);
            }
            catch { }

            game.CustomCover = string.Empty;
            imageCache.Clear();
            gifCache.Clear();
            SaveGameState(game);
            ApplyFilter();
            RefreshDashboard();
            RefreshBoard();
        }

        /// <summary>Ein Bild auf eine Karte ziehen setzt es als Cover.</summary>
        private void HookCoverDrop(UIElement element, GameItem game)
        {
            element.AllowDrop = true;
            element.DragOver += (s, e) =>
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Any(IsImageFile))
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                    e.Handled = true;
                }
            };
            element.Drop += (s, e) =>
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files) return;

                string? image = files.FirstOrDefault(IsImageFile);
                if (image == null) return;

                SetCustomCover(game, image);
                e.Handled = true;
            };
        }

        private void AttachGif(System.Windows.Controls.Image image, UIElement hoverTarget, string path)
        {
            if (settings.PerformanceMode) return;

            GifAnimator? animator = null;
            bool wanted = false;

            async Task StartAsync()
            {
                wanted = true;

                if (animator == null)
                {
                    if (gifCache.TryGetValue(path, out var cachedAnimator))
                    {
                        animator = cachedAnimator;
                    }
                    else
                    {
                        if (gifFailed.Contains(path)) return;

                        var loaded = await Task.Run(() => GifAnimator.TryLoad(path, 300));
                        if (loaded == null)
                        {
                            gifFailed.Add(path);
                            LogError("Animation", new InvalidOperationException(System.IO.Path.GetFileName(path) + ": " + (GifAnimator.LastError ?? "unbekannt")));
                            return;
                        }

                        if (gifCache.Count >= 4) gifCache.Clear();
                        gifCache[path] = loaded;
                        animator = loaded;
                    }
                }

                if (wanted) animator.Attach(image);
            }

            void Stop()
            {
                wanted = false;
                animator?.Detach();
            }

            if (settings.AlwaysPlayGifs)
            {
                image.Loaded += async (s, e) => await StartAsync();
                image.Unloaded += (s, e) => Stop();
            }
            else
            {
                hoverTarget.MouseEnter += async (s, e) => await StartAsync();
                hoverTarget.MouseLeave += (s, e) => Stop();
            }
        }

        private string HeroFilePath(GameItem game)
            => System.IO.Path.Combine(CoversDir, SafeFileName(game.Source + "_" + game.Name) + "_hero.jpg");

        private BitmapImage? GetHeroImage(GameItem game)
        {
            try
            {
                string file = HeroFilePath(game);
                if (!File.Exists(file) || new FileInfo(file).Length < 5000) return null;

                string key = file + "|hero|" + settings.PerformanceMode;
                if (imageCache.TryGetValue(key, out var cached)) return cached;

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(file);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = settings.PerformanceMode ? 960 : 1920;
                image.EndInit();
                image.Freeze();
                imageCache[key] = image;
                return image;
            }
            catch
            {
                return null;
            }
        }

        private readonly HashSet<string> heroRequests = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Lädt das breite Originalbild eines Steam-Spiels (für das Banner im Dashboard).</summary>
        private async Task EnsureHeroAsync(GameItem game)
        {
            if (!settings.OnlineFeatures || game.Source != "Steam" || game.AppId.Length == 0) return;
            if (!heroRequests.Add(game.Name)) return;

            string file = HeroFilePath(game);
            if (File.Exists(file)) return;

            try
            {
                Directory.CreateDirectory(CoversDir);
                string url = $"https://cdn.akamai.steamstatic.com/steam/apps/{game.AppId}/library_hero.jpg";
                if (await TryDownloadAsync(new[] { url }, file)) RefreshDashboard();
            }
            catch { }
        }

        // ───────────────────────────── Spielzeit-Ausnahmen (z. B. Wallpaper Engine) ─────────────────────────────

        private bool IsTrackingIgnored(GameItem game)
            => game.AppId == "431960"
               || BuiltInIgnored.Any(p => game.Name.Contains(p, StringComparison.OrdinalIgnoreCase))
               || settings.TrackingIgnored.Contains(game.Name, StringComparer.OrdinalIgnoreCase);

        private void ToggleTrackingIgnored(GameItem game)
        {
            if (settings.TrackingIgnored.RemoveAll(n => string.Equals(n, game.Name, StringComparison.OrdinalIgnoreCase)) == 0)
                settings.TrackingIgnored.Add(game.Name);

            activeSessions.Remove(game);
            sessionStart.Remove(game);
            SaveSettings();
            ApplyFilter();
            RefreshDashboard();
        }

        private void BtnPickIgnored_Click(object sender, RoutedEventArgs e)
        {
            var names = allGames.Select(g => g.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            var chosen = PickFromList("Spielzeit-Ausnahmen",
                "Für diese Programme wird keine Spielzeit erfasst. Wallpaper Engine, Lively und Rainmeter sind bereits automatisch ausgenommen.",
                names, settings.TrackingIgnored.ToHashSet(StringComparer.OrdinalIgnoreCase));
            if (chosen == null) return;

            settings.TrackingIgnored = chosen;
            foreach (var game in allGames.Where(IsTrackingIgnored))
            {
                activeSessions.Remove(game);
                sessionStart.Remove(game);
            }
            SaveSettings();
            ApplyFilter();
            RefreshDashboard();
        }

        // ───────────────────────────── Logos der Stores (Angebote) ─────────────────────────────

        private ImageSource? GetStoreLogo(string store)
        {
            if (storeLogos.TryGetValue(store, out var cached) && cached != null) return cached;

            ImageSource? logo = null;
            try
            {
                string? exe = null;
                if (store == "steam")
                {
                    string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                    if (!string.IsNullOrEmpty(steamPath)) exe = System.IO.Path.GetFullPath(System.IO.Path.Combine(steamPath, "steam.exe"));
                }
                else if (store == "epic")
                {
                    string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    exe = new[]
                    {
                        System.IO.Path.Combine(root, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
                        System.IO.Path.Combine(root, "Epic Games", "Launcher", "Portal", "Binaries", "Win32", "EpicGamesLauncher.exe")
                    }.FirstOrDefault(File.Exists);
                }

                if (exe != null && File.Exists(exe)) logo = ExtractIcon(exe);
            }
            catch { }

            // Ohne installierten Launcher: Seiten-Symbol aus dem Internet
            if (logo == null && settings.OnlineFeatures)
            {
                string domain = store == "steam" ? "store.steampowered.com" : "store.epicgames.com";
                logo = LoadWebImage("https://www.google.com/s2/favicons?domain=" + domain + "&sz=64");
            }

            storeLogos[store] = logo;
            return logo;
        }

        private void ApplyStoreLogos()
        {
            if (ImgEpicFree == null) return;

            var epic = GetStoreLogo("epic");
            var steam = GetStoreLogo("steam");
            ImgEpicFree.Source = epic;
            ImgEpicSoon.Source = epic;
            ImgSteamDeals.Source = steam;
        }

        // ───────────────────────────── SteamGridDB (Cover in Originalqualität) ─────────────────────────────

        private async Task<string?> SgdbGetAsync(string path)
        {
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://www.steamgriddb.com/api/v2" + path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.SteamGridDbKey.Trim());
            using var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
        }

        private static string? FirstSgdbGridUrl(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;

            foreach (var item in data.EnumerateArray())
            {
                string url = GetJsonString(item, "url");
                if (url.Length > 0) return url;
            }
            return null;
        }

        private async Task<string?> FindSgdbCoverUrlAsync(GameItem game)
        {
            if (string.IsNullOrWhiteSpace(settings.SteamGridDbKey) || !settings.OnlineFeatures) return null;

            try
            {
                const string query = "dimensions=600x900,342x482&types=static&mimes=image/png,image/jpeg&nsfw=false&humor=false";

                if (game.Source == "Steam" && game.AppId.Length > 0)
                {
                    string? url = FirstSgdbGridUrl(await SgdbGetAsync($"/grids/steam/{game.AppId}?{query}"));
                    if (url != null) return url;
                }

                string? search = await SgdbGetAsync("/search/autocomplete/" + Uri.EscapeDataString(game.Name));
                if (string.IsNullOrEmpty(search)) return null;

                long id = 0;
                using (var doc = JsonDocument.Parse(search))
                {
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        string wanted = NormalizeTitle(game.Name);
                        foreach (var item in data.EnumerateArray())
                        {
                            if (id == 0) id = JsonLong(item, "id");
                            if (NormalizeTitle(GetJsonString(item, "name")) == wanted)
                            {
                                id = JsonLong(item, "id");
                                break;
                            }
                        }
                    }
                }

                return id > 0 ? FirstSgdbGridUrl(await SgdbGetAsync($"/grids/game/{id}?{query}")) : null;
            }
            catch
            {
                return null;
            }
        }

        // ───────── Cover-Auswahl direkt aus SteamGridDB ─────────

        private sealed class SgdbGrid
        {
            public string Url { get; init; } = string.Empty;
            public string Thumb { get; init; } = string.Empty;
            public int Width { get; init; }
            public int Height { get; init; }
            public bool Animated { get; init; }
        }

        private static List<SgdbGrid> ParseSgdbGrids(string? json)
        {
            var list = new List<SgdbGrid>();
            if (string.IsNullOrEmpty(json)) return list;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;

                foreach (var item in data.EnumerateArray())
                {
                    string url = GetJsonString(item, "url");
                    if (url.Length == 0) continue;
                    string thumb = GetJsonString(item, "thumb");
                    string mime = GetJsonString(item, "mime");
                    string ext = GridExtension(url);
                    list.Add(new SgdbGrid
                    {
                        Url = url,
                        Thumb = thumb.Length > 0 ? thumb : url,
                        Width = (int)JsonLong(item, "width"),
                        Height = (int)JsonLong(item, "height"),
                        Animated = ext is ".gif" or ".webp" or ".apng" || mime.Contains("gif") || mime.Contains("webp")
                    });
                }
            }
            catch { }

            return list;
        }

        private static List<(long Id, string Name)> ParseSgdbGames(string? json)
        {
            var list = new List<(long, string)>();
            if (string.IsNullOrEmpty(json)) return list;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
                foreach (var item in data.EnumerateArray())
                {
                    long id = JsonLong(item, "id");
                    if (id > 0) list.Add((id, GetJsonString(item, "name")));
                }
            }
            catch { }

            return list;
        }

        /// <summary>Cover-Auswahl: Suche bei SteamGridDB mit Vorschaubildern, ein Klick setzt das Cover. Eigene Datei geht weiterhin.</summary>
        private void ShowCoverPicker(GameItem game)
        {
            var dialog = CreateDialog("Cover auswählen", 940, out var panel);
            bool closed = false;
            int searchVersion = 0;
            dialog.Closed += (s, e) => closed = true;

            panel.Children.Add(new TextBlock { Text = game.Name, Foreground = System.Windows.Media.Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Klicke auf ein Cover, um es zu übernehmen. Die Bilder kommen direkt von SteamGridDB."),
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 14)
            });

            // Schlüssel fehlt: direkt hier eingeben
            var keyBox = new Border
            {
                Background = MakeBrush("#14FFFFFF"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var keyStack = new StackPanel();
            keyStack.Children.Add(new TextBlock
            {
                Text = Loc.T("Für die Suche brauchst du einen kostenlosen SteamGridDB-Schlüssel. Du findest ihn nach dem Anmelden unter Profil → Einstellungen → API."),
                Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            var keyRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var keyInput = new PasswordBox { Height = 38, VerticalContentAlignment = System.Windows.VerticalAlignment.Center };
            var keySave = new System.Windows.Controls.Button { Content = Loc.T("Speichern"), Margin = new Thickness(10, 0, 0, 0) };
            keySave.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            var keyGet = new System.Windows.Controls.Button { Content = Loc.T("Schlüssel holen"), Margin = new Thickness(10, 0, 0, 0) };
            keyGet.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://www.steamgriddb.com/profile/preferences/api") { UseShellExecute = true }); }
                catch { }
            };
            Grid.SetColumn(keySave, 1);
            Grid.SetColumn(keyGet, 2);
            keyRow.Children.Add(keyInput);
            keyRow.Children.Add(keySave);
            keyRow.Children.Add(keyGet);
            keyStack.Children.Add(keyRow);
            keyBox.Child = keyStack;
            panel.Children.Add(keyBox);

            // Suche
            var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var searchInput = new System.Windows.Controls.TextBox { Text = game.Name, Height = 38, VerticalContentAlignment = System.Windows.VerticalAlignment.Center };
            var searchButton = new System.Windows.Controls.Button { Content = "🔎  " + Loc.T("Suchen"), Margin = new Thickness(10, 0, 0, 0) };
            var animatedCheck = new System.Windows.Controls.CheckBox { Content = Loc.T("Auch animierte"), IsChecked = settings.SgdbAnimated, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(searchButton, 1);
            Grid.SetColumn(animatedCheck, 2);
            searchRow.Children.Add(searchInput);
            searchRow.Children.Add(searchButton);
            searchRow.Children.Add(animatedCheck);
            panel.Children.Add(searchRow);

            var gameChips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            panel.Children.Add(gameChips);

            var status = new TextBlock { Foreground = BrushSubtle, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(status);

            var grid = new WrapPanel();
            var scroller = new ScrollViewer
            {
                Content = grid,
                Height = 470,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            panel.Children.Add(scroller);

            // Knöpfe unten
            var bottom = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            var fromFile = new System.Windows.Controls.Button { Content = "📁  " + Loc.T("Aus Datei wählen …"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            fromFile.Click += (s, e) =>
            {
                dialog.Close();
                ChooseCoverFromFile(game);
            };
            var close = new System.Windows.Controls.Button { Content = Loc.T("Abbrechen"), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, IsCancel = true };
            bottom.Children.Add(fromFile);
            bottom.Children.Add(close);
            panel.Children.Add(bottom);

            bool HasKey() => !string.IsNullOrWhiteSpace(settings.SteamGridDbKey);
            System.Windows.Controls.Primitives.ToggleButton? activeChip = null;

            void ShowGrids(List<SgdbGrid> grids, int version)
            {
                if (closed || version != searchVersion) return;
                grid.Children.Clear();
                if (grids.Count == 0)
                {
                    status.Text = Loc.T("Keine Cover gefunden. Versuche einen anderen Suchbegriff.");
                    return;
                }

                status.Text = Loc.T($"{grids.Count} Cover gefunden.");
                foreach (var item in grids.Take(60))
                    grid.Children.Add(BuildGridTile(item));
            }

            FrameworkElement BuildGridTile(SgdbGrid item)
            {
                var tile = new Border
                {
                    Width = 136,
                    Height = 204,
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 12, 12),
                    Background = MakeBrush("#1AFFFFFF"),
                    BorderThickness = new Thickness(2),
                    BorderBrush = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = item.Width > 0 ? $"{item.Width} × {item.Height}" : null
                };
                var inner = new Grid();
                inner.Children.Add(new TextBlock { Text = "…", Foreground = BrushSubtle, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                if (item.Animated)
                {
                    var badge = new Border
                    {
                        Background = MakeBrush("#CC000000"),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(6),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                        VerticalAlignment = System.Windows.VerticalAlignment.Top,
                        Child = new TextBlock { Text = "▶ " + Loc.T("animiert"), Foreground = System.Windows.Media.Brushes.White, FontSize = 11 }
                    };
                    inner.Children.Add(badge);
                }
                tile.Child = inner;

                tile.MouseEnter += (s, e) => tile.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                tile.MouseLeave += (s, e) => tile.BorderBrush = System.Windows.Media.Brushes.Transparent;
                tile.MouseLeftButtonUp += async (s, e) =>
                {
                    status.Text = Loc.T("Cover wird geladen …");
                    grid.IsEnabled = false;
                    string? file = await Task.Run(async () =>
                    {
                        try
                        {
                            byte[] bytes = await Http.GetByteArrayAsync(item.Url);
                            string ext = GridExtension(item.Url);
                            if (ext.Length == 0) ext = ".png";
                            string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dfp_cover_" + Guid.NewGuid().ToString("N") + ext);
                            await File.WriteAllBytesAsync(temp, bytes);
                            return temp;
                        }
                        catch
                        {
                            return null;
                        }
                    });

                    if (closed) return;
                    if (file == null)
                    {
                        grid.IsEnabled = true;
                        status.Text = Loc.T("Das Cover konnte nicht geladen werden. Bitte versuche es noch einmal.");
                        return;
                    }

                    SetCustomCover(game, file);
                    try { File.Delete(file); } catch { }
                    dialog.Close();
                };

                // Vorschau im Hintergrund laden
                _ = Task.Run(async () =>
                {
                    try
                    {
                        byte[] bytes = await Http.GetByteArrayAsync(item.Thumb);
                        Dispatcher.Invoke(() =>
                        {
                            if (closed) return;
                            try
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.StreamSource = new MemoryStream(bytes);
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.DecodePixelWidth = 280;
                                bitmap.EndInit();
                                bitmap.Freeze();
                                inner.Children.RemoveAt(0);
                                inner.Children.Insert(0, new Border
                                {
                                    CornerRadius = new CornerRadius(8),
                                    Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill }
                                });
                            }
                            catch
                            {
                                if (inner.Children[0] is TextBlock text) text.Text = item.Animated ? "▶" : "?";
                            }
                        });
                    }
                    catch { }
                });

                return tile;
            }

            string GridQuery()
            {
                const string common = "dimensions=600x900,342x482,660x930&nsfw=false&humor=false";
                return animatedCheck.IsChecked == true
                    ? common + "&types=static,animated"
                    : common + "&types=static&mimes=image/png,image/jpeg";
            }

            async Task LoadGrids(string basePath)
            {
                int version = ++searchVersion;
                status.Text = Loc.T("Suche Cover …");
                grid.Children.Clear();
                grid.IsEnabled = true;
                string query = GridQuery();
                var grids = await Task.Run(async () =>
                {
                    try { return ParseSgdbGrids(await SgdbGetAsync($"{basePath}?{query}")); }
                    catch { return new List<SgdbGrid>(); }
                });
                ShowGrids(grids, version);
            }

            async Task Search()
            {
                if (!HasKey()) return;
                if (!settings.OnlineFeatures)
                {
                    status.Text = Loc.T("Die Online-Funktionen sind ausgeschaltet. Du kannst sie in den Einstellungen wieder einschalten.");
                    return;
                }

                string term = searchInput.Text.Trim();
                if (term.Length == 0) return;

                int version = ++searchVersion;
                status.Text = Loc.T("Suche Cover …");
                gameChips.Children.Clear();
                grid.Children.Clear();

                var games = await Task.Run(async () =>
                {
                    try { return ParseSgdbGames(await SgdbGetAsync("/search/autocomplete/" + Uri.EscapeDataString(term))); }
                    catch { return new List<(long Id, string Name)>(); }
                });
                if (closed || version != searchVersion) return;

                // Steam-Spiele: erst die Cover zur genauen Steam-Nummer
                bool steamFirst = game.Source == "Steam" && game.AppId.Length > 0 && string.Equals(term, game.Name, StringComparison.OrdinalIgnoreCase);

                if (games.Count == 0 && !steamFirst)
                {
                    status.Text = Loc.T("Kein Spiel mit diesem Namen gefunden. Versuche einen anderen Suchbegriff.");
                    return;
                }

                string wanted = NormalizeTitle(term);
                int preferred = Math.Max(0, games.FindIndex(g => NormalizeTitle(g.Name) == wanted));

                for (int i = 0; i < games.Count && i < 8; i++)
                {
                    var entry = games[i];
                    var chip = new System.Windows.Controls.RadioButton
                    {
                        Content = entry.Name,
                        GroupName = "SgdbGame" + game.GetHashCode(),
                        Margin = new Thickness(0, 0, 8, 8)
                    };
                    if (TryFindResource("ChipStyle") is Style chipStyle) chip.Style = chipStyle;
                    chip.Checked += async (s, e) =>
                    {
                        if (activeChip == chip) return;
                        activeChip = chip;
                        await LoadGrids($"/grids/game/{entry.Id}");
                    };
                    gameChips.Children.Add(chip);
                }

                if (steamFirst)
                {
                    activeChip = null;
                    await LoadGrids($"/grids/steam/{game.AppId}");
                }
                else if (gameChips.Children.Count > preferred && gameChips.Children[preferred] is System.Windows.Controls.RadioButton first)
                {
                    first.IsChecked = true;
                }
            }

            void UpdateKeyState()
            {
                keyBox.Visibility = HasKey() ? Visibility.Collapsed : Visibility.Visible;
                searchRow.IsEnabled = HasKey();
                if (!HasKey()) status.Text = Loc.T("Ohne Schlüssel kannst du ein Cover aus einer Datei wählen.");
            }

            keySave.Click += async (s, e) =>
            {
                string key = keyInput.Password.Trim();
                if (key.Length == 0) return;
                settings.SteamGridDbKey = key;
                SaveSettings();
                if (TxtSgdbKey != null) TxtSgdbKey.Text = key;
                UpdateKeyState();
                await Search();
            };
            searchButton.Click += async (s, e) => await Search();
            searchInput.KeyDown += async (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    await Search();
                }
            };
            animatedCheck.Click += async (s, e) =>
            {
                if (activeChip != null)
                {
                    var current = activeChip;
                    activeChip = null;
                    current.IsChecked = false;
                    current.IsChecked = true;
                }
                else
                {
                    await Search();
                }
            };

            dialog.Loaded += async (s, e) =>
            {
                UpdateKeyState();
                await Search();
            };
            dialog.ShowDialog();
        }

        private async void BtnTestSgdb_Click(object sender, RoutedEventArgs e)
        {
            string key = TxtSgdbKey.Text.Trim();
            if (key.Length == 0)
            {
                TxtSgdbStatus.Text = Loc.T("Bitte zuerst einen Schlüssel eingeben.");
                return;
            }

            string previous = settings.SteamGridDbKey;
            settings.SteamGridDbKey = key;
            TxtSgdbStatus.Text = Loc.T("Teste Verbindung ...");

            try
            {
                string? result = await SgdbGetAsync("/search/autocomplete/portal");
                TxtSgdbStatus.Text = result != null
                    ? Loc.T("Verbindung erfolgreich. Der Schlüssel funktioniert.")
                    : Loc.T("Der Schlüssel wurde abgelehnt oder SteamGridDB ist nicht erreichbar.");
            }
            catch
            {
                TxtSgdbStatus.Text = Loc.T("Der Schlüssel wurde abgelehnt oder SteamGridDB ist nicht erreichbar.");
            }
            finally
            {
                settings.SteamGridDbKey = previous;
            }
        }

        private void BtnSaveSgdb_Click(object sender, RoutedEventArgs e)
        {
            settings.SteamGridDbKey = TxtSgdbKey.Text.Trim();
            SaveSettings();
            TxtSgdbStatus.Text = settings.SteamGridDbKey.Length > 0
                ? Loc.T("Gespeichert. Neue Cover kommen jetzt zuerst von SteamGridDB.")
                : Loc.T("Schlüssel entfernt. Es werden wieder Steam- und Epic-Cover verwendet.");

            BtnReloadCovers_Click(sender, e);
        }

        // ───────────────────────────── Spielzeit von Hand anpassen ─────────────────────────────

        private void EditPlaytime(GameItem game)
        {
            string current = (game.PlaySeconds / 3600.0).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
            string? input = PromptText("Spielzeit bearbeiten",
                $"Gesamte Spielzeit von „{game.Name}“ in Stunden (zum Beispiel 12,5):", current);
            if (input == null) return;

            string cleaned = input.Trim().Replace(',', '.');
            if (!double.TryParse(cleaned, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double hours) || hours < 0 || hours > 100000)
            {
                Msg("Bitte gib eine Zahl ein (zum Beispiel 12,5).", "Spielzeit bearbeiten", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            game.PlaySeconds = (long)Math.Round(hours * 3600);
            SaveGameState(game);
            ApplyFilter();
            RefreshDashboard();
        }

        // ───────────────────────────── Animierte Cover: WebP, APNG und GIF ─────────────────────────────

        /// <summary>GIF, WebP und APNG können Animationen enthalten.</summary>
        private static bool IsAnimatedCover(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".gif" or ".webp" or ".apng") return true;
            if (ext != ".png") return false;

            try
            {
                using var stream = File.OpenRead(path);
                var buffer = new byte[1024];
                int read = stream.Read(buffer, 0, buffer.Length);
                return System.Text.Encoding.ASCII.GetString(buffer, 0, read).Contains("acTL", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Liest ein Standbild, das WPF selbst nicht kennt (zum Beispiel WebP), über ImageSharp.</summary>
        private static BitmapImage? DecodeStillWithImageSharp(string path, int decodeWidth)
        {
            try
            {
                var options = new SixLabors.ImageSharp.Formats.DecoderOptions { MaxFrames = 1 };
                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(options, path);
                using var stream = new MemoryStream();
                image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                stream.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static string CoverBasePath(GameItem game) => System.IO.Path.ChangeExtension(CoverFilePath(game), null);

        /// <summary>Sucht ein gespeichertes Cover. Animierte Fassungen haben Vorrang.</summary>
        private static string? FindCachedCover(GameItem game)
        {
            string basePath = CoverBasePath(game);
            foreach (string ext in new[] { ".webp", ".apng", ".jpg" })
            {
                string file = basePath + ext;
                try
                {
                    if (File.Exists(file) && new FileInfo(file).Length > 1500) return file;
                }
                catch { }
            }
            return null;
        }

        private static void RemoveOtherCoverVariants(GameItem game, string keep)
        {
            string basePath = CoverBasePath(game);
            foreach (string ext in new[] { ".webp", ".apng", ".jpg" })
            {
                string file = basePath + ext;
                try
                {
                    if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase) && File.Exists(file)) File.Delete(file);
                }
                catch { }
            }
        }

        private static string GridExtension(string url)
        {
            try
            {
                return System.IO.Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static List<string> SgdbGridUrls(string? json)
        {
            var urls = new List<string>();
            if (string.IsNullOrEmpty(json)) return urls;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return urls;

                foreach (var item in data.EnumerateArray())
                {
                    string url = GetJsonString(item, "url");
                    if (url.Length > 0) urls.Add(url);
                }
            }
            catch { }

            return urls;
        }

        private async Task<long> FindSgdbGameIdAsync(string name)
        {
            string? search = await SgdbGetAsync("/search/autocomplete/" + Uri.EscapeDataString(name));
            if (string.IsNullOrEmpty(search)) return 0;

            long id = 0;
            using var doc = JsonDocument.Parse(search);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return 0;

            string wanted = NormalizeTitle(name);
            foreach (var item in data.EnumerateArray())
            {
                if (id == 0) id = JsonLong(item, "id");
                if (NormalizeTitle(GetJsonString(item, "name")) == wanted)
                {
                    id = JsonLong(item, "id");
                    break;
                }
            }
            return id;
        }

        private async Task<(string Url, bool Animated)?> SgdbPickFromBaseAsync(string basePath, bool animatedOnly = false)
        {
            const string common = "dimensions=600x900,342x482&nsfw=false&humor=false";

            if (settings.SgdbAnimated)
            {
                foreach (string url in SgdbGridUrls(await SgdbGetAsync($"{basePath}?{common}&types=animated")))
                {
                    if (GridExtension(url) is ".webp" or ".apng" or ".gif" or ".png") return (url, true);
                }
            }

            if (animatedOnly) return null;

            var still = SgdbGridUrls(await SgdbGetAsync($"{basePath}?{common}&types=static&mimes=image/png,image/jpeg"));
            return still.Count > 0 ? (still[0], false) : null;
        }

        /// <summary>Sucht bei SteamGridDB das passende Cover, wenn gewünscht auch eine animierte Fassung.</summary>
        private async Task<(string Url, bool Animated)?> FindSgdbCoverAsync(GameItem game, bool animatedOnly = false)
        {
            if (string.IsNullOrWhiteSpace(settings.SteamGridDbKey) || !settings.OnlineFeatures) return null;

            try
            {
                if (game.Source == "Steam" && game.AppId.Length > 0)
                {
                    var viaSteam = await SgdbPickFromBaseAsync($"/grids/steam/{game.AppId}", animatedOnly);
                    if (viaSteam != null) return viaSteam;
                }

                long id = await FindSgdbGameIdAsync(game.Name);
                return id > 0 ? await SgdbPickFromBaseAsync($"/grids/game/{id}", animatedOnly) : null;
            }
            catch
            {
                return null;
            }
        }

        // ───────────────────────────── Weitere Spiele-Stores: GOG, EA, Ubisoft, Battle.net ─────────────────────────────

        private static string FolderTitle(string path)
            => System.IO.Path.GetFileName(System.IO.Path.GetFullPath(path).TrimEnd('\\', '/'));

        private static void AddStoreGame(Dictionary<string, GameItem> found, string name, string source, string folder,
            string exePath, string? launchUri = null, string appId = "")
        {
            if (string.IsNullOrWhiteSpace(name) || found.ContainsKey(name)) return;

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) exePath = FindMainExe(folder);
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            found[name] = new GameItem
            {
                Name = name,
                ExecutablePath = exePath,
                InstallDir = folder,
                Source = source,
                AppId = appId,
                LaunchUri = launchUri,
                LocalIcon = ExtractIcon(exePath)
            };
        }

        /// <summary>GOG Galaxy und GOG-Offline-Installer tragen ihre Spiele in die Registry ein.</summary>
        private static void ScanGogGames(Dictionary<string, GameItem> found)
        {
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
                if (root == null) return;

                foreach (string id in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(id);
                    if (key == null) continue;

                    string name = key.GetValue("gameName") as string ?? string.Empty;
                    string folder = key.GetValue("path") as string ?? string.Empty;
                    string exe = key.GetValue("exe") as string ?? string.Empty;
                    if (name.Length == 0 || !Directory.Exists(folder)) continue;

                    AddStoreGame(found, name, "GOG", folder, exe);
                }
            }
            catch { }
        }

        /// <summary>Ubisoft Connect: Der Ordner liefert den Namen, gestartet wird über den uplay-Link.</summary>
        private static void ScanUbisoftGames(Dictionary<string, GameItem> found)
        {
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
                if (root == null) return;

                foreach (string id in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(id);
                    string? dir = key?.GetValue("InstallDir") as string;
                    if (string.IsNullOrWhiteSpace(dir)) continue;

                    string folder = System.IO.Path.GetFullPath(dir).TrimEnd('\\', '/');
                    if (!Directory.Exists(folder)) continue;

                    AddStoreGame(found, FolderTitle(folder), "Ubisoft", folder, string.Empty, $"uplay://launch/{id}/0", id);
                }
            }
            catch { }
        }

        /// <summary>EA App und Origin: Spiele liegen meist in "EA Games" oder "Origin Games".</summary>
        private static void ScanEaGames(Dictionary<string, GameItem> found)
        {
            try
            {
                var roots = new List<string>();
                foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                {
                    string programs = Environment.GetFolderPath(special);
                    if (programs.Length == 0) continue;
                    roots.Add(System.IO.Path.Combine(programs, "EA Games"));
                    roots.Add(System.IO.Path.Combine(programs, "Origin Games"));
                }

                foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string folder in Directory.GetDirectories(root))
                        AddStoreGame(found, FolderTitle(folder), "EA", folder, string.Empty);
                }

                using var registry = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\EA Games");
                if (registry == null) return;

                foreach (string title in registry.GetSubKeyNames())
                {
                    using var key = registry.OpenSubKey(title);
                    string? dir = key?.GetValue("Install Dir") as string;
                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
                    AddStoreGame(found, title, "EA", dir, string.Empty);
                }
            }
            catch { }
        }

        /// <summary>Battle.net: Blizzard-Spiele erscheinen in der Liste der installierten Programme.</summary>
        private static void ScanBattleNetGames(Dictionary<string, GameItem> found)
        {
            foreach (string path in new[]
            {
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
            })
            {
                try
                {
                    using var root = Registry.LocalMachine.OpenSubKey(path);
                    if (root == null) continue;

                    foreach (string sub in root.GetSubKeyNames())
                    {
                        using var key = root.OpenSubKey(sub);
                        string publisher = key?.GetValue("Publisher") as string ?? string.Empty;
                        string name = key?.GetValue("DisplayName") as string ?? string.Empty;
                        string folder = key?.GetValue("InstallLocation") as string ?? string.Empty;

                        if (!publisher.Contains("Blizzard", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.Length == 0 || name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase)) continue;
                        if (folder.Length == 0 || !Directory.Exists(folder)) continue;

                        AddStoreGame(found, name, "Battle.net", folder, string.Empty);
                    }
                }
                catch { }
            }
        }

        // ───────────────────────────── Fehlende Spiele erkennen ─────────────────────────────

        /// <summary>Manuell hinzugefügte Spiele, deren Datei nicht mehr existiert.</summary>
        private void CheckMissingManualGames()
        {
            var missing = settings.ManualGames
                .Where(m => !string.IsNullOrWhiteSpace(m.ExecutablePath) && !File.Exists(m.ExecutablePath))
                .ToList();

            if (missing.Count == 0)
            {
                RemoveBanner("missing");
                return;
            }

            ShowBanner("missing", "⚠", $"{missing.Count} manuell hinzugefügte Spiele wurden nicht mehr gefunden.", "Aufräumen", CleanUpMissingGames);
        }

        private void CleanUpMissingGames()
        {
            var missing = settings.ManualGames
                .Where(m => !string.IsNullOrWhiteSpace(m.ExecutablePath) && !File.Exists(m.ExecutablePath))
                .Select(m => m.Name)
                .ToList();
            if (missing.Count == 0) return;

            var chosen = PickFromList("Fehlende Spiele",
                "Diese Spiele wurden nicht mehr gefunden. Markierte Einträge werden aus der Liste entfernt.",
                missing, missing.ToHashSet(StringComparer.OrdinalIgnoreCase));
            if (chosen == null || chosen.Count == 0) return;

            settings.ManualGames.RemoveAll(m => chosen.Contains(m.Name, StringComparer.OrdinalIgnoreCase));
            SaveSettings();
            RemoveBanner("missing");
            BtnRescanGames_Click(this, new RoutedEventArgs());
        }

        // ───────────────────────────── Wunschliste mit Preisalarm ─────────────────────────────

        private sealed class SteamPriceInfo
        {
            public string Name { get; set; } = string.Empty;
            public bool Free { get; set; }
            public double FinalValue { get; set; }
            public double InitialValue { get; set; }
            public int Discount { get; set; }
            public string Formatted { get; set; } = string.Empty;
        }

        private static string PriceRegion()
        {
            try
            {
                return RegionInfo.CurrentRegion.TwoLetterISORegionName.ToLowerInvariant();
            }
            catch
            {
                return "de";
            }
        }

        private static async Task<SteamPriceInfo?> FetchSteamPriceAsync(string appId, string region)
        {
            string json = await Http.GetStringAsync(
                $"https://store.steampowered.com/api/appdetails?appids={appId}&cc={region}&filters=basic,price_overview");
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(appId, out var app) || app.ValueKind != JsonValueKind.Object) return null;
            if (!app.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
            if (!app.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;

            var info = new SteamPriceInfo { Name = GetJsonString(data, "name") };

            if (data.TryGetProperty("is_free", out var free) && free.ValueKind == JsonValueKind.True)
            {
                info.Free = true;
                return info;
            }

            if (data.TryGetProperty("price_overview", out var price) && price.ValueKind == JsonValueKind.Object)
            {
                info.FinalValue = JsonLong(price, "final") / 100.0;
                info.InitialValue = JsonLong(price, "initial") / 100.0;
                info.Discount = (int)JsonLong(price, "discount_percent");
                info.Formatted = GetJsonString(price, "final_formatted");
            }

            return info;
        }

        private static bool TryParsePrice(string text, out double value)
            => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0 && value < 100000;

        private static string FormatMoney(double value) => value.ToString("C", CultureInfo.CurrentCulture);

        private async Task UpdateWishPriceAsync(WishItem item, string region)
        {
            var info = await FetchSteamPriceAsync(item.AppId, region);
            if (info == null) return;

            item.Free = info.Free;
            item.FinalPrice = info.FinalValue;
            item.InitialPrice = info.InitialValue;
            item.Discount = info.Discount;
            item.PriceText = info.Free ? "Kostenlos" : info.Formatted;
            item.Checked = DateTime.Now;
        }

        private static bool WishTargetReached(WishItem item)
        {
            if (item.Free || item.FinalPrice <= 0) return false;
            return item.TargetPrice > 0 ? item.FinalPrice <= item.TargetPrice : item.Discount >= 20;
        }

        private async Task CheckWishlistAsync(bool notify)
        {
            if (!settings.OnlineFeatures || settings.Wishlist.Count == 0) return;

            string region = PriceRegion();
            var reached = new List<string>();

            foreach (var item in settings.Wishlist.ToList())
            {
                try
                {
                    await UpdateWishPriceAsync(item, region);
                }
                catch
                {
                    break;   // keine Verbindung: später noch einmal versuchen
                }

                if (WishTargetReached(item))
                {
                    if (item.NotifiedPrice <= 0 || item.FinalPrice < item.NotifiedPrice - 0.001)
                    {
                        item.NotifiedPrice = item.FinalPrice;
                        reached.Add($"{item.Name} ({item.PriceText})");
                    }
                }
                else
                {
                    item.NotifiedPrice = 0;
                }

                await Task.Delay(400);
            }

            settings.LastWishCheck = DateTime.Now;
            SaveSettings();
            if (ViewWishlist.Visibility == Visibility.Visible) RenderWishlist();

            if (notify && reached.Count > 0)
            {
                string names = string.Join(", ", reached.Take(3));
                ShowToast("💝", "Preisalarm", names, 10, () => NavigateTo("wishlist"));
                if (!IsVisible && trayIcon != null && !AlertsMuted())
                    trayIcon.ShowBalloonTip(4000, "DFP Pro Launcher", Loc.T("Preisalarm") + ": " + names, Forms.ToolTipIcon.Info);
            }
        }

        private async Task CheckWishlistIfDueAsync()
        {
            if (settings.Wishlist.Count == 0 || !settings.OnlineFeatures) return;
            if ((DateTime.Now - settings.LastWishCheck).TotalHours < 6) return;
            await CheckWishlistAsync(true);
        }

        private void NavWishlist_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewWishlist);
            RenderWishlist();

            if (settings.Wishlist.Count > 0 && (DateTime.Now - settings.LastWishCheck).TotalMinutes > 60)
                _ = CheckWishlistAsync(false);
        }

        private async void BtnWishRefresh_Click(object sender, RoutedEventArgs e)
        {
            TxtWishStatus.Text = Loc.T("Preise werden geprüft ...");
            await CheckWishlistAsync(false);
            RenderWishlist();
        }

        private async void BtnWishAdd_Click(object sender, RoutedEventArgs e)
        {
            string? name = PromptText("Wunschliste", "Name des Spiels (genau wie im Steam-Shop):", string.Empty);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();

            if (settings.Wishlist.Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                Msg("Dieses Spiel steht schon auf der Wunschliste.", "Wunschliste");
                return;
            }
            if (!settings.OnlineFeatures)
            {
                Msg("Online-Funktionen sind in den Einstellungen deaktiviert.", "Wunschliste", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string appId = await FindSteamAppIdAsync(name);
            if (appId.Length == 0)
            {
                Msg("Das Spiel wurde im Steam-Shop nicht gefunden. Prüfe die Schreibweise, der Name muss genau stimmen.",
                    "Wunschliste", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? target = PromptText("Zielpreis",
                $"Zielpreis für „{name}“ (zum Beispiel 14,99). Leer lassen = bei jedem Rabatt ab 20 % benachrichtigen:", string.Empty);
            if (target == null) return;

            double targetValue = 0;
            if (target.Trim().Length > 0 && !TryParsePrice(target, out targetValue))
            {
                Msg("Bitte gib eine Zahl ein (zum Beispiel 12,5).", "Zielpreis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var item = new WishItem { Name = name, AppId = appId, TargetPrice = targetValue };
            settings.Wishlist.Add(item);

            try { await UpdateWishPriceAsync(item, PriceRegion()); } catch { }

            SaveSettings();
            RenderWishlist();
        }

        private void EditWishTarget(WishItem item)
        {
            string current = item.TargetPrice > 0 ? item.TargetPrice.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;
            string? input = PromptText("Zielpreis",
                $"Zielpreis für „{item.Name}“ (zum Beispiel 14,99). Leer lassen = bei jedem Rabatt ab 20 % benachrichtigen:", current);
            if (input == null) return;

            double value = 0;
            if (input.Trim().Length > 0 && !TryParsePrice(input, out value))
            {
                Msg("Bitte gib eine Zahl ein (zum Beispiel 12,5).", "Zielpreis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            item.TargetPrice = value;
            item.NotifiedPrice = 0;
            SaveSettings();
            RenderWishlist();
        }

        private void RenderWishlist()
        {
            if (WishContainer == null) return;
            WishContainer.Children.Clear();

            if (settings.Wishlist.Count == 0)
            {
                TxtWishStatus.Text = string.Empty;
                WishContainer.Children.Add(new TextBlock
                {
                    Text = Loc.T("Noch keine Spiele auf der Wunschliste. Füge ein Steam-Spiel hinzu, der Launcher meldet sich, sobald der Preis fällt."),
                    Foreground = BrushSubtle,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            var last = settings.Wishlist.Max(w => w.Checked);
            TxtWishStatus.Text = last > DateTime.MinValue ? Loc.T("Zuletzt geprüft:") + " " + last.ToString("HH:mm") : string.Empty;

            foreach (var wish in settings.Wishlist.OrderByDescending(WishTargetReached).ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = wish;
                bool reached = WishTargetReached(item);

                var layout = new Grid();
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var picture = new Border { Width = 128, Height = 60, CornerRadius = new CornerRadius(8), Background = BrushFooter, Margin = new Thickness(0, 0, 14, 0), ClipToBounds = true };
                var header = LoadWebImage($"https://cdn.akamai.steamstatic.com/steam/apps/{item.AppId}/header.jpg");
                if (header != null) picture.Background = new ImageBrush(header) { Stretch = Stretch.UniformToFill };
                layout.Children.Add(picture);

                var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
                Grid.SetColumn(texts, 1);
                texts.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                string target = item.TargetPrice > 0
                    ? Loc.T("Zielpreis:") + " " + FormatMoney(item.TargetPrice)
                    : Loc.T("Ziel: ab 20 % Rabatt");
                texts.Children.Add(new TextBlock { Text = target, Foreground = BrushSubtle, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
                if (reached)
                {
                    texts.Children.Add(new TextBlock
                    {
                        Text = "🔔 " + Loc.T("Ziel erreicht"),
                        Foreground = MakeBrush("#34D399"),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 3, 0, 0)
                    });
                }
                layout.Children.Add(texts);

                var priceBox = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(14, 0, 4, 0) };
                Grid.SetColumn(priceBox, 2);
                string priceText = string.IsNullOrEmpty(item.PriceText) ? "–" : Loc.T(item.PriceText);
                var price = new TextBlock
                {
                    Text = priceText,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Foreground = reached ? MakeBrush("#34D399") : System.Windows.Media.Brushes.White
                };
                priceBox.Children.Add(price);
                if (item.Discount > 0)
                {
                    priceBox.Children.Add(new TextBlock
                    {
                        Text = $"-{item.Discount} %",
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = BrushGold,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                    });
                }
                layout.Children.Add(priceBox);

                var menu = CreateMenu();
                AddMenuItem(menu, "🛒  Im Steam-Shop öffnen", () => OpenShell($"https://store.steampowered.com/app/{item.AppId}"));
                AddMenuItem(menu, "🎯  Zielpreis ändern ...", () => EditWishTarget(item));
                AddMenuItem(menu, "🗑  Entfernen", () =>
                {
                    settings.Wishlist.Remove(item);
                    SaveSettings();
                    RenderWishlist();
                });

                var card = new Border
                {
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 10),
                    CornerRadius = new CornerRadius(14),
                    Background = BrushCardBg,
                    BorderBrush = BrushCardBorder,
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ContextMenu = menu,
                    Width = 700,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Child = layout
                };
                card.MouseEnter += (s, e) => card.Background = BrushTileHover;
                card.MouseLeave += (s, e) => card.Background = BrushCardBg;
                card.MouseLeftButtonUp += (s, e) => OpenShell($"https://store.steampowered.com/app/{item.AppId}");
                WishContainer.Children.Add(card);
            }
        }

        // ───────────────────────────── Spielinfos, Neuigkeiten, Genre-Filter und Kosten ─────────────────────────────

        private string genreFilter = string.Empty;
        private bool gameInfoLoading;

        private static string SteamLanguageCode(string code) => code switch
        {
            "de" => "german",
            "zh" => "schinese",
            "es" => "spanish",
            "fr" => "french",
            "pt" => "brazilian",
            "ru" => "russian",
            "ja" => "japanese",
            _ => "english"
        };

        private string CurrentSteamLanguage() => SteamLanguageCode(ResolveLanguage(settings.Language));

        private static string StripHtml(string text)
            => System.Net.WebUtility.HtmlDecode(Regex.Replace(text, "<.*?>", " ")).Replace("  ", " ").Trim();

        private async Task<GameInfoEntry?> FetchGameInfoAsync(string appId)
        {
            string language = CurrentSteamLanguage();
            string json = await Http.GetStringAsync(
                $"https://store.steampowered.com/api/appdetails?appids={appId}&l={language}&filters=basic,genres,release_date");
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(appId, out var app) || app.ValueKind != JsonValueKind.Object) return null;
            if (!app.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
            if (!app.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;

            var entry = new GameInfoEntry
            {
                Name = GetJsonString(data, "name"),
                Description = StripHtml(GetJsonString(data, "short_description")),
                Lang = language,
                Updated = DateTime.Now
            };

            if (data.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            {
                foreach (var genre in genres.EnumerateArray())
                {
                    string name = GetJsonString(genre, "description");
                    if (name.Length > 0) entry.Genres.Add(name);
                }
            }

            if (TryPath(data, out var release, "release_date")) entry.Release = GetJsonString(release, "date");

            if (data.TryGetProperty("developers", out var developers) && developers.ValueKind == JsonValueKind.Array)
            {
                foreach (var developer in developers.EnumerateArray())
                {
                    if (developer.ValueKind == JsonValueKind.String)
                    {
                        entry.Developer = developer.GetString() ?? string.Empty;
                        break;
                    }
                }
            }

            return entry;
        }

        /// <summary>Lädt im Hintergrund Genre und Beschreibung für Steam-Spiele nach (höchstens 60 pro Durchgang).</summary>
        private async Task LoadGameInfoAsync()
        {
            if (gameInfoLoading || !settings.OnlineFeatures || !settings.FetchGameInfo) return;
            gameInfoLoading = true;

            try
            {
                var pending = allGames
                    .Where(g => g.Source == "Steam" && g.AppId.Length > 0 && !settings.GameInfo.ContainsKey(g.AppId))
                    .Select(g => g.AppId)
                    .Distinct()
                    .Take(60)
                    .ToList();

                int count = 0;
                foreach (string id in pending)
                {
                    try
                    {
                        var entry = await FetchGameInfoAsync(id);
                        settings.GameInfo[id] = entry ?? new GameInfoEntry { Lang = CurrentSteamLanguage(), Updated = DateTime.Now };
                    }
                    catch
                    {
                        break;   // Netzwerkfehler: später weitermachen
                    }

                    count++;
                    if (count % 10 == 0) SaveSettings();
                    await Task.Delay(1500);
                }

                if (count > 0) SaveSettings();
            }
            finally
            {
                gameInfoLoading = false;
            }
        }

        private bool GameHasGenre(GameItem game, string genre)
            => game.AppId.Length > 0
               && settings.GameInfo.TryGetValue(game.AppId, out var info)
               && info.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase);

        private void SetGenreFilter(string genre)
        {
            genreFilter = genre;
            BtnGenre.Content = genre.Length == 0 ? "🏷 Genre" : "🏷 " + genre + "  ✕";
            controllerIndex = 0;
            ApplyFilter();
        }

        private void BtnGenre_Click(object sender, RoutedEventArgs e)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var game in allGames.Where(g => !g.Hidden && g.AppId.Length > 0))
            {
                if (!settings.GameInfo.TryGetValue(game.AppId, out var info)) continue;
                foreach (string genre in info.Genres)
                {
                    counts.TryGetValue(genre, out int current);
                    counts[genre] = current + 1;
                }
            }

            var menu = CreateMenu();
            if (genreFilter.Length > 0) AddMenuItem(menu, "✖  Alle Genres", () => SetGenreFilter(string.Empty));

            if (counts.Count == 0)
            {
                AddMenuItem(menu, "Noch keine Spielinfos geladen", () => { });
            }
            else
            {
                foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(25))
                {
                    string genre = pair.Key;
                    AddMenuItem(menu, $"{genre}  ({pair.Value})", () => SetGenreFilter(genre));
                }
            }

            menu.PlacementTarget = (UIElement)sender;
            menu.IsOpen = true;
        }

        private void EditPrice(GameItem game)
        {
            string current = game.Price > 0 ? game.Price.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;
            string? input = PromptText("Kaufpreis", $"Was hat „{game.Name}“ gekostet? (zum Beispiel 29,99, leer = nicht angeben):", current);
            if (input == null) return;

            double value = 0;
            if (input.Trim().Length > 0 && !TryParsePrice(input, out value))
            {
                Msg("Bitte gib eine Zahl ein (zum Beispiel 12,5).", "Kaufpreis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            game.Price = value;
            SaveGameState(game);
        }

        /// <summary>Ergänzt den Details-Dialog um Kosten, Beschreibung, Genre und aktuelle Neuigkeiten.</summary>
        private void AddGameInfoSection(StackPanel panel, GameItem game)
        {
            if (game.Price > 0)
            {
                string text = Loc.T("Kaufpreis:") + " " + FormatMoney(game.Price);
                if (game.PlaySeconds >= 600)
                    text += "   ·   " + Loc.T("Kosten pro Stunde:") + " " + FormatMoney(game.Price / (game.PlaySeconds / 3600.0));
                panel.Children.Add(new TextBlock { Text = text, Foreground = BrushGold, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
            }

            if (game.Source != "Steam" || game.AppId.Length == 0 || !settings.OnlineFeatures || !settings.FetchGameInfo) return;

            var meta = new TextBlock { Foreground = BrushSubtle, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
            var description = new TextBlock { Foreground = MakeBrush("#D1D5DB"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed };
            var news = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            panel.Children.Add(meta);
            panel.Children.Add(description);
            panel.Children.Add(news);

            _ = FillGameInfoAsync(game, meta, description, news);
        }

        private async Task FillGameInfoAsync(GameItem game, TextBlock meta, TextBlock description, StackPanel news)
        {
            try
            {
                string language = CurrentSteamLanguage();
                if (!settings.GameInfo.TryGetValue(game.AppId, out var info) || info.Name.Length == 0 || info.Lang != language)
                {
                    var fresh = await FetchGameInfoAsync(game.AppId);
                    if (fresh != null)
                    {
                        settings.GameInfo[game.AppId] = fresh;
                        info = fresh;
                    }
                }

                if (info != null && info.Name.Length > 0)
                {
                    var parts = new List<string>();
                    if (info.Genres.Count > 0) parts.Add(string.Join(", ", info.Genres));
                    if (info.Release.Length > 0) parts.Add(info.Release);
                    if (info.Developer.Length > 0) parts.Add(info.Developer);
                    if (parts.Count > 0)
                    {
                        meta.Text = string.Join("   ·   ", parts);
                        meta.Visibility = Visibility.Visible;
                    }
                    if (info.Description.Length > 0)
                    {
                        description.Text = info.Description;
                        description.Visibility = Visibility.Visible;
                    }
                }

                string json = await Http.GetStringAsync(
                    $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={game.AppId}&count=3&maxlength=0&format=json");
                using var doc = JsonDocument.Parse(json);
                if (!TryPath(doc.RootElement, out var items, "appnews", "newsitems") || items.ValueKind != JsonValueKind.Array) return;

                bool header = false;
                foreach (var item in items.EnumerateArray())
                {
                    string title = GetJsonString(item, "title");
                    string url = GetJsonString(item, "url");
                    long unix = JsonLong(item, "date");
                    if (title.Length == 0) continue;

                    if (!header)
                    {
                        news.Children.Add(new TextBlock { Text = Loc.T("Neuigkeiten"), Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 6) });
                        header = true;
                    }

                    string date = unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime.ToString("dd.MM.yyyy") : string.Empty;
                    var link = new TextBlock
                    {
                        Text = (date.Length > 0 ? date + "   " : string.Empty) + title,
                        Foreground = System.Windows.Media.Brushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    link.MouseEnter += (s, e) => link.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                    link.MouseLeave += (s, e) => link.Foreground = System.Windows.Media.Brushes.White;
                    if (url.Length > 0) link.MouseLeftButtonUp += (s, e) => OpenShell(url);
                    news.Children.Add(link);
                }
            }
            catch { }
        }

        // ───────────────────────────── Aktivitätskalender ─────────────────────────────

        private void BuildHeatmap()
        {
            if (HeatmapGrid == null) return;

            HeatmapGrid.Children.Clear();
            HeatmapGrid.RowDefinitions.Clear();
            HeatmapGrid.ColumnDefinitions.Clear();

            const int weeks = 26;
            for (int r = 0; r < 7; r++) HeatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int c = 0; c < weeks; c++) HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Wochentage: Montag bis Sonntag
            int mondayOffset = ((int)DateTime.Today.DayOfWeek + 6) % 7;
            DateTime start = DateTime.Today.AddDays(-mondayOffset - (weeks - 1) * 7);

            double max = 0;
            for (int i = 0; i < weeks * 7; i++)
            {
                var day = start.AddDays(i);
                if (day > DateTime.Today) break;
                settings.PlayLog.TryGetValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out double seconds);
                max = Math.Max(max, seconds);
            }

            var dayFormat = GermanCulture.DateTimeFormat;
            string[] labels =
            {
                dayFormat.GetAbbreviatedDayName(DayOfWeek.Monday), string.Empty,
                dayFormat.GetAbbreviatedDayName(DayOfWeek.Wednesday), string.Empty,
                dayFormat.GetAbbreviatedDayName(DayOfWeek.Friday), string.Empty,
                dayFormat.GetAbbreviatedDayName(DayOfWeek.Sunday)
            };
            for (int r = 0; r < 7; r++)
            {
                var label = new TextBlock { Text = labels[r], Foreground = BrushSubtle, FontSize = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                Grid.SetRow(label, r);
                HeatmapGrid.Children.Add(label);
            }

            var accent = GetAccentColor();
            int activeDays = 0;
            for (int c = 0; c < weeks; c++)
            {
                for (int r = 0; r < 7; r++)
                {
                    var day = start.AddDays(c * 7 + r);
                    if (day > DateTime.Today) continue;

                    settings.PlayLog.TryGetValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out double seconds);
                    if (seconds >= 60) activeDays++;

                    double level = seconds < 60 || max <= 0 ? 0 : seconds / max;
                    double opacity = level == 0 ? 0 : level > 0.75 ? 1.0 : level > 0.5 ? 0.75 : level > 0.25 ? 0.5 : 0.3;

                    var cell = new Border
                    {
                        Width = 15,
                        Height = 15,
                        Margin = new Thickness(2),
                        CornerRadius = new CornerRadius(4),
                        Background = opacity == 0 ? MakeBrush("#1F2937") : new SolidColorBrush(accent) { Opacity = opacity },
                        ToolTip = day.ToString("dd.MM.yyyy") + (seconds >= 60 ? "  ·  " + FormatPlaytime((long)seconds) : string.Empty)
                    };
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c + 1);
                    HeatmapGrid.Children.Add(cell);
                }
            }

            TxtHeatmapInfo.Text = activeDays == 0
                ? Loc.T("Noch keine Aktivität. Jeder Tag mit Spielzeit färbt hier ein Feld.")
                : $"{activeDays} " + Loc.T("Tage mit Spielzeit in den letzten 26 Wochen");
        }

        // ───────────────────────────── Spielprofile (pro Spiel) ─────────────────────────────

        private bool ProfileWanted(GameItem game)
            => game.ProfileMode == "on" || (game.ProfileMode != "off" && settings.GamingProfile);

        private bool CloseAppsWanted(GameItem game)
            => game.CloseMode == "on" || (game.CloseMode != "off" && settings.CloseAppsEnabled);

        private void ShowGameProfile(GameItem game)
        {
            var dialog = CreateDialog("Spielprofil: " + game.Name, 540, out var panel);
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Lege fest, was für dieses Spiel gilt. „Standard“ übernimmt die allgemeine Einstellung."),
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var chipStyle = TryFindResource("ChipStyle") as Style;

            string ReadGroup(List<(System.Windows.Controls.RadioButton Button, string Value)> options)
                => options.FirstOrDefault(o => o.Button.IsChecked == true).Value ?? "global";

            List<(System.Windows.Controls.RadioButton Button, string Value)> AddGroup(string title, string description, string current, string group)
            {
                panel.Children.Add(new TextBlock { Text = Loc.T(title), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });
                panel.Children.Add(new TextBlock { Text = Loc.T(description), Foreground = BrushSubtle, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 8) });

                var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
                var options = new List<(System.Windows.Controls.RadioButton, string)>();
                foreach (var (value, label) in new[] { ("global", "Standard"), ("on", "Immer an"), ("off", "Immer aus") })
                {
                    var radio = new System.Windows.Controls.RadioButton { Content = Loc.T(label), GroupName = group, IsChecked = current == value || (value == "global" && current != "on" && current != "off") };
                    if (chipStyle != null) radio.Style = chipStyle;
                    options.Add((radio, value));
                    wrap.Children.Add(radio);
                }
                panel.Children.Add(wrap);
                return options;
            }

            var profile = AddGroup("Höchstleistung während des Spiels",
                "Schaltet den Windows-Energiesparplan auf „Höchstleistung“, solange dieses Spiel läuft.", game.ProfileMode, "ProfileMode");
            var closeApps = AddGroup("Programme beim Start schließen",
                "Schließt die in den Einstellungen gewählten Programme, bevor dieses Spiel startet.", game.CloseMode, "CloseMode");

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var cancel = new System.Windows.Controls.Button { Content = Loc.T("Abbrechen"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var save = new System.Windows.Controls.Button { Content = Loc.T("Speichern"), Padding = new Thickness(26, 8, 26, 8), IsDefault = true };
            save.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            save.Click += (s, e) =>
            {
                game.ProfileMode = ReadGroup(profile);
                game.CloseMode = ReadGroup(closeApps);
                SaveGameState(game);
                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            dialog.ShowDialog();
        }

        // ───────────────────────────── Spielbericht, Tageslimit, Wiederöffnen ─────────────────────────────

        private void SampleSessions()
        {
            if (!settings.SessionReport || activeSessions.Count == 0) return;

            MetricsSample m;
            try { m = SampleMetrics(); }
            catch { return; }

            foreach (var game in activeSessions.Keys)
            {
                if (!sessionStats.TryGetValue(game, out var stats))
                {
                    stats = new SessionStats { Started = sessionStart.TryGetValue(game, out var started) ? started : DateTime.Now };
                    sessionStats[game] = stats;
                }

                stats.Samples++;
                stats.CpuSum += m.Cpu;
                stats.CpuMax = Math.Max(stats.CpuMax, m.Cpu);
                stats.RamSum += m.Ram;
                stats.RamMax = Math.Max(stats.RamMax, m.Ram);
                if (m.HasGpu)
                {
                    stats.HasGpu = true;
                    stats.GpuSum += m.Gpu;
                    stats.GpuMax = Math.Max(stats.GpuMax, m.Gpu);
                }
            }
        }

        private void ShowSessionReport(GameItem game)
        {
            if (!sessionStats.TryGetValue(game, out var stats)) return;
            sessionStats.Remove(game);

            if (!settings.SessionReport || stats.Samples < 30) return;

            var duration = DateTime.Now - stats.Started;
            string time = $"{(int)duration.TotalHours}:{duration.Minutes:D2} {Loc.T("Std.")}";
            string max = Loc.T("max.");

            var lines = new List<string>
            {
                $"{Loc.T("Prozessor")}: Ø {stats.CpuSum / stats.Samples:F0} % ({max} {stats.CpuMax:F0} %)"
            };
            if (stats.HasGpu)
                lines.Add($"{Loc.T("Grafikkarte")}: Ø {stats.GpuSum / stats.Samples:F0} % ({max} {stats.GpuMax:F0} %)");
            lines.Add($"RAM: Ø {stats.RamSum / stats.Samples:F0} % ({max} {stats.RamMax:F0} %)");

            ShowToast("📊", Loc.T("Spielbericht") + ": " + game.Name, time + "\n" + string.Join("\n", lines), 14);
        }

        private void CheckDailyLimit(DateTime now)
        {
            if (settings.DailyLimitHours <= 0 || activeSessions.Count == 0) return;

            string key = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (limitNotifiedDay == key) return;

            settings.PlayLog.TryGetValue(key, out double seconds);
            if (seconds < settings.DailyLimitHours * 3600.0) return;

            limitNotifiedDay = key;
            overlayNotice = Loc.T("⏳ Tageslimit erreicht");
            overlayNoticeUntil = now.AddSeconds(30);
            ShowToast("⏳", "Tageslimit erreicht", Loc.T("Du hast heute") + " " + FormatPlaytime((long)seconds) + " " + Loc.T("gespielt."), 12, null, true);
        }

        private void ReopenLauncherAfterGame()
        {
            if (!IsVisible || WindowState == WindowState.Minimized) ShowFromTray();
        }

        // ───────────────────────────── Alphabet-Leiste ─────────────────────────────

        private static string LetterOf(string name)
        {
            char first = name.TrimStart().FirstOrDefault();
            char upper = char.ToUpperInvariant(first);
            return upper >= 'A' && upper <= 'Z' ? upper.ToString() : "#";
        }

        private void BuildAlphaBar()
        {
            if (AlphaBar == null) return;

            AlphaBar.Children.Clear();
            var letters = new List<string> { "#" };
            for (char c = 'A'; c <= 'Z'; c++) letters.Add(c.ToString());

            foreach (string letter in letters)
            {
                string target = letter;
                var block = new TextBlock
                {
                    Text = letter,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = BrushSubtle,
                    TextAlignment = TextAlignment.Center,
                    Padding = new Thickness(0, 2, 0, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = letter
                };
                block.MouseEnter += (s, e) => block.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                block.MouseLeave += (s, e) => UpdateAlphaBar();
                block.MouseLeftButtonUp += (s, e) => JumpToLetter(target);
                AlphaBar.Children.Add(block);
            }
        }

        private void UpdateAlphaBar()
        {
            if (AlphaHost == null || AlphaBar == null) return;

            bool show = settings.ShowAlphaBar && settings.SortMode == 0 && !controllerMode && visibleGames.Count > 15;
            AlphaHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;

            var present = new HashSet<string>(visibleGames.Select(g => LetterOf(g.Name)));
            foreach (var block in AlphaBar.Children.OfType<TextBlock>())
            {
                bool has = present.Contains(block.Tag as string ?? string.Empty);
                block.Foreground = has ? System.Windows.Media.Brushes.White : MakeBrush("#3A4156");
            }
        }

        private void JumpToLetter(string letter)
        {
            int index = visibleGames.FindIndex(g => LetterOf(g.Name) == letter);
            if (index < 0) return;

            System.Windows.Controls.Panel container = settings.CardLayout == "list" ? GamesListContainer : GamesContainer;
            if (index >= container.Children.Count || container.Children[index] is not FrameworkElement target) return;

            try
            {
                var point = target.TransformToAncestor(GamesScroll).Transform(new System.Windows.Point(0, 0));
                GamesScroll.ScrollToVerticalOffset(Math.Max(0, GamesScroll.VerticalOffset + point.Y - 8));
            }
            catch { }
        }

        // ───────────────────────────── Mehrere Spiele bearbeiten ─────────────────────────────

        private void BtnBulk_Click(object sender, RoutedEventArgs e)
        {
            // Mit Strg+Klick markierte Spiele direkt bearbeiten
            if (selectedGames.Count > 0)
            {
                var selectionMenu = BuildBulkMenu(selectedGames.ToList());
                selectionMenu.PlacementTarget = (UIElement)sender;
                selectionMenu.IsOpen = true;
                return;
            }

            var names = visibleGames.Select(g => g.Name).ToList();
            if (names.Count == 0) return;

            var chosen = PickFromList("Mehrere Spiele bearbeiten",
                "Wähle die Spiele aus, auf die die Aktion angewendet wird. Es werden nur die aktuell angezeigten Spiele aufgelistet.", names);
            if (chosen == null || chosen.Count == 0) return;

            var games = visibleGames.Where(g => chosen.Contains(g.Name)).ToList();
            var menu = CreateMenu();

            void Bulk(string text, Action<GameItem> action) => AddMenuItem(menu, text, () => ApplyBulk(games, action));

            Bulk("🙈  Verstecken", g => g.Hidden = true);
            Bulk("👁  Wieder anzeigen", g => g.Hidden = false);
            Bulk("★  Zu Favoriten hinzufügen", g => g.IsFavorite = true);
            Bulk("☆  Aus Favoriten entfernen", g => g.IsFavorite = false);
            foreach (var option in StatusOptions)
            {
                string key = option.Key;
                Bulk(option.Title, g => g.Status = key);
            }
            foreach (string collection in settings.CollectionNames)
            {
                string name = collection;
                Bulk(Loc.T("📁  Zur Sammlung") + " „" + name + "“", g =>
                {
                    if (!g.Collections.Contains(name)) g.Collections.Add(name);
                });
            }

            menu.PlacementTarget = (UIElement)sender;
            menu.IsOpen = true;
        }

        private void ApplyBulk(List<GameItem> games, Action<GameItem> action)
        {
            foreach (var game in games)
            {
                action(game);
                CopyStateToSettings(game);
            }

            SaveSettings();
            ApplyFilter();
            RefreshDashboard();
            RefreshBoard();
            ShowToast("✔", "Fertig", $"{games.Count} " + Loc.T("Spiele geändert"), 4);
        }

        // ───────────────────────────── Mod-Ordner ─────────────────────────────

        private static string? FindModFolder(string name, string installDir, string custom)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom)) return custom;

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string lower = name.ToLowerInvariant();

                string? known = null;
                if (lower.Contains("minecraft"))
                    known = System.IO.Path.Combine(appData, ".minecraft", "mods");
                else if (lower.Contains("cities: skylines") && !lower.Contains("ii"))
                    known = System.IO.Path.Combine(localData, "Colossal Order", "Cities_Skylines", "Addons", "Mods");
                else if (lower.Contains("the sims 4"))
                    known = System.IO.Path.Combine(documents, "Electronic Arts", "The Sims 4", "Mods");
                else if (lower.Contains("terraria"))
                    known = System.IO.Path.Combine(documents, "My Games", "Terraria", "tModLoader", "Mods");
                else if (lower.Contains("euro truck simulator 2"))
                    known = System.IO.Path.Combine(documents, "Euro Truck Simulator 2", "mod");
                else if (lower.Contains("american truck simulator"))
                    known = System.IO.Path.Combine(documents, "American Truck Simulator", "mod");

                if (known != null && Directory.Exists(known)) return known;

                if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return null;

                if (Regex.IsMatch(lower, "skyrim|fallout|oblivion|starfield|morrowind"))
                {
                    string data = System.IO.Path.Combine(installDir, "Data");
                    if (Directory.Exists(data)) return data;
                }
                if (lower.Contains("kerbal space program"))
                {
                    string gameData = System.IO.Path.Combine(installDir, "GameData");
                    if (Directory.Exists(gameData)) return gameData;
                }
                if (lower.Contains("bannerlord") || lower.Contains("mount & blade"))
                {
                    string modules = System.IO.Path.Combine(installDir, "Modules");
                    if (Directory.Exists(modules)) return modules;
                }

                string bepInEx = System.IO.Path.Combine(installDir, "BepInEx", "plugins");
                if (Directory.Exists(bepInEx)) return bepInEx;

                var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mods", "mod", "addons" };

                foreach (string first in Directory.EnumerateDirectories(installDir).Take(80))
                {
                    if (wanted.Contains(System.IO.Path.GetFileName(first))) return first;
                }

                foreach (string first in Directory.EnumerateDirectories(installDir).Take(40))
                {
                    foreach (string second in Directory.EnumerateDirectories(first).Take(40))
                    {
                        if (wanted.Contains(System.IO.Path.GetFileName(second))) return second;
                    }
                }
            }
            catch { }

            return null;
        }

        private async Task ScanModFoldersAsync()
        {
            if (modScanRunning || allGames.Count == 0) return;
            modScanRunning = true;

            try
            {
                var snapshot = allGames.Select(g => (g.Name, g.InstallDir, g.ModPath)).ToList();
                var found = await Task.Run(() =>
                {
                    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (name, dir, custom) in snapshot)
                    {
                        string? path = FindModFolder(name, dir, custom);
                        if (path != null) result[name] = path;
                    }
                    return result;
                });

                bool changed = found.Count != modFolders.Count
                    || found.Any(p => !modFolders.TryGetValue(p.Key, out var existing) || existing != p.Value);
                if (!changed) return;

                modFolders.Clear();
                foreach (var pair in found) modFolders[pair.Key] = pair.Value;
                ApplyFilter();
            }
            catch (Exception ex)
            {
                LogError("Mod-Ordner", ex);
            }
            finally
            {
                modScanRunning = false;
            }
        }

        /// <summary>Kleiner Knopf unten rechts auf dem Cover, der beim Darüberfahren erscheint.</summary>
        private void AddModButton(System.Windows.Controls.Panel area, UIElement hoverTarget, GameItem game, bool underStar = false)
        {
            if (!settings.ShowModButton || !modFolders.TryGetValue(game.Name, out var path)) return;

            var button = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = MakeBrush("#D90F111A"),
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = underStar ? System.Windows.VerticalAlignment.Top : System.Windows.VerticalAlignment.Bottom,
                Margin = underStar ? new Thickness(8, 46, 8, 0) : new Thickness(8),
                Opacity = 0,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = Loc.T("Mod-Ordner öffnen"),
                Child = new TextBlock
                {
                    Text = "🧩",
                    FontSize = 16,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            };

            void Fade(double to)
            {
                if (settings.PerformanceMode || !settings.HoverAnimations) button.Opacity = to;
                else button.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(130)));
            }

            hoverTarget.MouseEnter += (s, e) => Fade(1);
            hoverTarget.MouseLeave += (s, e) => Fade(0);
            button.MouseEnter += (s, e) => button.BorderBrush = System.Windows.Media.Brushes.White;
            button.MouseLeave += (s, e) => button.BorderBrush = BrushCardBorder;
            button.MouseLeftButtonDown += (s, e) => e.Handled = true;   // startet nicht das Spiel
            button.MouseLeftButtonUp += (s, e) =>
            {
                OpenShell(path);
                e.Handled = true;
            };

            area.Children.Add(button);
        }

        private void PickModFolder(GameItem game)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = Loc.T("Mod-Ordner dieses Spiels auswählen"),
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;

            game.ModPath = dialog.SelectedPath;
            SaveGameState(game);
            modFolders[game.Name] = game.ModPath;
            ApplyFilter();
        }

        private void ResetModFolder(GameItem game)
        {
            game.ModPath = string.Empty;
            SaveGameState(game);

            string? detected = FindModFolder(game.Name, game.InstallDir, string.Empty);
            if (detected != null) modFolders[game.Name] = detected;
            else modFolders.Remove(game.Name);
            ApplyFilter();
        }

        // ───────────────────────────── Galerie: löschen und Pfad öffnen ─────────────────────────────

        private FrameworkElement CreateShotActions(Border tile, string filePath)
        {
            Border MakeButton(string glyph, string tip, Action action)
            {
                var button = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = MakeBrush("#CC0B0E16"),
                    BorderBrush = BrushCardBorder,
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = Loc.T(tip),
                    Child = new TextBlock
                    {
                        Text = glyph,
                        FontSize = 15,
                        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    }
                };
                button.MouseEnter += (s, e) => button.BorderBrush = System.Windows.Media.Brushes.White;
                button.MouseLeave += (s, e) => button.BorderBrush = BrushCardBorder;
                button.MouseLeftButtonDown += (s, e) => e.Handled = true;
                button.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    action();
                };
                return button;
            }

            var bar = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 2, 0),
                Opacity = 0
            };
            bar.Children.Add(MakeButton("📂", "Dateipfad öffnen", () => OpenShell("explorer.exe", $"/select,\"{filePath}\"")));
            bar.Children.Add(MakeButton("🗑", "Screenshot löschen", () => DeleteShot(tile, filePath)));

            tile.MouseEnter += (s, e) => bar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            tile.MouseLeave += (s, e) => bar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
            return bar;
        }

        private void DeleteShot(Border tile, string path)
        {
            var answer = Msg("Diesen Screenshot in den Papierkorb verschieben?", "Screenshot löschen", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                GalleryContainer.Children.Remove(tile);
                ShowToast("🗑", "Screenshot gelöscht", System.IO.Path.GetFileName(path), 4);
            }
            catch (Exception ex)
            {
                LogError("Galerie", ex);
                Msg($"Der Screenshot konnte nicht gelöscht werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ───────────────────────────── Spiele-Ansicht „Cover-Flow“ ─────────────────────────────

        private List<PadItem> flowItems = new();

        private void InitExtras14()
        {
            LibraryFlow.ItemFactory = index => CreateFlowCard(flowItems[index], LibraryFlow.ItemWidth, LibraryFlow.ItemHeight);
            LibraryFlow.FocusChanged = FlowFocus;
            LibraryFlow.SelectionChanged += (s, e) =>
            {
                UpdateFlowInfo();
                PrefetchFlow(LibraryFlow, flowItems);
            };
            LibraryFlow.ItemActivated += (s, index) => LaunchFlowSelection();
            FlowHost.SizeChanged += (s, e) => ResizeFlow();

            ChkQuickRealIcons.IsChecked = settings.QuickRealIcons;
            ChipViewCards.IsChecked = settings.LibraryView != "flow";
            ChipViewFlow.IsChecked = settings.LibraryView == "flow";
            ApplyLibraryView();
        }

        private void ChkQuickRealIcons_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            settings.QuickRealIcons = ChkQuickRealIcons.IsChecked == true;
            SaveSettings();
            BuildLinkTiles();
            FillQuickWidget();
        }

        private void LibraryView_Checked(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings || sender is not System.Windows.Controls.RadioButton { Tag: string tag }) return;

            settings.LibraryView = tag == "flow" ? "flow" : "cards";
            SaveSettings();
            ApplyLibraryView();
            if (ViewGames.Visibility == Visibility.Visible) ApplyFilter();
        }

        private void ApplyLibraryView()
        {
            if (FlowHost == null) return;

            bool flow = settings.LibraryView == "flow";
            GamesScroll.Visibility = flow ? Visibility.Collapsed : Visibility.Visible;
            FlowHost.Visibility = flow ? Visibility.Visible : Visibility.Collapsed;
            if (flow) AlphaHost.Visibility = Visibility.Collapsed;
        }

        private void ResizeFlow()
        {
            if (FlowHost.ActualHeight < 200) return;

            double height = Math.Clamp(FlowHost.ActualHeight - 190, 220, 560);
            double width = Math.Round(height * 2.0 / 3.0);
            if (Math.Abs(LibraryFlow.ItemHeight - height) < 2) return;

            LibraryFlow.ItemHeight = height;
            LibraryFlow.ItemWidth = width;
            LibraryFlow.SetCount(flowItems.Count, Math.Max(0, LibraryFlow.SelectedIndex));
        }

        private void RenderFlow()
        {
            if (LibraryFlow == null) return;

            string? keep = LibraryFlow.SelectedIndex >= 0 && LibraryFlow.SelectedIndex < flowItems.Count ? flowItems[LibraryFlow.SelectedIndex].Name : null;
            flowItems = visibleGames.Select(PadItemFromGame).ToList();

            int selected = keep == null ? 0 : Math.Max(0, flowItems.FindIndex(i => i.Name == keep));
            ResizeFlow();
            LibraryFlow.SetCount(flowItems.Count, selected);
            PrefetchFlow(LibraryFlow, flowItems);
            TxtFlowEmpty.Visibility = flowItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateFlowInfo()
        {
            if (TxtFlowTitle == null) return;

            int index = LibraryFlow.SelectedIndex;
            if (index < 0 || index >= flowItems.Count)
            {
                TxtFlowTitle.Text = string.Empty;
                TxtFlowMeta.Text = string.Empty;
                FlowActions.Visibility = Visibility.Collapsed;
                return;
            }

            var item = flowItems[index];
            TxtFlowTitle.Text = item.Name;
            TxtFlowMeta.Text = item.Meta;
            FlowActions.Visibility = Visibility.Visible;
            BtnFlowFav.Content = Loc.T(item.Game?.IsFavorite == true ? "★  Favorit" : "☆  Favorit");
        }

        private GameItem? FlowSelectedGame()
        {
            int index = LibraryFlow.SelectedIndex;
            return index >= 0 && index < flowItems.Count ? flowItems[index].Game : null;
        }

        private void LaunchFlowSelection()
        {
            var game = FlowSelectedGame();
            if (game != null) LaunchGame(game);
        }

        private void BtnFlowPlay_Click(object sender, RoutedEventArgs e) => LaunchFlowSelection();

        private void BtnFlowDetails_Click(object sender, RoutedEventArgs e)
        {
            var game = FlowSelectedGame();
            if (game != null) ShowGameDetails(game);
        }

        private void BtnFlowFav_Click(object sender, RoutedEventArgs e)
        {
            var game = FlowSelectedGame();
            if (game != null) ToggleFavorite(game);
        }

        private bool FlowKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (controllerMode || settings.LibraryView != "flow" || ViewGames.Visibility != Visibility.Visible) return false;
            if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return false;

            switch (e.Key)
            {
                case System.Windows.Input.Key.Left: LibraryFlow.Move(-1); return true;
                case System.Windows.Input.Key.Right: LibraryFlow.Move(1); return true;
                case System.Windows.Input.Key.PageUp: LibraryFlow.Move(-8); return true;
                case System.Windows.Input.Key.PageDown: LibraryFlow.Move(8); return true;
                case System.Windows.Input.Key.Home: LibraryFlow.Select(0); return true;
                case System.Windows.Input.Key.End: LibraryFlow.Select(flowItems.Count - 1); return true;
                case System.Windows.Input.Key.Enter: LaunchFlowSelection(); return true;
                default: return false;
            }
        }

        // ───────────────────────────── Cover-Bilder ohne Ruckeln laden ─────────────────────────────

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapSource> flowImages = new();
        private System.Threading.CancellationTokenSource? flowPrefetch;

        private static BitmapSource? DecodeFlowImage(string path)
        {
            try
            {
                if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return DecodeStillWithImageSharp(path, 380);

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 380;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Liefert Bild und Art (Logo oder Cover). Ist das Bild noch nicht geladen, wird es im Hintergrund geholt und danach gemeldet.</summary>
        private void BeginFlowPicture(PadItem item, Action<ImageSource, bool> apply)
        {
            string? path = null;
            bool logo = true;
            ImageSource? direct = null;

            if (item.Game != null)
            {
                var game = item.Game;
                if (HasCover(game))
                {
                    path = EffectiveCover(game);
                    logo = path == game.FallbackCoverPath;
                }
                else
                {
                    direct = game.LocalIcon;
                }
            }
            else
            {
                direct = item.App?.Icon;
            }

            if (direct != null)
            {
                apply(direct, true);
                return;
            }
            if (path == null) return;

            if (flowImages.TryGetValue(path, out var cached))
            {
                apply(cached, logo);
                return;
            }

            string file = path;
            bool isLogo = logo;
            Task.Run(() =>
            {
                var image = flowImages.TryGetValue(file, out var again) ? again : DecodeFlowImage(file);
                if (image == null) return;
                flowImages[file] = image;
                Dispatcher.BeginInvoke(new Action(() => apply(image, isLogo)), DispatcherPriority.Background);
            });
        }

        private void PrefetchFlow(CoverFlowControl flow, List<PadItem> list)
        {
            if (flow.Count == 0 || list.Count == 0) return;

            int center = Math.Max(0, flow.SelectedIndex);
            flowPrefetch?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            flowPrefetch = cts;

            var paths = new List<string>();
            for (int distance = 0; distance <= 10; distance++)
            {
                foreach (int i in distance == 0 ? new[] { center } : new[] { center + distance, center - distance })
                {
                    if (i >= 0 && i < list.Count && list[i].Game is { } game && HasCover(game)) paths.Add(EffectiveCover(game));
                }
            }

            Task.Run(() =>
            {
                foreach (string path in paths)
                {
                    if (cts.IsCancellationRequested) return;
                    if (flowImages.ContainsKey(path)) continue;

                    var image = DecodeFlowImage(path);
                    if (image != null) flowImages[path] = image;
                }

                if (flowImages.Count > 160) flowImages.Clear();
            });
        }

        private FrameworkElement CreateFlowCard(PadItem item, double width, double height)
        {
            double radius = 18;
            var surface = new Grid { Background = BrushCardBg };

            var letter = new TextBlock
            {
                Text = item.Name.Length > 0 ? item.Name.Substring(0, 1).ToUpperInvariant() : "?",
                FontSize = width * 0.34,
                FontWeight = FontWeights.Bold,
                Foreground = BrushPlaceholder,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            surface.Children.Add(letter);

            var image = new System.Windows.Controls.Image { Visibility = Visibility.Collapsed };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            surface.Children.Add(image);

            var frame = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(4), Opacity = 0 };
            frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            surface.Children.Add(frame);

            BeginFlowPicture(item, (picture, logo) =>
            {
                image.Source = picture;
                image.Stretch = logo ? Stretch.Uniform : Stretch.UniformToFill;
                image.Margin = logo ? new Thickness(width * 0.16) : new Thickness(0);
                image.Visibility = Visibility.Visible;
                letter.Visibility = Visibility.Collapsed;
            });

            return new Border
            {
                CornerRadius = new CornerRadius(radius),
                Background = BrushCardBg,
                Child = surface,
                Clip = new RectangleGeometry(new System.Windows.Rect(0, 0, width, height), radius, radius),
                Tag = frame,
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private PadItem? padBackdropItem;
        private DispatcherTimer? padBackdropTimer;
        private DateTime lastMoveSound = DateTime.MinValue;

        private void OnPadSelectionChanged()
        {
            if (padFlow == null || padTitle == null || padMeta == null) return;

            int index = padFlow.SelectedIndex;
            if (index < 0 || index >= padItems.Count)
            {
                padTitle.Text = string.Empty;
                padMeta.Text = string.Empty;
                padBackdropItem = null;
                if (padBackdrop != null)
                {
                    padBackdrop.BeginAnimation(UIElement.OpacityProperty, null);
                    padBackdrop.Opacity = 0;
                }
                return;
            }

            var item = padItems[index];
            padTitle.Text = item.Name;
            padMeta.Text = item.Meta;

            // Der große Hintergrund wird erst geändert, wenn kurz nichts mehr gescrollt wird
            padBackdropItem = item;
            padBackdropTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            padBackdropTimer.Tick -= PadBackdropTick;
            padBackdropTimer.Tick += PadBackdropTick;
            padBackdropTimer.Stop();
            padBackdropTimer.Start();

            if (padFlow.IsLoaded && controllerMode)
            {
                PrefetchFlow(padFlow, padItems);
                if ((DateTime.Now - lastMoveSound).TotalMilliseconds > 70)
                {
                    lastMoveSound = DateTime.Now;
                    PlayUiSound("move");
                }
            }
        }

        private void PadBackdropTick(object? sender, EventArgs e)
        {
            padBackdropTimer?.Stop();

            var item = padBackdropItem;
            if (item == null || padBackdrop == null) return;

            padBackdrop.Opacity = 0;
            BeginFlowPicture(item, (picture, logo) =>
            {
                if (padBackdropItem != item || logo || padBackdrop == null) return;
                padBackdrop.Source = picture;
                padBackdrop.Opacity = 0.34;
            });
        }

        // ═════════════════════════════ Bibliothek: Auswahl, smarte Listen, Vorschlag, Jahresrückblick, Downloads ═════════════════════════════

        private void InitExtras20()
        {
            BuildSmartListChips();

            downloadDoneTimer.Tick += async (s, e) => await CheckFinishedDownloadsAsync();
            downloadDoneTimer.Start();
        }

        // ───────── Mehrfachauswahl (Strg+Klick, Rechtsklick für alle) ─────────

        private readonly HashSet<GameItem> selectedGames = new();
        private readonly Dictionary<Border, (System.Windows.Media.Brush? Brush, Thickness Thickness)> selectionOriginal = new();

        private void HookMultiSelect(Border element, GameItem game)
        {
            if (selectedGames.Contains(game)) MarkSelected(element, true);

            element.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
                e.Handled = true;   // kein Spielstart beim Markieren

                if (!selectedGames.Remove(game)) selectedGames.Add(game);
                MarkSelected(element, selectedGames.Contains(game));
                UpdateSelectionButton();
            };

            element.ContextMenuOpening += (s, e) =>
            {
                if (selectedGames.Count < 2 || !selectedGames.Contains(game)) return;
                e.Handled = true;
                var menu = BuildBulkMenu(selectedGames.ToList());
                menu.PlacementTarget = element;
                menu.IsOpen = true;
            };
        }

        private void MarkSelected(Border element, bool selected)
        {
            if (selected)
            {
                if (!selectionOriginal.ContainsKey(element)) selectionOriginal[element] = (element.BorderBrush, element.BorderThickness);
                element.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                element.BorderThickness = new Thickness(3);
            }
            else if (selectionOriginal.Remove(element, out var original))
            {
                element.BorderBrush = original.Brush;
                element.BorderThickness = original.Thickness;
            }
        }

        private void UpdateSelectionButton()
        {
            if (BtnBulk == null) return;
            BtnBulk.Content = selectedGames.Count > 0 ? Loc.T($"☑ {selectedGames.Count} ausgewählt") : Loc.T("☑ Auswahl bearbeiten");
            if (selectedGames.Count > 0) BtnBulk.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            else BtnBulk.Background = MakeBrush("#1F2937");
            BtnBulk.Foreground = selectedGames.Count > 0 ? System.Windows.Media.Brushes.White : MakeBrush("#9CA3AF");
        }

        private void ClearGameSelection()
        {
            selectedGames.Clear();
            foreach (var element in selectionOriginal.Keys.ToList()) MarkSelected(element, false);
            UpdateSelectionButton();
        }

        private System.Windows.Controls.ContextMenu BuildBulkMenu(List<GameItem> games)
        {
            var menu = CreateMenu();
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = Loc.T($"{games.Count} Spiele ausgewählt"), IsEnabled = false });

            var collections = new System.Windows.Controls.MenuItem { Header = Loc.T("📁  Sammlungen") };
            foreach (string name in settings.CollectionNames)
            {
                string collection = name;
                bool all = games.All(g => g.Collections.Contains(collection));
                var item = new System.Windows.Controls.MenuItem { Header = collection, IsCheckable = true, IsChecked = all };
                item.Click += (s, e) => ApplyBulk(games, g =>
                {
                    if (all) g.Collections.Remove(collection);
                    else if (!g.Collections.Contains(collection)) g.Collections.Add(collection);
                });
                collections.Items.Add(item);
            }
            if (settings.CollectionNames.Count > 0) collections.Items.Add(new System.Windows.Controls.Separator());
            var create = new System.Windows.Controls.MenuItem { Header = Loc.T("＋  Neue Sammlung ...") };
            create.Click += (s, e) =>
            {
                string? name = PromptText("Neue Sammlung", "Name der Sammlung (zum Beispiel „Backlog“ oder „Koop“):", string.Empty);
                if (name == null) return;
                if (!settings.CollectionNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    settings.CollectionNames.Add(name);
                    BuildCollectionChips();
                }
                string existing = settings.CollectionNames.First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                ApplyBulk(games, g =>
                {
                    if (!g.Collections.Contains(existing)) g.Collections.Add(existing);
                });
            };
            collections.Items.Add(create);
            menu.Items.Add(collections);

            AddMenuItem(menu, "★  Zu Favoriten hinzufügen", () => ApplyBulk(games, g => g.IsFavorite = true));
            AddMenuItem(menu, "☆  Aus Favoriten entfernen", () => ApplyBulk(games, g => g.IsFavorite = false));
            foreach (var option in StatusOptions)
            {
                string key = option.Key;
                AddMenuItem(menu, option.Title, () => ApplyBulk(games, g => g.Status = key));
            }
            AddMenuItem(menu, "🙈  Verstecken", () => ApplyBulk(games, g => g.Hidden = true));
            AddMenuItem(menu, "👁  Wieder anzeigen", () => ApplyBulk(games, g => g.Hidden = false));
            menu.Items.Add(new System.Windows.Controls.Separator());
            AddMenuItem(menu, "✕  Auswahl aufheben", ClearGameSelection);
            return menu;
        }

        // ───────── Smarte Listen ─────────

        private bool sizeScanRunning;
        private DateTime lastSizeScanCheck = DateTime.MinValue;

        private long? KnownGameSize(GameItem game)
        {
            if (string.IsNullOrEmpty(game.InstallDir)) return null;
            return settings.GameSizes.TryGetValue(game.InstallDir, out var entry) ? entry.Bytes : null;
        }

        private bool MatchesSmartList(SmartList list, GameItem game)
        {
            if (list.NeverPlayed && (game.PlaySeconds >= 60 || game.LaunchCount > 0)) return false;
            if (list.NotPlayedMonths > 0 && (!game.LastPlayed.HasValue || game.LastPlayed.Value > DateTime.Now.AddMonths(-list.NotPlayedMonths))) return false;
            if (list.Source.Length > 0 && !string.Equals(game.Source, list.Source, StringComparison.OrdinalIgnoreCase)) return false;
            if (list.MaxSizeGb > 0)
            {
                long? size = KnownGameSize(game);
                if (size == null)
                {
                    StartGameSizeScan();
                    return false;
                }
                if (size.Value > list.MaxSizeGb * 1024L * 1024L * 1024L) return false;
            }
            return true;
        }

        /// <summary>Misst fehlende oder alte Spielgrößen im Hintergrund und aktualisiert danach die Liste.</summary>
        private void StartGameSizeScan(bool force = false)
        {
            if (sizeScanRunning) return;

            // Wird beim Filtern für jedes Spiel ohne Größe aufgerufen: höchstens alle 30 Sekunden neu prüfen
            if (!force && (DateTime.Now - lastSizeScanCheck).TotalSeconds < 30) return;
            lastSizeScanCheck = DateTime.Now;

            var targets = allGames
                .Where(g => !string.IsNullOrEmpty(g.InstallDir) && Directory.Exists(g.InstallDir))
                .Select(g => g.InstallDir)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(dir => force || !settings.GameSizes.TryGetValue(dir, out var entry) || (DateTime.Now - entry.Measured).TotalDays > 14)
                .ToList();
            if (targets.Count == 0) return;

            sizeScanRunning = true;
            _ = Task.Run(() => targets.Select(dir => (Dir: dir, Size: GetDirectorySize(dir))).ToList()).ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    sizeScanRunning = false;
                    if (task.Status != TaskStatus.RanToCompletion) return;
                    foreach (var (dir, size) in task.Result)
                        settings.GameSizes[dir] = new GameSizeEntry { Bytes = size, Measured = DateTime.Now };
                    SaveSettings();
                    if (currentFilter.StartsWith("smart:")) ApplyFilter();
                }));
            });
        }

        private void BuildSmartListChips()
        {
            if (FilterChips == null) return;

            foreach (var chip in FilterChips.Children.OfType<System.Windows.Controls.RadioButton>()
                         .Where(c => c.Tag is string tag && tag.StartsWith("smart:")).ToList())
                FilterChips.Children.Remove(chip);

            int index = FilterChips.Children.IndexOf(BtnGenre);
            if (index < 0) index = FilterChips.Children.Count;

            foreach (var item in settings.SmartLists)
            {
                var list = item;
                var chip = new System.Windows.Controls.RadioButton
                {
                    Content = "✨ " + list.Name,
                    Tag = "smart:" + list.Id,
                    GroupName = "Filter",
                    Style = (Style)FindResource("ChipStyle"),
                    ToolTip = DescribeSmartList(list),
                    IsChecked = currentFilter == "smart:" + list.Id
                };
                chip.Checked += Filter_Checked;

                var menu = CreateMenu();
                AddMenuItem(menu, "✏  Bearbeiten ...", () => EditSmartList(list));
                AddMenuItem(menu, "🗑  Smarte Liste löschen", () => DeleteSmartList(list));
                chip.ContextMenu = menu;

                FilterChips.Children.Insert(index++, chip);
            }
        }

        private string DescribeSmartList(SmartList list)
        {
            var parts = new List<string>();
            if (list.NeverPlayed) parts.Add(Loc.T("nie gespielt"));
            if (list.MaxSizeGb > 0) parts.Add(Loc.T($"kleiner als {list.MaxSizeGb} GB"));
            if (list.NotPlayedMonths > 0) parts.Add(Loc.T($"seit {list.NotPlayedMonths} Monaten nicht gespielt"));
            if (list.Source.Length > 0) parts.Add(Loc.T("Quelle") + ": " + list.Source);
            return parts.Count == 0 ? Loc.T("Alle Spiele") : string.Join(" · ", parts);
        }

        private void BtnSmartList_Click(object sender, RoutedEventArgs e) => EditSmartList(null);

        private void DeleteSmartList(SmartList list)
        {
            if (Msg($"Smarte Liste „{list.Name}“ löschen?\nDie Spiele selbst bleiben erhalten.", "Smarte Liste", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            settings.SmartLists.Remove(list);
            SaveSettings();
            if (currentFilter == "smart:" + list.Id)
            {
                currentFilter = "all";
                var all = FilterChips.Children.OfType<System.Windows.Controls.RadioButton>().FirstOrDefault(c => c.Tag as string == "all");
                if (all != null) all.IsChecked = true;
            }
            BuildSmartListChips();
            ApplyFilter();
        }

        private void EditSmartList(SmartList? existing)
        {
            var draft = new SmartList
            {
                Name = existing?.Name ?? string.Empty,
                NeverPlayed = existing?.NeverPlayed ?? false,
                MaxSizeGb = existing?.MaxSizeGb ?? 0,
                NotPlayedMonths = existing?.NotPlayedMonths ?? 0,
                Source = existing?.Source ?? string.Empty
            };

            var dialog = CreateDialog(existing == null ? "Neue smarte Liste" : "Smarte Liste bearbeiten", 580, out var panel);
            var chipStyle = (Style)FindResource("ChipStyle");

            panel.Children.Add(new TextBlock { Text = Loc.T("Name"), Foreground = BrushSubtle, FontSize = 12 });
            var name = new System.Windows.Controls.TextBox { Text = draft.Name, Height = 38, Margin = new Thickness(0, 4, 0, 16) };
            panel.Children.Add(name);

            panel.Children.Add(new TextBlock { Text = Loc.T("Regeln (alle ausgewählten müssen passen)"), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });

            var preview = new TextBlock { Foreground = MakeBrush("#34D399"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 18) };
            void UpdatePreview()
            {
                if (draft.MaxSizeGb > 0) StartGameSizeScan();
                int count = allGames.Count(g => !g.Hidden && MatchesSmartList(draft, g));
                string text = Loc.T($"Passt jetzt auf {count} Spiele.");
                if (draft.MaxSizeGb > 0 && sizeScanRunning) text += " " + Loc.T("Die Spielgrößen werden gerade ermittelt, danach können es mehr werden.");
                preview.Text = text;
            }

            var never = new System.Windows.Controls.CheckBox { Content = Loc.T("Nie gespielt"), IsChecked = draft.NeverPlayed, Margin = new Thickness(0, 0, 0, 12) };
            never.Checked += (s, e) => { draft.NeverPlayed = true; UpdatePreview(); };
            never.Unchecked += (s, e) => { draft.NeverPlayed = false; UpdatePreview(); };
            panel.Children.Add(never);

            WrapPanel Chips<T>(string label, IEnumerable<(string Text, T Value)> options, T current, Action<T> set, string group)
            {
                panel.Children.Add(new TextBlock { Text = Loc.T(label), Foreground = BrushSubtle, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
                var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
                foreach (var (text, value) in options)
                {
                    var option = value;
                    var chip = new System.Windows.Controls.RadioButton { Content = text, GroupName = group, Style = chipStyle, IsChecked = Equals(option, current) };
                    chip.Checked += (s, e) => { set(option); UpdatePreview(); };
                    wrap.Children.Add(chip);
                }
                panel.Children.Add(wrap);
                return wrap;
            }

            Chips("Kleiner als", new[] { 0, 5, 10, 20, 50, 100 }.Select(v => (v == 0 ? Loc.T("Egal") : $"{v} GB", v)), draft.MaxSizeGb, v => draft.MaxSizeGb = v, "SmartSize");
            Chips("Nicht mehr gespielt seit", new[] { 0, 1, 3, 6, 12, 24 }.Select(v => (v == 0 ? Loc.T("Egal") : v == 1 ? Loc.T("1 Monat") : Loc.T($"{v} Monate"), v)), draft.NotPlayedMonths, v => draft.NotPlayedMonths = v, "SmartMonths");
            var sources = allGames.Select(g => g.Source).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (draft.Source.Length > 0 && !sources.Contains(draft.Source, StringComparer.OrdinalIgnoreCase)) sources.Add(draft.Source);
            Chips("Quelle ist", new[] { (Loc.T("Egal"), string.Empty) }.Concat(sources.Select(s => (s, s))), draft.Source, v => draft.Source = v, "SmartSource");

            panel.Children.Add(preview);
            UpdatePreview();

            var buttons = new Grid();
            var right = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var cancel = new System.Windows.Controls.Button { Content = "Abbrechen", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var save = new System.Windows.Controls.Button { Content = "Speichern", Padding = new Thickness(28, 8, 28, 8), IsDefault = true };
            save.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            right.Children.Add(cancel);
            right.Children.Add(save);
            buttons.Children.Add(right);
            panel.Children.Add(buttons);

            save.Click += (s, e) =>
            {
                string text = name.Text.Trim();
                if (text.Length == 0)
                {
                    name.Focus();
                    return;
                }

                var target = existing ?? new SmartList();
                target.Name = text;
                target.NeverPlayed = draft.NeverPlayed;
                target.MaxSizeGb = draft.MaxSizeGb;
                target.NotPlayedMonths = draft.NotPlayedMonths;
                target.Source = draft.Source;
                if (existing == null) settings.SmartLists.Add(target);
                SaveSettings();
                dialog.DialogResult = true;

                currentFilter = "smart:" + target.Id;
                BuildSmartListChips();
                playCardEntrance = true;
                ApplyFilter();
            };

            dialog.Loaded += (s, e) => name.Focus();
            dialog.ShowDialog();
        }

        // ───────── Spielvorschlag nach Laune ─────────

        private static readonly string[] RelaxedKeys = { "casual", "gelegenheit", "simulation", "puzzle", "rätsel", "family", "familie", "aufbau", "building", "farming", "entspann", "chill", "cozy", "gemütlich" };
        private static readonly string[] ExcitingKeys = { "action", "shooter", "racing", "rennspiel", "fighting", "kampf", "horror", "survival", "multiplayer", "massively", "sport", "battle", "spannend", "competitive" };
        private static readonly string[] ShortKeys = { "casual", "gelegenheit", "arcade", "puzzle", "rätsel", "racing", "rennspiel", "sport", "fighting", "kampf", "roguelike", "rogue-lite", "roguelite" };
        private static readonly string[] LongKeys = { "rpg", "rollenspiel", "strateg", "massively", "mmo", "open world", "offene welt" };

        /// <summary>Genres (Steam-Infos) und eigene Sammlungen eines Spiels, klein geschrieben.</summary>
        private List<string> GameTags(GameItem game)
        {
            var tags = new List<string>();
            if (game.AppId.Length > 0 && settings.GameInfo.TryGetValue(game.AppId, out var info))
                tags.AddRange(info.Genres.Select(g => g.ToLowerInvariant()));
            tags.AddRange(game.Collections.Select(c => c.ToLowerInvariant()));
            return tags;
        }

        private static bool HasAny(List<string> tags, string[] keys) => tags.Any(t => keys.Any(k => t.Contains(k, StringComparison.Ordinal)));

        /// <summary>Bewertet ein Spiel für Zeit (30/60/0 = egal) und Laune (relaxed/exciting/new). Liefert Punkte und Begründung.</summary>
        private (double Score, List<string> Reasons) ScoreSuggestion(GameItem game, int minutes, string mood)
        {
            var tags = GameTags(game);
            bool relaxed = HasAny(tags, RelaxedKeys), exciting = HasAny(tags, ExcitingKeys);
            bool shortGame = HasAny(tags, ShortKeys), longGame = HasAny(tags, LongKeys);
            var reasons = new List<string>();
            double score = random.NextDouble() * 1.5;   // etwas Zufall für Abwechslung

            switch (mood)
            {
                case "relaxed":
                    if (relaxed) { score += 3; reasons.Add("Entspannt"); }
                    if (HasAny(tags, new[] { "horror", "shooter" })) score -= 2;
                    break;
                case "exciting":
                    if (exciting) { score += 3; reasons.Add("Spannend"); }
                    else if (relaxed) score -= 1;
                    break;
                case "new":
                    if (game.PlaySeconds < 60 && game.LaunchCount == 0) { score += 4; reasons.Add("noch nie gespielt"); }
                    else if (game.PlaySeconds < 2 * 3600) { score += 2; reasons.Add("erst kurz angespielt"); }
                    if (game.FirstSeen.HasValue && (DateTime.Now - game.FirstSeen.Value).TotalDays < 30) { score += 1; reasons.Add("neu in deiner Bibliothek"); }
                    if (game.Status == "backlog") { score += 1; reasons.Add("steht auf deiner Liste"); }
                    if (game.PlaySeconds > 20 * 3600) score -= 3;
                    break;
            }

            if (minutes == 30)
            {
                if (shortGame) { score += 2; reasons.Add("gut für zwischendurch"); }
                if (longGame) score -= 2;
            }
            else if (minutes == 60)
            {
                if (shortGame) score += 1;
                if (HasAny(tags, new[] { "massively", "mmo" })) score -= 1;
            }

            if (game.Status == "done") score -= 2;
            if (game.LastPlayed.HasValue && (DateTime.Now - game.LastPlayed.Value).TotalHours < 12) score -= 1.5;
            if (game.IsFavorite) score += 0.5;
            score += game.Rating * 0.3;

            return (score, reasons);
        }

        private void BtnSuggestGame_Click(object sender, RoutedEventArgs e) => ShowGameSuggestion();

        private void ShowGameSuggestion()
        {
            var pool = allGames.Where(g => !g.Hidden && !IsTrackingIgnored(g)).ToList();
            if (pool.Count == 0)
            {
                Msg("Es sind noch keine Spiele in deiner Bibliothek.", "Spielvorschlag");
                return;
            }

            var dialog = CreateDialog("Was soll ich spielen?", 560, out var panel);
            var chipStyle = (Style)FindResource("ChipStyle");
            int minutes = 0;
            string mood = "relaxed";
            var shown = new HashSet<GameItem>();
            GameItem? current = null;

            // Ergebnis-Felder zuerst anlegen, die Auswahl-Chips greifen darauf zu
            var cover = new System.Windows.Controls.Image { Width = 120, Height = 170, Stretch = Stretch.UniformToFill };
            var title = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, TextWrapping = TextWrapping.Wrap };
            var reason = new TextBlock { Foreground = BrushSubtle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            var meta = new TextBlock { Foreground = MakeBrush("#9CA3AF"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };

            panel.Children.Add(new TextBlock { Text = Loc.T("Wie viel Zeit hast du?"), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var timeChips = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var (text, value) in new[] { ("30 Minuten", 30), ("1 Stunde", 60), ("Egal", 0) })
            {
                int option = value;
                var chip = new System.Windows.Controls.RadioButton { Content = Loc.T(text), GroupName = "SuggestTime", Style = chipStyle, IsChecked = option == minutes };
                chip.Checked += (s, e) => { minutes = option; shown.Clear(); Suggest(); };
                timeChips.Children.Add(chip);
            }
            panel.Children.Add(timeChips);

            panel.Children.Add(new TextBlock { Text = Loc.T("Worauf hast du Lust?"), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var moodChips = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            foreach (var (text, value) in new[] { ("Etwas Entspanntes", "relaxed"), ("Etwas Spannendes", "exciting"), ("Etwas Neues", "new") })
            {
                string option = value;
                var chip = new System.Windows.Controls.RadioButton { Content = Loc.T(text), GroupName = "SuggestMood", Style = chipStyle, IsChecked = option == mood };
                chip.Checked += (s, e) => { mood = option; shown.Clear(); Suggest(); };
                moodChips.Children.Add(chip);
            }
            panel.Children.Add(moodChips);

            // Ergebnis
            var coverBox = new Border { Width = 120, Height = 170, CornerRadius = new CornerRadius(12), Background = BrushCardBg, ClipToBounds = true, Child = cover, Margin = new Thickness(0, 0, 18, 0) };
            var result = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            result.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            result.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            result.Children.Add(coverBox);
            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Children = { title, reason, meta } };
            Grid.SetColumn(texts, 1);
            result.Children.Add(texts);
            panel.Children.Add(new Border { Background = MakeBrush("#14FFFFFF"), CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Child = result, Margin = new Thickness(0, 0, 0, 18) });

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var another = new System.Windows.Controls.Button { Content = Loc.T("🎲  Anderer Vorschlag"), Margin = new Thickness(0, 0, 10, 0) };
            var play = new System.Windows.Controls.Button { Content = Loc.T("▶  Spielen"), Padding = new Thickness(26, 8, 26, 8), IsDefault = true };
            play.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            var close = new System.Windows.Controls.Button { Content = Loc.T("Schließen"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            buttons.Children.Add(close);
            buttons.Children.Add(another);
            buttons.Children.Add(play);
            panel.Children.Add(buttons);

            void Suggest()
            {
                var ranked = pool.Where(g => !shown.Contains(g))
                    .Select(g => (Game: g, Rating: ScoreSuggestion(g, minutes, mood)))
                    .OrderByDescending(x => x.Rating.Score)
                    .Take(5)
                    .ToList();
                if (ranked.Count == 0)
                {
                    shown.Clear();
                    ranked = pool.Select(g => (Game: g, Rating: ScoreSuggestion(g, minutes, mood))).OrderByDescending(x => x.Rating.Score).Take(5).ToList();
                }

                var pick = ranked[random.Next(ranked.Count)];
                current = pick.Game;
                shown.Add(current);

                title.Text = current.Name;
                var reasons = pick.Rating.Reasons.Count > 0 ? pick.Rating.Reasons : new List<string> { "Ein Zufallstreffer aus deiner Bibliothek" };
                reason.Text = string.Join(" · ", reasons.Select(r => Loc.T(r)));
                meta.Text = current.PlaySeconds >= 60 && !SHide(settings.StreamHideStats)
                    ? $"{current.Source}  ·  {Loc.T(FormatPlaytime(current.PlaySeconds))}"
                    : current.Source;
                cover.Source = HasCover(current) ? GetCachedImage(EffectiveCover(current)) : current.LocalIcon;
            }

            another.Click += (s, e) => Suggest();
            play.Click += (s, e) =>
            {
                var game = current;
                dialog.DialogResult = true;
                if (game != null) LaunchGame(game);
            };

            Suggest();
            dialog.ShowDialog();
        }

        // ───────── Jahresrückblick ─────────

        private void AddYearPlay(GameItem game, double seconds)
        {
            settings.YearStatsSince ??= DateTime.Now;
            string year = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
            if (!settings.YearStats.TryGetValue(year, out var stat) || stat == null)
            {
                stat = new YearStat();
                settings.YearStats[year] = stat;
            }
            stat.Games.TryGetValue(game.Name, out double current);
            stat.Games[game.Name] = current + seconds;
            stat.Sources[game.Name] = game.Source;
        }

        private void RecordSession(GameItem game, TimeSpan duration)
        {
            if (duration.TotalMinutes < 1 || IsTrackingIgnored(game)) return;
            string year = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
            if (!settings.YearStats.TryGetValue(year, out var stat) || stat == null)
            {
                stat = new YearStat();
                settings.YearStats[year] = stat;
            }
            if (duration.TotalSeconds <= stat.LongestSeconds) return;

            stat.LongestSeconds = duration.TotalSeconds;
            stat.LongestGame = game.Name;
            stat.LongestDate = DateTime.Now;
        }

        private void ShowYearReview(int year)
        {
            if (SHide(settings.StreamHideStats)
                && Msg("Der Streamer-Modus verbirgt gerade Spielzeiten und Statistiken. Den Jahresrückblick trotzdem anzeigen?", "Jahresrückblick", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var dialog = CreateDialog("Jahresrückblick", 760, out var panel);
            var sheet = new StackPanel();
            panel.Children.Add(sheet);

            // Jahre, für die es Daten gibt
            var years = settings.PlayLog.Keys.Select(k => k.Length >= 4 && int.TryParse(k.Substring(0, 4), out int y) ? y : 0)
                .Concat(settings.YearStats.Keys.Select(k => int.TryParse(k, out int y) ? y : 0))
                .Where(y => y > 2000).Append(DateTime.Now.Year).Distinct().OrderByDescending(y => y).ToList();

            var yearChips = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            if (years.Count > 1)
            {
                var chipStyle = (Style)FindResource("ChipStyle");
                foreach (int y in years)
                {
                    int option = y;
                    var chip = new System.Windows.Controls.RadioButton { Content = option.ToString(CultureInfo.InvariantCulture), GroupName = "ReviewYear", Style = chipStyle, IsChecked = option == year };
                    chip.Checked += (s, e) => Render(option);
                    yearChips.Children.Add(chip);
                }
                panel.Children.Add(yearChips);
            }

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var saveImage = new System.Windows.Controls.Button { Content = Loc.T("📸  Als Bild speichern"), Margin = new Thickness(0, 0, 10, 0) };
            var close = new System.Windows.Controls.Button { Content = Loc.T("Schließen"), Padding = new Thickness(26, 8, 26, 8), IsCancel = true };
            close.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            buttons.Children.Add(saveImage);
            buttons.Children.Add(close);
            panel.Children.Add(buttons);

            saveImage.Click += (s, e) => SaveVisualAsPng(sheet, Loc.T("Bild speichern"));

            void Render(int shownYear)
            {
                sheet.Children.Clear();
                BuildYearSheet(sheet, shownYear);
                TranslateTree(sheet);
                if (StreamerOn) ScrubSensitiveText();
            }

            Render(year);
            dialog.ShowDialog();
        }

        private void BuildYearSheet(StackPanel sheet, int year)
        {
            string key = year.ToString(CultureInfo.InvariantCulture);
            var days = new List<(DateTime Day, double Seconds)>();
            foreach (var pair in settings.PlayLog)
            {
                if (DateTime.TryParseExact(pair.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day.Year == year)
                    days.Add((day, pair.Value));
            }
            settings.YearStats.TryGetValue(key, out var stat);

            // Kopf mit Verlauf in der Akzentfarbe
            var accent = TryFindResource("AccentBrush") is SolidColorBrush accentBrush ? accentBrush.Color : System.Windows.Media.Color.FromRgb(0x8B, 0x5C, 0xF6);
            var header = new Border
            {
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(26, 22, 26, 22),
                Margin = new Thickness(0, 0, 0, 16),
                Background = new LinearGradientBrush(accent, System.Windows.Media.Color.FromRgb(0x13, 0x17, 0x22), 20),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = Loc.T($"🎉 Dein Spielejahr {year}"), FontSize = 30, FontWeight = FontWeights.ExtraBold, Foreground = System.Windows.Media.Brushes.White },
                        new TextBlock { Text = "DFP Pro Launcher  ·  " + DisplayUserName(), FontSize = 14, Foreground = MakeBrush("#E5E7EB"), Margin = new Thickness(0, 6, 0, 0) }
                    }
                }
            };
            sheet.Children.Add(header);

            if (days.Count == 0 && (stat == null || stat.Games.Count == 0))
            {
                sheet.Children.Add(new TextBlock
                {
                    Text = Loc.T("Für dieses Jahr gibt es noch keine Spieldaten. Der Launcher erfasst die Spielzeit automatisch, sobald ein Spiel läuft."),
                    Foreground = BrushSubtle,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 0, 4, 8)
                });
                return;
            }

            // Top 5: echte Jahreswerte, sonst Rückfall auf die Gesamtzeit der in diesem Jahr gespielten Spiele
            bool yearly = stat != null && stat.Games.Count > 0;
            List<(string Name, double Seconds, string Source)> top = yearly
                ? stat!.Games.OrderByDescending(p => p.Value).Take(5)
                    .Select(p => (p.Key, p.Value, stat.Sources.TryGetValue(p.Key, out var src) ? src : string.Empty)).ToList()
                : allGames.Where(g => g.LastPlayed?.Year == year && g.PlaySeconds >= 60 && !IsTrackingIgnored(g))
                    .OrderByDescending(g => g.PlaySeconds).Take(5).Select(g => (g.Name, (double)g.PlaySeconds, g.Source)).ToList();

            double total = days.Sum(d => d.Seconds);
            if (total <= 0 && yearly) total = stat!.Games.Values.Sum();

            string favoriteSource = (yearly
                    ? stat!.Games.GroupBy(p => stat.Sources.TryGetValue(p.Key, out var src) ? src : string.Empty).Select(g => (Source: g.Key, Seconds: g.Sum(p => p.Value)))
                    : top.GroupBy(t => t.Source).Select(g => (Source: g.Key, Seconds: g.Sum(t => t.Seconds))))
                .Where(x => x.Source.Length > 0).OrderByDescending(x => x.Seconds).Select(x => x.Source).FirstOrDefault() ?? "–";

            int played = days.Count(d => d.Seconds >= 300);
            int streak = 0, run = 0;
            DateTime? previous = null;
            foreach (var d in days.Where(x => x.Seconds >= 300).OrderBy(x => x.Day))
            {
                run = previous.HasValue && (d.Day - previous.Value).Days == 1 ? run + 1 : 1;
                streak = Math.Max(streak, run);
                previous = d.Day;
            }

            string longest = stat != null && stat.LongestSeconds >= 60
                ? $"{FormatPlaytime((long)stat.LongestSeconds)}"
                : "–";
            string longestHint = stat != null && stat.LongestSeconds >= 60 ? stat.LongestGame : Loc.T("wird ab jetzt aufgezeichnet");

            // Kacheln
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 16) };
            void Tile(string icon, string label, string value, string hint = "")
            {
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = icon + "  " + Loc.T(label), Foreground = BrushSubtle, FontSize = 12 });
                stack.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                if (hint.Length > 0) stack.Children.Add(new TextBlock { Text = hint, Foreground = MakeBrush("#9CA3AF"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                tiles.Children.Add(new Border { Background = MakeBrush("#14FFFFFF"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 10, 10), Child = stack });
            }

            Tile("⏱", "Gesamte Spielzeit", FormatPlaytimeLong((long)total));
            Tile("📅", "Tage mit Spielzeit", played.ToString(CultureInfo.InvariantCulture));
            Tile("🔥", "Längste Serie", Loc.T($"{streak} Tage"));
            Tile("🏁", "Längste Sitzung", longest, longestHint);
            Tile("🏪", "Lieblingsquelle", favoriteSource);
            Tile("🎮", "Gespielte Spiele", (yearly ? stat!.Games.Count : top.Count).ToString(CultureInfo.InvariantCulture));
            sheet.Children.Add(tiles);

            // Top 5 mit Balken
            if (top.Count > 0)
            {
                sheet.Children.Add(new TextBlock { Text = Loc.T("Top 5 des Jahres"), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 10) });
                double max = top.Max(t => t.Seconds);
                int rank = 1;
                foreach (var (name, seconds, _) in top)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var rankText = new TextBlock { Text = $"{rank}.", FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                    rankText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                    row.Children.Add(rankText);

                    var middle = new StackPanel();
                    middle.Children.Add(new TextBlock { Text = name, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                    var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = MakeBrush("#26FFFFFF"), Margin = new Thickness(0, 5, 12, 0) };
                    var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                    fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    bar.Child = fill;
                    double fraction = max > 0 ? seconds / max : 0;
                    bar.SizeChanged += (s, e) => fill.Width = Math.Max(4, bar.ActualWidth * fraction);
                    middle.Children.Add(bar);
                    Grid.SetColumn(middle, 1);
                    row.Children.Add(middle);

                    var time = new TextBlock { Text = FormatPlaytime((long)seconds), Foreground = MakeBrush("#D1D5DB"), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                    Grid.SetColumn(time, 2);
                    row.Children.Add(time);
                    sheet.Children.Add(row);
                    rank++;
                }
            }

            // Hinweis zur Datengrundlage
            string note = !yearly
                ? "Für dieses Jahr gibt es noch keine Zeiten pro Spiel. Gezeigt wird die Gesamtspielzeit der Spiele, die du dieses Jahr gespielt hast."
                : settings.YearStatsSince is DateTime since && since.Year == year && since.DayOfYear > 1
                    ? Loc.T($"Spielzeit pro Spiel und längste Sitzung werden seit dem {since:dd.MM.yyyy} aufgezeichnet.")
                    : string.Empty;
            if (note.Length > 0)
                sheet.Children.Add(new TextBlock { Text = Loc.T(note), Foreground = MakeBrush("#9CA3AF"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        }

        // ───────── Download fertig ─────────

        private readonly DispatcherTimer downloadDoneTimer = new() { Interval = TimeSpan.FromSeconds(15) };
        private Dictionary<string, (string Store, string Name, string Manifest)>? activeDownloads;
        private bool downloadDoneBusy;

        private static List<string> SteamLibraryFolders()
        {
            var libraries = new List<string>();
            string? steamPath =
                Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return libraries;

            libraries.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(steamPath, "steamapps")));
            string vdf = System.IO.Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string library = System.IO.Path.GetFullPath(System.IO.Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));
                    if (Directory.Exists(library) && !libraries.Contains(library, StringComparer.OrdinalIgnoreCase)) libraries.Add(library);
                }
            }
            return libraries;
        }

        /// <summary>Alle laufenden Downloads von Steam und Epic (Schlüssel = Manifest-Datei). Läuft im Hintergrund.</summary>
        private static Dictionary<string, (string Store, string Name, string Manifest)> ScanActiveDownloads()
        {
            var result = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);

            foreach (string library in SteamLibraryFolders())
            {
                try
                {
                    foreach (string acf in Directory.EnumerateFiles(library, "appmanifest_*.acf"))
                    {
                        try
                        {
                            string text = File.ReadAllText(acf);
                            long flags = ReadAcfNumber(text, "StateFlags");
                            // 256 = Update läuft, 1048576 = lädt herunter, 2097152 = entpackt; 512 = pausiert
                            if ((flags & (256 | 1048576 | 2097152)) == 0 || (flags & 512) != 0) continue;
                            var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"");
                            result[acf] = ("Steam", name.Success ? name.Groups[1].Value : "Steam", acf);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            string epicDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(epicDir))
            {
                foreach (string file in Directory.EnumerateFiles(epicDir, "*.item"))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(file));
                        var root = document.RootElement;
                        if (!root.TryGetProperty("bIsIncompleteInstall", out var incomplete) || incomplete.ValueKind != JsonValueKind.True) continue;
                        string name = GetJsonString(root, "DisplayName");
                        if (name.Length > 0) result[file] = ("Epic Games", name, file);
                    }
                    catch { }
                }
            }

            return result;
        }

        /// <summary>Ist der Download wirklich fertig (nicht nur pausiert oder abgebrochen)?</summary>
        private static bool IsDownloadComplete(string store, string manifest)
        {
            try
            {
                if (!File.Exists(manifest)) return false;   // abgebrochen oder deinstalliert
                string text = File.ReadAllText(manifest);

                if (store == "Steam")
                {
                    long flags = ReadAcfNumber(text, "StateFlags");
                    return (flags & 4) != 0 && (flags & (2 | 256 | 512 | 1048576 | 2097152)) == 0;   // 4 = vollständig installiert
                }

                using var document = JsonDocument.Parse(text);
                return document.RootElement.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.False;
            }
            catch
            {
                return false;
            }
        }

        private async Task CheckFinishedDownloadsAsync()
        {
            if (downloadDoneBusy) return;
            if (!settings.DownloadDoneNotify)
            {
                activeDownloads = null;
                return;
            }

            downloadDoneBusy = true;
            try
            {
                var previous = activeDownloads;
                var current = await Task.Run(ScanActiveDownloads);
                activeDownloads = current;
                if (previous == null) return;   // erster Durchlauf: nur merken

                foreach (var (manifest, info) in previous)
                {
                    if (current.ContainsKey(manifest)) continue;
                    bool done = await Task.Run(() => IsDownloadComplete(info.Store, info.Manifest));
                    if (done) NotifyDownloadDone(info.Name, info.Store);
                }
            }
            catch { }
            finally
            {
                downloadDoneBusy = false;
            }
        }

        private void NotifyDownloadDone(string name, string store)
        {
            if (!settings.DownloadDoneNotify || AlertsMuted()) return;

            string text = Loc.T($"„{name}“ ist fertig.");
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                ShowToast("✅", Loc.T("Download fertig"), text + " (" + store + ")", 8, () => NavigateTo("games"));
            }
            else if (trayIcon != null && trayIcon.Visible)
            {
                trayIcon.ShowBalloonTip(6000, Loc.T("Download fertig"), text, Forms.ToolTipIcon.Info);
            }
        }

        private void ChkDownloadDone_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            settings.DownloadDoneNotify = ChkDownloadDone.IsChecked == true;
            SaveSettings();
        }
    }
}
