using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Dashboard: Steam-Freunde und „Fast geschafft“ ─────────────────────────────

        private StackPanel? friendsWidget;
        private StackPanel? achievementsWidget;
        private DispatcherTimer? steamSocialTimer;

        private sealed class SteamFriend
        {
            public string Name = string.Empty, Avatar = string.Empty, GameName = string.Empty, GameId = string.Empty;
            public int State;   // 0 offline, 1 online, 2 beschäftigt, 3 abwesend, 4 schlafend, 5/6 sucht
        }

        private List<SteamFriend>? friendsData;
        private string friendsError = string.Empty;
        private DateTime friendsLoaded = DateTime.MinValue;
        private bool friendsLoading;

        private sealed class GameProgress
        {
            public GameItem Game = null!;
            public int Unlocked, Total;
        }

        private sealed class RecentUnlock
        {
            public GameItem Game = null!;
            public string Name = string.Empty, ApiName = string.Empty;
            public DateTime At;
            public double Percent = -1;
        }

        private List<GameProgress>? progressData;
        private RecentUnlock? recentUnlock;
        private string progressError = string.Empty;
        private DateTime progressLoaded = DateTime.MinValue;
        private bool progressLoading;

        private void InitSteamSocial()
        {
            // Bestehende Nutzer: Die neuen Widgets erst einmal ausgeblendet lassen (über „Widget hinzufügen“ einschaltbar)
            if (!settings.DashboardSteamWidgetsAdded)
            {
                foreach (string key in new[] { "friends", "achievements" })
                    if (!settings.DashboardHidden.Contains(key)) settings.DashboardHidden.Add(key);
                settings.DashboardSteamWidgetsAdded = true;
                SaveSettings();
            }

            // Freunde alle 2 Minuten neu laden, solange das Dashboard zu sehen ist
            steamSocialTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            steamSocialTimer.Tick += (s, e) =>
            {
                if (ViewDashboard.IsVisible && WindowState != WindowState.Minimized && !DashOff("friends")) _ = LoadFriendsAsync(true);
            };
            steamSocialTimer.Start();
        }

        private bool SteamApiReady(out string key)
        {
            key = SecretStore.Unprotect(settings.SteamApiKeyProtected);
            return key.Length > 0 && settings.SteamId64.Length == 17 && settings.OnlineFeatures;
        }

        private string SteamApiHint()
        {
            if (!settings.OnlineFeatures) return "Die Online-Funktionen sind ausgeschaltet (Einstellungen, Online-Funktionen und Datenschutz).";
            return "Trage dafür in den Einstellungen unter „Spiele und Cover“, Karte „Cover“, einen Steam-API-Schlüssel ein.";
        }

        // ───────── Freunde ─────────

        private void FillFriendsWidget()
        {
            if (friendsWidget == null) return;
            friendsWidget.Children.Clear();
            if (DashHidden("friends"))
            {
                friendsWidget.Visibility = Visibility.Collapsed;
                return;
            }

            var body = new StackPanel();
            if (!SteamApiReady(out _))
            {
                body.Children.Add(WidgetHint(SteamApiHint()));
            }
            else
            {
                if ((DateTime.Now - friendsLoaded).TotalMinutes >= 2) _ = LoadFriendsAsync(false);

                if (friendsError.Length > 0) body.Children.Add(WidgetHint(friendsError));
                else if (friendsData == null) body.Children.Add(WidgetHint("Freunde werden geladen ..."));
                else
                {
                    var online = friendsData.Where(f => f.State != 0)
                        .OrderByDescending(f => f.GameName.Length > 0).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

                    if (online.Count == 0) body.Children.Add(WidgetHint("Gerade ist keiner deiner Steam-Freunde online."));
                    foreach (var friend in online.Take(8)) body.Children.Add(FriendRow(friend));
                    if (online.Count > 8)
                        body.Children.Add(new TextBlock { Text = Loc.T($"+ {online.Count - 8} weitere online"), Foreground = BrushSubtle, Margin = new Thickness(0, 2, 0, 0) });
                }
            }

            friendsWidget.Children.Add(WidgetCard("👥  Steam-Freunde", body));
            friendsWidget.Visibility = Visibility.Visible;
        }

        private Grid FriendRow(SteamFriend friend)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            bool playing = friend.GameName.Length > 0;
            var avatar = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0),
                Background = BrushFooter,
                BorderThickness = new Thickness(2),
                BorderBrush = MakeBrush(playing ? "#34D399" : friend.State == 1 ? "#60A5FA" : "#6B7280")
            };
            if (friend.Avatar.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(friend.Avatar);
                    image.DecodePixelWidth = 64;
                    image.EndInit();
                    avatar.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                }
                catch { }
            }
            row.Children.Add(avatar);

            string status = playing ? Loc.T($"spielt {friend.GameName}") : Loc.T(friend.State switch
            {
                2 => "Beschäftigt",
                3 => "Abwesend",
                4 => "Abwesend",
                _ => "Online"
            });
            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = friend.Name, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            texts.Children.Add(new TextBlock { Text = status, Foreground = playing ? MakeBrush("#34D399") : BrushSubtle, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);

            // Hast du das Spiel auch, kannst du direkt mitspielen
            var own = playing && friend.GameId.Length > 0 ? VisibleGames.FirstOrDefault(g => g.Source == "Steam" && g.AppId == friend.GameId) : null;
            if (own != null)
            {
                var play = new System.Windows.Controls.Button { Content = "▶", Padding = new Thickness(14, 6, 14, 6), ToolTip = Loc.T("Auch starten") };
                play.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                play.Click += (s, e) => LaunchGame(own);
                Grid.SetColumn(play, 2);
                row.Children.Add(play);
            }
            return row;
        }

        private async Task LoadFriendsAsync(bool quiet)
        {
            if (friendsLoading || !SteamApiReady(out string key)) return;
            friendsLoading = true;
            string steamId = settings.SteamId64;

            try
            {
                var friends = await Task.Run(async () =>
                {
                    string listUrl = $"https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key={Uri.EscapeDataString(key)}&steamid={steamId}&relationship=friend";
                    using var listResponse = await Http.GetAsync(listUrl);
                    if (listResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw new InvalidOperationException(Loc.T("Deine Steam-Freundesliste ist nicht öffentlich. In Steam: Profil bearbeiten, Privatsphäre, Freundesliste auf „Öffentlich“."));
                    listResponse.EnsureSuccessStatusCode();

                    var ids = new List<string>();
                    using (var doc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()))
                    {
                        if (doc.RootElement.TryGetProperty("friendslist", out var fl) && fl.TryGetProperty("friends", out var items))
                            foreach (var item in items.EnumerateArray()) ids.Add(GetJsonString(item, "steamid"));
                    }

                    var result = new List<SteamFriend>();
                    foreach (var chunk in ids.Where(i => i.Length == 17).Chunk(100))
                    {
                        string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={Uri.EscapeDataString(key)}&steamids={string.Join(",", chunk)}";
                        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                        foreach (var p in doc.RootElement.GetProperty("response").GetProperty("players").EnumerateArray())
                        {
                            result.Add(new SteamFriend
                            {
                                Name = GetJsonString(p, "personaname"),
                                Avatar = GetJsonString(p, "avatarmedium"),
                                GameName = GetJsonString(p, "gameextrainfo"),
                                GameId = GetJsonString(p, "gameid"),
                                State = p.TryGetProperty("personastate", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : 0
                            });
                        }
                    }
                    return result;
                });

                friendsData = friends;
                friendsError = string.Empty;
            }
            catch (Exception ex)
            {
                friendsError = ex is InvalidOperationException ? ex.Message : Loc.T("Steam ist gerade nicht erreichbar.");
                if (!quiet) LogError("Steam-Freunde", ex);
            }
            finally
            {
                friendsLoaded = DateTime.Now;
                friendsLoading = false;
            }

            FillFriendsWidget();
        }

        // ───────── Erfolge: Fast geschafft ─────────

        private void FillAchievementsWidget()
        {
            if (achievementsWidget == null) return;
            achievementsWidget.Children.Clear();
            if (DashHidden("achievements"))
            {
                achievementsWidget.Visibility = Visibility.Collapsed;
                return;
            }

            var body = new StackPanel();
            if (!SteamApiReady(out _))
            {
                body.Children.Add(WidgetHint(SteamApiHint()));
            }
            else
            {
                if ((DateTime.Now - progressLoaded).TotalMinutes >= 30) _ = LoadProgressAsync();

                if (progressError.Length > 0) body.Children.Add(WidgetHint(progressError));
                else if (progressData == null) body.Children.Add(WidgetHint("Erfolge werden geladen ..."));
                else
                {
                    var close = progressData.Where(p => p.Unlocked > 0 && p.Unlocked < p.Total)
                        .OrderByDescending(p => p.Unlocked / (double)p.Total).ThenBy(p => p.Total - p.Unlocked).Take(3).ToList();

                    if (close.Count == 0) body.Children.Add(WidgetHint("Spiel ein paar Steam-Spiele mit Erfolgen, dann erscheint hier, was dir noch fehlt."));
                    foreach (var p in close) body.Children.Add(ProgressRow(p));

                    if (recentUnlock != null) body.Children.Add(RecentUnlockRow(recentUnlock));
                }
            }

            achievementsWidget.Children.Add(WidgetCard("🏆  Fast geschafft", body));
            achievementsWidget.Visibility = Visibility.Visible;
        }

        private Grid ProgressRow(GameProgress p)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10), Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ToolTip = Loc.T("Alle Erfolge anzeigen");
            row.MouseLeftButtonUp += (s, e) => _ = ShowSteamAchievementsAsync(p.Game);

            var thumb = new Border { Width = 30, Height = 42, CornerRadius = new CornerRadius(5), Background = BrushFooter, Margin = new Thickness(0, 0, 10, 0) };
            if (HasCover(p.Game) && GetCachedImage(EffectiveCover(p.Game)) is BitmapImage image)
                thumb.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            row.Children.Add(thumb);

            int missing = p.Total - p.Unlocked;
            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock { Text = p.Game.Name, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var count = new TextBlock { Text = $"{p.Unlocked}/{p.Total}", Foreground = BrushSubtle, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(count, 1);
            head.Children.Add(count);
            texts.Children.Add(head);

            double fraction = p.Unlocked / (double)p.Total;
            var bar = new Grid { Margin = new Thickness(0, 5, 0, 3) };
            bar.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = MakeBrush("#1F2937") });
            var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bar.Children.Add(fill);
            bar.SizeChanged += (s, e) => fill.Width = bar.ActualWidth * fraction;
            texts.Children.Add(bar);

            texts.Children.Add(new TextBlock
            {
                Text = missing == 1 ? Loc.T("Nur noch 1 Erfolg bis 100 %") : Loc.T($"Noch {missing} Erfolge bis 100 %"),
                Foreground = BrushSubtle,
                FontSize = 12
            });
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            return row;
        }

        private FrameworkElement RecentUnlockRow(RecentUnlock unlock)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            panel.Children.Add(new TextBlock { Text = Loc.T("Zuletzt freigeschaltet"), Foreground = BrushSubtle, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) });

            string rarity = unlock.Percent >= 0
                ? "  ·  " + Loc.T($"nur {unlock.Percent.ToString(unlock.Percent < 10 ? "0.0" : "0", Loc.Culture)} % der Spieler haben ihn")
                : string.Empty;
            panel.Children.Add(new TextBlock
            {
                Text = "🏅 " + unlock.Name,
                Foreground = unlock.Percent >= 0 && unlock.Percent < 5 ? BrushGold : System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            panel.Children.Add(new TextBlock
            {
                Text = unlock.Game.Name + "  ·  " + unlock.At.ToString("d", Loc.Culture) + rarity,
                Foreground = BrushSubtle,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            return panel;
        }

        /// <summary>Lädt den Fortschritt der zuletzt gespielten Steam-Spiele (höchstens 12, je eine Anfrage).</summary>
        private async Task LoadProgressAsync()
        {
            if (progressLoading || !SteamApiReady(out string key)) return;
            progressLoading = true;
            string steamId = settings.SteamId64, lang = SteamLanguage();

            var games = VisibleGames.Where(g => g.Source == "Steam" && g.AppId.Length > 0 && (g.LastPlayed != null || g.PlaySeconds > 0))
                .OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue).Take(12).ToList();

            try
            {
                var progress = new List<GameProgress>();
                RecentUnlock? newest = null;
                bool privateProfile = false;
                using var gate = new SemaphoreSlim(4);

                await Task.WhenAll(games.Select(async game =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        string url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={Uri.EscapeDataString(key)}&steamid={steamId}&appid={game.AppId}&l={lang}";
                        using var doc = JsonDocument.Parse(await GetSteamStringAsync(url));
                        if (!doc.RootElement.TryGetProperty("playerstats", out var stats)) return;
                        if (stats.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                        {
                            if (GetJsonString(stats, "error").Contains("not public", StringComparison.OrdinalIgnoreCase)) privateProfile = true;
                            return;
                        }
                        if (!stats.TryGetProperty("achievements", out var items)) return;   // Spiel ohne Erfolge

                        int total = 0, unlocked = 0;
                        foreach (var item in items.EnumerateArray())
                        {
                            total++;
                            if (!item.TryGetProperty("achieved", out var a) || a.GetInt32() != 1) continue;
                            unlocked++;
                            if (!item.TryGetProperty("unlocktime", out var t) || t.GetInt64() <= 0) continue;
                            var at = DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()).LocalDateTime;
                            lock (progress)
                            {
                                if (newest == null || at > newest.At)
                                    newest = new RecentUnlock { Game = game, At = at, ApiName = GetJsonString(item, "apiname"), Name = GetJsonString(item, "name") };
                            }
                        }
                        if (total > 0) lock (progress) progress.Add(new GameProgress { Game = game, Unlocked = unlocked, Total = total });
                    }
                    catch { }   // Ein einzelnes Spiel darf scheitern
                    finally
                    {
                        gate.Release();
                    }
                }));

                // Seltenheit des neuesten Erfolgs
                if (newest != null)
                {
                    if (newest.Name.Length == 0) newest.Name = newest.ApiName;
                    try
                    {
                        string url = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={newest.Game.AppId}";
                        using var doc = JsonDocument.Parse(await GetSteamStringAsync(url));
                        foreach (var item in doc.RootElement.GetProperty("achievementpercentages").GetProperty("achievements").EnumerateArray())
                        {
                            if (GetJsonString(item, "name") != newest.ApiName || !item.TryGetProperty("percent", out var p)) continue;
                            newest.Percent = p.ValueKind == JsonValueKind.Number ? p.GetDouble()
                                : double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : -1;
                        }
                    }
                    catch { }
                }

                progressData = progress;
                recentUnlock = newest;
                progressError = progress.Count == 0 && privateProfile
                    ? Loc.T("Dein Steam-Profil zeigt die Spieldetails nicht öffentlich. In Steam: Profil bearbeiten, Privatsphäre, Spieldetails auf „Öffentlich“.")
                    : string.Empty;
            }
            catch (Exception ex)
            {
                progressError = Loc.T("Steam ist gerade nicht erreichbar.");
                LogError("Steam-Erfolge", ex);
            }
            finally
            {
                progressLoaded = DateTime.Now;
                progressLoading = false;
            }

            FillAchievementsWidget();
        }

        /// <summary>Nach einer Spielsitzung sind neue Erfolge möglich: beim nächsten Anzeigen neu laden.</summary>
        private void InvalidateSteamProgress()
        {
            progressLoaded = DateTime.MinValue;
            achievementCache.Clear();
        }
    }
}
