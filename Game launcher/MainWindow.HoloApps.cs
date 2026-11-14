using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Anwendungen ─────────────────────────────
        //
        // Links eine Kategorie-Schiene, oben die Favoriten als Dock mit großen Symbolen, darunter alle Apps als schmale
        // Modul-Kacheln. Suche und Filter bauen nur das Raster neu (das Suchfeld behält den Fokus).

        private string holoAppFilter = "all";
        private string holoAppSearch = string.Empty;
        private WrapPanel? holoAppGrid;
        private TextBlock? holoAppCount;
        private bool holoAppsRequested;

        private FrameworkElement BuildHoloApps()
        {
            // Nur einmal anstoßen (meldet sich über RenderApps zurück; bei einem Fehler keine Endlosschleife)
            if (!appsDiscovered && !appsLoading && !holoAppsRequested)
            {
                holoAppsRequested = true;
                _ = DiscoverAppsAsync(false);
            }
            var all = cachedApps.Count > 0 ? cachedApps : BuildAppList();
            if (holoAppFilter != "all" && holoAppFilter != "fav" && !all.Any(a => a.Category == holoAppFilter)) holoAppFilter = "all";

            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Anwendungen"), Loc.T("Anwendungen"), TxtAppsSubtitle?.Text,
                HoloButton("App hinzufügen", () => BtnAddApp_Click(this, new RoutedEventArgs()), true, ""),
                HoloButton("Neu laden", () => BtnRefreshApps_Click(this, new RoutedEventArgs()), false, "")));

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(layout);

            // ── Schiene ──
            var rail = new StackPanel();
            void Rail(string key, string label, int count, string glyph)
                => rail.Children.Add(HoloRailItem(label, count.ToString(Loc.Culture), holoAppFilter == key, () => { holoAppFilter = key; RefreshHoloPage(); }, glyph));
            Rail("all", Loc.T("Alle"), all.Count, "");
            Rail("fav", Loc.T("Favoriten"), all.Count(a => a.IsFavorite), "");
            foreach (string category in AppCategories)
            {
                int count = all.Count(a => a.Category == category);
                if (count > 0) Rail(category, Loc.T(category), count, CategoryGlyph(category));
            }
            layout.Children.Add(HoloPanel(Loc.T("Kategorien"), rail));

            // ── Inhalt ──
            var content = new StackPanel();
            Grid.SetColumn(content, 1);
            layout.Children.Add(content);

            var favorites = all.Where(a => a.IsFavorite).ToList();
            if (favorites.Count > 0 && holoAppFilter == "all" && holoAppSearch.Length == 0)
            {
                var dock = new WrapPanel();
                foreach (var app in favorites) dock.Children.Add(HoloAppDockItem(app));
                content.Children.Add(HoloPanel(Loc.T("Favoriten"), dock));
            }

            var body = new StackPanel();
            var tools = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tools.Children.Add(HoloSearchBox("App suchen ...", holoAppSearch, text => { holoAppSearch = text; RenderHoloAppGrid(all); }));
            holoAppCount = new TextBlock { FontSize = 12, Foreground = ConsoleMuted, VerticalAlignment = System.Windows.VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 10) };
            holoAppCount.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            Grid.SetColumn(holoAppCount, 1);
            tools.Children.Add(holoAppCount);
            body.Children.Add(tools);

            holoAppGrid = new WrapPanel();
            HoloFitWrap(holoAppGrid, 262 + 12);
            body.Children.Add(holoAppGrid);
            RenderHoloAppGrid(all);

            string title = holoAppFilter switch { "all" => Loc.T("Alle Anwendungen"), "fav" => Loc.T("Favoriten"), _ => Loc.T(holoAppFilter) };
            content.Children.Add(HoloPanel(title, body));
            return page;
        }

        private static string CategoryGlyph(string category) => category switch
        {
            "Gaming" => "",
            "Chat" => "",
            "Browser" => "",
            "Medien" => "",
            "Office" => "",
            "Tools" => "",
            _ => ""
        };

        private void RenderHoloAppGrid(List<AppEntry> all)
        {
            if (holoAppGrid == null) return;
            holoAppGrid.Children.Clear();

            IEnumerable<AppEntry> query = all;
            if (holoAppFilter == "fav") query = query.Where(a => a.IsFavorite);
            else if (holoAppFilter != "all") query = query.Where(a => a.Category == holoAppFilter);
            if (holoAppSearch.Length > 0) query = query.Where(a => a.Name.Contains(holoAppSearch, StringComparison.CurrentCultureIgnoreCase));
            var shown = query.ToList();

            foreach (var app in shown) holoAppGrid.Children.Add(HoloAppModule(app));
            if (holoAppCount != null) holoAppCount.Text = shown.Count.ToString(Loc.Culture) + " / " + all.Count.ToString(Loc.Culture);

            if (shown.Count == 0)
            {
                string hint = appsLoading && all.Count == 0 ? "Anwendungen werden gesucht ..."
                    : all.Count == 0 ? "Keine Apps erkannt. Füge deine Programme mit „App hinzufügen“ selbst hinzu."
                    : "Keine Treffer.";
                holoAppGrid.Children.Add(HoloHint(hint));
            }
        }

        private System.Windows.Controls.ContextMenu HoloAppMenu(AppEntry app)
        {
            var menu = CreateMenu();
            AddMenuItem(menu, "▶  Starten", () => LaunchApp(app));
            AddMenuItem(menu, app.IsFavorite ? "☆  Aus Favoriten entfernen" : "★  Zu Favoriten hinzufügen", () => ToggleAppFavorite(app));
            if (app.ExePath.Length > 0 && File.Exists(app.ExePath))
            {
                string? folder = System.IO.Path.GetDirectoryName(app.ExePath);
                if (!string.IsNullOrEmpty(folder)) AddMenuItem(menu, "📂  Dateispeicherort öffnen", () => OpenShell(folder));
            }
            if (app.IsManual) AddMenuItem(menu, "🗑  Aus Liste entfernen", () => RemoveManualApp(app));
            return menu;
        }

        private FrameworkElement HoloAppIcon(AppEntry app, double size)
        {
            if (app.Icon != null)
            {
                var image = new System.Windows.Controls.Image { Source = app.Icon, Width = size, Height = size, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                return image;
            }
            var glyph = new TextBlock { Text = CategoryGlyph(app.Category), FontSize = size * 0.7, Foreground = ConsoleCyan, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            return glyph;
        }

        /// <summary>Schmale Modul-Kachel: Symbol in abgeschrägtem Fach, Name, Kategorie, Stern.</summary>
        private FrameworkElement HoloAppModule(AppEntry app)
        {
            const double width = 262, height = 74;
            var shape = new System.Windows.Shapes.Path { Data = ChamferGeometry(width, height, 12), Fill = MakeBrush("#B30D1424"), Stroke = MakeBrush("#3322D3EE"), StrokeThickness = 1 };

            var row = new Grid { Margin = new Thickness(12, 0, 14, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bay = new Grid { Width = 48, Height = 48, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            bay.Children.Add(new System.Windows.Shapes.Path { Data = ChamferGeometry(48, 48, 9), Fill = MakeBrush("#1A2338"), Stroke = MakeBrush("#2A3756"), StrokeThickness = 1 });
            var icon = HoloAppIcon(app, 30);
            bay.Children.Add(icon);
            row.Children.Add(bay);

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var name = new TextBlock { Text = app.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            texts.Children.Add(name);
            var category = HoloLabel(Loc.T(app.IsManual ? "Eigene App" : app.Category), 10);
            category.Foreground = ConsoleMuted;
            category.Margin = new Thickness(0, 3, 0, 0);
            texts.Children.Add(category);
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);

            if (app.IsFavorite)
            {
                var star = new TextBlock { Text = "★", FontSize = 14, Foreground = MakeBrush("#FACC15"), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                Grid.SetColumn(star, 2);
                row.Children.Add(star);
            }

            var tile = new Grid { Height = height, Margin = new Thickness(0, 0, 12, 12), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = app.ExePath.Length > 0 && !SHide(settings.StreamHidePaths) ? app.ExePath : app.Name, ContextMenu = HoloAppMenu(app) };
            tile.Children.Add(HoloShapeLayer(shape));
            tile.Children.Add(row);
            tile.SizeChanged += (s, e) => shape.Data = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, 12);

            var lift = new TranslateTransform();
            tile.RenderTransform = lift;
            tile.MouseEnter += (s, e) =>
            {
                shape.Stroke = HoloEdgeBrush(0xFF);
                shape.Fill = MakeBrush("#D9121C33");
                name.Foreground = ConsoleCyan;
                if (settings.HoverAnimations) lift.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(-3, TimeSpan.FromMilliseconds(140)));
            };
            tile.MouseLeave += (s, e) =>
            {
                shape.Stroke = MakeBrush("#3322D3EE");
                shape.Fill = MakeBrush("#B30D1424");
                name.Foreground = System.Windows.Media.Brushes.White;
                lift.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            };
            tile.MouseLeftButtonUp += (s, e) => LaunchApp(app);
            return tile;
        }

        /// <summary>Favorit im Dock: großes Symbol in einem Sechseck, Name darunter.</summary>
        private FrameworkElement HoloAppDockItem(AppEntry app)
        {
            const double size = 84;
            var hex = new Grid { Width = size, Height = size, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            var shape = new System.Windows.Shapes.Path { Data = HexGeometry(size - 4), Margin = new Thickness(2), Fill = MakeBrush("#B3101A2E"), Stroke = MakeBrush("#5522D3EE"), StrokeThickness = 1.4 };
            hex.Children.Add(shape);
            hex.Children.Add(HoloAppIcon(app, 40));

            var name = new TextBlock { Text = app.Name, FontSize = 12.5, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            var item = new StackPanel { Width = 110, Margin = new Thickness(0, 0, 8, 6), Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent, ContextMenu = HoloAppMenu(app) };
            item.Children.Add(hex);
            item.Children.Add(name);

            var accent = GetAccentColor();
            item.MouseEnter += (s, e) =>
            {
                shape.Stroke = new SolidColorBrush(accent);
                shape.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, accent.R, accent.G, accent.B));
                if (!settings.PerformanceMode) shape.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.7 };
                name.Foreground = ConsoleCyan;
            };
            item.MouseLeave += (s, e) =>
            {
                shape.Stroke = MakeBrush("#5522D3EE");
                shape.Fill = MakeBrush("#B3101A2E");
                shape.Effect = null;
                name.Foreground = System.Windows.Media.Brushes.White;
            };
            item.MouseLeftButtonUp += (s, e) => LaunchApp(app);
            return item;
        }
    }
}
