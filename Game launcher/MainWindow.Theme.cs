using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Design-Thema: Thema 1 „Nova“ / Thema 2 „Cyber“ ─────────────────────────────
        //
        // Das Thema gilt ab dem Start (Wechsel = Neustart). Thema 2 liest Themes\Cyber.xaml ein und ersetzt damit gleichnamige
        // Farben, Schriften und Bausteine aus MainWindow.xaml; dazu kommt die Konsolen-Oberfläche (MainWindow.Console.cs).
        // In den Einstellungen heißt das Thema intern weiter "brutal", damit gespeicherte Einstellungen gültig bleiben.

        private const string PaperColor = "#E8F0F8";   // helle Schrift und Linien in Thema 2

        private string activeUiTheme = "nova";
        private bool IsBrutal => activeUiTheme == "brutal";   // = Thema 2

        private string cyberBakedAccent = string.Empty;
        private ResourceDictionary? cyberDictionary;
        private bool uiThemeChosen;

        /// <summary>Beim ersten Laden der Einstellungen, bevor irgendetwas gebaut wird.</summary>
        private void ApplyUiThemeAtStartup()
        {
            // Nur beim ersten Laden: Später (Einstellungen importieren, Profil) gilt das Thema erst nach einem Neustart
            if (uiThemeChosen) return;
            uiThemeChosen = true;

            activeUiTheme = settings.UiTheme == "brutal" ? "brutal" : "nova";
            if (!IsBrutal) return;

            if (!LoadCyberDictionary())
            {
                activeUiTheme = "nova";   // Datei fehlt oder ist kaputt: lieber Thema 1 als ein halbes Thema 2
                return;
            }

            // Überschriften in „Chakra Petch“ (eingebettet), Fließtext bleibt gut lesbar in Segoe UI
            try
            {
                Resources["DisplayFont"] = new System.Windows.Media.FontFamily(
                    new Uri("pack://application:,,,/DFPProLauncher;component/"), "./Fonts/#Chakra Petch, Segoe UI Variable Display, Segoe UI");
            }
            catch { }
        }

        private static string HexOf(System.Windows.Media.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        private static string HexOf(System.Windows.Media.Color color, byte alpha) => $"#{alpha:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        /// <summary>Schrift auf der Akzentfarbe: dunkel bei hellen Akzenten, sonst hell.</summary>
        private static string InkFor(System.Windows.Media.Color accent)
            => 0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B > 150 ? "#0B0B0B" : "#FFFFFF";

        private bool LoadCyberDictionary()
        {
            try
            {
                var accent = ParseColor(settings.AccentColor, "#8B5CF6");
                using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Game_launcher.Themes.Cyber.xaml");
                if (stream == null) return false;
                using var reader = new StreamReader(stream);
                string xaml = reader.ReadToEnd()
                    .Replace("__ACCENT40__", HexOf(accent, 0x66))
                    .Replace("__ACCENT22__", HexOf(accent, 0x38))
                    .Replace("__ACCENT__", HexOf(accent))
                    .Replace("__INK__", InkFor(accent));

                var dictionary = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
                foreach (object key in dictionary.Keys.Cast<object>().ToList())
                    Resources[key] = dictionary[key];

                cyberDictionary = dictionary;
                cyberBakedAccent = HexOf(accent);
                return true;
            }
            catch (Exception ex)
            {
                LogError("Thema 2 konnte nicht geladen werden", ex);
                return false;
            }
        }

        /// <summary>
        /// Läuft am Ende von ApplyTheme: Der Code setzt dort Flächen für Thema 1, die hier durch die von Thema 2 ersetzt werden.
        /// Hat sich die Akzentfarbe geändert, wird das Thema neu eingelesen (Leuchten und Verläufe nutzen die feste Farbe).
        /// </summary>
        private void ApplyBrutalTokens()
        {
            if (!IsBrutal) return;

            if (cyberBakedAccent != HexOf(ParseColor(settings.AccentColor, "#8B5CF6"))) LoadCyberDictionary();
            if (cyberDictionary == null) return;

            foreach (string key in new[]
            {
                "SidebarBrush", "CardBrush", "CardBorderBrush", "SurfaceBrush", "CardFillBrush",
                "CardEdgeBrush", "ControlBrush", "ControlHoverBrush", "ControlBorderBrush"
            })
            {
                if (cyberDictionary.Contains(key)) Resources[key] = cyberDictionary[key];
            }

            // Hintergrund: mit eigenem Hintergrundbild bleibt die Abdunklung in deiner Hintergrundfarbe (wie in Thema 1),
            // sonst das Blauschwarz mit Neonschein
            if (!HasBackgroundImage() && !(settings.GlassEffect && SupportsGlass()) && cyberDictionary.Contains("AppBackgroundBrush"))
                Resources["AppBackgroundBrush"] = cyberDictionary["AppBackgroundBrush"];

            // Pinsel, die der Code direkt für Kacheln und Karten benutzt
            BrushCardBg = MakeBrush("#0E121B");
            BrushCardBorder = MakeBrush("#1E2638");
            BrushFooter = MakeBrush("#121726");
            BrushTileHover = MakeBrush("#1A2133");
            ApplyHoloBackground();   // Hintergrundbild/Glas auch auf den Holo-Seiten
        }

        // ───────── Kopfleiste oben (Thema 2) ─────────

        private const double TitleBarHeight = 34;   // eigene Fensterleiste mit Minimieren/Maximieren/Schließen

        /// <summary>Inhalt beginnt unter Fensterleiste und der Kopfleiste der Konsolen-Oberfläche (76 hoch).</summary>
        private Thickness TopNavContentMargin() => new(48, TitleBarHeight + 76 + 24, 48, 24);

        /// <summary>Form der waagerechten Leiste: In Thema 2 ersetzt sie die Kopfleiste der Konsolen-Oberfläche, unten ist sie die schwebende Pille.</summary>
        private void ApplyDockShape(bool top)
        {
            if (NavDock == null) return;

            NavDock.BeginAnimation(FrameworkElement.MarginProperty, null);   // Anheben über die Taskleiste gilt nur unten
            if (top)
            {
                NavDock.Visibility = Visibility.Collapsed;
                dockTaskbarLift = 0;
                return;
            }

            NavDock.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            NavDock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            NavDock.Margin = new Thickness(0, 0, 0, DockBaseMargin + dockTaskbarLift);
            NavDock.CornerRadius = new CornerRadius(26);
            NavDock.BorderThickness = new Thickness(1);
            NavDock.BorderBrush = MakeBrush("#33FFFFFF");
            NavDock.Padding = new Thickness(10, 6, 10, 6);
        }

        // ───────── Einstellungen: Thema wählen ─────────

        private void BuildUiThemeChoice()
        {
            if (UiThemePanel == null) return;
            UiThemePanel.Children.Clear();

            foreach (var (id, title, text) in new[]
            {
                ("nova", "Thema 1 · Nova", "Weich und rund, Glas und Leuchten, dein Hintergrundbild."),
                ("brutal", "Thema 2 · Cyber", "Neonkanten und schräge Knöpfe, eigener Aufbau: Start mit großem Spielbild, Dashboard, Bibliothek in Reihen oder als Raster.")
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
