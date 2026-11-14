using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Optimierung („Tuning-Station“) ─────────────────────────────
        //
        // Gaming-Check als großer Ring mit vier Prüfpunkten, Energiepläne als Leistungszellen (1–4 Balken),
        // Windows-Schalter als Holo-Schalter, Treiber, Autostart mit Schaltern, Aufräumen als Kapseln mit Größenbalken.

        private bool cleanupMeasured;

        private static readonly (string Guid, string Name, string Hint, int Level)[] HoloPlans =
        {
            ("a1841308-3541-4fab-bc81-f71556f20b4a", "Energiesparen", "Spart Strom, weniger Leistung", 1),
            ("381b4222-f694-41f0-9685-ff5bb260df2e", "Ausbalanciert", "Empfohlen, passt sich automatisch an", 2),
            ("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "Höchstleistung", "Höhere Taktraten, etwas mehr Stromverbrauch", 3),
            ("e9a42b02-d5df-448d-aa00-03f14749eb61", "Ultimative Leistung", "Maximale Leistung, keine Stromsparfunktionen", 4)
        };

        private FrameworkElement BuildHoloOptimization()
        {
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Optimierung"), Loc.T("Optimierung"), Loc.T("Mehr Leistung und Platz für deine Spiele."),
                HoloButton("Energieoptionen", () => OpenShell("powercfg.cpl"), false, ""),
                HoloButton("Task-Manager", () => OpenShell("taskmgr.exe"), false, "")));

            bool powerOk = currentPowerPlanName is "Höchstleistung" or "Ultimative Leistung";
            bool gameModeOk = ReadGameModeEnabled();
            bool mouseOk = !ReadMouseAccelEnabled();
            bool dvrOk = !ReadGameDvrEnabled();
            int score = (powerOk ? 1 : 0) + (gameModeOk ? 1 : 0) + (mouseOk ? 1 : 0) + (dvrOk ? 1 : 0);

            // ── Gaming-Check ──
            var check = new Grid();
            check.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            check.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var ringColor = score == 4 ? MakeColor("#34D399") : GetAccentColor();
            check.Children.Add(HoloRing(score / 4.0, $"{score}/4", Loc.T("erfüllt"), 168, ringColor, 12));

            var points = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0) };
            var hint = new TextBlock
            {
                Text = score == 4 ? Loc.T("Alles bereit. Dein PC ist für Spiele optimal eingestellt.") : Loc.T($"Noch {4 - score} von 4 Punkten offen. Du kannst sie unten mit einem Klick verbessern."),
                FontSize = 15,
                Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
            points.Children.Add(hint);
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            grid.Children.Add(HoloCheckPoint("Energieplan: Höchstleistung", powerOk, powerOk ? null : () => _ = ActivatePowerPlanAsync(HoloPlans[2].Guid)));
            grid.Children.Add(HoloCheckPoint("Spielmodus ist an", gameModeOk, gameModeOk ? null : () => { SetGameModeEnabled(true); AfterOptimizationToggle(); }));
            grid.Children.Add(HoloCheckPoint("Mausbeschleunigung ist aus", mouseOk, mouseOk ? null : () => { SetMouseAccelEnabled(false); AfterOptimizationToggle(); }));
            grid.Children.Add(HoloCheckPoint("Hintergrundaufnahme ist aus", dvrOk, dvrOk ? null : () => { SetGameDvrEnabled(false); AfterOptimizationToggle(); }));
            points.Children.Add(grid);
            Grid.SetColumn(points, 1);
            check.Children.Add(points);
            page.Children.Add(HoloPanel(Loc.T("Gaming-Check"), check, null, 26));

            // ── Energiepläne ──
            var plans = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            foreach (var plan in HoloPlans) plans.Children.Add(HoloPowerCell(plan.Guid, plan.Name, plan.Hint, plan.Level, currentPowerPlanName == plan.Name));
            var planBody = new StackPanel();
            planBody.Children.Add(HoloHint(Loc.T("Aktiver Plan:") + " " + Loc.T(currentPowerPlanName ?? "unbekannt") + "  ·  " + Loc.T("Klicke auf einen Plan, um ihn sofort zu aktivieren.")));
            plans.Margin = new Thickness(0, 10, 0, 0);
            planBody.Children.Add(plans);
            page.Children.Add(HoloPanel(Loc.T("Energiesparplan"), planBody));

            var left = new StackPanel();
            var right = new StackPanel();
            page.Children.Add(HoloColumns(left, right, 0.45));

            // ── Windows für Spiele ──
            var windows = new StackPanel();
            windows.Children.Add(HoloToggleRow("Spielmodus", "Windows gibt dem Spiel Vorrang und hält Updates im Hintergrund zurück.", ReadGameModeEnabled(), on => { SetGameModeEnabled(on); AfterOptimizationToggle(); }));
            windows.Children.Add(HoloToggleRow("Hintergrundaufnahme", "Nimmt ständig die letzten Minuten auf. Aus bedeutet mehr FPS, Screenshots und manuelle Aufnahmen gehen weiter.", ReadGameDvrEnabled(), on => { SetGameDvrEnabled(on); AfterOptimizationToggle(); }));
            windows.Children.Add(HoloToggleRow("Mausbeschleunigung", "Für Shooter besser aus: Die gleiche Handbewegung ergibt immer die gleiche Strecke auf dem Bildschirm.", ReadMouseAccelEnabled(), on => { SetMouseAccelEnabled(on); AfterOptimizationToggle(); }));
            left.Children.Add(HoloPanel(Loc.T("Windows für Spiele"), windows));

            // ── Aufräumen ──
            left.Children.Add(BuildHoloCleanup());

            // ── Treiber ──
            var driver = new StackPanel();
            driver.Children.Add(new TextBlock { Text = TxtDriverInfo?.Text ?? string.Empty, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = MakeBrush("#34D399"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            driver.Children.Add(HoloHint("Neue Treiber bringen oft mehr FPS und beheben Abstürze in neuen Spielen."));
            var driverButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, -10) };
            driverButtons.Children.Add(HoloButton("Treiber-Seite öffnen", () => BtnOpenDriverPage_Click(this, new RoutedEventArgs()), true, ""));
            driverButtons.Children.Add(HoloButton("Geräte-Manager", () => OpenShell("devmgmt.msc"), false, ""));
            driver.Children.Add(driverButtons);
            right.Children.Add(HoloPanel(Loc.T("Grafiktreiber"), driver));

            // ── Autostart ──
            right.Children.Add(BuildHoloAutostart());
            return page;
        }

        private void AfterOptimizationToggle()
        {
            PopulateOptimizationToggles();   // Schalter von Thema 1 nachziehen (ohne erneutes Schreiben)
            RefreshHoloPage();
        }

        private FrameworkElement HoloCheckPoint(string text, bool ok, Action? fix)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 18, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mark = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0) };
            mark.Children.Add(new System.Windows.Shapes.Path { Data = HexGeometry(22), Fill = MakeBrush(ok ? "#2634D399" : "#26F59E0B"), Stroke = MakeBrush(ok ? "#34D399" : "#F59E0B"), StrokeThickness = 1.2 });
            var glyph = new TextBlock { Text = ok ? "" : "", FontSize = 10, Foreground = MakeBrush(ok ? "#34D399" : "#F59E0B"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            mark.Children.Add(glyph);
            row.Children.Add(mark);

            var label = new TextBlock { Text = Loc.T(text), FontSize = 13.5, Foreground = ok ? MakeBrush("#C9D3E3") : System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            if (fix != null)
            {
                var button = HoloButton("Beheben", fix, true);
                button.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(button, 2);
                row.Children.Add(button);
            }
            return row;
        }

        /// <summary>Energieplan als Zelle: Leistungsbalken (1–4), Name, Beschreibung; aktiv leuchtet.</summary>
        private FrameworkElement HoloPowerCell(string guid, string name, string hint, int level, bool active)
        {
            var accent = GetAccentColor();
            var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };

            var bars = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Height = 40, Margin = new Thickness(0, 0, 0, 12) };
            for (int i = 1; i <= 4; i++)
            {
                var bar = new Border { Width = 12, Height = 10 + i * 7.5, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 5, 0), RenderTransform = new SkewTransform(-14, 0) };
                if (i <= level)
                {
                    if (active) bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    else bar.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, HoloCyan.R, HoloCyan.G, HoloCyan.B));
                }
                else bar.Background = MakeBrush("#1C2740");
                bars.Children.Add(bar);
            }
            body.Children.Add(bars);

            var title = new TextBlock { Text = Loc.T(name).ToUpper(Loc.Culture), FontSize = 14.5, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            body.Children.Add(title);
            body.Children.Add(new TextBlock { Text = Loc.T(hint), FontSize = 12, Foreground = ConsoleMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            if (active)
            {
                var badge = ConsoleChip(null, Loc.T("Aktiv"), null, true);
                ((FrameworkElement)badge).Margin = new Thickness(0, 10, 0, 0);
                ((FrameworkElement)badge).HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                body.Children.Add(badge);
            }

            var shape = new System.Windows.Shapes.Path
            {
                Fill = active ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, accent.R, accent.G, accent.B)) : MakeBrush("#990D1424"),
                Stroke = active ? new SolidColorBrush(accent) : MakeBrush("#2A3756"),
                StrokeThickness = active ? 1.6 : 1
            };
            if (active && !settings.PerformanceMode) shape.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
            var cell = new Grid { Margin = new Thickness(0, 0, 14, 0), Cursor = active ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
            cell.Children.Add(HoloShapeLayer(shape));
            cell.Children.Add(body);
            cell.SizeChanged += (s, e) => shape.Data = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, 14);
            if (!active)
            {
                cell.MouseEnter += (s, e) => shape.Stroke = new SolidColorBrush(HoloCyan);
                cell.MouseLeave += (s, e) => shape.Stroke = MakeBrush("#2A3756");
                cell.MouseLeftButtonUp += async (s, e) => await ActivatePowerPlanAsync(guid);
            }
            return cell;
        }

        private FrameworkElement BuildHoloAutostart()
        {
            var body = new StackPanel();
            body.Children.Add(HoloHint("Programme, die mit Windows starten. Abwählen verhindert den Start (nur für deinen Benutzer)."));
            var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var (hive, editable) in new[] { (Registry.CurrentUser, true), (Registry.LocalMachine, false) })
            {
                try
                {
                    using var run = hive.OpenSubKey(AutostartRunKey);
                    using var approved = hive.OpenSubKey(AutostartApprovedKey);
                    if (run == null) continue;
                    foreach (string name in run.GetValueNames())
                    {
                        string entry = name;
                        string command = run.GetValue(name) as string ?? string.Empty;
                        bool enabled = !(approved?.GetValue(name) is byte[] flags && flags.Length > 0 && (flags[0] & 1) != 0);
                        string description = SHide(settings.StreamHidePaths) ? string.Empty : command;
                        list.Children.Add(HoloToggleRow(entry, description, enabled, on => SetAutostartEnabled(entry, on), editable,
                            editable ? null : Loc.T("Gilt für alle Benutzer. Ändern erfordert Administratorrechte.")));
                    }
                }
                catch { }
            }
            if (list.Children.Count == 0) list.Children.Add(HoloHint("Keine Autostart-Programme gefunden."));
            body.Children.Add(list);
            return HoloPanel(Loc.T("Autostart-Programme") + "  " + list.Children.OfType<Grid>().Count().ToString(Loc.Culture), body);
        }

        private FrameworkElement BuildHoloCleanup()
        {
            var body = new StackPanel();
            if (cleanupItems.Count == 0) BuildCleanupItems();
            long total = cleanupItems.Sum(i => i.Bytes);

            var head = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sum = new StackPanel();
            sum.Children.Add(HoloLabel(Loc.T("Freigebbar")));
            sum.Children.Add(ConsoleTitle(cleanupMeasured ? (total > 0 ? FormatCleanupSize(total) : Loc.T("Alles sauber")) : "…", 32));
            head.Children.Add(sum);
            var buttons = new WrapPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            buttons.Children.Add(HoloButton("Alles leeren", () => BtnCleanupAll_Click(this, new RoutedEventArgs()), true, ""));
            buttons.Children.Add(HoloButton("Neu berechnen", () => _ = MeasureCleanupAsync(), false, ""));
            Grid.SetColumn(buttons, 1);
            head.Children.Add(buttons);
            body.Children.Add(head);

            long max = Math.Max(1, cleanupItems.Count > 0 ? cleanupItems.Max(i => i.Bytes) : 1);
            foreach (var entry in cleanupItems)
            {
                var item = entry;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var capsule = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = System.Windows.VerticalAlignment.Top };
                capsule.Children.Add(new System.Windows.Shapes.Path { Data = HexGeometry(40), Fill = MakeBrush("#1A22D3EE"), Stroke = MakeBrush("#4422D3EE"), StrokeThickness = 1 });
                capsule.Children.Add(new TextBlock { Text = item.Icon, FontSize = 16, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                row.Children.Add(capsule);

                var texts = new StackPanel();
                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.Children.Add(new TextBlock { Text = Loc.T(item.Title), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White });
                string sizeText = item.NoSize ? string.Empty : !cleanupMeasured ? "…" : item.ShowCount && item.Count > 0 ? $"{FormatCleanupSize(item.Bytes)} ({item.Count})" : FormatCleanupSize(item.Bytes);
                var size = new TextBlock { Text = sizeText, FontSize = 12.5, Foreground = ConsoleCyan, Margin = new Thickness(12, 0, 0, 0) };
                size.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(size, 1);
                top.Children.Add(size);
                texts.Children.Add(top);
                texts.Children.Add(new TextBlock { Text = Loc.T(item.Description), FontSize = 12, Foreground = ConsoleMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                if (!item.NoSize && item.Bytes > 0)
                {
                    double share = item.Bytes / (double)max;
                    var track = new Grid { Height = 4, Margin = new Thickness(0, 6, 0, 0) };
                    track.Children.Add(new Border { Background = MakeBrush("#18223A"), RenderTransform = new SkewTransform(-20, 0) });
                    var bar = new Border { HorizontalAlignment = System.Windows.HorizontalAlignment.Left, RenderTransform = new SkewTransform(-20, 0) };
                    bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    track.SizeChanged += (s, e) => bar.Width = Math.Max(3, e.NewSize.Width * share);
                    track.Children.Add(bar);
                    texts.Children.Add(track);
                }
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);

                bool canClear = !cleanupBusy && (item.NoSize || item.Bytes > 0 || item.Count > 0);
                if (canClear)
                {
                    var clear = HoloButton("Leeren", async () => { await ClearCleanupItemAsync(item); }, false);
                    clear.Margin = new Thickness(14, 0, 0, 0);
                    clear.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                    Grid.SetColumn(clear, 2);
                    row.Children.Add(clear);
                }
                body.Children.Add(row);
            }
            if (TxtCleanupNote?.Visibility == Visibility.Visible) body.Children.Add(HoloHint(TxtCleanupNote.Text));
            return HoloPanel(Loc.T("Aufräumen"), body);
        }
    }
}
