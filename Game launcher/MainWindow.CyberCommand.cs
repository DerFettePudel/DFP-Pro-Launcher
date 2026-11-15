using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Strg+K als Befehlszeile ─────────────────────────────
        //
        // Die Spotlight-Suche bekommt in Thema 2 einen Terminal-Look (Eingabe-Prompt, Monospace, Neonrahmen) und versteht
        // Befehle: „start <spiel>“, „details <spiel>“, „öffne <seite>“, „einstellung <begriff>“, „zufall“, „hilfe“.
        // Ohne Befehl sucht sie wie bisher über alles (Spiele, Seiten, Anwendungen, Werkzeuge ...).

        private bool cyberCommandReady;
        private TextBlock? cyberCommandHint;

        private static readonly (string[] Verbs, string Usage, string Description)[] CyberCommandVerbs =
        {
            (new[] { "start", "spiel", "play", "s" }, "start <spiel>", "Spiel sofort starten"),
            (new[] { "details", "info", "d" }, "details <spiel>", "Spiel-Ansicht öffnen"),
            (new[] { "öffne", "oeffne", "open", "gehe", "go", "o" }, "öffne <seite>", "Bereich öffnen (z. B. statistik, system)"),
            (new[] { "einstellung", "einstellungen", "set", "settings", "e" }, "einstellung <begriff>", "Einstellungen durchsuchen"),
            (new[] { "zufall", "random", "würfel" }, "zufall", "Zufälliges Spiel vorschlagen"),
            (new[] { "hilfe", "help", "?" }, "hilfe", "Alle Befehle zeigen")
        };

        /// <summary>Spotlight in Thema 2 umgestalten: Terminal-Rahmen, Kopfzeile, Prompt vor der Eingabe.</summary>
        private void InitCyberCommand()
        {
            if (cyberCommandReady || TxtSpotlight?.Parent is not StackPanel stack || stack.Parent is not Border card) return;
            cyberCommandReady = true;

            card.Style = null;
            card.Width = 780;
            card.Padding = new Thickness(0);
            card.CornerRadius = new CornerRadius(0);
            card.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF5, ConsoleBase.R, ConsoleBase.G, ConsoleBase.B));
            card.BorderBrush = HoloEdgeBrush(0xFF);
            card.BorderThickness = new Thickness(1);
            if (!settings.PerformanceMode) card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = HoloCyan, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.35 };

            // Kopfzeile wie ein Terminalfenster
            var bar = new Grid { Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, HoloCyan.R, HoloCyan.G, HoloCyan.B)) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = "DFP://" + Loc.T("Befehlszeile").ToUpper(Loc.Culture), FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = ConsoleCyan, Margin = new Thickness(16, 7, 0, 7) };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            bar.Children.Add(title);
            var keys = new TextBlock { Text = "STRG+K", FontSize = 10.5, Foreground = ConsoleMuted, Margin = new Thickness(0, 7, 16, 7) };
            keys.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            Grid.SetColumn(keys, 1);
            bar.Children.Add(keys);
            stack.Children.Insert(0, bar);

            // Eingabe mit Prompt „›“
            int index = stack.Children.IndexOf(TxtSpotlight);
            stack.Children.Remove(TxtSpotlight);
            var line = new Grid { Margin = new Thickness(16, 12, 16, 0) };
            var prompt = new TextBlock { Text = "›", FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 2), IsHitTestVisible = false };
            prompt.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            TxtSpotlight.Padding = new Thickness(30, 0, 10, 0);
            TxtSpotlight.FontSize = 17;
            TxtSpotlight.Background = MakeBrush("#CC0B1222");
            TxtSpotlight.BorderBrush = MakeBrush("#5522D3EE");
            TxtSpotlight.Foreground = System.Windows.Media.Brushes.White;
            TxtSpotlight.CaretBrush = new SolidColorBrush(HoloCyan);
            TxtSpotlight.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
            TxtSpotlight.SetResourceReference(System.Windows.Controls.Control.FontFamilyProperty, "MonoFont");
            line.Children.Add(TxtSpotlight);
            line.Children.Add(prompt);
            stack.Children.Insert(index, line);

            SpotlightResults.Margin = new Thickness(10, 10, 10, 0);

            // Fußzeile: Befehle statt Tastenhinweis
            if (stack.Children.OfType<TextBlock>().LastOrDefault() is TextBlock footer)
            {
                cyberCommandHint = footer;
                footer.Text = Loc.T("start <spiel>  ·  öffne <seite>  ·  einstellung <begriff>  ·  hilfe      ↑↓ Enter Esc");
                footer.Margin = new Thickness(16, 8, 16, 12);
                footer.Foreground = ConsoleMuted;
                footer.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            }

            TxtSpotlight.TextChanged += (s, e) => { if (SpotlightLayer.Visibility == Visibility.Visible) PlayCyberSound("type"); };
        }

        /// <summary>Befehl erkannt? Dann Einträge dafür liefern (true) – sonst sucht Spotlight normal weiter.</summary>
        private bool BuildCyberCommandEntries(string query, List<SpotlightEntry> entries)
        {
            if (!IsBrutal) return false;

            if (query.Length == 0)
            {
                foreach (var game in VisibleGames.Where(g => g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).Take(3))
                {
                    var target = game;
                    entries.Add(new SpotlightEntry { Icon = "▶", Title = "start " + target.Name, Subtitle = Loc.T("Zuletzt gespielt") + "  ·  " + Loc.T(target.Source), Action = () => LaunchGame(target) });
                }
                AddCyberHelp(entries);
                return true;
            }

            string lower = query.ToLowerInvariant();
            int space = lower.IndexOf(' ');
            string verb = space > 0 ? lower[..space] : lower;
            string rest = space > 0 ? query[(space + 1)..].Trim() : string.Empty;
            var match = CyberCommandVerbs.FirstOrDefault(c => c.Verbs.Contains(verb));
            if (match.Verbs == null) return false;
            // Einbuchstabige Kürzel nur mit Leerzeichen dahinter (sonst wäre „s“ schon ein Befehl beim Suchen)
            if (verb.Length == 1 && verb != "?" && space < 0) return false;

            string kind = match.Verbs[0];
            switch (kind)
            {
                case "start":
                case "details":
                    {
                        var games = rest.Length == 0
                            ? VisibleGames.OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue).Take(6).ToList()
                            : RankGames(rest).Take(7).ToList();
                        foreach (var game in games)
                        {
                            var target = game;
                            bool start = kind == "start";
                            entries.Add(new SpotlightEntry
                            {
                                Icon = start ? "▶" : "ℹ",
                                Title = (start ? "start " : "details ") + target.Name,
                                Subtitle = (start ? Loc.T("Spiel sofort starten") : Loc.T("Spiel-Ansicht öffnen")) + "  ·  " + Loc.T(target.Source),
                                Action = start ? () => LaunchGame(target) : () => OpenCyberDetail(target)
                            });
                        }
                        if (entries.Count == 0) entries.Add(new SpotlightEntry { Icon = "✕", Title = Loc.T("Kein Spiel gefunden."), Subtitle = rest, Action = () => { } });
                        break;
                    }
                case "öffne":
                    {
                        var pages = PageEntries.Select(p => (Page: p, Score: rest.Length == 0 ? 0 : ScoreMatch(Loc.T(p.Title), rest)))
                            .Where(p => p.Score >= 0).OrderByDescending(p => p.Score).Take(8);
                        if (rest.Length == 0 || ScoreMatch(Loc.T("Start"), rest) >= 0)
                            entries.Add(new SpotlightEntry { Icon = "⌂", Title = "öffne " + Loc.T("Start"), Subtitle = Loc.T("Bereich öffnen"), Action = ShowConsoleHome });
                        if (rest.Length == 0 || ScoreMatch(Loc.T("Bibliothek"), rest) >= 0)
                            entries.Add(new SpotlightEntry { Icon = "▦", Title = "öffne " + Loc.T("Bibliothek"), Subtitle = Loc.T("Bereich öffnen"), Action = OpenConsoleLibraryOverview });
                        foreach (var (page, _) in pages)
                        {
                            string key = page.Key;
                            entries.Add(new SpotlightEntry { Icon = page.Icon, Title = "öffne " + Loc.T(page.Title), Subtitle = Loc.T("Bereich öffnen"), Action = () => ShowConsolePage(key) });
                        }
                        break;
                    }
                case "einstellung":
                    {
                        if (rest.Length > 0)
                        {
                            string text = rest;
                            entries.Add(new SpotlightEntry { Icon = "⚙", Title = "einstellung " + rest, Subtitle = Loc.T("Einstellungen durchsuchen"), Action = () => OpenCyberSettingsSearch(text) });
                        }
                        CollectHoloSettingsCards();
                        foreach (var card in holoSettingsCards.Where(c => c.Card.Visibility == Visibility.Visible && (rest.Length == 0 || c.Title.Contains(rest, StringComparison.CurrentCultureIgnoreCase))).Take(7))
                        {
                            string category = card.Category, title = card.Title;
                            entries.Add(new SpotlightEntry { Icon = "⚙", Title = title, Subtitle = Loc.T(CategoryLabel(category)), Action = () => OpenCyberSettingsSearch(title) });
                        }
                        break;
                    }
                case "zufall":
                    entries.Add(new SpotlightEntry { Icon = "🎲", Title = "zufall", Subtitle = Loc.T("Zufälliges Spiel vorschlagen"), Action = () => BtnRandomGame_Click(this, new RoutedEventArgs()) });
                    break;
                default:
                    AddCyberHelp(entries);
                    break;
            }
            return true;
        }

        private IEnumerable<GameItem> RankGames(string text)
        {
            var parsed = ParseSearch(text);
            return VisibleGames.Select(g => (Game: g, Score: Math.Max(GameSearchScore(g, parsed), parsed.Filters.Count == 0 ? ScoreMatch(g.Name, text) : -1)))
                .Where(h => h.Score >= 0).OrderByDescending(h => h.Score).ThenBy(h => h.Game.Name, StringComparer.CurrentCultureIgnoreCase).Select(h => h.Game);
        }

        private void AddCyberHelp(List<SpotlightEntry> entries)
        {
            foreach (var (verbs, usage, description) in CyberCommandVerbs)
            {
                string fill = verbs[0] + (usage.Contains('<') ? " " : string.Empty);
                entries.Add(new SpotlightEntry
                {
                    Icon = "›",
                    Title = Loc.T(usage),
                    Subtitle = Loc.T(description),
                    Action = () => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // Befehl in die Zeile schreiben statt auszuführen
                        OpenSpotlight();
                        TxtSpotlight.Text = fill;
                        TxtSpotlight.CaretIndex = fill.Length;
                    }))
                });
            }
        }

        private void OpenCyberDetail(GameItem game)
        {
            if (IsConsole) ShowConsoleDetail(game);
            else ShowGameDetails(game);
        }

        private void OpenCyberSettingsSearch(string text)
        {
            ShowConsolePage("settings");
            holoSettingsSearch = text;
            if (holoKey == "settings") BuildHoloPage(false);
        }

        /// <summary>Ergebniszeilen im Terminal-Stil (Thema 2).</summary>
        private void RenderCyberCommand()
        {
            SpotlightResults.Children.Clear();
            if (spotlightEntries.Count == 0)
            {
                var none = new TextBlock { Text = "// " + Loc.T("Keine Treffer.").ToUpper(Loc.Culture), Foreground = ConsoleMuted, Margin = new Thickness(8, 8, 0, 4) };
                none.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                SpotlightResults.Children.Add(none);
                return;
            }

            var accent = GetAccentColor();
            for (int i = 0; i < spotlightEntries.Count; i++)
            {
                var entry = spotlightEntries[i];
                int index = i;
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new TextBlock { Text = entry.Icon, FontSize = 16, Foreground = ConsoleCyan, VerticalAlignment = System.Windows.VerticalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol, Segoe UI Emoji") };
                line.Children.Add(icon);
                var title = new TextBlock { Text = entry.Title, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                title.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(title, 1);
                line.Children.Add(title);
                var sub = new TextBlock { Text = Loc.T(entry.Subtitle), FontSize = 11.5, Foreground = ConsoleMuted, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                Grid.SetColumn(sub, 2);
                line.Children.Add(sub);

                var item = new Border { Padding = new Thickness(10, 8, 12, 8), Cursor = System.Windows.Input.Cursors.Hand, Child = line, BorderThickness = new Thickness(3, 0, 0, 0), Tag = "cyber" };
                item.MouseEnter += (s, e) => { spotlightIndex = index; HighlightSpotlight(); };
                item.MouseLeftButtonUp += (s, e) => ActivateSpotlight(entry);
                SpotlightResults.Children.Add(item);
            }
            HighlightSpotlight();
        }

        private void HighlightCyberCommand()
        {
            var accent = GetAccentColor();
            for (int i = 0; i < SpotlightResults.Children.Count; i++)
            {
                if (SpotlightResults.Children[i] is not Border item) continue;
                bool active = i == spotlightIndex;
                item.Background = active ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, accent.R, accent.G, accent.B)) : System.Windows.Media.Brushes.Transparent;
                item.BorderBrush = active ? new SolidColorBrush(accent) : System.Windows.Media.Brushes.Transparent;
            }
        }
    }
}
