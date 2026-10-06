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
            PopulateNavPosition();
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

        // ───────────────────────────── Position der Seitenleiste (links, rechts, unten schwebend) ─────────────────────────────

        private System.Windows.Controls.Panel? navPanelHome;
        private int navPanelHomeIndex;
        private ColumnDefinition? colContent;
        private string appliedNavPosition = string.Empty;

        private string NavPosition => settings.SidebarPosition is "right" or "bottom" ? settings.SidebarPosition : "left";
        private bool IsBottomNav => NavPosition == "bottom";

        private void InitExtras22()
        {
            // Rechtsklick auf die schwebende Leiste: dasselbe Menü wie auf der Seitenleiste
            NavDock.ContextMenu = BuildDockMenu();
            NavDock.ContextMenuOpening += (s, e) => NavDock.ContextMenu = BuildDockMenu();

            // Breite der schwebenden Leiste an das Fenster anpassen (sie verkleinert sich bei schmalen Fenstern)
            RootGrid.SizeChanged += (s, e) => NavDock.MaxWidth = Math.Max(320, RootGrid.ActualWidth - 40);

            DockStreamBadge.ToolTip = MakeNavToolTip(Loc.T("Streamer-Modus ist an. Klicken zum Ausschalten"), true);
            System.Windows.Controls.ToolTipService.SetInitialShowDelay(DockStreamBadge, 120);
            AttachDockHover(DockStreamBadge);
        }

        /// <summary>Hinweis-Blase im Launcher-Design statt Windows-Tooltip (unten mittig über dem Symbol mit Pfeil, an der Seitenleiste daneben).</summary>
        private System.Windows.Controls.ToolTip MakeNavToolTip(object content, bool dock)
        {
            var tip = new System.Windows.Controls.ToolTip { Content = content };
            if (TryFindResource(dock ? "DockToolTip" : "SideToolTip") is Style style) tip.Style = style;
            if (dock)
            {
                // Mittig über dem Symbol, mit etwas Luft für das beim Drüberfahren angehobene Symbol
                tip.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
                tip.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(
                        new System.Windows.Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 6),
                        System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
                };
            }
            else
            {
                tip.Placement = NavPosition == "right"
                    ? System.Windows.Controls.Primitives.PlacementMode.Left
                    : System.Windows.Controls.Primitives.PlacementMode.Right;
            }
            return tip;
        }

        /// <summary>Rechtsklick auf die schwebende Leiste: Schnellstart-Programm hinzufügen, dazu das Bereiche-Menü.</summary>
        private System.Windows.Controls.ContextMenu BuildDockMenu()
        {
            var menu = BuildNavMenu(null);
            if (settings.QuickButtons.Count >= 12) return menu;

            var add = new System.Windows.Controls.MenuItem { Header = Loc.T("＋  Programm zum Schnellstart hinzufügen") };
            add.Click += (s, e) => AddQuickFromDock();
            menu.Items.Insert(0, add);
            menu.Items.Insert(1, new System.Windows.Controls.Separator());
            return menu;
        }

        private void AddQuickFromDock()
        {
            if (!settings.QuickButtonsEnabled)
            {
                // Wer ein Programm hinzufügt, möchte den Schnellstart auch sehen
                settings.QuickButtonsEnabled = true;
                SaveSettings();
            }
            PickQuickTarget(null);
            RenderSidebarExtras();
        }

        /// <summary>Schnellstart-Knöpfe und Musik (Logos der gewählten Dienste, Steuerung) in der schwebenden Leiste.</summary>
        private void RenderDockExtras()
        {
            if (NavDockExtras == null) return;
            NavDockExtras.Children.Clear();
            dockPlayIcon = dockPauseIcon = null;

            if (IsBottomNav)
            {
                if (settings.QuickButtonsEnabled)
                {
                    foreach (var button in settings.QuickButtons.ToList())
                    {
                        var tile = DockifyTile(CreateQuickTile(button), button.Name);
                        if (tile.ContextMenu is System.Windows.Controls.ContextMenu menu && settings.QuickButtons.Count < 12)
                        {
                            menu.Items.Add(new System.Windows.Controls.Separator());
                            var add = new System.Windows.Controls.MenuItem { Header = Loc.T("＋  Programm zum Schnellstart hinzufügen") };
                            add.Click += (s, e) => AddQuickFromDock();
                            menu.Items.Add(add);
                        }
                        NavDockExtras.Children.Add(tile);
                    }
                }

                if (settings.MusicPlayer && !SHide(settings.StreamHideMusic))
                {
                    foreach (var service in ActiveMusicServices())
                    {
                        string key = service.Key;
                        var tile = DockTile(MusicLogo(key, 26), service.Name, () => OpenMusic(key));
                        if (musicView != null && musicLoadedService == key)
                        {
                            // Der gerade laufende Dienst bekommt einen Rahmen in der Akzentfarbe
                            tile.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                            tile.BorderThickness = new Thickness(2);
                        }
                        NavDockExtras.Children.Add(tile);
                    }

                    if (musicView != null)
                    {
                        var controls = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
                        controls.Children.Add(DockTile(ControlIcon("prev"), Loc.T("Vorheriges Lied"), () => SendMediaKey(0xB1), 34));
                        controls.Children.Add(BuildDockPlayPause());
                        controls.Children.Add(DockTile(ControlIcon("next"), Loc.T("Nächstes Lied"), () => SendMediaKey(0xB0), 34));
                        musicControls = controls;
                        NavDockExtras.Children.Add(controls);
                    }
                }
            }

            if (NavDockDivider != null)
                NavDockDivider.Visibility = NavDockExtras.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Steuerungs-Symbole als Vektorgrafik, alle gleich groß (prev, play, pause, next).</summary>
        private static FrameworkElement ControlIcon(string kind)
        {
            var canvas = new Canvas { Width = 16, Height = 16 };
            var white = System.Windows.Media.Brushes.White;

            System.Windows.Shapes.Rectangle Bar(double left, double top, double width, double height, double radius)
            {
                var bar = new System.Windows.Shapes.Rectangle { Width = width, Height = height, RadiusX = radius, RadiusY = radius, Fill = white };
                Canvas.SetLeft(bar, left);
                Canvas.SetTop(bar, top);
                return bar;
            }

            System.Windows.Shapes.Path Triangle(string data, double stroke) => new()
            {
                Data = Geometry.Parse(data),
                Fill = white,
                Stroke = white,
                StrokeThickness = stroke,
                StrokeLineJoin = PenLineJoin.Round
            };

            switch (kind)
            {
                case "prev":
                    canvas.Children.Add(Bar(2.5, 3, 2.2, 10, 0.9));
                    canvas.Children.Add(Triangle("M13.5,3.2 L5.6,8 L13.5,12.8 Z", 1.2));
                    break;
                case "next":
                    canvas.Children.Add(Triangle("M2.5,3.2 L10.4,8 L2.5,12.8 Z", 1.2));
                    canvas.Children.Add(Bar(11.3, 3, 2.2, 10, 0.9));
                    break;
                case "pause":
                    canvas.Children.Add(Bar(3.6, 2.6, 3.2, 10.8, 1.1));
                    canvas.Children.Add(Bar(9.2, 2.6, 3.2, 10.8, 1.1));
                    break;
                default:   // play
                    canvas.Children.Add(Triangle("M4.6,2.8 L13.2,8 L4.6,13.2 Z", 1.4));
                    break;
            }

            return new Viewbox
            {
                Width = 15,
                Height = 15,
                Child = canvas,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
        }

        private Border DockTile(FrameworkElement content, string tip, Action click, double size = 40)
        {
            var tile = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(12),
                Background = MakeBrush("#1AFFFFFF"),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Child = content
            };
            tile.MouseEnter += (s, e) => tile.Background = MakeBrush("#33FFFFFF");
            tile.MouseLeave += (s, e) => tile.Background = MakeBrush("#1AFFFFFF");
            tile.MouseLeftButtonUp += (s, e) => click();
            return DockifyTile(tile, tip);
        }

        // ───────── Play/Pause: zeigt immer den passenden Knopf, mit Übergang ─────────

        private FrameworkElement? dockPlayIcon, dockPauseIcon;
        private bool musicIsPlaying;

        private Border BuildDockPlayPause()
        {
            dockPlayIcon = ControlIcon("play");
            dockPauseIcon = ControlIcon("pause");
            foreach (var icon in new[] { dockPlayIcon, dockPauseIcon })
            {
                icon.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                icon.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new RotateTransform(0) } };
            }
            var stack = new Grid { Children = { dockPlayIcon, dockPauseIcon } };
            try { musicIsPlaying = musicView?.CoreWebView2?.IsDocumentPlayingAudio ?? false; }
            catch { musicIsPlaying = false; }
            ShowPlayPause(musicIsPlaying, false);

            var tile = DockTile(stack, Loc.T("Wiedergabe oder Pause"), () =>
            {
                // Sofort umschalten, die echte Meldung des Players bestätigt es kurz danach
                ShowPlayPause(!musicIsPlaying, true);
                musicIsPlaying = !musicIsPlaying;
                _ = MusicTogglePlayAsync();
            }, 34);
            return tile;
        }

        /// <summary>Wird vom Musik-Player gemeldet, sobald Ton startet oder stoppt.</summary>
        private void OnMusicPlayingChanged(bool playing)
        {
            if (playing == musicIsPlaying) return;   // passt schon (zum Beispiel nach dem eigenen Klick)
            musicIsPlaying = playing;
            ShowPlayPause(playing, true);
        }

        private void ShowPlayPause(bool playing, bool animate)
        {
            if (dockPlayIcon == null || dockPauseIcon == null) return;

            // Läuft Musik, zeigt der Knopf „Pause“, sonst „Weiter“
            var show = playing ? dockPauseIcon : dockPlayIcon;
            var hide = playing ? dockPlayIcon : dockPauseIcon;

            if (!animate || !settings.HoverAnimations || settings.PerformanceMode)
            {
                show.Opacity = 1;
                hide.Opacity = 0;
                return;
            }

            void Morph(FrameworkElement icon, bool appear)
            {
                var group = (TransformGroup)icon.RenderTransform;
                var scale = (ScaleTransform)group.Children[0];
                var rotate = (RotateTransform)group.Children[1];
                var duration = TimeSpan.FromMilliseconds(appear ? 320 : 180);
                var ease = appear
                    ? (IEasingFunction)new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }
                    : new CubicEase { EasingMode = EasingMode.EaseIn };

                icon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(appear ? 1 : 0, TimeSpan.FromMilliseconds(appear ? 200 : 150)));
                var size = new DoubleAnimation(appear ? 0.4 : 1, appear ? 1 : 0.4, duration) { EasingFunction = ease };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, size);
                rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(appear ? -90 : 0, appear ? 0 : 90, duration) { EasingFunction = ease });
            }

            Morph(hide, false);
            Morph(show, true);
        }

        // ───────── Logos der Musikdienste (als Vektorgrafik, ohne Bilddateien) ─────────

        private static FrameworkElement MusicLogo(string key, double size)
        {
            var canvas = new Canvas { Width = 26, Height = 26 };
            System.Windows.Media.Brush Fill(string hex) => MakeBrush(hex);

            switch (key)
            {
                case "spotify":
                    canvas.Children.Add(new System.Windows.Shapes.Ellipse { Width = 26, Height = 26, Fill = Fill("#1DB954") });
                    foreach (var (data, thickness) in new[]
                    {
                        ("M6.2,9.6 C10.4,8.1 15.6,8.4 19.9,10.6", 2.4),
                        ("M7.1,13.3 C10.6,12.2 14.9,12.5 18.5,14.2", 2.0),
                        ("M8.0,16.8 C10.9,16.0 14.2,16.2 17.0,17.5", 1.7)
                    })
                    {
                        canvas.Children.Add(new System.Windows.Shapes.Path
                        {
                            Data = Geometry.Parse(data),
                            Stroke = Fill("#121212"),
                            StrokeThickness = thickness,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round
                        });
                    }
                    break;

                case "ytmusic":
                    canvas.Children.Add(new System.Windows.Shapes.Ellipse { Width = 26, Height = 26, Fill = Fill("#FF0033") });
                    var ring = new System.Windows.Shapes.Ellipse { Width = 15, Height = 15, Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1.6 };
                    Canvas.SetLeft(ring, 5.5);
                    Canvas.SetTop(ring, 5.5);
                    canvas.Children.Add(ring);
                    canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M11.2,9.6 L17,13 L11.2,16.4 Z"), Fill = System.Windows.Media.Brushes.White });
                    break;

                default:   // Apple Music
                    var gradient = new LinearGradientBrush(System.Windows.Media.Color.FromRgb(0xFA, 0x58, 0x6A), System.Windows.Media.Color.FromRgb(0xFB, 0x23, 0x3B), 90);
                    gradient.Freeze();
                    canvas.Children.Add(new System.Windows.Shapes.Rectangle { Width = 26, Height = 26, RadiusX = 6.5, RadiusY = 6.5, Fill = gradient });
                    canvas.Children.Add(new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M10.2,8.2 L17.4,6.6 L17.4,15.6 A2.3,1.9 0 1 1 16.0,13.9 L16.0,9.4 L11.6,10.4 L11.6,17.2 A2.3,1.9 0 1 1 10.2,15.5 Z"),
                        Fill = System.Windows.Media.Brushes.White
                    });
                    break;
            }

            return new Viewbox { Width = size, Height = size, Child = canvas, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        }

        /// <summary>Macht eine Kachel passend für die schwebende Leiste: Abstand, Hinweis-Blase und Hover-Effekt.</summary>
        private Border DockifyTile(Border tile, string tip)
        {
            tile.Margin = new Thickness(3, 0, 3, 0);
            tile.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            tile.ToolTip = MakeNavToolTip(tip, true);
            System.Windows.Controls.ToolTipService.SetInitialShowDelay(tile, 120);
            System.Windows.Controls.ToolTipService.SetBetweenShowDelay(tile, 0);
            AttachDockHover(tile);
            return tile;
        }

        /// <summary>Kachel wächst beim Drüberfahren federnd und hebt sich leicht an (wie die Symbole der Bereiche).</summary>
        private void AttachDockHover(FrameworkElement element)
        {
            var scale = new ScaleTransform(1, 1);
            var lift = new TranslateTransform(0, 0);
            element.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            element.RenderTransform = new TransformGroup { Children = { scale, lift } };

            void Animate(bool hover)
            {
                if (!settings.HoverAnimations) return;
                var grow = new DoubleAnimation(hover ? 1.22 : 1.0, TimeSpan.FromMilliseconds(hover ? 420 : 200))
                {
                    EasingFunction = hover
                        ? new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }
                        : new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(hover ? -6 : 0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }

            element.MouseEnter += (s, e) => Animate(true);
            element.MouseLeave += (s, e) => Animate(false);
        }

        /// <summary>Ordnet Seitenleiste, Inhalt und schwebende Leiste nach der gewählten Position an.</summary>
        private void ApplySidebarPosition(bool animate)
        {
            if (RootGrid == null || NavDock == null || ContentArea == null) return;

            string position = NavPosition;
            bool right = position == "right";
            bool bottom = position == "bottom";

            // Spalten: Seitenleiste links oder rechts
            colContent ??= RootGrid.ColumnDefinitions.FirstOrDefault(c => !ReferenceEquals(c, ColSidebar));
            if (colContent != null && RootGrid.ColumnDefinitions.IndexOf(ColSidebar) != (right ? 1 : 0))
            {
                RootGrid.ColumnDefinitions.Clear();
                if (right)
                {
                    RootGrid.ColumnDefinitions.Add(colContent);
                    RootGrid.ColumnDefinitions.Add(ColSidebar);
                }
                else
                {
                    RootGrid.ColumnDefinitions.Add(ColSidebar);
                    RootGrid.ColumnDefinitions.Add(colContent);
                }
            }
            int sideColumn = right ? 1 : 0, contentColumn = right ? 0 : 1;
            foreach (UIElement child in RootGrid.Children)
            {
                if (ReferenceEquals(child, NavDock)) continue;
                Grid.SetColumn(child, ReferenceEquals(child, SidebarBorder) ? sideColumn : contentColumn);
            }
            SidebarBorder.BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);

            // Unten: Seitenleiste aus, Navigation wandert in die schwebende Leiste
            SidebarBorder.Visibility = bottom ? Visibility.Collapsed : Visibility.Visible;
            NavDock.Visibility = bottom ? Visibility.Visible : Visibility.Collapsed;
            ContentArea.Margin = bottom ? new Thickness(28, 42, 28, 104)
                               : right ? new Thickness(16, 42, 28, 16)
                               : new Thickness(28, 42, 16, 16);

            if (bottom && !ReferenceEquals(NavPanel.Parent, NavDockPanel))
            {
                if (NavPanel.Parent is System.Windows.Controls.Panel home)
                {
                    navPanelHome = home;
                    navPanelHomeIndex = home.Children.IndexOf(NavPanel);
                    home.Children.Remove(NavPanel);
                }
                NavPanel.Orientation = System.Windows.Controls.Orientation.Horizontal;
                NavDockPanel.Children.Insert(0, NavPanel);
            }
            else if (!bottom && ReferenceEquals(NavPanel.Parent, NavDockPanel) && navPanelHome != null)
            {
                NavDockPanel.Children.Remove(NavPanel);
                NavPanel.Orientation = System.Windows.Controls.Orientation.Vertical;
                navPanelHome.Children.Insert(Math.Clamp(navPanelHomeIndex, 0, navPanelHome.Children.Count), NavPanel);
            }

            foreach (var item in NavPanel.Children.OfType<System.Windows.Controls.RadioButton>())
            {
                AttachNavIconHover(item);
                var icon = NavIcon(item);
                if (bottom)
                {
                    item.Margin = new Thickness(3, 0, 3, 0);
                    if (icon != null) icon.FontSize = 24;
                }
                else
                {
                    item.ClearValue(FrameworkElement.MarginProperty);
                    icon?.ClearValue(TextBlock.FontSizeProperty);
                }
            }

            // Schatten nur ohne Leistungsmodus
            NavDock.Effect = bottom && !settings.PerformanceMode
                ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = 0.45, Color = Colors.Black }
                : null;
            NavDock.MaxWidth = Math.Max(320, RootGrid.ActualWidth - 40);
            if (DockStreamBadge != null) DockStreamBadge.Visibility = StreamerOn ? Visibility.Visible : Visibility.Collapsed;

            // Beim Wechsel nach unten schwebt die Leiste von unten herein
            if (bottom && appliedNavPosition != "bottom" && animate && settings.PageAnimations)
            {
                var slide = new TranslateTransform(0, 70);
                NavDock.RenderTransform = slide;
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(70, 0, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
                });
                NavDock.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            }

            // Schnellstart und Musik wechseln zwischen Seitenleiste und schwebender Leiste
            bool changed = appliedNavPosition != position;
            appliedNavPosition = position;
            if (changed && SidebarExtras != null) RenderSidebarExtras();
        }

        private static TextBlock? NavIcon(System.Windows.Controls.RadioButton item)
            => item.Content is StackPanel panel && panel.Children.Count > 0 ? panel.Children[0] as TextBlock : null;

        /// <summary>Symbol wird beim Drüberfahren größer und wackelt kurz; in der schwebenden Leiste hebt es sich zusätzlich an.</summary>
        private void AttachNavIconHover(System.Windows.Controls.RadioButton item)
        {
            var icon = NavIcon(item);
            if (icon == null || icon.RenderTransform is TransformGroup) return;   // schon eingerichtet

            var scale = new ScaleTransform(1, 1);
            var rotate = new RotateTransform(0);
            var lift = new TranslateTransform(0, 0);
            icon.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            icon.RenderTransform = new TransformGroup { Children = { scale, rotate, lift } };

            item.MouseEnter += (s, e) => AnimateNavIcon(scale, rotate, lift, true);
            item.MouseLeave += (s, e) => AnimateNavIcon(scale, rotate, lift, false);
        }

        private void AnimateNavIcon(ScaleTransform scale, RotateTransform rotate, TranslateTransform lift, bool hover)
        {
            if (!settings.HoverAnimations)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                lift.BeginAnimation(TranslateTransform.YProperty, null);
                return;
            }

            bool dock = IsBottomNav;
            double target = hover ? (dock ? 1.45 : 1.28) : 1.0;
            var grow = new DoubleAnimation(target, TimeSpan.FromMilliseconds(hover ? 420 : 200))
            {
                EasingFunction = hover
                    ? new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }
                    : new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(hover && dock ? -8 : 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            if (hover)
            {
                // Kurzes Wackeln
                var wiggle = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
                wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(-12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
                wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
                wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(-5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340))));
                wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460))));
                rotate.BeginAnimation(RotateTransform.AngleProperty, wiggle);
            }
            else
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        private void PopulateNavPosition()
        {
            if (ChipNavLeft == null) return;
            bool before = isLoadingSettings;
            isLoadingSettings = true;
            try
            {
                ChipNavLeft.IsChecked = NavPosition == "left";
                ChipNavRight.IsChecked = NavPosition == "right";
                ChipNavBottom.IsChecked = NavPosition == "bottom";
            }
            finally
            {
                isLoadingSettings = before;
            }
        }

        private void NavPosition_Checked(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings || sender is not FrameworkElement { Tag: string position }) return;
            if (position == settings.SidebarPosition) return;

            settings.SidebarPosition = position;
            SaveSettings();
            ApplySidebar(true);
        }
    }
}
