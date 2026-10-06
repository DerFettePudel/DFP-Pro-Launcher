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
        // ───────────────────────────── Einstellungen & Theme ─────────────────────────────

        private void LoadSettings()
        {
            try
            {
                string? source = File.Exists(SettingsFilePath) ? SettingsFilePath
                               : File.Exists(LegacySettingsPath) ? LegacySettingsPath
                               : null;

                if (source != null)
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(source)) ?? new AppSettings();
            }
            catch
            {
                settings = TryRestoreSettingsBackup() ?? new AppSettings();
            }

            if (string.IsNullOrWhiteSpace(settings.UserName)) settings.UserName = "Gamer";
            settings.WeatherCity ??= string.Empty;
            settings.ManualGames ??= new List<ManualGame>();
            settings.ManualApps ??= new List<ManualApp>();
            settings.GameStates ??= new Dictionary<string, GameState>();
            settings.SteamIds ??= new Dictionary<string, int>();
            settings.HiddenSections ??= new List<string>();
            settings.CollectionNames ??= new List<string>();
            settings.PlayLog ??= new Dictionary<string, double>();
            settings.SeenFreeGames ??= new List<string>();
            settings.DefaultCompanions ??= new List<string>();
            settings.Achievements ??= new Dictionary<string, string>();
            settings.Releases ??= new List<ReleaseEntry>();
            settings.ScreenshotFolders ??= new List<string>();
            settings.WeeklyGoalHours = Math.Clamp(settings.WeeklyGoalHours, 0, 40);
            settings.BreakReminderMinutes = Math.Clamp(settings.BreakReminderMinutes, 0, 240);
            settings.PreviousPowerPlan ??= string.Empty;
            settings.BackgroundImagePath ??= string.Empty;
            if (settings.CardLayout != "detail" && settings.CardLayout != "list") settings.CardLayout = "cards";
            settings.BgDim = Math.Clamp(settings.BgDim, 0, 90);
            settings.BgBlur = Math.Clamp(settings.BgBlur, 0, 30);
            settings.OverlayCorner = Math.Clamp(settings.OverlayCorner, 0, 3);
            settings.Language ??= "auto";
            settings.CloseApps ??= new List<string>();

            // Windows-Einstellung "Animationen anzeigen" respektieren (einmalig beim ersten Start)
            if (!settings.MotionChecked)
            {
                settings.MotionChecked = true;
                if (!SystemParameters.ClientAreaAnimation)
                {
                    settings.PageAnimations = false;
                    settings.CardAnimations = false;
                    settings.SmoothMetrics = false;
                    settings.HoverGlow = false;
                    settings.AnimatedBackground = false;
                }
            }

            settings.CardWidth = Math.Clamp(settings.CardWidth, 160, 300);
            settings.CardCorner = Math.Clamp(settings.CardCorner, 0, 28);
            settings.UiScale = Math.Clamp(settings.UiScale, 80, 130);
            settings.HoverZoom = Math.Clamp(settings.HoverZoom, 100, 112);
            settings.RecentCount = Math.Clamp(settings.RecentCount, 3, 12);
            settings.NavOrder ??= new List<string>();
            settings.CardSpacing = Math.Clamp(settings.CardSpacing, 4, 28);
            settings.CardAspect = Math.Clamp(settings.CardAspect, 100, 170);
            settings.SidebarWidth = Math.Clamp(settings.SidebarWidth, 200, 340);
            if (settings.SidebarPosition is not ("left" or "right" or "bottom")) settings.SidebarPosition = "left";
            settings.BrandLogoImage ??= string.Empty;
            if (settings.BrandLogoShape is not ("circle" or "rounded" or "square")) settings.BrandLogoShape = "circle";
            settings.BackgroundSourcePath ??= string.Empty;
            settings.NavFontSize = Math.Clamp(settings.NavFontSize, 11, 20);
            settings.NavItemPadding = Math.Clamp(settings.NavItemPadding, 4, 16);
            settings.BrandLogoSize = Math.Clamp(settings.BrandLogoSize, 32, 96);
            settings.BrandNameSize = Math.Clamp(settings.BrandNameSize, 12, 30);
            settings.QuickTileSize = Math.Clamp(settings.QuickTileSize, 150, 260);
            settings.TrackingIgnored ??= new List<string>();
            settings.QuickLinks ??= new List<QuickLink>();
            settings.SteamGridDbKey ??= string.Empty;
            settings.Wishlist ??= new List<WishItem>();
            settings.GameInfo ??= new Dictionary<string, GameInfoEntry>();
            settings.UpdateRepo ??= string.Empty;
            settings.CloudBackupFolder ??= string.Empty;
            settings.DailyLimitHours = Math.Clamp(settings.DailyLimitHours, 0, 12);
            settings.OverlayOpacity = Math.Clamp(settings.OverlayOpacity, 30, 100);
            settings.OverlayScale = Math.Clamp(settings.OverlayScale, 70, 200);
            settings.DashboardOrder ??= new List<string>();
            settings.DashboardHidden ??= new List<string>();
            settings.DiscordWebhook ??= string.Empty;
            settings.SkippedUpdate ??= string.Empty;
            settings.AppFavorites ??= new List<string>();
            settings.AnimatedChecked ??= new List<string>();
            settings.EggSeen ??= new List<string>();
            settings.MilestonesReached ??= new List<int>();
            settings.FirstRunDate ??= string.Empty;
            settings.StreamerApps ??= new List<QuickButton>();
            settings.StreamerLinks ??= new List<QuickLink>();
            settings.StreamerHiddenApps ??= new List<string>();
            if (settings.StreamCensorStyle != "blur") settings.StreamCensorStyle = "bar";
            settings.PadSeenLayouts ??= new List<string>();
            settings.PadRepeat = Math.Clamp(settings.PadRepeat, 1, 3);
            if (settings.LibraryView != "flow") settings.LibraryView = "cards";
            if (settings.PadComboMode is not ("backstart" or "bumpers" or "hold")) settings.PadComboMode = "backstart";
            settings.SidebarColor ??= string.Empty;
            settings.NavTextColor ??= string.Empty;
            settings.NavActiveColor ??= string.Empty;
            settings.RgbSpeed = Math.Clamp(settings.RgbSpeed, 1, 3);
            if (!settings.RgbUnlocked) settings.RgbAccent = false;
            settings.QuickButtons ??= new List<QuickButton>();
            settings.Routines ??= new List<AutomationRoutine>();
            if (string.IsNullOrWhiteSpace(settings.DeviceProfile)) settings.DeviceProfile = "auto";
            if (string.IsNullOrWhiteSpace(settings.MusicService)) settings.MusicService = "spotify";
            settings.MusicServicesOn ??= new List<string>();
            settings.MusicServicesOn = settings.MusicServicesOn.Where(k => MusicServices.Any(s => s.Key == k)).Distinct().ToList();
            if (settings.MusicServicesOn.Count == 0)
                settings.MusicServicesOn.Add(MusicServices.Any(s => s.Key == settings.MusicService) ? settings.MusicService : "spotify");
            if (!settings.SgdbAnimatedMigrated)
            {
                // Einmalig: Wer einen SteamGridDB-Schlüssel hat, bekommt ab jetzt animierte Cover
                settings.SgdbAnimatedMigrated = true;
                if (!string.IsNullOrWhiteSpace(settings.SteamGridDbKey)) settings.SgdbAnimated = true;
            }
            if (!new[] { 500, 1000, 2000, 5000 }.Contains(settings.MonitorIntervalMs)) settings.MonitorIntervalMs = 1000;
            if (string.IsNullOrWhiteSpace(settings.ObsHost)) settings.ObsHost = "localhost";
            if (settings.ObsPort is < 1 or > 65535) settings.ObsPort = 4455;
            settings.ObsPasswordProtected ??= string.Empty;
            settings.ObsStreamerScenes ??= new List<string>();
            settings.Profiles ??= new List<SettingsProfile>();
            settings.Profiles.RemoveAll(p => p == null);
            foreach (var profile in settings.Profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = "Profil";
                profile.Icon ??= "⭐";
                profile.Groups ??= new List<string>();
                profile.Values ??= new Dictionary<string, JsonElement>();
                profile.PowerPlan ??= string.Empty;
            }
            settings.ActiveProfileId ??= string.Empty;
            settings.ProfileOnGame ??= string.Empty;
            settings.ProfileOnObs ??= string.Empty;
            if (!GoLiveMinuteOptions.Contains(settings.GoLiveMinutes)) settings.GoLiveMinutes = 5;
            settings.GoLiveRoutine ??= string.Empty;
            settings.SmartLists ??= new List<SmartList>();
            settings.SmartLists.RemoveAll(l => l == null);
            foreach (var list in settings.SmartLists)
            {
                if (string.IsNullOrWhiteSpace(list.Id)) list.Id = Guid.NewGuid().ToString("N");
                list.Name ??= string.Empty;
                list.Source ??= string.Empty;
            }
            settings.GameSizes ??= new Dictionary<string, GameSizeEntry>(StringComparer.OrdinalIgnoreCase);
            settings.YearStats ??= new Dictionary<string, YearStat>();
            foreach (var stat in settings.YearStats.Values.Where(v => v != null))
            {
                stat.Games ??= new Dictionary<string, double>();
                stat.Sources ??= new Dictionary<string, string>();
                stat.LongestGame ??= string.Empty;
            }
            settings.PadMappings ??= new Dictionary<string, Dictionary<string, string>>();
            foreach (string layoutKey in settings.PadMappings.Keys.ToList())
            {
                // Ungültige oder unvollständige Belegungen verwerfen (dann gilt die empfohlene)
                if (!IsValidPadMapping(settings.PadMappings[layoutKey])) settings.PadMappings.Remove(layoutKey);
            }

            ApplyTheme();
            UpdateUserNameDisplay();
            PopulateControlsFromSettings();
        }

        private void SaveSettings()
        {
            // Nach einem Import wartet die neue Datei auf den Neustart und darf nicht mehr überschrieben werden
            if (settingsSaveBlocked) return;

            try
            {
                Directory.CreateDirectory(SettingsDir);
                lock (settings.SteamIds)
                {
                    string json = JsonSerializer.Serialize(settings, JsonOptions);
                    string temp = SettingsFilePath + ".tmp";
                    File.WriteAllText(temp, json);
                    File.Move(temp, SettingsFilePath, true);
                }
            }
            catch { }
        }

        private void PopulateControlsFromSettings()
        {
            SliderCardSize.Value = settings.CardWidth;
            SliderCorner.Value = settings.CardCorner;
            SliderScale.Value = settings.UiScale;
            SliderZoom.Value = settings.HoverZoom;
            SliderRecent.Value = settings.RecentCount;

            ChkHover.IsChecked = settings.HoverAnimations;
            ChkShowNames.IsChecked = settings.ShowCardNames;
            ChkShowSource.IsChecked = settings.ShowCardSource;
            ChkShowClock.IsChecked = settings.ShowClock;
            ChkClock24.IsChecked = settings.Clock24h;
            ChkClockSeconds.IsChecked = settings.ClockSeconds;
            ChkShowWeather.IsChecked = settings.ShowWeather;
            ChkFahrenheit.IsChecked = settings.Fahrenheit;
            ChkShowStats.IsChecked = settings.ShowStats;
            ChkShowRecent.IsChecked = settings.ShowRecent;
            ChkShowFavorites.IsChecked = settings.ShowFavorites;
            ChkFullRes.IsChecked = settings.FullResCovers;
            ChkSparklines.IsChecked = settings.ShowSparklines;
            ChkMinimize.IsChecked = settings.MinimizeOnLaunch;
            ChkTopmost.IsChecked = settings.AlwaysOnTop;
            ChkRememberWindow.IsChecked = settings.RememberWindow;
            ChkOnline.IsChecked = settings.OnlineFeatures;
            ChkAutostart.IsChecked = IsAutostartEnabled();

            ChipMon500.IsChecked = settings.MonitorIntervalMs == 500;
            ChipMon1000.IsChecked = settings.MonitorIntervalMs == 1000;
            ChipMon2000.IsChecked = settings.MonitorIntervalMs == 2000;
            ChipMon5000.IsChecked = settings.MonitorIntervalMs == 5000;
            ChipStartDashboard.IsChecked = settings.StartPage != "games";
            ChipStartGames.IsChecked = settings.StartPage == "games";

            TxtWeatherCity.Text = settings.WeatherCity;
            PopulateFeatureSettings();
            PopulateExtraSettings();
            UpdateSliderLabels();
        }

        private void ReadSettingsFromControls()
        {
            settings.CardWidth = (int)Math.Round(SliderCardSize.Value);
            settings.CardCorner = (int)Math.Round(SliderCorner.Value);
            settings.UiScale = (int)Math.Round(SliderScale.Value);
            settings.HoverZoom = (int)Math.Round(SliderZoom.Value);
            settings.RecentCount = (int)Math.Round(SliderRecent.Value);

            settings.HoverAnimations = ChkHover.IsChecked == true;
            settings.ShowCardNames = ChkShowNames.IsChecked == true;
            settings.ShowCardSource = ChkShowSource.IsChecked == true;
            settings.ShowClock = ChkShowClock.IsChecked == true;
            settings.Clock24h = ChkClock24.IsChecked == true;
            settings.ClockSeconds = ChkClockSeconds.IsChecked == true;
            settings.ShowWeather = ChkShowWeather.IsChecked == true;
            settings.Fahrenheit = ChkFahrenheit.IsChecked == true;
            settings.ShowStats = ChkShowStats.IsChecked == true;
            settings.ShowRecent = ChkShowRecent.IsChecked == true;
            settings.ShowFavorites = ChkShowFavorites.IsChecked == true;
            settings.FullResCovers = ChkFullRes.IsChecked == true;
            settings.ShowSparklines = ChkSparklines.IsChecked == true;
            settings.MinimizeOnLaunch = ChkMinimize.IsChecked == true;
            settings.AlwaysOnTop = ChkTopmost.IsChecked == true;
            settings.RememberWindow = ChkRememberWindow.IsChecked == true;
            settings.OnlineFeatures = ChkOnline.IsChecked == true;

            settings.MonitorIntervalMs = ChipMon500.IsChecked == true ? 500
                                       : ChipMon2000.IsChecked == true ? 2000
                                       : ChipMon5000.IsChecked == true ? 5000
                                       : 1000;
            settings.StartPage = ChipStartGames.IsChecked == true ? "games" : "dashboard";
            ReadFeatureSettings();
            ReadExtraSettings();
        }

        private void UpdateSliderLabels()
        {
            TxtCardSizeValue.Text = $"{settings.CardWidth} px";
            TxtCornerValue.Text = $"{settings.CardCorner} px";
            TxtScaleValue.Text = $"{settings.UiScale} %";
            TxtZoomValue.Text = $"{settings.HoverZoom} %";
            TxtRecentValue.Text = $"{settings.RecentCount} Spiele";
            UpdateFeatureSliderLabels();
            UpdateExtraSliderLabels();
            UpdateExtra3SliderLabels();
            UpdateExtra4SliderLabels();
            UpdateExtra7SliderLabels();
        }

        /// <summary>Wendet alle Einstellungen an, die das Aussehen und die Widgets betreffen.</summary>
        private void ApplyViewSettings()
        {
            double scale = settings.UiScale / 100.0;
            RootGrid.LayoutTransform = settings.UiScale == 100
                ? System.Windows.Media.Transform.Identity
                : new ScaleTransform(scale, scale);

            Topmost = settings.AlwaysOnTop;
            ApplyBrand();
            ApplyNavSizes();
            ApplyStreamerMode();
            UpdateAlphaBar();
            if (trayIcon != null) trayIcon.Visible = settings.ShowTrayIcon || settings.MinimizeToTray;
            systemTimer.Interval = TimeSpan.FromMilliseconds(settings.MonitorIntervalMs);

            ClockCard.Visibility = settings.ShowClock ? Visibility.Visible : Visibility.Collapsed;
            WeatherCard.Visibility = settings.ShowWeather ? Visibility.Visible : Visibility.Collapsed;
            ColWeather.Width = settings.ShowWeather ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            ClockCard.Margin = settings.ShowWeather ? new Thickness(0, 0, 12, 0) : new Thickness(0);
            WidgetsRow.Visibility = settings.ShowClock || settings.ShowWeather ? Visibility.Visible : Visibility.Collapsed;

            StatsGrid.Visibility = settings.ShowStats ? Visibility.Visible : Visibility.Collapsed;
            RecentSection.Visibility = settings.ShowRecent ? Visibility.Visible : Visibility.Collapsed;

            var sparkVisibility = settings.ShowSparklines ? Visibility.Visible : Visibility.Collapsed;
            CanvasCpu.Visibility = sparkVisibility;
            CanvasRam.Visibility = sparkVisibility;
            CanvasGpu.Visibility = sparkVisibility;

            ApplyAppearance();
        }

        private void UpdateUserNameDisplay()
        {
            TxtUserName.Text = DisplayUserName();
            TxtSettingsUserName.Text = settings.UserName;
        }

        private static bool TryPickColor(string currentHex, out string newHex)
        {
            using var dialog = new Forms.ColorDialog { FullOpen = true };

            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(currentHex);
                dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            }
            catch { }

            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                newHex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                return true;
            }

            newHex = currentHex;
            return false;
        }

        private static bool IsAutostartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                return key?.GetValue(AutostartName) != null;
            }
            catch
            {
                return false;
            }
        }

        private static void SetAutostart(bool enable, bool controller = false)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            // „Als Konsole starten“: mit dem Startparameter --controller öffnet sich direkt der Controller-Modus
            if (enable && Environment.ProcessPath is string exe)
                key.SetValue(AutostartName, controller ? $"\"{exe}\" --controller" : $"\"{exe}\"");
            else
                key.DeleteValue(AutostartName, false);
        }

        private void RestoreWindowBounds()
        {
            if (!settings.RememberWindow || settings.WinWidth < 600 || settings.WinHeight < 400) return;

            double left = settings.WinLeft;
            double top = settings.WinTop;
            bool onScreen =
                left + 100 > SystemParameters.VirtualScreenLeft &&
                left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                top + 50 > SystemParameters.VirtualScreenTop &&
                top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;
            if (!onScreen) return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
            Width = settings.WinWidth;
            Height = settings.WinHeight;
            if (settings.WinMaximized) WindowState = WindowState.Maximized;
        }

        private void SaveWindowBounds()
        {
            if (!settings.RememberWindow) return;

            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            settings.WinLeft = bounds.Left;
            settings.WinTop = bounds.Top;
            settings.WinWidth = bounds.Width;
            settings.WinHeight = bounds.Height;
            settings.WinMaximized = WindowState == WindowState.Maximized;
            SaveSettings();
        }

        private void ShowStartPage()
        {
            if (settings.StartPage != "games") return;

            ShowView(ViewGames);
            NavGamesBtn.IsChecked = true;
        }

        private void BtnSaveUserName_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSettingsUserName.Text)) return;

            settings.UserName = TxtSettingsUserName.Text.Trim();
            SaveSettings();
            UpdateUserNameDisplay();
            RefreshDashboard();
            if (CheckNameEgg(settings.UserName)) ShowToast("✅", "Name gespeichert", settings.UserName, 3);
            else Msg("Name erfolgreich gespeichert!");
        }

        private void BtnPickBgColor_Click(object sender, RoutedEventArgs e)
        {
            if (!TryPickColor(settings.BackgroundColor, out string hex)) return;
            settings.BackgroundColor = hex;
            SaveSettings();
            RefreshAfterThemeChange();
        }

        private void BtnPickPlayColor_Click(object sender, RoutedEventArgs e)
        {
            if (!TryPickColor(settings.AccentColor, out string hex)) return;
            settings.AccentColor = hex;
            SaveSettings();
            RefreshAfterThemeChange();
        }

        private void BtnResetTheme_Click(object sender, RoutedEventArgs e)
        {
            settings.BackgroundColor = "#0F111A";
            settings.AccentColor = "#8B5CF6";
            SaveSettings();
            RefreshAfterThemeChange();
        }

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isLoadingSettings) return;

            ReadSettingsFromControls();
            UpdateSliderLabels();

            // Erst anwenden, wenn der Regler kurz still steht
            applyTimer.Stop();
            applyTimer.Start();
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            ReadSettingsFromControls();
            SaveSettings();
            ApplyViewSettings();
            ApplyFilter();
            RefreshDashboard();
            UpdateClock();

            if (ReferenceEquals(sender, ChkOled))
            {
                RebuildStaticTiles();
                RefreshBoard();
                BuildCollectionChips();
            }
            if (ReferenceEquals(sender, ChkSensors)) ApplySensorSettings();

            if (ReferenceEquals(sender, ChkSgdbAnimated))
            {
                if (settings.SgdbAnimated)
                {
                    settings.AnimatedChecked.Clear();
                    ShowToast("🖼", "Animierte Cover", "Die animierten Cover werden im Hintergrund geladen.", 6);
                    _ = UpgradeToAnimatedCoversAsync();
                }
                else
                {
                    ShowToast("🖼", "Cover-Quelle geändert", "Klicke auf „Cover neu laden“, damit die Änderung greift.", 6);
                }
            }

            if (ReferenceEquals(sender, ChkDeals) && settings.ShowDeals) _ = RefreshDealsAsync(true);

            if (ReferenceEquals(sender, ChkMusic) || ReferenceEquals(sender, ChkQuickButtons))
            {
                if (!settings.MusicPlayer) MusicPanel.Visibility = Visibility.Collapsed;
                RenderSidebarExtras();
            }

            if (ReferenceEquals(sender, ChkPowerButtons) || ReferenceEquals(sender, ChkPowerConfirm)) RebuildStaticTiles();

            try
            {
                ApplyOverlayLook();
                UpdateOverlayVisibility();
            }
            catch { }

            if (ReferenceEquals(sender, ChkSoftware))
                Msg("Die Änderung wird nach einem Neustart des Launchers wirksam.");

            if (ReferenceEquals(sender, ChkShowWeather) || ReferenceEquals(sender, ChkFahrenheit) || ReferenceEquals(sender, ChkOnline))
                _ = RefreshWeatherAsync();
        }

        private void ChkAutostart_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            try
            {
                bool on = ChkAutostart.IsChecked == true;
                // Ohne Autostart gibt es auch keinen Konsolen-Start
                if (!on && settings.ConsoleStart)
                {
                    settings.ConsoleStart = false;
                    SaveSettings();
                    PopulateControllerExtras();
                }
                SetAutostart(on, settings.ConsoleStart);
            }
            catch (Exception ex)
            {
                Msg($"Autostart konnte nicht geändert werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnApplyCity_Click(object sender, RoutedEventArgs e)
        {
            settings.WeatherCity = TxtWeatherCity.Text.Trim();
            weatherLocation = null;
            SaveSettings();
            _ = RefreshWeatherAsync();
        }

        private async void BtnReloadCovers_Click(object sender, RoutedEventArgs e)
        {
            var answer = Msg("Alle gespeicherten Cover löschen und neu herunterladen?", "Cover neu laden",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            try { if (Directory.Exists(CoversDir)) Directory.Delete(CoversDir, true); } catch { }

            lock (settings.SteamIds)
            {
                foreach (var key in settings.SteamIds.Where(p => p.Value <= 0).Select(p => p.Key).ToList())
                    settings.SteamIds.Remove(key);
            }

            imageCache.Clear();
            settings.AnimatedChecked.Clear();
            foreach (var game in allGames) game.CoverPath = string.Empty;
            ApplyFilter();

            await DownloadCoversAsync(allGames);
        }

        private void BtnResetHistory_Click(object sender, RoutedEventArgs e)
        {
            var answer = Msg("Verlauf (Zuletzt gespielt, Startzähler und Spielzeit) wirklich zurücksetzen?\nFavoriten, Bewertungen und Notizen bleiben erhalten.",
                "Verlauf zurücksetzen", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            foreach (var state in settings.GameStates.Values)
            {
                state.LastPlayed = null;
                state.LaunchCount = 0;
                state.PlaySeconds = 0;
            }
            foreach (var game in allGames)
            {
                game.LastPlayed = null;
                game.LaunchCount = 0;
                game.PlaySeconds = 0;
            }
            settings.PlayLog.Clear();

            SaveSettings();
            ApplyFilter();
            RefreshDashboard();
        }

        private void BtnOpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(SettingsDir);
            OpenShell(SettingsDir);
        }

        // ───────────────────────────── Aussehen: Theme, Hintergrund, Sidebar ─────────────────────────────

        private static bool SupportsGlass() => Environment.OSVersion.Version.Build >= 22621;

        private bool HasBackgroundImage()
            => !string.IsNullOrEmpty(settings.BackgroundImagePath) && File.Exists(settings.BackgroundImagePath);

        private void ApplyTheme()
        {
            ApplySurfaceBrushes();
            bool glass = settings.GlassEffect && SupportsGlass();
            bool translucent = glass || HasBackgroundImage();
            if (TopScrim != null) TopScrim.Visibility = translucent ? Visibility.Visible : Visibility.Collapsed;

            Resources["AppBackgroundBrush"] = glass
                ? MakeAlphaBrush(EffectiveBackground(), 0xC8)
                : MakeBrush(EffectiveBackground(), "#0F111A");
            Resources["AccentBrush"] = MakeBrush(settings.AccentColor, "#8B5CF6");
            if (System.Windows.Application.Current != null)   // für Rechtsklick-Menüs außerhalb des Fensters (zum Beispiel in Textfeldern)
                System.Windows.Application.Current.Resources["AccentBrush"] = Resources["AccentBrush"];
            Resources["SidebarBrush"] = MakeAlphaBrush(SidebarBaseColor(), translucent ? (byte)0xD2 : (byte)0xFF);
            ApplyNavBrushes();

            if (BackgroundGlow != null) ApplyGlowBackground();
            UpdateRgbAccent();
        }

        private void ApplyAppearance()
        {
            ApplyTheme();
            ApplyBackgroundImage();
            ApplyGlowBackground();
            ApplySidebar(false);
            ApplyNavVisibility();
            ApplyWindowEffects();
            RegisterGlobalHotkey();
            ApplyPixelShift();

            if (trayIcon != null) trayIcon.Visible = settings.ShowTrayIcon || settings.MinimizeToTray;
        }

        private void ApplyBackgroundImage()
        {
            if (!HasBackgroundImage())
            {
                BackgroundImage.Source = null;
                BackgroundImage.Visibility = Visibility.Collapsed;
                BackgroundDim.Visibility = Visibility.Collapsed;
                loadedBackgroundPath = string.Empty;
                return;
            }

            if (loadedBackgroundPath != settings.BackgroundImagePath)
            {
                try
                {
                    BackgroundImage.Source = LoadBackgroundBitmap(settings.BackgroundImagePath);
                    loadedBackgroundPath = settings.BackgroundImagePath;
                }
                catch
                {
                    BackgroundImage.Source = null;
                    loadedBackgroundPath = string.Empty;
                }
            }

            BackgroundImage.Visibility = Visibility.Visible;
            BackgroundImage.Margin = new Thickness(-settings.BgBlur);
            BackgroundImage.Effect = settings.BgBlur > 0
                ? new System.Windows.Media.Effects.BlurEffect { Radius = settings.BgBlur }
                : null;

            BackgroundDim.Visibility = Visibility.Visible;
            BackgroundDim.Opacity = settings.BgDim / 100.0;
        }

        private void ApplyGlowBackground()
        {
            if (!settings.AnimatedBackground)
            {
                BackgroundGlow.Visibility = Visibility.Collapsed;
                BackgroundGlow.Background = null;
                return;
            }

            var accent = GetAccentColor();
            var brush = new RadialGradientBrush
            {
                RadiusX = 0.75,
                RadiusY = 0.75,
                Center = new System.Windows.Point(0.25, 0.3),
                GradientOrigin = new System.Windows.Point(0.25, 0.3)
            };
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x3C, accent.R, accent.G, accent.B), 0));
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1));

            BackgroundGlow.Background = brush;
            BackgroundGlow.Visibility = Visibility.Visible;

            var move = new PointAnimation(new System.Windows.Point(0.2, 0.25), new System.Windows.Point(0.85, 0.75), TimeSpan.FromSeconds(16))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            };
            brush.BeginAnimation(RadialGradientBrush.CenterProperty, move);
            brush.BeginAnimation(RadialGradientBrush.GradientOriginProperty, move);
        }

        /// <summary>Farbe hinter dem Fenster. Sie scheint an Rändern durch, die das Programm nicht überdeckt (zum Beispiel eine Pixelreihe ganz oben).</summary>
        private System.Windows.Media.Color WindowBackdropColor()
        {
            try
            {
                return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(EffectiveBackground());
            }
            catch
            {
                return System.Windows.Media.Color.FromRgb(0x0F, 0x11, 0x1A);
            }
        }

        private void ApplyWindowEffects()
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            try
            {
                int dark = 1;
                NativeFeatures.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // dunkle Titelleiste

                int noBorder = unchecked((int)0xFFFFFFFE);                              // Windows 11: keinen hellen Fensterrand zeichnen
                NativeFeatures.DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int));

                bool glass = settings.GlassEffect && SupportsGlass();
                int backdrop = glass ? 2 : 1; // 2 = Mica, 1 = aus
                NativeFeatures.DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));

                var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                if (source?.CompositionTarget != null)
                {
                    source.CompositionTarget.BackgroundColor = glass
                        ? System.Windows.Media.Colors.Transparent
                        : WindowBackdropColor();
                }

                var margins = glass
                    ? new NativeFeatures.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 }
                    : new NativeFeatures.Margins();
                NativeFeatures.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            }
            catch { }
        }

        private void ApplySidebar(bool animate)
        {
            ApplySidebarPosition(animate);

            bool collapsed = settings.SidebarCollapsed;
            bool bottomNav = IsBottomNav;
            double target = controllerMode || bottomNav ? 0 : collapsed ? 90 : settings.SidebarWidth;
            var textVisibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            bool iconsOnly = collapsed || bottomNav;   // unten: nur Symbole, der Name steht im Hinweis
            if (SidebarExtras != null && sidebarExtrasCompact != collapsed) RenderSidebarExtras();

            BrandText.Visibility = textVisibility;
            BrandTextHost.Visibility = textVisibility;
            UserCard.Visibility = textVisibility;
            SidebarFooterText.Visibility = textVisibility;
            SidebarCredit.Visibility = textVisibility;
            ApplyBrand();
            // Pfeil zeigt in die Richtung, in die die Leiste klappt (rechts ist es spiegelverkehrt)
            BtnSidebar.Content = collapsed != (NavPosition == "right") ? "»" : "«";

            foreach (var item in NavPanel.Children.OfType<System.Windows.Controls.RadioButton>())
            {
                if (item.Content is StackPanel panel && panel.Children.Count > 1)
                {
                    panel.Children[1].Visibility = iconsOnly ? Visibility.Collapsed : Visibility.Visible;
                    // Name als Hinweis-Blase im Launcher-Design (folgt der übersetzten Beschriftung)
                    if (iconsOnly && panel.Children[1] is TextBlock label)
                    {
                        var text = new TextBlock();
                        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = label });
                        item.ToolTip = MakeNavToolTip(text, bottomNav);
                        System.Windows.Controls.ToolTipService.SetInitialShowDelay(item, bottomNav ? 120 : 300);
                        System.Windows.Controls.ToolTipService.SetBetweenShowDelay(item, 0);
                    }
                    else item.ToolTip = null;
                }
            }

            if (animate && settings.PageAnimations)
                Tween(ColSidebar.Width.Value, target, 240, value => ColSidebar.Width = new GridLength(value));
            else
                ColSidebar.Width = new GridLength(target);
        }

        private void BtnSidebar_Click(object sender, RoutedEventArgs e)
        {
            settings.SidebarCollapsed = !settings.SidebarCollapsed;
            SaveSettings();
            ApplySidebar(true);
        }

        private void BuildThemePresets()
        {
            ThemePresetsPanel.Children.Clear();

            foreach (var preset in ThemePresets)
            {
                var swatch = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    BorderBrush = BrushCardBorder,
                    BorderThickness = new Thickness(1),
                    Background = new LinearGradientBrush(ParseColor(preset.Bg, "#0F111A"), ParseColor(preset.Accent, "#8B5CF6"), 45)
                };

                var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                content.Children.Add(swatch);
                content.Children.Add(new TextBlock
                {
                    Text = preset.Name,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                });

                var button = new System.Windows.Controls.Button
                {
                    Content = content,
                    Background = MakeBrush("#1F2937"),
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(12, 8, 14, 8)
                };

                string bg = preset.Bg;
                string accent = preset.Accent;
                button.Click += (s, e) =>
                {
                    settings.BackgroundColor = bg;
                    settings.AccentColor = accent;
                    SaveSettings();
                    RefreshAfterThemeChange();
                };

                ThemePresetsPanel.Children.Add(button);
            }
        }

        private void BtnPickBgImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Bilder (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Hintergrundbild wählen"
            };
            if (dialog.ShowDialog() != true) return;

            SetBackgroundFromFile(dialog.FileName);   // danach Ausschnitt wählen
        }

        private void BtnClearBgImage_Click(object sender, RoutedEventArgs e)
        {
            settings.BackgroundImagePath = string.Empty;
            loadedBackgroundPath = string.Empty;
            SaveSettings();
            ApplyViewSettings();
        }

        // ───────────────────────────── Einstellungen (neue Optionen) ─────────────────────────────

        private void PopulateFeatureSettings()
        {
            SliderBgDim.Value = settings.BgDim;
            SliderBgBlur.Value = settings.BgBlur;

            ChkPageAnim.IsChecked = settings.PageAnimations;
            ChkCardAnim.IsChecked = settings.CardAnimations;
            ChkSmooth.IsChecked = settings.SmoothMetrics;
            ChkGlow.IsChecked = settings.HoverGlow;
            ChkAnimBg.IsChecked = settings.AnimatedBackground;
            ChkGlass.IsChecked = settings.GlassEffect;
            ChkHero.IsChecked = settings.ShowHero;

            ChipLayoutCards.IsChecked = settings.CardLayout != "detail" && settings.CardLayout != "list";
            ChipLayoutDetail.IsChecked = settings.CardLayout == "detail";
            ChipLayoutList.IsChecked = settings.CardLayout == "list";


            ChkTrack.IsChecked = settings.TrackPlaytime;
            ChkProfile.IsChecked = settings.GamingProfile;
            ChkOverlay.IsChecked = settings.ShowOverlay;
            ChipOv0.IsChecked = settings.OverlayCorner == 0;
            ChipOv1.IsChecked = settings.OverlayCorner == 1;
            ChipOv2.IsChecked = settings.OverlayCorner == 2;
            ChipOv3.IsChecked = settings.OverlayCorner == 3;
            ChkController.IsChecked = settings.ControllerSupport;
            ChkTray.IsChecked = settings.MinimizeToTray;
            ChkStartMin.IsChecked = settings.StartMinimized;
        }

        private void ReadFeatureSettings()
        {
            settings.BgDim = (int)Math.Round(SliderBgDim.Value);
            settings.BgBlur = (int)Math.Round(SliderBgBlur.Value);

            settings.PageAnimations = ChkPageAnim.IsChecked == true;
            settings.CardAnimations = ChkCardAnim.IsChecked == true;
            settings.SmoothMetrics = ChkSmooth.IsChecked == true;
            settings.HoverGlow = ChkGlow.IsChecked == true;
            settings.AnimatedBackground = ChkAnimBg.IsChecked == true;
            settings.GlassEffect = ChkGlass.IsChecked == true;
            settings.ShowHero = ChkHero.IsChecked == true;

            settings.CardLayout = ChipLayoutList.IsChecked == true ? "list"
                                : ChipLayoutDetail.IsChecked == true ? "detail"
                                : "cards";


            settings.TrackPlaytime = ChkTrack.IsChecked == true;
            settings.GamingProfile = ChkProfile.IsChecked == true;
            settings.ShowOverlay = ChkOverlay.IsChecked == true;
            settings.OverlayCorner = ChipOv0.IsChecked == true ? 0
                                   : ChipOv2.IsChecked == true ? 2
                                   : ChipOv3.IsChecked == true ? 3
                                   : 1;
            settings.ControllerSupport = ChkController.IsChecked == true;
            settings.MinimizeToTray = ChkTray.IsChecked == true;
            settings.StartMinimized = ChkStartMin.IsChecked == true;
        }

        private void UpdateFeatureSliderLabels()
        {
            TxtBgDimValue.Text = $"{settings.BgDim} %";
            TxtBgBlurValue.Text = $"{settings.BgBlur} px";
        }

        private void BtnTestOverlay_Click(object sender, RoutedEventArgs e)
        {
            overlayTestUntil = DateTime.Now.AddSeconds(8);
            UpdateOverlayVisibility();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8.5) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                UpdateOverlayVisibility();
            };
            timer.Start();
        }

        private const string SettingsExportFormat = "DFP-Pro-Launcher-Einstellungen";
        private const int SettingsExportFormatVersion = 1;

        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"DFP_Pro_Launcher_Einstellungen_{DateTime.Now:yyyy-MM-dd}.json",
                Filter = Loc.T("Einstellungen") + " (*.json)|*.json"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                SaveSettings();
                var file = new SettingsExportFile
                {
                    Format = SettingsExportFormat,
                    FormatVersion = SettingsExportFormatVersion,
                    AppVersion = VersionText(),
                    Exported = DateTime.Now,
                    Settings = settings
                };

                string json;
                lock (settings.SteamIds) json = JsonSerializer.Serialize(file, JsonOptions);
                File.WriteAllText(dialog.FileName, json);
                Msg("Die Einstellungen wurden gesichert.");
            }
            catch (Exception ex)
            {
                Msg($"Sichern fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private sealed class SettingsImport
        {
            public AppSettings Settings = new();
            public string AppVersion = string.Empty;   // leer = alte Datei ohne Versionsangabe
            public DateTime? Exported;
            public bool NewerFormat;
        }

        /// <summary>Liest eine exportierte Datei (neues Format mit Versionsangabe oder eine alte settings.json).</summary>
        private static SettingsImport ReadSettingsImport(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException(Loc.T("Die Datei enthält keine gültigen Einstellungen."));

            if (root.TryGetProperty("Format", out var format) && format.ValueKind == JsonValueKind.String
                && format.GetString() == SettingsExportFormat)
            {
                var file = root.Deserialize<SettingsExportFile>();
                if (file?.Settings == null) throw new InvalidDataException(Loc.T("Die Datei enthält keine gültigen Einstellungen."));
                return new SettingsImport
                {
                    Settings = file.Settings,
                    AppVersion = file.AppVersion ?? string.Empty,
                    Exported = file.Exported == default ? null : file.Exported,
                    NewerFormat = file.FormatVersion > SettingsExportFormatVersion
                };
            }

            // Alte Sicherung: direkt eine settings.json. Ein paar typische Einträge müssen vorhanden sein.
            if (!root.TryGetProperty("UserName", out _) && !root.TryGetProperty("AccentColor", out _) && !root.TryGetProperty("GameStates", out _))
                throw new InvalidDataException(Loc.T("Die Datei enthält keine gültigen Einstellungen."));

            var plain = root.Deserialize<AppSettings>() ?? throw new InvalidDataException(Loc.T("Die Datei enthält keine gültigen Einstellungen."));
            return new SettingsImport { Settings = plain };
        }

        /// <summary>Zählt die Einstellungen, die sich durch den Import ändern würden (ohne Fensterposition).</summary>
        private int CountChangedSettings(AppSettings incoming)
        {
            try
            {
                string currentJson, incomingJson;
                lock (settings.SteamIds) currentJson = JsonSerializer.Serialize(settings);
                incomingJson = JsonSerializer.Serialize(incoming);

                using var current = JsonDocument.Parse(currentJson);
                using var next = JsonDocument.Parse(incomingJson);
                int changed = 0;
                foreach (var property in next.RootElement.EnumerateObject())
                {
                    if (property.Name.StartsWith("Win", StringComparison.Ordinal)) continue;
                    if (!current.RootElement.TryGetProperty(property.Name, out var old) || old.GetRawText() != property.Value.GetRawText())
                        changed++;
                }
                return changed;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Vorschau „Das wird übernommen“. Gibt true zurück, wenn der Nutzer den Import bestätigt.</summary>
        private bool ShowImportPreview(SettingsImport import)
        {
            var dialog = CreateDialog("Einstellungen laden", 560, out var panel);
            var incoming = import.Settings;

            panel.Children.Add(new TextBlock { Text = Loc.T("Das wird übernommen"), Foreground = System.Windows.Media.Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });

            void Line(string text, string color = "#D1D5DB")
                => panel.Children.Add(new TextBlock { Text = text, Foreground = MakeBrush(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

            if (import.AppVersion.Length > 0)
            {
                string when = import.Exported is DateTime date ? date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : "?";
                Line(Loc.T($"Gesichert mit Version {import.AppVersion} am {when}"), "#9CA3AF");
            }
            else
            {
                Line(Loc.T("Ältere Sicherungsdatei ohne Versionsangabe"), "#9CA3AF");
            }

            bool newerVersion = TryParseAppVersion(import.AppVersion, out var fileVersion) && CompareAppVersions(fileVersion, CurrentAppVersion()) > 0;
            if (newerVersion || import.NewerFormat)
                Line(Loc.T($"Die Datei stammt aus einer neueren Version ({import.AppVersion}). Einstellungen, die diese Version noch nicht kennt, werden übersprungen."), "#F59E0B");

            panel.Children.Add(new Border { Height = 1, Background = MakeBrush("#26FFFFFF"), Margin = new Thickness(0, 6, 0, 12) });

            Line("• " + Loc.T($"Spiele mit Spielzeit, Favoriten oder Notizen: {incoming.GameStates?.Count ?? 0}"));
            Line("• " + Loc.T($"Manuell hinzugefügte Spiele: {incoming.ManualGames?.Count ?? 0}"));
            Line("• " + Loc.T($"Schnellzugriffe und Links: {(incoming.QuickLinks?.Count ?? 0) + (incoming.StreamerLinks?.Count ?? 0)}"));
            Line("• " + Loc.T($"Abläufe: {incoming.Routines?.Count ?? 0}"));
            Line("• " + Loc.T($"Wunschliste: {incoming.Wishlist?.Count ?? 0}"));
            Line("• " + Loc.T($"Einstellungs-Profile: {incoming.Profiles?.Count ?? 0}"));
            Line("• " + Loc.T($"Geänderte Einstellungen insgesamt: {CountChangedSettings(incoming)}"));

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Alles wird durch den Inhalt der Datei ersetzt, auch Spielzeiten. Deine aktuellen Einstellungen werden vorher automatisch gesichert. Danach startet der Launcher neu."),
                Foreground = BrushSubtle,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 18)
            });

            bool confirmed = false;
            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var cancel = new System.Windows.Controls.Button { Content = "Abbrechen", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var ok = new System.Windows.Controls.Button { Content = "Übernehmen und neu starten", Padding = new Thickness(20, 8, 20, 8) };
            ok.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            ok.Click += (s, e) =>
            {
                confirmed = true;
                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            dialog.ShowDialog();
            return confirmed;
        }

        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            if (activeSessions.Count > 0)
            {
                Msg("Es läuft noch ein Spiel. Lade die Einstellungen bitte nach dem Spiel.", "Einstellungen laden");
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = Loc.T("Einstellungen") + " (*.json)|*.json",
                Title = Loc.T("Einstellungen laden")
            };
            if (dialog.ShowDialog() != true) return;

            SettingsImport import;
            try
            {
                import = ReadSettingsImport(dialog.FileName);
            }
            catch (Exception ex)
            {
                string reason = ex is JsonException ? Loc.T("Die Datei enthält keine gültigen Einstellungen.") : ex.Message;
                Msg($"Laden fehlgeschlagen:\n{reason}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!ShowImportPreview(import)) return;

            try
            {
                // Vorher den aktuellen Stand sichern
                SaveSettings();
                CreateSettingsBackup("vor-import");

                Directory.CreateDirectory(SettingsDir);
                string temp = SettingsFilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(import.Settings, JsonOptions));
                File.Move(temp, SettingsFilePath, true);

                // Ab jetzt nichts mehr speichern, sonst würde der alte Stand beim Beenden die neue Datei überschreiben
                settingsSaveBlocked = true;
            }
            catch (Exception ex)
            {
                Msg($"Laden fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RestartLauncher();
        }

        private void RestartLauncher()
        {
            try
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException();

                Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = true });
                ExitApplication();
            }
            catch
            {
                Msg("Bitte starte den Launcher jetzt neu, damit die geladenen Einstellungen übernommen werden.", "Einstellungen laden");
            }
        }

        private void SaveLogoIcon(string path)
        {
            var bitmap = CreateLogoBitmap(256);
            using var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(png);
            byte[] data = png.ToArray();

            using var file = File.Create(path);
            using var writer = new BinaryWriter(file);
            writer.Write((ushort)0);          // reserviert
            writer.Write((ushort)1);          // Typ: Symbol
            writer.Write((ushort)1);          // Anzahl Bilder
            writer.Write((byte)0);            // Breite 256
            writer.Write((byte)0);            // Höhe 256
            writer.Write((byte)0);            // Farben
            writer.Write((byte)0);            // reserviert
            writer.Write((ushort)1);          // Ebenen
            writer.Write((ushort)32);         // Bits pro Pixel
            writer.Write((uint)data.Length);  // Größe
            writer.Write((uint)22);           // Startposition der Bilddaten
            writer.Write(data);
        }

        private void BtnExportLogo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "DFP_Pro_Launcher.ico",
                Filter = "Symbol (*.ico)|*.ico"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                SaveLogoIcon(dialog.FileName);
                Msg("Das Logo wurde als Symbol gespeichert.");
            }
            catch (Exception ex)
            {
                Msg($"Speichern fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCreateShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("Der Programmpfad ist unbekannt.");

                Directory.CreateDirectory(SettingsDir);
                string icon = System.IO.Path.Combine(SettingsDir, "DFP_Pro_Launcher.ico");
                SaveLogoIcon(icon);

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string link = System.IO.Path.Combine(desktop, "DFP Pro Launcher.lnk");

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new InvalidOperationException("Windows-Skripthost nicht verfügbar.");

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(link);
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(exe);
                shortcut.IconLocation = icon;
                shortcut.Description = "DFP Pro Launcher";
                shortcut.Save();

                Msg("Die Verknüpfung mit deinem Logo wurde auf dem Desktop erstellt.");
            }
            catch (Exception ex)
            {
                Msg($"Verknüpfung fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────────────────────────── Sprache ─────────────────────────────

        private static string SearchPlaceholder => Loc.T("Spiel suchen...");

        private static string ResolveLanguage(string setting)
            => Loc.Languages.Any(l => l.Code == setting) ? setting : Loc.Detect();

        private void InitLanguage()
        {
            Loc.SetLanguage(ResolveLanguage(settings.Language));
            TxtSearch.Text = SearchPlaceholder;

            translateTimer.Interval = TimeSpan.FromMilliseconds(60);
            translateTimer.Tick += (s, e) =>
            {
                translateTimer.Stop();
                RunTranslationPass();
            };

            // Jede Änderung der Oberfläche löst (leicht verzögert) eine neue Übersetzung aus
            LayoutUpdated += (s, e) =>
            {
                if (Loc.Language == "de" && !germanRestorePending) return;
                translateTimer.Stop();
                translateTimer.Start();
            };

            translateTimer.Start();
        }

        private void RunTranslationPass()
        {
            // Im Controller-Modus ist die normale Oberfläche ausgeblendet: nur die Controller-Ebene übersetzen,
            // damit der Durchlauf den Oberflächen-Thread nicht lange blockiert (Eingabe-Verzögerung)
            if (controllerMode && padLayer != null)
            {
                TranslateTree(padLayer);
                TranslateTree(ToastHost);
            }
            else TranslateTree(this);
            if (Loc.Language == "de") germanRestorePending = false;
        }

        private void TranslateProperty(DependencyObject element, DependencyProperty property, string current,
            System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, TextState> table)
        {
            if (current.Length == 0) return;

            var state = table.GetOrCreateValue(element);
            if (state.Version == Loc.Version && current == state.Shown) return;   // bereits übersetzt

            if (current != state.Shown) state.Original = current;                 // neuer Text von außen

            string translated = Loc.T(state.Original);
            state.Shown = translated;
            state.Version = Loc.Version;

            if (translated != current) element.SetCurrentValue(property, translated);
        }

        private void TranslateNode(DependencyObject node)
        {
            switch (node)
            {
                case TextBlock textBlock:
                    TranslateProperty(textBlock, TextBlock.TextProperty, textBlock.Text, textStates);
                    break;
                case ContentControl control when control.Content is string content:
                    TranslateProperty(control, ContentControl.ContentProperty, content, textStates);
                    break;
            }

            if (node is FrameworkElement element && element.ToolTip is string tip)
                TranslateProperty(element, FrameworkElement.ToolTipProperty, tip, tipStates);
        }

        private void TranslateTree(DependencyObject? node)
        {
            if (node == null) return;

            TranslateNode(node);
            if (node is not Visual) return;

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                TranslateTree(VisualTreeHelper.GetChild(node, i));

            if (count == 0 && node is ContentControl control && control.Content is DependencyObject content)
                TranslateTree(content);
        }

        private string ReadLanguageChip()
        {
            if (ChipLangDe.IsChecked == true) return "de";
            if (ChipLangEn.IsChecked == true) return "en";
            if (ChipLangZh.IsChecked == true) return "zh";
            if (ChipLangEs.IsChecked == true) return "es";
            if (ChipLangFr.IsChecked == true) return "fr";
            if (ChipLangPt.IsChecked == true) return "pt";
            if (ChipLangRu.IsChecked == true) return "ru";
            if (ChipLangJa.IsChecked == true) return "ja";
            return "auto";
        }

        private void PopulateLanguageChips()
        {
            string code = settings.Language;
            ChipLangAuto.IsChecked = code == "auto" || !Loc.Languages.Any(l => l.Code == code);
            ChipLangDe.IsChecked = code == "de";
            ChipLangEn.IsChecked = code == "en";
            ChipLangZh.IsChecked = code == "zh";
            ChipLangEs.IsChecked = code == "es";
            ChipLangFr.IsChecked = code == "fr";
            ChipLangPt.IsChecked = code == "pt";
            ChipLangRu.IsChecked = code == "ru";
            ChipLangJa.IsChecked = code == "ja";
        }

        private void Language_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            string code = ReadLanguageChip();
            if (code == settings.Language) return;
            ApplyLanguage(code);
        }

        private void ApplyLanguage(string setting)
        {
            string oldPlaceholder = SearchPlaceholder;

            settings.Language = setting;
            Loc.SetLanguage(ResolveLanguage(setting));
            SaveSettings();

            if (TxtSearch.Text == oldPlaceholder) TxtSearch.Text = SearchPlaceholder;

            germanRestorePending = true;
            RebuildAfterLanguageChange();
            RunTranslationPass();
        }

        private void RebuildAfterLanguageChange()
        {
            ApplyFilter();
            RefreshDashboard();
            RebuildStaticTiles();
            RefreshApps();
            RefreshBoard();
            RenderDeals();
            UpdateClock();
            UpdateSliderLabels();
            RebuildTrayMenu();
            BuildSchemeTiles();
            BuildDashWidgetSettings();
            RenderWishlist();
            ApplyUpdateUi();
            UpdatePlanTiles(currentPowerPlanName);
            if (ViewOptimization.Visibility == Visibility.Visible)
            {
                RenderCleanup();
                _ = MeasureCleanupAsync();
            }

            if (ViewStats.Visibility == Visibility.Visible) RefreshStats(false);
            if (ViewDownloads.Visibility == Visibility.Visible) RefreshDownloads();
            if (ViewStorage.Visibility == Visibility.Visible) RefreshDrives();

            _ = RefreshWeatherAsync();
        }

        private void ReadComfortSettings()
        {
            settings.CloseAppsEnabled = ChkCloseApps.IsChecked == true;
            settings.AutoRescan = ChkAutoRescan.IsChecked == true;
            settings.GlobalHotkey = ChkHotkey.IsChecked == true;
        }

        private void PopulateComfortSettings()
        {
            ChkCloseApps.IsChecked = settings.CloseAppsEnabled;
            ChkAutoRescan.IsChecked = settings.AutoRescan;
            ChkHotkey.IsChecked = settings.GlobalHotkey;
            PopulateLanguageChips();
        }

        private void InitComfort()
        {
            StartSingleInstanceListener();
            SourceInitialized += (s, e) => RegisterGlobalHotkey();
            Activated += OnWindowActivated;
            Closed += (s, e) =>
            {
                UnregisterGlobalHotkey();
                try { singleInstanceMutex?.ReleaseMutex(); } catch { }
            };

            UpdateDriverInfo();
        }

        // ───────────────────────────── Leistung für ältere PCs ─────────────────────────────

        private void ApplyPerformancePreset(bool on)
        {
            settings.PerformanceMode = on;
            settings.PageAnimations = !on;
            settings.CardAnimations = !on;
            settings.SmoothMetrics = !on;
            settings.HoverGlow = !on;
            settings.HoverAnimations = !on;
            settings.AnimatedBackground = false;
            settings.ShowSparklines = !on;
            settings.FullResCovers = !on;
            settings.AlwaysPlayGifs = false;
            settings.MonitorIntervalMs = on ? 2000 : 1000;
            if (on)
            {
                settings.BgBlur = 0;
                settings.GlassEffect = false;
                settings.ClockSeconds = false;
            }
        }

        private void ChkPerf_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            ApplyPerformancePreset(ChkPerf.IsChecked == true);

            isLoadingSettings = true;
            try { PopulateControlsFromSettings(); }
            finally { isLoadingSettings = false; }

            SaveSettings();
            imageCache.Clear();
            gifCache.Clear();
            ApplyViewSettings();
            ApplyFilter();
            RefreshDashboard();

            ShowToast("⚡", settings.PerformanceMode ? "Performance-Modus aktiv" : "Performance-Modus aus",
                settings.PerformanceMode ? "Animationen und Effekte sind abgeschaltet." : "Die Standard-Einstellungen sind wieder aktiv.", 5);
        }

        private void UpdateTimersForVisibility()
        {
            bool hidden = !IsVisible || WindowState == WindowState.Minimized;
            bool pause = settings.PauseWhenMinimized && hidden;

            if (pause)
            {
                ringTimer.Stop();
                clockTimer.Stop();
            }
            else
            {
                if (!ringTimer.IsEnabled) ringTimer.Start();
                if (!clockTimer.IsEnabled) clockTimer.Start();
            }
        }

        // ───────────────────────────── OLED-Modus ─────────────────────────────

        private string EffectiveBackground() => settings.OledMode ? "#000000" : settings.BackgroundColor;

        private void ApplySurfaceBrushes()
        {
            bool oled = settings.OledMode;

            BrushCardBg = MakeBrush(oled ? "#0A0A0D" : "#131722");
            BrushCardBorder = MakeBrush(oled ? "#1A1A20" : "#1F2937");
            BrushFooter = MakeBrush(oled ? "#0F0F13" : "#181D2A");
            BrushTileHover = MakeBrush(oled ? "#17171D" : "#1B2132");

            Resources["CardBrush"] = BrushCardBg;
            Resources["CardBorderBrush"] = BrushCardBorder;
            Resources["SurfaceBrush"] = BrushFooter;
        }

        private void RebuildStaticTiles()
        {
            ToolsContainer.Children.Clear();
            BuildToolTiles();
            BuildLinkTiles();
            RefreshApps();
            RenderSidebarExtras();
            RenderRoutines();
        }

        private void AnimateShift(double x, double y)
        {
            if (RootGrid.RenderTransform is not TranslateTransform shift)
            {
                shift = new TranslateTransform();
                RootGrid.RenderTransform = shift;
            }

            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromSeconds(2)));
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromSeconds(2)));
        }

        /// <summary>Burn-in-Schutz: Die Oberfläche wandert alle paar Minuten um wenige Pixel.</summary>
        private void ApplyPixelShift()
        {
            if (settings.OledMode && settings.OledPixelShift)
            {
                if (!shiftTimer.IsEnabled) shiftTimer.Start();
            }
            else
            {
                shiftTimer.Stop();
                if (RootGrid.RenderTransform is TranslateTransform) AnimateShift(0, 0);
            }
        }

        // ───────────────────────────── Einstellungen: Anpassung, Leistung, Controller ─────────────────────────────

        private void ReadExtra3Settings()
        {
            settings.CardSpacing = (int)Math.Round(SliderSpacing.Value);
            settings.CardAspect = (int)Math.Round(SliderAspect.Value);
            settings.SidebarWidth = (int)Math.Round(SliderSidebarW.Value);

            settings.OledMode = ChkOled.IsChecked == true;
            settings.OledPixelShift = ChkOledShift.IsChecked == true;
            settings.PerformanceMode = ChkPerf.IsChecked == true;
            settings.SoftwareRendering = ChkSoftware.IsChecked == true;
            settings.PauseWhenMinimized = ChkPauseMin.IsChecked == true;
            settings.AlwaysPlayGifs = ChkGifAlways.IsChecked == true;

            settings.ControllerLayout = ChipPadXbox.IsChecked == true ? "xbox"
                                      : ChipPadPs.IsChecked == true ? "ps"
                                      : ChipPadNintendo.IsChecked == true ? "nintendo"
                                      : "auto";
        }

        private void PopulateExtra3Settings()
        {
            SliderSpacing.Value = settings.CardSpacing;
            SliderAspect.Value = settings.CardAspect;
            SliderSidebarW.Value = settings.SidebarWidth;

            ChkOled.IsChecked = settings.OledMode;
            ChkOledShift.IsChecked = settings.OledPixelShift;
            ChkPerf.IsChecked = settings.PerformanceMode;
            ChkSoftware.IsChecked = settings.SoftwareRendering;
            ChkPauseMin.IsChecked = settings.PauseWhenMinimized;
            ChkGifAlways.IsChecked = settings.AlwaysPlayGifs;

            ChipPadAuto.IsChecked = settings.ControllerLayout is not ("xbox" or "ps" or "nintendo");
            ChipPadXbox.IsChecked = settings.ControllerLayout == "xbox";
            ChipPadPs.IsChecked = settings.ControllerLayout == "ps";
            ChipPadNintendo.IsChecked = settings.ControllerLayout == "nintendo";
        }

        private void UpdateExtra3SliderLabels()
        {
            TxtSpacingValue.Text = $"{settings.CardSpacing} px";
            TxtAspectValue.Text = $"{settings.CardAspect} %";
            TxtSidebarWValue.Text = $"{settings.SidebarWidth} px";
        }

        // ───────────────────────────── Einstellungen: Kategorien ─────────────────────────────

        private void ApplySettingsCategory(string category)
        {
            if (SettingsCards == null) return;

            settingsCategory = category;
            if (TxtSettingsSearch != null && TxtSettingsSearch.Text.Trim().Length > 0)
            {
                FilterSettings(TxtSettingsSearch.Text.Trim());
                return;
            }

            foreach (var card in SettingsCards.Children.OfType<Border>())
                card.Visibility = card.Tag as string == category ? Visibility.Visible : Visibility.Collapsed;

            if (category == "perf") UpdateMemoryText();
        }

        private void SettingsCategory_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton { Tag: string category })
                ApplySettingsCategory(category);
        }

        // ───────────────────────────── Logo und Name der Seitenleiste ─────────────────────────────

        private void ApplyBrand()
        {
            if (BrandLogo == null) return;
            ApplyBrandImage();

            string design = BrandDesigns.ContainsKey(settings.BrandDesign) ? settings.BrandDesign : "original";
            var (ring, chain) = BrandDesigns[design];

            var ringColor = design == "accent" ? GetAccentColor() : ParseColor(ring, "#8B5CF6");

            // Die Logo-Pinsel werden gemeinsam genutzt (auch für Tray-Symbol und Export): Farben direkt ändern
            if (Resources["BrandRingBrush"] is SolidColorBrush ringBrush && !ringBrush.IsFrozen)
                ringBrush.Color = ringColor;

            if (Resources["LogoGold"] is LinearGradientBrush goldBrush && !goldBrush.IsFrozen)
            {
                for (int i = 0; i < goldBrush.GradientStops.Count && i < chain.Length; i++)
                    goldBrush.GradientStops[i].Color = ParseColor(chain[i], "#F59E0B");
            }

            double logoSize = settings.SidebarCollapsed ? Math.Min(settings.BrandLogoSize, 54) : settings.BrandLogoSize;
            if (!settings.SidebarCollapsed)
            {
                // Das Logo darf nie mehr Platz brauchen, als die Seitenleiste hergibt (mit Name höchstens die Hälfte)
                double content = Math.Max(120, settings.SidebarWidth - 36);
                logoSize = Math.Min(logoSize, settings.BrandShowName ? content * 0.5 : content);
            }
            BrandLogo.Width = logoSize;
            BrandLogo.Height = logoSize;

            BrandTitle.Text = string.IsNullOrWhiteSpace(settings.BrandName) ? "DFP PRO" : settings.BrandName;
            BrandTitle.FontSize = settings.BrandNameSize;
            BrandSubtitle.Text = string.IsNullOrWhiteSpace(settings.BrandSubtitle) ? "LAUNCHER" : settings.BrandSubtitle;
            BrandSubtitle.FontSize = Math.Max(10, Math.Round(settings.BrandNameSize * 0.6));

            BrandTitle.Visibility = settings.BrandShowName ? Visibility.Visible : Visibility.Collapsed;
            BrandSubtitle.Visibility = settings.BrandShowName && settings.BrandShowSubtitle ? Visibility.Visible : Visibility.Collapsed;

            switch (settings.BrandNameColor)
            {
                case "accent":
                    BrandTitle.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                    break;
                case "gold":
                    BrandTitle.Foreground = BrushGold;
                    break;
                default:
                    BrandTitle.Foreground = System.Windows.Media.Brushes.White;
                    break;
            }
        }

        private void BtnApplyBrandText_Click(object sender, RoutedEventArgs e)
        {
            settings.BrandName = TxtBrandName.Text.Trim();
            settings.BrandSubtitle = TxtBrandSub.Text.Trim();
            SaveSettings();
            ApplyBrand();
        }

        private void BtnResetBrand_Click(object sender, RoutedEventArgs e)
        {
            settings.BrandLogoSize = 54;
            settings.BrandNameSize = 20;
            settings.BrandShowName = true;
            settings.BrandShowSubtitle = true;
            settings.BrandDesign = "original";
            settings.BrandNameColor = "white";
            settings.BrandName = "DFP PRO";
            settings.BrandSubtitle = "LAUNCHER";
            SaveSettings();

            isLoadingSettings = true;
            try { PopulateControlsFromSettings(); }
            finally { isLoadingSettings = false; }
            ApplyBrand();
        }

        // ───────────────────────────── Größe der Menüeinträge ─────────────────────────────

        private void ApplyNavSizes()
        {
            foreach (var button in NavButtonMap().Values)
            {
                button.FontSize = settings.NavFontSize;
                button.Padding = new Thickness(12, settings.NavItemPadding, 12, settings.NavItemPadding);

                if (button.Content is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is TextBlock icon)
                {
                    icon.FontSize = settings.NavFontSize + 2;
                    icon.Width = Math.Round(settings.NavFontSize * 2.1);
                }
            }
        }

        private void ChangeNavSize(int delta)
        {
            settings.NavFontSize = Math.Clamp(settings.NavFontSize + delta, 11, 20);
            settings.NavItemPadding = Math.Clamp(settings.NavItemPadding + delta, 4, 16);
            SaveSettings();
            ApplyNavSizes();

            isLoadingSettings = true;
            try
            {
                SliderNavFont.Value = settings.NavFontSize;
                SliderNavPad.Value = settings.NavItemPadding;
                UpdateExtra4SliderLabels();
            }
            finally { isLoadingSettings = false; }
        }

        // ───────────────────────────── Einstellungen: Logo, Navigation, Cover-Quelle ─────────────────────────────

        private void ReadExtra4Settings()
        {
            settings.NavFontSize = (int)Math.Round(SliderNavFont.Value);
            settings.NavItemPadding = (int)Math.Round(SliderNavPad.Value);
            settings.BrandLogoSize = (int)Math.Round(SliderBrandLogo.Value);
            settings.BrandNameSize = (int)Math.Round(SliderBrandName.Value);
            settings.BrandShowName = ChkBrandName.IsChecked == true;
            settings.BrandShowSubtitle = ChkBrandSub.IsChecked == true;
            settings.ShowTrayIcon = ChkShowTray.IsChecked == true;

            settings.BrandDesign = ChipBrandGold.IsChecked == true ? "gold"
                                 : ChipBrandNeon.IsChecked == true ? "neon"
                                 : ChipBrandIce.IsChecked == true ? "ice"
                                 : ChipBrandMono.IsChecked == true ? "mono"
                                 : ChipBrandAccent.IsChecked == true ? "accent"
                                 : "original";
            settings.BrandNameColor = ChipNameAccent.IsChecked == true ? "accent"
                                    : ChipNameGold.IsChecked == true ? "gold"
                                    : "white";
        }

        private void PopulateExtra4Settings()
        {
            SliderNavFont.Value = settings.NavFontSize;
            SliderNavPad.Value = settings.NavItemPadding;
            SliderBrandLogo.Value = settings.BrandLogoSize;
            SliderBrandName.Value = settings.BrandNameSize;
            ChkBrandName.IsChecked = settings.BrandShowName;
            ChkBrandSub.IsChecked = settings.BrandShowSubtitle;
            ChkShowTray.IsChecked = settings.ShowTrayIcon;

            ChipBrandOriginal.IsChecked = settings.BrandDesign is not ("gold" or "neon" or "ice" or "mono" or "accent");
            ChipBrandGold.IsChecked = settings.BrandDesign == "gold";
            ChipBrandNeon.IsChecked = settings.BrandDesign == "neon";
            ChipBrandIce.IsChecked = settings.BrandDesign == "ice";
            ChipBrandMono.IsChecked = settings.BrandDesign == "mono";
            ChipBrandAccent.IsChecked = settings.BrandDesign == "accent";

            ChipNameWhite.IsChecked = settings.BrandNameColor is not ("accent" or "gold");
            ChipNameAccent.IsChecked = settings.BrandNameColor == "accent";
            ChipNameGold.IsChecked = settings.BrandNameColor == "gold";

            TxtBrandName.Text = settings.BrandName;
            TxtBrandSub.Text = settings.BrandSubtitle;
            TxtSgdbKey.Text = settings.SteamGridDbKey;
        }

        private void UpdateExtra4SliderLabels()
        {
            TxtNavFontValue.Text = $"{settings.NavFontSize} px";
            TxtNavPadValue.Text = $"{settings.NavItemPadding} px";
            TxtBrandLogoValue.Text = $"{settings.BrandLogoSize} px";
            TxtBrandNameValue.Text = $"{settings.BrandNameSize} px";
        }

        // ───────────────────────────── Einstellungen: Meldungen, Start, Beenden, Cover ─────────────────────────────

        private void ReadExtra5Settings()
        {
            settings.ShowToasts = ChkToasts.IsChecked == true;
            settings.DoubleClickLaunch = ChkDoubleClick.IsChecked == true;
            settings.ExitWarning = ChkExitWarn.IsChecked == true;
            settings.SgdbAnimated = ChkSgdbAnimated.IsChecked == true;
        }

        private void PopulateExtra5Settings()
        {
            ChkToasts.IsChecked = settings.ShowToasts;
            ChkDoubleClick.IsChecked = settings.DoubleClickLaunch;
            ChkExitWarn.IsChecked = settings.ExitWarning;
            ChkSgdbAnimated.IsChecked = settings.SgdbAnimated;
        }

        // ───────────────────────────── Klang-Effekte ─────────────────────────────

        private static byte[] MakeToneWav(params (double Frequency, int Milliseconds)[] notes)
        {
            const int rate = 22050;
            var samples = new List<short>();
            foreach (var (frequency, milliseconds) in notes)
            {
                int count = rate * milliseconds / 1000;
                for (int i = 0; i < count; i++)
                {
                    double envelope = Math.Min(1.0, i / 150.0) * Math.Min(1.0, (count - i) / 500.0);
                    samples.Add((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * 2400 * envelope));
                }
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            int dataLength = samples.Count * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            foreach (short sample in samples) writer.Write(sample);
            writer.Flush();
            return stream.ToArray();
        }

        private void PlayUiSound(string kind)
        {
            if (!settings.UiSounds) return;

            try
            {
                if (!uiSounds.TryGetValue(kind, out var player))
                {
                    byte[] wav = kind switch
                    {
                        "move" => MakeToneWav((880, 35)),
                        "launch" => MakeToneWav((660, 70), (990, 110)),
                        "bark" => MakeToneWav((520, 55), (400, 80), (520, 55), (330, 130)),
                        "dice" => MakeToneWav((660, 40), (520, 40), (780, 70)),
                        _ => MakeToneWav((740, 55))
                    };
                    player = new System.Media.SoundPlayer(new MemoryStream(wav));
                    player.Load();
                    uiSounds[kind] = player;
                }
                // Auf einem Hintergrund-Thread abspielen, damit die Oberfläche nie darauf wartet
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { player.Play(); }
                    catch { }
                });
            }
            catch { }
        }

        // ───────────────────────────── Design teilen ─────────────────────────────

        private void BtnThemeExport_Click(object sender, RoutedEventArgs e)
        {
            var pack = new ThemePack
            {
                BackgroundColor = settings.BackgroundColor,
                AccentColor = settings.AccentColor,
                CardWidth = settings.CardWidth,
                CardCorner = settings.CardCorner,
                CardSpacing = settings.CardSpacing,
                CardAspect = settings.CardAspect,
                UiScale = settings.UiScale,
                HoverZoom = settings.HoverZoom,
                GlassEffect = settings.GlassEffect,
                OledMode = settings.OledMode,
                SidebarWidth = settings.SidebarWidth,
                NavFontSize = settings.NavFontSize,
                NavItemPadding = settings.NavItemPadding,
                BrandDesign = settings.BrandDesign,
                BrandNameColor = settings.BrandNameColor,
                BrandLogoSize = settings.BrandLogoSize,
                BrandNameSize = settings.BrandNameSize,
                BrandName = settings.BrandName,
                BrandSubtitle = settings.BrandSubtitle
            };

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("Design exportieren"),
                Filter = "DFP-Design (*.dfptheme)|*.dfptheme",
                FileName = "Mein-Design.dfptheme"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(pack, JsonOptions));
                ShowToast("🎨", "Design gespeichert", System.IO.Path.GetFileName(dialog.FileName), 4);
            }
            catch (Exception ex)
            {
                Msg($"Speichern fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsHexColor(string value) => Regex.IsMatch(value ?? string.Empty, "^#[0-9A-Fa-f]{6}$");

        private void BtnThemeImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Design importieren"),
                Filter = "DFP-Design (*.dfptheme)|*.dfptheme"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var pack = JsonSerializer.Deserialize<ThemePack>(File.ReadAllText(dialog.FileName));
                if (pack == null) throw new InvalidDataException();

                if (IsHexColor(pack.BackgroundColor)) settings.BackgroundColor = pack.BackgroundColor;
                if (IsHexColor(pack.AccentColor)) settings.AccentColor = pack.AccentColor;
                settings.CardWidth = Math.Clamp(pack.CardWidth, 160, 300);
                settings.CardCorner = Math.Clamp(pack.CardCorner, 0, 28);
                settings.CardSpacing = Math.Clamp(pack.CardSpacing, 4, 28);
                settings.CardAspect = Math.Clamp(pack.CardAspect, 100, 170);
                settings.UiScale = Math.Clamp(pack.UiScale, 80, 130);
                settings.HoverZoom = Math.Clamp(pack.HoverZoom, 100, 112);
                settings.GlassEffect = pack.GlassEffect;
                settings.OledMode = pack.OledMode;
                settings.SidebarWidth = Math.Clamp(pack.SidebarWidth, 200, 340);
                settings.NavFontSize = Math.Clamp(pack.NavFontSize, 11, 20);
                settings.NavItemPadding = Math.Clamp(pack.NavItemPadding, 4, 16);
                settings.BrandDesign = BrandDesigns.ContainsKey(pack.BrandDesign) ? pack.BrandDesign : "original";
                settings.BrandNameColor = pack.BrandNameColor is "accent" or "gold" ? pack.BrandNameColor : "white";
                settings.BrandLogoSize = Math.Clamp(pack.BrandLogoSize, 32, 96);
                settings.BrandNameSize = Math.Clamp(pack.BrandNameSize, 12, 30);
                settings.BrandName = string.IsNullOrWhiteSpace(pack.BrandName) ? "DFP PRO" : pack.BrandName.Trim();
                settings.BrandSubtitle = string.IsNullOrWhiteSpace(pack.BrandSubtitle) ? "LAUNCHER" : pack.BrandSubtitle.Trim();
                SaveSettings();

                isLoadingSettings = true;
                try { PopulateControlsFromSettings(); }
                finally { isLoadingSettings = false; }

                imageCache.Clear();
                ApplyViewSettings();
                ApplyTheme();
                ApplyFilter();
                RefreshDashboard();
                ShowToast("🎨", "Design geladen", System.IO.Path.GetFileName(dialog.FileName), 4);
            }
            catch (Exception ex)
            {
                Msg($"Laden fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────────────────────────── Einrichtungs-Assistent ─────────────────────────────

        private void BtnWizard_Click(object sender, RoutedEventArgs e) => RunSetupWizard();

        private void RunSetupWizard()
        {
            while (ShowSetupWizard()) { }

            settings.SetupDone = true;
            SaveSettings();
        }

        /// <summary>Zeigt den Assistenten. Gibt true zurück, wenn er wegen eines Sprachwechsels neu gestartet werden soll.</summary>
        private bool ShowSetupWizard()
        {
            var dialog = CreateDialog("Willkommen beim DFP Pro Launcher", 600, out var panel);
            bool restart = false;
            int step = 0;

            var pages = new List<StackPanel>();
            string accentChoice = settings.AccentColor;
            bool oledChoice = settings.OledMode;
            bool onlineChoice = settings.OnlineFeatures;
            var chipStyle = TryFindResource("ChipStyle") as Style;

            TextBlock Heading(string text) => new() { Text = Loc.T(text), Foreground = System.Windows.Media.Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            TextBlock Hint(string text) => new() { Text = Loc.T(text), Foreground = BrushSubtle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };

            // Seite 1: Sprache
            var page1 = new StackPanel();
            page1.Children.Add(Heading("Willkommen!"));
            page1.Children.Add(Hint("Wähle deine Sprache. Alles lässt sich später in den Einstellungen ändern."));
            var languages = new WrapPanel();
            foreach (var (code, label) in new[]
            {
                ("auto", "Automatisch (Windows)"), ("de", "Deutsch"), ("en", "English"), ("zh", "中文"), ("es", "Español"),
                ("fr", "Français"), ("pt", "Português"), ("ru", "Русский"), ("ja", "日本語")
            })
            {
                string chosen = code;
                var radio = new System.Windows.Controls.RadioButton { Content = Loc.T(label), GroupName = "WizardLanguage", IsChecked = settings.Language == chosen || (chosen == "auto" && !Loc.Languages.Any(l => l.Code == settings.Language)) };
                if (chipStyle != null) radio.Style = chipStyle;
                radio.Checked += (s, e) =>
                {
                    if (chosen == settings.Language) return;
                    ApplyLanguage(chosen);
                    restart = true;
                    dialog.DialogResult = true;
                };
                languages.Children.Add(radio);
            }
            page1.Children.Add(languages);
            pages.Add(page1);

            // Seite 2: Name und Farbe
            var page2 = new StackPanel();
            page2.Children.Add(Heading("Wie sollen wir dich nennen?"));
            page2.Children.Add(Hint("Der Name erscheint im Dashboard. Suche dir außerdem eine Farbe aus."));
            var nameBox = new System.Windows.Controls.TextBox { Text = settings.UserName == "Gamer" ? string.Empty : settings.UserName, MaxLength = 24, Height = 40, Margin = new Thickness(0, 0, 0, 16) };
            page2.Children.Add(nameBox);

            var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            var swatchBorders = new List<(Border Border, string Color)>();
            void RefreshSwatches()
            {
                foreach (var (border, color) in swatchBorders)
                {
                    bool selected = string.Equals(color, accentChoice, StringComparison.OrdinalIgnoreCase);
                    border.BorderBrush = selected ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
                }
            }
            foreach (string color in new[] { "#8B5CF6", "#DC2626", "#3B82F6", "#10B981", "#F59E0B", "#EC4899", "#06B6D4" })
            {
                string picked = color;
                var swatch = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(18),
                    Margin = new Thickness(0, 0, 10, 0),
                    Background = MakeBrush(color),
                    BorderThickness = new Thickness(3),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                swatch.MouseLeftButtonUp += (s, e) =>
                {
                    accentChoice = picked;
                    RefreshSwatches();
                };
                swatchBorders.Add((swatch, color));
                swatches.Children.Add(swatch);
            }
            RefreshSwatches();
            page2.Children.Add(swatches);

            var oledBox = new System.Windows.Controls.CheckBox { Content = Loc.T("OLED-Modus (reines Schwarz für OLED-Bildschirme)"), IsChecked = oledChoice, Foreground = System.Windows.Media.Brushes.White };
            oledBox.Checked += (s, e) => oledChoice = true;
            oledBox.Unchecked += (s, e) => oledChoice = false;
            page2.Children.Add(oledBox);
            pages.Add(page2);

            // Seite 3: Online-Funktionen und Cover
            var page3 = new StackPanel();
            page3.Children.Add(Heading("Cover und Online-Funktionen"));
            page3.Children.Add(Hint("Für Cover, Wetter und Angebote braucht der Launcher das Internet. Mit einem kostenlosen SteamGridDB-Schlüssel (steamgriddb.com) bekommst du besonders schöne Cover. Beides ist freiwillig."));
            var onlineBox = new System.Windows.Controls.CheckBox { Content = Loc.T("Online-Funktionen erlauben (Cover-Download und Wetter)"), IsChecked = onlineChoice, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 14) };
            onlineBox.Checked += (s, e) => onlineChoice = true;
            onlineBox.Unchecked += (s, e) => onlineChoice = false;
            page3.Children.Add(onlineBox);
            page3.Children.Add(new TextBlock { Text = Loc.T("SteamGridDB-Schlüssel (optional)"), Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 6) });
            var keyBox = new System.Windows.Controls.TextBox { Text = settings.SteamGridDbKey, Height = 40 };
            page3.Children.Add(keyBox);
            pages.Add(page3);

            // Seite 4: Fertig
            var page4 = new StackPanel();
            page4.Children.Add(Heading("Alles bereit!"));
            page4.Children.Add(Hint("Der Launcher sucht jetzt deine Spiele von Steam, Epic, GOG, EA, Ubisoft, Battle.net, Rockstar und Xbox. Weitere Spiele ziehst du einfach per Drag-and-drop ins Fenster. Strg+K öffnet die Suche."));
            pages.Add(page4);

            var host = new ContentControl();
            panel.Children.Add(host);

            var progress = new TextBlock { Foreground = BrushSubtle, FontSize = 12, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var skip = new System.Windows.Controls.Button { Content = Loc.T("Überspringen"), Margin = new Thickness(0, 0, 10, 0) };
            var back = new System.Windows.Controls.Button { Content = Loc.T("Zurück"), Margin = new Thickness(0, 0, 10, 0) };
            var next = new System.Windows.Controls.Button { Padding = new Thickness(26, 8, 26, 8), IsDefault = true };
            next.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");

            void Show()
            {
                host.Content = pages[step];
                progress.Text = $"{step + 1} / {pages.Count}";
                back.IsEnabled = step > 0;
                next.Content = Loc.T(step == pages.Count - 1 ? "Los geht's" : "Weiter");
                skip.Visibility = step == pages.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
            }

            void Apply()
            {
                string name = nameBox.Text.Trim();
                if (name.Length > 0) settings.UserName = name;
                settings.AccentColor = accentChoice;
                settings.OledMode = oledChoice;
                settings.OnlineFeatures = onlineChoice;
                settings.SteamGridDbKey = keyBox.Text.Trim();
                settings.SetupDone = true;
                SaveSettings();

                isLoadingSettings = true;
                try { PopulateControlsFromSettings(); }
                finally { isLoadingSettings = false; }

                TxtUserName.Text = DisplayUserName();
                ApplyViewSettings();
                ApplyTheme();
                RefreshDashboard();
            }

            skip.Click += (s, e) => dialog.DialogResult = true;
            back.Click += (s, e) => { if (step > 0) { step--; Show(); } };
            next.Click += (s, e) =>
            {
                if (step < pages.Count - 1)
                {
                    step++;
                    Show();
                    return;
                }
                Apply();
                dialog.DialogResult = true;
            };

            var footer = new Grid { Margin = new Thickness(0, 22, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.Children.Add(progress);
            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            buttons.Children.Add(skip);
            buttons.Children.Add(back);
            buttons.Children.Add(next);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);
            panel.Children.Add(footer);

            Show();
            dialog.ShowDialog();
            return restart;
        }

        // ───────────────────────────── Einstellungen: neue Schalter ─────────────────────────────

        private void ReadExtra6Settings()
        {
            settings.UiSounds = ChkSounds.IsChecked == true;
            settings.SensorsEnabled = ChkSensors.IsChecked == true;
            settings.FetchGameInfo = ChkFetchInfo.IsChecked == true;
            settings.CheckUpdates = ChkUpdates.IsChecked == true;
        }

        private void PopulateExtra6Settings()
        {
            ChkSounds.IsChecked = settings.UiSounds;
            ChkSensors.IsChecked = settings.SensorsEnabled;
            ChkFetchInfo.IsChecked = settings.FetchGameInfo;
            ChkUpdates.IsChecked = settings.CheckUpdates;
            TxtUpdateRepo.Text = settings.UpdateRepo;
            UpdateCloudFolderText();
        }

        // ───────────────────────────── Einstellungen durchsuchen ─────────────────────────────

        private static void CollectTexts(DependencyObject node, System.Text.StringBuilder sb)
        {
            if (node is TextBlock block) sb.Append(block.Text).Append(' ');
            else if (node is ContentControl control && control.Content is string text) sb.Append(text).Append(' ');

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                CollectTexts(child, sb);
        }

        private void ResetSettingsRows()
        {
            foreach (var card in SettingsCards.Children.OfType<Border>())
            {
                if (card.Child is not StackPanel inner) continue;
                foreach (UIElement child in inner.Children) child.Visibility = Visibility.Visible;
            }
        }

        private void FilterSettings(string query)
        {
            foreach (var card in SettingsCards.Children.OfType<Border>())
            {
                if (card.Child is not StackPanel inner) continue;

                string title = inner.Children.Count > 0 && inner.Children[0] is TextBlock heading ? heading.Text : string.Empty;
                bool titleMatch = title.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                bool any = false;

                for (int i = 1; i < inner.Children.Count; i++)
                {
                    var child = inner.Children[i];
                    var sb = new System.Text.StringBuilder();
                    CollectTexts(child, sb);

                    bool match = titleMatch || sb.ToString().Contains(query, StringComparison.CurrentCultureIgnoreCase);
                    child.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                    any |= match;
                }

                card.Visibility = any || titleMatch ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TxtSettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SettingsCards == null) return;

            string query = TxtSettingsSearch.Text.Trim();
            SettingsSearchHint.Visibility = TxtSettingsSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (query.Length == 0)
            {
                ResetSettingsRows();
                ApplySettingsCategory(settingsCategory);
            }
            else
            {
                FilterSettings(query);
            }
        }

        // ───────────────────────────── Einstellungen lesen und anzeigen ─────────────────────────────

        private void ReadExtra7Settings()
        {
            settings.StreamerAuto = ChkStreamAuto.IsChecked == true;
            settings.StreamHideName = ChkStreamName.IsChecked == true;
            settings.StreamHideLocation = ChkStreamLocation.IsChecked == true;
            settings.StreamHideStats = ChkStreamStats.IsChecked == true;
            settings.StreamHideRecent = ChkStreamRecent.IsChecked == true;
            settings.StreamHidePaths = ChkStreamPaths.IsChecked == true;
            settings.StreamHideShots = ChkStreamShots.IsChecked == true;
            settings.StreamMuteAlerts = ChkStreamMute.IsChecked == true;

            settings.ReopenAfterGame = ChkReopen.IsChecked == true;
            settings.AskOnClose = ChkAskClose.IsChecked == true;
            settings.TrayClickOpensLauncher = ChkTrayClick.IsChecked == true;
            settings.TypeToSearch = ChkTypeSearch.IsChecked == true;
            settings.FavoritesFirst = ChkFavFirst.IsChecked == true;
            settings.ShowAlphaBar = ChkAlphaBar.IsChecked == true;
            settings.SessionReport = ChkSessionReport.IsChecked == true;
            settings.MuteWhilePlaying = ChkMuteGame.IsChecked == true;

            settings.DailyLimitHours = (int)Math.Round(SliderDailyLimit.Value);
            settings.OverlayOpacity = (int)Math.Round(SliderOvOpacity.Value);
            settings.OverlayScale = (int)Math.Round(SliderOvScale.Value);
            settings.OverlayShowCpu = ChkOvCpu.IsChecked == true;
            settings.OverlayShowGpu = ChkOvGpu.IsChecked == true;
            settings.OverlayShowRam = ChkOvRam.IsChecked == true;
            settings.OverlayShowClock = ChkOvClock.IsChecked == true;
        }

        private void PopulateExtra7Settings()
        {
            ChkStreamer.IsChecked = settings.StreamerMode;
            ChkStreamAuto.IsChecked = settings.StreamerAuto;
            ChkStreamName.IsChecked = settings.StreamHideName;
            ChkStreamLocation.IsChecked = settings.StreamHideLocation;
            ChkStreamStats.IsChecked = settings.StreamHideStats;
            ChkStreamRecent.IsChecked = settings.StreamHideRecent;
            ChkStreamPaths.IsChecked = settings.StreamHidePaths;
            ChkStreamShots.IsChecked = settings.StreamHideShots;
            ChkStreamMute.IsChecked = settings.StreamMuteAlerts;

            ChkReopen.IsChecked = settings.ReopenAfterGame;
            ChkAskClose.IsChecked = settings.AskOnClose;
            ChkTrayClick.IsChecked = settings.TrayClickOpensLauncher;
            ChkTypeSearch.IsChecked = settings.TypeToSearch;
            ChkFavFirst.IsChecked = settings.FavoritesFirst;
            ChkAlphaBar.IsChecked = settings.ShowAlphaBar;
            ChkSessionReport.IsChecked = settings.SessionReport;
            ChkMuteGame.IsChecked = settings.MuteWhilePlaying;

            SliderDailyLimit.Value = settings.DailyLimitHours;
            SliderOvOpacity.Value = settings.OverlayOpacity;
            SliderOvScale.Value = settings.OverlayScale;
            ChkOvCpu.IsChecked = settings.OverlayShowCpu;
            ChkOvGpu.IsChecked = settings.OverlayShowGpu;
            ChkOvRam.IsChecked = settings.OverlayShowRam;
            ChkOvClock.IsChecked = settings.OverlayShowClock;
        }

        private void UpdateExtra7SliderLabels()
        {
            TxtDailyLimitValue.Text = settings.DailyLimitHours == 0 ? Loc.T("Aus") : $"{settings.DailyLimitHours} {Loc.T("Std.")}";
            TxtOvOpacityValue.Text = $"{settings.OverlayOpacity} %";
            TxtOvScaleValue.Text = $"{settings.OverlayScale} %";
        }

        private void RefreshDashboard(bool animate = false)
        {
            RefreshDashboardCore(animate);
            ApplyDashboardLayout();
        }

        // ───────────────────────────── Ansichts-Schemas ─────────────────────────────

        private sealed class SchemeSnapshot
        {
            public string CardLayout = "cards";
            public int CardWidth, CardSpacing, CardAspect, HoverZoom, RecentCount, SidebarWidth, NavFontSize, NavItemPadding;
            public bool SidebarCollapsed, ShowCardNames, ShowCardSource, PageAnimations, CardAnimations, HoverAnimations, HoverGlow, ShowSparklines;
            public bool ShowHero, ShowClock, ShowWeather, ShowStats, ShowRecent, ShowFavorites, ControllerSupport;
            public List<string> HiddenSections = new(), DashboardOrder = new(), DashboardHidden = new();

            public static SchemeSnapshot Capture(AppSettings s) => new()
            {
                CardLayout = s.CardLayout,
                CardWidth = s.CardWidth,
                CardSpacing = s.CardSpacing,
                CardAspect = s.CardAspect,
                HoverZoom = s.HoverZoom,
                RecentCount = s.RecentCount,
                SidebarWidth = s.SidebarWidth,
                NavFontSize = s.NavFontSize,
                NavItemPadding = s.NavItemPadding,
                SidebarCollapsed = s.SidebarCollapsed,
                ShowCardNames = s.ShowCardNames,
                ShowCardSource = s.ShowCardSource,
                PageAnimations = s.PageAnimations,
                CardAnimations = s.CardAnimations,
                HoverAnimations = s.HoverAnimations,
                HoverGlow = s.HoverGlow,
                ShowSparklines = s.ShowSparklines,
                ShowHero = s.ShowHero,
                ShowClock = s.ShowClock,
                ShowWeather = s.ShowWeather,
                ShowStats = s.ShowStats,
                ShowRecent = s.ShowRecent,
                ShowFavorites = s.ShowFavorites,
                ControllerSupport = s.ControllerSupport,
                HiddenSections = s.HiddenSections.ToList(),
                DashboardOrder = s.DashboardOrder.ToList(),
                DashboardHidden = s.DashboardHidden.ToList()
            };

            public void Restore(AppSettings s)
            {
                s.CardLayout = CardLayout; s.CardWidth = CardWidth; s.CardSpacing = CardSpacing; s.CardAspect = CardAspect;
                s.HoverZoom = HoverZoom; s.RecentCount = RecentCount; s.SidebarWidth = SidebarWidth; s.NavFontSize = NavFontSize;
                s.NavItemPadding = NavItemPadding; s.SidebarCollapsed = SidebarCollapsed; s.ShowCardNames = ShowCardNames;
                s.ShowCardSource = ShowCardSource; s.PageAnimations = PageAnimations; s.CardAnimations = CardAnimations;
                s.HoverAnimations = HoverAnimations; s.HoverGlow = HoverGlow; s.ShowSparklines = ShowSparklines;
                s.ShowHero = ShowHero; s.ShowClock = ShowClock; s.ShowWeather = ShowWeather; s.ShowStats = ShowStats;
                s.ShowRecent = ShowRecent; s.ShowFavorites = ShowFavorites; s.ControllerSupport = ControllerSupport;
                s.HiddenSections = HiddenSections.ToList(); s.DashboardOrder = DashboardOrder.ToList(); s.DashboardHidden = DashboardHidden.ToList();
            }
        }

        private static readonly (string Key, string Icon, string Title, string Description)[] Schemes =
        {
            ("standard", "🏠", "Standard", "Die ursprüngliche Ansicht mit allen Standard-Einstellungen."),
            ("minimal", "🪶", "Minimalistisch", "Nur das Wesentliche: Seitenleiste eingeklappt, wenige Bereiche, keine Animationen."),
            ("tidy", "🧹", "Aufgeräumt", "Ruhige Oberfläche mit Banner, „Zuletzt gespielt“ und Favoriten. Selten genutzte Bereiche sind ausgeblendet."),
            ("info", "📊", "Informativ", "Alles auf einen Blick: Detailkacheln, Statistik, Wochenziel, Backlog, Wunschliste und Schnellzugriff."),
            ("compact", "📦", "Kompakt", "Viele Spiele auf wenig Platz: Liste, eingeklappte Seitenleiste und schmale Abstände."),
            ("couch", "🛋", "Wohnzimmer", "Große Karten und große Schrift für Fernseher und Controller.")
        };

        private void ApplySchemeValues(string key)
        {
            // Ausgangswerte (Standard)
            settings.CardLayout = "cards";
            settings.CardWidth = 220;
            settings.CardSpacing = 12;
            settings.CardAspect = 136;
            settings.HoverZoom = 105;
            settings.ShowCardNames = true;
            settings.ShowCardSource = true;
            settings.SidebarCollapsed = false;
            settings.SidebarWidth = 250;
            settings.NavFontSize = 14;
            settings.NavItemPadding = 8;
            settings.PageAnimations = true;
            settings.CardAnimations = true;
            settings.HoverAnimations = true;
            settings.HoverGlow = true;
            settings.ShowSparklines = true;
            settings.ShowHero = settings.ShowClock = settings.ShowWeather = true;
            settings.ShowStats = settings.ShowRecent = settings.ShowFavorites = true;
            settings.RecentCount = 6;
            settings.HiddenSections = new List<string>();
            settings.DashboardOrder = new List<string>();
            settings.DashboardHidden = new List<string>(DefaultDashHidden);

            switch (key)
            {
                case "minimal":
                    settings.CardWidth = 200;
                    settings.CardSpacing = 16;
                    settings.ShowCardSource = false;
                    settings.SidebarCollapsed = true;
                    settings.PageAnimations = false;
                    settings.CardAnimations = false;
                    settings.HoverGlow = false;
                    settings.ShowSparklines = false;
                    settings.RecentCount = 8;
                    settings.HiddenSections = new List<string>
                        { "stats", "deals", "backlog", "gallery", "apps", "links", "wishlist", "downloads", "storage", "optimization", "tools" };
                    settings.DashboardHidden = new List<string>
                        { "hero", "clock", "stats", "favorites", "goal", "backlog", "wishlist", "quick", "actions" };
                    break;

                case "tidy":
                    settings.CardSpacing = 14;
                    settings.ShowCardSource = false;
                    settings.HoverGlow = false;
                    settings.HiddenSections = new List<string> { "deals", "gallery", "downloads", "storage", "optimization", "tools" };
                    settings.DashboardHidden = new List<string> { "clock", "stats", "goal", "backlog", "wishlist", "quick" };
                    break;

                case "info":
                    settings.CardLayout = "detail";
                    settings.CardWidth = 260;
                    settings.RecentCount = 8;
                    settings.DashboardHidden = new List<string>();
                    break;

                case "compact":
                    settings.CardLayout = "list";
                    settings.CardSpacing = 6;
                    settings.SidebarCollapsed = true;
                    settings.HoverGlow = false;
                    settings.CardAnimations = false;
                    settings.RecentCount = 5;
                    settings.DashboardOrder = new List<string>
                        { "stats", "recent", "goal", "backlog", "hero", "clock", "favorites", "wishlist", "quick", "actions" };
                    settings.DashboardHidden = new List<string> { "hero", "clock", "favorites", "wishlist", "quick", "actions" };
                    break;

                case "couch":
                    settings.CardWidth = 280;
                    settings.CardSpacing = 20;
                    settings.ShowCardSource = false;
                    settings.HoverZoom = 108;
                    settings.NavFontSize = 17;
                    settings.NavItemPadding = 11;
                    settings.SidebarWidth = 290;
                    settings.ControllerSupport = true;
                    settings.HiddenSections = new List<string> { "stats", "deals", "gallery", "apps", "downloads", "storage", "optimization", "tools" };
                    settings.DashboardHidden = new List<string> { "clock", "stats", "goal", "backlog", "wishlist", "quick" };
                    break;
            }
        }

        private void RefreshAfterScheme()
        {
            isLoadingSettings = true;
            try { PopulateControlsFromSettings(); }
            finally { isLoadingSettings = false; }

            ApplyViewSettings();
            ApplyNavVisibility();
            BuildNavSettings();
            BuildDashWidgetSettings();
            ApplySidebar(true);
            ApplyFilter();
            RefreshDashboard();
            UpdateSliderLabels();
        }

        private void ApplyScheme(string key, string title)
        {
            schemeUndo = SchemeSnapshot.Capture(settings);
            ApplySchemeValues(key);
            SaveSettings();
            RefreshAfterScheme();

            ShowToast("🎨", "Schema angewendet", Loc.T(title) + "  ·  " + Loc.T("Klicken zum Rückgängigmachen"), 9, UndoScheme, true);
        }

        private void UndoScheme()
        {
            if (schemeUndo == null) return;

            schemeUndo.Restore(settings);
            schemeUndo = null;
            SaveSettings();
            RefreshAfterScheme();
            ShowToast("↩", "Schema zurückgesetzt", "Die vorherigen Einstellungen sind wieder aktiv.", 5, null, true);
        }

        private void BuildSchemeTiles()
        {
            if (SchemePanel == null) return;
            SchemePanel.Children.Clear();

            foreach (var scheme in Schemes)
            {
                var item = scheme;

                var text = new StackPanel();
                text.Children.Add(new TextBlock
                {
                    Text = item.Icon + "  " + Loc.T(item.Title),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14
                });
                text.Children.Add(new TextBlock
                {
                    Text = Loc.T(item.Description),
                    Foreground = BrushSubtle,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0)
                });

                var tile = new Border
                {
                    Width = 230,
                    MinHeight = 112,
                    Margin = new Thickness(0, 0, 12, 12),
                    Padding = new Thickness(14),
                    CornerRadius = new CornerRadius(12),
                    Background = BrushFooter,
                    BorderBrush = BrushCardBorder,
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = text
                };
                tile.MouseEnter += (s, e) => tile.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                tile.MouseLeave += (s, e) => tile.BorderBrush = BrushCardBorder;
                tile.MouseLeftButtonUp += (s, e) => ApplyScheme(item.Key, item.Title);
                SchemePanel.Children.Add(tile);
            }
        }

        // ───────────────────────────── Einstellungen lesen und anzeigen ─────────────────────────────

        private void ReadExtra8Settings()
        {
            settings.ShowModButton = ChkModButton.IsChecked == true;
            settings.WelcomeAnimation = ChkWelcome.IsChecked == true;
            settings.DiscordEnabled = ChkDiscord.IsChecked == true;
            settings.DiscordOnStart = ChkDiscordStart.IsChecked == true;
            settings.DiscordOnEnd = ChkDiscordEnd.IsChecked == true;
            settings.DiscordSendName = ChkDiscordName.IsChecked == true;
        }

        private void PopulateExtra8Settings()
        {
            ChkModButton.IsChecked = settings.ShowModButton;
            ChkWelcome.IsChecked = settings.WelcomeAnimation;
            ChkDiscord.IsChecked = settings.DiscordEnabled;
            ChkDiscordStart.IsChecked = settings.DiscordOnStart;
            ChkDiscordEnd.IsChecked = settings.DiscordOnEnd;
            ChkDiscordName.IsChecked = settings.DiscordSendName;
            TxtDiscordWebhook.Text = settings.DiscordWebhook;
        }

        // ═════════════════════════════ Hintergrund, Farben und animierte Cover ═════════════════════════════

        private readonly HashSet<string> gifFailed = new(StringComparer.OrdinalIgnoreCase);
        private bool upgradingCovers;

        private static (int Width, int Height) LargestScreenSize()
        {
            int width = 0, height = 0;
            foreach (var display in ReadDisplays())
            {
                width = Math.Max(width, display.Width);
                height = Math.Max(height, display.Height);
            }
            return width > 0 ? (width, height) : (2560, 1440);
        }

        /// <summary>Lädt das Hintergrundbild in voller Schärfe: nur verkleinern, wenn es größer als der größte Bildschirm ist.</summary>
        private static BitmapImage LoadBackgroundBitmap(string path)
        {
            var screen = LargestScreenSize();
            int nativeWidth = 0, nativeHeight = 0;

            try
            {
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                nativeWidth = decoder.Frames[0].PixelWidth;
                nativeHeight = decoder.Frames[0].PixelHeight;
            }
            catch { }

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;

            if (nativeWidth > 0 && nativeHeight > 0)
            {
                double need = Math.Max(screen.Width / (double)nativeWidth, screen.Height / (double)nativeHeight);
                if (need < 1.0) image.DecodePixelWidth = (int)Math.Ceiling(nativeWidth * need);
            }
            else
            {
                image.DecodePixelWidth = Math.Max(screen.Width, 1920);
            }

            image.EndInit();
            image.Freeze();
            return image;
        }

        /// <summary>Wendet Farbänderungen überall an, nicht nur auf den Hintergrund.</summary>
        private void RefreshAfterThemeChange()
        {
            ApplyViewSettings();
            ApplyFilter();
            RefreshDashboard();
            RebuildStaticTiles();
            BuildSchemeTiles();
        }

        /// <summary>Ersetzt vorhandene Standbild-Cover durch animierte Fassungen von SteamGridDB (einmal pro Spiel).</summary>
        private async Task UpgradeToAnimatedCoversAsync()
        {
            if (upgradingCovers || !settings.SgdbAnimated || !settings.OnlineFeatures
                || string.IsNullOrWhiteSpace(settings.SteamGridDbKey)) return;

            upgradingCovers = true;
            try
            {
                var pending = allGames
                    .Where(g => !string.IsNullOrEmpty(g.CoverPath) && !IsAnimatedCover(g.CoverPath)
                                && !settings.AnimatedChecked.Contains(g.Name))
                    .ToList();
                if (pending.Count == 0) return;

                int done = 0;
                using var gate = new System.Threading.SemaphoreSlim(2);

                var tasks = pending.Select(async game =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        var found = await FindSgdbCoverAsync(game, true);
                        if (found != null && found.Value.Animated)
                        {
                            string extension = GridExtension(found.Value.Url);
                            string target = System.IO.Path.ChangeExtension(CoverFilePath(game), extension == ".png" ? ".apng" : extension);

                            if (await TryDownloadAsync(new[] { found.Value.Url }, target))
                            {
                                RemoveOtherCoverVariants(game, target);
                                game.CoverPath = target;
                            }
                        }

                        lock (settings.AnimatedChecked) settings.AnimatedChecked.Add(game.Name);
                    }
                    catch { }
                    finally
                    {
                        gate.Release();

                        int finished = System.Threading.Interlocked.Increment(ref done);
                        if (finished % 6 == 0 || finished == pending.Count)
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
            catch (Exception ex)
            {
                LogError("Animierte Cover", ex);
            }
            finally
            {
                upgradingCovers = false;
            }
        }

        // ───────────────────────────── Geräteprofil (Desktop, Laptop, Handheld) ─────────────────────────────

        private sealed record DeviceInfo(string Kind, double Diagonal, bool Battery);

        private static DeviceInfo DetectDevice()
        {
            bool battery = false;
            try
            {
                var power = Forms.SystemInformation.PowerStatus;
                battery = (power.BatteryChargeStatus & Forms.BatteryChargeStatus.NoSystemBattery) == 0
                          && power.BatteryChargeStatus != Forms.BatteryChargeStatus.Unknown;
            }
            catch { }

            double smallest = 0;
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(@"root\wmi",
                    "SELECT Active, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams");
                foreach (System.Management.ManagementBaseObject monitor in searcher.Get())
                {
                    if (monitor["Active"] is bool active && !active) continue;

                    double width = Convert.ToDouble(monitor["MaxHorizontalImageSize"]);
                    double height = Convert.ToDouble(monitor["MaxVerticalImageSize"]);
                    if (width <= 0 || height <= 0) continue;

                    double inches = Math.Sqrt(width * width + height * height) / 2.54;
                    if (smallest == 0 || inches < smallest) smallest = inches;
                }
            }
            catch { }

            var smallDisplay = ReadDisplays().OrderBy(d => (long)d.Width * d.Height).FirstOrDefault();

            string kind;
            if (!battery) kind = "desktop";
            else if ((smallest > 0 && smallest <= 9.5) || (smallest == 0 && smallDisplay != null && smallDisplay.Width <= 1280 && smallDisplay.Height <= 800)) kind = "handheld";
            else kind = "laptop";

            return new DeviceInfo(kind, smallest, battery);
        }

        private static string DeviceKindName(string kind) => kind switch
        {
            "handheld" => "Handheld",
            "laptop" => "Laptop",
            _ => "Desktop-PC"
        };

        private string DeviceInfoText(DeviceInfo info)
        {
            string text = Loc.T($"Erkannt: {Loc.T(DeviceKindName(info.Kind))}");
            if (info.Diagonal > 0) text += " · " + info.Diagonal.ToString("0.#") + " " + Loc.T("Zoll");
            text += " · " + (info.Battery ? Loc.T("Akku vorhanden") : Loc.T("kein Akku"));
            return text;
        }

        private void ApplyDevicePreset(string kind)
        {
            schemeUndo = SchemeSnapshot.Capture(settings);

            switch (kind)
            {
                case "handheld":
                    settings.SidebarCollapsed = true;
                    settings.CardWidth = 170;
                    settings.CardSpacing = 8;
                    settings.NavFontSize = 14;
                    settings.ShowCardSource = false;
                    settings.ControllerSupport = true;
                    break;

                case "laptop":
                    settings.SidebarCollapsed = false;
                    settings.SidebarWidth = 220;
                    settings.CardWidth = 190;
                    settings.CardSpacing = 10;
                    settings.NavFontSize = 13;
                    settings.NavItemPadding = 7;
                    break;

                default:
                    settings.SidebarCollapsed = false;
                    settings.SidebarWidth = 250;
                    settings.CardWidth = 220;
                    settings.CardSpacing = 12;
                    settings.NavFontSize = 14;
                    settings.NavItemPadding = 8;
                    break;
            }

            SaveSettings();
            RefreshAfterScheme();
            ShowToast("📱", "Für dein Gerät angepasst", Loc.T(DeviceKindName(kind)) + "  ·  " + Loc.T("Klicken zum Rückgängigmachen"), 9, UndoScheme, true);
        }

        private bool deviceUiUpdating;

        private void UpdateDeviceUi(DeviceInfo? info = null)
        {
            if (ChipDevAuto == null) return;

            deviceUiUpdating = true;
            try
            {
                var chips = new[] { ChipDevAuto, ChipDevDesktop, ChipDevLaptop, ChipDevHandheld };
                foreach (var chip in chips) chip.IsChecked = (chip.Tag as string) == settings.DeviceProfile;
                if (chips.All(c => c.IsChecked != true)) ChipDevAuto.IsChecked = true;
            }
            finally
            {
                deviceUiUpdating = false;
            }

            if (info != null) TxtDeviceInfo.Text = DeviceInfoText(info);
        }

        private async Task AutoDetectDeviceAsync()
        {
            if (settings.DeviceProfile != "auto" || settings.DeviceAutoApplied) return;

            var info = await Task.Run(DetectDevice);
            settings.DeviceAutoApplied = true;
            UpdateDeviceUi(info);

            if (info.Kind == "desktop")
            {
                SaveSettings();
                return;
            }
            ApplyDevicePreset(info.Kind);
        }

        private async void Device_Checked(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings || deviceUiUpdating || sender is not System.Windows.Controls.RadioButton { Tag: string kind }) return;

            settings.DeviceProfile = kind;
            SaveSettings();

            var info = await Task.Run(DetectDevice);
            UpdateDeviceUi(info);
            ApplyDevicePreset(kind == "auto" ? info.Kind : kind);
        }

        private async void BtnDeviceDetect_Click(object sender, RoutedEventArgs e)
        {
            var info = await Task.Run(DetectDevice);
            UpdateDeviceUi(info);
            ApplyDevicePreset(settings.DeviceProfile == "auto" ? info.Kind : settings.DeviceProfile);
        }

        /// <summary>Auf kleinen Bildschirmen (Handhelds, kleine Laptops) darf das Fenster nicht über den Rand ragen.</summary>
        private void FitWindowToScreen()
        {
            var area = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, area.Width);
            MinHeight = Math.Min(MinHeight, area.Height);
            if (Width > area.Width) Width = area.Width;
            if (Height > area.Height) Height = area.Height;
        }

        // ───────────────────────────── Einstellungen und Start ─────────────────────────────

        private void ReadExtra11Settings()
        {
            settings.MusicPlayer = ChkMusic.IsChecked == true;
            settings.QuickButtonsEnabled = ChkQuickButtons.IsChecked == true;
            settings.WarnBattery = ChkWarnBattery.IsChecked == true;
            settings.PowerButtons = ChkPowerButtons.IsChecked == true;
            settings.PowerConfirm = ChkPowerConfirm.IsChecked == true;
            settings.AutomationsEnabled = ChkAutomations.IsChecked == true;
        }

        private void PopulateExtra11Settings()
        {
            ChkMusic.IsChecked = settings.MusicPlayer;
            UpdateMusicServiceUi();
            ChkQuickButtons.IsChecked = settings.QuickButtonsEnabled;
            ChkWarnBattery.IsChecked = settings.WarnBattery;
            ChkPowerButtons.IsChecked = settings.PowerButtons;
            ChkPowerConfirm.IsChecked = settings.PowerConfirm;
            ChkAutomations.IsChecked = settings.AutomationsEnabled;
            UpdateDeviceUi();
        }

        private void InitExtras11()
        {
            musicService = MusicServices.Any(s => s.Key == settings.MusicService) ? settings.MusicService : "spotify";
            if (!settings.MusicServicesOn.Contains(musicService)) musicService = settings.MusicServicesOn[0];

            ChkMusic.Checked += (s, e) => UpdateMusicServiceVisibility();
            ChkMusic.Unchecked += (s, e) => UpdateMusicServiceVisibility();

            FitWindowToScreen();
            RenderSidebarExtras();
            RenderRoutines();
            UpdateDeviceUi();

            SizeChanged += (s, e) => FitMusicPanel();
            Closed += (s, e) =>
            {
                try { musicView?.Dispose(); }
                catch { }
            };

            Loaded += async (s, e) =>
            {
                await Task.Delay(4500);
                await AutoDetectDeviceAsync();
                RunRoutinesFor("start", null);
            };
        }

        // ───────────────────────────── Eigene Fensterleiste (transparent) ─────────────────────────────

        private void InitExtras12()
        {
            StateChanged += (s, e) =>
            {
                UpdateCaptionButtons();
                FixMaximizedOverhang();
            };
            Loaded += (s, e) =>
            {
                UpdateCaptionButtons();
                FixMaximizedOverhang();
            };

            UpdateRgbUi();
            UpdateRgbAccent();

#if DEBUG
            BuildDevPanel();
            ChipCatDev.Visibility = Visibility.Visible;
#else
            SettingsCards.Children.Remove(DevCard);
            ChipCatDev.Visibility = Visibility.Collapsed;
#endif
        }

        private void BtnWinMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnWinMax_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void BtnWinClose_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateCaptionButtons()
        {
            if (BtnWinMax == null) return;

            bool maximized = WindowState == WindowState.Maximized;
            BtnWinMax.Content = maximized ? "\uE923" : "\uE922";
            BtnWinMax.ToolTip = Loc.T(maximized ? "Verkleinern" : "Maximieren");
        }

        /// <summary>Ein maximiertes Fenster ohne Windows-Rahmen ragt an den Rändern über den Bildschirm. Das gleichen wir hier aus.</summary>
        private void FixMaximizedOverhang()
        {
            if (WindowRoot == null) return;

            if (WindowState != WindowState.Maximized || WindowStyle == WindowStyle.None)
            {
                WindowRoot.Margin = new Thickness(0);
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    if (handle == IntPtr.Zero || !NativeExtras.GetWindowRect(handle, out var rect)) return;

                    var screen = Forms.Screen.FromHandle(handle);
                    var work = controllerMode || settings.TaskbarHideMaximized ? screen.Bounds : screen.WorkingArea;   // Vollbild: ganzer Bildschirm
                    var dpi = VisualTreeHelper.GetDpi(this);

                    WindowRoot.Margin = new Thickness(
                        Math.Max(0, work.Left - rect.Left) / dpi.DpiScaleX,
                        Math.Max(0, work.Top - rect.Top) / dpi.DpiScaleY,
                        Math.Max(0, rect.Right - work.Right) / dpi.DpiScaleX,
                        Math.Max(0, rect.Bottom - work.Bottom) / dpi.DpiScaleY);
                }
                catch { }
            }), DispatcherPriority.Loaded);
        }

        // ───────────────────────────── Farben der Seitenleiste und der Registerkarten ─────────────────────────────

        private string SidebarBaseColor()
            => !string.IsNullOrWhiteSpace(settings.SidebarColor) ? settings.SidebarColor : (settings.OledMode ? "#000000" : "#0B0E16");

        private void ApplyNavBrushes()
        {
            Resources["NavTextBrush"] = MakeBrush(string.IsNullOrWhiteSpace(settings.NavTextColor) ? "#9CA3AF" : settings.NavTextColor, "#9CA3AF");

            if (!string.IsNullOrWhiteSpace(settings.NavActiveColor))
            {
                Resources["NavActiveBrush"] = MakeBrush(settings.NavActiveColor, "#8B5CF6");
                Resources["NavActiveBgBrush"] = MakeAlphaBrush(settings.NavActiveColor, 0x38);
            }
            else
            {
                Resources["NavActiveBrush"] = Resources["AccentBrush"];
                Resources["NavActiveBgBrush"] = MakeBrush("#1C2233");
            }
        }

        private void BtnPickSidebarColor_Click(object sender, RoutedEventArgs e)
        {
            if (!TryPickColor(SidebarBaseColor(), out string hex)) return;
            settings.SidebarColor = hex;
            SaveSettings();
            RefreshAfterThemeChange();
        }

        private void BtnPickNavTextColor_Click(object sender, RoutedEventArgs e)
        {
            string current = string.IsNullOrWhiteSpace(settings.NavTextColor) ? "#9CA3AF" : settings.NavTextColor;
            if (!TryPickColor(current, out string hex)) return;
            settings.NavTextColor = hex;
            SaveSettings();
            RefreshAfterThemeChange();
        }

        private void BtnPickNavActiveColor_Click(object sender, RoutedEventArgs e)
        {
            string current = string.IsNullOrWhiteSpace(settings.NavActiveColor) ? settings.AccentColor : settings.NavActiveColor;
            if (!TryPickColor(current, out string hex)) return;
            settings.NavActiveColor = hex;
            SaveSettings();
            RefreshAfterThemeChange();
        }

        private void BtnResetNavColor_Click(object sender, RoutedEventArgs e)
        {
            switch ((sender as FrameworkElement)?.Tag as string)
            {
                case "sidebar": settings.SidebarColor = string.Empty; break;
                case "text": settings.NavTextColor = string.Empty; break;
                case "active": settings.NavActiveColor = string.Empty; break;
                default: return;
            }

            SaveSettings();
            RefreshAfterThemeChange();
        }

        // ───────────────────────────── RGB-Akzentfarbe (Belohnung für den Konami-Code) ─────────────────────────────

        private DispatcherTimer? rgbTimer;
        private double rgbHue;

        private bool RgbActive => settings.RgbUnlocked && settings.RgbAccent && !settings.PerformanceMode;

        private void UpdateRgbAccent()
        {
            if (!RgbActive)
            {
                if (rgbTimer != null && rgbTimer.IsEnabled)
                {
                    rgbTimer.Stop();
                    Resources["AccentBrush"] = MakeBrush(settings.AccentColor, "#8B5CF6");
                    if (string.IsNullOrWhiteSpace(settings.NavActiveColor)) Resources["NavActiveBrush"] = Resources["AccentBrush"];
                }
                return;
            }

            if (rgbTimer == null)
            {
                rgbTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
                rgbTimer.Tick += (s, e) => RgbStep();
            }
            if (!rgbTimer.IsEnabled) rgbTimer.Start();
        }

        private void RgbStep()
        {
            double step = settings.RgbSpeed switch { 1 => 1.5, 3 => 6.0, _ => 3.0 };
            rgbHue = (rgbHue + step) % 360;

            var brush = new SolidColorBrush(HslColor(rgbHue, 0.85, 0.6));
            brush.Freeze();
            Resources["AccentBrush"] = brush;
            if (string.IsNullOrWhiteSpace(settings.NavActiveColor)) Resources["NavActiveBrush"] = brush;
        }

        private void UpdateRgbUi()
        {
            if (RgbPanel == null) return;

            RgbPanel.Visibility = settings.RgbUnlocked ? Visibility.Visible : Visibility.Collapsed;

            bool before = isLoadingSettings;
            isLoadingSettings = true;
            try
            {
                ChkRgb.IsChecked = settings.RgbAccent;
                ChipRgbSlow.IsChecked = settings.RgbSpeed == 1;
                ChipRgbMid.IsChecked = settings.RgbSpeed == 2;
                ChipRgbFast.IsChecked = settings.RgbSpeed == 3;
            }
            finally
            {
                isLoadingSettings = before;
            }
        }

        private void RgbSpeed_Checked(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings || sender is not System.Windows.Controls.RadioButton { Tag: string tag }) return;

            settings.RgbSpeed = int.TryParse(tag, out int value) ? Math.Clamp(value, 1, 3) : 2;
            SaveSettings();
        }

        private void ReadExtra12Settings()
        {
            settings.RgbAccent = ChkRgb.IsChecked == true && settings.RgbUnlocked;
            UpdateRgbAccent();
        }

        private void PopulateExtra12Settings() => UpdateRgbUi();

        private void UnlockRgbWithPopup()
        {
            if (settings.RgbUnlocked) return;

            settings.RgbUnlocked = true;
            SaveSettings();
            UpdateRgbUi();
            Dispatcher.BeginInvoke(new Action(ShowRgbUnlockedDialog), DispatcherPriority.ApplicationIdle);
        }

        private void ShowRgbUnlockedDialog()
        {
            var dialog = CreateDialog("RGB-Akzentfarbe freigeschaltet", 520, out var panel);
            dialog.Topmost = true;

            panel.Children.Add(new TextBlock { Text = "🌈", FontSize = 52, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("RGB-Akzentfarbe freigeschaltet"),
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 10)
            });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Du hast den Konami-Code gefunden! Als Belohnung gibt es ab jetzt die RGB-Akzentfarbe: Sie wechselt dauernd durch alle Regenbogenfarben. Du findest sie für immer unter Einstellungen → Darstellung."),
                Foreground = MakeBrush("#D1D5DB"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            });

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            var later = new System.Windows.Controls.Button { Content = Loc.T("Später"), Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(22, 8, 22, 8), IsCancel = true };
            var enable = new System.Windows.Controls.Button { Content = Loc.T("Jetzt einschalten"), Padding = new Thickness(22, 8, 22, 8), IsDefault = true };
            enable.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            buttons.Children.Add(later);
            buttons.Children.Add(enable);
            panel.Children.Add(buttons);

            enable.Click += (s, e) =>
            {
                rainbowTimer?.Stop();
                settings.RgbAccent = true;
                SaveSettings();
                UpdateRgbUi();
                UpdateRgbAccent();
                dialog.DialogResult = true;
            };

            dialog.ShowDialog();
        }

        // ───────────────────────────── Nach oben in den Einstellungen ─────────────────────────────

        private void BtnSettingsTop_Click(object sender, RoutedEventArgs e) => ViewSettings.ScrollToTop();

        // ═════════════════════════════ Einstellungs-Profile ═════════════════════════════

        /// <summary>Bereiche, die ein Profil umschalten kann, mit den zugehörigen Einstellungen.</summary>
        private static readonly (string Key, string Title, string[] Props)[] ProfileGroups =
        {
            ("design", "Farben und Design", new[] { "BackgroundColor", "AccentColor", "SidebarColor", "NavTextColor", "NavActiveColor", "GlassEffect",
                "AnimatedBackground", "OledMode", "RgbAccent", "RgbSpeed", "UiScale", "CardWidth", "CardCorner", "CardSpacing", "CardAspect",
                "CardLayout", "LibraryView", "HoverZoom", "HoverGlow" }),
            ("background", "Hintergrund", new[] { "BackgroundImagePath", "BgDim", "BgBlur" }),
            ("streamer", "Streamer-Modus", new[] { "StreamerMode", "StreamerAuto", "StreamHideName", "StreamHideLocation", "StreamHideStats",
                "StreamHideRecent", "StreamHidePaths", "StreamHideShots", "StreamHidePc", "StreamHideMusic", "StreamMuteAlerts", "StreamCensorStyle", "ObsAutoStreamer" }),
            ("power", "Energieplan", Array.Empty<string>()),
            ("sidebar", "Seitenleisten-Bereiche", new[] { "HiddenSections", "SidebarCollapsed", "SidebarPosition" }),
            ("controller", "Controller-Optionen", new[] { "ControllerSupport", "ControllerLayout", "PadCombo", "PadComboMode", "PadComboInGame",
                "PadVibrate", "PadRepeat", "PadAutoStart" })
        };

        private static readonly string[] ProfileIcons = { "🎮", "📡", "💼", "🌙", "🎧", "⭐", "🏆", "🛋" };
        private static readonly string[] ObsProcessNames = { "obs64", "obs32", "obs" };

        private sealed class ProfileRestorePoint
        {
            public Dictionary<string, JsonElement> Values = new();
            public string PowerPlan = string.Empty;
            public string ActiveId = string.Empty;
        }

        private ProfileRestorePoint? gameRestore, obsRestore;
        private readonly DispatcherTimer profileObsTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private bool obsProcessRunning;
        private Forms.ToolStripMenuItem? trayProfilesItem;

        private void InitExtras17()
        {
            SeedProfiles();
            RenderProfiles();

            profileObsTimer.Tick += async (s, e) => await CheckObsProcessForProfileAsync();
            profileObsTimer.Start();
        }

        private void SeedProfiles()
        {
            if (settings.ProfilesSeeded) return;
            settings.ProfilesSeeded = true;

            // Drei Vorschläge: Sie schalten nur den Streamer-Modus und den Energieplan. Mit „Aktuellen Stand speichern“ kommt mehr dazu.
            SettingsProfile Make(string name, string icon, bool streamer, string plan) => new()
            {
                Name = name,
                Icon = icon,
                Groups = new List<string> { "streamer", "power" },
                Values = new Dictionary<string, JsonElement> { ["StreamerMode"] = JsonSerializer.SerializeToElement(streamer) },
                PowerPlan = plan
            };

            if (settings.Profiles.Count == 0)
            {
                settings.Profiles.Add(Make("Zocken", "🎮", false, "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));
                settings.Profiles.Add(Make("Streamen", "📡", true, "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));
                settings.Profiles.Add(Make("Büro", "💼", false, "381b4222-f694-41f0-9685-ff5bb260df2e"));
            }
            SaveSettings();
        }

        private SettingsProfile? FindProfile(string id)
            => string.IsNullOrEmpty(id) ? null : settings.Profiles.FirstOrDefault(p => p.Id == id);

        // ───────── Werte erfassen und anwenden ─────────

        private Dictionary<string, JsonElement> CaptureProfileValues(IEnumerable<string> groups)
        {
            var values = new Dictionary<string, JsonElement>();
            foreach (var group in ProfileGroups.Where(g => groups.Contains(g.Key)))
            {
                foreach (string name in group.Props)
                {
                    var property = typeof(AppSettings).GetProperty(name);
                    if (property == null) continue;
                    values[name] = JsonSerializer.SerializeToElement(property.GetValue(settings), property.PropertyType);
                }
            }
            return values;
        }

        /// <summary>Setzt die gespeicherten Werte und frischt die Oberfläche auf. Unbekannte Einträge werden übersprungen.</summary>
        private void ApplyProfileValues(Dictionary<string, JsonElement> values)
        {
            bool design = false;
            var designProps = ProfileGroups.First(g => g.Key == "design").Props.Concat(ProfileGroups.First(g => g.Key == "background").Props).ToHashSet();

            foreach (var (name, value) in values)
            {
                var property = typeof(AppSettings).GetProperty(name);
                if (property == null || !property.CanWrite) continue;
                try
                {
                    property.SetValue(settings, value.Deserialize(property.PropertyType));
                    if (designProps.Contains(name)) design = true;
                }
                catch { }
            }

            // Absichern wie beim Laden
            settings.HiddenSections ??= new List<string>();
            settings.BackgroundImagePath ??= string.Empty;
            if (!settings.RgbUnlocked) settings.RgbAccent = false;
            SaveSettings();

            isLoadingSettings = true;
            try { PopulateControlsFromSettings(); }
            finally { isLoadingSettings = false; }

            if (design)
            {
                imageCache.Clear();
                ApplyTheme();
                ApplyBackgroundImage();
                ApplyGlowBackground();
                ApplyViewSettings();
                ApplyFilter();
                ApplyPixelShift();
            }
            ApplySidebar(false);
            ApplyNavVisibility();
            BuildNavSettings();
            ApplyStreamerMode();
            UpdateStreamerToggles();
            RefreshDashboard();
        }

        private static async Task<string> ReadActivePowerPlanAsync()
        {
            var result = await Task.Run(() => RunHidden("powercfg", "/getactivescheme"));
            var match = Regex.Match(result.Output, @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
            return match.Success ? match.Value : string.Empty;
        }

        private void ApplyProfile(SettingsProfile profile, bool auto)
        {
            ApplyProfileValues(profile.Values);
            if (profile.Groups.Contains("power") && profile.PowerPlan.Length > 0) _ = ActivatePowerPlanAsync(profile.PowerPlan, auto);

            settings.ActiveProfileId = profile.Id;
            SaveSettings();
            RenderProfiles();
            ShowToast(profile.Icon, auto ? "Profil automatisch gewechselt" : "Profil gewechselt", Loc.T($"Profil „{profile.Name}“ ist aktiv."), 4);
        }

        /// <summary>Umschalten von Hand (Einstellungen, Suche, Infobereich). Eine laufende Automatik kehrt danach nicht mehr zurück.</summary>
        private void ActivateProfileManually(SettingsProfile profile)
        {
            gameRestore = null;
            obsRestore = null;
            ApplyProfile(profile, false);
        }

        // ───────── Automatik: Spielstart und OBS ─────────

        private async Task AutoActivateProfileAsync(string profileId, bool forGame)
        {
            var profile = FindProfile(profileId);
            if (profile == null || settings.ActiveProfileId == profile.Id) return;
            if (forGame ? gameRestore != null : obsRestore != null) return;

            // Den jetzigen Stand genau der Bereiche merken, die das Profil ändert
            var point = new ProfileRestorePoint
            {
                Values = CaptureProfileValues(profile.Groups),
                ActiveId = settings.ActiveProfileId
            };
            if (profile.Groups.Contains("power") && profile.PowerPlan.Length > 0) point.PowerPlan = await ReadActivePowerPlanAsync();

            if (forGame) gameRestore = point;
            else obsRestore = point;
            ApplyProfile(profile, true);
        }

        private void AutoRestoreProfile(bool forGame)
        {
            var point = forGame ? gameRestore : obsRestore;
            if (forGame) gameRestore = null;
            else obsRestore = null;
            if (point == null) return;

            ApplyProfileValues(point.Values);
            if (point.PowerPlan.Length > 0) _ = ActivatePowerPlanAsync(point.PowerPlan, true);
            settings.ActiveProfileId = FindProfile(point.ActiveId)?.Id ?? string.Empty;
            SaveSettings();
            RenderProfiles();
            ShowToast("↩", "Profil zurückgesetzt", forGame ? "Das Spiel ist beendet. Deine vorherigen Einstellungen gelten wieder." : "OBS wurde beendet. Deine vorherigen Einstellungen gelten wieder.", 4);
        }

        private void OnProfileGameStarted()
        {
            if (settings.ProfileOnGame.Length > 0) _ = AutoActivateProfileAsync(settings.ProfileOnGame, true);
        }

        private void OnProfileGameEnded()
        {
            if (gameRestore != null) AutoRestoreProfile(true);
        }

        private async Task CheckObsProcessForProfileAsync()
        {
            if (settings.ProfileOnObs.Length == 0 && obsRestore == null)
            {
                obsProcessRunning = false;
                return;
            }

            bool running = await Task.Run(() => ObsProcessNames.Any(IsProcessRunning));
            if (running == obsProcessRunning) return;
            obsProcessRunning = running;

            if (running && settings.ProfileOnObs.Length > 0) await AutoActivateProfileAsync(settings.ProfileOnObs, false);
            else if (!running && obsRestore != null) AutoRestoreProfile(false);
        }

        // ───────── Anzeige in den Einstellungen ─────────

        private string DescribeProfile(SettingsProfile profile)
        {
            var parts = new List<string>();
            foreach (var group in ProfileGroups.Where(g => profile.Groups.Contains(g.Key)))
            {
                if (group.Key == "power")
                {
                    string plan = PowerPlanNames.TryGetValue(profile.PowerPlan, out var name) ? name : string.Empty;
                    parts.Add(plan.Length > 0 ? Loc.T("Energieplan") + ": " + Loc.T(plan) : Loc.T("Energieplan"));
                }
                else if (group.Key == "streamer" && profile.Values.TryGetValue("StreamerMode", out var mode))
                {
                    parts.Add(Loc.T(mode.ValueKind == JsonValueKind.True ? "Streamer-Modus an" : "Streamer-Modus aus"));
                }
                else parts.Add(Loc.T(group.Title));
            }
            return parts.Count == 0 ? Loc.T("Schaltet noch nichts um") : string.Join(" · ", parts);
        }

        private void RenderProfiles()
        {
            if (ProfilesPanel == null) return;
            ProfilesPanel.Children.Clear();

            foreach (var item in settings.Profiles)
            {
                var profile = item;
                bool active = profile.Id == settings.ActiveProfileId;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(new TextBlock { Text = profile.Icon, FontSize = 22, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center });

                var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                var title = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                title.Children.Add(new TextBlock { Text = profile.Name, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });
                if (active)
                    title.Children.Add(new TextBlock { Text = Loc.T("aktiv"), Foreground = MakeBrush("#34D399"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 1, 0, 0) });
                texts.Children.Add(title);
                texts.Children.Add(new TextBlock { Text = DescribeProfile(profile), Foreground = BrushSubtle, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);

                var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var use = new System.Windows.Controls.Button { Content = Loc.T("Aktivieren"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
                if (!active) use.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                use.Click += (s, e) => ActivateProfileManually(profile);
                var edit = new System.Windows.Controls.Button { Content = Loc.T("Bearbeiten"), Padding = new Thickness(14, 6, 14, 6) };
                edit.Click += (s, e) => EditProfile(profile);
                buttons.Children.Add(use);
                buttons.Children.Add(edit);
                Grid.SetColumn(buttons, 2);
                row.Children.Add(buttons);

                ProfilesPanel.Children.Add(row);
            }

            if (settings.Profiles.Count == 0)
                ProfilesPanel.Children.Add(new TextBlock { Text = Loc.T("Noch keine Profile angelegt."), Foreground = BrushSubtle, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });

            RenderProfileAutoChips(ProfileOnGamePanel, settings.ProfileOnGame, id => settings.ProfileOnGame = id);
            RenderProfileAutoChips(ProfileOnObsPanel, settings.ProfileOnObs, id => settings.ProfileOnObs = id);
        }

        private void RenderProfileAutoChips(System.Windows.Controls.WrapPanel panel, string selected, Action<string> set)
        {
            panel.Children.Clear();
            string group = "ProfileAuto" + panel.Name;
            var style = TryFindResource("ChipStyle") as Style;

            void Chip(string text, string id)
            {
                var chip = new System.Windows.Controls.RadioButton { Content = text, GroupName = group, IsChecked = id == selected };
                if (style != null) chip.Style = style;
                chip.Checked += (s, e) =>
                {
                    set(id);
                    SaveSettings();
                };
                panel.Children.Add(chip);
            }

            Chip(Loc.T("Aus"), string.Empty);
            foreach (var profile in settings.Profiles) Chip(profile.Icon + " " + profile.Name, profile.Id);
        }

        // ───────── Anlegen, bearbeiten, löschen ─────────

        private void BtnNewProfile_Click(object sender, RoutedEventArgs e) => EditProfile(null);

        private void EditProfile(SettingsProfile? existing)
        {
            bool isNew = existing == null;
            var dialog = CreateDialog(isNew ? "Neues Profil" : "Profil bearbeiten", 560, out var panel);

            panel.Children.Add(new TextBlock { Text = Loc.T("Name"), Foreground = BrushSubtle, FontSize = 12 });
            var name = new System.Windows.Controls.TextBox { Text = existing?.Name ?? string.Empty, Height = 38, Margin = new Thickness(0, 4, 0, 14) };
            panel.Children.Add(name);

            panel.Children.Add(new TextBlock { Text = Loc.T("Symbol"), Foreground = BrushSubtle, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            var icons = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            string icon = existing?.Icon ?? "⭐";
            var chipStyle = TryFindResource("ChipStyle") as Style;
            foreach (string candidate in ProfileIcons)
            {
                string value = candidate;
                var chip = new System.Windows.Controls.RadioButton { Content = value, GroupName = "ProfileIcon", IsChecked = value == icon, FontSize = 16 };
                if (chipStyle != null) chip.Style = chipStyle;
                chip.Checked += (s, e) => icon = value;
                icons.Children.Add(chip);
            }
            panel.Children.Add(icons);

            panel.Children.Add(new TextBlock { Text = Loc.T("Was das Profil umschaltet"), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            var groupBoxes = new Dictionary<string, System.Windows.Controls.CheckBox>();
            var planPanel = new WrapPanel { Margin = new Thickness(56, 0, 0, 10) };
            foreach (var group in ProfileGroups)
            {
                var box = new System.Windows.Controls.CheckBox
                {
                    Content = Loc.T(group.Title),
                    IsChecked = existing == null ? group.Key is "design" or "streamer" : existing.Groups.Contains(group.Key),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                groupBoxes[group.Key] = box;
                panel.Children.Add(box);
                if (group.Key == "power") panel.Children.Add(planPanel);
            }

            // Energieplan: einer der Windows-Standardpläne
            string plan = existing?.PowerPlan ?? string.Empty;
            if (!PowerPlanNames.ContainsKey(plan))
                plan = PowerPlanNames.FirstOrDefault(p => p.Value == currentPowerPlanName).Key ?? "381b4222-f694-41f0-9685-ff5bb260df2e";
            foreach (var (guid, title) in PowerPlanNames)
            {
                string value = guid;
                var chip = new System.Windows.Controls.RadioButton { Content = Loc.T(title), GroupName = "ProfilePlan", IsChecked = value.Equals(plan, StringComparison.OrdinalIgnoreCase) };
                if (chipStyle != null) chip.Style = chipStyle;
                chip.Checked += (s, e) => plan = value;
                planPanel.Children.Add(chip);
            }
            void UpdatePlanVisibility() => planPanel.Visibility = groupBoxes["power"].IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            groupBoxes["power"].Checked += (s, e) => UpdatePlanVisibility();
            groupBoxes["power"].Unchecked += (s, e) => UpdatePlanVisibility();
            UpdatePlanVisibility();

            System.Windows.Controls.CheckBox? capture = null;
            if (!isNew)
            {
                capture = new System.Windows.Controls.CheckBox { Content = Loc.T("Meine aktuellen Einstellungen in das Profil übernehmen"), Margin = new Thickness(0, 6, 0, 0) };
                panel.Children.Add(capture);
            }
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T(isNew
                    ? "Das Profil merkt sich deine jetzigen Einstellungen für die ausgewählten Bereiche."
                    : "Neu ausgewählte Bereiche übernehmen immer deine jetzigen Einstellungen."),
                Foreground = BrushSubtle,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 18)
            });

            var buttons = new Grid();
            var right = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var cancel = new System.Windows.Controls.Button { Content = "Abbrechen", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var save = new System.Windows.Controls.Button { Content = "Speichern", Padding = new Thickness(28, 8, 28, 8), IsDefault = true };
            save.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            right.Children.Add(cancel);
            right.Children.Add(save);
            buttons.Children.Add(right);

            if (existing != null)
            {
                var delete = new System.Windows.Controls.Button { Content = "Löschen", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Background = MakeBrush("#7F1D1D") };
                delete.Click += (s, e) =>
                {
                    if (Msg($"Profil „{existing.Name}“ wirklich löschen?", "Profil löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    settings.Profiles.Remove(existing);
                    if (settings.ActiveProfileId == existing.Id) settings.ActiveProfileId = string.Empty;
                    if (settings.ProfileOnGame == existing.Id) settings.ProfileOnGame = string.Empty;
                    if (settings.ProfileOnObs == existing.Id) settings.ProfileOnObs = string.Empty;
                    SaveSettings();
                    dialog.DialogResult = false;
                };
                buttons.Children.Add(delete);
            }
            panel.Children.Add(buttons);

            save.Click += (s, e) =>
            {
                string text = name.Text.Trim();
                if (text.Length == 0)
                {
                    name.Focus();
                    return;
                }

                var groups = ProfileGroups.Select(g => g.Key).Where(k => groupBoxes[k].IsChecked == true).ToList();
                var profile = existing ?? new SettingsProfile();
                var added = groups.Except(profile.Groups).ToList();
                bool takeAll = isNew || capture?.IsChecked == true;

                // Werte: bei „übernehmen“ alles neu erfassen, sonst nur neu hinzugekommene Bereiche; abgewählte Bereiche entfernen
                var values = takeAll ? new Dictionary<string, JsonElement>() : new Dictionary<string, JsonElement>(profile.Values);
                foreach (var (key, value) in CaptureProfileValues(takeAll ? groups : added)) values[key] = value;
                var keep = ProfileGroups.Where(g => groups.Contains(g.Key)).SelectMany(g => g.Props).ToHashSet();
                foreach (string key in values.Keys.Where(k => !keep.Contains(k)).ToList()) values.Remove(key);

                profile.Name = text;
                profile.Icon = icon;
                profile.Groups = groups;
                profile.Values = values;
                profile.PowerPlan = groups.Contains("power") ? plan : string.Empty;
                if (isNew) settings.Profiles.Add(profile);
                SaveSettings();
                dialog.DialogResult = true;
            };

            dialog.Loaded += (s, e) => name.Focus();
            dialog.ShowDialog();
            RenderProfiles();
        }

        // ───────── Infobereich ─────────

        private void AddTrayProfilesMenu(Forms.ContextMenuStrip menu)
        {
            trayProfilesItem = new Forms.ToolStripMenuItem(Loc.T("Profil wechseln"));
            if (trayProfilesItem.DropDown is Forms.ToolStripDropDownMenu dropDown)
            {
                dropDown.Renderer = menu.Renderer;
                dropDown.BackColor = menu.BackColor;
                dropDown.ForeColor = menu.ForeColor;
                dropDown.ShowImageMargin = false;
                dropDown.ShowCheckMargin = true;
            }
            menu.Items.Add(trayProfilesItem);
            menu.Opening += (s, e) => RefreshTrayProfiles();
        }

        private void RefreshTrayProfiles()
        {
            if (trayProfilesItem == null) return;
            trayProfilesItem.Text = Loc.T("Profil wechseln");
            trayProfilesItem.DropDownItems.Clear();
            foreach (var item in settings.Profiles)
            {
                var profile = item;
                var entry = new Forms.ToolStripMenuItem(profile.Icon + "  " + profile.Name) { Checked = profile.Id == settings.ActiveProfileId };
                entry.Click += (s, e) => ActivateProfileManually(profile);
                trayProfilesItem.DropDownItems.Add(entry);
            }
            trayProfilesItem.Visible = settings.Profiles.Count > 0;
        }

        // ───────────────────────────── Bild zuschneiden (Hintergrund und Logo) ─────────────────────────────

        private static BitmapImage LoadFullBitmap(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;            // Datei danach nicht gesperrt
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }

        /// <summary>
        /// Zeigt das Bild in einem Rahmen mit dem gewünschten Seitenverhältnis: verschieben mit der Maus, zoomen mit Regler oder Mausrad.
        /// Liefert den Ausschnitt oder null (abgebrochen). useWhole = „Ganzes Bild verwenden“ gewählt.
        /// </summary>
        private BitmapSource? ShowImageCropper(string path, double aspect, string title, bool allowWhole, int maxWidth, out bool useWhole)
        {
            useWhole = false;
            BitmapImage source;
            try
            {
                source = LoadFullBitmap(path);
            }
            catch (Exception ex)
            {
                Msg($"Das Bild konnte nicht geladen werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            int pixelWidth = source.PixelWidth, pixelHeight = source.PixelHeight;
            if (pixelWidth < 2 || pixelHeight < 2) return null;

            var dialog = CreateDialog(title, 880, out var panel);
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Ziehe das Bild mit der Maus an die gewünschte Stelle. Mit dem Regler oder dem Mausrad zoomst du hinein."),
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Sichtfenster im gewünschten Seitenverhältnis
            double viewWidth = 820, viewHeight = viewWidth / aspect;
            if (viewHeight > 470)
            {
                viewHeight = 470;
                viewWidth = viewHeight * aspect;
            }

            var image = new System.Windows.Controls.Image { Source = source, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var canvas = new Canvas
            {
                Width = viewWidth,
                Height = viewHeight,
                ClipToBounds = true,
                Background = MakeBrush("#0B0E16"),
                Cursor = System.Windows.Input.Cursors.SizeAll
            };
            canvas.Children.Add(image);
            var frame = new Border { BorderThickness = new Thickness(2), Child = canvas, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
            frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            panel.Children.Add(frame);

            double baseScale = Math.Max(viewWidth / pixelWidth, viewHeight / pixelHeight);
            double zoom = 1, offsetX, offsetY;
            {
                double s0 = baseScale;
                offsetX = (viewWidth - pixelWidth * s0) / 2;
                offsetY = (viewHeight - pixelHeight * s0) / 2;
            }

            void Layout()
            {
                double scale = baseScale * zoom;
                image.Width = pixelWidth * scale;
                image.Height = pixelHeight * scale;
                offsetX = Math.Clamp(offsetX, viewWidth - pixelWidth * scale, 0);
                offsetY = Math.Clamp(offsetY, viewHeight - pixelHeight * scale, 0);
                Canvas.SetLeft(image, offsetX);
                Canvas.SetTop(image, offsetY);
            }
            Layout();

            // Ziehen
            System.Windows.Point? dragStart = null;
            double startX = 0, startY = 0;
            canvas.MouseLeftButtonDown += (s, e) =>
            {
                dragStart = e.GetPosition(canvas);
                startX = offsetX;
                startY = offsetY;
                canvas.CaptureMouse();
            };
            canvas.MouseMove += (s, e) =>
            {
                if (dragStart is not System.Windows.Point start) return;
                var now = e.GetPosition(canvas);
                offsetX = startX + (now.X - start.X);
                offsetY = startY + (now.Y - start.Y);
                Layout();
            };
            canvas.MouseLeftButtonUp += (s, e) =>
            {
                dragStart = null;
                canvas.ReleaseMouseCapture();
            };

            // Zoom (bleibt um die Mitte des Sichtfensters)
            var zoomRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            zoomRow.Children.Add(new TextBlock { Text = "🔍  " + Loc.T("Zoom"), Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            var slider = new Slider { Minimum = 1, Maximum = 4, Value = 1, SmallChange = 0.05, LargeChange = 0.25 };
            Grid.SetColumn(slider, 1);
            zoomRow.Children.Add(slider);
            panel.Children.Add(zoomRow);

            slider.ValueChanged += (s, e) =>
            {
                double before = baseScale * zoom;
                double centerX = (viewWidth / 2 - offsetX) / before;
                double centerY = (viewHeight / 2 - offsetY) / before;
                zoom = slider.Value;
                double after = baseScale * zoom;
                offsetX = viewWidth / 2 - centerX * after;
                offsetY = viewHeight / 2 - centerY * after;
                Layout();
            };
            canvas.MouseWheel += (s, e) => slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? 0.15 : -0.15), slider.Minimum, slider.Maximum);

            // Knöpfe
            bool confirmed = false, whole = false;
            var buttons = new Grid();
            var right = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var cancel = new System.Windows.Controls.Button { Content = Loc.T("Abbrechen"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var ok = new System.Windows.Controls.Button { Content = Loc.T("Übernehmen"), Padding = new Thickness(28, 8, 28, 8), IsDefault = true };
            ok.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            ok.Click += (s, e) =>
            {
                confirmed = true;
                dialog.DialogResult = true;
            };
            right.Children.Add(cancel);
            right.Children.Add(ok);
            buttons.Children.Add(right);
            if (allowWhole)
            {
                var all = new System.Windows.Controls.Button { Content = Loc.T("Ganzes Bild verwenden"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                all.Click += (s, e) =>
                {
                    whole = true;
                    dialog.DialogResult = true;
                };
                buttons.Children.Add(all);
            }
            panel.Children.Add(buttons);

            dialog.ShowDialog();
            useWhole = whole;
            if (!confirmed) return null;

            // Ausschnitt in Bildpunkten des Originals
            double finalScale = baseScale * zoom;
            int x = (int)Math.Round(-offsetX / finalScale);
            int y = (int)Math.Round(-offsetY / finalScale);
            int width = (int)Math.Round(viewWidth / finalScale);
            int height = (int)Math.Round(viewHeight / finalScale);
            x = Math.Clamp(x, 0, pixelWidth - 1);
            y = Math.Clamp(y, 0, pixelHeight - 1);
            width = Math.Clamp(width, 1, pixelWidth - x);
            height = Math.Clamp(height, 1, pixelHeight - y);

            BitmapSource result = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
            if (width > maxWidth)
            {
                double factor = (double)maxWidth / width;
                result = new TransformedBitmap(result, new ScaleTransform(factor, factor));
            }
            result.Freeze();
            return result;
        }

        private static void SaveBitmap(BitmapSource bitmap, string path, bool jpeg)
        {
            BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        /// <summary>Löscht ältere, vom Launcher erzeugte Dateien eines Musters (zum Beispiel alte Ausschnitte), außer der aktuellen.</summary>
        private static void DeleteOldGenerated(string pattern, string keep)
        {
            try
            {
                foreach (string file in Directory.GetFiles(SettingsDir, pattern))
                {
                    if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        // ───────── Hintergrundbild: Ausschnitt wählen ─────────

        /// <summary>Neues Hintergrundbild: Original sichern, dann Ausschnitt wählen lassen.</summary>
        private void SetBackgroundFromFile(string file)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string original = System.IO.Path.Combine(SettingsDir, $"background_original_{DateTime.Now:yyyyMMddHHmmss}" + System.IO.Path.GetExtension(file).ToLowerInvariant());
                File.Copy(file, original, true);
                if (!EditBackground(original))
                {
                    try { File.Delete(original); } catch { }   // abgebrochen: nichts ändern
                    return;
                }
                DeleteOldGenerated("background_original_*", original);
            }
            catch (Exception ex)
            {
                Msg($"Das Bild konnte nicht geladen werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Ausschnitt für den Hintergrund wählen. Gibt false zurück, wenn abgebrochen wurde.</summary>
        private bool EditBackground(string source)
        {
            double width = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : ActualWidth;
            double height = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : ActualHeight;
            double aspect = Math.Clamp(width / Math.Max(1, height), 0.5, 4);

            var cropped = ShowImageCropper(source, aspect, "Ausschnitt für den Hintergrund", true, 3840, out bool whole);
            string target;
            if (whole)
            {
                target = source;
            }
            else if (cropped != null)
            {
                target = System.IO.Path.Combine(SettingsDir, $"background_crop_{DateTime.Now:yyyyMMddHHmmss}.jpg");
                SaveBitmap(cropped, target, true);
            }
            else
            {
                return false;
            }

            DeleteOldGenerated("background_crop_*", target);
            settings.BackgroundImagePath = target;
            settings.BackgroundSourcePath = source;
            loadedBackgroundPath = string.Empty;
            SaveSettings();
            ApplyViewSettings();
            return true;
        }

        private void BtnEditBgImage_Click(object sender, RoutedEventArgs e)
        {
            string source = File.Exists(settings.BackgroundSourcePath) ? settings.BackgroundSourcePath
                          : HasBackgroundImage() ? settings.BackgroundImagePath
                          : string.Empty;
            if (source.Length == 0)
            {
                Msg("Es ist noch kein Hintergrundbild gesetzt. Wähle zuerst eines aus.", "Hintergrundbild");
                return;
            }

            try { EditBackground(source); }
            catch (Exception ex) { Msg($"Das Bild konnte nicht geladen werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ───────── Eigenes Logo-Bild ─────────

        private bool HasBrandImage => !string.IsNullOrEmpty(settings.BrandLogoImage) && File.Exists(settings.BrandLogoImage);

        /// <summary>Logo oben links: eigenes Bild (rund, abgerundet oder eckig) oder das Pudel-Logo.</summary>
        private void ApplyBrandImage()
        {
            if (BrandLogo == null) return;

            if (!HasBrandImage)
            {
                if (BrandLogo.Content is not Viewbox) BrandLogo.Content = FindResource("DfpLogo");
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(settings.BrandLogoImage);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.DecodePixelWidth = 256;
                bitmap.EndInit();
                bitmap.Freeze();

                double radius = settings.BrandLogoShape switch { "circle" => 999, "rounded" => 14, _ => 2 };
                BrandLogo.Content = new Border
                {
                    CornerRadius = new CornerRadius(radius),
                    Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill }
                };
            }
            catch
            {
                BrandLogo.Content = FindResource("DfpLogo");
            }
        }

        private void BtnBrandImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Logo-Bild wählen"),
                Filter = Loc.T("Bilder") + " (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            };
            if (dialog.ShowDialog() != true) return;

            var cropped = ShowImageCropper(dialog.FileName, 1.0, "Ausschnitt für das Logo", false, 512, out _);
            if (cropped == null) return;

            try
            {
                Directory.CreateDirectory(SettingsDir);
                string target = System.IO.Path.Combine(SettingsDir, $"brand_logo_{DateTime.Now:yyyyMMddHHmmss}.png");
                SaveBitmap(cropped, target, false);
                DeleteOldGenerated("brand_logo_*", target);

                settings.BrandLogoImage = target;
                SaveSettings();
                ApplyBrand();
                PopulateBrandImage();
            }
            catch (Exception ex)
            {
                Msg($"Das Logo konnte nicht gespeichert werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBrandImageReset_Click(object sender, RoutedEventArgs e)
        {
            settings.BrandLogoImage = string.Empty;
            SaveSettings();
            ApplyBrand();
            PopulateBrandImage();
        }

        private void LogoShape_Checked(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings || sender is not FrameworkElement { Tag: string shape }) return;
            settings.BrandLogoShape = shape;
            SaveSettings();
            ApplyBrand();
        }

        private void PopulateBrandImage()
        {
            if (ChipLogoCircle == null) return;
            bool before = isLoadingSettings;
            isLoadingSettings = true;
            try
            {
                ChipLogoCircle.IsChecked = settings.BrandLogoShape == "circle";
                ChipLogoRounded.IsChecked = settings.BrandLogoShape == "rounded";
                ChipLogoSquare.IsChecked = settings.BrandLogoShape == "square";
                BtnBrandImageReset.IsEnabled = HasBrandImage;
                BrandShapePanel.Visibility = HasBrandImage ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                isLoadingSettings = before;
            }
        }
    }
}
