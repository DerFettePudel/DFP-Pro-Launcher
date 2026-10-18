using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Handy-Fernbedienung ─────────────────────────────

        private RemoteServer? remoteServer;
        private readonly ConcurrentDictionary<string, byte[]> remoteCovers = new();

        /// <summary>Texte der Handy-Seite. Jeder steht in der Übersetzungstabelle.</summary>
        private static readonly string[] RemotePageTexts =
        {
            "Läuft gerade", "Lautstärke", "PC", "Bibliothek", "Spiel suchen...", "Sperren", "Energie sparen", "Herunterfahren",
            "Beenden", "Stumm", "Ton an", "Keine Treffer.", "Gerade läuft kein Spiel.", "Dieses Spiel starten?",
            "Spiel wird gestartet ...", "Spiel wird beendet ...", "Spiel beenden? Nicht gespeicherter Fortschritt geht verloren.",
            "PC in den Energiesparmodus schicken?", "PC jetzt herunterfahren?", "Es läuft noch ein Spiel.", "PC fährt herunter ...",
            "PC wird gesperrt.", "Der PC antwortet nicht.",
            "Der Schlüssel ist nicht mehr gültig. Bitte scanne den QR-Code im Launcher neu.",
            "Scanne den QR-Code im Launcher: Einstellungen, Steuerung, Handy-Fernbedienung."
        };

        private void InitRemote()
        {
            Loaded += (s, e) => { if (settings.RemoteEnabled) StartRemote(false); };
            if (System.Windows.Application.Current is { } app) app.Exit += (s, e) => remoteServer?.Stop();
        }

        private void ReadRemoteSettings()
        {
            bool before = settings.RemoteEnabled;
            settings.RemoteEnabled = ChkRemote.IsChecked == true;
            if (before == settings.RemoteEnabled) return;

            if (settings.RemoteEnabled) StartRemote(true);
            else StopRemote();
        }

        private void PopulateRemoteSettings()
        {
            ChkRemote.IsChecked = settings.RemoteEnabled;
            UpdateRemoteStatus();
        }

        private string RemoteToken()
        {
            string token = SecretStore.Unprotect(settings.RemoteTokenProtected);
            if (token.Length < 16)
            {
                token = RemoteServer.NewToken();
                settings.RemoteTokenProtected = SecretStore.Protect(token);
                SaveSettings();
            }
            return token;
        }

        private void StartRemote(bool showErrors)
        {
            try
            {
                remoteServer?.Stop();
                remoteServer = new RemoteServer(RemoteToken(), BuildRemotePage, HandleRemoteAsync);
                remoteServer.Start(settings.RemotePort);
            }
            catch (Exception ex)
            {
                remoteServer = null;
                LogError("Handy-Fernbedienung", ex);
                if (showErrors) Msg("Die Handy-Fernbedienung konnte nicht gestartet werden. Ein anderes Programm belegt die Ports.", "Handy-Fernbedienung");
            }
            UpdateRemoteStatus();
        }

        private void StopRemote()
        {
            remoteServer?.Stop();
            remoteServer = null;
            UpdateRemoteStatus();
        }

        private string? RemoteAddress()
        {
            string? ip = RemoteServer.LocalAddress();
            return ip == null || remoteServer is not { Running: true } ? null : $"http://{ip}:{remoteServer.Port}/";
        }

        private void UpdateRemoteStatus()
        {
            if (TxtRemoteStatus == null) return;

            string? address = RemoteAddress();
            if (!settings.RemoteEnabled)
            {
                TxtRemoteStatus.Text = Loc.T("Aus.");
                TxtRemoteStatus.Foreground = BrushSubtle;
            }
            else if (address == null)
            {
                TxtRemoteStatus.Text = remoteServer == null
                    ? Loc.T("Konnte nicht gestartet werden.")
                    : Loc.T("Läuft, aber dieser PC hat gerade keine Verbindung zum Heimnetz.");
                TxtRemoteStatus.Foreground = MakeBrush("#F59E0B");
            }
            else
            {
                TxtRemoteStatus.Text = Loc.T($"Läuft unter {address}");
                TxtRemoteStatus.Foreground = MakeBrush("#34D399");
            }
            BtnRemoteQr.IsEnabled = address != null;
        }

        private string BuildRemotePage()
        {
            var strings = RemotePageTexts.ToDictionary(t => t, t => Loc.T(t));
            string accent = "#7C3AED";
            Dispatcher.Invoke(() =>
            {
                if (TryFindResource("AccentBrush") is SolidColorBrush brush)
                    accent = $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
            });
            return RemotePage.Html.Replace("{{STRINGS}}", JsonSerializer.Serialize(strings)).Replace("{{ACCENT}}", accent)
                .Replace("<html lang=\"de\">", $"<html lang=\"{Loc.Language}\">");
        }

        private static string RemoteGameId(GameItem game)
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(game.Source + "|" + game.Name + "|" + game.AppId));
            return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
        }

        private GameItem? FindRemoteGame(string id) => VisibleGames.FirstOrDefault(g => RemoteGameId(g) == id);

        private static string RemoteBodyValue(string body, string name)
        {
            try
            {
                using var doc = JsonDocument.Parse(body.Length == 0 ? "{}" : body);
                if (!doc.RootElement.TryGetProperty(name, out var value)) return string.Empty;
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => string.Empty
                };
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Läuft im Server-Thread. Alles, was die Oberfläche oder Spieldaten anfasst, geht über den Dispatcher.</summary>
        private async Task<RemoteServer.Response> HandleRemoteAsync(RemoteServer.Request request)
        {
            try
            {
                switch (request.Method + " " + request.Path)
                {
                    case "GET /api/state":
                    {
                        var running = await Dispatcher.InvokeAsync(() => activeSessions.Keys
                            .Select(g => new { id = RemoteGameId(g), name = g.Name }).ToList());
                        var volume = SystemVolume.Read();
                        return RemoteServer.Response.Json(JsonSerializer.Serialize(new
                        {
                            running,
                            volume = volume == null ? 0 : (int)Math.Round(volume.Value.Level * 100),
                            muted = volume?.Muted ?? false
                        }));
                    }

                    case "GET /api/games":
                    {
                        var games = await Dispatcher.InvokeAsync(() => VisibleGames
                            .OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                            .Select(g => new { id = RemoteGameId(g), name = g.Name, source = g.Source, cover = HasCover(g) }).ToList());
                        return RemoteServer.Response.Json(JsonSerializer.Serialize(games));
                    }

                    case "GET /api/cover":
                        return await RemoteCoverAsync(RemoteServer.QueryValue(request.Query, "id"));

                    case "POST /api/launch":
                    {
                        string id = RemoteBodyValue(request.Body, "id");
                        bool ok = await Dispatcher.InvokeAsync(() =>
                        {
                            var game = FindRemoteGame(id);
                            if (game == null) return false;
                            ShowToast("📱", "Handy-Fernbedienung", Loc.T($"Wird gestartet: {game.Name}"), 4);
                            LaunchGame(game);
                            return true;
                        });
                        return ok ? RemoteServer.Response.Json("{\"ok\":true}") : RemoteServer.Response.Error(404, "Spiel nicht gefunden");
                    }

                    case "POST /api/end":
                    {
                        string id = RemoteBodyValue(request.Body, "id");
                        bool ok = await Dispatcher.InvokeAsync(() =>
                        {
                            var game = activeSessions.Keys.FirstOrDefault(g => RemoteGameId(g) == id);
                            if (game == null) return false;
                            _ = EndGameAsync(game);
                            return true;
                        });
                        return ok ? RemoteServer.Response.Json("{\"ok\":true}") : RemoteServer.Response.Error(404, "Spiel läuft nicht");
                    }

                    case "POST /api/volume":
                    {
                        var current = SystemVolume.Read();
                        if (current == null) return RemoteServer.Response.Error(500, "Keine Lautstärke");
                        float level = current.Value.Level;
                        bool muted = current.Value.Muted;

                        string value = RemoteBodyValue(request.Body, "level");
                        if (int.TryParse(value, out int percent)) { level = Math.Clamp(percent, 0, 100) / 100f; muted = false; }
                        string mute = RemoteBodyValue(request.Body, "mute");
                        if (mute.Length > 0) muted = mute == "true";

                        lock (padVolumeLock) SystemVolume.Write(level, muted);
                        return RemoteServer.Response.Json("{\"ok\":true}");
                    }

                    case "POST /api/power":
                    {
                        string kind = RemoteBodyValue(request.Body, "kind");
                        (string File, string Args)? action = kind switch
                        {
                            "lock" => ("rundll32.exe", "user32.dll,LockWorkStation"),
                            "sleep" => ("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"),
                            "shutdown" => ("shutdown.exe", "/s /t 10"),
                            _ => null
                        };
                        if (action == null) return RemoteServer.Response.Error(400, "Unbekannte Aktion");

                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (kind == "shutdown") ShowToast("📱", "Handy-Fernbedienung", Loc.T("Der PC fährt in 10 Sekunden herunter."), 10, null, true);
                            FlushSettings();
                        });
                        Process.Start(new ProcessStartInfo(action.Value.File, action.Value.Args) { UseShellExecute = false, CreateNoWindow = true });
                        return RemoteServer.Response.Json("{\"ok\":true}");
                    }
                }

                return RemoteServer.Response.Error(404, "Nicht gefunden");
            }
            catch (Exception ex)
            {
                LogError("Handy-Fernbedienung", ex);
                return RemoteServer.Response.Error(500, "Fehler");
            }
        }

        /// <summary>Kleines JPEG des Covers (200 px breit), einmal erzeugt und dann im Speicher gehalten.</summary>
        private async Task<RemoteServer.Response> RemoteCoverAsync(string id)
        {
            if (remoteCovers.TryGetValue(id, out var cached))
                return new RemoteServer.Response { ContentType = "image/jpeg", Body = cached, CacheControl = "private, max-age=86400" };

            string path = await Dispatcher.InvokeAsync(() =>
            {
                var game = FindRemoteGame(id);
                return game != null && HasCover(game) ? EffectiveCover(game) : string.Empty;
            });
            if (path.Length == 0 || !File.Exists(path)) return RemoteServer.Response.Error(404, "Kein Cover");

            BitmapSource? image;
            if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            {
                image = DecodeWebpStill(path, 200);
            }
            else
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 200;
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
            if (image == null) return RemoteServer.Response.Error(404, "Kein Cover");

            // JPEG kennt keine Transparenz: auf dunklen Grund setzen
            var flat = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
            var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
            encoder.Frames.Add(BitmapFrame.Create(flat));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            byte[] bytes = stream.ToArray();

            if (remoteCovers.Count > 500) remoteCovers.Clear();
            remoteCovers[id] = bytes;
            return new RemoteServer.Response { ContentType = "image/jpeg", Body = bytes, CacheControl = "private, max-age=86400" };
        }

        private void BtnRemoteQr_Click(object sender, RoutedEventArgs e)
        {
            if (StreamerOn)
            {
                Msg("Im Streamer-Modus wird der QR-Code nicht angezeigt, weil er den geheimen Schlüssel enthält.", "Handy-Fernbedienung");
                return;
            }

            string? address = RemoteAddress();
            if (address == null) return;

            string url = address + "#t=" + RemoteToken();
            var dialog = CreateDialog(Loc.T("Handy-Fernbedienung"), 460, out var panel);
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Scanne den Code mit der Kamera deines Handys. Handy und PC müssen im selben WLAN sein. Tipp: Danach die Seite zum Startbildschirm hinzufügen."),
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });
            panel.Children.Add(new Border
            {
                Background = System.Windows.Media.Brushes.White,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Child = new System.Windows.Controls.Image { Source = RenderQr(url), Width = 300, Height = 300, SnapsToDevicePixels = true }
            });
            panel.Children.Add(new TextBlock
            {
                Text = address,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Fragt Windows nach der Firewall, bitte „Zulassen“ für private Netzwerke wählen."),
                Foreground = BrushSubtle,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });
            var close = new System.Windows.Controls.Button { Content = Loc.T("Schließen"), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, IsCancel = true, Padding = new Thickness(26, 8, 26, 8) };
            close.Click += (s2, e2) => dialog.Close();
            panel.Children.Add(close);
            dialog.ShowDialog();
        }

        private static BitmapSource RenderQr(string text)
        {
            var qr = QrCode.Encode(text);
            const int scale = 8;
            int size = qr.Size * scale;
            var pixels = new byte[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = qr[x / scale, y / scale] ? (byte)0 : (byte)255;

            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Gray8, null, pixels, size);
            bitmap.Freeze();
            return bitmap;
        }

        private void BtnRemoteNewKey_Click(object sender, RoutedEventArgs e)
        {
            var answer = Msg("Einen neuen Schlüssel erzeugen? Alle Handys, die bisher verbunden waren, müssen den QR-Code dann neu scannen.",
                "Handy-Fernbedienung", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            settings.RemoteTokenProtected = SecretStore.Protect(RemoteServer.NewToken());
            SaveSettings();
            if (settings.RemoteEnabled) StartRemote(true);
            ShowToast("📱", "Handy-Fernbedienung", Loc.T("Neuer Schlüssel erzeugt. Bitte den QR-Code neu scannen."), 5);
        }
    }
}
