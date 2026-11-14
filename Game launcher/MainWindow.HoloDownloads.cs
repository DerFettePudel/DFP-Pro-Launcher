using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Downloads („Eingang“) ─────────────────────────────
        //
        // Zahlen-Platten (Dateien, Gesamtgröße, heute neu, größte Datei), Verteilung nach Dateityp und eine Zeitleiste:
        // senkrechte Lichtlinie mit Knoten pro Tag, jede Datei als Zeile mit Typ-Symbol, Größe, Uhrzeit und Knöpfen beim Drüberfahren.

        private FrameworkElement BuildHoloDownloads()
        {
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Downloads"), Loc.T("Downloads"), Loc.T("Deine zuletzt heruntergeladenen Dateien."),
                HoloButton("Ordner öffnen", () => BtnOpenDownloadsFolder_Click(this, new RoutedEventArgs()), true, ""),
                HoloButton("Aktualisieren", RefreshHoloPage, false, "")));

            if (SHide(settings.StreamHidePaths))
            {
                page.Children.Add(HoloPanel(Loc.T("Streamer-Modus"), HoloHint("Im Streamer-Modus ausgeblendet.")));
                return page;
            }

            var files = new List<FileInfo>();
            try
            {
                if (Directory.Exists(DownloadsPath))
                    files = new DirectoryInfo(DownloadsPath).EnumerateFiles()
                        .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                        .OrderByDescending(f => f.LastWriteTime)
                        .Take(60)
                        .ToList();
            }
            catch { }

            if (files.Count == 0)
            {
                page.Children.Add(HoloPanel(Loc.T("Downloads"), HoloHint("Keine Dateien gefunden.")));
                return page;
            }

            // ── Zahlen ──
            var stats = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(-6, 0, 4, 4) };
            stats.Children.Add(HoloStat(Loc.T("Dateien"), files.Count.ToString(Loc.Culture), null, files.Count));
            stats.Children.Add(HoloStat(Loc.T("Gesamtgröße"), FormatBytes(files.Sum(f => f.Length))));
            int today = files.Count(f => f.LastWriteTime.Date == DateTime.Today);
            stats.Children.Add(HoloStat(Loc.T("Heute neu"), today.ToString(Loc.Culture), null, today));
            var biggest = files.OrderByDescending(f => f.Length).First();
            stats.Children.Add(HoloStat(Loc.T("Größte Datei"), FormatBytes(biggest.Length), () => OpenShell("explorer.exe", $"/select,\"{biggest.FullName}\"")));
            page.Children.Add(stats);

            var left = new StackPanel();
            var right = new StackPanel();
            page.Children.Add(HoloColumns(left, right, 0.3));

            // ── Zeitleiste ──
            var timeline = new StackPanel();
            foreach (var group in files.GroupBy(f => DayGroup(f.LastWriteTime)))
            {
                timeline.Children.Add(HoloTimelineNode(group.Key));
                foreach (var file in group) timeline.Children.Add(HoloFileRow(file));
            }
            left.Children.Add(HoloPanel(Loc.T("Eingang"), timeline));

            // ── Dateitypen ──
            var types = files.GroupBy(f => FileKind(f.Extension).Label)
                .Select(g => (Label: g.Key, Count: g.Count(), Bytes: g.Sum(f => f.Length), Kind: FileKind(g.First().Extension)))
                .OrderByDescending(t => t.Bytes)
                .ToList();
            long total = Math.Max(1, files.Sum(f => f.Length));
            var typeList = new StackPanel();
            foreach (var type in types)
            {
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                var head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var glyph = new TextBlock { Text = type.Kind.Glyph, FontSize = 14, Foreground = MakeBrush(type.Kind.Color), Margin = new Thickness(0, 0, 10, 0) };
                glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                head.Children.Add(glyph);
                var label = new TextBlock { Text = Loc.T(type.Label) + "  ·  " + type.Count.ToString(Loc.Culture), FontSize = 13.5, Foreground = System.Windows.Media.Brushes.White };
                Grid.SetColumn(label, 1);
                head.Children.Add(label);
                var size = new TextBlock { Text = FormatBytes(type.Bytes), FontSize = 12, Foreground = ConsoleMuted };
                size.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(size, 2);
                head.Children.Add(size);
                row.Children.Add(head);

                double share = type.Bytes / (double)total;
                var track = new Grid { Height = 5, Margin = new Thickness(0, 6, 0, 0) };
                track.Children.Add(new Border { Background = MakeBrush("#18223A"), RenderTransform = new SkewTransform(-20, 0) });
                var bar = new Border { HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Background = MakeBrush(type.Kind.Color), RenderTransform = new SkewTransform(-20, 0) };
                track.SizeChanged += (s, e) => bar.Width = Math.Max(3, e.NewSize.Width * share);
                track.Children.Add(bar);
                row.Children.Add(track);
                typeList.Children.Add(row);
            }
            right.Children.Add(HoloPanel(Loc.T("Dateitypen"), typeList));

            var folder = new StackPanel();
            var path = new TextBlock { Text = DownloadsPath, FontSize = 12.5, Foreground = System.Windows.Media.Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            path.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            folder.Children.Add(path);
            folder.Children.Add(HoloButton("Ordner öffnen", () => BtnOpenDownloadsFolder_Click(this, new RoutedEventArgs()), false, ""));
            right.Children.Add(HoloPanel(Loc.T("Download-Ordner"), folder));
            return page;
        }

        private static string DayGroup(DateTime time)
        {
            var day = time.Date;
            if (day == DateTime.Today) return "Heute";
            if (day == DateTime.Today.AddDays(-1)) return "Gestern";
            if (day > DateTime.Today.AddDays(-7)) return "Diese Woche";
            if (day > DateTime.Today.AddDays(-31)) return "Diesen Monat";
            return "Älter";
        }

        private static (string Label, string Glyph, string Color) FileKind(string extension) => extension.ToLowerInvariant() switch
        {
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => ("Archive", "", "#FACC15"),
            ".exe" or ".msi" or ".msix" or ".appx" => ("Installer", "", "#F87171"),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => ("Bilder", "", "#34D399"),
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" => ("Videos", "", "#A78BFA"),
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => ("Audio", "", "#F472B6"),
            ".pdf" or ".doc" or ".docx" or ".txt" or ".xlsx" or ".pptx" or ".md" => ("Dokumente", "", "#60A5FA"),
            ".iso" or ".img" => ("Abbilder", "", "#FB923C"),
            _ => ("Sonstige", "", "#94A3B8")
        };

        private FrameworkElement HoloTimelineNode(string label)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var node = new System.Windows.Shapes.Path { Data = HexGeometry(14), Width = 14, Height = 14, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            node.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
            if (!settings.PerformanceMode) node.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
            row.Children.Add(node);
            var text = HoloLabel(Loc.T(label), 11.5);
            text.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            return row;
        }

        private FrameworkElement HoloFileRow(FileInfo file)
        {
            var kind = FileKind(file.Extension);
            var row = new Grid { Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = file.Name };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // senkrechte Linie der Zeitleiste
            var line = new Border { Width = 2, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(9, 0, 0, 0), Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, HoloCyan.R, HoloCyan.G, HoloCyan.B)) };
            row.Children.Add(line);

            var bay = new Grid { Width = 38, Height = 38, Margin = new Thickness(0, 5, 12, 5) };
            var color = MakeColor(kind.Color);
            bay.Children.Add(new System.Windows.Shapes.Path { Data = ChamferGeometry(38, 38, 8), Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, color.R, color.G, color.B)), Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, color.R, color.G, color.B)), StrokeThickness = 1 });
            var glyph = new TextBlock { Text = kind.Glyph, FontSize = 16, Foreground = new SolidColorBrush(color), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            bay.Children.Add(glyph);
            Grid.SetColumn(bay, 1);
            row.Children.Add(bay);

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var name = new TextBlock { Text = file.Name, FontSize = 14, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            texts.Children.Add(name);
            var meta = new TextBlock { Text = FormatBytes(file.Length) + "  ·  " + file.LastWriteTime.ToString("HH:mm", Loc.Culture) + (file.LastWriteTime.Date != DateTime.Today ? "  ·  " + file.LastWriteTime.ToString("d", Loc.Culture) : string.Empty), FontSize = 11, Foreground = ConsoleMuted, Margin = new Thickness(0, 2, 0, 0) };
            meta.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            texts.Children.Add(meta);
            Grid.SetColumn(texts, 2);
            row.Children.Add(texts);

            var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center, Visibility = Visibility.Hidden };
            FrameworkElement Action(string glyphText, string tip, Action click)
            {
                var button = new Border { Width = 34, Height = 30, Margin = new Thickness(6, 0, 0, 0), Background = MakeBrush("#A60E1526"), BorderBrush = MakeBrush("#5522D3EE"), BorderThickness = new Thickness(1), ToolTip = Loc.T(tip), RenderTransform = new SkewTransform(-12, 0) };
                var icon = new TextBlock { Text = glyphText, FontSize = 13, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                button.Child = icon;
                button.MouseEnter += (s, e) => button.BorderBrush = new SolidColorBrush(HoloCyan);
                button.MouseLeave += (s, e) => button.BorderBrush = MakeBrush("#5522D3EE");
                button.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
                return button;
            }
            actions.Children.Add(Action("", "Öffnen", () => OpenShell(file.FullName)));
            actions.Children.Add(Action("", "Im Ordner zeigen", () => OpenShell("explorer.exe", $"/select,\"{file.FullName}\"")));
            Grid.SetColumn(actions, 3);
            row.Children.Add(actions);

            row.MouseEnter += (s, e) => { name.Foreground = ConsoleCyan; actions.Visibility = Visibility.Visible; };
            row.MouseLeave += (s, e) => { name.Foreground = System.Windows.Media.Brushes.White; actions.Visibility = Visibility.Hidden; };
            row.MouseLeftButtonUp += (s, e) => { if (e.ClickCount == 1) OpenShell(file.FullName); };
            return row;
        }
    }
}
