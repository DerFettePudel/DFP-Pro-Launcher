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
        // ───────────────────────────── Dashboard, Uhr & Wetter ─────────────────────────────

        private void UpdateClock()
        {
            if (!settings.ShowClock || ViewDashboard.Visibility != Visibility.Visible) return;

            var now = DateTime.Now;
            string format = settings.Clock24h
                ? (settings.ClockSeconds ? "HH:mm:ss" : "HH:mm")
                : (settings.ClockSeconds ? "hh:mm:ss tt" : "hh:mm tt");

            TxtClock.Text = now.ToString(format, CultureInfo.InvariantCulture);
            TxtClockDate.Text = now.ToString("D", GermanCulture);
            TxtClockWeek.Text = $"Kalenderwoche {ISOWeek.GetWeekOfYear(now)}";
        }

        private static (string Icon, string Text) DescribeWeather(int code, bool isDay) => code switch
        {
            0 => (isDay ? "☀" : "🌙", "Klarer Himmel"),
            1 => (isDay ? "🌤" : "🌙", "Überwiegend klar"),
            2 => ("⛅", "Teilweise bewölkt"),
            3 => ("☁", "Bedeckt"),
            45 or 48 => ("🌫", "Nebel"),
            51 or 53 or 55 => ("🌦", "Nieselregen"),
            56 or 57 => ("🌧", "Gefrierender Nieselregen"),
            61 or 63 or 65 => ("🌧", "Regen"),
            66 or 67 => ("🌧", "Gefrierender Regen"),
            71 or 73 or 75 => ("❄", "Schneefall"),
            77 => ("❄", "Schneegriesel"),
            80 or 81 or 82 => ("🌦", "Regenschauer"),
            85 or 86 => ("🌨", "Schneeschauer"),
            95 => ("⛈", "Gewitter"),
            96 or 99 => ("⛈", "Gewitter mit Hagel"),
            _ => ("🌡", "Unbekannt")
        };

        private void SetWeather(string icon, string temp, string description, string details, string range)
        {
            TxtWeatherIcon.Text = icon;
            TxtWeatherTemp.Text = temp;
            TxtWeatherDesc.Text = description;
            TxtWeatherDetails.Text = details;
            TxtWeatherRange.Text = range;
        }

        private async Task<(double Lat, double Lon, string Name)?> ResolveLocationAsync()
        {
            string city = settings.WeatherCity.Trim();
            if (weatherLocation != null && weatherLocationKey == city) return weatherLocation;

            try
            {
                if (city.Length > 0)
                {
                    string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=de&format=json";
                    using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));

                    if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                    {
                        var first = results[0];
                        weatherLocation = (first.GetProperty("latitude").GetDouble(),
                                           first.GetProperty("longitude").GetDouble(),
                                           GetJsonString(first, "name"));
                    }
                    else
                    {
                        weatherLocation = null;
                    }
                }
                else
                {
                    // Ungefährer Standort über die IP-Adresse
                    using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipwho.is/"));
                    var root = doc.RootElement;

                    if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                        weatherLocation = null;
                    else
                        weatherLocation = (root.GetProperty("latitude").GetDouble(),
                                           root.GetProperty("longitude").GetDouble(),
                                           GetJsonString(root, "city"));
                }

                weatherLocationKey = city;
            }
            catch
            {
                weatherLocation = null;
            }

            return weatherLocation;
        }

        private async Task RefreshWeatherAsync()
        {
            if (!settings.ShowWeather) return;

            if (!settings.OnlineFeatures)
            {
                SetWeather("🌐", "–", "Online-Funktionen sind deaktiviert", "Aktiviere sie in den Einstellungen.", string.Empty);
                return;
            }

            try
            {
                var location = await ResolveLocationAsync();
                if (location == null)
                {
                    SetWeather("❓", "–", "Standort nicht gefunden", "Gib in den Einstellungen einen Ort an.", string.Empty);
                    return;
                }

                string lat = location.Value.Lat.ToString(CultureInfo.InvariantCulture);
                string lon = location.Value.Lon.ToString(CultureInfo.InvariantCulture);
                string unit = settings.Fahrenheit ? "fahrenheit" : "celsius";
                string url = "https://api.open-meteo.com/v1/forecast" +
                             $"?latitude={lat}&longitude={lon}" +
                             "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day" +
                             "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1&wind_speed_unit=kmh" +
                             $"&temperature_unit={unit}";

                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                var current = doc.RootElement.GetProperty("current");
                var daily = doc.RootElement.GetProperty("daily");

                double temp = current.GetProperty("temperature_2m").GetDouble();
                double feels = current.GetProperty("apparent_temperature").GetDouble();
                double humidity = current.GetProperty("relative_humidity_2m").GetDouble();
                double wind = current.GetProperty("wind_speed_10m").GetDouble();
                int code = current.GetProperty("weather_code").GetInt32();
                bool isDay = current.TryGetProperty("is_day", out var dayValue) && dayValue.GetInt32() == 1;
                double max = daily.GetProperty("temperature_2m_max")[0].GetDouble();
                double min = daily.GetProperty("temperature_2m_min")[0].GetDouble();

                string symbol = settings.Fahrenheit ? "°F" : "°C";
                var (icon, text) = DescribeWeather(code, isDay);
                string place = location.Value.Name;

                SetWeather(icon,
                    $"{temp:F0}{symbol}",
                    place.Length > 0 ? $"{text}  ·  {place}" : text,
                    $"Gefühlt {feels:F0}{symbol}  ·  Wind {wind:F0} km/h  ·  Luftfeuchte {humidity:F0} %",
                    $"Heute  {min:F0}{symbol} bis {max:F0}{symbol}");
            }
            catch
            {
                SetWeather("⚠", "–", "Wetter nicht erreichbar", "Prüfe deine Internetverbindung.", string.Empty);
            }
        }

        private void BtnRandomGame_Click(object sender, RoutedEventArgs e) => ShowRoulette();

        // ───────────────────────────── Dashboard ─────────────────────────────

        private void RefreshDashboardCore(bool animate = false)
        {
            if (TxtGreeting == null || RecentContainer == null) return;

            bool play = animate && settings.PageAnimations;

            TxtGreeting.Text = $"{GetGreeting()}, {DisplayUserName()}";
            TxtDate.Text = DateTime.Now.ToString("D", GermanCulture);

            int favoriteCount = VisibleGames.Count(g => g.IsFavorite);
            if (play)
            {
                CountUp(TxtStatGames, VisibleGames.Count());
                CountUp(TxtStatFavs, favoriteCount);
            }
            else
            {
                TxtStatGames.Text = VisibleGames.Count().ToString();
                TxtStatFavs.Text = favoriteCount.ToString();
            }
            TxtStatPlaytime.Text = FormatPlaytime(VisibleGames.Where(g => !IsTrackingIgnored(g)).Sum(g => g.PlaySeconds));

            var played = VisibleGames
                .Where(g => g.LastPlayed.HasValue)
                .OrderByDescending(g => g.LastPlayed)
                .ToList();

            TxtStatLast.Text = played.Count > 0 ? played[0].Name : "–";

            var favorites = VisibleGames
                .Where(g => g.IsFavorite)
                .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(settings.RecentCount)
                .ToList();

            BuildHero(played.FirstOrDefault() ?? favorites.FirstOrDefault(), play);

            RecentContainer.Children.Clear();
            RecentContainer.Margin = new Thickness(-settings.CardSpacing, 0, 0, 12);
            int index = 0;
            foreach (var game in played.Take(settings.RecentCount))
                RecentContainer.Children.Add(CreateGameCard(game, 180, play ? index++ : -1));
            TxtRecentEmpty.Visibility = played.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            FavoritesContainer.Children.Clear();
            FavoritesContainer.Margin = new Thickness(-settings.CardSpacing, 0, 0, 12);
            index = 0;
            foreach (var game in favorites)
                FavoritesContainer.Children.Add(CreateGameCard(game, 180, play ? index++ : -1));
            FavSection.Visibility = settings.ShowFavorites && favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildHero(GameItem? game, bool animate)
        {
            if (!settings.ShowHero || game == null)
            {
                HeroHost.Content = null;
                HeroHost.Visibility = Visibility.Collapsed;
                return;
            }

            HeroHost.Visibility = Visibility.Visible;
            BitmapImage? cover = HasCover(game) ? GetCachedImage(EffectiveCover(game)) : null;
            var themeColor = ParseColor(settings.BackgroundColor, "#0F111A");

            var hero = new Border
            {
                Height = 240,
                CornerRadius = new CornerRadius(18),
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                Background = BrushCardBg
            };

            var wide = GetHeroImage(game);
            if (wide == null) _ = EnsureHeroAsync(game);
            var backdrop = wide ?? cover;
            if (backdrop != null)
            {
                hero.Background = new ImageBrush(backdrop)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentY = wide != null ? AlignmentY.Center : AlignmentY.Top,
                    Opacity = 0.55
                };
            }

            var shade = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 0)
            };
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xF5, themeColor.R, themeColor.G, themeColor.B), 0.0));
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xB0, themeColor.R, themeColor.G, themeColor.B), 0.55));
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x30, themeColor.R, themeColor.G, themeColor.B), 1.0));

            var layout = new Grid { Background = shade };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var poster = new Border
            {
                Width = 126,
                Height = 189,
                Margin = new Thickness(26, 0, 24, 0),
                CornerRadius = new CornerRadius(12),
                Background = BrushCardBg,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            if (cover != null)
                poster.Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
            else
                poster.Child = new TextBlock
                {
                    Text = game.Name.Length > 0 ? game.Name.Substring(0, 1).ToUpperInvariant() : "?",
                    FontSize = 54,
                    FontWeight = FontWeights.Bold,
                    Foreground = BrushPlaceholder,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
            layout.Children.Add(poster);

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 26, 0) };
            Grid.SetColumn(texts, 1);

            var label = new TextBlock { Text = "WEITERSPIELEN", FontSize = 12, FontWeight = FontWeights.SemiBold };
            label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            texts.Children.Add(label);

            texts.Children.Add(new TextBlock
            {
                Text = game.Name,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 4)
            });

            var meta = new List<string> { game.Source };
            if (game.PlaySeconds >= 60 && !SHide(settings.StreamHideStats)) meta.Add(FormatPlaytime(game.PlaySeconds) + " gespielt");
            if (game.LastPlayed.HasValue) meta.Add("zuletzt " + FormatRelative(game.LastPlayed.Value));
            texts.Children.Add(new TextBlock
            {
                Text = string.Join("  ·  ", meta),
                Foreground = MakeBrush("#D1D5DB"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 18)
            });

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var play = new System.Windows.Controls.Button { Content = "▶  Weiterspielen", Padding = new Thickness(22, 11, 22, 11), Margin = new Thickness(0, 0, 10, 0) };
            play.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            play.Click += (s, e) => LaunchGame(game);
            var details = new System.Windows.Controls.Button { Content = "Details", Padding = new Thickness(18, 11, 18, 11) };
            details.Click += (s, e) => ShowGameDetails(game);
            buttons.Children.Add(play);
            buttons.Children.Add(details);
            texts.Children.Add(buttons);
            layout.Children.Add(texts);

            hero.Child = layout;
            HeroHost.Content = hero;

            if (animate)
            {
                hero.Opacity = 0;
                hero.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)));
            }
        }

        private void BtnSpotlight_Click(object sender, RoutedEventArgs e) => OpenSpotlight();
        private void BtnController_Click(object sender, RoutedEventArgs e) => ToggleControllerMode();

        // ───────────────────────────── Infobereich (Tray) ─────────────────────────────

        private void SetupTray()
        {
            try
            {
                trayIcon = new Forms.NotifyIcon
                {
                    Text = "DFP Pro Launcher · by Der_Fette_Pudel",
                    Icon = BitmapSourceToIcon(CreateLogoBitmap(32)),
                    Visible = settings.ShowTrayIcon || settings.MinimizeToTray
                };
                trayIcon.DoubleClick += (s, e) => ShowFromTray();
                HookTrayFlyout();

                var menu = CreateTrayMenu();
                menu.Items.Add("Öffnen", null, (s, e) => ShowFromTray());
                menu.Items.Add("Zufälliges Spiel", null, (s, e) =>
                {
                    ShowFromTray();
                    BtnRandomGame_Click(this, new RoutedEventArgs());
                });
                menu.Items.Add("Beenden", null, (s, e) => ExitApplication());
                trayIcon.ContextMenuStrip = menu;
                RebuildTrayMenu();
            }
            catch
            {
                trayIcon = null;
            }
        }

        private void HideToTray()
        {
            Hide();

            if (!trayHintShown && trayIcon != null)
            {
                trayHintShown = true;
                trayIcon.ShowBalloonTip(2500, "DFP Pro Launcher", Loc.T("Der Launcher läuft im Hintergrund weiter. Doppelklick auf das Symbol öffnet ihn wieder."), Forms.ToolTipIcon.Info);
            }
        }

        private void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            if (settings.ExitWarning && !exitRequested && activeSessions.Count > 0)
            {
                var answer = Msg("Es läuft noch ein Spiel. Wenn du den Launcher beendest, wird die Spielzeit nicht weiter erfasst.\n\nTrotzdem beenden?",
                    "Launcher beenden", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }

            exitRequested = true;
            CloseSensors();
            if (trayIcon != null) trayIcon.Visible = false;
            overlayWindow?.Close();
            Close();
            System.Windows.Application.Current.Shutdown();
        }

        private void OnClosingFeatures(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!exitRequested && settings.AskOnClose && IsVisible)
            {
                var answer = Msg("Soll der Launcher im Hintergrund weiterlaufen?\n\nJa: Im Infobereich weiterlaufen\nNein: Launcher beenden\nAbbrechen: Fenster offen lassen", "Launcher schließen",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                e.Cancel = true;
                if (answer == MessageBoxResult.Yes && trayIcon != null && trayIcon.Visible) HideToTray();
                else if (answer != MessageBoxResult.Cancel) Dispatcher.BeginInvoke(new Action(ExitApplication));
                return;
            }

            if (settings.MinimizeToTray && !exitRequested && trayIcon != null)
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        // ───────────────────────────── Hinweise, Toasts & Konfetti ─────────────────────────────

        private void ShowToast(string icon, string title, string text, int seconds = 6, Action? onClick = null, bool important = false)
        {
            if (!settings.ShowToasts) return;
            if (!important && AlertsMuted()) return;
            PlayUiSound("select");

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            content.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 26,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            });

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(texts, 1);
            texts.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            if (text.Length > 0)
            {
                texts.Children.Add(new TextBlock
                {
                    Text = text,
                    Foreground = BrushSubtle,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            content.Children.Add(texts);

            var slide = new TranslateTransform(60, 0);
            var toast = new Border
            {
                Background = MakeBrush("#F2161B28"),
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 12, 18, 12),
                Margin = new Thickness(0, 10, 0, 0),
                MinWidth = 300,
                MaxWidth = 400,
                Cursor = System.Windows.Input.Cursors.Hand,
                RenderTransform = slide,
                Opacity = 0,
                Child = content
            };

            toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(60, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            void Dismiss()
            {
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (s, e) => ToastHost.Children.Remove(toast);
                toast.BeginAnimation(UIElement.OpacityProperty, fade);
            }

            toast.MouseLeftButtonUp += (s, e) =>
            {
                onClick?.Invoke();
                Dismiss();
            };

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Dismiss();
            };
            timer.Start();

            ToastHost.Children.Add(toast);
        }

        private void Confetti()
        {
            if (!settings.PageAnimations) return;

            double width = ConfettiLayer.ActualWidth > 0 ? ConfettiLayer.ActualWidth : ActualWidth;
            double height = ConfettiLayer.ActualHeight > 0 ? ConfettiLayer.ActualHeight : ActualHeight;
            var accent = GetAccentColor();
            var palette = new[]
            {
                accent,
                System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24),
                System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99),
                System.Windows.Media.Color.FromRgb(0x60, 0xA5, 0xFA),
                System.Windows.Media.Color.FromRgb(0xF4, 0x72, 0xB6),
                System.Windows.Media.Color.FromRgb(0xFB, 0x92, 0x3C)
            };

            for (int i = 0; i < 70; i++)
            {
                var rotation = new RotateTransform(random.Next(360));
                var piece = new Border
                {
                    Width = random.Next(6, 11),
                    Height = random.Next(9, 17),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(palette[random.Next(palette.Length)]),
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    RenderTransform = rotation
                };

                double startX = random.NextDouble() * width;
                Canvas.SetLeft(piece, startX);
                Canvas.SetTop(piece, -20);
                ConfettiLayer.Children.Add(piece);

                var duration = TimeSpan.FromMilliseconds(1800 + random.Next(1500));
                var delay = TimeSpan.FromMilliseconds(random.Next(0, 500));

                var fall = new DoubleAnimation(-20, height + 40, duration)
                {
                    BeginTime = delay,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                fall.Completed += (s, e) => ConfettiLayer.Children.Remove(piece);

                piece.BeginAnimation(Canvas.TopProperty, fall);
                piece.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(startX, startX + random.Next(-90, 90), duration) { BeginTime = delay });
                rotation.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(rotation.Angle, rotation.Angle + random.Next(-540, 540), duration) { BeginTime = delay });
            }
        }

        private void ShowBanner(string key, string icon, string text, string actionText, Action action, Action? onClose = null)
        {
            if (banners.ContainsKey(key) || AlertsMuted()) return;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 20,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });

            var label = new TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var actionButton = new System.Windows.Controls.Button
            {
                Content = actionText,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(12, 0, 8, 0)
            };
            actionButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            Grid.SetColumn(actionButton, 2);
            grid.Children.Add(actionButton);

            var closeButton = new System.Windows.Controls.Button
            {
                Content = "✕",
                Padding = new Thickness(10, 6, 10, 6),
                Background = System.Windows.Media.Brushes.Transparent
            };
            Grid.SetColumn(closeButton, 3);
            grid.Children.Add(closeButton);

            var banner = new Border
            {
                Background = MakeBrush("#1C2233"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 10, 10, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = grid
            };
            banner.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

            actionButton.Click += (s, e) =>
            {
                RemoveBanner(key);
                action();
            };
            closeButton.Click += (s, e) =>
            {
                RemoveBanner(key);
                onClose?.Invoke();
            };

            banners[key] = banner;
            BannerHost.Children.Add(banner);
        }

        private void RemoveBanner(string key)
        {
            if (!banners.TryGetValue(key, out var banner)) return;
            BannerHost.Children.Remove(banner);
            banners.Remove(key);
        }

        private void RemoveBannersStartingWith(string prefix)
        {
            foreach (string key in banners.Keys.Where(k => k.StartsWith(prefix)).ToList())
                RemoveBanner(key);
        }

        private void CheckStorage()
        {
            if (!settings.StorageWarning)
            {
                RemoveBannersStartingWith("storage:");
                return;
            }

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                    double free = drive.AvailableFreeSpace / GigaByte;
                    double percent = drive.TotalSize > 0 ? drive.AvailableFreeSpace * 100.0 / drive.TotalSize : 100;

                    if (free < 15 || percent < 8)
                    {
                        ShowBanner("storage:" + drive.Name, "⚠", $"Auf {drive.Name} sind nur noch {free:F1} GB frei ({percent:F0} %).",
                            "Speicher ansehen", () => NavigateTo("storage"));
                        return;
                    }
                }
                catch { }
            }

            RemoveBannersStartingWith("storage:");
        }

        private void CheckReleases()
        {
            foreach (var release in settings.Releases)
            {
                if (release.Date.Date != DateTime.Today) continue;
                ShowBanner("release:" + release.Name, "🗓", $"Heute erscheint: {release.Name}", "Planer öffnen", () => NavigateTo("backlog"));
            }
        }

        // ───────────────────────────── Infobereich-Menü ─────────────────────────────

        private void RebuildTrayMenu()
        {
            if (trayIcon == null) return;

            var menu = CreateTrayMenu();
            menu.Items.Add(Loc.T("Öffnen"), null, (s, e) => ShowFromTray());
            menu.Items.Add(Loc.T("Zufälliges Spiel"), null, (s, e) =>
            {
                ShowFromTray();
                ShowRoulette();
            });
            AddTrayProfilesMenu(menu);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Loc.T("Beenden"), null, (s, e) => ExitApplication());
            menu.Opening += (s, e) => RefreshTrayRecents(menu);

            var old = trayIcon.ContextMenuStrip;
            trayIcon.ContextMenuStrip = menu;
            old?.Dispose();
        }

        private void RefreshTrayRecents(Forms.ContextMenuStrip menu)
        {
            foreach (var item in trayRecentItems) menu.Items.Remove(item);
            trayRecentItems.Clear();

            var recent = allGames.Where(g => g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).Take(5).ToList();
            if (recent.Count == 0) return;

            int position = 0;
            var header = new Forms.ToolStripMenuItem(Loc.T("Zuletzt gespielt:")) { Enabled = false };
            menu.Items.Insert(position++, header);
            trayRecentItems.Add(header);

            foreach (var game in recent)
            {
                var target = game;
                var entry = new Forms.ToolStripMenuItem(target.Name);
                entry.Click += (s, e) => LaunchGame(target);
                menu.Items.Insert(position++, entry);
                trayRecentItems.Add(entry);
            }

            var separator = new Forms.ToolStripSeparator();
            menu.Items.Insert(position, separator);
            trayRecentItems.Add(separator);
        }

        // ───────────────────────────── Schnellzugriff (eigene Kategorie) ─────────────────────────────

        private void SeedQuickLinks()
        {
            if (settings.QuickLinksSeeded) return;

            foreach (var item in DefaultQuickLinks)
                settings.QuickLinks.Add(new QuickLink { Name = item.Name, Icon = item.Icon, Target = item.Url });

            settings.QuickLinksSeeded = true;
            SaveSettings();
        }

        private static bool LooksLikePath(string text)
            => text.Contains('\\') || text.Contains('/') || text.Contains(':') || text.Contains('.');


        private static string QuickEmoji(QuickLink link)
        {
            string icon = link.Icon.Trim();
            return icon.Length > 0 && !LooksLikePath(icon) ? icon : "🔗";
        }

        private static string DescribeTarget(string target)
        {
            try
            {
                if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                    return uri.Host.Replace("www.", string.Empty);
                return System.IO.Path.GetFileName(target.TrimEnd('\\', '/'));
            }
            catch
            {
                return target;
            }
        }

        private void BuildLinkTiles()
        {
            if (QuickContainer == null) return;

            SeedQuickLinks();
            QuickContainer.Children.Clear();

            foreach (var link in settings.QuickLinks)
            {
                var item = link;
                var menu = CreateMenu();
                AddMenuItem(menu, "✏  Bearbeiten ...", () => EditQuickLink(item));
                AddMenuItem(menu, "◀  Nach vorn", () => MoveQuickLink(item, -1));
                AddMenuItem(menu, "▶  Nach hinten", () => MoveQuickLink(item, 1));
                AddMenuItem(menu, "🗑  Entfernen", () => RemoveQuickLink(item));

                QuickContainer.Children.Add(CreateTile(QuickEmoji(item), ResolveQuickIcon(item), item.Name,
                    DescribeTarget(item.Target), () => OpenShell(item.Target), menu, settings.QuickTileSize));
            }

            if (settings.QuickLinks.Count == 0)
            {
                QuickContainer.Children.Add(new TextBlock
                {
                    Text = Loc.T("Noch keine Verknüpfungen. Füge mit „Hinzufügen“ Webseiten, Programme oder Ordner hinzu."),
                    Foreground = BrushSubtle
                });
            }
        }

        private void MoveQuickLink(QuickLink link, int delta)
        {
            int index = settings.QuickLinks.IndexOf(link);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= settings.QuickLinks.Count) return;

            settings.QuickLinks.RemoveAt(index);
            settings.QuickLinks.Insert(target, link);
            SaveSettings();
            BuildLinkTiles();
        }

        private void RemoveQuickLink(QuickLink link)
        {
            settings.QuickLinks.Remove(link);
            SaveSettings();
            BuildLinkTiles();
        }

        private void BtnAddQuick_Click(object sender, RoutedEventArgs e) => EditQuickLink(null);

        private void BtnResetQuick_Click(object sender, RoutedEventArgs e)
        {
            var answer = Msg("Alle Verknüpfungen durch die Standard-Auswahl ersetzen?", "Schnellzugriff",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            settings.QuickLinks = new List<QuickLink>();
            settings.QuickLinksSeeded = false;
            BuildLinkTiles();
        }

        private void EditQuickLink(QuickLink? existing, List<QuickLink>? list = null, Action? after = null)
        {
            var dialog = CreateDialog(existing == null ? "Verknüpfung hinzufügen" : "Verknüpfung bearbeiten", 560, out var panel);

            TextBlock Label(string text) => new() { Text = Loc.T(text), Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 6) };

            panel.Children.Add(Label("Name"));
            var name = new System.Windows.Controls.TextBox { Text = existing?.Name ?? string.Empty, Height = 38, Margin = new Thickness(0, 0, 0, 14) };
            panel.Children.Add(name);

            panel.Children.Add(Label("Ziel (Webadresse, Programm, Datei oder Ordner)"));
            var target = new System.Windows.Controls.TextBox { Text = existing?.Target ?? string.Empty, Height = 38 };
            panel.Children.Add(target);

            var targetButtons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 14) };
            var pickFile = new System.Windows.Controls.Button { Content = Loc.T("Datei wählen ..."), Margin = new Thickness(0, 0, 8, 0) };
            pickFile.Click += (s, args) =>
            {
                var picker = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Datei wählen ...") };
                if (picker.ShowDialog() == true) target.Text = picker.FileName;
            };
            var pickFolder = new System.Windows.Controls.Button { Content = Loc.T("Ordner wählen ...") };
            pickFolder.Click += (s, args) =>
            {
                using var picker = new Forms.FolderBrowserDialog();
                if (picker.ShowDialog() == Forms.DialogResult.OK) target.Text = picker.SelectedPath;
            };
            targetButtons.Children.Add(pickFile);
            targetButtons.Children.Add(pickFolder);
            panel.Children.Add(targetButtons);

            panel.Children.Add(Label("Symbol (Emoji eingeben oder Bild wählen, leer = Symbol der Datei)"));
            var icon = new System.Windows.Controls.TextBox { Text = existing?.Icon ?? string.Empty, Height = 38 };
            panel.Children.Add(icon);

            var iconButtons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 20) };
            var pickImage = new System.Windows.Controls.Button { Content = Loc.T("Bild wählen ..."), Margin = new Thickness(0, 0, 8, 0) };
            pickImage.Click += (s, args) =>
            {
                var picker = new Microsoft.Win32.OpenFileDialog
                {
                    Title = Loc.T("Bild wählen ..."),
                    Filter = Loc.T("Bilder") + " (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe"
                };
                if (picker.ShowDialog() == true) icon.Text = picker.FileName;
            };
            var clearIcon = new System.Windows.Controls.Button { Content = Loc.T("Symbol leeren") };
            clearIcon.Click += (s, args) => icon.Text = string.Empty;
            iconButtons.Children.Add(pickImage);
            iconButtons.Children.Add(clearIcon);
            panel.Children.Add(iconButtons);

            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancel = new System.Windows.Controls.Button { Content = Loc.T("Abbrechen"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var save = new System.Windows.Controls.Button { Content = Loc.T("Speichern"), Padding = new Thickness(26, 8, 26, 8), IsDefault = true };
            save.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            save.Click += (s, args) =>
            {
                if (name.Text.Trim().Length == 0 || target.Text.Trim().Length == 0)
                {
                    Msg("Bitte gib einen Namen und ein Ziel ein.", "Schnellzugriff", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var link = existing ?? new QuickLink();
                link.Name = name.Text.Trim();
                link.Target = target.Text.Trim();
                link.Icon = icon.Text.Trim();
                if (existing == null) (list ?? settings.QuickLinks).Add(link);

                SaveSettings();
                if (after != null) after();
                else BuildLinkTiles();
                dialog.DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            dialog.ShowDialog();
        }

        // ───────────────────────────── Infobereich-Panel (Quick-Panel unten rechts) ─────────────────────────────

        private void HookTrayFlyout()
        {
            if (trayIcon == null) return;

            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button != Forms.MouseButtons.Left) return;
                if (settings.TrayClickOpensLauncher)
                {
                    Dispatcher.BeginInvoke(new Action(ShowFromTray));
                    return;
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    flyoutTimer.Stop();
                    flyoutTimer.Start();
                }));
            };
            trayIcon.DoubleClick += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                flyoutTimer.Stop();
                CloseTrayFlyout();
            }));
        }

        private void CloseTrayFlyout()
        {
            var window = trayFlyout;
            trayFlyout = null;
            try { window?.Close(); } catch { }
        }

        private void ShowTrayFlyout()
        {
            if (trayFlyout != null)
            {
                CloseTrayFlyout();
                return;
            }

            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                Width = 350,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000
            };
            window.Resources.MergedDictionaries.Add(Resources);
            window.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AppBackgroundBrush");

            var root = new StackPanel { Margin = new Thickness(18) };

            var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            header.Children.Add(new ContentControl { Content = FindResource("DfpLogo"), Width = 40, Height = 40 });
            var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "DFP Pro Launcher", Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, FontSize = 16 });
            titles.Children.Add(new TextBlock { Text = "by Der_Fette_Pudel", Foreground = BrushSubtle, FontSize = 11 });
            header.Children.Add(titles);
            root.Children.Add(header);

            var sample = SampleMetrics();
            root.Children.Add(new TextBlock
            {
                Text = $"CPU {sample.Cpu:F0}%   ·   RAM {sample.Ram:F0}%" + (sample.HasGpu ? $"   ·   GPU {sample.Gpu:F0}%" : string.Empty),
                Foreground = BrushSubtle,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var games = VisibleGames
                .Where(g => g.LastPlayed.HasValue || g.IsFavorite)
                .OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                .Take(6)
                .ToList();

            root.Children.Add(new TextBlock { Text = Loc.T("Schnell starten"), Foreground = BrushSubtle, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
            if (games.Count == 0)
                root.Children.Add(new TextBlock { Text = Loc.T("Noch nichts gestartet."), Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 12) });

            foreach (var game in games)
            {
                var item = game;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var thumb = new Border { Width = 30, Height = 42, CornerRadius = new CornerRadius(5), Background = BrushCardBg, Margin = new Thickness(0, 0, 10, 0) };
                if (HasCover(item) && GetCachedImage(EffectiveCover(item)) is BitmapImage image)
                    thumb.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                row.Children.Add(thumb);

                var name = new TextBlock
                {
                    Text = item.Name,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);

                var play = new System.Windows.Controls.Button { Content = "▶", Padding = new Thickness(14, 6, 14, 6) };
                play.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                play.Click += (s, e) =>
                {
                    CloseTrayFlyout();
                    LaunchGame(item);
                };
                Grid.SetColumn(play, 2);
                row.Children.Add(play);

                root.Children.Add(row);
            }

            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            void AddAction(string text, Action action, bool accent = false)
            {
                var button = new System.Windows.Controls.Button { Content = Loc.T(text), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 7, 12, 7) };
                if (accent) button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                button.Click += (s, e) =>
                {
                    CloseTrayFlyout();
                    action();
                };
                actions.Children.Add(button);
            }

            AddAction("Launcher öffnen", ShowFromTray, true);
            AddAction("🔍  Suche", () => { ShowFromTray(); OpenSpotlight(); });
            AddAction("🎲  Zufällig", () => { ShowFromTray(); BtnRandomGame_Click(this, new RoutedEventArgs()); });
            AddAction("🎮  Controller-Modus", () => { ShowFromTray(); ToggleControllerMode(); });
            AddAction("⚙  Einstellungen", () => { ShowFromTray(); NavigateTo("settings"); });
            AddAction("📡  Streamer-Modus", ToggleStreamerMode);
            AddAction("⏻  Beenden", ExitApplication);
            root.Children.Add(actions);

            window.Content = new Border
            {
                Child = root,
                BorderThickness = new Thickness(1),
                Background = MakeBrush("#161B28")
            };
            ((Border)window.Content).SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

            window.Loaded += (s, e) =>
            {
                window.UpdateLayout();
                var area = SystemParameters.WorkArea;
                window.Left = area.Right - window.ActualWidth - 12;
                window.Top = area.Bottom - window.ActualHeight - 12;
                window.Activate();
            };
            window.Deactivated += (s, e) =>
            {
                if (ReferenceEquals(trayFlyout, window)) CloseTrayFlyout();
            };

            trayFlyout = window;
            window.Show();
        }

        // ───────────────────────────── Dashboard-Widgets ─────────────────────────────

        private List<string> GetDashOrder()
        {
            var all = DashWidgetInfo.Select(w => w.Key).ToList();
            var order = settings.DashboardOrder.Where(all.Contains).Distinct().ToList();
            order.AddRange(all.Where(k => !order.Contains(k)));
            return order;
        }

        private bool DashHidden(string key) => settings.DashboardHidden.Contains(key);

        private UIElement[] WidgetElements(string key)
        {
            UIElement?[] items = key switch
            {
                "hero" => new UIElement?[] { HeroHost },
                "clock" => new UIElement?[] { WidgetsRow },
                "stats" => new UIElement?[] { StatsGrid },
                "recent" => new UIElement?[] { RecentSection },
                "favorites" => new UIElement?[] { FavSection },
                "actions" => new UIElement?[] { DashActionsTitle, DashActions },
                "goal" => new UIElement?[] { goalWidget },
                "backlog" => new UIElement?[] { backlogWidget },
                "wishlist" => new UIElement?[] { wishWidget },
                "quick" => new UIElement?[] { quickWidget },
                _ => Array.Empty<UIElement?>()
            };
            return items.Where(i => i != null).Select(i => i!).ToArray();
        }

        private void EnsureDashWidgets()
        {
            if (goalWidget != null) return;

            goalWidget = new StackPanel();
            backlogWidget = new StackPanel();
            wishWidget = new StackPanel();
            quickWidget = new StackPanel();
            ApplyStreamerMode();
        }

        private void ApplyDashboardLayout()
        {
            if (DashStack == null || HeroHost == null || BannerHost == null) return;

            EnsureDashWidgets();
            var order = GetDashOrder();

            var elements = new List<UIElement>();
            foreach (string key in order) elements.AddRange(WidgetElements(key));

            foreach (var element in elements) DashStack.Children.Remove(element);
            int index = DashStack.Children.IndexOf(BannerHost) + 1;
            foreach (var element in elements)
                DashStack.Children.Insert(Math.Min(index++, DashStack.Children.Count), element);

            foreach (string key in order)
            {
                if (!DashHidden(key)) continue;
                foreach (var element in WidgetElements(key)) element.Visibility = Visibility.Collapsed;
            }

            FillGoalWidget();
            FillBacklogWidget();
            FillWishWidget();
            FillQuickWidget();
        }

        private Border WidgetCard(string title, UIElement body, Action? onTitleClick = null)
        {
            var heading = new TextBlock { Text = Loc.T(title) };
            if (TryFindResource("SectionTitle") is Style titleStyle) heading.Style = titleStyle;
            if (onTitleClick != null)
            {
                heading.Cursor = System.Windows.Input.Cursors.Hand;
                heading.MouseLeftButtonUp += (s, e) => onTitleClick();
            }

            var stack = new StackPanel();
            stack.Children.Add(heading);
            stack.Children.Add(body);

            var card = new Border { Margin = new Thickness(0, 0, 0, 12), Child = stack };
            if (TryFindResource("CardStyle") is Style cardStyle) card.Style = cardStyle;
            return card;
        }

        private TextBlock WidgetHint(string text)
            => new() { Text = Loc.T(text), Foreground = BrushSubtle, TextWrapping = TextWrapping.Wrap };

        private Grid MiniGameRow(GameItem game)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var thumb = new Border { Width = 30, Height = 42, CornerRadius = new CornerRadius(5), Background = BrushFooter, Margin = new Thickness(0, 0, 10, 0) };
            if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage image)
                thumb.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            row.Children.Add(thumb);

            var name = new TextBlock
            {
                Text = game.Name,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var play = new System.Windows.Controls.Button { Content = "▶", Padding = new Thickness(14, 6, 14, 6) };
            play.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            play.Click += (s, e) => LaunchGame(game);
            Grid.SetColumn(play, 2);
            row.Children.Add(play);

            return row;
        }

        private void FillGoalWidget()
        {
            if (goalWidget == null) return;
            goalWidget.Children.Clear();
            if (DashHidden("goal"))
            {
                goalWidget.Visibility = Visibility.Collapsed;
                return;
            }

            var body = new StackPanel();
            double goalHours = settings.WeeklyGoalHours;

            if (goalHours <= 0)
            {
                body.Children.Add(WidgetHint("Lege in den Einstellungen unter „Spiele und Cover“ ein Wochenziel fest."));
            }
            else
            {
                double week = GetWeekSeconds();
                double fraction = Math.Clamp(week / (goalHours * 3600.0), 0, 1);

                body.Children.Add(new TextBlock
                {
                    Text = $"{FormatPlaytime((long)week)} / {goalHours} {Loc.T("Std.")}",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                });

                var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = MakeBrush("#1F2937") };
                var fill = new Border { Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                var bar = new Grid();
                bar.Children.Add(track);
                bar.Children.Add(fill);
                bar.SizeChanged += (s, e) => fill.Width = bar.ActualWidth * fraction;
                body.Children.Add(bar);
            }

            goalWidget.Children.Add(WidgetCard("🎯  Wochenziel", body));
            goalWidget.Visibility = Visibility.Visible;
        }

        private void FillBacklogWidget()
        {
            if (backlogWidget == null) return;
            backlogWidget.Children.Clear();
            if (DashHidden("backlog"))
            {
                backlogWidget.Visibility = Visibility.Collapsed;
                return;
            }

            var body = new StackPanel();
            var games = VisibleGames.Where(g => g.Status == "backlog")
                .OrderByDescending(g => g.FirstSeen ?? DateTime.MinValue).Take(5).ToList();

            if (games.Count == 0) body.Children.Add(WidgetHint("Dein Backlog ist leer. Markiere Spiele per Rechtsklick mit „Will ich spielen“."));
            foreach (var game in games) body.Children.Add(MiniGameRow(game));

            backlogWidget.Children.Add(WidgetCard("📋  Als Nächstes spielen", body, () => NavigateTo("backlog")));
            backlogWidget.Visibility = Visibility.Visible;
        }

        private void FillWishWidget()
        {
            if (wishWidget == null) return;
            wishWidget.Children.Clear();
            if (DashHidden("wishlist"))
            {
                wishWidget.Visibility = Visibility.Collapsed;
                return;
            }

            var body = new StackPanel();
            var items = settings.Wishlist.OrderByDescending(WishTargetReached).ThenByDescending(w => w.Discount).Take(4).ToList();

            if (items.Count == 0) body.Children.Add(WidgetHint("Noch keine Spiele auf der Wunschliste."));
            foreach (var item in items)
            {
                bool reached = WishTargetReached(item);
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(new TextBlock
                {
                    Text = (reached ? "🔔 " : string.Empty) + item.Name,
                    Foreground = System.Windows.Media.Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                string price = string.IsNullOrEmpty(item.PriceText) ? "–" : Loc.T(item.PriceText);
                if (item.Discount > 0) price += $"   -{item.Discount} %";
                var priceText = new TextBlock
                {
                    Text = price,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(16, 0, 0, 0),
                    Foreground = reached ? MakeBrush("#34D399") : BrushSubtle
                };
                Grid.SetColumn(priceText, 1);
                row.Children.Add(priceText);
                body.Children.Add(row);
            }

            wishWidget.Children.Add(WidgetCard("💝  Wunschliste", body, () => NavigateTo("wishlist")));
            wishWidget.Visibility = Visibility.Visible;
        }

        private void FillQuickWidget()
        {
            if (quickWidget == null) return;
            quickWidget.Children.Clear();
            if (DashHidden("quick"))
            {
                quickWidget.Visibility = Visibility.Collapsed;
                return;
            }

            SeedQuickLinks();

            var body = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };
            foreach (var link in settings.QuickLinks.Take(8))
            {
                var item = link;
                var menu = CreateMenu();
                AddMenuItem(menu, "🔗  Schnellzugriff verwalten", () => NavigateTo("links"));
                body.Children.Add(CreateTile(QuickEmoji(item), ResolveQuickIcon(item), item.Name,
                    DescribeTarget(item.Target), () => OpenShell(item.Target), menu, 170));
            }
            if (settings.QuickLinks.Count == 0) body.Children.Add(WidgetHint("Noch keine Verknüpfungen."));

            quickWidget.Children.Add(WidgetCard("🔗  Schnellzugriff", body, () => NavigateTo("links")));
            quickWidget.Visibility = Visibility.Visible;
        }

        private void BuildDashWidgetSettings()
        {
            if (DashWidgetPanel == null) return;

            dashPanelBuilding = true;
            DashWidgetPanel.Children.Clear();

            var order = GetDashOrder();
            for (int i = 0; i < order.Count; i++)
            {
                string key = order[i];
                var info = DashWidgetInfo.First(w => w.Key == key);

                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var box = new System.Windows.Controls.CheckBox
                {
                    Content = info.Icon + "  " + Loc.T(info.Title),
                    Foreground = System.Windows.Media.Brushes.White,
                    IsChecked = !DashHidden(key),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                box.Checked += (s, e) => { if (!dashPanelBuilding) SetDashWidgetVisible(key, true); };
                box.Unchecked += (s, e) => { if (!dashPanelBuilding) SetDashWidgetVisible(key, false); };
                row.Children.Add(box);

                var up = new System.Windows.Controls.Button { Content = "▲", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 4, 0), IsEnabled = i > 0 };
                up.Click += (s, e) => MoveDashWidget(key, -1);
                Grid.SetColumn(up, 1);
                row.Children.Add(up);

                var down = new System.Windows.Controls.Button { Content = "▼", Padding = new Thickness(10, 3, 10, 3), IsEnabled = i < order.Count - 1 };
                down.Click += (s, e) => MoveDashWidget(key, 1);
                Grid.SetColumn(down, 2);
                row.Children.Add(down);

                DashWidgetPanel.Children.Add(row);
            }

            dashPanelBuilding = false;
        }

        private bool dashPanelBuilding;

        private void SetDashWidgetVisible(string key, bool visible)
        {
            if (visible) settings.DashboardHidden.Remove(key);
            else if (!settings.DashboardHidden.Contains(key)) settings.DashboardHidden.Add(key);

            SaveSettings();
            RefreshDashboard();
        }

        private void MoveDashWidget(string key, int delta)
        {
            var order = GetDashOrder();
            int index = order.IndexOf(key);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= order.Count) return;

            order.RemoveAt(index);
            order.Insert(target, key);
            settings.DashboardOrder = order;

            SaveSettings();
            BuildDashWidgetSettings();
            RefreshDashboard();
        }

        private void BtnResetWidgets_Click(object sender, RoutedEventArgs e)
        {
            settings.DashboardOrder = new List<string>();
            settings.DashboardHidden = new List<string>(DefaultDashHidden);
            SaveSettings();
            BuildDashWidgetSettings();
            RefreshDashboard();
        }

        // ───────────────────────────── Willkommensanimation ─────────────────────────────

        private void StartWelcomeAnimation()
        {
            if (WelcomeLayer == null || !settings.WelcomeAnimation || !settings.SetupDone || settings.StartMinimized) return;

            bool reduced = settings.PerformanceMode || !settings.PageAnimations;

            string? specialGreeting = PickGreeting(out bool rareGreeting);
            WelcomeGreeting.Text = (specialGreeting ?? Loc.T(GetGreeting())) + ",";
            if (rareGreeting) Dispatcher.BeginInvoke(new Action(() => UnlockSecret("secret_lucky")), DispatcherPriority.ApplicationIdle);
            WelcomeName.Text = DisplayUserName();
            WelcomeLayer.Opacity = 1;
            WelcomeLayer.Visibility = Visibility.Visible;

            if (reduced)
            {
                WelcomeLogo.Opacity = 1;
                WelcomeGreeting.Opacity = 1;
                WelcomeName.Opacity = 1;
                WelcomeLine.Width = 160;
            }
            else
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                WelcomeLogo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
                var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(700))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
                };
                WelcomeLogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                WelcomeLogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

                var greetingFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { BeginTime = TimeSpan.FromMilliseconds(450) };
                WelcomeGreeting.BeginAnimation(UIElement.OpacityProperty, greetingFade);

                var nameFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(700) };
                WelcomeName.BeginAnimation(UIElement.OpacityProperty, nameFade);
                var nameSlide = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(550))
                {
                    BeginTime = TimeSpan.FromMilliseconds(700),
                    EasingFunction = ease
                };
                WelcomeNameShift.BeginAnimation(TranslateTransform.YProperty, nameSlide);

                var line = new DoubleAnimation(0, 160, TimeSpan.FromMilliseconds(600)) { BeginTime = TimeSpan.FromMilliseconds(950), EasingFunction = ease };
                WelcomeLine.BeginAnimation(FrameworkElement.WidthProperty, line);
            }

            welcomeTimer?.Stop();
            welcomeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(reduced ? 900 : 2700) };
            welcomeTimer.Tick += (s, e) => FinishWelcome();
            welcomeTimer.Start();
        }

        private void FinishWelcome()
        {
            welcomeTimer?.Stop();
            if (WelcomeLayer == null || WelcomeLayer.Visibility != Visibility.Visible) return;

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450));
            fade.Completed += (s, e) =>
            {
                WelcomeLayer.BeginAnimation(UIElement.OpacityProperty, null);
                WelcomeLayer.Visibility = Visibility.Collapsed;
            };
            WelcomeLayer.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void WelcomeLayer_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => FinishWelcome();

        // ───────────────────────────── Seitenleiste: Schnellstart und Musik ─────────────────────────────

        private readonly Dictionary<string, ImageSource?> quickIcons = new(StringComparer.OrdinalIgnoreCase);
        private bool sidebarExtrasCompact;
        private string nowPlaying = string.Empty;
        private TextBlock? nowPlayingText;
        private FrameworkElement? musicControls;

        private static readonly (string Key, string Name, string Url, string Color)[] MusicServices =
        {
            ("spotify", "Spotify", "https://open.spotify.com", "#1DB954"),
            ("ytmusic", "YouTube Music", "https://music.youtube.com", "#FF3355"),
            ("apple", "Apple Music", "https://music.apple.com", "#FA586A")
        };

        private void RenderSidebarExtras()
        {
            if (SidebarExtras == null) return;

            bool compact = settings.SidebarCollapsed;
            sidebarExtrasCompact = compact;
            SidebarExtras.Children.Clear();
            nowPlayingText = null;
            musicControls = null;

            // Schwebende Leiste unten: Schnellstart und Musik stehen dort als Symbole
            RenderDockExtras();
            if (IsBottomNav)
            {
                SidebarExtras.Visibility = Visibility.Collapsed;
                return;
            }

            if (settings.QuickButtonsEnabled) SidebarExtras.Children.Add(BuildQuickButtons(compact));
            if (settings.MusicPlayer && !SHide(settings.StreamHideMusic)) SidebarExtras.Children.Add(BuildMusicCard(compact));

            SidebarExtras.Visibility = SidebarExtras.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }


        private FrameworkElement BuildQuickButtons(bool compact)
        {
            var host = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };

            if (!compact)
            {
                host.Children.Add(new TextBlock
                {
                    Text = Loc.T("Schnellstart"),
                    Foreground = BrushSubtle,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(4, 0, 0, 6)
                });
            }

            var wrap = new WrapPanel { HorizontalAlignment = compact ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left };
            foreach (var button in settings.QuickButtons.ToList()) wrap.Children.Add(CreateQuickTile(button));
            if (settings.QuickButtons.Count < 12) wrap.Children.Add(CreateQuickAddTile());
            host.Children.Add(wrap);
            return host;
        }

        private Border QuickTileShell(FrameworkElement content, string tip)
        {
            var tile = new Border
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(10),
                Background = MakeBrush("#161B28"),
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = tip,
                Child = content
            };
            tile.MouseEnter += (s, e) => tile.Background = BrushTileHover;
            tile.MouseLeave += (s, e) => tile.Background = MakeBrush("#161B28");
            return tile;
        }

        private Border CreateQuickTile(QuickButton button)
        {
            FrameworkElement content;
            var icon = QuickIcon(button);
            if (icon != null)
            {
                var image = new System.Windows.Controls.Image { Source = icon, Width = 26, Height = 26, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                content = image;
            }
            else
            {
                content = new TextBlock
                {
                    Text = "🧩",
                    FontSize = 20,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
            }

            var tile = QuickTileShell(content, button.Name);
            tile.MouseLeftButtonUp += (s, e) => LaunchQuickButton(button);

            var menu = CreateMenu();
            AddMenuItem(menu, "▶  Starten", () => LaunchQuickButton(button));
            AddMenuItem(menu, "✏  Umbenennen", () =>
            {
                string? name = PromptText("Schnellstart-Button", "Name des Buttons", button.Name);
                if (name == null) return;
                button.Name = name;
                SaveSettings();
                RenderSidebarExtras();
            });
            AddMenuItem(menu, "🔁  Anderes Programm wählen", () => PickQuickTarget(button));
            AddMenuItem(menu, "🗑  Entfernen", () =>
            {
                settings.QuickButtons.Remove(button);
                SaveSettings();
                RenderSidebarExtras();
            });
            tile.ContextMenu = menu;
            return tile;
        }

        private Border CreateQuickAddTile()
        {
            var plus = new TextBlock
            {
                Text = "＋",
                FontSize = 18,
                Foreground = BrushSubtle,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var tile = QuickTileShell(plus, Loc.T("Programm hinzufügen"));
            tile.MouseLeftButtonUp += (s, e) => PickQuickTarget(null);
            return tile;
        }


        private void LaunchQuickButton(QuickButton button)
        {
            try
            {
                if (button.Path.StartsWith("app:", StringComparison.Ordinal))
                {
                    var app = cachedApps.FirstOrDefault(a => a.Name == button.Path.Substring(4));
                    if (app != null)
                    {
                        LaunchApp(app);
                    }
                    else if (button.AppId.Length > 0)
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + button.AppId) { UseShellExecute = true });
                    }
                    else if (button.ExePath.Length > 0 && File.Exists(button.ExePath))
                    {
                        Process.Start(new ProcessStartInfo(button.ExePath) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(button.ExePath) ?? string.Empty });
                    }
                    else
                    {
                        ShowToast("⚠", "Programm nicht gefunden", button.Name, 6, null, true);
                    }
                    return;
                }

                if (!File.Exists(button.Path) && !Directory.Exists(button.Path))
                {
                    ShowToast("⚠", "Programm nicht gefunden", button.Name, 6, null, true);
                    return;
                }

                Process.Start(new ProcessStartInfo(button.Path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(button.Path) ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                LogError("Schnellstart", ex);
                Msg($"Das Programm konnte nicht gestartet werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private FrameworkElement BuildMusicCard(bool compact)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 14, 0, 0),
                Padding = compact ? new Thickness(0, 8, 0, 8) : new Thickness(12),
                Background = BrushCardBg,
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12)
            };

            var stack = new StackPanel();
            card.Child = stack;

            if (compact)
            {
                var open = MiniButton("🎵", "Musik", () => OpenMusic(null), 40);
                open.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                open.Margin = new Thickness(0);
                stack.Children.Add(open);
                return card;
            }

            stack.Children.Add(new TextBlock
            {
                Text = "🎵  " + Loc.T("Musik"),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var services = new WrapPanel();
            foreach (var service in ActiveMusicServices())
            {
                var item = service;
                var chip = new Border
                {
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 4, 4),
                    CornerRadius = new CornerRadius(8),
                    Background = MakeBrush("#1AFFFFFF"),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = item.Name,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = MakeBrush(item.Color)
                    }
                };
                chip.MouseEnter += (s, e) => chip.Background = MakeBrush("#33FFFFFF");
                chip.MouseLeave += (s, e) => chip.Background = MakeBrush("#1AFFFFFF");
                chip.MouseLeftButtonUp += (s, e) => OpenMusic(item.Key);
                services.Children.Add(chip);
            }
            stack.Children.Add(services);

            nowPlayingText = new TextBlock
            {
                Text = "♪ " + nowPlaying,
                Foreground = MakeBrush("#D1D5DB"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = nowPlaying,
                Visibility = nowPlaying.Length > 0 ? Visibility.Visible : Visibility.Collapsed
            };
            nowPlayingText.MouseLeftButtonUp += (s, e) => OpenMusic(null);
            stack.Children.Add(nowPlayingText);

            var controls = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = musicView != null ? Visibility.Visible : Visibility.Collapsed
            };
            controls.Children.Add(MiniButton("⏮", "Vorheriges Lied", () => SendMediaKey(0xB1)));
            controls.Children.Add(MiniButton("⏯", "Wiedergabe oder Pause", () => _ = MusicTogglePlayAsync()));
            controls.Children.Add(MiniButton("⏭", "Nächstes Lied", () => SendMediaKey(0xB0)));
            controls.Children.Add(MiniButton("⤢", "Player anzeigen", () => OpenMusic(null)));
            musicControls = controls;
            stack.Children.Add(controls);

            return card;
        }

        // ───────────────────────────── Musik-Player (Browser-Player im Launcher) ─────────────────────────────

        private Microsoft.Web.WebView2.Wpf.WebView2? musicView;
        private string musicService = "spotify";
        private string musicLoadedService = string.Empty;
        private bool musicStarting;
        private bool musicSuspendedForOverlay;

        private void FitMusicPanel()
        {
            if (MusicPanel == null) return;
            MusicPanel.Height = Math.Clamp(ActualHeight - 90, 380, 640);
        }

        private void HighlightMusicTabs()
        {
            if (MusicTabs == null) return;
            foreach (var tab in MusicTabs.Children.OfType<System.Windows.Controls.Button>())
            {
                string tabKey = tab.Tag as string ?? string.Empty;
                tab.Visibility = settings.MusicServicesOn.Contains(tabKey) ? Visibility.Visible : Visibility.Collapsed;
                tab.Opacity = tabKey == musicService ? 1.0 : 0.55;
            }
        }

        private async void OpenMusic(string? key)
        {
            if (!settings.MusicPlayer) return;
            if (SHide(settings.StreamHideMusic))
            {
                ShowToast("🎵", "Musik", "Im Streamer-Modus ist der Musik-Player verborgen.", 5);
                return;
            }

            if (key != null && settings.MusicServicesOn.Contains(key)) musicService = key;
            if (!settings.MusicServicesOn.Contains(musicService)) musicService = settings.MusicServicesOn[0];

            var service = MusicServices.First(s => s.Key == musicService);
            FitMusicPanel();
            HighlightMusicTabs();
            MusicPanel.Visibility = Visibility.Visible;

            try
            {
                await EnsureMusicViewAsync();
            }
            catch (Exception ex)
            {
                MusicPanel.Visibility = Visibility.Collapsed;
                MusicStartFailed(ex);
                return;
            }

            if (musicView?.CoreWebView2 != null && musicLoadedService != musicService)
            {
                musicLoadedService = musicService;
                nowPlaying = string.Empty;
                musicView.CoreWebView2.Navigate(service.Url);
            }

            if (settings.MusicService != musicService)
            {
                settings.MusicService = musicService;
                SaveSettings();
            }

            // Schwebende Leiste: Rahmen auf das Logo des laufenden Dienstes setzen
            if (IsBottomNav) RenderDockExtras();
        }

        private async Task EnsureMusicViewAsync()
        {
            if (musicView?.CoreWebView2 != null) return;

            while (musicStarting) await Task.Delay(100);
            if (musicView?.CoreWebView2 != null) return;

            musicStarting = true;
            try
            {
                var view = musicView ?? new Microsoft.Web.WebView2.Wpf.WebView2();
                if (musicView == null)
                {
                    musicView = view;
                    MusicHost.Children.Add(view);
                }

                string data = System.IO.Path.Combine(SettingsDir, "webview2");
                Directory.CreateDirectory(data);

                var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, data);
                await view.EnsureCoreWebView2Async(environment);

                view.CoreWebView2.Settings.AreDevToolsEnabled = false;
                view.CoreWebView2.Settings.IsStatusBarEnabled = false;
                view.CoreWebView2.DocumentTitleChanged += (s, e) => UpdateNowPlaying(view.CoreWebView2.DocumentTitle);
                // Läuft gerade Ton? Damit zeigt der Play/Pause-Knopf der schwebenden Leiste immer das Richtige
                view.CoreWebView2.IsDocumentPlayingAudioChanged += (s, e) => OnMusicPlayingChanged(view.CoreWebView2.IsDocumentPlayingAudio);

                RenderSidebarExtras();
            }
            catch
            {
                if (musicView != null)
                {
                    try { MusicHost.Children.Remove(musicView); musicView.Dispose(); }
                    catch { }
                    musicView = null;
                }
                throw;
            }
            finally
            {
                musicStarting = false;
            }
        }

        private void MusicStartFailed(Exception ex)
        {
            LogError("Musik-Player", ex);

            if (ex is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
            {
                var answer = Msg("Der Musik-Player braucht die Microsoft Edge WebView2 Runtime. Sie ist bei Windows 11 meist schon da, bei Windows 10 manchmal nicht. Jetzt die Download-Seite öffnen?",
                    "Musik-Player", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes) OpenShell("https://developer.microsoft.com/microsoft-edge/webview2/");
                return;
            }

            Msg($"Der Musik-Player konnte nicht gestartet werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private string CleanMusicTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;
            if (Regex.IsMatch(title, @"^(Spotify|Web Player|Apple Music|YouTube Music|music\.youtube|Wird geladen|Loading)", RegexOptions.IgnoreCase)) return string.Empty;

            var parts = Regex.Split(title, @"\s[•\-–|·]\s")
                .Select(p => p.Trim())
                .Where(p => p.Length > 0 && !MusicServices.Any(s => p.Equals(s.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            return string.Join(" – ", parts);
        }

        private void UpdateNowPlaying(string rawTitle)
        {
            nowPlaying = CleanMusicTitle(rawTitle);
            if (nowPlayingText == null) return;

            nowPlayingText.Text = "♪ " + nowPlaying;
            nowPlayingText.ToolTip = nowPlaying;
            nowPlayingText.Visibility = nowPlaying.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void SendMediaKey(byte key)
        {
            try
            {
                NativeExtras.keybd_event(key, 0, 0, UIntPtr.Zero);
                NativeExtras.keybd_event(key, 0, 2, UIntPtr.Zero);
            }
            catch { }
        }

        private async Task MusicTogglePlayAsync()
        {
            try
            {
                if (musicView?.CoreWebView2 != null)
                {
                    string result = await musicView.CoreWebView2.ExecuteScriptAsync(
                        "(()=>{const m=[...document.querySelectorAll('audio,video')];if(!m.length)return 'none';const p=m.some(x=>!x.paused);m.forEach(x=>{if(p){x.pause();}else{x.play();}});return p?'paused':'playing';})()");
                    if (!result.Contains("none")) return;
                }
            }
            catch { }

            SendMediaKey(0xB3);
        }

        private void MusicTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button { Tag: string key }) OpenMusic(key);
        }

        private void BtnMusicHide_Click(object sender, RoutedEventArgs e) => MusicPanel.Visibility = Visibility.Collapsed;

        private void BtnMusicStop_Click(object sender, RoutedEventArgs e)
        {
            MusicPanel.Visibility = Visibility.Collapsed;
            nowPlaying = string.Empty;
            musicLoadedService = string.Empty;
            musicIsPlaying = false;

            try
            {
                if (musicView != null)
                {
                    MusicHost.Children.Remove(musicView);
                    musicView.Dispose();
                    musicView = null;
                }
            }
            catch { }

            RenderSidebarExtras();
        }

        private void BtnMusicBrowser_Click(object sender, RoutedEventArgs e)
        {
            var service = MusicServices.First(s => s.Key == musicService);
            OpenShell(service.Url);
        }

        /// <summary>Der Player liegt als eigenes Fenster-Element über der Oberfläche und würde die Suche verdecken.</summary>
        private void SuspendMusicOverlay()
        {
            if (MusicPanel.Visibility != Visibility.Visible) return;
            musicSuspendedForOverlay = true;
            MusicPanel.Visibility = Visibility.Hidden;
        }

        private void ResumeMusicOverlay()
        {
            if (!musicSuspendedForOverlay) return;
            musicSuspendedForOverlay = false;
            MusicPanel.Visibility = Visibility.Visible;
        }

        // ───────────────────────────── Musikdienste auswählen ─────────────────────────────

        private IEnumerable<(string Key, string Name, string Url, string Color)> ActiveMusicServices()
            => MusicServices.Where(s => settings.MusicServicesOn.Contains(s.Key));

        private void UpdateMusicServiceVisibility()
        {
            if (MusicServicePanel != null)
                MusicServicePanel.Visibility = ChkMusic.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateMusicServiceUi()
        {
            if (ChkMusicSpotify == null) return;

            ChkMusicSpotify.IsChecked = settings.MusicServicesOn.Contains("spotify");
            ChkMusicYtMusic.IsChecked = settings.MusicServicesOn.Contains("ytmusic");
            ChkMusicApple.IsChecked = settings.MusicServicesOn.Contains("apple");
            UpdateMusicServiceVisibility();
        }

        private void MusicServiceChoice_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            var chosen = new List<string>();
            if (ChkMusicSpotify.IsChecked == true) chosen.Add("spotify");
            if (ChkMusicYtMusic.IsChecked == true) chosen.Add("ytmusic");
            if (ChkMusicApple.IsChecked == true) chosen.Add("apple");

            if (chosen.Count == 0)
            {
                // Mindestens ein Dienst bleibt aktiv: der gerade abgewählte wird wieder eingeschaltet
                isLoadingSettings = true;
                try
                {
                    if (sender is System.Windows.Controls.CheckBox box) box.IsChecked = true;
                }
                finally
                {
                    isLoadingSettings = false;
                }

                ShowToast("🎵", "Musik", "Mindestens ein Dienst muss aktiv bleiben.", 4);
                return;
            }

            settings.MusicServicesOn = chosen;
            SaveSettings();

            bool switched = !chosen.Contains(musicService);
            if (switched) musicService = chosen[0];

            RenderSidebarExtras();
            HighlightMusicTabs();

            // Läuft der Player gerade mit einem abgewählten Dienst, wechselt er zum ersten gewählten
            if (switched && MusicPanel.Visibility == Visibility.Visible) OpenMusic(null);
        }

        // ───────────────────────────── Schnellstart: Symbole zuverlässig und Auswahl mit Suche ─────────────────────────────

        private static string IconCacheDir => System.IO.Path.Combine(SettingsDir, "icons");

        private static string IconCacheFile(string key)
        {
            byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
            return System.IO.Path.Combine(IconCacheDir, Convert.ToHexString(hash, 0, 8) + ".png");
        }

        private static ImageSource? LoadIconFromDisk(string key)
        {
            try
            {
                string file = IconCacheFile(key);
                if (!File.Exists(file)) return null;

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(file);
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

        private static void SaveIconToDisk(string key, ImageSource icon)
        {
            try
            {
                if (icon is not BitmapSource bitmap) return;

                Directory.CreateDirectory(IconCacheDir);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(IconCacheFile(key));
                encoder.Save(stream);
            }
            catch { }
        }

        private ImageSource? QuickIcon(QuickButton button)
        {
            string key = "quick|" + (button.AppId.Length > 0 ? button.AppId : button.Path);
            if (quickIcons.TryGetValue(key, out var cached) && cached != null) return cached;

            ImageSource? icon = LoadIconFromDisk(key);

            if (icon == null && button.AppId.Length > 0)
                icon = ShellImage("shell:AppsFolder\\" + button.AppId, 96);

            if (icon == null && button.ExePath.Length > 0 && File.Exists(button.ExePath))
                icon = ShellImage(button.ExePath, 96) ?? ExtractIcon(button.ExePath);

            if (icon == null && button.Path.StartsWith("app:", StringComparison.Ordinal))
                icon = cachedApps.FirstOrDefault(a => a.Name == button.Path.Substring(4))?.Icon;

            if (icon == null && !button.Path.StartsWith("app:", StringComparison.Ordinal)
                && (File.Exists(button.Path) || Directory.Exists(button.Path)))
                icon = ShellImage(button.Path, 96) ?? ExtractIcon(button.Path);

            if (icon != null)
            {
                SaveIconToDisk(key, icon);
                quickIcons[key] = icon;
            }
            return icon;
        }

        /// <summary>Älteren Schnellstart-Knöpfen (nur mit App-Namen gespeichert) die feste Kennung nachtragen.</summary>
        private void MigrateQuickButtons()
        {
            bool changed = false;
            foreach (var button in settings.QuickButtons)
            {
                if (!button.Path.StartsWith("app:", StringComparison.Ordinal) || button.AppId.Length > 0) continue;

                var app = cachedApps.FirstOrDefault(a => a.Name == button.Path.Substring(4));
                if (app == null) continue;

                button.AppId = app.AppId;
                button.ExePath = app.ExePath;
                changed = true;
            }

            if (changed) SaveSettings();
        }

        private (string Path, string Name, string AppId, string ExePath)? ShowAppPicker(string title)
        {
            var dialog = CreateDialog(title, 540, out var panel);
            (string Path, string Name, string AppId, string ExePath)? result = null;

            var searchHost = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            var search = new System.Windows.Controls.TextBox { Height = 40, Padding = new Thickness(40, 0, 10, 0), VerticalContentAlignment = System.Windows.VerticalAlignment.Center };
            searchHost.Children.Add(search);
            searchHost.Children.Add(new TextBlock
            {
                Text = "🔍",
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                IsHitTestVisible = false
            });
            var hint = new TextBlock
            {
                Text = Loc.T("Programm suchen ..."),
                Margin = new Thickness(40, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = MakeBrush("#6B7280"),
                IsHitTestVisible = false
            };
            searchHost.Children.Add(hint);
            panel.Children.Add(searchHost);

            var list = new StackPanel();
            panel.Children.Add(new ScrollViewer { Height = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });

            void Fill(string filter)
            {
                list.Children.Clear();
                var matches = cachedApps
                    .Where(a => filter.Length == 0 || a.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                    .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Take(80)
                    .ToList();

                if (matches.Count == 0)
                {
                    list.Children.Add(new TextBlock { Text = Loc.T("Nichts gefunden. Wähle unten eine Datei aus."), Foreground = BrushSubtle, Margin = new Thickness(4, 8, 0, 0) });
                    return;
                }

                foreach (var app in matches)
                {
                    var item = app;
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    FrameworkElement icon = item.Icon != null
                        ? new System.Windows.Controls.Image { Source = item.Icon, Width = 28, Height = 28, Stretch = Stretch.Uniform }
                        : new TextBlock { Text = "🧩", FontSize = 20, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                    icon.Margin = new Thickness(0, 0, 12, 0);
                    row.Children.Add(icon);

                    var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
                    texts.Children.Add(new TextBlock { Text = item.Name, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
                    texts.Children.Add(new TextBlock { Text = Loc.T(item.Category), Foreground = BrushSubtle, FontSize = 11 });
                    Grid.SetColumn(texts, 1);
                    row.Children.Add(texts);

                    var card = new Border { Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(10), Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Child = row };
                    card.MouseEnter += (s, e) => card.Background = BrushTileHover;
                    card.MouseLeave += (s, e) => card.Background = System.Windows.Media.Brushes.Transparent;
                    card.MouseLeftButtonUp += (s, e) =>
                    {
                        result = ("app:" + item.Name, item.Name, item.AppId, item.ExePath);
                        dialog.DialogResult = true;
                    };
                    list.Children.Add(card);
                }
            }

            search.TextChanged += (s, e) =>
            {
                hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                Fill(search.Text.Trim());
            };
            Fill(string.Empty);

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var file = new System.Windows.Controls.Button { Content = Loc.T("Datei auswählen ..."), Margin = new Thickness(0, 0, 10, 0) };
            var cancel = new System.Windows.Controls.Button { Content = Loc.T("Abbrechen"), IsCancel = true };
            buttons.Children.Add(file);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            file.Click += (s, e) =>
            {
                var open = new Microsoft.Win32.OpenFileDialog { Filter = "Programme und Verknüpfungen|*.exe;*.lnk;*.bat;*.cmd;*.url|Alle Dateien|*.*" };
                if (open.ShowDialog() != true) return;

                result = (open.FileName, System.IO.Path.GetFileNameWithoutExtension(open.FileName), string.Empty, open.FileName);
                dialog.DialogResult = true;
            };

            dialog.Loaded += (s, e) => search.Focus();
            dialog.ShowDialog();
            return result;
        }

        private void PickQuickTarget(QuickButton? existing)
        {
            var picked = ShowAppPicker(Loc.T("Schnellstart-Button"));
            if (picked == null) return;

            var (path, name, appId, exePath) = picked.Value;

            if (existing != null)
            {
                existing.Path = path;
                existing.AppId = appId;
                existing.ExePath = exePath;
                if (string.IsNullOrWhiteSpace(existing.Name)) existing.Name = name;
            }
            else
            {
                settings.QuickButtons.Add(new QuickButton { Name = name, Path = path, AppId = appId, ExePath = exePath });
            }

            SaveSettings();
            RenderSidebarExtras();
        }

        // ───────────────────────────── Dashboard-Schnellzugriff: echte Symbole ─────────────────────────────

        private readonly Dictionary<string, ImageSource?> linkIconCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> faviconTried = new(StringComparer.OrdinalIgnoreCase);

        private ImageSource? ResolveQuickIcon(QuickLink link)
        {
            try
            {
                string icon = link.Icon.Trim();

                // 1) Der Nutzer hat selbst eine Bilddatei oder Symbol-Datei gewählt
                if (icon.Length > 0 && LooksLikePath(icon) && File.Exists(icon))
                {
                    if (IsImageFile(icon))
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.UriSource = new Uri(icon);
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.DecodePixelWidth = 128;
                        image.EndInit();
                        image.Freeze();
                        return image;
                    }
                    return ExtractIcon(icon);
                }

                string target = link.Target.Trim();

                if (!settings.QuickRealIcons)
                    return icon.Length == 0 && File.Exists(target) ? ExtractIcon(target) : null;

                // 2) Programme und Ordner: das echte Symbol der Datei
                if (File.Exists(target) || Directory.Exists(target))
                {
                    string key = "file|" + target;
                    if (!linkIconCache.TryGetValue(key, out var fileIcon))
                    {
                        fileIcon = ShellImage(target, 96) ?? ExtractIcon(target);
                        linkIconCache[key] = fileIcon;
                    }
                    return fileIcon;
                }

                // 3) Webseiten: das Symbol der Seite (wird einmal geladen und gespeichert)
                if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    string host = uri.Host.Replace("www.", string.Empty);
                    string file = System.IO.Path.Combine(IconCacheDir, "fav_" + host + ".png");

                    if (File.Exists(file))
                    {
                        string key = "fav|" + host;
                        if (!linkIconCache.TryGetValue(key, out var favicon))
                        {
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.UriSource = new Uri(file);
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            image.DecodePixelWidth = 128;
                            image.EndInit();
                            image.Freeze();
                            favicon = image;
                            linkIconCache[key] = favicon;
                        }
                        return favicon;
                    }

                    if (settings.OnlineFeatures && faviconTried.Add(host)) _ = DownloadFaviconAsync(host);
                }
            }
            catch { }

            return null;
        }

        private async Task DownloadFaviconAsync(string host)
        {
            try
            {
                byte[] bytes = await Http.GetByteArrayAsync($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=128");
                if (bytes.Length < 200) return;

                Directory.CreateDirectory(IconCacheDir);
                await File.WriteAllBytesAsync(System.IO.Path.Combine(IconCacheDir, "fav_" + host + ".png"), bytes);

                BuildLinkTiles();
                FillQuickWidget();
                if (ViewStreamer.Visibility == Visibility.Visible) RenderStreamerPage();
            }
            catch { }
        }

        // ───────────────────────────── Download-Anzeige im Dashboard ─────────────────────────────

        private sealed class DownloadState
        {
            public string Name = string.Empty;
            public string Store = string.Empty;
            public double Progress = -1;   // 0..1, -1 = unbekannt
            public double Speed;           // Bytes pro Sekunde
            public long Remaining = -1;    // Bytes, -1 = unbekannt
            public bool Estimated;
            public string? OpenUri;
        }

        private DispatcherTimer? downloadTimer;
        private bool downloadBusy;
        private long nicLastBytes = -1;
        private DateTime nicLastStamp = DateTime.MinValue;
        private double nicSpeed;
        private long steamLastDone = -1;
        private DateTime steamLastStamp = DateTime.MinValue;
        private double steamSpeed;
        private int fastNetworkTicks;
        private DateTime downloadLastSeen = DateTime.MinValue;
        private string? downloadOpenUri;
        private DownloadState? downloadFake;
        private DateTime downloadFakeUntil = DateTime.MinValue;
        private long epicSizeCache = -1;
        private DateTime epicSizeStamp = DateTime.MinValue;

        private void StartDownloadMonitor()
        {
            downloadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            downloadTimer.Tick += async (s, e) => await PollDownloadsAsync();
            downloadTimer.Start();
        }

        private async Task PollDownloadsAsync()
        {
            if (downloadBusy) return;

            if (!settings.DownloadWidget || !IsVisible || WindowState == WindowState.Minimized || ViewDashboard.Visibility != Visibility.Visible)
            {
                if (DownloadPill.Visibility == Visibility.Visible) DownloadPill.Visibility = Visibility.Collapsed;
                return;
            }

            downloadBusy = true;
            try
            {
                DownloadState? state;
                if (downloadFake != null && DateTime.Now < downloadFakeUntil) state = downloadFake;
                else state = await Task.Run(ReadDownloadState);

                ShowDownloadState(state);
            }
            catch { }
            finally
            {
                downloadBusy = false;
            }
        }

        private void SampleNetworkSpeed()
        {
            long total = 0;
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
                total += nic.GetIPv4Statistics().BytesReceived;
            }

            var now = DateTime.Now;
            if (nicLastBytes >= 0)
            {
                double seconds = Math.Max(0.5, (now - nicLastStamp).TotalSeconds);
                double current = Math.Max(0, total - nicLastBytes) / seconds;
                nicSpeed = nicSpeed * 0.5 + current * 0.5;
            }
            nicLastBytes = total;
            nicLastStamp = now;
        }

        private static long ReadAcfNumber(string text, string key)
        {
            var match = Regex.Match(text, "\"" + key + "\"\\s+\"(\\d+)\"");
            return match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : 0;
        }

        private (string Name, long Done, long Total)? ReadSteamDownload()
        {
            string? steamPath =
                Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return null;

            var libraries = new List<string> { System.IO.Path.GetFullPath(System.IO.Path.Combine(steamPath, "steamapps")) };
            string vdf = System.IO.Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string library = System.IO.Path.GetFullPath(System.IO.Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));
                    if (Directory.Exists(library) && !libraries.Contains(library, StringComparer.OrdinalIgnoreCase)) libraries.Add(library);
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
                        long flags = ReadAcfNumber(text, "StateFlags");

                        // 256 = Update läuft, 512 = pausiert, 1048576 = lädt herunter, 2097152 = entpackt
                        bool active = (flags & (256 | 1048576 | 2097152)) != 0 && (flags & 512) == 0;
                        if (!active) continue;

                        long total = ReadAcfNumber(text, "BytesToDownload");
                        long done = ReadAcfNumber(text, "BytesDownloaded");
                        if (total <= 0) continue;

                        var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"");
                        return (name.Success ? name.Groups[1].Value : "Steam", done, total);
                    }
                    catch { }
                }
            }

            return null;
        }

        private (string Name, long InstallSize, string Location)? ReadEpicDownload()
        {
            string manifestDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(manifestDir)) return null;

            foreach (string file in Directory.EnumerateFiles(manifestDir, "*.item"))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    var root = document.RootElement;
                    if (!root.TryGetProperty("bIsIncompleteInstall", out var incomplete) || incomplete.ValueKind != JsonValueKind.True) continue;

                    string name = GetJsonString(root, "DisplayName");
                    string location = GetJsonString(root, "InstallLocation");
                    long size = root.TryGetProperty("InstallSize", out var sizeElement) && sizeElement.TryGetInt64(out long parsed) ? parsed : 0;
                    if (name.Length > 0 && location.Length > 0) return (name, size, location);
                }
                catch { }
            }

            return null;
        }

        private static readonly (string Process, string Label)[] StoreLaunchers =
        {
            ("EADesktop", "EA app"), ("Battle.net", "Battle.net"), ("UbisoftConnect", "Ubisoft Connect"), ("upc", "Ubisoft Connect"),
            ("GalaxyClient", "GOG Galaxy"), ("XboxPcApp", "Xbox"), ("GamingApp", "Xbox")
        };

        private DownloadState? ReadDownloadState()
        {
            try { SampleNetworkSpeed(); }
            catch { }

            // Steam: genaue Werte aus den Manifesten
            var steam = ReadSteamDownload();
            if (steam != null)
            {
                var (name, done, total) = steam.Value;
                var now = DateTime.Now;
                if (steamLastDone >= 0 && done > steamLastDone)
                {
                    double current = (done - steamLastDone) / Math.Max(0.5, (now - steamLastStamp).TotalSeconds);
                    steamSpeed = steamSpeed * 0.5 + current * 0.5;
                }
                if (done != steamLastDone)
                {
                    steamLastDone = done;
                    steamLastStamp = now;
                }

                double speed = nicSpeed > 100_000 ? nicSpeed : steamSpeed;
                return new DownloadState
                {
                    Name = name,
                    Store = "Steam",
                    Progress = Math.Clamp((double)done / total, 0, 1),
                    Speed = speed,
                    Remaining = Math.Max(0, total - done),
                    OpenUri = "steam://open/downloads"
                };
            }
            steamLastDone = -1;

            // Epic: nur eine Schätzung über die Ordnergröße
            var epic = ReadEpicDownload();
            if (epic != null && nicSpeed > 300_000)
            {
                var (name, installSize, location) = epic.Value;
                if ((DateTime.Now - epicSizeStamp).TotalSeconds > 8)
                {
                    epicSizeStamp = DateTime.Now;
                    try
                    {
                        epicSizeCache = Directory.EnumerateFiles(location, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                            .Sum(f => new FileInfo(f).Length);
                    }
                    catch
                    {
                        epicSizeCache = -1;
                    }
                }

                double progress = installSize > 0 && epicSizeCache >= 0 ? Math.Clamp((double)epicSizeCache / installSize, 0, 0.99) : -1;
                return new DownloadState
                {
                    Name = name,
                    Store = "Epic Games",
                    Progress = progress,
                    Speed = nicSpeed,
                    Remaining = progress >= 0 && installSize > 0 ? (long)(installSize * (1 - progress)) : -1,
                    Estimated = true
                };
            }

            // Andere Stores: kein genauer Fortschritt, aber die Geschwindigkeit ist sichtbar
            if (nicSpeed > 3_000_000)
            {
                fastNetworkTicks++;
                if (fastNetworkTicks >= 2)
                {
                    foreach (var (process, label) in StoreLaunchers)
                    {
                        if (System.Diagnostics.Process.GetProcessesByName(process).Length == 0) continue;
                        return new DownloadState { Name = label, Store = label, Speed = nicSpeed };
                    }
                }
            }
            else
            {
                fastNetworkTicks = 0;
            }

            return null;
        }

        private static string FormatRemaining(long bytes, double speed)
        {
            if (bytes < 0 || speed < 50_000) return string.Empty;

            double seconds = bytes / speed;
            if (seconds < 90) return Loc.T($"noch {Math.Max(1, (int)seconds)} Sek.");

            int minutes = (int)Math.Round(seconds / 60);
            if (minutes < 60) return Loc.T($"noch {minutes} Min.");

            return Loc.T($"noch {minutes / 60} Std. {minutes % 60} Min.");
        }

        private void ShowDownloadState(DownloadState? state)
        {
            if (state == null)
            {
                // Kurz sichtbar lassen, damit die Anzeige nicht flackert
                if (DownloadPill.Visibility == Visibility.Visible && (DateTime.Now - downloadLastSeen).TotalSeconds > 8)
                    DownloadPill.Visibility = Visibility.Collapsed;
                return;
            }

            downloadLastSeen = DateTime.Now;
            downloadOpenUri = state.OpenUri;
            DownloadPill.Cursor = state.OpenUri != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;

            TxtDlName.Text = state.Name;

            var parts = new List<string>();
            if (state.Progress >= 0) parts.Add((state.Estimated ? "≈ " : string.Empty) + $"{state.Progress * 100:F0} %");
            if (state.Speed > 0) parts.Add(FormatSpeed(state.Speed));
            string remaining = FormatRemaining(state.Remaining, state.Speed);
            if (remaining.Length > 0) parts.Add(remaining);
            TxtDlInfo.Text = string.Join("  ·  ", parts);

            DlFillScale.ScaleX = state.Progress >= 0 ? state.Progress : 1;
            DlFill.Opacity = state.Progress >= 0 ? 1 : 0.25;
            DownloadPill.ToolTip = state.Store;
            DownloadPill.Visibility = Visibility.Visible;
        }

        private void DownloadPill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (downloadOpenUri != null) OpenShell(downloadOpenUri);
        }

        private void ChkDownloadWidget_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            settings.DownloadWidget = ChkDownloadWidget.IsChecked == true;
            SaveSettings();
            if (!settings.DownloadWidget) DownloadPill.Visibility = Visibility.Collapsed;
        }

        private void TestDownloadPill()
        {
            downloadFake = new DownloadState
            {
                Name = "Beispiel-Spiel",
                Store = "Steam",
                Progress = 0.42,
                Speed = 38_500_000,
                Remaining = 6_200_000_000
            };
            downloadFakeUntil = DateTime.Now.AddSeconds(20);
            _ = PollDownloadsAsync();
        }

        private void InitExtras15()
        {
            bool before = isLoadingSettings;
            isLoadingSettings = true;
            try { ChkDownloadWidget.IsChecked = settings.DownloadWidget; }
            finally { isLoadingSettings = before; }

            StartDownloadMonitor();
        }
    }
}
