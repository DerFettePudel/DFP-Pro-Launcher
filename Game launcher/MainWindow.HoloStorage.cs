using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Speicher („Datenträger-Karte“) ─────────────────────────────
        //
        // Jedes Laufwerk als Ring mit belegtem Anteil, darunter wie viel davon Spiele sind. Darunter eine Flächenkarte
        // (Treemap): jedes Spiel ein Rechteck, so groß wie sein Platzbedarf, mit Cover und Launcher-Farbe.

        private FrameworkElement BuildHoloStorage()
        {
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Speicher"), Loc.T("Speicher"), Loc.T("Belegung deiner Laufwerke und der Platzbedarf deiner Spiele."),
                HoloButton("Platz schaffen", () => ShowSpaceAssistant(), true, ""),
                HoloButton(sizeScanRunning ? "Wird gemessen ..." : "Größen messen", () => { StartGameSizeScan(true); RefreshHoloPage(); }, false, "")));

            var sized = VisibleGames.Select(g => (Game: g, Bytes: KnownGameSize(g) ?? 0)).Where(x => x.Bytes > 0).OrderByDescending(x => x.Bytes).ToList();
            if (sized.Count == 0 && !sizeScanRunning) StartGameSizeScan();

            // ── Laufwerke ──
            var drives = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                    drives.Children.Add(HoloDrivePod(drive, sized));
                }
                catch { }
            }
            if (drives.Children.Count > 0)
            {
                drives.Columns = Math.Min(drives.Children.Count, 4);
                drives.Rows = (int)Math.Ceiling(drives.Children.Count / (double)drives.Columns);
                page.Children.Add(drives);
            }

            // ── Flächenkarte ──
            var body = new StackPanel();
            if (sized.Count == 0)
            {
                body.Children.Add(HoloHint(sizeScanRunning ? "Die Größen deiner Spiele werden gemessen ..." : "Noch keine Größen bekannt. Klick auf „Größen messen“."));
            }
            else
            {
                var info = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
                info.Children.Add(ConsoleChip(Loc.T("Spiele"), sized.Count.ToString(Loc.Culture)));
                info.Children.Add(ConsoleChip(Loc.T("Zusammen"), FormatBytes(sized.Sum(x => x.Bytes)), null, true));
                info.Children.Add(ConsoleChip(Loc.T("Größtes Spiel"), sized[0].Game.Name + "  ·  " + FormatBytes(sized[0].Bytes)));
                body.Children.Add(info);
                body.Children.Add(BuildHoloTreemap(sized.Take(40).ToList()));
            }
            page.Children.Add(HoloPanel(Loc.T("Speicherbedarf der Spiele"), body, null, 24));
            return page;
        }

        private FrameworkElement HoloDrivePod(DriveInfo drive, List<(GameItem Game, long Bytes)> sized)
        {
            double total = drive.TotalSize, free = drive.AvailableFreeSpace, used = total - free;
            double fraction = total > 0 ? used / total : 0;
            string root = drive.Name;
            long games = sized.Where(x => x.Game.InstallDir.StartsWith(root, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Bytes);
            var color = fraction > 0.9 ? MakeColor("#EF4444") : fraction > 0.75 ? MakeColor("#F59E0B") : GetAccentColor();

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.Children.Add(HoloRing(fraction, Math.Round(fraction * 100).ToString(Loc.Culture) + "%", Loc.T("belegt"), 118, color));

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
            texts.Children.Add(HoloLabel(Loc.T("Frei")));
            texts.Children.Add(ConsoleTitle(FormatBytes((long)free), 26));
            var line = new TextBlock { Text = FormatBytes((long)used) + " / " + FormatBytes((long)total), FontSize = 12, Foreground = ConsoleMuted, Margin = new Thickness(0, 4, 0, 0) };
            line.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            texts.Children.Add(line);
            if (games > 0)
            {
                var chip = ConsoleChip(Loc.T("Spiele"), FormatBytes(games));
                ((FrameworkElement)chip).Margin = new Thickness(0, 10, 0, 0);
                ((FrameworkElement)chip).HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                texts.Children.Add(chip);
            }
            Grid.SetColumn(texts, 1);
            body.Children.Add(texts);

            string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? Loc.T("Lokaler Datenträger") : drive.VolumeLabel;
            return HoloPanel(drive.Name.TrimEnd('\\') + "  " + label, body, () => ShowSpaceAssistant(root));
        }

        // ───────── Flächenkarte (squarified) ─────────

        private FrameworkElement BuildHoloTreemap(List<(GameItem Game, long Bytes)> items)
        {
            var canvas = new Canvas { Height = 520, ClipToBounds = true };
            double lastWidth = -1;
            canvas.SizeChanged += (s, e) =>
            {
                if (Math.Abs(e.NewSize.Width - lastWidth) < 1) return;
                lastWidth = e.NewSize.Width;
                LayoutTreemap(canvas, items, new Rect(0, 0, e.NewSize.Width, e.NewSize.Height));
            };
            return canvas;
        }

        private void LayoutTreemap(Canvas canvas, List<(GameItem Game, long Bytes)> items, Rect area)
        {
            canvas.Children.Clear();
            double total = items.Sum(i => (double)i.Bytes);
            if (total <= 0 || area.Width <= 0) return;
            double scale = area.Width * area.Height / total;
            var values = items.Select(i => i.Bytes * scale).ToList();

            var rects = new List<Rect>();
            Squarify(values, 0, area, rects);
            for (int i = 0; i < rects.Count && i < items.Count; i++)
                canvas.Children.Add(HoloTreemapCell(items[i].Game, items[i].Bytes, rects[i], i));
        }

        /// <summary>Teilt die Fläche in Reihen auf, deren Seitenverhältnisse möglichst nah an 1 liegen.</summary>
        private static void Squarify(List<double> values, int start, Rect area, List<Rect> result)
        {
            while (start < values.Count)
            {
                bool horizontal = area.Width >= area.Height;
                double side = horizontal ? area.Height : area.Width;
                int end = start;
                double rowSum = 0, worst = double.MaxValue;
                while (end < values.Count)
                {
                    double next = rowSum + values[end];
                    double max = values.Skip(start).Take(end - start + 1).Max(), min = values.Skip(start).Take(end - start + 1).Min();
                    // schlechtestes Seitenverhältnis der Reihe: max(w²·max / s², s² / (w²·min))
                    double ratio = Math.Max(side * side * max / (next * next), next * next / (side * side * min));
                    if (ratio > worst && end > start) break;
                    worst = ratio;
                    rowSum = next;
                    end++;
                }

                double thick = rowSum / side;
                double offset = 0;
                for (int i = start; i < end; i++)
                {
                    double length = values[i] / thick;
                    result.Add(horizontal
                        ? new Rect(area.X, area.Y + offset, thick, length)
                        : new Rect(area.X + offset, area.Y, length, thick));
                    offset += length;
                }
                area = horizontal
                    ? new Rect(area.X + thick, area.Y, Math.Max(0, area.Width - thick), area.Height)
                    : new Rect(area.X, area.Y + thick, area.Width, Math.Max(0, area.Height - thick));
                start = end;
            }
        }

        private FrameworkElement HoloTreemapCell(GameItem game, long bytes, Rect rect, int index)
        {
            const double gap = 3;
            double width = Math.Max(1, rect.Width - gap), height = Math.Max(1, rect.Height - gap);
            var tint = LauncherTint(game.Source);

            var cell = new Grid { Width = width, Height = height, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = game.Name + "  ·  " + FormatBytes(bytes), ClipToBounds = true };
            Canvas.SetLeft(cell, rect.X);
            Canvas.SetTop(cell, rect.Y);

            var back = new Border { Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, tint.R, tint.G, tint.B)) };
            if ((HasCover(game) ? GetHeroImage(game) ?? (BitmapSource?)GetCachedImage(EffectiveCover(game)) : GetHeroImage(game)) is BitmapSource image)
                back.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill, Opacity = 0.55 };
            cell.Children.Add(back);

            var shade = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x20, tint.R, tint.G, tint.B), 0));
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xE6, 0x06, 0x09, 0x10), 1));
            cell.Children.Add(new Border { Background = shade });
            var edge = new Border { BorderThickness = new Thickness(1, 3, 1, 1), BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, tint.R, tint.G, tint.B)) };
            cell.Children.Add(edge);

            if (width > 70 && height > 40)
            {
                var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 8) };
                var name = new TextBlock { Text = game.Name, FontSize = Math.Clamp(Math.Min(width, height) / 9, 11, 20), FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
                name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                texts.Children.Add(name);
                var size = new TextBlock { Text = FormatBytes(bytes), FontSize = 11, Foreground = ConsoleCyan };
                size.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                texts.Children.Add(size);
                cell.Children.Add(texts);
            }

            cell.MouseEnter += (s, e) => { edge.BorderBrush = new SolidColorBrush(HoloCyan); edge.BorderThickness = new Thickness(2, 3, 2, 2); };
            cell.MouseLeave += (s, e) => { edge.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, tint.R, tint.G, tint.B)); edge.BorderThickness = new Thickness(1, 3, 1, 1); };
            cell.MouseLeftButtonUp += (s, e) => ShowConsoleDetail(game);

            if (holoAnimate)
            {
                cell.Opacity = 0;
                cell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = TimeSpan.FromMilliseconds(350 + index * 35) });
            }
            return cell;
        }
    }
}
