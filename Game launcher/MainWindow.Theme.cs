using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Design-Thema: Thema 1 „Nova“ / Thema 2 „Brutal“ ─────────────────────────────
        //
        // Das Thema gilt ab dem Start (Wechsel = Neustart). Thema 2 liest Themes\Brutal.xaml ein und ersetzt damit gleichnamige
        // Farben, Schriften und Bausteine aus MainWindow.xaml. Was erst im Code entsteht (Spielkarten, Widgets, Meldungen),
        // wird beim Erscheinen eckig und flach gemacht (OnBrutalElementLoaded).

        private const string PaperColor = "#F2F0E6";   // helle Linien und Schrift in Thema 2

        private string activeUiTheme = "nova";
        private bool IsBrutal => activeUiTheme == "brutal";

        private static bool brutalActive;
        private static bool brutalPerformance;
        private static DropShadowEffect? brutalHardShadow;
        private static bool brutalHandlersRegistered;
        private string brutalBakedAccent = string.Empty;
        private ResourceDictionary? brutalDictionary;

        /// <summary>Direkt nach dem Laden der Einstellungen, bevor irgendetwas gebaut wird.</summary>
        private bool uiThemeChosen;

        private void ApplyUiThemeAtStartup()
        {
            // Nur beim ersten Laden: Später (Einstellungen importieren, Profil) gilt das Thema erst nach einem Neustart
            if (uiThemeChosen) return;
            uiThemeChosen = true;

            activeUiTheme = settings.UiTheme == "brutal" ? "brutal" : "nova";
            if (!IsBrutal) return;

            if (!LoadBrutalDictionary())
            {
                activeUiTheme = "nova";   // Datei fehlt oder ist kaputt: lieber Thema 1 als ein halbes Thema 2
                return;
            }

            FontFamily = new System.Windows.Media.FontFamily("Bahnschrift, Segoe UI");
            brutalActive = true;
            brutalPerformance = settings.PerformanceMode;
            RegisterBrutalHandlers();
        }

        private static string HexOf(System.Windows.Media.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        /// <summary>Schrift auf der Akzentfarbe: dunkel bei hellen Akzenten, sonst hell.</summary>
        private static string InkFor(System.Windows.Media.Color accent)
            => 0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B > 150 ? "#0B0B0B" : "#FFFFFF";

        private bool LoadBrutalDictionary()
        {
            try
            {
                var accent = ParseColor(settings.AccentColor, "#8B5CF6");
                using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Game_launcher.Themes.Brutal.xaml");
                if (stream == null) return false;
                using var reader = new StreamReader(stream);
                string xaml = reader.ReadToEnd()
                    .Replace("__ACCENT__", HexOf(accent))
                    .Replace("__INK__", InkFor(accent))
                    .Replace("__PAPER__", PaperColor);

                var dictionary = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
                foreach (object key in dictionary.Keys.Cast<object>().ToList())
                    Resources[key] = dictionary[key];

                brutalDictionary = dictionary;
                brutalBakedAccent = HexOf(accent);

                var shadow = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 5, Direction = 315, Opacity = 1, Color = accent };
                shadow.Freeze();
                brutalHardShadow = shadow;
                return true;
            }
            catch (Exception ex)
            {
                LogError("Thema 2 konnte nicht geladen werden", ex);
                return false;
            }
        }

        /// <summary>
        /// Läuft am Ende von ApplyTheme: Der Code setzt dort Farben für Thema 1 (Hintergrund, Flächen, Kanten), die hier
        /// wieder durch die von Thema 2 ersetzt werden. Hat sich die Akzentfarbe geändert, wird das Thema neu eingelesen.
        /// </summary>
        private void ApplyBrutalTokens()
        {
            if (!IsBrutal) return;

            if (brutalBakedAccent != HexOf(ParseColor(settings.AccentColor, "#8B5CF6"))) LoadBrutalDictionary();
            if (brutalDictionary == null) return;

            foreach (string key in new[]
            {
                "AppBackgroundBrush", "SidebarBrush", "CardBrush", "CardBorderBrush", "SurfaceBrush", "CardFillBrush",
                "CardEdgeBrush", "ControlBrush", "ControlHoverBrush", "ControlBorderBrush"
            })
            {
                if (brutalDictionary.Contains(key)) Resources[key] = brutalDictionary[key];
            }

            // Pinsel, die der Code direkt für Kacheln und Karten benutzt
            BrushCardBg = MakeBrush("#121212");
            BrushCardBorder = MakeBrush("#3A3A3A");
            BrushFooter = MakeBrush("#181818");
            BrushTileHover = MakeBrush("#202020");
            brutalPerformance = settings.PerformanceMode;
        }

        // ───────── Alles, was im Code entsteht: eckig und flach ─────────

        private void RegisterBrutalHandlers()
        {
            if (brutalHandlersRegistered) return;
            brutalHandlersRegistered = true;

            // WPF meldet „Loaded“ nur Elementen, die selbst darauf hören (ein allgemeiner Klassen-Handler wird übergangen).
            // Darum bekommt jeder Rahmen, Text, Bereich und jedes Rechteck über einen Standard-Stil einen eigenen Handler.
            foreach (var type in new[]
            {
                typeof(Border), typeof(TextBlock), typeof(Grid), typeof(StackPanel), typeof(WrapPanel), typeof(DockPanel),
                typeof(Canvas), typeof(System.Windows.Shapes.Rectangle), typeof(System.Windows.Controls.Image)
            })
            {
                var style = new Style(type, TryFindResource(type) as Style);
                style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnBrutalElementLoaded)));
                Resources[type] = style;
            }

            // Manche Karten setzen ihren abgerundeten Ausschnitt bei jeder Größenänderung neu
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler((s, e) => { if (brutalActive && s is UIElement element) FlattenClip(element); }), true);
        }

        private static void OnBrutalElementLoaded(object sender, RoutedEventArgs e)
        {
            if (!brutalActive) return;

            try
            {
                // Feste Nova-Blautöne (z. B. Banner #1C2233) werden zu neutralem Grau gleicher Helligkeit
                if (sender is Border tinted)
                {
                    if (Neutralize(tinted.Background) is { } background) tinted.Background = background;
                    if (Neutralize(tinted.BorderBrush) is { } edge) tinted.BorderBrush = edge;
                }
                else if (sender is System.Windows.Controls.Panel panel && Neutralize(panel.Background) is { } panelBackground)
                {
                    panel.Background = panelBackground;
                }

                switch (sender)
                {
                    case Border border:
                        if (border.CornerRadius != default) border.CornerRadius = new CornerRadius(0);
                        // Weiche Schatten werden zu harten, versetzten (im Leistungsmodus ganz ohne)
                        if (border.Effect is DropShadowEffect soft && soft.BlurRadius > 0.5)
                            border.Effect = brutalPerformance ? null : brutalHardShadow;
                        break;

                    case TextBlock text when text.Effect is DropShadowEffect:
                        text.Effect = null;   // Textschatten passen nicht zum flachen Stil
                        break;

                    case System.Windows.Shapes.Rectangle rectangle:
                        rectangle.RadiusX = 0;
                        rectangle.RadiusY = 0;
                        break;
                }

                if (sender is UIElement element) FlattenClip(element);
            }
            catch { }
        }

        /// <summary>
        /// Dunkle, bläuliche Flächen aus Thema 1 (Navy-Töne) als neutrales Grau. Akzentfarben und helle Farben bleiben,
        /// auch Pinsel aus Ressourcen (die liefern in Thema 2 ohnehin neutrale Werte). Null = nichts ändern.
        /// </summary>
        private static System.Windows.Media.Brush? Neutralize(System.Windows.Media.Brush? brush)
        {
            if (brush is not SolidColorBrush solid) return null;
            var c = solid.Color;
            int average = (c.R + c.G + c.B) / 3;
            if (average >= 90 || c.B <= c.R + 8 || c.B <= c.G + 3) return null;

            byte level = (byte)average;
            var neutral = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, level, level, level));
            neutral.Freeze();
            return neutral;
        }

        /// <summary>Abgerundete Ausschnitte (z. B. Cover in Karten) werden eckig.</summary>
        private static void FlattenClip(UIElement element)
        {
            if (element.Clip is RectangleGeometry clip && (clip.RadiusX > 0 || clip.RadiusY > 0))
                element.Clip = new RectangleGeometry(clip.Rect);
        }

        // ───────── Kopfleiste oben (Thema 2) ─────────

        private const double TitleBarHeight = 34;   // eigene Fensterleiste mit Minimieren/Maximieren/Schließen

        /// <summary>Inhalt beginnt unter Fensterleiste und der Kopfleiste der Konsolen-Oberfläche (76 hoch).</summary>
        private Thickness TopNavContentMargin() => new(64, TitleBarHeight + 76 + 28, 64, 24);

        /// <summary>Form der waagerechten Leiste: oben als durchgehende Kopfleiste, unten als schwebende Pille.</summary>
        private void ApplyDockShape(bool top)
        {
            if (NavDock == null) return;

            NavDock.BeginAnimation(FrameworkElement.MarginProperty, null);   // Anheben über die Taskleiste gilt nur unten
            // Thema 2: Die Konsolen-Oberfläche hat ihre eigene Kopfleiste (START / BIBLIOTHEK / MEHR), die alte Leiste bleibt verborgen
            if (top && IsConsole)
            {
                NavDock.Visibility = Visibility.Collapsed;
                dockTaskbarLift = 0;
                return;
            }
            if (top)
            {
                dockTaskbarLift = 0;
                NavDock.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                NavDock.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                NavDock.Margin = new Thickness(0, TitleBarHeight, 0, 0);
                NavDock.CornerRadius = new CornerRadius(0);
                NavDock.BorderThickness = new Thickness(0, 2, 0, 2);
                NavDock.SetResourceReference(Border.BorderBrushProperty, "PaperBrush");
                NavDock.Padding = new Thickness(18, 8, 18, 8);
                NavDock.RenderTransform = null;
                NavDock.BeginAnimation(UIElement.OpacityProperty, null);
                NavDock.Opacity = 1;
            }
            else
            {
                NavDock.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
                NavDock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                NavDock.Margin = new Thickness(0, 0, 0, DockBaseMargin + dockTaskbarLift);
                NavDock.CornerRadius = new CornerRadius(26);
                NavDock.BorderThickness = new Thickness(1);
                NavDock.BorderBrush = MakeBrush("#33FFFFFF");
                NavDock.Padding = new Thickness(10, 6, 10, 6);
            }
        }

        // ───────── Einstellungen: Thema wählen ─────────

        private void BuildUiThemeChoice()
        {
            if (UiThemePanel == null) return;
            UiThemePanel.Children.Clear();

            foreach (var (id, title, text) in new[]
            {
                ("nova", "Thema 1 · Nova", "Weich und rund, Glas und Leuchten, dein Hintergrundbild."),
                ("brutal", "Thema 2 · Konsole", "Ganz anderer Aufbau: Das Spiel füllt den Bildschirm, Karussell und Reihen statt Kartenraster, eigene Detailseite, oben nur Start, Bibliothek und Mehr.")
            })
            {
                string theme = id;
                bool active = activeUiTheme == theme;
                bool chosen = settings.UiTheme == theme;

                var content = new StackPanel { Width = 300, Margin = new Thickness(4) };
                content.Children.Add(new TextBlock { Text = Loc.T(title), FontSize = 17, FontWeight = FontWeights.Bold, FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont") });
                content.Children.Add(new TextBlock { Text = Loc.T(text), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Opacity = 0.85 });
                if (active)
                    content.Children.Add(new TextBlock { Text = Loc.T("● aktiv"), FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) });
                else if (chosen)
                    content.Children.Add(new TextBlock { Text = Loc.T("Nach dem Neustart aktiv"), FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) });

                var chip = new System.Windows.Controls.RadioButton
                {
                    Content = content,
                    GroupName = "UiTheme",
                    Style = (Style)FindResource("ChipStyle"),
                    IsChecked = chosen,
                    Margin = new Thickness(0, 0, 12, 12)
                };
                chip.Checked += (s, e) => ChooseUiTheme(theme);
                UiThemePanel.Children.Add(chip);
            }
        }

        private void ChooseUiTheme(string theme)
        {
            if (isLoadingSettings || settings.UiTheme == theme) return;

            settings.UiTheme = theme;
            SaveSettings();
            BuildUiThemeChoice();
            if (theme == activeUiTheme) return;   // zurück zum laufenden Thema: kein Neustart nötig

            var answer = Msg("Das Thema wird beim nächsten Start übernommen.\n\nJetzt neu starten?", "Design-Thema",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) RestartLauncher();
        }
    }
}
