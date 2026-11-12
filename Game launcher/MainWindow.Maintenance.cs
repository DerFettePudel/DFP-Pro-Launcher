using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Spieldateien reparieren ─────────────────────────────

        /// <summary>Wo im jeweiligen Launcher die Reparatur steht (nur Steam kann direkt von außen prüfen).</summary>
        private static readonly Dictionary<string, string> RepairSteps = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Epic Games"] = "Im Epic Games Launcher: Bibliothek → ⋯ beim Spiel → Verwalten → Dateien überprüfen.",
            ["Xbox"] = "In den Windows-Einstellungen: Apps → Installierte Apps → ⋯ beim Spiel → Erweiterte Optionen → Reparieren. In der Xbox-App geht es auch: Spiel → ⋯ → Verwalten → Dateien → Überprüfen und reparieren.",
            ["EA"] = "In der EA app: Bibliothek → ⋯ beim Spiel → Reparieren.",
            ["Ubisoft"] = "In Ubisoft Connect: Spiele → Spiel auswählen → Eigenschaften → Dateien überprüfen.",
            ["GOG"] = "In GOG Galaxy: Spiel öffnen → Regler-Symbol neben „Spielen“ → Installation verwalten → Überprüfen / Reparieren.",
            ["Battle.net"] = "In Battle.net: Spiel auswählen → Zahnrad neben „Spielen“ → Scannen und reparieren.",
            ["Rockstar"] = "Im Rockstar Games Launcher: Einstellungen → Meine installierten Spiele → Spiel → Integrität überprüfen.",
            ["Amazon Games"] = "In der Amazon Games App: Bibliothek → ⋯ beim Spiel → Verwalten → Überprüfen.",
            ["Riot Games"] = "Im Riot Client: Profil-Symbol → Einstellungen → Spiel auswählen → Reparieren.",
            ["Heroic"] = "In Heroic: Spiel öffnen → ⋯ → Installation überprüfen und reparieren.",
            ["Wargaming"] = "Im Wargaming.net Game Center: Spiel auswählen → Spieleinstellungen → Spiel überprüfen und reparieren.",
            ["HoYoPlay"] = "In HoYoPlay: Spiel auswählen → ⋯ neben „Spielen“ → Spieldateien reparieren.",
            ["Plarium Play"] = "In Plarium Play: Spiel auswählen → ⋯ → Reparieren."
        };

        private static bool CanRepair(GameItem game)
            => (game.Source == "Steam" && game.AppId.Length > 0) || RepairSteps.ContainsKey(game.Source);

        private void RepairGame(GameItem game)
        {
            if (activeSessions.ContainsKey(game))
            {
                Msg($"„{game.Name}“ läuft gerade. Bitte beende das Spiel, bevor du die Dateien prüfst.", "Spieldateien reparieren", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Steam kann die Prüfung direkt starten
            if (game.Source == "Steam" && game.AppId.Length > 0)
            {
                OpenShell($"steam://validate/{game.AppId}");
                ShowToast("🛠", Loc.T("Spieldateien reparieren"), Loc.T($"Steam prüft jetzt die Dateien von „{game.Name}“. Fehlende oder kaputte Dateien lädt Steam neu."), 8, null, true);
                return;
            }

            if (!RepairSteps.TryGetValue(game.Source, out var steps)) return;

            // Den passenden Launcher öffnen, damit der Weg kurz ist
            if (game.Source == "Xbox")
            {
                OpenShell("ms-settings:appsfeatures");
            }
            else
            {
                string? exe = LauncherExePath(game.Source);
                if (exe != null) OpenShell(exe);
                else if (game.Source == "Epic Games") OpenShell("com.epicgames.launcher://");
            }

            Msg(Loc.T($"Prüfen und reparieren macht bei „{game.Name}“ der Launcher selbst.") + "\n\n" + Loc.T(steps),
                "Spieldateien reparieren", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ───────────────────────────── Platz schaffen ─────────────────────────────

        private static string DriveOf(GameItem game)
        {
            try { return System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(game.InstallDir)) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string FreeSpaceText(string drive)
        {
            try
            {
                var info = new DriveInfo(drive);
                return info.IsReady ? FormatBytes(info.AvailableFreeSpace) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>„zuletzt gespielt vor 8 Monaten“ – für lange Zeiträume gröber als FormatRelative.</summary>
        private static string LastPlayedText(GameItem game)
        {
            if (!game.LastPlayed.HasValue) return Loc.T("nie gespielt");

            double days = (DateTime.Now - game.LastPlayed.Value).TotalDays;
            string when = days < 30 ? Loc.T(FormatRelative(game.LastPlayed.Value))
                : days < 60 ? Loc.T("vor einem Monat")
                : days < 365 ? Loc.T($"vor {(int)(days / 30.4)} Monaten")
                : days < 730 ? Loc.T("vor über einem Jahr")
                : Loc.T($"vor {(int)(days / 365.25)} Jahren");
            return Loc.T("zuletzt gespielt") + " " + when;
        }

        /// <summary>Zeigt die installierten Spiele nach Größe (oder nach „lange nicht gespielt“) und deinstalliert die markierten.</summary>
        private void ShowSpaceAssistant(string? driveFilter = null)
        {
            var candidates = allGames
                .Where(g => !string.IsNullOrEmpty(g.InstallDir) && Directory.Exists(g.InstallDir))
                .GroupBy(g => g.InstallDir, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (candidates.Count == 0)
            {
                Msg("Keine installierten Spiele gefunden.", "Platz schaffen");
                return;
            }

            var drives = candidates.Select(DriveOf).Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
            string drive = driveFilter != null && drives.Contains(driveFilter, StringComparer.OrdinalIgnoreCase) ? driveFilter : string.Empty;
            int sortMode = 0;   // 0 = größte zuerst, 1 = am längsten nicht gespielt
            var selected = new HashSet<GameItem>();
            var chipStyle = (Style)FindResource("ChipStyle");

            var dialog = CreateDialog("Platz schaffen", 700, out var panel);

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Deine installierten Spiele mit ihrer Größe. Markiere die Spiele, die weg können: Der Launcher startet dann die Deinstallation und sichert vorher den Spielstand, wenn ein Spielstand-Ordner festgelegt ist."),
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var list = new StackPanel();
            var summary = new TextBlock { Foreground = BrushSubtle, VerticalAlignment = System.Windows.VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var remove = new System.Windows.Controls.Button { Content = Loc.T("🗑  Ausgewählte deinstallieren"), Padding = new Thickness(20, 8, 20, 8), IsEnabled = false };
            remove.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");

            void UpdateSummary()
            {
                long bytes = selected.Sum(g => KnownGameSize(g) ?? 0);
                summary.Text = selected.Count == 0
                    ? Loc.T("Noch nichts ausgewählt.")
                    : Loc.T($"{selected.Count} ausgewählt") + "  ·  " + Loc.T($"ca. {FormatBytes(bytes)} werden frei");
                remove.IsEnabled = selected.Count > 0;
            }

            void Rebuild()
            {
                list.Children.Clear();
                var items = candidates.Where(g => drive.Length == 0 || string.Equals(DriveOf(g), drive, StringComparison.OrdinalIgnoreCase));
                items = sortMode == 0
                    ? items.OrderByDescending(g => KnownGameSize(g) ?? -1).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                    : items.OrderBy(g => g.LastPlayed ?? DateTime.MinValue).ThenByDescending(g => KnownGameSize(g) ?? -1);

                foreach (var item in items)
                {
                    var game = item;
                    long? size = KnownGameSize(game);

                    var meta = new List<string> { size.HasValue ? FormatBytes(size.Value) : Loc.T("Größe wird ermittelt ..."), Loc.T(game.Source), LastPlayedText(game) };
                    if (game.PlaySeconds >= 60 && !SHide(settings.StreamHideStats)) meta.Add(Loc.T("Spielzeit") + " " + FormatPlaytime(game.PlaySeconds));
                    if (activeSessions.ContainsKey(game)) meta.Add(Loc.T("läuft gerade"));

                    var text = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
                    text.Children.Add(new TextBlock { Text = game.Name, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                    text.Children.Add(new TextBlock { Text = string.Join("  ·  ", meta), Foreground = BrushSubtle, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

                    var box = new System.Windows.Controls.CheckBox
                    {
                        Content = text,
                        IsChecked = selected.Contains(game),
                        IsEnabled = !activeSessions.ContainsKey(game),
                        Margin = new Thickness(0, 0, 0, 10),
                        VerticalContentAlignment = System.Windows.VerticalAlignment.Center
                    };
                    box.Checked += (s, e) => { selected.Add(game); UpdateSummary(); };
                    box.Unchecked += (s, e) => { selected.Remove(game); UpdateSummary(); };
                    list.Children.Add(box);
                }

                if (list.Children.Count == 0)
                    list.Children.Add(new TextBlock { Text = Loc.T("Keine Einträge vorhanden."), Foreground = BrushSubtle });
                UpdateSummary();
            }

            // Laufwerke (mit freiem Platz)
            var driveRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            void DriveChip(string label, string value)
            {
                var chip = new System.Windows.Controls.RadioButton { Content = label, GroupName = "SpaceDrive", Style = chipStyle, IsChecked = drive == value };
                chip.Checked += (s, e) => { drive = value; Rebuild(); };
                driveRow.Children.Add(chip);
            }
            DriveChip(Loc.T("Alle Laufwerke"), string.Empty);
            foreach (string d in drives)
            {
                string free = FreeSpaceText(d);
                DriveChip(free.Length > 0 ? d.TrimEnd('\\') + "  ·  " + Loc.T($"{free} frei") : d.TrimEnd('\\'), d);
            }
            panel.Children.Add(driveRow);

            var sortRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var (label, mode) in new[] { ("Größte zuerst", 0), ("Am längsten nicht gespielt", 1) })
            {
                int value = mode;
                var chip = new System.Windows.Controls.RadioButton { Content = Loc.T(label), GroupName = "SpaceSort", Style = chipStyle, IsChecked = sortMode == value };
                chip.Checked += (s, e) => { sortMode = value; Rebuild(); };
                sortRow.Children.Add(chip);
            }
            panel.Children.Add(sortRow);

            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 14) });

            var footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.Children.Add(summary);
            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
            buttons.Children.Add(new System.Windows.Controls.Button { Content = Loc.T("Schließen"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true });
            buttons.Children.Add(remove);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);
            panel.Children.Add(footer);

            List<GameItem>? toRemove = null;
            remove.Click += (s, e) =>
            {
                var chosen = selected.Where(g => !activeSessions.ContainsKey(g)).ToList();
                if (chosen.Count == 0) return;

                long bytes = chosen.Sum(g => KnownGameSize(g) ?? 0);
                string names = string.Join("\n", chosen.Take(12).Select(g => "• " + g.Name)) + (chosen.Count > 12 ? "\n…" : string.Empty);
                var answer = Msg(Loc.T($"{chosen.Count} Spiele deinstallieren?") + "\n\n" + names + "\n\n" + Loc.T($"ca. {FormatBytes(bytes)} werden frei"),
                    "Platz schaffen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;

                toRemove = chosen;
                dialog.DialogResult = true;
            };

            Rebuild();

            // Fehlende Größen im Hintergrund messen, danach die Liste auffrischen
            var unknown = candidates.Where(g => KnownGameSize(g) == null).Select(g => g.InstallDir).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (unknown.Count > 0)
            {
                _ = Task.Run(() => unknown.Select(dir => (Dir: dir, Size: GetDirectorySize(dir))).ToList()).ContinueWith(task =>
                {
                    if (task.Status != TaskStatus.RanToCompletion) return;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        foreach (var (dir, size) in task.Result)
                            settings.GameSizes[dir] = new GameSizeEntry { Bytes = size, Measured = DateTime.Now };
                        SaveSettings();
                        if (dialog.IsVisible) Rebuild();
                    }));
                });
            }

            dialog.ShowDialog();
            if (toRemove != null) _ = UninstallManyAsync(toRemove);
        }

        private async Task UninstallManyAsync(List<GameItem> games)
        {
            int started = 0;
            foreach (var game in games)
            {
                if (await UninstallGameAsync(game, true)) started++;
                await Task.Delay(1200);   // die Launcher öffnen ihre Fenster nacheinander
            }

            if (started > 0)
                Msg(Loc.T($"Für {started} Spiele wurde die Deinstallation gestartet.") + "\n" +
                    Loc.T(settings.AutoDetectGames
                        ? "Bestätige sie bitte in den Fenstern der Launcher. Die Bibliothek aktualisiert sich danach von selbst."
                        : "Bestätige sie bitte in den Fenstern der Launcher und klicke danach auf „Neu scannen“."),
                    "Platz schaffen");
        }

        private void BtnSpaceAssistant_Click(object sender, RoutedEventArgs e) => ShowSpaceAssistant();
    }
}
