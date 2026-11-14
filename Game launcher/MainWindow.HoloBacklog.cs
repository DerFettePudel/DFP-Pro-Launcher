using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Backlog („Missions-Board“) ─────────────────────────────
        //
        // Fortschritt oben (Ring + Zahlen), drei Bahnen nebeneinander (Warteschlange, Aktiv, Abgeschlossen) mit Ziehen und
        // Ablegen, darunter der Release-Radar: kommende Spiele mit großem Tage-Zähler.

        private static readonly (string Status, string Title, string Code, string Glyph)[] HoloLanes =
        {
            ("backlog", "Will ich spielen", "Warteschlange", ""),
            ("playing", "Spiele ich gerade", "Aktiv", ""),
            ("done", "Durchgespielt", "Abgeschlossen", "")
        };

        private FrameworkElement BuildHoloBacklog()
        {
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Backlog"), Loc.T("Backlog und Planer"), Loc.T("Ziehe Spiele zwischen den Spalten. Doppelklick startet ein Spiel. Mit ＋ fügst du mehrere auf einmal hinzu."),
                HoloButton("Termin", () => BtnAddRelease_Click(this, new RoutedEventArgs()), false, "")));

            var games = VisibleGames.ToList();
            int backlog = games.Count(g => g.Status == "backlog"), playing = games.Count(g => g.Status == "playing"), done = games.Count(g => g.Status == "done");
            int total = backlog + playing + done;

            // ── Fortschritt ──
            var progress = new Grid();
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progress.Children.Add(HoloRing(total > 0 ? done / (double)total : 0, total > 0 ? Math.Round(done * 100.0 / total).ToString(Loc.Culture) + "%" : "—", Loc.T("Durchgespielt"), 128, HoloCyan));
            var numbers = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
            foreach (var (status, title, code, _) in HoloLanes)
            {
                int count = status == "backlog" ? backlog : status == "playing" ? playing : done;
                var cell = new StackPanel();
                cell.Children.Add(HoloLabel(Loc.T(code)));
                cell.Children.Add(ConsoleTitle(count.ToString(Loc.Culture), 42));
                cell.Children.Add(new TextBlock { Text = Loc.T(title), FontSize = 12.5, Foreground = ConsoleMuted });
                numbers.Children.Add(cell);
            }
            Grid.SetColumn(numbers, 1);
            progress.Children.Add(numbers);
            page.Children.Add(HoloPanel(Loc.T("Fortschritt"), progress, null, 24));

            // ── Bahnen ──
            var lanes = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            foreach (var lane in HoloLanes) lanes.Children.Add(HoloLane(lane.Status, lane.Title, lane.Code, lane.Glyph, games));
            page.Children.Add(lanes);

            // ── Release-Radar ──
            page.Children.Add(HoloPanel(Loc.T("Bald erscheinend"), BuildHoloReleases(), BtnAddReleaseAction));
            return page;
        }

        private void BtnAddReleaseAction() => BtnAddRelease_Click(this, new RoutedEventArgs());

        private FrameworkElement HoloLane(string status, string title, string code, string glyph, List<GameItem> games)
        {
            var accent = GetAccentColor();
            var body = new StackPanel { MinHeight = 220 };

            var add = HoloButton("Spiele hinzufügen", () => BtnBoardAdd_Click(new System.Windows.Controls.Button { Tag = status }, new RoutedEventArgs()), false, "");
            add.Margin = new Thickness(0, 0, 0, 14);
            body.Children.Add(add);

            var list = games.Where(g => g.Status == status).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var game in list) body.Children.Add(HoloBoardCard(game));
            if (list.Count == 0) body.Children.Add(HoloHint("Leer. Ziehe Spiele hierher oder nutze ＋."));

            var panel = HoloPanel(Loc.T(code) + "  " + list.Count.ToString(Loc.Culture), body);

            // Ablegen: ganze Bahn ist Ziel, leuchtet beim Drüberziehen
            panel.AllowDrop = true;
            if (panel is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Canvas layer && layer.Children.Count > 0 && layer.Children[0] is System.Windows.Shapes.Path edge)
            {
                panel.DragEnter += (s, e) => { edge.Stroke = new SolidColorBrush(HoloCyan); edge.StrokeThickness = 2.5; };
                panel.DragLeave += (s, e) => { edge.Stroke = HoloEdgeBrush(); edge.StrokeThickness = 1; };
                panel.Drop += (s, e) => { edge.Stroke = HoloEdgeBrush(); edge.StrokeThickness = 1; };
            }
            panel.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
                e.Handled = true;
            };
            panel.Drop += (s, e) =>
            {
                if (e.Data.GetData(System.Windows.DataFormats.StringFormat) is string name && allGames.FirstOrDefault(g => g.Name == name) is GameItem game && game.Status != status)
                    SetStatus(game, status);
                e.Handled = true;
            };
            return panel;
        }

        private FrameworkElement HoloBoardCard(GameItem game)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8), Cursor = System.Windows.Input.Cursors.Hand, ContextMenu = BuildGameMenu(game), ToolTip = Loc.T("Ziehen zum Verschieben, Doppelklick zum Starten") };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var shape = new System.Windows.Shapes.Path { Fill = MakeBrush("#990E1628"), Stroke = MakeBrush("#2A3756"), StrokeThickness = 1 };
            var back = HoloShapeLayer(shape);
            Grid.SetColumnSpan(back, 2);
            row.Children.Add(back);
            row.SizeChanged += (s, e) => shape.Data = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, 10);

            var thumb = new Border { Width = 40, Height = 56, Margin = new Thickness(8, 8, 12, 8), Background = MakeBrush("#141C2E"), Clip = ChamferGeometry(40, 56, 8) };
            if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage image) thumb.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            row.Children.Add(thumb);

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var name = new TextBlock { Text = game.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            texts.Children.Add(name);
            var meta = new TextBlock { Text = BuildMetaText(game), FontSize = 11, Foreground = ConsoleMuted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            texts.Children.Add(meta);
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);

            var dragStart = new System.Windows.Point();
            row.MouseLeftButtonDown += (s, e) =>
            {
                dragStart = e.GetPosition(null);
                if (e.ClickCount == 2) LaunchGame(game);
            };
            row.MouseMove += (s, e) =>
            {
                if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                var position = e.GetPosition(null);
                if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                row.Opacity = 0.5;
                System.Windows.DragDrop.DoDragDrop(row, game.Name, System.Windows.DragDropEffects.Move);
                row.Opacity = 1;
            };
            row.MouseEnter += (s, e) => { shape.Stroke = new SolidColorBrush(HoloCyan); name.Foreground = ConsoleCyan; };
            row.MouseLeave += (s, e) => { shape.Stroke = MakeBrush("#2A3756"); name.Foreground = System.Windows.Media.Brushes.White; };
            return row;
        }

        private FrameworkElement BuildHoloReleases()
        {
            var items = settings.Releases.OrderBy(r => r.Date).ToList();
            if (items.Count == 0) return HoloHint("Noch keine Einträge. Trage kommende Spiele ein, der Launcher zählt die Tage herunter.");

            var grid = HoloCardGrid(260);
            foreach (var release in items)
            {
                var entry = release;
                int days = (entry.Date.Date - DateTime.Today).Days;
                var cell = new Grid { Margin = new Thickness(0, 0, 14, 14) };
                var shape = new System.Windows.Shapes.Path { Fill = MakeBrush("#990E1628"), Stroke = days <= 0 ? MakeBrush("#FACC15") : MakeBrush("#3322D3EE"), StrokeThickness = 1 };
                cell.Children.Add(HoloShapeLayer(shape));
                cell.SizeChanged += (s, e) => shape.Data = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, 12);

                var layout = new Grid { Margin = new Thickness(16, 12, 16, 12) };
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var counter = new StackPanel { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center, MinWidth = 64 };
                var number = ConsoleTitle(days > 0 ? days.ToString(Loc.Culture) : "✓", 36);
                number.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                if (days <= 0) number.Foreground = MakeBrush("#FACC15");
                counter.Children.Add(number);
                var unit = HoloLabel(days == 1 ? Loc.T("Tag") : days > 1 ? Loc.T("Tage") : days == 0 ? Loc.T("heute!") : Loc.T("erschienen"), 10);
                unit.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                counter.Children.Add(unit);
                layout.Children.Add(counter);

                var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var title = new TextBlock { Text = entry.Name, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
                title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                texts.Children.Add(title);
                var date = new TextBlock { Text = entry.Date.ToString("d", Loc.Culture), FontSize = 12, Foreground = ConsoleMuted, Margin = new Thickness(0, 3, 0, 0) };
                date.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                texts.Children.Add(date);
                Grid.SetColumn(texts, 1);
                layout.Children.Add(texts);
                cell.Children.Add(layout);

                var menu = CreateMenu();
                AddMenuItem(menu, "🗑  Eintrag löschen", () =>
                {
                    settings.Releases.Remove(entry);
                    SaveSettings();
                    RefreshReleases();
                });
                cell.ContextMenu = menu;
                grid.Children.Add(cell);
            }
            return grid;
        }
    }
}
