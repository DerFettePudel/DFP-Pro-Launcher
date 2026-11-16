using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Bibliothek (Raster) und Spiel-Ansicht ─────────────────────────────
        //
        // Raster: links eine Schiene mit Filtern (Alle, Favoriten, Neu, Ungespielt, Launcher, Sammlungen, smarte Listen,
        // Versteckt), rechts schräge Glas-Karten, die beim Hovern zur Maus kippen, einen Lichtreflex zeigen und in der
        // Hauptfarbe ihres Covers leuchten. Ein Klick öffnet die Spiel-Ansicht (Hologramm), ein Doppelklick startet.

        private string holoGamesFilter = "all";
        private string holoGamesSearch = string.Empty;
        private string holoGamesGenre = string.Empty;
        private System.Windows.Controls.ContextMenu? holoRailMenu;   // Rechtsklick-Menü für den nächsten Schienen-Eintrag (smarte Listen)
        private string? holoRailTip;
        private WrapPanel? holoGamesGrid;
        private TextBlock? holoGamesCount;

        private FrameworkElement BuildHoloGames()
        {
            var page = new StackPanel();

            var sort = HoloButton(Loc.T("Sortierung") + ": " + Loc.T(SortLabels[Math.Clamp(settings.SortMode, 0, SortLabels.Length - 1)]), () => { }, false, "");
            sort.MouseLeftButtonUp += (s, e) =>
            {
                var menu = CreateMenu();
                for (int i = 0; i < SortLabels.Length; i++)
                {
                    int mode = i;
                    AddMenuItem(menu, (mode == settings.SortMode ? "✓  " : "     ") + Loc.T(SortLabels[mode]), () =>
                    {
                        settings.SortMode = mode;
                        SaveSettings();
                        ApplyFilter();   // Thema 1 mitziehen
                        RefreshHoloPage();
                    });
                }
                menu.PlacementTarget = sort;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            };
            // Genre-Filter (wie „🏷 Genre“ in Thema 1)
            var genre = HoloButton(holoGamesGenre.Length == 0 ? Loc.T("Genre") : Loc.T("Genre") + ": " + holoGamesGenre + "  ✕", () => { }, holoGamesGenre.Length > 0, "");
            genre.MouseLeftButtonUp += (s, e) =>
            {
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var game in allGames.Where(g => !g.Hidden && g.AppId.Length > 0))
                {
                    if (!settings.GameInfo.TryGetValue(game.AppId, out var info)) continue;
                    foreach (string name in info.Genres) counts[name] = counts.TryGetValue(name, out int c) ? c + 1 : 1;
                }
                var menu = CreateMenu();
                if (holoGamesGenre.Length > 0) AddMenuItem(menu, "✖ Alle Genres", () => { holoGamesGenre = string.Empty; RefreshHoloPage(); });
                if (counts.Count == 0) AddMenuItem(menu, "Noch keine Spielinfos geladen", () => { });
                foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(25))
                {
                    string chosen = pair.Key;
                    AddMenuItem(menu, $"{chosen}  ({pair.Value})", () => { holoGamesGenre = chosen; RefreshHoloPage(); });
                }
                menu.PlacementTarget = genre;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            };

            // Alles, was Thema 1 über der Spieleliste anbietet: Vorschlag, Mehrfach-Bearbeitung, smarte Listen, Spiele-Ordner
            FrameworkElement? more = null;
            more = HoloButton("Mehr", () =>
            {
                var menu = CreateMenu();
                AddMenuItem(menu, "🎯 Was soll ich spielen?", ShowGameSuggestion);
                AddMenuItem(menu, "☑ Auswahl bearbeiten", HoloBulkEdit);
                AddMenuItem(menu, "＋ Smarte Liste", () => { EditSmartList(null); RefreshHoloPage(); });
                menu.Items.Add(new System.Windows.Controls.Separator());
                AddMenuItem(menu, "📂  Spiele-Ordner hinzufügen ...", AddGameFolder);
                if (settings.GameFolders.Count > 0)
                {
                    var folders = new System.Windows.Controls.MenuItem { Header = Loc.T("📂  Spiele-Ordner entfernen") };
                    foreach (string folder in settings.GameFolders.ToList())
                    {
                        string path = folder;
                        AddSubItem(folders, StreamerOn ? FolderTitle(path) : path, () => { RemoveGameFolder(path); RefreshHoloPage(); });
                    }
                    menu.Items.Add(folders);
                }
                menu.PlacementTarget = more;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }, false, "");

            page.Children.Add(HoloHeader(Loc.T("Bibliothek"), Loc.T("Alle Spiele"), Loc.T("Klick öffnet ein Spiel, Doppelklick startet es."),
                sort,
                genre,
                more,
                HoloButton("Spiel hinzufügen", () => BtnAddGame_Click(this, new RoutedEventArgs()), false, ""),
                HoloButton("Neu scannen", () => BtnRescanGames_Click(this, new RoutedEventArgs()), false, "")));

            // Schiene links, Raster rechts
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rail = new StackPanel();
            rail.Children.Add(HoloPanel(Loc.T("Filter"), BuildHoloGamesRail(), null, 16));
            layout.Children.Add(rail);

            var right = new StackPanel();
            var tools = new Grid { Margin = new Thickness(0, 0, 18, 6) };
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tools.Children.Add(HoloSearchBox("Spiel suchen ...", holoGamesSearch, text =>
            {
                holoGamesSearch = text;
                RenderHoloGamesGrid();
            }, 380));
            holoGamesCount = new TextBlock { FontSize = 12, Foreground = ConsoleMuted, VerticalAlignment = System.Windows.VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 10) };
            holoGamesCount.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            Grid.SetColumn(holoGamesCount, 1);
            tools.Children.Add(holoGamesCount);
            right.Children.Add(tools);

            holoGamesGrid = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            HoloFitWrap(holoGamesGrid, 196);
            right.Children.Add(holoGamesGrid);
            Grid.SetColumn(right, 1);
            layout.Children.Add(right);

            // Schmales Fenster: Schiene über dem Raster
            layout.SizeChanged += (s, e) =>
            {
                bool narrow = e.NewSize.Width < 900;
                layout.ColumnDefinitions[0].Width = narrow ? new GridLength(0) : new GridLength(250);
                Grid.SetColumn(rail, 0);
                Grid.SetColumnSpan(rail, narrow ? 2 : 1);
                if (narrow && layout.RowDefinitions.Count == 0)
                {
                    layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
                Grid.SetRow(right, narrow ? 1 : 0);
                if (narrow) layout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(right, narrow ? 0 : 1);
                Grid.SetColumnSpan(right, narrow ? 2 : 1);
            };
            page.Children.Add(layout);

            RenderHoloGamesGrid();
            return page;
        }

        /// <summary>Filter-Schiene: feste Filter, Launcher, Sammlungen, smarte Listen, Versteckt.</summary>
        private FrameworkElement BuildHoloGamesRail()
        {
            var rail = new StackPanel();
            var visible = VisibleGames.ToList();

            void Item(string key, string label, int count, string? glyph = null, ImageSource? icon = null)
            {
                var item = HoloRailItem(label, count.ToString(Loc.Culture), holoGamesFilter == key, () =>
                {
                    holoGamesFilter = key;
                    RefreshHoloPage();
                }, glyph);
                if (icon != null && item is Grid grid && grid.Children.Count > 1 && grid.Children[1] is Grid row)
                {
                    var image = new System.Windows.Controls.Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    row.Children.Add(image);
                }
                if (holoRailMenu != null)
                {
                    item.ContextMenu = holoRailMenu;
                    item.ToolTip = holoRailTip;
                    holoRailMenu = null;
                }
                rail.Children.Add(item);
            }

            void Section(string title)
            {
                var label = HoloLabel(Loc.T(title), 10);
                label.Margin = new Thickness(4, 14, 0, 6);
                label.Foreground = ConsoleMuted;
                rail.Children.Add(label);
            }

            Item("all", Loc.T("Alle"), visible.Count, "");
            Item("fav", Loc.T("Favoriten"), visible.Count(g => g.IsFavorite), "");
            Item("new", Loc.T("Neu"), visible.Count(IsNewGame), "");
            Item("unplayed", Loc.T("Ungespielt"), visible.Count(g => g.PlaySeconds < 60 && g.LaunchCount == 0), "");

            var sources = visible.Where(g => !string.IsNullOrEmpty(g.Source)).GroupBy(g => g.Source, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            if (sources.Count > 0)
            {
                Section("Launcher");
                foreach (string source in OrderLaunchers(sources.Keys))
                    Item(source, Loc.T(source), sources[source], null, GetLauncherIcon(source));
            }

            if (settings.CollectionNames.Count > 0)
            {
                Section("Sammlungen");
                foreach (string collection in settings.CollectionNames)
                    Item("col:" + collection, collection, visible.Count(g => g.Collections.Contains(collection)), "");
            }

            if (settings.SmartLists.Count > 0)
            {
                Section("Smarte Listen");
                foreach (var list in settings.SmartLists.ToList())
                {
                    var smart = list;
                    var smartMenu = CreateMenu();
                    AddMenuItem(smartMenu, "✏  Bearbeiten ...", () => { EditSmartList(smart); RefreshHoloPage(); });
                    AddMenuItem(smartMenu, "🗑  Smarte Liste löschen", () => { DeleteSmartList(smart); RefreshHoloPage(); });
                    holoRailMenu = smartMenu;
                    holoRailTip = DescribeSmartList(smart);
                    Item("smart:" + list.Id, list.Name, visible.Count(g => MatchesSmartList(list, g)), "");
                }
            }

            int hidden = allGames.Count(g => g.Hidden);
            if (hidden > 0)
            {
                Section("Weitere");
                Item("hidden", Loc.T("Versteckt"), hidden, "");
            }
            return rail;
        }

        private List<GameItem> HoloGamesFiltered()
        {
            string filter = holoGamesFilter;
            // Gelöschte Sammlung / Liste: auf „Alle“ zurück
            if (filter.StartsWith("col:", StringComparison.Ordinal) && !settings.CollectionNames.Contains(filter[4..])) filter = holoGamesFilter = "all";
            if (filter.StartsWith("smart:", StringComparison.Ordinal) && settings.SmartLists.All(l => l.Id != filter[6..])) filter = holoGamesFilter = "all";

            IEnumerable<GameItem> query = allGames.Where(g => g.Hidden == (filter == "hidden"));
            if (filter == "fav") query = query.Where(g => g.IsFavorite);
            else if (filter == "new") query = query.Where(IsNewGame);
            else if (filter == "unplayed") query = query.Where(g => g.PlaySeconds < 60 && g.LaunchCount == 0);
            else if (filter.StartsWith("col:", StringComparison.Ordinal)) query = query.Where(g => g.Collections.Contains(filter[4..]));
            else if (filter.StartsWith("smart:", StringComparison.Ordinal))
            {
                var smart = settings.SmartLists.First(l => l.Id == filter[6..]);
                query = query.Where(g => MatchesSmartList(smart, g));
            }
            else if (filter is not ("all" or "hidden")) query = query.Where(g => string.Equals(g.Source, filter, StringComparison.OrdinalIgnoreCase));
            if (holoGamesGenre.Length > 0) query = query.Where(g => GameHasGenre(g, holoGamesGenre));

            if (holoGamesSearch.Length > 0)
            {
                var parsed = ParseSearch(holoGamesSearch);
                return query.Select(g => (Game: g, Score: GameSearchScore(g, parsed))).Where(h => h.Score >= 0)
                    .OrderByDescending(h => h.Score).ThenBy(h => h.Game.Name, StringComparer.CurrentCultureIgnoreCase).Select(h => h.Game).ToList();
            }

            var sorted = SortGames(query);
            if (settings.FavoritesFirst) sorted = sorted.OrderByDescending(g => g.IsFavorite);
            return sorted.ToList();
        }

        /// <summary>Mehrere Spiele auf einmal bearbeiten (wie „☑ Auswahl bearbeiten“ in Thema 1), aus den gerade gezeigten Spielen.</summary>
        private void HoloBulkEdit()
        {
            var shown = HoloGamesFiltered();
            if (shown.Count == 0) return;

            var chosen = PickFromList("Mehrere Spiele bearbeiten",
                "Wähle die Spiele aus, auf die die Aktion angewendet wird. Es werden nur die aktuell angezeigten Spiele aufgelistet.",
                shown.Select(g => g.Name).ToList());
            if (chosen == null || chosen.Count == 0) return;

            var menu = BuildBulkMenu(shown.Where(g => chosen.Contains(g.Name)).ToList());
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.Closed += (s, e) => RefreshHoloPage();
            menu.IsOpen = true;
        }

        private void RenderHoloGamesGrid()
        {
            if (holoGamesGrid == null) return;
            holoGamesGrid.Children.Clear();
            var games = HoloGamesFiltered();
            if (holoGamesCount != null) holoGamesCount.Text = GamesText(games.Count).ToUpper(Loc.Culture);

            if (games.Count == 0)
            {
                holoGamesGrid.Children.Add(HoloHint(holoGamesSearch.Length > 0 ? "Keine Treffer." : "Hier ist noch nichts."));
                return;
            }

            // Nur die ersten Karten fliegen ein (danach sieht man sie ohnehin erst beim Scrollen)
            int index = 0;
            foreach (var game in games)
            {
                var card = HoloGameCard(game);
                if (holoAnimate && index < 24) HoloCardEnter(card, index);
                holoGamesGrid.Children.Add(card);
                index++;
            }
        }

        private void HoloCardEnter(FrameworkElement card, int index)
        {
            card.Opacity = 0;
            var lift = new TranslateTransform(0, 22);
            card.RenderTransform = lift;
            var begin = TimeSpan.FromMilliseconds(180 + index * 28);
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = begin });
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(520)) { BeginTime = begin, EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Glas-Karte für das Raster: Cover, Name, Spielzeit; kippt beim Hovern zur Maus und leuchtet in der Cover-Farbe.</summary>
        private FrameworkElement HoloGameCard(GameItem game)
        {
            var cell = new Grid { Margin = new Thickness(0, 0, 18, 22), Background = System.Windows.Media.Brushes.Transparent };
            var card = new Grid { Cursor = System.Windows.Input.Cursors.Hand, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), ContextMenu = BuildGameMenu(game), ToolTip = game.Name };
            var scale = new ScaleTransform(1, 1);
            var skew = new SkewTransform(0, 0);
            card.RenderTransform = new TransformGroup { Children = { skew, scale } };
            cell.Children.Add(card);

            // Feste Seitenverhältnis-Box: Höhe folgt der Breite
            var body = new Grid();
            card.Children.Add(body);
            cell.SizeChanged += (s, e) => body.Height = Math.Max(120, (e.NewSize.Width - 18) * 1.42);

            var image = new Border { Background = MakeBrush("#141C2E") };
            if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage bitmap)
            {
                bool isLogo = EffectiveCover(game) == game.FallbackCoverPath;
                image.Background = new ImageBrush(bitmap) { Stretch = isLogo ? Stretch.Uniform : Stretch.UniformToFill };
                if (isLogo) image.Padding = new Thickness(26);
            }
            else
            {
                var letter = new TextBlock { Text = game.Name.Length > 0 ? game.Name[..1].ToUpperInvariant() : "?", FontSize = 54, FontWeight = FontWeights.Bold, Foreground = ConsoleMuted, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                letter.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                image.Child = letter;
            }
            body.Children.Add(image);

            // Unten: Verlauf mit Name und Spielzeit
            var shade = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, ConsoleBase.R, ConsoleBase.G, ConsoleBase.B), 0.45));
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xF2, ConsoleBase.R, ConsoleBase.G, ConsoleBase.B), 1));
            body.Children.Add(new Border { Background = shade, IsHitTestVisible = false });

            var info = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 22, 12) };
            var name = new TextBlock { Text = game.Name, FontSize = 14.5, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            info.Children.Add(name);
            var metaRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            var icon = new System.Windows.Controls.Image { Source = GetLauncherIcon(game.Source), Width = 12, Height = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            metaRow.Children.Add(icon);
            string meta = game.PlaySeconds >= 60 && !SHide(settings.StreamHideStats) ? FormatPlaytime(game.PlaySeconds) : game.LaunchCount == 0 && game.PlaySeconds < 60 ? Loc.T("Ungespielt") : Loc.T(game.Source);
            var metaText = new TextBlock { Text = meta.ToUpper(Loc.Culture), FontSize = 10.5, Foreground = ConsoleCyan, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            metaText.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            metaRow.Children.Add(metaText);
            info.Children.Add(metaRow);
            body.Children.Add(info);

            // Abzeichen oben: Favorit, Update
            var badges = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = System.Windows.VerticalAlignment.Top, Margin = new Thickness(10, 9, 0, 0) };
            if (game.IsFavorite) badges.Children.Add(new TextBlock { Text = "★", FontSize = 15, Foreground = MakeBrush("#FACC15"), Margin = new Thickness(0, 0, 6, 0) });
            if (game.UpdatePending)
            {
                var update = new Border { Padding = new Thickness(7, 2, 8, 2), ToolTip = Loc.T("Update verfügbar"), RenderTransform = new SkewTransform(-12, 0) };
                update.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                var ink = MakeBrush(InkFor(GetAccentColor()));
                var glyph = new TextBlock { Text = "", FontSize = 11, Foreground = ink, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
                glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                var word = new TextBlock { Text = "UPDATE", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = ink, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                word.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                update.Child = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Children = { glyph, word } };
                badges.Children.Add(update);
            }
            body.Children.Add(badges);

            // Lichtreflex, folgt der Maus
            var glare = new RadialGradientBrush { RadiusX = 0.9, RadiusY = 0.6 };
            glare.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0));
            glare.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
            var glareLayer = new Border { Background = glare, Opacity = 0, IsHitTestVisible = false };
            body.Children.Add(glareLayer);

            // Kante + abgeschrägte Form
            var edge = new System.Windows.Shapes.Path { Stroke = MakeBrush("#33A5B4D0"), StrokeThickness = 1, IsHitTestVisible = false };
            var edgeLayer = HoloShapeLayer(edge);
            body.Children.Add(edgeLayer);
            body.SizeChanged += (s, e) =>
            {
                double cut = Math.Round(e.NewSize.Width * 0.12);
                body.Clip = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, cut);
                edge.Data = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, cut);
            };

            bool motion = settings.HoverAnimations && !settings.PerformanceMode;
            card.MouseEnter += (s, e) =>
            {
                PlayCyberSound("hover");
                edge.StrokeThickness = 2;
                edge.Stroke = HoloEdgeBrush(0xFF);
                ApplyCoverGlow(game, edge);
                if (!motion) return;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
                glareLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                System.Windows.Controls.Panel.SetZIndex(cell, 5);
            };
            card.MouseMove += (s, e) =>
            {
                if (!motion || body.ActualWidth <= 0) return;
                var p = e.GetPosition(body);
                double rx = Math.Clamp(p.X / body.ActualWidth, 0, 1) - 0.5, ry = Math.Clamp(p.Y / body.ActualHeight, 0, 1) - 0.5;
                // Leichtes Kippen zur Maus (2D-Annäherung: Scherung beider Achsen) und Reflex an der Mausposition
                skew.AngleX = -rx * 4.5;
                skew.AngleY = ry * 3.0;
                glare.Center = glare.GradientOrigin = new System.Windows.Point(rx + 0.5, ry + 0.5);
            };
            card.MouseLeave += (s, e) =>
            {
                edge.StrokeThickness = 1;
                edge.Stroke = MakeBrush("#33A5B4D0");
                edge.Effect = null;
                skew.AngleX = skew.AngleY = 0;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
                glareLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
                System.Windows.Controls.Panel.SetZIndex(cell, 0);
            };
            card.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { e.Handled = true; LaunchGame(game); }
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (e.ClickCount > 1) return;
                PlayCyberSound("click");
                ShowConsoleDetail(game);
            };
            return cell;
        }

        // ───────── Cover-Farbe ─────────

        /// <summary>Hauptfarbe des Covers (im Hintergrund bestimmt, gemerkt); kräftig genug zum Leuchten aufgehellt.</summary>
        private void WithCoverColor(GameItem game, Action<System.Windows.Media.Color> apply)
        {
            if (!HasCover(game)) { apply(HoloCyan); return; }
            string path = EffectiveCover(game);
            if (coverColors.TryGetValue(path, out var known))
            {
                apply(known is System.Windows.Media.Color c ? GlowableColor(c) : HoloCyan);
                return;
            }
            Task.Run(() => ExtractCoverColor(path)).ContinueWith(task =>
            {
                var color = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    coverColors[path] = color;
                    apply(color is System.Windows.Media.Color c ? GlowableColor(c) : HoloCyan);
                }));
            });
        }

        /// <summary>Zu dunkle Farben aufhellen, damit das Leuchten auf Blauschwarz sichtbar ist.</summary>
        private static System.Windows.Media.Color GlowableColor(System.Windows.Media.Color c)
        {
            double max = Math.Max(c.R, Math.Max(c.G, c.B));
            if (max < 1) return System.Windows.Media.Color.FromRgb(0x22, 0xD3, 0xEE);
            double factor = Math.Max(1, 230 / max);
            return System.Windows.Media.Color.FromRgb((byte)Math.Min(255, c.R * factor), (byte)Math.Min(255, c.G * factor), (byte)Math.Min(255, c.B * factor));
        }

        /// <summary>Thema 2: Kante leuchtet in der Cover-Farbe (abschaltbar; im Leistungsmodus ohne Schein).</summary>
        private void ApplyCoverGlow(GameItem game, System.Windows.Shapes.Path edge)
        {
            if (!settings.CyberCoverGlow) return;
            WithCoverColor(game, color =>
            {
                if (!edge.IsMouseOver && edge.Parent is FrameworkElement layer && layer.Parent is FrameworkElement body && !body.IsMouseOver) return;
                var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 1) };
                brush.GradientStops.Add(new GradientStop(color, 0));
                brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x99, color.R, color.G, color.B), 0.5));
                brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 1));
                edge.Stroke = brush;
                if (!settings.PerformanceMode)
                    edge.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.95 };
            });
        }

        // ───────── Sitzungen je Spiel ─────────

        private void LogGameSession(GameItem game, DateTime start, TimeSpan played)
        {
            if (played.TotalMinutes < 1 || IsTrackingIgnored(game)) return;
            if (!settings.GameSessions.TryGetValue(game.Name, out var list) || list == null)
            {
                list = new List<GameSessionEntry>();
                settings.GameSessions[game.Name] = list;
            }
            list.Insert(0, new GameSessionEntry { Start = start, Seconds = Math.Round(played.TotalSeconds) });
            if (list.Count > 12) list.RemoveRange(12, list.Count - 12);
        }

        /// <summary>Letzte Sitzungen als leuchtende Balken (neueste rechts).</summary>
        private FrameworkElement HoloSessionBars(GameItem game, double height = 120)
        {
            if (!settings.GameSessions.TryGetValue(game.Name, out var list) || list == null || list.Count == 0)
                return HoloHint("Noch keine Sitzungen aufgezeichnet. Ab jetzt merkt sich der Launcher jede Sitzung dieses Spiels.");

            var sessions = list.Take(10).Reverse().ToList();
            double max = Math.Max(sessions.Max(s => s.Seconds), 1800);
            var bars = new Grid();
            for (int i = 0; i < sessions.Count; i++)
            {
                var entry = sessions[i];
                bool newest = i == sessions.Count - 1;
                bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var column = new StackPanel { ToolTip = entry.Start.ToString("g", Loc.Culture) + "  ·  " + FormatPlaytime((long)entry.Seconds) };
                var slot = new Grid { Height = height };
                var value = new TextBlock { Text = FormatPlaytime((long)entry.Seconds), FontSize = 9.5, Foreground = newest ? System.Windows.Media.Brushes.White : ConsoleMuted, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis };
                value.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                double barHeight = Math.Max(4, (height - 18) * entry.Seconds / max);
                value.Margin = new Thickness(0, 0, 0, barHeight + 4);
                var bar = new Border { Height = barHeight, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 0), RenderTransformOrigin = new System.Windows.Point(0.5, 1) };
                var accent = GetAccentColor();
                var fill = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 1), EndPoint = new System.Windows.Point(0, 0) };
                var tint = newest ? accent : HoloCyan;
                fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x40, tint.R, tint.G, tint.B), 0));
                fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xEE, tint.R, tint.G, tint.B), 1));
                bar.Background = fill;
                bar.BorderBrush = new SolidColorBrush(tint);
                bar.BorderThickness = new Thickness(0, 2, 0, 0);
                if (newest && !settings.PerformanceMode) bar.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
                var grow = new ScaleTransform(1, 1);
                bar.RenderTransform = new TransformGroup { Children = { grow, new SkewTransform(-12, 0) } };
                if (holoAnimate)
                {
                    grow.ScaleY = 0;
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(560)) { BeginTime = TimeSpan.FromMilliseconds(420 + i * 55), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                slot.Children.Add(bar);
                slot.Children.Add(value);
                column.Children.Add(slot);
                var day = new TextBlock { Text = entry.Start.ToString("dd.MM", Loc.Culture), FontSize = 10, Foreground = newest ? System.Windows.Media.Brushes.White : ConsoleMuted, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                day.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                column.Children.Add(day);
                Grid.SetColumn(column, i);
                bars.Children.Add(column);
            }
            return bars;
        }

        // ───────── Spiel-Ansicht (Hologramm) ─────────

        private GameItem? holoDetailShown;

        private void BuildConsoleDetail(GameItem game)
        {
            if (consoleView == null) return;
            consoleView.Children.Clear();
            consoleFocus = game;
            SetConsoleBackdrop(game);

            bool fresh = !ReferenceEquals(holoDetailShown, game);
            holoDetailShown = game;
            holoPanelIndex = 0;
            holoAnimate = fresh && settings.PageAnimations && !settings.PerformanceMode;
            if (holoAnimate) holoAnimatingUntil = DateTime.Now.AddMilliseconds(1400);

            var layout = new Grid { Margin = new Thickness(56, ConsoleTopInset + 26, 40, 30) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var scroll = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            consoleView.Children.Add(scroll);

            // ── Links: großes Cover + START ──
            var left = new StackPanel { Width = 330, Margin = new Thickness(0, 0, 34, 0), VerticalAlignment = System.Windows.VerticalAlignment.Top };
            left.Children.Add(HoloDetailCover(game, 330, 470));
            var start = HoloLaunchButton(game);
            start.Margin = new Thickness(0, 22, 0, 0);
            left.Children.Add(start);
            if (SteamUpdateText(game) is string update)
            {
                var updateLine = new TextBlock { Text = "⬇  " + update, Foreground = MakeBrush("#FBBF24"), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 0, 0) };
                updateLine.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                left.Children.Add(updateLine);
            }
            var back = HoloButton("Zurück", () => ConsoleBack(), false, "");
            back.Margin = new Thickness(0, 14, 0, 0);
            back.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            left.Children.Add(back);
            layout.Children.Add(left);
            HoloEnter(left);

            // ── Rechts: Titel, Werte, Sitzungen, Notizen, Aktionen ──
            var right = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            Grid.SetColumn(right, 1);
            layout.Children.Add(right);

            var eyebrow = ConsoleEyebrow("// " + Loc.T(game.Source).ToUpper(Loc.Culture) + (game.Status == "done" ? "   ·   " + Loc.T("Durchgespielt").ToUpper(Loc.Culture) : game.Status == "playing" ? "   ·   " + Loc.T("Spiele ich gerade").ToUpper(Loc.Culture) : ""));
            right.Children.Add(eyebrow);
            var title = ConsoleTitle(game.Name, 60);
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.None;
            title.Margin = new Thickness(0, 0, 18, 0);
            var titleHost = new Grid();
            titleHost.Children.Add(title);
            right.Children.Add(titleHost);
            MakeGlitchText(title);
            HoloEnter(titleHost);

            var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 18) };
            FillConsoleChips(chips, game);
            right.Children.Add(chips);
            right.Children.Add(HoloPanel(Loc.T("Auf einen Blick"), BuildGameFacts(game), null, 16));

            // Spielzeit-Ring + Werte | Letzte Sitzungen
            var ringRow = new Grid();
            ringRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ringRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var counted = VisibleGames.Where(g => !IsTrackingIgnored(g)).ToList();
            long maxSeconds = Math.Max(1, counted.Count > 0 ? counted.Max(g => g.PlaySeconds) : 1);
            long total = Math.Max(1, counted.Sum(g => g.PlaySeconds));
            int rank = counted.OrderByDescending(g => g.PlaySeconds).ToList().IndexOf(game) + 1;
            bool hideStats = SHide(settings.StreamHideStats);
            ringRow.Children.Add(HoloRing(hideStats ? 0 : game.PlaySeconds / (double)maxSeconds,
                hideStats ? "—" : game.PlaySeconds >= 60 ? FormatPlaytime(game.PlaySeconds) : "0m",
                rank > 0 && game.PlaySeconds >= 60 && !hideStats ? Loc.T("Platz") + " " + rank.ToString(Loc.Culture) : Loc.T("Spielzeit"), 150, null, 10));

            var values = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
            void Value(string caption, string text)
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 12) };
                cell.Children.Add(HoloLabel(caption));
                var number = ConsoleTitle(text, 24);
                number.Margin = new Thickness(0, 2, 0, 0);
                cell.Children.Add(number);
                values.Children.Add(cell);
            }
            Value(Loc.T("Starts"), game.LaunchCount.ToString(Loc.Culture));
            if (!SHide(settings.StreamHideRecent)) Value(Loc.T("zuletzt gespielt"), game.LastPlayed.HasValue ? Loc.T(FormatRelative(game.LastPlayed.Value)) : Loc.T("nie"));
            if (!hideStats) Value(Loc.T("Anteil"), (game.PlaySeconds * 100.0 / total).ToString("0.#", Loc.Culture) + " %");
            if (KnownGameSize(game) is long bytes && bytes > 0) Value(Loc.T("Größe"), FormatBytes(bytes));
            if (game.Rating > 0) Value(Loc.T("Bewertung"), new string('★', Math.Clamp(game.Rating, 1, 5)));
            Grid.SetColumn(values, 1);
            ringRow.Children.Add(values);

            var sessionBody = hideStats || SHide(settings.StreamHideRecent) ? (FrameworkElement)HoloHint("Im Streamer-Modus ausgeblendet.") : HoloSessionBars(game);
            var panels = HoloColumns(
                new StackPanel { Children = { HoloPanel(Loc.T("Spielzeit"), ringRow, null, 20) } },
                new StackPanel { Children = { HoloPanel(Loc.T("Letzte Sitzungen"), sessionBody, null, 20) } }, 0.5);
            right.Children.Add(panels);
            right.Children.Add(HoloPanel(Loc.T("Läuft das auf meinem PC?"), BuildRequirementCheck(game), null, 16));

            if (!string.IsNullOrWhiteSpace(game.Notes))
            {
                var note = new TextBlock { Text = game.Notes.Trim(), FontSize = 14.5, Foreground = MakeBrush("#C9D4E3"), TextWrapping = TextWrapping.Wrap, MaxHeight = 110, TextTrimming = TextTrimming.WordEllipsis };
                right.Children.Add(HoloPanel(Loc.T("Notizen"), note, () => ShowGameDetails(game), 16));
            }

            // Aktionen
            var actions = new WrapPanel();
            actions.Children.Add(HoloButton(game.IsFavorite ? "Favorit entfernen" : "Favorit", () => { ToggleFavorite(game); BuildConsoleDetail(game); }, false, game.IsFavorite ? "" : ""));
            actions.Children.Add(HoloButton("Details", () => ShowGameDetails(game), false, ""));
            if (!string.IsNullOrEmpty(game.InstallDir) || System.IO.File.Exists(game.ExecutablePath))
                actions.Children.Add(HoloButton("Ordner", () => OpenGameLocation(game), false, ""));
            actions.Children.Add(HoloButton("Jetzt sichern", () => _ = BackupNowAsync(game), false, ""));
            if (game.Source == "Steam" && game.AppId.Length > 0)
                actions.Children.Add(HoloButton("Erfolge", () => _ = ShowSteamAchievementsAsync(game), false, ""));
            if (CanRepair(game))
                actions.Children.Add(HoloButton("Reparieren", () => RepairGame(game), false, ""));
            FrameworkElement? more = null;
            more = HoloButton("Mehr", () =>
            {
                var menu = BuildGameMenu(game);
                menu.PlacementTarget = more;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.IsOpen = true;
            }, false, "");
            actions.Children.Add(more);
            right.Children.Add(HoloPanel(Loc.T("Aktionen"), actions, null, 16));

            if (fresh) PlayCyberSound("open");
        }

        /// <summary>Großes Cover mit Neonkante in der Cover-Farbe und Scan-Linie beim Aufbauen.</summary>
        private FrameworkElement HoloDetailCover(GameItem game, double width, double height)
        {
            double cut = Math.Round(width * 0.1);
            var host = new Grid { Width = width, Height = height };
            var image = new Border { Background = MakeBrush("#141C2E"), Clip = ChamferGeometry(width, height, cut) };
            if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage bitmap)
            {
                bool isLogo = EffectiveCover(game) == game.FallbackCoverPath;
                image.Background = new ImageBrush(bitmap) { Stretch = isLogo ? Stretch.Uniform : Stretch.UniformToFill };
            }
            else
            {
                var name = ConsoleTitle(game.Name, 30);
                name.TextWrapping = TextWrapping.Wrap;
                name.TextAlignment = TextAlignment.Center;
                name.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                name.Margin = new Thickness(20);
                image.Child = name;
            }
            host.Children.Add(image);

            // Feine Rasterlinien wie bei einer Projektion
            var lines = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), null, new RectangleGeometry(new Rect(0, 0, 1, 1))))
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 4, 4),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 4, 4),
                ViewboxUnits = BrushMappingMode.Absolute
            };
            host.Children.Add(new Border { Background = lines, Clip = ChamferGeometry(width, height, cut), IsHitTestVisible = false, Opacity = 0.6 });

            var edge = new System.Windows.Shapes.Path { Data = ChamferGeometry(width, height, cut), Stroke = HoloEdgeBrush(0xFF), StrokeThickness = 2, IsHitTestVisible = false };
            host.Children.Add(edge);
            WithCoverColor(game, color =>
            {
                if (!settings.CyberCoverGlow) return;
                edge.Stroke = new LinearGradientBrush(color, HoloCyan, 45);
                if (!settings.PerformanceMode) edge.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 34, ShadowDepth = 0, Opacity = 0.9 };
            });

            if (holoAnimate)
            {
                // Hologramm baut sich auf: Bild wird von oben nach unten „gescannt“, eine Lichtlinie läuft mit
                var reveal = new RectangleGeometry(new Rect(0, 0, width, 0));
                var mask = new GeometryGroup { Children = { reveal } };
                image.Clip = new CombinedGeometry(GeometryCombineMode.Intersect, ChamferGeometry(width, height, cut), mask);
                reveal.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(new Rect(0, 0, width, 0), new Rect(0, 0, width, height), TimeSpan.FromMilliseconds(720)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });

                var scan = new Border { Height = 3, VerticalAlignment = System.Windows.VerticalAlignment.Top, IsHitTestVisible = false, Background = new SolidColorBrush(HoloCyan) };
                if (!settings.PerformanceMode) scan.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = HoloCyan, BlurRadius = 16, ShadowDepth = 0 };
                var move = new TranslateTransform();
                scan.RenderTransform = move;
                host.Children.Add(scan);
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, height, TimeSpan.FromMilliseconds(720)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(840) };
                fade.Completed += (s, e) => host.Children.Remove(scan);
                scan.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            return host;
        }

        /// <summary>Großer START-Knopf: schräg, in der Akzentfarbe, pulsiert sanft.</summary>
        private FrameworkElement HoloLaunchButton(GameItem game)
        {
            var accent = GetAccentColor();
            var plate = new Border { Height = 66, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), RenderTransform = new SkewTransform(-12, 0), BorderThickness = new Thickness(1) };
            plate.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            plate.BorderBrush = System.Windows.Media.Brushes.White;
            System.Windows.Media.Effects.DropShadowEffect? glow = null;
            if (!settings.PerformanceMode)
            {
                glow = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.7 };
                plate.Effect = glow;
                if (settings.PageAnimations)
                    glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(16, 36, TimeSpan.FromMilliseconds(1300)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            }

            var ink = MakeBrush(InkFor(accent));
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = "▶", FontSize = 22, Foreground = ink, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center });
            var label = new TextBlock { Text = Loc.T("Spielen").ToUpper(Loc.Culture), FontSize = 26, FontWeight = FontWeights.Bold, Foreground = ink, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            row.Children.Add(label);

            var button = new Grid { Cursor = System.Windows.Input.Cursors.Hand, ToolTip = Loc.T("Startet das Spiel (Enter)") };
            button.Children.Add(plate);
            button.Children.Add(row);
            button.MouseEnter += (s, e) => { plate.Opacity = 0.9; PlayCyberSound("hover"); };
            button.MouseLeave += (s, e) => plate.Opacity = 1;
            button.MouseLeftButtonUp += (s, e) => LaunchGame(game);
            HoloEnter(button);
            return button;
        }
    }
}
