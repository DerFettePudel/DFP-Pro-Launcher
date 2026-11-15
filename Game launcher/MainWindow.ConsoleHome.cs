using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Infotafel auf START, Launcher-Präsentation in der BIBLIOTHEK ─────────────────────────────

        // ───────── Bausteine ─────────

        /// <summary>Feine Neonlinie (Akzent → Cyan → durchsichtig).</summary>
        private Border ConsoleRule()
        {
            var accent = GetAccentColor();
            var fade = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
            fade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x99, accent.R, accent.G, accent.B), 0));
            fade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x55, HoloCyan.R, HoloCyan.G, HoloCyan.B), 0.5));
            fade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, HoloCyan.R, HoloCyan.G, HoloCyan.B), 1));
            return new Border { Height = 1, Background = fade, IsHitTestVisible = false };
        }

        /// <summary>Kleiner schräger Info-Chip: optional Logo, Beschriftung (Cyan, Monospace) und Wert.</summary>
        private FrameworkElement ConsoleChip(string? caption, string value, ImageSource? icon = null, bool accent = false, string? valueColor = null)
        {
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(14, 6, 14, 6), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            if (icon != null)
            {
                var image = new System.Windows.Controls.Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                row.Children.Add(image);
            }
            if (!string.IsNullOrEmpty(caption))
            {
                var label = new TextBlock { Text = caption.ToUpper(Loc.Culture), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = ConsoleCyan, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                row.Children.Add(label);
            }
            var text = new TextBlock
            {
                Text = value,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = accent ? MakeBrush(InkFor(GetAccentColor())) : valueColor != null ? MakeBrush(valueColor) : System.Windows.Media.Brushes.White
            };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            row.Children.Add(text);

            var plate = new Border
            {
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1),
                Background = MakeBrush("#A60E1320"),
                BorderBrush = MakeBrush("#3A4766"),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new SkewTransform(-10, 0)
            };
            if (accent)
            {
                plate.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                plate.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                if (!settings.PerformanceMode) plate.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
            }

            var chip = new Grid { Margin = new Thickness(0, 0, 10, 8) };
            chip.Children.Add(plate);
            chip.Children.Add(row);
            return chip;
        }

        /// <summary>Infos zum gewählten Spiel auf START als Chips.</summary>
        private void FillConsoleChips(WrapPanel host, GameItem game)
        {
            host.Children.Clear();
            if (!string.IsNullOrEmpty(game.Source)) host.Children.Add(ConsoleChip(null, Loc.T(game.Source), GetLauncherIcon(game.Source)));
            if (game.UpdatePending) host.Children.Add(ConsoleChip(null, Loc.T("Update verfügbar"), null, true));
            if (game.PlaySeconds >= 60 && !SHide(settings.StreamHideStats)) host.Children.Add(ConsoleChip(Loc.T("Spielzeit"), FormatPlaytime(game.PlaySeconds)));
            if (!SHide(settings.StreamHideRecent))
                host.Children.Add(ConsoleChip(Loc.T("zuletzt gespielt"), game.LastPlayed.HasValue ? Loc.T(FormatRelative(game.LastPlayed.Value)) : Loc.T("nie")));
            if (KnownGameSize(game) is long bytes && bytes > 0) host.Children.Add(ConsoleChip(Loc.T("Größe"), FormatBytes(bytes)));
            if (game.Rating > 0) host.Children.Add(ConsoleChip(null, new string('★', Math.Clamp(game.Rating, 1, 5)), null, false, "#FACC15"));
        }

        /// <summary>Karte der Infotafel: Überschrift in Cyan, kleine Akzent-Ecke oben links, optional anklickbar.</summary>
        private FrameworkElement ConsoleHudCard(string title, UIElement content, Action? click = null)
        {
            var stack = new StackPanel();
            var head = ConsoleEyebrow("// " + title.ToUpper(Loc.Culture));
            head.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(head);
            stack.Children.Add(content);

            var card = new Border
            {
                Child = stack,
                Padding = new Thickness(18, 14, 18, 16),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Background = MakeBrush("#B80B0F18")
            };
            card.SetResourceReference(Border.BorderBrushProperty, "CardEdgeBrush");

            var host = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            host.Children.Add(card);
            foreach (var (width, height) in new[] { (26.0, 2.0), (2.0, 12.0) })
            {
                var corner = new Border { Width = width, Height = height, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = System.Windows.VerticalAlignment.Top, IsHitTestVisible = false };
                corner.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                host.Children.Add(corner);
            }

            if (click != null)
            {
                card.Cursor = System.Windows.Input.Cursors.Hand;
                card.MouseEnter += (s, e) => card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                card.MouseLeave += (s, e) => card.SetResourceReference(Border.BorderBrushProperty, "CardEdgeBrush");
                card.MouseLeftButtonUp += (s, e) => click();
            }
            return host;
        }

        // ───────── START: Infotafel ─────────

        private FrameworkElement BuildConsoleHud()
        {
            var hud = new StackPanel { Width = 340, Margin = new Thickness(0, ConsoleTopInset + 26, 64, 16), VerticalAlignment = System.Windows.VerticalAlignment.Top };

            // Ohne Kopfleiste (Navigation links/rechts) steht die Uhr hier groß
            if (!IsTopNav)
            {
                consoleHudClock = ConsoleTitle(ConsoleTimeText(), 58);
                consoleHudClock.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                hud.Children.Add(consoleHudClock);
            }

            var greeting = new TextBlock
            {
                Text = GreetingLine(),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            greeting.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            if (settings.CyberGreeting)
            {
                // Status nach Tageszeit (SYSTEM ONLINE …) über der Begrüßung
                var status = ConsoleEyebrow("// " + CyberStatusLine());
                status.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                status.Margin = new Thickness(0, 0, 0, 4);
                hud.Children.Add(status);
            }
            hud.Children.Add(greeting);

            var now = DateTime.Now;
            var date = ConsoleEyebrow(now.ToString("dddd", Loc.Culture).ToUpper(Loc.Culture) + "  ·  " + now.ToString("M", Loc.Culture).ToUpper(Loc.Culture));
            date.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            date.Margin = new Thickness(0, 4, 0, 16);
            hud.Children.Add(date);

            if (!SHide(settings.StreamHideStats)) hud.Children.Add(ConsoleHudCard(Loc.T("Diese Woche"), BuildConsoleWeek()));
            hud.Children.Add(ConsoleHudCard(Loc.T("Bibliothek"), BuildConsoleLibraryStats()));

            var updates = VisibleGames.Where(g => g.UpdatePending).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (updates.Count > 0)
                hud.Children.Add(ConsoleHudCard(Loc.T("Updates bereit") + "   " + updates.Count.ToString(Loc.Culture), BuildConsoleUpdates(updates)));

            // Gratis-Spiele bei Epic (wie das Banner im Dashboard von Thema 1)
            if (epicFree.Count > 0)
            {
                var list = new StackPanel();
                foreach (var deal in epicFree.Take(3))
                {
                    list.Children.Add(new TextBlock
                    {
                        Text = "▸  " + deal.Title,
                        FontSize = 14,
                        Foreground = System.Windows.Media.Brushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                }
                hud.Children.Add(ConsoleHudCard(Loc.T("Gratis bei Epic Games"), list, () => ShowConsolePage("deals")));
            }
            return hud;
        }

        /// <summary>Karten, die nicht mehr ganz auf die Infotafel passen, ausblenden statt abschneiden.</summary>
        private static void FitConsoleHud(FrameworkElement hud, Grid layout)
        {
            if (hud is not StackPanel panel || layout.RowDefinitions.Count < 2) return;
            double available = layout.RowDefinitions[0].ActualHeight + layout.RowDefinitions[1].ActualHeight - panel.Margin.Top - panel.Margin.Bottom;
            double used = 0;
            foreach (UIElement child in panel.Children)
            {
                child.Visibility = Visibility.Visible;
                child.Measure(new System.Windows.Size(panel.Width, double.PositiveInfinity));
                used += child.DesiredSize.Height;
                if (used > available) child.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Spielzeit dieser Woche: Summe, Serie, Balken der letzten 7 Tage, Wochenziel.</summary>
        private FrameworkElement BuildConsoleWeek()
        {
            var panel = new StackPanel();
            double week = GetWeekSeconds();

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(ConsoleTitle(week >= 60 ? FormatPlaytime((long)week) : "—", 30));
            int streak = GetPlayStreak();
            if (streak >= 2)
            {
                var fire = new TextBlock { Text = "🔥 " + Loc.T($"{streak} Tage in Folge"), FontSize = 12.5, Foreground = MakeBrush("#FDBA74"), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                Grid.SetColumn(fire, 1);
                top.Children.Add(fire);
            }
            panel.Children.Add(top);

            // Balken: letzte 7 Tage, heute in der Akzentfarbe
            var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i - 6)).ToList();
            var values = days.Select(d => settings.PlayLog.TryGetValue(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), out double s) ? s : 0).ToList();
            double max = Math.Max(values.Max(), 1800);

            var bars = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            for (int i = 0; i < 7; i++)
            {
                bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bool today = i == 6;
                var column = new StackPanel { ToolTip = days[i].ToString("dddd", Loc.Culture) + ": " + (values[i] >= 60 ? FormatPlaytime((long)values[i]) : "—") };
                var slot = new Grid { Height = 52 };
                var bar = new Border { Height = Math.Max(3, 52 * values[i] / max), VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 0), CornerRadius = new CornerRadius(1) };
                if (today)
                {
                    bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    if (!settings.PerformanceMode) bar.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
                }
                else bar.Background = values[i] > 0 ? MakeBrush("#9922D3EE") : MakeBrush("#263049");
                slot.Children.Add(bar);
                column.Children.Add(slot);

                string day = days[i].ToString("ddd", Loc.Culture).TrimEnd('.');
                var label = new TextBlock
                {
                    Text = (day.Length > 2 ? day[..2] : day).ToUpper(Loc.Culture),
                    FontSize = 10,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 0),
                    Foreground = today ? System.Windows.Media.Brushes.White : ConsoleMuted
                };
                label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                column.Children.Add(label);
                Grid.SetColumn(column, i);
                bars.Children.Add(column);
            }
            panel.Children.Add(bars);

            if (settings.WeeklyGoalHours > 0)
            {
                double percent = Math.Min(100, week / (settings.WeeklyGoalHours * 3600.0) * 100);
                var goal = new Grid { Margin = new Thickness(0, 14, 0, 6) };
                goal.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                goal.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                goal.Children.Add(new TextBlock { Text = Loc.T("Wochenziel"), FontSize = 12.5, Foreground = ConsoleMuted });
                var number = new TextBlock { Text = Math.Round(percent).ToString(Loc.Culture) + " %", FontSize = 12.5, Foreground = System.Windows.Media.Brushes.White };
                number.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(number, 1);
                goal.Children.Add(number);
                panel.Children.Add(goal);

                var progress = new System.Windows.Controls.ProgressBar { Value = percent };
                progress.SetResourceReference(StyleProperty, "SlimProgress");
                panel.Children.Add(progress);
            }
            return panel;
        }

        /// <summary>Zahlen zur Bibliothek und bereitstehende Updates.</summary>
        private FrameworkElement BuildConsoleLibraryStats()
        {
            var games = VisibleGames.ToList();
            var panel = new StackPanel();

            var numbers = new Grid();
            var cells = new[]
            {
                (Loc.T("Spiele"), games.Count),
                (Loc.T("Favoriten"), games.Count(g => g.IsFavorite)),
                (Loc.T("Ungespielt"), games.Count(g => g.PlaySeconds < 60 && g.LaunchCount == 0))
            };
            for (int i = 0; i < cells.Length; i++)
            {
                numbers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var cell = new StackPanel();
                cell.Children.Add(ConsoleTitle(cells[i].Item2.ToString(Loc.Culture), 26));
                var caption = new TextBlock { Text = cells[i].Item1.ToUpper(Loc.Culture), FontSize = 10, Foreground = ConsoleMuted, Margin = new Thickness(0, 2, 0, 0) };
                caption.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                cell.Children.Add(caption);
                Grid.SetColumn(cell, i);
                numbers.Children.Add(cell);
            }
            panel.Children.Add(numbers);
            return panel;
        }

        /// <summary>Spiele, für die ein Update bereitsteht: jedes einzeln, ein Klick öffnet das Spiel.</summary>
        private FrameworkElement BuildConsoleUpdates(List<GameItem> updates)
        {
            var list = new StackPanel();
            foreach (var game in updates.Take(8))
            {
                var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                var icon = GetLauncherIcon(game.Source);
                if (icon != null)
                {
                    var image = new System.Windows.Controls.Image { Source = icon, Width = 15, Height = 15, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    row.Children.Add(image);
                }
                var name = new TextBlock { Text = game.Name, FontSize = 14, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                row.Children.Add(name);

                var item = new Border { Child = row, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(-8, 0, -8, 1), CornerRadius = new CornerRadius(3), Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
                item.MouseEnter += (s, e) => { item.Background = MakeBrush("#1A22D3EE"); name.Foreground = ConsoleCyan; };
                item.MouseLeave += (s, e) => { item.Background = System.Windows.Media.Brushes.Transparent; name.Foreground = System.Windows.Media.Brushes.White; };
                item.MouseLeftButtonUp += (s, e) => ShowConsoleDetail(game);
                list.Children.Add(item);
            }
            if (updates.Count > 8)
                list.Children.Add(new TextBlock { Text = "…", FontSize = 14, Foreground = ConsoleMuted, Margin = new Thickness(0, 2, 0, 0) });
            return list;
        }

        // ───────── BIBLIOTHEK: Launcher-Logos und Präsentation ─────────

        private string? consolePresentedLauncher;
        private Grid? consolePresentOverlay;
        private ScrollViewer? consolePresentScroll;
        private DateTime consolePresentingUntil = DateTime.MinValue;

        /// <summary>BIBLIOTHEK über den Reiter: immer die Übersicht (eine offene Launcher-Präsentation wird geschlossen).</summary>
        private void OpenConsoleLibraryOverview()
        {
            consolePresentedLauncher = null;
            ShowConsoleLibrary();
        }

        /// <summary>Logo des Launchers in beliebiger Größe; dunkle Web-Logos bekommen eine helle Kachel, ohne Logo ein Symbol.</summary>
        private FrameworkElement LauncherLogoElement(string source, double size)
        {
            var icon = size > 40 ? GetLauncherLogoLarge(source) : GetLauncherIcon(source);
            if (icon == null)
            {
                var glyph = new TextBlock
                {
                    Text = source switch { "Xbox" => "", FolderSource => "", "Manuell" => "", _ => "" },
                    FontSize = size * 0.8,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                return new Grid { Width = size, Height = size, Children = { glyph } };
            }

            var image = new System.Windows.Controls.Image { Source = icon, Width = size, Height = size };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            if (webLogos.Contains(source) && DarkLogos.Contains(source))
                return new Border { Child = image, Background = MakeBrush("#E8FFFFFF"), CornerRadius = new CornerRadius(size * 0.18), Padding = new Thickness(size * 0.08) };
            return image;
        }

        private FrameworkElement ConsoleLauncherTile(string source, int count)
        {
            var tint = LauncherTint(source);
            var stack = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var logo = LauncherLogoElement(source, 52);
            logo.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            stack.Children.Add(logo);

            var name = new TextBlock { Text = Loc.T(source), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 12, 0, 2) };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            stack.Children.Add(name);
            var number = new TextBlock { Text = GamesText(count).ToUpper(Loc.Culture), FontSize = 10.5, Foreground = ConsoleCyan, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            number.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            stack.Children.Add(number);

            var fill = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x2E, tint.R, tint.G, tint.B), 0));
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xE6, 0x0B, 0x0F, 0x18), 0.7));

            var lift = new TranslateTransform();
            var tile = new Border
            {
                Width = 168,
                Height = 150,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Background = fill,
                BorderBrush = MakeBrush("#263049"),
                Cursor = System.Windows.Input.Cursors.Hand,
                RenderTransform = lift,
                Child = stack
            };
            var tintBrush = new SolidColorBrush(tint);
            tile.MouseEnter += (s, e) =>
            {
                tile.BorderBrush = tintBrush;
                if (!settings.PerformanceMode) tile.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.6, Color = tint };
                if (settings.HoverAnimations) lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            };
            tile.MouseLeave += (s, e) =>
            {
                tile.BorderBrush = MakeBrush("#263049");
                tile.Effect = null;
                lift.BeginAnimation(TranslateTransform.YProperty, settings.HoverAnimations ? new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) : null);
            };
            tile.MouseLeftButtonUp += (s, e) => PresentConsoleLauncher(source, true);
            return tile;
        }

        /// <summary>
        /// Präsentiert die Spiele eines Launchers: Logo und Name erscheinen groß mit Glitch und Lichtschein in der Farbe des Launchers,
        /// eine Scanlinie läuft über den Bildschirm, dann fliegen die Spiele nacheinander ein. Zurück: Esc, Maus-Zurück oder ZURÜCK.
        /// </summary>
        private void PresentConsoleLauncher(string source, bool animate)
        {
            if (consoleView == null) return;
            if (consolePresentOverlay != null) consoleView.Children.Remove(consolePresentOverlay);
            consolePresentedLauncher = source;

            bool motion = animate && settings.PageAnimations && !settings.PerformanceMode;
            var tint = LauncherTint(source);
            var games = VisibleGames.Where(g => string.Equals(g.Source, source, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var overlay = new Grid { Background = new SolidColorBrush(ConsoleBase), ClipToBounds = true };
            var glow = new RadialGradientBrush { Center = new System.Windows.Point(0.15, 0), GradientOrigin = new System.Windows.Point(0.15, 0), RadiusX = 0.8, RadiusY = 0.9 };
            glow.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x40, tint.R, tint.G, tint.B), 0));
            glow.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, tint.R, tint.G, tint.B), 1));
            overlay.Children.Add(new Border { Background = glow, IsHitTestVisible = false });

            var content = new Grid { Margin = new Thickness(64, ConsoleTopInset + 20, 64, 0) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Kopf: Logo, Name, Anzahl, Zurück
            var headMove = new TranslateTransform();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 10), RenderTransform = headMove };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var smallLogo = LauncherLogoElement(source, 64);
            smallLogo.Margin = new Thickness(0, 0, 22, 0);
            smallLogo.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            if (!settings.PerformanceMode) smallLogo.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.7, Color = tint };
            head.Children.Add(smallLogo);
            var titles = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            titles.Children.Add(ConsoleEyebrow("// " + Loc.T("Launcher").ToUpper(Loc.Culture) + "   " + GamesText(games.Count).ToUpper(Loc.Culture)));
            titles.Children.Add(ConsoleTitle(Loc.T(source), 52));
            Grid.SetColumn(titles, 1);
            head.Children.Add(titles);
            var back = ConsoleButton("←  " + Loc.T("Zurück").ToUpper(Loc.Culture), false, CloseConsoleLauncher);
            back.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            Grid.SetColumn(back, 2);
            head.Children.Add(back);
            content.Children.Add(head);

            // Spiele: fliegen nacheinander von unten ein
            var wrap = new WrapPanel();
            for (int i = 0; i < games.Count; i++)
            {
                var move = new TranslateTransform(0, motion ? 46 : 0);
                var holder = new Border { Child = ConsoleLibraryTile(games[i], new Thickness(0)), Margin = new Thickness(0, 0, 18, 24), RenderTransform = move, Opacity = motion ? 0 : 1 };
                if (motion)
                {
                    var start = TimeSpan.FromMilliseconds(820 + Math.Min(i, 36) * 28);
                    holder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = start });
                    move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(46, 0, TimeSpan.FromMilliseconds(460))
                    {
                        BeginTime = start,
                        EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
                    });
                }
                wrap.Children.Add(holder);
            }
            consolePresentScroll = new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 22, 0, 40) };
            Grid.SetRow(consolePresentScroll, 1);
            content.Children.Add(consolePresentScroll);
            overlay.Children.Add(content);

            if (motion)
            {
                consolePresentingUntil = DateTime.Now.AddMilliseconds(1900);
                overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

                head.Opacity = 0;
                head.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = TimeSpan.FromMilliseconds(760) });
                headMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-18, 0, TimeSpan.FromMilliseconds(380))
                {
                    BeginTime = TimeSpan.FromMilliseconds(760),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

                overlay.Children.Add(BuildLauncherIntro(source, games.Count, tint));
                overlay.Children.Add(BuildScanLine(tint));
            }
            else consolePresentingUntil = DateTime.MinValue;

            consolePresentOverlay = overlay;
            consoleView.Children.Add(overlay);
        }

        /// <summary>Großes Logo und Name in der Mitte: wächst herein, glitcht kurz, Name tippt sich ein, dann gleitet alles nach oben weg.</summary>
        private FrameworkElement BuildLauncherIntro(string source, int count, System.Windows.Media.Color tint)
        {
            var scale = new ScaleTransform(0.55, 0.55);
            var move = new TranslateTransform();
            var intro = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                IsHitTestVisible = false,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { scale, move } },
                Opacity = 0
            };

            var logo = LauncherLogoElement(source, 150);
            logo.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            logo.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = 0.85, Color = tint };
            intro.Children.Add(logo);

            // Name mit Farbversatz (Cyan/Magenta), der beim Glitch aufblitzt
            string text = Loc.T(source).ToUpper(Loc.Culture);
            var nameHost = new Grid { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
            TextBlock Layer(string hex, double opacity)
            {
                var layer = new TextBlock { Text = text, FontSize = 64, FontWeight = FontWeights.Bold, Foreground = MakeBrush(hex), Opacity = opacity, RenderTransform = new TranslateTransform() };
                layer.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                return layer;
            }
            var cyan = Layer("#22D3EE", 0);
            var magenta = Layer("#FF2BD6", 0);
            var main = Layer("#FFFFFF", 1);
            main.Text = string.Empty;
            main.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.6, Color = tint };
            nameHost.Children.Add(cyan);
            nameHost.Children.Add(magenta);
            nameHost.Children.Add(main);
            // Breite vom vollen Namen, damit der Text beim Eintippen nicht hin und her springt
            nameHost.Children.Add(Layer("#FFFFFF", 0));
            intro.Children.Add(nameHost);

            var sub = ConsoleEyebrow("// " + GamesText(count).ToUpper(Loc.Culture));
            sub.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            sub.FontSize = 16;
            sub.Margin = new Thickness(0, 10, 0, 0);
            intro.Children.Add(sub);

            DoubleAnimationUsingKeyFrames Frames(params (double Ms, double Value, bool Ease)[] frames)
            {
                var animation = new DoubleAnimationUsingKeyFrames();
                foreach (var (ms, value, ease) in frames)
                {
                    var time = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms));
                    animation.KeyFrames.Add(ease
                        ? new EasingDoubleKeyFrame(value, time, new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut })
                        : new LinearDoubleKeyFrame(value, time));
                }
                return animation;
            }

            intro.BeginAnimation(UIElement.OpacityProperty, Frames((0, 0, false), (200, 1, false), (700, 1, false), (960, 0, false)));
            var size = Frames((0, 0.55, false), (420, 1, true), (700, 1, false), (960, 0.82, false));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, size);
            move.BeginAnimation(TranslateTransform.YProperty, Frames((0, 0, false), (700, 0, false), (960, -70, false)));
            move.BeginAnimation(TranslateTransform.XProperty, Steps((0, 0), (260, 10), (300, -7), (350, 4), (400, 0)));

            foreach (var (layer, sign) in new[] { (cyan, -1.0), (magenta, 1.0) })
            {
                layer.BeginAnimation(UIElement.OpacityProperty, Steps((0, 0), (260, 0.85), (330, 0.2), (380, 0.75), (460, 0)));
                ((TranslateTransform)layer.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Steps((0, 0), (260, sign * 7), (330, -sign * 4), (380, sign * 9), (460, 0)));
            }

            // Name tippt sich ein
            Tween(0, text.Length, 360, value => main.Text = text[..Math.Clamp((int)Math.Round(value), 0, text.Length)], () => main.Text = text);
            return intro;
        }

        /// <summary>Eine leuchtende Linie in der Launcher-Farbe läuft einmal von oben nach unten.</summary>
        private FrameworkElement BuildScanLine(System.Windows.Media.Color tint)
        {
            var line = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
            line.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, tint.R, tint.G, tint.B), 0));
            line.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xDD, tint.R, tint.G, tint.B), 0.5));
            line.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, tint.R, tint.G, tint.B), 1));

            var move = new TranslateTransform();
            var scan = new Border
            {
                Height = 2,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Background = line,
                IsHitTestVisible = false,
                RenderTransform = move,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9, Color = tint }
            };
            double height = consoleView?.ActualHeight > 0 ? consoleView.ActualHeight : 1200;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, height, TimeSpan.FromMilliseconds(760)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            scan.BeginAnimation(UIElement.OpacityProperty, Steps((0, 1), (760, 0)));
            return scan;
        }

        private void CloseConsoleLauncher()
        {
            consolePresentedLauncher = null;
            consolePresentingUntil = DateTime.MinValue;
            var overlay = consolePresentOverlay;
            consolePresentOverlay = null;
            consolePresentScroll = null;
            if (overlay == null || consoleView == null) return;

            if (!settings.PageAnimations || settings.PerformanceMode)
            {
                consoleView.Children.Remove(overlay);
                return;
            }
            overlay.IsHitTestVisible = false;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (s, e) => consoleView?.Children.Remove(overlay);
            overlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
