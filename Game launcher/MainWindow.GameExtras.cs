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
        }

        private void PopulateGameExtrasSettings()
        {
            ChkCloseCompanions.IsChecked = settings.CloseCompanionsAfterGame;
            ChkCoverTint.IsChecked = settings.DashboardCoverTint;
        }

        // ───────────────────────────── Dashboard passend zum letzten Spiel einfärben ─────────────────────────────

        private readonly Dictionary<string, System.Windows.Media.Color?> coverColors = new(StringComparer.OrdinalIgnoreCase);
        private string coverTintPath = string.Empty;

        private void UpdateCoverTint()
        {
            if (CoverTint == null) return;

            var last = settings.DashboardCoverTint && ViewDashboard.Visibility == Visibility.Visible
                ? allGames.Where(g => !g.Hidden && g.LastPlayed.HasValue && HasCover(g)).OrderByDescending(g => g.LastPlayed).FirstOrDefault()
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

        /// <summary>Kein Spiel läuft mehr: aufräumen, was für die Spiele gestartet oder umgestellt wurde.</summary>
        private void OnAllGamesEnded()
        {
            CloseStartedCompanions();
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
