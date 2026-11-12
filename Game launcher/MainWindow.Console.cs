using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Konsolen-Oberfläche ─────────────────────────────
        //
        // Eigener Aufbau für Maus und Tastatur: oben nur START / BIBLIOTHEK / MEHR, das gewählte Spiel füllt den Bildschirm,
        // darunter ein Karussell (Start) bzw. waagerechte Reihen (Bibliothek), dazu eine eigene Detailseite.
        // Alle anderen Seiten (Einstellungen, System, Statistik ...) öffnen sich über MEHR im Stil von Thema 2.

        private bool IsConsole => IsBrutal;

        private Grid? consoleStage;        // Hintergrund + aktuelle Ansicht (Start, Bibliothek, Details)
        private Grid? consoleBar;          // Kopfleiste mit den Reitern
        private Border? consoleBarBack;
        private Grid? consoleView;         // Inhalt der Bühne
        private System.Windows.Controls.Image? consoleBackA, consoleBackB;
        private bool consoleBackAFront = true;
        private Border? consoleDim;        // zusätzliche Abdunklung in der Bibliothek
        private string consoleMode = "home";   // home | library | detail | page
        private string consoleReturnMode = "home";
        private GameItem? consoleFocus;
        private GameItem? consoleDetailGame;
        private readonly List<(GameItem Game, Border Tile)> consoleHomeTiles = new();
        private int consoleHomeIndex;
        private readonly Dictionary<string, Border> consoleTabs = new();
        private TextBlock? consoleClock;
        private string consoleSearch = string.Empty;
        private ScrollViewer? consoleLibraryScroll;
        private bool consoleNavigating;
        private readonly DispatcherTimer consoleRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly DispatcherTimer consoleHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
        private GameItem? consolePendingHover;

        private static readonly SolidColorBrush ConsoleInk = MakeBrush("#0B0B0B");
        private static readonly SolidColorBrush ConsoleMuted = MakeBrush("#9A9890");

        private void InitConsole()
        {
            if (!IsConsole || WindowRoot == null) return;

            BuildConsoleStage();
            BuildConsoleBar();

            consoleRefreshTimer.Tick += (s, e) => { consoleRefreshTimer.Stop(); RefreshConsole(); };
            consoleHoverTimer.Tick += (s, e) =>
            {
                consoleHoverTimer.Stop();
                if (consolePendingHover != null) FocusConsoleGame(consolePendingHover, false);
            };
            PreviewKeyDown += OnConsoleKeyDown;

            var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            clockTimer.Tick += (s, e) => UpdateConsoleClock();
            clockTimer.Start();
            UpdateConsoleClock();

            if (settings.StartPage == "games") ShowConsoleLibrary();
            else ShowConsoleHome();
        }

        /// <summary>Nach Scan, Cover-Download oder Spielstart (gesammelt, damit es nicht flackert).</summary>
        private void RequestConsoleRefresh()
        {
            if (!IsConsole || consoleStage == null) return;
            consoleRefreshTimer.Stop();
            consoleRefreshTimer.Start();
        }

        private void RefreshConsole()
        {
            if (!IsConsole || consoleStage == null) return;
            switch (consoleMode)
            {
                case "home": BuildConsoleHome(); break;
                case "library":
                    double offset = consoleLibraryScroll?.VerticalOffset ?? 0;
                    BuildConsoleLibrary();
                    consoleLibraryScroll?.ScrollToVerticalOffset(offset);
                    break;
                case "detail":
                    if (consoleDetailGame != null) BuildConsoleDetail(consoleDetailGame);
                    break;
            }
        }

        // ───────── Gerüst: Hintergrund, Bühne, Kopfleiste ─────────

        private void BuildConsoleStage()
        {
            consoleStage = new Grid { Background = ConsoleInk, ClipToBounds = true };
            System.Windows.Controls.Panel.SetZIndex(consoleStage, 25);

            // Beide Bilder (für das Überblenden) in einem eigenen Behälter ganz unten, damit Abdunklung und Inhalt darüber liegen
            consoleBackA = NewBackdropImage();
            consoleBackB = NewBackdropImage();
            var backdropHost = new Grid { IsHitTestVisible = false };
            backdropHost.Children.Add(consoleBackA);
            backdropHost.Children.Add(consoleBackB);
            consoleStage.Children.Add(backdropHost);

            // Abdunkeln von links (dort steht der Text) und von unten (dort liegen die Cover)
            var fromLeft = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
            fromLeft.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xF0, 0x0B, 0x0B, 0x0B), 0.0));
            fromLeft.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x99, 0x0B, 0x0B, 0x0B), 0.45));
            fromLeft.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x22, 0x0B, 0x0B, 0x0B), 1.0));
            consoleStage.Children.Add(new Border { Background = fromLeft, IsHitTestVisible = false });

            var fromBottom = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            fromBottom.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x99, 0x0B, 0x0B, 0x0B), 0.0));
            fromBottom.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x0B, 0x0B, 0x0B), 0.22));
            fromBottom.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x0B, 0x0B, 0x0B), 0.5));
            fromBottom.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xF5, 0x0B, 0x0B, 0x0B), 1.0));
            consoleStage.Children.Add(new Border { Background = fromBottom, IsHitTestVisible = false });

            consoleDim = new Border { Background = MakeBrush("#B80B0B0B"), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            consoleStage.Children.Add(consoleDim);

            consoleView = new Grid();
            consoleStage.Children.Add(consoleView);

            WindowRoot.Children.Add(consoleStage);
        }

        private System.Windows.Controls.Image NewBackdropImage()
        {
            var image = new System.Windows.Controls.Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, settings.PerformanceMode ? BitmapScalingMode.LowQuality : BitmapScalingMode.HighQuality);
            return image;
        }

        private void BuildConsoleBar()
        {
            consoleBar = new Grid { VerticalAlignment = System.Windows.VerticalAlignment.Top, Height = 76, Margin = new Thickness(0, TitleBarHeight, 0, 0) };
            System.Windows.Controls.Panel.SetZIndex(consoleBar, 40);

            consoleBarBack = new Border { IsHitTestVisible = false };
            consoleBar.Children.Add(consoleBarBack);

            var row = new Grid { Margin = new Thickness(64, 0, 64, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            consoleBar.Children.Add(row);

            // Marke
            var brand = new TextBlock
            {
                Text = (string.IsNullOrWhiteSpace(settings.BrandName) ? "DFP PRO" : settings.BrandName).ToUpper(Loc.Culture),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 56, 0)
            };
            brand.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            row.Children.Add(brand);

            // Reiter
            var tabs = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Stretch };
            Grid.SetColumn(tabs, 1);
            tabs.Children.Add(ConsoleTab("home", "Start", ShowConsoleHome));
            tabs.Children.Add(ConsoleTab("library", "Bibliothek", ShowConsoleLibrary));
            tabs.Children.Add(ConsoleTab("page", "Mehr", () => OpenConsoleMoreMenu()));
            row.Children.Add(tabs);

            // Rechts: Suche, Uhr, Einstellungen
            var right = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(right, 3);
            right.Children.Add(ConsoleIconButton("", Loc.T("Suche (Strg+K)"), OpenSpotlight));
            consoleClock = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(22, 0, 22, 0)
            };
            consoleClock.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            right.Children.Add(consoleClock);
            right.Children.Add(ConsoleIconButton("", Loc.T("Einstellungen"), () => ShowConsolePage("settings")));
            row.Children.Add(right);

            WindowRoot.Children.Add(consoleBar);
        }

        private Border ConsoleTab(string mode, string title, Action open)
        {
            var text = new TextBlock
            {
                Text = Loc.T(title).ToUpper(Loc.Culture),
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = ConsoleMuted,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");

            var underline = new Border { Height = 4, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 14), Opacity = 0 };
            underline.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

            var grid = new Grid();
            grid.Children.Add(text);
            grid.Children.Add(underline);

            var tab = new Border { Child = grid, Padding = new Thickness(0, 0, 0, 0), Margin = new Thickness(0, 0, 34, 0), Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Tag = (text, underline) };
            tab.MouseEnter += (s, e) => { if (consoleMode != mode && !(mode == "page" && consoleMode == "page")) text.Foreground = System.Windows.Media.Brushes.White; };
            tab.MouseLeave += (s, e) => UpdateConsoleTabs();
            tab.MouseLeftButtonUp += (s, e) => open();
            consoleTabs[mode] = tab;
            return tab;
        }

        private Border ConsoleIconButton(string glyph, string tip, Action click)
        {
            var icon = new TextBlock { Text = glyph, FontSize = 20, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            var button = new Border
            {
                Width = 44,
                Height = 44,
                Child = icon,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = MakeBrush("#55FFFFFF"),
                BorderThickness = new Thickness(2),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = tip
            };
            button.MouseEnter += (s, e) => button.BorderBrush = System.Windows.Media.Brushes.White;
            button.MouseLeave += (s, e) => button.BorderBrush = MakeBrush("#55FFFFFF");
            button.MouseLeftButtonUp += (s, e) => click();
            return button;
        }

        private void UpdateConsoleTabs()
        {
            foreach (var (mode, tab) in consoleTabs)
            {
                if (tab.Tag is not ValueTuple<TextBlock, Border> parts) continue;
                bool active = consoleMode == mode || (mode == "library" && consoleMode == "detail" && consoleReturnMode == "library")
                                                  || (mode == "home" && consoleMode == "detail" && consoleReturnMode == "home");
                parts.Item1.Foreground = active ? System.Windows.Media.Brushes.White : ConsoleMuted;
                parts.Item2.Opacity = active ? 1 : 0;
            }

            // Auf den übrigen Seiten bekommt die Kopfleiste eine feste Fläche, sonst liegt sie frei über dem Bild
            if (consoleBarBack != null)
            {
                if (consoleMode == "page")
                {
                    consoleBarBack.Background = ConsoleInk;
                    consoleBarBack.BorderBrush = MakeBrush(PaperColor);
                    consoleBarBack.BorderThickness = new Thickness(0, 0, 0, 2);
                }
                else
                {
                    consoleBarBack.Background = System.Windows.Media.Brushes.Transparent;
                    consoleBarBack.BorderThickness = new Thickness(0);
                }
            }
        }

        private void UpdateConsoleClock()
        {
            if (consoleClock != null) consoleClock.Text = DateTime.Now.ToString(settings.Clock24h ? "HH:mm" : "h:mm tt", Loc.Culture);
        }

        /// <summary>MEHR: alle übrigen Bereiche als Menü direkt unter dem Reiter.</summary>
        private void OpenConsoleMoreMenu()
        {
            var menu = CreateMenu();
            foreach (var (icon, title, key) in PageEntries)
            {
                if (key is "dashboard" or "games") continue;   // die sind START und BIBLIOTHEK
                string target = key;
                AddMenuItem(menu, icon + "  " + Loc.T(title), () => ShowConsolePage(target));
            }
            if (consoleTabs.TryGetValue("page", out var tab))
            {
                menu.PlacementTarget = tab;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            }
            menu.IsOpen = true;
        }

        // ───────── Umschalten zwischen den Ansichten ─────────

        private void SetConsoleMode(string mode)
        {
            if (consoleStage == null) return;
            if (mode != "detail" && mode != "page") consoleReturnMode = mode;
            consoleMode = mode;

            consoleStage.Visibility = mode == "page" ? Visibility.Collapsed : Visibility.Visible;
            if (consoleDim != null) consoleDim.Visibility = mode == "library" ? Visibility.Visible : Visibility.Collapsed;
            UpdateConsoleTabs();
        }

        private void ShowConsoleHome()
        {
            SetConsoleMode("home");
            BuildConsoleHome();
        }

        private void ShowConsoleLibrary()
        {
            SetConsoleMode("library");
            BuildConsoleLibrary();
        }

        private void ShowConsoleDetail(GameItem game)
        {
            SetConsoleMode("detail");
            consoleDetailGame = game;
            BuildConsoleDetail(game);
        }

        /// <summary>Eine der übrigen Seiten (Einstellungen, System ...) unter der Kopfleiste öffnen.</summary>
        private void ShowConsolePage(string key)
        {
            SetConsoleMode("page");
            consoleNavigating = true;
            try { NavigateTo(key); }
            finally { consoleNavigating = false; }
        }

        /// <summary>
        /// Wird am Anfang von NavigateTo aufgerufen: Andere Stellen im Launcher wollen „Dashboard“ oder „Spiele“ öffnen,
        /// in Thema 2 sind das START und BIBLIOTHEK. Gibt true zurück, wenn NavigateTo nichts mehr tun muss.
        /// </summary>
        private bool HandleConsoleNavigation(string key)
        {
            if (!IsConsole || consoleNavigating || consoleStage == null) return false;
            if (key == "dashboard") { ShowConsoleHome(); return true; }
            if (key == "games") { ShowConsoleLibrary(); return true; }
            SetConsoleMode("page");
            return false;
        }

        /// <summary>Zurück (Maus-Seitentaste, Esc in den Details): von den Details zur vorigen Ansicht.</summary>
        private bool ConsoleBack()
        {
            if (!IsConsole || consoleMode != "detail") return false;
            if (consoleReturnMode == "library") ShowConsoleLibrary();
            else ShowConsoleHome();
            return true;
        }

        // ───────── Hintergrund ─────────

        private void SetConsoleBackdrop(GameItem? game)
        {
            if (consoleBackA == null || consoleBackB == null) return;

            BitmapSource? image = null;
            bool wide = false;
            if (game != null)
            {
                image = GetHeroImage(game);
                wide = image != null;
                if (image == null)
                {
                    image = HasCover(game) ? GetCachedImage(EffectiveCover(game)) : null;
                    _ = LoadConsoleHeroLaterAsync(game);
                }
            }

            var front = consoleBackAFront ? consoleBackA : consoleBackB;
            var back = consoleBackAFront ? consoleBackB : consoleBackA;
            if (front.Source == image && image != null) return;

            back.Source = image;
            // Hochkant-Cover als Hintergrund: weich gezeichnet, damit es nicht pixelig wirkt
            back.Effect = !wide && image != null && !settings.PerformanceMode ? new System.Windows.Media.Effects.BlurEffect { Radius = 36 } : null;
            back.Margin = wide ? new Thickness(0) : new Thickness(-60);

            var duration = TimeSpan.FromMilliseconds(settings.PageAnimations && !settings.PerformanceMode ? 380 : 1);
            back.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(image == null ? 0 : wide ? 1 : 0.7, duration));
            front.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, duration));
            System.Windows.Controls.Panel.SetZIndex(back, 1);
            System.Windows.Controls.Panel.SetZIndex(front, 0);
            consoleBackAFront = !consoleBackAFront;
        }

        private async System.Threading.Tasks.Task LoadConsoleHeroLaterAsync(GameItem game)
        {
            await EnsureHeroAsync(game);
            if (consoleFocus == game && GetHeroImage(game) != null) SetConsoleBackdrop(game);
        }

        // ───────── Bausteine: Cover-Kachel, Knöpfe ─────────

        private const double ConsoleTileWidth = 160, ConsoleTileHeight = 240;

        private Border ConsoleCoverTile(GameItem game, double width, double height)
        {
            var image = new Grid { Background = MakeBrush("#161616") };
            var cover = HasCover(game) ? GetCachedImage(EffectiveCover(game)) : null;
            if (cover != null)
            {
                bool isLogo = EffectiveCover(game) == game.FallbackCoverPath;
                var picture = new System.Windows.Controls.Image { Source = cover, Stretch = isLogo ? Stretch.Uniform : Stretch.UniformToFill, Margin = isLogo ? new Thickness(width * 0.14) : new Thickness(0) };
                RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
                image.Children.Add(picture);
            }
            else
            {
                var letter = new TextBlock
                {
                    Text = game.Name.ToUpper(Loc.Culture),
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(12)
                };
                letter.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                image.Children.Add(letter);
            }

            if (game.IsFavorite)
            {
                image.Children.Add(new TextBlock
                {
                    Text = "★",
                    FontSize = 16,
                    Foreground = MakeBrush("#FACC15"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 8, 0)
                });
            }

            var tile = new Border
            {
                Width = width,
                Height = height,
                Child = image,
                BorderThickness = new Thickness(4),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                RenderTransform = new TranslateTransform(),
                ContextMenu = BuildGameMenu(game),
                ToolTip = game.Name
            };
            return tile;
        }

        private static void MarkConsoleTile(Border tile, bool focused, bool animate)
        {
            tile.BorderBrush = focused ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
            if (tile.RenderTransform is TranslateTransform move)
            {
                double target = focused ? -14 : 0;
                if (animate) move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                else
                {
                    move.BeginAnimation(TranslateTransform.YProperty, null);
                    move.Y = target;
                }
            }
        }

        /// <summary>Großer Knopf: gefüllt (Akzent) oder nur mit hellem Rahmen.</summary>
        private Border ConsoleButton(string text, bool primary, Action click, double fontSize = 18)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = primary ? MakeBrush(InkFor(GetAccentColor())) : System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");

            var button = new Border
            {
                Child = label,
                Padding = primary ? new Thickness(34, 12, 34, 12) : new Thickness(22, 12, 22, 12),
                Margin = new Thickness(0, 0, 14, 0),
                BorderThickness = new Thickness(2),
                BorderBrush = primary ? System.Windows.Media.Brushes.White : MakeBrush("#88FFFFFF"),
                Background = primary ? null : MakeBrush("#330B0B0B"),
                Cursor = System.Windows.Input.Cursors.Hand,
                RenderTransform = new TranslateTransform()
            };
            if (primary) button.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

            button.MouseEnter += (s, e) =>
            {
                if (!primary) button.BorderBrush = System.Windows.Media.Brushes.White;
                if (button.RenderTransform is TranslateTransform t) { t.X = -2; t.Y = -2; }
            };
            button.MouseLeave += (s, e) =>
            {
                if (!primary) button.BorderBrush = MakeBrush("#88FFFFFF");
                if (button.RenderTransform is TranslateTransform t) { t.X = 0; t.Y = 0; }
            };
            button.MouseLeftButtonDown += (s, e) => { if (button.RenderTransform is TranslateTransform t) { t.X = 2; t.Y = 2; } };
            button.MouseLeftButtonUp += (s, e) =>
            {
                if (button.RenderTransform is TranslateTransform t) { t.X = 0; t.Y = 0; }
                click();
            };
            return button;
        }

        private TextBlock ConsoleTitle(string text, double size)
        {
            var title = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = size * 0.95,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            return title;
        }

        private TextBlock ConsoleEyebrow(string text)
        {
            var eyebrow = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) };
            eyebrow.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            eyebrow.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            return eyebrow;
        }

        /// <summary>„18 Std. gespielt · zuletzt gestern · Steam“ (Spielzeit im Streamer-Modus verborgen).</summary>
        private string ConsoleMeta(GameItem game)
        {
            var parts = new List<string>();
            if (game.PlaySeconds >= 60 && !SHide(settings.StreamHideStats)) parts.Add(Loc.T("Spielzeit") + " " + FormatPlaytime(game.PlaySeconds));
            parts.Add(game.LastPlayed.HasValue ? Loc.T("zuletzt gespielt") + " " + Loc.T(FormatRelative(game.LastPlayed.Value)) : Loc.T("nie gespielt"));
            parts.Add(Loc.T(game.Source));
            return string.Join("   ·   ", parts);
        }

        // ───────── START ─────────

        private TextBlock? consoleHomeTitle, consoleHomeMeta, consoleHomeEyebrow;
        private Border? consoleHomeFav;

        private void BuildConsoleHome()
        {
            if (consoleView == null) return;
            consoleView.Children.Clear();
            consoleHomeTiles.Clear();

            var games = VisibleGames.ToList();
            var row = games.Where(g => g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).Take(14).ToList();
            foreach (var favorite in games.Where(g => g.IsFavorite).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
                if (row.Count < 18 && !row.Contains(favorite)) row.Add(favorite);
            foreach (var other in games.OrderByDescending(g => g.FirstSeen ?? DateTime.MinValue))
                if (row.Count < 18 && !row.Contains(other)) row.Add(other);

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            consoleView.Children.Add(layout);

            if (row.Count == 0)
            {
                var empty = ConsoleTitle(Loc.T("Noch keine Spiele gefunden.").ToUpper(Loc.Culture), 48);
                empty.Margin = new Thickness(64, 0, 64, 0);
                empty.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                layout.Children.Add(empty);
                SetConsoleBackdrop(null);
                return;
            }

            // Infos zum gewählten Spiel
            var info = new StackPanel { Margin = new Thickness(64, 0, 64, 34), MaxWidth = 1200, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            Grid.SetRow(info, 1);
            consoleHomeEyebrow = ConsoleEyebrow(string.Empty);
            consoleHomeTitle = ConsoleTitle(string.Empty, 92);
            consoleHomeMeta = new TextBlock { FontSize = 18, Foreground = MakeBrush("#D6D4CC"), Margin = new Thickness(0, 10, 0, 26) };
            info.Children.Add(consoleHomeEyebrow);
            info.Children.Add(consoleHomeTitle);
            info.Children.Add(consoleHomeMeta);

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            buttons.Children.Add(ConsoleButton("▶  " + Loc.T("Spielen").ToUpper(Loc.Culture), true, () => { if (consoleFocus != null) LaunchGame(consoleFocus); }, 20));
            buttons.Children.Add(ConsoleButton(Loc.T("Details").ToUpper(Loc.Culture), false, () => { if (consoleFocus != null) ShowConsoleDetail(consoleFocus); }));
            consoleHomeFav = ConsoleButton("☆", false, () =>
            {
                if (consoleFocus == null) return;
                ToggleFavorite(consoleFocus);
            }, 20);
            buttons.Children.Add(consoleHomeFav);
            info.Children.Add(buttons);
            layout.Children.Add(info);

            // Karussell
            var strip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(64, 18, 64, 56) };
            foreach (var item in row)
            {
                var game = item;
                var tile = ConsoleCoverTile(game, ConsoleTileWidth, ConsoleTileHeight);
                tile.Margin = new Thickness(0, 0, 20, 0);
                tile.MouseEnter += (s, e) => { consolePendingHover = game; consoleHoverTimer.Stop(); consoleHoverTimer.Start(); };
                tile.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount >= 2) LaunchGame(game);
                    else FocusConsoleGame(game, false);
                };
                strip.Children.Add(tile);
                consoleHomeTiles.Add((game, tile));
            }

            var all = ConsoleButton(Loc.T("Alle Spiele").ToUpper(Loc.Culture) + "  →", false, ShowConsoleLibrary);
            all.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            strip.Children.Add(all);

            var scroller = new ScrollViewer
            {
                Content = strip,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 16, 0, 0)
            };
            // Mausrad blättert waagerecht durch das Karussell
            scroller.PreviewMouseWheel += (s, e) =>
            {
                scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
                e.Handled = true;
            };
            Grid.SetRow(scroller, 2);
            layout.Children.Add(scroller);

            var keep = consoleFocus != null ? row.IndexOf(consoleFocus) : -1;
            consoleHomeIndex = keep >= 0 ? keep : 0;
            FocusConsoleGame(row[consoleHomeIndex], false, false);
        }

        private void FocusConsoleGame(GameItem game, bool bringIntoView, bool animate = true)
        {
            consoleFocus = game;
            SetConsoleBackdrop(game);

            if (consoleMode == "home")
            {
                for (int i = 0; i < consoleHomeTiles.Count; i++)
                {
                    bool focused = consoleHomeTiles[i].Game == game;
                    if (focused) consoleHomeIndex = i;
                    MarkConsoleTile(consoleHomeTiles[i].Tile, focused, animate);
                    if (focused && bringIntoView) consoleHomeTiles[i].Tile.BringIntoView();
                }

                if (consoleHomeTitle != null) consoleHomeTitle.Text = game.Name.ToUpper(Loc.Culture);
                if (consoleHomeMeta != null) consoleHomeMeta.Text = ConsoleMeta(game);
                if (consoleHomeEyebrow != null)
                    consoleHomeEyebrow.Text = (game.LastPlayed.HasValue ? Loc.T("Weiterspielen") : Loc.T("Aus deiner Bibliothek")).ToUpper(Loc.Culture);
                if (consoleHomeFav?.Child is TextBlock star)
                {
                    star.Text = game.IsFavorite ? "★" : "☆";
                    star.Foreground = game.IsFavorite ? MakeBrush("#FACC15") : System.Windows.Media.Brushes.White;
                }
            }
        }

        // ───────── BIBLIOTHEK ─────────

        private void BuildConsoleLibrary()
        {
            if (consoleView == null) return;
            consoleView.Children.Clear();

            var games = VisibleGames.ToList();
            var page = new StackPanel { Margin = new Thickness(64, TitleBarHeight + 96, 64, 40) };

            // Kopf: Titel, Anzahl, Suche
            var head = new Grid { Margin = new Thickness(0, 0, 0, 26) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(ConsoleEyebrow(GamesText(games.Count).ToUpper(Loc.Culture)));
            titles.Children.Add(ConsoleTitle(Loc.T("Bibliothek").ToUpper(Loc.Culture), 72));
            head.Children.Add(titles);

            var searchHost = new Grid { Width = 380, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
            var search = new System.Windows.Controls.TextBox { Text = consoleSearch, Height = 46, FontSize = 16 };
            var hint = new TextBlock
            {
                Text = Loc.T("Spiel suchen ..."),
                Foreground = ConsoleMuted,
                IsHitTestVisible = false,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 0, 0),
                Visibility = consoleSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed
            };
            search.TextChanged += (s, e) =>
            {
                consoleSearch = search.Text;
                hint.Visibility = consoleSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RebuildConsoleRows();
            };
            searchHost.Children.Add(search);
            searchHost.Children.Add(hint);
            Grid.SetColumn(searchHost, 1);
            head.Children.Add(searchHost);
            page.Children.Add(head);

            consoleRowsHost = new StackPanel();
            page.Children.Add(consoleRowsHost);
            RebuildConsoleRows();

            consoleLibraryScroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            consoleView.Children.Add(consoleLibraryScroll);

            if (consoleFocus == null || !games.Contains(consoleFocus)) consoleFocus = games.OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue).FirstOrDefault();
            SetConsoleBackdrop(consoleFocus);

            if (consoleSearch.Length > 0)
                Dispatcher.BeginInvoke(new Action(() => { search.Focus(); search.CaretIndex = search.Text.Length; }), DispatcherPriority.Input);
        }

        private StackPanel? consoleRowsHost;

        private void RebuildConsoleRows()
        {
            if (consoleRowsHost == null) return;
            consoleRowsHost.Children.Clear();
            var games = VisibleGames.ToList();

            // Mit Suche: alle Treffer als Raster (schlaue Suche, auch „steam:“ oder „gta“)
            if (consoleSearch.Trim().Length > 0)
            {
                var parsed = ParseSearch(consoleSearch.Trim());
                var hits = games.Select(g => (Game: g, Score: GameSearchScore(g, parsed))).Where(h => h.Score >= 0)
                    .OrderByDescending(h => h.Score).ThenBy(h => h.Game.Name, StringComparer.CurrentCultureIgnoreCase).Select(h => h.Game).ToList();
                consoleRowsHost.Children.Add(ConsoleRowTitle(Loc.T("Treffer"), hits.Count, null));
                var grid = new WrapPanel();
                foreach (var game in hits) grid.Children.Add(ConsoleLibraryTile(game, new Thickness(0, 0, 20, 24)));
                if (hits.Count == 0)
                    grid.Children.Add(new TextBlock { Text = Loc.T("Keine Treffer."), Foreground = ConsoleMuted, FontSize = 18 });
                consoleRowsHost.Children.Add(grid);
                return;
            }

            void Row(string title, IEnumerable<GameItem> items)
            {
                var list = items.ToList();
                if (list.Count == 0) return;

                var strip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 8) };
                foreach (var game in list) strip.Children.Add(ConsoleLibraryTile(game, new Thickness(0, 0, 20, 0)));
                var scroller = new ScrollViewer
                {
                    Content = strip,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Margin = new Thickness(0, 0, 0, 34)
                };
                // Umschalt + Mausrad blättert die Reihe, das normale Mausrad scrollt die Seite
                scroller.PreviewMouseWheel += (s, e) =>
                {
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
                        e.Handled = true;
                    }
                    else
                    {
                        e.Handled = true;
                        consoleLibraryScroll?.ScrollToVerticalOffset(consoleLibraryScroll.VerticalOffset - e.Delta);
                    }
                };
                consoleRowsHost.Children.Add(ConsoleRowTitle(title, list.Count, scroller));
                consoleRowsHost.Children.Add(scroller);
            }

            Row(Loc.T("Zuletzt gespielt"), games.Where(g => g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).Take(20));
            Row(Loc.T("Favoriten"), games.Where(g => g.IsFavorite).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));

            var counts = games.GroupBy(g => g.Source, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (string source in OrderLaunchers(counts.Keys))
                Row(Loc.T(source), games.Where(g => string.Equals(g.Source, source, StringComparison.OrdinalIgnoreCase)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));

            Row(Loc.T("Ungespielt"), games.Where(g => g.PlaySeconds < 60 && g.LaunchCount == 0).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
        }

        private Grid ConsoleRowTitle(string title, int count, ScrollViewer? pager)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Bottom };
            text.Children.Add(ConsoleTitle(title.ToUpper(Loc.Culture), 30));
            var number = new TextBlock { Text = count.ToString(Loc.Culture), FontSize = 14, Foreground = ConsoleMuted, Margin = new Thickness(12, 0, 0, 5), VerticalAlignment = System.Windows.VerticalAlignment.Bottom };
            number.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            text.Children.Add(number);
            row.Children.Add(text);

            if (pager != null)
            {
                // ‹ › blättert eine Reihe weiter (ohne Mausrad-Trick)
                var arrows = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                arrows.Children.Add(ConsoleIconButton("", Loc.T("Zurück"), () => pager.ScrollToHorizontalOffset(pager.HorizontalOffset - pager.ViewportWidth * 0.8)));
                var next = ConsoleIconButton("", Loc.T("Weiter"), () => pager.ScrollToHorizontalOffset(pager.HorizontalOffset + pager.ViewportWidth * 0.8));
                next.Margin = new Thickness(10, 0, 0, 0);
                arrows.Children.Add(next);
                Grid.SetColumn(arrows, 1);
                row.Children.Add(arrows);
            }
            return row;
        }

        private Border ConsoleLibraryTile(GameItem game, Thickness margin)
        {
            var tile = ConsoleCoverTile(game, ConsoleTileWidth, ConsoleTileHeight);
            tile.Margin = margin;
            tile.MouseEnter += (s, e) =>
            {
                MarkConsoleTile(tile, true, true);
                consolePendingHover = game;
                consoleHoverTimer.Stop();
                consoleHoverTimer.Start();
            };
            tile.MouseLeave += (s, e) => MarkConsoleTile(tile, false, true);
            tile.MouseLeftButtonUp += (s, e) => ShowConsoleDetail(game);   // Starten dann auf der Detailseite (oder Enter)
            return tile;
        }

        // ───────── DETAILS ─────────

        private void BuildConsoleDetail(GameItem game)
        {
            if (consoleView == null) return;
            consoleView.Children.Clear();
            consoleFocus = game;
            SetConsoleBackdrop(game);

            var layout = new Grid { Margin = new Thickness(64, TitleBarHeight + 120, 64, 56) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            consoleView.Children.Add(layout);

            var left = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 48, 0) };
            left.Children.Add(ConsoleEyebrow(Loc.T(game.Source).ToUpper(Loc.Culture)));
            var title = ConsoleTitle(game.Name.ToUpper(Loc.Culture), 96);
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.None;
            left.Children.Add(title);

            // Große Zahlen
            var stats = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 30, 0, 30) };
            void Stat(string label, string value)
            {
                var block = new StackPanel { Margin = new Thickness(0, 0, 56, 0) };
                var number = ConsoleTitle(value, 46);
                block.Children.Add(number);
                var caption = new TextBlock { Text = label.ToUpper(Loc.Culture), FontSize = 12, Foreground = ConsoleMuted, Margin = new Thickness(0, 4, 0, 0) };
                caption.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                block.Children.Add(caption);
                stats.Children.Add(block);
            }
            if (!SHide(settings.StreamHideStats)) Stat(Loc.T("Spielzeit"), game.PlaySeconds >= 60 ? FormatPlaytime(game.PlaySeconds) : "—");
            Stat(Loc.T("zuletzt gespielt"), game.LastPlayed.HasValue ? Loc.T(FormatRelative(game.LastPlayed.Value)) : Loc.T("nie"));
            Stat(Loc.T("Starts"), game.LaunchCount.ToString(Loc.Culture));
            if (game.Rating > 0) Stat(Loc.T("Bewertung"), new string('★', Math.Clamp(game.Rating, 1, 5)));
            left.Children.Add(stats);

            if (!string.IsNullOrWhiteSpace(game.Notes))
            {
                left.Children.Add(new TextBlock
                {
                    Text = game.Notes.Trim(),
                    FontSize = 16,
                    Foreground = MakeBrush("#D6D4CC"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 72,
                    TextTrimming = TextTrimming.WordEllipsis,
                    MaxWidth = 900,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 26)
                });
            }

            var actions = new WrapPanel();
            actions.Children.Add(ConsoleButton("▶  " + Loc.T("Spielen").ToUpper(Loc.Culture), true, () => LaunchGame(game), 20));
            actions.Children.Add(ConsoleButton(game.IsFavorite ? "★  " + Loc.T("Favorit").ToUpper(Loc.Culture) : "☆  " + Loc.T("Favorit").ToUpper(Loc.Culture), false, () =>
            {
                ToggleFavorite(game);
                BuildConsoleDetail(game);
            }));
            if (!string.IsNullOrEmpty(game.InstallDir) || System.IO.File.Exists(game.ExecutablePath))
                actions.Children.Add(ConsoleButton(Loc.T("Ordner").ToUpper(Loc.Culture), false, () => OpenGameLocation(game)));
            if (CanRepair(game))
                actions.Children.Add(ConsoleButton(Loc.T("Reparieren").ToUpper(Loc.Culture), false, () => RepairGame(game)));
            Border? more = null;
            more = ConsoleButton("⋯", false, () =>
            {
                var menu = BuildGameMenu(game);
                menu.PlacementTarget = more;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.IsOpen = true;
            }, 20);
            actions.Children.Add(more);
            actions.Children.Add(ConsoleButton("←  " + Loc.T("Zurück").ToUpper(Loc.Culture), false, () => ConsoleBack()));
            left.Children.Add(actions);
            layout.Children.Add(left);

            // Rechts: großes Cover mit hartem Schatten
            var cover = ConsoleCoverTile(game, 300, 450);
            cover.BorderBrush = System.Windows.Media.Brushes.White;
            cover.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            cover.Margin = new Thickness(0, 0, 10, 10);
            if (!settings.PerformanceMode) cover.SetResourceReference(UIElement.EffectProperty, "HardShadow");
            Grid.SetColumn(cover, 1);
            layout.Children.Add(cover);
        }

        // ───────── Tastatur ─────────

        private void OnConsoleKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!IsConsole || controllerMode || consoleStage?.Visibility != Visibility.Visible) return;
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;   // beim Tippen in der Suche nicht stören
            if (SpotlightLayer.Visibility == Visibility.Visible) return;

            if (consoleMode == "home" && consoleHomeTiles.Count > 0)
            {
                int index = consoleHomeIndex;
                if (e.Key == Key.Right) index = Math.Min(consoleHomeTiles.Count - 1, index + 1);
                else if (e.Key == Key.Left) index = Math.Max(0, index - 1);
                else if (e.Key == Key.Enter && consoleFocus != null) { LaunchGame(consoleFocus); e.Handled = true; return; }
                else if (e.Key == Key.Down) { ShowConsoleLibrary(); e.Handled = true; return; }
                else return;

                FocusConsoleGame(consoleHomeTiles[index].Game, true);
                e.Handled = true;
            }
            else if (consoleMode == "detail")
            {
                if (e.Key == Key.Escape || e.Key == Key.Back) { ConsoleBack(); e.Handled = true; }
                else if (e.Key == Key.Enter && consoleDetailGame != null) { LaunchGame(consoleDetailGame); e.Handled = true; }
            }
        }
    }
}
