using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Spiel-Extras: Begleitprogramme schließen ─────────────────────────────

        // Prozessnamen der Begleitprogramme, die der Launcher selbst gestartet hat (vorher laufende bleiben offen)
        private readonly HashSet<string> startedCompanions = new(StringComparer.OrdinalIgnoreCase);

        private void ReadGameExtrasSettings()
        {
            settings.CloseCompanionsAfterGame = ChkCloseCompanions.IsChecked == true;
            settings.DashboardCoverTint = ChkCoverTint.IsChecked == true;

            bool presenceBefore = settings.DiscordPresence;
            settings.DiscordPresence = ChkDiscordPresence.IsChecked == true;
            if (presenceBefore != settings.DiscordPresence) UpdateDiscordPresence();
        }

        private void PopulateGameExtrasSettings()
        {
            ChkCloseCompanions.IsChecked = settings.CloseCompanionsAfterGame;
            ChkCoverTint.IsChecked = settings.DashboardCoverTint;
            ChkDiscordPresence.IsChecked = settings.DiscordPresence;
            DiscordPresenceRow.Visibility = DiscordApplicationId.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            PwdSteamKey.Password = SecretStore.Unprotect(settings.SteamApiKeyProtected);
            if (settings.SteamId64.Length == 0) settings.SteamId64 = DetectSteamId64();
            TxtSteamId.Text = settings.SteamId64;
        }

        // ───────────────────────────── Dashboard passend zum letzten Spiel einfärben ─────────────────────────────

        private readonly Dictionary<string, System.Windows.Media.Color?> coverColors = new(StringComparer.OrdinalIgnoreCase);
        private string coverTintPath = string.Empty;

        private void UpdateCoverTint()
        {
            if (CoverTint == null) return;

            var last = settings.DashboardCoverTint && ViewDashboard.Visibility == Visibility.Visible
                ? allGames.Where(g => !g.Hidden && g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).Take(5).FirstOrDefault(HasCover)
                : null;
            if (last == null)
            {
                coverTintPath = string.Empty;
                CoverTint.BeginAnimation(UIElement.OpacityProperty, null);
                CoverTint.Opacity = 0;
                CoverTint.Visibility = Visibility.Collapsed;
                return;
            }

            string path = EffectiveCover(last);
            if (path == coverTintPath && CoverTint.Visibility == Visibility.Visible) return;
            coverTintPath = path;

            if (coverColors.TryGetValue(path, out var known))
            {
                ApplyCoverTint(known);
                return;
            }

            // Bild im Hintergrund winzig decodieren und die Hauptfarbe bestimmen
            Task.Run(() => ExtractCoverColor(path)).ContinueWith(task =>
            {
                var color = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    coverColors[path] = color;
                    if (coverTintPath == path) ApplyCoverTint(color);
                }));
            });
        }

        private void ApplyCoverTint(System.Windows.Media.Color? color)
        {
            if (color == null)
            {
                CoverTint.Visibility = Visibility.Collapsed;
                return;
            }

            var c = color.Value;
            CoverTint.Background = new System.Windows.Media.LinearGradientBrush(
                new System.Windows.Media.GradientStopCollection
                {
                    new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0x70, c.R, c.G, c.B), 0.0),
                    new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0x26, c.R, c.G, c.B), 0.45),
                    new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0x00, c.R, c.G, c.B), 0.8)
                },
                new System.Windows.Point(1, 0), new System.Windows.Point(0, 1));
            CoverTint.Visibility = Visibility.Visible;

            if (settings.PerformanceMode || !settings.PageAnimations)
            {
                CoverTint.BeginAnimation(UIElement.OpacityProperty, null);
                CoverTint.Opacity = 1;
            }
            else
            {
                CoverTint.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)));
            }
        }

        /// <summary>Kräftigste Durchschnittsfarbe des Covers: graue, sehr dunkle und sehr helle Pixel zählen kaum.</summary>
        private static System.Windows.Media.Color? ExtractCoverColor(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.DecodePixelWidth = 32;
                image.EndInit();
                image.Freeze();

                var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int width = bgra.PixelWidth, height = bgra.PixelHeight;
                var pixels = new byte[width * height * 4];
                bgra.CopyPixels(pixels, width * 4, 0);

                double r = 0, g = 0, b = 0, total = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    double pb = pixels[i] / 255.0, pg = pixels[i + 1] / 255.0, pr = pixels[i + 2] / 255.0;
                    double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
                    double saturation = max > 0 ? (max - min) / max : 0;
                    double weight = saturation * saturation * (max > 0.15 && max < 0.97 ? 1 : 0.1) + 0.002;
                    r += pr * weight;
                    g += pg * weight;
                    b += pb * weight;
                    total += weight;
                }
                if (total <= 0) return null;

                // Etwas kräftiger und gleichmäßig hell, damit der Verlauf auf dunklem Grund sichtbar ist
                r /= total; g /= total; b /= total;
                double peak = Math.Max(r, Math.Max(g, b));
                if (peak < 0.05) return null;
                double scale = 0.85 / peak;
                return System.Windows.Media.Color.FromRgb(
                    (byte)Math.Clamp(r * scale * 255, 0, 255),
                    (byte)Math.Clamp(g * scale * 255, 0, 255),
                    (byte)Math.Clamp(b * scale * 255, 0, 255));
            }
            catch
            {
                return null;
            }
        }

        private System.Windows.Threading.DispatcherTimer? displayWatchTimer;
        private DateTime displayChangedAt = DateTime.MinValue;

        private void InitGameExtras()
        {
            // Ist der Launcher abgestürzt, während Auflösung oder HDR für ein Spiel umgestellt waren: zurückstellen
            if (settings.DisplayChangedByLauncher || settings.HdrRestore >= 0) RestoreGameDisplay();

            if (System.Windows.Application.Current is { } app) app.Exit += (s, e) => RestoreGameDisplay();
        }

        /// <summary>Kein Spiel läuft mehr: aufräumen, was für die Spiele gestartet oder umgestellt wurde.</summary>
        private void OnAllGamesEnded()
        {
            CloseStartedCompanions();
            RestoreGameDisplay();
        }

        // ───────────────────────────── Discord-Status ─────────────────────────────

        // Application ID der Discord-Anwendung „DFP Pro Launcher“ (discord.com/developers). Leer = Funktion ausgeblendet.
        private const string DiscordApplicationId = "";

        private DiscordPresence? discordPresence;

        private void UpdateDiscordPresence()
        {
            if (DiscordApplicationId.Length == 0) return;
            discordPresence ??= new DiscordPresence(DiscordApplicationId);

            var game = settings.DiscordPresence
                ? activeSessions.Keys.Where(sessionStart.ContainsKey).OrderByDescending(g => sessionStart[g]).FirstOrDefault()
                : null;

            if (game == null) _ = discordPresence.ClearAsync();
            else _ = discordPresence.SetAsync(game.Name, Loc.T("über DFP Pro Launcher"), sessionStart[game]);
        }

        // ───────────────────────────── Auflösung und HDR pro Spiel ─────────────────────────────

        private void ApplyGameDisplay(GameItem game)
        {
            bool changed = false;

            if (DisplayModes.Mode.Parse(game.DisplayMode) is { } wanted && DisplayModes.Current() != wanted)
            {
                if (DisplayModes.Apply(wanted))
                {
                    settings.DisplayChangedByLauncher = true;
                    changed = true;
                }
                else
                {
                    ShowToast("🖥", Loc.T("Auflösung nicht möglich"), Loc.T($"Der Bildschirm unterstützt {wanted.Width} × {wanted.Height} bei {wanted.Hz} Hz nicht."), 6);
                }
            }

            if (game.HdrMode is "on" or "off" && DisplayModes.HdrEnabled() is bool hdrNow)
            {
                bool want = game.HdrMode == "on";
                if (hdrNow != want && DisplayModes.SetHdr(want))
                {
                    if (settings.HdrRestore < 0) settings.HdrRestore = hdrNow ? 1 : 0;   // nur den ursprünglichen Zustand merken
                    changed = true;
                }
            }

            if (!changed) return;
            SaveSettings();
            displayChangedAt = DateTime.Now;

            // Wird das Spiel nie als laufend erkannt (Spielzeit-Erfassung aus, Spiel startet nicht), trotzdem zurückstellen
            displayWatchTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            displayWatchTimer.Tick -= DisplayWatchTick;
            displayWatchTimer.Tick += DisplayWatchTick;
            displayWatchTimer.Start();
        }

        private void DisplayWatchTick(object? sender, EventArgs e)
        {
            if (!settings.DisplayChangedByLauncher && settings.HdrRestore < 0)
            {
                displayWatchTimer?.Stop();
                return;
            }
            if (activeSessions.Count == 0 && (DateTime.Now - displayChangedAt).TotalMinutes >= 3) RestoreGameDisplay();
        }

        private void RestoreGameDisplay()
        {
            displayWatchTimer?.Stop();
            if (!settings.DisplayChangedByLauncher && settings.HdrRestore < 0) return;

            try
            {
                if (settings.DisplayChangedByLauncher) DisplayModes.Reset();
                if (settings.HdrRestore >= 0) DisplayModes.SetHdr(settings.HdrRestore == 1);
            }
            catch { }

            settings.DisplayChangedByLauncher = false;
            settings.HdrRestore = -1;
            SaveSettings();
        }

        /// <summary>Abschnitt „Bildschirm beim Spielstart“ für den Dialog mit den Startoptionen.</summary>
        private void AddDisplayOptions(System.Windows.Controls.Panel panel, Func<string> getMode, Action<string> setMode, Func<string> getHdr, Action<string> setHdr)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = Loc.T("Bildschirm beim Spielstart"), Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 8) });

            var row = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            var modeButton = new System.Windows.Controls.Button { Margin = new Thickness(0, 0, 10, 0) };
            var hdrButton = new System.Windows.Controls.Button();
            row.Children.Add(modeButton);
            row.Children.Add(hdrButton);
            panel.Children.Add(row);

            bool? hdrSupported = DisplayModes.HdrEnabled();
            hdrButton.IsEnabled = hdrSupported != null;
            if (hdrSupported == null) hdrButton.ToolTip = Loc.T("Dein Hauptbildschirm unterstützt kein HDR.");

            void Refresh()
            {
                modeButton.Content = (DisplayModes.Mode.Parse(getMode()) is { } m
                    ? $"🖥 {m.Width} × {m.Height} · {m.Hz} Hz"
                    : "🖥 " + Loc.T("Auflösung nicht ändern")) + " ▾";
                hdrButton.Content = "HDR: " + Loc.T(getHdr() switch { "on" => "An", "off" => "Aus", _ => "nicht ändern" }) + " ▾";
            }

            modeButton.Click += (s, e) =>
            {
                var menu = CreateMenu();
                void Add(string header, string value)
                {
                    var item = new System.Windows.Controls.MenuItem { Header = header, IsCheckable = true, IsChecked = getMode() == value };
                    item.Click += (s2, e2) => { setMode(value); Refresh(); };
                    menu.Items.Add(item);
                }

                Add(Loc.T("Auflösung nicht ändern"), string.Empty);
                var current = DisplayModes.Current();
                foreach (var mode in DisplayModes.All().Take(40))
                    Add($"{mode.Width} × {mode.Height} · {mode.Hz} Hz" + (mode == current ? "  (" + Loc.T("aktuell") + ")" : string.Empty), mode.ToString());

                menu.PlacementTarget = modeButton;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.MaxHeight = 420;
                menu.IsOpen = true;
            };

            hdrButton.Click += (s, e) =>
            {
                var menu = CreateMenu();
                foreach (var (label, value) in new[] { ("nicht ändern", ""), ("An", "on"), ("Aus", "off") })
                {
                    var item = new System.Windows.Controls.MenuItem { Header = Loc.T(label), IsCheckable = true, IsChecked = getHdr() == value };
                    item.Click += (s2, e2) => { setHdr(value); Refresh(); };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = hdrButton;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            };

            Refresh();
        }

        private void CloseStartedCompanions()
        {
            var names = startedCompanions.ToList();
            startedCompanions.Clear();
            if (!settings.CloseCompanionsAfterGame || names.Count == 0) return;

            // Nur freundlich schließen (wie das X im Fenster), nie hart beenden: OBS & Co. dürfen nichts verlieren
            Task.Run(() =>
            {
                foreach (string name in names)
                {
                    if (ProtectedProcesses.Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    try
                    {
                        foreach (var process in Process.GetProcessesByName(name))
                        {
                            try { process.CloseMainWindow(); }
                            catch { }
                            finally { process.Dispose(); }
                        }
                    }
                    catch { }
                }
            });
        }
    }
}
