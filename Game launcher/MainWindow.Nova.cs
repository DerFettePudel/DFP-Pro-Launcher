using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Game_launcher
{
    /// <summary>
    /// Design „Nova“ (Overhaul November 2026): Suche in der Seitenleiste, Farbleuchten im Hintergrund,
    /// Tastenkürzel-Übersicht (F1) und das Dashboard-Widget „Spiel des Tages“.
    /// </summary>
    public partial class MainWindow
    {
        // ───────────────────────────── Suche in der Seitenleiste ─────────────────────────────

        private void SidebarSearch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenSpotlight();

        // ───────────────────────────── Farbleuchten im Hintergrund ─────────────────────────────

        /// <summary>Zwei ruhige, unbewegte Lichtflecken (Akzentfarbe oben rechts, kühlerer Ton unten links). Kostet praktisch keine Leistung.</summary>
        private void ApplyAmbientGlow()
        {
            if (AmbientGlowLayer == null) return;
            if (!settings.AmbientGlow || settings.OledMode)
            {
                AmbientGlowLayer.Child = null;
                AmbientGlowLayer.Visibility = Visibility.Collapsed;
                return;
            }

            var accent = ParseColor(settings.AccentColor, "#8B5CF6");
            var cool = System.Windows.Media.Color.FromRgb(0x22, 0xD3, 0xEE);
            static byte Mix(byte a, byte b) => (byte)((a + b) / 2);
            var second = System.Windows.Media.Color.FromRgb(Mix(accent.R, cool.R), Mix(accent.G, cool.G), Mix(accent.B, cool.B));

            // Mit Hintergrundbild etwas zurückhaltender, damit das Bild wirkt
            byte strength = HasBackgroundImage() ? (byte)0x18 : (byte)0x2C;

            static RadialGradientBrush Spot(System.Windows.Media.Color color, byte alpha, double x, double y, double rx, double ry)
            {
                var brush = new RadialGradientBrush
                {
                    Center = new System.Windows.Point(x, y),
                    GradientOrigin = new System.Windows.Point(x, y),
                    RadiusX = rx,
                    RadiusY = ry
                };
                brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B), 0));
                brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb((byte)(alpha / 3), color.R, color.G, color.B), 0.45));
                brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, color.R, color.G, color.B), 1));
                brush.Freeze();
                return brush;
            }

            var layers = new Grid();
            layers.Children.Add(new Border { Background = Spot(accent, strength, 0.92, 0.0, 0.55, 0.65) });
            layers.Children.Add(new Border { Background = Spot(second, (byte)(strength * 2 / 3), 0.12, 1.0, 0.5, 0.6) });
            AmbientGlowLayer.Child = layers;
            AmbientGlowLayer.Visibility = Visibility.Visible;
        }

        // ───────────────────────────── Tastenkürzel-Übersicht (F1) ─────────────────────────────

        private Grid? shortcutsLayer;

        private bool ShortcutsOpen => shortcutsLayer != null && shortcutsLayer.Visibility == Visibility.Visible;

        /// <summary>F1 öffnet/schließt die Übersicht, Esc schließt sie. Gibt true zurück, wenn die Taste verbraucht wurde.</summary>
        private bool HandleShortcutsOverlayKey(System.Windows.Input.KeyEventArgs e)
        {
            if (controllerMode) return false;
            if (e.Key == System.Windows.Input.Key.F1)
            {
                ToggleShortcutsOverlay();
                e.Handled = true;
                return true;
            }
            if (ShortcutsOpen && e.Key == System.Windows.Input.Key.Escape)
            {
                CloseShortcutsOverlay();
                e.Handled = true;
                return true;
            }
            return false;
        }

        private void ToggleShortcutsOverlay()
        {
            if (ShortcutsOpen) CloseShortcutsOverlay();
            else OpenShortcutsOverlay();
        }

        private void CloseShortcutsOverlay()
        {
            if (shortcutsLayer == null) return;
            var layer = shortcutsLayer;
            if (!settings.PageAnimations)
            {
                layer.Visibility = Visibility.Collapsed;
                return;
            }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
            fade.Completed += (s, e) => layer.Visibility = Visibility.Collapsed;
            layer.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void OpenShortcutsOverlay()
        {
            // Bei jedem Öffnen neu aufbauen: So passt die Sprache immer
            if (shortcutsLayer != null) WindowRoot.Children.Remove(shortcutsLayer);

            var layer = new Grid { Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB8, 0x05, 0x06, 0x0A)) };
            System.Windows.Controls.Panel.SetZIndex(layer, 900);
            layer.MouseLeftButtonUp += (s, e) =>
            {
                if (ReferenceEquals(e.OriginalSource, layer)) CloseShortcutsOverlay();
            };

            var card = new Border
            {
                MaxWidth = 820,
                Margin = new Thickness(40),
                Padding = new Thickness(30, 26, 30, 24),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            if (TryFindResource("CardStyle") is Style cardStyle) card.Style = cardStyle;
            card.CornerRadius = new CornerRadius(22);
            card.Padding = new Thickness(30, 26, 30, 24);
            if (!settings.PerformanceMode)
                card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Opacity = 0.6, Color = Colors.Black };

            var stack = new StackPanel();

            // Kopf: Symbol, Titel, Untertitel
            var head = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var iconTile = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 16, 0) };
            iconTile.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            var keyboard = new TextBlock { Text = "", FontSize = 22, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            keyboard.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            keyboard.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            iconTile.Child = keyboard;
            head.Children.Add(iconTile);

            var titles = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(titles, 1);
            var title = new TextBlock { Text = Loc.T("Tastenkürzel"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            titles.Children.Add(title);
            var subtitle = new TextBlock { Text = Loc.T("Alles schneller erreichen, ganz ohne Maus."), FontSize = 13, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            titles.Children.Add(subtitle);
            head.Children.Add(titles);
            stack.Children.Add(head);

            var rows = new (string[] Keys, string Text)[]
            {
                (new[] { "Strg", "K" }, "Suche öffnen (Spiele, Apps, Befehle)"),
                (new[] { "Strg", "F" }, "In der Spiele-Bibliothek suchen"),
                (new[] { "Strg", "R" }, "Spiele neu scannen"),
                (new[] { "Strg", "," }, "Einstellungen öffnen"),
                (new[] { "Strg", "1 – 9" }, "Bereiche der Seitenleiste direkt öffnen"),
                (new[] { "Strg", "Umschalt", "S" }, "Streamer-Modus ein oder aus"),
                (new[] { "F11" }, "Controller-Modus ein oder aus"),
                (new[] { "F1" }, "Diese Übersicht ein- oder ausblenden"),
                (new[] { "← →", "Enter" }, "Cover-Ansicht: blättern und Spiel starten"),
                (new[] { "Esc" }, "Suche oder Übersicht schließen")
            };

            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            foreach (var row in rows) grid.Children.Add(ShortcutRow(row.Keys, row.Text));
            stack.Children.Add(grid);

            // Fuß: Über den Launcher, Schließen
            var foot = new Grid { Margin = new Thickness(0, 20, 0, 0) };
            var hint = new TextBlock { Text = Loc.T("Tipp: In der Suche führt ein „/“ zu versteckten Befehlen."), FontSize = 12, VerticalAlignment = System.Windows.VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 240, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            foot.Children.Add(hint);
            var footButtons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var about = new System.Windows.Controls.Button { Content = Loc.T("Über den Launcher"), Margin = new Thickness(0, 0, 8, 0) };
            about.Click += (s, e) => { CloseShortcutsOverlay(); ShowAbout(); };
            var close = new System.Windows.Controls.Button { Content = Loc.T("Schließen"), Padding = new Thickness(20, 8, 20, 8) };
            close.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            close.Click += (s, e) => CloseShortcutsOverlay();
            footButtons.Children.Add(about);
            footButtons.Children.Add(close);
            foot.Children.Add(footButtons);
            stack.Children.Add(foot);

            card.Child = stack;
            layer.Children.Add(card);
            WindowRoot.Children.Add(layer);
            shortcutsLayer = layer;

            if (settings.PageAnimations)
            {
                layer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
                var scale = new ScaleTransform(0.96, 0.96);
                card.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                card.RenderTransform = scale;
                var grow = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
        }

        private FrameworkElement ShortcutRow(string[] keys, string text)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 18, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var caps = new WrapPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            foreach (string key in keys)
            {
                var cap = new Border { Margin = new Thickness(0, 0, 5, 0) };
                if (TryFindResource("KeyCap") is Style capStyle) cap.Style = capStyle;
                var label = new TextBlock { Text = Loc.T(key), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White };
                cap.Child = label;
                caps.Children.Add(cap);
            }
            row.Children.Add(caps);

            var description = new TextBlock { Text = Loc.T(text), FontSize = 13, VerticalAlignment = System.Windows.VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(description, 1);
            row.Children.Add(description);

            var shell = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 6), Child = row };
            shell.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            return shell;
        }

        // ───────────────────────────── Dashboard-Widget „Spiel des Tages“ ─────────────────────────────

        private StackPanel? dailyWidget;
        private int dailyOffset;   // „Anderer Vorschlag“ blättert weiter (nur bis zum Neustart)

        private void RerollDailyPick()
        {
            dailyOffset++;
            FillDailyWidget(true);
        }

        /// <summary>
        /// Wählt für heute ein Spiel aus der Bibliothek: bevorzugt Backlog, lange nicht oder nie gespielte Spiele und Favoriten.
        /// Der Vorschlag bleibt den ganzen Tag gleich (Datum als Startwert), „Anderer Vorschlag“ geht zum nächsten.
        /// </summary>
        private GameItem? PickDailyGame(out string reason)
        {
            reason = string.Empty;
            var now = DateTime.Now;
            var ranked = VisibleGames
                .Where(g => g.Status != "done" && !activeSessions.ContainsKey(g))
                .Select(g =>
                {
                    int score = 0;
                    if (g.Status == "backlog") score += 4;
                    if (!g.LastPlayed.HasValue) score += 2;
                    else if ((now - g.LastPlayed.Value).TotalDays > 30) score += 3;
                    else if ((now - g.LastPlayed.Value).TotalDays < 3) score -= 4;
                    if (g.IsFavorite) score += 1;
                    if (g.Rating >= 4) score += 1;
                    return (Game: g, Score: score);
                })
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Game.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(15)
                .ToList();
            if (ranked.Count == 0) return null;

            int seed = DateTime.Today.Year * 400 + DateTime.Today.DayOfYear;
            int index = (int)(((long)seed * 7919 + dailyOffset) % ranked.Count);
            var game = ranked[index].Game;

            if (game.Status == "backlog") reason = Loc.T("Steht auf deiner Backlog-Liste");
            else if (!game.LastPlayed.HasValue) reason = Loc.T("Noch nie über den Launcher gestartet");
            else if ((now - game.LastPlayed.Value).TotalDays > 30) reason = Loc.T($"Seit {(int)(now - game.LastPlayed.Value).TotalDays} Tagen nicht gespielt");
            else if (game.IsFavorite) reason = Loc.T("Einer deiner Favoriten");
            else reason = Loc.T("Mal wieder Lust darauf?");
            return game;
        }

        private void FillDailyWidget(bool animate = false)
        {
            if (dailyWidget == null) return;
            dailyWidget.Children.Clear();
            if (DashHidden("daily"))
            {
                dailyWidget.Visibility = Visibility.Collapsed;
                return;
            }

            var game = PickDailyGame(out string reason);
            UIElement body;
            if (game == null)
            {
                body = WidgetHint("Sobald Spiele in deiner Bibliothek sind, schlägt dir der Launcher hier jeden Tag eines vor.");
            }
            else
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var thumb = new Border
                {
                    Width = 64,
                    Height = 96,
                    CornerRadius = new CornerRadius(10),
                    Background = BrushFooter,
                    Margin = new Thickness(0, 0, 16, 0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage image)
                    thumb.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                else
                    thumb.Child = new TextBlock
                    {
                        Text = game.Name.Length > 0 ? game.Name.Substring(0, 1).ToUpperInvariant() : "?",
                        FontSize = 28,
                        FontWeight = FontWeights.Bold,
                        Foreground = BrushPlaceholder,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };
                var picked = game;
                thumb.MouseLeftButtonUp += (s, e) => ShowGameDetails(picked);
                row.Children.Add(thumb);

                var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
                Grid.SetColumn(texts, 1);
                var name = new TextBlock
                {
                    Text = game.Name,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                texts.Children.Add(name);
                var why = new TextBlock { Text = reason, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
                why.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                texts.Children.Add(why);
                var source = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                source.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                source.Child = new TextBlock { Text = game.Source, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White };
                texts.Children.Add(source);
                row.Children.Add(texts);

                var buttons = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
                Grid.SetColumn(buttons, 2);
                var play = new System.Windows.Controls.Button { Content = Loc.T("▶  Spielen"), Padding = new Thickness(22, 10, 22, 10), Margin = new Thickness(0, 0, 0, 8) };
                play.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentGradientBrush");
                play.Click += (s, e) => LaunchGame(picked);
                var other = new System.Windows.Controls.Button { Content = Loc.T("Anderer Vorschlag"), Padding = new Thickness(14, 8, 14, 8) };
                other.Click += (s, e) => RerollDailyPick();
                buttons.Children.Add(play);
                buttons.Children.Add(other);
                row.Children.Add(buttons);
                body = row;
            }

            var card = WidgetCard("✨  Spiel des Tages", body);
            dailyWidget.Children.Add(card);
            dailyWidget.Visibility = Visibility.Visible;

            if (animate && settings.PageAnimations && body is FrameworkElement content)
                content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        }
    }
}
