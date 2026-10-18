using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Steam-Erfolge (Steam-Web-API) ─────────────────────────────

        private sealed class SteamAchievement
        {
            public string Name = string.Empty, Description = string.Empty, Icon = string.Empty, IconGray = string.Empty;
            public bool Unlocked, Hidden;
            public DateTime? UnlockedAt;
            public double Percent = -1;
        }

        private readonly Dictionary<string, (DateTime Loaded, List<SteamAchievement> List)> achievementCache = new();

        /// <summary>SteamID64 des zuletzt angemeldeten Steam-Kontos aus loginusers.vdf.</summary>
        private static string DetectSteamId64()
        {
            try
            {
                string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrEmpty(steamPath)) return string.Empty;
                string file = System.IO.Path.Combine(steamPath, "config", "loginusers.vdf");
                if (!File.Exists(file)) return string.Empty;

                string text = File.ReadAllText(file);
                var users = Regex.Matches(text, @"""(\d{17})""\s*\{([^}]*)\}");
                var recent = users.FirstOrDefault(m => Regex.IsMatch(m.Groups[2].Value, @"""MostRecent""\s*""1"""));
                return (recent ?? users.FirstOrDefault())?.Groups[1].Value ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SteamLanguage() => Loc.Language switch
        {
            "en" => "english", "zh" => "schinese", "es" => "spanish", "fr" => "french",
            "pt" => "brazilian", "ru" => "russian", "ja" => "japanese", _ => "german"
        };

        private async Task<List<SteamAchievement>> LoadSteamAchievementsAsync(string appId, string key, string steamId)
        {
            if (achievementCache.TryGetValue(appId, out var cached) && (DateTime.Now - cached.Loaded).TotalMinutes < 20) return cached.List;

            string lang = SteamLanguage();
            string schemaUrl = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={Uri.EscapeDataString(key)}&appid={appId}&l={lang}";
            string playerUrl = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={Uri.EscapeDataString(key)}&steamid={steamId}&appid={appId}";
            string globalUrl = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}";

            var schemaTask = Http.GetStringAsync(schemaUrl);
            var playerTask = GetSteamStringAsync(playerUrl);
            var globalTask = GetSteamStringAsync(globalUrl);
            await Task.WhenAll(schemaTask, playerTask, globalTask);

            var list = new Dictionary<string, SteamAchievement>();
            using (var schema = JsonDocument.Parse(schemaTask.Result))
            {
                if (schema.RootElement.TryGetProperty("game", out var g) && g.TryGetProperty("availableGameStats", out var stats)
                    && stats.TryGetProperty("achievements", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        string api = GetJsonString(item, "name");
                        list[api] = new SteamAchievement
                        {
                            Name = GetJsonString(item, "displayName"),
                            Description = GetJsonString(item, "description"),
                            Icon = GetJsonString(item, "icon"),
                            IconGray = GetJsonString(item, "icongray"),
                            Hidden = item.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.Number && h.GetInt32() == 1
                        };
                    }
                }
            }

            // Spiele ohne Erfolge: Steam meldet beim Spieler dann einen Fehler, das ist aber kein Problem
            if (list.Count == 0)
            {
                achievementCache[appId] = (DateTime.Now, new List<SteamAchievement>());
                return new List<SteamAchievement>();
            }

            // Antwort des Spielers: bei privatem Profil kommt ein Fehlertext statt der Liste
            using (var player = JsonDocument.Parse(playerTask.Result))
            {
                if (!player.RootElement.TryGetProperty("playerstats", out var stats))
                    throw new InvalidOperationException(Loc.T("Steam hat keine Daten zu deinem Konto geliefert."));
                if (stats.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                    throw new InvalidOperationException(GetJsonString(stats, "error"));
                if (stats.TryGetProperty("achievements", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (!list.TryGetValue(GetJsonString(item, "apiname"), out var entry)) continue;
                        entry.Unlocked = item.TryGetProperty("achieved", out var a) && a.GetInt32() == 1;
                        if (entry.Unlocked && item.TryGetProperty("unlocktime", out var t) && t.GetInt64() > 0)
                            entry.UnlockedAt = DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()).LocalDateTime;
                    }
                }
            }

            try
            {
                using var global = JsonDocument.Parse(globalTask.Result);
                foreach (var item in global.RootElement.GetProperty("achievementpercentages").GetProperty("achievements").EnumerateArray())
                {
                    if (!list.TryGetValue(GetJsonString(item, "name"), out var entry) || !item.TryGetProperty("percent", out var p)) continue;
                    entry.Percent = p.ValueKind == JsonValueKind.Number ? p.GetDouble()
                        : double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : -1;
                }
            }
            catch { }   // Seltenheit ist nur ein Zusatz

            var result = list.Values.ToList();
            achievementCache[appId] = (DateTime.Now, result);
            return result;
        }

        /// <summary>Wie GetStringAsync, liefert bei HTTP-Fehlern aber den Antworttext (Steam meldet private Profile mit 403 und JSON).</summary>
        private static async Task<string> GetSteamStringAsync(string url)
        {
            using var response = await Http.GetAsync(url);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode && !body.TrimStart().StartsWith("{"))
                throw new InvalidOperationException(Loc.T($"Steam antwortet mit {(int)response.StatusCode}."));
            return body.TrimStart().StartsWith("{") ? body : "{}";
        }

        private async Task ShowSteamAchievementsAsync(GameItem game)
        {
            string key = SecretStore.Unprotect(settings.SteamApiKeyProtected);
            if (key.Length == 0 || settings.SteamId64.Length == 0)
            {
                Msg("Für die Steam-Erfolge brauchst du einen Steam-Web-API-Schlüssel.\n\nEinstellungen, Cover und Spielinfos, „Steam-Erfolge (optional)“.", "Steam-Erfolge");
                return;
            }
            if (!settings.OnlineFeatures)
            {
                Msg("Die Online-Funktionen sind ausgeschaltet (Einstellungen, Online-Funktionen und Datenschutz).", "Steam-Erfolge");
                return;
            }

            var dialog = CreateDialog(Loc.T("Steam-Erfolge") + ": " + game.Name, 620, out var panel);
            var status = new TextBlock { Text = Loc.T("Erfolge werden geladen ..."), Foreground = BrushSubtle, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(status);
            var list = new StackPanel();
            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 16) });
            var close = new System.Windows.Controls.Button { Content = Loc.T("Schließen"), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, IsCancel = true, Padding = new Thickness(26, 8, 26, 8) };
            close.Click += (s, e) => dialog.Close();
            panel.Children.Add(close);

            dialog.Loaded += async (s, e) =>
            {
                try
                {
                    var achievements = await LoadSteamAchievementsAsync(game.AppId, key, settings.SteamId64);
                    if (achievements.Count == 0)
                    {
                        status.Text = Loc.T("Dieses Spiel hat keine Steam-Erfolge.");
                        return;
                    }

                    int unlocked = achievements.Count(a => a.Unlocked);
                    status.Text = Loc.T($"{unlocked} von {achievements.Count} freigeschaltet ({unlocked * 100 / achievements.Count} %)");
                    var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = MakeBrush("#26FFFFFF"), Margin = new Thickness(0, 0, 0, 14) };
                    var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                    fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    bar.Child = fill;
                    bar.SizeChanged += (s2, e2) => fill.Width = bar.ActualWidth * unlocked / achievements.Count;
                    panel.Children.Insert(1, bar);

                    // Freigeschaltete (neueste zuerst), dann die übrigen nach Seltenheit (häufigste zuerst)
                    foreach (var a in achievements.OrderByDescending(a => a.Unlocked).ThenByDescending(a => a.UnlockedAt).ThenByDescending(a => a.Percent))
                        list.Children.Add(CreateAchievementRow(a));
                }
                catch (Exception ex)
                {
                    status.Text = ex.Message.Contains("not public", StringComparison.OrdinalIgnoreCase)
                        ? Loc.T("Dein Steam-Profil zeigt die Spieldetails nicht öffentlich. In Steam: Profil bearbeiten, Privatsphäre, Spieldetails auf „Öffentlich“.")
                        : Loc.T("Die Erfolge konnten nicht geladen werden:") + " " + ex.Message;
                    status.Foreground = MakeBrush("#F59E0B");
                }
            };

            dialog.ShowDialog();
        }

        private FrameworkElement CreateAchievementRow(SteamAchievement a)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10), Opacity = a.Unlocked ? 1 : 0.55 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string icon = a.Unlocked ? a.Icon : a.IconGray;
            if (icon.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(icon);
                    image.DecodePixelWidth = 64;
                    image.EndInit();
                    row.Children.Add(new System.Windows.Controls.Image { Source = image, Width = 42, Height = 42, VerticalAlignment = System.Windows.VerticalAlignment.Top });
                }
                catch { }
            }

            var texts = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            texts.Children.Add(new TextBlock { Text = a.Name, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            string description = a.Hidden && !a.Unlocked && a.Description.Length == 0 ? Loc.T("Verborgener Erfolg") : a.Description;
            if (description.Length > 0)
                texts.Children.Add(new TextBlock { Text = description, Foreground = BrushSubtle, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);

            var side = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            if (a.UnlockedAt is { } at)
                side.Children.Add(new TextBlock { Text = at.ToString("d", Loc.Culture), Foreground = MakeBrush("#34D399"), FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Right });
            if (a.Percent >= 0)
                side.Children.Add(new TextBlock
                {
                    Text = a.Percent.ToString(a.Percent < 10 ? "0.0" : "0", Loc.Culture) + " %",
                    Foreground = a.Percent < 5 ? MakeBrush("#FBBF24") : BrushSubtle,
                    FontSize = 12,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    ToolTip = Loc.T("Anteil der Spieler mit diesem Erfolg")
                });
            Grid.SetColumn(side, 2);
            row.Children.Add(side);
            return row;
        }

        private async void BtnTestSteamKey_Click(object sender, RoutedEventArgs e)
        {
            string key = PwdSteamKey.Password.Trim();
            string id = TxtSteamId.Text.Trim();
            TxtSteamKeyStatus.Foreground = BrushSubtle;
            TxtSteamKeyStatus.Text = Loc.T("Wird geprüft ...");

            try
            {
                if (key.Length == 0 || !Regex.IsMatch(id, @"^\d{17}$")) throw new InvalidOperationException(Loc.T("Bitte Schlüssel und eine 17-stellige SteamID64 eintragen."));

                string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={Uri.EscapeDataString(key)}&steamids={id}";
                using var response = await Http.GetAsync(url);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Loc.T("Steam lehnt den Schlüssel ab."));

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var players = doc.RootElement.GetProperty("response").GetProperty("players");
                if (players.GetArrayLength() == 0) throw new InvalidOperationException(Loc.T("Zu dieser SteamID gibt es kein Profil."));

                bool open = players[0].TryGetProperty("communityvisibilitystate", out var v) && v.GetInt32() == 3;
                TxtSteamKeyStatus.Foreground = MakeBrush(open ? "#34D399" : "#F59E0B");
                TxtSteamKeyStatus.Text = open
                    ? Loc.T("✓ Schlüssel funktioniert.")
                    : Loc.T("✓ Schlüssel funktioniert, aber dein Profil ist nicht öffentlich. Erfolge sind dann nicht sichtbar.");
            }
            catch (Exception ex)
            {
                TxtSteamKeyStatus.Foreground = MakeBrush("#F87171");
                TxtSteamKeyStatus.Text = ex is InvalidOperationException ? ex.Message : Loc.T("Steam ist nicht erreichbar.");
            }
        }

        private void BtnSaveSteamKey_Click(object sender, RoutedEventArgs e)
        {
            settings.SteamApiKeyProtected = SecretStore.Protect(PwdSteamKey.Password.Trim());
            settings.SteamId64 = TxtSteamId.Text.Trim();
            achievementCache.Clear();
            SaveSettings();
            TxtSteamKeyStatus.Foreground = MakeBrush("#34D399");
            TxtSteamKeyStatus.Text = Loc.T("Gespeichert.");
        }
    }
}
