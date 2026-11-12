using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Launcher: Liste, Filter-Chips, weitere Stores, neue Spiele erkennen ─────────────────────────────

        /// <summary>Quelle für Spiele aus den eigenen Spiele-Ordnern (Einstellung GameFolders).</summary>
        private const string FolderSource = "Spiele-Ordner";

        /// <summary>Alle Launcher, die der Scan kennt, in der Reihenfolge der Filter-Chips.</summary>
        private static readonly string[] KnownLaunchers =
        {
            "Steam", "Epic Games", "Xbox", "EA", "Ubisoft", "GOG", "Battle.net", "Rockstar",
            "Amazon Games", "Riot Games", "itch.io", "Heroic", "Humble", "Legacy Games",
            "Wargaming", "HoYoPlay", "Plarium Play", "Big Fish", FolderSource, "Manuell"
        };

        /// <summary>Ohne eigene Auswahl stehen nur diese oben (und nur, wenn es dort Spiele gibt).</summary>
        private static readonly string[] MainLaunchers = { "Steam", "Epic Games", "Xbox", "EA", "Ubisoft" };

        private readonly List<System.Windows.Controls.RadioButton> launcherChips = new();
        private readonly Dictionary<string, ImageSource?> launcherIcons = new(StringComparer.OrdinalIgnoreCase);

        private void InitLaunchers()
        {
            BuildLauncherChips();
            _ = DownloadMissingLauncherLogosAsync();

            newGamesTimer.Tick += async (s, e) => await CheckForNewGamesAsync();
            newGamesTimer.Start();
        }

        private static string GamesText(int count) => count == 1 ? Loc.T("1 Spiel") : Loc.T($"{count} Spiele");

        private static bool IsSpecialFilter(string filter)
            => filter is "all" or "fav" or "new" or "unplayed" or "hidden"
               || filter.StartsWith("col:") || filter.StartsWith("smart:");

        private Dictionary<string, int> LauncherCounts()
            => allGames.Where(g => !g.Hidden && !string.IsNullOrEmpty(g.Source))
                .GroupBy(g => g.Source, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Bekannte Launcher zuerst (feste Reihenfolge), danach unbekannte Quellen alphabetisch.</summary>
        private static List<string> OrderLaunchers(IEnumerable<string> sources)
        {
            var set = new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
            var ordered = KnownLaunchers.Where(set.Contains).ToList();
            ordered.AddRange(set.Where(s => !KnownLaunchers.Contains(s, StringComparer.OrdinalIgnoreCase))
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase));
            return ordered;
        }

        /// <summary>Welche Launcher stehen oben als Chip? Ohne eigene Auswahl: die wichtigsten mit mindestens einem Spiel.</summary>
        private HashSet<string> ShownLaunchers(Dictionary<string, int> counts)
        {
            if (settings.LauncherChips != null)
                return new HashSet<string>(settings.LauncherChips, StringComparer.OrdinalIgnoreCase);

            // Eigene Spiele-Ordner hat der Nutzer selbst angelegt: die gehören dann auch automatisch nach oben
            return new HashSet<string>(MainLaunchers.Append(FolderSource).Where(counts.ContainsKey), StringComparer.OrdinalIgnoreCase);
        }

        private void BuildLauncherChips()
        {
            if (FilterChips == null || BtnLaunchers == null) return;

            foreach (var old in launcherChips) FilterChips.Children.Remove(old);
            launcherChips.Clear();

            var counts = LauncherCounts();
            var shown = ShownLaunchers(counts);
            var chipStyle = (Style)FindResource("ChipStyle");
            int index = FilterChips.Children.IndexOf(BtnLaunchers);
            if (index < 0) index = FilterChips.Children.Count;

            foreach (string source in OrderLaunchers(shown))
            {
                string launcher = source;
                counts.TryGetValue(launcher, out int count);

                var chip = new System.Windows.Controls.RadioButton
                {
                    Content = LauncherLabel(launcher, 15),
                    Tag = launcher,
                    GroupName = "Filter",
                    Style = chipStyle,
                    ToolTip = GamesText(count),
                    IsChecked = currentFilter == launcher
                };
                chip.Checked += Filter_Checked;

                var menu = CreateMenu();
                AddMenuItem(menu, "✕  Hier ausblenden", () => SetLauncherChip(launcher, false));
                chip.ContextMenu = menu;

                FilterChips.Children.Insert(index++, chip);
                launcherChips.Add(chip);
            }

            // Gewählter Launcher wurde ausgeblendet: zurück auf „Alle“
            if (!IsSpecialFilter(currentFilter) && !shown.Contains(currentFilter))
            {
                currentFilter = "all";
                var all = FilterChips.Children.OfType<System.Windows.Controls.RadioButton>().FirstOrDefault(c => c.Tag as string == "all");
                if (all != null) all.IsChecked = true;
                ApplyFilter();
            }
        }

        /// <summary>Logo des Launchers und Name nebeneinander (mit <paramref name="suffix"/> leiser dahinter, z. B. die Anzahl im Menü).</summary>
        private FrameworkElement LauncherLabel(string source, double iconSize, string? suffix = null)
        {
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var icon = GetLauncherIcon(source);
            if (icon != null)
            {
                var image = new System.Windows.Controls.Image
                {
                    Source = icon,
                    Width = iconSize,
                    Height = iconSize,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);

                // Dunkle Logos (EA, Ubisoft, Plarium) wären auf dem dunklen Design kaum zu sehen: kleine helle Kachel dahinter
                if (webLogos.Contains(source) && DarkLogos.Contains(source))
                {
                    row.Children.Add(new Border
                    {
                        Child = image,
                        Background = MakeBrush("#E8FFFFFF"),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(1),
                        Margin = new Thickness(0, 0, 7, 0),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    });
                }
                else
                {
                    image.Margin = new Thickness(0, 0, 7, 0);
                    row.Children.Add(image);
                }
            }
            else
            {
                var glyph = new TextBlock
                {
                    Text = source switch { "Xbox" => "\uE990", FolderSource => "\uE8B7", "Manuell" => "\uE710", _ => "\uE7FC" },
                    FontSize = iconSize - 2,
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                row.Children.Add(glyph);
            }
            row.Children.Add(new TextBlock { Text = Loc.T(source), VerticalAlignment = System.Windows.VerticalAlignment.Center });
            if (!string.IsNullOrEmpty(suffix))
            {
                var extra = new TextBlock { Text = "  ·  " + suffix, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                extra.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                row.Children.Add(extra);
            }
            return row;
        }

        /// <summary>Webseite je Launcher: Von dort kommt das Logo, wenn der Launcher nicht installiert ist.</summary>
        private static readonly Dictionary<string, string> LauncherDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Steam"] = "store.steampowered.com",
            ["Epic Games"] = "store.epicgames.com",
            ["Xbox"] = "xbox.com",
            ["EA"] = "ea.com",
            ["Ubisoft"] = "ubisoft.com",
            ["GOG"] = "gog.com",
            ["Battle.net"] = "battle.net",
            ["Rockstar"] = "rockstargames.com",
            ["Amazon Games"] = "amazongames.com",
            ["Riot Games"] = "riotgames.com",
            ["itch.io"] = "itch.io",
            ["Heroic"] = "heroicgameslauncher.com",
            ["Humble"] = "humblebundle.com",
            ["Legacy Games"] = "legacygames.com",
            ["Wargaming"] = "wargaming.net",
            ["HoYoPlay"] = "hoyoverse.com",
            ["Plarium Play"] = "plarium.com",
            ["Big Fish"] = "bigfishgames.com"
        };

        private static readonly HashSet<string> DarkLogos = new(StringComparer.OrdinalIgnoreCase) { "EA", "Ubisoft", "Plarium Play" };
        private readonly HashSet<string> webLogos = new(StringComparer.OrdinalIgnoreCase);   // Logos, die von der Webseite stammen (nicht aus der .exe)

        private static string LauncherLogoFile(string source)
            => System.IO.Path.Combine(LauncherLogosDir, string.Concat(source.Where(char.IsLetterOrDigit)).ToLowerInvariant() + ".png");

        /// <summary>Logo: erst aus der installierten .exe, sonst das gespeicherte Logo von der Webseite.</summary>
        private ImageSource? GetLauncherIcon(string source)
        {
            if (launcherIcons.TryGetValue(source, out var cached)) return cached;

            ImageSource? icon = null;
            try
            {
                string? exe = LauncherExePath(source);
                if (exe != null) icon = ExtractIcon(exe);

                string file = LauncherLogoFile(source);
                if (icon == null && LauncherDomains.ContainsKey(source) && File.Exists(file))
                {
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(file);
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    icon = image;
                    webLogos.Add(source);
                }
            }
            catch { }

            launcherIcons[source] = icon;
            return icon;
        }

        private bool launcherLogosLoading;

        /// <summary>Lädt einmalig die Logos der Launcher, die nicht installiert sind, und speichert sie (danach auch offline da).</summary>
        private async Task DownloadMissingLauncherLogosAsync()
        {
            if (launcherLogosLoading || !settings.OnlineFeatures) return;

            var missing = LauncherDomains.Keys.Where(s => GetLauncherIcon(s) == null).ToList();
            if (missing.Count == 0) return;

            launcherLogosLoading = true;
            bool any = false;
            try
            {
                Directory.CreateDirectory(LauncherLogosDir);
                foreach (string source in missing)
                {
                    try
                    {
                        byte[] bytes = await Http.GetByteArrayAsync("https://www.google.com/s2/favicons?domain=" + LauncherDomains[source] + "&sz=64");
                        // Ohne eigenes Logo liefert der Dienst nur eine kleine Weltkugel: dann lieber das Ersatzsymbol behalten
                        bool usable = await Task.Run(() =>
                        {
                            using var stream = new MemoryStream(bytes);
                            var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                                System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
                            return frame.PixelWidth >= 32;
                        });
                        if (!usable) continue;

                        await File.WriteAllBytesAsync(LauncherLogoFile(source), bytes);
                        launcherIcons.Remove(source);
                        any = true;
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                launcherLogosLoading = false;
            }

            if (any) BuildLauncherChips();
        }

        /// <summary>Wo die .exe des Launchers liegt (für Symbol und „Launcher öffnen“). Null, wenn nicht gefunden.</summary>
        private static string? LauncherExePath(string source)
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var candidates = new List<string>();

            static string? RegString(string key, string value)
            {
                try { return Registry.GetValue(key, value, null) as string; }
                catch { return null; }
            }

            switch (source)
            {
                case "Steam":
                    string? steam = RegString(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath");
                    if (!string.IsNullOrEmpty(steam)) candidates.Add(System.IO.Path.Combine(steam, "steam.exe"));
                    break;
                case "Epic Games":
                    candidates.Add(System.IO.Path.Combine(pf, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"));
                    candidates.Add(System.IO.Path.Combine(pf, "Epic Games", "Launcher", "Portal", "Binaries", "Win32", "EpicGamesLauncher.exe"));
                    candidates.Add(System.IO.Path.Combine(pf86, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"));
                    candidates.Add(System.IO.Path.Combine(pf86, "Epic Games", "Launcher", "Portal", "Binaries", "Win32", "EpicGamesLauncher.exe"));
                    break;
                case "EA":
                    candidates.Add(System.IO.Path.Combine(pf, "Electronic Arts", "EA Desktop", "EA Desktop", "EADesktop.exe"));
                    candidates.Add(System.IO.Path.Combine(pf86, "Origin", "Origin.exe"));
                    break;
                case "Ubisoft":
                    string? ubi = RegString(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir");
                    if (!string.IsNullOrEmpty(ubi))
                    {
                        candidates.Add(System.IO.Path.Combine(ubi, "UbisoftConnect.exe"));
                        candidates.Add(System.IO.Path.Combine(ubi, "upc.exe"));
                    }
                    candidates.Add(System.IO.Path.Combine(pf86, "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe"));
                    break;
                case "GOG":
                    string? gog = RegString(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths", "client");
                    if (!string.IsNullOrEmpty(gog)) candidates.Add(System.IO.Path.Combine(gog, "GalaxyClient.exe"));
                    candidates.Add(System.IO.Path.Combine(pf86, "GOG Galaxy", "GalaxyClient.exe"));
                    break;
                case "Battle.net":
                    candidates.Add(System.IO.Path.Combine(pf86, "Battle.net", "Battle.net Launcher.exe"));
                    candidates.Add(System.IO.Path.Combine(pf86, "Battle.net", "Battle.net.exe"));
                    break;
                case "Rockstar":
                    candidates.Add(System.IO.Path.Combine(pf, "Rockstar Games", "Launcher", "Launcher.exe"));
                    break;
                case "Amazon Games":
                    candidates.Add(System.IO.Path.Combine(local, "Amazon Games", "App", "Amazon Games.exe"));
                    break;
                case "Riot Games":
                    string riot = RiotClientPath() ?? string.Empty;
                    if (riot.Length > 0) candidates.Add(riot);
                    candidates.Add(@"C:\Riot Games\Riot Client\RiotClientServices.exe");
                    break;
                case "itch.io":
                    candidates.Add(System.IO.Path.Combine(local, "itch", "itch.exe"));
                    break;
                case "Heroic":
                    candidates.Add(System.IO.Path.Combine(local, "Programs", "heroic", "Heroic.exe"));
                    break;
                case "Humble":
                    candidates.Add(System.IO.Path.Combine(local, "Programs", "Humble App", "Humble App.exe"));
                    break;
                case "Legacy Games":
                    candidates.Add(System.IO.Path.Combine(pf, "Legacy Games", "Legacy Games Launcher", "Legacy Games Launcher.exe"));
                    break;
                case "Wargaming":
                    candidates.Add(System.IO.Path.Combine(common, "Wargaming.net", "GameCenter", "wgc.exe"));
                    break;
                case "HoYoPlay":
                    candidates.Add(System.IO.Path.Combine(pf, "HoYoPlay", "launcher.exe"));
                    break;
                case "Plarium Play":
                    candidates.Add(System.IO.Path.Combine(local, "PlariumPlay", "PlariumPlay.exe"));
                    break;
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string? RiotClientPath()
        {
            try
            {
                string file = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "RiotClientInstalls.json");
                if (!File.Exists(file)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                string path = GetJsonString(doc.RootElement, "rc_default");
                return path.Length > 0 && File.Exists(path) ? System.IO.Path.GetFullPath(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private void SetLauncherChip(string source, bool visible)
        {
            var counts = LauncherCounts();
            settings.LauncherChips ??= OrderLaunchers(ShownLaunchers(counts));
            settings.LauncherChips.RemoveAll(s => string.Equals(s, source, StringComparison.OrdinalIgnoreCase));
            if (visible) settings.LauncherChips.Add(source);
            SaveSettings();
            BuildLauncherChips();
        }

        /// <summary>Menü direkt auf der Spiele-Seite: jeden Launcher einzeln ein- oder ausblenden, Spiele-Ordner verwalten.</summary>
        private void BtnLaunchers_Click(object sender, RoutedEventArgs e)
        {
            var counts = LauncherCounts();
            var shown = ShownLaunchers(counts);
            var menu = CreateMenu();

            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = Loc.T("Oben anzeigen:"), IsEnabled = false });

            foreach (string source in OrderLaunchers(KnownLaunchers.Concat(counts.Keys)))
            {
                string launcher = source;
                counts.TryGetValue(launcher, out int count);

                var item = new System.Windows.Controls.MenuItem
                {
                    Header = LauncherLabel(launcher, 16, count > 0 ? GamesText(count) : Loc.T("keine Spiele")),
                    IsCheckable = true,
                    IsChecked = shown.Contains(launcher),
                    StaysOpenOnClick = true,
                    Opacity = count > 0 ? 1.0 : 0.55
                };
                item.Click += (s, args) => SetLauncherChip(launcher, item.IsChecked);
                menu.Items.Add(item);
            }

            menu.Items.Add(new System.Windows.Controls.Separator());
            AddMenuItem(menu, "★  Nur die wichtigsten", () =>
            {
                settings.LauncherChips = null;
                SaveSettings();
                BuildLauncherChips();
            });
            AddMenuItem(menu, "▦  Alle mit Spielen einblenden", () =>
            {
                settings.LauncherChips = OrderLaunchers(LauncherCounts().Keys);
                SaveSettings();
                BuildLauncherChips();
            });

            menu.Items.Add(new System.Windows.Controls.Separator());
            AddMenuItem(menu, "📂  Spiele-Ordner hinzufügen ...", AddGameFolder);
            if (settings.GameFolders.Count > 0)
            {
                var folders = new System.Windows.Controls.MenuItem { Header = Loc.T("📂  Spiele-Ordner entfernen") };
                foreach (string folder in settings.GameFolders.ToList())
                {
                    string path = folder;
                    AddSubItem(folders, StreamerOn ? FolderTitle(path) : path, () => RemoveGameFolder(path));
                }
                menu.Items.Add(folders);
            }

            menu.PlacementTarget = BtnLaunchers;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddGameFolder()
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = Loc.T("Ordner wählen, in dem deine Spiele liegen (jeder Unterordner ist ein Spiel)"),
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;

            string folder = System.IO.Path.GetFullPath(dialog.SelectedPath).TrimEnd('\\', '/');
            if (System.IO.Path.GetPathRoot(folder)?.TrimEnd('\\', '/') == folder)
            {
                Msg("Bitte wähle einen Ordner und nicht ein ganzes Laufwerk.", "Spiele-Ordner", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (settings.GameFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;

            settings.GameFolders.Add(folder);
            if (settings.LauncherChips != null && !settings.LauncherChips.Contains(FolderSource)) settings.LauncherChips.Add(FolderSource);
            SaveSettings();
            BtnRescanGames_Click(this, new RoutedEventArgs());
        }

        private void RemoveGameFolder(string folder)
        {
            var answer = Msg(Loc.T("Diesen Spiele-Ordner nicht mehr durchsuchen?") + "\n\n" + (StreamerOn ? FolderTitle(folder) : folder) + "\n\n" + Loc.T("Die Spiele selbst bleiben auf deinem PC."),
                "Spiele-Ordner", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            settings.GameFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            SaveSettings();
            BtnRescanGames_Click(this, new RoutedEventArgs());
        }

        // ───────── Weitere Launcher (laufen im Hintergrund-Thread des Scans) ─────────

        private sealed record UninstallEntry(string Name, string Publisher, string Location, string Uninstall, string Icon);

        /// <summary>Alle Einträge der Windows-Programmliste (64 Bit, 32 Bit und Benutzer).</summary>
        private static List<UninstallEntry> ReadUninstallEntries()
        {
            var entries = new List<UninstallEntry>();
            foreach (var (hive, path) in new[]
            {
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            })
            {
                try
                {
                    using var root = hive.OpenSubKey(path);
                    if (root == null) continue;
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = root.OpenSubKey(sub);
                            if (key == null) continue;
                            entries.Add(new UninstallEntry(
                                (key.GetValue("DisplayName") as string ?? string.Empty).Trim(),
                                key.GetValue("Publisher") as string ?? string.Empty,
                                (key.GetValue("InstallLocation") as string ?? string.Empty).Trim().Trim('"'),
                                key.GetValue("UninstallString") as string ?? string.Empty,
                                key.GetValue("DisplayIcon") as string ?? string.Empty));
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return entries;
        }

        /// <summary>Ordner eines Eintrags: InstallLocation, sonst der Ordner des Symbols.</summary>
        private static string EntryFolder(UninstallEntry entry)
        {
            if (entry.Location.Length > 0 && Directory.Exists(entry.Location)) return entry.Location;

            string icon = entry.Icon.Split(',')[0].Trim().Trim('"');
            if (icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon))
                return System.IO.Path.GetDirectoryName(icon) ?? string.Empty;
            return string.Empty;
        }

        /// <summary>Riot, Amazon, Wargaming, HoYoPlay, Plarium, Legacy Games und Big Fish tragen ihre Spiele in die Programmliste ein.</summary>
        private static void ScanRegistryLaunchers(Dictionary<string, GameItem> found)
        {
            List<UninstallEntry> entries;
            try { entries = ReadUninstallEntries(); }
            catch { return; }

            bool Publisher(UninstallEntry e, params string[] names)
                => names.Any(n => e.Publisher.Contains(n, StringComparison.OrdinalIgnoreCase));
            bool NameHas(UninstallEntry e, params string[] parts)
                => parts.Any(p => e.Name.Contains(p, StringComparison.OrdinalIgnoreCase));

            foreach (var entry in entries)
            {
                try
                {
                    if (entry.Name.Length == 0 || found.ContainsKey(entry.Name)) continue;

                    // Riot: "RiotClientServices.exe --uninstall-product=valorant --uninstall-patchline=live"
                    if (entry.Uninstall.Contains("RiotClientServices", StringComparison.OrdinalIgnoreCase))
                    {
                        var product = Regex.Match(entry.Uninstall, @"--uninstall-product=([\w.-]+)");
                        if (!product.Success || product.Groups[1].Value.Equals("riot_client", StringComparison.OrdinalIgnoreCase)) continue;
                        var patch = Regex.Match(entry.Uninstall, @"--uninstall-patchline=([\w.-]+)");
                        var (client, _) = SplitCommand(entry.Uninstall);
                        string folder = EntryFolder(entry);
                        if (folder.Length == 0 || !File.Exists(client)) continue;

                        AddStoreGame(found, entry.Name, "Riot Games", folder, string.Empty, client, product.Groups[1].Value);
                        if (found.TryGetValue(entry.Name, out var riotGame) && riotGame.Source == "Riot Games")
                            riotGame.LaunchUriArgs = $"--launch-product={product.Groups[1].Value} --launch-patchline={(patch.Success ? patch.Groups[1].Value : "live")}";
                        continue;
                    }

                    // Amazon Games: "Amazon Game Remover.exe -m Game -p amzn1.adg.product.…"
                    if (entry.Uninstall.Contains("Amazon Game Remover", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = Regex.Match(entry.Uninstall, @"-p\s+([\w.\-]+)");
                        string folder = EntryFolder(entry);
                        if (!id.Success || folder.Length == 0) continue;
                        AddStoreGame(found, entry.Name, "Amazon Games", folder, string.Empty, $"amazon-games://play/{id.Groups[1].Value}", id.Groups[1].Value);
                        continue;
                    }

                    string? source = null;
                    if (Publisher(entry, "Wargaming") && !NameHas(entry, "Game Center", "Wargaming.net")) source = "Wargaming";
                    else if (Publisher(entry, "miHoYo", "COGNOSPHERE", "HoYoverse") && !NameHas(entry, "HoYoPlay")) source = "HoYoPlay";
                    else if (entry.Uninstall.Contains("PlariumPlay", StringComparison.OrdinalIgnoreCase) && !NameHas(entry, "Plarium Play")) source = "Plarium Play";
                    else if (Publisher(entry, "Legacy Games") && !NameHas(entry, "Launcher")) source = "Legacy Games";
                    else if (Publisher(entry, "Big Fish") && !NameHas(entry, "Game Manager")) source = "Big Fish";
                    if (source == null) continue;

                    string gameFolder = EntryFolder(entry);
                    if (gameFolder.Length == 0) continue;
                    AddStoreGame(found, entry.Name, source, gameFolder, string.Empty);
                }
                catch { }
            }
        }

        /// <summary>Ordner, in denen die itch-App Spiele ablegt (Standard und eigene Installationsorte).</summary>
        private static List<string> ItchRoots()
        {
            string itch = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "itch");
            var roots = new List<string> { System.IO.Path.Combine(itch, "apps") };

            try
            {
                string prefs = System.IO.Path.Combine(itch, "preferences.json");
                if (File.Exists(prefs))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(prefs));
                    if (doc.RootElement.TryGetProperty("installLocations", out var locations) && locations.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var location in locations.EnumerateObject())
                        {
                            string path = location.Value.ValueKind == JsonValueKind.Object ? GetJsonString(location.Value, "path")
                                : location.Value.ValueKind == JsonValueKind.String ? location.Value.GetString() ?? string.Empty : string.Empty;
                            if (path.Length == 0) continue;
                            roots.Add(path);
                            roots.Add(System.IO.Path.Combine(path, "apps"));
                        }
                    }
                }
            }
            catch { }

            return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>itch.io: Jeder Spiel-Ordner hat eine Quittung (.itch\receipt.json.gz) mit dem Titel.</summary>
        private static void ScanItchGames(Dictionary<string, GameItem> found)
        {
            foreach (string root in ItchRoots())
            {
                try
                {
                    foreach (string folder in Directory.GetDirectories(root))
                    {
                        try
                        {
                            string receipt = System.IO.Path.Combine(folder, ".itch", "receipt.json.gz");
                            if (!File.Exists(receipt)) continue;

                            string title = string.Empty;
                            using (var file = File.OpenRead(receipt))
                            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                            using (var doc = JsonDocument.Parse(gzip))
                            {
                                if (doc.RootElement.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Object)
                                    title = GetJsonString(game, "title");
                            }

                            AddStoreGame(found, title.Length > 0 ? title : FolderTitle(folder), "itch.io", folder, string.Empty);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private static string HeroicDir()
            => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic");

        private static string[] HeroicFiles()
        {
            string heroic = HeroicDir();
            return new[]
            {
                System.IO.Path.Combine(heroic, "legendaryConfig", "legendary", "installed.json"),
                System.IO.Path.Combine(heroic, "gog_store", "installed.json"),
                System.IO.Path.Combine(heroic, "nile_config", "nile", "installed.json")
            };
        }

        /// <summary>Heroic Games Launcher (Epic, GOG und Amazon über Heroic).</summary>
        private static void ScanHeroicGames(Dictionary<string, GameItem> found)
        {
            var files = HeroicFiles();

            // Epic über Legendary: { "AppName": { "title", "install_path", "executable", "is_dlc" } }
            try
            {
                if (File.Exists(files[0]))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(files[0]));
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in doc.RootElement.EnumerateObject())
                        {
                            var game = property.Value;
                            if (game.ValueKind != JsonValueKind.Object) continue;
                            if (game.TryGetProperty("is_dlc", out var dlc) && dlc.ValueKind == JsonValueKind.True) continue;

                            string app = GetJsonString(game, "app_name");
                            if (app.Length == 0) app = property.Name;
                            string folder = GetJsonString(game, "install_path");
                            string exe = GetJsonString(game, "executable");
                            if (folder.Length == 0 || !Directory.Exists(folder)) continue;

                            string title = GetJsonString(game, "title");
                            AddStoreGame(found, title.Length > 0 ? title : FolderTitle(folder), "Heroic", folder,
                                exe.Length > 0 ? System.IO.Path.Combine(folder, exe) : string.Empty, $"heroic://launch/legendary/{app}", app);
                        }
                    }
                }
            }
            catch { }

            // GOG: { "installed": [ { "appName", "install_path", "platform", "is_dlc" } ] }
            try
            {
                if (File.Exists(files[1]))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(files[1]));
                    if (doc.RootElement.TryGetProperty("installed", out var installed) && installed.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var game in installed.EnumerateArray())
                        {
                            if (game.ValueKind != JsonValueKind.Object) continue;
                            if (game.TryGetProperty("is_dlc", out var dlc) && dlc.ValueKind == JsonValueKind.True) continue;
                            string platform = GetJsonString(game, "platform");
                            if (platform.Length > 0 && !platform.Equals("windows", StringComparison.OrdinalIgnoreCase)) continue;

                            string app = GetJsonString(game, "appName");
                            string folder = GetJsonString(game, "install_path");
                            if (app.Length == 0 || folder.Length == 0 || !Directory.Exists(folder)) continue;
                            AddStoreGame(found, FolderTitle(folder), "Heroic", folder, string.Empty, $"heroic://launch/gog/{app}", app);
                        }
                    }
                }
            }
            catch { }

            // Amazon über Nile: [ { "id", "path" } ]
            try
            {
                if (File.Exists(files[2]))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(files[2]));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var game in doc.RootElement.EnumerateArray())
                        {
                            if (game.ValueKind != JsonValueKind.Object) continue;
                            string id = GetJsonString(game, "id");
                            string folder = GetJsonString(game, "path");
                            if (id.Length == 0 || folder.Length == 0 || !Directory.Exists(folder)) continue;
                            AddStoreGame(found, FolderTitle(folder), "Heroic", folder, string.Empty, $"heroic://launch/nile/{id}", id);
                        }
                    }
                }
            }
            catch { }
        }

        private static string HumbleConfig()
            => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Humble App", "config.json");

        /// <summary>Humble App: In der config.json stehen die geladenen Spiele mit Ordner und .exe.</summary>
        private static void ScanHumbleGames(Dictionary<string, GameItem> found)
        {
            try
            {
                string file = HumbleConfig();
                if (!File.Exists(file)) return;

                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var game in property.Value.EnumerateArray())
                    {
                        if (game.ValueKind != JsonValueKind.Object) continue;
                        string name = GetJsonString(game, "gameName");
                        string folder = GetJsonString(game, "filePath");
                        if (name.Length == 0 || folder.Length == 0 || !Directory.Exists(folder)) continue;

                        string exe = GetJsonString(game, "executablePath");
                        if (exe.Length > 0 && !System.IO.Path.IsPathRooted(exe)) exe = System.IO.Path.Combine(folder, exe);
                        AddStoreGame(found, name, "Humble", folder, exe);
                    }
                }
            }
            catch { }
        }

        /// <summary>Ordner, die selbst Bibliotheken anderer Launcher sind, sind keine Spiele.</summary>
        private static readonly string[] LibraryFolderNames =
        {
            "SteamLibrary", "steamapps", "Steam", "Epic Games", "XboxGames", "WindowsApps", "GOG Galaxy", "GOG Games",
            "EA Games", "Origin Games", "Ubisoft", "Ubisoft Game Launcher", "Riot Games", "Battle.net", "Amazon Games", "itch", "Heroic"
        };

        /// <summary>Eigene Spiele-Ordner: Jeder Unterordner mit einer passenden .exe ist ein Spiel (für alles, was kein Launcher meldet).</summary>
        private static void ScanGameFolders(Dictionary<string, GameItem> found, List<string> folders)
        {
            var knownDirs = found.Values.Select(g => g.InstallDir).Where(d => !string.IsNullOrEmpty(d))
                .Select(d => System.IO.Path.GetFullPath(d).TrimEnd('\\', '/') + "\\").ToList();

            foreach (string root in folders)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string folder in Directory.GetDirectories(root))
                    {
                        try
                        {
                            string name = FolderTitle(folder);
                            if (name.StartsWith('.') || LibraryFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                            if (Directory.Exists(System.IO.Path.Combine(folder, "steamapps"))) continue;

                            // Schon über einen Launcher gefunden (gleicher Ordner, darüber oder darunter)
                            string full = System.IO.Path.GetFullPath(folder).TrimEnd('\\', '/') + "\\";
                            if (knownDirs.Any(d => d.StartsWith(full, StringComparison.OrdinalIgnoreCase) || full.StartsWith(d, StringComparison.OrdinalIgnoreCase))) continue;

                            AddStoreGame(found, name, FolderSource, folder, string.Empty);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // ───────── Neue Spiele sofort erkennen ─────────

        private readonly DispatcherTimer newGamesTimer = new() { Interval = TimeSpan.FromSeconds(20) };
        private int? libraryFingerprint;
        private bool newGamesBusy;

        /// <summary>
        /// Kurzer „Fingerabdruck“ aller Stellen, an denen Launcher installierte Spiele eintragen.
        /// Ändert er sich, wird neu gescannt. Läuft im Hintergrund und liest nur Namen und kleine Dateien.
        /// </summary>
        private static int ComputeLibraryFingerprint(List<string> gameFolders)
        {
            var text = new StringBuilder();

            void SubKeys(RegistryKey hive, string path)
            {
                try
                {
                    using var key = hive.OpenSubKey(path);
                    if (key == null) return;
                    text.Append('|').Append(path).Append(':');
                    foreach (string name in key.GetSubKeyNames()) text.Append(name).Append(',');
                }
                catch { }
            }

            void SubDirs(string path)
            {
                try
                {
                    if (!Directory.Exists(path)) return;
                    text.Append('|').Append(path).Append(':');
                    foreach (string dir in Directory.GetDirectories(path)) text.Append(System.IO.Path.GetFileName(dir)).Append(',');
                }
                catch { }
            }

            void FileStamp(string path)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists) text.Append('|').Append(path).Append(':').Append(info.Length);
                }
                catch { }
            }

            // Steam: Manifeste und ob das Spiel fertig installiert ist (nicht die Uhrzeit, die ändert sich bei jedem Start)
            foreach (string library in SteamLibraryFolders())
            {
                try
                {
                    foreach (string acf in Directory.EnumerateFiles(library, "appmanifest_*.acf"))
                    {
                        text.Append(System.IO.Path.GetFileName(acf));
                        try { text.Append((ReadAcfNumber(File.ReadAllText(acf), "StateFlags") & 4) != 0 ? "+" : "-"); }
                        catch { }
                    }
                }
                catch { }
            }

            // Epic Games: Manifeste und ob die Installation fertig ist
            string epic = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            try
            {
                if (Directory.Exists(epic))
                {
                    foreach (string item in Directory.EnumerateFiles(epic, "*.item"))
                    {
                        text.Append(System.IO.Path.GetFileName(item));
                        try { text.Append(Regex.IsMatch(File.ReadAllText(item), "\"bIsIncompleteInstall\"\\s*:\\s*true") ? "-" : "+"); }
                        catch { }
                    }
                }
            }
            catch { }

            // Programmliste, Xbox-Pakete und die Registry-Zweige der Stores
            SubKeys(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            SubKeys(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            SubKeys(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            SubKeys(Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            SubKeys(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\Games");
            SubKeys(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
            SubKeys(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\EA Games");
            SubKeys(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Rockstar Games");

            // Ordner-basierte Launcher und eigene Spiele-Ordner
            foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string programs = Environment.GetFolderPath(special);
                if (programs.Length == 0) continue;
                SubDirs(System.IO.Path.Combine(programs, "EA Games"));
                SubDirs(System.IO.Path.Combine(programs, "Origin Games"));
            }
            foreach (string root in ItchRoots()) SubDirs(root);
            foreach (string folder in gameFolders) SubDirs(folder);
            foreach (string file in HeroicFiles()) FileStamp(file);
            FileStamp(HumbleConfig());

            return text.ToString().GetHashCode();
        }

        private async Task CheckForNewGamesAsync()
        {
            if (!settings.AutoDetectGames || newGamesBusy || isScanning) return;

            newGamesBusy = true;
            try
            {
                var folders = settings.GameFolders.ToList();
                int current = await Task.Run(() => ComputeLibraryFingerprint(folders));

                if (libraryFingerprint == null)
                {
                    libraryFingerprint = current;   // erster Durchlauf (oder nach einem Scan): nur merken
                    return;
                }
                if (current == libraryFingerprint) return;

                // Während eines Spiels oder im Controller-Modus nicht scannen: beim nächsten Durchlauf erneut versuchen
                if (activeSessions.Count > 0 || controllerMode || isScanning) return;

                var before = allGames.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                silentRescan = true;
                try { await LoadInstalledGamesAsync(); }
                finally { silentRescan = false; }
                libraryFingerprint = current;

                var added = allGames.Where(g => !g.Hidden && !before.Contains(g.Name))
                    // Fertige Downloads von Steam und Epic melden sich schon selbst
                    .Where(g => !(settings.DownloadDoneNotify && (g.Source == "Steam" || g.Source == "Epic Games")))
                    .ToList();
                if (added.Count > 0) NotifyNewGames(added);
            }
            catch { }
            finally
            {
                newGamesBusy = false;
            }
        }

        private void NotifyNewGames(List<GameItem> added)
        {
            string title = added.Count == 1 ? Loc.T("Neues Spiel erkannt") : Loc.T($"{added.Count} neue Spiele erkannt");
            string text = added.Count == 1
                ? $"{added[0].Name} ({Loc.T(added[0].Source)})"
                : string.Join(", ", added.Take(3).Select(g => g.Name)) + (added.Count > 3 ? " …" : string.Empty);

            if (IsVisible && WindowState != WindowState.Minimized)
                ShowToast("🆕", title, text, 8, () => NavigateTo("games"));
            else if (trayIcon != null && trayIcon.Visible && !AlertsMuted())
                trayIcon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info);
        }

        private void ChkAutoDetectGames_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            settings.AutoDetectGames = ChkAutoDetectGames.IsChecked == true;
            libraryFingerprint = null;
            SaveSettings();
        }

#if DEBUG
        private void AddLauncherDevButtons(Action<string> group, Action<string, Action> dev)
        {
            group("Launcher, Reparieren und Platz schaffen");
            dev("🆕 Meldung „Neues Spiel erkannt“ (Test)", () =>
            {
                var sample = allGames.Where(g => !g.Hidden).Take(2).ToList();
                if (sample.Count > 0) NotifyNewGames(sample);
            });
            dev("🔍 Neue Spiele jetzt prüfen (Fingerabdruck)", async () =>
            {
                int before = libraryFingerprint ?? 0;
                int now = await Task.Run(() => ComputeLibraryFingerprint(settings.GameFolders.ToList()));
                Msg($"Gemerkt: {before}\nJetzt: {now}\nGeändert: {(before != now ? "ja" : "nein")}", "Entwickler");
            });
            dev("🔄 Nächsten Durchlauf erzwingen (wie neues Spiel)", () => libraryFingerprint = (libraryFingerprint ?? 0) + 1);
            dev("📋 Gefundene Launcher und Anzahl", () =>
            {
                var counts = LauncherCounts();
                Msg(string.Join("\n", OrderLaunchers(KnownLaunchers.Concat(counts.Keys))
                    .Select(s => $"{s}: {(counts.TryGetValue(s, out int c) ? c : 0)} · exe {(LauncherExePath(s) != null ? "gefunden" : "–")}")), "Entwickler");
            });
            dev("🧹 Platz schaffen öffnen", () => ShowSpaceAssistant());
            dev("🛠 Reparieren: erstes Steam-Spiel", () =>
            {
                var game = allGames.FirstOrDefault(g => g.Source == "Steam");
                if (game != null) RepairGame(game);
            });
        }
#endif
    }
}
