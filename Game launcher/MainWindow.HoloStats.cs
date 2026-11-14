using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Statistik ─────────────────────────────
        //
        // Profil-Kern (Gesamtspielzeit groß, Ringe für Woche/Ziel und Durchgespielt), Donut „Spielzeit pro Launcher“,
        // Aktivitäts-Raster der letzten 26 Wochen, Tagesbalken, Bestenliste mit Leuchtbalken, Erfolge als Sechseck-Abzeichen.

        private FrameworkElement BuildHoloStats()
        {
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Statistik"), Loc.T("Statistik"), Loc.T("Deine Spielzeit wird automatisch erfasst, sobald ein Spiel läuft."),
                HoloButton("Mein Spielejahr", () => ShowYearReview(DateTime.Now.Year), true, "")));

            if (SHide(settings.StreamHideStats))
            {
                page.Children.Add(HoloPanel(Loc.T("Streamer-Modus"), HoloHint("Der Streamer-Modus verbirgt gerade Spielzeiten und Statistiken.")));
                return page;
            }

            var games = allGames.Where(g => !IsTrackingIgnored(g)).ToList();

            // ── Kern + Launcher-Donut ──
            var left = new StackPanel();
            var right = new StackPanel();
            page.Children.Add(HoloColumns(left, right, 0.4));

            left.Children.Add(BuildHoloStatsCore(games));
            left.Children.Add(HoloPanel(Loc.T("Aktivität (letzte 26 Wochen)"), BuildHoloHeatmap()));
            left.Children.Add(HoloPanel(Loc.T("Meistgespielte Spiele"), BuildHoloLeaderboard(games)));

            right.Children.Add(HoloPanel(Loc.T("Spielzeit pro Launcher"), BuildHoloLauncherDonut(games)));
            right.Children.Add(HoloPanel(Loc.T("Letzte 7 Tage"), BuildHoloDays()));

            page.Children.Add(BuildHoloAchievementsPanel());
            return page;
        }

        // ───────── Kern ─────────

        private FrameworkElement BuildHoloStatsCore(List<GameItem> games)
        {
            long total = games.Sum(g => g.PlaySeconds);
            double week = GetWeekSeconds();
            int launches = VisibleGames.Sum(g => g.LaunchCount);
            int played = VisibleGames.Count(g => g.PlaySeconds >= 60);
            int done = VisibleGames.Count(g => g.Status == "done");
            int streak = GetPlayStreak();
            var top = games.OrderByDescending(g => g.PlaySeconds).FirstOrDefault(g => g.PlaySeconds >= 60);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            info.Children.Add(HoloLabel(Loc.T("Gesamtspielzeit")));
            var big = ConsoleTitle(FormatPlaytimeLong(total), 58);
            big.Margin = new Thickness(0, 2, 0, 12);
            info.Children.Add(big);

            var facts = new WrapPanel();
            facts.Children.Add(ConsoleChip(Loc.T("Starts"), launches.ToString("N0", Loc.Culture)));
            facts.Children.Add(ConsoleChip(Loc.T("Gespielt"), played.ToString(Loc.Culture) + " / " + VisibleGames.Count().ToString(Loc.Culture)));
            if (streak >= 1) facts.Children.Add(ConsoleChip(Loc.T("Serie"), "🔥 " + Loc.T($"{streak} Tage in Folge"), null, false, "#FDBA74"));
            if (top != null) facts.Children.Add(ConsoleChip(Loc.T("Meistgespielt"), top.Name, GetLauncherIcon(top.Source)));
            info.Children.Add(facts);
            body.Children.Add(info);

            var rings = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
            double goal = settings.WeeklyGoalHours;
            if (goal > 0)
                rings.Children.Add(HoloRing(week / (goal * 3600.0), Math.Round(Math.Min(week / (goal * 3600.0), 9.99) * 100).ToString(Loc.Culture) + "%", Loc.T("Wochenziel"), 136));
            else
                rings.Children.Add(HoloRing(0, week >= 60 ? FormatPlaytime((long)week) : "—", Loc.T("Diese Woche"), 136));
            var doneRing = HoloRing(VisibleGames.Any() ? done / (double)VisibleGames.Count() : 0, done.ToString(Loc.Culture), Loc.T("Durchgespielt"), 136, HoloCyan);
            doneRing.Margin = new Thickness(16, 0, 0, 0);
            rings.Children.Add(doneRing);
            Grid.SetColumn(rings, 1);
            body.Children.Add(rings);

            return HoloPanel(Loc.T("Profil"), body, null, 26);
        }

        // ───────── Aktivität: schräges Raster, eine Zelle pro Tag ─────────

        private FrameworkElement BuildHoloHeatmap()
        {
            const int weeks = 26;
            const double cell = 17, gap = 4;
            var accent = GetAccentColor();

            var today = DateTime.Today;
            int offset = ((int)today.DayOfWeek + 6) % 7;             // Montag = 0
            var start = today.AddDays(-offset - (weeks - 1) * 7);

            var canvas = new Canvas { Height = 7 * (cell + gap) + 22, Width = weeks * (cell + gap) + 40, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            int activeDays = 0;
            double totalSeconds = 0;
            int lastMonth = -1;

            for (int w = 0; w < weeks; w++)
            {
                for (int d = 0; d < 7; d++)
                {
                    var day = start.AddDays(w * 7 + d);
                    if (day > today) continue;
                    settings.PlayLog.TryGetValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out double seconds);
                    if (seconds >= 60) { activeDays++; totalSeconds += seconds; }

                    byte alpha = seconds < 60 ? (byte)0 : seconds < 1800 ? (byte)0x55 : seconds < 3600 ? (byte)0x88 : seconds < 7200 ? (byte)0xBB : (byte)0xFF;
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = cell,
                        Height = cell,
                        Fill = alpha == 0 ? MakeBrush("#141D30") : new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, accent.R, accent.G, accent.B)),
                        Stroke = day == today ? new SolidColorBrush(HoloCyan) : null,
                        StrokeThickness = 1.5,
                        RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                        RenderTransform = new SkewTransform(-12, 0),
                        ToolTip = day.ToString("ddd, d. MMM yyyy", Loc.Culture) + "\n" + (seconds >= 60 ? FormatPlaytime((long)seconds) : Loc.T("nicht gespielt"))
                    };
                    Canvas.SetLeft(rect, 34 + w * (cell + gap));
                    Canvas.SetTop(rect, 20 + d * (cell + gap));
                    if (holoAnimate)
                    {
                        rect.Opacity = 0;
                        rect.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(300 + w * 22 + d * 6) });
                    }
                    canvas.Children.Add(rect);

                    if (d == 0 && day.Month != lastMonth)
                    {
                        lastMonth = day.Month;
                        var month = new TextBlock { Text = day.ToString("MMM", Loc.Culture).TrimEnd('.').ToUpper(Loc.Culture), FontSize = 10, Foreground = ConsoleMuted };
                        month.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                        Canvas.SetLeft(month, 34 + w * (cell + gap));
                        canvas.Children.Add(month);
                    }
                }
            }
            for (int d = 0; d < 7; d += 2)
            {
                var name = new TextBlock { Text = start.AddDays(d).ToString("ddd", Loc.Culture).TrimEnd('.').ToUpper(Loc.Culture), FontSize = 10, Foreground = ConsoleMuted };
                name.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Canvas.SetLeft(name, 0);
                Canvas.SetTop(name, 20 + d * (cell + gap) + 1);
                canvas.Children.Add(name);
            }

            var panel = new StackPanel();
            // Wächst mit der Breite des Panels (bis zu einer sinnvollen Höhe)
            panel.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 250, HorizontalAlignment = System.Windows.HorizontalAlignment.Left });

            var legend = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            legend.Children.Add(ConsoleChip(Loc.T("Aktive Tage"), activeDays.ToString(Loc.Culture)));
            if (activeDays > 0) legend.Children.Add(ConsoleChip(Loc.T("Schnitt pro Tag"), FormatPlaytime((long)(totalSeconds / activeDays))));
            panel.Children.Add(legend);
            return panel;
        }

        // ───────── Bestenliste ─────────

        private FrameworkElement BuildHoloLeaderboard(List<GameItem> games)
        {
            var ranked = games.Where(g => g.PlaySeconds >= 60).OrderByDescending(g => g.PlaySeconds).Take(10).ToList();
            if (ranked.Count == 0) return HoloHint("Noch keine Spielzeit erfasst. Starte ein Spiel, der Launcher misst die Zeit automatisch.");

            long best = ranked[0].PlaySeconds;
            var list = new StackPanel();
            for (int i = 0; i < ranked.Count; i++)
            {
                var game = ranked[i];
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10), Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var rank = new TextBlock
                {
                    Text = (i + 1).ToString("00", Loc.Culture),
                    FontSize = i < 3 ? 24 : 18,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Foreground = i == 0 ? MakeBrush("#FACC15") : i < 3 ? System.Windows.Media.Brushes.White : ConsoleMuted
                };
                rank.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                row.Children.Add(rank);

                var thumb = new Border { Width = 34, Height = 48, Margin = new Thickness(0, 0, 14, 0), Background = MakeBrush("#141C2E"), Clip = ChamferGeometry(34, 48, 7) };
                if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage image) thumb.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                Grid.SetColumn(thumb, 1);
                row.Children.Add(thumb);

                var right = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = new TextBlock { Text = game.Name, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
                top.Children.Add(name);
                var time = new TextBlock { Text = FormatPlaytime(game.PlaySeconds), FontSize = 12.5, Foreground = ConsoleCyan, Margin = new Thickness(12, 0, 0, 0) };
                time.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(time, 1);
                top.Children.Add(time);
                right.Children.Add(top);

                double share = best > 0 ? game.PlaySeconds / (double)best : 0;
                var track = new Grid { Height = 6, Margin = new Thickness(0, 7, 0, 0) };
                track.Children.Add(new Border { Background = MakeBrush("#18223A"), RenderTransform = new SkewTransform(-20, 0) });
                var bar = new Border { HorizontalAlignment = System.Windows.HorizontalAlignment.Left, RenderTransformOrigin = new System.Windows.Point(0, 0.5) };
                bar.SetResourceReference(Border.BackgroundProperty, i == 0 ? "AccentBrush" : "AccentBrush");
                if (i > 0) bar.Opacity = 0.55 + 0.45 * share;
                if (!settings.PerformanceMode && i < 3) bar.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
                var scale = new ScaleTransform(1, 1);
                bar.RenderTransform = new TransformGroup { Children = { scale, new SkewTransform(-20, 0) } };
                track.SizeChanged += (s, e) => bar.Width = e.NewSize.Width * share;
                if (holoAnimate) scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(380 + i * 60), EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
                track.Children.Add(bar);
                right.Children.Add(track);
                Grid.SetColumn(right, 2);
                row.Children.Add(right);

                row.MouseEnter += (s, e) => name.Foreground = ConsoleCyan;
                row.MouseLeave += (s, e) => name.Foreground = System.Windows.Media.Brushes.White;
                row.MouseLeftButtonUp += (s, e) => ShowConsoleDetail(game);
                list.Children.Add(row);
            }
            return list;
        }

        // ───────── Donut: Spielzeit pro Launcher ─────────

        private FrameworkElement BuildHoloLauncherDonut(List<GameItem> games)
        {
            var parts = games.Where(g => g.PlaySeconds >= 60 && !string.IsNullOrEmpty(g.Source))
                .GroupBy(g => g.Source)
                .Select(group => (Source: group.Key, Seconds: (double)group.Sum(g => g.PlaySeconds)))
                .OrderByDescending(p => p.Seconds)
                .ToList();
            if (parts.Count == 0) return HoloHint("Noch keine Spielzeit erfasst. Starte ein Spiel, der Launcher misst die Zeit automatisch.");

            // Kleine Anteile zu „Andere“ zusammenfassen
            if (parts.Count > 6)
            {
                double rest = parts.Skip(5).Sum(p => p.Seconds);
                parts = parts.Take(5).ToList();
                parts.Add((Loc.T("Andere"), rest));
            }
            double total = parts.Sum(p => p.Seconds);

            const double size = 176, thickness = 20;
            double radius = size / 2 - thickness / 2 - 4;
            var donut = new Grid { Width = size, Height = size, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            donut.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(4), Stroke = MakeBrush("#141D30"), StrokeThickness = thickness });

            var colors = parts.Select((p, i) => p.Source == Loc.T("Andere") ? MakeColor("#64748B") : LauncherTint(p.Source)).ToList();
            double from = 0;
            var slices = new List<(System.Windows.Shapes.Path Path, double From, double To)>();
            for (int i = 0; i < parts.Count; i++)
            {
                double to = from + parts[i].Seconds / total;
                var path = new System.Windows.Shapes.Path { Stroke = new SolidColorBrush(colors[i]), StrokeThickness = thickness, IsHitTestVisible = true, ToolTip = Loc.T(parts[i].Source) + ": " + FormatPlaytime((long)parts[i].Seconds) };
                double gapFraction = parts.Count > 1 ? 0.006 : 0;
                slices.Add((path, from + gapFraction, Math.Max(from + gapFraction, to - gapFraction)));
                donut.Children.Add(path);
                from = to;
            }

            void Draw(double progress)
            {
                foreach (var (path, start, end) in slices)
                    path.Data = ArcSlice(size / 2, radius, start * progress, end * progress);
            }
            if (holoAnimate)
            {
                Draw(0);
                var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1000)) { BeginTime = TimeSpan.FromMilliseconds(350), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var clock = animation.CreateClock();
                clock.CurrentTimeInvalidated += (s, e) => { if (clock.CurrentProgress.HasValue) Draw(animation.GetCurrentValue(0.0, 1.0, clock)); };
            }
            else Draw(1);

            var center = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var count = ConsoleTitle(parts.Count(p => p.Source != Loc.T("Andere")).ToString(Loc.Culture), 34);
            count.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            center.Children.Add(count);
            var caption = HoloLabel(Loc.T("Launcher"));
            caption.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            caption.Foreground = ConsoleMuted;
            center.Children.Add(caption);
            donut.Children.Add(center);

            var body = new StackPanel();
            body.Children.Add(donut);
            var legend = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            for (int i = 0; i < parts.Count; i++)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Border { Width = 12, Height = 12, Background = new SolidColorBrush(colors[i]), Margin = new Thickness(0, 0, 10, 0), RenderTransform = new SkewTransform(-14, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center });
                var name = new TextBlock { Text = Loc.T(parts[i].Source), FontSize = 13.5, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var value = new TextBlock { Text = Math.Round(parts[i].Seconds / total * 100).ToString(Loc.Culture) + " %  ·  " + FormatPlaytime((long)parts[i].Seconds), FontSize = 12, Foreground = ConsoleMuted };
                value.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(value, 2);
                row.Children.Add(value);
                legend.Children.Add(row);
            }
            body.Children.Add(legend);
            return body;
        }

        // ───────── Letzte 7 Tage: hohe Balken mit Werten ─────────

        private FrameworkElement BuildHoloDays()
        {
            var body = new StackPanel();
            double week = GetWeekSeconds();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(ConsoleTitle(week >= 60 ? FormatPlaytime((long)week) : "—", 30));
            var avg = new TextBlock { Text = Loc.T("Schnitt pro Tag") + "  " + (week >= 60 ? FormatPlaytime((long)(week / 7)) : "—"), FontSize = 12, Foreground = ConsoleMuted, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            avg.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            Grid.SetColumn(avg, 1);
            head.Children.Add(avg);
            body.Children.Add(head);
            body.Children.Add(HoloWeekBars(150));
            return body;
        }

        // ───────── Erfolge: Sechseck-Abzeichen ─────────

        private FrameworkElement BuildHoloAchievementsPanel()
        {
            var defs = GetAchievementDefs();
            var rows = defs.Select(d =>
            {
                bool done = settings.Achievements.ContainsKey(d.Id);
                return (Def: d, Done: done, Percent: done ? 1.0 : Math.Clamp(d.Current / d.Target, 0, 1));
            })
            .OrderByDescending(r => r.Done).ThenByDescending(r => r.Percent).ToList();

            var wrap = new WrapPanel();
            foreach (var (def, done, percent) in rows) wrap.Children.Add(HoloBadge(def, done, percent));

            var body = new StackPanel();
            var head = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(ConsoleChip(Loc.T("Freigeschaltet"), rows.Count(r => r.Done).ToString(Loc.Culture) + " / " + rows.Count.ToString(Loc.Culture), null, true));
            body.Children.Add(head);
            body.Children.Add(wrap);
            return HoloPanel(Loc.T("Erfolge"), body);
        }

        private static Geometry HexGeometry(double size)
        {
            double r = size / 2, c = size / 2;
            var points = Enumerable.Range(0, 6).Select(i =>
            {
                double angle = Math.PI / 3 * i - Math.PI / 2;
                return new System.Windows.Point(c + r * Math.Cos(angle), c + r * Math.Sin(angle));
            }).ToList();
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            figure.Segments.Add(new PolyLineSegment(points.Skip(1), true));
            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            return geometry;
        }

        private FrameworkElement HoloBadge(AchievementDef def, bool done, double percent)
        {
            bool hidden = def.Secret && !done;
            var accent = GetAccentColor();
            const double size = 76;

            var hex = new Grid { Width = size, Height = size, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            var shape = new System.Windows.Shapes.Path
            {
                Data = HexGeometry(size - 4),
                Margin = new Thickness(2),
                StrokeThickness = done ? 2 : 1.2,
                Fill = done ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x38, accent.R, accent.G, accent.B)) : MakeBrush("#0F1626"),
                Stroke = done ? new SolidColorBrush(accent) : MakeBrush("#2C3A58")
            };
            if (done && !settings.PerformanceMode) shape.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.7 };
            hex.Children.Add(shape);

            // Fortschritt als Füllung von unten (nur offene Erfolge)
            if (!done && percent > 0 && !hidden)
            {
                var fill = new System.Windows.Shapes.Path { Data = HexGeometry(size - 4), Margin = new Thickness(2), Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, HoloCyan.R, HoloCyan.G, HoloCyan.B)) };
                fill.Clip = new RectangleGeometry(new Rect(0, (size - 4) * (1 - percent), size, size));
                hex.Children.Add(fill);
            }

            hex.Children.Add(new TextBlock
            {
                Text = hidden ? "❓" : def.Icon,
                FontSize = 28,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Opacity = done ? 1 : 0.45
            });

            var tile = new StackPanel { Width = 132, Margin = new Thickness(0, 0, 10, 16), ToolTip = hidden ? Loc.T("Geheim: Finde das passende Easter Egg.") : Loc.T(def.Description) };
            tile.Children.Add(hex);
            var title = new TextBlock
            {
                Text = hidden ? "???" : Loc.T(def.Title),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = done ? System.Windows.Media.Brushes.White : ConsoleMuted,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 8, 0, 0)
            };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            tile.Children.Add(title);
            var state = new TextBlock
            {
                Text = done ? Loc.T("Freigeschaltet") : def.Secret ? Loc.T("Geheim") : $"{Math.Min(def.Current, def.Target):F0} / {def.Target:F0}",
                FontSize = 10.5,
                Foreground = done ? ConsoleCyan : ConsoleMuted,
                TextAlignment = TextAlignment.Center
            };
            state.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            tile.Children.Add(state);
            return tile;
        }
    }
}
