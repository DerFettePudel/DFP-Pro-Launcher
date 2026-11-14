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
        // ───────────────────────────── Thema 2: Konsolen-Oberfläche im Cyber-Stil ─────────────────────────────
        //
        // Kopfleiste: START und BIBLIOTHEK (eigene Ansichten) plus der komplette Inhalt der schwebenden Leiste aus Thema 1
        // (Logo, alle Bereiche als Symbole inkl. Dashboard, Schnellstart, Musik) – so gelten Reihenfolge, ausgeblendete Bereiche,
        // Logo- und Farbeinstellungen auch hier. START: Spiel bildschirmfüllend + Karussell. BIBLIOTHEK: Reihen (oder als Raster
        // die normale Spiele-Seite mit allen Filtern). Eigene Detailseite pro Spiel. Alle Bereiche öffnen sich unter der Kopfleiste.

        private bool IsConsole => IsBrutal;

        private Grid? consoleStage;        // Hintergrund + aktuelle Ansicht (Start, Bibliothek, Details)
        private Grid? consoleBar;          // Kopfleiste
        private Border? consoleBarBack;
        private Grid? consoleBarNavHost;   // hier hängt der Inhalt der schwebenden Leiste
        private Grid? consoleView;
        private System.Windows.Controls.Image? consoleBackA, consoleBackB;
        private bool consoleBackAFront = true;
        private Border? consoleDim;
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
        private StackPanel? consoleRowsHost;
        private bool consoleNavigating;
        private readonly DispatcherTimer consoleRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly DispatcherTimer consoleHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
        private GameItem? consolePendingHover;

        private static readonly System.Windows.Media.Color ConsoleBase = System.Windows.Media.Color.FromRgb(0x06, 0x08, 0x0D);
        private static readonly SolidColorBrush ConsoleInk = MakeBrush("#06080D");
        private static readonly SolidColorBrush ConsoleMuted = MakeBrush("#8494AB");
        private static readonly SolidColorBrush ConsoleCyan = MakeBrush("#22D3EE");

        private void InitConsole()
        {
            if (!IsConsole || WindowRoot == null) return;

            BuildConsoleStage();
            BuildConsoleBar();
            AttachDockToConsoleBar();
            BuildConsoleSideTabs();
            InitCyber();
            ApplyConsoleNavLayout();

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

            // Startseite aus den Einstellungen: „Spiele“ = Bibliothek, sonst START
            if (settings.StartPage == "games") ShowConsoleLibrary();
            else ShowConsoleHome();
        }

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
                    // Während die Launcher-Präsentation einfliegt, nicht neu aufbauen (sonst bricht die Animation ab)
                    if (DateTime.Now < consolePresentingUntil)
                    {
                        RequestConsoleRefresh();
                        break;
                    }
                    double offset = consoleLibraryScroll?.VerticalOffset ?? 0;
                    double presentOffset = consolePresentScroll?.VerticalOffset ?? 0;
                    BuildConsoleLibrary();
                    consoleLibraryScroll?.ScrollToVerticalOffset(offset);
                    consolePresentScroll?.ScrollToVerticalOffset(presentOffset);
                    break;
                case "detail":
                    if (consoleDetailGame != null) BuildConsoleDetail(consoleDetailGame);
                    break;
                case "page":
                    RefreshHoloPage();
                    break;
            }
        }

        // ───────── Gerüst ─────────

        private void BuildConsoleStage()
        {
            consoleStage = new Grid { Background = ConsoleInk, ClipToBounds = true };
            System.Windows.Controls.Panel.SetZIndex(consoleStage, 25);

            // Beide Bilder (für das Überblenden) in einem eigenen Behälter ganz unten
            consoleBackA = NewBackdropImage();
            consoleBackB = NewBackdropImage();
            var backdropHost = new Grid { IsHitTestVisible = false };
            backdropHost.Children.Add(consoleBackA);
            backdropHost.Children.Add(consoleBackB);
            consoleStage.Children.Add(backdropHost);

            Border Shade(System.Windows.Point start, System.Windows.Point end, params (byte Alpha, double Offset)[] stops)
            {
                var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
                foreach (var (alpha, offset) in stops)
                    brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(alpha, ConsoleBase.R, ConsoleBase.G, ConsoleBase.B), offset));
                return new Border { Background = brush, IsHitTestVisible = false };
            }

            consoleStage.Children.Add(Shade(new System.Windows.Point(0, 0), new System.Windows.Point(1, 0), (0xF2, 0), (0x99, 0.45), (0x1A, 1)));
            consoleStage.Children.Add(Shade(new System.Windows.Point(0, 0), new System.Windows.Point(0, 1), (0xCC, 0), (0x00, 0.22), (0x00, 0.5), (0xF7, 1)));

            // Neonschein unten links in der Akzentfarbe (dezent)
            var accent = GetAccentColor();
            var glow = new RadialGradientBrush { Center = new System.Windows.Point(0.05, 1.0), GradientOrigin = new System.Windows.Point(0.05, 1.0), RadiusX = 0.6, RadiusY = 0.7 };
            glow.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x33, accent.R, accent.G, accent.B), 0));
            glow.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1));
            consoleStage.Children.Add(new Border { Background = glow, IsHitTestVisible = false });

            consoleDim = new Border { Background = MakeBrush("#B306080D"), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            consoleStage.Children.Add(consoleDim);

            consoleView = new Grid();
            consoleStage.Children.Add(consoleView);

            // In der Inhalts-Spalte: Bei der Seitenleiste links/rechts bleibt sie sichtbar, oben ist die Spalte 0 breit
            RootGrid.Children.Add(consoleStage);
            Grid.SetColumn(consoleStage, NavPosition == "right" ? 0 : 1);
        }

        /// <summary>Abstand von oben: Fensterleiste plus Kopfleiste (nur wenn die Navigation oben sitzt).</summary>
        private double ConsoleTopInset => IsTopNav ? TitleBarHeight + 76 : TitleBarHeight;

        // ───────── Navigation links/rechts: START und BIBLIOTHEK oben in der Seitenleiste ─────────

        private StackPanel? consoleSideTabs;
        private System.Windows.Controls.RadioButton? consoleSideHome, consoleSideLibrary;

        private void BuildConsoleSideTabs()
        {
            if (NavPanel == null) return;
            var home = ReferenceEquals(NavPanel.Parent, NavDockPanel) ? navPanelHome : NavPanel.Parent as System.Windows.Controls.Panel;
            if (home == null) return;

            System.Windows.Controls.RadioButton Item(string glyph, string title, Action open)
            {
                var icon = new TextBlock { Text = glyph, Width = 30, FontSize = 16, VerticalAlignment = System.Windows.VerticalAlignment.Center, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5) };
                icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                var label = new TextBlock { Text = Loc.T(title).ToUpper(Loc.Culture), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var item = new System.Windows.Controls.RadioButton { GroupName = "Nav", Content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Children = { icon, label } } };
                item.SetResourceReference(StyleProperty, "NavButtonStyle");
                item.Click += (s, e) => open();
                AttachNavIconHover(item);
                return item;
            }

            consoleSideHome = Item("", "Start", ShowConsoleHome);
            consoleSideLibrary = Item("", "Bibliothek", OpenConsoleLibraryOverview);

            var line = new Border { Height = 1, Margin = new Thickness(6, 10, 6, 10), IsHitTestVisible = false };
            var accent = GetAccentColor();
            var fade = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
            fade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xAA, accent.R, accent.G, accent.B), 0));
            fade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x66, 0x22, 0xD3, 0xEE), 0.6));
            fade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x22, 0xD3, 0xEE), 1));
            line.Background = fade;

            consoleSideTabs = new StackPanel { Children = { consoleSideHome, consoleSideLibrary, line } };
            home.Children.Insert(0, consoleSideTabs);
            if (!ReferenceEquals(NavPanel.Parent, home)) navPanelHomeIndex = home.Children.IndexOf(consoleSideTabs) + 1;
        }

        /// <summary>Nach jedem Anordnen der Navigation: Kopfleiste oder Seitenleiste, Bühne in der richtigen Spalte.</summary>
        private void ApplyConsoleNavLayout()
        {
            if (!IsConsole || consoleStage == null) return;

            bool top = IsTopNav;
            if (consoleBar != null) consoleBar.Visibility = top ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(consoleStage, NavPosition == "right" ? 0 : 1);
            PlaceHoloHost();
            if (top) AttachDockToConsoleBar();

            if (consoleSideTabs != null)
            {
                consoleSideTabs.Visibility = top ? Visibility.Collapsed : Visibility.Visible;
                bool compact = settings.SidebarCollapsed;
                foreach (var item in new[] { consoleSideHome, consoleSideLibrary })
                {
                    if (item?.Content is not StackPanel { Children.Count: > 1 } panel) continue;
                    panel.Children[1].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                    item.ToolTip = compact && panel.Children[1] is TextBlock label ? MakeNavToolTip(label.Text, false) : null;
                }
            }

            ApplyCyberSidebarEdge();
            UpdateConsoleTabs();
            RefreshConsole();
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

            var row = new Grid { Margin = new Thickness(40, 0, 40, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // START / BIBLIOTHEK
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // Leisten-Inhalt (Logo, Bereiche ...)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // Suche, Uhr
            consoleBar.Children.Add(row);

            var tabs = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            tabs.Children.Add(ConsoleTab("home", "Start", ShowConsoleHome));
            tabs.Children.Add(ConsoleTab("library", "Bibliothek", OpenConsoleLibraryOverview));
            row.Children.Add(tabs);

            consoleBarNavHost = new Grid { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
            Grid.SetColumn(consoleBarNavHost, 1);
            row.Children.Add(consoleBarNavHost);

            var right = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(right, 2);
            var search = ConsoleButton("", false, OpenSpotlight, 16);
            if (search.Content is TextBlock lens) lens.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            search.ToolTip = Loc.T("Suche (Strg+K)");
            right.Children.Add(search);
            consoleClock = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            consoleClock.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            right.Children.Add(consoleClock);
            row.Children.Add(right);

            WindowRoot.Children.Add(consoleBar);
        }

        /// <summary>Den Inhalt der schwebenden Leiste (Logo, Bereiche, Schnellstart, Musik) in die Kopfleiste hängen.</summary>
        private void AttachDockToConsoleBar()
        {
            if (consoleBarNavHost == null || NavDockPanel == null) return;
            if (ReferenceEquals(NavDockPanel.Parent, consoleBarNavHost.Children.OfType<Viewbox>().FirstOrDefault())) return;

            if (NavDockPanel.Parent is Viewbox oldBox) oldBox.Child = null;
            else if (NavDockPanel.Parent is System.Windows.Controls.Panel oldPanel) oldPanel.Children.Remove(NavDockPanel);

            var box = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Child = NavDockPanel, MaxHeight = 60 };
            consoleBarNavHost.Children.Clear();
            consoleBarNavHost.Children.Add(box);
        }

        private Border ConsoleTab(string mode, string title, Action open)
        {
            var text = new TextBlock { Text = Loc.T(title).ToUpper(Loc.Culture), FontSize = 18, FontWeight = FontWeights.Bold, Foreground = ConsoleMuted, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");

            var underline = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16), Opacity = 0 };
            underline.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            if (!settings.PerformanceMode) underline.SetResourceReference(UIElement.EffectProperty, "NeonGlow");

            var grid = new Grid();
            grid.Children.Add(text);
            grid.Children.Add(underline);

            var tab = new Border { Child = grid, Margin = new Thickness(0, 0, 30, 0), Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Tag = (text, underline) };
            tab.MouseEnter += (s, e) => text.Foreground = System.Windows.Media.Brushes.White;
            tab.MouseLeave += (s, e) => UpdateConsoleTabs();
            tab.MouseLeftButtonUp += (s, e) => open();
            consoleTabs[mode] = tab;
            return tab;
        }

        private void UpdateConsoleTabs()
        {
            foreach (var (mode, tab) in consoleTabs)
            {
                if (tab.Tag is not ValueTuple<TextBlock, Border> parts) continue;
                bool active = consoleMode == mode || (consoleMode == "detail" && consoleReturnMode == mode);
                parts.Item1.Foreground = active ? System.Windows.Media.Brushes.White : ConsoleMuted;
                parts.Item2.Opacity = active ? 1 : 0;
            }

            // In START und BIBLIOTHEK ist kein Bereich der Leiste ausgewählt
            if (consoleMode != "page" && NavPanel != null)
                foreach (var item in NavPanel.Children.OfType<System.Windows.Controls.RadioButton>()) item.IsChecked = false;

            // Seitenleiste (links/rechts): START bzw. BIBLIOTHEK markieren
            string side = consoleMode == "detail" ? consoleReturnMode : consoleMode;
            if (consoleSideHome != null) consoleSideHome.IsChecked = side == "home";
            if (consoleSideLibrary != null) consoleSideLibrary.IsChecked = side == "library";

            if (consoleBarBack == null) return;
            if (consoleMode == "page")
            {
                // Auf den Bereichs-Seiten: feste Fläche mit Neonlinie unten
                consoleBarBack.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
                var line = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
                var accent = GetAccentColor();
                line.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xAA, accent.R, accent.G, accent.B), 0));
                line.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x22, 0x30, 0x3F, 0x5A), 0.5));
                line.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x88, 0x22, 0xD3, 0xEE), 1));
                consoleBarBack.BorderBrush = line;
                consoleBarBack.BorderThickness = new Thickness(0, 0, 0, 1);
            }
            else
            {
                var fade = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
                fade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xB3, ConsoleBase.R, ConsoleBase.G, ConsoleBase.B), 0));
                fade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, ConsoleBase.R, ConsoleBase.G, ConsoleBase.B), 1));
                consoleBarBack.Background = fade;
                consoleBarBack.BorderThickness = new Thickness(0);
            }
        }

        private string ConsoleTimeText() => DateTime.Now.ToString(settings.Clock24h ? "HH:mm" : "h:mm tt", Loc.Culture);

        private void UpdateConsoleClock()
        {
            if (consoleClock != null) consoleClock.Text = ConsoleTimeText();
            if (consoleHudClock != null) consoleHudClock.Text = ConsoleTimeText();
            if (holoClock != null) holoClock.Text = ConsoleTimeText();
        }

        // ───────── Umschalten ─────────

        private void SetConsoleMode(string mode)
        {
            if (consoleStage == null) return;
            if (mode is "home" or "library") consoleReturnMode = mode;
            consoleMode = mode;

            consoleStage.Visibility = mode == "page" ? Visibility.Collapsed : Visibility.Visible;
            if (mode != "page") HideHoloPage();
            if (consoleDim != null) consoleDim.Visibility = mode == "library" ? Visibility.Visible : Visibility.Collapsed;
            UpdateConsoleTabs();
        }

        private void ShowConsoleHome()
        {
            consolePresentedLauncher = null;   // eine offene Launcher-Präsentation gilt nur innerhalb der Bibliothek
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

        /// <summary>Einen Bereich (Dashboard, Einstellungen, Spiele als Raster ...) unter der Kopfleiste öffnen.</summary>
        private void ShowConsolePage(string key)
        {
            SetConsoleMode("page");
            consoleNavigating = true;
            try { NavigateTo(key); }
            finally { consoleNavigating = false; }
        }

        /// <summary>Aus ShowView: Ein Bereich wird geöffnet (Symbol in der Kopfleiste, Suche, Meldung ...) → Seite unter der Kopfleiste zeigen.</summary>
        private void OnConsoleShowView()
        {
            if (!IsConsole || consoleStage == null || consoleMode == "page") return;
            SetConsoleMode("page");
        }

        /// <summary>Am Anfang von NavigateTo: „Spiele“ von außen (Meldungen, Suche) öffnet in Thema 2 die BIBLIOTHEK.</summary>
        private bool HandleConsoleNavigation(string key)
        {
            if (!IsConsole || consoleNavigating || consoleStage == null) return false;
            if (key == "games") { ShowConsoleLibrary(); return true; }
            return false;
        }

        private bool ConsoleBack()
        {
            if (IsConsole && consoleMode == "library" && consolePresentedLauncher != null)
            {
                CloseConsoleLauncher();   // Präsentation eines Launchers schließen
                return true;
            }
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
            back.Effect = !wide && image != null && !settings.PerformanceMode ? new System.Windows.Media.Effects.BlurEffect { Radius = 40 } : null;
            back.Margin = wide ? new Thickness(0) : new Thickness(-70);

            var duration = TimeSpan.FromMilliseconds(settings.PageAnimations && !settings.PerformanceMode ? 380 : 1);
            back.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(image == null ? 0 : wide ? 1 : 0.6, duration));
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

        // ───────── Bausteine ─────────

        private const double ConsoleTileWidth = 160, ConsoleTileHeight = 240;
        private const double ConsoleTileRadius = 10;

        private Border ConsoleCoverTile(GameItem game, double width, double height)
        {
            double inner = ConsoleTileRadius - 2;
            var image = new Grid { Background = MakeBrush("#0E121B"), Clip = new RectangleGeometry(new Rect(0, 0, width - 4, height - 4), inner, inner) };
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
                var name = new TextBlock { Text = game.Name, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(12) };
                name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                image.Children.Add(name);
            }

            if (game.IsFavorite)
                image.Children.Add(new TextBlock { Text = "★", FontSize = 16, Foreground = MakeBrush("#FACC15"), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = System.Windows.VerticalAlignment.Top, Margin = new Thickness(0, 6, 9, 0) });

            return new Border
            {
                Width = width,
                Height = height,
                Child = image,
                CornerRadius = new CornerRadius(ConsoleTileRadius),
                BorderThickness = new Thickness(2),
                BorderBrush = MakeBrush("#22FFFFFF"),
                Cursor = System.Windows.Input.Cursors.Hand,
                RenderTransform = new TranslateTransform(),
                ContextMenu = BuildGameMenu(game),
                ToolTip = game.Name
            };
        }

        private void MarkConsoleTile(Border tile, bool focused, bool animate)
        {
            if (focused) tile.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else tile.BorderBrush = MakeBrush("#22FFFFFF");

            if (focused && !settings.PerformanceMode) tile.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
            else tile.Effect = null;

            if (tile.RenderTransform is TranslateTransform move)
            {
                double target = focused ? -12 : 0;
                if (animate && settings.HoverAnimations)
                    move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                else
                {
                    move.BeginAnimation(TranslateTransform.YProperty, null);
                    move.Y = target;
                }
            }
        }

        /// <summary>Knopf im Stil von Thema 2 (schräg, Neonkante): primär = in der Akzentfarbe.</summary>
        private System.Windows.Controls.Button ConsoleButton(string text, bool primary, Action click, double fontSize = 16)
        {
            var label = new TextBlock { Text = text, FontSize = fontSize, FontWeight = FontWeights.Bold, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");

            var button = new System.Windows.Controls.Button
            {
                Content = label,
                Padding = primary ? new Thickness(30, 11, 30, 11) : new Thickness(20, 11, 20, 11),
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = primary ? MakeBrush(InkFor(GetAccentColor())) : System.Windows.Media.Brushes.White
            };
            if (primary)
            {
                button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "AccentBrush");
            }
            button.Click += (s, e) => click();
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
                LineHeight = size * 1.02,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            if (!settings.PerformanceMode && size >= 40) title.SetResourceReference(UIElement.EffectProperty, "TitleGlow");
            return title;
        }

        private TextBlock ConsoleEyebrow(string text)
        {
            var eyebrow = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = ConsoleCyan };
            eyebrow.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            return eyebrow;
        }

        private string ConsoleMeta(GameItem game)
        {
            var parts = new List<string>();
            if (game.PlaySeconds >= 60 && !SHide(settings.StreamHideStats)) parts.Add(Loc.T("Spielzeit") + " " + FormatPlaytime(game.PlaySeconds));
            parts.Add(game.LastPlayed.HasValue ? Loc.T("zuletzt gespielt") + " " + Loc.T(FormatRelative(game.LastPlayed.Value)) : Loc.T("nie gespielt"));
            parts.Add(Loc.T(game.Source));
            return string.Join("   ·   ", parts);
        }

        // ───────── START ─────────

        private TextBlock? consoleHomeTitle, consoleHomeEyebrow, consoleHomeNote, consoleHudClock;
        private WrapPanel? consoleHomeChips;
        private System.Windows.Controls.Button? consoleHomeFav;

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
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            consoleView.Children.Add(layout);

            // Rechts oben: Infotafel (Begrüßung, Datum, Woche, Bibliothek, Gratis-Spiele); bei schmalem Fenster ausgeblendet
            consoleHudClock = null;
            var hud = BuildConsoleHud();
            hud.ClipToBounds = true;
            Grid.SetColumn(hud, 1);
            Grid.SetRowSpan(hud, 2);
            layout.Children.Add(hud);
            layout.SizeChanged += (s, e) =>
            {
                hud.Visibility = e.NewSize.Width >= 1180 ? Visibility.Visible : Visibility.Collapsed;
                FitConsoleHud(hud, layout);
            };
            hud.Visibility = consoleView.ActualWidth == 0 || consoleView.ActualWidth >= 1180 ? Visibility.Visible : Visibility.Collapsed;

            if (row.Count == 0)
            {
                var empty = ConsoleTitle(Loc.T("Noch keine Spiele gefunden."), 44);
                empty.Margin = new Thickness(64, 0, 64, 0);
                empty.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                layout.Children.Add(empty);
                SetConsoleBackdrop(null);
                return;
            }

            var info = new StackPanel { Margin = new Thickness(64, 0, 40, 26), MaxWidth = 1200, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            Grid.SetRow(info, 1);
            consoleHomeEyebrow = ConsoleEyebrow(string.Empty);
            consoleHomeTitle = ConsoleTitle(string.Empty, 68);
            consoleHomeChips = new WrapPanel { Margin = new Thickness(0, 14, 0, 6) };
            consoleHomeNote = new TextBlock
            {
                FontSize = 15,
                Foreground = MakeBrush("#C9D4E3"),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.WordEllipsis,
                MaxHeight = 44,
                MaxWidth = 820,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0)
            };
            info.Children.Add(consoleHomeEyebrow);
            info.Children.Add(consoleHomeTitle);
            info.Children.Add(consoleHomeChips);
            info.Children.Add(consoleHomeNote);
            info.Children.Add(new Border { Height = 18 });

            var buttons = new WrapPanel();
            buttons.Children.Add(ConsoleButton("▶  " + Loc.T("Spielen").ToUpper(Loc.Culture), true, () => { if (consoleFocus != null) LaunchGame(consoleFocus); }, 18));
            buttons.Children.Add(ConsoleButton(Loc.T("Details").ToUpper(Loc.Culture), false, () => { if (consoleFocus != null) ShowConsoleDetail(consoleFocus); }));
            consoleHomeFav = ConsoleButton("☆", false, () => { if (consoleFocus != null) ToggleFavorite(consoleFocus); }, 18);
            buttons.Children.Add(consoleHomeFav);
            buttons.Children.Add(ConsoleButton("⋯", false, () =>
            {
                if (consoleFocus == null) return;
                var menu = BuildGameMenu(consoleFocus);
                menu.IsOpen = true;
            }, 18));
            info.Children.Add(buttons);
            layout.Children.Add(info);

            var strip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(64, 18, 64, 52) };
            foreach (var item in row)
            {
                var game = item;
                var tile = ConsoleCoverTile(game, ConsoleTileWidth, ConsoleTileHeight);
                tile.Margin = new Thickness(0, 0, 18, 0);
                tile.MouseEnter += (s, e) => { consolePendingHover = game; consoleHoverTimer.Stop(); consoleHoverTimer.Start(); };
                tile.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount >= 2) LaunchGame(game);   // Doppelklick startet, ein Klick wählt aus
                    else FocusConsoleGame(game, false);
                };
                strip.Children.Add(tile);
                consoleHomeTiles.Add((game, tile));
            }

            var all = ConsoleButton(Loc.T("Alle Spiele").ToUpper(Loc.Culture) + "  →", false, ShowConsoleLibrary);
            all.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            strip.Children.Add(all);

            var scroller = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 18, 0, 0) };
            scroller.PreviewMouseWheel += (s, e) =>
            {
                scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
                e.Handled = true;
            };

            // Überschrift über dem Karussell mit feiner Neonlinie
            var stripHead = new Grid { Margin = new Thickness(64, 0, 64, -8) };
            stripHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stripHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var stripLabel = ConsoleEyebrow("// " + Loc.T("Deine Spiele").ToUpper(Loc.Culture) + "   " + row.Count.ToString(Loc.Culture));
            stripLabel.Margin = new Thickness(0, 0, 16, 0);
            stripHead.Children.Add(stripLabel);
            var rule = ConsoleRule();
            rule.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            rule.Margin = new Thickness(0, 0, 0, 4);
            Grid.SetColumn(rule, 1);
            stripHead.Children.Add(rule);

            var bottom = new StackPanel();
            bottom.Children.Add(stripHead);
            bottom.Children.Add(scroller);
            Grid.SetRow(bottom, 2);
            Grid.SetColumnSpan(bottom, 2);
            layout.Children.Add(bottom);

            int keep = consoleFocus != null ? row.IndexOf(consoleFocus) : -1;
            consoleHomeIndex = keep >= 0 ? keep : 0;
            FocusConsoleGame(row[consoleHomeIndex], false, false);
        }

        private void FocusConsoleGame(GameItem game, bool bringIntoView, bool animate = true)
        {
            consoleFocus = game;
            SetConsoleBackdrop(game);
            if (consoleMode != "home") return;

            for (int i = 0; i < consoleHomeTiles.Count; i++)
            {
                bool focused = consoleHomeTiles[i].Game == game;
                if (focused) consoleHomeIndex = i;
                MarkConsoleTile(consoleHomeTiles[i].Tile, focused, animate);
                if (focused && bringIntoView) consoleHomeTiles[i].Tile.BringIntoView();
            }

            if (consoleHomeTitle != null) consoleHomeTitle.Text = game.Name;
            if (consoleHomeChips != null) FillConsoleChips(consoleHomeChips, game);
            if (consoleHomeNote != null)
            {
                consoleHomeNote.Text = string.IsNullOrWhiteSpace(game.Notes) ? string.Empty : game.Notes.Trim();
                consoleHomeNote.Visibility = consoleHomeNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (consoleHomeEyebrow != null)
                consoleHomeEyebrow.Text = "// " + (game.LastPlayed.HasValue ? Loc.T("Weiterspielen") : Loc.T("Aus deiner Bibliothek")).ToUpper(Loc.Culture);
            if (consoleHomeFav?.Content is TextBlock star)
            {
                star.Text = game.IsFavorite ? "★" : "☆";
                star.Foreground = game.IsFavorite ? MakeBrush("#FACC15") : System.Windows.Media.Brushes.White;
            }
        }

        // ───────── BIBLIOTHEK ─────────

        private void BuildConsoleLibrary()
        {
            if (consoleView == null) return;
            consoleView.Children.Clear();

            var games = VisibleGames.ToList();
            var page = new StackPanel { Margin = new Thickness(64, ConsoleTopInset + 20, 64, 40) };

            var head = new Grid { Margin = new Thickness(0, 0, 0, 24) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(ConsoleEyebrow("// " + GamesText(games.Count).ToUpper(Loc.Culture)));
            titles.Children.Add(ConsoleTitle(Loc.T("Bibliothek"), 56));
            head.Children.Add(titles);

            // Rechts: Suche und Umschalten auf die volle Spiele-Seite (alle Filter, Sammlungen, smarte Listen, Sortierung)
            var tools = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
            var searchHost = new Grid { Width = 360, Margin = new Thickness(0, 0, 14, 0) };
            var search = new System.Windows.Controls.TextBox { Text = consoleSearch, Height = 44, FontSize = 15 };
            var hint = new TextBlock { Text = Loc.T("Spiel suchen ..."), Foreground = ConsoleMuted, IsHitTestVisible = false, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0), Visibility = consoleSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
            search.TextChanged += (s, e) =>
            {
                consoleSearch = search.Text;
                hint.Visibility = consoleSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RebuildConsoleRows();
            };
            searchHost.Children.Add(search);
            searchHost.Children.Add(hint);
            tools.Children.Add(searchHost);
            var grid = ConsoleButton("▦  " + Loc.T("Raster-Ansicht").ToUpper(Loc.Culture), false, () => ShowConsolePage("games"));
            grid.ToolTip = Loc.T("Die volle Spiele-Seite mit allen Filtern, Sammlungen, smarten Listen und der Sortierung");
            tools.Children.Add(grid);
            Grid.SetColumn(tools, 1);
            head.Children.Add(tools);
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

            // War gerade ein Launcher geöffnet (z. B. zurück aus den Spiel-Details): ohne Einflug-Animation wieder zeigen
            consolePresentScroll = null;
            if (consolePresentedLauncher != null)
            {
                if (games.Any(g => string.Equals(g.Source, consolePresentedLauncher, StringComparison.OrdinalIgnoreCase)))
                    PresentConsoleLauncher(consolePresentedLauncher, false);
                else consolePresentedLauncher = null;
            }
        }

        private void RebuildConsoleRows()
        {
            if (consoleRowsHost == null) return;
            consoleRowsHost.Children.Clear();
            var games = VisibleGames.ToList();

            if (consoleSearch.Trim().Length > 0)
            {
                var parsed = ParseSearch(consoleSearch.Trim());
                var hits = games.Select(g => (Game: g, Score: GameSearchScore(g, parsed))).Where(h => h.Score >= 0)
                    .OrderByDescending(h => h.Score).ThenBy(h => h.Game.Name, StringComparer.CurrentCultureIgnoreCase).Select(h => h.Game).ToList();
                consoleRowsHost.Children.Add(ConsoleRowTitle(Loc.T("Treffer"), hits.Count, null));
                var wrap = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
                foreach (var game in hits) wrap.Children.Add(ConsoleLibraryTile(game, new Thickness(0, 0, 18, 24)));
                if (hits.Count == 0) wrap.Children.Add(new TextBlock { Text = Loc.T("Keine Treffer."), Foreground = ConsoleMuted, FontSize = 18 });
                consoleRowsHost.Children.Add(wrap);
                return;
            }

            void Row(string title, IEnumerable<GameItem> items)
            {
                var list = items.ToList();
                if (list.Count == 0) return;

                var strip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 8) };
                foreach (var game in list) strip.Children.Add(ConsoleLibraryTile(game, new Thickness(0, 0, 18, 0)));
                var scroller = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 30) };
                // Umschalt + Mausrad blättert die Reihe, das normale Mausrad scrollt die Seite
                scroller.PreviewMouseWheel += (s, e) =>
                {
                    e.Handled = true;
                    if (Keyboard.Modifiers == ModifierKeys.Shift) scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
                    else consoleLibraryScroll?.ScrollToVerticalOffset(consoleLibraryScroll.VerticalOffset - e.Delta);
                };
                consoleRowsHost.Children.Add(ConsoleRowTitle(title, list.Count, scroller));
                consoleRowsHost.Children.Add(scroller);
            }

            // Launcher nicht mehr als einzelne Reihen, sondern als Logo-Leiste: Ein Klick präsentiert die Spiele des Launchers
            var counts = games.Where(g => !string.IsNullOrEmpty(g.Source))
                .GroupBy(g => g.Source, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            if (counts.Count > 0)
            {
                consoleRowsHost.Children.Add(ConsoleRowTitle(Loc.T("Launcher"), counts.Count, null));
                var hint = new TextBlock { Text = Loc.T("Klick auf ein Logo zeigt alle Spiele dieses Launchers"), FontSize = 13, Foreground = ConsoleMuted, Margin = new Thickness(0, 4, 0, 0) };
                consoleRowsHost.Children.Add(hint);
                var logos = new WrapPanel { Margin = new Thickness(0, 16, 0, 26) };
                foreach (string source in OrderLaunchers(counts.Keys))
                    logos.Children.Add(ConsoleLauncherTile(source, counts[source]));
                consoleRowsHost.Children.Add(logos);
            }

            Row(Loc.T("Zuletzt gespielt"), games.Where(g => g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).Take(20));
            Row(Loc.T("Favoriten"), games.Where(g => g.IsFavorite).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));

            // Eigene Sammlungen als eigene Reihen
            foreach (string collection in settings.CollectionNames)
                Row("📁 " + collection, games.Where(g => g.Collections.Contains(collection)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));

            Row(Loc.T("Ungespielt"), games.Where(g => g.PlaySeconds < 60 && g.LaunchCount == 0).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
        }

        private Grid ConsoleRowTitle(string title, int count, ScrollViewer? pager)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Bottom };
            text.Children.Add(ConsoleTitle(title, 24));
            var number = new TextBlock { Text = count.ToString(Loc.Culture), FontSize = 13, Foreground = ConsoleCyan, Margin = new Thickness(12, 0, 0, 4), VerticalAlignment = System.Windows.VerticalAlignment.Bottom };
            number.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            text.Children.Add(number);
            row.Children.Add(text);

            if (pager != null)
            {
                var arrows = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                var back = ConsoleButton("", false, () => pager.ScrollToHorizontalOffset(pager.HorizontalOffset - pager.ViewportWidth * 0.8), 13);
                var next = ConsoleButton("", false, () => pager.ScrollToHorizontalOffset(pager.HorizontalOffset + pager.ViewportWidth * 0.8), 13);
                foreach (var b in new[] { back, next })
                {
                    if (b.Content is TextBlock glyph) glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                    b.Padding = new Thickness(14, 8, 14, 8);
                    arrows.Children.Add(b);
                }
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

            var layout = new Grid { Margin = new Thickness(64, ConsoleTopInset + 34, 64, 50) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            consoleView.Children.Add(layout);

            var left = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 48, 0) };
            left.Children.Add(ConsoleEyebrow("// " + Loc.T(game.Source).ToUpper(Loc.Culture)));
            var title = ConsoleTitle(game.Name, 72);
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.None;
            left.Children.Add(title);

            var stats = new WrapPanel { Margin = new Thickness(0, 28, 0, 26) };
            void Stat(string label, string value)
            {
                var block = new Border { Margin = new Thickness(0, 0, 14, 10), Padding = new Thickness(18, 10, 22, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
                block.SetResourceReference(Border.BackgroundProperty, "CardFillBrush");
                block.SetResourceReference(Border.BorderBrushProperty, "CardEdgeBrush");
                var stack = new StackPanel();
                stack.Children.Add(ConsoleTitle(value, 30));
                var caption = new TextBlock { Text = label.ToUpper(Loc.Culture), FontSize = 11, Foreground = ConsoleCyan, Margin = new Thickness(0, 2, 0, 0) };
                caption.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                stack.Children.Add(caption);
                block.Child = stack;
                stats.Children.Add(block);
            }
            if (!SHide(settings.StreamHideStats)) Stat(Loc.T("Spielzeit"), game.PlaySeconds >= 60 ? FormatPlaytime(game.PlaySeconds) : "—");
            Stat(Loc.T("zuletzt gespielt"), game.LastPlayed.HasValue ? Loc.T(FormatRelative(game.LastPlayed.Value)) : Loc.T("nie"));
            Stat(Loc.T("Starts"), game.LaunchCount.ToString(Loc.Culture));
            if (game.Rating > 0) Stat(Loc.T("Bewertung"), new string('★', Math.Clamp(game.Rating, 1, 5)));
            if (KnownGameSize(game) is long bytes && bytes > 0) Stat(Loc.T("Größe"), FormatBytes(bytes));
            left.Children.Add(stats);

            if (!string.IsNullOrWhiteSpace(game.Notes))
            {
                left.Children.Add(new TextBlock
                {
                    Text = game.Notes.Trim(),
                    FontSize = 15,
                    Foreground = MakeBrush("#C9D4E3"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 66,
                    TextTrimming = TextTrimming.WordEllipsis,
                    MaxWidth = 900,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 22)
                });
            }

            // Alle wichtigen Aktionen direkt anklickbar, der Rest im ⋯-Menü
            var actions = new WrapPanel();
            actions.Children.Add(ConsoleButton("▶  " + Loc.T("Spielen").ToUpper(Loc.Culture), true, () => LaunchGame(game), 18));
            actions.Children.Add(ConsoleButton((game.IsFavorite ? "★  " : "☆  ") + Loc.T("Favorit").ToUpper(Loc.Culture), false, () => { ToggleFavorite(game); BuildConsoleDetail(game); }));
            actions.Children.Add(ConsoleButton("📝  " + Loc.T("Details").ToUpper(Loc.Culture), false, () => ShowGameDetails(game)));
            if (!string.IsNullOrEmpty(game.InstallDir) || System.IO.File.Exists(game.ExecutablePath))
                actions.Children.Add(ConsoleButton("📂  " + Loc.T("Ordner").ToUpper(Loc.Culture), false, () => OpenGameLocation(game)));
            actions.Children.Add(ConsoleButton("💾  " + Loc.T("Jetzt sichern").ToUpper(Loc.Culture), false, () => _ = BackupNowAsync(game)));
            if (game.Source == "Steam" && game.AppId.Length > 0)
                actions.Children.Add(ConsoleButton("🏆  " + Loc.T("Erfolge").ToUpper(Loc.Culture), false, () => _ = ShowSteamAchievementsAsync(game)));
            if (CanRepair(game))
                actions.Children.Add(ConsoleButton("🛠  " + Loc.T("Reparieren").ToUpper(Loc.Culture), false, () => RepairGame(game)));
            System.Windows.Controls.Button? more = null;
            more = ConsoleButton("⋯", false, () =>
            {
                var menu = BuildGameMenu(game);
                menu.PlacementTarget = more;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.IsOpen = true;
            }, 18);
            actions.Children.Add(more);
            actions.Children.Add(ConsoleButton("←  " + Loc.T("Zurück").ToUpper(Loc.Culture), false, () => ConsoleBack()));
            left.Children.Add(actions);
            layout.Children.Add(left);

            var cover = ConsoleCoverTile(game, 300, 450);
            cover.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            cover.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            if (!settings.PerformanceMode) cover.SetResourceReference(UIElement.EffectProperty, "HardShadow");
            Grid.SetColumn(cover, 1);
            layout.Children.Add(cover);
        }

        // ───────── Tastatur ─────────

        private void OnConsoleKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!IsConsole || controllerMode || consoleStage?.Visibility != Visibility.Visible) return;
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
            if (SpotlightLayer.Visibility == Visibility.Visible) return;

            if (consoleMode == "library" && consolePresentedLauncher != null && e.Key is Key.Escape or Key.Back)
            {
                CloseConsoleLauncher();
                e.Handled = true;
                return;
            }

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
