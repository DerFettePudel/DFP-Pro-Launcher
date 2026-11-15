using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: eigene Farben ─────────────────────────────
        //
        // Thema 2 nutzte bisher fest Cyan als zweite Leuchtfarbe und Blauschwarz als Grund. Jetzt wählbar:
        // zweite Leuchtfarbe, Hintergrund-Ton, Deckkraft der Glas-Panels und Leuchtstärke (plus Vorlagen).
        // Umsetzung: Alle festen Werte (#22D3EE, #06080D) laufen in Thema 2 über die Palette – in Cyber.xaml beim Laden,
        // im Code über MakeBrush und die Felder HoloCyan/ConsoleCyan/ConsoleBase/ConsoleInk.

        private static bool cyberPaletteActive;
        private static string cyberSecondHex = "22D3EE";
        private static string cyberBaseHex = "06080D";
        private static double cyberGlassFactor = 1.0;
        private static double cyberGlowFactor = 1.0;
        private static System.Windows.Media.Color CyberPanelTop = System.Windows.Media.Color.FromRgb(0x0E, 0x16, 0x26);
        private static System.Windows.Media.Color CyberPanelBottom = System.Windows.Media.Color.FromRgb(0x07, 0x0B, 0x14);

        private static readonly (string Name, string Second, string Base)[] CyberPresets =
        {
            ("Neon-Cyan", "#22D3EE", "#06080D"),
            ("Synthwave", "#FF2BD6", "#0A0612"),
            ("Matrix", "#39FF88", "#040A06"),
            ("Gold", "#FACC15", "#0A0805"),
            ("Eis", "#A5F3FC", "#060A10"),
            ("Blutmond", "#FF4D4D", "#0A0505")
        };

        /// <summary>Feste Farbwerte von Thema 2 auf die gewählte Palette umbiegen (nur in Thema 2 aktiv).</summary>
        private static string RemapCyberHex(string hex)
        {
            if (!cyberPaletteActive || hex.Length < 7) return hex;
            if (hex.IndexOf("22D3EE", StringComparison.OrdinalIgnoreCase) >= 0) hex = Regex.Replace(hex, "22D3EE", cyberSecondHex, RegexOptions.IgnoreCase);
            if (hex.IndexOf("06080D", StringComparison.OrdinalIgnoreCase) >= 0) hex = Regex.Replace(hex, "06080D", cyberBaseHex, RegexOptions.IgnoreCase);
            return hex;
        }

        private static System.Windows.Media.Color Lerp(System.Windows.Media.Color from, System.Windows.Media.Color to, double amount)
            => System.Windows.Media.Color.FromRgb(
                (byte)Math.Round(from.R + (to.R - from.R) * amount),
                (byte)Math.Round(from.G + (to.G - from.G) * amount),
                (byte)Math.Round(from.B + (to.B - from.B) * amount));

        /// <summary>Palette aus den Einstellungen übernehmen (vor dem Aufbau der Oberfläche und nach jeder Änderung).</summary>
        private void ApplyCyberPalette()
        {
            if (!IsBrutal) return;
            var second = ParseColor(settings.CyberSecondColor, "#22D3EE");
            var basis = ParseColor(settings.CyberBaseColor, "#06080D");

            // Der Grund muss dunkel bleiben, sonst ist weiße Schrift nicht lesbar
            double brightness = 0.2126 * basis.R + 0.7152 * basis.G + 0.0722 * basis.B;
            if (brightness > 40) basis = Lerp(basis, Colors.Black, 1 - 40 / brightness);

            cyberSecondHex = $"{second.R:X2}{second.G:X2}{second.B:X2}";
            cyberBaseHex = $"{basis.R:X2}{basis.G:X2}{basis.B:X2}";
            cyberGlassFactor = Math.Clamp(settings.CyberGlassOpacity, 30, 100) / 88.0;
            cyberGlowFactor = Math.Clamp(settings.CyberGlow, 0, 150) / 100.0;
            if (CyberNightNow) cyberGlowFactor *= 0.65;   // Nachtmodus: gedämpftes Leuchten (MainWindow.CyberFx.cs)
            cyberPaletteActive = true;

            HoloCyan = second;
            ConsoleBase = basis;
            ConsoleInk = new SolidColorBrush(basis);
            ConsoleInk.Freeze();
            ConsoleCyan = new SolidColorBrush(second);
            ConsoleCyan.Freeze();
            CyberPanelTop = Lerp(Lerp(basis, System.Windows.Media.Color.FromRgb(0x10, 0x18, 0x28), 0.6), second, 0.04);
            CyberPanelBottom = Lerp(basis, second, 0.012);
        }

        /// <summary>Cyber.xaml vor dem Einlesen an die Palette anpassen: Farben ersetzen, Leuchten verstärken/abschwächen.</summary>
        private static string ApplyPaletteToXaml(string xaml)
        {
            if (!cyberPaletteActive) return xaml;
            xaml = Regex.Replace(xaml, "22D3EE", cyberSecondHex, RegexOptions.IgnoreCase);
            xaml = Regex.Replace(xaml, "06080D", cyberBaseHex, RegexOptions.IgnoreCase);
            xaml = Regex.Replace(xaml, "(<DropShadowEffect x:Key=\"(?:NeonGlow|CyanGlow|TitleGlow)\"[^>]*?Opacity=\")([0-9.]+)(\")", m =>
            {
                double value = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return m.Groups[1].Value + Math.Clamp(value * cyberGlowFactor, 0, 1).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + m.Groups[3].Value;
            });
            return xaml;
        }

        // ───────── Einstellungskarte (nur in Thema 2, Bereich „Darstellung“) ─────────

        private readonly DispatcherTimer cyberColorTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private bool cyberColorTimerHooked;
        private Border? cyberSecondSwatch, cyberBaseSwatch;
        private System.Windows.Controls.Primitives.UniformGrid? cyberPresetHost;

        private void BuildCyberColorsCard()
        {
            if (!IsBrutal || SettingsCards == null) return;

            var body = new StackPanel();
            var heading = new TextBlock { Text = Loc.T("Farben von Thema 2") };
            heading.SetResourceReference(StyleProperty, "SectionTitle");
            body.Children.Add(heading);

            StackPanel Row(string title, string description)
            {
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
                row.Children.Add(new TextBlock { Text = Loc.T(title), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                var hint = new TextBlock { Text = Loc.T(description), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 10) };
                hint.SetResourceReference(StyleProperty, "SubtleText");
                hint.FontSize = 12;
                row.Children.Add(hint);
                body.Children.Add(row);
                return row;
            }

            // Vorlagen
            var presets = Row("Vorlagen", "Fertige Kombinationen aus Leuchtfarbe und Hintergrund-Ton. Deine Akzentfarbe bleibt, wie sie ist.");
            cyberPresetHost = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            presets.Children.Add(cyberPresetHost);
            RenderCyberPresets();

            // Leuchtfarbe und Grund
            var colors = Row("Zweite Leuchtfarbe und Hintergrund", "Die zweite Leuchtfarbe färbt Kanten, Beschriftungen, Ringe und Linien (bisher Cyan). Der Hintergrund-Ton bleibt immer dunkel, damit alles lesbar ist.");
            var pickers = new WrapPanel();
            System.Windows.Controls.Button Picker(string label, Action click)
            {
                var button = new System.Windows.Controls.Button { Content = Loc.T(label), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 10) };
                button.Click += (s, e) => click();
                return button;
            }
            cyberSecondSwatch = new Border { Width = 34, Height = 34, Margin = new Thickness(0, 0, 22, 10), BorderBrush = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(1) };
            cyberBaseSwatch = new Border { Width = 34, Height = 34, Margin = new Thickness(0, 0, 22, 10), BorderBrush = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(1) };
            pickers.Children.Add(Picker("Leuchtfarbe wählen", () =>
            {
                if (!TryPickColor(settings.CyberSecondColor, out string hex)) return;
                settings.CyberSecondColor = hex;
                CyberColorsChanged();
            }));
            pickers.Children.Add(cyberSecondSwatch);
            pickers.Children.Add(Picker("Hintergrund-Ton wählen", () =>
            {
                if (!TryPickColor(settings.CyberBaseColor, out string hex)) return;
                settings.CyberBaseColor = hex;
                CyberColorsChanged();
            }));
            pickers.Children.Add(cyberBaseSwatch);
            colors.Children.Add(pickers);

            // Regler
            void Slider(string title, string description, int min, int max, Func<int> get, Action<int> set)
            {
                var row = Row(title, description);
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                var slider = new System.Windows.Controls.Slider { Minimum = min, Maximum = max, Value = get(), IsSnapToTickEnabled = true, TickFrequency = 5, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var value = new TextBlock { Text = get() + " %", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
                slider.ValueChanged += (s, e) =>
                {
                    set((int)Math.Round(e.NewValue));
                    value.Text = get() + " %";
                    CyberColorsChanged();
                };
                line.Children.Add(slider);
                Grid.SetColumn(value, 1);
                line.Children.Add(value);
                row.Children.Add(line);
            }
            Slider("Deckkraft der Glas-Panels", "Weniger Deckkraft lässt Hintergrundbild und Raster stärker durchscheinen.", 30, 100, () => settings.CyberGlassOpacity, v => settings.CyberGlassOpacity = v);
            Slider("Leuchtstärke", "Wie stark Kanten, Knöpfe und Titel leuchten. 0 % schaltet das Leuchten aus.", 0, 150, () => settings.CyberGlow, v => settings.CyberGlow = v);

            var reset = new System.Windows.Controls.Button { Content = Loc.T("Farben von Thema 2 zurücksetzen"), Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            reset.Click += (s, e) =>
            {
                settings.CyberSecondColor = "#22D3EE";
                settings.CyberBaseColor = "#06080D";
                settings.CyberGlassOpacity = 88;
                settings.CyberGlow = 100;
                SaveSettings();
                ApplyCyberColorsNow();
                // Regler auf die Standardwerte setzen: Karte neu aufbauen
                RebuildCyberColorsCard();
            };
            body.Children.Add(reset);
            var note = new TextBlock { Text = Loc.T("Wird sofort übernommen. Ein paar Leisten übernehmen die Farbe erst nach einem Neustart."), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
            note.SetResourceReference(StyleProperty, "SubtleText");
            note.FontSize = 12;
            body.Children.Add(note);

            var card = new Border { Tag = "look", Margin = new Thickness(0, 0, 0, 16), Child = body, Name = "CyberColorsCard" };
            card.SetResourceReference(StyleProperty, "CardStyle");
            // Direkt hinter die Karte „Design-Thema“
            int index = SettingsCards.Children.OfType<Border>().ToList().FindIndex(b => b.Tag as string == "look");
            SettingsCards.Children.Insert(Math.Max(0, index + 1), card);
            UpdateCyberSwatches();
        }

        private void RebuildCyberColorsCard()
        {
            var old = holoSettingsCards.FirstOrDefault(c => c.Card.Name == "CyberColorsCard");
            if (old != null)
            {
                holoSettingsCards.Remove(old);
                if (old.Body.Parent is System.Windows.Controls.Panel parent) parent.Children.Remove(old.Body);
                SettingsCards.Children.Remove(old.Card);
            }
            BuildCyberColorsCard();
            if (holoKey == "settings") SelectHoloSettingsCategory("look");
        }

        private void RenderCyberPresets()
        {
            if (cyberPresetHost == null) return;
            cyberPresetHost.Children.Clear();
            foreach (var preset in CyberPresets)
            {
                var item = preset;
                bool active = string.Equals(settings.CyberSecondColor, item.Second, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(settings.CyberBaseColor, item.Base, StringComparison.OrdinalIgnoreCase);
                var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                row.Children.Add(new Border { Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), Background = MakeBrushRaw(item.Second), BorderBrush = MakeBrushRaw(item.Base), BorderThickness = new Thickness(2), VerticalAlignment = System.Windows.VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = Loc.T(item.Name), VerticalAlignment = System.Windows.VerticalAlignment.Center });
                var chip = new System.Windows.Controls.RadioButton { Content = row, GroupName = "CyberPreset", IsChecked = active, Margin = new Thickness(0, 0, 8, 8) };
                chip.SetResourceReference(StyleProperty, "ChipStyle");
                chip.Checked += (s, e) =>
                {
                    if (string.Equals(settings.CyberSecondColor, item.Second, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(settings.CyberBaseColor, item.Base, StringComparison.OrdinalIgnoreCase)) return;
                    settings.CyberSecondColor = item.Second;
                    settings.CyberBaseColor = item.Base;
                    CyberColorsChanged();
                };
                cyberPresetHost.Children.Add(chip);
            }
        }

        /// <summary>Pinsel ohne Umbiegen auf die Palette (für die Vorschau der Vorlagen).</summary>
        private static SolidColorBrush MakeBrushRaw(string hex)
        {
            bool active = cyberPaletteActive;
            cyberPaletteActive = false;
            try { return MakeBrush(hex); }
            finally { cyberPaletteActive = active; }
        }

        private void UpdateCyberSwatches()
        {
            if (cyberSecondSwatch != null) cyberSecondSwatch.Background = MakeBrushRaw(settings.CyberSecondColor);
            if (cyberBaseSwatch != null) cyberBaseSwatch.Background = MakeBrushRaw(settings.CyberBaseColor);
        }

        /// <summary>Änderung merken und kurz danach anwenden (Regler feuern bei jedem Schritt).</summary>
        private void CyberColorsChanged()
        {
            SaveSettings();
            UpdateCyberSwatches();
            if (!cyberColorTimerHooked)
            {
                cyberColorTimerHooked = true;
                cyberColorTimer.Tick += (s, e) => { cyberColorTimer.Stop(); ApplyCyberColorsNow(); };
            }
            cyberColorTimer.Stop();
            cyberColorTimer.Start();
        }

        private void ApplyCyberColorsNow()
        {
            if (!IsBrutal) return;
            ApplyCyberPalette();
            LoadCyberDictionary();
            ApplyBrutalTokens();
            ApplyCyberSidebarEdge();
            RenderCyberPresets();
            UpdateCyberSwatches();
            if (holoHost != null && holoHost.Children.Count > 0)
            {
                // Hintergrund (Raster, Leuchten) neu in den neuen Farben – erst entfernen, dann einsetzen
                holoHost.Children.RemoveAt(0);
                holoHost.Children.Insert(0, BuildHoloBackdrop());
            }
            ApplyHoloBackground();
            RefreshConsole();

            // Die Einstellungsseite baut sich sonst nie neu auf: hier ja, damit Kanten und Beschriftungen sofort die neue
            // Farbe zeigen – aber nicht, während ein Regler noch gezogen wird (sonst ginge er unter der Maus verloren)
            if (holoKey == "settings" && System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Released)
            {
                double offset = holoScroll?.VerticalOffset ?? 0;
                BuildHoloPage(false);
                holoScroll?.ScrollToVerticalOffset(offset);
            }
            else if (holoKey == "settings") CyberColorsChanged();   // nach dem Loslassen erneut versuchen
        }
    }
}
