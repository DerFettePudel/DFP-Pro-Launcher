using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Dashboard ─────────────────────────────
        //
        // Gleiche Daten wie das Dashboard aus Thema 1, aber komplett eigener Aufbau: Kommandozentrale mit Zahlen-Platten,
        // großem Fokus-Panel (zuletzt gespielt), Cover-Streifen und rechts einer Spalte aus Ringanzeigen und Listen.
        // Ausgeblendete Widgets (Dashboard anpassen in Thema 1) bleiben auch hier ausgeblendet.

        private FrameworkElement BuildHoloDashboard()
        {
            var page = new StackPanel();
            var games = VisibleGames.ToList();
            // Streamer-Modus: dieselben Kategorien wie im Dashboard von Thema 1 (dort abgedeckt, hier gar nicht erst gezeigt)
            bool hideStats = SHide(settings.StreamHideStats);
            bool hideRecent = SHide(settings.StreamHideRecent);
            bool hideLocation = SHide(settings.StreamHideLocation);
            bool hidePaths = SHide(settings.StreamHidePaths);
            bool hideShots = SHide(settings.StreamHideShots);
            bool hideName = SHide(settings.StreamHideName);

            // ── Kopf ──
            var now = DateTime.Now;
            string code = now.ToString("dddd", Loc.Culture) + "  ·  " + now.ToString("M", Loc.Culture) + "  ·  " + Loc.T($"KW {System.Globalization.ISOWeek.GetWeekOfYear(now)}");
            var actions = new List<FrameworkElement>();
            if (!DashOff("actions"))
            {
                actions.Add(HoloButton("Zufallsspiel", ShowRoulette, true, ""));
                actions.Add(HoloButton("Spiel hinzufügen", () => BtnAddGame_Click(this, new RoutedEventArgs()), false, ""));
                actions.Add(HoloButton("Neu scannen", () => BtnRescanGames_Click(this, new RoutedEventArgs()), false, ""));
            }
            page.Children.Add(HoloHeader(code, GreetingLine(), null, actions.ToArray()));

            // ── Zahlen-Platten ──
            if (!DashOff("stats") && !hideStats)
            {
                var stats = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(-6, 0, 4, 4) };
                stats.Children.Add(HoloStat(Loc.T("Spiele"), games.Count.ToString(Loc.Culture), OpenConsoleLibraryOverview, games.Count));
                int favorites = games.Count(g => g.IsFavorite);
                stats.Children.Add(HoloStat(Loc.T("Favoriten"), favorites.ToString(Loc.Culture), null, favorites));
                stats.Children.Add(HoloStat(Loc.T("Gesamtspielzeit"), FormatPlaytime(games.Where(g => !IsTrackingIgnored(g)).Sum(g => g.PlaySeconds)), () => NavigateTo("stats")));
                int updates = games.Count(g => g.UpdatePending);
                stats.Children.Add(HoloStat(Loc.T("Updates bereit"), updates.ToString(Loc.Culture), null, updates));
                page.Children.Add(stats);
            }

            var left = new StackPanel();
            var right = new StackPanel();
            page.Children.Add(HoloColumns(left, right));

            var played = games.Where(g => g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).ToList();
            var favoriteList = games.Where(g => g.IsFavorite).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            // ── Links: Fokus, Spiel des Tages, Cover-Streifen ──
            var focus = played.FirstOrDefault() ?? favoriteList.FirstOrDefault();
            if (!DashOff("hero") && focus != null && !hideRecent) left.Children.Add(BuildHoloFocus(focus));
            if (!DashOff("daily")) left.Children.Add(BuildHoloDaily());
            if (!DashOff("recent") && !hideRecent)
                left.Children.Add(HoloPanel(Loc.T("Zuletzt gespielt"), HoloCoverStrip(played.Take(Math.Max(settings.RecentCount, 6)).ToList(), "Noch nichts gespielt."), OpenConsoleLibraryOverview));
            if (!DashOff("favorites") && favoriteList.Count > 0 && !hideRecent)
                left.Children.Add(HoloPanel(Loc.T("Favoriten"), HoloCoverStrip(favoriteList.Take(Math.Max(settings.RecentCount, 6)).ToList(), null)));
            if (!DashOff("quick") && !hidePaths) left.Children.Add(BuildHoloQuick());

            // ── Rechts: Zeit und Wetter, Woche, Backlog, Updates, Wunschliste, Steam ──
            if (!DashOff("clock")) right.Children.Add(BuildHoloClock(hideLocation));
            if (!hideStats && !DashOff("goal")) right.Children.Add(BuildHoloWeek());
            var pending = games.Where(g => g.UpdatePending).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (pending.Count > 0)
            {
                var list = new StackPanel();
                foreach (var game in pending.Take(8)) list.Children.Add(HoloGameRow(game, Loc.T("Update"), "#22D3EE"));
                if (pending.Count > 8) list.Children.Add(new TextBlock { Text = "+ " + (pending.Count - 8).ToString(Loc.Culture) + " …", Foreground = ConsoleMuted });
                right.Children.Add(HoloPanel(Loc.T("Updates bereit") + "  " + pending.Count.ToString(Loc.Culture), list));
            }
            if (!DashOff("backlog")) right.Children.Add(BuildHoloBacklog(games));
            if (!DashOff("wishlist") && !hideShots) right.Children.Add(BuildHoloWish());
            if (epicFree.Count > 0)
            {
                var list = new StackPanel();
                foreach (var deal in epicFree.Take(3))
                {
                    var line = new TextBlock { Text = "▸  " + deal.Title, FontSize = 14, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 5) };
                    list.Children.Add(line);
                }
                right.Children.Add(HoloPanel(Loc.T("Gratis bei Epic Games"), list, () => NavigateTo("deals")));
            }
            if (!DashOff("friends") && !hideName) right.Children.Add(BuildHoloFriends());
            if (!DashOff("achievements")) right.Children.Add(BuildHoloAchievements());

            return page;
        }

        // ───────── Fokus: zuletzt gespieltes Spiel ─────────

        private FrameworkElement BuildHoloFocus(GameItem game)
        {
            var body = new Grid { Height = 270 };

            // Breites Bild rechts, nach links in die Glasfläche ausgeblendet
            BitmapSource? wide = GetHeroImage(game);
            BitmapSource? picture = wide ?? (HasCover(game) ? GetCachedImage(EffectiveCover(game)) : null);
            if (picture != null)
            {
                var image = new System.Windows.Controls.Image
                {
                    Source = picture,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new Thickness(-22, -16, -22, -20),
                    IsHitTestVisible = false
                };
                RenderOptions.SetBitmapScalingMode(image, settings.PerformanceMode ? BitmapScalingMode.LowQuality : BitmapScalingMode.HighQuality);
                var mask = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
                mask.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0, 0, 0), 0.05));
                mask.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x70, 0, 0, 0), 0.45));
                mask.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xFF, 0, 0, 0), 0.8));
                image.OpacityMask = mask;
                body.Children.Add(image);

                // Waagerechte Scanlinien über dem Bild
                var lines = new DrawingBrush(new GeometryDrawing(MakeBrush("#22000000"), null, new RectangleGeometry(new Rect(0, 0, 4, 1))))
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 4, 3),
                    ViewportUnits = BrushMappingMode.Absolute
                };
                body.Children.Add(new Border { Background = lines, Margin = image.Margin, IsHitTestVisible = false });
            }

            var info = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, MaxWidth = 560, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            info.Children.Add(HoloLabel(game.LastPlayed.HasValue ? Loc.T("Weiterspielen") : Loc.T("Favoriten")));
            var title = ConsoleTitle(game.Name, 42);
            title.TextWrapping = TextWrapping.Wrap;
            title.MaxHeight = 96;
            title.Margin = new Thickness(0, 4, 0, 10);
            info.Children.Add(title);

            var chips = new WrapPanel();
            FillConsoleChips(chips, game);
            info.Children.Add(chips);

            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, -10) };
            buttons.Children.Add(HoloButton("Spielen", () => LaunchGame(game), true, ""));
            buttons.Children.Add(HoloButton("Details", () => ShowConsoleDetail(game), false, ""));
            info.Children.Add(buttons);
            body.Children.Add(info);

            return HoloPanel(Loc.T("Fokus"), body, null, 26);
        }

        // ───────── Spiel des Tages ─────────

        private FrameworkElement BuildHoloDaily()
        {
            var game = PickDailyGame(out string reason);
            if (SHide(settings.StreamHideRecent)) reason = string.Empty;   // Grund verrät, wann zuletzt gespielt wurde
            if (game == null) return HoloPanel(Loc.T("Spiel des Tages"), HoloHint("Sobald Spiele in deiner Bibliothek sind, schlägt dir der Launcher hier jeden Tag eines vor."));

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(HoloCover(game, 96, 144));

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var name = ConsoleTitle(game.Name, 26);
            name.TextWrapping = TextWrapping.Wrap;
            texts.Children.Add(name);
            texts.Children.Add(new TextBlock { Text = reason, FontSize = 14, Foreground = ConsoleMuted, Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap });
            var chips = new WrapPanel();
            chips.Children.Add(ConsoleChip(null, Loc.T(game.Source), GetLauncherIcon(game.Source)));
            texts.Children.Add(chips);

            var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, -10) };
            buttons.Children.Add(HoloButton("Spielen", () => LaunchGame(game), true, ""));
            buttons.Children.Add(HoloButton("Anderer Vorschlag", () => { dailyOffset++; RefreshHoloPage(); }, false, ""));
            texts.Children.Add(buttons);

            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            return HoloPanel(Loc.T("Spiel des Tages"), row);
        }

        // ───────── Cover-Streifen ─────────

        private FrameworkElement HoloCoverStrip(List<GameItem> list, string? empty)
        {
            if (list.Count == 0) return HoloHint(empty ?? string.Empty);

            // So viele Cover wie in die Breite passen (kein waagerechtes Scrollen)
            var strip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            foreach (var game in list) strip.Children.Add(HoloCover(game, 118, 177));
            var host = new Grid { ClipToBounds = true, Height = 190 };
            host.Children.Add(strip);
            host.SizeChanged += (s, e) =>
            {
                double x = 0;
                foreach (FrameworkElement cover in strip.Children)
                {
                    x += 132;
                    cover.Visibility = x - 14 <= e.NewSize.Width + 0.5 ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            return host;
        }

        // ───────── Uhr und Wetter ─────────

        private FrameworkElement BuildHoloClock(bool hideLocation)
        {
            var body = new StackPanel();
            if (settings.ShowClock)
            {
                holoClock = ConsoleTitle(ConsoleTimeText(), 60);
                body.Children.Add(holoClock);
            }

            // Streamer-Modus „Standort verbergen“: Wetter nennt den Ort, darum ganz weglassen
            if (hideLocation) body.Children.Add(HoloHint("Wetter im Streamer-Modus ausgeblendet (Standort)."));
            else if (settings.ShowWeather && TxtWeatherTemp != null && TxtWeatherTemp.Text.Length > 0)
            {
                var weather = new Grid { Margin = new Thickness(0, settings.ShowClock ? 10 : 0, 0, 0) };
                weather.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                weather.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var icon = new TextBlock { Text = TxtWeatherIcon.Text, FontSize = 34, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                weather.Children.Add(icon);
                var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var temp = ConsoleTitle(TxtWeatherTemp.Text, 26);
                texts.Children.Add(temp);
                texts.Children.Add(new TextBlock { Text = TxtWeatherDesc.Text, FontSize = 13, Foreground = System.Windows.Media.Brushes.White, TextWrapping = TextWrapping.Wrap });
                if (TxtWeatherRange.Text.Length > 0)
                    texts.Children.Add(new TextBlock { Text = TxtWeatherRange.Text, FontSize = 12, Foreground = ConsoleMuted, Margin = new Thickness(0, 2, 0, 0) });
                Grid.SetColumn(texts, 1);
                weather.Children.Add(texts);
                body.Children.Add(weather);
            }

            if (body.Children.Count == 0) body.Children.Add(HoloHint("Uhr und Wetter sind in den Einstellungen ausgeschaltet."));
            return HoloPanel(Loc.T("Zeit und Wetter"), body);
        }

        // ───────── Woche: Ring fürs Wochenziel, Serie, Balken ─────────

        private FrameworkElement BuildHoloWeek()
        {
            double week = GetWeekSeconds();
            double goal = settings.WeeklyGoalHours;
            var body = new StackPanel();

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (goal > 0)
            {
                double fraction = week / (goal * 3600.0);
                top.Children.Add(HoloRing(fraction, Math.Round(Math.Min(fraction, 9.99) * 100).ToString(Loc.Culture) + "%", Loc.T("Wochenziel"), 128));
            }
            else
            {
                top.Children.Add(HoloRing(0, "—", Loc.T("Wochenziel"), 128));
            }

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
            texts.Children.Add(HoloLabel(Loc.T("Diese Woche")));
            texts.Children.Add(ConsoleTitle(week >= 60 ? FormatPlaytime((long)week) : "—", 30));
            if (goal > 0)
                texts.Children.Add(new TextBlock { Text = Loc.T($"Ziel {goal} Std."), FontSize = 13, Foreground = ConsoleMuted });
            else
                texts.Children.Add(HoloHint("Lege in den Einstellungen unter „Spiele und Cover“ ein Wochenziel fest."));
            int streak = GetPlayStreak();
            if (streak >= 2)
                texts.Children.Add(new TextBlock { Text = "🔥 " + Loc.T($"{streak} Tage in Folge"), FontSize = 13, Foreground = MakeBrush("#FDBA74"), Margin = new Thickness(0, 6, 0, 0) });
            Grid.SetColumn(texts, 1);
            top.Children.Add(texts);
            body.Children.Add(top);

            var bars = HoloWeekBars();
            ((FrameworkElement)bars).Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(bars);
            return HoloPanel(Loc.T("Woche"), body, () => NavigateTo("stats"));
        }

        // ───────── Backlog: Ring (erledigt) und die nächsten Spiele ─────────

        private FrameworkElement BuildHoloBacklog(List<GameItem> games)
        {
            var backlog = games.Where(g => g.Status == "backlog").OrderByDescending(g => g.FirstSeen ?? DateTime.MinValue).ToList();
            int done = games.Count(g => g.Status == "done");
            int playing = games.Count(g => g.Status == "playing");
            int total = backlog.Count + done + playing;

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.Children.Add(HoloRing(total > 0 ? done / (double)total : 0, done.ToString(Loc.Culture) + "/" + total.ToString(Loc.Culture), Loc.T("Durchgespielt"), 104, HoloCyan, 8));

            var list = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            if (backlog.Count == 0) list.Children.Add(HoloHint("Dein Backlog ist leer. Markiere Spiele per Rechtsklick mit „Will ich spielen“."));
            foreach (var game in backlog.Take(4)) list.Children.Add(HoloGameRow(game));
            Grid.SetColumn(list, 1);
            body.Children.Add(list);
            return HoloPanel(Loc.T("Als Nächstes spielen"), body, () => NavigateTo("backlog"));
        }

        // ───────── Wunschliste ─────────

        private FrameworkElement BuildHoloWish()
        {
            var body = new StackPanel();
            var items = settings.Wishlist.OrderByDescending(WishTargetReached).ThenByDescending(w => w.Discount).Take(4).ToList();
            if (items.Count == 0) body.Children.Add(HoloHint("Noch keine Spiele auf der Wunschliste."));

            foreach (var item in items)
            {
                bool reached = WishTargetReached(item);
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = (reached ? "🔔 " : string.Empty) + item.Name, FontSize = 14, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = System.Windows.VerticalAlignment.Center });

                if (item.Discount > 0)
                {
                    var discount = ConsoleChip(null, "-" + item.Discount.ToString(Loc.Culture) + " %", null, true);
                    ((FrameworkElement)discount).Margin = new Thickness(10, 0, 0, 0);
                    Grid.SetColumn(discount, 1);
                    row.Children.Add(discount);
                }
                var price = new TextBlock
                {
                    Text = string.IsNullOrEmpty(item.PriceText) ? "–" : Loc.T(item.PriceText),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Foreground = reached ? MakeBrush("#34D399") : ConsoleMuted
                };
                price.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(price, 2);
                row.Children.Add(price);
                body.Children.Add(row);
            }
            return HoloPanel(Loc.T("Wunschliste"), body, () => NavigateTo("wishlist"));
        }

        // ───────── Schnellzugriff: Symbol-Kacheln ─────────

        private FrameworkElement BuildHoloQuick()
        {
            SeedQuickLinks();
            var body = new WrapPanel();
            foreach (var link in settings.QuickLinks.Take(12))
            {
                var item = link;
                var tile = new StackPanel { Width = 92, Margin = new Thickness(0, 0, 10, 10), Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent, ToolTip = DescribeTarget(item.Target) };

                var plate = new Grid { Width = 58, Height = 58, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                var shape = new System.Windows.Shapes.Path { Data = ChamferGeometry(58, 58, 12), Fill = MakeBrush("#B3101A2E"), Stroke = MakeBrush("#4422D3EE"), StrokeThickness = 1 };
                plate.Children.Add(shape);
                if (ResolveQuickIcon(item) is ImageSource source)
                {
                    var image = new System.Windows.Controls.Image { Source = source, Width = 30, Height = 30 };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    plate.Children.Add(image);
                }
                else plate.Children.Add(new TextBlock { Text = QuickEmoji(item), FontSize = 24, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                tile.Children.Add(plate);

                var name = new TextBlock { Text = item.Name, FontSize = 12, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                tile.Children.Add(name);

                tile.MouseEnter += (s, e) => { shape.Stroke = new SolidColorBrush(HoloCyan); name.Foreground = ConsoleCyan; };
                tile.MouseLeave += (s, e) => { shape.Stroke = MakeBrush("#4422D3EE"); name.Foreground = System.Windows.Media.Brushes.White; };
                tile.MouseLeftButtonUp += (s, e) => OpenShell(item.Target);
                body.Children.Add(tile);
            }
            if (settings.QuickLinks.Count == 0) body.Children.Add(HoloHint("Noch keine Verknüpfungen."));
            return HoloPanel(Loc.T("Schnellzugriff"), body, () => NavigateTo("links"));
        }

        // ───────── Steam-Freunde ─────────

        private FrameworkElement BuildHoloFriends()
        {
            var body = new StackPanel();
            if (!SteamApiReady(out _)) body.Children.Add(HoloHint(SteamApiHint()));
            else
            {
                if ((DateTime.Now - friendsLoaded).TotalMinutes >= 2) _ = LoadFriendsAsync(false);
                if (friendsError.Length > 0) body.Children.Add(HoloHint(friendsError));
                else if (friendsData == null) body.Children.Add(HoloHint("Freunde werden geladen ..."));
                else
                {
                    var online = friendsData.Where(f => f.State != 0)
                        .OrderByDescending(f => f.GameName.Length > 0).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    if (online.Count == 0) body.Children.Add(HoloHint("Gerade ist keiner deiner Steam-Freunde online."));
                    foreach (var friend in online.Take(6)) body.Children.Add(FriendRow(friend));
                    if (online.Count > 6) body.Children.Add(HoloHint($"+ {online.Count - 6} weitere online"));
                }
            }
            return HoloPanel(Loc.T("Steam-Freunde"), body);
        }

        // ───────── Erfolge: kleine Ringe pro Spiel ─────────

        private FrameworkElement BuildHoloAchievements()
        {
            var body = new StackPanel();
            if (!SteamApiReady(out _)) body.Children.Add(HoloHint(SteamApiHint()));
            else
            {
                if ((DateTime.Now - progressLoaded).TotalMinutes >= 30) _ = LoadProgressAsync();
                if (progressError.Length > 0) body.Children.Add(HoloHint(progressError));
                else if (progressData == null) body.Children.Add(HoloHint("Erfolge werden geladen ..."));
                else
                {
                    var close = progressData.Where(p => p.Unlocked > 0 && p.Unlocked < p.Total)
                        .OrderByDescending(p => p.Unlocked / (double)p.Total).ThenBy(p => p.Total - p.Unlocked).Take(3).ToList();
                    if (close.Count == 0) body.Children.Add(HoloHint("Spiel ein paar Steam-Spiele mit Erfolgen, dann erscheint hier, was dir noch fehlt."));

                    var rings = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
                    foreach (var p in close)
                    {
                        var cell = new StackPanel { Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
                        double f = p.Unlocked / (double)p.Total;
                        var ring = HoloRing(f, Math.Round(f * 100).ToString(Loc.Culture) + "%", $"{p.Unlocked}/{p.Total}", 92, MakeColor("#FACC15"), 7);
                        ring.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                        cell.Children.Add(ring);
                        cell.Children.Add(new TextBlock { Text = p.Game.Name, FontSize = 12.5, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
                        var game = p.Game;
                        cell.MouseLeftButtonUp += (s, e) => ShowConsoleDetail(game);
                        rings.Children.Add(cell);
                    }
                    if (close.Count > 0) body.Children.Add(rings);
                }
            }
            return HoloPanel(Loc.T("Fast geschafft"), body);
        }

        private static System.Windows.Media.Color MakeColor(string hex) => ((SolidColorBrush)MakeBrush(hex)).Color;
    }
}
