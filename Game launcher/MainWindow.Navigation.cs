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
        // ───────────────────────────── Navigation ─────────────────────────────

        private void NavDashboard_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewDashboard);
            RefreshDashboard(true);
            UpdateClock();
        }

        private void NavApps_Click(object sender, RoutedEventArgs e) => ShowView(ViewApps);
        private void NavTools_Click(object sender, RoutedEventArgs e) => ShowView(ViewTools);
        private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowView(ViewSettings);

        private void NavLinks_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewLinks);
            BuildLinkTiles();
        }

        private void NavDownloads_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewDownloads);
            RefreshDownloads();
        }

        private void NavSystem_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewSystem);
            if (HardwareList.ItemsSource == null) _ = RefreshHardwareAsync();
        }

        private void NavStorage_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewStorage);
            RefreshDrives();
        }

        private async void NavOptimization_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewOptimization);
            RefreshAutostart();
            PopulateOptimizationToggles();
            RenderCleanup();
            _ = MeasureCleanupAsync();
            await RefreshPowerPlanAsync();
        }

        // ───────────────────────────── Navigation & Seitenübergänge ─────────────────────────────

        private void ShowView(FrameworkElement activeView)
        {
            var views = new FrameworkElement[]
            {
                ViewDashboard, ViewGames, ViewStats, ViewApps, ViewLinks, ViewWishlist, ViewDownloads, ViewSystem,
                ViewStorage, ViewOptimization, ViewTools, ViewSettings, ViewDeals, ViewBacklog, ViewGallery, ViewStreamer
            };

            foreach (var view in views)
                view.Visibility = Visibility.Collapsed;

            activeView.Visibility = Visibility.Visible;
            AnimateIn(activeView);

            if (StreamerOn) Dispatcher.BeginInvoke(new Action(() => { ApplyCensorCategories(true); ScrubSensitiveText(); }), DispatcherPriority.Background);
        }

        private void AnimateIn(FrameworkElement view)
        {
            if (!settings.PageAnimations)
            {
                view.BeginAnimation(UIElement.OpacityProperty, null);
                view.Opacity = 1;
                view.RenderTransform = System.Windows.Media.Transform.Identity;
                return;
            }

            var slide = new TranslateTransform(0, 18);
            view.RenderTransform = slide;
            view.Opacity = 0;

            view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(340))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void NavGames_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewGames);
            playCardEntrance = true;
            ApplyFilter();
        }

        private void NavStats_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewStats);
            RefreshStats(true);
        }

        private void NavigateTo(string key)
        {
            var args = new RoutedEventArgs();

            switch (key)
            {
                case "dashboard": NavDashboardBtn.IsChecked = true; NavDashboard_Click(this, args); break;
                case "games": NavGamesBtn.IsChecked = true; NavGames_Click(this, args); break;
                case "stats": NavStatsBtn.IsChecked = true; NavStats_Click(this, args); break;
                case "apps": NavAppsBtn.IsChecked = true; NavApps_Click(this, args); break;
                case "links": NavLinksBtn.IsChecked = true; NavLinks_Click(this, args); break;
                case "wishlist": NavWishBtn.IsChecked = true; NavWishlist_Click(this, args); break;
                case "downloads": NavDownloadsBtn.IsChecked = true; NavDownloads_Click(this, args); break;
                case "system": NavSystemBtn.IsChecked = true; NavSystem_Click(this, args); break;
                case "storage": NavStorageBtn.IsChecked = true; NavStorage_Click(this, args); break;
                case "optimization": NavOptimizationBtn.IsChecked = true; NavOptimization_Click(this, args); break;
                case "tools": NavToolsBtn.IsChecked = true; NavTools_Click(this, args); break;
                case "deals": NavDealsBtn.IsChecked = true; NavDeals_Click(this, args); break;
                case "backlog": NavBacklogBtn.IsChecked = true; NavBacklog_Click(this, args); break;
                case "gallery": NavGalleryBtn.IsChecked = true; NavGallery_Click(this, args); break;
                case "streamer": NavStreamerBtn.IsChecked = true; NavStreamer_Click(this, args); break;
                case "settings": NavSettingsBtn.IsChecked = true; NavSettings_Click(this, args); break;
            }
        }

        // ───────────────────────────── Spotlight-Suche (Strg+K) ─────────────────────────────

        private static int ScoreMatch(string text, string query)
        {
            if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 100;
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) return 60;

            int position = 0;
            foreach (char c in query)
            {
                position = text.IndexOf(c.ToString(), position, StringComparison.OrdinalIgnoreCase);
                if (position < 0) return -1;
                position++;
            }
            return 30;
        }

        private void OpenSpotlight()
        {
            SuspendMusicOverlay();
            SpotlightLayer.Visibility = Visibility.Visible;
            SpotlightLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

            TxtSpotlight.Text = string.Empty;
            BuildSpotlightResults();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                TxtSpotlight.Focus();
                System.Windows.Input.Keyboard.Focus(TxtSpotlight);
            }), DispatcherPriority.Input);
        }

        private void CloseSpotlight()
        {
            ResumeMusicOverlay();
            SpotlightLayer.Visibility = Visibility.Collapsed;
        }

        private void BuildSpotlightResults()
        {
            string query = TxtSpotlight.Text.Trim();
            var entries = new List<SpotlightEntry>();

            void Add(string icon, string title, string subtitle, Action action, int bonus)
            {
                int score = ScoreMatch(title, query);
                if (score < 0) return;
                entries.Add(new SpotlightEntry { Icon = icon, Title = title, Subtitle = subtitle, Action = action, Score = score + bonus });
            }

            if (query.StartsWith("/", StringComparison.Ordinal) && AddEggCommands(query, entries))
            {
                spotlightEntries.Clear();
                spotlightEntries.AddRange(entries);
                spotlightIndex = 0;
                RenderSpotlight();
                return;
            }

            if (query.Length == 0)
            {
                foreach (var game in VisibleGames.Where(g => g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).Take(4))
                {
                    var target = game;
                    entries.Add(new SpotlightEntry { Icon = "🎮", Title = target.Name, Subtitle = "Zuletzt gespielt  ·  " + target.Source, Action = () => LaunchGame(target) });
                }

                entries.Add(new SpotlightEntry { Icon = "🎲", Title = "Zufälliges Spiel", Subtitle = "Lass den Launcher entscheiden", Action = () => BtnRandomGame_Click(this, new RoutedEventArgs()) });
                entries.Add(new SpotlightEntry { Icon = "🎮", Title = "Controller-Modus", Subtitle = "Vollbild mit Gamepad-Steuerung (F11)", Action = ToggleControllerMode });
                entries.Add(new SpotlightEntry { Icon = "🔄", Title = "Spiele neu scannen", Subtitle = "Bibliothek aktualisieren", Action = () => BtnRescanGames_Click(this, new RoutedEventArgs()) });
            }
            else
            {
                foreach (var game in VisibleGames)
                {
                    var target = game;
                    Add("🎮", target.Name, "Spiel  ·  " + target.Source, () => LaunchGame(target), target.LastPlayed.HasValue ? 25 : 20);
                }
                foreach (var app in cachedApps)
                {
                    var target = app;
                    Add("🧩", target.Name, "Anwendung", () => LaunchApp(target), 15);
                }
                foreach (var tool in toolEntries)
                {
                    string target = tool.Target;
                    Add(tool.Icon, tool.Title, tool.Text, () => HandleToolTarget(target), 10);
                }
                foreach (var quickLink in settings.QuickLinks)
                {
                    string quickTarget = quickLink.Target;
                    Add(QuickEmoji(quickLink), quickLink.Name, DescribeTarget(quickTarget), () => OpenShell(quickTarget), 8);
                }
                foreach (var page in PageEntries)
                {
                    string key = page.Key;
                    Add(page.Icon, page.Title, "Bereich öffnen", () => NavigateTo(key), 12);
                }
                foreach (var (_, commands) in SystemCommandGroups)
                {
                    foreach (var command in commands)
                    {
                        var picked = command;
                        Add(picked.Icon, picked.Title, "Systembefehl", () => RunSystemCommand(picked), 6);
                    }
                }
                foreach (var streamLink in settings.StreamerLinks)
                {
                    string streamTarget = streamLink.Target;
                    Add(QuickEmoji(streamLink), streamLink.Name, DescribeTarget(streamTarget), () => OpenShell(streamTarget), 7);
                }
                Add("🎲", "Zufälliges Spiel", "Lass den Launcher entscheiden", () => BtnRandomGame_Click(this, new RoutedEventArgs()), 5);
                Add("🎮", "Controller-Modus", "Vollbild mit Gamepad-Steuerung", ToggleControllerMode, 5);
                Add("📡", "Streamer-Modus umschalten", "Verbirgt private Angaben", ToggleStreamerMode, 5);
                if (ObsActive)
                {
                    Add("🔴", obsStreaming ? "OBS: Stream beenden" : "OBS: Stream starten", "OBS-Steuerung", () => _ = ObsToggleStreamAsync(), 9);
                    Add("⏺", obsRecording ? "OBS: Aufnahme beenden" : "OBS: Aufnahme starten", "OBS-Steuerung", () => _ = ObsToggleRecordAsync(), 9);
                    if (obsMicInput.Length > 0)
                        Add("🎙", obsMicMuted ? "OBS: Mikrofon einschalten" : "OBS: Mikrofon stumm schalten", "OBS-Steuerung", () => _ = ObsToggleMicAsync(), 9);
                    foreach (string scene in obsScenes)
                    {
                        string target = scene;
                        Add("🎬", "OBS-Szene: " + target, "Zu dieser Szene wechseln", () => _ = ObsSetSceneAsync(target), 8);
                    }
                }
                else if (settings.ObsEnabled)
                {
                    Add("🎥", "OBS verbinden", "OBS-Steuerung", RestartObs, 6);
                }
                Add("🎥", "OBS-Einstellungen", "Verbindung zu OBS einrichten", OpenObsSettings, 4);
                Add("🎯", "Was soll ich spielen?", "Spielvorschlag nach Laune", ShowGameSuggestion, 6);
                Add("🎉", "Jahresrückblick", "Dein Spielejahr", () => ShowYearReview(DateTime.Now.Year), 5);
                foreach (var profileEntry in settings.Profiles)
                {
                    var profile = profileEntry;
                    Add(profile.Icon, "Profil: " + profile.Name, profile.Id == settings.ActiveProfileId ? "Aktives Profil" : "Profil aktivieren",
                        () => ActivateProfileManually(profile), 9);
                }
                foreach (var routineEntry in settings.Routines.Where(r => r.Enabled))
                {
                    var routine = routineEntry;
                    Add("⚙", "Ablauf: " + routine.Name, "Ablauf ausführen", () => _ = RunRoutineAsync(routine, true), 8);
                }
            }

            spotlightEntries.Clear();
            spotlightEntries.AddRange(entries.OrderByDescending(x => x.Score).Take(8));
            spotlightIndex = 0;
            RenderSpotlight();
        }

        private void RenderSpotlight()
        {
            SpotlightResults.Children.Clear();

            if (spotlightEntries.Count == 0)
            {
                SpotlightResults.Children.Add(new TextBlock { Text = "Keine Treffer.", Foreground = BrushSubtle, Margin = new Thickness(6, 8, 0, 4) });
                return;
            }

            for (int i = 0; i < spotlightEntries.Count; i++)
            {
                var entry = spotlightEntries[i];
                int index = i;

                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                line.Children.Add(new TextBlock
                {
                    Text = entry.Icon,
                    FontSize = 20,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                });

                var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
                Grid.SetColumn(texts, 1);
                texts.Children.Add(new TextBlock { Text = entry.Title, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                texts.Children.Add(new TextBlock { Text = entry.Subtitle, Foreground = BrushSubtle, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                line.Children.Add(texts);

                var item = new Border
                {
                    Padding = new Thickness(10, 7, 10, 7),
                    CornerRadius = new CornerRadius(10),
                    Background = i == spotlightIndex ? BrushTileHover : System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = line
                };
                item.MouseEnter += (s, e) =>
                {
                    spotlightIndex = index;
                    HighlightSpotlight();
                };
                item.MouseLeftButtonUp += (s, e) => ActivateSpotlight(entry);

                SpotlightResults.Children.Add(item);
            }
        }

        private void HighlightSpotlight()
        {
            for (int i = 0; i < SpotlightResults.Children.Count; i++)
            {
                if (SpotlightResults.Children[i] is Border item)
                    item.Background = i == spotlightIndex ? BrushTileHover : System.Windows.Media.Brushes.Transparent;
            }
        }

        private void ActivateSpotlight(SpotlightEntry entry)
        {
            CloseSpotlight();
            entry.Action();
        }

        private void TxtSpotlight_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SpotlightLayer.Visibility != Visibility.Visible) return;

            // Geheime Namen werden nie vorgeschlagen und lösen nur beim vollständigen Eintippen aus
            if (TryPlayEggFromSearch(TxtSpotlight.Text))
            {
                CloseSpotlight();
                return;
            }

            BuildSpotlightResults();
        }

        private void TxtSpotlight_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Down:
                    if (spotlightEntries.Count > 0) spotlightIndex = (spotlightIndex + 1) % spotlightEntries.Count;
                    HighlightSpotlight();
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Up:
                    if (spotlightEntries.Count > 0) spotlightIndex = (spotlightIndex - 1 + spotlightEntries.Count) % spotlightEntries.Count;
                    HighlightSpotlight();
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Enter:
                    if (TryPlayEggFromSearch(TxtSpotlight.Text))
                    {
                        CloseSpotlight();
                        e.Handled = true;
                        break;
                    }
                    if (spotlightIndex >= 0 && spotlightIndex < spotlightEntries.Count) ActivateSpotlight(spotlightEntries[spotlightIndex]);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Escape:
                    CloseSpotlight();
                    e.Handled = true;
                    break;
            }
        }

        private void Spotlight_BackgroundClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CloseSpotlight();
        private void Spotlight_CardClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;

        // ───────────────────────────── Navigation der neuen Bereiche ─────────────────────────────

        private void NavDeals_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewDeals);
            ApplyStoreLogos();
            MarkFreeGamesSeen();

            if ((DateTime.Now - lastDealsRefresh).TotalMinutes > 30)
                _ = RefreshDealsAsync(false);
            else
                RenderDeals();
        }

        private void NavBacklog_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewBacklog);
            RefreshBoard();
        }

        private void NavGallery_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ViewGallery);
            _ = RefreshGalleryAsync();
        }

        private void ReadExtraSettings()
        {
            settings.WeeklyGoalHours = (int)Math.Round(SliderGoal.Value);
            settings.BreakReminderMinutes = (int)Math.Round(SliderBreak.Value);
            settings.ShowDeals = ChkDeals.IsChecked == true;
            settings.StorageWarning = ChkStorageWarn.IsChecked == true;
            settings.AutoBackupSaves = ChkAutoBackup.IsChecked == true;
            ReadComfortSettings();
            ReadExtra3Settings();
            ReadExtra4Settings();
            ReadExtra5Settings();
            ReadExtra6Settings();
            ReadExtra7Settings();
            ReadExtra8Settings();
            ReadExtra10Settings();
            ReadExtra11Settings();
            ReadExtra12Settings();
            ReadExtra14Settings();

        }

        private void PopulateExtraSettings()
        {
            SliderGoal.Value = settings.WeeklyGoalHours;
            SliderBreak.Value = settings.BreakReminderMinutes;
            ChkDeals.IsChecked = settings.ShowDeals;
            ChkStorageWarn.IsChecked = settings.StorageWarning;
            ChkAutoBackup.IsChecked = settings.AutoBackupSaves;
            PopulateComfortSettings();
            PopulateExtra3Settings();
            PopulateExtra4Settings();
            PopulateExtra5Settings();
            PopulateExtra6Settings();
            PopulateExtra7Settings();
            PopulateExtra8Settings();
            PopulateExtra10Settings();
            PopulateExtra11Settings();
            PopulateExtra12Settings();
            PopulateExtra14Settings();
            PopulateObsSettings();
            PopulateStreamerExtras();
            PopulateControllerExtras();
            if (ChkDownloadDone != null) ChkDownloadDone.IsChecked = settings.DownloadDoneNotify;
            PopulateReportAndBeta();
        }

        private void UpdateExtraSliderLabels()
        {
            TxtGoalValue.Text = settings.WeeklyGoalHours == 0 ? "Aus" : $"{settings.WeeklyGoalHours} Std. pro Woche";
            TxtBreakValue.Text = settings.BreakReminderMinutes == 0 ? "Aus" : $"alle {settings.BreakReminderMinutes} Min.";
        }

        // ───────────────────────────── Tastenkürzel ─────────────────────────────

        private bool HandleShortcuts(System.Windows.Input.KeyEventArgs e, bool ctrl)
        {
            if (!ctrl || SpotlightLayer.Visibility == Visibility.Visible) return false;

            if (e.Key == System.Windows.Input.Key.S && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0)
            {
                ToggleStreamerMode();
                e.Handled = true;
                return true;
            }

            if (e.Key == System.Windows.Input.Key.F)
            {
                NavigateTo("games");
                TxtSearch.Focus();
                e.Handled = true;
                return true;
            }

            if (e.Key == System.Windows.Input.Key.R)
            {
                BtnRescanGames_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return true;
            }

            if (e.Key == System.Windows.Input.Key.OemComma)
            {
                NavigateTo("settings");
                e.Handled = true;
                return true;
            }

            if (e.Key >= System.Windows.Input.Key.D1 && e.Key <= System.Windows.Input.Key.D9)
            {
                int index = e.Key - System.Windows.Input.Key.D1;
                if (index < PageEntries.Length)
                {
                    NavigateTo(PageEntries[index].Key);
                    e.Handled = true;
                    return true;
                }
            }

            return false;
        }

        // ───────────────────────────── Suche: Enter und Esc ─────────────────────────────

        private void TxtSearch_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (TxtSearch.Text != SearchPlaceholder && visibleGames.Count > 0)
                {
                    LaunchGame(visibleGames[0]);
                    e.Handled = true;
                }
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                TxtSearch.Text = string.Empty;
                System.Windows.Input.Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        // ───────────────────────────── Seitenleiste: Bereiche ein-/ausblenden und sortieren ─────────────────────────────

        private Dictionary<string, System.Windows.Controls.RadioButton> NavButtonMap() => new()
        {
            ["dashboard"] = NavDashboardBtn,
            ["games"] = NavGamesBtn,
            ["stats"] = NavStatsBtn,
            ["deals"] = NavDealsBtn,
            ["backlog"] = NavBacklogBtn,
            ["gallery"] = NavGalleryBtn,
            ["apps"] = NavAppsBtn,
            ["links"] = NavLinksBtn,
            ["wishlist"] = NavWishBtn,
            ["downloads"] = NavDownloadsBtn,
            ["system"] = NavSystemBtn,
            ["storage"] = NavStorageBtn,
            ["optimization"] = NavOptimizationBtn,
            ["tools"] = NavToolsBtn,
            ["streamer"] = NavStreamerBtn,
            ["settings"] = NavSettingsBtn
        };

        private List<string> GetNavOrder()
        {
            var all = PageEntries.Select(p => p.Key).ToList();
            var order = settings.NavOrder.Where(k => all.Contains(k)).Distinct().ToList();
            var missing = all.Where(k => !order.Contains(k)).ToList();
            int settingsAt = order.IndexOf("settings");
            if (settingsAt >= 0) order.InsertRange(settingsAt, missing);
            else order.AddRange(missing);
            return order;
        }

        private void ApplyNavVisibility()
        {
            var map = NavButtonMap();
            var order = GetNavOrder();

            for (int i = 0; i < order.Count; i++)
            {
                if (!map.TryGetValue(order[i], out var button)) continue;

                int current = NavPanel.Children.IndexOf(button);
                if (current != i && current >= 0)
                {
                    NavPanel.Children.RemoveAt(current);
                    NavPanel.Children.Insert(Math.Min(i, NavPanel.Children.Count), button);
                }

                bool hidden = order[i] != "settings" && settings.HiddenSections.Contains(order[i]);
                button.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void SetSectionVisible(string key, bool visible)
        {
            if (key == "settings") return;
            if (key == "streamer") UpdateStreamerToggles();

            if (visible) settings.HiddenSections.Remove(key);
            else if (!settings.HiddenSections.Contains(key)) settings.HiddenSections.Add(key);

            SaveSettings();
            ApplyNavVisibility();
            if (!buildingNavSettings) BuildNavSettings();
        }

        private void MoveSection(string key, int delta)
        {
            var order = GetNavOrder();
            int index = order.IndexOf(key);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= order.Count) return;

            order.RemoveAt(index);
            order.Insert(target, key);
            settings.NavOrder = order;

            SaveSettings();
            ApplyNavVisibility();
            BuildNavSettings();
        }

        private void ResetNavOrder()
        {
            settings.NavOrder = new List<string>();
            settings.HiddenSections = new List<string>();
            SaveSettings();
            ApplyNavVisibility();
            BuildNavSettings();
        }

        private System.Windows.Controls.MenuItem BuildSectionsSubMenu()
        {
            var sub = new System.Windows.Controls.MenuItem { Header = Loc.T("👁  Bereiche anzeigen") };

            foreach (var key in GetNavOrder())
            {
                string sectionKey = key;
                var page = PageEntries.First(p => p.Key == sectionKey);
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = page.Icon + "  " + Loc.T(page.Title),
                    IsCheckable = true,
                    IsChecked = sectionKey == "settings" || !settings.HiddenSections.Contains(sectionKey),
                    IsEnabled = sectionKey != "settings"
                };
                item.Click += (s, e) => SetSectionVisible(sectionKey, item.IsChecked);
                sub.Items.Add(item);
            }

            return sub;
        }

        private System.Windows.Controls.ContextMenu BuildNavMenu(string? key)
        {
            var menu = CreateMenu();

            if (key != null)
            {
                if (key != "settings")
                    AddMenuItem(menu, "🙈  Diesen Bereich ausblenden", () => SetSectionVisible(key, false));
                AddMenuItem(menu, "⬆  Nach oben", () => MoveSection(key, -1));
                AddMenuItem(menu, "⬇  Nach unten", () => MoveSection(key, 1));
                menu.Items.Add(new System.Windows.Controls.Separator());
            }

            menu.Items.Add(BuildSectionsSubMenu());
            AddMenuItem(menu, "🔠  Einträge größer", () => ChangeNavSize(1));
            AddMenuItem(menu, "🔡  Einträge kleiner", () => ChangeNavSize(-1));
            AddMenuItem(menu, "↺  Reihenfolge zurücksetzen", ResetNavOrder);
            return menu;
        }

        private void BuildNavMenus()
        {
            foreach (var pair in NavButtonMap())
            {
                string key = pair.Key;
                var button = pair.Value;
                button.ContextMenu = BuildNavMenu(key);
                button.ContextMenuOpening += (s, e) => button.ContextMenu = BuildNavMenu(key);
            }

            SidebarBorder.ContextMenu = BuildNavMenu(null);
            SidebarBorder.ContextMenuOpening += (s, e) => SidebarBorder.ContextMenu = BuildNavMenu(null);
        }

        private void BuildNavSettings()
        {
            if (NavSettingsPanel == null) return;

            buildingNavSettings = true;
            NavSettingsPanel.Children.Clear();

            var order = GetNavOrder();
            for (int i = 0; i < order.Count; i++)
            {
                string key = order[i];
                var page = PageEntries.First(p => p.Key == key);

                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var box = new System.Windows.Controls.CheckBox
                {
                    Content = page.Icon + "  " + Loc.T(page.Title),
                    Foreground = System.Windows.Media.Brushes.White,
                    IsChecked = key == "settings" || !settings.HiddenSections.Contains(key),
                    IsEnabled = key != "settings",
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                box.Checked += (s, e) => { if (!buildingNavSettings) SetSectionVisible(key, true); };
                box.Unchecked += (s, e) => { if (!buildingNavSettings) SetSectionVisible(key, false); };
                row.Children.Add(box);

                var up = new System.Windows.Controls.Button
                {
                    Content = "▲",
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(8, 0, 4, 0),
                    IsEnabled = i > 0
                };
                up.Click += (s, e) => MoveSection(key, -1);
                Grid.SetColumn(up, 1);
                row.Children.Add(up);

                var down = new System.Windows.Controls.Button
                {
                    Content = "▼",
                    Padding = new Thickness(10, 3, 10, 3),
                    IsEnabled = i < order.Count - 1
                };
                down.Click += (s, e) => MoveSection(key, 1);
                Grid.SetColumn(down, 2);
                row.Children.Add(down);

                NavSettingsPanel.Children.Add(row);
            }

            buildingNavSettings = false;
        }

        // ───────────────────────────── Tippen startet die Suche ─────────────────────────────

        private void OnTypeToSearch(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (!settings.TypeToSearch || controllerMode || ViewGames.Visibility != Visibility.Visible) return;
            if (SpotlightLayer.Visibility == Visibility.Visible) return;
            if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
            if (e.Text.Length != 1 || !char.IsLetterOrDigit(e.Text[0])) return;

            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            if ((modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt)) != 0) return;

            TxtSearch.Focus();
            TxtSearch.Text = e.Text;
            TxtSearch.CaretIndex = TxtSearch.Text.Length;
            e.Handled = true;
        }
    }
}
