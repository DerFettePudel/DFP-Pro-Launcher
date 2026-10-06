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
        // ═════════════════════════════ Streamer-Modus und Komfort-Einstellungen ═════════════════════════════

        private bool streamAutoOn;
        private bool streamAutoSuppressed;
        private bool lastStreamerState;
        private bool refreshingStreamer;
        private string settingsCategory = "general";
        private string limitNotifiedDay = string.Empty;
        private readonly DispatcherTimer streamTimer = new();
        private readonly Dictionary<GameItem, SessionStats> sessionStats = new();

        private static readonly string[] StreamProcesses =
            { "obs64", "obs32", "obs", "streamlabs obs", "slobs", "Streamlabs Desktop", "Streamlabs", "TwitchStudio", "Twitch Studio", "XSplit.Core", "XSplit.Gamecaster",
              "vMix64", "vMix", "prismlive", "PRISMLiveStudio", "Wirecast", "TikTok LIVE Studio", "Medal", "Meld", "StreamElements" };

        private sealed class SessionStats
        {
            public DateTime Started = DateTime.Now;
            public int Samples;
            public bool HasGpu;
            public double CpuSum, CpuMax, GpuSum, GpuMax, RamSum, RamMax;
        }

        private bool StreamerOn => settings.StreamerMode || streamAutoOn || obsAutoOn;
        private bool SHide(bool option) => StreamerOn && option;
        private string DisplayUserName() => SHide(settings.StreamHideName) ? "Gamer" : settings.UserName;

        private bool AlertsMuted()
            => (StreamerOn && settings.StreamMuteAlerts) || (settings.MuteWhilePlaying && activeSessions.Count > 0);

        private void InitExtras7()
        {
            streamTimer.Interval = TimeSpan.FromSeconds(5);
            streamTimer.Tick += async (s, e) => await CheckStreamingSoftwareAsync();
            streamTimer.Start();

            lastStreamerState = StreamerOn;
            ApplyStreamerMode();
            BuildAlphaBar();
            UpdateMemoryText();

            PreviewTextInput += OnTypeToSearch;
        }

        // ───────────────────────────── Streamer-Modus ─────────────────────────────

        private static bool IsProcessRunning(string name)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                bool any = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                return any;
            }
            catch
            {
                return false;
            }
        }

        private async Task CheckStreamingSoftwareAsync()
        {
            if (!settings.StreamerAuto)
            {
                streamAutoSuppressed = false;
                if (streamAutoOn)
                {
                    streamAutoOn = false;
                    ApplyStreamerMode();
                }
                return;
            }

            bool running = await Task.Run(() => StreamProcesses.Any(IsProcessRunning));

            if (running && !streamAutoOn && !streamAutoSuppressed && !settings.StreamerMode)
            {
                ShowToast("📡", "Streamer-Modus aktiv", "Eine Streaming-Software läuft. Private Angaben sind verborgen.", 6);
                streamAutoOn = true;
                ApplyStreamerMode();
            }
            else if (!running)
            {
                streamAutoSuppressed = false;
                if (streamAutoOn)
                {
                    streamAutoOn = false;
                    ApplyStreamerMode();
                }
            }
        }

        private void ToggleStreamerMode()
        {
            bool turnOn = !StreamerOn;

            if (turnOn)
            {
                ShowToast("📡", "Streamer-Modus aktiv", "Private Angaben sind jetzt verborgen.", 4);
                settings.StreamerMode = true;
            }
            else
            {
                if (streamAutoOn) streamAutoSuppressed = true;
                streamAutoOn = false;
                if (obsAutoOn) obsAutoSuppressed = true;   // bleibt aus, bis der Stream endet oder die Szene wechselt
                obsAutoOn = false;
                settings.StreamerMode = false;
            }

            SaveSettings();
            ApplyStreamerMode();

            if (!turnOn) ShowToast("📡", "Streamer-Modus aus", "Alle Angaben sind wieder sichtbar.", 4);
        }

        private void StreamBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ToggleStreamerMode();

        private void ChkStreamer_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            bool wanted = ChkStreamer.IsChecked == true;
            if (wanted == settings.StreamerMode) return;

            settings.StreamerMode = wanted;
            if (!wanted && streamAutoOn)
            {
                streamAutoSuppressed = true;
                streamAutoOn = false;
            }
            if (!wanted && obsAutoOn)
            {
                obsAutoSuppressed = true;
                obsAutoOn = false;
            }
            SaveSettings();
            ApplyStreamerMode();
        }

        private void ApplyStreamerMode()
        {
            if (StreamBadge == null) return;

            bool on = StreamerOn;

            ApplyCensorCategories(on);
            if (on && settings.StreamHideMusic && MusicPanel.Visibility == Visibility.Visible) MusicPanel.Visibility = Visibility.Collapsed;

            if (on) StartCensorTimer();
            else StopCensorTimer();

            StreamBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            TxtStreamBadge.Text = settings.StreamerMode ? Loc.T("📡 Streamer-Modus") : Loc.T("📡 Streamer-Modus (automatisch)");

            bool previous = isLoadingSettings;
            isLoadingSettings = true;
            try { ChkStreamer.IsChecked = settings.StreamerMode; }
            finally { isLoadingSettings = previous; }

            if (lastStreamerState != on && !refreshingStreamer)
            {
                lastStreamerState = on;
                refreshingStreamer = true;
                try
                {
                    TxtUserName.Text = DisplayUserName();
                    ApplyFilter();
                    RefreshDashboard();
                    RenderSidebarExtras();
                }
                finally
                {
                    refreshingStreamer = false;
                }
            }
        }

        // ───────────────────────────── Streamer-Seite ─────────────────────────────

        private static readonly Regex StreamerAppPattern = new(
            @"\bobs\b|obs studio|streamlabs|twitch studio|xsplit|vmix|prism live|elgato|stream ?deck|voicemeeter|wirecast|tiktok live|restream|streamelements|medal|nvidia broadcast|meld studio|camera hub",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private void NavStreamer_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewStreamer);
            RenderStreamerPage();
        }

        private void BtnStreamerToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleStreamerMode();
            RenderStreamerPage();
        }

        private void RenderStreamerPage()
        {
            if (StreamerAppsPanel == null) return;

            bool on = StreamerOn;
            TxtStreamerState.Text = on
                ? (settings.StreamerMode ? Loc.T("Aktiv (manuell)")
                   : obsAutoOn ? Loc.T("Aktiv (automatisch durch OBS)")
                   : Loc.T("Aktiv (automatisch, weil eine Streaming-Software läuft)"))
                : Loc.T("Aus");
            TxtStreamerState.Foreground = MakeBrush(on ? "#34D399" : "#9CA3AF");
            BtnStreamerToggle.Content = Loc.T(on ? "Ausschalten" : "Einschalten");

            // Programme (erkannte und eigene, bearbeitbar)
            RenderStreamerApps();

            // Schnellzugriffe (frei bearbeitbar)
            RenderStreamerLinks();

            // Countdown und Checkliste
            RenderGoLive();
            RenderStreamerChecklist(StreamProcesses.Any(IsProcessRunning));
        }

        private void RenderStreamerChecklist(bool softwareRunning)
        {
            StreamerChecklist.Children.Clear();
            void Check(bool ok, string text, string? fixText = null, Action? fix = null)
            {
                var line = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                line.Children.Add(new TextBlock
                {
                    Text = ok ? "✓" : "⚠",
                    Foreground = MakeBrush(ok ? "#34D399" : "#F59E0B"),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                });
                var label = new TextBlock { Text = Loc.T(text), Foreground = MakeBrush("#D1D5DB"), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                Grid.SetColumn(label, 1);
                line.Children.Add(label);

                if (!ok && fix != null && fixText != null)
                {
                    var button = new System.Windows.Controls.Button { Content = Loc.T(fixText), Padding = new Thickness(14, 5, 14, 5) };
                    button.Click += (s, e) =>
                    {
                        fix();
                        RenderStreamerPage();
                    };
                    Grid.SetColumn(button, 2);
                    line.Children.Add(button);
                }

                StreamerChecklist.Children.Add(line);
            }

            Check(softwareRunning, "Eine Streaming-Software läuft");
            if (settings.ObsEnabled || obsSimulated)
                Check(ObsActive, "Der Launcher ist mit OBS verbunden", "Verbinden", RestartObs);
            Check(StreamerOn, "Der Streamer-Modus ist an", "Einschalten", () =>
            {
                if (!StreamerOn) ToggleStreamerMode();
            });
            Check(settings.StreamMuteAlerts, "Hinweis-Meldungen sind stumm", "Stumm schalten", () =>
            {
                settings.StreamMuteAlerts = true;
                SaveSettings();
            });
            Check(settings.StreamHideName, "Dein Name wird verborgen", "Verbergen", () =>
            {
                settings.StreamHideName = true;
                SaveSettings();
                ApplyStreamerMode();
            });
            Check(settings.StreamHideMusic, "Der Musik-Player bleibt verborgen", "Verbergen", () =>
            {
                settings.StreamHideMusic = true;
                SaveSettings();
                ApplyStreamerMode();
            });
            if (CaptureExclusionSupported)
                Check(settings.CaptureExclude, "Der Launcher ist in Aufnahmen unsichtbar", "Unsichtbar machen", () => SetCaptureExclude(true));
        }

        private void UpdateStreamerToggles()
        {
            if (ChkStreamerPage == null) return;

            bool before = isLoadingSettings;
            isLoadingSettings = true;
            try
            {
                ChkStreamerPage.IsChecked = !settings.HiddenSections.Contains("streamer");
                ChkStreamHideMusic.IsChecked = settings.StreamHideMusic;
                ChkStreamHidePc.IsChecked = settings.StreamHidePc;
                ChipCensorBar.IsChecked = settings.StreamCensorStyle != "blur";
                ChipCensorBlur.IsChecked = settings.StreamCensorStyle == "blur";
            }
            finally
            {
                isLoadingSettings = before;
            }
        }

        private void ChkStreamerPage_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            SetSectionVisible("streamer", ChkStreamerPage.IsChecked == true);
        }

        private void ChkStreamHideMusic_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            settings.StreamHideMusic = ChkStreamHideMusic.IsChecked == true;
            SaveSettings();
            ApplyStreamerMode();
        }

        // ───────────────────────────── Streamer-Links bearbeiten ─────────────────────────────

        private void SeedStreamerLinks()
        {
            if (settings.StreamerLinksSeeded) return;

            settings.StreamerLinksSeeded = true;
            settings.StreamerLinks = new List<QuickLink>
            {
                new() { Name = "Twitch Dashboard", Target = "https://dashboard.twitch.tv", Icon = "🟣" },
                new() { Name = "YouTube Studio", Target = "https://studio.youtube.com", Icon = "▶" },
                new() { Name = "TikTok LIVE Center", Target = "https://livecenter.tiktok.com", Icon = "🎵" },
                new() { Name = "Kick Dashboard", Target = "https://dashboard.kick.com", Icon = "🟢" },
                new() { Name = "StreamElements", Target = "https://streamelements.com/dashboard", Icon = "🛠" },
                new() { Name = "Streamlabs", Target = "https://streamlabs.com/dashboard", Icon = "📈" }
            };
            SaveSettings();
        }

        private void RenderStreamerLinks()
        {
            SeedStreamerLinks();
            StreamerLinksPanel.Children.Clear();

            foreach (var link in settings.StreamerLinks.ToList())
            {
                var item = link;
                var menu = CreateMenu();
                AddMenuItem(menu, "✏  Bearbeiten ...", () => EditQuickLink(item, settings.StreamerLinks, RenderStreamerPage));
                AddMenuItem(menu, "◀  Nach vorn", () => MoveStreamerLink(item, -1));
                AddMenuItem(menu, "▶  Nach hinten", () => MoveStreamerLink(item, 1));
                AddMenuItem(menu, "🗑  Entfernen", () => RemoveStreamerLink(item));

                StreamerLinksPanel.Children.Add(CreateTile(QuickEmoji(item), ResolveQuickIcon(item), item.Name,
                    DescribeTarget(item.Target), () => OpenShell(item.Target), menu, 200));
            }

            var addMenu = CreateMenu();
            AddMenuItem(addMenu, "↺  Standard-Links wiederherstellen", ResetStreamerLinks);
            StreamerLinksPanel.Children.Add(CreateTile("＋", null, "Link hinzufügen", "Webseite, Programm oder Ordner",
                () => EditQuickLink(null, settings.StreamerLinks, RenderStreamerPage), addMenu, 200));
        }

        private void MoveStreamerLink(QuickLink link, int delta)
        {
            int index = settings.StreamerLinks.IndexOf(link);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= settings.StreamerLinks.Count) return;

            settings.StreamerLinks.RemoveAt(index);
            settings.StreamerLinks.Insert(target, link);
            SaveSettings();
            RenderStreamerPage();
        }

        private void RemoveStreamerLink(QuickLink link)
        {
            settings.StreamerLinks.Remove(link);
            SaveSettings();
            RenderStreamerPage();
        }

        private void ResetStreamerLinks()
        {
            var answer = Msg("Alle Streamer-Links durch die Standard-Auswahl ersetzen?", "Streamer", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            settings.StreamerLinksSeeded = false;
            settings.StreamerLinks = new List<QuickLink>();
            RenderStreamerPage();
        }

        // ───────────────────────────── Zensur im Streamer-Modus (Balken oder verschwommen) ─────────────────────────────

        private readonly Dictionary<FrameworkElement, CensorAdorner> censorAdorners = new();
        private readonly HashSet<FrameworkElement> censorBlurred = new();
        private readonly HashSet<FrameworkElement> censorScrubbed = new();
        private DispatcherTimer? censorTimer;

        private void SetCensor(FrameworkElement element, bool on)
        {
            if (!on)
            {
                RemoveCensor(element);
                return;
            }

            if (settings.StreamCensorStyle == "blur")
            {
                RemoveCensorAdorner(element);
                if (censorBlurred.Add(element)) element.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 16 };
                return;
            }

            if (censorBlurred.Remove(element)) element.Effect = null;
            if (censorAdorners.ContainsKey(element)) return;

            var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(element);
            if (layer == null) return;

            var adorner = new CensorAdorner(element, TryFindResource("AccentBrush") as System.Windows.Media.Brush);
            layer.Add(adorner);
            censorAdorners[element] = adorner;
        }

        private void RemoveCensor(FrameworkElement element)
        {
            RemoveCensorAdorner(element);
            if (censorBlurred.Remove(element)) element.Effect = null;
        }

        private void RemoveCensorAdorner(FrameworkElement element)
        {
            if (!censorAdorners.Remove(element, out var adorner)) return;
            System.Windows.Documents.AdornerLayer.GetAdornerLayer(element)?.Remove(adorner);
        }

        private void ClearAllCensors()
        {
            foreach (var element in censorAdorners.Keys.ToList()) RemoveCensorAdorner(element);
            foreach (var element in censorBlurred.ToList())
            {
                element.Effect = null;
                censorBlurred.Remove(element);
            }
            censorScrubbed.Clear();
        }

        private void ApplyCensorCategories(bool on)
        {
            void Censor(bool option, params FrameworkElement?[] items)
            {
                bool hide = on && option;
                foreach (var item in items)
                {
                    if (item != null) SetCensor(item, hide);
                }
            }

            Censor(settings.StreamHideName, TxtSettingsUserName);
            Censor(settings.StreamHideLocation, WeatherCard, TxtWeatherCity);
            Censor(settings.StreamHideStats, StatsGrid, goalWidget);
            Censor(settings.StreamHideRecent, HeroHost, RecentSection, FavSection);
            Censor(settings.StreamHidePaths, GridDownloads, AppsContainer, DrivesControl, GameSizesControl, TxtSgdbKey, TxtCloudFolder, TxtUpdateRepo, TxtDiscordWebhook, quickWidget, TxtObsHost);
            Censor(settings.StreamHideShots, GalleryContainer, WishContainer, wishWidget);

            foreach (var adorner in censorAdorners.Values) adorner.InvalidateVisual();
        }

        private List<string> SensitiveTokens()
        {
            var tokens = new List<string>();
            void Add(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 3 && !tokens.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
                    tokens.Add(value.Trim());
            }

            if (settings.StreamHidePc)
            {
                Add(Environment.MachineName);
                Add(Environment.UserName);
            }
            if (settings.StreamHideName) Add(settings.UserName);
            return tokens;
        }

        private void CollectSensitive(DependencyObject node, List<string> tokens, HashSet<FrameworkElement> found)
        {
            if (node is UIElement ui && !ui.IsVisible) return;
            if (ReferenceEquals(node, MusicPanel) || ReferenceEquals(node, GamesContainer) || ReferenceEquals(node, GamesListContainer) || ReferenceEquals(node, GalleryContainer)) return;

            bool Match(string? text) => !string.IsNullOrEmpty(text) && tokens.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

            if (node is TextBlock block)
            {
                if (Match(block.Text)) found.Add(block);
                return;
            }
            if (node is System.Windows.Controls.TextBox box)
            {
                if (Match(box.Text)) found.Add(box);
                return;
            }
            if (node is ContentControl { Content: string content } control && Match(content))
            {
                found.Add(control);
                return;
            }

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) CollectSensitive(VisualTreeHelper.GetChild(node, i), tokens, found);
        }

        /// <summary>Sucht überall im Launcher nach Computername, Benutzername und Co. und zensiert genau diese Textstellen.</summary>
        private void ScrubSensitiveText()
        {
            var found = new HashSet<FrameworkElement>();
            if (StreamerOn)
            {
                var tokens = SensitiveTokens();
                if (tokens.Count > 0)
                {
                    CollectSensitive(WindowRoot, tokens, found);

                    // Auch Dialogfenster (CreateDialog, Meldungen) erfassen
                    foreach (var window in CensorableDialogs())
                    {
                        if (window.Content is DependencyObject root) CollectSensitive(root, tokens, found);
                    }
                }
            }

            foreach (var element in censorScrubbed.Where(e => !found.Contains(e)).ToList())
            {
                censorScrubbed.Remove(element);
                RemoveCensor(element);
            }
            foreach (var element in found)
            {
                censorScrubbed.Add(element);
                SetCensor(element, true);
            }
        }

        private void StartCensorTimer()
        {
            if (censorTimer == null)
            {
                censorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                censorTimer.Tick += (s, e) =>
                {
                    if (!StreamerOn) return;
                    ApplyCensorCategories(true);
                    ScrubSensitiveText();
                };
            }

            if (!censorTimer.IsEnabled) censorTimer.Start();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyCensorCategories(StreamerOn);
                ScrubSensitiveText();
            }), DispatcherPriority.Background);
        }

        private void StopCensorTimer()
        {
            censorTimer?.Stop();
            ClearAllCensors();
        }

        private void CensorStyle_Checked(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings || sender is not System.Windows.Controls.RadioButton { Tag: string tag }) return;

            settings.StreamCensorStyle = tag == "blur" ? "blur" : "bar";
            SaveSettings();

            ClearAllCensors();
            if (StreamerOn) StartCensorTimer();
        }

        private void ChkStreamHidePc_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            settings.StreamHidePc = ChkStreamHidePc.IsChecked == true;
            SaveSettings();
            if (StreamerOn) ScrubSensitiveText();
        }

        // ───────────────────────────── Streamer: Programme bearbeiten und entfernen ─────────────────────────────

        private static string DescribeStreamerTarget(QuickButton button)
            => button.ExePath.Length > 0 ? button.ExePath : button.AppId.Length > 0 ? button.AppId : button.Path;

        private void RenderStreamerApps()
        {
            StreamerAppsPanel.Children.Clear();

            var detected = cachedApps
                .Where(a => StreamerAppPattern.IsMatch(a.Name) && !settings.StreamerHiddenApps.Contains(a.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var app in detected)
            {
                var entry = app;
                var tile = CreateAppTile(entry);

                var menu = CreateMenu();
                AddMenuItem(menu, "▶  Starten", () => LaunchApp(entry));
                AddMenuItem(menu, "✏  Bearbeiten ...", () => EditDetectedStreamerApp(entry));
                AddMenuItem(menu, "🗑  Aus der Liste entfernen", () =>
                {
                    settings.StreamerHiddenApps.Add(entry.Name);
                    SaveSettings();
                    RenderStreamerPage();
                });
                tile.ContextMenu = menu;
                StreamerAppsPanel.Children.Add(tile);
            }

            foreach (var custom in settings.StreamerApps.ToList())
            {
                var button = custom;
                var entry = new AppEntry
                {
                    Name = button.Name,
                    AppId = button.AppId,
                    ExePath = button.ExePath,
                    Icon = QuickIcon(button),
                    Category = "Medien",
                    IsManual = true
                };
                var tile = CreateAppTile(entry);

                var menu = CreateMenu();
                AddMenuItem(menu, "▶  Starten", () => LaunchApp(entry));
                AddMenuItem(menu, "✏  Bearbeiten ...", () => EditStreamerApp(button));
                AddMenuItem(menu, "🗑  Aus der Liste entfernen", () =>
                {
                    settings.StreamerApps.Remove(button);
                    SaveSettings();
                    RenderStreamerPage();
                });
                tile.ContextMenu = menu;
                StreamerAppsPanel.Children.Add(tile);
            }

            var addMenu = CreateMenu();
            if (settings.StreamerHiddenApps.Count > 0)
            {
                AddMenuItem(addMenu, "↺  Ausgeblendete Programme wieder anzeigen", () =>
                {
                    settings.StreamerHiddenApps.Clear();
                    SaveSettings();
                    RenderStreamerPage();
                });
            }

            var add = new Border
            {
                Width = 150,
                Height = 150,
                Margin = new Thickness(0, 0, 12, 12),
                CornerRadius = new CornerRadius(14),
                Background = BrushCardBg,
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = Loc.T("Programm hinzufügen"),
                ContextMenu = addMenu.Items.Count > 0 ? addMenu : null,
                Child = new TextBlock
                {
                    Text = "＋",
                    FontSize = 34,
                    Foreground = BrushSubtle,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            };
            add.MouseEnter += (s, e) => add.Background = BrushTileHover;
            add.MouseLeave += (s, e) => add.Background = BrushCardBg;
            add.MouseLeftButtonUp += (s, e) =>
            {
                var picked = ShowAppPicker(Loc.T("Streaming-Programm"));
                if (picked == null) return;

                var (path, name, appId, exePath) = picked.Value;
                settings.StreamerApps.Add(new QuickButton { Name = name, Path = path, AppId = appId, ExePath = exePath });
                SaveSettings();
                RenderStreamerPage();
            };
            StreamerAppsPanel.Children.Add(add);

            int shown = detected.Count;
            TxtStreamerAppsHint.Text = shown == 0
                ? Loc.T("Es wurde noch keine Streaming-Software erkannt. Füge deine Programme mit dem Plus hinzu.")
                : Loc.T($"{shown} Programme automatisch erkannt.") + "  " + Loc.T("Rechtsklick auf ein Programm bearbeitet oder entfernt es.");
        }

        private void EditDetectedStreamerApp(AppEntry app)
        {
            // Ein erkanntes Programm wird zu einem eigenen Eintrag, den man frei bearbeiten kann
            var copy = new QuickButton { Name = app.Name, Path = "app:" + app.Name, AppId = app.AppId, ExePath = app.ExePath };
            settings.StreamerApps.Add(copy);
            settings.StreamerHiddenApps.Add(app.Name);
            SaveSettings();
            EditStreamerApp(copy);
        }

        private void EditStreamerApp(QuickButton button)
        {
            var dialog = CreateDialog("Streaming-Programm bearbeiten", 540, out var panel);

            TextBlock Label(string text) => new() { Text = Loc.T(text), Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 6) };

            panel.Children.Add(Label("Name"));
            var name = new System.Windows.Controls.TextBox { Text = button.Name, Height = 38, Margin = new Thickness(0, 0, 0, 14) };
            panel.Children.Add(name);

            panel.Children.Add(Label("Programm"));
            string path = button.Path, appId = button.AppId, exePath = button.ExePath;
            var target = new TextBlock
            {
                Text = DescribeStreamerTarget(button),
                Foreground = System.Windows.Media.Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(target);

            var change = new System.Windows.Controls.Button
            {
                Content = Loc.T("Anderes Programm wählen ..."),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 22)
            };
            change.Click += (s, e) =>
            {
                var picked = ShowAppPicker(Loc.T("Streaming-Programm"));
                if (picked == null) return;

                var chosen = picked.Value;
                path = chosen.Path;
                appId = chosen.AppId;
                exePath = chosen.ExePath;
                target.Text = chosen.ExePath.Length > 0 ? chosen.ExePath : chosen.AppId.Length > 0 ? chosen.AppId : chosen.Path;
                if (name.Text.Trim().Length == 0 || name.Text == button.Name) name.Text = chosen.Name;
            };
            panel.Children.Add(change);

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var cancel = new System.Windows.Controls.Button { Content = Loc.T("Abbrechen"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var save = new System.Windows.Controls.Button { Content = Loc.T("Speichern"), Padding = new Thickness(26, 8, 26, 8), IsDefault = true };
            save.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            save.Click += (s, e) =>
            {
                if (name.Text.Trim().Length == 0) return;

                button.Name = name.Text.Trim();
                button.Path = path;
                button.AppId = appId;
                button.ExePath = exePath;
                SaveSettings();
                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            dialog.ShowDialog();
            RenderStreamerPage();
        }

        // ═════════════════════════════ OBS-Steuerung (OBS WebSocket v5) ═════════════════════════════

        private enum ObsState { Off, Connecting, Connected, Failed, Lost, Simulated }

        private sealed class ObsStatusSnapshot
        {
            public bool Streaming, Recording, RecordPaused;
            public long StreamMs, RecordMs, Skipped, Total;
        }

        private readonly ObsClient obs = new();
        private readonly SemaphoreSlim obsGate = new(1, 1);
        private readonly DispatcherTimer obsUiTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private DispatcherTimer? obsGraceTimer;
        private CancellationTokenSource? obsLoopCts;
        private ObsState obsState = ObsState.Off;
        private ObsConnectResult obsLastResult = ObsConnectResult.NotReachable;
        private bool obsSimulated;
        private bool obsStreaming, obsRecording, obsRecordPaused, obsMicMuted;
        private DateTime? obsStreamStart, obsRecordStart;
        private long obsRecordFrozenMs;
        private long obsSkipped, obsTotal;
        private string obsMicInput = string.Empty;
        private string obsCurrentScene = string.Empty;
        private List<string> obsScenes = new();
        private bool obsAutoOn, obsAutoSuppressed;

        private bool ObsActive => obsSimulated || obsState == ObsState.Connected;

        private void InitExtras16()
        {
            obs.EventReceived += (type, data) => Dispatcher.BeginInvoke(new Action(() => HandleObsEvent(type, data)));
            obs.Disconnected += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!obsSimulated && obsState == ObsState.Connected) SetObsState(ObsState.Lost);
            }));

            obsUiTimer.Tick += (s, e) =>
            {
                if (obsSimulated && obsStreaming)
                {
                    // Simulation: Bilder zählen und ab und zu eines verlieren
                    obsTotal += 60;
                    if (random.Next(8) == 0) obsSkipped += random.Next(1, 4);
                }
                UpdateObsTimes();
            };
            Closed += (s, e) =>
            {
                try { obsLoopCts?.Cancel(); } catch { }
                obs.Dispose();
            };

            SetObsState(ObsState.Off);
            RestartObs();
        }

        // ───────── Verbindung im Hintergrund, mit automatischer Wiederverbindung ─────────

        private void RestartObs()
        {
            try { obsLoopCts?.Cancel(); } catch { }
            obsLoopCts = null;
            if (obsSimulated) return;

            if (!settings.ObsEnabled)
            {
                _ = Task.Run(async () =>
                {
                    await obsGate.WaitAsync();
                    try { await obs.DisconnectAsync(); } finally { obsGate.Release(); }
                });
                ClearObsData();
                SetObsState(ObsState.Off);
                return;
            }

            var cts = new CancellationTokenSource();
            obsLoopCts = cts;
            string host = settings.ObsHost;
            int port = settings.ObsPort;
            string password = SecretStore.Unprotect(settings.ObsPasswordProtected);
            _ = Task.Run(() => ObsLoopAsync(host, port, password, cts.Token));
        }

        private void ObsUi(Action action) => Dispatcher.BeginInvoke(action);

        private async Task ObsLoopAsync(string host, int port, string password, CancellationToken token)
        {
            // Nur eine Schleife gleichzeitig: Die alte gibt die Verbindung frei, bevor die neue verbindet
            try { await obsGate.WaitAsync(token); }
            catch { return; }

            try
            {
                int delay = 3;
                while (!token.IsCancellationRequested)
                {
                    ObsUi(() => { if (!obsSimulated && obsState != ObsState.Lost) SetObsState(ObsState.Connecting); });

                    var result = await obs.ConnectAsync(host, port, password, token);
                    if (token.IsCancellationRequested) break;

                    if (result == ObsConnectResult.Connected)
                    {
                        delay = 3;
                        await ObsRefreshAllAsync();
                        ObsUi(() => { if (!obsSimulated) SetObsState(ObsState.Connected); });

                        // Status alle 2 Sekunden abfragen (Dauer, verlorene Bilder); Zustandswechsel kommen sofort als Ereignis
                        while (!token.IsCancellationRequested && obs.IsConnected)
                        {
                            try { await Task.Delay(2000, token); } catch { break; }
                            var status = await ObsReadStatusAsync();
                            if (status != null) ObsUi(() => ApplyObsStatus(status));
                        }
                        if (token.IsCancellationRequested) break;
                        ObsUi(() => { if (!obsSimulated) SetObsState(ObsState.Lost); });
                    }
                    else
                    {
                        var failed = result;
                        ObsUi(() =>
                        {
                            obsLastResult = failed;
                            if (!obsSimulated && obsState != ObsState.Lost) SetObsState(ObsState.Failed);
                        });
                        // Bei falschem Passwort seltener versuchen, damit OBS nicht dauernd Anmeldefehler meldet
                        if (failed is ObsConnectResult.AuthFailed or ObsConnectResult.PasswordRequired) delay = 60;
                    }

                    try { await Task.Delay(TimeSpan.FromSeconds(delay), token); } catch { break; }
                    delay = Math.Min(delay * 2, 30);
                }
            }
            catch { }
            finally
            {
                try { await obs.DisconnectAsync(); } catch { }
                obsGate.Release();
            }
        }

        private static string ObsStr(JsonElement? element, string name)
            => element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty : string.Empty;

        private static bool ObsBool(JsonElement? element, string name)
            => element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static long ObsLong(JsonElement? element, string name)
            => element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? (long)v.GetDouble() : 0;

        private async Task<ObsStatusSnapshot?> ObsReadStatusAsync()
        {
            var stream = await obs.RequestAsync("GetStreamStatus");
            var record = await obs.RequestAsync("GetRecordStatus");
            if (stream == null && record == null) return null;

            return new ObsStatusSnapshot
            {
                Streaming = ObsBool(stream, "outputActive"),
                StreamMs = ObsLong(stream, "outputDuration"),
                Skipped = ObsLong(stream, "outputSkippedFrames"),
                Total = ObsLong(stream, "outputTotalFrames"),
                Recording = ObsBool(record, "outputActive"),
                RecordPaused = ObsBool(record, "outputPaused"),
                RecordMs = ObsLong(record, "outputDuration")
            };
        }

        /// <summary>Lädt Szenen, Mikrofon und Status neu (im Hintergrund) und überträgt alles auf die Oberfläche.</summary>
        private async Task ObsRefreshAllAsync()
        {
            var sceneData = await obs.RequestAsync("GetSceneList");
            var scenes = new List<(long Index, string Name)>();
            if (sceneData is { ValueKind: JsonValueKind.Object } sd && sd.TryGetProperty("scenes", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    string name = ObsStr(item, "sceneName");
                    if (name.Length > 0) scenes.Add((ObsLong(item, "sceneIndex"), name));
                }
            }
            string current = ObsStr(sceneData, "currentProgramSceneName");

            var special = await obs.RequestAsync("GetSpecialInputs");
            string mic = ObsStr(special, "mic1");
            bool muted = false;
            if (mic.Length > 0) muted = ObsBool(await obs.RequestAsync("GetInputMute", new { inputName = mic }), "inputMuted");

            var status = await ObsReadStatusAsync();

            ObsUi(() =>
            {
                // OBS liefert die unterste Szene zuerst; wie in OBS von oben nach unten anzeigen
                obsScenes = scenes.OrderByDescending(s => s.Index).Select(s => s.Name).ToList();
                obsCurrentScene = current;
                obsMicInput = mic;
                obsMicMuted = muted;
                if (status != null) ApplyObsStatus(status);
                RenderObs();
                RenderObsSceneAutoList();
                UpdateObsAutoStreamer();
            });
        }

        private void ApplyObsStatus(ObsStatusSnapshot status)
        {
            if (obsSimulated) return;

            obsStreaming = status.Streaming;
            obsRecording = status.Recording;
            obsRecordPaused = status.RecordPaused;
            obsSkipped = status.Skipped;
            obsTotal = status.Total;

            // Startzeit aus der Dauer zurückrechnen, nur bei spürbarer Abweichung nachstellen (sonst springt die Anzeige)
            DateTime now = DateTime.Now;
            if (obsStreaming)
            {
                var start = now - TimeSpan.FromMilliseconds(status.StreamMs);
                if (obsStreamStart == null || Math.Abs((obsStreamStart.Value - start).TotalSeconds) > 2) obsStreamStart = start;
            }
            else obsStreamStart = null;

            obsRecordFrozenMs = status.RecordMs;
            if (obsRecording)
            {
                var start = now - TimeSpan.FromMilliseconds(status.RecordMs);
                if (obsRecordStart == null || Math.Abs((obsRecordStart.Value - start).TotalSeconds) > 2) obsRecordStart = start;
            }
            else obsRecordStart = null;

            RenderObs();
            UpdateObsAutoStreamer();
        }

        private void HandleObsEvent(string type, JsonElement data)
        {
            if (obsSimulated) return;

            switch (type)
            {
                case "StreamStateChanged":
                    obsStreaming = ObsBool(data, "outputActive");
                    if (obsStreaming) obsStreamStart ??= DateTime.Now;
                    else
                    {
                        obsStreamStart = null;
                        obsSkipped = obsTotal = 0;
                    }
                    break;
                case "RecordStateChanged":
                    string state = ObsStr(data, "outputState");
                    obsRecording = ObsBool(data, "outputActive");
                    bool pausedNow = state == "OBS_WEBSOCKET_OUTPUT_PAUSED";
                    if (pausedNow && !obsRecordPaused && obsRecordStart is DateTime pausedFrom)
                        obsRecordFrozenMs = (long)(DateTime.Now - pausedFrom).TotalMilliseconds;
                    if (!pausedNow && obsRecordPaused)
                        obsRecordStart = DateTime.Now - TimeSpan.FromMilliseconds(obsRecordFrozenMs);   // nach der Pause weiterzählen
                    obsRecordPaused = pausedNow;
                    if (obsRecording) obsRecordStart ??= DateTime.Now;
                    else obsRecordStart = null;
                    break;
                case "CurrentProgramSceneChanged":
                    obsCurrentScene = ObsStr(data, "sceneName");
                    break;
                case "SceneListChanged":
                case "SceneCreated":
                case "SceneRemoved":
                case "SceneNameChanged":
                    _ = Task.Run(ObsRefreshAllAsync);
                    return;
                case "InputMuteStateChanged":
                    if (ObsStr(data, "inputName") == obsMicInput) obsMicMuted = ObsBool(data, "inputMuted");
                    break;
                default:
                    return;
            }

            RenderObs();
            UpdateObsAutoStreamer();
        }

        private void ClearObsData()
        {
            obsStreaming = obsRecording = obsRecordPaused = obsMicMuted = false;
            obsStreamStart = obsRecordStart = null;
            obsSkipped = obsTotal = 0;
            obsMicInput = string.Empty;
            obsCurrentScene = string.Empty;
            obsScenes = new List<string>();
            UpdateObsAutoStreamer();
        }

        private void SetObsState(ObsState state)
        {
            obsState = state;

            obsGraceTimer?.Stop();
            if (state == ObsState.Lost)
            {
                // Kurz abgerissen: Zustand (und damit den Streamer-Modus) 30 Sekunden halten, falls OBS gleich wieder da ist
                obsGraceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                obsGraceTimer.Tick += (s, e) =>
                {
                    obsGraceTimer?.Stop();
                    if (obsState == ObsState.Lost && !obsSimulated)
                    {
                        obsLastResult = ObsConnectResult.NotReachable;
                        SetObsState(settings.ObsEnabled ? ObsState.Failed : ObsState.Off);
                    }
                };
                obsGraceTimer.Start();
            }
            else if (state is ObsState.Off or ObsState.Failed)
            {
                ClearObsData();
            }

            RenderObs();
        }

        // ───────── Anzeige ─────────

        private void RenderObs()
        {
            if (ObsCard == null) return;

            bool active = ObsActive;
            string dot, status, hint;
            switch (obsSimulated ? ObsState.Simulated : obsState)
            {
                case ObsState.Simulated:
                    dot = "#A78BFA"; status = "Simulation"; hint = "Testmodus ohne OBS. Alle Knöpfe wirken nur im Launcher.";
                    break;
                case ObsState.Connected:
                    dot = "#34D399"; status = "Verbunden"; hint = string.Empty;
                    break;
                case ObsState.Connecting:
                    dot = "#F59E0B"; status = "Verbinde …"; hint = "Der Launcher verbindet sich mit OBS.";
                    break;
                case ObsState.Lost:
                    dot = "#F59E0B"; status = "Verbindung unterbrochen"; hint = "Die Verbindung zu OBS ist abgerissen. Der Launcher verbindet sich automatisch neu.";
                    break;
                case ObsState.Failed:
                    dot = "#EF4444"; status = "Nicht verbunden"; hint = ObsResultText(obsLastResult) + " " + Loc.T("Der Launcher versucht es automatisch weiter.");
                    break;
                default:
                    dot = "#6B7280"; status = "Aus";
                    hint = "Steuere Szenen, Stream und Aufnahme direkt von hier. Richte die Verbindung unter Einstellungen → Streamer → OBS-Steuerung ein.";
                    break;
            }

            ObsStatusDot.Fill = MakeBrush(dot);
            TxtObsStatus.Text = Loc.T(status);
            TxtObsHint.Text = Loc.T(hint);
            TxtObsHint.Visibility = hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ObsOfflineButtons.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            BtnObsConnect.Visibility = settings.ObsEnabled ? Visibility.Visible : Visibility.Collapsed;
            ObsControls.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

            if (active)
            {
                BtnObsStream.Content = Loc.T(obsStreaming ? "Stream beenden" : "Stream starten");
                BtnObsStream.Background = MakeBrush(obsStreaming ? "#DC2626" : "#374151");
                BtnObsRecord.Content = Loc.T(obsRecording ? "Aufnahme beenden" : "Aufnahme starten");
                BtnObsRecord.Background = MakeBrush(obsRecording ? "#DC2626" : "#374151");

                BtnObsMic.IsEnabled = obsMicInput.Length > 0;
                BtnObsMic.Content = Loc.T(obsMicInput.Length == 0 ? "🎙 Kein Mikrofon" : obsMicMuted ? "🔇 Mikrofon stumm" : "🎙 Mikrofon an");
                BtnObsMic.Background = MakeBrush(obsMicMuted ? "#B45309" : "#374151");
                BtnObsMic.ToolTip = obsMicInput.Length == 0
                    ? Loc.T("In OBS ist unter Einstellungen → Audio kein Mikrofon eingerichtet.")
                    : Loc.T("Schaltet das Mikrofon in OBS stumm oder wieder an.");

                RenderObsScenes();
            }

            UpdateObsTimes();
            bool ticking = active && (obsStreaming || obsRecording);
            if (ticking && !obsUiTimer.IsEnabled) obsUiTimer.Start();
            else if (!ticking && obsUiTimer.IsEnabled) obsUiTimer.Stop();
        }

        private static string FormatObsDuration(TimeSpan span)
            => $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";

        private void UpdateObsTimes()
        {
            if (TxtObsLive == null) return;

            TxtObsLive.Text = obsStreaming && obsStreamStart is DateTime start
                ? Loc.T($"Live seit {start:HH:mm} ({FormatObsDuration(DateTime.Now - start)})")
                : Loc.T("Offline");
            TxtObsLive.Foreground = MakeBrush(obsStreaming ? "#F87171" : "#FFFFFF");

            if (obsRecording && obsRecordStart is DateTime recStart)
            {
                // Während einer Pause bleibt die Zeit stehen
                if (obsRecordPaused)
                    TxtObsRecTime.Text = Loc.T($"{FormatObsDuration(TimeSpan.FromMilliseconds(obsRecordFrozenMs))} (pausiert)");
                else
                    TxtObsRecTime.Text = FormatObsDuration(DateTime.Now - recStart);
            }
            else TxtObsRecTime.Text = Loc.T("Aus");

            if (obsStreaming && obsTotal > 0)
            {
                double percent = obsSkipped * 100.0 / obsTotal;
                TxtObsDropped.Text = Loc.T(string.Format(Loc.Culture, "{0} von {1} ({2:0.0} %)", obsSkipped, obsTotal, percent));
                TxtObsDropped.Foreground = MakeBrush(percent >= 5 ? "#F87171" : percent >= 1 ? "#F59E0B" : "#FFFFFF");
            }
            else
            {
                TxtObsDropped.Text = "–";
                TxtObsDropped.Foreground = MakeBrush("#FFFFFF");
            }
        }

        private void RenderObsScenes()
        {
            ObsScenePanel.Children.Clear();
            if (obsScenes.Count == 0)
            {
                ObsScenePanel.Children.Add(new TextBlock { Text = Loc.T("Keine Szenen gefunden."), Foreground = BrushSubtle, FontSize = 12 });
                return;
            }

            foreach (string scene in obsScenes)
            {
                string target = scene;
                bool current = scene == obsCurrentScene;
                var button = new System.Windows.Controls.Button
                {
                    Content = scene,
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(16, 8, 16, 8),
                    ToolTip = current ? Loc.T("Aktuelle Szene") : Loc.T("Zu dieser Szene wechseln")
                };
                if (current) button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                button.Click += (s, e) => _ = ObsSetSceneAsync(target);
                ObsScenePanel.Children.Add(button);
            }
        }

        // ───────── Befehle (gehen bei der Simulation nicht an OBS) ─────────

        private async Task ObsCommandAsync(string requestType, object? data = null)
        {
            if (obsSimulated)
            {
                SimulateObsCommand(requestType, data);
                return;
            }
            if (obsState != ObsState.Connected) return;

            var result = await Task.Run(() => obs.RequestAsync(requestType, data));
            if (result == null)
                ShowToast("🎥", "OBS", Loc.T("OBS hat den Befehl nicht ausgeführt."), 5);

            // Zustand gleich nachladen, damit die Knöpfe sofort stimmen
            var status = await Task.Run(ObsReadStatusAsync);
            if (status != null) ApplyObsStatus(status);
        }

        private async Task ObsSetSceneAsync(string scene)
        {
            if (!ObsActive) return;
            await ObsCommandAsync("SetCurrentProgramScene", new { sceneName = scene });
            if (!obsSimulated && obsState == ObsState.Connected)
            {
                obsCurrentScene = scene;
                RenderObs();
                UpdateObsAutoStreamer();
            }
        }

        private async Task ObsToggleStreamAsync()
        {
            if (!ObsActive) return;
            if (obsStreaming)
            {
                if (Msg("Stream wirklich beenden?", "OBS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                await ObsCommandAsync("StopStream");
            }
            else await ObsCommandAsync("StartStream");
        }

        private async Task ObsToggleRecordAsync()
        {
            if (!ObsActive) return;
            if (obsRecording)
            {
                if (Msg("Aufnahme wirklich beenden?", "OBS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                await ObsCommandAsync("StopRecord");
            }
            else await ObsCommandAsync("StartRecord");
        }

        private async Task ObsToggleMicAsync()
        {
            if (!ObsActive || obsMicInput.Length == 0) return;
            bool mute = !obsMicMuted;
            await ObsCommandAsync("SetInputMute", new { inputName = obsMicInput, inputMuted = mute });
            if (!obsSimulated) obsMicMuted = mute;
            RenderObs();
        }

        private void BtnObsStream_Click(object sender, RoutedEventArgs e) => _ = ObsToggleStreamAsync();
        private void BtnObsRecord_Click(object sender, RoutedEventArgs e) => _ = ObsToggleRecordAsync();
        private void BtnObsMic_Click(object sender, RoutedEventArgs e) => _ = ObsToggleMicAsync();

        private void BtnObsConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!settings.ObsEnabled) return;
            RestartObs();
        }

        private void BtnObsSettings_Click(object sender, RoutedEventArgs e) => OpenObsSettings();

        private void OpenObsSettings()
        {
            NavigateTo("settings");
            ChipCatStreamer.IsChecked = true;
            Dispatcher.BeginInvoke(new Action(() => ObsSettingsCard.BringIntoView()), DispatcherPriority.Background);
        }

        // ───────── Streamer-Modus automatisch mit Stream und Szenen ─────────

        private void UpdateObsAutoStreamer()
        {
            bool sceneMatch = ObsActive && obsCurrentScene.Length > 0 && settings.ObsStreamerScenes.Contains(obsCurrentScene);
            bool want = (settings.ObsAutoStreamer && obsStreaming) || sceneMatch;

            if (!want)
            {
                obsAutoSuppressed = false;
                if (obsAutoOn)
                {
                    obsAutoOn = false;
                    ApplyStreamerMode();
                    if (!StreamerOn) ShowToast("📡", "Streamer-Modus aus", "OBS sendet nicht mehr. Alle Angaben sind wieder sichtbar.", 5);
                    RenderStreamerPageIfVisible();
                }
                return;
            }

            if (obsAutoOn || obsAutoSuppressed) return;
            obsAutoOn = true;
            if (!settings.StreamerMode && !streamAutoOn)
                ShowToast("📡", "Streamer-Modus aktiv", obsStreaming ? "Dein Stream läuft. Private Angaben sind verborgen." : "Diese OBS-Szene ist für den Streamer-Modus markiert. Private Angaben sind verborgen.", 6);
            ApplyStreamerMode();
            RenderStreamerPageIfVisible();
        }

        private void RenderStreamerPageIfVisible()
        {
            if (ViewStreamer.Visibility == Visibility.Visible) RenderStreamerPage();
        }

        // ───────── Einstellungen ─────────

        private void PopulateObsSettings()
        {
            TxtObsHost.Text = settings.ObsHost;
            TxtObsPort.Text = settings.ObsPort.ToString(CultureInfo.InvariantCulture);
            PwdObs.Password = SecretStore.Unprotect(settings.ObsPasswordProtected);
            ChkObsEnabled.IsChecked = settings.ObsEnabled;
            ChkObsAutoStreamer.IsChecked = settings.ObsAutoStreamer;
            RenderObsSceneAutoList();
        }

        /// <summary>Übernimmt Adresse, Port und Passwort aus den Feldern. Gibt true zurück, wenn sich etwas geändert hat.</summary>
        private bool ReadObsConnectionFields()
        {
            bool changed = false;

            string host = TxtObsHost.Text.Trim();
            if (host.Length == 0) host = "localhost";
            if (host != settings.ObsHost) { settings.ObsHost = host; changed = true; }
            TxtObsHost.Text = host;

            if (int.TryParse(TxtObsPort.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535)
            {
                if (port != settings.ObsPort) { settings.ObsPort = port; changed = true; }
            }
            TxtObsPort.Text = settings.ObsPort.ToString(CultureInfo.InvariantCulture);

            string password = PwdObs.Password;
            if (password != SecretStore.Unprotect(settings.ObsPasswordProtected))
            {
                settings.ObsPasswordProtected = SecretStore.Protect(password);
                changed = true;
            }

            if (changed) SaveSettings();
            return changed;
        }

        private void ChkObsEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            ReadObsConnectionFields();
            settings.ObsEnabled = ChkObsEnabled.IsChecked == true;
            SaveSettings();
            RestartObs();
        }

        private void ChkObsAutoStreamer_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            settings.ObsAutoStreamer = ChkObsAutoStreamer.IsChecked == true;
            SaveSettings();
            UpdateObsAutoStreamer();
        }

        private void ObsConnectionField_LostFocus(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            if (ReadObsConnectionFields() && settings.ObsEnabled) RestartObs();
        }

        private static string ObsResultText(ObsConnectResult result) => Loc.T(result switch
        {
            ObsConnectResult.NotReachable => "OBS antwortet nicht. Läuft OBS, und ist der WebSocket-Server eingeschaltet (Werkzeuge → WebSocket-Servereinstellungen)? Stimmen Adresse und Port?",
            ObsConnectResult.PasswordRequired => "OBS verlangt ein Passwort. Trage es in den Einstellungen ein.",
            ObsConnectResult.AuthFailed => "Das Passwort für OBS ist falsch.",
            _ => "Die Verbindung zu OBS ist fehlgeschlagen."
        });

        private async void BtnObsTest_Click(object sender, RoutedEventArgs e)
        {
            bool changed = ReadObsConnectionFields();
            string host = settings.ObsHost;
            int port = settings.ObsPort;
            string password = SecretStore.Unprotect(settings.ObsPasswordProtected);

            BtnObsTest.IsEnabled = false;
            TxtObsTestResult.Foreground = MakeBrush("#9CA3AF");
            TxtObsTestResult.Text = Loc.T("Teste die Verbindung …");

            // Eigener Test-Client, damit die laufende Verbindung nicht gestört wird
            var (result, version) = await Task.Run(async () =>
            {
                using var test = new ObsClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var r = await test.ConnectAsync(host, port, password, cts.Token);
                string v = string.Empty;
                if (r == ObsConnectResult.Connected)
                {
                    v = ObsStr(await test.RequestAsync("GetVersion"), "obsVersion");
                    await test.DisconnectAsync();
                }
                return (r, v);
            });

            BtnObsTest.IsEnabled = true;
            if (result == ObsConnectResult.Connected)
            {
                TxtObsTestResult.Foreground = MakeBrush("#34D399");
                TxtObsTestResult.Text = Loc.T($"✓ Verbindung klappt: OBS {(version.Length > 0 ? version : "?")}")
                    + (settings.ObsEnabled ? string.Empty : " " + Loc.T("Schalte oben „OBS aus dem Launcher steuern“ ein, um OBS von der Streamer-Seite zu bedienen."));
                if (settings.ObsEnabled && (changed || obsState != ObsState.Connected)) RestartObs();
            }
            else
            {
                TxtObsTestResult.Foreground = MakeBrush("#F87171");
                TxtObsTestResult.Text = "✗ " + ObsResultText(result);
            }
        }

        private void RenderObsSceneAutoList()
        {
            if (ObsSceneAutoPanel == null) return;
            ObsSceneAutoPanel.Children.Clear();

            var names = obsScenes.Concat(settings.ObsStreamerScenes).Distinct().ToList();
            foreach (string scene in names)
            {
                string target = scene;
                var box = new System.Windows.Controls.CheckBox
                {
                    Content = scene,
                    IsChecked = settings.ObsStreamerScenes.Contains(scene),
                    Margin = new Thickness(0, 0, 20, 10)
                };
                box.Checked += (s, e) => SetObsStreamerScene(target, true);
                box.Unchecked += (s, e) => SetObsStreamerScene(target, false);
                ObsSceneAutoPanel.Children.Add(box);
            }

            TxtObsSceneAutoHint.Text = obsScenes.Count == 0
                ? Loc.T("Sobald OBS verbunden ist, erscheinen hier deine Szenen zum Auswählen.")
                : string.Empty;
            TxtObsSceneAutoHint.Visibility = obsScenes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetObsStreamerScene(string scene, bool on)
        {
            settings.ObsStreamerScenes.Remove(scene);
            if (on) settings.ObsStreamerScenes.Add(scene);
            SaveSettings();
            UpdateObsAutoStreamer();
        }

        // ───────── Simulation für den Entwickler-Reiter (ohne OBS) ─────────

        private void StartObsSimulation()
        {
            try { obsLoopCts?.Cancel(); } catch { }
            obsLoopCts = null;
            obsSimulated = true;
            obsScenes = new List<string> { "Startet gleich", "Gameplay", "Desktop", "Pause" };
            obsCurrentScene = "Startet gleich";
            obsMicInput = "Mikrofon/AUX";
            obsMicMuted = false;
            obsStreaming = obsRecording = obsRecordPaused = false;
            obsStreamStart = obsRecordStart = null;
            obsSkipped = obsTotal = 0;
            RenderObs();
            RenderObsSceneAutoList();
            UpdateObsAutoStreamer();
            ShowToast("🎥", "OBS", Loc.T("OBS-Simulation gestartet."), 4);
        }

        private void StopObsSimulation()
        {
            if (!obsSimulated) return;
            obsSimulated = false;
            ClearObsData();
            SetObsState(ObsState.Off);
            RenderObsSceneAutoList();
            RestartObs();
        }

        private void HandleObsPauseSimulation()
        {
            if (!obsSimulated || !obsRecording || obsRecordStart is not DateTime start) return;

            if (!obsRecordPaused)
                obsRecordFrozenMs = (long)(DateTime.Now - start).TotalMilliseconds;
            else
                obsRecordStart = DateTime.Now - TimeSpan.FromMilliseconds(obsRecordFrozenMs);
            obsRecordPaused = !obsRecordPaused;
            RenderObs();
        }

        private void SimulateObsCommand(string requestType, object? data)
        {
            string Prop(string name) => data?.GetType().GetProperty(name)?.GetValue(data)?.ToString() ?? string.Empty;

            switch (requestType)
            {
                case "StartStream": obsStreaming = true; obsStreamStart = DateTime.Now; obsSkipped = 0; obsTotal = 1; break;
                case "StopStream": obsStreaming = false; obsStreamStart = null; obsSkipped = obsTotal = 0; break;
                case "StartRecord": obsRecording = true; obsRecordPaused = false; obsRecordStart = DateTime.Now; break;
                case "StopRecord": obsRecording = false; obsRecordPaused = false; obsRecordStart = null; break;
                case "SetInputMute": obsMicMuted = Prop("inputMuted") == bool.TrueString; break;
                case "SetCurrentProgramScene": obsCurrentScene = Prop("sceneName"); break;
            }

            RenderObs();
            UpdateObsAutoStreamer();
        }

        // ═════════════════════════════ Streamer-Extras: Aufnahme-Schutz, Dialog-Zensur, Countdown ═════════════════════════════

        [Interop.DllImport("user32.dll", SetLastError = true)]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        private const uint WdaNone = 0x00;
        private const uint WdaExcludeFromCapture = 0x11;   // ab Windows 10 Version 2004 (Build 19041)

        private static readonly int[] GoLiveMinuteOptions = { 1, 2, 3, 5, 10, 15 };
        private const string StreamStartRoutineName = "Stream-Start";

        private readonly DispatcherTimer goLiveTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private DateTime goLiveEnd;
        private TimeSpan goLiveTotal;
        private DateTime goLiveLastCheck;
        private bool goLiveDone;

        private static bool CaptureExclusionSupported
            => Environment.OSVersion.Version.Major > 10
               || (Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Build >= 19041);

        private void InitExtras18()
        {
            // Jedes Fenster des Launchers (Hauptfenster, Dialoge, Menüs) beim Laden aus Aufnahmen ausschließen
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, e) =>
                {
                    if (s is Window window && settings.CaptureExclude) ApplyCaptureExclusion(window, true);
                }));

            goLiveTimer.Tick += (s, e) => GoLiveTick();
        }

        /// <summary>Offene Dialogfenster des Launchers (ohne Hauptfenster und Spiel-Overlay).</summary>
        private IEnumerable<Window> CensorableDialogs()
        {
            var app = System.Windows.Application.Current;
            if (app == null) yield break;
            foreach (Window window in app.Windows)
            {
                if (ReferenceEquals(window, this) || ReferenceEquals(window, overlayWindow) || !window.IsVisible) continue;
                yield return window;
            }
        }

        // ───────── Fenster in Aufnahmen unsichtbar ─────────

        private void ApplyCaptureExclusion(Window window, bool on)
        {
            // Das Spiel-Overlay soll im Stream sichtbar bleiben dürfen
            if (!CaptureExclusionSupported || ReferenceEquals(window, overlayWindow)) return;

            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            try
            {
                if (!SetWindowDisplayAffinity(handle, on ? WdaExcludeFromCapture : WdaNone) && on)
                    LogError("Aufnahme-Schutz", new System.ComponentModel.Win32Exception(Interop.Marshal.GetLastWin32Error()));
            }
            catch (Exception ex)
            {
                LogError("Aufnahme-Schutz", ex);
            }
        }

        private void ApplyCaptureExclusionToAll()
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;
            foreach (Window window in app.Windows) ApplyCaptureExclusion(window, settings.CaptureExclude);
        }

        private void SetCaptureExclude(bool on)
        {
            settings.CaptureExclude = on && CaptureExclusionSupported;
            SaveSettings();
            ApplyCaptureExclusionToAll();
            PopulateStreamerExtras();
            ShowToast("🙈", settings.CaptureExclude ? "In Aufnahmen unsichtbar" : "In Aufnahmen sichtbar",
                settings.CaptureExclude ? "OBS, Discord und Bildschirmfotos zeigen den Launcher nicht mehr." : "Der Launcher erscheint wieder in Aufnahmen.", 4);
        }

        private void ChkCaptureExclude_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            SetCaptureExclude(ChkCaptureExclude.IsChecked == true);
        }

        private void PopulateStreamerExtras()
        {
            if (ChkCaptureExclude == null) return;

            bool before = isLoadingSettings;
            isLoadingSettings = true;
            try
            {
                ChkCaptureExclude.IsChecked = settings.CaptureExclude;
                ChkCaptureExclude.IsEnabled = CaptureExclusionSupported;
                TxtCaptureExcludeHint.Text = CaptureExclusionSupported ? string.Empty
                    : Loc.T("Dafür braucht es Windows 10 Version 2004 oder neuer. Auf diesem PC ist das nicht verfügbar.");
                TxtCaptureExcludeHint.Visibility = CaptureExclusionSupported ? Visibility.Collapsed : Visibility.Visible;
                ChkGoLiveObs.IsChecked = settings.GoLiveStartObs;
            }
            finally
            {
                isLoadingSettings = before;
            }
        }

        // ───────── Countdown „Gleich live“ ─────────

        private bool GoLiveRunning => goLiveTimer.IsEnabled;

        private void RenderGoLive()
        {
            if (GoLiveCard == null) return;
            var chipStyle = TryFindResource("ChipStyle") as Style;

            GoLiveMinutesPanel.Children.Clear();
            foreach (int minutes in GoLiveMinuteOptions)
            {
                int value = minutes;
                var chip = new System.Windows.Controls.RadioButton { Content = Loc.T($"{value} Min."), GroupName = "GoLiveMinutes", IsChecked = value == settings.GoLiveMinutes };
                if (chipStyle != null) chip.Style = chipStyle;
                chip.Checked += (s, e) =>
                {
                    settings.GoLiveMinutes = value;
                    SaveSettings();
                };
                GoLiveMinutesPanel.Children.Add(chip);
            }

            GoLiveRoutinePanel.Children.Clear();
            void RoutineChip(string text, string name)
            {
                var chip = new System.Windows.Controls.RadioButton { Content = text, GroupName = "GoLiveRoutine", IsChecked = name == settings.GoLiveRoutine };
                if (chipStyle != null) chip.Style = chipStyle;
                chip.Checked += (s, e) =>
                {
                    settings.GoLiveRoutine = name;
                    SaveSettings();
                };
                GoLiveRoutinePanel.Children.Add(chip);
            }
            if (settings.GoLiveRoutine.Length > 0 && !settings.Routines.Any(r => r.Name == settings.GoLiveRoutine))
                settings.GoLiveRoutine = string.Empty;   // Ablauf wurde gelöscht oder umbenannt
            RoutineChip(Loc.T("Keiner"), string.Empty);
            foreach (var routine in settings.Routines) RoutineChip("⚙ " + routine.Name, routine.Name);

            BtnGoLiveRoutine.Visibility = settings.Routines.Any(r => r.Name == StreamStartRoutineName) ? Visibility.Collapsed : Visibility.Visible;
            UpdateGoLiveUi();
        }

        private void UpdateGoLiveUi()
        {
            if (GoLiveCard == null) return;

            bool running = GoLiveRunning;
            GoLiveOptions.IsEnabled = !running;
            BtnGoLive.Content = Loc.T(running ? "Countdown abbrechen" : "Countdown starten");
            if (running) BtnGoLive.Background = MakeBrush("#374151");
            else BtnGoLive.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            GoLiveBar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

            if (running)
            {
                var left = goLiveEnd - DateTime.Now;
                if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                TxtGoLiveTime.Text = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
                TxtGoLiveTime.Visibility = Visibility.Visible;
                double fraction = goLiveTotal.TotalSeconds > 0 ? 1 - left.TotalSeconds / goLiveTotal.TotalSeconds : 1;
                GoLiveFill.Width = Math.Max(0, GoLiveBar.ActualWidth * Math.Clamp(fraction, 0, 1));
            }
            else if (goLiveDone)
            {
                TxtGoLiveTime.Text = Loc.T("🔴 Live!");
                TxtGoLiveTime.Visibility = Visibility.Visible;
            }
            else
            {
                TxtGoLiveTime.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnGoLive_Click(object sender, RoutedEventArgs e)
        {
            if (GoLiveRunning)
            {
                goLiveTimer.Stop();
                goLiveDone = false;
                UpdateGoLiveUi();
                ShowToast("⏱", "Countdown abgebrochen", "Der Countdown „Gleich live“ wurde gestoppt.", 3);
                return;
            }

            StartGoLive(TimeSpan.FromMinutes(settings.GoLiveMinutes));
        }

        private void StartGoLive(TimeSpan duration)
        {
            goLiveTotal = duration;
            goLiveEnd = DateTime.Now + duration;
            goLiveLastCheck = DateTime.MinValue;
            goLiveDone = false;
            goLiveTimer.Start();
            UpdateGoLiveUi();
        }

        private void GoLiveTick()
        {
            if (DateTime.Now >= goLiveEnd)
            {
                FinishGoLive();
                return;
            }

            UpdateGoLiveUi();

            // Checkliste alle 5 Sekunden auffrischen (Prozesse im Hintergrund prüfen)
            if ((DateTime.Now - goLiveLastCheck).TotalSeconds >= 5)
            {
                goLiveLastCheck = DateTime.Now;
                _ = RefreshGoLiveChecklistAsync();
            }
        }

        private async Task RefreshGoLiveChecklistAsync()
        {
            bool running = await Task.Run(() => StreamProcesses.Any(IsProcessRunning));
            if (ViewStreamer.Visibility == Visibility.Visible) RenderStreamerChecklist(running);
        }

        private void FinishGoLive()
        {
            goLiveTimer.Stop();
            goLiveDone = true;
            UpdateGoLiveUi();
            _ = RefreshGoLiveChecklistAsync();

            PlayUiSound("launch");
            ShowToast("🔴", "Gleich live!", "Der Countdown ist abgelaufen. Viel Spaß beim Stream!", 10, () => NavigateTo("streamer"), true);

            if (settings.GoLiveStartObs && ObsActive && !obsStreaming) _ = ObsCommandAsync("StartStream");

            var routine = settings.Routines.FirstOrDefault(r => r.Name == settings.GoLiveRoutine);
            if (routine != null) _ = RunRoutineAsync(routine, true);
        }

        private void ChkGoLiveObs_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            settings.GoLiveStartObs = ChkGoLiveObs.IsChecked == true;
            SaveSettings();
        }

        private void BtnGoLiveRoutine_Click(object sender, RoutedEventArgs e)
        {
            if (!settings.Routines.Any(r => r.Name == StreamStartRoutineName))
            {
                settings.Routines.Add(new AutomationRoutine
                {
                    Name = StreamStartRoutineName,
                    Trigger = "manual",
                    Steps = new List<AutomationStep>
                    {
                        new() { Type = "streamer", Mode = "on" },
                        new() { Type = "toast", Target = "Du bist live. Viel Spaß beim Stream!" }
                    }
                });
            }
            settings.GoLiveRoutine = StreamStartRoutineName;
            SaveSettings();
            RenderGoLive();
            ShowToast("⚙", "Ablauf angelegt", "„Stream-Start“ schaltet den Streamer-Modus ein. Weitere Schritte fügst du bei den Abläufen hinzu.", 7, null, true);
        }
    }
}
